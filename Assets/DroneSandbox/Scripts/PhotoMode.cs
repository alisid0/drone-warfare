using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // Photo mode (P): the HUD goes away and the camera becomes a free-flying camera drone, anywhere on the map and
    // down to a metre above the ground or water. It eases into and out of moves and banks a little into turns,
    // like a real drone, focuses on whatever is in the middle of the frame (depth of field), lets you zoom and
    // move the time of day, and saves a picture to Pictures/Drone Warfare.
    public class PhotoMode : MonoBehaviour
    {
        const float Speed = 12f, FastMultiplier = 3.5f, LookSpeed = 0.15f, TurnSpeed = 60f;
        const float Clearance = 1.2f, Ceiling = 170f, Limit = 228f;
        const float HintSeconds = 7f;

        public static PhotoMode Instance { get; private set; }
        public static bool Active => Instance != null && Instance.active;

        public bool Capturing { get; private set; } // the frame being saved, so the HUD stays out of it
        public float HintUntil { get; private set; }

        SandboxGame game;
        RtsController rts;
        Camera cam;
        bool active;
        Vector3 velocity;
        float yaw, pitch, roll, fieldOfView, normalFieldOfView;
        Vector2 lastMouse;
        bool looking;
        Volume focusVolume;
        DepthOfField focus;
        float focusDistance = 40f;

        public void Init(SandboxGame sandbox, RtsController controller, Camera camera)
        {
            Instance = this;
            game = sandbox;
            rts = controller;
            cam = camera;
            // The saved profile (made by Drone Sandbox > Configure Project): player builds only keep the depth of
            // field shader if a saved profile uses it.
            var profile = Resources.Load<VolumeProfile>("PhotoFocus");
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                ConfigureFocus(profile);
            }
            profile.TryGet(out focus);
            var go = new GameObject("Photo Focus");
            go.transform.SetParent(transform, false);
            focusVolume = go.AddComponent<Volume>();
            focusVolume.isGlobal = true;
            focusVolume.priority = 30f;
            focusVolume.sharedProfile = profile;
            focusVolume.weight = 0f;
        }

        // Depth of field like a camera lens: sharp around the focus distance, softening in front and behind
        // (UpdateFocus moves the ranges with the focus). Also screen-space global illumination: sunlight bouncing
        // off the ground, water and walls into the shade. Too slow for play, fine for pictures.
        public static void ConfigureFocus(VolumeProfile profile)
        {
            var dof = profile.Add<DepthOfField>(true);
            dof.focusMode.Override(DepthOfFieldMode.Manual);
            SetRanges(dof, 40f);
            var bounce = profile.Add<GlobalIllumination>(true);
            bounce.enable.Override(true);
            bounce.tracing.Override(RayCastingMode.RayMarching);
        }

        static void SetRanges(DepthOfField dof, float distance)
        {
            dof.nearFocusStart.Override(0f);
            dof.nearFocusEnd.Override(distance * 0.12f);
            // Behind, only a gentle softening well beyond the subject, and the further the focus the further that
            // starts: something close stands out from a soft background, a landscape stays sharp to the clouds.
            float far = distance * 6f * (1f + distance / 10f);
            dof.farFocusStart.Override(far);
            dof.farFocusEnd.Override(far * 5f);
        }

        public void Enter()
        {
            if (active) return;
            active = true;
            Vector3 euler = cam.transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            roll = 0f;
            velocity = Vector3.zero;
            normalFieldOfView = fieldOfView = cam.fieldOfView;
            focusVolume.weight = 1f;
            HintUntil = Time.unscaledTime + HintSeconds;
            looking = false;
        }

        public void Exit()
        {
            if (!active) return;
            active = false;
            focusVolume.weight = 0f;
            cam.fieldOfView = normalFieldOfView;
            rts.Restore();
        }

        // Puts the camera somewhere exact, looking along yaw and pitch (degrees, pitch down is positive).
        // Used by the auto-test for its photos.
        public void Place(Vector3 position, float yawDegrees, float pitchDegrees, float zoom = 0f)
        {
            Enter();
            HintUntil = 0f;
            yaw = yawDegrees;
            pitch = pitchDegrees;
            roll = 0f;
            velocity = Vector3.zero;
            if (zoom > 0f) fieldOfView = zoom;
            cam.fieldOfView = fieldOfView;
            cam.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, yaw, 0f));
            UpdateFocus();
        }

        // The same, aimed at a point.
        public void PlaceLooking(Vector3 position, Vector3 target, float zoom = 0f)
        {
            Vector3 d = target - position;
            float yawDegrees = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitchDegrees = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            Place(position, yawDegrees, pitchDegrees, zoom);
        }

        void Update()
        {
            if (GameInput.Pressed(GameKey.Photo))
            {
                if (active) Exit();
                else if (!game.UiBlocking) Enter();
            }
            if (!active) return;
            if (game.UiBlocking)
            {
                Exit();
                return;
            }
            float dt = Time.unscaledDeltaTime;

            // Look round with the right mouse button held, turn with Q / E, zoom with the wheel.
            Vector2 mouse = GameInput.MousePosition;
            if (GameInput.RightHeld)
            {
                if (looking)
                {
                    Vector2 delta = mouse - lastMouse;
                    yaw += delta.x * LookSpeed;
                    pitch = Mathf.Clamp(pitch - delta.y * LookSpeed, -80f, 88f);
                }
                looking = true;
            }
            else looking = false;
            lastMouse = mouse;
            if (GameInput.Held(GameKey.RotateLeft)) yaw -= TurnSpeed * dt;
            if (GameInput.Held(GameKey.RotateRight)) yaw += TurnSpeed * dt;
            float scroll = GameInput.Scroll;
            if (scroll != 0f) fieldOfView = Mathf.Clamp(fieldOfView * (scroll > 0f ? 0.9f : 1.1f), 12f, 75f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fieldOfView, 1f - Mathf.Exp(-10f * dt));

            // Fly: WASD across, R / F up and down, Shift for speed. Moves ease in and out.
            var wish = Vector3.zero;
            if (GameInput.Held(GameKey.Up)) wish.z += 1f;
            if (GameInput.Held(GameKey.Down)) wish.z -= 1f;
            if (GameInput.Held(GameKey.Right)) wish.x += 1f;
            if (GameInput.Held(GameKey.Left)) wish.x -= 1f;
            if (GameInput.Held(GameKey.Rise)) wish.y += 1f;
            if (GameInput.Held(GameKey.Sink)) wish.y -= 1f;
            float speed = Speed * (GameInput.Held(GameKey.Shift) ? FastMultiplier : 1f);
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Vector3 target = heading * new Vector3(wish.x, 0f, wish.z).normalized * speed + Vector3.up * wish.y * speed * 0.6f;
            velocity = Vector3.Lerp(velocity, target, 1f - Mathf.Exp(-2.5f * dt));

            Vector3 p = cam.transform.position + velocity * dt;
            p.x = Mathf.Clamp(p.x, -Limit, Limit);
            p.z = Mathf.Clamp(p.z, -Limit, Limit);
            p.y = Mathf.Clamp(p.y, Floor(p), Ceiling);
            float side = (Quaternion.Inverse(heading) * velocity).x;
            roll = Mathf.Lerp(roll, -side / (Speed * FastMultiplier) * 10f, 1f - Mathf.Exp(-3f * dt));
            cam.transform.SetPositionAndRotation(p, Quaternion.Euler(pitch, yaw, roll));

            // Time of day with [ and ] (an hour every two seconds).
            var dayNight = DayNight.Instance;
            if (dayNight != null)
            {
                float shift = (GameInput.Held(GameKey.TimeForward) ? 1f : 0f) - (GameInput.Held(GameKey.TimeBack) ? 1f : 0f);
                if (shift != 0f) dayNight.SetTime(dayNight.hour + shift * 0.5f * dt, dayNight.hoursPerMinute);
            }

            UpdateFocus();
            if (GameInput.Pressed(GameKey.Screenshot) && !Capturing) StartCoroutine(SavePhoto());
        }

        // Never below the ground or the water.
        static float Floor(Vector3 p)
        {
            float floor = Ground.HeightAt(p);
            floor = Mathf.Max(floor, MapLayout.WaterLevelAt(p.x, p.z));
            float river = MapLayout.RiverField.Query(p.x, p.z, out float water, out _);
            if (river < MapLayout.RiverHalfWidth) floor = Mathf.Max(floor, water);
            return floor + Clearance;
        }

        // Focus on whatever is in the middle of the frame.
        void UpdateFocus()
        {
            float distance = 300f;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (Ground.Raycast(ray, out Vector3 hit)) distance = Vector3.Distance(cam.transform.position, hit);
            // Something nearer in the middle of the frame takes the focus: a unit, or a bird (which has no collider).
            if (Physics.Raycast(ray, out RaycastHit unit, distance, ~0, QueryTriggerInteraction.Ignore)) distance = unit.distance;
            foreach (var b in Wildlife.Birds)
            {
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                Vector3 to = b.transform.position - ray.origin;
                float along = Vector3.Dot(to, ray.direction);
                if (along > 0.5f && along < distance && (to - ray.direction * along).magnitude < 0.8f) distance = along;
            }
            focusDistance = Mathf.Lerp(focusDistance, Mathf.Clamp(distance, 2f, 300f), 0.25f);
            if (focus != null) SetRanges(focus, focusDistance);
        }

        IEnumerator SavePhoto()
        {
            Capturing = true;
            yield return null; // a frame drawn without the HUD
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Drone Warfare");
            string file = Path.Combine(folder, $"DroneWarfare_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(file, texture.EncodeToPNG());
                game.ShowMessage("Photo saved to Pictures/Drone Warfare", 3f);
            }
            catch (Exception e)
            {
                game.ShowMessage("Couldn't save the photo: " + e.Message, 4f);
            }
            Destroy(texture);
            Capturing = false;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // RTS camera and orders: select drones with the left mouse button, right-click to move or attack.
    public class RtsController : MonoBehaviour
    {
        const float ClickPixels = 6f; // a left-button drag shorter than this counts as a click

        public readonly List<Drone> selected = new List<Drone>();
        public bool IsDragging { get; private set; }
        public Vector2 DragStart { get; private set; }
        public EnemyUnit HoveredEnemy { get; private set; }
        public Camera Cam { get; private set; }
        public float Distance => distance;

        SandboxGame game;
        SandboxHud hud;
        Vector3 focus;
        float yaw = 45f;
        float distance = 55f;
        float zoomTarget = 55f;   // the mouse wheel sets this; the camera eases to it
        bool orbiting;
        Drone following;          // double-clicked: the camera keeps it in the middle until you pan
        Drone lastClicked;
        float lastClickTime;

        public void Init(SandboxGame sandbox, SandboxHud overlay, Camera camera, Vector3 startFocus)
        {
            game = sandbox;
            hud = overlay;
            Cam = camera;
            focus = startFocus;
            ApplyCamera();
        }

        // Behind the main menu the camera circles the middle of the map.
        public void Orbit(bool on)
        {
            orbiting = on;
            if (on) LookAt(new Vector3(-10f, 0f, -10f), 105f, yaw);
        }

        // Points the camera at a spot on the map (used by missions and the auto-test).
        public void LookAt(Vector3 point, float zoomDistance, float yawDegrees)
        {
            focus = Ground.OnGround(point);
            distance = zoomTarget = zoomDistance;
            yaw = yawDegrees;
            following = null;
            ApplyCamera();
        }

        void Update()
        {
            if (Cam == null) return;
            selected.RemoveAll(d => d == null || !d.IsAlive);
            if (PhotoMode.Active)
            {
                IsDragging = false;
                HoveredEnemy = null;
                return; // the photo camera has the controls
            }
            if (game.UiBlocking)
            {
                // Menus and mission panels have the mouse; only the menu's slow orbit moves the camera.
                IsDragging = false;
                HoveredEnemy = null;
                if (orbiting)
                {
                    yaw += 4f * Time.unscaledDeltaTime;
                    ApplyCamera();
                }
                return;
            }
            MoveCamera(Time.unscaledDeltaTime);
            HandleKeys();
            HandleMouse();
        }

        // ---- Camera ----

        void MoveCamera(float dt)
        {
            var pan = Vector3.zero;
            if (GameInput.Held(GameKey.Up)) pan.z += 1f;
            if (GameInput.Held(GameKey.Down)) pan.z -= 1f;
            if (GameInput.Held(GameKey.Right)) pan.x += 1f;
            if (GameInput.Held(GameKey.Left)) pan.x -= 1f;
            if (pan != Vector3.zero) following = null;
            if (following != null && following.IsAlive)
                focus = Vector3.Lerp(focus, following.transform.position, 1f - Mathf.Exp(-6f * dt));
            focus += Quaternion.Euler(0f, yaw, 0f) * Vector3.ClampMagnitude(pan, 1f) * distance * 0.9f * dt;
            float limit = SandboxGame.MapHalfSize;
            focus.x = Mathf.Clamp(focus.x, -limit, limit);
            focus.z = Mathf.Clamp(focus.z, -limit, limit);
            focus.y = Mathf.Lerp(focus.y, Ground.HeightAt(focus), 1f - Mathf.Exp(-5f * dt)); // glide over hills

            if (GameInput.Held(GameKey.RotateLeft)) yaw += 90f * dt;
            if (GameInput.Held(GameKey.RotateRight)) yaw -= 90f * dt;

            float scroll = GameInput.Scroll;
            if (scroll != 0f && !hud.IsPointerOverHud(GameInput.MousePosition))
                zoomTarget = Mathf.Clamp(zoomTarget * (scroll > 0f ? 0.85f : 1.15f), 15f, 110f);
            distance = Mathf.Lerp(distance, zoomTarget, 1f - Mathf.Exp(-dt / 0.15f)); // eased over about 0.15 s

            ApplyCamera();
        }

        // Puts the camera back where the RTS view had it (after photo mode).
        public void Restore() => ApplyCamera();

        void ApplyCamera()
        {
            // Look more steeply down when zoomed out, flatter when zoomed in.
            float pitch = Mathf.Lerp(42f, 64f, Mathf.InverseLerp(15f, 110f, distance));
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Cam.transform.position = focus + rotation * new Vector3(0f, 0f, -distance);
            Cam.transform.rotation = rotation;
        }

        // ---- Keyboard ----

        void HandleKeys()
        {
            if (GameInput.Pressed(GameKey.SelectAll))
            {
                selected.Clear();
                selected.AddRange(game.drones);
            }
            if (GameInput.Pressed(GameKey.Deselect)) selected.Clear();
            if (GameInput.Pressed(GameKey.Stop))
                foreach (var d in selected) d.Stop();
            if (GameInput.Pressed(GameKey.Help)) hud.ToggleHelp();
            if (GameInput.Pressed(GameKey.Pause)) game.TogglePause();
            if (GameInput.Pressed(GameKey.Feed) && DroneFeed.Instance != null) DroneFeed.Instance.Visible = !DroneFeed.Instance.Visible;
            if (GameInput.Pressed(GameKey.Recall))
            {
                game.Recall(selected);
                Sound.Ui("move");
            }
            if (GameInput.Pressed(GameKey.Focus) && selected.Count > 0)
            {
                Vector3 middle = Vector3.zero;
                foreach (var d in selected) middle += d.transform.position;
                LookAt(middle / selected.Count, distance, yaw);
            }
            if (GameInput.Pressed(GameKey.Launch1)) game.SpawnDrone(DroneType.Scout);
            if (GameInput.Pressed(GameKey.Launch2)) game.SpawnDrone(DroneType.Strike);
            if (GameInput.Pressed(GameKey.Launch3)) game.SpawnDrone(DroneType.Kamikaze);
            if (GameInput.Pressed(GameKey.Launch4) && game.BomberAvailable) game.SpawnDrone(DroneType.Bomber);
            if (GameInput.Pressed(GameKey.Thermal)) game.ToggleThermal();
            if (GameInput.Pressed(GameKey.SkipTime) && game.Mode == GameMode.Sandbox && DayNight.Instance != null)
                DayNight.Instance.SetTime(DayNight.Instance.hour + 2f, DayNight.Instance.hoursPerMinute);
        }

        // ---- Mouse ----

        void HandleMouse()
        {
            Vector2 mouse = GameInput.MousePosition;
            bool overHud = hud.IsPointerOverHud(mouse);
            HoveredEnemy = overHud ? null : PickEnemy(mouse);

            if (GameInput.LeftDown && !overHud)
            {
                IsDragging = true;
                DragStart = mouse;
            }
            // Also finish the drag if the release happened outside the window.
            if (IsDragging && (GameInput.LeftUp || !GameInput.LeftHeld))
            {
                IsDragging = false;
                bool additive = GameInput.Held(GameKey.Shift);
                int before = selected.Count;
                if ((mouse - DragStart).magnitude < ClickPixels) ClickSelect(mouse, additive);
                else BoxSelect(ScreenRect(DragStart, mouse), additive);
                if (selected.Count > 0 && selected.Count != before) Sound.Ui("select");
            }

            if (GameInput.RightDown && !overHud && selected.Count > 0) IssueOrder(mouse);
        }

        void ClickSelect(Vector2 mouse, bool additive)
        {
            Drone hit = PickDrone(mouse);
            // A double-click on a drone follows it with the camera.
            if (hit != null && hit == lastClicked && Time.unscaledTime - lastClickTime < 0.35f)
            {
                following = hit;
                game.ShowMessage("Following " + hit.unitName + " (pan to stop)");
            }
            lastClicked = hit;
            lastClickTime = Time.unscaledTime;
            if (!additive) selected.Clear();
            if (hit == null) return;
            if (additive && selected.Contains(hit)) selected.Remove(hit);
            else if (!selected.Contains(hit)) selected.Add(hit);
        }

        void BoxSelect(Rect box, bool additive)
        {
            if (!additive) selected.Clear();
            foreach (var d in game.drones)
                if (ToScreen(d.AimPoint, out Vector2 p) && box.Contains(p) && !selected.Contains(d))
                    selected.Add(d);
        }

        void IssueOrder(Vector2 mouse)
        {
            if (HoveredEnemy != null && !HoveredEnemy.Observed)
            {
                // Something we can't see right now: fly to where it was, ready to engage.
                Vector3 last = Ground.OnGround(HoveredEnemy.MarkerPosition);
                foreach (var d in selected) d.MoveTo(last, GameInput.Held(GameKey.Shift), true);
                hud.Acknowledge(last, true);
                Sound.Ui("attack");
                return;
            }
            if (HoveredEnemy != null)
            {
                foreach (var d in selected) d.Attack(HoveredEnemy);
                Effects.Marker(HoveredEnemy.transform.position, Art.EnemyRed);
                hud.Acknowledge(HoveredEnemy.AimPoint, true);
                Sound.Ui("attack");
                return;
            }
            if (!GroundPoint(mouse, out Vector3 point)) return;
            bool queue = GameInput.Held(GameKey.Shift), attackMove = GameInput.Held(GameKey.Ctrl);
            Effects.Marker(point, attackMove ? new Color(1f, 0.76f, 0.35f) : new Color(0.35f, 1f, 0.6f));
            hud.Acknowledge(point, false);
            Sound.Ui(attackMove ? "attack" : "move");

            // Spread the group in a loose grid around the clicked point, lined up with the camera.
            int count = selected.Count;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt(count / (float)columns);
            const float spacing = 3f;
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < count; i++)
            {
                int row = i / columns, column = i % columns;
                var offset = new Vector3((column - (columns - 1) * 0.5f) * spacing, 0f, -(row - (rows - 1) * 0.5f) * spacing);
                selected[i].MoveTo(point + facing * offset, queue, attackMove);
            }
        }

        // ---- Picking helpers ----

        Drone PickDrone(Vector2 mouse)
        {
            Drone best = null;
            float bestPixels = 30f * hud.Scale;
            foreach (var d in game.drones)
            {
                if (!ToScreen(d.AimPoint, out Vector2 p)) continue;
                float pixels = Vector2.Distance(p, mouse);
                if (pixels < bestPixels)
                {
                    best = d;
                    bestPixels = pixels;
                }
            }
            return best;
        }

        EnemyUnit PickEnemy(Vector2 mouse)
        {
            EnemyUnit best = null;
            float bestPixels = 36f * hud.Scale;
            foreach (var e in game.enemies)
            {
                if (!e.Known || !ToScreen(e.MarkerPosition, out Vector2 p)) continue;
                float pixels = Vector2.Distance(p, mouse);
                if (pixels < bestPixels)
                {
                    best = e;
                    bestPixels = pixels;
                }
            }
            return best;
        }

        public bool ToScreen(Vector3 world, out Vector2 screen)
        {
            Vector3 p = Cam.WorldToScreenPoint(world);
            screen = new Vector2(p.x, p.y);
            return p.z > 0f;
        }

        public bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            return Ground.Raycast(Cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f)), out point);
        }

        static Rect ScreenRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
    }
}

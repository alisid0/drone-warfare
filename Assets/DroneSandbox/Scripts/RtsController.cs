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

        SandboxGame game;
        SandboxHud hud;
        Vector3 focus;
        float yaw = 45f;
        float distance = 55f;

        public void Init(SandboxGame sandbox, SandboxHud overlay, Camera camera, Vector3 startFocus)
        {
            game = sandbox;
            hud = overlay;
            Cam = camera;
            focus = startFocus;
            ApplyCamera();
        }

        // Points the camera at a spot on the map (used by the auto-test; handy for future cutscenes too).
        public void LookAt(Vector3 point, float zoomDistance, float yawDegrees)
        {
            focus = new Vector3(point.x, 0f, point.z);
            distance = zoomDistance;
            yaw = yawDegrees;
            ApplyCamera();
        }

        void Update()
        {
            if (Cam == null) return;
            selected.RemoveAll(d => d == null || !d.IsAlive);
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
            focus += Quaternion.Euler(0f, yaw, 0f) * Vector3.ClampMagnitude(pan, 1f) * distance * 0.9f * dt;
            float limit = SandboxGame.MapHalfSize;
            focus.x = Mathf.Clamp(focus.x, -limit, limit);
            focus.z = Mathf.Clamp(focus.z, -limit, limit);

            if (GameInput.Held(GameKey.RotateLeft)) yaw += 90f * dt;
            if (GameInput.Held(GameKey.RotateRight)) yaw -= 90f * dt;

            float scroll = GameInput.Scroll;
            if (scroll != 0f && !hud.IsPointerOverHud(GameInput.MousePosition))
                distance = Mathf.Clamp(distance * (scroll > 0f ? 0.88f : 1.12f), 15f, 110f);

            ApplyCamera();
        }

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
            if (GameInput.Pressed(GameKey.Help)) hud.showHelp = !hud.showHelp;
            if (GameInput.Pressed(GameKey.Launch1)) game.SpawnDrone(DroneType.Scout);
            if (GameInput.Pressed(GameKey.Launch2)) game.SpawnDrone(DroneType.Strike);
            if (GameInput.Pressed(GameKey.Launch3)) game.SpawnDrone(DroneType.Kamikaze);
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
                if ((mouse - DragStart).magnitude < ClickPixels) ClickSelect(mouse, additive);
                else BoxSelect(ScreenRect(DragStart, mouse), additive);
            }

            if (GameInput.RightDown && !overHud && selected.Count > 0) IssueOrder(mouse);
        }

        void ClickSelect(Vector2 mouse, bool additive)
        {
            Drone hit = PickDrone(mouse);
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
            if (HoveredEnemy != null)
            {
                foreach (var d in selected) d.Attack(HoveredEnemy);
                Effects.Marker(HoveredEnemy.transform.position, Art.EnemyRed);
                return;
            }
            if (!GroundPoint(mouse, out Vector3 point)) return;
            Effects.Marker(point, new Color(0.35f, 1f, 0.6f));

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
                selected[i].MoveTo(point + facing * offset);
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
                if (!ToScreen(e.AimPoint, out Vector2 p)) continue;
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
            Ray ray = Cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }
            point = Vector3.zero;
            return false;
        }

        static Rect ScreenRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
    }
}

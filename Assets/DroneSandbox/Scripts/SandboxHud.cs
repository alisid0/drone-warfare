using UnityEngine;

namespace DroneSandbox
{
    // On-screen HUD drawn with Unity's immediate-mode GUI (OnGUI): health bars, selection
    // brackets, AA threat rings, launch buttons and help. Fine for a prototype; move to
    // UI Toolkit once the look of the game is decided.
    public class SandboxHud : MonoBehaviour
    {
        const float LaunchButtonWidth = 160f;
        const float LaunchButtonGap = 8f;
        const float DroneFlightAltitude = 8f; // threat rings show AA reach at this height

        static readonly Color Friendly = new Color(0.35f, 1f, 0.6f);
        static readonly Color Hostile = new Color(1f, 0.32f, 0.25f);
        static readonly Color PanelColor = new Color(0.04f, 0.07f, 0.09f, 0.8f);

        static readonly string[] LaunchNames = { "Scout", "Strike", "Kamikaze" };
        static readonly string[] LaunchInfo = { "fast, machine gun", "long-range missiles", "one-way attack" };
        static readonly string[] HelpLines =
        {
            "<b>CONTROLS</b>   (H to hide)",
            "Left-click / drag  -  select drones",
            "Shift + click  -  add to selection",
            "Right-click  -  move, or attack an enemy",
            "WASD / arrows  -  pan     Q / E  -  rotate",
            "Mouse wheel  -  zoom",
            "1 / 2 / 3  -  launch Scout / Strike / Kamikaze",
            "Space  -  select all     X  -  stop     Esc  -  deselect",
            "Red dots: AA gun reach. Radars boost nearby AA.",
        };

        public bool showHelp = true;
        public float Scale => Mathf.Max(1f, Screen.height / 900f);

        SandboxGame game;
        RtsController rts;
        GUIStyle label, title, small, centered, button;
        float stylesBuiltForScale;

        public void Init(SandboxGame sandbox, RtsController controller)
        {
            game = sandbox;
            rts = controller;
        }

        // ---- Layout (GUI space: origin at top-left) ----

        Rect StatsPanel() => new Rect(12f * Scale, 12f * Scale, 360f * Scale, 84f * Scale);

        Rect HelpPanel()
        {
            float s = Scale;
            float height = (HelpLines.Length * 20f + 16f) * s;
            return new Rect(Screen.width - 392f * s, 12f * s, 380f * s, height);
        }

        Rect LaunchBar()
        {
            float s = Scale;
            float width = (3f * LaunchButtonWidth + 2f * LaunchButtonGap) * s;
            return new Rect((Screen.width - width) / 2f, Screen.height - 64f * s, width, 50f * s);
        }

        Rect SelectionPanel() => new Rect(12f * Scale, Screen.height - 124f * Scale, 330f * Scale, 112f * Scale);

        // Mouse position is in screen space (origin bottom-left), as the input backends report it.
        public bool IsPointerOverHud(Vector2 screenPosition)
        {
            var p = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            float pad = 6f * Scale;
            Rect bar = LaunchBar();
            bar = new Rect(bar.x - pad, bar.y - pad, bar.width + 2f * pad, bar.height + 2f * pad);
            return bar.Contains(p)
                || StatsPanel().Contains(p)
                || (showHelp && HelpPanel().Contains(p))
                || (rts != null && rts.selected.Count > 0 && SelectionPanel().Contains(p));
        }

        // ---- Drawing ----

        void OnGUI()
        {
            if (game == null || rts == null || rts.Cam == null) return;
            BuildStyles();

            if (Event.current.type == EventType.Repaint)
            {
                DrawWorldOverlays();
                DrawDragBox();
            }
            DrawStats();
            DrawLaunchBar();
            DrawSelection();
            DrawHelp();
        }

        void BuildStyles()
        {
            float s = Scale;
            if (label != null && Mathf.Approximately(stylesBuiltForScale, s)) return;
            stylesBuiltForScale = s;

            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14f * s), richText = true };
            label.normal.textColor = new Color(0.85f, 0.95f, 1f);
            title = new GUIStyle(label) { fontSize = Mathf.RoundToInt(18f * s), fontStyle = FontStyle.Bold };
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(12f * s) };
            small.normal.textColor = new Color(0.65f, 0.75f, 0.8f);
            centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(14f * s), richText = true };
        }

        void DrawWorldOverlays()
        {
            float s = Scale;

            foreach (var e in game.enemies)
                if (e.type == EnemyType.AAGun) ThreatRing(e, e == rts.HoveredEnemy);

            foreach (var e in game.enemies)
            {
                if (!ToGui(e.AimPoint, out Vector2 c)) continue;
                bool hovered = e == rts.HoveredEnemy;
                float half = PixelRadius(e.AimPoint, e.radius);
                if (hovered)
                {
                    Brackets(c, half, Hostile);
                    GUI.Label(new Rect(c.x - 80f * s, c.y - half - 36f * s, 160f * s, 20f * s), e.unitName, centered);
                }
                if (hovered || e.hp < e.maxHp) HealthBar(new Vector2(c.x, c.y - half - 10f * s), e.hp / e.maxHp, Hostile);
            }

            foreach (var d in game.drones)
            {
                if (!ToGui(d.AimPoint, out Vector2 c)) continue;
                bool isSelected = rts.selected.Contains(d);
                float half = PixelRadius(d.AimPoint, d.radius);
                if (isSelected) Brackets(c, half, Friendly);
                if (isSelected || d.hp < d.maxHp) HealthBar(new Vector2(c.x, c.y - half - 8f * s), d.hp / d.maxHp, Friendly);
            }
        }

        // Dotted line on the ground showing how far this AA gun reaches a drone cruising at DroneFlightAltitude
        // over that ground. It bulges out over hollows and pulls in over hills.
        void ThreatRing(EnemyUnit aa, bool bright)
        {
            const int dots = 64;
            float range = aa.CurrentRange;
            float size = 3f * Scale;
            Color color = new Color(Hostile.r, Hostile.g, Hostile.b, bright ? 0.95f : 0.4f);
            for (int i = 0; i < dots; i++)
            {
                float a = i * Mathf.PI * 2f / dots;
                var direction = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = aa.transform.position + direction * aa.ThreatRadiusToward(direction, DroneFlightAltitude, range);
                p.y = Ground.HeightAt(p) + 0.1f;
                if (ToGui(p, out Vector2 g)) Fill(new Rect(g.x - size / 2f, g.y - size / 2f, size, size), color);
            }
        }

        void DrawDragBox()
        {
            if (!rts.IsDragging) return;
            Vector2 a = rts.DragStart, b = GameInput.MousePosition;
            if ((b - a).magnitude < 6f) return;
            var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y),
                Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
            Fill(r, new Color(Friendly.r, Friendly.g, Friendly.b, 0.12f));
            Outline(r, Friendly, Mathf.Max(1f, Scale));
        }

        void DrawStats()
        {
            float s = Scale;
            Rect r = StatsPanel();
            Fill(r, PanelColor);
            GUI.Label(new Rect(r.x + 10f * s, r.y + 6f * s, r.width, 26f * s), "DRONE SANDBOX", title);
            GUI.Label(new Rect(r.x + 10f * s, r.y + 34f * s, r.width, 22f * s),
                $"Targets destroyed: <b>{game.enemiesDestroyed}</b>     Enemy units: {game.enemies.Count}", label);
            GUI.Label(new Rect(r.x + 10f * s, r.y + 56f * s, r.width, 22f * s),
                $"Drones: <b>{game.drones.Count}</b>/{SandboxGame.MaxDrones}     Lost: {game.dronesLost}     Expended: {game.dronesExpended}", label);
        }

        void DrawLaunchBar()
        {
            float s = Scale;
            Rect bar = LaunchBar();
            float pad = 6f * s;
            Fill(new Rect(bar.x - pad, bar.y - pad, bar.width + 2f * pad, bar.height + 2f * pad), PanelColor);

            GUI.enabled = game.drones.Count < SandboxGame.MaxDrones;
            for (int i = 0; i < 3; i++)
            {
                var b = new Rect(bar.x + i * (LaunchButtonWidth + LaunchButtonGap) * s, bar.y, LaunchButtonWidth * s, bar.height);
                string text = $"<b>[{i + 1}] {LaunchNames[i]}</b>\n<size={Mathf.RoundToInt(11f * s)}>{LaunchInfo[i]}</size>";
                if (GUI.Button(b, text, button)) game.SpawnDrone((DroneType)i);
            }
            GUI.enabled = true;
        }

        void DrawSelection()
        {
            var sel = rts.selected;
            if (sel.Count == 0) return;
            float s = Scale;
            Rect r = SelectionPanel();
            Fill(r, PanelColor);
            float x = r.x + 10f * s;
            float y = r.y + 8f * s;

            void Line(string text, GUIStyle style)
            {
                GUI.Label(new Rect(x, y, r.width - 20f * s, 22f * s), text, style);
                y += 24f * s;
            }

            if (sel.Count == 1)
            {
                var d = sel[0];
                Line($"<b>{d.unitName}</b>     {Mathf.CeilToInt(d.hp)} / {Mathf.CeilToInt(d.maxHp)} HP", label);
                Line(d.Status, label);
                Line(d.IsKamikaze ? "Right-click an enemy to dive on it." : "Fires at enemies in range when idle.", small);
            }
            else
            {
                int scouts = 0, strikes = 0, kamikazes = 0;
                foreach (var d in sel)
                {
                    if (d.type == DroneType.Scout) scouts++;
                    else if (d.type == DroneType.Strike) strikes++;
                    else kamikazes++;
                }
                Line($"<b>{sel.Count} drones selected</b>", label);
                Line($"Scout {scouts}     Strike {strikes}     Kamikaze {kamikazes}", label);
                Line("Right-click an enemy to attack together.", small);
            }
            Line("Right-click ground: move", small);
        }

        void DrawHelp()
        {
            float s = Scale;
            if (!showHelp)
            {
                GUI.Label(new Rect(Screen.width - 140f * s, 12f * s, 128f * s, 22f * s), "H  -  help", small);
                return;
            }
            Rect r = HelpPanel();
            Fill(r, PanelColor);
            for (int i = 0; i < HelpLines.Length; i++)
                GUI.Label(new Rect(r.x + 10f * s, r.y + 8f * s + i * 20f * s, r.width - 20f * s, 20f * s), HelpLines[i],
                    i == 0 ? label : small);
        }

        // ---- Primitives ----

        bool ToGui(Vector3 world, out Vector2 gui)
        {
            Vector3 p = rts.Cam.WorldToScreenPoint(world);
            gui = new Vector2(p.x, Screen.height - p.y);
            return p.z > 0f;
        }

        float PixelRadius(Vector3 world, float radius)
        {
            Vector3 a = rts.Cam.WorldToScreenPoint(world);
            Vector3 b = rts.Cam.WorldToScreenPoint(world + rts.Cam.transform.right * radius);
            return Mathf.Max(Mathf.Abs(b.x - a.x), 10f * Scale);
        }

        void HealthBar(Vector2 center, float fraction, Color color)
        {
            float s = Scale;
            float w = 34f * s, h = 4f * s;
            var back = new Rect(center.x - w / 2f, center.y - h / 2f, w, h);
            Fill(back, new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(back.x, back.y, w * Mathf.Clamp01(fraction), h), color);
        }

        void Brackets(Vector2 c, float half, Color color)
        {
            float t = Mathf.Max(1f, 2f * Scale);
            float len = half * 0.6f;
            float left = c.x - half, right = c.x + half, top = c.y - half, bottom = c.y + half;
            Fill(new Rect(left, top, len, t), color);
            Fill(new Rect(left, top, t, len), color);
            Fill(new Rect(right - len, top, len, t), color);
            Fill(new Rect(right - t, top, t, len), color);
            Fill(new Rect(left, bottom - t, len, t), color);
            Fill(new Rect(left, bottom - len, t, len), color);
            Fill(new Rect(right - len, bottom - t, len, t), color);
            Fill(new Rect(right - t, bottom - len, t, len), color);
        }

        static void Outline(Rect r, Color color, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), color);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), color);
            Fill(new Rect(r.x, r.y, t, r.height), color);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), color);
        }

        static void Fill(Rect r, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}

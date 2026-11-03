using UnityEngine;

namespace DroneSandbox
{
    // On-screen HUD drawn with Unity's immediate-mode GUI (OnGUI): the main menu, the theatre map, tech shop, mission
    // briefing and results, health bars, selection brackets, target markers, search circles, AA threat rings,
    // jamming circles, launch buttons and help.
    // Fine for a prototype; move to UI Toolkit once the look of the game is decided.
    public partial class SandboxHud : MonoBehaviour
    {
        const float LaunchButtonWidth = 160f;
        const float LaunchButtonGap = 8f;
        const float DroneFlightAltitude = 8f; // threat rings show AA reach at this height

        static readonly Color Friendly = new Color(0.35f, 1f, 0.6f);
        static readonly Color Hostile = new Color(1f, 0.32f, 0.25f);
        static readonly Color TargetColor = new Color(1f, 0.82f, 0.2f);
        static readonly Color PanelColor = new Color(0.04f, 0.07f, 0.09f, 0.8f);
        static readonly Color ModalColor = new Color(0.03f, 0.05f, 0.07f, 0.94f);

        static readonly string[] LaunchNames = { "Scout", "Strike", "Kamikaze", "Nightjar" };
        static readonly string[] LaunchInfo = { "fast, machine gun", "long-range missiles", "one-way attack", "heavy bomber, rearms" };
        static readonly string[] HelpLines =
        {
            "<b>CONTROLS</b>   (H to hide)",
            "Left-click / drag  -  select drones",
            "Shift + click  -  add to selection",
            "Right-click  -  move, or attack an enemy",
            "Shift + right-click  -  queue a waypoint",
            "Ctrl + right-click  -  attack-move (engage on the way)",
            "Tab  -  tactical pause     F  -  centre on selection",
            "B  -  bring selected (or all) drones home",
            "WASD / arrows  -  pan     Q / E  -  rotate",
            "Mouse wheel  -  zoom",
            "1 - 4  -  launch Scout / Strike / Kamikaze / Nightjar",
            "Space  -  select all     X  -  stop     Esc  -  deselect",
            "Red dots: AA gun reach. Radars boost nearby AA.",
            "T  -  thermal view (Wren-T Scout or Nightjar)",
            "N  -  skip 2 hours (free play)",
            "P  -  photo mode: fly a camera drone, F12 saves",
            "Yellow: mission targets and search areas.",
        };

        public bool showHelp = true;
        public bool ShopOpen;  // the tech shop, opened from the main menu
        public bool TheatreOpen; // the theatre map, opened from the main menu
        Hotspot chosen;          // the hotspot picked on the theatre map
        static Texture2D dotTexture;
        public float Scale => Mathf.Max(1f, Screen.height / 900f);

        SandboxGame game;
        RtsController rts;
        GUIStyle label, title, small, smallWrap, centered, button, leftButton, big, heading, wrap, targetLabel, lockedLabel, placeLabel, placeShadow;
        float stylesBuiltForScale;
        static Texture2D starTexture;

        public void Init(SandboxGame sandbox, RtsController controller)
        {
            game = sandbox;
            rts = controller;
        }

        // ---- Layout (GUI space: origin at top-left) ----

        Rect StatsPanel() => new Rect(12f * Scale, 12f * Scale, 360f * Scale, 84f * Scale);

        Rect MenuButton() => new Rect(StatsPanel().xMax + 8f * Scale, 12f * Scale, 72f * Scale, 30f * Scale);

        Rect HelpPanel()
        {
            float s = Scale;
            float height = (HelpLines.Length * 20f + 16f) * s;
            return new Rect(Screen.width - 392f * s, 12f * s, 380f * s, height);
        }

        int LaunchCount => game.BomberAvailable ? 4 : 3;

        Rect LaunchBar()
        {
            float s = Scale;
            float width = (LaunchCount * LaunchButtonWidth + (LaunchCount - 1) * LaunchButtonGap) * s;
            return new Rect((Screen.width - width) / 2f, Screen.height - 64f * s, width, 50f * s);
        }

        Rect SelectionPanel() => new Rect(12f * Scale, Screen.height - 124f * Scale, 330f * Scale, 112f * Scale);

        Rect Centered(float width, float height)
        {
            float s = Scale;
            return new Rect((Screen.width - width * s) / 2f, (Screen.height - height * s) / 2f, width * s, height * s);
        }

        // Mouse position is in screen space (origin bottom-left), as the input backends report it.
        public bool IsPointerOverHud(Vector2 screenPosition)
        {
            if (game != null && game.UiBlocking) return true;
            var p = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            float pad = 6f * Scale;
            Rect bar = LaunchBar();
            bar = new Rect(bar.x - pad, bar.y - pad, bar.width + 2f * pad, bar.height + 2f * pad);
            return bar.Contains(p)
                || StatsPanel().Contains(p)
                || MenuButton().Contains(p)
                || (showHelp && HelpPanel().Contains(p))
                || (rts != null && rts.selected.Count > 0 && SelectionPanel().Contains(p));
        }

        // ---- Drawing ----

        void OnGUI()
        {
            if (game == null || rts == null || rts.Cam == null) return;
            BuildStyles();

            if (game.Loading)
            {
                DrawLoading();
                return;
            }
            if (game.Mode == GameMode.Menu)
            {
                if (ShopOpen) DrawShop();
                else if (TheatreOpen) DrawTheatre();
                else DrawMenu();
                return;
            }

            if (PhotoMode.Active)
            {
                DrawPhotoMode();
                return;
            }

            if (Event.current.type == EventType.Repaint)
            {
                DrawWorldOverlays();
                DrawDragBox();
            }
            if (!game.UiBlocking) AutoHideHelp();
            GUI.enabled = !game.UiBlocking;
            DrawStats();
            DrawLaunchBar();
            DrawSelection();
            DrawHelp();
            GUI.enabled = true;
            DrawMessage();
            DrawPauseBanner();

            if (game.BriefingOpen) DrawBriefing();
            else if (game.Result != MissionResult.None && Time.unscaledTime >= game.ResultsAt) DrawResults();
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
            smallWrap = new GUIStyle(small) { wordWrap = true };
            centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(14f * s), richText = true };
            leftButton = new GUIStyle(button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(Mathf.RoundToInt(16f * s), 8, 4, 4) };
            big = new GUIStyle(label) { fontSize = Mathf.RoundToInt(34f * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            heading = new GUIStyle(label) { fontSize = Mathf.RoundToInt(24f * s), fontStyle = FontStyle.Bold };
            wrap = new GUIStyle(label) { wordWrap = true };
            targetLabel = Tinted(centered, TargetColor);
            lockedLabel = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            placeLabel = new GUIStyle(centered) { fontSize = Mathf.RoundToInt(13f * s), fontStyle = FontStyle.Bold };
            placeLabel.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
            placeShadow = new GUIStyle(placeLabel);
            placeShadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
        }

        void DrawWorldOverlays()
        {
            float s = Scale;
            if (rts.Distance >= 55f) DrawPlaceNames();

            foreach (var e in game.enemies)
            {
                if (e.Hidden) SearchCircle(e.searchCenter);
                else if (e.Intel < Intel.Identified) continue; // a gun nobody has found draws no ring
                else if (e.type == EnemyType.AAGun) ThreatRing(e, e == rts.HoveredEnemy);
                else if (e.type == EnemyType.Jammer) JamCircle(e);
            }

            foreach (var e in game.enemies)
            {
                if (e.Hidden) continue;
                if (!e.Drawn)
                {
                    IntelMarker(e);
                    continue;
                }
                if (!ToGui(e.AimPoint, out Vector2 c)) continue;
                bool hovered = e == rts.HoveredEnemy;
                float half = Mathf.Max(PixelRadius(e.AimPoint, e.radius), 14f * s);
                if (hovered)
                {
                    TargetBrackets(e, c, half, Hostile);
                    GUI.Label(new Rect(c.x - 80f * s, c.y - half - 36f * s, 160f * s, 20f * s), e.unitName, centered);
                }
                else if (e.isTarget)
                {
                    TargetBrackets(e, c, half + 4f * s, TargetColor);
                    GUI.Label(new Rect(c.x - 60f * s, c.y - half - 34f * s, 120f * s, 18f * s), "<b>TARGET</b>", targetLabel);
                }
                else EnemyMarker(e, c);
                if (hovered || e.hp < e.maxHp) HealthBar(new Vector2(c.x, c.y - half - 10f * s), e.hp / e.maxHp, Hostile);
            }

            DrawTactical();
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

        // Names of the (fictional) places, shown when the camera is zoomed out: the sector's villages, the two
        // bases, and its river where it passes nearest the middle (the Varrow Valley's by the lake).
        void DrawPlaceNames()
        {
            var plan = MapLayout.Plan;
            for (int i = 0; i < plan.villages.Count && i < plan.villageNames.Count; i++)
            {
                Vector2 v = plan.villages[i];
                PlaceName(new Vector3(v.x, 0f, v.y), 12f, plan.villageNames[i]);
            }
            PlaceName(game.basePosition, 16f, Factions.PlayerBase);
            PlaceName(game.enemyHqPosition, 18f, Factions.EnemyBase);
            var points = MapLayout.RiverField.points;
            if (points.Count == 0) return;
            Vector2 near = plan.kind == SectorKind.VarrowValley ? new Vector2(-105f, 2f) : Vector2.zero;
            Vector2 river = points[0];
            foreach (var p in points)
                if ((p - near).sqrMagnitude < (river - near).sqrMagnitude) river = p;
            PlaceName(new Vector3(river.x, 0f, river.y), 4f, plan.kind == SectorKind.VarrowValley ? Factions.River : plan.riverName);
        }

        // A jammer's circle on the ground: drones that fly into it lose their link.
        void JamCircle(EnemyUnit jammer)
        {
            const int dashes = 48;
            float size = 3.5f * Scale;
            var color = new Color(0.75f, 0.45f, 1f, 0.7f);
            for (int i = 0; i < dashes; i++)
            {
                if (i % 3 == 2) continue;
                float a = i * Mathf.PI * 2f / dashes;
                Vector3 p = jammer.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * EnemyUnit.JamRadius;
                p.y = Ground.HeightAt(p) + 0.1f;
                if (ToGui(p, out Vector2 g)) Fill(new Rect(g.x - size / 2f, g.y - size / 2f, size, size), color);
            }
        }

        void PlaceName(Vector3 at, float height, string text)
        {
            if (!ToGui(Ground.OnGround(at) + Vector3.up * height, out Vector2 g)) return;
            float s = Scale;
            var r = new Rect(g.x - 110f * s, g.y - 10f * s, 220f * s, 20f * s);
            GUI.Label(new Rect(r.x + 1.5f * s, r.y + 1.5f * s, r.width, r.height), text, placeShadow);
            GUI.Label(r, text, placeLabel);
        }

        // Dashed yellow circle on the ground where a hidden target is somewhere inside.
        void SearchCircle(Vector3 center)
        {
            const int dashes = 40;
            float size = 4f * Scale;
            var color = new Color(TargetColor.r, TargetColor.g, TargetColor.b, 0.8f);
            for (int i = 0; i < dashes; i++)
            {
                float a = i * Mathf.PI * 2f / dashes;
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * SandboxGame.SearchRadius;
                p.y = Ground.HeightAt(p) + 0.1f;
                if (ToGui(p, out Vector2 g)) Fill(new Rect(g.x - size / 2f, g.y - size / 2f, size, size), color);
            }
            Vector3 mid = Ground.OnGround(center) + Vector3.up * 0.5f;
            if (ToGui(mid, out Vector2 m))
                GUI.Label(new Rect(m.x - 70f * Scale, m.y - 10f * Scale, 140f * Scale, 20f * Scale), "<b>SEARCH AREA</b>", targetLabel);
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
            if (game.Mode == GameMode.Mission)
            {
                var m = game.Mission;
                GUI.Label(new Rect(r.x + 10f * s, r.y + 6f * s, r.width, 26f * s), m.name.ToUpperInvariant(), title);
                GUI.Label(new Rect(r.x + 10f * s, r.y + 34f * s, r.width, 22f * s), Objective(), label);
                GUI.Label(new Rect(r.x + 10f * s, r.y + 56f * s, r.width, 22f * s),
                    $"Drones used <b>{game.DronesUsed}</b>/{m.par}   lost <b>{game.dronesLost}</b>     {Clock(game.MissionTime)}", label);
            }
            else
            {
                GUI.Label(new Rect(r.x + 10f * s, r.y + 6f * s, r.width, 26f * s), "FREE PLAY", title);
                GUI.Label(new Rect(r.x + 10f * s, r.y + 34f * s, r.width, 22f * s),
                    $"Targets destroyed: <b>{game.enemiesDestroyed}</b>     Known enemies: {KnownEnemies()}", label);
                GUI.Label(new Rect(r.x + 10f * s, r.y + 56f * s, r.width, 22f * s),
                    $"Drones: <b>{game.drones.Count}</b>/{SandboxGame.MaxDrones}     Lost: {game.dronesLost}     Expended: {game.dronesExpended}", label);
            }
            var dayNight = DayNight.Instance;
            if (dayNight != null)
            {
                string clock = $"{dayNight.Clock}  {dayNight.PartOfDay}" + (dayNight.Thermal ? "   <b>THERMAL</b>" : "");
                GUI.Label(new Rect(r.x + 10f * s, r.y + 9f * s, r.width - 20f * s, 22f * s), clock,
                    new GUIStyle(small) { alignment = TextAnchor.UpperRight, richText = true });
            }
            if (GUI.Button(MenuButton(), "Menu", button)) game.OpenMenu();
        }

        void DrawLaunchBar()
        {
            float s = Scale;
            Rect bar = LaunchBar();
            float pad = 6f * s;
            Fill(new Rect(bar.x - pad, bar.y - pad, bar.width + 2f * pad, bar.height + 2f * pad), PanelColor);

            bool wasEnabled = GUI.enabled;
            for (int i = 0; i < LaunchCount; i++)
            {
                int left = game.StockLeft((DroneType)i);
                GUI.enabled = wasEnabled && game.drones.Count < SandboxGame.MaxDrones && left != 0;
                var b = new Rect(bar.x + i * (LaunchButtonWidth + LaunchButtonGap) * s, bar.y, LaunchButtonWidth * s, bar.height);
                string count = left >= 0 ? $"   x{left}" : "";
                string text = $"<b>[{i + 1}] {LaunchNames[i]}{count}</b>\n<size={Mathf.RoundToInt(11f * s)}>{LaunchInfo[i]}</size>";
                if (GUI.Button(b, text, button)) game.SpawnDrone((DroneType)i);
            }
            GUI.enabled = wasEnabled;
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
                string bombs = d.IsBomber ? $"     Bombs {d.BombsLeft}/{Drone.BomberLoad}" : "";
                Line($"<b>{d.unitName}</b>     {Mathf.CeilToInt(d.hp)} / {Mathf.CeilToInt(d.maxHp)} HP{bombs}", label);
                Line(d.Status, label);
                Line(d.IsKamikaze ? "Right-click an enemy to dive on it."
                    : d.IsBomber ? "Right-click an enemy to bomb it. Rearms at base."
                    : "Fires at enemies in range when idle.", small);
            }
            else
            {
                var counts = new int[LaunchNames.Length];
                foreach (var d in sel) counts[(int)d.type]++;
                Line($"<b>{sel.Count} drones selected</b>", label);
                Line($"Scout {counts[0]}     Strike {counts[1]}     Kamikaze {counts[2]}" + (counts[3] > 0 ? $"     Nightjar {counts[3]}" : ""), label);
                Line("Right-click an enemy to attack together.", small);
            }
            Line("Right-click: move   Shift: queue   Ctrl: attack-move", small);
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

        // Photo mode keeps the screen clear: the controls for a few seconds after it opens, and saved-photo news.
        void DrawPhotoMode()
        {
            var photo = PhotoMode.Instance;
            if (photo.Capturing) return;
            if (Time.unscaledTime < photo.HintUntil)
            {
                float s = Scale;
                var r = new Rect((Screen.width - 900f * s) / 2f, Screen.height - 70f * s, 900f * s, 52f * s);
                Fill(r, PanelColor);
                GUI.Label(new Rect(r.x, r.y + 4f * s, r.width, 22f * s), "<b>PHOTO MODE</b>", Tinted(centered, TargetColor));
                GUI.Label(new Rect(r.x, r.y + 26f * s, r.width, 20f * s),
                    "WASD fly   R / F up, down   Q / E turn   right-drag look   wheel zoom   Shift fast   [ ] time of day   F12 save   P exit",
                    new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            }
            DrawMessage();
        }

        // Short news at the top of the screen: a target spotted or destroyed, no drones of a type left.
        void DrawMessage()
        {
            if (string.IsNullOrEmpty(game.Message) || Time.unscaledTime > game.MessageUntil) return;
            float s = Scale;
            var r = new Rect((Screen.width - 420f * s) / 2f, 16f * s, 420f * s, 34f * s);
            Fill(r, PanelColor);
            GUI.Label(r, $"<b>{game.Message}</b>", targetLabel);
        }

        // ---- Menu, briefing, results ----

        void DrawMenu()
        {
            float s = Scale;
            var missions = Missions.All;
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.3f));
            float rowHeight = 50f, gap = 8f;
            float height = 172f + (missions.Count + 3) * (rowHeight + gap) + 56f;
            Rect r = Centered(580f, height);
            Fill(r, ModalColor);

            GUI.Label(new Rect(r.x, r.y + 18f * s, r.width, 44f * s), "DRONE WARFARE", big);
            GUI.Label(new Rect(r.x, r.y + 66f * s, r.width, 22f * s), "<b>ASSET HUNT</b>", Tinted(centered, TargetColor));
            GUI.Label(new Rect(r.x + 30f * s, r.y + 94f * s, r.width - 60f * s, 22f * s),
                "Destroy enemy targets with a limited stock of drones. Fewer drones, more stars.",
                Tinted(new GUIStyle(wrap) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.RoundToInt(13f * s) }, small.normal.textColor));
            GUI.Label(new Rect(r.x + 30f * s, r.y + 120f * s, r.width - 60f * s, 40f * s),
                $"{Capitalised(Factions.Region)}: you fly for the {Factions.Player} against the {Factions.Enemy}. " +
                "All sides and places are fictional.",
                Tinted(new GUIStyle(wrap) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.RoundToInt(12f * s) }, small.normal.textColor));

            float y = r.y + 172f * s;
            var theatre = new Rect(r.x + 30f * s, y, r.width - 60f * s, rowHeight * s);
            if (GUI.Button(theatre, "<b>Theatre of war</b>   <size=" + Mathf.RoundToInt(12f * s) + ">pick a hotspot on the front: 30 kinds of sector, 30 situations</size>", leftButton))
            {
                TheatreOpen = true;
                chosen = null;
            }
            y += (rowHeight + gap) * s;
            for (int i = 0; i < missions.Count; i++)
            {
                var row = new Rect(r.x + 30f * s, y, r.width - 60f * s, rowHeight * s);
                bool open = SandboxGame.Unlocked(i);
                GUI.enabled = open;
                if (GUI.Button(row, $"<b>{i + 1}.  {missions[i].name}</b>", leftButton)) game.StartMission(i);
                GUI.enabled = true;
                if (open) DrawStars(new Rect(row.xMax - 110f * s, row.y + 13f * s, 96f * s, 24f * s), SandboxGame.BestStars(i));
                else GUI.Label(new Rect(row.xMax - 110f * s, row.y, 96f * s, row.height), "Locked", lockedLabel);
                y += (rowHeight + gap) * s;
            }
            var sandbox = new Rect(r.x + 30f * s, y, r.width - 60f * s, rowHeight * s);
            if (GUI.Button(sandbox, "<b>Free play sandbox</b>   <size=" + Mathf.RoundToInt(12f * s) + ">unlimited drones, all tech, enemies respawn</size>", leftButton))
                game.StartSandbox();
            y += (rowHeight + gap) * s;
            var shop = new Rect(r.x + 30f * s, y, r.width - 60f * s, rowHeight * s);
            if (GUI.Button(shop, "<b>Tech shop</b>   <size=" + Mathf.RoundToInt(12f * s) + ">new drones and upgrades</size>", leftButton))
                ShopOpen = true;
            GUI.Label(new Rect(shop.xMax - 130f * s, shop.y, 116f * s, shop.height), $"<b>{Tech.Points}</b> points",
                Tinted(new GUIStyle(label) { alignment = TextAnchor.MiddleRight }, TargetColor));
            GUI.Label(new Rect(r.x, r.yMax - 40f * s, r.width, 24f * s), "Finish a mission to unlock the next one.", Tinted(centered, small.normal.textColor));
        }

        // Buy new drones and upgrades with points earned in missions. Purchases are kept between missions.
        void DrawShop()
        {
            float s = Scale;
            var items = Tech.Items;
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.3f));
            float rowHeight = 62f, gap = 6f;
            Rect r = Centered(700f, 132f + items.Length * (rowHeight + gap) + 72f);
            Fill(r, ModalColor);

            GUI.Label(new Rect(r.x, r.y + 16f * s, r.width, 44f * s), "TECH SHOP", big);
            GUI.Label(new Rect(r.x, r.y + 62f * s, r.width, 24f * s),
                $"<b>{Tech.Points}</b> points     {Tech.OwnedCount} of {items.Length} bought", Tinted(centered, TargetColor));
            GUI.Label(new Rect(r.x + 40f * s, r.y + 90f * s, r.width - 80f * s, 36f * s),
                "Earn points by destroying Dravin units in missions, plus a bonus for each new star. " +
                "Free play has all the tech.",
                Tinted(new GUIStyle(wrap) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.RoundToInt(12f * s) }, small.normal.textColor));

            float y = r.y + 132f * s;
            foreach (var item in items)
            {
                var row = new Rect(r.x + 24f * s, y, r.width - 48f * s, rowHeight * s);
                bool owned = Tech.Owns(item.id);
                Fill(row, owned ? new Color(Friendly.r, Friendly.g, Friendly.b, 0.08f) : new Color(1f, 1f, 1f, 0.04f));
                float textWidth = row.width - 190f * s;
                GUI.Label(new Rect(row.x + 14f * s, row.y + 7f * s, textWidth, 22f * s), $"<b>{item.name}</b>", label);
                GUI.Label(new Rect(row.x + 14f * s, row.y + 29f * s, textWidth, 32f * s), item.info, smallWrap);
                var buy = new Rect(row.xMax - 160f * s, row.y + 12f * s, 146f * s, row.height - 24f * s);
                if (owned)
                {
                    GUI.Label(buy, "<b>OWNED</b>", Tinted(centered, Friendly));
                }
                else
                {
                    GUI.enabled = Tech.CanBuy(item);
                    if (GUI.Button(buy, $"<b>Buy</b>    {item.cost} pts", button)) Tech.Buy(item);
                    GUI.enabled = true;
                }
                y += (rowHeight + gap) * s;
            }

            var back = new Rect(r.x + (r.width - 200f * s) / 2f, r.yMax - 60f * s, 200f * s, 42f * s);
            bool escape = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
            if (GUI.Button(back, "<b>Back</b>  (Esc)", button) || escape)
            {
                if (escape) Event.current.Use();
                ShopOpen = false;
            }
        }

        void DrawBriefing()
        {
            float s = Scale;
            var m = game.Mission;
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
            Rect r = Centered(620f, 340f);
            Fill(r, ModalColor);
            float x = r.x + 28f * s, w = r.width - 56f * s;
            string where = game.Hotspot != null
                ? $"{MapLayout.Plan.name.ToUpperInvariant()}  \u00b7  {MapLayout.Plan.Type.name.ToUpperInvariant()}  \u00b7  {game.Hotspot.weather.ToString().ToUpperInvariant()}"
                : $"MISSION {game.MissionIndex + 1}  \u00b7  {Factions.Region.Replace("the ", "").ToUpperInvariant()}";
            GUI.Label(new Rect(x, r.y + 20f * s, w, 20f * s), where, Tinted(small, TargetColor));
            GUI.Label(new Rect(x, r.y + 40f * s, w, 34f * s), m.name, heading);
            GUI.Label(new Rect(x, r.y + 84f * s, w, 96f * s), m.briefing, wrap);
            int bombers = game.StockLeft(DroneType.Bomber);
            GUI.Label(new Rect(x, r.y + 188f * s, w, 22f * s),
                $"Drones:   Scout x{game.StockLeft(DroneType.Scout)}     Strike x{game.StockLeft(DroneType.Strike)}     " +
                $"Kamikaze x{game.StockLeft(DroneType.Kamikaze)}" + (bombers > 0 ? $"     Nightjar x{bombers}" : ""), label);
            GUI.Label(new Rect(x, r.y + 212f * s, w, 22f * s),
                $"Targets: {game.TargetsTotal}     Three stars: {m.par} drones or fewer, none lost", label);
            var start = new Rect(r.x + (r.width - 220f * s) / 2f, r.yMax - 76f * s, 220f * s, 46f * s);
            bool enter = Event.current.type == EventType.KeyDown &&
                         (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (GUI.Button(start, "<b>Start mission</b>  (Enter)", button) || enter)
            {
                if (enter) Event.current.Use();
                game.CloseBriefing();
            }
        }

        void DrawResults()
        {
            float s = Scale;
            bool won = game.Result == MissionResult.Won;
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
            Rect r = Centered(520f, 350f);
            Fill(r, ModalColor);
            GUI.Label(new Rect(r.x, r.y + 22f * s, r.width, 40f * s), won ? "MISSION COMPLETE" : "MISSION FAILED",
                Tinted(new GUIStyle(heading) { alignment = TextAnchor.MiddleCenter }, won ? Friendly : Hostile));
            GUI.Label(new Rect(r.x, r.y + 66f * s, r.width, 22f * s), game.ResultReason, centered);
            DrawStars(new Rect(r.x + (r.width - 180f * s) / 2f, r.y + 100f * s, 180f * s, 52f * s), game.Stars);
            GUI.Label(new Rect(r.x, r.y + 160f * s, r.width, 22f * s),
                $"Drones used: <b>{game.DronesUsed}</b> (par {game.Mission.par})     Lost: <b>{game.dronesLost}</b>     Home: <b>{game.DronesHome}</b>     Time: {Clock(game.MissionTime)}", centered);
            GUI.Label(new Rect(r.x, r.y + 178f * s, r.width, 18f * s),
                "Three stars: par or fewer drones, none lost.", Tinted(new GUIStyle(small) { alignment = TextAnchor.MiddleCenter }, small.normal.textColor));
            string bonus = game.StarBonus > 0 ? $"  ({game.StarBonus} for new stars)" : "";
            GUI.Label(new Rect(r.x, r.y + 202f * s, r.width, 22f * s),
                $"Points earned: <b>+{game.PointsEarned}</b>{bonus}     Total: <b>{Tech.Points}</b>", targetLabel);

            float bw = 130f * s, gap = 12f * s;
            float x0 = r.x + (r.width - 3f * bw - 2f * gap) / 2f, y = r.yMax - 72f * s;
            if (GUI.Button(new Rect(x0, y, bw, 44f * s), "<b>Retry</b>", button)) game.RetryMission();
            if (game.Hotspot != null)
            {
                if (GUI.Button(new Rect(x0 + bw + gap, y, bw, 44f * s), "<b>Theatre</b>", button))
                {
                    game.OpenMenu();
                    TheatreOpen = true;
                    chosen = null;
                }
            }
            else
            {
                GUI.enabled = won && game.HasNextMission;
                if (GUI.Button(new Rect(x0 + bw + gap, y, bw, 44f * s), "<b>Next mission</b>", button)) game.NextMission();
                GUI.enabled = true;
            }
            if (GUI.Button(new Rect(x0 + 2f * (bw + gap), y, bw, 44f * s), "<b>Menu</b>", button)) game.OpenMenu();
        }

        void DrawLoading()
        {
            float s = Scale;
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
            Rect r = Centered(420f, 90f);
            Fill(r, ModalColor);
            GUI.Label(r, "<b>Flying in...</b>   laying out the sector", centered);
        }

        // The theatre of war: the front on a map, hotspots glowing where the enemy is active. Pick one to see what's
        // there, then fly in.
        void DrawTheatre()
        {
            float s = Scale;
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
            Rect r = Centered(1080f, 760f);
            Fill(r, ModalColor);
            GUI.Label(new Rect(r.x, r.y + 12f * s, r.width, 40f * s), "THEATRE OF WAR", big);
            GUI.Label(new Rect(r.x, r.y + 52f * s, r.width, 22f * s),
                $"The {Theatre.FrontKilometres:0} km Norvane-Dravin front     <b>{Theatre.Wins}</b> sectors won     <b>{Tech.Points}</b> points",
                Tinted(centered, TargetColor));

            var mapRect = new Rect(r.x + 24f * s, r.y + 86f * s, r.width - 48f * s, (r.width - 48f * s) * 560f / 1024f);
            GUI.DrawTexture(mapRect, Theatre.Map());
            GUI.Label(new Rect(mapRect.x + 10f * s, mapRect.yMax - 26f * s, 300f * s, 20f * s), "<b>NORVANE</b>  (you)", Tinted(small, Friendly));
            GUI.Label(new Rect(mapRect.x + 10f * s, mapRect.y + 6f * s, 300f * s, 20f * s), "<b>DRAVIN COALITION</b>", Tinted(small, Hostile));

            Hotspot hovered = null;
            Vector2 mouse = Event.current.mousePosition;
            foreach (var h in Theatre.Hotspots)
            {
                var c = new Vector2(mapRect.x + h.mapPosition.x * mapRect.width, mapRect.yMax - h.mapPosition.y * mapRect.height);
                bool done = Theatre.Done(h);
                float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 3f + h.seed);
                float radius = (10f + 12f * h.heat) * s * (done ? 0.7f : pulse);
                var glow = done ? new Color(0.35f, 1f, 0.6f, 0.75f) : Color.Lerp(new Color(1f, 0.8f, 0.2f, 0.85f), new Color(1f, 0.25f, 0.15f, 0.95f), h.heat);
                Dot(c, radius * 1.8f, new Color(glow.r, glow.g, glow.b, 0.18f));
                Dot(c, radius, glow);
                if ((mouse - c).magnitude < radius + 4f * s) hovered = h;
                if (h == chosen) Brackets(c, radius + 6f * s, Color.white);
            }
            if (hovered != null && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                chosen = hovered;
                Event.current.Use();
            }

            // The card for the picked (or hovered) hotspot.
            var spot = chosen ?? hovered;
            var card = new Rect(r.x + 24f * s, mapRect.yMax + 12f * s, r.width - 48f * s, r.yMax - mapRect.yMax - 24f * s);
            Fill(card, new Color(1f, 1f, 1f, 0.04f));
            if (spot != null)
            {
                var type = SectorTypes.Get(spot.kind);
                float km = spot.mapPosition.x * Theatre.FrontKilometres;
                GUI.Label(new Rect(card.x + 16f * s, card.y + 10f * s, card.width - 300f * s, 30f * s),
                    $"<b>{spot.sectorName}</b>  \u00b7  {type.name}", heading);
                GUI.Label(new Rect(card.x + 16f * s, card.y + 44f * s, card.width - 300f * s, 22f * s),
                    $"<b>{Situations.Name(spot.situation)}</b>     {spot.daylight}, {spot.weather.ToString().ToLowerInvariant()}     km {km:0} of the front     " +
                    $"activity {Mathf.RoundToInt(spot.heat * 100f)}%" + (Theatre.Done(spot) ? "     <b>WON</b>" : ""), label);
                GUI.Label(new Rect(card.x + 16f * s, card.y + 68f * s, card.width - 300f * s, 40f * s), type.blurb, smallWrap);
                if (chosen != null)
                {
                    var fly = new Rect(card.xMax - 250f * s, card.y + 22f * s, 230f * s, 50f * s);
                    bool enter = Event.current.type == EventType.KeyDown &&
                                 (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
                    if (GUI.Button(fly, "<b>Fly in</b>  (Enter)", button) || enter)
                    {
                        if (enter) Event.current.Use();
                        TheatreOpen = false;
                        game.PlayHotspot(chosen);
                    }
                }
            }
            else
            {
                GUI.Label(card, "Hotspots glow where the Dravin are active: the bigger and redder, the hotter. Pick one.", centered);
            }

            var back = new Rect(r.xMax - 150f * s, r.y + 16f * s, 130f * s, 36f * s);
            bool escape = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
            if (GUI.Button(back, "<b>Back</b>  (Esc)", button) || escape)
            {
                if (escape) Event.current.Use();
                TheatreOpen = false;
            }
        }

        // A soft round dot.
        void Dot(Vector2 c, float radius, Color color)
        {
            if (dotTexture == null)
            {
                const int size = 64;
                dotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                        pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 6f));
                    }
                dotTexture.SetPixels(pixels);
                dotTexture.Apply();
            }
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), dotTexture);
            GUI.color = old;
        }

        // Three stars side by side in the rect, the first `filled` of them gold.
        void DrawStars(Rect r, int filled)
        {
            float size = Mathf.Min(r.height, r.width / 3f);
            float gap = (r.width - 3f * size) / 2f;
            Color old = GUI.color;
            for (int i = 0; i < 3; i++)
            {
                GUI.color = i < filled ? TargetColor : new Color(1f, 1f, 1f, 0.18f);
                GUI.DrawTexture(new Rect(r.x + i * (size + gap), r.y + (r.height - size) / 2f, size, size), Star());
            }
            GUI.color = old;
        }

        // A white five-pointed star with soft edges, made once.
        static Texture2D Star()
        {
            if (starTexture != null) return starTexture;
            const int n = 64;
            var corners = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f, radius = i % 2 == 0 ? 0.48f : 0.2f;
                corners[i] = new Vector2(0.5f + Mathf.Cos(a) * radius, 0.5f + Mathf.Sin(a) * radius) * n;
            }
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (InPolygon(new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f), corners)) inside++;
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)(inside * 255 / 16));
                }
            }
            starTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            starTexture.SetPixels32(pixels);
            starTexture.Apply();
            return starTexture;
        }

        static bool InPolygon(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        // ---- Primitives ----

        static string Clock(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

        static string Capitalised(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        static GUIStyle Tinted(GUIStyle style, Color color)
        {
            var tinted = new GUIStyle(style);
            tinted.normal.textColor = color;
            return tinted;
        }

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

        void Brackets(Vector2 c, float half, Color color, float thickness = 0f)
        {
            float t = thickness > 0f ? thickness : Mathf.Max(1f, 2f * Scale);
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

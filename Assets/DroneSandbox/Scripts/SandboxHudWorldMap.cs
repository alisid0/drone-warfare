using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // A north-up atlas of the whole theatre: static geography is painted once, live flight and intel stay separate.
    public partial class SandboxHud
    {
        public bool WorldMapOpen { get; private set; }
        public bool WorldMapBlocksInput => WorldMapOpen || Time.frameCount == worldMapClosedFrame;
        const int WorldMapPixels = 2048;
        const float WorldMapMinimum = 4000f;
        static readonly Color MapWater = new Color(0.22f, 0.48f, 0.59f);
        static readonly Color MapHighway = new Color(0.83f, 0.72f, 0.47f);
        static readonly Color MapRoad = new Color(0.66f, 0.68f, 0.61f);
        static readonly Color MapTrack = new Color(0.39f, 0.43f, 0.36f);
        static readonly string[] MapLegendNames = { "Norvane sites / drone range", "Dravin sites once known / front", "Current 2 km sector", "Highway", "Road / dashed railway", "Track", "Rivers and lakes" };
        static readonly Color[] MapCover =
        {
            new Color(0.29f, 0.36f, 0.29f), new Color(0.39f, 0.41f, 0.28f),
            new Color(0.15f, 0.26f, 0.22f), new Color(0.24f, 0.36f, 0.34f), MapWater,
            new Color(0.49f, 0.47f, 0.41f), new Color(0.44f, 0.43f, 0.36f), new Color(0.47f, 0.49f, 0.47f)
        };
        Texture2D worldMapTexture;
        WorldSite worldMapIdentity;
        readonly HashSet<WorldSite> worldMapKnown = new HashSet<WorldSite>();
        readonly List<Rect> worldMapLabels = new List<Rect>();
        Vector2 worldMapCentre, worldMapPress, worldMapPressCentre, worldMapFleetScroll;
        float worldMapSpan = WorldPlan.Size, worldMapIntelAt;
        bool worldMapPressed, worldMapDragged, worldMapPhotoEnabled;
        int worldMapClosedFrame = -1;
        GUIStyle worldMapText;
        float worldMapStyleScale;
        string worldMapNotice = "Shift-click fleet entries to select several drones.";

        // Leave enough space for a fleet list at any HUD scale; the atlas itself uses square metres per pixel.
        Rect WorldMapRect()
        {
            float s = Scale;
            return new Rect(16f * s, 70f * s, Mathf.Max(100f, Screen.width - 300f * s), Mathf.Max(100f, Screen.height - 158f * s));
        }

        // Fit the entire square at the widest zoom, with harmless margins on the viewport's longer axis.
        float WorldMapMetres(Rect rect) => worldMapSpan / Mathf.Min(rect.width, rect.height);

        // Convert a global x/z position to absolute GUI pixels (north is up, GUI y increases downwards).
        public Vector2 WorldMapPoint(Vector2 world)
        {
            Rect rect = WorldMapRect();
            return rect.center + new Vector2(world.x - worldMapCentre.x, worldMapCentre.y - world.y) / WorldMapMetres(rect);
        }

        // Invert the same transform for the cursor, camera jumps, and orders, without a physics raycast.
        Vector2 WorldMapWorld(Vector2 gui)
        {
            Rect rect = WorldMapRect();
            Vector2 d = (gui - rect.center) * WorldMapMetres(rect);
            return worldMapCentre + new Vector2(d.x, -d.y);
        }

        // Keep the view inside the world; at full extent, centre an axis whose viewport is wider than the land.
        void ClampWorldMap()
        {
            Rect rect = WorldMapRect();
            float metres = WorldMapMetres(rect);
            float x = Mathf.Max(0f, WorldPlan.Half - rect.width * metres / 2f);
            float z = Mathf.Max(0f, WorldPlan.Half - rect.height * metres / 2f);
            worldMapCentre = new Vector2(Mathf.Clamp(worldMapCentre.x, -x, x), Mathf.Clamp(worldMapCentre.y, -z, z));
        }

        // Opening is independent of tactical pause; PhotoMode's separate Update must yield its keyboard too.
        public void OpenWorldMap()
        {
            if (WorldMapOpen || game == null || game.UiBlocking || game.Loading || !FarTerrain.Active || PhotoMode.Active) return;
            UpdateWorldMapIntel();
            WorldMapOpen = true;
            worldMapPressed = worldMapDragged = false;
            if (PhotoMode.Instance != null)
            {
                worldMapPhotoEnabled = PhotoMode.Instance.enabled;
                PhotoMode.Instance.enabled = false;
            }
        }

        // Consume the closing frame so Escape cannot deselect or a released mouse button select behind the atlas.
        public void CloseWorldMap()
        {
            if (!WorldMapOpen) return;
            WorldMapOpen = false;
            worldMapPressed = worldMapDragged = false;
            worldMapClosedFrame = Time.frameCount;
            if (PhotoMode.Instance != null) PhotoMode.Instance.enabled = worldMapPhotoEnabled;
        }

        // Called before RTS controls, including while paused. Only pause and map navigation pass through here.
        public bool HandleWorldMapInput()
        {
            if (WorldMapOpen && (game.UiBlocking || game.Loading || !FarTerrain.Active)) CloseWorldMap();
            if (GameInput.Pressed(GameKey.WorldMap))
            {
                if (WorldMapOpen) CloseWorldMap();
                else OpenWorldMap();
            }
            if (!WorldMapOpen) return WorldMapBlocksInput;
            if (GameInput.Pressed(GameKey.Deselect)) CloseWorldMap();
            else
            {
                if (GameInput.Pressed(GameKey.Pause)) game.TogglePause();
                Vector2 mouse = GameInput.MousePosition;
                WorldMapPointer(new Vector2(mouse.x, Screen.height - mouse.y), GameInput.LeftDown, GameInput.LeftHeld,
                    GameInput.LeftUp, GameInput.RightDown, GameInput.Scroll);
            }
            return true;
        }

        // The real pointer path is also used by AutoTest, so its synthetic click checks the actual coordinate conversion.
        public void WorldMapPointer(Vector2 gui, bool down, bool held, bool up, bool right, float scroll)
        {
            if (!WorldMapOpen || game.UiBlocking || game.Loading || !FarTerrain.Active) return;
            Rect rect = WorldMapRect();
            if (rect.Contains(gui) && scroll != 0f)
            {
                Vector2 anchor = WorldMapWorld(gui);
                worldMapSpan = Mathf.Clamp(worldMapSpan * (scroll > 0f ? 0.8f : 1.25f), WorldMapMinimum, WorldPlan.Size);
                worldMapCentre += anchor - WorldMapWorld(gui);
                ClampWorldMap();
                worldMapPressed = false;
            }
            if (down && rect.Contains(gui))
            {
                worldMapPress = gui;
                worldMapPressCentre = worldMapCentre;
                worldMapPressed = true;
                worldMapDragged = false;
            }
            if (worldMapPressed)
            {
                Vector2 delta = gui - worldMapPress;
                if (delta.magnitude >= 6f * Scale) worldMapDragged = true;
                if (worldMapDragged)
                {
                    worldMapCentre = worldMapPressCentre + new Vector2(-delta.x, delta.y) * WorldMapMetres(rect);
                    ClampWorldMap();
                }
                if (up || !held)
                {
                    worldMapPressed = false;
                    Vector2 world = WorldMapWorld(gui);
                    if (up && !worldMapDragged && rect.Contains(gui) && WorldPlan.Inside(world.x, world.y))
                    {
                        rts.LookAt(new Vector3(world.x - FarTerrain.Origin.x, 0f, world.y - FarTerrain.Origin.y),
                            rts.Distance, rts.Cam.transform.eulerAngles.y);
                        worldMapNotice = "View moved. Close the map to look around.";
                    }
                }
            }
            if (!right || !rect.Contains(gui)) return;
            Vector2 target = WorldMapWorld(gui);
            if (!WorldPlan.Inside(target.x, target.y)) return;
            Vector3 local = new Vector3(target.x - FarTerrain.Origin.x, 0f, target.y - FarTerrain.Origin.y);
            local = Ground.OnGround(local);
            int sent = 0;
            foreach (var drone in rts.selected)
            {
                if (drone == null || !drone.IsAlive) continue;
                drone.MoveTo(local, GameInput.Held(GameKey.Shift), GameInput.Held(GameKey.Ctrl));
                sent++;
            }
            worldMapNotice = sent == 0 ? "Select a drone in the fleet list first." : $"Move order sent to {sent} drone(s).";
            if (sent > 0) Sound.Ui("move");
        }

        // Strategic sites have no EnemyUnit yet: remember observation using Recon's existing sight rules, even off-map.
        // No render/debug visibility flag grants intel. Cache identity also detects regeneration with the same seed.
        public void UpdateWorldMapIntel()
        {
            if (!WorldPlan.Ready || WorldPlan.Sites.Count == 0) return;
            if (!ReferenceEquals(worldMapIdentity, WorldPlan.Sites[0]))
            {
                worldMapIdentity = WorldPlan.Sites[0];
                worldMapKnown.Clear();
                if (worldMapTexture != null) Destroy(worldMapTexture);
                worldMapTexture = null;
                worldMapCentre = Vector2.zero;
                worldMapSpan = WorldPlan.Size;
                worldMapIntelAt = 0f;
            }
            if (game == null || game.UiBlocking || game.Loading || !FarTerrain.Active || game.Paused || Time.time < worldMapIntelAt) return;
            worldMapIntelAt = Time.time + 0.15f;
            float dark = DayNight.Instance != null ? 1f - DayNight.Instance.Daylight : 0f;
            float eyes = (Mathf.Lerp(Recon.IdentifyRange, Recon.NightRange, dark) + (Tech.Active(TechId.Optics) ? Recon.OpticsBonus : 0f)) * Recon.WeatherFactor;
            float thermal = Recon.ThermalRange * Mathf.Lerp(1f, Recon.WeatherFactor, 0.5f);
            Vector2 home = WorldMapPosition(game.basePosition);
            foreach (var site in WorldPlan.Sites)
            {
                if (site.kind != WorldSiteKind.EnemyDepot || worldMapKnown.Contains(site)) continue;
                bool seen = Vector2.Distance(site.position, home) <= Recon.BaseSensorRange;
                foreach (var drone in game.drones)
                {
                    if (seen) break;
                    if (drone == null || !drone.IsAlive) continue;
                    float range = (drone.HasThermal ? Mathf.Max(eyes, thermal) : eyes) * drone.SightFactor;
                    seen = Vector2.Distance(site.position, WorldMapPosition(drone.transform.position)) <= range;
                }
                if (seen) worldMapKnown.Add(site);
            }
        }

        // Positions in gameplay are sector-local; all atlas features and distances use the global metre grid.
        static Vector2 WorldMapPosition(Vector3 local) => new Vector2(local.x, local.z) + FarTerrain.Origin;

        // The out-and-back estimate reserves one fifth, and never mistakes current airspeed for cruise speed.
        static float WorldMapRange(Drone drone) => Mathf.Max(0f, drone.Battery * drone.Endurance * drone.speed * 0.4f);

        // The atlas owns its texture; release it when the HUD is destroyed, without changing world/terrain resources.
        void OnDestroy()
        {
            CloseWorldMap();
            if (worldMapTexture != null) Destroy(worldMapTexture);
        }

        // Paint only geography into the persistent picture. Military symbols are deliberately absent from its pixels.
        void BuildWorldMapTexture()
        {
            var pixels = new Color32[WorldMapPixels * WorldMapPixels];
            float step = WorldPlan.Size / WorldMapPixels;
            for (int y = 0; y < WorldMapPixels; y++)
                for (int x = 0; x < WorldMapPixels; x++)
                {
                    float wx = -WorldPlan.Half + (x + 0.5f) * step, wz = -WorldPlan.Half + (y + 0.5f) * step;
                    Cover cover = WorldPlan.CoverAt(wx, wz);
                    float dx = (WorldPlan.Height(wx + 64f, wz) - WorldPlan.Height(wx - 64f, wz)) / 128f;
                    float dz = (WorldPlan.Height(wx, wz + 64f) - WorldPlan.Height(wx, wz - 64f)) / 128f;
                    float shade = cover == Cover.Water ? 1f : Mathf.Clamp(0.92f + (dz - dx) * 1.8f, 0.55f, 1.35f);
                    Color c = MapCover[(int)cover] * shade;
                    c.a = 1f;
                    pixels[y * WorldMapPixels + x] = c;
                }
            foreach (var river in WorldPlan.Rivers) PaintWorldMapPath(pixels, river.points, MapWater, 1.3f);
            // Minor access routes lie under highways, and the railway gets a dark bed with pale sleepers.
            for (int kind = (int)RoadClass.Track; kind >= (int)RoadClass.Highway; kind--)
                foreach (var road in WorldPlan.Roads)
                    if ((int)road.kind == kind) PaintWorldMapPath(pixels, road.points,
                        road.kind == RoadClass.Highway ? MapHighway : road.kind == RoadClass.Road ? MapRoad : MapTrack,
                        road.kind == RoadClass.Highway ? 1.5f : 0.8f);
            foreach (var rail in WorldPlan.Railways)
            {
                PaintWorldMapPath(pixels, rail.points, Ink, 2f);
                PaintWorldMapPath(pixels, rail.points, MapRoad, 0.8f, true);
            }
            worldMapTexture = new Texture2D(WorldMapPixels, WorldMapPixels, TextureFormat.RGBA32, true)
                { name = "World map atlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            worldMapTexture.SetPixels32(pixels);
            worldMapTexture.Apply(true, true);
        }

        // Rasterise anti-aliased vector paths once, so long road networks cost one texture draw in OnGUI.
        static void PaintWorldMapPath(Color32[] pixels, List<Vector2> points, Color colour, float width, bool dashed = false)
        {
            float along = 0f, scale = WorldMapPixels / WorldPlan.Size;
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = (points[i - 1] + Vector2.one * WorldPlan.Half) * scale;
                Vector2 b = (points[i] + Vector2.one * WorldPlan.Half) * scale;
                float length = Vector2.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length * 2f));
                for (int j = 0; j <= steps; j++)
                {
                    if (dashed && (along + length * j / steps) % 6f > 3f) continue;
                    Vector2 p = Vector2.Lerp(a, b, j / (float)steps);
                    int reach = Mathf.CeilToInt(width);
                    for (int y = Mathf.FloorToInt(p.y) - reach; y <= Mathf.CeilToInt(p.y) + reach; y++)
                        for (int x = Mathf.FloorToInt(p.x) - reach; x <= Mathf.CeilToInt(p.x) + reach; x++)
                        {
                            if (x < 0 || y < 0 || x >= WorldMapPixels || y >= WorldMapPixels) continue;
                            float alpha = Mathf.Clamp01(width / 2f + 0.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), p));
                            int at = y * WorldMapPixels + x;
                            pixels[at] = Color.Lerp(pixels[at], colour, alpha);
                        }
                }
                along += length;
            }
        }

        // Full-screen command console. Static work and map primitives run only on Repaint, buttons on every GUI pass.
        void DrawWorldMap()
        {
            float s = Scale;
            if (worldMapText == null || !Mathf.Approximately(worldMapStyleScale, s))
            {
                worldMapStyleScale = s;
                worldMapText = new GUIStyle(small) { fontSize = Mathf.RoundToInt(11f * s), richText = false };
            }
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                CloseWorldMap();
                Event.current.Use();
                return;
            }
            ClampWorldMap();
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), Ink);
            GUI.Label(new Rect(20f * s, 12f * s, 280f * s, 32f * s), "WORLD MAP", heading);
            GUI.Label(new Rect(20f * s, 43f * s, Screen.width - 220f * s, 22f * s),
                "NORVANE / DRAVIN   ·   56 × 56 km   ·   " + (game.Paused ? "TACTICAL PAUSE" : $"LIVE  {game.GameSpeed}x"), small);
            if (GUI.Button(new Rect(Screen.width - 168f * s, 18f * s, 148f * s, 34f * s),
                "Close  " + GameInput.BindingLabel(GameKey.WorldMap) + " / Esc", button)) CloseWorldMap();
            Rect rect = WorldMapRect();
            if (Event.current.type == EventType.Repaint)
            {
                if (worldMapTexture == null) BuildWorldMapTexture();
            }
            DrawWorldMapLand(rect);
            DrawWorldMapFleet(new Rect(rect.xMax + 16f * s, rect.y, Screen.width - rect.xMax - 32f * s, rect.height));
            Vector2 cursor = WorldMapWorld(new Vector2(GameInput.MousePosition.x, Screen.height - GameInput.MousePosition.y));
            string readout = rect.Contains(new Vector2(GameInput.MousePosition.x, Screen.height - GameInput.MousePosition.y)) && WorldPlan.Inside(cursor.x, cursor.y)
                ? $"WORLD  E {cursor.x / 1000f:+0.00;-0.00;0.00} km   N {cursor.y / 1000f:+0.00;-0.00;0.00} km     |     {Vector2.Distance(cursor, WorldMapPosition(game.basePosition)) / 1000f:0.00} km from launch base"
                : "World coordinates in kilometres   ·   north is up";
            GUI.Label(new Rect(rect.x, rect.yMax + 10f * s, Screen.width - 32f * s, 22f * s), readout, label);
            GUI.Label(new Rect(rect.x, rect.yMax + 34f * s, Screen.width - 32f * s, 22f * s),
                "Wheel: zoom   ·   Drag: pan   ·   Left-click: move view   ·   Right-click: move selected drones   ·   " +
                GameInput.BindingLabel(GameKey.Pause) + ": pause", small);
        }

        // Clip all atlas marks to its viewport; geographic proportions remain exact at every aspect ratio and zoom.
        void DrawWorldMapLand(Rect rect)
        {
            float s = Scale;
            GUI.BeginGroup(rect);
            if (Event.current.type != EventType.Repaint) { GUI.EndGroup(); return; }
            Fill(new Rect(0f, 0f, rect.width, rect.height), new Color(0.025f, 0.045f, 0.055f));
            Vector2 topLeft = WorldMapPoint(new Vector2(-WorldPlan.Half, WorldPlan.Half)) - rect.position;
            float side = WorldPlan.Size / WorldMapMetres(rect);
            GUI.DrawTexture(new Rect(topLeft.x, topLeft.y, side, side), worldMapTexture);
            worldMapLabels.Clear();
            for (int i = 1; i < WorldPlan.FrontLine.Count; i++)
            {
                Vector2 a = WorldMapPoint(WorldPlan.FrontLine[i - 1]) - rect.position, b = WorldMapPoint(WorldPlan.FrontLine[i]) - rect.position;
                Dashes(a + Vector2.left * 2f * s, b + Vector2.left * 2f * s, Teal, 1.5f * s, 7f * s, 5f * s, 0f);
                Dashes(a + Vector2.right * 2f * s, b + Vector2.right * 2f * s, DravinOrange, 1.5f * s, 7f * s, 5f * s, 0f);
            }
            Vector2 sector = WorldMapPoint(FarTerrain.Origin) - rect.position;
            float half = SandboxGame.MapHalfSize / WorldMapMetres(rect);
            Outline(new Rect(sector.x - half, sector.y - half, half * 2f, half * 2f), TargetColor, 1.5f * s);
            foreach (var drone in rts.selected)
            {
                if (drone == null || !drone.IsAlive) continue;
                Vector2 at = WorldMapPoint(WorldMapPosition(drone.transform.position)) - rect.position;
                float radius = WorldMapRange(drone) / WorldMapMetres(rect);
                for (int i = 0; i < 128; i++)
                {
                    float a = i * Mathf.PI / 64f, b = (i + 1) * Mathf.PI / 64f;
                    Dashes(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                        at + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, new Color(Teal.r, Teal.g, Teal.b, 0.65f), s, 0f, 0f, 0f);
                }
                if (drone.MoveTarget.HasValue)
                    Dashes(at, WorldMapPoint(WorldMapPosition(drone.MoveTarget.Value)) - rect.position, Teal, s, 6f * s, 5f * s, 0f);
            }
            foreach (var drone in game.drones)
            {
                if (drone == null || !drone.IsAlive) continue;
                Vector2 at = WorldMapPoint(WorldMapPosition(drone.transform.position)) - rect.position;
                bool selected = rts.selected.Contains(drone);
                Stamp(DroneIcon(drone.type), at, 10f * s, selected ? Color.white : Teal);
                if (selected)
                {
                    Outline(new Rect(at.x - 13f * s, at.y - 13f * s, 26f * s, 26f * s), Teal, s);
                    WorldMapLabel(rect, at, $"{drone.unitName}  {drone.Battery * 100f:0}%", Teal);
                }
            }
            Vector2 home = WorldMapPoint(WorldMapPosition(game.basePosition)) - rect.position;
            Outline(new Rect(home.x - 5f * s, home.y - 5f * s, 10f * s, 10f * s), Teal, 2f * s);
            WorldMapLabel(rect, home, "Launch base", Teal);
            WorldMapLabel(rect, sector + new Vector2(-half, half), "Current sector", TargetColor);
            foreach (var site in WorldPlan.Sites)
            {
                if (site.kind == WorldSiteKind.EnemyDepot && !worldMapKnown.Contains(site)) continue;
                Vector2 at = WorldMapPoint(site.position) - rect.position;
                Color colour = site.kind == WorldSiteKind.Landmark ? MapRoad : site.norvane ? Teal : DravinOrange;
                if (site.kind == WorldSiteKind.Landmark)
                {
                    Dashes(at + new Vector2(-5f, 4f) * s, at + new Vector2(0f, -5f) * s, colour, s, 0f, 0f, 0f);
                    Dashes(at + new Vector2(0f, -5f) * s, at + new Vector2(5f, 4f) * s, colour, s, 0f, 0f, 0f);
                }
                else if (!site.norvane) Diamond(at, 6f * s, colour);
                else
                {
                    Outline(new Rect(at.x - 5f * s, at.y - 5f * s, 10f * s, 10f * s), colour, s);
                    Dashes(at - Vector2.up * 7f * s, at + Vector2.up * 7f * s, colour, 2f * s, 0f, 0f, 0f);
                }
                WorldMapLabel(rect, at, site.name, colour);
            }
            foreach (var river in WorldPlan.Rivers) DrawWorldMapRiverName(rect, river);
            foreach (var lake in WorldPlan.Lakes)
                WorldMapLabel(rect, WorldMapPoint(lake.outlet) - rect.position, lake.name, new Color(0.52f, 0.78f, 0.86f));
            foreach (var place in WorldPlan.Settlements)
            {
                Vector2 at = WorldMapPoint(place.center) - rect.position;
                float dot = (place.town ? 4f : 2f) * s;
                Fill(new Rect(at.x - dot / 2f, at.y - dot / 2f, dot, dot), MapRoad);
                WorldMapLabel(rect, at, place.name, place.town ? Color.white : MapRoad);
            }
            // Scale bar is in actual metres; its tick lengths never depend on the screen's aspect ratio.
            float km = worldMapSpan >= 20000f ? 5f : worldMapSpan >= 8000f ? 2f : 1f;
            float bar = km * 1000f / WorldMapMetres(rect);
            Fill(new Rect(16f * s, rect.height - 28f * s, bar, 2f * s), Color.white);
            Fill(new Rect(16f * s, rect.height - 32f * s, s, 10f * s), Color.white);
            Fill(new Rect(16f * s + bar, rect.height - 32f * s, s, 10f * s), Color.white);
            GUI.Label(new Rect(16f * s, rect.height - 54f * s, 100f * s, 22f * s), $"{km:0} km", small);
            Vector2 north = new Vector2(rect.width - 30f * s, 40f * s);
            Dashes(north + Vector2.up * 18f * s, north - Vector2.up * 10f * s, Color.white, 2f * s, 0f, 0f, 0f);
            Dashes(north - Vector2.up * 10f * s, north + new Vector2(-5f, -2f) * s, Color.white, 2f * s, 0f, 0f, 0f);
            Dashes(north - Vector2.up * 10f * s, north + new Vector2(5f, -2f) * s, Color.white, 2f * s, 0f, 0f, 0f);
            GUI.Label(new Rect(north.x - 7f * s, 8f * s, 20f * s, 22f * s), "N", label);
            GUI.EndGroup();
            Outline(rect, new Color(0.2f, 0.3f, 0.34f), s);
        }

        // Name the visible reach, rather than losing the river's label when its halfway point is outside a close view.
        void DrawWorldMapRiverName(Rect rect, WorldRiver river)
        {
            int closest = -1;
            float distance = float.MaxValue;
            for (int i = 0; i < river.points.Count; i++)
            {
                float d = (river.points[i] - worldMapCentre).sqrMagnitude;
                if (d < distance) { distance = d; closest = i; }
            }
            if (closest < 0) return;
            for (int i = 0; i < 9; i++)
            {
                int offset = (i + 1) / 2 * Mathf.Max(1, river.points.Count / 16) * (i % 2 == 0 ? 1 : -1);
                int at = Mathf.Clamp(closest + offset, 0, river.points.Count - 1);
                if (WorldMapLabel(rect, WorldMapPoint(river.points[at]) - rect.position, river.name, new Color(0.52f, 0.78f, 0.86f))) break;
            }
        }

        // Try both sides of a symbol and suppress overlapping labels, while keeping the symbol itself visible.
        bool WorldMapLabel(Rect viewport, Vector2 at, string text, Color colour)
        {
            if (at.x < 0f || at.y < 0f || at.x > viewport.width || at.y > viewport.height) return false;
            float s = Scale;
            worldMapText.normal.textColor = colour;
            Vector2 size = worldMapText.CalcSize(new GUIContent(text));
            for (int side = 0; side < 2; side++)
            {
                var rect = new Rect(side == 0 ? at.x + 10f * s : at.x - size.x - 14f * s, at.y - 9f * s, size.x + 4f * s, 19f * s);
                if (rect.x < 0f || rect.y < 0f || rect.xMax > viewport.width || rect.yMax > viewport.height) continue;
                bool overlap = false;
                foreach (var previous in worldMapLabels) if (rect.Overlaps(previous)) { overlap = true; break; }
                if (overlap) continue;
                Fill(rect, new Color(Ink.r, Ink.g, Ink.b, 0.78f));
                GUI.Label(rect, text, worldMapText);
                worldMapLabels.Add(rect);
                return true;
            }
            return false;
        }

        // Selection stays available on the atlas, with battery/range guidance and a compact geographic legend.
        void DrawWorldMapFleet(Rect rect)
        {
            float s = Scale;
            bool compact = rect.height < 420f * s;
            GUI.Label(new Rect(rect.x, rect.y, rect.width, 26f * s), "FLIGHT PLANNING", title);
            GUI.Label(new Rect(rect.x, rect.y + 34f * s, rect.width, (compact ? 34f : 65f) * s),
                compact ? "Out-and-back rings, 20% reserve. Swift can make long trips."
                    : "Rings show remaining out-and-back range from each selected drone, with 20% in reserve. Swift is built for long trips.", smallWrap);
            GUI.Label(new Rect(rect.x, rect.y + (compact ? 72f : 104f) * s, rect.width, (compact ? 34f : 44f) * s), worldMapNotice, smallWrap);
            float listTop = compact ? 110f : 154f;
            var list = new Rect(rect.x, rect.y + listTop * s, rect.width, Mathf.Max(50f * s, rect.height - (listTop + 204f) * s));
            worldMapFleetScroll = GUI.BeginScrollView(list, worldMapFleetScroll,
                new Rect(0f, 0f, list.width - 20f * s, Mathf.Max(list.height, game.drones.Count * 55f * s)));
            int row = 0;
            foreach (var drone in game.drones)
            {
                if (drone == null || !drone.IsAlive) continue;
                bool selected = rts.selected.Contains(drone);
                if (GUI.Button(new Rect(0f, row++ * 55f * s, list.width - 22f * s, 49f * s),
                    $"{drone.unitName}   {drone.Battery * 100f:0}%\n{WorldMapRange(drone) / 1000f:0.0} km out-and-back radius", selected ? chosenButton : button))
                {
                    if (!GameInput.Held(GameKey.Shift)) rts.selected.Clear();
                    if (!rts.selected.Contains(drone)) rts.selected.Add(drone);
                    else rts.selected.Remove(drone);
                }
            }
            GUI.EndScrollView();
            float y = list.yMax + 14f * s;
            GUI.Label(new Rect(rect.x, y, rect.width, 23f * s), "MAP KEY", title);
            for (int i = 0; i < MapLegendNames.Length; i++)
            {
                Color colour = i == 0 ? Teal : i == 1 ? DravinOrange : i == 2 ? TargetColor : i == 3 ? MapHighway : i == 4 ? MapRoad : i == 5 ? MapTrack : MapWater;
                Fill(new Rect(rect.x, y + (32f + 20f * i) * s, 16f * s, 2f * s), colour);
                GUI.Label(new Rect(rect.x + 24f * s, y + (23f + 20f * i) * s, rect.width - 24f * s, 22f * s), MapLegendNames[i], small);
            }
        }
    }
}

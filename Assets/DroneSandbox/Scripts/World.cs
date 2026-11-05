using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Builds everything on the map that isn't a unit: terrain, river, fields, villages, woods, bridges, the
    // sector's special features and both bases, all from MapLayout. Used by the game (SandboxGame) and by the
    // editor preview (MapPreview).
    public static class World
    {
        const int TreeLimit = 11500; // shared by woods, lake, gardens, field edges and orchards
        public static int TreeCount { get; private set; }

        // Builds one sector and resets the shared scenery budget before any builder plants trees.
        // How long each part of the last Build took, for the test log (a 2 km sector has to load quickly).
        public static string Timings { get; private set; } = "";

        public static Transform Build(Transform parent, bool preview)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var times = new System.Text.StringBuilder();
            void Lap(string what) { times.Append($"{what} {clock.ElapsedMilliseconds}, "); clock.Restart(); }
            MapLayout.Prepare();
            Lap("layout");
            TreeCount = 0;
            Undergrowth.Reset(); // every tree builder shares its spacing grid
            var root = new GameObject("World").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(MapLayout.Plan.worldSeed); // same map every time for a sector

            Ground.Build(root);
            Lap("ground");
            Backdrop.Build(root);
            Water.Build(root);
            Reservoir.Build(root);
            Lap("water");
            Farmland.Build(root, rng);
            Lap("fields");
            var scenery = new GameObject("Scenery").transform;
            scenery.SetParent(root, false);
            foreach (var bridge in MapLayout.Bridges) BuildBridge(scenery, bridge);
            BuildVillages(scenery, rng);
            Lap("villages");
            Grass.Plant(Ground.Current); // after the villages, so it keeps off their worn paths and yards
            Lap("grass");
            Features.Build(scenery); // reserve orchard trees before filling the wild woods
            BuildWoods(scenery, rng);
            Lap("woods");
            BuildRocks(scenery, rng);
            Undergrowth.Build(scenery);
            Lap("undergrowth");
            Bases.BuildPlayerBase(root);
            Bases.BuildEnemyHq(root);
            if (preview) Bases.PreviewUnits(root);
            Lap("bases");
            Timings = $"{times}{TreeCount} trees; layout: {MapLayout.Timings}{MapLayout.FieldTimings}";
            return root;
        }

        // Places Resources/Models/<model> on the ground (sunk a little so it doesn't float on slopes),
        // or builds a primitive stand-in when the model is missing. Trees return null when crowded or over budget.
        public static GameObject Place(string model, Vector3 pos, float yaw, float scale, float sink, Transform parent,
            System.Action<Transform> fallback = null)
        {
            bool tree = model == "Scenery_Tree" || model == "Scenery_Tree_B" || model == "Scenery_Birch"
                || model == "Scenery_Poplar" || model == "Scenery_Spruce" || model == "Scenery_Pine" || model == "Scenery_Pine_B";
            var p = new Vector2(pos.x, pos.z);
            if (tree && (TreeCount >= TreeLimit || Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge
                || Undergrowth.UnderTrees(p, 3.2f))) return null;
            var root = new GameObject(model).transform;
            root.SetParent(parent, false);
            float r = 1.2f * scale;
            float ground = Mathf.Min(Ground.HeightAt(pos), Ground.HeightAt(pos + new Vector3(r, 0f, r)),
                Ground.HeightAt(pos - new Vector3(r, 0f, r)), Ground.HeightAt(pos + new Vector3(r, 0f, -r)),
                Ground.HeightAt(pos + new Vector3(-r, 0f, r)));
            root.position = new Vector3(pos.x, ground - sink, pos.z);
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Art.TryModel(model, root, scale) == null) (fallback ?? (t => StandIn(t, scale)))(root);
            if (tree)
            {
                TreeCount++;
                Undergrowth.NoteTree(p);
            }
            return root.gameObject;
        }

        static void StandIn(Transform root, float scale)
        {
            Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.5f * scale, 0f), Vector3.one * scale, new Color(0.4f, 0.4f, 0.38f));
        }

        // ---- Bridge ----

        static void BuildBridge(Transform parent, Vector2 p)
        {
            MapLayout.RoadField.Query(p.x, p.y, out _, out int segment);
            Vector2 dir = MapLayout.RoadField.Direction(segment);
            // The deck sits level with the road where it meets the banks, and spans the dip between them.
            var a = new Vector3(p.x - dir.x * 11f, 0f, p.y - dir.y * 11f);
            var b = new Vector3(p.x + dir.x * 11f, 0f, p.y + dir.y * 11f);
            float deck = Mathf.Max(Ground.HeightAt(a), Ground.HeightAt(b)) + 0.05f;
            float span = 4f;
            for (float s = 2f; s < 20f; s += 0.5f)
            {
                bool low = false;
                foreach (float sign in new[] { -1f, 1f })
                {
                    var q = new Vector3(p.x + dir.x * s * sign, 0f, p.y + dir.y * s * sign);
                    if (Ground.HeightAt(q) < deck - 0.35f) low = true;
                }
                if (low) span = s;
            }
            var bridge = new GameObject("Scenery_Bridge").transform;
            bridge.SetParent(parent, false);
            bridge.position = new Vector3(p.x, deck - 0.3f, p.y);
            bridge.rotation = Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 0f);
            var model = Art.TryModel("Scenery_Bridge", bridge);
            if (model != null) model.transform.localScale = new Vector3(1f, 1f, Mathf.Clamp((span * 2f + 3f) / 18f, 0.9f, 1.8f));
        }

        // ---- Villages ----

        static void BuildVillages(Transform parent, System.Random rng)
        {
            var plan = MapLayout.Plan;
            for (int v = 0; v < plan.villages.Count; v++)
            {
                var root = new GameObject("Village " + (v + 1)).transform;
                root.SetParent(parent, false);
                Vector2 center = plan.villages[v];
                bool farmstead = plan.farmsteads[v];
                MapLayout.RoadField.Query(center.x, center.y, out _, out int segment);
                Vector2 along = MapLayout.RoadField.Direction(segment);
                Vector2 across = new Vector2(-along.y, along.x);
                float[] slots = farmstead ? new[] { -6f, 7f } : new[] { -19f, -6.5f, 6.5f, 19f };
                foreach (float side in new[] { -1f, 1f })
                {
                    foreach (float s in slots)
                    {
                        if (farmstead && side > 0f && s > 0f) continue;
                        bool church = v == 0 && !farmstead && side < 0f && Mathf.Abs(s - 6.5f) < 0.1f;
                        // Plots aren't a ruled row: each house sits a little along its plot and 4 to 9 m back from
                        // the road, and now and then a plot is a paddock with no house on it.
                        float shift = church ? 0f : Range(rng, -2.5f, 2.5f), setback = church ? 8f : Range(rng, 6.5f, 11f);
                        if (!church && !farmstead && rng.NextDouble() < 0.12) continue;
                        Vector2 p = center + along * (s + shift) + across * side * (MapLayout.RoadHalfWidth + setback);
                        if (!Buildable(p)) continue;
                        string model;
                        if (church) model = "Scenery_Church";
                        else if (farmstead) model = s < 0f ? "Scenery_Barn" : "Scenery_House_A";
                        else
                        {
                            double roll = rng.NextDouble();
                            model = roll < 0.5 ? "Scenery_House_A" : roll < 0.82 ? "Scenery_House_B" : "Scenery_Barn";
                        }
                        // Doors (the model's +Z) face the road. Houses differ a little in size (and so in height).
                        Vector2 facing = -across * side;
                        float yaw = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg + Range(rng, -6f, 6f);
                        var house = Place(model, new Vector3(p.x, 0f, p.y), yaw, church ? 1f : Range(rng, 0.88f, 1.12f), 0.35f, root);
                        if (model != "Scenery_Church") Repaint(house, v, p);
                        // A path worn from the door to the road at most houses; a farmyard of bare earth round a
                        // farmstead's buildings.
                        Vector2 toRoad = p + facing * (setback + 0.4f);
                        if (farmstead) Ground.Wear(p, toRoad, Range(rng, 7f, 10f), 0.6f);
                        else if (church || rng.NextDouble() < 0.7) Ground.Wear(p + facing * 3f, toRoad, church ? 3f : Range(rng, 1.4f, 2.4f), 0.75f);
                        Garden(root, p, along, across * side, rng);
                        if (farmstead && s < 0f && rng.NextDouble() < 0.6)
                        {
                            // A grain silo beside the barn.
                            Vector2 silo = p + along * Range(rng, -9f, -7f) + across * side * Range(rng, 2f, 5f);
                            if (Buildable(silo)) Place("Scenery_Silo", new Vector3(silo.x, 0f, silo.y), Range(rng, 0f, 360f), Range(rng, 0.8f, 1f), 0.1f, root);
                        }
                    }
                }
            }
        }

        static readonly string[] Roofs = { "RoofTile", "RoofTileDark", "RoofTileWarm", "RoofSlate" };
        static readonly string[] Walls = { "PlasterWhite", "PlasterCream", "PlasterOchre", "PlasterPink", "PlasterGrey" };

        // Each village leans to its own roofs and walls, and each house varies on that, so a street isn't the same
        // house over and over. Picked by the village and the house's position (not the map's random numbers, which
        // would move everything built after).
        static void Repaint(GameObject house, int village, Vector2 at)
        {
            int h = ((Mathf.FloorToInt(at.x * 7.3f) * 73856093) ^ (Mathf.FloorToInt(at.y * 7.3f) * 19349663)) & 0x7fffffff;
            int roof = (village + (h % 3 == 0 ? h / 7 : 0)) % Roofs.Length;
            int wall = (village * 2 + (h % 2 == 0 ? h / 11 : 0)) % Walls.Length;
            Art.Swap(house, "RoofTile", Roofs[roof]);
            Art.Swap(house, "PlasterWhite", Walls[wall]);
        }

        // A fence behind the house, a fruit tree or two, in about a third of gardens a shed (with a log pile by
        // it in some), and in a third a vegetable patch of dug earth.
        static void Garden(Transform root, Vector2 house, Vector2 along, Vector2 outward, System.Random rng)
        {
            if (rng.NextDouble() < 0.35)
            {
                Vector2 patch = house + outward * Range(rng, 5.5f, 7f) + along * Range(rng, -3f, 3f);
                if (Buildable(patch)) Ground.Wear(patch - along * Range(rng, 2f, 3.5f), patch + along * Range(rng, 2f, 3.5f), Range(rng, 2.5f, 3.5f), 0.65f);
            }
            if (rng.NextDouble() < 0.35)
            {
                Vector2 shed = house + outward * Range(rng, 5.5f, 6.8f) + along * ((rng.NextDouble() < 0.5 ? -1f : 1f) * Range(rng, 4f, 6f));
                float shedYaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg + 180f + Range(rng, -10f, 10f);
                if (Buildable(shed))
                {
                    Place("Scenery_Hut", new Vector3(shed.x, 0f, shed.y), shedYaw, Range(rng, 0.7f, 0.95f), 0.1f, root);
                    Vector2 logs = shed + along * 3f;
                    if (rng.NextDouble() < 0.45 && Buildable(logs))
                        Place("Scenery_LogPile", new Vector3(logs.x, 0f, logs.y), shedYaw + 90f, 0.8f, 0.05f, root);
                }
            }
            Vector2 back = house + outward * 8f;
            float yaw = Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg + 90f;
            for (int k = -2; k <= 2; k++)
            {
                Vector2 p = back + along * (k * 3f);
                if (rng.NextDouble() < 0.15 || !Buildable(p)) continue;
                Place("Scenery_Fence", new Vector3(p.x, 0f, p.y), yaw, 1f, 0.1f, root);
            }
            int trees = rng.Next(0, 3);
            for (int k = 0; k < trees; k++)
            {
                Vector2 p = house + outward * Range(rng, 4.5f, 6.5f) + along * Range(rng, -6f, 6f);
                if (!Buildable(p)) continue;
                Place(rng.NextDouble() < 0.7 ? "Scenery_Tree" : "Scenery_Birch", new Vector3(p.x, 0f, p.y),
                    Range(rng, 0f, 360f), Range(rng, 0.5f, 0.7f), 0.1f, root);
            }
        }

        // Keeps village props inside the terrain and off roads, water, fields and levelled sites.
        static bool Buildable(Vector2 p)
        {
            if (Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge) return false;
            if (MapLayout.RoadField.Distance(p.x, p.y) < MapLayout.RoadHalfWidth + 2.5f) return false;
            if (MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverHalfWidth + 5f) return false;
            return !MapLayout.NearFlatSite(p, 4f) && !MapLayout.InField(p, 1f);
        }

        // ---- Woods ----

        // Plants larger ragged woods at real spacing, bounded by both a local and an all-builder tree allowance.
        static void BuildWoods(Transform parent, System.Random rng)
        {
            var root = new GameObject("Woods").transform;
            root.SetParent(parent, false);

            // Fewer, larger blocks with real tree spacing. Collect candidates first, then sample across all woods
            // when the budget fills, so neither the last wood nor the last row gets chopped off.
            var candidates = new List<(Vector2 p, bool outer, float age)>();
            var plan = MapLayout.Plan;
            int innerWant = Mathf.Min(10, Mathf.RoundToInt(6f * plan.forest / 0.3f));
            int outerWant = Mathf.Min(22, Mathf.RoundToInt(16f * plan.forest / 0.3f));
            int inner = 0, outer = 0;
            int attempts = 600 + 20 * (innerWant + outerWant);
            float limit = MapLayout.Edge - 10f, softEdge = Scale.D(7f);
            for (int attempt = 0; attempt < attempts && (inner < innerWant || outer < outerWant); attempt++)
            {
                var c = new Vector2(Range(rng, -limit, limit), Range(rng, -limit, limit));
                bool isOuter = Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.y)) > Mathf.Min(Scale.D(105f), MapLayout.Edge * 0.48f);
                if (isOuter ? outer >= outerWant : inner >= innerWant) continue;
                if (!MapLayout.IsWild(c, 3f)) continue;
                if (isOuter) outer++; else inner++;
                float radius = Mathf.Min(Scale.D(isOuter ? Range(rng, 16f, 30f) : Range(rng, 8f, 17f)), Ground.Size * 0.075f);
                float spacing = isOuter ? 5.6f : 4.6f;
                int seed = rng.Next();
                // Each wood is stretched along its own axis and lopsided.
                float axis = (seed % 360) * Mathf.Deg2Rad, stretch = 1f + (seed / 360 % 100) / 100f * 1.4f;
                float cos = Mathf.Cos(axis), sin = Mathf.Sin(axis), reach = (radius * 1.1f + softEdge) * Mathf.Sqrt(stretch);
                for (float x = -reach; x <= reach; x += spacing)
                {
                    for (float z = -reach; z <= reach; z += spacing)
                    {
                        Vector2 p = c + new Vector2(x, z) + new Vector2(Range(rng, -1.6f, 1.6f), Range(rng, -1.6f, 1.6f));
                        float du = (x * cos + z * sin) / Mathf.Sqrt(stretch), dv = (z * cos - x * sin) * Mathf.Sqrt(stretch);
                        float angle = Mathf.Atan2(dv, du), r = Mathf.Sqrt(du * du + dv * dv);
                        float edge = radius * (0.7f + 0.22f * Mathf.PerlinNoise(seed % 1000 + angle * 1.6f, 0.5f)
                                                    + 0.18f * Mathf.PerlinNoise(seed % 997 + angle * 4.3f, 3.7f));
                        // A broad, ragged margin and glades inside; the spacing of individual trees stays real.
                        if (r > edge && Mathf.PerlinNoise(p.x * 0.31f + seed % 113, p.y * 0.31f) > 0.62f - 0.5f * (r - edge) / softEdge) continue;
                        if (r > edge + softEdge || Mathf.PerlinNoise(p.x * 0.07f / Scale.Distance + seed % 211, p.y * 0.07f / Scale.Distance + 5f) < 0.28f) continue;
                        if (!MapLayout.IsWild(p, 0.5f) || InClearing(p)) continue;
                        // Trees of an age together (a stand planted or seeded at once), 0.7 to 1.3 times the usual size
                        // over 15-25 m, and here and there a young one half the size.
                        float age = 0.7f + 0.6f * Mathf.PerlinNoise(p.x * 0.05f + seed % 89, p.y * 0.05f + 31f);
                        if (Mathf.PerlinNoise(p.x * 0.27f + 12f, p.y * 0.27f + seed % 53) > 0.74f) age *= 0.55f;
                        candidates.Add((p, isOuter, age));
                    }
                }
            }

            int startCount = TreeCount;
            for (int i = candidates.Count - 1; i >= 0 && TreeCount - startCount < 8000 && TreeCount < TreeLimit; i--)
            {
                int pick = rng.Next(i + 1);
                var tree = candidates[pick];
                candidates[pick] = candidates[i];
                Place(PickTree(rng, tree.outer, tree.p), new Vector3(tree.p.x, 0f, tree.p.y), Range(rng, 0f, 360f),
                    tree.age * Range(rng, 0.88f, 1.12f), 0.25f, root);
            }

            // Sample by metres, not vertex count, so changing the river curve's tessellation cannot change
            // the real spacing or pack all the bank trees into one end of the larger map.
            var river = MapLayout.RiverField.points;
            float walked = 0f, next = 0f;
            for (int i = 1; i < river.Count; i++)
            {
                float length = Vector2.Distance(river[i - 1], river[i]);
                if (length < 0.001f) continue;
                Vector2 dir = (river[i] - river[i - 1]) / length, side = new Vector2(-dir.y, dir.x);
                for (; next <= walked + length; next += 4f)
                {
                    Vector2 bank = Vector2.Lerp(river[i - 1], river[i], (next - walked) / length);
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        if (rng.NextDouble() > 0.42) continue;
                        Vector2 p = bank + side * sign * (MapLayout.RiverHalfWidth + Range(rng, 3.2f, 9f));
                        if (Vector2.Distance(p, MapLayout.BridgePoint) < 14f || !MapLayout.IsWild(p, -2f)) continue;
                        double roll = rng.NextDouble();
                        string model = roll < 0.45 ? "Scenery_Tree" : roll < 0.65 ? "Scenery_Tree_B" : roll < 0.8 ? "Scenery_Birch" : "Scenery_Bush";
                        Place(model, new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), Range(rng, 0.8f, 1.15f), 0.25f, root);
                    }
                }
                walked += length;
            }
        }

        // Logging clearings (Features fills them with stumps and log stacks) stay open.
        static bool InClearing(Vector2 p)
        {
            foreach (var c in MapLayout.Plan.clearings)
                if (Vector2.Distance(p, c) < Scale.D(24f)) return true;
            return false;
        }

        // A tree for a wood at p: the Varrow Valley's own mix, or, elsewhere, the sector's shares of conifers and
        // birches (more conifers on the outer hills). Woods seen from above grow in stands of one kind 15 to 35 m
        // across (a clump of birch, a block of spruce), so three trees in four take the kind of the stand they're
        // in, and the rest are strays from the whole mix.
        static string PickTree(System.Random rng, bool outer, Vector2 p)
        {
            var plan = MapLayout.Plan;
            float stand = Mathf.Clamp01((Mathf.PerlinNoise(p.x * 0.04f + 300f, p.y * 0.04f + 150f) - 0.22f) / 0.56f);
            double r = rng.NextDouble();
            r = r < 0.75 ? stand : (r - 0.75) / 0.25;
            if (plan.kind != SectorKind.VarrowValley)
            {
                float roll = (float)r, conifer = outer ? Mathf.Min(1f, plan.conifer + 0.35f) : plan.conifer;
                if (roll < conifer * 0.25f) return "Scenery_Pine";
                if (roll < conifer * 0.4f) return "Scenery_Pine_B";
                if (roll < conifer) return "Scenery_Spruce";
                if (roll < conifer + plan.birch * (1f - conifer)) return "Scenery_Birch";
                float rest = (float)rng.NextDouble();
                return rest < 0.55f ? "Scenery_Tree" : rest < 0.85f ? "Scenery_Tree_B" : "Scenery_Poplar";
            }
            if (outer)
                return r < 0.4 ? "Scenery_Spruce" : r < 0.6 ? "Scenery_Pine" : r < 0.75 ? "Scenery_Pine_B" : r < 0.9 ? "Scenery_Tree" : "Scenery_Tree_B";
            return r < 0.35 ? "Scenery_Tree" : r < 0.6 ? "Scenery_Tree_B" : r < 0.72 ? "Scenery_Birch" : r < 0.86 ? "Scenery_Pine" : "Scenery_Spruce";
        }

        // ---- Rocks ----

        // Boulders out on the open ground and against the riverbanks by the bridge: photo-scanned ones (see Scans)
        // when they've been imported, else the procedural rocks.
        static void BuildRocks(Transform parent, System.Random rng)
        {
            var root = new GameObject("Rocks").transform;
            root.SetParent(parent, false);
            int placed = 0;
            for (int attempt = 0; attempt < 1200 && placed < 136; attempt++)
            {
                var p = new Vector2(Range(rng, -MapLayout.Edge, MapLayout.Edge), Range(rng, -MapLayout.Edge, MapLayout.Edge));
                if (!MapLayout.IsWild(p, 1f)) continue;
                float scale = Range(rng, 0.6f, 1.8f);
                double roll = rng.NextDouble();
                Scans.Rock(Scans.Boulders, roll, new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), 0.55f + 0.3f * scale, root,
                    roll < 0.5 ? "Scenery_Rock" : "Scenery_Rock_B", scale, 0.3f * scale);
                placed++;
            }
            // Boulders piled against the riverbanks by the bridge.
            Vector2 b = MapLayout.BridgePoint;
            var variety = new System.Random(13); // picks the scans without changing rng's sequence
            for (int k = 0; k < 8; k++)
            {
                var p = b + new Vector2(Range(rng, -11f, 11f), Range(rng, -11f, 11f));
                float d = MapLayout.RiverField.Distance(p.x, p.y);
                if (d < MapLayout.RiverHalfWidth - 0.5f || d > MapLayout.RiverHalfWidth + 3f) continue;
                if (MapLayout.RoadField.Distance(p.x, p.y) < MapLayout.RoadHalfWidth + 3.5f) continue;
                float scale = Range(rng, 0.5f, 0.9f);
                Scans.Rock(Scans.Rocks, variety.NextDouble(), new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), 0.4f + 0.75f * scale, root,
                    "Scenery_Rock_B", scale, 0.25f * scale);
            }
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}

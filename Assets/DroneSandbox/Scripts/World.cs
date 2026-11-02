using UnityEngine;

namespace DroneSandbox
{
    // Builds everything on the map that isn't a unit: terrain, river, fields, villages, woods, bridges, the
    // sector's special features and both bases, all from MapLayout. Used by the game (SandboxGame) and by the
    // editor preview (MapPreview).
    public static class World
    {
        public static Transform Build(Transform parent, bool preview)
        {
            MapLayout.Prepare();
            Undergrowth.Reset(); // the woods below note their trees for it
            var root = new GameObject("World").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(MapLayout.Plan.worldSeed); // same map every time for a sector

            Ground.Build(root);
            Backdrop.Build(root);
            Water.Build(root);
            Reservoir.Build(root);
            Farmland.Build(root, rng);
            Grass.Plant(Ground.Current);
            var scenery = new GameObject("Scenery").transform;
            scenery.SetParent(root, false);
            foreach (var bridge in MapLayout.Bridges) BuildBridge(scenery, bridge);
            BuildVillages(scenery, rng);
            BuildWoods(scenery, rng);
            BuildRocks(scenery, rng);
            Features.Build(scenery);
            Undergrowth.Build(scenery);
            Bases.BuildPlayerBase(root);
            Bases.BuildEnemyHq(root);
            if (preview) Bases.PreviewUnits(root);
            return root;
        }

        // Places Resources/Models/<model> on the ground (sunk a little so it doesn't float on slopes),
        // or builds a primitive stand-in when the model is missing.
        public static GameObject Place(string model, Vector3 pos, float yaw, float scale, float sink, Transform parent,
            System.Action<Transform> fallback = null)
        {
            var root = new GameObject(model).transform;
            root.SetParent(parent, false);
            float r = 1.2f * scale;
            float ground = Mathf.Min(Ground.HeightAt(pos), Ground.HeightAt(pos + new Vector3(r, 0f, r)),
                Ground.HeightAt(pos - new Vector3(r, 0f, r)), Ground.HeightAt(pos + new Vector3(r, 0f, -r)),
                Ground.HeightAt(pos + new Vector3(-r, 0f, r)));
            root.position = new Vector3(pos.x, ground - sink, pos.z);
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Art.TryModel(model, root, scale) == null) (fallback ?? (t => StandIn(t, scale)))(root);
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
                        Vector2 p = center + along * s + across * side * (MapLayout.RoadHalfWidth + 8f);
                        if (!Buildable(p)) continue;
                        string model;
                        if (v == 0 && !farmstead && side < 0f && Mathf.Abs(s - 6.5f) < 0.1f) model = "Scenery_Church";
                        else if (farmstead) model = s < 0f ? "Scenery_Barn" : "Scenery_House_A";
                        else
                        {
                            double roll = rng.NextDouble();
                            model = roll < 0.5 ? "Scenery_House_A" : roll < 0.82 ? "Scenery_House_B" : "Scenery_Barn";
                        }
                        // Doors (the model's +Z) face the road.
                        Vector2 facing = -across * side;
                        float yaw = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg + Range(rng, -4f, 4f);
                        Place(model, new Vector3(p.x, 0f, p.y), yaw, 1f, 0.35f, root);
                        Garden(root, p, along, across * side, rng);
                    }
                }
            }
        }

        // A fence behind the house and a fruit tree or two.
        static void Garden(Transform root, Vector2 house, Vector2 along, Vector2 outward, System.Random rng)
        {
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

        static bool Buildable(Vector2 p)
        {
            if (MapLayout.RoadField.Distance(p.x, p.y) < MapLayout.RoadHalfWidth + 2.5f) return false;
            if (MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverHalfWidth + 5f) return false;
            return !MapLayout.NearFlatSite(p, 4f) && !MapLayout.InField(p, 1f);
        }

        // ---- Woods ----

        static void BuildWoods(Transform parent, System.Random rng)
        {
            var root = new GameObject("Woods").transform;
            root.SetParent(parent, false);

            // Woods inside the battlefield (broadleaf and pine) and darker conifer forest on the hills around it, as
            // many as the sector's forest share asks for (0.3 in the Varrow Valley: 11 and 28 woods).
            var plan = MapLayout.Plan;
            int innerWant = Mathf.Min(24, Mathf.RoundToInt(11f * plan.forest / 0.3f));
            int outerWant = Mathf.Min(44, Mathf.RoundToInt(28f * plan.forest / 0.3f));
            int inner = 0, outer = 0;
            int attempts = plan.kind == SectorKind.VarrowValley ? 600 : 600 + 20 * (innerWant + outerWant);
            for (int attempt = 0; attempt < attempts && (inner < innerWant || outer < outerWant); attempt++)
            {
                var c = new Vector2(Range(rng, -215f, 215f), Range(rng, -215f, 215f));
                bool isOuter = Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.y)) > 105f;
                if (isOuter ? outer >= outerWant : inner >= innerWant) continue;
                if (!MapLayout.IsWild(c, 3f)) continue;
                if (isOuter) outer++; else inner++;
                float radius = isOuter ? Range(rng, 16f, 30f) : Range(rng, 8f, 17f);
                float spacing = isOuter ? 5.6f : 4.6f;
                int seed = rng.Next();
                for (float x = -radius; x <= radius; x += spacing)
                {
                    for (float z = -radius; z <= radius; z += spacing)
                    {
                        Vector2 p = c + new Vector2(x, z) + new Vector2(Range(rng, -1.6f, 1.6f), Range(rng, -1.6f, 1.6f));
                        // Ragged edges: the radius wobbles with direction.
                        float wobble = 0.75f + 0.35f * Mathf.PerlinNoise(seed % 1000 + Mathf.Atan2(z, x) * 1.3f, 0.5f);
                        if (new Vector2(x, z).magnitude > radius * wobble || !MapLayout.IsWild(p, 0.5f) || InClearing(p)) continue;
                        Place(PickTree(rng, isOuter), new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), Range(rng, 0.8f, 1.2f), 0.25f, root);
                        Undergrowth.NoteTree(p);
                    }
                }
            }

            // Trees and bushes along both river banks.
            var river = MapLayout.RiverField.points;
            for (int i = 0; i < river.Count - 1; i += 2)
            {
                Vector2 dir = (river[i + 1] - river[i]).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x);
                foreach (float sign in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() > 0.42) continue;
                    Vector2 p = river[i] + side * sign * (MapLayout.RiverHalfWidth + Range(rng, 3.2f, 9f));
                    if (Vector2.Distance(p, MapLayout.BridgePoint) < 14f || !MapLayout.IsWild(p, -2f)) continue;
                    double roll = rng.NextDouble();
                    string model = roll < 0.45 ? "Scenery_Tree" : roll < 0.65 ? "Scenery_Tree_B" : roll < 0.8 ? "Scenery_Birch" : "Scenery_Bush";
                    Place(model, new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), Range(rng, 0.8f, 1.15f), 0.25f, root);
                    if (model != "Scenery_Bush") Undergrowth.NoteTree(p);
                }
            }
        }

        // Logging clearings (Features fills them with stumps and log stacks) stay open.
        static bool InClearing(Vector2 p)
        {
            foreach (var c in MapLayout.Plan.clearings)
                if (Vector2.Distance(p, c) < 24f) return true;
            return false;
        }

        // A tree for a wood: the Varrow Valley's own mix, or, elsewhere, the sector's shares of conifers and birches
        // (more conifers on the outer hills).
        static string PickTree(System.Random rng, bool outer)
        {
            var plan = MapLayout.Plan;
            if (plan.kind != SectorKind.VarrowValley)
            {
                float roll = (float)rng.NextDouble(), conifer = outer ? Mathf.Min(1f, plan.conifer + 0.35f) : plan.conifer;
                if (roll < conifer * 0.25f) return "Scenery_Pine";
                if (roll < conifer * 0.4f) return "Scenery_Pine_B";
                if (roll < conifer) return "Scenery_Spruce";
                if (roll < conifer + plan.birch * (1f - conifer)) return "Scenery_Birch";
                float rest = (float)rng.NextDouble();
                return rest < 0.55f ? "Scenery_Tree" : rest < 0.85f ? "Scenery_Tree_B" : "Scenery_Poplar";
            }
            double r = rng.NextDouble();
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
            for (int attempt = 0; attempt < 300 && placed < 34; attempt++)
            {
                var p = new Vector2(Range(rng, -200f, 200f), Range(rng, -200f, 200f));
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

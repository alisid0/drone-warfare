using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Ground cover from the photo scans (see Scans), after an art review of the map that found the ground too bare
    // and too even: a stony, weedy band along the lake shore and the riverbanks, ferns, nettles and moss among the
    // trees, bushes and shrubs along the edges of the woods, and stumps and fallen trunks here and there. Each kind
    // is spread by its own noise mask, so it comes in patches with bare stretches between, the way real ground
    // cover grows, instead of an even sprinkle. Counts are capped (about 270 objects over the whole map) to keep
    // the frame rate: most of the plant scans are clumps of several plants in one mesh, and small ones are culled
    // once they're a few pixels tall (Art.TuneLods).
    public static class Undergrowth
    {
        const float Cell = 8f; // grid for finding nearby trees and undergrowth quickly

        static readonly List<Vector2> trees = new List<Vector2>();
        static readonly Dictionary<Vector2Int, List<Vector2>> treeCells = new Dictionary<Vector2Int, List<Vector2>>();
        static readonly Dictionary<Vector2Int, List<Vector2>> placedCells = new Dictionary<Vector2Int, List<Vector2>>();

        public static void Reset()
        {
            trees.Clear();
            treeCells.Clear();
            placedCells.Clear();
        }

        // World.BuildWoods and Reservoir.Forest call this for each tree they plant, so the undergrowth can grow
        // round them.
        public static void NoteTree(Vector2 p)
        {
            trees.Add(p);
            Add(treeCells, p);
        }

        public static void Build(Transform parent)
        {
            if (!Scans.Has(Scans.Ferns[0])) return; // the scans haven't been imported (Tools/Blender/import_scans.py)
            var root = new GameObject("Undergrowth").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(41); // its own numbers, so nothing else on the map moves
            LakeShore(Group(root, "Lake Shore"), rng);
            Riverbanks(Group(root, "Riverbanks"), rng);
            WoodlandFloor(Group(root, "Woodland Floor"), rng);
            WoodEdges(Group(root, "Wood Edges"), rng);
            Deadwood(Group(root, "Deadwood"), rng);
            placedCells.Clear(); // the trees stay noted: missions keep their targets out from under them (UnderTrees)
        }

        // ---- The waterside ----

        // The lake shore: a band that widens and narrows between 2 and 6 m from the water. The reservoir lies
        // upstream of the dam, so points are tried in a box round it in dam coordinates; round natural lakes and
        // pools, in a ring round a random one. Points are kept when they land in the band.
        static void LakeShore(Transform root, System.Random rng)
        {
            int[] count = new int[3], cap = { 22, 14, 30 }; // pebbles, stones, plants
            var ponds = MapLayout.Plan.ponds;
            if (!MapLayout.HasDam && ponds.Count == 0) return;
            for (int attempt = 0; attempt < 30000 && !Full(count, cap); attempt++)
            {
                Vector2 p;
                float level;
                if (MapLayout.HasDam && (ponds.Count == 0 || rng.NextDouble() < 0.5))
                {
                    var local = new Vector2(Range(rng, -140f, 2f), Range(rng, -50f, 125f));
                    p = MapLayout.DamPoint + MapLayout.DamDown * local.x + MapLayout.DamAcross * local.y;
                    level = MapLayout.LakeLevel;
                }
                else
                {
                    var pond = ponds[rng.Next(ponds.Count)];
                    float a = Range(rng, 0f, Mathf.PI * 2f);
                    p = pond.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * pond.radius * Range(rng, 0.85f, 1.45f);
                    level = pond.level;
                }
                float edge = MapLayout.LakeDistance(p.x, p.y);
                if (edge < 0.3f || edge > Band(p, 0f) || !OpenGround(p)) continue;
                if (Ground.HeightAt(At(p)) < level + 0.05f) continue; // still under the water
                Waterside(root, rng, p, edge, count, cap);
            }
        }

        // Both riverbanks below the gorge (which has its own boulders): a point either side of each river point
        // (they're 2 m apart), taken in random order so the caps don't all fill up near the dam.
        static void Riverbanks(Transform root, System.Random rng)
        {
            var field = MapLayout.RiverField;
            var line = field.points;
            float hw = MapLayout.RiverHalfWidth;
            int[] count = new int[3], cap = { 10, 8, 20 };
            foreach (int i in Shuffled(line.Count, rng))
            {
                if (Full(count, cap)) break;
                if (i == 0 || !field.IsValidSegment(i - 1)) continue;
                Vector2 dir = (line[i] - line[i - 1]).normalized, side = new Vector2(-dir.y, dir.x);
                float sign = rng.NextDouble() < 0.5 ? -1f : 1f;
                float edge = Range(rng, 0.3f, Band(line[i], sign * 40f));
                Vector2 p = line[i] + side * sign * (hw + edge);
                if (!OpenGround(p) || Vector2.Distance(p, MapLayout.BridgePoint) < 16f) continue;
                if (field.Distance(p.x, p.y) < hw + 0.2f) continue; // a bend brings the water closer here
                if (Ground.HeightAt(At(p)) < field.values[i] + 0.05f) continue;
                Waterside(root, rng, p, edge, count, cap);
            }
        }

        // One thing for the waterside at p, `edge` metres from the water: on the strip the water washes, drifts of
        // pebbles and here and there a bigger stone or a slab; further up, patches of weeds, herbs and flowers.
        // count/cap: pebbles, stones, plants.
        static void Waterside(Transform root, System.Random rng, Vector2 p, float edge, int[] count, int[] cap)
        {
            float yaw = Range(rng, 0f, 360f);
            if (edge < 1.6f)
            {
                if (rng.NextDouble() > Patches(p, 0.09f, 11f, 0.5f)) return;
                if (rng.NextDouble() < 0.6)
                {
                    if (count[0] < cap[0] && Put(Scans.Pick(Scans.Pebbles, rng.NextDouble()), p, yaw, Range(rng, 0.8f, 1.2f), root,
                            room: 2.5f, sink: 0f, tilt: 1f)) count[0]++;
                }
                else if (count[1] < cap[1])
                {
                    // The scanned stones are 30 and 15 cm; scaled up they make knee-high rocks and slabs.
                    bool flat = rng.NextDouble() < 0.5;
                    if (Put(flat ? Scans.FlatStone : Scans.Stone, p, yaw, flat ? Range(rng, 6f, 10f) : Range(rng, 2.5f, 4.5f), root,
                            room: 2f, sink: 0.3f, tilt: flat ? 0.8f : 0f, footprint: flat ? 0f : 0.7f)) count[1]++;
                }
            }
            else if (count[2] < cap[2] && rng.NextDouble() < Patches(p, 0.07f, 23f, 0.52f))
            {
                if (Put(Scans.Pick(Scans.Wildflowers, rng.NextDouble()), p, yaw, Range(rng, 0.85f, 1.2f), root, room: 2.2f)) count[2]++;
            }
        }

        // How far from the water the waterside band reaches here: 2 to 6 m, wandering along the shore.
        static float Band(Vector2 p, float seed) => Mathf.Lerp(2f, 6f, Mathf.PerlinNoise(p.x * 0.035f + 61f + seed, p.y * 0.035f + 7f));

        // ---- The woods ----

        // Among the trees: moss at the foot of trunks, ferns in the gaps between them, and patches of nettles.
        static void WoodlandFloor(Transform root, System.Random rng)
        {
            int ferns = 0, moss = 0, nettles = 0;
            foreach (int t in Shuffled(trees.Count, rng))
            {
                if (ferns >= 60 && moss >= 20 && nettles >= 18) break;
                Vector2 tree = trees[t];
                if (rng.NextDouble() > Interest(tree)) continue;
                Vector2 dir = Direction(rng);
                double kind = rng.NextDouble();
                if (kind < 0.25)
                {
                    Vector2 p = tree + dir * Range(rng, 0.7f, 1.2f);
                    if (moss < 20 && rng.NextDouble() < Patches(p, 0.05f, 37f, 0.5f) && MapLayout.IsWild(p, 0f)
                        && Put(Scans.Pick(Scans.Moss, rng.NextDouble()), p, Range(rng, 0f, 360f), Range(rng, 0.9f, 1.3f), root,
                            room: 1f, sink: 0f, tilt: 1f)) moss++;
                }
                else if (kind < 0.8)
                {
                    // In the gaps, where some light gets through (and the camera can see them past the crowns).
                    Vector2 p = tree + dir * Range(rng, 1.4f, 3.2f);
                    if (ferns < 60 && NearestTree(p) > 1.2f && rng.NextDouble() < Patches(p, 0.04f, 53f, 0.45f) && MapLayout.IsWild(p, 0f)
                        && Put(Scans.Pick(Scans.Ferns, rng.NextDouble()), p, Range(rng, 0f, 360f), Range(rng, 0.85f, 1.25f), root,
                            room: 1.6f, sink: 0.03f, tilt: 0.5f)) ferns++;
                }
                else
                {
                    Vector2 p = tree + dir * Range(rng, 1.5f, 3.5f);
                    if (nettles < 18 && NearestTree(p) > 1.2f && rng.NextDouble() < Patches(p, 0.05f, 89f, 0.5f) && MapLayout.IsWild(p, 0f)
                        && Put(Scans.Pick(Scans.Nettles, rng.NextDouble()), p, Range(rng, 0f, 360f), Range(rng, 0.9f, 1.15f), root,
                            room: 2f, sink: 0.03f, tilt: 0.3f)) nettles++;
                }
            }
        }

        // Bushes, shrubs and nettles just outside the edge of a wood, where there's light. A tree is on the edge
        // when the trees within 10 m of it all lie off to one side; the plant goes on the other side.
        static void WoodEdges(Transform root, System.Random rng)
        {
            int bushes = 0, shrubs = 0, nettles = 0;
            foreach (int t in Shuffled(trees.Count, rng))
            {
                if (bushes >= 22 && shrubs >= 14 && nettles >= 10) break;
                Vector2 tree = trees[t];
                if (rng.NextDouble() > Interest(tree)) continue;
                Vector2 middle = Vector2.zero;
                int neighbours = 0;
                foreach (var other in TreesNear(tree, 10f))
                {
                    if (other == tree) continue;
                    middle += other;
                    neighbours++;
                }
                if (neighbours < 2) continue; // a lone tree, not a wood
                Vector2 inward = middle / neighbours - tree;
                if (inward.magnitude < 3f) continue; // trees all round: inside the wood
                Vector2 outward = -inward.normalized;
                Vector2 p = tree + outward * Range(rng, 2.2f, 4f) + new Vector2(-outward.y, outward.x) * Range(rng, -2f, 2f);
                if (!MapLayout.IsWild(p, 0f) || NearestTree(p) < 1.8f || rng.NextDouble() > Patches(p, 0.05f, 71f, 0.4f)) continue;
                double kind = rng.NextDouble();
                float yaw = Range(rng, 0f, 360f);
                if (kind < 0.5 && bushes < 22)
                {
                    if (Put(Scans.Pick(Scans.Bushes, rng.NextDouble()), p, yaw, Range(rng, 0.85f, 1.2f), root, room: 3f, sink: 0.04f, tilt: 0f, footprint: 0.5f)) bushes++;
                }
                else if (kind < 0.8 && shrubs < 14)
                {
                    if (Put(Scans.Pick(Scans.Shrubs, rng.NextDouble()), p, yaw, Range(rng, 0.85f, 1.15f), root, room: 3f, sink: 0.03f, tilt: 0f, footprint: 0.5f)) shrubs++;
                }
                else if (nettles < 10)
                {
                    if (Put(Scans.Pick(Scans.Nettles, rng.NextDouble()), p, yaw, Range(rng, 0.9f, 1.2f), root, room: 2f, sink: 0.03f, tilt: 0.3f)) nettles++;
                }
            }
        }

        // Old stumps in the gaps of the woods, and a few fallen trunks lying along the ground, on gentle slopes only.
        static void Deadwood(Transform root, System.Random rng)
        {
            int stumps = 0, trunks = 0;
            foreach (int t in Shuffled(trees.Count, rng))
            {
                if (stumps >= 14 && trunks >= 6) break;
                Vector2 tree = trees[t];
                if (rng.NextDouble() > 0.4f * Interest(tree)) continue;
                Vector2 p = tree + Direction(rng) * Range(rng, 2.5f, 5f);
                if (!MapLayout.IsWild(p, 0f) || NearestTree(p) < 2f || Ground.NormalAt(At(p)).y < 0.94f) continue;
                float yaw = Range(rng, 0f, 360f);
                if (rng.NextDouble() < 0.7)
                {
                    // The stump scans stand on a mound of leaf litter; sunk so the mound's rim meets the ground.
                    if (stumps < 14 && Put(Scans.Pick(Scans.Stumps, rng.NextDouble()), p, yaw, Range(rng, 0.85f, 1.15f), root,
                            room: 3f, sink: 0.38f, tilt: 0.6f)) stumps++;
                }
                else if (trunks < 6)
                {
                    // The trunk lies along its X; both ends must be clear of trees.
                    float a = yaw * Mathf.Deg2Rad;
                    var along = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * 1.6f;
                    if (NearestTree(p + along) < 1f || NearestTree(p - along) < 1f || !MapLayout.IsWild(p + along, 0f) || !MapLayout.IsWild(p - along, 0f)) continue;
                    if (Put(Scans.FallenTrunk, p, yaw, Range(rng, 0.9f, 1.2f), root, room: 3.5f, sink: 0.25f, tilt: 1f)) trunks++;
                }
            }
        }

        // ---- Helpers ----

        // Stands a scan at p (see Scans.Place) unless other undergrowth is within `room` metres. The defaults suit
        // ground cover: set by the ground at its centre and leaning partly with the slope. Pebbles and the low
        // carpets (moss, celandine, dandelions) don't cast shadows: they're barely off the ground.
        static bool Put(string model, Vector2 p, float yaw, float scale, Transform root, float room,
            float sink = 0.03f, float tilt = 0.6f, float footprint = 0f)
        {
            if (model == null || Crowded(p, room)) return false;
            bool low = model.StartsWith("Scan_Pebbles") || model.StartsWith("Scan_Moss_") || model.StartsWith("Scan_Celandine")
                       || model.StartsWith("Scan_Dandelions");
            if (Scans.Place(model, At(p), yaw, scale, root, sink, tilt, footprint, shadows: !low) == null) return false;
            Add(placedCells, p);
            return true;
        }

        // Where the camera looks: the battlefield the RTS camera roams (plus what it sees past the edge) and the lake,
        // photo mode's showpiece. The far hills get only a little.
        static float Interest(Vector2 p) =>
            Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) < 130f || MapLayout.LakeDistance(p.x, p.y) < 45f ? 1f : 0.15f;

        // Patchy cover: two octaves of Perlin noise (offset by seed, so each kind has its own patches) turned into a
        // 0-1 chance with a soft edge at threshold. 0.5 covers about half the ground; higher, less.
        static float Patches(Vector2 p, float frequency, float seed, float threshold)
        {
            float n = 0.7f * Mathf.PerlinNoise(p.x * frequency + seed, p.y * frequency + seed * 0.37f)
                    + 0.3f * Mathf.PerlinNoise(p.x * frequency * 3.3f - seed, p.y * frequency * 3.3f + seed * 0.71f);
            return Mathf.SmoothStep(0f, 1f, (n - threshold + 0.06f) / 0.12f);
        }

        // Open ground by the water: inside the map, off the roads, fields, villages and bases, and clear of the dam
        // and the gorge. (MapLayout.IsWild says the same but also keeps a few metres from the water.)
        static bool OpenGround(Vector2 p)
        {
            if (Mathf.Abs(p.x) > 220f || Mathf.Abs(p.y) > 220f) return false;
            if (MapLayout.RoadField.Distance(p.x, p.y) < MapLayout.RoadHalfWidth + 1.5f) return false;
            if (MapLayout.NearDam(p, 4f) || MapLayout.InGorge(p, 2f)) return false;
            return !MapLayout.NearFlatSite(p, 3f) && !MapLayout.NearVillage(p, 0f) && !MapLayout.InField(p, 1f);
        }

        static float NearestTree(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var t in TreesNear(p, Cell)) best = Mathf.Min(best, Vector2.Distance(p, t));
            return best;
        }

        static IEnumerable<Vector2> TreesNear(Vector2 p, float radius) => Near(treeCells, p, radius);

        // Whether a tree stands within `room` of p: Situations keeps mission targets out from under the canopies.
        public static bool UnderTrees(Vector2 p, float room)
        {
            foreach (var _ in TreesNear(p, room)) return true;
            return false;
        }

        static bool Crowded(Vector2 p, float room)
        {
            foreach (var _ in Near(placedCells, p, room)) return true;
            return false;
        }

        static IEnumerable<Vector2> Near(Dictionary<Vector2Int, List<Vector2>> cells, Vector2 p, float radius)
        {
            int reach = Mathf.CeilToInt(radius / Cell);
            var c = CellOf(p);
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                    if (cells.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var list))
                        foreach (var q in list)
                            if ((q - p).sqrMagnitude < radius * radius) yield return q;
        }

        static void Add(Dictionary<Vector2Int, List<Vector2>> cells, Vector2 p)
        {
            var c = CellOf(p);
            if (!cells.TryGetValue(c, out var list)) cells[c] = list = new List<Vector2>();
            list.Add(p);
        }

        static Vector2Int CellOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));

        // 0..count-1 in random order.
        static int[] Shuffled(int count, System.Random rng)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }

        static bool Full(int[] count, int[] cap)
        {
            for (int i = 0; i < count.Length; i++)
                if (count[i] < cap[i]) return false;
            return true;
        }

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static Vector2 Direction(System.Random rng)
        {
            float a = Range(rng, 0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        static Vector3 At(Vector2 p) => new Vector3(p.x, 0f, p.y);

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}

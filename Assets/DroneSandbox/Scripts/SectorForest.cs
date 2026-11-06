using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Logical woodland survives every render tier: one compact record per stem and one index per candidate cell.
    public static class SectorForest
    {
        // The stand dimensions also supply crown occlusion and drone clearance independently of rendering.
        public struct Stem
        {
            public Vector3 position;
            public ForestPattern.Stand stand;
            public int i, j;
        }

        // Recipe-only maps use irregular groves; world crops use the world's boundary without another shuffle.
        struct Grove
        {
            public Vector2 center;
            public float radius, stretch, angle;
        }

        static readonly List<Stem> stems = new List<Stem>();
        static readonly List<Grove> groves = new List<Grove>();
        // Cached bounds let the renderer find small recipe groves without scanning empty world candidate cells.
        public sealed class LocalCell
        {
            public readonly List<Stem> stems = new List<Stem>();
            public Bounds bounds;
        }

        static readonly Dictionary<long, LocalCell> localCells = new Dictionary<long, LocalCell>();
        public const int RenderCell = 120;
        public static IReadOnlyDictionary<long, LocalCell> LocalCells => localCells;
        static int[] indices;
        static int firstX, firstZ, columns, rows;
        public static IReadOnlyList<Stem> Stems => stems;
        public static Vector2 Origin { get; private set; }
        public static int Version { get; private set; }
        public static bool Ready { get; private set; }
        public static double BuildMilliseconds { get; private set; }
        public static List<ForestHlod.Data> PreparedCanopy { get; private set; }

        // Drop sector ownership before planting gardens, so stale logical trees cannot exclude the next sector.
        public static void Reset()
        {
            Ready = false; indices = null; stems.Clear(); groves.Clear(); localCells.Clear(); PreparedCanopy = null; Version++;
        }

        // Populate every accepted stem, without a render budget or a GameObject; reuse the floor's existing input.
        public static void Build(Random rng, List<Vector2> woodland)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Reset(); woodland.Clear();
            Origin = MapLayout.Plan.world != null ? MapLayout.Plan.world.center : FarTerrain.Origin;
            if (MapLayout.Plan.world == null) PlanGroves(rng);
            float step = ForestPattern.TreeStep, half = Ground.Size * 0.5f;
            firstX = Mathf.FloorToInt((Origin.x - half) / step);
            firstZ = Mathf.FloorToInt((Origin.y - half) / step);
            columns = Mathf.CeilToInt((Origin.x + half) / step) - firstX;
            rows = Mathf.CeilToInt((Origin.y + half) / step) - firstZ;
            indices = new int[columns * rows];
            var candidates = new Stem[indices.Length];
            // All worker inputs are completed read-only plans; terrain sampling and occupancy writes stay below.
            System.Threading.Tasks.Parallel.For(0, rows, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4 }, row =>
            {
                int j = firstZ + row;
                for (int i = firstX; i < firstX + columns; i++)
                {
                    Vector2 p = ForestPattern.Candidate(i, j) - Origin;
                    float edge = Edge(p);
                    if (edge <= 0f || !ForestPattern.Tree(i, j, edge, out _, out var stand)) continue;
                    candidates[row * columns + i - firstX] = new Stem { position = new Vector3(p.x, 0f, p.y), stand = stand, i = i, j = j };
                }
            });
            // Stable row order makes thread scheduling irrelevant. The pattern already enforces mutual spacing.
            for (int k = 0; k < candidates.Length; k++)
            {
                var stem = candidates[k];
                if (stem.stand.height <= 0f) continue;
                Vector2 p = new Vector2(stem.position.x, stem.position.z);
                if (Undergrowth.UnderTrees(p, 2.4f)) continue; // only gardens/orchards exist in the grid yet
                stem.position.y = Ground.HeightAt(stem.position) - 0.15f;
                stems.Add(stem); indices[k] = stems.Count; woodland.Add(p);
                if (MapLayout.Plan.world == null)
                {
                    int cx = Mathf.FloorToInt((p.x + Origin.x) / RenderCell), cz = Mathf.FloorToInt((p.y + Origin.y) / RenderCell);
                    long key = ((long)cx << 32) ^ (uint)cz;
                    if (!localCells.TryGetValue(key, out var cell))
                        localCells.Add(key, cell = new LocalCell { bounds = new Bounds(stem.position, Vector3.zero) });
                    cell.stems.Add(stem); cell.bounds.Encapsulate(stem.position);
                }
            }
            foreach (var cell in localCells.Values) cell.bounds.Expand(52f);
            foreach (var stem in stems) Undergrowth.NoteTree(new Vector2(stem.position.x, stem.position.z));
            // Publish occupancy only after all indices and stems exist; renderers invalidate any pre-build cache.
            Ready = true; Version++;
            PreparedCanopy = PrepareCanopy();
            BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
        }

        // Stand-scale relief is ready with the sector, so its first frame has the same age zones as its trees.
        static List<ForestHlod.Data> PrepareCanopy()
        {
            var result = new List<ForestHlod.Data>();
            int x0 = Mathf.FloorToInt((Origin.x - Ground.Size * 0.5f + WorldPlan.Half) / ForestHlod.BlockSize);
            int z0 = Mathf.FloorToInt((Origin.y - Ground.Size * 0.5f + WorldPlan.Half) / ForestHlod.BlockSize);
            int x1 = Mathf.FloorToInt((Origin.x + Ground.Size * 0.5f + WorldPlan.Half) / ForestHlod.BlockSize);
            int z1 = Mathf.FloorToInt((Origin.y + Ground.Size * 0.5f + WorldPlan.Half) / ForestHlod.BlockSize);
            for (int j = z0; j <= z1; j++) for (int i = x0; i <= x1; i++)
            {
                // World stands start at 32 m; recipe groves still resolve at 4 m before their floor is painted.
                var data = new ForestHlod.Data { corner = new Vector2(i * ForestHlod.BlockSize - WorldPlan.Half, j * ForestHlod.BlockSize - WorldPlan.Half),
                    step = 32, local = MapLayout.Plan.world == null };
                var build = ForestHlod.Build(data, null); while (build.MoveNext()) { }
                result.Add(data);
            }
            return result;
        }

        // Exact half-open ownership prevents both a duplicate row and an empty seam at the terrain boundary.
        public static bool Contains(Vector2 p) => Ready && p.x >= -Ground.Size * 0.5f && p.x < Ground.Size * 0.5f
            && p.y >= -Ground.Size * 0.5f && p.y < Ground.Size * 0.5f;

        // All renderer tiers ask the same occupancy table inside the crop, including excluded road/mission clearings.
        public static bool Tree(int i, int j, out Vector2 world, out ForestPattern.Stand stand)
        {
            world = ForestPattern.Candidate(i, j);
            if (!Contains(world - Origin)) return ForestPattern.Tree(i, j, out world, out stand);
            int x = i - firstX, z = j - firstZ;
            int index = x >= 0 && z >= 0 && x < columns && z < rows ? indices[z * columns + x] : 0;
            stand = index > 0 ? stems[index - 1].stand : default;
            return index > 0;
        }

        // Only logical stems have continuous canopy coverage; decorative bank/garden trees can cull independently.
        public static bool HasFloorCoverage(Vector2 local)
        {
            if (!Contains(local)) return false;
            int x = Mathf.FloorToInt((local.x + Origin.x) / ForestPattern.TreeStep) - firstX;
            int z = Mathf.FloorToInt((local.y + Origin.y) / ForestPattern.TreeStep) - firstZ;
            // At distant world coordinates a jittered candidate can round onto the next five-metre cell edge.
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int i = x + dx, j = z + dz;
                int index = i >= 0 && i < columns && j >= 0 && j < rows ? indices[j * columns + i] : 0;
                if (index <= 0) continue;
                Vector3 p = stems[index - 1].position;
                if ((local - new Vector2(p.x, p.z)).sqrMagnitude < 0.01f) return true;
            }
            return false;
        }

        // Narrow recipe woods can fall between four-metre surface nodes; actual crowns support those boundary nodes.
        public static float CrownSupport(Vector2 local)
        {
            if (!Contains(local)) return float.MinValue;
            int x = Mathf.FloorToInt((local.x + Origin.x) / ForestPattern.TreeStep) - firstX;
            int z = Mathf.FloorToInt((local.y + Origin.y) / ForestPattern.TreeStep) - firstZ;
            float top = float.MinValue;
            for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++)
            {
                int i = x + dx, j = z + dz;
                int index = i >= 0 && i < columns && j >= 0 && j < rows ? indices[j * columns + i] : 0;
                if (index <= 0) continue;
                var stem = stems[index - 1];
                float distance = (local - new Vector2(stem.position.x, stem.position.z)).sqrMagnitude;
                float radius = stem.stand.radius;
                if (distance >= radius * radius) continue;
                top = Mathf.Max(top, stem.position.y + stem.stand.height * 0.65f * Mathf.Sqrt(1f - distance / (radius * radius)));
            }
            return top;
        }

        // HLOD uses the same glades, local reserves and species as its individual crowns.
        public static ForestPattern.Stand At(float x, float z)
        {
            Vector2 p = new Vector2(x, z) - Origin;
            return Contains(p) ? ForestPattern.At(x, z, Edge(p)) : ForestPattern.At(x, z);
        }

        // Protect roads, fields, sites and logging glades, including the special Deep Forest reserve.
        public static float Edge(Vector2 p)
        {
            var plan = MapLayout.Plan;
            float extent = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
            if (extent >= Ground.Size * 0.5f) return 0f;
            // Logical world trees do not inherit the old GameObject scenery's empty 15 m border strip.
            if (extent >= MapLayout.Edge)
                return plan.world != null ? ForestPattern.Edge(p.x + Origin.x, p.y + Origin.y) : 0f;
            bool core = plan.kind == SectorKind.DeepForest && plan.reservedFeatures.Count > 0 && plan.reservedFeatures[0].Contains(p);
            float edge = 0f;
            if (plan.world != null) edge = ForestPattern.Edge(p.x + Origin.x, p.y + Origin.y);
            else foreach (var grove in groves)
                {
                    Vector2 d = p - grove.center;
                    float ca = Mathf.Cos(grove.angle), sa = Mathf.Sin(grove.angle);
                    float u = (d.x * ca + d.y * sa) / grove.stretch, v = (d.y * ca - d.x * sa) * grove.stretch;
                    float ripple = (ForestPattern.Noise(p.x + Origin.x, p.y + Origin.y, 19f, 95) - 0.5f) * 14f;
                    edge = Mathf.Max(edge, Mathf.SmoothStep(0f, 1f, (grove.radius + ripple - Mathf.Sqrt(u * u + v * v)) / 25f));
                }
            if (core)
            {
                var a = plan.reservedFeatures[0];
                Vector2 d = p - a.center;
                float inset = Mathf.Min(a.halfLength - Mathf.Abs(Vector2.Dot(d, a.along)), a.halfWidth - Mathf.Abs(Vector2.Dot(d, a.Across)));
                edge = Mathf.Max(edge, Mathf.SmoothStep(0f, 1f, inset / 25f));
            }
            if (edge <= 0f || !(core ? CoreOpen(p) : MapLayout.IsWild(p, 0.5f))) return 0f;
            foreach (var c in plan.clearings) edge *= Mathf.SmoothStep(0f, 1f, (Vector2.Distance(p, c) - Scale.D(24f)) / 20f);
            foreach (var g in plan.woodlandGlades)
            {
                // Keep the reserved interior clear, with irregular young trees through a soft outer rim.
                float ripple = ForestPattern.Noise(p.x + Origin.x, p.y + Origin.y, 17f, 96) * 8f;
                edge *= Mathf.SmoothStep(0f, 1f, (Vector2.Distance(p, g.center) - g.radius - ripple) / 24f);
            }
            return edge;
        }

        // The reserve is forest land, so bypass only its reservation while retaining physical and mission clearances.
        static bool CoreOpen(Vector2 p)
        {
            var plan = MapLayout.Plan;
            if (MapLayout.LakeDistance(p.x, p.y) < 2f || MapLayout.NearDam(p, 1f) || MapLayout.InGorge(p, 1f)
                || MapLayout.NearFlatSite(p, 4f) || MapLayout.NearVillage(p, 1f) || MapLayout.InField(p, 1f)) return false;
            if (Vector2.Distance(p, new Vector2(plan.basePosition.x, plan.basePosition.z)) < MapLayout.BaseRadius + 11f
                || Vector2.Distance(p, new Vector2(plan.enemyHqPosition.x, plan.enemyHqPosition.z)) < MapLayout.HqRadius + 11f) return false;
            return MapLayout.RoadField.Distance(p.x, p.y) > MapLayout.RoadWidthAt(p.x, p.y) + 2f
                && MapLayout.TrackField.Distance(p.x, p.y) > 2f && MapLayout.RailField.Distance(p.x, p.y) > 4f
                && (!MapLayout.HasRiver || MapLayout.RiverField.Distance(p.x, p.y) > MapLayout.RiverWidthAt(p.x, p.y) + 3f);
        }

        // Bound local woods geographically, never by deleting stems when a shared renderer allowance fills up.
        static void PlanGroves(Random rng)
        {
            var plan = MapLayout.Plan;
            int innerWant = Mathf.Min(10, Mathf.RoundToInt(6f * plan.forest / 0.3f));
            int outerWant = Mathf.Min(22, Mathf.RoundToInt(16f * plan.forest / 0.3f));
            int inner = 0, outer = 0;
            float limit = MapLayout.Edge - 10f, innerLimit = Mathf.Min(Scale.D(105f), MapLayout.Edge * 0.48f);
            for (int attempt = 0; attempt < 1600 && (inner < innerWant || outer < outerWant); attempt++)
            {
                // Finish the inner quota explicitly; random sampling across 4 km2 otherwise favours the outer ring.
                float reach = inner < innerWant ? innerLimit : limit;
                Vector2 p = new Vector2((float)rng.NextDouble() * reach * 2f - reach, (float)rng.NextDouble() * reach * 2f - reach);
                bool isOuter = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) > innerLimit;
                if (isOuter ? outer >= outerWant : inner >= innerWant) continue;
                if (!MapLayout.IsWild(p, 3f)) continue;
                if (isOuter) outer++; else inner++;
                groves.Add(new Grove { center = p,
                    radius = Mathf.Min(Scale.D((isOuter ? 16f : 8f) + (float)rng.NextDouble() * (isOuter ? 14f : 9f)), Ground.Size * 0.075f),
                    stretch = 1f + (float)rng.NextDouble() * 0.5f, angle = (float)rng.NextDouble() * Mathf.PI });
            }
        }
    }
}

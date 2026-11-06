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
            Ready = false; indices = null; stems.Clear(); groves.Clear(); PreparedCanopy = null; Version++;
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
            }
            foreach (var stem in stems) Undergrowth.NoteTree(new Vector2(stem.position.x, stem.position.z));
            // Publish occupancy only after all indices and stems exist; renderers invalidate any pre-build cache.
            Ready = true; Version++;
            PreparedCanopy = PrepareCanopy();
            BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
        }

        // A cheap coarse surface is ready with the sector, so the first frame never shows an empty plantation.
        static List<ForestHlod.Data> PrepareCanopy()
        {
            var result = new List<ForestHlod.Data>();
            int x0 = Mathf.FloorToInt((Origin.x - Ground.Size * 0.5f + WorldPlan.Half) / ForestHlod.BlockSize);
            int z0 = Mathf.FloorToInt((Origin.y - Ground.Size * 0.5f + WorldPlan.Half) / ForestHlod.BlockSize);
            int x1 = Mathf.FloorToInt((Origin.x + Ground.Size * 0.5f + WorldPlan.Half) / ForestHlod.BlockSize);
            int z1 = Mathf.FloorToInt((Origin.y + Ground.Size * 0.5f + WorldPlan.Half) / ForestHlod.BlockSize);
            for (int j = z0; j <= z1; j++) for (int i = x0; i <= x1; i++)
            {
                var data = new ForestHlod.Data { corner = new Vector2(i * ForestHlod.BlockSize - WorldPlan.Half, j * ForestHlod.BlockSize - WorldPlan.Half), step = 128 };
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
            foreach (var g in plan.woodlandGlades) edge *= Mathf.SmoothStep(0f, 1f, (Vector2.Distance(p, g.center) - g.radius) / 18f);
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
            int count = Mathf.Min(32, Mathf.RoundToInt(22f * MapLayout.Plan.forest / 0.3f));
            float limit = MapLayout.Edge - 10f;
            for (int attempt = 0; attempt < 1600 && groves.Count < count; attempt++)
            {
                Vector2 p = new Vector2((float)rng.NextDouble() * limit * 2f - limit, (float)rng.NextDouble() * limit * 2f - limit);
                if (!MapLayout.IsWild(p, 3f)) continue;
                groves.Add(new Grove { center = p, radius = Mathf.Min(Scale.D(12f + (float)rng.NextDouble() * 18f), Ground.Size * 0.075f),
                    stretch = 1f + (float)rng.NextDouble() * 0.5f, angle = (float)rng.NextDouble() * Mathf.PI });
            }
        }
    }
}

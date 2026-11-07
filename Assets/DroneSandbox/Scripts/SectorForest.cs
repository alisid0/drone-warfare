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
            public float radius, stretch, cosine, sine, reach;
        }

        // Reserved glades keep their required clear circle inside an elongated, track-aligned visual opening.
        struct Glade
        {
            public Vector2 center, along;
            public float length, width, rim;
        }
        static readonly List<Glade> glades = new List<Glade>();
        const float MarginCell = 4f;
        static float[] clearance;
        static int marginSide;

        static readonly List<Stem> stems = new List<Stem>();
        const int GroveCell = 64;
        static readonly Dictionary<long, List<Grove>> groveCells = new Dictionary<long, List<Grove>>();
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
        public static double StemsMilliseconds { get; private set; }
        public static double IndicesMilliseconds { get; private set; }
        public static double CanopyMilliseconds { get; private set; }
        public static List<ForestHlod.Data> PreparedCanopy { get; private set; }

        // Drop sector ownership before planting gardens, so stale logical trees cannot exclude the next sector.
        public static void Reset()
        {
            Ready = false; indices = null; clearance = null; stems.Clear(); groveCells.Clear(); glades.Clear(); localCells.Clear(); PreparedCanopy = null; Version++;
            BuildMilliseconds = StemsMilliseconds = IndicesMilliseconds = CanopyMilliseconds = 0;
        }

        // Populate every accepted stem, without a render budget or a GameObject; reuse the floor's existing input.
        public static void Build(Random rng, List<Vector2> woodland)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Reset(); woodland.Clear();
            Origin = MapLayout.Plan.world != null ? MapLayout.Plan.world.center : FarTerrain.Origin;
            if (MapLayout.Plan.world == null) PlanGroves(rng);
            PlanGlades();
            PlanMargins();
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
            StemsMilliseconds = timer.Elapsed.TotalMilliseconds;
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
            IndicesMilliseconds = timer.Elapsed.TotalMilliseconds - StemsMilliseconds;
            PreparedCanopy = PrepareCanopy();
            BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
            CanopyMilliseconds = BuildMilliseconds - StemsMilliseconds - IndicesMilliseconds;
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
                if (data.local) ForestHlod.SampleGround(data);
                else { var build = ForestHlod.Build(data, null); while (build.MoveNext()) { } }
                result.Add(data);
            }
            if (MapLayout.Plan.world == null)
                System.Threading.Tasks.Parallel.For(0, result.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4 }, k =>
                {
                    // Terrain is already sampled; each worker owns its geometry, caches and colour array until joined.
                    var data = result[k];
                    var build = ForestHlod.Build(data, null); while (build.MoveNext()) { }
                    data.sampledGround = null;
                });
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

        // Skip only provably empty recipe quads; keep crop seams and special reserves on the exact sampling path.
        public static bool MayHaveCanopy(Vector2 corner, float size)
        {
            var plan = MapLayout.Plan;
            float half = Ground.Size * 0.5f;
            if (!Ready || plan.world != null || (plan.kind == SectorKind.DeepForest && plan.reservedFeatures.Count > 0)
                || corner.x < -half || corner.y < -half || corner.x + size >= half || corner.y + size >= half) return true;
            int x0 = Mathf.FloorToInt(corner.x / GroveCell), x1 = Mathf.FloorToInt((corner.x + size) / GroveCell);
            int z0 = Mathf.FloorToInt(corner.y / GroveCell), z1 = Mathf.FloorToInt((corner.y + size) / GroveCell);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                if (groveCells.TryGetValue(((long)x << 32) ^ (uint)z, out var nearby))
                    foreach (var grove in nearby)
                        if (corner.x <= grove.center.x + grove.reach && corner.x + size >= grove.center.x - grove.reach
                            && corner.y <= grove.center.y + grove.reach && corner.y + size >= grove.center.y - grove.reach) return true;
            return false;
        }

        // HLOD uses the same glades, local reserves and species as its individual crowns.
        public static ForestPattern.Stand At(float x, float z)
        {
            Vector2 p = new Vector2(x, z) - Origin;
            return Contains(p) ? ForestPattern.At(x, z, Edge(p)) : ForestPattern.At(x, z);
        }

        // Only accepted margin stems need a gradient; cache their lean instead of resampling it during flight.
        public static Vector2 EdgeLean(Vector2 world, float cover, bool local)
        {
            if (cover >= 0.98f) return Vector2.zero;
            const float d = 3f;
            Vector2 direction = new Vector2(Margin(world - Vector2.right * d, local) - Margin(world + Vector2.right * d, local),
                Margin(world - Vector2.up * d, local) - Margin(world + Vector2.up * d, local));
            return direction.normalized * ((1f - cover) * 0.18f);
        }

        // Both the regional mask and the sector's protected clearings contribute to crown direction.
        static float Margin(Vector2 world, bool local) => Mathf.Min(ForestPattern.Glade(world.x, world.y),
            local ? Edge(world - Origin) : ForestPattern.Edge(world.x, world.y));

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
            else if (groveCells.TryGetValue(((long)Mathf.FloorToInt(p.x / GroveCell) << 32) ^ (uint)Mathf.FloorToInt(p.y / GroveCell), out var nearby))
            {
                float ripple = (ForestPattern.Noise(p.x + Origin.x, p.y + Origin.y, 19f, 95) - 0.5f) * 14f;
                foreach (var grove in nearby)
                {
                    Vector2 d = p - grove.center;
                    float u = (d.x * grove.cosine + d.y * grove.sine) / grove.stretch, v = (d.y * grove.cosine - d.x * grove.sine) * grove.stretch;
                    edge = Mathf.Max(edge, Mathf.Clamp01((grove.radius + ripple - Mathf.Sqrt(u * u + v * v)) / 25f));
                }
            }
            if (core)
            {
                var a = plan.reservedFeatures[0];
                Vector2 d = p - a.center;
                float inset = Mathf.Min(a.halfLength - Mathf.Abs(Vector2.Dot(d, a.along)), a.halfWidth - Mathf.Abs(Vector2.Dot(d, a.Across)));
                edge = Mathf.Max(edge, Mathf.Clamp01(inset / 25f));
            }
            if (edge <= 0f || !Open(p, core)) return 0f;
            // A cached distance field avoids repeated expanded parcel searches on every tree and surface sample.
            edge = Mathf.Min(edge, Clearance(p));
            foreach (var g in glades)
            {
                Vector2 d = p - g.center;
                float ripple = ForestPattern.Noise(p.x + Origin.x, p.y + Origin.y, 23f, 96) * 5f;
                float distance = ForestPattern.EllipseDistance(Vector2.Dot(d, g.along), Vector2.Dot(d, new Vector2(-g.along.y, g.along.x)), g.length, g.width);
                edge = Mathf.Min(edge, Mathf.Clamp01((distance - ripple) / g.rim));
            }
            return edge;
        }

        // Reuse the existing gameplay reservations when constructing the visual distance field.
        static bool Open(Vector2 p, bool core) => core ? CoreOpen(p) : MapLayout.IsWild(p, 0.5f);

        // A four-metre chamfer field lowers crowns before protected paths, sites and fields, with one megabyte of storage.
        static void PlanMargins()
        {
            marginSide = Mathf.CeilToInt(Ground.Size / MarginCell) + 1;
            clearance = new float[marginSide * marginSide];
            var plan = MapLayout.Plan;
            for (int z = 0; z < marginSide; z++) for (int x = 0; x < marginSide; x++)
            {
                Vector2 p = new Vector2(x * MarginCell - Ground.Size * 0.5f, z * MarginCell - Ground.Size * 0.5f);
                bool core = plan.kind == SectorKind.DeepForest && plan.reservedFeatures.Count > 0 && plan.reservedFeatures[0].Contains(p);
                bool border = plan.world != null && Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) >= MapLayout.Edge;
                clearance[z * marginSide + x] = border || Open(p, core) ? 24f : 0f;
            }
            for (int pass = 0; pass < 2; pass++)
            {
                int direction = pass == 0 ? 1 : -1;
                for (int k = pass == 0 ? 0 : clearance.Length - 1; k >= 0 && k < clearance.Length; k += direction)
                {
                    int x = k % marginSide, z = k / marginSide, px = x - direction, pz = z - direction;
                    float value = clearance[k];
                    if (px >= 0 && px < marginSide) value = Mathf.Min(value, clearance[k - direction] + MarginCell);
                    if (pz >= 0 && pz < marginSide)
                        for (int dx = -1; dx <= 1; dx++) if (x + dx >= 0 && x + dx < marginSide)
                            value = Mathf.Min(value, clearance[pz * marginSide + x + dx] + MarginCell * (dx == 0 ? 1f : 1.414214f));
                    clearance[k] = value;
                }
            }
        }

        // Subtract one grid cell conservatively so a protected boundary meets the floor before any hard exclusion.
        static float Clearance(Vector2 p)
        {
            if (clearance == null) return 1f;
            float x = (p.x + Ground.Size * 0.5f) / MarginCell, z = (p.y + Ground.Size * 0.5f) / MarginCell;
            int i = Mathf.Clamp(Mathf.FloorToInt(x), 0, marginSide - 2), j = Mathf.Clamp(Mathf.FloorToInt(z), 0, marginSide - 2);
            float u = Mathf.Clamp01(x - i), v = Mathf.Clamp01(z - j);
            int k = j * marginSide + i;
            float distance = Mathf.Lerp(Mathf.Lerp(clearance[k], clearance[k + 1], u),
                Mathf.Lerp(clearance[k + marginSide], clearance[k + marginSide + 1], u), v);
            return Mathf.Clamp01((distance - MarginCell) / 20f);
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

        // Cache orientation once, before workers sample rims; nearby tracks and streams explain elongated openings.
        static void PlanGlades()
        {
            foreach (var g in MapLayout.Plan.woodlandGlades) AddGlade(g.center, g.radius);
            foreach (var p in MapLayout.Plan.clearings) AddGlade(p, Scale.D(24f));
        }

        // Expand only the long axis so huts, log piles and their original reserved interiors remain clear.
        static void AddGlade(Vector2 p, float radius)
        {
            int i = Mathf.FloorToInt(p.x + Origin.x), j = Mathf.FloorToInt(p.y + Origin.y);
            float angle = ForestPattern.Unit(i, j, 97) * Mathf.PI;
            Vector2 along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float track = MapLayout.TrackField.Query(p.x, p.y, out _, out int segment);
            if (track < radius + 60f && segment >= 0) along = MapLayout.TrackField.Direction(segment);
            else if (MapLayout.HasRiver && MapLayout.RiverField.Query(p.x, p.y, out _, out segment) < radius + 80f && segment >= 0)
                along = MapLayout.RiverField.Direction(segment);
            glades.Add(new Glade { center = p, along = along, length = radius * (1.7f + ForestPattern.Unit(i, j, 98)),
                width = radius, rim = 10f + 15f * ForestPattern.Unit(i, j, 99) });
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
                float radius = Mathf.Min(Scale.D((isOuter ? 16f : 8f) + (float)rng.NextDouble() * (isOuter ? 14f : 9f)), Ground.Size * 0.075f);
                float stretch = 1f + (float)rng.NextDouble() * 0.5f, angle = (float)rng.NextDouble() * Mathf.PI;
                IndexGrove(new Grove { center = p, radius = radius, stretch = stretch, cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle),
                    reach = (radius + 7f) * stretch + 6f });
            }
        }

        // Cache immutable neighbours before workers start; include the 7 m ripple and the largest (<6 m) crown.
        static void IndexGrove(Grove grove)
        {
            int x0 = Mathf.FloorToInt((grove.center.x - grove.reach) / GroveCell), x1 = Mathf.FloorToInt((grove.center.x + grove.reach) / GroveCell);
            int z0 = Mathf.FloorToInt((grove.center.y - grove.reach) / GroveCell), z1 = Mathf.FloorToInt((grove.center.y + grove.reach) / GroveCell);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                long key = ((long)x << 32) ^ (uint)z;
                if (!groveCells.TryGetValue(key, out var nearby)) groveCells.Add(key, nearby = new List<Grove>());
                nearby.Add(grove);
            }
        }
    }
}

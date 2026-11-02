using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum Crop { Wheat, YoungCrop, Ploughed, Sunflower, MownHay }

    // A rectangular field, with its rows running along `along`.
    public struct FieldPlot
    {
        public Vector2 center, along;
        public float halfLength, halfWidth;
        public Crop crop;

        public Vector2 Across => new Vector2(-along.y, along.x);

        // True if p is inside the field grown by margin on every side.
        public bool Contains(Vector2 p, float margin)
        {
            Vector2 d = p - center;
            return Mathf.Abs(Vector2.Dot(d, along)) < halfLength + margin && Mathf.Abs(Vector2.Dot(d, Across)) < halfWidth + margin;
        }

        public Vector2 Point(float u, float v) => center + along * u + Across * v;
    }

    // The layout of the map being played: bases, defences, the river, roads, villages, water, farm fields and the
    // sector's special features. It comes from a SectorPlan (the hand-made Varrow Valley until another sector is
    // loaded with Use). Terrain, water, fields and scenery are all built from this, in the game and in the editor
    // preview. Positions are (x, z) in metres; the battlefield is about 180 m across, inside a ring of hills.
    public static class MapLayout
    {
        public const float BaseRadius = 18f, HqRadius = 17f, DefenceRadius = 6f;
        public const float RoadHalfWidth = 2f, RailHalfWidth = 2f;
        public const float VillageRadius = 22f;
        const float Edge = 225f; // keep fields and scenery inside the terrain
        const float Far = 100000f;

        static SectorPlan plan;
        static bool prepared;

        public static SectorPlan Plan => plan ?? (plan = SectorPlan.Varrow());

        // Switches to another sector; the next Prepare (and World.Build) lays it out.
        public static void Use(SectorPlan sector)
        {
            plan = sector;
            prepared = false;
        }

        public static float RiverHalfWidth => Plan.riverHalfWidth;
        public static Vector3 BasePosition => Plan.basePosition;
        public static Vector3 EnemyHqPosition => Plan.enemyHqPosition;
        public static Rect EnemyArea => Plan.enemyArea;
        public static IReadOnlyList<(EnemyType type, Vector3 position)> FixedDefences => Plan.defences;
        public static IReadOnlyList<Vector2> Villages => Plan.villages;
        public static bool HasRiver => Plan.river != null;
        public static bool HasDam => Plan.hasDam;
        public static Vector2 DamPoint => Plan.damPoint;
        public static float LakeLevel => Plan.lakeLevel;
        public static float DamHalfSpan => Plan.damHalfSpan;
        public static IReadOnlyList<Vector2> Bridges => Plan.bridges;
        // The main bridge, or a point far off the map when no road crosses a river.
        public static Vector2 BridgePoint => Plan.bridges.Count > 0 ? Plan.bridges[0] : new Vector2(Far, Far);

        // Downstream along the river at the dam, and across the dam.
        public static Vector2 DamDown { get; private set; } = Vector2.right;
        public static Vector2 DamAcross => new Vector2(-DamDown.y, DamDown.x);

        public static List<Vector2> RiverLine { get; private set; }
        public static List<List<Vector2>> RoadLines { get; private set; }
        public static List<List<Vector2>> RailLines { get; private set; }
        public static PathField RiverField { get; private set; }
        public static PathField RoadField { get; private set; }
        public static PathField RailField { get; private set; }
        public static readonly List<Ground.FlatSite> FlatSites = new List<Ground.FlatSite>();
        public static readonly List<FieldPlot> Fields = new List<FieldPlot>();

        // Builds the lines, distance fields and field plan for the current sector. Only runs once per sector.
        public static void Prepare()
        {
            if (prepared) return;
            prepared = true;
            var p = Plan;

            FlatSites.Clear();
            FlatSites.Add(new Ground.FlatSite(p.basePosition, BaseRadius, true));
            FlatSites.Add(new Ground.FlatSite(p.enemyHqPosition, HqRadius, true));
            foreach (var d in p.defences) FlatSites.Add(new Ground.FlatSite(d.position, DefenceRadius, true));
            foreach (var s in p.sites)
                if (s.flat > 0f) FlatSites.Add(new Ground.FlatSite(new Vector3(s.position.x, 0f, s.position.y), s.flat, false));
            if (p.Has(SectorFeature.Airstrip))
                for (float t = -p.airstripLength / 2f; t <= p.airstripLength / 2f + 0.1f; t += 14f)
                {
                    Vector2 c = p.airstripCenter + p.airstripAlong * t;
                    FlatSites.Add(new Ground.FlatSite(new Vector3(c.x, 0f, c.y), 19f, false));
                }

            RiverLine = p.river != null ? Smooth(p.river, 2f) : new List<Vector2>();
            DamDown = p.hasDam && RiverLine.Count > 1 ? (RiverLine[Mathf.Min(4, RiverLine.Count - 1)] - RiverLine[0]).normalized : Vector2.right;
            RoadLines = new List<List<Vector2>>();
            foreach (var road in p.roads) RoadLines.Add(Smooth(road, 2f));
            RailLines = new List<List<Vector2>>();
            foreach (var rail in p.railways) RailLines.Add(Smooth(rail, 2f));
            // The river field reaches far out so the hills can open into a valley around the river.
            RiverField = new PathField(p.river != null ? new[] { RiverLine } : new List<Vector2>[0], 80f, Ground.Size);
            RoadField = new PathField(RoadLines, 12f, Ground.Size);
            RailField = new PathField(RailLines, 12f, Ground.Size);

            PlanFields(new System.Random(p.fieldSeed));
        }

        // ---- Questions the builders ask ----

        public static bool NearFlatSite(Vector2 p, float margin)
        {
            foreach (var s in FlatSites)
                if (Vector2.Distance(p, new Vector2(s.center.x, s.center.z)) < s.radius + margin) return true;
            return false;
        }

        public static bool NearVillage(Vector2 p, float margin)
        {
            foreach (var v in Plan.villages)
                if (Vector2.Distance(p, v) < VillageRadius + margin) return true;
            return false;
        }

        public static bool InField(Vector2 p, float margin)
        {
            foreach (var f in Fields)
                if (f.Contains(p, margin)) return true;
            foreach (var f in Plan.orchards)
                if (f.Contains(p, margin)) return true;
            return false;
        }

        // A point relative to the dam: x metres downstream (negative is up the lake), y metres across. Far away
        // from everything when there's no dam.
        public static Vector2 DamLocal(float x, float z)
        {
            if (!Plan.hasDam) return new Vector2(Far, Far);
            var d = new Vector2(x - Plan.damPoint.x, z - Plan.damPoint.y);
            return new Vector2(Vector2.Dot(d, DamDown), Vector2.Dot(d, DamAcross));
        }

        // Signed distance to the nearest shore of any standing water (the reservoir, lakes and pools) in metres:
        // negative on the water, positive on land.
        public static float LakeDistance(float x, float z) => Mathf.Min(ReservoirDistance(x, z), PondDistance(x, z, out _));

        // The same for the reservoir alone. The lobes' wobbly shores give coves and headlands; the dam cuts the
        // lake off.
        public static float ReservoirDistance(float x, float z)
        {
            if (!Plan.hasDam) return Far;
            Vector2 local = DamLocal(x, z);
            float s = float.MaxValue;
            foreach (var lobe in Plan.lakeLobes)
            {
                float a = lobe.angle * Mathf.Deg2Rad;
                var axis = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 d = local - lobe.center;
                float du = Vector2.Dot(d, axis) / lobe.halfLength, dv = (axis.x * d.y - axis.y * d.x) / lobe.halfWidth;
                float angle = Mathf.Atan2(dv, du);
                float wobble = 1f + 0.09f * Mathf.Sin(3f * angle + lobe.seed) + 0.06f * Mathf.Sin(5f * angle + 2.1f * lobe.seed)
                             + 0.035f * Mathf.Sin(11f * angle + 4f + lobe.seed);
                float lobeDistance = (Mathf.Sqrt(du * du + dv * dv) - wobble) * lobe.halfWidth;
                s = s == float.MaxValue ? lobeDistance : SmoothMin(s, lobeDistance, 14f);
            }
            s = Mathf.Max(s, local.x + 0.5f);
            foreach (var island in Plan.islands) s = Mathf.Max(s, island.radius - Vector2.Distance(local, island.center));
            return s;
        }

        // Signed distance to the nearest pool or natural lake, and which one it is (-1 for none).
        public static float PondDistance(float x, float z, out int index)
        {
            float best = Far;
            index = -1;
            var ponds = Plan.ponds;
            for (int i = 0; i < ponds.Count; i++)
            {
                var pond = ponds[i];
                Vector2 d = new Vector2(x, z) - pond.center;
                if (d.sqrMagnitude > (pond.radius * 1.4f + 80f) * (pond.radius * 1.4f + 80f)) continue;
                float angle = Mathf.Atan2(d.y, d.x);
                float wobble = 1f + 0.12f * Mathf.Sin(3f * angle + pond.seed) + 0.07f * Mathf.Sin(5f * angle + 2.3f * pond.seed)
                             + 0.04f * Mathf.Sin(9f * angle + 1.7f * pond.seed);
                float s = d.magnitude - pond.radius * wobble;
                if (s < best)
                {
                    best = s;
                    index = i;
                }
            }
            return best;
        }

        // The surface of the standing water at (x, z), or -infinity on dry land.
        public static float WaterLevelAt(float x, float z)
        {
            if (ReservoirDistance(x, z) < 0f) return Plan.lakeLevel;
            if (PondDistance(x, z, out int i) < 0f) return Plan.ponds[i].level;
            return float.NegativeInfinity;
        }

        static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        public static int IslandCount => Plan.islands.Length;
        public static Vector2 IslandCenter(int i) => Plan.damPoint + DamDown * Plan.islands[i].center.x + DamAcross * Plan.islands[i].center.y;
        public static float IslandRadius(int i) => Plan.islands[i].radius;

        // The gorge below the dam, which stays clear so the spillway can be seen.
        public static bool InGorge(Vector2 p, float margin)
        {
            Vector2 local = DamLocal(p.x, p.y);
            return local.x > -2f && local.x < 48f + margin && Mathf.Abs(local.y) < 22f + margin;
        }

        // The dam and its abutments, with a margin.
        public static bool NearDam(Vector2 p, float margin)
        {
            Vector2 local = DamLocal(p.x, p.y);
            return local.x > -12f - margin && local.x < 14f + margin && Mathf.Abs(local.y) < 52f + margin;
        }

        // The quarry pit, the airstrip and the railway, which trees and fields keep off.
        public static bool OnWorks(Vector2 p, float margin)
        {
            var plan = Plan;
            if (plan.Has(SectorFeature.Quarry) && Vector2.Distance(p, plan.quarry) < plan.quarryRadius + 6f + margin) return true;
            if (plan.Has(SectorFeature.Airstrip))
            {
                Vector2 d = p - plan.airstripCenter;
                Vector2 across = new Vector2(-plan.airstripAlong.y, plan.airstripAlong.x);
                if (Mathf.Abs(Vector2.Dot(d, plan.airstripAlong)) < plan.airstripLength / 2f + 20f + margin
                    && Mathf.Abs(Vector2.Dot(d, across)) < 26f + margin) return true;
            }
            return RailLines != null && RailLines.Count > 0 && RailField.Distance(p.x, p.y) < RailHalfWidth + 2.5f + margin;
        }

        // Open ground for trees and rocks: not on a road, in the river or lake, on a field, in a village or a base.
        public static bool IsWild(Vector2 p, float margin)
        {
            if (Mathf.Abs(p.x) > Edge || Mathf.Abs(p.y) > Edge) return false;
            if (LakeDistance(p.x, p.y) < 1.5f + margin || NearDam(p, margin) || InGorge(p, margin)) return false;
            if (RoadField.Distance(p.x, p.y) < RoadHalfWidth + 1.5f + margin) return false;
            if (HasRiver && RiverField.Distance(p.x, p.y) < RiverHalfWidth + 2.5f + margin) return false;
            if (OnWorks(p, margin)) return false;
            return !NearFlatSite(p, 3f + margin) && !NearVillage(p, margin) && !InField(p, 1f + margin);
        }

        // ---- Fields ----

        // Lines fields up along both sides of every road, then fills the open land between them. How much is
        // farmed follows the sector's fields share (0.7 in the Varrow Valley).
        static void PlanFields(System.Random rng)
        {
            Fields.Clear();
            float share = Plan.fields;
            if (share <= 0f) return;
            double roadsideSkip = Mathf.Clamp01(0.15f + (0.7f - share) * 1.2f), fillSkip = Mathf.Clamp01(1f - share);
            foreach (var line in RoadLines)
            {
                float next = Range(rng, 8f, 20f), walked = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    walked += Vector2.Distance(line[i - 1], line[i]);
                    if (walked < next) continue;
                    next = walked + Range(rng, 26f, 40f);
                    Vector2 along = (line[i] - line[i - 1]).normalized;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        if (rng.NextDouble() < roadsideSkip) continue;
                        float halfLength = Range(rng, 14f, 26f), halfWidth = Range(rng, 12f, 24f);
                        Vector2 dir = Rotate(along, Range(rng, -2f, 2f) * Mathf.Deg2Rad);
                        Vector2 normal = new Vector2(-dir.y, dir.x) * side;
                        // Rows run along the road on some fields and away from it on others.
                        bool rowsAway = rng.NextDouble() < 0.4;
                        float depth = rowsAway ? halfLength : halfWidth; // the field's half-extent away from the road
                        var crop = PickCrop(rng);
                        // Set back from the road by a verge, further if it won't fit (the road may curve).
                        for (float setback = 4f; setback <= 13f; setback += 3f)
                        {
                            var plot = new FieldPlot
                            {
                                center = line[i] + normal * (RoadHalfWidth + setback + depth),
                                along = rowsAway ? normal : dir,
                                halfLength = halfLength,
                                halfWidth = halfWidth,
                                crop = crop,
                            };
                            if (!Fits(plot)) continue;
                            Fields.Add(plot);
                            break;
                        }
                    }
                }
            }

            // Then fill the open valley floor in between, biggest fields first, lined up with the nearest road
            // (or, far from roads, with a slowly turning grid). Some cells are left as meadow, and woods grow
            // wherever fields don't fit, including the hills round the edge.
            for (float gz = -150f; gz <= 150f; gz += 17f)
            {
                for (float gx = -150f; gx <= 150f; gx += 17f)
                {
                    if (rng.NextDouble() < fillSkip) continue;
                    var c = new Vector2(gx + Range(rng, -5f, 5f), gz + Range(rng, -5f, 5f));
                    RoadField.Query(c.x, c.y, out _, out int s);
                    Vector2 grid = s >= 0 ? RoadField.Direction(s)
                        : Rotate(Vector2.up, (Mathf.PerlinNoise(c.x * 0.004f + 3f, c.y * 0.004f + 8f) - 0.5f) * 2.4f);
                    Vector2 dir = Rotate(grid, (rng.NextDouble() < 0.5 ? 0f : 90f + Range(rng, -3f, 3f)) * Mathf.Deg2Rad);
                    var crop = PickCrop(rng);
                    foreach (float size in new[] { 1f, 0.75f, 0.55f })
                    {
                        var plot = new FieldPlot
                        {
                            center = c, along = dir, crop = crop,
                            halfLength = Range(rng, 18f, 26f) * size, halfWidth = Range(rng, 14f, 22f) * size,
                        };
                        if (!Fits(plot)) continue;
                        Fields.Add(plot);
                        break;
                    }
                }
            }

            // Finally squeeze smaller fields into the gaps left between roads, at random spots.
            int attempts = Mathf.RoundToInt(500 * Mathf.Min(1f, share / 0.7f));
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var c = new Vector2(Range(rng, -130f, 130f), Range(rng, -130f, 130f));
                RoadField.Query(c.x, c.y, out _, out int s);
                Vector2 grid = s >= 0 ? RoadField.Direction(s)
                    : Rotate(Vector2.up, (Mathf.PerlinNoise(c.x * 0.004f + 3f, c.y * 0.004f + 8f) - 0.5f) * 2.4f);
                Vector2 dir = Rotate(grid, (rng.NextDouble() < 0.5 ? 0f : 90f) * Mathf.Deg2Rad);
                var plot = new FieldPlot
                {
                    center = c, along = dir, crop = PickCrop(rng),
                    halfLength = Range(rng, 7f, 14f), halfWidth = Range(rng, 6f, 11f),
                };
                if (Fits(plot)) Fields.Add(plot);
            }
        }

        // Whether a field (or orchard) fits here: clear of roads, water, sites, villages and other plots.
        public static bool Fits(FieldPlot plot)
        {
            foreach (var other in Fields)
                if (Overlaps(plot, other, 3f)) return false;
            foreach (var other in Plan.orchards)
                if (Overlaps(plot, other, 3f)) return false;
            // Sample the plot every few metres, corners included.
            for (float u = -plot.halfLength; u <= plot.halfLength + 0.01f; u += plot.halfLength / 5f)
            {
                for (float v = -plot.halfWidth; v <= plot.halfWidth + 0.01f; v += plot.halfWidth / 4f)
                {
                    Vector2 p = plot.Point(u, v);
                    if (Mathf.Abs(p.x) > Edge - 15f || Mathf.Abs(p.y) > Edge - 15f) return false;
                    if (RoadField.Distance(p.x, p.y) < RoadHalfWidth + 2f) return false;
                    if (HasRiver && RiverField.Distance(p.x, p.y) < RiverHalfWidth + 6f) return false;
                    if (LakeDistance(p.x, p.y) < 30f || NearDam(p, 15f) || OnWorks(p, 4f)) return false;
                    if (NearFlatSite(p, 4f) || NearVillage(p, 0f)) return false;
                }
            }
            return true;
        }

        // Separating-axis test between two rectangles, grown by margin.
        static bool Overlaps(FieldPlot a, FieldPlot b, float margin)
        {
            foreach (var axis in new[] { a.along, a.Across, b.along, b.Across })
            {
                float ra = Extent(a, axis) + margin, rb = Extent(b, axis);
                if (Mathf.Abs(Vector2.Dot(b.center - a.center, axis)) > ra + rb) return false;
            }
            return true;
        }

        static float Extent(FieldPlot f, Vector2 axis) =>
            Mathf.Abs(Vector2.Dot(f.along, axis)) * f.halfLength + Mathf.Abs(Vector2.Dot(f.Across, axis)) * f.halfWidth;

        static Crop PickCrop(System.Random rng)
        {
            double r = rng.NextDouble();
            if (r < 0.30) return Crop.Wheat;
            if (r < 0.55) return Crop.YoungCrop;
            if (r < 0.75) return Crop.Ploughed;
            if (r < 0.85) return Crop.Sunflower;
            return Crop.MownHay;
        }

        // ---- Helpers ----

        // A Catmull-Rom curve through the points, sampled about every `spacing` metres.
        public static List<Vector2> Smooth(Vector2[] pts, float spacing)
        {
            var result = new List<Vector2>();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Length - 1)];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / spacing));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                    result.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                                       + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            result.Add(pts[pts.Length - 1]);
            return result;
        }

        public static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        public static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}

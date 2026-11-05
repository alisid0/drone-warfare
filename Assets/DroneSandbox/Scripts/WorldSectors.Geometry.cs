using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Crop world vectors without moving them, retaining source parameters for water-width/level interpolation.
    public static partial class WorldSectors
    {
        // A clipped vertex still knows which original segment and interpolation fraction produced it.
        struct CutPoint
        {
            public Vector2 point;
            public int segment;
            public float t;
        }

        // Project a point onto a segment, sharing the same bounded parameter throughout the adapter.
        internal static float Projection(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            return ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        }

        // Liang-Barsky clipping returns parameters, so interpolated elevations remain on the original river.
        static bool Clip(Vector2 a, Vector2 b, float half, out float lo, out float hi)
        {
            lo = 0f; hi = 1f;
            Vector2 d = b - a;
            return ClipSide(-d.x, a.x + half, ref lo, ref hi) && ClipSide(d.x, half - a.x, ref lo, ref hi)
                && ClipSide(-d.y, a.y + half, ref lo, ref hi) && ClipSide(d.y, half - a.y, ref lo, ref hi);
        }

        // Intersect one slab with the surviving parameter interval, including parallel boundary segments.
        static bool ClipSide(float p, float q, ref float lo, ref float hi)
        {
            if (Mathf.Abs(p) < 0.000001f) return q >= 0f;
            float t = q / p;
            if (p < 0f) lo = Mathf.Max(lo, t); else hi = Mathf.Min(hi, t);
            return lo <= hi;
        }

        // Disconnected visits to the crop become separate reaches. Boundary guard vertices hold the old smoother
        // on the world segment near crossings; no curve is allowed to invent a neighbouring reach.
        static List<List<CutPoint>> Cut(IList<Vector2> source, Vector2 center)
        {
            var result = new List<List<CutPoint>>();
            List<CutPoint> current = null;
            for (int i = 1; i < source.Count; i++)
            {
                Vector2 a = source[i - 1] - center, b = source[i] - center;
                if (!Clip(a, b, Half + Margin, out float lo, out float hi) || hi - lo < 0.000001f) { current = null; continue; }
                Vector2 first = Vector2.Lerp(a, b, lo);
                if (current == null || (current[current.Count - 1].point - first).sqrMagnitude > 0.0001f)
                { current = new List<CutPoint>(); result.Add(current); }
                var times = new List<float> { lo, hi };
                if (Clip(a, b, Half, out float enter, out float leave))
                {
                    AddTime(times, enter, lo, hi); AddTime(times, leave, lo, hi);
                    float guard = 8f / Mathf.Max(0.01f, Vector2.Distance(a, b));
                    if (enter > 0f) { AddTime(times, enter - guard, lo, hi); AddTime(times, enter + guard, lo, hi); }
                    if (leave < 1f) { AddTime(times, leave - guard, lo, hi); AddTime(times, leave + guard, lo, hi); }
                }
                times.Sort();
                foreach (float t in times)
                {
                    Vector2 q = Vector2.Lerp(a, b, t);
                    if (Mathf.Abs(Mathf.Abs(q.x) - Half) < 0.002f) q.x = Mathf.Sign(q.x) * Half;
                    if (Mathf.Abs(Mathf.Abs(q.y) - Half) < 0.002f) q.y = Mathf.Sign(q.y) * Half;
                    if (current.Count > 0 && (current[current.Count - 1].point - q).sqrMagnitude < 0.000001f) continue;
                    current.Add(new CutPoint { point = q, segment = i - 1, t = t });
                }
                if (hi < 1f) current = null;
            }
            return result;
        }

        // Guard vertices are inserted only within the retained segment, never extended beyond the source.
        static void AddTime(List<float> times, float t, float lo, float hi)
        {
            if (t > lo && t < hi) times.Add(t);
        }

        // Copy river attributes and keep the longest in-square reach in the single-river compatibility field.
        static void Rivers(SectorPlan plan)
        {
            var data = plan.world;
            float longest = -1f;
            for (int r = 0; r < WorldPlan.Rivers.Count; r++)
            {
                var source = WorldPlan.Rivers[r];
                foreach (var cut in Cut(source.points, data.center))
                {
                    if (cut.Count < 2) continue;
                    var reach = new SectorRiver { name = source.name, source = r, points = new Vector2[cut.Count],
                        halfWidths = new float[cut.Count], waterLevels = new float[cut.Count] };
                    float length = 0f;
                    for (int i = 0; i < cut.Count; i++)
                    {
                        var c = cut[i]; int k = c.segment;
                        reach.points[i] = c.point;
                        reach.halfWidths[i] = Mathf.Lerp(source.halfWidths[k], source.halfWidths[k + 1], c.t);
                        reach.waterLevels[i] = Mathf.Lerp(source.waterLevels[k], source.waterLevels[k + 1], c.t);
                        if (i > 0 && Clip(reach.points[i - 1], c.point, Half, out float lo, out float hi))
                            length += Vector2.Distance(reach.points[i - 1], c.point) * (hi - lo);
                    }
                    data.rivers.Add(reach);
                    if (length <= longest) continue;
                    longest = length; data.primaryRiver = data.rivers.Count - 1;
                    plan.river = reach.points; plan.riverName = reach.name;
                    plan.riverHalfWidth = 0f;
                    foreach (float width in reach.halfWidths) plan.riverHalfWidth = Mathf.Max(plan.riverHalfWidth, width);
                }
            }
            // Old exclusion callers ask one width for the entire field. Use the largest retained width until
            // they adopt the per-point width query, so a wider secondary reach is never treated as dry ground.
            foreach (var reach in data.rivers)
                foreach (float width in reach.halfWidths) plan.riverHalfWidth = Mathf.Max(plan.riverHalfWidth, width);
        }

        // Split roads at the terrain edge: both Smooth and Wander leave endpoints fixed, including the margin
        // continuation. Internal junctions are still the world's original coincident vertices.
        static void Roads(SectorPlan plan)
        {
            for (int r = 0; r < WorldPlan.Roads.Count; r++)
            {
                var source = WorldPlan.Roads[r];
                foreach (var cut in Cut(source.points, plan.world.center))
                {
                    var part = new List<Vector2>();
                    foreach (var c in cut)
                    {
                        part.Add(c.point);
                        if (part.Count > 1 && AtEdge(c.point))
                        { AddRoad(plan, part, source.kind, r); part = new List<Vector2> { c.point }; }
                    }
                    if (part.Count > 1) AddRoad(plan, part, source.kind, r);
                }
            }
            foreach (var source in WorldPlan.Railways)
                foreach (var cut in Cut(source.points, plan.world.center))
                {
                    var points = new List<Vector2>();
                    foreach (var c in cut) points.Add(c.point);
                    if (points.Count > 1) plan.railways.Add(points.ToArray());
                }
        }

        // Each road class remains attached to its copied fragment; later mission access tracks are kept separate.
        static void AddRoad(SectorPlan plan, List<Vector2> points, RoadClass kind, int source)
        {
            ProtectEnd(points);
            points.Reverse(); ProtectEnd(points); points.Reverse();
            var line = points.ToArray();
            plan.world.roads.Add(new SectorRoad { kind = kind, source = source, points = line });
            plan.roads.Add(line);
        }

        // The old uniform Catmull-Rom smoother overshoots a tiny final segment after a long one. Insert a
        // collinear neighbour of matching length without removing or moving any world vertex or crossing.
        static void ProtectEnd(List<Vector2> points)
        {
            if (points.Count < 3) return;
            int n = points.Count;
            float last = Vector2.Distance(points[n - 2], points[n - 1]);
            float before = Vector2.Distance(points[n - 3], points[n - 2]);
            if (before > last * 2f && last > 0.001f)
                points.Insert(n - 2, Vector2.Lerp(points[n - 3], points[n - 2], 1f - last / before));
        }

        // Float rounding of a translated world segment can be a fraction of a millimetre at the boundary.
        static bool AtEdge(Vector2 p) => Mathf.Abs(Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) - Half) < 0.002f;

        // Standing water keeps its complete shore, including enclosing lakes and concave sections outside the crop.
        static void Water(SectorPlan plan)
        {
            var data = plan.world;
            foreach (var source in WorldPlan.Lakes)
            {
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                foreach (var p in source.shore)
                { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.y); z1 = Mathf.Max(z1, p.y); }
                float reach = Half + Margin;
                if (x1 < data.center.x - reach || x0 > data.center.x + reach || z1 < data.center.y - reach || z0 > data.center.y + reach) continue;
                var lake = new WorldLake { name = source.name, reservoir = source.reservoir, level = source.level, outlet = source.outlet - data.center };
                foreach (var p in source.shore) lake.shore.Add(p - data.center);
                data.lakes.Add(lake);
            }
            foreach (var source in WorldPlan.Dams)
            {
                Vector2 a = source.a - data.center, b = source.b - data.center;
                if (!Clip(a, b, Half + Margin, out _, out _)) continue;
                data.dams.Add(new WorldDam { name = source.name, a = a, b = b, crest = source.crest, tailwater = source.tailwater });
                // The legacy dam assumes river[0] is its outlet and a fixed model span. Exact dam geometry belongs
                // to data.dams; setting hasDam here would carve a false gorge through a cropped upstream reach.
                plan.damPoint = (a + b) / 2f; plan.damHalfSpan = Vector2.Distance(a, b) / 2f;
                foreach (var lake in data.lakes) if (lake.reservoir) plan.lakeLevel = lake.level;
            }
            foreach (var source in WorldPlan.Bridges)
            {
                Vector2 p = source.position - data.center;
                if (!Inside(p, Half + Margin)) continue;
                data.bridges.Add(new WorldBridge { position = p, a = source.a - data.center, b = source.b - data.center,
                    deckLevel = source.deckLevel, railway = source.railway, route = source.route, river = source.river });
                if (!source.railway && Inside(p, Half)) plan.bridges.Add(p);
            }
            // Inscribed, deliberately conservative compatibility pools. The exact polygon/level queries above are
            // the water inputs for integration; old wobbly Pond shapes cannot reproduce arbitrary world shores.
            for (float z = -900f; z <= 900f; z += 150f)
                for (float x = -900f; x <= 900f; x += 150f)
                {
                    var q = new Vector2(x, z);
                    float d = data.LakeDistance(q, out float level);
                    if (d > -65f) continue;
                    float radius = Mathf.Min(145f, -d / 2.1f);
                    bool covered = false;
                    foreach (var pond in plan.ponds) if (Vector2.Distance(q, pond.center) < pond.radius) { covered = true; break; }
                    if (!covered) plan.ponds.Add(new Pond { center = q, radius = radius, seed = 0f, level = level });
                }
        }

        // Local square inclusion is shared by geometry, placement and the hotspot occupancy check.
        internal static bool Inside(Vector2 p, float half) => Mathf.Abs(p.x) <= half && Mathf.Abs(p.y) <= half;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Clips shared world parcels to the sector and its exclusions while retaining each parcel's crop and rows.
    internal static class FarFieldSector
    {
        // Only districts touching the 2 km crop are requested. Legacy sectors continue through FieldParcels.Plan.
        public static float Plan(Func<Vector2, bool> blocked)
        {
            FarFieldSurface.Clear();
            var source = MapLayout.Plan.world;
            var roads = new List<FarDetailData.Segment>();
            foreach (var road in WorldPlan.Roads) for (int k = 1; k < road.points.Count; k++)
                roads.Add(new FarDetailData.Segment { ax = road.points[k - 1].x, az = road.points[k - 1].y, bx = road.points[k].x, bz = road.points[k].y, kind = (int)road.kind });
            var plan = new FarFieldPlan(source.seed, roads.ToArray());
            const float half = Ground.Size * .5f;
            int x0 = (int)Math.Floor((source.center.x - half) / 700f) - 1, x1 = (int)Math.Floor((source.center.x + half) / 700f) + 1;
            int z0 = (int)Math.Floor((source.center.y - half) / 700f) - 1, z1 = (int)Math.Floor((source.center.y + half) / 700f) + 1;
            float available = 0f, accepted = 0f;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                var district = plan.Get(x, z);
                foreach (var parcel in district.parcels)
                {
                    var plot = parcel.Plot(district, source.center);
                    var polygon = Corners(plot);
                    polygon = FarFieldPlan.Clip(FarFieldPlan.Clip(FarFieldPlan.Clip(FarFieldPlan.Clip(polygon, -1, 0, half), 1, 0, half), 0, -1, half), 0, 1, half);
                    if (polygon.Count < 3) continue;
                    foreach (var fragment in Trapezoids(polygon, plot))
                    {
                        available += 4f * fragment.halfLength * fragment.halfWidth;
                        Cut(fragment, plot, blocked, 0);
                    }
                }
            }
            foreach (var plot in MapLayout.Fields) accepted += 4f * plot.halfLength * plot.halfWidth;
            return available > 0f ? accepted / available : 0f;
        }

        // Four actual corners, before any sector crop; world FieldPlot.Point intentionally has no local warp.
        static List<Vector2> Corners(FieldPlot f) => new List<Vector2> {
            f.Point(-f.halfLength, -f.halfWidth), f.Point(f.halfLength, -f.halfWidth),
            f.Point(f.halfLength, f.halfWidth), f.Point(-f.halfLength, f.halfWidth) };

        // A convex polygon splits exactly into trapezoids at its row-coordinate vertices; no new border setback.
        static List<FieldPlot> Trapezoids(List<Vector2> polygon, FieldPlot source)
        {
            var local = new List<Vector2>(); var cuts = new List<float>(); var result = new List<FieldPlot>();
            foreach (var point in polygon)
            {
                Vector2 p = point - source.center;
                float u = Vector2.Dot(p, source.along), v = Vector2.Dot(p, source.Across);
                local.Add(new Vector2(u, v)); cuts.Add(u);
            }
            cuts.Sort();
            for (int k = 1; k < cuts.Count; k++)
            {
                float u0 = cuts[k - 1], u1 = cuts[k];
                if (u1 - u0 < .01f) continue;
                Span(local, u0, out float a0, out float a1); Span(local, u1, out float b0, out float b1);
                float width = a1 - a0 + b1 - b0;
                if (width < .01f) continue;
                var f = source;
                f.center += source.along * ((u0 + u1) * .5f) + source.Across * ((a0 + a1 + b0 + b1) * .25f);
                f.halfLength = (u1 - u0) * .5f; f.halfWidth = width * .25f;
                f.taper = (b1 - b0 - a1 + a0) / width; f.shear = (b0 + b1 - a0 - a1) / (2f * (u1 - u0));
                result.Add(f);
            }
            return result;
        }

        // Evaluate both polygon sides at a clipping cut, including a triangular sector-corner fragment.
        static void Span(List<Vector2> polygon, float u, out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                if (u < Math.Min(a.x, b.x) - .002f || u > Math.Max(a.x, b.x) + .002f) continue;
                if (Math.Abs(b.x - a.x) < .0001f) { lo = Math.Min(lo, Math.Min(a.y, b.y)); hi = Math.Max(hi, Math.Max(a.y, b.y)); }
                else { float v = a.y + (b.y - a.y) * Mathf.Clamp01((u - a.x) / (b.x - a.x)); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
            }
        }

        // Obstacle subdivisions keep the parent's crop. The existing 2 m exclusion map includes works and reserved land.
        static void Cut(FieldPlot f, FieldPlot original, Func<Vector2, bool> blocked, int depth)
        {
            int nu = Math.Max(1, (int)Math.Ceiling(2f * f.halfLength / 2f)), nv = Math.Max(1, (int)Math.Ceiling(2f * f.halfWidth * (1f + Math.Abs(f.taper)) / 2f));
            int clear = 0, total = (nu + 1) * (nv + 1);
            var source = MapLayout.Plan.world;
            for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++)
            {
                Vector2 p = f.Point(f.halfLength * (2f * i / nu - 1f), f.halfWidth * (2f * j / nv - 1f));
                if (!blocked(p) && source.CoverAt(p.x, p.y) == Cover.Farmland) clear++;
            }
            if (clear == 0) return;
            if (clear == total) { MapLayout.Fields.Add(f); FarFieldSurface.Register(f, original); AddEdges(f, original); return; }
            if (depth >= 10 || f.halfLength * f.halfWidth < FarFieldPlan.MinimumArea * .5f) return;
            bool along = f.halfLength > f.halfWidth;
            var a = Slice(f, along, false); var b = Slice(f, along, true);
            if (Usable(a)) Cut(a, original, blocked, depth + 1);
            if (Usable(b)) Cut(b, original, blocked, depth + 1);
        }

        // Remnants created by an obstacle stay meadow below the same workability limits as the world parcels.
        static bool Usable(FieldPlot f) => 4f * f.halfLength * f.halfWidth >= FarFieldPlan.MinimumArea
            && f.halfLength >= 9f && f.halfWidth * (1f - Math.Abs(f.taper)) >= 12f;

        // Exact halves of the same trapezoid retain the outside boundary, row axis and crop.
        static FieldPlot Slice(FieldPlot f, bool along, bool upper)
        {
            float sign = upper ? 1f : -1f;
            if (along)
            {
                float u = f.halfLength * .5f * sign, slope = f.halfWidth * f.taper / f.halfLength;
                f.center += (f.along + f.Across * f.shear) * u;
                f.halfWidth += slope * u; f.halfLength *= .5f; f.taper = slope * f.halfLength / f.halfWidth;
            }
            else
            {
                float v = f.halfWidth * .5f * sign;
                f.center += f.Across * v; f.shear += v * f.taper / f.halfLength; f.halfWidth *= .5f;
            }
            return f;
        }

        // Boundary props stop at real margins; artificial sector cuts must never acquire a hedge across the seam.
        static void AddEdges(FieldPlot f, FieldPlot original)
        {
            var corners = Corners(f);
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = corners[i], b = corners[(i + 1) % 4];
                if (Vector2.Distance(a, b) < 18f) continue;
                float half = Ground.Size * .5f;
                if ((Math.Abs(a.x) > half - .02f && Math.Abs(b.x) > half - .02f) || (Math.Abs(a.y) > half - .02f && Math.Abs(b.y) > half - .02f)) continue;
                if (i != 0 && i != 2) continue;
                Vector2 u = a - original.center, v = b - original.center;
                float au = Vector2.Dot(u, original.along), bu = Vector2.Dot(v, original.along);
                float av = Vector2.Dot(u, original.Across) - original.shear * au, bv = Vector2.Dot(v, original.Across) - original.shear * bu;
                if (Math.Abs(Math.Abs(av) - original.HalfWidthAt(au)) > .02f || Math.Abs(Math.Abs(bv) - original.HalfWidthAt(bu)) > .02f) continue;
                MapLayout.Boundaries.Add(new FieldBoundary { a = a, b = b, first = MapLayout.Fields.Count - 1, second = -1, kind = FieldBoundaryKind.Hedge });
            }
        }
    }
}

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
            var plan = MapLayout.DetailPlan.fields;
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
                    var pieces = new List<FieldPlot>();
                    foreach (var fragment in Trapezoids(polygon, plot))
                    {
                        available += 4f * fragment.halfLength * fragment.halfWidth;
                        Cut(fragment, blocked, pieces, 0);
                    }
                    Merge(pieces);
                    foreach (var piece in pieces)
                    {
                        MapLayout.Fields.Add(piece); FarFieldSurface.Register(piece, plot);
                        if (parcel.hedge) AddEdges(piece, plot);
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
        static void Cut(FieldPlot f, Func<Vector2, bool> blocked, List<FieldPlot> pieces, int depth)
        {
            int nu = Math.Max(1, (int)Math.Ceiling(2f * f.halfLength / 2f)), nv = Math.Max(1, (int)Math.Ceiling(2f * f.halfWidth * (1f + Math.Abs(f.taper)) / 2f));
            int clear = 0, total = (nu + 1) * (nv + 1);
            var low = new int[nu + 1]; var high = new int[nu + 1]; var counts = new int[nu + 1];
            for(int i=0;i<=nu;i++) { low[i]=nv+1; high[i]=-1; }
            for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++)
            {
                Vector2 p = f.Point(f.halfLength * (2f * i / nu - 1f), f.halfWidth * (2f * j / nv - 1f));
                if (!blocked(p)) { clear++; counts[i]++; low[i]=Math.Min(low[i],j); high[i]=Math.Max(high[i],j); }
            }
            if (clear == 0) return;
            if (clear == total) { pieces.Add(f); return; }
            if (FitVerge(f,low,high,counts,nu,nv,blocked,out var fitted)) { pieces.Add(fitted); return; }
            if (depth >= 16 || f.halfLength < 6f && f.halfWidth < 6f) return;
            bool along = f.halfLength > f.halfWidth;
            var a = Slice(f, along, false); var b = Slice(f, along, true);
            Cut(a, blocked, pieces, depth + 1);
            Cut(b, blocked, pieces, depth + 1);
        }

        // Fit a straight field edge to the sampled verge before subdividing. A single continuous run in every
        // column can be enclosed by two conservative chords, avoiding a staircase of little rectangular cuts.
        static bool FitVerge(FieldPlot f,int[] low,int[] high,int[] counts,int nu,int nv,Func<Vector2,bool> blocked,out FieldPlot result)
        {
            result=default;
            for(int i=0;i<=nu;i++) if(counts[i]<2 || high[i]-low[i]+1!=counts[i]) return false;
            float V(int i,int j) { float u=f.halfLength*(2f*i/nu-1f); return f.shear*u+f.HalfWidthAt(u)*(2f*j/nv-1f); }
            float a0=V(0,low[0]),a1=V(0,high[0]),b0=V(nu,low[nu]),b1=V(nu,high[nu]);
            float lower=0f,upper=0f,area=0f;
            for(int i=0;i<=nu;i++)
            {
                float t=i/(float)nu,lo=V(i,low[i]),hi=V(i,high[i]);
                lower=Math.Max(lower,lo-Mathf.Lerp(a0,b0,t)); upper=Math.Max(upper,Mathf.Lerp(a1,b1,t)-hi);
                area+=hi-lo;
            }
            a0+=lower+.65f; b0+=lower+.65f; a1-=upper+.65f; b1-=upper+.65f;
            float width=a1-a0+b1-b0;
            if(Math.Min(a1-a0,b1-b0)<1f || width*.5f < area/(nu+1)*.90f) return false;
            result=f;result.center+=f.Across*((a0+a1+b0+b1)*.25f);result.halfWidth=width*.25f;
            result.taper=(b1-b0-a1+a0)/width;result.shear=(b0+b1-a0-a1)/(4f*f.halfLength);
            // Recheck the new sample lattice, as its diagonal edges no longer pass through the old sample nodes.
            int rows=Math.Max(1,(int)Math.Ceiling(result.halfWidth*2f*(1f+Math.Abs(result.taper))/2f));
            for(int j=0;j<=rows;j++) for(int i=0;i<=nu;i++)
                if(blocked(result.Point(result.halfLength*(2f*i/nu-1f),result.halfWidth*(2f*j/rows-1f)))) return false;
            return true;
        }

        // Road-edge pieces belong to their parent field, sharing crop, rows and one cached detail map. Merge
        // adjacent pieces before rendering instead of declaring every small tessellation fragment a new field.
        static void Merge(List<FieldPlot> pieces)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < pieces.Count; i++) for (int j = i + 1; j < pieces.Count; j++)
                    if (FieldParcels.Join(pieces[i], pieces[j], out var joined))
                    { joined.crop = pieces[i].crop; pieces[i] = joined; pieces.RemoveAt(j); changed = true; j = i; }
            }
        }

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
        // Only the parcel's hedged edges get one; the others keep their strip, track or bare crop edge.
        static void AddEdges(FieldPlot f, FieldPlot original)
        {
            var corners = Corners(f);
            var parentCorners = Corners(original);
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = corners[i], b = corners[(i + 1) % 4];
                if (Vector2.Distance(a, b) < 18f) continue;
                float half = Ground.Size * .5f;
                if ((Math.Abs(a.x) > half - .02f && Math.Abs(b.x) > half - .02f) || (Math.Abs(a.y) > half - .02f && Math.Abs(b.y) > half - .02f)) continue;
                Vector2 u = a - original.center, v = b - original.center;
                float au = Vector2.Dot(u, original.along), bu = Vector2.Dot(v, original.along);
                float av = Vector2.Dot(u, original.Across) - original.shear * au, bv = Vector2.Dot(v, original.Across) - original.shear * bu;
                bool side = av * bv > 0f && Math.Abs(Math.Abs(av) - original.HalfWidthAt(au)) <= .02f && Math.Abs(Math.Abs(bv) - original.HalfWidthAt(bu)) <= .02f;
                bool end = au * bu > 0f && Math.Abs(Math.Abs(au) - original.halfLength) <= .02f && Math.Abs(Math.Abs(bu) - original.halfLength) <= .02f;
                if (!side && !end) continue;
                  int edge = side ? (av < 0f ? 0 : 2) : (au > 0f ? 1 : 3);
                  if (FieldAppearance.EdgeUse(original.edges, edge) != (int)FieldEdge.Hedge) continue;
                  MapLayout.Boundaries.Add(new FieldBoundary { a = a, b = b, first = MapLayout.Fields.Count - 1,
                      second = -1, kind = FieldBoundaryKind.Hedge, hasGate = true,
                      gate = FieldAppearance.Gate(parentCorners[edge],parentCorners[(edge+1)%4]) });
            }
        }
    }
}

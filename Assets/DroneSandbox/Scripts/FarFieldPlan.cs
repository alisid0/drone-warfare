using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DroneSandbox
{
    // Immutable, lazily built farm blocks. Point queries and sector meshes read the very same trapezoids.
    // Farmland is laid out the way consolidated land is: regions about 4 km across, each square to its main road, are
    // cut into staggered rectangular blocks between straight farm tracks, and every block into strips of fields. Only
    // where two regions meet does a block end on a slant; there no field keeps a corner under 60 degrees, and a sharp
    // or narrow leftover joins the field beside it or stays grass.
    public sealed class FarFieldPlan
    {
        public const float DistrictSize = 700f, MinimumArea = 600f, MinimumWidth = 12f, MinimumEdge = 18f;
        public const float RegionSize = 4000f, MaxSlope = .58f; // a field side's slope: every corner at least 60 degrees
        const int First = -12, Count = 24, BlockSpan = 26, Slots = 2 * BlockSpan + 1; // a region reaches under 5 km from its site
        readonly int seed;
        readonly FarDetailCover cover;
        readonly Func<float, float, float> height;
        readonly Region[] regions = new Region[Count * Count];
        readonly Vector2[] sites = new Vector2[Count * Count];
        readonly object[] gates = new object[64];
        readonly FarDetailData.Segment[] roads; // through-roads only; tracks never square a region
        static readonly Parcel[] Grass = new Parcel[0]; // a grazed slot, which no remnant may take
        long buildTicks;
        int builtDistricts;
        public double BuildMilliseconds => Interlocked.Read(ref buildTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        public int BuiltDistricts => Volatile.Read(ref builtDistricts);

        // Keep the through-roads once; no global polygon pass or native Unity call is needed on world setup.
        public FarFieldPlan(int seed, FarDetailData.Segment[] roads, FarDetailCover cover = null, Func<float, float, float> height = null)
        {
            this.seed = seed; this.cover = cover; this.height = height;
            for (int i = 0; i < gates.Length; i++) gates[i] = new object();
            // Every pasture texel asks for its block, so the jittered region sites are hashed once, not nine times a query.
            for (int j = 0; j < Count; j++) for (int i = 0; i < Count; i++) sites[j * Count + i] = SiteAt(First + i, First + j);
            var through = new List<FarDetailData.Segment>();
            foreach (var s in roads) if (s.kind != (int)RoadClass.Track) through.Add(s);
            this.roads = through.ToArray();
        }

        // A district keeps its actual polygon for audit and an O(1) strip/cell index for painting.
        public sealed class District
        {
            public int x, z;
            public Vector2 site, along;
            public float angle, width, slope, settlement, wet, hedgeDensity, fallow;
            public int hash;
            public bool pasture; // a grassland block: grazed paddocks and the odd hay meadow, no crops
            public Vector2[] outline;
            public Parcel[] parcels;
            internal float stripOrigin; // strips are laid from the block's own edge, so none is left as a sliver
            internal Strip[] strips;

            // Candidate slots partition local coordinates; rejected slots remain rough meadow.
            public FarDetailData.FieldSample Sample(float x, float z)
            {
                float dx = x - site.x, dz = z - site.y;
                float u = dx * along.x + dz * along.y, v = -dx * along.y + dz * along.x;
                int row = (int)Math.Floor((v - stripOrigin) / width);
                if (row < 0 || row >= strips.Length) return new FarDetailData.FieldSample { layer = 4 };
                var strip = strips[row];
                int column = (int)Math.Floor((u - strip.offset) / strip.length);
                if (column < 0 || column >= strip.parcels.Length) return new FarDetailData.FieldSample { layer = 4 };
                var pieces = strip.parcels[column];
                if (pieces == null) return new FarDetailData.FieldSample { layer = 4 };
                var best = new FarDetailData.FieldSample { layer = 4 };
                float nearest = -3f;
                foreach (var p in pieces)
                {
                    float pu = p.cross ? v : u, pv = p.cross ? -u : v;
                    if (pu < p.u0 - 3f || pu > p.u1 + 3f) continue;
                    float t = (pu - p.u0) / (p.u1 - p.u0);
                    float lo = p.a0 + (p.b0 - p.a0) * t, hi = p.a1 + (p.b1 - p.a1) * t;
                    float lower = (pv - lo) * p.lowerNormal, upper = (hi - pv) * p.upperNormal, near = pu - p.u0, far = p.u1 - pu;
                    float side = Math.Min(lower, upper), end = Math.Min(near, far);
                    float score = Math.Min(side, end);
                    if (score < nearest) continue;
                    nearest = score;
                    int sideUse = FieldAppearance.EdgeUse(p.edges, lower <= upper ? 0 : 2), endUse = FieldAppearance.EdgeUse(p.edges, far <= near ? 1 : 3);
                    best = new FarDetailData.FieldSample { worldX = x, worldZ = z, use = p.use, layer = 5 + (int)p.crop, angle = angle + (p.cross ? (float)Math.PI * .5f : 0f), projection = Math.Abs(along.x) + Math.Abs(along.y), width = hi - lo, length = p.u1 - p.u0,
                        across = pv - lo, along = pu - p.u0, side = side, end = end, districtEdge = score + 4f, hedge = sideUse == (int)FieldEdge.Hedge,
                        hedgeEnd = endUse == (int)FieldEdge.Hedge, sideUse = sideUse, endUse = endUse,
                        sideMargin = FieldAppearance.MarginFor(sideUse, x, z), endMargin = FieldAppearance.MarginFor(endUse, x, z),
                        gateDistance = score < 3f ? FieldAppearance.GateDistance(pu,pv,p.u0,p.u1,p.a0,p.a1,p.b0,p.b1) : float.MaxValue,
                        shade = Shade(this, p), soil = Soil(this, p) };
                    if (score >= 0f) return best;
                }
                return best;
            }

            // Which pasture paddock a point lies in (0-1 from its strip slot, below 0 outside the strips), and how far it
            // is from that paddock's fences. Long slots are halved, so paddocks run about 50-240 m.
            public float Paddock(float x, float z, out float boundary)
            {
                float dx = x - site.x, dz = z - site.y;
                float u = dx * along.x + dz * along.y, v = -dx * along.y + dz * along.x;
                boundary = 0f;
                int row = (int)Math.Floor((v - stripOrigin) / width);
                if (row < 0 || row >= strips.Length) return -1f;
                var strip = strips[row];
                float length = strip.length > 240f ? strip.length * .5f : strip.length;
                float w = (u - strip.offset) / length, r = (v - stripOrigin) / width;
                int column = (int)Math.Floor(w);
                boundary = Math.Min(Math.Min(w - column, column + 1 - w) * length, Math.Min(r - (float)Math.Floor(r), (float)Math.Floor(r) + 1 - r) * width);
                return FieldAppearance.Shade(column, row, hash, 91);
            }
        }

        // A finite four-sided strip has square row ends and two straight, bounded-slope sides.
        public struct Parcel
        {
            public float u0, u1, a0, a1, b0, b1, lowerNormal, upperNormal;
            public Crop crop;
            public int use; // 0 crop, 1 meadow, 2 fallow, 3 orchard, 4 market garden
            public bool valid, hedge, cross; // hedge: at least one edge is a woody boundary
            public ushort edges; // FieldEdge per edge, four bits each: 0 lower side, 1 far end, 2 upper side, 3 near end

            // Translate the shared world geometry into the existing Farmland mesh input without re-planning it.
            public FieldPlot Plot(District district, Vector2 origin)
            {
                Vector2 along = cross ? new Vector2(-district.along.y, district.along.x) : district.along;
                Vector2 across = new Vector2(-along.y, along.x);
                float width = a1 - a0 + b1 - b0, length = u1 - u0;
                return new FieldPlot { center = district.site - origin + along * ((u0 + u1) * .5f) + across * ((a0 + a1 + b0 + b1) * .25f),
                    along = along, halfLength = length * .5f, halfWidth = width * .25f,
                    taper = (b1 - b0 - a1 + a0) / width, shear = (b0 + b1 - a0 - a1) / (2f * length), crop = crop, edges = edges };
            }

            // The parcel's middle in world metres, exactly as Plot places it before the sector origin is taken off.
            public Vector2 Centre(District district)
            {
                Vector2 along = cross ? new Vector2(-district.along.y, district.along.x) : district.along;
                return district.site + along * ((u0 + u1) * .5f) + new Vector2(-along.y, along.x) * ((a0 + a1 + b0 + b1) * .25f);
            }
        }

        // One field's own shade from its parcel's stored edges, so the sector's crop mesh and every painter agree exactly.
        public static float Shade(District district, Parcel p) =>
            FieldAppearance.Shade(district.x, district.z, (int)Math.Floor(p.u0 * 8f) ^ (p.cross ? 1 << 30 : 0), (int)Math.Floor((p.a0 + p.b1) * 8f));

        // The soil band under a field, taken at its middle so one field is one soil.
        public static float Soil(District district, Parcel p)
        {
            Vector2 c = p.Centre(district);
            return FieldAppearance.Soil(c.x, c.y);
        }

        // Strip offsets stagger cross-boundaries, while every slot still has constant-time lookup.
        internal sealed class Strip
        {
            public float length, offset;
            public Parcel[][] parcels;
        }

        // A region shares one frame: its blocks' tracks run square to it, and so do all their strips.
        sealed class Region
        {
            public int x, z, hash;
            public Vector2 site, along, across;
            public float angle, row, column, scale;
            public Vector2[] outline; // in the region frame, already set back from the neighbouring regions' land
            public readonly District[] blocks = new District[Slots * Slots];
        }

        // Nearest-site selection is identical for tiles, the picture, clipped sectors and the audit harness.
        public District At(float x, float z)
        {
            var r = RegionAt(x, z);
            float dx = x - r.site.x, dz = z - r.site.y;
            float u = dx * r.along.x + dz * r.along.y, v = dx * r.across.x + dz * r.across.y;
            int k = Row(r, v);
            return Block(r, k, Column(r, k, u));
        }

        // The block in the middle of an old 700 m district cell, for older audit tools.
        public District Get(int x, int z) => At((x + .5f) * DistrictSize, (z + .5f) * DistrictSize);

        // Every block with fields whose ground may lie in the world rectangle, for the sector's clipping.
        public List<District> Districts(Vector2 min, Vector2 max)
        {
            var list = new List<District>();
            for (int z = RegionCell(min.y) - 2; z <= RegionCell(max.y) + 2; z++)
                for (int x = RegionCell(min.x) - 2; x <= RegionCell(max.x) + 2; x++)
                {
                    var r = GetRegion(x, z);
                    float lu = float.MaxValue, hu = float.MinValue, lv = float.MaxValue, hv = float.MinValue;
                    for (int c = 0; c < 4; c++)
                    {
                        float dx = ((c & 1) == 0 ? min.x : max.x) - r.site.x, dz = ((c & 2) == 0 ? min.y : max.y) - r.site.y;
                        float u = dx * r.along.x + dz * r.along.y, v = dx * r.across.x + dz * r.across.y;
                        lu = Math.Min(lu, u); hu = Math.Max(hu, u); lv = Math.Min(lv, v); hv = Math.Max(hv, v);
                    }
                    float ou = float.MaxValue, oU = float.MinValue, ov = float.MaxValue, oV = float.MinValue;
                    foreach (var q in r.outline) { ou = Math.Min(ou, q.x); oU = Math.Max(oU, q.x); ov = Math.Min(ov, q.y); oV = Math.Max(oV, q.y); }
                    lu = Math.Max(lu, ou); hu = Math.Min(hu, oU); lv = Math.Max(lv, ov); hv = Math.Min(hv, oV);
                    if (lu > hu || lv > hv) continue;
                    for (int k = Row(r, lv), k1 = Row(r, hv); k <= k1; k++)
                        for (int m = Column(r, k, lu), m1 = Column(r, k, hu); m <= m1; m++)
                        {
                            var d = Block(r, k, m);
                            if (d.parcels.Length > 0) list.Add(d);
                        }
                }
            return list;
        }

        // The region whose jittered site is nearest: plain Voronoi cells about 4 km across.
        Region RegionAt(float x, float z)
        {
            int gx = RegionCell(x), gz = RegionCell(z), bx = gx, bz = gz;
            float best = float.MaxValue;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
            {
                Vector2 s = Site(gx + i, gz + j);
                float dx = s.x - x, dz = s.y - z, d = dx * dx + dz * dz;
                if (d < best) { best = d; bx = gx + i; bz = gz + j; }
            }
            return GetRegion(bx, bz);
        }

        // Only touched regions are constructed; striped locks keep competing workers from building one twice.
        Region GetRegion(int x, int z)
        {
            int i = x - First, j = z - First;
            if (i < 0 || j < 0 || i >= Count || j >= Count) return BuildRegion(x, z);
            int k = j * Count + i;
            var result = Volatile.Read(ref regions[k]);
            if (result != null) return result;
            lock (gates[k & 63])
            {
                result = regions[k];
                if (result == null) { result = BuildRegion(x, z); Volatile.Write(ref regions[k], result); }
                return result;
            }
        }

        // Only touched blocks are constructed, each once; their parcels never change afterwards.
        District Block(Region r, int k, int m)
        {
            if (k < -BlockSpan || k > BlockSpan || m < -BlockSpan || m > BlockSpan) return Build(r, k, m);
            int index = (k + BlockSpan) * Slots + m + BlockSpan;
            var result = Volatile.Read(ref r.blocks[index]);
            if (result != null) return result;
            lock (gates[(r.hash + index) & 63])
            {
                result = r.blocks[index];
                if (result == null) { result = Build(r, k, m); Volatile.Write(ref r.blocks[index], result); }
                return result;
            }
        }

        // Sum construction CPU time separately from painting wall time; concurrent workers can overlap.
        District Build(Region r, int k, int m)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = BuildBlock(r, k, m);
            Interlocked.Add(ref buildTicks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
            Interlocked.Increment(ref builtDistricts);
            return result;
        }

        // The region's frame and outline: four-metre tracks between regions, and a bevel where three regions meet so
        // three blocks never form spokes. Bigger, emptier country has bigger blocks (200-700 m a side).
        Region BuildRegion(int x, int z)
        {
            var r = new Region { x = x, z = z, site = Site(x, z), hash = Hash(x * 7 + 3, z * 11 + 5) };
            r.angle = RegionAngle(r.site);
            r.along = new Vector2((float)Math.Cos(r.angle), (float)Math.Sin(r.angle));
            r.across = new Vector2(-r.along.y, r.along.x);
            r.scale = .8f + .45f * Value(r.site.x / 9000f - 13f, r.site.y / 9000f + 29f);
            r.row = (330f + 120f * Unit(r.hash, 0, 4)) * r.scale;
            r.column = (400f + 160f * Unit(r.hash, 0, 5)) * r.scale;
            float reach = 2.5f * RegionSize;
            var polygon = new List<Vector2> { new Vector2(-reach, -reach), new Vector2(reach, -reach), new Vector2(reach, reach), new Vector2(-reach, reach) };
            for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++)
            {
                if (i == 0 && j == 0) continue;
                Vector2 delta = Site(x + i, z + j) - r.site;
                float length = (float)Math.Sqrt(delta.x * delta.x + delta.y * delta.y);
                polygon = Clip(polygon, Dot(delta, r.along) / length, Dot(delta, r.across) / length, length * .5f - 2f);
            }
            var original = polygon.ToArray();
            for (int k = 0; k < original.Length; k++)
            {
                Vector2 p = original[k], before = Unit(p - original[(k + original.Length - 1) % original.Length]), after = Unit(original[(k + 1) % original.Length] - p);
                Vector2 normal = Unit(new Vector2(before.y + after.y, -before.x - after.x));
                polygon = Clip(polygon, normal.x, normal.y, Dot(normal, p) - 12f);
            }
            r.outline = polygon.ToArray();
            return r;
        }

        // Block rows are 200-550 m bands across the region, their tracks straight and square to its frame.
        float RowEdge(Region r, int k) => (k + .24f * (Unit(r.hash, k, 1) - .5f)) * r.row;

        // Each row's blocks are offset from the next row's, so tracks meet in T-junctions rather than crossings.
        float ColumnEdge(Region r, int k, int m) => (m + .5f * Unit(r.hash, k, 2) + .3f * (Unit(r.hash, k * 131 + m, 3) - .5f)) * r.column;

        // The block row holding a region-frame offset; edges move under half a row, so one neighbour check suffices.
        int Row(Region r, float v)
        {
            int k = (int)Math.Floor(v / r.row);
            if (v < RowEdge(r, k)) return k - 1;
            return v >= RowEdge(r, k + 1) ? k + 1 : k;
        }

        // The block within a row holding a region-frame offset along it.
        int Column(Region r, int k, float u)
        {
            int m = (int)Math.Floor(u / r.column - .5f * Unit(r.hash, k, 2));
            if (u < ColumnEdge(r, k, m)) return m - 1;
            return u >= ColumnEdge(r, k, m + 1) ? m + 1 : m;
        }

        // One farm block between its tracks, cut into strips from its own edges so whole fields fill it: rectangles inside
        // the region, fitted trapezoids where it meets the next region. Grassland blocks lie on slopes, by water and round
        // villages; elsewhere one block in ten is grazed.
        District BuildBlock(Region r, int k, int m)
        {
            float r0 = RowEdge(r, k), r1 = RowEdge(r, k + 1), c0 = ColumnEdge(r, k, m), c1 = ColumnEdge(r, k, m + 1);
            var d = new District { x = r.x * 32 + k, z = r.z * 32 + m };
            int hash = d.hash = Hash(d.x, d.z);
            d.site = r.site + r.along * ((c0 + c1) * .5f) + r.across * ((r0 + r1) * .5f);
            float sx = d.site.x, sz = d.site.y;
            d.slope = height == null ? 0f : (float)Math.Sqrt(Math.Pow((height(sx + 96f, sz) - height(sx - 96f, sz)) / 192f, 2)
                + Math.Pow((height(sx, sz + 96f) - height(sx, sz - 96f)) / 192f, 2));
            if (cover != null) for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++)
            {
                var s = cover.At(sx + i * 160f, sz + j * 160f, false);
                d.settlement = Math.Max(d.settlement, (s.Weight(Cover.Village) + s.Weight(Cover.Town)) / (1f + .3f * (i * i + j * j)));
                d.wet = Math.Max(d.wet, (s.Weight(Cover.Water) + s.Weight(Cover.Wetland)) / (1f + .3f * (i * i + j * j)));
            }
            float small = Math.Max(FarDetailData.Clamp(d.slope / .14f, 0f, 1f), d.settlement);
            float regional = Value(sx / 2200f + 19f, sz / 2200f - 7f);
            float width = FarDetailData.Mix(80f + regional * 105f, 34f + regional * 25f, small) * r.scale;
            bool estate = small < .22f && (hash & 15) < 3;
            if (estate) width = 260f + (hash >> 8 & 63);
            d.width = width;
            // Woody boundaries are a minority of field edges (about 5% of farmland in Europe): 5-15% in most districts,
            // more around villages and in small-strip country.
            d.hedgeDensity = Math.Min(.45f, .05f + .10f * Value(sx / 1800f - 81f, sz / 1800f + 9f) + .18f * d.settlement + .10f * small);
            d.fallow = .1f + .1f * Value(sx / 1600f + 71f, sz / 1600f + 23f);
            float grassland = .07f + .45f * d.settlement + .4f * FarDetailData.Clamp(d.slope / .1f, 0f, 1f) + Math.Min(.45f, d.wet);
            d.pasture = Unit(hash, 0, 6) < Math.Min(.8f, grassland);
            var block = Clip(Clip(Clip(Clip(r.outline, -1, 0, -(c0 + 2f)), 1, 0, c1 - 2f), 0, -1, -(r0 + 2f)), 0, 1, r1 - 2f);
            if (block.Count < 3) { d.outline = new Vector2[0]; d.strips = new Strip[0]; d.parcels = new Parcel[0]; return d; }
            // Strips run along the block's longer side three times in five, otherwise across it.
            bool longU = c1 - c0 >= r1 - r0, turned = (hash >> 3) % 5 < 3 ? !longU : longU;
            Vector2 lo = new Vector2(c0 + 2f, r0 + 2f), hi = new Vector2(c1 - 2f, r1 - 2f);
            var best = Lay(r, d, block, r.angle + (turned ? (float)(Math.PI * .5) : 0f), lo, hi, true, estate, small, width, out float most);
            if (Area(block) > (hi.x - lo.x) * (hi.y - lo.y) * .995f) return best;
            // A block cut by the next region's land tries its other grid direction and both directions of the longest
            // cutting edge, keeping whichever fills it with the most field (the region's own grid wins near-ties).
            most *= 1.04f;
            float edge = 0f, longest = 0f;
            for (int i = 0; i < block.Count; i++)
            {
                Vector2 e = block[(i + 1) % block.Count] - block[i];
                float length = (float)Math.Sqrt(Dot(e, e));
                if (length > longest && Math.Abs(e.x) > .05f * length && Math.Abs(e.y) > .05f * length) { longest = length; edge = (float)Math.Atan2(e.y, e.x); }
            }
            for (int option = 0; option < 3; option++)
            {
                if (option > 0 && longest <= 0f) break;
                float angle = option == 0 ? r.angle + (turned ? 0f : (float)(Math.PI * .5)) : r.angle + edge + (option == 2 ? (float)(Math.PI * .5) : 0f);
                var other = Lay(r, d, block, angle, lo, hi, option == 0, estate, small, width, out float laid);
                if (laid * (option == 0 ? 1.04f : 1f) > most) { best = other; most = laid * (option == 0 ? 1.04f : 1f); }
            }
            return best;
        }

        // Lay one block's strips and fields in one direction: from the block's own edges (square to the region) or the
        // cut polygon's (along a cutting edge), so whole fields fill it; returns the field area laid.
        District Lay(Region r, District template, List<Vector2> block, float angle, Vector2 lo, Vector2 hi, bool square,
            bool estate, float small, float width, out float laid)
        {
            var d = new District { x = template.x, z = template.z, hash = template.hash, site = template.site, slope = template.slope,
                settlement = template.settlement, wet = template.wet, hedgeDensity = template.hedgeDensity, fallow = template.fallow,
                pasture = template.pasture };
            int hash = d.hash;
            // Use the control texture's exact angle grid so mesh rows and shader rows cannot drift in world metres.
            angle -= (float)Math.Floor(angle / Math.PI) * (float)Math.PI;
            d.angle = (float)(Math.Round(angle / Math.PI * 65535) * Math.PI / 65535);
            d.along = new Vector2((float)Math.Cos(d.angle), (float)Math.Sin(d.angle));
            Vector2 across = new Vector2(-d.along.y, d.along.x);
            var polygon = new List<Vector2>(block.Count);
            foreach (var q in block) polygon.Add(Local(r, d, across, q));
            d.outline = polygon.ToArray();
            float loU = float.MaxValue, hiU = float.MinValue, loV = float.MaxValue, hiV = float.MinValue;
            if (square)
                for (int c = 0; c < 4; c++)
                {
                    var q = Local(r, d, across, new Vector2((c & 1) == 0 ? lo.x : hi.x, (c & 2) == 0 ? lo.y : hi.y));
                    loU = Math.Min(loU, q.x); hiU = Math.Max(hiU, q.x); loV = Math.Min(loV, q.y); hiV = Math.Max(hiV, q.y);
                }
            else foreach (var q in polygon) { loU = Math.Min(loU, q.x); hiU = Math.Max(hiU, q.x); loV = Math.Min(loV, q.y); hiV = Math.Max(hiV, q.y); }
            int rows = Math.Max(1, (int)Math.Round((hiV - loV) / width));
            d.width = (hiV - loV) / rows; d.stripOrigin = loV;
            d.strips = new Strip[rows];
            var parcels = new List<Parcel>();
            for (int row = 0; row < rows; row++)
            {
                int hs = Hash(row, hash);
                float target = estate ? 420f + (hs & 127) : FarDetailData.Mix(260f + (hs & 255) * .8f, 95f + (hs & 127), small);
                int columns = Math.Max(1, (int)Math.Round((hiU - loU) / target));
                var strip = new Strip { length = (hiU - loU) / columns, offset = loU, parcels = new Parcel[columns][] };
                d.strips[row] = strip;
                float v = loV + row * d.width;
                for (int col = 0; col < columns; col++)
                {
                    float u = loU + col * strip.length;
                    int h = Hash(col, hs);
                    // A grassland block is grazed paddocks (painted from the strip slots), with the odd hay meadow.
                    if (d.pasture && (h >> 20 & 7) != 0) { strip.parcels[col] = Grass; continue; }
                    var crop = Clip(Clip(Clip(Clip(polygon, -1, 0, -u - .65f), 1, 0, u + strip.length - .65f), 0, -1, -v - .65f), 0, 1, v + d.width - .65f);
                    Parcel p = BestFit(crop);
                    if (!Usable(p)) continue; // a sharp or narrow leftover: offered to its neighbour below
                    int kind = h % 25;
                    p.crop = kind < 8 ? Crop.Wheat : kind < 14 ? Crop.YoungCrop : kind < 18 ? Crop.Sunflower : kind < 20 ? Crop.Ploughed : Crop.MownHay;
                    float idle = (h >> 8 & 65535) / 65536f;
                    p.use = d.pasture ? 1 : idle < d.fallow ? ((h & 1) == 0 ? 1 : 2) : d.settlement > .18f && (h >> 4 & 7) == 0 ? ((h & 1) == 0 ? 3 : 4) : 0;
                    if (p.use == 1) p.crop = Crop.MownHay;
                    if (p.use == 2) p.crop = Crop.Ploughed;
                    if (p.use == 3 || p.use == 4) p.crop = Crop.YoungCrop;
                    p.edges = Edges(d.hedgeDensity, h, p.use);
                    p.hedge = HasHedge(p.edges);
                    // Land cover clips the surface, not the ownership fabric. Cutting here AND in the sector
                    // discarded whole workable edge strips twice. Meadow is an enclosed hay/stubble parcel.
                    strip.parcels[col] = new[] { p }; parcels.Add(p);
                }
                MergeRemnants(strip, v, v + d.width, polygon, parcels);
            }
            d.parcels = parcels.ToArray();
            laid = 0f;
            foreach (var p in d.parcels) laid += ParcelArea(p);
            // Grazed slots count as laid too: a grassland block keeps its grid whatever its fields.
            if (d.pasture) laid = Area(block);
            return d;
        }

        // Shoelace area of a convex outline.
        static float Area(List<Vector2> polygon)
        {
            float sum = 0f;
            for (int i = 0; i < polygon.Count; i++) { Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count]; sum += a.x * b.y - b.x * a.y; }
            return Math.Abs(sum) * .5f;
        }

        // A region-frame point in a block's own field frame.
        static Vector2 Local(Region r, District d, Vector2 across, Vector2 q)
        {
            Vector2 delta = r.site + r.along * q.x + r.across * q.y - d.site;
            return new Vector2(Dot(delta, d.along), Dot(delta, across));
        }

        // A field is kept only as a fair trapezoid: at least 20 m across its narrow end and two fifths of its wide end,
        // so no wedge or sliver is drawn; its sides are already bounded to 60-degree corners (Fit). An awkward remnant
        // that no neighbour can take stays meadow.
        static bool Usable(Parcel p)
        {
            if (!p.valid) return false;
            float a = p.a1 - p.a0, b = p.b1 - p.b0;
            return Math.Min(a, b) >= 20f && Math.Min(a, b) >= .4f * Math.Max(a, b);
        }

        // Whether a strip slot holds a field (a grazed slot is empty but taken).
        static bool Holds(Parcel[] slot) => slot != null && slot.Length > 0;

        // An undersized or sharp end slot belongs to its adjoining field. Refit their union inside the same block,
        // preserving the neighbour's crop and updating both constant-time lookup slots to the merged parcel; a union
        // that would itself be a wedge stays apart, and the leftover stays grass.
        static void MergeRemnants(Strip strip, float low, float high, List<Vector2> district, List<Parcel> parcels)
        {
            for (int cell = 0; cell < strip.parcels.Length; cell++)
            {
                if (strip.parcels[cell] != null) continue;
                int neighbour = cell > 0 && Holds(strip.parcels[cell - 1]) ? cell - 1 : cell + 1;
                if (neighbour >= strip.parcels.Length || !Holds(strip.parcels[neighbour])) continue;
                var shared = strip.parcels[neighbour]; var old = shared[0];
                float start = old.cross ? -Math.Max(old.a1, old.b1) : old.u0, end = old.cross ? -Math.Min(old.a0, old.b0) : old.u1;
                float u = cell * strip.length + strip.offset;
                start = Math.Min(start, u + .65f); end = Math.Max(end, u + strip.length - .65f);
                var polygon = Clip(Clip(Clip(Clip(district, -1, 0, -start), 1, 0, end), 0, -1, -low - .65f), 0, 1, high - .65f);
                var joined = BestFit(polygon);
                if (!Usable(joined) || ParcelArea(joined) <= ParcelArea(old) + 1f) continue;
                joined.crop = old.crop; joined.use = old.use; joined.hedge = old.hedge; joined.edges = old.edges;
                int at = parcels.IndexOf(old); if (at < 0) continue; parcels[at] = joined;
                var replacement = new[] { joined };
                for (int i = 0; i < strip.parcels.Length; i++) if (ReferenceEquals(strip.parcels[i], shared)) strip.parcels[i] = replacement;
                strip.parcels[cell] = replacement;
            }
        }

        // Each edge is chosen on its own: a hedge at the district's woody share (pasture and orchards are enclosed more
        // often), otherwise crop to the boundary, a grass strip, a wider uncultivated margin or a field track.
        ushort Edges(float woody, int h, int use)
        {
            float enclosed = use == 1 ? 1.8f : use == 3 ? 1.5f : use == 2 ? 1.2f : 1f;
            int edges = 0;
            for (int e = 0; e < 4; e++)
            {
                int r = Hash(e + 11, h);
                FieldEdge kind;
                if ((r & 1023) / 1024f < woody * enclosed) kind = FieldEdge.Hedge;
                else
                {
                    int k = (r >> 10) % 20;
                    kind = k < 8 ? FieldEdge.Bare : k < 14 ? FieldEdge.Strip : k < 17 ? FieldEdge.Wide : FieldEdge.Track;
                }
                edges |= (int)kind << (e * 4);
            }
            return (ushort)edges;
        }

        // Any hedged edge makes a parcel enclosed for gates and legacy callers.
        static bool HasHedge(ushort edges)
        {
            for (int e = 0; e < 4; e++) if (FieldAppearance.EdgeUse(edges, e) == (int)FieldEdge.Hedge) return true;
            return false;
        }

        // Edge fields may turn a quarter turn to meet the farm boundary without sacrificing a broad wedge.
        static Parcel BestFit(List<Vector2> polygon)
        {
            Parcel first = Fit(polygon);
            var turned = new List<Vector2>(polygon.Count);
            foreach (var p in polygon) turned.Add(new Vector2(p.y, -p.x));
            Parcel second = Fit(turned); second.cross = true;
            return ParcelArea(second) > ParcelArea(first) ? second : first;
        }

        // Invalid candidates contribute no area to the orientation comparison.
        static float ParcelArea(Parcel p) => p.valid ? (p.u1 - p.u0) * (p.a1 - p.a0 + p.b1 - p.b0) * .5f : 0f;

        // Chords of a convex intersection lie inside it: tip removal never grows crops across a neighbour's land.
        static Parcel Fit(List<Vector2> polygon)
        {
            if (polygon.Count < 3) return default;
            float left = float.MaxValue, right = float.MinValue;
            foreach (var p in polygon) { left = Math.Min(left, p.x); right = Math.Max(right, p.x); }
            if (right - left < MinimumEdge) return default;
            Span(polygon, left, out float a0, out float a1); Span(polygon, right, out float b0, out float b1);
            if (a1 - a0 < 18.1f)
            {
                float a = left, b = (left + right) * .5f;
                Span(polygon, b, out float lo, out float hi);
                if (hi - lo < 18.1f) return default;
                for (int i = 0; i < 12; i++) { float m = (a + b) * .5f; Span(polygon, m, out lo, out hi); if (hi - lo < 18.1f) a = m; else b = m; }
                left = b;
            }
            if (b1 - b0 < 18.1f)
            {
                float a = (left + right) * .5f, b = right;
                Span(polygon, a, out float lo, out float hi);
                if (hi - lo < 18.1f) return default;
                for (int i = 0; i < 12; i++) { float m = (a + b) * .5f; Span(polygon, m, out lo, out hi); if (hi - lo < 18.1f) b = m; else a = m; }
                right = a;
            }
            float length = right - left;
            if (length < MinimumEdge) return default;
            Span(polygon, left, out a0, out a1); Span(polygon, right, out b0, out b1);
            // Bound both side slopes: every corner is at least 60 degrees, so no field reads as a triangle.
            float limit = length * MaxSlope;
            a0 = Math.Max(a0, b0 - limit); b0 = Math.Max(b0, a0 - limit);
            a1 = Math.Min(a1, b1 + limit); b1 = Math.Min(b1, a1 + limit);
            float width = Math.Min(a1 - a0, b1 - b0);
            if (width < 18f || length * (a1 - a0 + b1 - b0) * .5f < MinimumArea) return default;
            float lower = (b0 - a0) / length, upper = (b1 - a1) / length;
            return new Parcel { valid = true, u0 = left, u1 = right, a0 = a0, a1 = a1, b0 = b0, b1 = b1,
                lowerNormal = 1f / (float)Math.Sqrt(1f + lower * lower), upperNormal = 1f / (float)Math.Sqrt(1f + upper * upper) };
        }

        // Find the convex polygon's lower and upper edge at a row coordinate, including vertical ends.
        static void Span(List<Vector2> polygon, float u, out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                if (u < Math.Min(a.x, b.x) - .002f || u > Math.Max(a.x, b.x) + .002f) continue;
                if (Math.Abs(b.x - a.x) < .0001f) { lo = Math.Min(lo, Math.Min(a.y, b.y)); hi = Math.Max(hi, Math.Max(a.y, b.y)); }
                else { float v = a.y + (b.y - a.y) * Math.Max(0f, Math.Min(1f, (u - a.x) / (b.x - a.x))); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
            }
        }

        // Sutherland-Hodgman half-plane clipping is used only when a district or a sector fragment is first built.
        public static List<Vector2> Clip(IList<Vector2> polygon, float nx, float ny, float limit)
        {
            var result = new List<Vector2>(polygon.Count + 1);
            if (polygon.Count == 0) return result;
            Vector2 a = polygon[polygon.Count - 1]; float da = a.x * nx + a.y * ny - limit;
            foreach (var b in polygon)
            {
                float db = b.x * nx + b.y * ny - limit;
                if ((da <= 0f) != (db <= 0f)) result.Add(a + (b - a) * (da / (da - db)));
                if (db <= 0f) result.Add(b);
                a = b; da = db;
            }
            return result;
        }

        // Broad, continuous angle changes correlate regions; the through-roads within 1.6 km square a region's blocks to
        // their main axis (length-weighted, nearer roads counting more), on whichever side lies nearer the broad angle.
        float RegionAngle(Vector2 site)
        {
            float angle = .4f + Value(site.x / 2800f, site.y / 2800f) * 1.15f;
            double c = 0, s = 0, total = 0;
            foreach (var r in roads)
            {
                if (Math.Min(r.ax, r.bx) > site.x + 1600f || Math.Max(r.ax, r.bx) < site.x - 1600f
                    || Math.Min(r.az, r.bz) > site.y + 1600f || Math.Max(r.az, r.bz) < site.y - 1600f) continue;
                float dx = r.bx - r.ax, dz = r.bz - r.az, length = dx * dx + dz * dz;
                if (length < 1f) continue;
                float t = Math.Max(0f, Math.Min(1f, ((site.x - r.ax) * dx + (site.y - r.az) * dz) / length));
                float ex = site.x - r.ax - dx * t, ez = site.y - r.az - dz * t, distance = (float)Math.Sqrt(ex * ex + ez * ez);
                if (distance > 1600f) continue;
                double w = Math.Sqrt(length) / (1.0 + distance / 400.0), phi = 4.0 * Math.Atan2(dz, dx);
                c += w * Math.Cos(phi); s += w * Math.Sin(phi); total += w;
            }
            // A quarter-turn-symmetric mean: a crossroads keeps one grid, and a road too winding to give an axis keeps the drift.
            if (total < 150.0 || c * c + s * s < .12 * total * total) return angle;
            float road = (float)(Math.Atan2(s, c) / 4.0);
            road += (float)Math.Round((angle - road) / (Math.PI * .5)) * (float)(Math.PI * .5);
            return road - (float)Math.Floor(road / Math.PI) * (float)Math.PI;
        }

        // Bilinear value noise supplies smooth orientations without a Unity-native noise dependency.
        float Value(float x, float z)
        {
            int i = (int)Math.Floor(x), j = (int)Math.Floor(z); float u = x - i, v = z - j;
            u = u * u * (3f - 2f * u); v = v * v * (3f - 2f * v);
            float a = (Hash(i, j) & 65535) / 65535f, b = (Hash(i + 1, j) & 65535) / 65535f;
            float c = (Hash(i, j + 1) & 65535) / 65535f, d = (Hash(i + 1, j + 1) & 65535) / 65535f;
            return (a + (b - a) * u) * (1f - v) + (c + (d - c) * u) * v;
        }

        // A region's jittered site, cached for the regions any query can reach.
        Vector2 Site(int x, int z)
        {
            int i = x - First, j = z - First;
            return i >= 0 && j >= 0 && i < Count && j < Count ? sites[j * Count + i] : SiteAt(x, z);
        }

        // The jittered site itself, from the stable hash.
        Vector2 SiteAt(int x, int z)
        { int h = Hash(x, z); return new Vector2((x + .15f + .7f * (h & 1023) / 1023f) * RegionSize, (z + .15f + .7f * (h >> 10 & 1023) / 1023f) * RegionSize); }

        // One stable value in [0, 1) per region (or block), index and purpose.
        float Unit(int hash, int index, int salt) => (Hash(index * 977 + salt * 7919, hash ^ salt * 104729) & 65535) / 65536f;

        // Stable integer hashing is independent of thread scheduling and sector-local RNG consumption.
        int Hash(int x, int z)
        { unchecked { int h = x * 73856093 ^ z * 19349663 ^ seed * 83492791; h ^= h >> 13; h *= 0x5bd1e995; return h & 0x7fffffff; } }
        // Signed region cells cover both halves of the theatre without truncation toward zero.
        static int RegionCell(float p) => (int)Math.Floor(p / RegionSize);
        // Managed vector operations also run in the standalone harness.
        static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        // Normalize only district edges, which are constructed with nonzero length.
        static Vector2 Unit(Vector2 p) { float n = (float)Math.Sqrt(Dot(p, p)); return n > .00001f ? p / n : Vector2.zero; }
    }
}

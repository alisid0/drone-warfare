using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DroneSandbox
{
    // Immutable, lazily built farm districts. Point queries and sector meshes read the very same trapezoids.
    public sealed class FarFieldPlan
    {
        public const float DistrictSize = 700f, MinimumArea = 600f, MinimumWidth = 12f, MinimumEdge = 18f;
        const int First = -42, Count = 84;
        readonly int seed;
        readonly FarDetailCover cover;
        readonly Func<float, float, float> height;
        readonly District[] districts = new District[Count * Count];
        readonly Dictionary<long, FarDetailData.Segment[]> roadCells = new Dictionary<long, FarDetailData.Segment[]>();

        // Index road directions once; no global polygon pass or native Unity call is needed on world setup.
        public FarFieldPlan(int seed, FarDetailData.Segment[] roads, FarDetailCover cover = null, Func<float, float, float> height = null)
        {
            this.seed = seed; this.cover = cover; this.height = height;
            var index = new Dictionary<long, List<FarDetailData.Segment>>();
            foreach (var s in roads)
            {
                if (s.kind == (int)RoadClass.Track) continue;
                int x0 = Cell(Math.Min(s.ax, s.bx) - 500f), x1 = Cell(Math.Max(s.ax, s.bx) + 500f);
                int z0 = Cell(Math.Min(s.az, s.bz) - 500f), z1 = Cell(Math.Max(s.az, s.bz) + 500f);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    long key = Key(x, z);
                    if (!index.TryGetValue(key, out var list)) { list = new List<FarDetailData.Segment>(); index.Add(key, list); }
                    list.Add(s);
                }
            }
            foreach (var pair in index) roadCells.Add(pair.Key, pair.Value.ToArray());
        }

        // A district keeps its actual polygon for audit and an O(1) strip/cell index for painting.
        public sealed class District
        {
            public int x, z;
            public Vector2 site, along;
            public float angle, width, slope, settlement, hedgeDensity, fallow;
            public Vector2[] outline;
            public Parcel[] parcels;
            internal int firstStrip;
            internal Strip[] strips;

            // Candidate slots partition local coordinates; rejected slots remain rough meadow.
            public FarDetailData.FieldSample Sample(float x, float z)
            {
                float dx = x - site.x, dz = z - site.y;
                float u = dx * along.x + dz * along.y, v = -dx * along.y + dz * along.x;
                int row = (int)Math.Floor(v / width) - firstStrip;
                if (row < 0 || row >= strips.Length) return new FarDetailData.FieldSample { layer = 4 };
                var strip = strips[row];
                int column = (int)Math.Floor((u - strip.offset) / strip.length) - strip.first;
                if (column < 0 || column >= strip.parcels.Length) return new FarDetailData.FieldSample { layer = 4 };
                var pieces = strip.parcels[column];
                if (pieces == null) return new FarDetailData.FieldSample { layer = 4 };
                var best = new FarDetailData.FieldSample { layer = 4 };
                float nearest = -3f;
                foreach (var p in pieces)
                {
                    if (u < p.u0 - 3f || u > p.u1 + 3f) continue;
                    float t = (u - p.u0) / (p.u1 - p.u0);
                    float lo = p.a0 + (p.b0 - p.a0) * t, hi = p.a1 + (p.b1 - p.a1) * t;
                    float side = Math.Min((v - lo) * p.lowerNormal, (hi - v) * p.upperNormal), end = Math.Min(u - p.u0, p.u1 - u);
                    float score = Math.Min(side, end);
                    if (score < nearest) continue;
                    nearest = score;
                    best = new FarDetailData.FieldSample { use = p.use, layer = p.use == 1 ? 4 : 5 + (int)p.crop, angle = angle, projection = Math.Abs(along.x) + Math.Abs(along.y), width = hi - lo, length = p.u1 - p.u0,
                        across = v - lo, along = u - p.u0, side = side, end = end, districtEdge = score + 4f, hedge = p.hedge };
                    if (score >= 0f) return best;
                }
                return best;
            }
        }

        // A finite four-sided strip has square row ends and two straight, bounded-slope sides.
        public struct Parcel
        {
            public float u0, u1, a0, a1, b0, b1, lowerNormal, upperNormal;
            public Crop crop;
            public int use; // 0 crop, 1 meadow, 2 fallow, 3 orchard, 4 market garden
            public bool valid, hedge;

            // Translate the shared world geometry into the existing Farmland mesh input without re-planning it.
            public FieldPlot Plot(District district, Vector2 origin)
            {
                Vector2 across = new Vector2(-district.along.y, district.along.x);
                float width = a1 - a0 + b1 - b0, length = u1 - u0;
                return new FieldPlot { center = district.site - origin + district.along * ((u0 + u1) * .5f) + across * ((a0 + a1 + b0 + b1) * .25f),
                    along = district.along, halfLength = length * .5f, halfWidth = width * .25f,
                    taper = (b1 - b0 - a1 + a0) / width, shear = (b0 + b1 - a0 - a1) / (2f * length), crop = crop };
            }
        }

        // Strip offsets stagger cross-boundaries, while every slot still has constant-time lookup.
        internal sealed class Strip
        {
            public float length, offset;
            public int first;
            public Parcel[][] parcels;
        }

        // Nearest-site selection is identical for tiles, the picture, clipped sectors and the audit harness.
        public District At(float x, float z)
        {
            int gx = Cell(x), gz = Cell(z), bx = gx, bz = gz;
            float best = float.MaxValue;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
            {
                Vector2 s = Site(gx + i, gz + j);
                float dx = s.x - x, dz = s.y - z, d = dx * dx + dz * dz;
                if (d < best) { best = d; bx = gx + i; bz = gz + j; }
            }
            return Get(bx, bz);
        }

        // Only touched districts are constructed; compare-exchange safely publishes one immutable result to workers.
        public District Get(int x, int z)
        {
            int i = x - First, j = z - First;
            if (i < 0 || j < 0 || i >= Count || j >= Count) return Build(x, z);
            int k = j * Count + i;
            var result = Volatile.Read(ref districts[k]);
            if (result != null) return result;
            result = Build(x, z);
            return Interlocked.CompareExchange(ref districts[k], result, null) ?? result;
        }

        // Keep a 10 m district corridor, bevel corners by 8 m, then fit workable parcels into its interior.
        District Build(int x, int z)
        {
            int hash = Hash(x, z);
            var d = new District { x = x, z = z, site = Site(x, z) };
            float sx = d.site.x, sz = d.site.y;
            d.slope = height == null ? 0f : (float)Math.Sqrt(Math.Pow((height(sx + 96f, sz) - height(sx - 96f, sz)) / 192f, 2)
                + Math.Pow((height(sx, sz + 96f) - height(sx, sz - 96f)) / 192f, 2));
            if (cover != null) for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++)
            {
                var s = cover.At(sx + i * 160f, sz + j * 160f, false);
                d.settlement = Math.Max(d.settlement, (s.Weight(Cover.Village) + s.Weight(Cover.Town)) / (1f + .3f * (i * i + j * j)));
            }
            float small = Math.Max(FarDetailData.Clamp(d.slope / .14f, 0f, 1f), d.settlement);
            float regional = Value(sx / 2200f + 19f, sz / 2200f - 7f);
            d.width = FarDetailData.Mix(80f + regional * 105f, 34f + regional * 25f, small);
            bool estate = small < .22f && (hash & 15) < 3;
            if (estate) d.width = 260f + (hash >> 8 & 63);
            d.hedgeDensity = .12f + .7f * Value(sx / 1800f - 81f, sz / 1800f + 9f);
            d.fallow = .1f + .1f * Value(sx / 1600f + 71f, sz / 1600f + 23f);
            // Use the control texture's exact angle grid so mesh rows and shader rows cannot drift in world metres.
            d.angle = (float)(Math.Round(Direction(d.site) / Math.PI * 65535) * Math.PI / 65535);
            d.along = new Vector2((float)Math.Cos(d.angle), (float)Math.Sin(d.angle));
            var polygon = new List<Vector2> { new Vector2(-1400, -1400), new Vector2(1400, -1400), new Vector2(1400, 1400), new Vector2(-1400, 1400) };
            for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++)
            {
                if (i == 0 && j == 0) continue;
                Vector2 delta = Site(x + i, z + j) - d.site;
                float length = (float)Math.Sqrt(delta.x * delta.x + delta.y * delta.y);
                Vector2 n = new Vector2((delta.x * d.along.x + delta.y * d.along.y) / length, (-delta.x * d.along.y + delta.y * d.along.x) / length);
                polygon = Clip(polygon, n.x, n.y, length * .5f - 5f);
            }
            var original = polygon.ToArray();
            for (int k = 0; k < original.Length; k++)
            {
                Vector2 p = original[k], before = Unit(p - original[(k + original.Length - 1) % original.Length]), after = Unit(original[(k + 1) % original.Length] - p);
                Vector2 normal = Unit(new Vector2(before.y + after.y, -before.x - after.x));
                polygon = Clip(polygon, normal.x, normal.y, Dot(normal, p) - 8f);
            }
            d.outline = polygon.ToArray();
            float loU = float.MaxValue, hiU = float.MinValue, loV = float.MaxValue, hiV = float.MinValue;
            foreach (var p in polygon) { loU = Math.Min(loU, p.x); hiU = Math.Max(hiU, p.x); loV = Math.Min(loV, p.y); hiV = Math.Max(hiV, p.y); }
            d.firstStrip = (int)Math.Floor(loV / d.width);
            int last = (int)Math.Floor(hiV / d.width);
            d.strips = new Strip[last - d.firstStrip + 1];
            var parcels = new List<Parcel>();
            for (int row = d.firstStrip; row <= last; row++)
            {
                int hs = Hash(row, hash);
                var strip = new Strip { length = estate ? 420f + (hs & 127) : FarDetailData.Mix(260f + (hs & 255) * .8f, 95f + (hs & 127), small), offset = (hs >> 8 & 255) };
                strip.first = (int)Math.Floor((loU - strip.offset) / strip.length);
                int end = (int)Math.Floor((hiU - strip.offset) / strip.length);
                strip.parcels = new Parcel[end - strip.first + 1][]; d.strips[row - d.firstStrip] = strip;
                for (int col = strip.first; col <= end; col++)
                {
                    float u = col * strip.length + strip.offset, v = row * d.width;
                    var crop = Clip(Clip(Clip(Clip(polygon, -1, 0, -u - 2.5f), 1, 0, u + strip.length - 2.5f), 0, -1, -v - 3f), 0, 1, v + d.width - 3f);
                    Parcel p = Fit(crop);
                    if (!p.valid) continue;
                    int h = Hash(col, hs), kind = h % 6;
                    p.crop = kind < 2 ? Crop.Wheat : kind == 2 ? Crop.YoungCrop : kind == 3 ? Crop.Sunflower : kind == 4 ? Crop.Ploughed : Crop.MownHay;
                    float idle = (h >> 8 & 65535) / 65536f;
                    p.use = idle < d.fallow ? ((h & 1) == 0 ? 1 : 2) : d.settlement > .18f && (h >> 4 & 7) == 0 ? ((h & 1) == 0 ? 3 : 4) : 0;
                    if (p.use == 2) p.crop = Crop.Ploughed;
                    if (p.use == 3 || p.use == 4) p.crop = Crop.YoungCrop;
                    p.hedge = (hs >> 16 & 1023) / 1024f < d.hedgeDensity;
                    var pieces = new List<Parcel>();
                    ClipCover(d, p, pieces, 0);
                    strip.parcels[col - strip.first] = pieces.ToArray(); parcels.AddRange(pieces);
                }
            }
            d.parcels = parcels.ToArray();
            return d;
        }

        // Boundary parcels are subdivided only during planning; every accepted remnant is a complete workable trapezoid.
        void ClipCover(District d, Parcel p, List<Parcel> result, int depth)
        {
            if (cover == null) { result.Add(p); return; }
            Vector2 World(float u, float v) => d.site + d.along * u + new Vector2(-d.along.y, d.along.x) * v;
            var a = World(p.u0, p.a0); var b = World(p.u0, p.a1); var c = World(p.u1, p.b0); var e = World(p.u1, p.b1);
            float x0 = Math.Min(Math.Min(a.x, b.x), Math.Min(c.x, e.x)), x1 = Math.Max(Math.Max(a.x, b.x), Math.Max(c.x, e.x));
            float z0 = Math.Min(Math.Min(a.y, b.y), Math.Min(c.y, e.y)), z1 = Math.Max(Math.Max(a.y, b.y), Math.Max(c.y, e.y));
            if (cover.Uniform(x0, z0, x1, z1, Cover.Farmland)) { result.Add(p); return; }
            int nu = (int)Math.Ceiling((p.u1 - p.u0) / 8f), nv = (int)Math.Ceiling(Math.Max(p.a1 - p.a0, p.b1 - p.b0) / 8f);
            int clear = 0;
            for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++)
            {
                float t = i / (float)nu;
                Vector2 w = World(FarDetailData.Mix(p.u0, p.u1, t), FarDetailData.Mix(FarDetailData.Mix(p.a0, p.b0, t), FarDetailData.Mix(p.a1, p.b1, t), j / (float)nv));
                if (cover.Farm(w.x, w.y)) clear++;
            }
            if (clear == 0) return;
            if (clear == (nu + 1) * (nv + 1)) { result.Add(p); return; }
            if (depth >= 8) return;
            bool along = p.u1 - p.u0 > Math.Max(p.a1 - p.a0, p.b1 - p.b0);
            for (int side = 0; side < 2; side++)
            {
                var q = p;
                if (along)
                {
                    float mid = (p.u0 + p.u1) * .5f, lo = (p.a0 + p.b0) * .5f, hi = (p.a1 + p.b1) * .5f;
                    if (side == 0) { q.u1 = mid; q.b0 = lo; q.b1 = hi; } else { q.u0 = mid; q.a0 = lo; q.a1 = hi; }
                }
                else if (side == 0) { q.a1 = (p.a0 + p.a1) * .5f; q.b1 = (p.b0 + p.b1) * .5f; }
                else { q.a0 = (p.a0 + p.a1) * .5f; q.b0 = (p.b0 + p.b1) * .5f; }
                float length = q.u1 - q.u0;
                if (length < MinimumEdge || Math.Min(q.a1 - q.a0, q.b1 - q.b0) < 24f || length * (q.a1 - q.a0 + q.b1 - q.b0) * .5f < MinimumArea) continue;
                q.lowerNormal = 1f / (float)Math.Sqrt(1f + Math.Pow((q.b0 - q.a0) / length, 2));
                q.upperNormal = 1f / (float)Math.Sqrt(1f + Math.Pow((q.b1 - q.a1) / length, 2));
                ClipCover(d, q, result, depth + 1);
            }
        }

        // Chords of a convex intersection lie inside it: tip removal never grows crops across a neighbour's land.
        static Parcel Fit(List<Vector2> polygon)
        {
            if (polygon.Count < 3) return default;
            float left = float.MaxValue, right = float.MinValue;
            foreach (var p in polygon) { left = Math.Min(left, p.x); right = Math.Max(right, p.x); }
            if (right - left < MinimumEdge) return default;
            Span(polygon, left, out float a0, out float a1); Span(polygon, right, out float b0, out float b1);
            if (a1 - a0 < 24f)
            {
                float a = left, b = (left + right) * .5f;
                Span(polygon, b, out float lo, out float hi);
                if (hi - lo < 24f) return default;
                for (int i = 0; i < 12; i++) { float m = (a + b) * .5f; Span(polygon, m, out lo, out hi); if (hi - lo < 24f) a = m; else b = m; }
                left = b + 8f;
            }
            if (b1 - b0 < 24f)
            {
                float a = (left + right) * .5f, b = right;
                Span(polygon, a, out float lo, out float hi);
                if (hi - lo < 24f) return default;
                for (int i = 0; i < 12; i++) { float m = (a + b) * .5f; Span(polygon, m, out lo, out hi); if (hi - lo < 24f) b = m; else a = m; }
                right = a - 8f;
            }
            float length = right - left;
            if (length < MinimumEdge) return default;
            Span(polygon, left, out a0, out a1); Span(polygon, right, out b0, out b1);
            // Bound both side slopes to 1: every corner is at least 45 degrees, comfortably above 35.
            a0 = Math.Max(a0, b0 - length); b0 = Math.Max(b0, a0 - length);
            a1 = Math.Min(a1, b1 + length); b1 = Math.Min(b1, a1 + length);
            float width = Math.Min(a1 - a0, b1 - b0);
            if (width < 24f || length * (a1 - a0 + b1 - b0) * .5f < MinimumArea) return default;
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

        // Broad, continuous angle changes correlate farms; a nearby through-road supplies its closest parallel axis.
        float Direction(Vector2 site)
        {
            float angle = .4f + Value(site.x / 2800f, site.y / 2800f) * 1.15f;
            if (!roadCells.TryGetValue(Key(Cell(site.x), Cell(site.y)), out var nearby)) return angle;
            float best = 500f * 500f, roadAngle = angle;
            foreach (var s in nearby)
            {
                float dx = s.bx - s.ax, dz = s.bz - s.az, length = dx * dx + dz * dz;
                if (length < 1f) continue;
                float t = Math.Max(0f, Math.Min(1f, ((site.x - s.ax) * dx + (site.y - s.az) * dz) / length));
                float ex = site.x - s.ax - dx * t, ez = site.y - s.az - dz * t, distance = ex * ex + ez * ez;
                if (distance >= best) continue;
                best = distance; roadAngle = (float)Math.Atan2(dz, dx);
            }
            if (best >= 500f * 500f) return angle;
            roadAngle += (float)Math.Round((angle - roadAngle) / (Math.PI * .5)) * (float)(Math.PI * .5);
            return roadAngle - (float)Math.Floor(roadAngle / Math.PI) * (float)Math.PI;
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

        // Preserve the old jittered sites so before/after topology audits compare the same 500 districts.
        public Vector2 Site(int x, int z)
        { int h = Hash(x, z); return new Vector2((x + .15f + .7f * (h & 1023) / 1023f) * DistrictSize, (z + .15f + .7f * (h >> 10 & 1023) / 1023f) * DistrictSize); }

        // Stable integer hashing is independent of thread scheduling and sector-local RNG consumption.
        int Hash(int x, int z)
        { unchecked { int h = x * 73856093 ^ z * 19349663 ^ seed * 83492791; h ^= h >> 13; h *= 0x5bd1e995; return h & 0x7fffffff; } }
        // Signed cells cover both halves of the theatre without truncation toward zero.
        static int Cell(float p) => (int)Math.Floor(p / DistrictSize);
        // Pair signed grid coordinates without collisions.
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        // Managed vector operations also run in the standalone harness.
        static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        // Normalize only district edges, which are constructed with nonzero length.
        static Vector2 Unit(Vector2 p) { float n = (float)Math.Sqrt(Dot(p, p)); return n > .00001f ? p / n : Vector2.zero; }
    }
}

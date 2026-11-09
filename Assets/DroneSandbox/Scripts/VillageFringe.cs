using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // A village uses its spare ground: small paddocks, vegetable plots, rough grass and small orchards instead of one mown
    // lawn round the houses, so the green between a village and its fields breaks into land that is visibly used.
    public sealed partial class VillagePlan
    {
        // Behind the outer gardens first, then the rest of the settlement's open ground in its street frame. Only dry
        // pasture or village ground is used, never crops, wood, rock or water, so fields and woods keep their land.
        void Fringe(int owner, FarDetailData.Segment[] roads, Vector2 anchor, float reach, bool town)
        {
            var rng = new Random(unchecked(seed * 6007 + owner * 92821));
            var candidates = new List<int>();
            for (int i = 0; i < plots.Count; i++)
            {
                var q = plots[i];
                if (q.owner == owner && !q.square && !q.church && q.landUse == 0) candidates.Add(i);
            }
            if (candidates.Count == 0) return;
            candidates.Sort((a, b) => (plots[b].front - anchor).sqrMagnitude.CompareTo((plots[a].front - anchor).sqrMagnitude));
            int limit = Math.Min(town ? 56 : 44, 10 + candidates.Count), placed = 0, orchards = 0, gardens = 0;
            // Only the roads and lanes within reach of this settlement's land can cross its spare plots.
            float bound = reach * 1.5f + (town ? 800f : 300f);
            roads = Around(roads, anchor, bound);
            var local = Around(lanes, anchor, bound);
            for (int k = 0; k < candidates.Count && placed < limit / 2; k++)
            {
                var house = plots[candidates[k]];
                Vector2 front = house.front + house.outward * (house.depth + 1f) + house.along * (float)(rng.NextDouble() * 12.0 - 6.0);
                if (TryFringe(owner, house, front, house.along, house.outward, rng, roads, local, ref orchards, ref gardens)) placed++;
            }
            // The street frame of the innermost property orients the remaining paddocks and rough ground, nearest first,
            // a little beyond the houses so the long open grass round the village is broken up too.
            var axis = plots[candidates[candidates.Count - 1]];
            float cell = 46f;
            reach *= 1.15f;
            var spots = new List<Vector2>();
            for (float y = -reach; y <= reach; y += cell)
                for (float x = -reach; x <= reach; x += cell)
                    if (x * x + y * y <= reach * reach) spots.Add(new Vector2(x, y));
            spots.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
            foreach (var at in spots)
            {
                if (placed >= limit) break;
                Vector2 c = anchor + axis.along * (at.x + (float)rng.NextDouble() * 10f - 5f) + axis.outward * (at.y + (float)rng.NextDouble() * 10f - 5f);
                if (Near(c, 22f)) continue;
                // A slight skew from the street frame keeps neighbouring enclosures from lining up like a grid.
                float turn = (float)(rng.NextDouble() - .5) * .3f;
                float cos = (float)Math.Cos(turn), sin = (float)Math.Sin(turn);
                Vector2 along = new Vector2(axis.along.x * cos - axis.along.y * sin, axis.along.x * sin + axis.along.y * cos);
                Vector2 outward = new Vector2(-along.y, along.x) * Math.Sign(Vector2.Dot(axis.outward, new Vector2(-axis.along.y, axis.along.x)));
                if (TryFringe(owner, axis, c - outward * 25f, along, outward, rng, roads, local, ref orchards, ref gardens)) placed++;
            }
        }

        // One paddock, vegetable plot, orchard or patch of rough grass; rejected if it would overlap a property, cross a
        // road or use farmland. Paddocks come in small and large, so neighbouring enclosures differ in size.
        bool TryFringe(int owner, VillagePlot near, Vector2 front, Vector2 along, Vector2 outward, Random rng,
            FarDetailData.Segment[] roads, FarDetailData.Segment[] local, ref int orchards, ref int gardens)
        {
            float roll = (float)rng.NextDouble();
            int use = roll < .4f ? 6 : roll < .62f && gardens < 3 ? 9 : roll < .86f || orchards >= 2 ? 8 : 7;
            var p = new VillagePlot
            {
                owner = owner, settlement = owner, town = near.town, along = along, outward = outward, front = front,
                frontage = 32f + (float)rng.NextDouble() * 30f, depth = 28f + (float)rng.NextDouble() * 24f, roadWidth = near.roadWidth,
                landUse = use, square = true, identity = rng.Next(), scale = 1f, pathWidth = 1.2f, driveWidth = 3f
            };
            if (use == 7) { p.frontage = Math.Min(p.frontage, 44f); p.depth = Math.Min(p.depth, 38f); }
            if (use == 9) { p.frontage = 16f + (p.frontage - 32f) * .45f; p.depth = 14f + (p.depth - 28f) * .45f; }
            if (use == 6 && (p.identity >> 9) % 3 == 0) { p.frontage *= .62f; p.depth *= .7f; }
            // Paddocks have post-and-rail fencing with a field gate and sometimes a tapered side; orchards are hedged or open;
            // about half the vegetable plots have a rail fence against rabbits.
            p.boundary = use == 6 ? 2 : use == 7 && (p.identity & 1) == 0 ? 1 : use == 9 && (p.identity & 2) == 0 ? 2 : 0;
            p.shape = use == 6 && (p.identity >> 2) % 3 == 0 ? 1 : 0;
            p.open = use == 6 && (p.identity >> 4) % 3 == 0 ? 4 : 0;
            if (Overlap(p) || CrossesRoad(p, roads) || CrossesRoad(p, local) || !Spare(p)) return false;
            Add(p);
            if (use == 7) orchards++;
            if (use == 9) gardens++;
            return true;
        }

        // The segments whose bounds, with their widths, reach the square of half-size r round a point.
        static FarDetailData.Segment[] Around(IList<FarDetailData.Segment> source, Vector2 c, float r)
        {
            var list = new List<FarDetailData.Segment>();
            foreach (var s in source)
            {
                float pad = Math.Max(s.widthA, s.widthB) + r;
                if (Math.Max(s.ax, s.bx) + pad < c.x || Math.Min(s.ax, s.bx) - pad > c.x || Math.Max(s.az, s.bz) + pad < c.y || Math.Min(s.az, s.bz) - pad > c.y) continue;
                list.Add(s);
            }
            return list.ToArray();
        }

        // Spare land is pasture or village ground, dry and unreserved.
        static bool Spare(VillagePlot p)
        {
            for (int j = 0; j <= 3; j++)
                for (int i = -2; i <= 2; i++)
                {
                    Vector2 q = p.front + p.along * (i * p.frontage * .25f) + p.outward * (p.depth * j / 3f);
                    var c = WorldPlan.CoverAt(q.x, q.y);
                    if (c != Cover.Pasture && c != Cover.Village && c != Cover.Town || WorldPlan.FeatureReserved(q, 4f)) return false;
                }
            return true;
        }

        // How far a point lies outside every plot (house, garden or fringe land) near it, up to reach metres; 0 inside one.
        public float Clearance(Vector2 p, float reach)
        {
            float best = reach;
            int x0 = (int)Math.Floor((p.x - reach) / 128f), x1 = (int)Math.Floor((p.x + reach) / 128f);
            int z0 = (int)Math.Floor((p.y - reach) / 128f), z1 = (int)Math.Floor((p.y + reach) / 128f);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (cells.TryGetValue(((long)x << 32) ^ (uint)z, out var list))
                        foreach (int i in list)
                        {
                            plots[i].Coordinates(p, out float u, out float v);
                            float du = Math.Max(0f, Math.Abs(u) - plots[i].frontage * .5f), dv = v < 0f ? -v : Math.Max(0f, v - plots[i].depth);
                            if (du >= best || dv >= best) continue;
                            best = Math.Min(best, (float)Math.Sqrt(du * du + dv * dv));
                            if (best <= 0f) return 0f;
                        }
            return best;
        }

        // A settlement's gardens closed on a 16 m lattice: a gap between its properties narrower than about 80 m (the
        // strip along a railway, a block's open middle, the ground between two garden rows) is the settlement's own
        // grass, and only land open to the country round it is farmed with that country (FarDetailData.SettlementOpen).
        sealed class Enclosure
        {
            public float x0, z0;
            public int w, h;
            public bool[] open;
        }

        const float EnclosureCell = 16f;
        const int Closing = 2; // cells
        readonly List<Enclosure> enclosures = new List<Enclosure>();

        // Mark the cells each settlement's gardens and town uses touch (not its paddocks, orchards or rough grass), grow
        // them by the closing radius, flood the country in from the border round the grown cells, and grow that back:
        // what the flood never reaches is a gap the settlement closes round. Two passes over all the plots.
        void Enclose(int settlements)
        {
            float[] minX = new float[settlements], minZ = new float[settlements], maxX = new float[settlements], maxZ = new float[settlements];
            for (int s = 0; s < settlements; s++) { minX[s] = minZ[s] = float.MaxValue; maxX[s] = maxZ[s] = float.MinValue; }
            foreach (var p in plots)
            {
                if (p.landUse >= 6 || (uint)p.owner >= (uint)settlements) continue;
                for (int c = 0; c < 4; c++)
                {
                    Vector2 q = p.Point((c & 1) == 0 ? -p.frontage * .5f : p.frontage * .5f, (c & 2) == 0 ? 0f : p.depth);
                    minX[p.owner] = Math.Min(minX[p.owner], q.x); minZ[p.owner] = Math.Min(minZ[p.owner], q.y);
                    maxX[p.owner] = Math.Max(maxX[p.owner], q.x); maxZ[p.owner] = Math.Max(maxZ[p.owner], q.y);
                }
            }
            var grids = new Enclosure[settlements];
            var marks = new bool[settlements][];
            float pad = (Closing + 2) * EnclosureCell;
            for (int s = 0; s < settlements; s++)
            {
                if (minX[s] > maxX[s]) continue;
                var e = grids[s] = new Enclosure { x0 = minX[s] - pad, z0 = minZ[s] - pad };
                e.w = (int)Math.Ceiling((maxX[s] + pad - e.x0) / EnclosureCell); e.h = (int)Math.Ceiling((maxZ[s] + pad - e.z0) / EnclosureCell);
                marks[s] = new bool[e.w * e.h];
            }
            foreach (var p in plots)
            {
                if (p.landUse >= 6 || (uint)p.owner >= (uint)settlements || grids[p.owner] == null) continue;
                var e = grids[p.owner]; var built = marks[p.owner];
                float ax = Math.Abs(p.along.x) * p.frontage * .5f + Math.Abs(p.outward.x) * p.depth * .5f + EnclosureCell * .5f;
                float az = Math.Abs(p.along.y) * p.frontage * .5f + Math.Abs(p.outward.y) * p.depth * .5f + EnclosureCell * .5f;
                Vector2 c = p.Point(0f, p.depth * .5f);
                int i0 = Math.Max(0, (int)Math.Floor((c.x - ax - e.x0) / EnclosureCell - .5f)), i1 = Math.Min(e.w - 1, (int)Math.Ceiling((c.x + ax - e.x0) / EnclosureCell - .5f));
                int j0 = Math.Max(0, (int)Math.Floor((c.y - az - e.z0) / EnclosureCell - .5f)), j1 = Math.Min(e.h - 1, (int)Math.Ceiling((c.y + az - e.z0) / EnclosureCell - .5f));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                        if (p.Contains(new Vector2(e.x0 + (i + .5f) * EnclosureCell, e.z0 + (j + .5f) * EnclosureCell), EnclosureCell * .5f)) built[j * e.w + i] = true;
            }
            for (int s = 0; s < settlements; s++)
                if (grids[s] != null) Enclose(grids[s], marks[s]);
        }

        // One settlement's closing: grow, flood the country in from the border, then grow that back a cell further, so
        // the open country reaches the outer gardens' own cells and only SettlementOpen's 2-6 m margin stays grass.
        void Enclose(Enclosure e, bool[] built)
        {
            var grown = Grow(built, e.w, e.h, Closing);
            var country = new bool[built.Length];
            var queue = new Queue<int>();
            for (int k = 0; k < built.Length; k++)
            {
                int i = k % e.w, j = k / e.w;
                if ((i == 0 || j == 0 || i == e.w - 1 || j == e.h - 1) && !grown[k]) { country[k] = true; queue.Enqueue(k); }
            }
            while (queue.Count > 0)
            {
                int k = queue.Dequeue(), i = k % e.w, j = k / e.w;
                for (int n = 0; n < 4; n++)
                {
                    int x = i + (n == 0 ? 1 : n == 1 ? -1 : 0), z = j + (n == 2 ? 1 : n == 3 ? -1 : 0);
                    if ((uint)x >= (uint)e.w || (uint)z >= (uint)e.h) continue;
                    int m = z * e.w + x;
                    if (country[m] || grown[m]) continue;
                    country[m] = true; queue.Enqueue(m);
                }
            }
            e.open = Grow(country, e.w, e.h, Closing + 1);
            enclosures.Add(e);
        }

        // Every cell within a radius (in cells) of a marked one; only marked cells on the edge of their region need stamping.
        static bool[] Grow(bool[] source, int w, int h, int radius)
        {
            var result = (bool[])source.Clone();
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int k = j * w + i;
                    if (!source[k] || i > 0 && i < w - 1 && j > 0 && j < h - 1 && source[k - 1] && source[k + 1] && source[k - w] && source[k + w]) continue;
                    for (int z = Math.Max(0, j - radius); z <= Math.Min(h - 1, j + radius); z++)
                        for (int x = Math.Max(0, i - radius); x <= Math.Min(w - 1, i + radius); x++)
                            if ((x - i) * (x - i) + (z - j) * (z - j) <= radius * radius) result[z * w + x] = true;
                }
            return result;
        }

        // How open a point's ground is to the country round its settlement: 1 beyond every settlement and on its outer
        // land, 0 in a gap it closes round, blended between the cells' middles so the change is never a 16 m step.
        public float Openness(Vector2 p)
        {
            float result = 1f;
            foreach (var e in enclosures)
            {
                float fx = (p.x - e.x0) / EnclosureCell - .5f, fz = (p.y - e.z0) / EnclosureCell - .5f;
                if (fx < -1f || fz < -1f || fx > e.w || fz > e.h) continue;
                int i = (int)Math.Floor(fx), j = (int)Math.Floor(fz);
                float u = fx - i, v = fz - j;
                float Cell(int x, int z) => (uint)x < (uint)e.w && (uint)z < (uint)e.h && !e.open[z * e.w + x] ? 0f : 1f;
                float top = Cell(i, j) + (Cell(i + 1, j) - Cell(i, j)) * u, bottom = Cell(i, j + 1) + (Cell(i + 1, j + 1) - Cell(i, j + 1)) * u;
                result = Math.Min(result, top + (bottom - top) * v);
            }
            return result;
        }

        // Inside a paddock, orchard or rough patch by at least inset metres; village props may stand there.
        public bool InFringe(Vector2 p, float inset)
        {
            long key = ((long)(int)Math.Floor(p.x / 128f) << 32) ^ (uint)(int)Math.Floor(p.y / 128f);
            if (cells.TryGetValue(key, out var list))
                foreach (int i in list)
                    if (plots[i].landUse >= 6 && plots[i].Contains(p, -inset)) return true;
            return false;
        }
    }
}

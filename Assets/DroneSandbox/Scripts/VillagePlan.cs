using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // A street parcel is shared by the scenery builder and both distant painters, in physical metres.
    public struct VillagePlot
    {
        public Vector2 front, along, outward;
        public float frontage, depth, setback;
        public int owner, style;
        public bool town, farm, church, square;
        public string model;
        public Vector2 House => front + outward * setback;

        // Signed distances from the side and front of this irregular street parcel.
        public void Coordinates(Vector2 p, out float u, out float v)
        {
            Vector2 d = p - front;
            u = Vector2.Dot(d, along);
            v = Vector2.Dot(d, outward);
        }

        // Plots own their whole garden, including the narrow boundary line, rather than a circle round the roof.
        public bool Contains(Vector2 p, float margin = 0f)
        {
            Coordinates(p, out float u, out float v);
            return Math.Abs(u) <= frontage * .5f + margin && v >= -margin && v <= depth + margin;
        }

        // Both the garden mesh and tile detail control use this property layout: lawn, soil or gravel.
        public int GroundMaterial(float u, float v)
        {
            if (square)
                return 2;
            float half = frontage * .5f;
            if (Math.Abs(u) < 1.2f && v < setback + 8f)
                return 2;
            if (Math.Abs(u) < half - 2f && v > setback + 4f && v < Math.Min(depth - 2f, setback + (farm ? 25f : 12f)))
                return farm ? 2 : 1;
            if (!church && !farm && u > -half + 2f && u < -1f)
                for (float row = setback + 15f; row < depth - 4f; row += 2.8f)
                    if (v >= row && v < Math.Min(row + 1.6f, depth - 3f))
                        return 1;
            return 0;
        }
    }

    // Immutable road-facing village and town plots. World snapshots never consult sector-local random state.
    public sealed class VillagePlan
    {
        public readonly List<VillagePlot> plots = new List<VillagePlot>();
        public readonly List<FarDetailData.Segment> lanes = new List<FarDetailData.Segment>();
        readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        readonly int seed;
        // Real through-roads carry the frontage; short side streets and back lanes complete each settlement.
        public VillagePlan(int seed, FarDetailData.Segment[] roads, IReadOnlyList<WorldSettlement> settlements)
        {
            this.seed = seed;
            for (int owner = 0; owner < settlements.Count; owner++)
            {
                var s = settlements[owner];
                float nearest = float.MaxValue;
                Vector2 anchor = s.center, along = Vector2.right;
                foreach (var road in roads)
                {
                    Vector2 a = new Vector2(road.ax, road.az), b = new Vector2(road.bx, road.bz), d = b - a;
                    float t = d.sqrMagnitude > .01f ? Math.Max(0, Math.Min(1, Vector2.Dot(s.center - a, d) / d.sqrMagnitude)) : 0f;
                    Vector2 q = a + d * t;
                    float distance = (q - s.center).sqrMagnitude;
                    if (distance >= nearest || d.sqrMagnitude < 1f)
                        continue;
                    nearest = distance;
                    anchor = q;
                    along = d.normalized;
                }

                float reach = s.town ? s.radius * .85f : Math.Min(s.radius * .85f, 220f);
                var street = new List<FarDetailData.Segment>();
                foreach (var road in roads)
                {
                    Vector2 mid = new Vector2((road.ax + road.bx) * .5f, (road.az + road.bz) * .5f);
                    if ((mid - anchor).sqrMagnitude < reach * reach)
                        street.Add(road);
                }

                int firstLane = lanes.Count;
                // One perpendicular village lane, or a town street grid with lanes behind the gardens.
                Vector2 across = new Vector2(-along.y, along.x);
                int crossCount = s.town ? 2 * (int)(reach / 120f) + 1 : 1;
                for (int i = 0; i < crossCount; i++)
                {
                    float offset = (i - (crossCount - 1) * .5f) * 120f;
                    Vector2 at = anchor + along * offset;
                    float length = s.town ? (float)Math.Sqrt(Math.Max(0f, reach * reach - offset * offset)) : 105f;
                    AddLane(street, at - across * length, at + across * length, owner * 128 + i, 2f);
                    if (s.town && Math.Abs(offset) > 1f)
                        AddLane(street, anchor + across * offset - along * length, anchor + across * offset + along * length, owner * 128 + 32 + i, 2f);
                    if (s.town)
                    {
                        float back = offset + 60f;
                        if (Math.Abs(back) < reach)
                        {
                            float end = (float)Math.Sqrt(reach * reach - back * back);
                            AddLane(null, anchor + across * back - along * end, anchor + across * back + along * end, owner * 128 + 64 + i, 1.5f);
                        }
                    }
                }

                bool church = false, square = false;
                var localLanes = lanes.GetRange(firstLane, lanes.Count - firstLane).ToArray();
                foreach (var road in street)
                {
                    Vector2 a = new Vector2(road.ax, road.az), b = new Vector2(road.bx, road.bz), dir = (b - a).normalized;
                    float length = (b - a).magnitude;
                    // A route's 120 m blocks keep frontage stable when the same road is cropped elsewhere.
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int first = (int)Math.Floor(road.walked / 120f), last = (int)Math.Floor((road.walked + length) / 120f);
                        for (int block = first; block <= last; block++)
                        {
                            var rng = new Random(unchecked(seed * 7919 + road.route * 104729 + block * 977 + side * 31));
                            float station = block * 120f;
                            int count = s.town ? 4 : 6;
                            float remaining = 120f;
                            for (int k = 0; k < count; k++)
                            {
                                float width = k == count - 1 ? remaining : s.town ? 30f : 16f + (float)rng.NextDouble() * 8f;
                                float mid = station + width * .5f;
                                station += width;
                                remaining -= width;
                                if (mid < road.walked || mid >= road.walked + length)
                                    continue;
                                Vector2 outward = new Vector2(-dir.y, dir.x) * side;
                                var p = new VillagePlot
                                {
                                    front = a + dir * (mid - road.walked) + outward * (road.widthA + 1.4f),
                                    along = dir,
                                    outward = outward,
                                    frontage = Math.Max(12f, Math.Min(30f, width)),
                                    depth = s.town ? 35f + (float)rng.NextDouble() * 15f : 25f + (float)rng.NextDouble() * 35f,
                                    setback = s.town ? 7f : 9f + (float)rng.NextDouble() * 3f,
                                    owner = owner,
                                    town = s.town,
                                    style = rng.Next(4)
                                };
                                float distance = (p.front - anchor).magnitude;
                                if (distance > reach + 30f)
                                    continue;
                                p.model = s.town && distance < 300f ? "Scenery_Townhouse_Row" : "Scenery_House_" + (p.style == 2 ? "D" : p.style == 1 ? "B" : "A");
                                if (!church && distance < 65f)
                                {
                                    p.church = true;
                                    p.frontage = 30f;
                                    p.depth = 55f;
                                    p.setback = 19f;
                                    p.model = "Scenery_Church";
                                }
                                else if (s.town && !square && distance < 80f)
                                {
                                    p.square = true;
                                    p.depth = 34f;
                                    p.model = null;
                                }
                                else if (distance > reach * .75f && p.style == 0)
                                {
                                    p.farm = !s.town;
                                    p.depth = 60f;
                                    p.frontage = 30f;
                                    p.model = s.town ? ((road.route + block + k) % 2 == 0 ? "Scenery_OfficeBlock" : "Scenery_Warehouse") : "Scenery_House_A";
                                    p.setback = s.town ? 18f : 11f;
                                }

                                // At town corners a shallower rear garden preserves a usable street frontage.
                                bool clear = !Overlap(p) && !CrossesRoad(p, localLanes) && !CrossesRoad(p, roads) && Dry(p);
                                while (!clear && p.town && !p.church && !p.square && p.depth > 25f)
                                {
                                    p.depth = Math.Max(25f, p.depth - 5f);
                                    clear = !Overlap(p) && !CrossesRoad(p, localLanes) && !CrossesRoad(p, roads) && Dry(p);
                                }
                                if (!clear)
                                    continue;
                                Add(p);
                                church |= p.church;
                                square |= p.square;
                            }
                        }
                    }
                }
            }
        }

        // Private access lanes are captured with the same vector widths used by tile and world painting.
        void AddLane(List<FarDetailData.Segment> streets, Vector2 a, Vector2 b, int id, float width)
        {
            var s = new FarDetailData.Segment
            {
                ax = a.x,
                az = a.y,
                bx = b.x,
                bz = b.y,
                widthA = width,
                widthB = width,
                route = 10000 + id,
                kind = (int)RoadClass.Track
            };
            lanes.Add(s);
            streets?.Add(s);
        }

        // Reserve dry, non-industrial land before any render object exists.
        static bool Dry(VillagePlot p)
        {
            for (int j = 0; j <= 3; j++)
                for (int i = -1; i <= 1; i++)
                {
                    Vector2 q = p.front + p.along * (i * p.frontage * .5f) + p.outward * (p.depth * j / 3f);
                    var c = WorldPlan.CoverAt(q.x, q.y);
                    if (c == Cover.Water || c == Cover.Wetland || c == Cover.Forest || c == Cover.Rock || WorldPlan.FeatureReserved(q, 0f))
                        return false;
                }

            return true;
        }

        // Side streets and through-roads keep their complete verges; a plot cannot straddle a junction.
        static bool CrossesRoad(VillagePlot p, FarDetailData.Segment[] roads)
        {
            foreach (var r in roads)
            {
                Vector2 a = new Vector2(r.ax, r.az), b = new Vector2(r.bx, r.bz);
                p.Coordinates(a, out float au, out float av);
                p.Coordinates(b, out float bu, out float bv);
                float pad = Math.Max(r.widthA, r.widthB) + .4f;
                if (Math.Max(av, bv) < -pad || Math.Min(av, bv) > p.depth + pad || Math.Max(au, bu) < -p.frontage * .5f - pad || Math.Min(au, bu) > p.frontage * .5f + pad)
                    continue;
                int n = Math.Max(1, (int)Math.Ceiling((b - a).magnitude / 4f));
                for (int k = 0; k <= n; k++)
                    if (p.Contains(a + (b - a) * (k / (float)n), pad))
                        return true;
            }

            return false;
        }

        // Four separating axes prevent interleaved plots at curved streets, while allowing shared boundaries.
        bool Overlap(VillagePlot p)
        {
            Vector2 c = p.front + p.outward * (p.depth * .5f);
            float r = (p.depth + p.frontage) * .5f + 2f;
            for (int z = (int)Math.Floor((c.y - r) / 128f); z <= (int)Math.Floor((c.y + r) / 128f); z++)
                for (int x = (int)Math.Floor((c.x - r) / 128f); x <= (int)Math.Floor((c.x + r) / 128f); x++)
                    if (cells.TryGetValue(((long)x << 32) ^ (uint)z, out var nearby))
                        foreach (int index in nearby) if (Intersects(p, plots[index])) return true;
            return false;
        }

        // The local builder uses the same rectangle test to keep gardens clear at legacy street junctions.
        public static bool Intersects(VillagePlot p, VillagePlot q)
        {
            Vector2 d = q.front + q.outward * (q.depth * .5f) - p.front - p.outward * (p.depth * .5f);
            if (d.sqrMagnitude > 150f * 150f)
                return false;
            for (int k = 0; k < 4; k++)
            {
                Vector2 axis = k == 0 ? p.along : k == 1 ? p.outward : k == 2 ? q.along : q.outward;
                float a = Math.Abs(Vector2.Dot(axis, p.along)) * p.frontage * .5f + Math.Abs(Vector2.Dot(axis, p.outward)) * p.depth * .5f;
                float b = Math.Abs(Vector2.Dot(axis, q.along)) * q.frontage * .5f + Math.Abs(Vector2.Dot(axis, q.outward)) * q.depth * .5f;
                if (Math.Abs(Vector2.Dot(d, axis)) >= a + b - .1f)
                    return false;
            }

            return true;
        }

        // A 128 m bin keeps garden painting independent of the number of settlements in the theatre.
        void Add(VillagePlot p)
        {
            int index = plots.Count;
            plots.Add(p);
            Vector2 c = p.front + p.outward * (p.depth * .5f);
            float r = p.depth * .5f + p.frontage * .5f + 2f;
            for (int z = (int)Math.Floor((c.y - r) / 128f); z <= (int)Math.Floor((c.y + r) / 128f); z++)
                for (int x = (int)Math.Floor((c.x - r) / 128f); x <= (int)Math.Floor((c.x + r) / 128f); x++)
                {
                    long key = ((long)x << 32) ^ (uint)z;
                    if (!cells.TryGetValue(key, out var list))
                        cells.Add(key, list = new List<int>());
                    list.Add(index);
                }
        }

        // Snapshot lookup is read-only and safe on detail-map workers.
        public bool At(Vector2 p, out VillagePlot plot)
        {
            long key = ((long)(int)Math.Floor(p.x / 128f) << 32) ^ (uint)(int)Math.Floor(p.y / 128f);
            if (cells.TryGetValue(key, out var list))
                foreach (int i in list)
                    if (plots[i].Contains(p))
                    {
                        plot = plots[i];
                        return true;
                    }

            plot = default;
            return false;
        }

        // Scenery and field exclusions follow actual gardens, with a bounded bin search for positive margins.
        public bool Near(Vector2 p, float margin)
        {
            float pad = Math.Max(0f, margin);
            int x0 = (int)Math.Floor((p.x - pad) / 128f), x1 = (int)Math.Floor((p.x + pad) / 128f);
            int z0 = (int)Math.Floor((p.y - pad) / 128f), z1 = (int)Math.Floor((p.y + pad) / 128f);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (cells.TryGetValue(((long)x << 32) ^ (uint)z, out var list))
                        foreach (int i in list)
                            if (plots[i].Contains(p, margin))
                                return true;
            return false;
        }
    }
}

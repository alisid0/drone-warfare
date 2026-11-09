using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // A street parcel is shared by the scenery builder and both distant painters, in physical metres.
    public partial struct VillagePlot
    {
        public Vector2 front, along, outward;
        public float frontage, depth, setback;
        public int owner, style;
        public int landUse; // 1 allotments, 2 playing field, 3 cemetery, 4 depot, 5 edge farm, 6 paddock, 7 orchard, 8 rough grass, 9 vegetable plot
        public bool town, farm, church, square;
        public string model;
        public bool old, core, cultivated, shed, logs, untended, roofChanged;
        public bool gable; // the house stands gable-end to the street, as about one newer house in five does
        // Town plots: apartment-estate ground (blocks in green space, not on the street), a paved yard or forecourt, a
        // terraced house sharing its side walls, and a building behind a street property, reached past it.
        public bool estate, yard, attached, backland;
        public int shape, palette, boundary, identity, settlement;
        public int open; // boundary runs left out: 1 front, 2 right side, 4 rear, 8 left side
        public float scale, pathWidth, driveWidth, roadWidth;
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
            Limits(v, out float lo, out float hi);
            return u >= lo - margin && u <= hi + margin && v >= -margin && v <= depth + margin;
        }

        // Both the garden mesh and tile detail control use this property layout: lawn, soil or gravel.
        public int GroundMaterial(float u, float v) => UseAt(u, v);
    }

    // Immutable road-facing village and town plots. World snapshots never consult sector-local random state.
    public sealed partial class VillagePlan
    {
        // One tree of a town's avenues, with the settlement that planted it.
        public struct StreetTree
        {
            public Vector2 at;
            public int owner;
        }

        public readonly List<VillagePlot> plots = new List<VillagePlot>();
        public readonly List<FarDetailData.Segment> lanes = new List<FarDetailData.Segment>();
        public readonly List<StreetTree> trees = new List<StreetTree>();
        readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        readonly int seed;
        // Real through-roads carry the frontage; short side streets and back lanes complete each settlement.
        public VillagePlan(int seed, FarDetailData.Segment[] roads, IReadOnlyList<WorldSettlement> settlements)
        {
            this.seed = seed;
            var anchors = new Vector2[settlements.Count];
            for (int owner = 0; owner < settlements.Count; owner++)
            {
                var s = settlements[owner];
                float nearest = float.MaxValue;
                Vector2 anchor = s.center;
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
                }

                anchors[owner] = anchor;
                if (!s.town) Grow(owner, s, roads, anchor);
                else Town(owner, s, roads, anchor);
            }
            // Spare ground is taken only after every settlement's houses, so it can never displace a property.
            for (int owner = 0; owner < settlements.Count; owner++)
                Fringe(owner, roads, anchors[owner], settlements[owner].radius, settlements[owner].town);
            Enclose(settlements.Count);
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
                kind = (int)(width >= 1.9f ? RoadClass.Road : RoadClass.Track)
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
        // Paddocks, orchards and rough grass are open ground, not gardens: woods, scrub and units may come near them.
        public bool Near(Vector2 p, float margin)
        {
            float pad = Math.Max(0f, margin);
            int x0 = (int)Math.Floor((p.x - pad) / 128f), x1 = (int)Math.Floor((p.x + pad) / 128f);
            int z0 = (int)Math.Floor((p.y - pad) / 128f), z1 = (int)Math.Floor((p.y + pad) / 128f);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (cells.TryGetValue(((long)x << 32) ^ (uint)z, out var list))
                        foreach (int i in list)
                            if (plots[i].landUse < 6 && plots[i].Contains(p, margin))
                                return true;
            return false;
        }
    }
}

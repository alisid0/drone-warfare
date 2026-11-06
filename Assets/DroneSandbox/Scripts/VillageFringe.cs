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
            var local = lanes.ToArray();
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

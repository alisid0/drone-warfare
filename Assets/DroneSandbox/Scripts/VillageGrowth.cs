using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Villages grow from an existing route, then acquire unequal side lanes and boundary farms.
    public sealed partial class VillagePlan
    {
        // Walk the old route in physical metres; bends and road connections survive resampling and sector cuts.
        void Grow(int owner, WorldSettlement settlement, FarDetailData.Segment[] roads, Vector2 anchor)
        {
            var rng = new Random(unchecked(seed * 3571 + owner * 104729));
            int route = -1; float nearest = float.MaxValue, station = 0f;
            foreach (var r in roads)
            {
                Vector2 a = new Vector2(r.ax, r.az), d = new Vector2(r.bx - r.ax, r.bz - r.az);
                float t = Math.Max(0f, Math.Min(1f, Vector2.Dot(anchor - a, d) / Math.Max(.01f, d.sqrMagnitude)));
                float distance = (anchor - a - d * t).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance; route = r.route; station = r.walked + d.magnitude * t;
            }
            float reach = Math.Min(220f, settlement.radius * .85f);
            var streets = new List<FarDetailData.Segment>();
            foreach (var r in roads)
                if (r.route == route && r.walked < station + reach && r.walked + new Vector2(r.bx - r.ax, r.bz - r.az).magnitude > station - reach)
                    streets.Add(r);
            streets.Sort((a, b) => a.walked.CompareTo(b.walked));
            int firstLane = lanes.Count;
            int branches = 2 + rng.Next(3);
            for (int branch = 0; branch < branches; branch++)
            {
                float at = station + ((branch + .35f + (float)rng.NextDouble() * .3f) / branches - .5f) * reach * 1.35f;
                if (!OnRoute(streets, at, out Vector2 start, out Vector2 direction, out _)) continue;
                Vector2 outwards = new Vector2(-direction.y, direction.x) * ((branch + owner) % 2 == 0 ? 1f : -1f);
                float length = 65f + (float)rng.NextDouble() * 100f;
                float period = 40f + (float)rng.NextDouble() * 50f, bend = 2f + (float)rng.NextDouble() * 4f;
                int n = (int)Math.Ceiling(length / 9f); Vector2 last = start;
                for (int i = 1; i <= n; i++)
                {
                    float walk = length * i / n;
                    Vector2 next = start + outwards * walk + direction * (bend * (1f - (float)Math.Cos(Math.PI * walk / period)) * .5f);
                    Cover cover = WorldPlan.CoverAt(next.x, next.y);
                    if (cover == Cover.Water || cover == Cover.Wetland || cover == Cover.Rock || cover == Cover.Forest || WorldPlan.FeatureReserved(next, 5f)) break;
                    AddLane(null, last, next, owner * 128 + branch, 2f);
                    var segment = lanes[lanes.Count - 1]; segment.walked = length * (i - 1) / n; lanes[lanes.Count - 1] = segment;
                    last = next;
                }
            }
            var local = lanes.GetRange(firstLane, lanes.Count - firstLane).ToArray();
            // Build close old frontage first; newer houses are spaced irregularly along the remaining road and lanes.
            Populate(streets, station - reach, station + reach, true, owner, anchor, reach, rng, roads, local);
            for (int branch = 0; branch < branches; branch++)
            {
                var line = new List<FarDetailData.Segment>();
                foreach (var r in local) if (r.route == 10000 + owner * 128 + branch) line.Add(r);
                if (line.Count == 0) continue;
                var end = line[line.Count - 1];
                Populate(line, 18f, end.walked + new Vector2(end.bx - end.ax, end.bz - end.az).magnitude - 6f,
                    false, owner, anchor, reach, rng, roads, local);
            }
            bool hasFarm = false; int edgePlot = -1; float outer = -1f;
            for (int i = 0; i < plots.Count; i++)
            {
                var p = plots[i]; if (p.owner != owner) continue;
                hasFarm |= p.farm;
                float distance = (p.front - anchor).sqrMagnitude;
                if (p.depth >= 38f && distance > outer) { outer = distance; edgePlot = i; }
            }
            if (!hasFarm && edgePlot >= 0)
            {
                var p = plots[edgePlot]; p.farm = true; p.cultivated = false; plots[edgePlot] = p;
            }
            VillageUse.Assign(plots, owner, seed, anchor);
        }

        // Route stations let a property span several short road segments without duplicating or dropping frontages.
        internal static bool OnRoute(List<FarDetailData.Segment> route, float station, out Vector2 point, out Vector2 along, out float width)
        {
            foreach (var r in route)
            {
                Vector2 d = new Vector2(r.bx - r.ax, r.bz - r.az); float length = d.magnitude;
                if (station < r.walked || station > r.walked + length || length < .01f) continue;
                along = d / length; point = new Vector2(r.ax, r.az) + along * (station - r.walked);
                width = r.widthA + (r.widthB - r.widthA) * (station - r.walked) / length; return true;
            }
            point = along = default; width = 0f; return false;
        }

        // Frontage grows narrower towards the old core; loose new plots and boundary farms keep their own gates.
        void Populate(List<FarDetailData.Segment> street, float low, float high, bool oldStreet, int owner, Vector2 core,
            float reach, Random rng, FarDetailData.Segment[] roads, FarDetailData.Segment[] local)
        {
            foreach (int side in new[] { -1, 1 })
            {
                float next = low + 10f + (float)rng.NextDouble() * 12f;
                while (next < high)
                {
                    if (!OnRoute(street, next, out Vector2 at, out Vector2 dir, out float roadWidth)) { next += 8f; continue; }
                    float distance = (at - core).magnitude;
                    bool old = oldStreet && distance < reach * .65f;
                    bool farm = distance > reach * .79f && rng.NextDouble() < .3;
                    float width = farm ? 36f : old ? 12f + (float)rng.NextDouble() * 2.2f : 21f + (float)rng.NextDouble() * 11f;
                    // Advance by full slots, then centre each roof in its slot; unequal widths cannot overlap.
                    if (!OnRoute(street, next + width * .5f, out at, out dir, out roadWidth)) break;
                    Vector2 outward = new Vector2(-dir.y, dir.x) * side;
                    var p = new VillagePlot { front = at + outward * (roadWidth + 1.8f), along = dir, outward = outward,
                        frontage = width, depth = farm ? 62f : old ? 27f + (float)rng.NextDouble() * 12f : 38f + (float)rng.NextDouble() * 18f,
                        setback = old ? 5f : 12f, roadWidth = roadWidth, owner = owner, old = old, core = old, farm = farm,
                        model = "Scenery_House_" + (old ? "D" : rng.Next(3) == 0 ? "B" : "A"), style = rng.Next(4) };
                    if (!Overlap(p) && !CrossesRoad(p, roads) && !CrossesRoad(p, local) && Dry(p)) Add(p);
                    next += width + (old ? .15f : 2f) + (float)rng.NextDouble() * (old ? .25f : 11f);
                }
            }
        }
    }
}

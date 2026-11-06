using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Working and community land occupies road-connected town margins before crop parcels are fitted.
    public sealed partial class VillagePlan
    {
        // Reserve a balanced set along each outgoing route, then fill gaps with ribbon houses and larger farmyards.
        void TownEdge(int owner, WorldSettlement settlement, FarDetailData.Segment[] roads,
            FarDetailData.Segment[] local, Vector2 anchor, float reach)
        {
            var routes = new SortedDictionary<int, List<FarDetailData.Segment>>();
            foreach (var road in roads)
            {
                Vector2 mid = new Vector2((road.ax + road.bx) * .5f, (road.az + road.bz) * .5f);
                if ((mid - anchor).magnitude > reach + 900f) continue;
                if (!routes.TryGetValue(road.route, out var line)) routes.Add(road.route, line = new List<FarDetailData.Segment>());
                line.Add(road);
            }
            var rng = new Random(unchecked(seed * 997 + owner * 7919));
            int[] counts = new int[6];
            foreach (var line in routes.Values)
            {
                line.Sort((a, b) => a.walked.CompareTo(b.walked));
                var end = line[line.Count - 1];
                float high = end.walked + new Vector2(end.bx-end.ax,end.bz-end.az).magnitude;
                for (float station = line[0].walked + 60f; station < high; station += 38f)
                {
                    if (!OnRoute(line, station, out var at, out var along, out float width)) continue;
                    float distance = (at-anchor).magnitude;
                    if (distance < reach * .68f || distance > reach + 650f) continue;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector2 outward = new Vector2(-along.y, along.x) * side;
                        int use = 0;
                        for (int k = 1; k <= 5; k++) if (counts[k] < (k >= 4 ? 3 : 1)) { use = k; break; }
                        var p = new VillagePlot { owner = owner, town = true, along = along, outward = outward,
                            front = at + outward * (width + 1.4f), roadWidth = width, style = rng.Next(4),
                            frontage = 20f + (float)rng.NextDouble() * 8f, depth = 34f, setback = 8f,
                            model = "Scenery_House_" + (rng.Next(3) == 0 ? "B" : "A") };
                        if (use != 0)
                        {
                            p.landUse = use; p.square = use <= 3; p.model = use <= 3 ? null : use == 4 ? "Scenery_Warehouse" : "Scenery_House_B";
                            p.frontage = use == 1 ? 74f : use == 2 ? 112f : use == 3 ? 60f : 64f;
                            p.depth = use == 1 ? 56f : use == 2 ? 78f : use == 3 ? 62f : 78f;
                            p.setback = use == 4 ? 40f : 10f; p.farm = use == 5;
                        }
                        if (Overlap(p) || CrossesRoad(p, roads) || CrossesRoad(p, local) || !Dry(p)) continue;
                        Add(p); if (use != 0) counts[use]++;
                    }
                }
            }
        }
    }

    // The same physical uses suppress crops and paint the sector, regional tiles and world picture.
    public partial struct VillagePlot
    {
        // Grids have wide shared paths and smaller cultivated beds; the pitch and cemetery remain green.
        int TownUse(float u, float v)
        {
            if (landUse == 1)
            {
                float x = u + frontage * .5f;
                if (x < 3f || x > frontage-3f || v < 3f || v > depth-3f) return 0;
                return x % 12f < 2f || v % 14f < 2f ? 2 : 1;
            }
            if (landUse == 2) return 0;
            if (landUse == 3) return Math.Abs(u) < 2f || v < 4f || Math.Abs(v-depth*.5f) < 1.5f ? 2 : 0;
            return landUse == 4 ? 2 : -1;
        }
    }
}

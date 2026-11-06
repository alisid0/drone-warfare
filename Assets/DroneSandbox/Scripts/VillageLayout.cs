using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Projects the immutable village plots into the playable crop, and gives legacy Operations the same plot data.
    internal static class VillageLayout
    {
        // Sector-only road wander must not move the shared old street through an already planned property.
        public static void HoldFrontages(List<List<Vector2>> lines)
        {
            var plan = MapLayout.Plan;
            if (plan.world == null || MapLayout.DetailPlan == null) return;
            foreach (var road in plan.world.roads)
            {
                // PlaceMission inserts its access route at index zero; ownership is by the shared source array.
                int k = plan.roads.IndexOf(road.points);
                if (k < 0 || k >= lines.Count) continue;
                var source = road.points;
                for (int i = 0; i < lines[k].Count; i++)
                {
                    Vector2 p = lines[k][i];
                    if (!MapLayout.DetailPlan.villages.Near(p + plan.world.center, 45f)) continue;
                    float best = float.MaxValue; Vector2 nearest = p;
                    for (int j = 1; j < source.Length; j++)
                    {
                        Vector2 a = source[j - 1], d = source[j] - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / Math.Max(.01f, d.sqrMagnitude));
                        Vector2 q = a + d * t; float distance = (q - p).sqrMagnitude;
                        if (distance < best) { best = distance; nearest = q; }
                    }
                    lines[k][i] = nearest;
                }
            }
        }

        // Match the street's actual owner after mission access is inserted, keeping verges and gates consistent.
        public static float RoadWidth(int index)
        {
            var plan = MapLayout.Plan;
            if (index < plan.roads.Count)
                foreach (var road in plan.world.roads)
                    if (ReferenceEquals(plan.roads[index], road.points))
                        return road.kind == RoadClass.Highway ? 3.5f : road.kind == RoadClass.Track ? 1.5f : 2f;
            return index < plan.roads.Count ? 1.5f : 2f;
        }

        // The old town recipe creates a second unconnected town on reserved farmland. Replace only that scenery;
        // mission pads, permanent works, railways and their sites keep their original ownership.
        public static void PrepareRecipe(SectorPlan p)
        {
            if (p.world == null || !p.Type.town || p.world.features.Count != 0)
                return;
            for (int i = p.sites.Count - 1; i >= 0; i--)
                foreach (var area in p.reservedFeatures)
                    if (area.Contains(p.sites[i].position, 30f))
                    {
                        p.sites.RemoveAt(i);
                        break;
                    }

            p.reservedFeatures.Clear();
            p.featureSurfaces.Clear();
            p.featureFences.Clear();
            p.featureFlats.Clear();
            p.featureStreets.Clear();
        }

        // Street and plot positions are translated once, preserving their world seed and frontages.
        public static void Project(VillagePlan source)
        {
            var plan = MapLayout.Plan;
            Vector2 origin = plan.world.center;
            foreach (var original in source.plots)
            {
                var p = original;
                p.front -= origin;
                Vector2 c = p.front + p.outward * (p.depth * .5f);
                if (Math.Abs(c.x) > 950f || Math.Abs(c.y) > 950f)
                    continue;
                int owner = -1;
                float best = float.MaxValue;
                for (int i = 0; i < plan.villages.Count; i++)
                {
                    float d = (plan.villages[i] - p.front).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        owner = i;
                    }
                }

                if (owner < 0 || best > 450f * 450f)
                    continue;
                p.owner = owner;
                MapLayout.VillagePlots.Add(p);
                var line = new List<Vector2>
                {
                    p.front - p.outward * p.StreetOffset - p.along * (p.frontage * .5f),
                    p.front - p.outward * p.StreetOffset + p.along * (p.frontage * .5f)
                };
                MapLayout.VillageStreets.Add(line);
                MapLayout.VillageOfStreet.Add(owner);
            }

            foreach (var lane in source.lanes)
            {
                Vector2 a = new Vector2(lane.ax, lane.az) - origin, b = new Vector2(lane.bx, lane.bz) - origin;
                if (!ClipLine(ref a, ref b))
                    continue;
                MapLayout.RoadLines.Add(MapLayout.Smooth(new[] { a, b }, 2f));
            }
        }

        // Long town streets are cropped to the same terrain window as their frontage, never discarded by endpoints.
        static bool ClipLine(ref Vector2 a, ref Vector2 b)
        {
            float low = 0f, high = 1f;
            Vector2 d = b - a;
            for (int axis = 0; axis < 2; axis++)
            {
                float p = axis == 0 ? a.x : a.y, delta = axis == 0 ? d.x : d.y;
                if (Math.Abs(delta) < .001f)
                {
                    if (Math.Abs(p) > 1050f)
                        return false;
                    continue;
                }

                float x = (-1050f - p) / delta, y = (1050f - p) / delta;
                low = Math.Max(low, Math.Min(x, y));
                high = Math.Min(high, Math.Max(x, y));
            }

            if (low >= high)
                return false;
            b = a + d * high;
            a += d * low;
            return true;
        }

        // Existing Operation streets keep their connectivity; only their irregular property divisions are new.
        public static void Legacy()
        {
            for (int s = 0; s < MapLayout.VillageStreets.Count; s++)
            {
                int owner = MapLayout.VillageOfStreet[s];
                if (MapLayout.Plan.farmsteads[owner])
                    continue;
                var line = MapLayout.VillageStreets[s];
                var rng = new Random(MapLayout.Plan.fieldSeed * 31 + s * 977 + owner);
                foreach (float side in new[]
                {
                    -1f,
                    1f
                }

                )
                {
                    float walked = 0f, next = 8f;
                    for (int i = 1; i < line.Count; i++)
                    {
                        Vector2 d = line[i] - line[i - 1];
                        float length = d.magnitude;
                        if (length < .01f)
                            continue;
                        while (next < walked + length)
                        {
                            float width = 16f + (float)rng.NextDouble() * 14f, depth = 25f + (float)rng.NextDouble() * 35f;
                            Vector2 along = d / length, outward = new Vector2(-along.y, along.x) * side;
                            var p = new VillagePlot
                            {
                                front = line[i - 1] + along * (next - walked) + outward * (MapLayout.RoadHalfWidth + 1.8f),
                                along = along,
                                outward = outward,
                                frontage = width,
                                depth = depth,
                                setback = 9f,
                                roadWidth = MapLayout.RoadHalfWidth,
                                owner = owner,
                                style = rng.Next(4),
                                model = rng.NextDouble() < .65 ? "Scenery_House_A" : "Scenery_House_B"
                            };
                            MapLayout.VillagePlots.Add(p);
                            next += width;
                        }

                        walked += length;
                    }
                }
            }
            for (int owner = 0; owner < MapLayout.Plan.villages.Count; owner++)
                VillageUse.Assign(MapLayout.VillagePlots, owner, MapLayout.Plan.fieldSeed, MapLayout.Plan.villages[owner]);
        }
    }
}

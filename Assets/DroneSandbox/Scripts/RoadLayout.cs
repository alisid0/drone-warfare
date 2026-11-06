using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Road ownership and chainage survive mission insertion, clipped world fragments and village side streets.
    public static class RoadLayout
    {
        // Legacy Operations have ordinary surfaced roads; mission spurs and emplacement access remain farm tracks.
        public static RoadClass Kind(int line)
        {
            var plan = MapLayout.Plan;
            if (plan.world == null) return RoadClass.Road;
            if (line < plan.roads.Count)
            {
                foreach (var road in plan.world.roads)
                    if (ReferenceEquals(plan.roads[line], road.points)) return road.kind;
                return RoadClass.Track;
            }
            var points = MapLayout.RoadLines[line];
            if (points.Count > 0)
            {
                Vector2 p = points[points.Count / 2] + plan.world.center;
                var lane = MapLayout.DetailPlan.roadNetwork.At(p.x, p.y);
                if (lane.found && lane.distance < .1f) return lane.kind;
            }
            return RoadClass.Road;
        }

        // Capture immutable local vectors for splat workers without a new two-kilometre distance grid.
        public static RoadNetwork Capture()
        {
            var result = new List<FarDetailData.Segment>();
            for (int i = 0; i < MapLayout.RoadLines.Count; i++)
            {
                var line = MapLayout.RoadLines[i];
                RoadClass kind = Kind(i); float walked = 0f; int route = -1 - i;
                if (MapLayout.DetailPlan != null && line.Count > 0)
                {
                    Vector2 mid = line[line.Count / 2] + MapLayout.Plan.world.center;
                    var shared = MapLayout.DetailPlan.roadNetwork.At(mid.x, mid.y);
                    if (i < MapLayout.Plan.roads.Count) foreach (var road in MapLayout.Plan.world.roads)
                        if (ReferenceEquals(MapLayout.Plan.roads[i], road.points)) shared = MapLayout.DetailPlan.roadNetwork.RouteAt(mid.x, mid.y, road.source);
                    if (shared.found && shared.distance < .1f && shared.kind == kind)
                    {
                        route = shared.route;
                        Vector2 start = line[0] + MapLayout.Plan.world.center;
                        walked = MapLayout.DetailPlan.roadNetwork.RouteAt(start.x, start.y, route).along;
                    }
                }
                Add(result, line, kind, route, walked);
            }
            for (int i = 0; i < MapLayout.TrackLines.Count; i++) Add(result, MapLayout.TrackLines[i], RoadClass.Track, -10000 - i, 0f);
            return new RoadNetwork(result.ToArray());
        }

        // Retain route metre coordinates while copying the prepared geometry; no Random state is shared.
        static void Add(List<FarDetailData.Segment> result, List<Vector2> line, RoadClass kind, int route, float walked)
        {
            for (int i = 1; i < line.Count; i++)
            {
                Vector2 a = line[i - 1], b = line[i]; float width = RoadAppearance.HalfWidth(kind);
                if (route >= 0 && MapLayout.DetailPlan != null)
                {
                    Vector2 absolute = a + MapLayout.Plan.world.center;
                    var source = MapLayout.DetailPlan.roadNetwork.RouteAt(absolute.x, absolute.y, route);
                    if (source.found) walked = source.along;
                }
                result.Add(new FarDetailData.Segment { ax = a.x, az = a.y, bx = b.x, bz = b.y,
                    widthA = width, widthB = width, kind = (int)kind, route = route, walked = walked });
                walked += Vector2.Distance(a, b);
            }
        }

        // Splats keep grass between track ruts and suppress high grass beneath surfaced roads and mown shoulders.
        public static void Splat(float x, float z, float pixel, ref float dirt, ref float wet, ref float rough)
        {
            var road = MapLayout.Roads.At(x, z);
            if (!road.found || road.edge > 3f) return;
            float cover = road.kind == RoadClass.Track ? RoadAppearance.Ruts(road.across, road.along, pixel) * road.endFade
                : FarDetailData.Coverage(-road.edge, pixel);
            dirt = Math.Max(dirt, cover);
            float mown = FarDetailData.Coverage(.65f - road.edge, pixel);
            rough *= 1f - mown; wet *= 1f - cover;
            float outer = 1.4f + .6f * RoadAppearance.Value(road.route, (int)Math.Floor(road.along / 96f));
            float verge = RoadAppearance.Strip(road.distance, road.width + (outer + .65f) * .5f, outer - .65f, pixel);
            rough = Math.Max(rough, verge * .7f);
        }
    }
}

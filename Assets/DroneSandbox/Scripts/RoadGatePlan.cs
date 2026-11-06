using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // A shared gateway starts at a real hedge opening and carries the road-side gravel and fading field ruts.
    public struct RoadGate
    {
        public Vector2 gate, road, along, into, roadDirection;
        public bool layby;

        // The same acceptance and position hash is used by sector boundaries and immutable world parcels.
        public static bool Try(Vector2 gate, RoadNetwork network, out RoadGate entry)
        {
            entry = default;
            var road = network.At(gate.x, gate.y);
            if (!road.found || road.edge < .7f || road.edge > 12f || road.kind == RoadClass.Track || road.junction > 0f) return false;
            Vector2 into = (gate - road.point).normalized;
            entry = new RoadGate { gate = gate, road = road.point + into * road.width, into = into,
                along = new Vector2(-into.y, into.x), roadDirection = road.direction,
                layby = RoadAppearance.Value((int)Math.Floor(road.along / 24f), road.route) > .67f };
            return true;
        }

        // Road-relative rectangles preserve narrow entrance area at the world picture's coarse resolution.
        public Vector3 Paint(Vector3 colour, Vector2 p, float pixel)
        {
            Vector2 d = p - road;
            float u = Vector2.Dot(d, along), v = Vector2.Dot(d, into), length = (gate - road).magnitude;
            float apron = RoadAppearance.Strip(u, 0f, 3.6f, pixel) * RoadAppearance.Strip(v, length * .5f, length, pixel);
            if (layby)
            {
                Vector2 centre = road + into * 1.25f, q = p - centre;
                apron = Math.Max(apron, RoadAppearance.Strip(Vector2.Dot(q, roadDirection), 0f, 14f, pixel)
                    * RoadAppearance.Strip(Vector2.Dot(q, into), .2f, 1.8f, pixel));
            }
            colour += (RoadAppearance.Gravel - colour) * apron;
            float beyond = v - length, fade = FarDetailData.Clamp((12f - beyond) / 6f, 0f, 1f);
            float ruts = (RoadAppearance.Strip(u, -.85f, .5f * fade, pixel) + RoadAppearance.Strip(u, .85f, .5f * fade, pixel))
                * RoadAppearance.Strip(beyond, 6f, 12f, pixel);
            return colour + (RoadAppearance.Soil - colour) * ruts;
        }
    }

    // Gateway lists are built only for road-adjacent farm districts and retained with the worker snapshot.
    public sealed class RoadGatePlan
    {
        readonly FarFieldPlan fields;
        readonly RoadNetwork roads;
        readonly Dictionary<FarFieldPlan.District, RoadGate[]> cache = new Dictionary<FarFieldPlan.District, RoadGate[]>();
        readonly object sync = new object();

        // Neither scene objects nor mutable MapLayout state are consulted by tile or regional workers.
        public RoadGatePlan(FarFieldPlan fields, RoadNetwork roads) { this.fields = fields; this.roads = roads; }

        // Only narrow roadside pixels visit the cache; distant cover painting avoids extra district work.
        public Vector3 Paint(Vector3 colour, float x, float z, RoadNetwork.Sample road, float pixel)
        {
            if (!road.found || road.edge < -.5f - pixel || road.edge > 24f + pixel) return colour;
            var district = fields.At(x, z);
            Vector2 p = new Vector2(x, z), side = new Vector2(-road.direction.y, road.direction.x) * 20f;
            colour = PaintDistrict(colour, p, pixel, district);
            var a = fields.At(road.point.x + side.x, road.point.y + side.y);
            var b = fields.At(road.point.x - side.x, road.point.y - side.y);
            if (!ReferenceEquals(a, district)) colour = PaintDistrict(colour, p, pixel, a);
            if (!ReferenceEquals(b, district) && !ReferenceEquals(b, a)) colour = PaintDistrict(colour, p, pixel, b);
            return colour;
        }

        // Entrances can sit across a district border from the road pixel; each distinct neighbour is painted once.
        Vector3 PaintDistrict(Vector3 colour, Vector2 p, float pixel, FarFieldPlan.District district)
        {
            RoadGate[] gates;
            lock (sync)
            {
                if (!cache.TryGetValue(district, out gates)) { gates = Build(district); cache.Add(district, gates); }
            }
            foreach (var gate in gates) if ((p - gate.gate).sqrMagnitude < (30f + pixel) * (30f + pixel)) colour = gate.Paint(colour, p, pixel);
            return colour;
        }

        // Original parcel edges define gates, never tessellation cuts or sector clipping fragments.
        RoadGate[] Build(FarFieldPlan.District d)
        {
            var result = new List<RoadGate>();
            foreach (var parcel in d.parcels)
            {
                if (!parcel.valid || !(parcel.hedge || parcel.use == 1 || parcel.use == 2)) continue;
                Vector2 along = parcel.cross ? new Vector2(-d.along.y, d.along.x) : d.along, across = new Vector2(-along.y, along.x);
                var corners = new[] { d.site + along * parcel.u0 + across * parcel.a0, d.site + along * parcel.u1 + across * parcel.b0,
                    d.site + along * parcel.u1 + across * parcel.b1, d.site + along * parcel.u0 + across * parcel.a1 };
                for (int i = 0; i < 4; i++)
                {
                    Vector2 point = FieldAppearance.Gate(corners[i], corners[(i + 1) % 4]);
                    if (RoadGate.Try(point, roads, out var gate)) result.Add(gate);
                }
            }
            return result.ToArray();
        }
    }
}

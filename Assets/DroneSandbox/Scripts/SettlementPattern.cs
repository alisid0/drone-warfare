using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Road-aligned 120 m town blocks share an identical plot plan between individual houses and combined roofs.
    public static class SettlementPattern
    {
        public const float BlockSize = 120f;

        // Dimensions describe the broad silhouettes of the seven existing house models, in model order.
        public struct House
        {
            public Vector2 world;
            public float yaw;
            public int kind;
        }

        // A block switches as one unit, keeping whole streets intact at the 1.5 km handover.
        public sealed class Block
        {
            public Vector2 center;
            public readonly List<House> houses = new List<House>();
        }

        // Nearby road segments avoid a search through the entire theatre for every frontage.
        struct Road
        {
            public Vector2 a, b;
            public float halfWidth;
        }

        public static readonly Vector3[] Size = { new Vector3(8.9f, 3f, 6.9f), new Vector3(9.2f, 6f, 7.7f),
            new Vector3(14.6f, 6.4f, 15.6f), new Vector3(7.7f, 3.15f, 6.7f),
            new Vector3(24.4f, 9.35f, 10.4f), new Vector3(30.5f, 12.8f, 12.5f), new Vector3(12.7f, 6.5f, 10.7f) };
        public static readonly float[] Roof = { 2.3f, 2.6f, 2.6f, 2.6f, 2.4f, 0f, 2f };

        // Iterate a settlement incrementally; generation depends only on world positions and the world seed.
        public static IEnumerable<Block> Build(WorldSettlement settlement)
        {
            var roads = new List<Road>();
            float reach = settlement.radius + 350f;
            foreach (var route in WorldPlan.Roads)
                for (int i = 1; i < route.points.Count; i++)
                {
                    Vector2 a = route.points[i - 1], b = route.points[i];
                    if (Vector2.Distance(Closest(settlement.center, a, b), settlement.center) > reach) continue;
                    roads.Add(new Road { a = a, b = b, halfWidth = route.kind == RoadClass.Highway ? 10f : route.kind == RoadClass.Road ? 5f : 3f });
                }
            Vector2 axis = Direction(settlement.center, roads, Vector2.right);
            Vector2 across = new Vector2(-axis.y, axis.x);
            var occupied = new Dictionary<long, List<House>>();
            int blocks = Mathf.CeilToInt(settlement.radius / BlockSize) + 1;
            for (int j = -blocks; j <= blocks; j++)
                for (int i = -blocks; i <= blocks; i++)
                {
                    Vector2 center = settlement.center + axis * (i * BlockSize) + across * (j * BlockSize);
                    if (Vector2.Distance(center, settlement.center) > settlement.radius + BlockSize) continue;
                    var block = new Block { center = center };
                    // The district grid follows the connecting road. Each block follows its nearest bend.
                    Vector2 tangent = Direction(center, roads, axis);
                    Vector2 normal = new Vector2(-tangent.y, tangent.x);
                    int gx = Mathf.RoundToInt(center.x), gz = Mathf.RoundToInt(center.y);
                    for (int side = 0; side < 4; side++)
                        for (int plot = -1; plot <= 1; plot++)
                        {
                            int salt = side * 7 + plot + 2;
                            float chance = ForestPattern.Unit(gx, gz, 100 + salt);
                            if (chance > (settlement.town ? 0.9f : 0.6f)) continue;
                            float pick = ForestPattern.Unit(gx, gz, 140 + salt);
                            int kind = settlement.town ? (pick < 0.38f ? 4 : pick < 0.53f && plot == 0 ? 5 : pick < 0.64f ? 6 : (int)(pick * 40f) % 4) : (int)(pick * 4f) % 4;
                            float setback = 3f + 6f * ForestPattern.Unit(gx, gz, 180 + salt);
                            Vector2 along = side < 2 ? tangent : normal;
                            Vector2 outward = side < 2 ? normal : tangent;
                            if ((side & 1) == 0) outward = -outward;
                            float depth = Size[kind].z;
                            Vector2 p = center + along * (plot * 28f) + outward * (BlockSize / 2f - 4f - setback - depth * 0.5f);
                            if (!WorldPlan.Inside(p.x, p.y) || FarScatter.InSector(p - FarTerrain.Origin)) continue;
                            var cover = WorldPlan.CoverAt(p.x, p.y);
                            if (cover != Cover.Town && cover != Cover.Village) continue;
                            bool onRoad = false;
                            foreach (var road in roads)
                                if (Vector2.Distance(p, Closest(p, road.a, road.b)) < road.halfWidth + Mathf.Max(Size[kind].x, depth) * 0.5f + 3f)
                                { onRoad = true; break; }
                            if (onRoad) continue;
                            // Models face local -Z; rotate their front to the street on this side of the block.
                            float yaw = Mathf.Atan2(-outward.x, -outward.y) * Mathf.Rad2Deg;
                            var house = new House { world = p, kind = kind, yaw = yaw };
                            if (Reserve(house, occupied)) block.houses.Add(house);
                        }
                    yield return block;
                }
        }

        // Bent street grids can bring adjacent block corners together; reject intersecting roof footprints.
        static bool Reserve(House house, Dictionary<long, List<House>> occupied)
        {
            int x = Mathf.FloorToInt(house.world.x / 64f), z = Mathf.FloorToInt(house.world.y / 64f);
            for (int j = z - 1; j <= z + 1; j++)
                for (int i = x - 1; i <= x + 1; i++)
                    if (occupied.TryGetValue(((long)i << 32) ^ (uint)j, out var neighbours))
                        foreach (var neighbour in neighbours) if (Overlap(house, neighbour)) return false;
            long key = ((long)x << 32) ^ (uint)z;
            if (!occupied.TryGetValue(key, out var list)) occupied.Add(key, list = new List<House>());
            list.Add(house);
            return true;
        }

        // Four separating axes test the oriented rectangles, with a metre of breathing room between roofs.
        public static bool Overlap(House a, House b)
        {
            float aa = a.yaw * Mathf.Deg2Rad, ba = b.yaw * Mathf.Deg2Rad;
            Vector2 ax = new Vector2(Mathf.Cos(aa), -Mathf.Sin(aa)), az = new Vector2(-ax.y, ax.x);
            Vector2 bx = new Vector2(Mathf.Cos(ba), -Mathf.Sin(ba)), bz = new Vector2(-bx.y, bx.x);
            Vector2 delta = b.world - a.world;
            for (int k = 0; k < 4; k++)
            {
                Vector2 axis = k == 0 ? ax : k == 1 ? az : k == 2 ? bx : bz;
                float ar = (Mathf.Abs(Vector2.Dot(axis, ax)) * Size[a.kind].x + Mathf.Abs(Vector2.Dot(axis, az)) * Size[a.kind].z) * 0.5f;
                float br = (Mathf.Abs(Vector2.Dot(axis, bx)) * Size[b.kind].x + Mathf.Abs(Vector2.Dot(axis, bz)) * Size[b.kind].z) * 0.5f;
                if (Mathf.Abs(Vector2.Dot(delta, axis)) >= ar + br + 1f) return false;
            }
            return true;
        }

        // The closest non-degenerate road tangent wins; a settlement with no road keeps a stable cardinal grid.
        static Vector2 Direction(Vector2 p, List<Road> roads, Vector2 fallback)
        {
            float best = float.MaxValue;
            foreach (var road in roads)
            {
                if ((road.b - road.a).sqrMagnitude < 0.01f) continue;
                float distance = (p - Closest(p, road.a, road.b)).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; fallback = (road.b - road.a).normalized;
            }
            return fallback;
        }

        // Project onto a finite segment, so buildings also stay clear of road ends and junctions.
        static Vector2 Closest(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return a + d * Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(0.01f, d.sqrMagnitude));
        }
    }
}

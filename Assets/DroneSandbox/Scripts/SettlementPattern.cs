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

        public static readonly Vector3[] Size = { new Vector3(8.9f, 3f, 6.9f), new Vector3(9.2f, 6f, 7.7f),
            new Vector3(14.6f, 6.4f, 15.6f), new Vector3(7.7f, 3.15f, 6.7f),
            new Vector3(24.4f, 9.35f, 10.4f), new Vector3(30.5f, 12.8f, 12.5f), new Vector3(12.7f, 6.5f, 10.7f), new Vector3(7.8f, 5.5f, 16f),
            new Vector3(24.2f, 10.1f, 17f), new Vector3(18.8f, 7f, 30.8f) };
        public static readonly float[] Roof = { 2.3f, 2.6f, 2.6f, 2.6f, 2.4f, 0f, 2f, 3.2f, 0f, 2.5f };

        // Iterate a settlement incrementally; generation depends only on world positions and the world seed.
        public static IEnumerable<Block> Build(WorldSettlement settlement)
        {
            int owner = WorldPlan.Settlements.IndexOf(settlement);
            if (owner < 0) yield break;
            var groups = new SortedDictionary<long, Block>();
            var detail = MapLayout.DetailPlan;
            if (detail == null || detail.seed != WorldPlan.Seed) detail = WorldPlan.CaptureDetail(null);
            foreach (var p in detail.villages.plots)
            {
                if (p.owner != owner || p.square || p.model == null) continue;
                int kind = p.model == "Scenery_House_A" ? 0 : p.model == "Scenery_House_B" ? 1
                    : p.model == "Scenery_House_D" ? 3 : p.model == "Scenery_Townhouse_Row" ? 4
                    : p.model == "Scenery_Church" ? 7 : p.model == "Scenery_OfficeBlock" ? 8 : 9;
                Vector2 at = p.House;
                if (FarScatter.InSector(at - FarTerrain.Origin)) continue;
                int x = Mathf.FloorToInt(at.x / BlockSize), z = Mathf.FloorToInt(at.y / BlockSize);
                long key = ((long)x << 32) ^ (uint)z;
                if (!groups.TryGetValue(key, out var block))
                { block = new Block { center = new Vector2((x + .5f) * BlockSize, (z + .5f) * BlockSize) }; groups.Add(key, block); }
                block.houses.Add(new House { world = at, kind = kind, yaw = Mathf.Atan2(-p.outward.x, -p.outward.y) * Mathf.Rad2Deg });
            }
            foreach (var block in groups.Values) yield return block;
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

    }
}

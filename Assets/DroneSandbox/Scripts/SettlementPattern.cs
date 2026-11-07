using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Road-aligned 120 m town blocks share an identical plot plan between individual houses and combined roofs.
    public static class SettlementPattern
    {
        public const float BlockSize = 120f;

        // The building and tree models the settlement scatter draws, in kind order; FarScatter loads them in this order.
        public static readonly string[] Models = { "Scenery_House_A", "Scenery_House_B", "Scenery_House_C", "Scenery_House_D",
            "Scenery_Townhouse_Row", "Scenery_Apartment", "Scenery_Shop", "Scenery_Church", "Scenery_OfficeBlock", "Scenery_Warehouse", "Scenery_Barn",
            "Scenery_House_Semi", "Scenery_Townhouse_Old", "Scenery_FillingStation", "Scenery_Tree" };

        // The four roofs a house may wear, in World.Roofs order; the scatter tints a model's RoofTile with these.
        public static readonly string[] RoofMaterials = { "RoofTile", "RoofTileDark", "RoofTileWarm", "RoofSlate" };

        // Dimensions describe the broad silhouettes of the existing building models, in FarScatter's model order.
        public struct House
        {
            public Vector2 world;
            public float yaw, scale;
            public int kind, roof; // roof: the plot's tone, an index into RoofMaterials
            public float Scale => scale > 0f ? scale : 1f;
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
            new Vector3(24.2f, 10.1f, 17f), new Vector3(18.8f, 7f, 30.8f), new Vector3(11.6f, 4f, 7.8f),
            new Vector3(12.4f, 6f, 9.2f), new Vector3(18.2f, 7.6f, 11f), new Vector3(16f, 5.4f, 17f), new Vector3(12f, 16f, 12f) };
        public static readonly float[] Roof = { 2.3f, 2.6f, 2.6f, 2.6f, 2.4f, 0f, 2f, 3.2f, 0f, 2.5f, 2.6f, 3f, 3.4f, 0f, 6f };
        public const int Barn = 10, Tree = 14;

        // The HLOD's palette: four plasters; the four roofs (tile, dark tile, warm tile, slate); a barn's weathered planks
        // and dark sheet roof; a flat roof's felt and a canopy's grey; a street tree's crown, lit and shaded.
        public static readonly Color32[] Palette = { new Color32(178, 168, 146, 255), new Color32(148, 144, 134, 255),
            new Color32(164, 154, 131, 255), new Color32(182, 174, 153, 255),
            new Color32(138, 79, 58, 255), new Color32(98, 66, 56, 255), new Color32(150, 94, 64, 255), new Color32(86, 93, 97, 255),
            new Color32(101, 86, 70, 255), new Color32(84, 82, 78, 255), new Color32(112, 108, 100, 255), new Color32(150, 150, 146, 255),
            new Color32(56, 70, 40, 255), new Color32(44, 56, 32, 255) };

        // The palette texel of a building's walls and of its roof.
        public static int WallTexel(House h) => h.kind == Barn ? 8 : h.kind == Tree ? 13 : h.kind == 13 ? 11 : h.kind % 4;
        public static int RoofTexel(House h) => h.kind == Barn ? 9 : h.kind == Tree ? 12 : Roof[h.kind] <= 0f ? 10 : 4 + (h.roof & 3);

        // The roof colour a building shows from above.
        public static Color32 RoofColour(House h) => Palette[RoofTexel(h)];

        // Iterate a settlement incrementally; generation depends only on world positions and the world seed. Every
        // farm's barn stands behind its yard exactly where the sector puts it (World.Garden), so a village keeps its
        // farms at every distance; a town's avenues are planted as trees.
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
                int kind = System.Array.IndexOf(Models, p.model);
                if (kind < 0) kind = 9;
                Vector2 at = p.House;
                if (FarScatter.InSector(at - FarTerrain.Origin)) continue;
                float yaw = Mathf.Atan2(-p.outward.x, -p.outward.y) * Mathf.Rad2Deg;
                Add(groups, new House { world = at, kind = kind, scale = p.Scale, roof = p.RoofTone,
                    yaw = p.gable ? Mathf.Atan2(p.along.x, p.along.y) * Mathf.Rad2Deg : yaw });
                Vector2 barn = p.Point(p.frontage < 28f ? 0f : -4f, Mathf.Min(p.depth - 9f, p.YardEnd + 5f));
                if (p.farm && kind <= 3 && !FarScatter.InSector(barn - FarTerrain.Origin))
                    Add(groups, new House { world = barn, kind = Barn, scale = .8f, yaw = yaw });
            }
            foreach (var tree in detail.villages.trees)
            {
                if (tree.owner != owner || FarScatter.InSector(tree.at - FarTerrain.Origin)) continue;
                TreePose(tree.at, out float turn, out float size);
                Add(groups, new House { world = tree.at, kind = Tree, yaw = turn, scale = size });
            }
            foreach (var block in groups.Values) yield return block;
        }

        // A street tree's turn and size from its world position, shared with the sector (World.StreetTrees).
        public static void TreePose(Vector2 at, out float yaw, out float scale)
        {
            uint h = unchecked((uint)Mathf.FloorToInt(at.x * 3.1f) * 73856093u ^ (uint)Mathf.FloorToInt(at.y * 3.1f) * 19349663u);
            h = unchecked((h ^ (h >> 13)) * 1274126177u);
            yaw = h % 360; scale = .42f + (h >> 9 & 15) * .01f;
        }

        // File one building under the 120 m block holding it.
        static void Add(SortedDictionary<long, Block> groups, House house)
        {
            int x = Mathf.FloorToInt(house.world.x / BlockSize), z = Mathf.FloorToInt(house.world.y / BlockSize);
            long key = ((long)x << 32) ^ (uint)z;
            if (!groups.TryGetValue(key, out var block))
            { block = new Block { center = new Vector2((x + .5f) * BlockSize, (z + .5f) * BlockSize) }; groups.Add(key, block); }
            block.houses.Add(house);
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
                float ar = (Mathf.Abs(Vector2.Dot(axis, ax)) * Size[a.kind].x + Mathf.Abs(Vector2.Dot(axis, az)) * Size[a.kind].z) * 0.5f * a.Scale;
                float br = (Mathf.Abs(Vector2.Dot(axis, bx)) * Size[b.kind].x + Mathf.Abs(Vector2.Dot(axis, bz)) * Size[b.kind].z) * 0.5f * b.Scale;
                if (Mathf.Abs(Vector2.Dot(delta, axis)) >= ar + br + 1f) return false;
            }
            return true;
        }

    }
}

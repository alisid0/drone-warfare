using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The debrief's map of the battlefield, drawn once per map from what the world was built from (no camera, so
    // nothing about the light or the weather can spoil it): land shaded by its slopes with 5 m contours, fields by
    // crop, woods from the tree tops, water, roads and rail, every building's footprint and a 50 m grid. A tactical
    // map rather than a photo, so the drones' paths read clearly over it.
    public static class MapSnapshot
    {
        public const int Size = 512;
        public static Texture2D Picture { get; private set; }
        public static float Half => SandboxGame.MapHalfSize + 20f; // metres from the middle to each edge

        static readonly HashSet<string> Buildings = new HashSet<string>
        {
            "Scenery_House_A", "Scenery_House_B", "Scenery_Barn", "Scenery_Church", "Scenery_Station", "Scenery_Hut",
            "Base_Hangar", "Base_Tent", "Base_FuelTanks", "HQ_Building", "HQ_Watchtower", "Prop_Bunker",
            "Prop_Container_Olive", "Prop_Container_Rust",
        };

        const byte Roof = 1, Eave = 2, Deck = 3; // the footprint mask: building, its outline, bridge deck

        static readonly Color Land = new Color(0.40f, 0.46f, 0.33f), Wood = new Color(0.20f, 0.29f, 0.19f),
            Water = new Color(0.23f, 0.37f, 0.45f), Road = new Color(0.72f, 0.66f, 0.53f), Verge = new Color(0.47f, 0.43f, 0.35f),
            Rail = new Color(0.30f, 0.29f, 0.28f), RoofColour = new Color(0.66f, 0.53f, 0.46f), EaveColour = new Color(0.24f, 0.20f, 0.18f);

        static Color CropColour(Crop crop)
        {
            switch (crop)
            {
                case Crop.Wheat: return new Color(0.66f, 0.58f, 0.34f);
                case Crop.YoungCrop: return new Color(0.43f, 0.54f, 0.28f);
                case Crop.Ploughed: return new Color(0.44f, 0.36f, 0.27f);
                case Crop.Sunflower: return new Color(0.62f, 0.55f, 0.24f);
                default: return new Color(0.58f, 0.55f, 0.38f); // mown hay
            }
        }

        public static void Capture(Transform world)
        {
            float started = Time.realtimeSinceStartup;
            if (Picture == null) Picture = new Texture2D(Size, Size, TextureFormat.RGB24, false) { name = "Battle Map", wrapMode = TextureWrapMode.Clamp };
            float half = Half, step = 2f * half / Size;
            float X(int i) => -half + (i + 0.5f) * step;

            var heights = new float[Size * Size];
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++) heights[j * Size + i] = Ground.HeightAt(new Vector3(X(i), 0f, X(j)));
            var mask = Footprints(world, half, step);

            Vector3 sun = new Vector3(-1f, 1.4f, 1f).normalized; // lit from the north-west, as maps are
            var fields = MapLayout.Fields;
            var roads = MapLayout.RoadField;
            var rails = MapLayout.RailField;
            var river = MapLayout.HasRiver ? MapLayout.RiverField : null;
            float riverHalf = MapLayout.RiverHalfWidth, roadHalf = MapLayout.RoadHalfWidth;
            var pixels = new Color32[Size * Size];
            for (int j = 0; j < Size; j++)
            {
                for (int i = 0; i < Size; i++)
                {
                    int k = j * Size + i;
                    float x = X(i), z = X(j), h = heights[k];
                    float hl = heights[j * Size + Mathf.Max(i - 1, 0)], hr = heights[j * Size + Mathf.Min(i + 1, Size - 1)];
                    float hd = heights[Mathf.Max(j - 1, 0) * Size + i], hu = heights[Mathf.Min(j + 1, Size - 1) * Size + i];
                    // Relief exaggerated twice over, so gentle slopes still show.
                    var normal = new Vector3(-(hr - hl) / step, 1f, -(hu - hd) / step).normalized;
                    float shade = Mathf.Clamp(1f + (Vector3.Dot(normal, sun) - sun.y) * 2f, 0.65f, 1.3f);

                    Color c;
                    bool wet = (river != null && river.Distance(x, z) < riverHalf) || h < MapLayout.WaterLevelAt(x, z);
                    if (mask[k] == Deck) c = Road;
                    else if (mask[k] == Eave) c = EaveColour;
                    else if (mask[k] == Roof) c = RoofColour * Mathf.Lerp(1f, shade, 0.4f);
                    else if (wet) c = Water;
                    else
                    {
                        c = Land;
                        var p = FieldPlot.Unwarp(new Vector2(x, z));
                        foreach (var field in fields)
                        {
                            if (!field.ContainsUnwarped(p, 0f)) continue;
                            c = field.ContainsUnwarped(p, -0.8f) ? CropColour(field.crop) : Verge; // a darker edge round each field
                            break;
                        }
                        // Woods: the canopy grid is 4 m square, so it's sampled a little off each pixel to fray the edges.
                        int hash = ((i * 73856093) ^ (j * 19349663)) & 0x7fffffff;
                        float jx = (hash % 7 - 3) * 0.5f, jz = (hash / 7 % 7 - 3) * 0.5f;
                        if (Canopy.TopNear(new Vector3(x + jx, 0f, z + jz), 0.5f) > h + 2.5f) c = Wood;
                        if (rails != null && rails.Distance(x, z) < 1.3f) c = Rail;
                        if (roads != null)
                        {
                            float d = roads.Distance(x, z);
                            if (d < roadHalf + 0.5f) c = d < roadHalf - 0.3f ? Road : Verge;
                        }
                        c *= shade;
                        // 5 m contours, where the height band changes from one pixel to the next.
                        int band = Mathf.FloorToInt(h / 5f);
                        if (band != Mathf.FloorToInt(hr / 5f) || band != Mathf.FloorToInt(hu / 5f)) c *= 0.8f;
                    }
                    // A faint 50 m grid, and the land outside the battle area dimmed.
                    if (Mathf.Abs(Mathf.Repeat(x + 25f, 50f) - 25f) < step * 0.5f || Mathf.Abs(Mathf.Repeat(z + 25f, 50f) - 25f) < step * 0.5f)
                        c = Color.Lerp(c, Color.white, 0.12f);
                    if (Mathf.Abs(x) > SandboxGame.MapHalfSize || Mathf.Abs(z) > SandboxGame.MapHalfSize) c *= 0.6f;
                    c.a = 1f;
                    pixels[k] = c;
                }
            }
            Picture.SetPixels32(pixels);
            Picture.Apply(false);
            Debug.Log($"Battle map drawn in {(Time.realtimeSinceStartup - started) * 1000f:0} ms");
        }

        // Which pixels are covered by a building (and its outline) or a bridge deck, from each one's meshes.
        static byte[] Footprints(Transform world, float half, float step)
        {
            var mask = new byte[Size * Size];
            if (world == null) return mask;
            foreach (var t in world.GetComponentsInChildren<Transform>(true))
            {
                bool bridge = t.name == "Scenery_Bridge";
                if (!bridge && !Buildings.Contains(t.name)) continue;
                if (t.parent != null && t.parent.name == t.name) continue; // the model inside its placed root
                // The footprint in the building's own frame, so a house set at an angle stays a rectangle.
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                foreach (var filter in t.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null) continue;
                    var b = filter.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                        Vector3 local = t.InverseTransformPoint(filter.transform.TransformPoint(corner));
                        x0 = Mathf.Min(x0, local.x); x1 = Mathf.Max(x1, local.x);
                        z0 = Mathf.Min(z0, local.z); z1 = Mathf.Max(z1, local.z);
                    }
                }
                if (x0 > x1) continue;
                float reach = Mathf.Sqrt(Mathf.Max(x0 * x0, x1 * x1) + Mathf.Max(z0 * z0, z1 * z1)) * Mathf.Max(t.lossyScale.x, t.lossyScale.z);
                Vector3 at = t.position;
                int i0 = Mathf.Max(0, Mathf.FloorToInt((at.x - reach + half) / step)), i1 = Mathf.Min(Size - 1, Mathf.CeilToInt((at.x + reach + half) / step));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((at.z - reach + half) / step)), j1 = Mathf.Min(Size - 1, Mathf.CeilToInt((at.z + reach + half) / step));
                float edge = step * 1.2f / Mathf.Max(t.lossyScale.x, 0.01f);
                for (int j = j0; j <= j1; j++)
                {
                    for (int i = i0; i <= i1; i++)
                    {
                        Vector3 local = t.InverseTransformPoint(new Vector3(-half + (i + 0.5f) * step, at.y, -half + (j + 0.5f) * step));
                        if (local.x < x0 || local.x > x1 || local.z < z0 || local.z > z1) continue;
                        int k = j * Size + i;
                        if (bridge) mask[k] = Deck;
                        else
                        {
                            bool rim = local.x < x0 + edge || local.x > x1 - edge || local.z < z0 + edge || local.z > z1 - edge;
                            if (mask[k] != Roof || rim) mask[k] = rim ? Eave : Roof;
                        }
                    }
                }
            }
            return mask;
        }
    }
}

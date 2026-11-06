using UnityEngine;

namespace DroneSandbox
{
    // A sector's actual terrain weights anchor the world picture at its edge. Only the small collar changes on travel.
    public static class FarDetailEdge
    {
        const float Collar = 256f;
        static Texture2D previousTexture;
        static Color[] previousPixels;
        static int previousX, previousZ, previousWidth, previousHeight;

        // Restore the last sector's patch first; rebuilding a sector must not leave its palette stamped into the world.
        public static void Apply(Texture2D texture, Vector2 origin)
        {
            if (previousTexture == texture && previousPixels != null)
                texture.SetPixels(previousX, previousZ, previousWidth, previousHeight, previousPixels);
            previousPixels = null; previousTexture = texture;
            var terrain = Ground.Current;
            if (terrain == null) return;
            var data = terrain.terrainData;
            float half = Ground.Size * .5f, pixel = WorldPlan.Size / texture.width;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((origin.x - half - Collar + WorldPlan.Half) / pixel));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((origin.y - half - Collar + WorldPlan.Half) / pixel));
            int x1 = Mathf.Min(texture.width, Mathf.CeilToInt((origin.x + half + Collar + WorldPlan.Half) / pixel));
            int z1 = Mathf.Min(texture.height, Mathf.CeilToInt((origin.y + half + Collar + WorldPlan.Half) / pixel));
            int width = x1 - x0, height = z1 - z0;
            previousX = x0; previousZ = z0; previousWidth = width; previousHeight = height;
            previousPixels = texture.GetPixels(x0, z0, width, height);
            var pixels = (Color[])previousPixels.Clone();
            Vector3[][] edges = Capture(data);
            for (int j = 0; j < height; j++) for (int i = 0; i < width; i++)
                {
                    float x = -WorldPlan.Half + (x0 + i + .5f) * pixel - origin.x;
                    float z = -WorldPlan.Half + (z0 + j + .5f) * pixel - origin.y;
                    float distance = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - half;
                    // The hidden interior need not be repainted. A pixel of overlap prevents bilinear edge leakage.
                    if (distance < -pixel || distance > Collar) continue;
                    // Shared world parcels reach the terrain edge. A splat-only collar would erase those crop meshes.
                    if (MapLayout.Plan.world != null && WorldPlan.CoverAt(x + origin.x, z + origin.y) == Cover.Farmland) continue;
                    int side = Mathf.Abs(x) > Mathf.Abs(z) ? (x < 0 ? 0 : 1) : (z < 0 ? 2 : 3);
                    float along = side < 2 ? z : x;
                    float u = Mathf.Clamp01((along + half) / Ground.Size) * (edges[side].Length - 1);
                    int a = Mathf.Min((int)u, edges[side].Length - 2);
                    Vector3 edge = Vector3.Lerp(edges[side][a], edges[side][a + 1], u - a);
                    var original = FarDetailPalette.Decode((Color32)pixels[j * width + i]);
                    pixels[j * width + i] = FarDetailPalette.Encode(Blend(edge, original, distance, Collar));
                }
            texture.SetPixels(x0, z0, width, height, pixels);
            texture.Apply(true, false);
        }

        // Read four narrow splat strips once during sector build, never from the streaming update.
        static Vector3[][] Capture(TerrainData data)
        {
            int n = data.alphamapResolution;
            int band = Mathf.Max(1, Mathf.RoundToInt(8f / Ground.Size * n));
            var strips = new[] { data.GetAlphamaps(0, 0, band, n), data.GetAlphamaps(n - band, 0, band, n),
                data.GetAlphamaps(0, 0, n, band), data.GetAlphamaps(0, n - band, n, band) };
            var edges = new Vector3[4][];
            for (int side = 0; side < 4; side++)
            {
                edges[side] = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 sum = Vector3.zero;
                    for (int b = 0; b < band; b++) for (int layer = 0; layer < Mathf.Min(5, data.alphamapLayers); layer++)
                            sum += FarDetailPalette.Means[layer] * (side < 2 ? strips[side][i, b, layer] : strips[side][b, i, layer]);
                    edges[side][i] = sum / band;
                }
            }
            return edges;
        }

        // A flat first 32 m matches filtered sector pixels; smooth falloff hides the join without a second hard edge.
        public static Vector3 Blend(Vector3 sector, Vector3 world, float distance, float width = Collar)
        {
            float t = Mathf.SmoothStep(0f, 1f, (distance - 32f) / (width - 32f));
            return Vector3.Lerp(sector, world, t);
        }
    }
}

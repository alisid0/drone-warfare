using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Rolling terrain built in code: hills in the play area, ridges around the edge, and flat
    // sites (bases, fixed defences) levelled to y = 0. Also answers "how high is the ground here?".
    public static class Ground
    {
        public const float Size = 480f;   // terrain is Size x Size metres, centred on the map
        const float Sink = 4f;            // terrain sits this far below y = 0, so hollows can dip below the flat sites
        const float HeightRange = 45f;
        const int HeightRes = 513;
        const int SplatRes = 512;

        public struct FlatSite
        {
            public Vector3 center;
            public float radius;
            public bool bare; // dirt instead of grass (base pads, emplacements)
            public FlatSite(Vector3 c, float r, bool bareGround) { center = c; radius = r; bare = bareGround; }
        }

        static Terrain terrain;
        static readonly List<FlatSite> sites = new List<FlatSite>();

        public static float HeightAt(Vector3 p) =>
            terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : 0f;

        public static Vector3 OnGround(Vector3 p) => new Vector3(p.x, HeightAt(p), p.z);

        public static Vector3 NormalAt(Vector3 p)
        {
            if (terrain == null) return Vector3.up;
            var data = terrain.terrainData;
            Vector3 local = p - terrain.transform.position;
            return data.GetInterpolatedNormal(local.x / data.size.x, local.z / data.size.z);
        }

        // Where a screen ray hits the ground.
        public static bool Raycast(Ray ray, out Vector3 point)
        {
            if (terrain != null && terrain.GetComponent<TerrainCollider>().Raycast(ray, out RaycastHit hit, 5000f))
            {
                point = hit.point;
                return true;
            }
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }
            point = Vector3.zero;
            return false;
        }

        public static Terrain Build(IEnumerable<FlatSite> flatSites)
        {
            sites.Clear();
            sites.AddRange(flatSites);

            var data = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = SplatRes };
            data.size = new Vector3(Size, HeightRange, Size);
            data.SetHeights(0, 0, BuildHeights());
            data.terrainLayers = BuildLayers();
            data.SetAlphamaps(0, 0, BuildSplat(data));

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = new Vector3(-Size / 2f, -Sink, -Size / 2f);
            terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 600f;
            var lit = Resources.Load<Material>("Materials/TerrainLit");
            if (lit != null) terrain.materialTemplate = lit;
            // Draw without GPU instancing. Instanced terrain takes its normals from a texture Unity generates, and
            // for this terrain built at runtime in a player that texture came out black, turning the ground near
            // black (and without the instancing shader variants the terrain doesn't draw at all).
            terrain.drawInstanced = false;
            return terrain;
        }

        // ---- Heights ----

        static float[,] BuildHeights()
        {
            var heights = new float[HeightRes, HeightRes];
            float step = Size / (HeightRes - 1);
            for (int zi = 0; zi < HeightRes; zi++)
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float world = Hills(x, z) * FlatMask(x, z, out _);
                    heights[zi, xi] = Mathf.Clamp01((world + Sink) / HeightRange);
                }
            }
            return heights;
        }

        // Height above y = 0 before flattening, in metres.
        static float Hills(float x, float z)
        {
            float h = (Mathf.PerlinNoise(x * 0.008f + 11.3f, z * 0.008f + 3.7f) - 0.42f) * 9f;  // broad hills
            h += (Mathf.PerlinNoise(x * 0.024f + 41.1f, z * 0.024f + 7.9f) - 0.5f) * 3f;       // bumps
            h += (Mathf.PerlinNoise(x * 0.09f + 5.5f, z * 0.09f + 91.2f) - 0.5f) * 0.6f;       // roughness
            // Ridges beyond the playable area so the battlefield sits in a valley.
            float edge = Mathf.InverseLerp(95f, 230f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
            h += edge * edge * 32f * (0.7f + 0.6f * Mathf.PerlinNoise(x * 0.015f, z * 0.015f));
            return h;
        }

        // 0 inside a flat site, easing up to 1 a little outside it.
        static float FlatMask(float x, float z, out bool bare)
        {
            float mask = 1f;
            bare = false;
            foreach (var s in sites)
            {
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(s.center.x, s.center.z));
                float m = Mathf.SmoothStep(0f, 1f, (d - s.radius) / (s.radius * 0.6f + 6f));
                if (m < mask) mask = m;
                if (s.bare && d < s.radius * 0.9f) bare = true;
            }
            return mask;
        }

        // ---- Texturing ----

        // Layer order: 0 grass, 1 dry meadow, 2 dirt, 3 rock.
        static TerrainLayer[] BuildLayers()
        {
            return new[]
            {
                Layer("grass", new Color(0.26f, 0.33f, 0.17f), new Color(0.20f, 0.27f, 0.12f), 10f),
                Layer("meadow", new Color(0.42f, 0.42f, 0.25f), new Color(0.33f, 0.33f, 0.18f), 10f),
                Layer("dirt", new Color(0.36f, 0.30f, 0.22f), new Color(0.27f, 0.22f, 0.16f), 6f),
                Layer("rock", new Color(0.42f, 0.41f, 0.39f), new Color(0.30f, 0.29f, 0.28f), 12f),
            };
        }

        // Uses Resources/Textures/Terrain/<name>_diff (and _nor) when present, else a generated noise texture.
        static TerrainLayer Layer(string name, Color light, Color dark, float tile)
        {
            var diffuse = Resources.Load<Texture2D>("Textures/Terrain/" + name + "_diff");
            var normal = Resources.Load<Texture2D>("Textures/Terrain/" + name + "_nor");
            return new TerrainLayer
            {
                name = name,
                diffuseTexture = diffuse != null ? diffuse : NoiseTexture(light, dark, name.GetHashCode()),
                normalMapTexture = normal,
                tileSize = new Vector2(tile, tile),
            };
        }

        static float[,,] BuildSplat(TerrainData data)
        {
            var splat = new float[SplatRes, SplatRes, 4];
            for (int zi = 0; zi < SplatRes; zi++)
            {
                for (int xi = 0; xi < SplatRes; xi++)
                {
                    float u = (xi + 0.5f) / SplatRes, v = (zi + 0.5f) / SplatRes;
                    float x = -Size / 2f + u * Size, z = -Size / 2f + v * Size;
                    float steep = data.GetSteepness(u, v);
                    FlatMask(x, z, out bool bare);

                    float rock = Mathf.SmoothStep(0f, 1f, (steep - 16f) / 14f);
                    float dirt = bare ? 1f : Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.05f + 300f, z * 0.05f) - 0.68f) * 6f);
                    float meadow = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.02f + 70f, z * 0.02f + 20f) - 0.45f) * 4f);
                    float grass = 1f;

                    // Each layer covers the ones before it.
                    float[] w = new float[4];
                    w[3] = rock;
                    w[2] = dirt * (1f - rock);
                    w[1] = meadow * (1f - rock) * (1f - dirt);
                    w[0] = grass * (1f - rock) * (1f - dirt) * (1f - meadow);
                    float sum = w[0] + w[1] + w[2] + w[3];
                    for (int k = 0; k < 4; k++) splat[zi, xi, k] = w[k] / sum;
                }
            }
            return splat;
        }

        // Tileable value-noise texture so placeholder ground isn't a flat colour.
        static Texture2D NoiseTexture(Color light, Color dark, int seed)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var pixels = new Color[size * size];
            var rng = new System.Random(seed);
            float[] lattice8 = Lattice(8, rng), lattice32 = Lattice(32, rng);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = 0.6f * Sample(lattice8, 8, x * 8f / size, y * 8f / size)
                            + 0.3f * Sample(lattice32, 32, x * 32f / size, y * 32f / size)
                            + 0.1f * (float)rng.NextDouble();
                    pixels[y * size + x] = Color.Lerp(dark, light, n);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        static float[] Lattice(int n, System.Random rng)
        {
            var values = new float[n * n];
            for (int i = 0; i < values.Length; i++) values[i] = (float)rng.NextDouble();
            return values;
        }

        static float Sample(float[] lattice, int n, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = Mathf.SmoothStep(0f, 1f, x - x0), fy = Mathf.SmoothStep(0f, 1f, y - y0);
            float At(int ix, int iy) => lattice[((iy % n + n) % n) * n + ((ix % n + n) % n)];
            float a = Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), fx);
            float b = Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }
    }
}

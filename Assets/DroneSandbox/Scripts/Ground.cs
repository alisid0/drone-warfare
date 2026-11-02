using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Rolling farmland terrain built in code from MapLayout: hills in the play area, wooded ridges around
    // the edge opening into a river valley, a river channel, dirt roads, and flat sites (bases, fixed
    // defences) levelled to y = 0. Also answers "how high is the ground here?".
    public static class Ground
    {
        public const float Size = 480f;   // terrain is Size x Size metres, centred on the map
        const float Sink = 8f;            // terrain sits this far below y = 0, so the river and hollows can dip below the flat sites
        const float HeightRange = 60f;
        const int HeightRes = 513;
        const int SplatRes = 1024;

        public struct FlatSite
        {
            public Vector3 center;
            public float radius;
            public bool bare; // dirt instead of grass (base pads, emplacements)
            public FlatSite(Vector3 c, float r, bool bareGround) { center = c; radius = r; bare = bareGround; }
        }

        static Terrain terrain;
        static readonly List<FlatSite> sites = new List<FlatSite>();

        public static Terrain Current => terrain;

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

        public static Terrain Build(Transform parent)
        {
            MapLayout.Prepare();
            sites.Clear();
            sites.AddRange(MapLayout.FlatSites);

            var data = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = SplatRes };
            data.size = new Vector3(Size, HeightRange, Size);
            data.SetHeights(0, 0, BuildHeights());
            data.terrainLayers = BuildLayers();
            data.SetAlphamaps(0, 0, BuildSplat(data));

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-Size / 2f, -Sink, -Size / 2f);
            terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 2f;
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
            var river = MapLayout.RiverField;
            var roads = MapLayout.RoadField;
            float step = Size / (HeightRes - 1);
            var world = new float[HeightRes, HeightRes]; // metres above y = 0

            // Hills, opened into a valley along the river and levelled under the flat sites.
            for (int zi = 0; zi < HeightRes; zi++)
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    world[zi, xi] = Hills(x, z, river.Distance(x, z)) * FlatMask(x, z, out _);
                }
            }

            // Roads follow a smoothed copy of the ground, sunk in a little.
            for (int i = 0; i < roads.points.Count; i++) roads.values[i] = Sample(world, roads.points[i]);
            SmoothValues(roads, 6);
            for (int zi = 0; zi < HeightRes; zi++)
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float d = roads.Query(x, z, out float level, out _);
                    if (d > MapLayout.RoadHalfWidth + 6f) continue;
                    float weight = 1f - Mathf.SmoothStep(0f, 1f, (d - MapLayout.RoadHalfWidth - 0.5f) / 5.5f);
                    float dip = 0.12f * Mathf.Clamp01(1f - d / MapLayout.RoadHalfWidth);
                    weight *= FlatMask(x, z, out _); // flat sites stay flat
                    world[zi, xi] = Mathf.Lerp(world[zi, xi], level - dip, weight);
                }
            }

            // The river: a water level that only ever falls downstream, kept below the banks on both sides,
            // then a channel carved under it.
            float hw = MapLayout.RiverHalfWidth;
            var line = river.points;
            for (int i = 0; i < line.Count; i++)
            {
                Vector2 dir = (line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x);
                float lowest = Sample(world, line[i]);
                foreach (float offset in new[] { hw + 3f, hw + 10f })
                {
                    lowest = Mathf.Min(lowest, Sample(world, line[i] + side * offset));
                    lowest = Mathf.Min(lowest, Sample(world, line[i] - side * offset));
                }
                river.values[i] = lowest - 0.7f;
            }
            MakeFalling(river.values);
            SmoothValues(river, 8);
            MakeFalling(river.values);

            for (int zi = 0; zi < HeightRes; zi++)
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float d = river.Query(x, z, out float water, out _);
                    if (d > hw + 45f) continue;
                    float h = world[zi, xi];
                    if (d <= hw)
                    {
                        float k = d / hw;
                        h = Mathf.Min(h, water - 0.3f - 1.5f * (1f - k * k)); // river bed
                    }
                    else
                    {
                        h = Mathf.Min(h, water + 0.3f + (d - hw) * 0.5f); // banks no steeper than this
                        if (d < hw + 2.5f) h = Mathf.Max(h, water + 0.12f); // and never below the water right at the edge
                    }
                    world[zi, xi] = h;
                }
            }

            var heights = new float[HeightRes, HeightRes];
            for (int zi = 0; zi < HeightRes; zi++)
                for (int xi = 0; xi < HeightRes; xi++)
                    heights[zi, xi] = Mathf.Clamp01((world[zi, xi] + Sink) / HeightRange);
            return heights;
        }

        // Height above y = 0 before flattening, in metres.
        static float Hills(float x, float z, float riverDistance)
        {
            float h = (Mathf.PerlinNoise(x * 0.008f + 11.3f, z * 0.008f + 3.7f) - 0.42f) * 9f;  // broad hills
            h += (Mathf.PerlinNoise(x * 0.024f + 41.1f, z * 0.024f + 7.9f) - 0.5f) * 3f;       // bumps
            h += (Mathf.PerlinNoise(x * 0.09f + 5.5f, z * 0.09f + 91.2f) - 0.5f) * 0.6f;       // roughness
            // Ridges beyond the playable area so the battlefield sits in a valley, with a gap for the river.
            float edge = Mathf.InverseLerp(95f, 230f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
            float valley = Mathf.SmoothStep(0f, 1f, (riverDistance - 20f) / 55f);
            h += edge * edge * 32f * (0.7f + 0.6f * Mathf.PerlinNoise(x * 0.015f, z * 0.015f)) * valley;
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

        // Bilinear sample of a heights grid at world (x, z).
        static float Sample(float[,] grid, Vector2 p)
        {
            float step = Size / (HeightRes - 1);
            float gx = Mathf.Clamp((p.x + Size / 2f) / step, 0f, HeightRes - 1.001f);
            float gz = Mathf.Clamp((p.y + Size / 2f) / step, 0f, HeightRes - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            float a = Mathf.Lerp(grid[z0, x0], grid[z0, x0 + 1], fx);
            float b = Mathf.Lerp(grid[z0 + 1, x0], grid[z0 + 1, x0 + 1], fx);
            return Mathf.Lerp(a, b, fz);
        }

        // Moving average of a line field's values over +-window points (lines are kept apart).
        static void SmoothValues(PathField field, int window)
        {
            var source = (float[])field.values.Clone();
            for (int i = 0; i < source.Length; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int j = i - window; j <= i + window; j++)
                {
                    if (j < 0 || j >= source.Length || !SameLine(field, i, j)) continue;
                    sum += source[j];
                    count++;
                }
                field.values[i] = sum / count;
            }
        }

        static bool SameLine(PathField field, int i, int j)
        {
            int lo = Mathf.Min(i, j), hi = Mathf.Max(i, j);
            for (int k = lo; k < hi; k++)
                if (!field.IsValidSegment(k)) return false;
            return true;
        }

        static void MakeFalling(float[] levels)
        {
            for (int i = 1; i < levels.Length; i++) levels[i] = Mathf.Min(levels[i], levels[i - 1]);
        }

        // ---- Texturing ----

        // Layer order: 0 grass, 1 a second grass in patches (breaks up the tiling), 2 dirt, 3 rock.
        // The grass textures are tinted to a lush summer green.
        static TerrainLayer[] BuildLayers()
        {
            return new[]
            {
                Layer("meadow", 7f, new Color(0.56f, 0.86f, 0.36f), new Color(0.26f, 0.36f, 0.15f), new Color(0.19f, 0.28f, 0.10f)),
                Layer("lush", 6f, new Color(0.72f, 0.98f, 0.50f), new Color(0.30f, 0.38f, 0.16f), new Color(0.22f, 0.30f, 0.12f)),
                Layer("dirt", 5f, Color.white, new Color(0.36f, 0.30f, 0.22f), new Color(0.27f, 0.22f, 0.16f)),
                Layer("rock", 12f, Color.white, new Color(0.42f, 0.41f, 0.39f), new Color(0.30f, 0.29f, 0.28f)),
            };
        }

        // Uses Resources/Textures/Terrain/<name>_diff (and _nor) when present, else a generated noise texture.
        static TerrainLayer Layer(string name, float tile, Color tint, Color light, Color dark)
        {
            var diffuse = Resources.Load<Texture2D>("Textures/Terrain/" + name + "_diff");
            var normal = Resources.Load<Texture2D>("Textures/Terrain/" + name + "_nor");
            return new TerrainLayer
            {
                name = name,
                diffuseTexture = diffuse != null ? diffuse : NoiseTexture(light, dark, name.GetHashCode()),
                normalMapTexture = normal,
                tileSize = new Vector2(tile, tile),
                diffuseRemapMin = Vector4.zero,
                diffuseRemapMax = diffuse != null ? (Vector4)tint : Vector4.one, // URP Terrain Lit multiplies the layer by this
            };
        }

        static float[,,] BuildSplat(TerrainData data)
        {
            var river = MapLayout.RiverField;
            var roads = MapLayout.RoadField;
            float hw = MapLayout.RiverHalfWidth, roadHw = MapLayout.RoadHalfWidth;
            var splat = new float[SplatRes, SplatRes, 4];
            for (int zi = 0; zi < SplatRes; zi++)
            {
                for (int xi = 0; xi < SplatRes; xi++)
                {
                    float u = (xi + 0.5f) / SplatRes, v = (zi + 0.5f) / SplatRes;
                    float x = -Size / 2f + u * Size, z = -Size / 2f + v * Size;
                    float steep = data.GetSteepness(u, v);
                    FlatMask(x, z, out bool bare);

                    float rock = Mathf.SmoothStep(0f, 1f, (steep - 22f) / 12f);
                    float dirt = bare ? 1f : 0f;
                    // Dirt roads: two wheel tracks with a strip of grass down the middle.
                    float dRoad = roads.Distance(x, z);
                    if (dRoad < roadHw + 1f)
                    {
                        float track = 1f - Mathf.SmoothStep(0f, 1f, (dRoad - roadHw + 0.4f) / 1.2f);
                        if (dRoad < 0.5f) track *= 0.35f;
                        dirt = Mathf.Max(dirt, track);
                    }
                    // Muddy river banks.
                    float dRiver = river.Distance(x, z);
                    if (dRiver < hw + 4f) dirt = Mathf.Max(dirt, 0.9f * (1f - Mathf.SmoothStep(0f, 1f, (dRiver - hw - 0.5f) / 3f)));
                    // A few worn patches.
                    dirt = Mathf.Max(dirt, 0.7f * Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.06f + 300f, z * 0.06f) - 0.76f) * 6f));
                    float variation = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.013f + 70f, z * 0.013f + 20f) * 0.7f
                        + Mathf.PerlinNoise(x * 0.05f + 9f, z * 0.05f + 4f) * 0.3f - 0.45f) * 4f);

                    // Each layer covers the ones before it.
                    float[] w = new float[4];
                    w[3] = rock;
                    w[2] = dirt * (1f - rock);
                    w[1] = variation * (1f - rock) * (1f - dirt);
                    w[0] = (1f - rock) * (1f - dirt) * (1f - variation);
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
                    float n = 0.6f * SampleLattice(lattice8, 8, x * 8f / size, y * 8f / size)
                            + 0.3f * SampleLattice(lattice32, 32, x * 32f / size, y * 32f / size)
                            + 0.1f * (float)rng.NextDouble();
                    pixels[y * size + x] = Color.Lerp(dark, light, n);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        internal static float[] Lattice(int n, System.Random rng)
        {
            var values = new float[n * n];
            for (int i = 0; i < values.Length; i++) values[i] = (float)rng.NextDouble();
            return values;
        }

        internal static float SampleLattice(float[] lattice, int n, float x, float y)
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

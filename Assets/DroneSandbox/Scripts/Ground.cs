using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Rolling farmland terrain built in code from MapLayout: hills in the play area (as high as the sector's type
    // makes them), ridges around the edge opening into a river valley, a river channel, the reservoir, lakes and
    // pools, dirt roads, railway beds, a quarry pit or a trench line where the sector has them, and flat sites
    // (bases, defences, buildings) levelled to y = 0. Also answers "how high is the ground here?".
    public static class Ground
    {
        public const float Size = 480f;   // terrain is Size x Size metres, centred on the map
        const float Sink = 8f;            // terrain sits this far below y = 0, so the river and hollows can dip below the flat sites
        const float HeightRange = 80f;
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
            if (MapLayout.HasDam) Reservoir(world, step); // before the roads, so a road past the lake follows its banks
            Ponds(world, step);
            Quarry(world, step);

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

            // Railway beds: smoother still (trains can't climb much).
            var rails = MapLayout.RailField;
            if (rails.points.Count > 0)
            {
                for (int i = 0; i < rails.points.Count; i++) rails.values[i] = Sample(world, rails.points[i]);
                SmoothValues(rails, 20);
                for (int zi = 0; zi < HeightRes; zi++)
                {
                    for (int xi = 0; xi < HeightRes; xi++)
                    {
                        float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                        float d = rails.Query(x, z, out float level, out _);
                        if (d > MapLayout.RailHalfWidth + 8f) continue;
                        float weight = (1f - Mathf.SmoothStep(0f, 1f, (d - MapLayout.RailHalfWidth - 0.5f) / 7.5f)) * FlatMask(x, z, out _);
                        world[zi, xi] = Mathf.Lerp(world[zi, xi], level, weight);
                    }
                }
            }
            Trench(world, step);

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
                    if (d > hw + 45f || MapLayout.DamLocal(x, z).x < -1f) continue; // the lake side of the dam is shaped already
                    float h = world[zi, xi];
                    float wet = MapLayout.RiverWidthAt(x, z); // the water's own width here, inside the full channel
                    if (d <= wet)
                    {
                        float k = d / wet;
                        h = Mathf.Min(h, water - 0.3f - 1.5f * (1f - k * k)); // river bed
                    }
                    else
                    {
                        h = Mathf.Min(h, water + 0.3f + (d - wet) * 0.5f); // banks no steeper than this
                        if (d < wet + 2.5f) h = Mathf.Max(h, water + 0.12f); // and never below the water right at the edge
                    }
                    world[zi, xi] = h;
                }
            }

            if (MapLayout.HasDam && line.Count > 0) BelowDam(world, step, river.values[0]);

            var heights = new float[HeightRes, HeightRes];
            for (int zi = 0; zi < HeightRes; zi++)
                for (int xi = 0; xi < HeightRes; xi++)
                    heights[zi, xi] = Mathf.Clamp01((world[zi, xi] + Sink) / HeightRange);
            return heights;
        }

        // The lake: a basin under the water with a shallow shelf at the edge, then banks rising gently out of it
        // (about 12 degrees) and easing into the natural hills further out. Where the dam's ends meet the valley
        // sides the ground comes up to the road on its crest.
        static void Reservoir(float[,] world, float step)
        {
            float level = MapLayout.LakeLevel, crest = level + 2.1f;
            for (int zi = 0; zi < HeightRes; zi++)
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float s = MapLayout.LakeDistance(x, z);
                    Vector2 local = MapLayout.DamLocal(x, z);
                    bool nearDam = local.x > -20f && local.x < 25f && Mathf.Abs(local.y) < 80f;
                    if (s > 60f && !nearDam) continue;
                    float h = world[zi, xi];
                    float bumps = (Mathf.PerlinNoise(x * 0.07f + 13f, z * 0.07f + 77f) - 0.5f) * 2.5f;
                    if (s < 0f)
                    {
                        h = Mathf.Min(h, level - Mathf.Min(14f, 0.4f - s * 0.28f) + bumps * 0.3f);
                    }
                    else if (local.x < -1f || Mathf.Abs(local.y) > MapLayout.DamHalfSpan)
                    {
                        float bank = level + 0.3f + s * 0.21f + bumps * Mathf.Clamp01(s / 8f);
                        float natural = Mathf.SmoothStep(0f, 1f, (s - 10f) / 45f);
                        float upstream = 1f - Mathf.SmoothStep(0f, 1f, (local.x - 5f) / 25f); // nothing to bank downstream
                        h = Mathf.Lerp(h, Mathf.Lerp(bank, h, natural), upstream);
                    }
                    // The valley sides the dam is built into, up to the road on its crest.
                    float side = Mathf.Abs(local.y);
                    if (nearDam && side > MapLayout.DamHalfSpan)
                    {
                        float abutment = crest + Mathf.Max(0f, side - 44f) * 0.35f + bumps * 0.5f;
                        float weight = (1f - Mathf.SmoothStep(0f, 1f, (Mathf.Abs(local.x - 2f) - 14f) / 8f))
                                     * (1f - Mathf.SmoothStep(0f, 1f, (side - 70f) / 10f));
                        h = Mathf.Lerp(h, Mathf.Max(h, abutment), weight);
                    }
                    world[zi, xi] = h;
                }
            }
        }

        // Lakes and pools: each sits a little below the lowest ground round its shore, with a basin under the
        // water and banks easing up from it into the natural ground. Their levels go back into the plan for Water.
        static void Ponds(float[,] world, float step)
        {
            var ponds = MapLayout.Plan.ponds;
            if (ponds.Count == 0) return;
            for (int i = 0; i < ponds.Count; i++)
            {
                var pond = ponds[i];
                float level = float.MaxValue;
                for (int k = 0; k < 32; k++)
                {
                    float a = k * Mathf.PI * 2f / 32f;
                    level = Mathf.Min(level, Sample(world, pond.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * pond.radius * 1.2f));
                }
                pond.level = level - 0.35f;
                ponds[i] = pond;
            }
            for (int zi = 0; zi < HeightRes; zi++)
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float s = MapLayout.PondDistance(x, z, out int i);
                    if (i < 0 || s > 40f) continue;
                    float level = ponds[i].level, h = world[zi, xi];
                    float bumps = (Mathf.PerlinNoise(x * 0.09f + 17f, z * 0.09f + 41f) - 0.5f) * 1.5f;
                    if (s < 0f)
                    {
                        // A shallow shelf for the first 3 m out from the shore (down to half a metre), then sloping
                        // to the deepest: a pond you can see the bottom of near its edges, as farm ponds are.
                        float deepest = ponds[i].radius > 60f ? 7f : 1.7f;
                        float depth = -s < 3f ? 0.08f - s * 0.14f : Mathf.Min(deepest, 0.5f + (-s - 3f) * 0.2f);
                        h = Mathf.Min(h, level - depth + bumps * 0.15f);
                    }
                    else
                    {
                        float bank = level + 0.25f + s * 0.12f + bumps * Mathf.Clamp01(s / 10f);
                        h = Mathf.Lerp(bank, h, Mathf.SmoothStep(0f, 1f, (s - 4f) / 32f));
                    }
                    world[zi, xi] = h;
                }
            }
        }

        // A stone quarry: a pit cut down in benches, its floor about 12 m below the ground round it.
        static void Quarry(float[,] world, float step)
        {
            var plan = MapLayout.Plan;
            if (!plan.Has(SectorFeature.Quarry)) return;
            float r = plan.quarryRadius, rim = Sample(world, plan.quarry);
            for (int zi = 0; zi < HeightRes; zi++)
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float d = Vector2.Distance(new Vector2(x, z), plan.quarry);
                    if (d > r + 12f) continue;
                    float depth = 1f - Mathf.SmoothStep(0f, 1f, (d / r - 0.45f) / 0.55f);
                    float benches = Mathf.Floor(depth * 4f) / 4f;
                    float pit = rim - 12f * Mathf.Lerp(depth, benches, 0.7f);
                    float outside = Mathf.SmoothStep(0f, 1f, (d - r) / 12f);
                    world[zi, xi] = Mathf.Lerp(Mathf.Min(world[zi, xi], pit), world[zi, xi], outside);
                }
            }
        }

        // A trench dug along the line behind the dragon's teeth, broken where roads cross it.
        static void Trench(float[,] world, float step)
        {
            var line = MapLayout.Plan.trenchLine;
            if (line == null) return;
            for (int zi = 0; zi < HeightRes; zi++)
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float d = DistanceToLine(new Vector2(x, z), line);
                    if (d > 1.6f || MapLayout.RoadField.Distance(x, z) < MapLayout.RoadHalfWidth + 3f) continue;
                    float k = d / 1.6f;
                    world[zi, xi] -= 1.7f * (1f - k * k * k * k);
                }
            }
        }

        static float DistanceToLine(Vector2 p, Vector2[] line)
        {
            float best = float.MaxValue;
            for (int i = 0; i < line.Length - 1; i++)
            {
                Vector2 a = line[i], ab = line[i + 1] - a;
                float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        // Below the dam: a rocky gorge whose sides climb from the river to the ends of the dam, opening out
        // downstream, and a wide plunge pool where the spillway lands.
        static void BelowDam(float[,] world, float step, float water)
        {
            float crest = MapLayout.LakeLevel + 2.1f;
            for (int zi = 0; zi < HeightRes; zi++)
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    Vector2 local = MapLayout.DamLocal(x, z);
                    if (local.x < -2f || local.x > 50f || Mathf.Abs(local.y) > 60f) continue;
                    float h = world[zi, xi];
                    float side = Mathf.Abs(local.y);
                    float bumps = (Mathf.PerlinNoise(x * 0.11f + 31f, z * 0.11f + 5f) - 0.5f) * 2f;
                    float open = 1f - Mathf.SmoothStep(0f, 1f, (local.x - 22f) / 26f);
                    if (side > 12f)
                    {
                        float wall = Mathf.Lerp(water + 0.5f, crest, Mathf.SmoothStep(0f, 1f, (side - 12f) / 20f)) + bumps;
                        h = Mathf.Lerp(h, Mathf.Max(h, wall), open);
                    }
                    float pool = Mathf.Pow((local.x - 12f) / 13f, 2f) + Mathf.Pow(local.y / 12f, 2f);
                    if (pool < 1.2f) h = Mathf.Min(h, water - 0.3f - 1.6f * Mathf.Clamp01(1.2f - pool));
                    world[zi, xi] = h;
                }
            }
        }

        // Height above y = 0 before flattening, in metres. The sector's type sets how high the hills and the
        // ridges round the edge are; its seed moves the noise, so no two sectors share a landscape.
        static float Hills(float x, float z, float riverDistance)
        {
            var plan = MapLayout.Plan;
            float k = plan.hills / 9f, nx = x + plan.noiseOffset.x, nz = z + plan.noiseOffset.y;
            float h = (Mathf.PerlinNoise(nx * 0.008f + 11.3f, nz * 0.008f + 3.7f) - 0.42f) * 9f * k;                        // broad hills
            h += (Mathf.PerlinNoise(nx * 0.024f + 41.1f, nz * 0.024f + 7.9f) - 0.5f) * 3f * Mathf.Clamp(k, 0.4f, 1.6f);     // bumps
            h += (Mathf.PerlinNoise(nx * 0.09f + 5.5f, nz * 0.09f + 91.2f) - 0.5f) * 0.6f;                                  // roughness
            // Ridges beyond the playable area so the battlefield sits in a valley, with a gap for the river.
            float edge = Mathf.InverseLerp(95f, 230f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
            float valley = Mathf.SmoothStep(0f, 1f, (riverDistance - 20f) / 55f);
            h += edge * edge * plan.ridges * (0.7f + 0.6f * Mathf.PerlinNoise(nx * 0.015f, nz * 0.015f)) * valley;
            return h;
        }

        // Where a bare site's worn ground stops, as a share of its radius in the direction (dx, dz): ragged, from
        // about 0.75 to 1.2, so a compound's trodden earth spills out in places and grass creeps in at others.
        public static float SiteEdge(float dx, float dz)
        {
            float a = Mathf.Atan2(dz, dx);
            return 0.95f + 0.12f * Mathf.Sin(3f * a + 1.3f) + 0.07f * Mathf.Sin(5f * a + 0.4f) + 0.04f * Mathf.Sin(9f * a + 2.2f)
                   + 0.12f * (Mathf.PerlinNoise(dx * 0.15f + 40f, dz * 0.15f + 9f) - 0.5f);
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
                if (s.bare && d < s.radius * SiteEdge(x - s.center.x, z - s.center.z)) bare = true;
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
                // Both photos are brownish ground with grass through it; the tints make the meadow a sunlit summer
                // green and the lush grass (in damp hollows, by woods and water) a deeper, bluer green, so from the
                // game camera, where the texture is a blur, the two read as the light and dark of a real meadow.
                Layer("meadow", 7f, new Color(0.44f, 0.80f, 0.34f), new Color(0.26f, 0.36f, 0.15f), new Color(0.19f, 0.28f, 0.10f)),
                Layer("lush", 6f, new Color(0.24f, 0.60f, 0.24f), new Color(0.30f, 0.38f, 0.16f), new Color(0.22f, 0.30f, 0.12f)),
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
                diffuseRemapMax = diffuse != null ? (Vector4)tint : Vector4.one, // the terrain shader multiplies the layer by this
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
                    // Gravel railway beds, and the quarry's dusty floor.
                    if (MapLayout.RailField.points.Count > 0)
                    {
                        float dRail = MapLayout.RailField.Distance(x, z);
                        if (dRail < MapLayout.RailHalfWidth + 1.5f)
                            dirt = Mathf.Max(dirt, 1f - Mathf.SmoothStep(0f, 1f, (dRail - MapLayout.RailHalfWidth) / 1.5f));
                    }
                    var plan = MapLayout.Plan;
                    if (plan.Has(SectorFeature.Quarry))
                    {
                        float dq = Vector2.Distance(new Vector2(x, z), plan.quarry);
                        if (dq < plan.quarryRadius)
                            dirt = Mathf.Max(dirt, 1f - Mathf.SmoothStep(0f, 1f, (dq - plan.quarryRadius * 0.4f) / (plan.quarryRadius * 0.6f)));
                    }
                    // Muddy river banks.
                    float dRiver = river.Distance(x, z);
                    if (dRiver < hw + 4f) dirt = Mathf.Max(dirt, 0.9f * (1f - Mathf.SmoothStep(0f, 1f, (dRiver - hw - 0.5f) / 3f)));
                    // A pale fringe of stones and gravel at the water's edge, in patches, outlining the river from above.
                    float edgeStones = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(dRiver - hw - 0.8f) / 1.6f);
                    if (edgeStones > 0f)
                        rock = Mathf.Max(rock, edgeStones * Mathf.SmoothStep(0f, 1f, Mathf.PerlinNoise(x * 0.12f + 11f, z * 0.12f + 5f) * 1.8f - 0.45f));
                    // The lake: a stony shore just above the water, and a rocky bed you see through the shallows.
                    float lake = MapLayout.LakeDistance(x, z);
                    if (lake < 3f)
                    {
                        float shore = 1f - Mathf.SmoothStep(0f, 1f, (lake - 0.5f) / 2.5f);
                        if (MapLayout.ReservoirDistance(x, z) <= lake + 0.01f)
                        {
                            rock = Mathf.Max(rock, shore * Mathf.SmoothStep(0f, 1f, Mathf.PerlinNoise(x * 0.15f + 50f, z * 0.15f) * 1.6f - 0.3f));
                            dirt = Mathf.Max(dirt, shore);
                        }
                        // A pond's edge is lush grass and reeds, trodden to mud only in patches (where things come to
                        // drink), not a ring of bare earth; under the water a pale bed of silt and gravel shows
                        // through the shallows.
                        else if (lake < 0f)
                        {
                            rock = Mathf.Max(rock, 0.45f);
                            dirt = Mathf.Max(dirt, 0.8f);
                        }
                        else dirt = Mathf.Max(dirt, shore * Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.18f + 7f, z * 0.18f + 3f) - 0.5f) * 4f));
                    }
                    // A few worn patches.
                    dirt = Mathf.Max(dirt, 0.7f * Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.06f + 300f, z * 0.06f) - 0.76f) * 6f));
                    // Light and dark grass in big drifts, smaller patches and a fine mottle, so from above a meadow
                    // has the uneven colour of real grass, and deeper green in the damp along the river and the lake.
                    float damp = Mathf.Max(1f - Mathf.SmoothStep(0f, 1f, (dRiver - hw) / 16f), 1f - Mathf.SmoothStep(0f, 1f, lake / 14f));
                    float patches = Mathf.PerlinNoise(x * 0.012f + 70f, z * 0.012f + 20f) * 0.55f
                        + Mathf.PerlinNoise(x * 0.045f + 9f, z * 0.045f + 4f) * 0.3f + Mathf.PerlinNoise(x * 0.2f + 31f, z * 0.2f + 17f) * 0.15f;
                    float variation = Mathf.Max(Mathf.SmoothStep(0f, 1f, (patches - 0.42f) * 3.5f), damp * 0.85f);
                    // A verge of rank, darker grass round each field, where the plough doesn't reach.
                    if (MapLayout.InField(new Vector2(x, z), 2.2f) && !MapLayout.InField(new Vector2(x, z), 0f)) variation = Mathf.Max(variation, 0.9f);

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

using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Rolling farmland terrain built in code from MapLayout: hills in the play area (as high as the sector's type
    // makes them), ridges around the edge opening into a river valley, a river channel, the reservoir, lakes and
    // pools, dirt roads, railway beds, a quarry pit or a trench line where the sector has them, and flat sites
    // (bases, defences, buildings) levelled to y = 0. Also answers "how high is the ground here?".
    // The land's shapes (hills, ridges, the lake's banks, the quarry) are the old 480 m map's, made Scale.Distance
    // times bigger; the river's channel, the roads and the dam are real sizes and stay as they were.
    public static class Ground
    {
        public const float Size = 480f * Scale.Distance;   // terrain is Size x Size metres, centred on the map
        const float Sink = 8f * Scale.Distance;            // terrain sits this far below y = 0, so the river and hollows can dip below the flat sites
        const float HeightRange = 80f * Scale.Distance;
        const int HeightRes = 1025;                        // about 2 m between heights
        const int SplatRes = 2048;                         // about 1 m per texel

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

        // Beyond the sector's terrain, in the big world, the far terrain's ground (drones fly out over it).
        public static float HeightAt(Vector3 p)
        {
            if (terrain == null) return 0f;
            if (FarTerrain.Active && (Mathf.Abs(p.x) > Size / 2f || Mathf.Abs(p.z) > Size / 2f)) return FarTerrain.SurfaceAt(new Vector2(p.x, p.z));
            return terrain.SampleHeight(p) + terrain.transform.position.y;
        }

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
            if (FarTerrain.Active && FarTerrain.Raycast(ray, out point)) return true;
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
            IndexSites();
            ClearWorn();

            FieldPlot.Warp(Vector2.zero); // its lookup grid is made on first use: make it before the parallel loops
            var data = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = SplatRes };
            data.size = new Vector3(Size, HeightRange, Size);
            data.SetHeights(0, 0, BuildHeights());
            data.terrainLayers = BuildLayers();
            lieHeights = BoxAverage(worldHeights, Mathf.RoundToInt(40f / (Size / (HeightRes - 1))));
            data.SetAlphamaps(0, 0, BuildSplat(data));

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-Size / 2f, -Sink, -Size / 2f);
            terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 2f;
            terrain.basemapDistance = 1200f;
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
            System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    world[zi, xi] = Hills(x, z, river.Distance(x, z)) * FlatMask(x, z, out _);
                }
            });
            if (MapLayout.HasDam) Reservoir(world, step); // before the roads, so a road past the lake follows its banks
            Ponds(world, step);
            Quarry(world, step);

            // Roads follow a smoothed copy of the ground, sunk in a little.
            for (int i = 0; i < roads.points.Count; i++) roads.values[i] = Sample(world, roads.points[i]);
            SmoothValues(roads, 6);
            System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
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
            });

            // Railway beds: smoother still (trains can't climb much).
            var rails = MapLayout.RailField;
            if (rails.points.Count > 0)
            {
                for (int i = 0; i < rails.points.Count; i++) rails.values[i] = Sample(world, rails.points[i]);
                SmoothValues(rails, 20);
                System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
                {
                    for (int xi = 0; xi < HeightRes; xi++)
                    {
                        float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                        float d = rails.Query(x, z, out float level, out _);
                        if (d > MapLayout.RailHalfWidth + 8f) continue;
                        float weight = (1f - Mathf.SmoothStep(0f, 1f, (d - MapLayout.RailHalfWidth - 0.5f) / 7.5f)) * FlatMask(x, z, out _);
                        world[zi, xi] = Mathf.Lerp(world[zi, xi], level, weight);
                    }
                });
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

            System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float d = river.Query(x, z, out float water, out _);
                    if (d > hw + 45f * Scale.Distance || MapLayout.DamLocal(x, z).x < -1f) continue; // the lake side of the dam is shaped already
                    float h = world[zi, xi];
                    float wet = MapLayout.RiverWidthAt(x, z); // the water's own width here, inside the full channel
                    if (d <= wet)
                    {
                        float k = d / wet;
                        h = Mathf.Min(h, water - 0.3f - 1.5f * (1f - k * k)); // river bed
                    }
                    else
                    {
                        // Banks aren't a trench: in places a low shelf of gravel and mud up to 2.5 m wide just
                        // above the water, then a slope gentle in some reaches and steeper in others (a flat
                        // site beside the river keeps its level ground).
                        float shelf = BankShelf(x, z);
                        float slope = 0.32f + 0.28f * Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f + 61f);
                        float bank = water + 0.18f + Mathf.Min(d - wet, shelf) * 0.04f + Mathf.Max(0f, d - wet - shelf) * slope;
                        if (d < wet + 2.5f) bank = Mathf.Max(bank, water + 0.12f); // never below the water right at the edge
                        h = Mathf.Lerp(h, Mathf.Min(h, bank), FlatMask(x, z, out _));
                    }
                    world[zi, xi] = h;
                }
            });

            if (MapLayout.HasDam && line.Count > 0) BelowDam(world, step, river.values[0]);
            worldHeights = world;
            steepness = Slopes(world, step);

            var heights = new float[HeightRes, HeightRes];
            for (int zi = 0; zi < HeightRes; zi++)
                for (int xi = 0; xi < HeightRes; xi++)
                    heights[zi, xi] = Mathf.Clamp01((world[zi, xi] + Sink) / HeightRange);
            return heights;
        }

        // The land's height above y = 0 and its slope in degrees at each heightmap point, kept from the build so the
        // painter and the grass can ask from any thread (the terrain's own GetSteepness is main-thread only).
        static float[,] worldHeights, steepness;

        // The lie of the land: the average height within about 40 m of each heightmap point, so the painter can tell
        // a hollow (where water gathers and the grass grows dark) from a rise. Made once per sector, before the splat.
        static float[,] lieHeights;

        public static float SteepnessAt(float x, float z) => steepness != null ? Sample(steepness, new Vector2(x, z)) : 0f;

        // A box average of a grid over +-radius points, from a summed-area table (one pass, any radius).
        static float[,] BoxAverage(float[,] grid, int radius)
        {
            int rows = grid.GetLength(0), cols = grid.GetLength(1);
            var sums = new double[rows + 1, cols + 1];
            for (int z = 0; z < rows; z++)
            {
                double line = 0.0;
                for (int x = 0; x < cols; x++)
                {
                    line += grid[z, x];
                    sums[z + 1, x + 1] = sums[z, x + 1] + line;
                }
            }
            var result = new float[rows, cols];
            for (int z = 0; z < rows; z++)
            {
                int z0 = Mathf.Max(0, z - radius), z1 = Mathf.Min(rows, z + radius + 1);
                for (int x = 0; x < cols; x++)
                {
                    int x0 = Mathf.Max(0, x - radius), x1 = Mathf.Min(cols, x + radius + 1);
                    double total = sums[z1, x1] - sums[z0, x1] - sums[z1, x0] + sums[z0, x0];
                    result[z, x] = (float)(total / ((z1 - z0) * (x1 - x0)));
                }
            }
            return result;
        }

        static float[,] Slopes(float[,] world, float step)
        {
            var slopes = new float[HeightRes, HeightRes];
            System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
            {
                int z0 = Mathf.Max(zi - 1, 0), z1 = Mathf.Min(zi + 1, HeightRes - 1);
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    int x0 = Mathf.Max(xi - 1, 0), x1 = Mathf.Min(xi + 1, HeightRes - 1);
                    float dx = (world[zi, x1] - world[zi, x0]) / ((x1 - x0) * step), dz = (world[z1, xi] - world[z0, xi]) / ((z1 - z0) * step);
                    slopes[zi, xi] = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                }
            });
            return slopes;
        }

        // The lake: a basin under the water with a shallow shelf at the edge, then banks rising gently out of it
        // (about 12 degrees) and easing into the natural hills further out. Where the dam's ends meet the valley
        // sides the ground comes up to the road on its crest.
        static void Reservoir(float[,] world, float step)
        {
            float level = MapLayout.LakeLevel, crest = level + 2.1f;
            System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float s = MapLayout.LakeDistance(x, z);
                    Vector2 local = MapLayout.DamLocal(x, z);
                    bool nearDam = local.x > -20f && local.x < 25f && Mathf.Abs(local.y) < 80f;
                    if (s > 60f * Scale.Distance && !nearDam) continue;
                    float h = world[zi, xi];
                    float bumps = (Mathf.PerlinNoise(x * 0.07f + 13f, z * 0.07f + 77f) - 0.5f) * 2.5f;
                    if (s < 0f)
                    {
                        h = Mathf.Min(h, level - Mathf.Min(14f, 0.4f - s * 0.28f) + bumps * 0.3f);
                    }
                    else if (local.x < -1f || Mathf.Abs(local.y) > MapLayout.DamHalfSpan)
                    {
                        float bank = level + 0.3f + s * 0.21f + bumps * Mathf.Clamp01(s / 8f);
                        float natural = Mathf.SmoothStep(0f, 1f, (s - 10f * Scale.Distance) / (45f * Scale.Distance));
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
            });
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
            System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float s = MapLayout.PondDistance(x, z, out int i);
                    if (i < 0 || s > 40f * Scale.Distance) continue;
                    float level = ponds[i].level, h = world[zi, xi];
                    float bumps = (Mathf.PerlinNoise(x * 0.09f + 17f, z * 0.09f + 41f) - 0.5f) * 1.5f;
                    if (s < 0f)
                    {
                        // A shallow shelf for the first 3 m out from the shore (down to half a metre), then sloping
                        // to the deepest: a pond you can see the bottom of near its edges, as farm ponds are.
                        float deepest = ponds[i].radius > 60f * Scale.Distance ? 7f : 1.7f;
                        float depth = -s < 3f ? 0.08f - s * 0.14f : Mathf.Min(deepest, 0.5f + (-s - 3f) * 0.2f);
                        h = Mathf.Min(h, level - depth + bumps * 0.15f);
                    }
                    else
                    {
                        float bank = level + 0.25f + s * 0.12f + bumps * Mathf.Clamp01(s / 10f);
                        h = Mathf.Lerp(bank, h, Mathf.SmoothStep(0f, 1f, (s - 4f) / (32f * Scale.Distance)));
                    }
                    world[zi, xi] = h;
                }
            });
        }

        // A stone quarry: a pit cut down in benches, its floor about 50 m below the ground round it.
        static void Quarry(float[,] world, float step)
        {
            var plan = MapLayout.Plan;
            if (!plan.Has(SectorFeature.Quarry)) return;
            float r = plan.quarryRadius, rim = Sample(world, plan.quarry);
            System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float d = Vector2.Distance(new Vector2(x, z), plan.quarry);
                    if (d > r + 12f * Scale.Distance) continue;
                    float depth = 1f - Mathf.SmoothStep(0f, 1f, (d / r - 0.45f) / 0.55f);
                    float benches = Mathf.Floor(depth * 4f) / 4f;
                    float pit = rim - 12f * Scale.Distance * Mathf.Lerp(depth, benches, 0.7f);
                    float outside = Mathf.SmoothStep(0f, 1f, (d - r) / (12f * Scale.Distance));
                    world[zi, xi] = Mathf.Lerp(Mathf.Min(world[zi, xi], pit), world[zi, xi], outside);
                }
            });
        }

        // A trench dug along the line behind the dragon's teeth, broken where roads cross it.
        static void Trench(float[,] world, float step)
        {
            var line = MapLayout.Plan.trenchLine;
            if (line == null) return;
            System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
            {
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                    float d = DistanceToLine(new Vector2(x, z), line);
                    if (d > 1.6f || MapLayout.RoadField.Distance(x, z) < MapLayout.RoadHalfWidth + 3f) continue;
                    float k = d / 1.6f;
                    world[zi, xi] -= 1.7f * (1f - k * k * k * k);
                }
            });
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
            System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
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
            });
        }

        // Height above y = 0 before flattening, in metres. The sector's type sets how high the hills and the
        // ridges round the edge are; its seed moves the noise, so no two sectors share a landscape.
        static float Hills(float x, float z, float riverDistance)
        {
            var plan = MapLayout.Plan;
            const float S = Scale.Distance;
            float k = plan.hills / 9f, nx = x / S + plan.noiseOffset.x, nz = z / S + plan.noiseOffset.y; // the old map's noise, stretched
            float h = (Mathf.PerlinNoise(nx * 0.008f + 11.3f, nz * 0.008f + 3.7f) - 0.42f) * 9f * S * k;                    // broad hills
            h += (Mathf.PerlinNoise(nx * 0.024f + 41.1f, nz * 0.024f + 7.9f) - 0.5f) * 3f * S * Mathf.Clamp(k, 0.4f, 1.6f); // bumps
            // Real-sized undulation and roughness on top, the ground a walker would feel.
            h += (Mathf.PerlinNoise(x * 0.024f + 17.3f, z * 0.024f + 63.1f) - 0.5f) * 1.6f;
            h += (Mathf.PerlinNoise(x * 0.09f + 5.5f, z * 0.09f + 91.2f) - 0.5f) * 0.6f;
            // Ridges beyond the playable area so the battlefield sits in a valley, with a gap for the river.
            float edge = Mathf.InverseLerp(95f * S, 230f * S, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
            float valley = Mathf.SmoothStep(0f, 1f, (riverDistance - 20f * S) / (55f * S));
            h += edge * edge * plan.ridges * S * (0.7f + 0.6f * Mathf.PerlinNoise(nx * 0.015f, nz * 0.015f)) * valley;
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

        // Which flat sites reach each 64 m square (their own ground and the slope easing out of it), so the millions
        // of FlatMask questions while the land is shaped and painted each look at only the few nearby.
        const float SiteCell = 64f;
        static readonly int siteCellCount = Mathf.CeilToInt(Size / SiteCell);
        static List<FlatSite>[] siteCells;

        static float SiteReach(FlatSite s) => s.radius * 1.25f + (s.radius * 0.6f + 6f) * Scale.Distance;

        static void IndexSites()
        {
            siteCells = new List<FlatSite>[siteCellCount * siteCellCount];
            foreach (var s in sites)
            {
                float reach = SiteReach(s);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((s.center.x - reach + Size / 2f) / SiteCell));
                int x1 = Mathf.Min(siteCellCount - 1, Mathf.FloorToInt((s.center.x + reach + Size / 2f) / SiteCell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((s.center.z - reach + Size / 2f) / SiteCell));
                int z1 = Mathf.Min(siteCellCount - 1, Mathf.FloorToInt((s.center.z + reach + Size / 2f) / SiteCell));
                for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                        (siteCells[cz * siteCellCount + cx] ??= new List<FlatSite>()).Add(s);
            }
        }

        // 0 inside a flat site, easing up to 1 a little outside it.
        static float FlatMask(float x, float z, out bool bare)
        {
            float mask = 1f;
            bare = false;
            int cx = Mathf.Clamp(Mathf.FloorToInt((x + Size / 2f) / SiteCell), 0, siteCellCount - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((z + Size / 2f) / SiteCell), 0, siteCellCount - 1);
            var near = siteCells != null ? siteCells[cz * siteCellCount + cx] : sites;
            if (near == null) return mask;
            foreach (var s in near)
            {
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(s.center.x, s.center.z));
                float m = Mathf.SmoothStep(0f, 1f, (d - s.radius) / ((s.radius * 0.6f + 6f) * Scale.Distance));
                if (m < mask) mask = m;
                if (s.bare && d < s.radius * SiteEdge(x - s.center.x, z - s.center.z)) bare = true;
            }
            return mask;
        }

        // Wears bare ground into the finished terrain, for things placed after it was painted (a garden path, a
        // farmyard, a vegetable patch): earth along the line from `from` to `to`, `width` wide with soft, uneven
        // edges, by `strength` (1 for bare earth). Grass is planted afterwards and keeps off it (Worn); writing
        // the grass layers a second time brought back the black tussocks.
        // Painting waits for FlushWear (World calls it after the villages), which writes every strip with one
        // GetAlphamaps and one SetAlphamaps: each SetAlphamaps re-uploads the terrain's whole splat textures, and a
        // village cluster's few hundred paths, one call each, queued over 5 GB in one frame and lost the GPU (hd127).
        public static void Wear(Vector2 from, Vector2 to, float width, float strength)
        {
            if (terrain == null) return;
            var strip = Strip(from, to, width, strength, 2);
            IndexWorn(strip, worn.Count);
            worn.Add(strip);
            pending.Add(strip);
        }

        // Paints a strip of another ground layer the same way (1, the dark damp grass, draws the hedge lines between
        // village gardens). Grass still grows on it.
        public static void Paint(Vector2 from, Vector2 to, float width, float strength, int layer)
        {
            if (terrain != null) pending.Add(Strip(from, to, width, strength, layer));
        }

        // Writes the strips Wear and Paint have collected into the terrain's layers, over the one rectangle that
        // holds them all.
        public static void FlushWear()
        {
            if (terrain == null || pending.Count == 0) return;
            var data = terrain.terrainData;
            int res = data.alphamapResolution;
            float cell = Size / res;
            Vector2 lo = pending[0].lo, hi = pending[0].hi;
            foreach (var strip in pending)
            {
                lo = Vector2.Min(lo, strip.lo);
                hi = Vector2.Max(hi, strip.hi);
            }
            int x0 = Mathf.Clamp(Mathf.FloorToInt((lo.x + Size / 2f) / cell), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((hi.x + Size / 2f) / cell), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((lo.y + Size / 2f) / cell), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((hi.y + Size / 2f) / cell), 0, res - 1);
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            var maps = data.GetAlphamaps(x0, z0, w, h);
            int layers = maps.GetLength(2);
            foreach (var strip in pending)
            {
                int i0 = Mathf.Max(0, Mathf.FloorToInt((strip.lo.x + Size / 2f) / cell) - x0), i1 = Mathf.Min(w - 1, Mathf.CeilToInt((strip.hi.x + Size / 2f) / cell) - x0);
                int j0 = Mathf.Max(0, Mathf.FloorToInt((strip.lo.y + Size / 2f) / cell) - z0), j1 = Mathf.Min(h - 1, Mathf.CeilToInt((strip.hi.y + Size / 2f) / cell) - z0);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        var p = new Vector2(-Size / 2f + (x0 + i + 0.5f) * cell, -Size / 2f + (z0 + j + 0.5f) * cell);
                        float wear = WearAt(p, strip.a, strip.b, strip.width) * strip.strength;
                        if (wear <= 0f) continue;
                        for (int k = 0; k < layers; k++) maps[j, i, k] *= 1f - wear;
                        maps[j, i, strip.layer] += wear;
                    }
            }
            data.SetAlphamaps(x0, z0, maps);
            pending.Clear();
        }

        struct WornStrip
        {
            public Vector2 a, b, lo, hi;
            public float width, strength;
            public int layer;
        }
        static readonly List<WornStrip> worn = new List<WornStrip>();    // bare earth, which grass keeps off
        static readonly List<WornStrip> pending = new List<WornStrip>(); // painted at the next FlushWear
        // Which worn strips reach each 16 m square, so Worn looks at a handful instead of every path on the map.
        const float WornCell = 16f;
        static List<int>[] wornCells;
        static int WornCellCount => Mathf.CeilToInt(Size / WornCell);

        static WornStrip Strip(Vector2 from, Vector2 to, float width, float strength, int layer)
        {
            float reach = width * 0.5f + 1.2f;
            return new WornStrip { a = from, b = to, width = width, strength = strength, layer = layer,
                lo = Vector2.Min(from, to) - Vector2.one * reach, hi = Vector2.Max(from, to) + Vector2.one * reach };
        }

        static void IndexWorn(WornStrip strip, int index)
        {
            int n = WornCellCount;
            wornCells ??= new List<int>[n * n];
            int x0 = Mathf.Clamp(Mathf.FloorToInt((strip.lo.x + Size / 2f) / WornCell), 0, n - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((strip.hi.x + Size / 2f) / WornCell), 0, n - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((strip.lo.y + Size / 2f) / WornCell), 0, n - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((strip.hi.y + Size / 2f) / WornCell), 0, n - 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    (wornCells[z * n + x] ??= new List<int>()).Add(index);
        }

        // Forgets the last sector's worn strips (Build, before anything wears the new one).
        static void ClearWorn()
        {
            worn.Clear();
            pending.Clear();
            wornCells = null;
        }

        // Whether Wear has worn the ground at p bare enough that no grass grows there.
        public static bool Worn(Vector2 p)
        {
            if (wornCells == null) return false;
            int n = WornCellCount;
            int x = Mathf.FloorToInt((p.x + Size / 2f) / WornCell), z = Mathf.FloorToInt((p.y + Size / 2f) / WornCell);
            if (x < 0 || z < 0 || x >= n || z >= n) return false;
            var near = wornCells[z * n + x];
            if (near == null) return false;
            foreach (int i in near)
            {
                var s = worn[i];
                if (p.x >= s.lo.x && p.x <= s.hi.x && p.y >= s.lo.y && p.y <= s.hi.y && WearAt(p, s.a, s.b, s.width) * s.strength > 0.35f) return true;
            }
            return false;
        }

        static float WearAt(Vector2 p, Vector2 a, Vector2 b, float width)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            float d = Vector2.Distance(p, a + ab * t);
            float edge = width * 0.5f * (0.8f + 0.4f * Mathf.PerlinNoise(p.x * 0.4f + 3f, p.y * 0.4f + 7f));
            return 1f - Mathf.SmoothStep(0f, 1f, (d - edge + 0.4f) / 0.8f);
        }

        // How wide the low shelf at the river's edge is here: none along much of the river, up to 2.5 m in reaches
        // (where it would build up on an inside bend).
        static float BankShelf(float x, float z) =>
            2.5f * Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.04f + 13f, z * 0.04f + 77f) - 0.45f) * 4f);

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

        // Layer order: 0 pasture, 1 damp grass, 2 dirt, 3 rock, 4 rough meadow.
        // The tints take each photo's average colour to the ground cover of aerial photos of farmland in summer
        // (ChatGPT's environment review): pasture (96, 118, 72), damp grass (74, 100, 70), rough uncut meadow
        // (122, 124, 84) and bare soil (115, 99, 76). The terrain shader multiplies in linear space, so each tint is
        // (wanted / photo average) to the power 2.2. More blue than before: the old tints made every meadow olive.
        static TerrainLayer[] BuildLayers()
        {
            return new[]
            {
                Layer("meadow", "meadow", 7f, new Color(0.40f, 0.74f, 0.57f), new Color(0.26f, 0.36f, 0.15f), new Color(0.19f, 0.28f, 0.10f)),
                Layer("lush", "lush", 6f, new Color(0.21f, 0.55f, 0.59f), new Color(0.30f, 0.38f, 0.16f), new Color(0.22f, 0.30f, 0.12f)),
                Layer("dirt", "dirt", 5f, new Color(1.2f, 1.23f, 1.28f), new Color(0.36f, 0.30f, 0.22f), new Color(0.27f, 0.22f, 0.16f)),
                Layer("rock", "rock", 12f, Color.white, new Color(0.42f, 0.41f, 0.39f), new Color(0.30f, 0.29f, 0.28f)),
                Layer("dry", "meadow", 8f, new Color(0.68f, 0.83f, 0.80f), new Color(0.40f, 0.40f, 0.22f), new Color(0.31f, 0.31f, 0.17f)),
            };
        }

        // Uses Resources/Textures/Terrain/<file>_diff (and _nor) when present, else a generated noise texture.
        static TerrainLayer Layer(string name, string file, float tile, Color tint, Color light, Color dark)
        {
            var diffuse = Resources.Load<Texture2D>("Textures/Terrain/" + file + "_diff");
            var normal = Resources.Load<Texture2D>("Textures/Terrain/" + file + "_nor");
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
            var splat = new float[SplatRes, SplatRes, 5];
            var junctions = MapLayout.Junctions;
            var tracks = MapLayout.TrackField;
            System.Threading.Tasks.Parallel.For(0, SplatRes, zi =>
            {
                for (int xi = 0; xi < SplatRes; xi++)
                {
                    float u = (xi + 0.5f) / SplatRes, v = (zi + 0.5f) / SplatRes;
                    float x = -Size / 2f + u * Size, z = -Size / 2f + v * Size;
                    float steep = Sample(steepness, new Vector2(x, z));
                    FlatMask(x, z, out bool bare);

                    float rock = Mathf.SmoothStep(0f, 1f, (steep - 22f) / 12f);
                    float dirt = bare ? 1f : 0f;
                    // Dirt roads: two wheel tracks with a strip of grass down the middle, wider and narrower along the
                    // way (3 to 4 m), with ragged edges where wheels have strayed onto the verge, and a wider patch of
                    // bare ground where roads meet.
                    float dRoad = roads.Distance(x, z);
                    float laneHw = roadHw * (0.75f + 0.3f * Mathf.PerlinNoise(x * 0.025f + 5f, z * 0.025f + 90f))
                                 + 0.5f * (Mathf.PerlinNoise(x * 0.35f + 17f, z * 0.35f + 4f) - 0.5f);
                    if (dRoad < laneHw + 2.5f)
                    {
                        // The ruts come and go (deep and bare in some stretches, grassed over in others), and the
                        // shoulder is churned to mud in patches every couple of dozen metres.
                        float track = 1f - Mathf.SmoothStep(0f, 1f, (dRoad - laneHw + 0.4f) / 1.2f);
                        track *= 0.72f + 0.28f * Mathf.PerlinNoise(x * 0.1f + 70f, z * 0.1f + 12f);
                        if (dRoad < 0.5f) track *= 0.35f;
                        float shoulder = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(dRoad - laneHw - 0.6f) / 0.9f);
                        float mud = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.07f + 33f, z * 0.07f + 81f) - 0.6f) * 6f);
                        dirt = Mathf.Max(dirt, Mathf.Max(track, 0.8f * shoulder * mud));
                    }
                    foreach (var j in junctions)
                    {
                        if (dRoad > 11f) break; // junctions are on the roads
                        float dj = Vector2.Distance(new Vector2(x, z), j);
                        if (dj > 11f) continue;
                        float apron = 6.5f + 3f * (Mathf.PerlinNoise(x * 0.2f + 8f, z * 0.2f + 61f) - 0.5f);
                        dirt = Mathf.Max(dirt, 0.85f * (1f - Mathf.SmoothStep(0f, 1f, (dj - apron + 1.5f) / 2.5f)));
                    }
                    // Wheel tracks out to the emplacements: two ruts 1.6 m apart, worn bare in stretches.
                    if (tracks.points.Count > 0)
                    {
                        float dTrack = tracks.Distance(x, z);
                        if (dTrack < 1.6f)
                        {
                            float rut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(dTrack - 0.8f) / 0.45f);
                            dirt = Mathf.Max(dirt, 0.75f * rut * Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.15f + 21f, z * 0.15f + 9f) - 0.25f) * 3f));
                        }
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
                    // River banks: bare earth where the bank is cut back (about a third of it, in reaches), gravel and
                    // mud on the shelves, and elsewhere grass growing down to the water.
                    float dRiver = river.Distance(x, z);
                    if (dRiver < hw + 4f)
                    {
                        float wetEdge = MapLayout.RiverWidthAt(x, z);
                        float onShelf = dRiver < wetEdge + BankShelf(x, z) + 0.3f ? 1f : 0f;
                        float cut = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.05f + 3f, z * 0.05f + 44f) - 0.52f) * 5f);
                        float nearWater = 1f - Mathf.SmoothStep(0f, 1f, (dRiver - wetEdge - 0.5f) / 2f);
                        dirt = Mathf.Max(dirt, 0.9f * nearWater * Mathf.Max(cut, onShelf));
                        if (onShelf > 0f) rock = Mathf.Max(rock, 0.35f * Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.3f + 9f, z * 0.3f) - 0.4f) * 3f));
                        // A steep bank of soil is soil, not cliff.
                        rock *= 0.4f + 0.6f * Mathf.SmoothStep(0f, 1f, (dRiver - hw - 2f) / 3f);
                    }
                    // Pale stones at the water's edge, in a few patches.
                    float edgeStones = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(dRiver - hw - 0.8f) / 1.6f);
                    if (edgeStones > 0f)
                        rock = Mathf.Max(rock, edgeStones * Mathf.SmoothStep(0f, 1f, Mathf.PerlinNoise(x * 0.12f + 11f, z * 0.12f + 5f) * 1.8f - 0.8f));
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
                    // Ground cover with a reason, as aerial photos show it. How wet the ground lies decides it: damp
                    // grass along the river and the lake and in hollows, pale rough grass on higher, drier ground,
                    // pasture between. Broad drifts (80 m and 25 m across) and a fine one (12 m) move it a little either
                    // way, so a meadow varies by about 10 RGB levels instead of breaking into dark blotches.
                    var here = new Vector2(x, z);
                    float damp = Mathf.Max(1f - Mathf.SmoothStep(0f, 1f, (dRiver - hw) / 16f), 1f - Mathf.SmoothStep(0f, 1f, lake / 14f));
                    float hollow = Mathf.SmoothStep(0f, 1f, (Sample(lieHeights, here) - Sample(worldHeights, here) - 0.3f) / 1.6f);
                    float high = Mathf.SmoothStep(0f, 1f, (Sample(worldHeights, here) - 1.5f * Scale.Distance) / (8f * Scale.Distance));
                    float drift = Mathf.PerlinNoise(x * 0.0125f + 70f, z * 0.0125f + 20f) * 0.65f + Mathf.PerlinNoise(x * 0.04f + 9f, z * 0.04f + 4f) * 0.35f;
                    float wetness = 0.5f + 0.6f * hollow - 0.45f * high - 0.7f * (drift - 0.5f)
                                  + 0.16f * (Mathf.PerlinNoise(x * 0.08f + 31f, z * 0.08f + 17f) - 0.5f);
                    wetness = Mathf.Max(Mathf.Clamp01(wetness), damp);
                    float wet = 0.8f * Mathf.SmoothStep(0f, 1f, (wetness - 0.55f) / 0.4f);
                    float rough = 0.55f * Mathf.SmoothStep(0f, 1f, (0.45f - wetness) / 0.4f);
                    // An uncut margin round each field, where the plough doesn't reach: a paler line that separates
                    // neighbouring fields from the air.
                    if (MapLayout.InField(here, 2.2f) && !MapLayout.InField(here, 0f)) rough = Mathf.Max(rough, 0.85f);
                    // And along the roads, in stretches: long uncut grass a couple of metres wide each side.
                    float verge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(dRoad - laneHw - 1.6f) / 1.4f);
                    if (verge > 0f) rough = Mathf.Max(rough, 0.75f * verge * Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(x * 0.05f + 40f, z * 0.05f + 2f) - 0.35f) * 4f));

                    // Each layer covers the ones before it.
                    float w3 = rock, w2 = dirt * (1f - rock), w1 = wet * (1f - rock) * (1f - dirt);
                    float w4 = rough * (1f - rock) * (1f - dirt) * (1f - wet);
                    float w0 = (1f - rock) * (1f - dirt) * (1f - wet) * (1f - rough);
                    float sum = w0 + w1 + w2 + w3 + w4;
                    splat[zi, xi, 0] = w0 / sum;
                    splat[zi, xi, 1] = w1 / sum;
                    splat[zi, xi, 2] = w2 / sum;
                    splat[zi, xi, 3] = w3 / sum;
                    splat[zi, xi, 4] = w4 / sum;
                }
            });
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

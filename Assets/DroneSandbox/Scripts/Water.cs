using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // The water, as HDRP water surfaces drawn over meshes built here (HDRP does the waves, reflections,
    // refraction, caustics and foam):
    // - The river: a ribbon along the river line at the water level Ground worked out, a little wider than the
    //   channel so the shoreline is wherever the banks rise out of it, plus the oval plunge pool below the dam.
    //   A current map makes it flow down its own course; foam is masked in around the spillway's landing and on
    //   riffles, and drifts away downstream.
    // - The lake behind the dam: a grid kept wherever the lake is, with small wind waves.
    // - Natural lakes and marsh pools: one surface over all of them, each at its own level.
    // A sector without a river, a dam or pools simply has none of that surface.
    public static class Water
    {
        const float FoamReach = 50f; // metres from the dam that the river carries foam

        static readonly List<Object> made = new List<Object>();

        // Replaces the previous sector's generated water meshes and masks; HDRP releases its own simulations.
        public static void Build(Transform parent)
        {
            foreach (var resource in made) Art.Discard(resource);
            made.Clear();
            var root = new GameObject("Water").transform;
            root.SetParent(parent, false);
            if (MapLayout.HasRiver && MapLayout.RiverField.points.Count > 1) BuildRiver(root);
            if (MapLayout.Plan.world != null)
            {
                foreach (var lake in MapLayout.Plan.world.lakes) BuildWorldLake(root, lake);
                BuildWorldStructures(root);
                return;
            }
            if (MapLayout.HasDam) BuildLake(root);
            if (MapLayout.Plan.ponds.Count > 0) BuildPonds(root);
        }

        static void BuildRiver(Transform root)
        {
            bool dam = MapLayout.HasDam;
            Vector2 start = dam ? MapLayout.DamPoint : MapLayout.RiverField.points[0];
            float riverLevel = MapLayout.RiverField.values[0];
            var river = MapLayout.Plan.world != null
                ? Surface("River", root, WaterSurfaceType.River, new Vector3(start.x, riverLevel, start.y), WorldRiverMeshes())
                : dam
                ? Surface("River", root, WaterSurfaceType.River, new Vector3(start.x, riverLevel, start.y), RiverMesh(), PoolMesh())
                : Surface("River", root, WaterSurfaceType.River, new Vector3(start.x, riverLevel, start.y), RiverMesh());
            // Clear, slightly teal water you can see the bed through at the edges, and ripples catching the sky:
            // from the game camera, looking almost straight down, the darker green it had read as a grassy ditch.
            river.refractionColor = new Color(0.16f, 0.40f, 0.40f);
            river.scatteringColor = new Color(0.05f, 0.19f, 0.19f);
            river.absorptionDistance = 3.5f;
            // A river doesn't swell like the sea: no long rolling waves, just a low chop and ripples, all running
            // down the current (little chaos, so they line up with it) rather than heaving every which way. The
            // foam below the dam comes from its mask, not from big waves.
            river.largeWindSpeed = dam ? 10f : 6f;
            river.largeChaos = 0.2f;
            river.largeBand0Multiplier = 0.1f; // the long swell band, all but off
            river.largeBand1Multiplier = 0.45f;
            river.ripplesWindSpeed = 10f;
            river.ripplesChaos = 0.3f;

            // The current runs down the river's own course (a map of its direction, bend by bend), carrying the
            // ripples and foam along with it.
            FlowMaps(start, out var current, out var foamMask, out Vector2 extent, out Vector2 offset);
            river.largeCurrentSpeedValue = 7f; // km/h: a brisk lowland river
            river.ripplesMotionMode = WaterPropertyOverrideMode.Inherit;
            river.largeCurrentMap = current;
            river.largeCurrentRegionExtent = extent;
            river.largeCurrentRegionOffset = offset;
            river.largeCurrentMapInfluence = 1f;

            // Foam: churned up where the spillway lands, and in streaks over riffles down the river. HDRP only
            // makes foam in strong wind by default, so the river makes it at any wind, where the mask lets it.
            river.foam = true;
            river.simulationFoamAmount = dam ? 0.8f : 0.6f;
            river.foamPersistenceMultiplier = 1f;
            river.simulationFoamWindCurve = AnimationCurve.Constant(0f, 1f, 1f);
            river.supportSimulationFoamMask = true;
            river.simulationFoamMask = foamMask;
            river.simulationFoamMaskExtent = extent;
            river.simulationFoamMaskOffset = offset;
        }

        // Two maps over the river, in the water surface's own space (centred on `origin`; HDRP places both by extent
        // and offset): the current, red and green the direction downstream (a neutral (1, 0.5) is +X) and blue full
        // strength; and the foam mask, white below the dam and in riffles along the way.
        static void FlowMaps(Vector2 origin, out Texture2D current, out Texture2D foam, out Vector2 extent, out Vector2 offset)
        {
            var field = MapLayout.RiverField;
            var line = field.points;
            float limit = Ground.Size / 2f;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            foreach (var p in line)
            {
                var q = new Vector2(Mathf.Clamp(p.x, -limit, limit), Mathf.Clamp(p.y, -limit, limit));
                min = Vector2.Min(min, q);
                max = Vector2.Max(max, q);
            }
            float margin = MapLayout.RiverHalfWidth + 20f; // the plunge pool and the river's edges
            min -= Vector2.one * margin;
            max += Vector2.one * margin;
            extent = max - min;
            offset = (min + max) / 2f - origin;

            // Downstream is the way the water level falls.
            float downstream = MapLayout.Plan.world != null || field.values[field.values.Length - 1] <= field.values[0] ? 1f : -1f;
            bool dam = MapLayout.HasDam;
            // About 2 m per texel on each axis, capped at 5 MiB for both maps together. Cache bend directions
            // once per segment rather than normalizing seven vectors for every texel of the larger river box.
            int width = Mathf.Clamp(Mathf.CeilToInt(extent.x / 2f), 64, 1024);
            int height = Mathf.Clamp(Mathf.CeilToInt(extent.y / 2f), 64, 1024);
            var directions = new Vector2[line.Count];
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2 dir = Vector2.zero;
                if (MapLayout.Plan.world != null)
                {
                    // Never average the tangent across a break between unrelated reaches.
                    if (field.IsValidSegment(i)) dir = field.Direction(i);
                    for (int k = i - 1; k >= Mathf.Max(0, i - 3) && field.IsValidSegment(k); k--) dir += field.Direction(k);
                    for (int k = i + 1; k <= Mathf.Min(line.Count - 2, i + 3) && field.IsValidSegment(k - 1) && field.IsValidSegment(k); k++) dir += field.Direction(k);
                }
                else
                    for (int k = -3; k <= 3; k++)
                        if (field.IsValidSegment(i + k)) dir += field.Direction(i + k);
                directions[i] = dir.sqrMagnitude > 0f ? dir.normalized * downstream : Vector2.right;
            }
            current = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { name = "River Current", wrapMode = TextureWrapMode.Clamp };
            foam = new Texture2D(width, height, TextureFormat.R8, false, true) { name = "River Foam", wrapMode = TextureWrapMode.Clamp };
            var flow = new Color32[width * height];
            var froth = new byte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float wx = min.x + (x + 0.5f) * extent.x / width, wz = min.y + (y + 0.5f) * extent.y / height;
                    float d = field.Query(wx, wz, out _, out int segment);
                    Vector2 dir = segment >= 0 ? directions[segment] : Vector2.right;
                    flow[y * width + x] = new Color(dir.x * 0.5f + 0.5f, dir.y * 0.5f + 0.5f, 1f, 1f);

                    float churn = 0f;
                    if (dam)
                    {
                        float r = Vector2.Distance(new Vector2(wx, wz), MapLayout.DamPoint) / FoamReach;
                        churn = 1f - Mathf.SmoothStep(0f, 1f, (r - 0.15f) / 0.85f);
                    }
                    float riffle = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(wx * 0.018f + 11f, wz * 0.018f + 4f) - 0.56f) / 0.14f) * 0.55f;
                    float v = d < MapLayout.RiverHalfWidth + 4f ? Mathf.Max(churn, riffle) : churn;
                    froth[y * width + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
                }
            current.SetPixels32(flow);
            current.Apply(false, true);
            foam.SetPixelData(froth, 0);
            foam.Apply(false, true);
            made.Add(current);
            made.Add(foam);
        }

        // The reservoir's waves and reflections span its enlarged shore while the dam itself stays real size.
        static void BuildLake(Transform root)
        {
            Vector2 dam = MapLayout.DamPoint;
            var lake = Surface("Lake", root, WaterSurfaceType.OceanSeaLake, new Vector3(dam.x, MapLayout.LakeLevel, dam.y), LakeMesh());
            lake.repetitionSize = 250f;
            lake.largeWindSpeed = 7f;   // km/h: a light breeze, small waves
            lake.largeChaos = 0.6f;
            lake.ripplesWindSpeed = 7f;  // about 2 m/s: fine ripples catching the light
            // Glossy all the way to the far shore, so it mirrors the hills and trees there.
            lake.startSmoothness = 0.97f;
            lake.endSmoothness = 0.93f;
            lake.smoothnessFadeStart = 150f * Scale.Camera;
            lake.smoothnessFadeDistance = 800f * Scale.Camera;
            // Patches of calm and of ruffled water, as gusts cross a real lake, and calm in the sheltered shallows.
            lake.waterMask = CalmMask(out Vector2 extent);
            lake.waterMaskExtent = extent;
            lake.waterMaskOffset = LakeMiddle - dam;
            lake.refractionColor = new Color(0.02f, 0.38f, 0.48f);
            lake.scatteringColor = new Color(0f, 0.22f, 0.24f);
            lake.absorptionDistance = 6f;
        }

        // Natural lakes and pools, as one calm surface: clear blue-green lakes, or peaty brown-green marsh water
        // when they're all small.
        static void BuildPonds(Transform root)
        {
            var ponds = MapLayout.Plan.ponds;
            bool lakes = false;
            foreach (var pond in ponds) if (pond.radius > 30f) lakes = true;
            if (!lakes)
            {
                // Small pools: a still, glossy surface of murky olive water (HDRP's water system drew them nearly
                // black from above, whatever their colour), reflecting the sky the way farm ponds do in aerial photos.
                var pools = new GameObject("Pools");
                pools.transform.SetParent(root, false);
                pools.AddComponent<MeshFilter>().sharedMesh = PondMesh();
                var renderer = pools.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var still = Art.NewMaterial(new Color(0.3f, 0.38f, 0.32f));
                still.SetFloat("_Smoothness", 0.92f);
                renderer.sharedMaterial = still;
                made.Add(still);
                return;
            }
            var first = ponds[0];
            var surface = Surface(lakes ? "Lakes" : "Pools", root, WaterSurfaceType.OceanSeaLake,
                new Vector3(first.center.x, first.level, first.center.y), PondMesh());
            surface.repetitionSize = 200f;
            surface.largeWindSpeed = lakes ? 7f : 3f;
            surface.largeChaos = 0.6f;
            surface.ripplesWindSpeed = lakes ? 7f : 4f;
            surface.startSmoothness = 0.97f;
            surface.endSmoothness = 0.93f;
            surface.smoothnessFadeStart = 150f * Scale.Camera;
            surface.smoothnessFadeDistance = 800f * Scale.Camera;
            // Brighter in colour than the reservoir's: seen from above, a basin of clear water otherwise reads black.
            // Pools are murky, olive-brown with silt and weed, as farm and marsh ponds look in aerial photographs.
            surface.refractionColor = lakes ? new Color(0.06f, 0.46f, 0.52f) : new Color(0.37f, 0.46f, 0.4f); // shallows (94, 118, 101)
            surface.scatteringColor = lakes ? new Color(0.05f, 0.3f, 0.32f) : new Color(0.23f, 0.33f, 0.31f); // deep (58, 83, 79)
            surface.absorptionDistance = lakes ? 9f : 1f;
        }

        // A bounded 2-4 m grid over every pool, at that pool's own level, kept wherever it's near the water.
        static Mesh PondMesh()
        {
            float cell = Mathf.Max(2f, Ground.Size / 512f);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float limit = Ground.Size / 2f - 1f;
            var ponds = MapLayout.Plan.ponds;
            for (int k = 0; k < ponds.Count; k++)
            {
                var pond = ponds[k];
                float reach = pond.radius * 2f + 4f; // stretched ponds reach further along their axis
                int n = Mathf.CeilToInt(reach * 2f / cell);
                var index = new Dictionary<int, int>();
                int Vertex(int i, int j)
                {
                    int key = j * (n + 1) + i;
                    if (index.TryGetValue(key, out int at)) return at;
                    float x = pond.center.x - reach + i * cell, z = pond.center.y - reach + j * cell;
                    index[key] = vertices.Count;
                    vertices.Add(new Vector3(x, pond.level, z));
                    uvs.Add(new Vector2(x, z));
                    return vertices.Count - 1;
                }
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        float x = pond.center.x - reach + i * cell, z = pond.center.y - reach + j * cell;
                        if (x < -limit || x + cell > limit || z < -limit || z + cell > limit) continue;
                        bool wet = false;
                        for (int c = 0; c < 4 && !wet; c++)
                        {
                            float s = MapLayout.PondDistance(x + (c & 1) * cell, z + (c >> 1) * cell, out int which);
                            wet = which == k && s < 1.5f;
                        }
                        if (!wet) continue;
                        int a = Vertex(i, j), b = Vertex(i + 1, j), d = Vertex(i, j + 1), e = Vertex(i + 1, j + 1);
                        triangles.AddRange(new[] { a, d, b, b, d, e });
                    }
                }
            }
            return Finish("Pools", vertices, uvs, triangles);
        }

        // A water surface over the given meshes. The meshes only give it its shape, so their own renderers stay
        // switched off.
        static WaterSurface Surface(string name, Transform parent, WaterSurfaceType type, Vector3 position, params Mesh[] meshes)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var surface = go.AddComponent<WaterSurface>();
            surface.surfaceType = type;
            surface.geometryType = WaterGeometryType.Custom;
            foreach (var mesh in meshes)
            {
                var part = new GameObject(mesh.name);
                part.transform.SetParent(parent, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.enabled = false;
                surface.meshRenderers.Add(renderer);
            }
            return surface;
        }

        // How much the wind stirs each part of the lake (red and green: its small waves, blue: its ripples): patches
        // where a gust ruffles it among calm, glassy ones, and calm in the lee of the shore. Covers the lake mesh
        // (LakeMesh), centred on the same point; extent gives its size in metres.
        static Texture2D CalmMask(out Vector2 extent)
        {
            const int size = 256;
            Rect bounds = LakeBounds();
            extent = bounds.size;
            Vector2 middle = bounds.center;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float wx = middle.x - extent.x / 2f + (x + 0.5f) * extent.x / size, wz = middle.y - extent.y / 2f + (y + 0.5f) * extent.y / size;
                    float gust = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(wx * 0.012f + 5f, wz * 0.012f + 9f) * 0.7f
                                                         + Mathf.PerlinNoise(wx * 0.045f + 1f, wz * 0.045f + 3f) * 0.3f - 0.3f) / 0.4f);
                    float lee = Mathf.SmoothStep(0f, 1f, -MapLayout.ReservoirDistance(wx, wz) / 25f);
                    float waves = Mathf.Lerp(0.5f, 1f, gust) * Mathf.Lerp(0.6f, 1f, lee);
                    float ripples = Mathf.Lerp(0.35f, 1f, gust) * Mathf.Lerp(0.4f, 1f, lee);
                    pixels[y * size + x] = new Color(waves, waves, ripples, 1f);
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            made.Add(texture);
            return texture;
        }

        // Bounds of the actual lobes, including their shore wobble and smooth unions. Keeping this in one place
        // lets water, shore plants and woods follow either the old or the enlarged layout, even when rotated.
        public static Rect LakeBounds(float margin = 0f)
        {
            Vector2 min = MapLayout.DamPoint, max = min;
            foreach (var lobe in MapLayout.Plan.lakeLobes)
            {
                float angle = lobe.angle * Mathf.Deg2Rad;
                Vector2 along = MapLayout.DamDown * Mathf.Cos(angle) + MapLayout.DamAcross * Mathf.Sin(angle);
                Vector2 across = new Vector2(-along.y, along.x);
                Vector2 c = MapLayout.DamPoint + MapLayout.DamDown * lobe.center.x + MapLayout.DamAcross * lobe.center.y;
                Vector2 reach = new Vector2(
                    Mathf.Sqrt(along.x * along.x * lobe.halfLength * lobe.halfLength + across.x * across.x * lobe.halfWidth * lobe.halfWidth),
                    Mathf.Sqrt(along.y * along.y * lobe.halfLength * lobe.halfLength + across.y * across.y * lobe.halfWidth * lobe.halfWidth));
                reach = reach * 1.2f + Vector2.one * Scale.D(14f);
                min = Vector2.Min(min, c - reach);
                max = Vector2.Max(max, c + reach);
            }
            float limit = Ground.Size / 2f - 1f;
            return Rect.MinMaxRect(Mathf.Clamp(min.x - margin, -limit, limit), Mathf.Clamp(min.y - margin, -limit, limit),
                Mathf.Clamp(max.x + margin, -limit, limit), Mathf.Clamp(max.y + margin, -limit, limit));
        }

        // The calm mask and mesh share their centre, whatever the dam's orientation or the shape of the lake.
        static Vector2 LakeMiddle => LakeBounds().center;

        static Mesh RiverMesh()
        {
            var field = MapLayout.RiverField;
            var line = field.points;
            float halfWidth = MapLayout.RiverHalfWidth + 1.8f;
            float limit = Ground.Size / 2f - 1f;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float travelled = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0) travelled += Vector2.Distance(line[i - 1], line[i]);
                Vector2 dir = (line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x) * halfWidth;
                Vector2 left = line[i] - side, right = line[i] + side;
                if (Mathf.Abs(line[i].x) > limit || Mathf.Abs(line[i].y) > limit) continue;
                float y = field.values[i];
                int start = vertices.Count;
                vertices.Add(new Vector3(left.x, y, left.y));
                vertices.Add(new Vector3(right.x, y, right.y));
                uvs.Add(new Vector2(-halfWidth, travelled));
                uvs.Add(new Vector2(halfWidth, travelled));
                if (start >= 2)
                {
                    // Wound so the surface faces up (Unity draws clockwise triangles as seen from above).
                    triangles.AddRange(new[] { start - 2, start - 1, start, start - 1, start + 1, start });
                }
            }
            return Finish("River", vertices, uvs, triangles);
        }

        // A grid over the lake, 2-4 m cells, kept wherever a corner is near water. HDRP adds the wave detail;
        // the terrain draws the shoreline. Grid density is bounded on the larger map. UVs are world metres.
        static Mesh LakeMesh(WorldLake lake = null)
        {
            float cell = Mathf.Max(2f, Ground.Size / 512f);
            Rect bounds = lake != null ? WorldLakeBounds(lake) : LakeBounds();
            int nx = Mathf.Max(1, Mathf.CeilToInt(bounds.width / cell)), nz = Mathf.Max(1, Mathf.CeilToInt(bounds.height / cell));
            float dx = bounds.width / nx, dz = bounds.height / nz;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var index = new Dictionary<int, int>();
            float y = lake != null ? lake.level : MapLayout.LakeLevel;
            // Each shoreline query is reused by up to four cells; large reservoirs stay bounded by the grid.
            var wetCorners = lake != null ? new bool[(nx + 1) * (nz + 1)] : null;
            if (lake != null)
                for (int j = 0; j <= nz; j++)
                    for (int i = 0; i <= nx; i++)
                        wetCorners[j * (nx + 1) + i] = WorldSectorData.ShoreDistance(new Vector2(bounds.xMin + i * dx, bounds.yMin + j * dz), lake.shore) < 1.5f;

            int Vertex(int i, int j)
            {
                int key = j * (nx + 1) + i;
                if (index.TryGetValue(key, out int at)) return at;
                float x = bounds.xMin + i * dx, z = bounds.yMin + j * dz;
                index[key] = vertices.Count;
                vertices.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(x, z));
                return vertices.Count - 1;
            }

            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    float x = bounds.xMin + i * dx, z = bounds.yMin + j * dz;
                    bool wet = false;
                    for (int c = 0; c < 4 && !wet; c++)
                        wet = lake != null ? wetCorners[(j + (c >> 1)) * (nx + 1) + i + (c & 1)]
                            : MapLayout.ReservoirDistance(x + (c & 1) * dx, z + (c >> 1) * dz) < 1.5f;
                    if (!wet) continue;
                    int a = Vertex(i, j), b = Vertex(i + 1, j), d = Vertex(i, j + 1), e = Vertex(i + 1, j + 1);
                    triangles.AddRange(new[] { a, d, b, b, d, e }); // clockwise from above, so it faces up
                }
            }
            return Finish("Lake", vertices, uvs, triangles);
        }

        // Each world lake owns its absolute level and complete polygon, even when its shore leaves the square.
        static void BuildWorldLake(Transform root, WorldLake lake)
        {
            var bounds = WorldLakeBounds(lake);
            if (bounds.width <= 0f || bounds.height <= 0f) return;
            var surface = Surface(lake.name, root, WaterSurfaceType.OceanSeaLake,
                new Vector3(bounds.center.x, lake.level, bounds.center.y), LakeMesh(lake));
            surface.repetitionSize = 250f;
            surface.largeWindSpeed = 7f;
            surface.largeChaos = 0.6f;
            surface.ripplesWindSpeed = 7f;
            surface.startSmoothness = 0.97f;
            surface.endSmoothness = 0.93f;
            surface.smoothnessFadeStart = 150f * Scale.Camera;
            surface.smoothnessFadeDistance = 800f * Scale.Camera;
            surface.refractionColor = new Color(0.06f, 0.46f, 0.52f);
            surface.scatteringColor = new Color(0.05f, 0.3f, 0.32f);
            surface.absorptionDistance = 9f;
        }

        // Bound the grid to the terrain, without inventing a shoreline or leaving a one-metre seam at its edge.
        static Rect WorldLakeBounds(WorldLake lake)
        {
            Vector2 min = lake.shore[0], max = min;
            foreach (var p in lake.shore) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            float limit = Ground.Size / 2f;
            return Rect.MinMaxRect(Mathf.Clamp(min.x - 2f, -limit, limit), Mathf.Clamp(min.y - 2f, -limit, limit),
                Mathf.Clamp(max.x + 2f, -limit, limit), Mathf.Clamp(max.y + 2f, -limit, limit));
        }

        // A separate mesh per reach keeps tangents, UV distance and triangles from crossing a line break.
        static Mesh[] WorldRiverMeshes()
        {
            var reaches = MapLayout.Plan.world.rivers;
            var meshes = new Mesh[reaches.Count];
            for (int i = 0; i < meshes.Length; i++) meshes[i] = WorldRiverMesh(reaches[i]);
            return meshes;
        }

        // Interpolate the reach's own widths and levels; a nearby tributary must not supply its vertex values.
        static Mesh WorldRiverMesh(SectorRiver reach)
        {
            var line = new List<Vector2>();
            var widths = new List<float>();
            var levels = new List<float>();
            for (int i = 1; i < reach.points.Length; i++)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(reach.points[i - 1], reach.points[i]) / 2f));
                for (int j = 0; j < n; j++)
                {
                    float t = j / (float)n;
                    line.Add(Vector2.Lerp(reach.points[i - 1], reach.points[i], t));
                    widths.Add(Mathf.Lerp(reach.halfWidths[i - 1], reach.halfWidths[i], t));
                    levels.Add(Mathf.Lerp(reach.waterLevels[i - 1], reach.waterLevels[i], t));
                }
            }
            int last = reach.points.Length - 1;
            line.Add(reach.points[last]); widths.Add(reach.halfWidths[last]); levels.Add(reach.waterLevels[last]);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float travelled = 0f;
            Vector3 oldLeft = default, oldRight = default;
            float oldWidth = 0f, oldTravelled = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0) travelled += Vector2.Distance(line[i - 1], line[i]);
                Vector2 dir = (line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(0, i - 1)]).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x) * widths[i];
                var left = new Vector3(line[i].x - side.x, levels[i], line[i].y - side.y);
                var right = new Vector3(line[i].x + side.x, levels[i], line[i].y + side.y);
                if (i > 0)
                {
                    var a = (oldLeft, new Vector2(-oldWidth, oldTravelled));
                    var b = (oldRight, new Vector2(oldWidth, oldTravelled));
                    var c = (left, new Vector2(-widths[i], travelled));
                    var d = (right, new Vector2(widths[i], travelled));
                    AddWaterTriangle(a, b, c, vertices, uvs, triangles);
                    AddWaterTriangle(b, d, c, vertices, uvs, triangles);
                }
                oldLeft = left; oldRight = right; oldWidth = widths[i]; oldTravelled = travelled;
            }
            return Finish(reach.name, vertices, uvs, triangles);
        }

        // Clip only boundary triangles, interpolating elevation and UVs right up to the square's exact edge.
        static void AddWaterTriangle((Vector3 p, Vector2 uv) a, (Vector3 p, Vector2 uv) b, (Vector3 p, Vector2 uv) c,
            List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            float limit = Ground.Size / 2f;
            bool Inside(Vector3 p) => Mathf.Abs(p.x) <= limit && Mathf.Abs(p.z) <= limit;
            void Add((Vector3 p, Vector2 uv) v) { triangles.Add(vertices.Count); vertices.Add(v.p); uvs.Add(v.uv); }
            if (Inside(a.p) && Inside(b.p) && Inside(c.p)) { Add(a); Add(b); Add(c); return; }
            if (Mathf.Min(a.p.x, Mathf.Min(b.p.x, c.p.x)) > limit || Mathf.Max(a.p.x, Mathf.Max(b.p.x, c.p.x)) < -limit
                || Mathf.Min(a.p.z, Mathf.Min(b.p.z, c.p.z)) > limit || Mathf.Max(a.p.z, Mathf.Max(b.p.z, c.p.z)) < -limit) return;
            var polygon = new List<(Vector3 p, Vector2 uv)> { a, b, c };
            for (int edge = 0; edge < 4 && polygon.Count > 0; edge++)
            {
                float Distance(Vector3 p) => limit - (edge == 0 ? p.x : edge == 1 ? -p.x : edge == 2 ? p.z : -p.z);
                var clipped = new List<(Vector3 p, Vector2 uv)>();
                var previous = polygon[polygon.Count - 1];
                float before = Distance(previous.p);
                foreach (var next in polygon)
                {
                    float after = Distance(next.p);
                    if ((before >= 0f) != (after >= 0f))
                    {
                        float t = before / (before - after);
                        clipped.Add((Vector3.Lerp(previous.p, next.p, t), Vector2.Lerp(previous.uv, next.uv, t)));
                    }
                    if (after >= 0f) clipped.Add(next);
                    previous = next; before = after;
                }
                polygon = clipped;
            }
            for (int i = 2; i < polygon.Count; i++) { Add(polygon[0]); Add(polygon[i - 1]); Add(polygon[i]); }
        }

        // Existing models use +Z along a bridge/downstream at a dam; their authored deck datums are 0.3/15 m.
        static void BuildWorldStructures(Transform root)
        {
            var source = MapLayout.Plan.world;
            foreach (var bridge in source.bridges)
            {
                Vector2 center = (bridge.a + bridge.b) / 2f, along = (bridge.b - bridge.a).normalized;
                var model = new GameObject(bridge.railway ? "World Rail Bridge" : "World Road Bridge").transform;
                model.SetParent(root, false);
                model.position = new Vector3(center.x, bridge.deckLevel - 0.3f, center.y);
                model.rotation = Quaternion.LookRotation(new Vector3(along.x, 0f, along.y));
                var mesh = Art.TryModel("Scenery_Bridge", model);
                if (mesh != null) mesh.transform.localScale = new Vector3(1f, 1f, Vector2.Distance(bridge.a, bridge.b) / 18f);
            }
            foreach (var dam in source.dams)
            {
                Vector2 center = (dam.a + dam.b) / 2f, across = (dam.b - dam.a).normalized;
                Vector2 down = new Vector2(-across.y, across.x);
                // The dry side has the larger signed shoreline distance, regardless of endpoint ordering.
                if (source.LakeDistance(center + down * 10f, out _) < source.LakeDistance(center - down * 10f, out _)) down = -down;
                var model = new GameObject(dam.name).transform;
                model.SetParent(root, false);
                model.position = new Vector3(center.x, dam.tailwater, center.y);
                model.rotation = Quaternion.LookRotation(new Vector3(down.x, 0f, down.y));
                var mesh = Art.TryModel("Scenery_Dam", model);
                if (mesh != null) mesh.transform.localScale = new Vector3(Vector2.Distance(dam.a, dam.b) / 72f,
                    Mathf.Max(0.01f, (dam.crest - dam.tailwater) / 15f), 1f);
            }
        }

        // The plunge pool below the spillway: an oval of river water wider than the river. UVs run downstream.
        static Mesh PoolMesh()
        {
            const int rings = 6, segments = 48;
            const float along = 14f, across = 13f, middleAlong = 12f;
            float y = MapLayout.RiverField.values[0];
            Vector2 dam = MapLayout.DamPoint, down = MapLayout.DamDown, side = MapLayout.DamAcross;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float k = r / (float)rings;
                for (int s = 0; s < (r == 0 ? 1 : segments); s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    float u = middleAlong + Mathf.Cos(a) * along * k, v = Mathf.Sin(a) * across * k;
                    Vector2 p = dam + down * u + side * v;
                    vertices.Add(new Vector3(p.x, y, p.y));
                    uvs.Add(new Vector2(v, u));
                }
            }
            for (int s = 0; s < segments; s++)
                triangles.AddRange(new[] { 0, 1 + (s + 1) % segments, 1 + s });
            for (int r = 1; r < rings; r++)
            {
                int inner = 1 + (r - 1) * segments, outer = 1 + r * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    triangles.AddRange(new[] { inner + s, inner + s1, outer + s, inner + s1, outer + s1, outer + s });
                }
            }
            return Finish("Plunge Pool", vertices, uvs, triangles);
        }

        // Builds the mesh, flipping it if it came out facing down.
        static Mesh Finish(string name, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            if (vertices.Count > 0 && Vector3.Dot(mesh.normals[0], Vector3.up) < 0f)
            {
                for (int i = 0; i < triangles.Count; i += 3) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
            }
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            made.Add(mesh);
            return mesh;
        }
    }
}

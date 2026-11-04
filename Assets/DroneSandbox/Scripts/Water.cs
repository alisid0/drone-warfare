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

        public static void Build(Transform parent)
        {
            var root = new GameObject("Water").transform;
            root.SetParent(parent, false);
            if (MapLayout.HasRiver && MapLayout.RiverField.points.Count > 1) BuildRiver(root);
            if (MapLayout.HasDam) BuildLake(root);
            if (MapLayout.Plan.ponds.Count > 0) BuildPonds(root);
        }

        static void BuildRiver(Transform root)
        {
            bool dam = MapLayout.HasDam;
            Vector2 start = dam ? MapLayout.DamPoint : MapLayout.RiverField.points[0];
            float riverLevel = MapLayout.RiverField.values[0];
            var river = dam
                ? Surface("River", root, WaterSurfaceType.River, new Vector3(start.x, riverLevel, start.y), RiverMesh(), PoolMesh())
                : Surface("River", root, WaterSurfaceType.River, new Vector3(start.x, riverLevel, start.y), RiverMesh());
            // Clear, slightly teal water you can see the bed through at the edges, and ripples catching the sky:
            // from the game camera, looking almost straight down, the darker green it had read as a grassy ditch.
            river.refractionColor = new Color(0.16f, 0.40f, 0.40f);
            river.scatteringColor = new Color(0.05f, 0.19f, 0.19f);
            river.absorptionDistance = 3.5f;
            river.ripplesWindSpeed = 12f;
            river.largeWindSpeed = dam ? 35f : 18f; // choppy enough below the dam to churn up foam, where the mask lets it
            river.largeChaos = 0.9f;

            // The current runs down the river's own course (a map of its direction, bend by bend), carrying the
            // ripples and foam along with it.
            FlowMaps(start, out var current, out var foamMask, out Vector2 extent, out Vector2 offset);
            river.largeCurrentSpeedValue = 6f; // km/h: a brisk lowland river
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
            float downstream = field.values[field.values.Length - 1] <= field.values[0] ? 1f : -1f;
            bool dam = MapLayout.HasDam;
            const int size = 256;
            current = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { name = "River Current", wrapMode = TextureWrapMode.Clamp };
            foam = new Texture2D(size, size, TextureFormat.R8, false, true) { name = "River Foam", wrapMode = TextureWrapMode.Clamp };
            var flow = new Color[size * size];
            var froth = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float wx = min.x + (x + 0.5f) * extent.x / size, wz = min.y + (y + 0.5f) * extent.y / size;
                    float d = field.Query(wx, wz, out _, out int s);
                    // The direction along a few segments either side, so the current turns smoothly round bends.
                    Vector2 dir = Vector2.zero;
                    for (int k = -3; k <= 3; k++)
                        if (field.IsValidSegment(s + k)) dir += field.Direction(s + k);
                    dir = dir.sqrMagnitude > 0f ? dir.normalized * downstream : Vector2.right;
                    flow[y * size + x] = new Color(dir.x * 0.5f + 0.5f, dir.y * 0.5f + 0.5f, 1f, 1f);

                    float churn = 0f;
                    if (dam)
                    {
                        float r = Vector2.Distance(new Vector2(wx, wz), MapLayout.DamPoint) / FoamReach;
                        churn = 1f - Mathf.SmoothStep(0f, 1f, (r - 0.15f) / 0.85f);
                    }
                    float riffle = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(wx * 0.018f + 11f, wz * 0.018f + 4f) - 0.56f) / 0.14f) * 0.55f;
                    float v = d < MapLayout.RiverHalfWidth + 4f ? Mathf.Max(churn, riffle) : churn;
                    froth[y * size + x] = new Color(v, v, v, 1f);
                }
            current.SetPixels(flow);
            current.Apply();
            foam.SetPixels(froth);
            foam.Apply();
        }

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
            lake.smoothnessFadeStart = 150f;
            lake.smoothnessFadeDistance = 800f;
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
            surface.smoothnessFadeStart = 150f;
            surface.smoothnessFadeDistance = 800f;
            // Brighter in colour than the reservoir's: seen from above, a basin of clear water otherwise reads black.
            // Pools are murky, olive-brown with silt and weed, as farm and marsh ponds look in aerial photographs.
            surface.refractionColor = lakes ? new Color(0.06f, 0.46f, 0.52f) : new Color(0.37f, 0.46f, 0.4f); // shallows (94, 118, 101)
            surface.scatteringColor = lakes ? new Color(0.05f, 0.3f, 0.32f) : new Color(0.23f, 0.33f, 0.31f); // deep (58, 83, 79)
            surface.absorptionDistance = lakes ? 9f : 1f;
        }

        // A 2 m grid over every pool, at that pool's own level, kept wherever it's near the water.
        static Mesh PondMesh()
        {
            const float cell = 2f;
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
                        if (Mathf.Abs(x) > limit || Mathf.Abs(z) > limit) continue;
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
            const float span = 240f;
            extent = new Vector2(span, span);
            Vector2 middle = LakeMiddle;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float wx = middle.x - span / 2f + (x + 0.5f) * span / size, wz = middle.y - span / 2f + (y + 0.5f) * span / size;
                    float gust = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(wx * 0.012f + 5f, wz * 0.012f + 9f) * 0.7f
                                                         + Mathf.PerlinNoise(wx * 0.045f + 1f, wz * 0.045f + 3f) * 0.3f - 0.3f) / 0.4f);
                    float lee = Mathf.SmoothStep(0f, 1f, -MapLayout.ReservoirDistance(wx, wz) / 25f);
                    float waves = Mathf.Lerp(0.5f, 1f, gust) * Mathf.Lerp(0.6f, 1f, lee);
                    float ripples = Mathf.Lerp(0.35f, 1f, gust) * Mathf.Lerp(0.4f, 1f, lee);
                    pixels[y * size + x] = new Color(waves, waves, ripples, 1f);
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static Vector2 LakeMiddle => MapLayout.DamPoint - MapLayout.DamDown * 60f + MapLayout.DamAcross * 40f;

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

        // A grid over the lake, 2 m cells, kept wherever any corner is near the water. The shores rise above
        // it, so the terrain draws the shoreline. UVs are world metres.
        static Mesh LakeMesh()
        {
            const float cell = 2f, reach = 110f;
            Vector2 middle = LakeMiddle;
            int n = Mathf.CeilToInt(reach * 2f / cell);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var index = new Dictionary<int, int>();
            float y = MapLayout.LakeLevel;

            int Vertex(int i, int j)
            {
                int key = j * (n + 1) + i;
                if (index.TryGetValue(key, out int at)) return at;
                float x = middle.x - reach + i * cell, z = middle.y - reach + j * cell;
                index[key] = vertices.Count;
                vertices.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(x, z));
                return vertices.Count - 1;
            }

            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    float x = middle.x - reach + i * cell, z = middle.y - reach + j * cell;
                    bool wet = false;
                    for (int c = 0; c < 4 && !wet; c++)
                        wet = MapLayout.ReservoirDistance(x + (c & 1) * cell, z + (c >> 1) * cell) < 1.5f;
                    if (!wet) continue;
                    int a = Vertex(i, j), b = Vertex(i + 1, j), d = Vertex(i, j + 1), e = Vertex(i + 1, j + 1);
                    triangles.AddRange(new[] { a, d, b, b, d, e }); // clockwise from above, so it faces up
                }
            }
            return Finish("Lake", vertices, uvs, triangles);
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
            return mesh;
        }
    }
}

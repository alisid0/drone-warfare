using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The rest of the world around the detailed sector: all 56 km of WorldPlan as 4 km chunks of terrain mesh, each
    // with three levels of detail (62.5, 125 and 250 m between heights) that Unity picks by how big the chunk looks on
    // screen, so wherever the camera or a drone goes there's real ground out to the horizon. Painted with one picture
    // of the whole world (fields, woods, towns, water and roads, about 14 m a pixel), made once per world.
    //
    // The detailed sector sits at Origin in the world (world = local + Origin; local (0, 0) is the sector's centre).
    // Under the sector's terrain and the near ring round it (Backdrop) the chunks sink out of sight, so the seams are
    // theirs. Every chunk gets a skirt hanging 40 m down its edges, so neighbours on different levels of detail
    // never show a crack.
    public static class FarTerrain
    {
        const float ChunkSize = 4000f;
        const int ChunkCount = 14; // WorldPlan.Size / ChunkSize
        static readonly int[] LodQuads = { 64, 32, 16 };
        const int TextureSize = 4096;
        const float Skirt = 40f;

        public static bool Active => root != null && WorldPlan.Ready;
        public static Vector2 Origin { get; private set; }

        static Transform root;
        static readonly List<Mesh> meshes = new List<Mesh>();
        static readonly List<MeshCollider> colliders = new List<MeshCollider>();
        static Texture2D worldTexture;
        static Material material;
        static int textureSeed = -1;

        // The near ring's reach and blend, which the chunks sink under (Backdrop builds the ring itself).
        public const float NearReach = 1500f, NearBlend = 400f;
        static float[] edgeHeights; // the detailed terrain's height where each of Backdrop's directions leaves it
        public const int EdgeSegments = 512;

        // Where the next sector sits in the world, snapped to 250 m so its edges fall on the chunks' height lines at
        // every level of detail.
        public static void SetOrigin(Vector2 world)
        {
            Origin = new Vector2(Mathf.Round(world.x / 250f) * 250f, Mathf.Round(world.y / 250f) * 250f);
        }

        // Where a theatre hotspot's sector sits in the world: along the front from north (the theatre map's left)
        // to south, nearer the line or further behind it as it lies on the theatre map.
        public static Vector2 SpotFor(Vector2 mapPosition)
        {
            if (!WorldPlan.Ready) WorldPlan.Generate(World.WorldSeed);
            float z = Mathf.Lerp(WorldPlan.Half - 6000f, -WorldPlan.Half + 6000f, mapPosition.x);
            float x = WorldPlan.FrontAt(z).x + (mapPosition.y - 0.5f) * 8000f - 2000f;
            return new Vector2(Mathf.Clamp(x, -WorldPlan.Half + 3000f, WorldPlan.Half - 3000f), z);
        }

        // Where the hand-made Varrow Valley sits in the world: its dam on the world's dam (Codex's world plan has the
        // reservoir it belongs to), so the lake and the valley carry on beyond the sector.
        public static Vector2 VarrowSpot()
        {
            if (!WorldPlan.Ready) WorldPlan.Generate(World.WorldSeed);
            if (WorldPlan.Dams.Count == 0) return Vector2.zero;
            var dam = WorldPlan.Dams[0];
            return (dam.a + dam.b) / 2f - SectorPlan.Varrow().damPoint;
        }

        // Builds (or rebuilds round a new sector) every chunk, after the detailed terrain exists. The world picture
        // is made only when the world changes. The trees and houses on it (FarScatter) follow the camera.
        public static void Build()
        {
            if (!WorldPlan.Ready) return;
            if (root == null)
            {
                root = new GameObject("Far Terrain").transform;
                Object.DontDestroyOnLoad(root.gameObject);
                root.gameObject.AddComponent<FarScatter>();
            }
            root.GetComponent<FarScatter>()?.Clear();
            foreach (Transform child in root) Object.Destroy(child.gameObject);
            foreach (var m in meshes) Art.Discard(m);
            meshes.Clear();
            colliders.Clear();
            if (textureSeed != WorldPlan.Seed)
            {
                BuildCanopy();
                PaintWorld();
            }
            SampleEdge();
            if (material == null)
            {
                material = Art.NewMaterial(Color.white);
                material.SetFloat("_Smoothness", 0.05f);
            }
            material.mainTexture = worldTexture;
            for (int cz = 0; cz < ChunkCount; cz++)
                for (int cx = 0; cx < ChunkCount; cx++)
                    BuildChunk(cx, cz);
        }

        // ---- Heights ----

        // The land's top as the far terrain shows it: the ground, plus the woods' canopy standing on it.
        public static float Elevation(float worldX, float worldZ) => WorldPlan.Height(worldX, worldZ) + Canopy(worldX, worldZ);

        // How high the woods stand over the ground (16 m at the heart of a wood, tapering at its edges), on a 64 m
        // grid smoothed over three cells, so from far off a forest is a mass with a silhouette, not a dark stain.
        const float CanopyStep = 64f, CanopyHeight = 16f;
        static int canopyCount;
        static float[] canopy;

        static void BuildCanopy()
        {
            int n = canopyCount = Mathf.RoundToInt(WorldPlan.Size / CanopyStep) + 1;
            var wood = new float[n * n];
            System.Threading.Tasks.Parallel.For(0, n, j =>
            {
                for (int i = 0; i < n; i++)
                {
                    float x = -WorldPlan.Half + i * CanopyStep, z = -WorldPlan.Half + j * CanopyStep;
                    int trees = 0;
                    for (int k = 0; k < 4; k++)
                        if (WorldPlan.CoverAt(x + ((k & 1) - 0.5f) * 32f, z + ((k >> 1) - 0.5f) * 32f) == Cover.Forest) trees++;
                    wood[j * n + i] = trees / 4f;
                }
            });
            var smoothed = new float[n * n];
            System.Threading.Tasks.Parallel.For(0, n, j =>
            {
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int a = Mathf.Clamp(i + di, 0, n - 1), b = Mathf.Clamp(j + dj, 0, n - 1);
                            sum += wood[b * n + a];
                            count++;
                        }
                    smoothed[j * n + i] = CanopyHeight * Mathf.SmoothStep(0f, 1f, sum / count);
                }
            });
            canopy = smoothed;
        }

        public static float CanopyAt(float x, float z) => Canopy(x, z);

        static float Canopy(float x, float z)
        {
            if (canopy == null) return 0f;
            int n = canopyCount;
            float gx = Mathf.Clamp((x + WorldPlan.Half) / CanopyStep, 0f, n - 1.001f), gz = Mathf.Clamp((z + WorldPlan.Half) / CanopyStep, 0f, n - 1.001f);
            int i = (int)gx, j = (int)gz;
            float fx = gx - i, fz = gz - j;
            float a = Mathf.Lerp(canopy[j * n + i], canopy[j * n + i + 1], fx), b = Mathf.Lerp(canopy[(j + 1) * n + i], canopy[(j + 1) * n + i + 1], fx);
            return Mathf.Lerp(a, b, fz);
        }

        // The ground's height at a local point outside the detailed terrain: the near ring's blend from the sector's
        // edge out to the world's own heights, then the world's.
        public static float SurfaceAt(Vector2 local) => Blend(local, Elevation(local.x + Origin.x, local.y + Origin.y));

        // The same without the woods' canopy: the ground itself, where FarScatter stands its trees and houses.
        public static float GroundAt(Vector2 local) => Blend(local, WorldPlan.Height(local.x + Origin.x, local.y + Origin.y));

        // A world height near the sector, joined to the sector's own edge over the near ring's first 400 m.
        static float Blend(Vector2 local, float world)
        {
            float edge = Ground.Size / 2f - 0.5f;
            float out_ = Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.y)) - edge;
            if (out_ >= NearBlend || edgeHeights == null || local.sqrMagnitude < 1f) return world;
            Vector2 dir = local.normalized;
            float toEdge = edge / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
            float distance = local.magnitude - toEdge;
            return Mathf.Lerp(EdgeHeight(dir), world, Mathf.SmoothStep(0f, 1f, distance / NearBlend));
        }

        // The detailed terrain's height where the direction `dir` from the sector's centre leaves it.
        public static float EdgeHeight(Vector2 dir)
        {
            float a = Mathf.Repeat(Mathf.Atan2(dir.y, dir.x), Mathf.PI * 2f) / (Mathf.PI * 2f) * EdgeSegments;
            int i = Mathf.FloorToInt(a) % EdgeSegments;
            return Mathf.Lerp(edgeHeights[i], edgeHeights[(i + 1) % EdgeSegments], a - Mathf.Floor(a));
        }

        static void SampleEdge()
        {
            edgeHeights = new float[EdgeSegments];
            float edge = Ground.Size / 2f - 0.5f;
            for (int s = 0; s < EdgeSegments; s++)
            {
                float a = s * Mathf.PI * 2f / EdgeSegments;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 p = dir * (edge / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y)));
                edgeHeights[s] = Ground.HeightAt(new Vector3(p.x, 0f, p.y));
            }
        }

        // Where a chunk's vertex goes: well under the detailed terrain, 3 m under the near ring (which reaches
        // NearReach out from the edge along each direction from the centre, as Backdrop lays it), else on the world.
        static float ChunkHeight(Vector2 local)
        {
            float edge = Ground.Size / 2f;
            if (Mathf.Abs(local.x) < edge && Mathf.Abs(local.y) < edge) return Ground.HeightAt(new Vector3(local.x, 0f, local.y)) - 40f;
            float h = SurfaceAt(local);
            Vector2 dir = local.normalized;
            float beyond = local.magnitude - (edge - 0.5f) / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
            return beyond < NearReach ? h - 3f : h;
        }

        // ---- Chunks ----

        static void BuildChunk(int cx, int cz)
        {
            Vector2 corner = new Vector2(-WorldPlan.Half + cx * ChunkSize, -WorldPlan.Half + cz * ChunkSize); // world
            var go = new GameObject($"Chunk {cx},{cz}");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(corner.x - Origin.x, 0f, corner.y - Origin.y);
            var lods = new LOD[LodQuads.Length];
            for (int l = 0; l < LodQuads.Length; l++)
            {
                var mesh = ChunkMesh(corner, LodQuads[l]);
                meshes.Add(mesh);
                var part = new GameObject("LOD" + l);
                part.transform.SetParent(go.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lods[l] = new LOD(l == 0 ? 0.55f : l == 1 ? 0.22f : 0.0001f, new Renderer[] { renderer });
                if (l == LodQuads.Length - 1)
                {
                    var collider = part.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                    colliders.Add(collider);
                }
            }
            var group = go.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        // A grid of (quads + 1) x (quads + 1) heights over the chunk, plus a skirt round it, with normals from the world's own
        // slopes (so neighbouring chunks light alike) and UVs into the world picture.
        static Mesh ChunkMesh(Vector2 corner, int quads)
        {
            int n = quads + 1;
            float step = ChunkSize / quads;
            var vertices = new Vector3[n * n + 4 * n];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    Vector2 world = corner + new Vector2(i * step, j * step);
                    Vector2 local = world - Origin;
                    int k = j * n + i;
                    vertices[k] = new Vector3(i * step, ChunkHeight(local), j * step);
                    float dx = Elevation(world.x + step, world.y) - Elevation(world.x - step, world.y);
                    float dz = Elevation(world.x, world.y + step) - Elevation(world.x, world.y - step);
                    normals[k] = new Vector3(-dx, 2f * step, -dz).normalized;
                    uvs[k] = new Vector2((world.x + WorldPlan.Half) / WorldPlan.Size, (world.y + WorldPlan.Half) / WorldPlan.Size);
                }
            var triangles = new List<int>(quads * quads * 6 + quads * 48);
            for (int j = 0; j < quads; j++)
                for (int i = 0; i < quads; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            // The skirt: each edge's vertices again, 40 m lower, joined to the edge facing out.
            int skirt = n * n;
            for (int side = 0; side < 4; side++)
                for (int t = 0; t < n; t++)
                {
                    int edgeIndex = side == 0 ? t : side == 1 ? t * n + quads : side == 2 ? quads * n + t : t * n;
                    int k = skirt + side * n + t;
                    vertices[k] = vertices[edgeIndex] - new Vector3(0f, Skirt, 0f);
                    normals[k] = normals[edgeIndex];
                    uvs[k] = uvs[edgeIndex];
                    if (t == 0) continue;
                    int e0 = side == 0 ? t - 1 : side == 1 ? (t - 1) * n + quads : side == 2 ? quads * n + t - 1 : (t - 1) * n;
                    int s0 = k - 1;
                    // Both faces, so the skirt shows from either side whichever way its edge runs.
                    triangles.Add(e0); triangles.Add(edgeIndex); triangles.Add(s0); triangles.Add(edgeIndex); triangles.Add(k); triangles.Add(s0);
                    triangles.Add(e0); triangles.Add(s0); triangles.Add(edgeIndex); triangles.Add(edgeIndex); triangles.Add(s0); triangles.Add(k);
                }
            var mesh = new Mesh { name = $"Far terrain {quads}" };
            mesh.indexFormat = vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Where a ray meets the far terrain (its coarsest level, which every chunk keeps as its collider).
        public static bool Raycast(Ray ray, out Vector3 point)
        {
            point = Vector3.zero;
            float best = float.MaxValue;
            foreach (var c in colliders)
            {
                if (c == null || !c.bounds.IntersectRay(ray)) continue;
                if (c.Raycast(ray, out RaycastHit hit, 80000f) && hit.distance < best)
                {
                    best = hit.distance;
                    point = hit.point;
                }
            }
            return best < float.MaxValue;
        }

        // ---- The world picture ----

        // One 4096 x 4096 picture of the whole world from WorldPlan: every cover its colour (those of the detailed
        // terrain, so the two match at the seam), farmland as a patchwork of 150-300 m fields in crop colours with
        // darker hedges on some edges, woods mottled, towns as roofs and streets, then rivers and roads drawn over.
        static void PaintWorld()
        {
            Art.Discard(worldTexture);
            var pixels = new Color32[TextureSize * TextureSize];
            float pixel = WorldPlan.Size / TextureSize;
            // The detailed fields' crops (ripe and ripening wheat, young and green crops, ploughed soil, hay), a
            // little nearer the meadow's colour, as distance evens them out.
            Color32[] crops =
            {
                new Color32(170, 146, 90, 255), new Color32(156, 138, 84, 255), new Color32(98, 114, 62, 255),
                new Color32(86, 104, 58, 255), new Color32(104, 82, 62, 255), new Color32(138, 132, 86, 255),
            };
            System.Threading.Tasks.Parallel.For(0, TextureSize, y =>
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float wx = -WorldPlan.Half + (x + 0.5f) * pixel, wz = -WorldPlan.Half + (y + 0.5f) * pixel;
                    float mottle = Mathf.PerlinNoise(wx / 180f + 3f, wz / 180f + 7f) - 0.5f + 0.6f * (Mathf.PerlinNoise(wx / 45f + 11f, wz / 45f) - 0.5f);
                    // The cover is looked up a little off the point, by up to 22 m in drifts, so the edges of woods and
                    // farmland wander instead of following the plan's 32 m squares.
                    float jx = 44f * (Mathf.PerlinNoise(wx / 70f, wz / 70f + 31f) - 0.5f), jz = 44f * (Mathf.PerlinNoise(wx / 70f + 57f, wz / 70f) - 0.5f);
                    Color32 c;
                    switch (WorldPlan.CoverAt(wx + jx, wz + jz))
                    {
                        case Cover.Farmland:
                            c = Field(wx, wz, crops);
                            break;
                        case Cover.Forest:
                            // Crowns in clumps and gaps: dark between them, lighter tops in the sun.
                            c = Shade(new Color32(54, 70, 42, 255), 0.95f + 0.45f * mottle + 0.35f * (Mathf.PerlinNoise(wx / 16f, wz / 16f + 9f) - 0.5f));
                            break;
                        case Cover.Wetland:
                            c = Shade(new Color32(80, 98, 72, 255), 1f + 0.3f * mottle);
                            break;
                        case Cover.Water:
                            c = new Color32(48, 66, 74, 255);
                            break;
                        case Cover.Town:
                            c = (Mathf.PerlinNoise(wx / 40f, wz / 40f) > 0.55f) ? new Color32(140, 84, 66, 255) : new Color32(118, 114, 108, 255);
                            break;
                        case Cover.Village:
                            c = (Mathf.PerlinNoise(wx / 30f + 5f, wz / 30f) > 0.62f) ? new Color32(146, 88, 70, 255) : new Color32(98, 116, 74, 255);
                            break;
                        case Cover.Rock:
                            c = Shade(new Color32(122, 118, 108, 255), 1f + 0.3f * mottle);
                            break;
                        default:
                            c = Shade(new Color32(96, 118, 72, 255), 1f + 0.16f * mottle + 0.08f * (Mathf.PerlinNoise(wx / 600f + 5f, wz / 600f) - 0.5f));
                            break;
                    }
                    // Rivers narrower than a pixel still show, as far as their width covers it.
                    float river = WorldPlan.RiverDistance(wx, wz);
                    if (river < pixel) c = Color32.Lerp(c, new Color32(48, 66, 74, 255), Mathf.Clamp01(1.2f - river / pixel));
                    pixels[y * TextureSize + x] = c;
                }
            });
            foreach (var road in WorldPlan.Roads)
            {
                var colour = road.kind == RoadClass.Highway ? new Color32(78, 78, 80, 255) : road.kind == RoadClass.Road ? new Color32(120, 112, 98, 255) : new Color32(130, 116, 92, 255);
                float width = road.kind == RoadClass.Highway ? 1.5f : 1f;
                for (int k = 1; k < road.points.Count; k++)
                    DrawLine(pixels, road.points[k - 1], road.points[k], width, colour, pixel);
            }
            worldTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true)
            {
                name = "World picture",
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 8,
            };
            worldTexture.SetPixels32(pixels);
            worldTexture.Apply(true, true); // no longer readable: keeps 64 MB of pixels out of memory
            textureSeed = WorldPlan.Seed;
        }

        // The world picture, for the near ring round the sector (Backdrop) to paint itself with.
        public static Texture2D WorldTexture => worldTexture;

        // A field's colour, as farmland looks from the air: districts (the cells of a jittered 700 m grid) each
        // ploughed its own way and cut into strips 35-110 m wide that bend a little, every strip divided along its
        // length into fields of 150-400 m with their own crops. About half the strip edges carry a hedge; the rest,
        // and the ends of fields, a paler uncut margin. Districts are edged with hedges.
        static Color32 Field(float x, float z, Color32[] crops)
        {
            const float cell = 700f;
            float fx = x / cell, fz = z / cell;
            int gx = Mathf.FloorToInt(fx), gz = Mathf.FloorToInt(fz);
            float best = float.MaxValue, second = float.MaxValue;
            int district = 0;
            for (int oz = -1; oz <= 1; oz++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = gx + ox, cz = gz + oz;
                    int h = Hash(cx, cz);
                    float sx = cx + 0.15f + 0.7f * ((h & 1023) / 1023f), sz = cz + 0.15f + 0.7f * (((h >> 10) & 1023) / 1023f);
                    float d = (sx - fx) * (sx - fx) + (sz - fz) * (sz - fz);
                    if (d < best) { second = best; best = d; district = h; }
                    else if (d < second) second = d;
                }
            float angle = ((district >> 3) & 1023) / 1023f * Mathf.PI;
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            float u = x * ca + z * sa, v = -x * sa + z * ca;
            v += 30f * (Mathf.PerlinNoise(u / 600f + (district & 63), 3.7f) - 0.5f);
            float width = 35f + ((district >> 13) & 63) / 63f * 75f;
            int strip = Mathf.FloorToInt(v / width);
            float across = v / width - strip;
            int hs = Hash(strip, district);
            float length = 150f + (hs & 255) / 255f * 250f;
            float along = u + ((hs >> 8) & 1023);
            float segment = along / length;
            var c = crops[Hash(Mathf.FloorToInt(segment), hs) % crops.Length];
            var margin = new Color32(126, 128, 86, 255);
            var hedge = new Color32(46, 62, 36, 255);
            float side = Mathf.Min(across, 1f - across) * width;
            float end = Mathf.Min(segment - Mathf.Floor(segment), Mathf.Ceil(segment) - segment) * length;
            if (side < 3f) c = ((hs >> 20) & 1) == 0 ? Color32.Lerp(c, hedge, 0.6f) : Color32.Lerp(c, margin, 0.45f);
            else if (end < 2.5f) c = Color32.Lerp(c, margin, 0.45f);
            if ((Mathf.Sqrt(second) - Mathf.Sqrt(best)) * cell < 4f) c = Color32.Lerp(c, hedge, 0.6f);
            return c;
        }

        static int Hash(int x, int z)
        {
            unchecked
            {
                int h = x * 73856093 ^ z * 19349663 ^ WorldPlan.Seed * 83492791;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                return h & 0x7fffffff;
            }
        }

        static Color32 Shade(Color32 c, float f) =>
            new Color32((byte)Mathf.Clamp(c.r * f, 0f, 255f), (byte)Mathf.Clamp(c.g * f, 0f, 255f), (byte)Mathf.Clamp(c.b * f, 0f, 255f), 255);

        static void DrawLine(Color32[] pixels, Vector2 a, Vector2 b, float width, Color32 colour, float pixel)
        {
            float length = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / (pixel * 0.5f)));
            int reach = Mathf.CeilToInt(width);
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                float px = (p.x + WorldPlan.Half) / pixel - 0.5f, pz = (p.y + WorldPlan.Half) / pixel - 0.5f;
                for (int oy = -reach; oy <= reach; oy++)
                    for (int ox = -reach; ox <= reach; ox++)
                    {
                        int x = Mathf.RoundToInt(px) + ox, y = Mathf.RoundToInt(pz) + oy;
                        if (x < 0 || y < 0 || x >= TextureSize || y >= TextureSize) continue;
                        float d = Mathf.Sqrt((x - px) * (x - px) + (y - pz) * (y - pz));
                        float cover = Mathf.Clamp01(width - d + 0.5f);
                        if (cover <= 0f) continue;
                        int k = y * TextureSize + x;
                        pixels[k] = Color32.Lerp(pixels[k], colour, cover);
                    }
            }
        }
    }
}

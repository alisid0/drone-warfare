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

        // HLOD owns the visible forest surface; Elevation retains its conservative flight canopy.
        public static bool ForestCanopyHlod = true;

        public static bool Active => root != null && WorldPlan.Ready;
        public static Vector2 Origin { get; private set; }

        static Transform root;
        static readonly List<Mesh> meshes = new List<Mesh>();
        static readonly List<MeshCollider> colliders = new List<MeshCollider>();
        static Texture2D worldTexture;
        static Material material;
        static int textureSeed = -1;
        static FarDetailData picturePlan;
        static FarWorldPicture painter;
        public static string BuildTimings { get; private set; } = "";
        public static string PictureTimings { get; private set; } = "not started";
        public static bool PicturePending => painter != null && painter.Busy;
        static double geometryMs, meshUploadMs, colliderMs, chunkSnapshotMs;

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
            var clock = System.Diagnostics.Stopwatch.StartNew();
            geometryMs = meshUploadMs = colliderMs = chunkSnapshotMs = 0;
            var times = new System.Text.StringBuilder();
            void Lap(string name) { times.AppendFormat("{0} {1:F2} ms, ", name, clock.Elapsed.TotalMilliseconds); clock.Restart(); }
            FarDetail.Clear();
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
            Lap("clear");
            var snapshot = WorldPlan.CaptureDetail(null);
            Lap("snapshot");
            if (textureSeed != WorldPlan.Seed || !ReferenceEquals(picturePlan, snapshot))
            {
                BuildCanopy();
                Lap("canopy");
                picturePlan = snapshot;
                PaintWorld();
                Lap("preview/dispatch");
            }
            FarDetailEdge.Apply(worldTexture, Origin);
            Lap("collar");
            SampleEdge();
            if (material == null)
            {
                material = FarDetailMaterial.Create();
                material.EnableKeyword("_FarRegionChunk");
                material.SetFloat("_Smoothness", 0f);
            }
            material.mainTexture = worldTexture;
            Lap("edge/material");
            for (int cz = 0; cz < ChunkCount; cz++)
                for (int cx = 0; cx < ChunkCount; cx++)
                    BuildChunk(cx, cz);
            Lap("chunks/colliders");
            FarDetail.Initialise(root);
            Lap("detail pools/region array");
            BuildTimings = times.ToString() + $"chunk parts: geometry {geometryMs:F2} ms, mesh APIs {meshUploadMs:F2} ms, collider bind/cook {colliderMs:F2} ms, snapshot {chunkSnapshotMs:F2} ms";
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

        // Mesh builders use the bare ground when HLOD owns the canopy; flight keeps SurfaceAt's clearance.
        public static float VisualSurfaceAt(Vector2 local) => ForestCanopyHlod ? GroundAt(local) : SurfaceAt(local);

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
            // Only the visible chunk loses its smooth shell; SurfaceAt also supplies drone flight clearance.
            float h = VisualSurfaceAt(local);
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
            FarDetailChunks.Geometry source = null;
            for (int l = 0; l < LodQuads.Length; l++)
            {
                var mesh = ChunkMesh(corner, LodQuads[l], out var geometry);
                if (l == 0) source = geometry;
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
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    var collider = part.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                    colliders.Add(collider);
                    colliderMs += MillisecondsSince(start);
                }
            }
            var group = go.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();
            long capture = System.Diagnostics.Stopwatch.GetTimestamp();
            FarDetailChunks.Register(corner, lods[0].renderers[0].GetComponent<MeshFilter>(), group, source);
            chunkSnapshotMs += MillisecondsSince(capture);
        }

        // A grid of (quads + 1) x (quads + 1) heights over the chunk, plus a skirt round it, with normals from the world's own
        // slopes (so neighbouring chunks light alike) and UVs into the world picture.
        static Mesh ChunkMesh(Vector2 corner, int quads, out FarDetailChunks.Geometry source)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
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
            var indices = triangles.ToArray();
            source = quads == LodQuads[0] ? new FarDetailChunks.Geometry { vertices = vertices, normals = normals, uv = uvs, triangles = indices } : null;
            geometryMs += MillisecondsSince(started); started = System.Diagnostics.Stopwatch.GetTimestamp();
            var mesh = new Mesh { name = $"Far terrain {quads}" };
            mesh.indexFormat = vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            meshUploadMs += MillisecondsSince(started);
            return mesh;
        }

        // Timestamp arithmetic avoids allocating a Stopwatch for every chunk and native API stage.
        static double MillisecondsSince(long start) => (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        // Where a ray meets the far terrain (its coarsest level, which every chunk keeps as its collider).
        public static bool Raycast(Ray ray, out Vector3 point)
        {
            point = Vector3.zero;
            float best = float.MaxValue;
            foreach (var c in colliders)
            {
                if (c == null || !c.bounds.IntersectRay(ray)) continue;
                if (c.Raycast(ray, out RaycastHit hit, 80000f) && hit.distance < best &&
                    !FarDetail.TryHeight(new Vector2(hit.point.x, hit.point.z), out _))
                {
                    best = hit.distance;
                    point = hit.point;
                }
            }
            return best < float.MaxValue;
        }

        // ---- The world picture ----

        // Display a cheap cover preview immediately, then paint shared parcels and vectors off the main thread.
        static void PaintWorld()
        {
            FarDetailEdge.Forget(worldTexture);
            Art.Discard(worldTexture);
            const int preview = 256;
            var pixels = picturePlan.PreviewPixels(preview);
            worldTexture = new Texture2D(preview, preview, TextureFormat.RGBA32, true)
            {
                name = "World picture",
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 8,
            };
            worldTexture.SetPixels32(pixels);
            worldTexture.Apply(true, false); // a small collar is restored/repainted at each sector change
            textureSeed = WorldPlan.Seed;
            painter = root.GetComponent<FarWorldPicture>() ?? root.gameObject.AddComponent<FarWorldPicture>();
            PictureTimings = "painting on worker (2 threads)";
            painter.Begin(picturePlan, worldTexture);
        }

        // Keep the texture object itself: Backdrop and regional shader bindings see the final picture together.
        internal static void FinishPicture(Texture2D target, Color32[] pixels, double workerMs)
        {
            if (target == null || target != worldTexture) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            FarDetailEdge.Forget(target);
            if (!target.Reinitialize(TextureSize, TextureSize, TextureFormat.RGBA32, true))
            { PictureFailed(); UnityEngine.Debug.LogError("Could not resize the world picture."); return; }
            double resize = clock.Elapsed.TotalMilliseconds; clock.Restart();
            target.SetPixelData(pixels, 0);
            double copy = clock.Elapsed.TotalMilliseconds; clock.Restart();
            // Paint the collar before the sole upload; the old path regenerated and uploaded all mips twice.
            FarDetailEdge.Apply(target, Origin, false);
            double collar = clock.Elapsed.TotalMilliseconds; clock.Restart();
            target.Apply(true, false);
            double apply = clock.Elapsed.TotalMilliseconds;
            PictureTimings = $"worker {workerMs:F2} ms, resize {resize:F2} ms, CPU copy 64 MiB {copy:F2} ms, collar {collar:F2} ms, Apply/mips {apply:F2} ms; "
                + $"districts {picturePlan.fields.BuiltDistricts}, district CPU {picturePlan.fields.BuildMilliseconds:F2} ms; "
                + $"snapshot cold {WorldPlan.DetailCaptureMs:F2} ms (clone {WorldPlan.DetailCopyMs:F2}, index {WorldPlan.DetailIndexMs:F2}); BC1 0 ms, full readback 0 ms";
        }

        // Both worker and native publication failures remain visible in the build log.
        internal static void PictureFailed() { PictureTimings = "failed (see exception)"; }

        // The world picture, for the near ring round the sector (Backdrop) to paint itself with.
        public static Texture2D WorldTexture => worldTexture;

        // The canopy's zero-height fringe must meet the same world picture, including its displaced cover edge:
        // sample the painted picture itself (kept readable for the sector collar) rather than a copy of its painter.
        public static Color CanopyGroundColour(float x, float z)
        {
            if (worldTexture == null) return new Color(0.115f, 0.178f, 0.066f);
            return worldTexture.GetPixelBilinear((x + WorldPlan.Half) / WorldPlan.Size, (z + WorldPlan.Half) / WorldPlan.Size).linear;
        }
    }
}

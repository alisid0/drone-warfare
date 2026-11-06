using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Lit woodland tiles sample at 16/32 m, distant masses at 64/128 m, and batch into 2 km draw blocks.
    // Generation is sliced between small sampling batches on the main thread, so changing WorldPlan cannot race a worker.
    public sealed class ForestHlod
    {
        public const int BlockSize = 2048, TileSize = 256;
        const int BlockCount = 28;
        const double BuildMilliseconds = 0.85;

        // The same plain mesh data goes to Unity and the offline harness; no substitute forest recipe.
        public sealed class Data
        {
            public Vector2 corner;
            public int step, textureSize;
            public bool adaptive;
            public Vector3 focus;
            public Vector3[] vertices, normals;
            public Vector2[] uv;
            public float[] ground, crown;
            public int[] triangles;
            public Color32[] pixels;
        }

        // Shader fades keep surfaces immutable, so uploaded blocks need no retained CPU vertex copy.
        sealed class Block
        {
            public Data data;
            public Mesh mesh;
            public Material material;
            public Texture2D texture;
            public Bounds bounds;
            public int lastSeen;
            public int triangles;
            public Block previous;
            public float changedAt;
        }

        readonly Dictionary<int, Block> blocks = new Dictionary<int, Block>();
        readonly List<int> stale = new List<int>();
        readonly Stopwatch timer = new Stopwatch();
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        IEnumerator builder;
        Data pending;
        Block pendingBlock;
        int pendingKey, frame, builtFrame = -1;
        Vector3 buildingFrom;
        Camera buildingCamera;
        public static int BlocksDrawn { get; private set; }
        public static int TrianglesDrawn { get; private set; }
        public static int CachedBlocks { get; private set; }
        public static float BuildMs { get; private set; }
        public static int CompletedBuilds { get; private set; }
        public static bool Building { get; private set; }
        public static int MissingBlocks { get; private set; }
        public static int CoarseBlocks { get; private set; }
        public static int NearCoarseBlocks { get; private set; }

        // Destroy all native allocations as well as a partially built block when the terrain origin changes.
        public void Clear()
        {
            foreach (var block in blocks.Values) Discard(block);
            if (pendingBlock != null) { Discard(pendingBlock); pendingBlock = null; }
            blocks.Clear(); builder = null; pending = null; builtFrame = -1;
            BlocksDrawn = TrianglesDrawn = CachedBlocks = MissingBlocks = CoarseBlocks = 0;
            CompletedBuilds = 0; BuildMs = 0f; Building = false;
        }

        // Sector construction has already sampled these coarse blocks; uploading does not re-run the recipe.
        public void AdoptSector()
        {
            var prepared = SectorForest.PreparedCanopy;
            if (prepared == null) return;
            foreach (var data in prepared)
            {
                int i = Mathf.RoundToInt((data.corner.x + WorldPlan.Half) / BlockSize);
                int j = Mathf.RoundToInt((data.corner.y + WorldPlan.Half) / BlockSize);
                blocks[j * BlockCount + i] = Upload(data);
            }
            CachedBlocks = blocks.Count;
        }

        // Each cached block owns exactly these three Unity resources.
        static void Discard(Block block)
        {
            if (block.previous != null) Discard(block.previous);
            Art.Discard(block.mesh); Art.Discard(block.material); Art.Discard(block.texture);
        }

        // Select by distance to the block's footprint, including altitude, rather than its distant centre.
        public static int Step(Vector3 camera, Vector2 corner)
        {
            float x = Mathf.Clamp(camera.x, corner.x, corner.x + BlockSize);
            float z = Mathf.Clamp(camera.z, corner.y, corner.y + BlockSize);
            float d = Vector3.Distance(camera, new Vector3(x, WorldPlan.Height(x, z) + 16f, z));
            return d < 1800f ? 16 : d < 4000f ? 32 : d < 9000f ? 64 : 128;
        }

        // Fill missing coverage first; rapid flight keeps the old 0.30 ms slice, while stationary views refine faster.
        public void Update(Camera camera, Plane[] planes, bool cruising = false)
        {
            BlocksDrawn = TrianglesDrawn = MissingBlocks = CoarseBlocks = NearCoarseBlocks = 0; frame++;
            if (!FarTerrain.ForestCanopyHlod) return;
            if (!WorldPlan.Ready)
            {
                foreach (var block in blocks.Values)
                    if ((block.mesh != null || block.previous != null) && GeometryUtility.TestPlanesAABB(planes, block.bounds)) Draw(block, camera);
                return;
            }
            Vector3 local = camera.transform.position;
            Vector3 world = local + new Vector3(FarTerrain.Origin.x, 0f, FarTerrain.Origin.y);
            // A teleport must not wait for an obsolete fine texture before receiving coarse woodland.
            if (builder != null && buildingCamera == camera && pending.step < 256 && (world - buildingFrom).sqrMagnitude > 1024f * 1024f)
            {
                if (pendingBlock != null) { Discard(pendingBlock); pendingBlock = null; }
                builder = null; pending = null;
            }
            int wantedKey = -1, wantedStep = 256;
            float best = float.MaxValue;
            for (int j = 0; j < BlockCount; j++)
                for (int i = 0; i < BlockCount; i++)
                {
                    int key = j * BlockCount + i;
                    Vector2 corner = new Vector2(-WorldPlan.Half + i * BlockSize, -WorldPlan.Half + j * BlockSize);
                    Vector2 middle = corner + Vector2.one * (BlockSize / 2f) - FarTerrain.Origin;
                    // Conservative bounds before construction; actual mesh bounds are used for submission.
                    var bounds = new Bounds(new Vector3(middle.x, 500f, middle.y), new Vector3(BlockSize, 2200f, BlockSize));
                    if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                    float distance = bounds.SqrDistance(local);
                    if (distance > 32000f * 32000f) continue;
                    int step = Step(world, corner);
                    if (blocks.TryGetValue(key, out Block block))
                    {
                        block.lastSeen = frame;
                        if ((block.mesh != null || block.previous != null) && GeometryUtility.TestPlanesAABB(planes, block.bounds)) Draw(block, camera);
                        bool moved = block.data.adaptive && block.data.step <= 16 && (world - block.data.focus).sqrMagnitude > 256f * 256f;
                        if (block.data.step > step || moved) { CoarseBlocks++; if (step <= 32) NearCoarseBlocks++; }
                        if (block.data.step <= step && !moved) continue;
                        // First give every visible block a coarse shape before refining any of them.
                        distance += 2e9f;
                    }
                    else MissingBlocks++;
                    if ((builder == null || key != pendingKey) && distance < best)
                    {
                        best = distance; wantedKey = key;
                        wantedStep = block == null ? 256 : step;
                    }
                }
            // Turning a camera can expose missing districts without a teleport; those also preempt expensive refinement.
            if (builder != null && pending.step < 256 && wantedKey >= 0 && wantedStep == 256)
            {
                if (pendingBlock != null) { Discard(pendingBlock); pendingBlock = null; }
                builder = null; pending = null;
            }
            if (builder == null && wantedKey >= 0)
            {
                pendingKey = wantedKey; buildingFrom = world; buildingCamera = camera;
                pending = new Data { corner = new Vector2(-WorldPlan.Half + wantedKey % BlockCount * BlockSize,
                    -WorldPlan.Half + wantedKey / BlockCount * BlockSize), step = wantedStep, adaptive = true, focus = world };
                builder = Prepare(pending);
            }
            timer.Restart();
            bool buildThisCamera = builtFrame != Time.frameCount;
            builtFrame = Time.frameCount;
            while (buildThisCamera && builder != null && timer.Elapsed.TotalMilliseconds < (cruising ? 0.30 : BuildMilliseconds))
                if (!builder.MoveNext())
                {
                    var next = pendingBlock; pendingBlock = null;
                    if (blocks.TryGetValue(pendingKey, out Block old))
                    {
                        if (old.previous != null) { Discard(old.previous); old.previous = null; }
                        next.previous = old; next.changedAt = Time.unscaledTime;
                        if (next.mesh == null) next.bounds = old.bounds;
                    }
                    blocks[pendingKey] = next;
                    CompletedBuilds++;
                    builder = null; pending = null;
                }
            if (buildThisCamera) BuildMs = (float)timer.Elapsed.TotalMilliseconds;
            Building = builder != null;
            // Retain a short look-behind cache, but bound native memory after a long flight or camera tour.
            stale.Clear();
            foreach (var pair in blocks) if (frame - pair.Value.lastSeen > 180) stale.Add(pair.Key);
            foreach (int key in stale) { Discard(blocks[key]); blocks.Remove(key); }
            CachedBlocks = blocks.Count;
        }

        // Sector loading can finish synchronously before play; flight uses the same upload in small slices.
        Block Upload(Data data)
        {
            // Upload releases its own CPU references; sector data remains reusable after Clear or renderer replacement.
            var copy = new Data { corner = data.corner, step = data.step, textureSize = data.textureSize, adaptive = data.adaptive, focus = data.focus,
                vertices = data.vertices, normals = data.normals, uv = data.uv, ground = data.ground,
                crown = data.crown, triangles = data.triangles, pixels = data.pixels };
            var block = new Block { data = copy, lastSeen = frame };
            var upload = UploadSteps(block); while (upload.MoveNext()) { }
            return block;
        }

        // Finish coarse sampling before native allocation, then keep every upload operation in the timed slice.
        IEnumerator Prepare(Data data)
        {
            var build = Build(data, null); while (build.MoveNext()) yield return null;
            pendingBlock = new Block { data = data, lastSeen = frame };
            var upload = UploadSteps(pendingBlock); while (upload.MoveNext()) yield return null;
        }

        // Separate channel copies and mip generation so refinement does not commit all resources in one frame.
        IEnumerator UploadSteps(Block block)
        {
            var data = block.data;
            if (data.triangles.Length == 0)
            {
                data.vertices = data.normals = null; data.uv = null;
                data.ground = data.crown = null; data.triangles = null; data.pixels = null;
                yield break;
            }
            var mesh = new Mesh { name = "Forest canopy " + data.step };
            block.mesh = mesh;
            mesh.indexFormat = data.vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = data.vertices; yield return null;
            mesh.normals = data.normals; yield return null;
            mesh.uv = data.uv; yield return null;
            mesh.triangles = data.triangles; yield return null;
            mesh.RecalculateBounds(); mesh.UploadMeshData(true); yield return null;
            var texture = new Texture2D(data.textureSize, data.textureSize, TextureFormat.RGBA32, true)
                { name = "Forest crown field", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            block.texture = texture;
            texture.SetPixels32(data.pixels); yield return null;
            texture.Apply(true, true); yield return null;
            var material = ForestMaterials.Create("Canopy", null, 3);
            material.mainTexture = texture; material.SetFloat("_Smoothness", 0.12f);
            if (ForestMaterials.Fades(material)) material.SetTexture("_ForestDetail", Resources.Load<Texture2D>("Textures/ForestDetail"));
            block.mesh = mesh; block.material = material; block.texture = texture; block.bounds = mesh.bounds;
            block.triangles = data.triangles.Length / 3;
            data.vertices = data.normals = null; data.uv = null;
            data.ground = data.crown = null; data.triangles = null;
            data.pixels = null;
        }

        // Crown cards overlap a lower stand surface; completed projected selections open it conservatively.
        // The shader opens the camera's tree bubble smoothly; no vertex uploads or 16 m stepping during flight.
        void Draw(Block block, Camera camera)
        {
            float fade = block.previous != null ? Mathf.Clamp01((Time.unscaledTime - block.changedAt) / 0.75f) : 1f;
            if (block.previous != null)
            {
                if (fade >= 1f) { Discard(block.previous); block.previous = null; }
                else if (block.previous.mesh != null) Submit(block.previous, camera, -Mathf.Max(0.00001f, fade));
            }
            if (block.mesh != null) Submit(block, camera, fade);
        }

        // Refinement shares complementary coverage for three quarters of a second instead of replacing a silhouette.
        void Submit(Block block, Camera camera, float fade)
        {
            FarScatter.SetForestView(properties, camera, fade);
            var rp = new RenderParams(block.material) { camera = camera, worldBounds = block.bounds,
                matProps = properties, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
            Graphics.RenderMesh(rp, block.mesh, 0, Matrix4x4.identity);
            BlocksDrawn++; TrianglesDrawn += block.triangles;
        }

        // World-coordinate sampling and a common 8 m perimeter join every refinement level without skirts.
        sealed class Surface
        {
            readonly Data data;
            readonly Dictionary<long, int> indices = new Dictionary<long, int>();
            readonly Dictionary<long, float> heights = new Dictionary<long, float>();
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<float> ground = new List<float>(), crown = new List<float>();
            public readonly List<int> triangles = new List<int>();

            // Temporary sampling caches belong to one block and disappear after its immutable arrays are uploaded.
            public Surface(Data target) { data = target; }

            // Use one height function for all tiers; averaging coarse vertices used to split shared block edges.
            float Height(int x, int z)
            {
                long key = ((long)x << 32) ^ (uint)z;
                if (heights.TryGetValue(key, out float height)) return height;
                Vector2 world = data.corner + new Vector2(x, z);
                var stand = SectorForest.At(world.x, world.y);
                float relief = ForestPattern.Canopy(stand, world.x, world.y) - (1f - stand.cover) * 1.7f;
                height = FarScatter.GroundAt(world - FarTerrain.Origin) + relief - 0.3f;
                heights.Add(key, height);
                return height;
            }

            // Central differences use the same world-space surface on both sides of every tile and block boundary.
            public int Vertex(int x, int z)
            {
                long key = ((long)x << 32) ^ (uint)z;
                if (indices.TryGetValue(key, out int index)) return index;
                Vector2 p = data.corner + new Vector2(x, z) - FarTerrain.Origin;
                float top = Height(x, z), floor = FarScatter.GroundAt(p);
                index = vertices.Count; indices.Add(key, index);
                vertices.Add(new Vector3(p.x, top, p.y)); ground.Add(floor); crown.Add(top - floor);
                float pixel = TexelSize(data), padding = pixel * 2f;
                uv.Add(new Vector2((x + padding) / (BlockSize + padding * 2f), (z + padding) / (BlockSize + padding * 2f)));
                const int d = 8;
                float dx = Height(x + d, z) - Height(x - d, z), dz = Height(x, z + d) - Height(x, z - d);
                normals.Add(new Vector3(-dx, 2f * d, -dz).normalized);
                return index;
            }

            // Entirely empty triangles disappear, while young margins descend gently into the actual ground.
            public void Face(int a, int b, int c)
            {
                if (Mathf.Max(crown[a], Mathf.Max(crown[b], crown[c])) < 0.25f) return;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            // Copy one channel per slice to avoid a large allocation-and-copy burst at the end of refinement.
            public IEnumerator Finish()
            {
                data.vertices = vertices.ToArray(); yield return null;
                data.normals = normals.ToArray(); yield return null;
                data.uv = uv.ToArray(); yield return null;
                data.ground = ground.ToArray(); yield return null;
                data.crown = crown.ToArray(); yield return null;
                data.triangles = triangles.ToArray();
            }
        }

        // Stitch a quad to the finer nodes on any neighbouring edge; regular interiors need only two triangles.
        static void Quad(Surface surface, int x, int z, int step, int south, int north, int west, int east)
        {
            if (south >= step && north >= step && west >= step && east >= step)
            {
                int a = surface.Vertex(x, z), b = surface.Vertex(x + step, z), c = surface.Vertex(x, z + step), d = surface.Vertex(x + step, z + step);
                surface.Face(a, c, b); surface.Face(b, c, d);
                return;
            }
            int center = surface.Vertex(x + step / 2, z + step / 2);
            for (int side = 0; side < 4; side++)
            {
                int stride = Mathf.Min(step, side == 0 ? west : side == 1 ? north : side == 2 ? east : south);
                int previous = -1;
                for (int t = 0; t <= step; t += stride)
                {
                    int vx = x + (side == 0 ? 0 : side == 1 ? t : side == 2 ? step : step - t);
                    int vz = z + (side == 0 ? t : side == 1 ? step : side == 2 ? step - t : 0);
                    int vertex = surface.Vertex(vx, vz);
                    if (previous >= 0) surface.Face(center, previous, vertex);
                    previous = vertex;
                }
            }
        }
        // Construct in 256 m tiles, retaining one batched draw per 2 km district rather than a draw per tile.
        public static IEnumerator Build(Data data, byte[] unused)
        {
            // Bootstrap coverage must not pay for a 1,024-vertex seam or thousands of fine crown texels.
            if (data.step == 256)
            {
                var coverage = Coverage(data); while (coverage.MoveNext()) yield return null;
                yield break;
            }
            var surface = new Surface(data);
            for (int tz = 0; tz < BlockSize; tz += TileSize)
                for (int tx = 0; tx < BlockSize; tx += TileSize)
                {
                    int step = TileStep(data, tx, tz);
                    int south = tz == 0 ? 8 : TileStep(data, tx, tz - TileSize);
                    int north = tz + TileSize == BlockSize ? 8 : TileStep(data, tx, tz + TileSize);
                    int west = tx == 0 ? 8 : TileStep(data, tx - TileSize, tz);
                    int east = tx + TileSize == BlockSize ? 8 : TileStep(data, tx + TileSize, tz);
                    for (int z = tz; z < tz + TileSize; z += step)
                        for (int x = tx; x < tx + TileSize; x += step)
                        {
                            Quad(surface, x, z, step, z == tz ? south : step, z + step == tz + TileSize ? north : step,
                                x == tx ? west : step, x + step == tx + TileSize ? east : step);
                            yield return null;
                        }
                }
            var finish = surface.Finish(); while (finish.MoveNext()) yield return null;
            if (data.triangles.Length == 0) yield break;
            int core = Mathf.RoundToInt(BlockSize / TexelSize(data));
            int size = data.textureSize = core + 4;
            float pixel = BlockSize / (float)core;
            data.pixels = new Color32[size * size];
            // The two-texel gutter uses the neighbour's world coordinates; UVs address identical boundary colours.
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = data.corner.x + (i - 1.5f) * pixel, z = data.corner.y + (j - 1.5f) * pixel;
                    var stand = SectorForest.At(x, z);
                    data.pixels[j * size + i] = ForestPalette.Surface(stand, x, z, pixel);
                    if ((i & 7) == 7) yield return null;
                }
        }

        // Keep 16 m geometry near visible crowns; shared detail supplies the smaller relief on every surface tier.
        static int TileStep(Data data, int x, int z)
        {
            if (!data.adaptive || data.step >= 64) return data.step;
            Vector2 corner = data.corner + new Vector2(x, z);
            float px = Mathf.Clamp(data.focus.x, corner.x, corner.x + TileSize);
            float pz = Mathf.Clamp(data.focus.z, corner.y, corner.y + TileSize);
            float distance = Vector3.Distance(data.focus, new Vector3(px, WorldPlan.Height(px, pz) + 16f, pz));
            return Mathf.Max(data.step, distance < 900f ? 16 : distance < 2200f ? 32 : 64);
        }

        // Small block textures carry soft species districts; the shared world detail resolves crowns at every tier.
        static float TexelSize(Data data) => data.step <= 32 ? 32f : Mathf.Max(32f, data.step * 0.25f);

        // A regular low-cost mass arrives first; temporary skirts hide mixed edges until stitched refinement replaces it.
        static IEnumerator Coverage(Data data)
        {
            const int n = BlockSize / 256 + 1;
            var vertices = new List<Vector3>(n * n + n * 4);
            var normals = new List<Vector3>(n * n + n * 4);
            var uv = new List<Vector2>(n * n + n * 4);
            var ground = new List<float>(n * n + n * 4);
            var crown = new List<float>(n * n + n * 4);
            var triangles = new List<int>();
            const int core = n - 1, size = core + 4;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                Vector2 world = data.corner + new Vector2(x * 256f, z * 256f), p = world - FarTerrain.Origin;
                var stand = SectorForest.At(world.x, world.y);
                float floor = FarScatter.GroundAt(p), height = ForestPattern.Canopy(stand, world.x, world.y);
                vertices.Add(new Vector3(p.x, floor + height - 0.3f, p.y));
                normals.Add(Vector3.up); ground.Add(floor); crown.Add(height);
                uv.Add(new Vector2((x + 2f) / size, (z + 2f) / size));
                yield return null;
            }
            for (int z = 0; z < n - 1; z++) for (int x = 0; x < n - 1; x++)
            {
                int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                if (Mathf.Max(crown[a], Mathf.Max(crown[b], Mathf.Max(crown[c], crown[d]))) < 0.25f) continue;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
            for (int side = 0; side < 4; side++) for (int k = 0; k < n - 1; k++)
            {
                int a = side == 0 ? k * n : side == 1 ? (n - 1) * n + k : side == 2 ? (n - 1 - k) * n + n - 1 : n - 1 - k;
                int b = side == 0 ? a + n : side == 1 ? a + 1 : side == 2 ? a - n : a - 1;
                if (Mathf.Max(crown[a], crown[b]) < 0.25f) continue;
                int c = vertices.Count;
                vertices.Add(vertices[a] - Vector3.up * 32f); vertices.Add(vertices[b] - Vector3.up * 32f);
                normals.Add(normals[a]); normals.Add(normals[b]); uv.Add(uv[a]); uv.Add(uv[b]);
                ground.Add(ground[a]); ground.Add(ground[b]); crown.Add(crown[a]); crown.Add(crown[b]);
                triangles.Add(a); triangles.Add(c); triangles.Add(b); triangles.Add(b); triangles.Add(c); triangles.Add(c + 1);
            }
            data.vertices = vertices.ToArray(); data.normals = normals.ToArray(); data.uv = uv.ToArray();
            data.ground = ground.ToArray(); data.crown = crown.ToArray(); data.triangles = triangles.ToArray();
            if (triangles.Count == 0) yield break;
            data.textureSize = size; data.pixels = new Color32[size * size];
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                Vector2 p = data.corner + new Vector2((x - 1.5f) * 256f, (z - 1.5f) * 256f);
                data.pixels[z * size + x] = ForestPalette.Surface(SectorForest.At(p.x, p.y), p.x, p.y, 256f);
                if ((x & 3) == 3) yield return null;
            }
        }

        // Find the largest connected forest district, then its densest neighbourhood for the autotest shot.
        public static Vector2 LargestForest()
        {
            int n = WorldPlan.CellCount;
            var seen = new bool[n * n];
            var queue = new Queue<int>();
            float largest = -1f; int chosen = 0;
            for (int start = 0; start < seen.Length; start++)
            {
                if (seen[start] || WorldPlan.Cell(start % n, start / n).forest < 0.3f) continue;
                seen[start] = true; queue.Enqueue(start);
                float area = 0f, best = -1f; int peak = start;
                while (queue.Count > 0)
                {
                    int cell = queue.Dequeue(), i = cell % n, j = cell / n;
                    area += WorldPlan.Cell(i, j).forest;
                    float score = WorldPlan.Cell(i, j).forest * 4f;
                    for (int z = -1; z <= 1; z++)
                        for (int x = -1; x <= 1; x++)
                            if (i + x >= 0 && j + z >= 0 && i + x < n && j + z < n) score += WorldPlan.Cell(i + x, j + z).forest;
                    if (score > best || (score == best && cell < peak)) { best = score; peak = cell; }
                    for (int side = 0; side < 4; side++)
                    {
                        int x = i + (side == 0 ? -1 : side == 1 ? 1 : 0), z = j + (side == 2 ? -1 : side == 3 ? 1 : 0);
                        if (x < 0 || z < 0 || x >= n || z >= n) continue;
                        int next = z * n + x;
                        if (seen[next] || WorldPlan.Cell(x, z).forest < 0.3f) continue;
                        seen[next] = true; queue.Enqueue(next);
                    }
                }
                if (area > largest) { largest = area; chosen = peak; }
            }
            return new Vector2(-WorldPlan.Half + (chosen % n + 0.5f) * WorldPlan.CellSize,
                -WorldPlan.Half + (chosen / n + 0.5f) * WorldPlan.CellSize);
        }
    }
}

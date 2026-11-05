using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Opaque, lit woodland masses in 2 km blocks. The base mesh samples at 16 m; distant LODs use 64/128 m.
    // Generation is sliced between rows on the main thread, so changing WorldPlan cannot race a worker.
    public sealed class ForestHlod
    {
        public const int BlockSize = 2048;
        const int BlockCount = 28;
        const double BuildMilliseconds = 0.7;

        // The same plain mesh data goes to Unity and the offline harness; no substitute forest recipe.
        public sealed class Data
        {
            public Vector2 corner;
            public int step, textureSize;
            public Vector3[] vertices, normals;
            public Vector2[] uv;
            public float[] ground, crown;
            public int[] triangles;
            public Color32[] pixels;
        }

        // Cached resources own their CPU vertex copy only while a nearby camera can open a tree bubble.
        sealed class Block
        {
            public Data data;
            public Mesh mesh;
            public Material material;
            public Texture2D texture;
            public Bounds bounds;
            public Vector3[] moved;
            public Vector3 lastCamera = Vector3.one * 1e8f;
            public int lastSeen;
            public bool hadBubble;
        }

        readonly Dictionary<int, Block> blocks = new Dictionary<int, Block>();
        readonly List<int> stale = new List<int>();
        readonly Stopwatch timer = new Stopwatch();
        IEnumerator builder;
        Data pending;
        int pendingKey, frame;
        byte[] quilt;
        public static int BlocksDrawn { get; private set; }
        public static int TrianglesDrawn { get; private set; }
        public static int CachedBlocks { get; private set; }
        public static float BuildMs { get; private set; }

        // Destroy all native allocations as well as a partially built block when the terrain origin changes.
        public void Clear()
        {
            foreach (var block in blocks.Values) Discard(block);
            blocks.Clear(); builder = null; pending = null;
            BlocksDrawn = TrianglesDrawn = CachedBlocks = 0;
        }

        // Each cached block owns exactly these three Unity resources.
        static void Discard(Block block)
        {
            Art.Discard(block.mesh); Art.Discard(block.material); Art.Discard(block.texture);
        }

        // Select by distance to the block's footprint, including altitude, rather than its distant centre.
        public static int Step(Vector3 camera, Vector2 corner)
        {
            float x = Mathf.Clamp(camera.x, corner.x, corner.x + BlockSize);
            float z = Mathf.Clamp(camera.z, corner.y, corner.y + BlockSize);
            float d = Vector3.Distance(camera, new Vector3(x, WorldPlan.Height(x, z) + 16f, z));
            return d < 3200f ? 16 : d < 8500f ? 64 : 128;
        }

        // Draw only camera-visible blocks and spend a bounded slice filling the nearest missing coverage first.
        public void Update(Camera camera, Plane[] planes)
        {
            BlocksDrawn = TrianglesDrawn = 0; frame++;
            if (!FarTerrain.ForestCanopyHlod) return;
            if (quilt == null)
            {
                var asset = Resources.Load<TextAsset>("Textures/ForestCrown");
                quilt = asset != null ? asset.bytes : new byte[0];
            }
            Vector3 local = camera.transform.position;
            Vector3 world = local + new Vector3(FarTerrain.Origin.x, 0f, FarTerrain.Origin.y);
            int wantedKey = -1, wantedStep = 128;
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
                        if (block.mesh != null && GeometryUtility.TestPlanesAABB(planes, block.bounds)) Draw(block, camera);
                        if (block.data.step == step) continue;
                        // First give every visible block a coarse shape before refining any of them.
                        if (block.bounds.SqrDistance(local) > 1400f * 1400f) distance += 2e9f;
                    }
                    if (builder == null && distance < best)
                    {
                        best = distance; wantedKey = key;
                        wantedStep = block == null ? 128 : step;
                    }
                }
            if (builder == null && wantedKey >= 0)
            {
                pendingKey = wantedKey;
                pending = new Data { corner = new Vector2(-WorldPlan.Half + wantedKey % BlockCount * BlockSize,
                    -WorldPlan.Half + wantedKey / BlockCount * BlockSize), step = wantedStep };
                builder = Build(pending, quilt);
            }
            timer.Restart();
            while (builder != null && timer.Elapsed.TotalMilliseconds < BuildMilliseconds)
                if (!builder.MoveNext())
                {
                    if (blocks.TryGetValue(pendingKey, out Block old)) Discard(old);
                    blocks[pendingKey] = Upload(pending);
                    builder = null; pending = null;
                }
            BuildMs = (float)timer.Elapsed.TotalMilliseconds;
            // Retain a short look-behind cache, but bound native memory after a long flight or camera tour.
            stale.Clear();
            foreach (var pair in blocks) if (frame - pair.Value.lastSeen > 180) stale.Add(pair.Key);
            foreach (int key in stale) { Discard(blocks[key]); blocks.Remove(key); }
            CachedBlocks = blocks.Count;
        }

        // Upload once; shared HDRP Lit supplies real lighting from the low-frequency surface normals.
        Block Upload(Data data)
        {
            var block = new Block { data = data, lastSeen = frame };
            if (data.triangles.Length == 0) { data.pixels = null; return block; }
            var mesh = new Mesh { name = "Forest canopy " + data.step };
            mesh.vertices = data.vertices; mesh.normals = data.normals; mesh.uv = data.uv;
            mesh.triangles = data.triangles; mesh.RecalculateBounds();
            if (data.step == 16) mesh.MarkDynamic();
            var texture = new Texture2D(data.textureSize, data.textureSize, TextureFormat.RGBA32, true)
                { name = "Forest crown quilt", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            texture.SetPixels32(data.pixels); texture.Apply(true, true);
            var material = Art.NewMaterial(Color.white);
            material.mainTexture = texture; material.SetFloat("_Smoothness", 0.12f);
            block.mesh = mesh; block.material = material; block.texture = texture; block.bounds = mesh.bounds;
            data.pixels = null;
            return block;
        }

        // Sink the near canopy beneath trees over 560-700 m, including coarse blocks awaiting refinement.
        void Draw(Block block, Camera camera)
        {
            Vector3 position = camera.transform.position;
            bool bubble = block.bounds.SqrDistance(position) < 900f * 900f;
            if ((bubble || block.hadBubble) && (position - block.lastCamera).sqrMagnitude > 16f * 16f)
            {
                var data = block.data;
                if (block.moved == null) block.moved = new Vector3[data.vertices.Length];
                for (int i = 0; i < data.vertices.Length; i++)
                {
                    Vector3 p = data.vertices[i];
                    float distance = Vector3.Distance(position, p);
                    float blend = Mathf.SmoothStep(0f, 1f, (distance - 560f) / 140f);
                    p.y = data.ground[i] - 2f + (data.crown[i] + 1.7f) * blend;
                    block.moved[i] = p;
                }
                block.mesh.vertices = block.moved;
                block.lastCamera = position;
                block.hadBubble = bubble;
            }
            var rp = new RenderParams(block.material) { camera = camera, worldBounds = block.bounds,
                shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
            Graphics.RenderMesh(rp, block.mesh, 0, Matrix4x4.identity);
            BlocksDrawn++; TrianglesDrawn += block.data.triangles.Length / 3;
        }

        // Build one bounded row at a time. Ground, glades, age, texture and normals all use the shared sampler.
        public static IEnumerator Build(Data data, byte[] quilt)
        {
            int q = BlockSize / data.step, n = q + 1, count = n * n;
            int total = count + n * 4;
            data.vertices = new Vector3[total]; data.normals = new Vector3[total]; data.uv = new Vector2[total];
            data.ground = new float[total]; data.crown = new float[total];
            var colours = new Color[count];
            var triangles = new List<int>();
            for (int j = 0; j <= q; j++)
            {
                for (int i = 0; i <= q; i++)
                {
                    int k = j * n + i;
                    Vector2 world = data.corner + new Vector2(i * data.step, j * data.step);
                    // Break the sampling lattice inside a block; shared boundaries stay on the same plane.
                    int gx = Mathf.FloorToInt(world.x / 16f), gz = Mathf.FloorToInt(world.y / 16f);
                    if (i > 0 && i < q) world.x += (ForestPattern.Unit(gx, gz, 85) - 0.5f) * 6f;
                    if (j > 0 && j < q) world.y += (ForestPattern.Unit(gx, gz, 86) - 0.5f) * 6f;
                    Vector2 p = world - FarTerrain.Origin;
                    var stand = ForestPattern.At(world.x, world.y);
                    float crown = ForestPattern.Canopy(stand, world.x, world.y);
                    if (FarScatter.InSector(p)) crown = 0f;
                    float ground = FarScatter.GroundAt(p);
                    data.ground[k] = ground; data.crown[k] = crown;
                    data.vertices[k] = new Vector3(p.x, ground + crown - 0.3f, p.y);
                    data.uv[k] = new Vector2(i / (float)q, j / (float)q);
                    colours[k] = Color.Lerp(ForestPalette.Ground, ForestPalette.Linear(stand), Mathf.SmoothStep(0f, 1f, stand.cover));
                }
                yield return null;
            }
            for (int j = 0; j <= q; j++)
            {
                for (int i = 0; i <= q; i++)
                {
                    int k = j * n + i;
                    Vector3 dx = data.vertices[j * n + Mathf.Min(q, i + 1)] - data.vertices[j * n + Mathf.Max(0, i - 1)];
                    Vector3 dz = data.vertices[Mathf.Min(q, j + 1) * n + i] - data.vertices[Mathf.Max(0, j - 1) * n + i];
                    data.normals[k] = Vector3.Cross(dz, dx).normalized;
                    if (i == q || j == q) continue;
                    if (Mathf.Max(Mathf.Max(data.crown[k], data.crown[k + 1]), Mathf.Max(data.crown[k + n], data.crown[k + n + 1])) < 0.5f) continue;
                    triangles.Add(k); triangles.Add(k + n); triangles.Add(k + 1);
                    triangles.Add(k + 1); triangles.Add(k + n); triangles.Add(k + n + 1);
                }
                yield return null;
            }
            // Boundary skirts reach the actual ground, hiding height differences between mesh LODs.
            for (int side = 0; side < 4; side++)
                for (int i = 0; i <= q; i++)
                {
                    int edge = side == 0 ? i : side == 1 ? i * n + q : side == 2 ? q * n + q - i : (q - i) * n;
                    int k = count + side * n + i;
                    data.vertices[k] = data.vertices[edge]; data.vertices[k].y = data.ground[edge] - 4f;
                    data.ground[k] = data.ground[edge] - 4f; data.crown[k] = 0f;
                    data.normals[k] = data.normals[edge]; data.uv[k] = data.uv[edge];
                    if (i == 0) continue;
                    int previous = side == 0 ? edge - 1 : side == 1 ? edge - n : side == 2 ? edge + 1 : edge + n;
                    if (data.crown[edge] < 0.5f && data.crown[previous] < 0.5f) continue;
                    triangles.Add(previous); triangles.Add(edge); triangles.Add(k);
                    triangles.Add(previous); triangles.Add(k); triangles.Add(k - 1);
                }
            data.triangles = triangles.ToArray();
            if (triangles.Count == 0) yield break;
            int size = data.textureSize = q * 4;
            data.pixels = new Color32[size * size];
            var filtered = new float[256];
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    float sum = 0f;
                    for (int j = 0; j < 8; j++)
                        for (int i = 0; i < 8; i++) sum += quilt != null && quilt.Length == 16384 ? quilt[(z * 8 + j) * 128 + x * 8 + i] : 128f;
                    filtered[z * 16 + x] = sum / (64f * 128f);
                }
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    // Pixel centres map through the mesh UVs, with the same colour on shared block edges.
                    float u = (i + 0.5f) * q / size, v = (j + 0.5f) * q / size;
                    int x = Mathf.Min(q - 1, (int)u), z = Mathf.Min(q - 1, (int)v), k = z * n + x;
                    Color c = Color.Lerp(Color.Lerp(colours[k], colours[k + 1], u - x),
                        Color.Lerp(colours[k + n], colours[k + n + 1], u - x), v - z);
                    float wx = data.corner.x + (i + 0.5f) * BlockSize / size;
                    float wz = data.corner.y + (j + 0.5f) * BlockSize / size;
                    // Average the half-metre photo samples over this four-metre texel, preserving luminance.
                    // Continuous warping removes the quilt's visible repeat without introducing tile seams.
                    float tx = wx + 18f * (ForestPattern.Noise(wx, wz, 47f, 81) - 0.5f);
                    float tz = wz + 18f * (ForestPattern.Noise(wx, wz, 59f, 82) - 0.5f);
                    float detail = filtered[(Mathf.FloorToInt(tz / 4f) & 15) * 16 + (Mathf.FloorToInt(tx / 4f) & 15)];
                    // At macro LODs use the quilt's mean, avoiding moire from sampling tiny crowns sparsely.
                    if (data.step > 16) detail = 1f;
                    c *= detail; c.a = 1f; data.pixels[j * size + i] = c.gamma;
                }
                yield return null;
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

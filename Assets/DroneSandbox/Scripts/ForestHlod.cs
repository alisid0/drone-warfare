using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Lit woodland masses in 2 km blocks. Sector patches sample at 16 m, margins at 8 m; distant LODs use 64/128 m.
    // Generation is sliced between small sampling batches on the main thread, so changing WorldPlan cannot race a worker.
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
        int pendingKey, frame;
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
            prepared.Clear();
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
            return d < 3200f ? 16 : d < 8500f ? 64 : 128;
        }

        // Draw only camera-visible blocks and spend a bounded slice filling the nearest missing coverage first.
        public void Update(Camera camera, Plane[] planes)
        {
            BlocksDrawn = TrianglesDrawn = 0; frame++;
            if (!FarTerrain.ForestCanopyHlod) return;
            if (!WorldPlan.Ready)
            {
                foreach (var block in blocks.Values)
                    if ((block.mesh != null || block.previous != null) && GeometryUtility.TestPlanesAABB(planes, block.bounds)) Draw(block, camera);
                return;
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
                        if ((block.mesh != null || block.previous != null) && GeometryUtility.TestPlanesAABB(planes, block.bounds)) Draw(block, camera);
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
                builder = Build(pending, null);
            }
            timer.Restart();
            while (builder != null && timer.Elapsed.TotalMilliseconds < BuildMilliseconds)
                if (!builder.MoveNext())
                {
                    var next = Upload(pending);
                    if (blocks.TryGetValue(pendingKey, out Block old))
                    {
                        if (old.previous != null) { Discard(old.previous); old.previous = null; }
                        next.previous = old; next.changedAt = Time.unscaledTime;
                        if (next.mesh == null) next.bounds = old.bounds;
                    }
                    blocks[pendingKey] = next;
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
            if (data.triangles.Length == 0)
            {
                data.vertices = data.normals = null; data.uv = null;
                data.ground = data.crown = null; data.triangles = null; data.pixels = null;
                return block;
            }
            var mesh = new Mesh { name = "Forest canopy " + data.step };
            mesh.indexFormat = data.vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = data.vertices; mesh.normals = data.normals; mesh.uv = data.uv;
            mesh.triangles = data.triangles; mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            var texture = new Texture2D(data.textureSize, data.textureSize, TextureFormat.RGBA32, true)
                { name = "Forest crown field", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            texture.SetPixels32(data.pixels); texture.Apply(true, true);
            var source = Resources.Load<Material>("Materials/Forest/Canopy");
            var material = source != null ? new Material(source) : Art.NewMaterial(Color.white);
            material.shader = Shader.Find("DroneSandbox/ForestLit"); material.SetFloat("_ForestTier", 3f);
            material.mainTexture = texture; material.SetFloat("_Smoothness", 0.12f);
            block.mesh = mesh; block.material = material; block.texture = texture; block.bounds = mesh.bounds;
            block.triangles = data.triangles.Length / 3;
            data.vertices = data.normals = null; data.uv = null;
            data.ground = data.crown = null; data.triangles = null;
            data.pixels = null;
            return block;
        }

        // Fine canopy patches cover 350-700 m and continue into HLOD with identical geometry at that boundary.
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
            properties.SetVector("_ForestCamera", camera.transform.position);
            properties.SetVector("_ForestDistances", FarScatter.TierDistances);
            properties.SetFloat("_ForestBuild", fade);
            var rp = new RenderParams(block.material) { camera = camera, worldBounds = block.bounds,
                matProps = properties, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
            Graphics.RenderMesh(rp, block.mesh, 0, Matrix4x4.identity);
            BlocksDrawn++; TrianglesDrawn += block.triangles;
        }

        // Reuse world-aligned vertices; fine outline samples are shared even across differently refined cells.
        sealed class Surface
        {
            readonly Data data;
            readonly Dictionary<long, int> indices = new Dictionary<long, int>();
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<float> ground = new List<float>(), crown = new List<float>();
            public readonly List<int> triangles = new List<int>();

            // One builder owns temporary lists only until the upload arrays are complete.
            public Surface(Data target) { data = target; }

            // Sample true edge cover at eight metres instead of interpolating a 32 m categorical outline.
            public int Vertex(int x, int z)
            {
                long key = ((long)x << 32) ^ (uint)z;
                if (indices.TryGetValue(key, out int index)) return index;
                Vector2 world = data.corner + new Vector2(x, z), p = world - FarTerrain.Origin;
                var stand = SectorForest.At(world.x, world.y);
                float height = Relief(stand, world.x, world.y);
                float floor = FarScatter.GroundAt(p);
                index = vertices.Count; indices.Add(key, index);
                vertices.Add(new Vector3(p.x, floor + height - 0.3f, p.y));
                ground.Add(floor); crown.Add(height);
                float padding = data.step == 16 ? 8f : data.step * 0.5f;
                uv.Add(new Vector2((x + padding) / (BlockSize + padding * 2f), (z + padding) / (BlockSize + padding * 2f)));
                // World-space gradients agree on block boundaries; stand-scale relief avoids triangle-sized bumps.
                const float d = 8f;
                float dx = Height(world.x + d, world.y) - Height(world.x - d, world.y);
                float dz = Height(world.x, world.y + d) - Height(world.x, world.y - d);
                normals.Add(new Vector3(-dx, 2f * d, -dz).normalized);
                return index;
            }

            // Use the same bare-ground provider for normals, mesh vertices and individual trees.
            float Height(float x, float z)
            {
                Vector2 local = new Vector2(x, z) - FarTerrain.Origin;
                var stand = SectorForest.At(x, z);
                return FarScatter.GroundAt(local) + Relief(stand, x, z);
            }

            // Distant geometry averages whole stands; sampling individual glades would make distant dots again.
            float Relief(ForestPattern.Stand stand, float x, float z)
            {
                if (data.step == 16) return ForestPattern.Canopy(stand, x, z) - (1f - stand.cover) * 1.7f;
                float height = 0f;
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        float sx = x + i * data.step * 0.5f, sz = z + j * data.step * 0.5f;
                        var sample = SectorForest.At(sx, sz);
                        height += ForestPattern.Canopy(sample, sx, sz);
                    }
                float edge = SectorForest.At(x, z).cover;
                return height / 9f * edge - (1f - edge) * 1.7f;
            }
            // Entirely empty triangles stay absent; marginal vertices sink through the terrain at zero cover.
            public void Face(int a, int b, int c)
            {
                if (Mathf.Max(crown[a], Mathf.Max(crown[b], crown[c])) < 0.25f) return;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            // Block skirts only hide mixed-resolution cracks; forest outlines taper through their own vertices.
            public IEnumerator Skirt(int x, int z, int dx, int dz, int step)
            {
                int previous = -1, bottom = -1;
                for (int i = 0; i <= BlockSize; i += step)
                {
                    long key = ((long)(x + dx * i) << 32) ^ (uint)(z + dz * i);
                    if (!indices.TryGetValue(key, out int top)) continue;
                    int k = vertices.Count;
                    var p = vertices[top]; p.y = ground[top] - 4f;
                    vertices.Add(p); normals.Add(normals[top]); uv.Add(uv[top]); ground.Add(p.y); crown.Add(0f);
                    if (previous >= 0) { Face(previous, top, k); Face(previous, k, bottom); }
                    previous = top; bottom = k;
                    if ((i / step & 7) == 0) yield return null;
                }
            }

            // Release construction dictionaries/lists after copying the immutable surface once.
            public void Finish()
            {
                data.vertices = vertices.ToArray(); data.normals = normals.ToArray(); data.uv = uv.ToArray();
                data.ground = ground.ToArray(); data.crown = crown.ToArray(); data.triangles = triangles.ToArray();
            }
        }

        // Reserve eight-metre samples for external edges and sixteen-metre samples for irregular glade rims.
        static int Spacing(Vector2 p)
        {
            float lo = 1f, hi = 0f, gladeLo = 1f, gladeHi = 0f;
            for (int j = 0; j <= 2; j++)
                for (int i = 0; i <= 2; i++)
                {
                    float x = p.x + i * 16, z = p.y + j * 16;
                    float edge = SectorForest.At(x, z).cover, glade = ForestPattern.Glade(x, z);
                    lo = Mathf.Min(lo, edge); hi = Mathf.Max(hi, edge);
                    gladeLo = Mathf.Min(gladeLo, glade); gladeHi = Mathf.Max(gladeHi, glade);
                }
            if (hi > 0.01f && lo < 0.98f && hi - lo > 0.12f) return 8;
            if (hi > 0.01f && SectorForest.Contains(p - SectorForest.Origin)) return 16;
            return hi > 0.01f && gladeHi - gladeLo > 0.12f ? 16 : 32;
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
        // Coarse interiors and stitched eight-metre margins save triangles for silhouettes, not empty ground.
        public static IEnumerator Build(Data data, byte[] unused)
        {
            bool fine = data.step == 16;
            int step = fine ? 32 : data.step, q = BlockSize / step;
            var surface = new Surface(data);
            var spacing = new int[(q + 2) * (q + 2)];
            for (int j = -1; j <= q; j++)
                for (int i = -1; i <= q; i++)
                {
                    spacing[(j + 1) * (q + 2) + i + 1] = fine ? Spacing(data.corner + new Vector2(i * step, j * step)) : step;
                    if ((i & 7) == 0) yield return null;
                }
            for (int j = 0; j < q; j++)
                for (int i = 0; i < q; i++)
                {
                    int x = i * step, z = j * step, key = (j + 1) * (q + 2) + i + 1, stride = spacing[key];
                    for (int dz = 0; dz < step; dz += stride)
                        for (int dx = 0; dx < step; dx += stride)
                            Quad(surface, x + dx, z + dz, stride,
                                dz == 0 ? spacing[key - q - 2] : stride, dz + stride == step ? spacing[key + q + 2] : stride,
                                dx == 0 ? spacing[key - 1] : stride, dx + stride == step ? spacing[key + 1] : stride);
                    yield return null;
                }            int skirtStep = fine ? 8 : step;
            var skirt = surface.Skirt(0, 0, 1, 0, skirtStep); while (skirt.MoveNext()) yield return null;
            skirt = surface.Skirt(BlockSize, 0, 0, 1, skirtStep); while (skirt.MoveNext()) yield return null;
            skirt = surface.Skirt(BlockSize, BlockSize, -1, 0, skirtStep); while (skirt.MoveNext()) yield return null;
            skirt = surface.Skirt(0, BlockSize, 0, -1, skirtStep); while (skirt.MoveNext()) yield return null;
            surface.Finish();
            if (data.triangles.Length == 0) yield break;
            int core = fine ? 512 : q * 4, size = data.textureSize = core + 4;
            float pixelSize = BlockSize / (float)core;
            Vector2 pictureCorner = data.corner - Vector2.one * (2f * pixelSize);
            var pixels = new Color[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float wx = pictureCorner.x + (i + 0.5f) * pixelSize, wz = pictureCorner.y + (j + 0.5f) * pixelSize;
                    if (fine)
                        pixels[j * size + i] = ForestPalette.Ground * (0.94f + 0.12f * ForestPattern.Noise(wx, wz, 29f, 83));
                    else
                    {
                        // Area-filter broad stands; never sample isolated crown pictures at macro distances.
                        Color c = new Color(0f, 0f, 0f);
                        for (int s = 0; s < 4; s++)
                        {
                            float sx = wx + ((s & 1) - 0.5f) * data.step, sz = wz + ((s >> 1) - 0.5f) * data.step;
                            var stand = SectorForest.At(sx, sz);
                            stand.radius = 4.6f;
                            Color sample = Color.Lerp(ForestPalette.Ground, ForestPalette.Linear(stand), stand.cover);
                            c = Add(c, sample * 0.25f);
                        }
                        pixels[j * size + i] = c;
                    }
                    if ((i & 31) == 0) yield return null;
                }
            if (fine)
            {
                int x0 = Mathf.FloorToInt((data.corner.x - 16f) / ForestPattern.TreeStep), z0 = Mathf.FloorToInt((data.corner.y - 16f) / ForestPattern.TreeStep);
                int x1 = Mathf.CeilToInt((data.corner.x + BlockSize + 16f) / ForestPattern.TreeStep), z1 = Mathf.CeilToInt((data.corner.y + BlockSize + 16f) / ForestPattern.TreeStep);
                var tops = new float[pixels.Length];
                for (int j = z0; j <= z1; j++)
                    for (int i = x0; i <= x1; i++)
                    {
                        if (SectorForest.Tree(i, j, out Vector2 world, out var stand))
                            Stamp(pixels, tops, size, pictureCorner, world, stand, i, j);
                        if ((i & 7) == 0) yield return null;
                    }
            }
            data.pixels = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                int x = i % size, z = i / size;
                Color c = pixels[i];
                if (fine)
                {
                    // A small area filter keeps crown relief from turning into high-contrast pinpricks at 2.5 km.
                    c = Add(c * 0.5f, Add(Add(pixels[z * size + Mathf.Max(0, x - 1)], pixels[z * size + Mathf.Min(size - 1, x + 1)]),
                        Add(pixels[Mathf.Max(0, z - 1) * size + x], pixels[Mathf.Min(size - 1, z + 1) * size + x])) * 0.125f) * 0.9f;
                }
                float wx = pictureCorner.x + (x + 0.5f) * pixelSize, wz = pictureCorner.y + (z + 0.5f) * pixelSize;
                float edge = SectorForest.At(wx, wz).cover;
                if (edge < 0.95f) c = Color.Lerp(FarTerrain.CanopyGroundColour(wx, wz), c, Mathf.SmoothStep(0f, 1f, edge / 0.65f));
                c.a = 1f; data.pixels[i] = c.gamma;
                if ((i & 255) == 0) yield return null;
            }
        }

        // Avoid changing colour-space or alpha while accumulating filtered linear-light samples.
        static Color Add(Color a, Color b) => new Color(a.r + b.r, a.g + b.g, a.b + b.b);

        // Stamp each shared tree with its own yaw, crop offset and photo variant; no repeating forest quilt.
        static void Stamp(Color[] pixels, float[] tops, int size, Vector2 corner, Vector2 world, ForestPattern.Stand stand, int i, int j)
        {
            float pixel = BlockSize / (float)(size - 4), radius = stand.radius * 1.18f;
            float angle = ForestPattern.Unit(i, j, 44) * Mathf.PI * 2f, ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            int variant = ForestPattern.Hash(i, j, 84) % 3;
            float ox = (ForestPattern.Unit(i, j, 85) - 0.5f) * 0.08f, oz = (ForestPattern.Unit(i, j, 86) - 0.5f) * 0.08f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((world.x - radius - corner.x) / pixel)), x1 = Mathf.Min(size - 1, Mathf.FloorToInt((world.x + radius - corner.x) / pixel));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((world.y - radius - corner.y) / pixel)), z1 = Mathf.Min(size - 1, Mathf.FloorToInt((world.y + radius - corner.y) / pixel));
            Color crown = ForestPalette.Crown(stand) * 1.16f;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float alpha = 0f, light = 0f;
                    // Four sub-pixel taps retain small saplings without a lattice of single bright texels.
                    for (int s = 0; s < 4; s++)
                    {
                        float dx = corner.x + (x + 0.25f + (s & 1) * 0.5f) * pixel - world.x;
                        float dz = corner.y + (z + 0.25f + (s >> 1) * 0.5f) * pixel - world.y;
                        float u = (ca * dx - sa * dz) / (radius * 2f) + 0.5f + ox, v = (sa * dx + ca * dz) / (radius * 2f) + 0.5f + oz;
                        float lum = ForestPalette.Picture(stand.species, variant, u, v, out float a);
                        alpha += a * 0.25f; light += lum * a * 0.25f;
                    }
                    int k = z * size + x;
                    if (alpha < 0.01f || tops[k] > stand.height) continue;
                    pixels[k] = Color.Lerp(pixels[k], crown * (light / alpha), alpha);
                    if (alpha > 0.5f) tops[k] = stand.height;
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

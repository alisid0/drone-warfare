using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Immutable world inputs keep tile workers independent of Unity and of the next sector/world rebuild.
    public sealed partial class FarDetailData
    {
        public const int Quads = 128, Samples = Quads + 1, Pixels = 256, TextureSize = Pixels + 2;
        public const float Size = 512f, Step = 4f;
        public readonly int seed;
        readonly float[] heights;
        readonly Cover[] covers;
        readonly int count;
        readonly Segment[] roads, rivers;
        readonly Dictionary<long, Segment[]> riverCells = new Dictionary<long, Segment[]>();
        public readonly FarFieldPlan fields;
        public readonly FarDetailCover cover;
        public readonly float[][] coarse;

        // A vector segment carries real widths, avoiding the world's deliberately exaggerated road pixels.
        public struct Segment
        {
            public float ax, az, bx, bz, widthA, widthB, walked;
            public int kind, route;
        }

        // Worker output contains only arrays and value types; the streamer owns all engine resources.
        public sealed class Tile
        {
            public int x, z;
            public Vector3[] vertices, normals, collision;
            public Vector2[] uv;
            public int[] triangles, collisionTriangles;
            public Color32[] pixels, controls;
            public Vector4[] tangents;
            public double generationMs;
        }

        // Called during world setup, before streaming starts; the native noise table is sampled on the main thread.
        public FarDetailData(int seed, float[] heights, Cover[] covers, int count, Segment[] roads, Segment[] rivers, float[][] coarse)
        {
            this.seed = seed; this.heights = heights; this.covers = covers; this.count = count;
            this.roads = roads; this.rivers = rivers; this.coarse = coarse;
            cover = new FarDetailCover(seed, covers, count);
            fields = new FarFieldPlan(seed, roads, cover, Height);
            var cells = new Dictionary<long, List<Segment>>();
            foreach (var s in rivers)
            {
                float pad = Math.Max(s.widthA, s.widthB) + 5f;
                int x0 = (int)Math.Floor((Math.Min(s.ax, s.bx) - pad) / Size), x1 = (int)Math.Floor((Math.Max(s.ax, s.bx) + pad) / Size);
                int z0 = (int)Math.Floor((Math.Min(s.az, s.bz) - pad) / Size), z1 = (int)Math.Floor((Math.Max(s.az, s.bz) + pad) / Size);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                    {
                        long key = ((long)x << 32) ^ (uint)z;
                        if (!cells.TryGetValue(key, out var list)) { list = new List<Segment>(); cells.Add(key, list); }
                        list.Add(s);
                    }
            }
            foreach (var pair in cells) riverCells.Add(pair.Key, pair.Value.ToArray());
        }

        // Bilinear sampling matches WorldPlan.Height, including its closed north/east boundary.
        public float Height(float x, float z)
        {
            float gx = Clamp((x + WorldPlan.Half) / 32f, 0f, count - 1), gz = Clamp((z + WorldPlan.Half) / 32f, 0f, count - 1);
            int i = Math.Min((int)gx, count - 2), j = Math.Min((int)gz, count - 2), k = j * count + i;
            return Mix(Mix(heights[k], heights[k + 1], gx - i), Mix(heights[k + count], heights[k + count + 1], gx - i), gz - j);
        }

        // The same nearest cover sample as the macro picture, without any native engine calls.
        public Cover CoverAt(float x, float z)
        {
            int i = (int)Clamp((float)Math.Floor((x + WorldPlan.Half) / 32f + 0.5f), 0, count - 1);
            int j = (int)Clamp((float)Math.Floor((z + WorldPlan.Half) / 32f + 0.5f), 0, count - 1);
            return covers[j * count + i];
        }

        // The actual LOD0 triangle, not a bilinear approximation: the tile's boundary joins this surface.
        public float CoarseHeight(float x, float z)
        {
            float gx = Clamp((x + WorldPlan.Half) / 4000f, 0, 13.999999f), gz = Clamp((z + WorldPlan.Half) / 4000f, 0, 13.999999f);
            int cx = (int)gx, cz = (int)gz;
            var h = coarse[cz * 14 + cx];
            float u = (gx - cx) * 64f, v = (gz - cz) * 64f;
            int i = Math.Min((int)u, 63), j = Math.Min((int)v, 63);
            return Triangle(h[j * 65 + i], h[j * 65 + i + 1], h[(j + 1) * 65 + i], h[(j + 1) * 65 + i + 1], u - i, v - j);
        }

        // Fixed 128 m edge bands meet neighbouring tiles even while their requests finish in a different order.
        public float Surface(float x, float z, float roughness)
        {
            float u = Repeat(x, Size), v = Repeat(z, Size);
            float edge = Math.Min(Math.Min(u, Size - u), Math.Min(v, Size - v));
            return Mix(CoarseHeight(x, z), Height(x, z) + roughness, Smooth(edge / 128f));
        }

        // Exact Ground world-plan roughness; only the main-thread sampler calls Unity's native Perlin implementation.
        public static float Roughness(float x, float z)
        {
            return (Mathf.PerlinNoise(x * 0.024f + 17.3f, z * 0.024f + 63.1f) - 0.5f) * 1.6f
                 + (Mathf.PerlinNoise(x * 0.09f + 5.5f, z * 0.09f + 91.2f) - 0.5f) * 0.6f;
        }

        // Build heights, normals, skirts, collider and pixels entirely as worker-owned managed data.
        public Tile Build(int x, int z, float[] roughness, Vector2[] coverPositions)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int n = Samples, length = n * n + 4 * n;
            var tile = new Tile { x = x, z = z, vertices = new Vector3[length], normals = new Vector3[length], uv = new Vector2[length] };
            float x0 = x * Size, z0 = z * Size;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    float wx = x0 + i * Step, wz = z0 + j * Step;
                    // Water suppresses small roughness just as the sector does. Vector rivers catch sub-grid streams.
                    float detail = Wet(wx, wz) ? 0f : roughness[k];
                    tile.vertices[k] = new Vector3(i * Step, Surface(wx, wz, detail), j * Step);
                    tile.uv[k] = new Vector2(i / (float)Quads, j / (float)Quads);
                }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = Math.Max(0, i - 1), b = Math.Min(Quads, i + 1), c = Math.Max(0, j - 1), d = Math.Min(Quads, j + 1);
                    float dx = (tile.vertices[j * n + b].y - tile.vertices[j * n + a].y) / ((b - a) * Step);
                    float dz = (tile.vertices[d * n + i].y - tile.vertices[c * n + i].y) / ((d - c) * Step);
                    float norm = 1f / (float)Math.Sqrt(dx * dx + dz * dz + 1f);
                    tile.normals[j * n + i] = new Vector3(-dx * norm, norm, -dz * norm);
                }
            var triangles = new List<int>(Quads * Quads * 6 + Quads * 48);
            Grid(triangles, Quads);
            for (int side = 0; side < 4; side++)
                for (int t = 0; t < n; t++)
                {
                    int e = Edge(side, t), k = n * n + side * n + t;
                    Vector3 p = tile.vertices[e]; p.y -= 80f;
                    tile.vertices[k] = p; tile.normals[k] = tile.normals[e]; tile.uv[k] = tile.uv[e];
                    if (t == 0) continue;
                    int prev = Edge(side, t - 1);
                    triangles.Add(prev); triangles.Add(e); triangles.Add(k - 1); triangles.Add(e); triangles.Add(k); triangles.Add(k - 1);
                    triangles.Add(prev); triangles.Add(k - 1); triangles.Add(e); triangles.Add(e); triangles.Add(k - 1); triangles.Add(k);
                }
            tile.triangles = triangles.ToArray();
            tile.collision = new Vector3[33 * 33];
            for (int j = 0; j < 33; j++) for (int i = 0; i < 33; i++) tile.collision[j * 33 + i] = tile.vertices[j * 4 * n + i * 4];
            triangles.Clear(); Grid(triangles, 32); tile.collisionTriangles = triangles.ToArray();
            tile.tangents = new Vector4[length];
            for (int k = 0; k < length; k++)
            {
                Vector3 normal = tile.normals[k];
                float inv = 1f / (float)Math.Sqrt(normal.y * normal.y + normal.x * normal.x);
                tile.tangents[k] = new Vector4(normal.y * inv, -normal.x * inv, 0f, -1f);
            }
            tile.pixels = new Color32[TextureSize * TextureSize];
            tile.controls = new Color32[tile.pixels.Length];
            var localRoads = Nearby(roads, x0, z0); var localRivers = Nearby(rivers, x0, z0);
            for (int j = 0; j < TextureSize; j++) for (int i = 0; i < TextureSize; i++)
                {
                    float wx = x0 + (i - 0.5f) * 2f, wz = z0 + (j - 0.5f) * 2f;
                    int k = j * TextureSize + i;
                    tile.pixels[k] = Paint(wx, wz, new Vector2(wx, wz), localRoads, localRivers);
                    tile.controls[k] = Control(wx, wz, new Vector2(wx, wz), localRoads, localRivers);
                }
            tile.generationMs = timer.Elapsed.TotalMilliseconds;
            return tile;
        }

        // Reuse the exact diagonal used by both meshes, so flight heights are on the visible triangles.
        public static float Triangle(float a, float b, float c, float d, float u, float v)
        { return u + v <= 1f ? a + (b - a) * u + (c - a) * v : d + (c - d) * (1f - u) + (b - d) * (1f - v); }

        // Common grid topology is also used for the deliberately cheaper 16 m collision mesh.
        static void Grid(List<int> indices, int quads)
        {
            int n = quads + 1;
            for (int j = 0; j < quads; j++) for (int i = 0; i < quads; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    indices.Add(a); indices.Add(c); indices.Add(b); indices.Add(b); indices.Add(c); indices.Add(d);
                }
        }

        // The four skirts share their surface vertices' height and texture coordinates.
        static int Edge(int side, int t) => side == 0 ? t : side == 1 ? t * Samples + Quads : side == 2 ? Quads * Samples + t : t * Samples;

        // Cull vector features once per tile, rather than searching the whole theatre per pixel.
        static Segment[] Nearby(Segment[] source, float x, float z, float size = Size)
        {
            var list = new List<Segment>();
            foreach (var s in source)
            {
                float pad = Math.Max(s.widthA, s.widthB) + 80f;
                if (Math.Max(s.ax, s.bx) + pad < x || Math.Min(s.ax, s.bx) - pad > x + size ||
                    Math.Max(s.az, s.bz) + pad < z || Math.Min(s.az, s.bz) - pad > z + size) continue;
                list.Add(s);
            }
            return list.ToArray();
        }

        // River vector widths decide whether the tiny height noise belongs on dry land.
        public bool Wet(float x, float z)
        {
            if (CoverAt(x, z) == Cover.Water) return true;
            long key = ((long)(int)Math.Floor(x / Size) << 32) ^ (uint)(int)Math.Floor(z / Size);
            if (riverCells.TryGetValue(key, out var nearby))
                foreach (var river in nearby) if (Distance(river, x, z, out float width) <= width) return true;
            return false;
        }

        // Vector distance includes an interpolated river half-width at bends and junctions.
        static float Distance(Segment s, float x, float z, out float width)
        {
            float dx = s.bx - s.ax, dz = s.bz - s.az, length = dx * dx + dz * dz;
            float t = length > 0 ? Clamp(((x - s.ax) * dx + (z - s.az) * dz) / length, 0, 1) : 0;
            width = Mix(s.widthA, s.widthB, t); dx = x - s.ax - t * dx; dz = z - s.az - t * dz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // This hash deliberately matches FarTerrain.Field, independently of worker completion order.
        int Hash(int x, int z)
        {
            unchecked { int h = x * 73856093 ^ z * 19349663 ^ seed * 83492791; h ^= h >> 13; h *= 0x5bd1e995; return h & 0x7fffffff; }
        }
        // Managed value noise is only for colour drift; height roughness always comes from Ground's native noise.
        float Noise(float x, float z)
        {
            int i = (int)Math.Floor(x), j = (int)Math.Floor(z); float u = Smooth(x - i), v = Smooth(z - j);
            return Mix(Mix((Hash(i, j) & 65535) / 65535f, (Hash(i + 1, j) & 65535) / 65535f, u), Mix((Hash(i, j + 1) & 65535) / 65535f, (Hash(i + 1, j + 1) & 65535) / 65535f, u), v);
        }
        // Small managed scalar and colour helpers keep the worker path free of native Unity calls.
        public static float Clamp(float x, float a, float b) => Math.Max(a, Math.Min(b, x));
        // Interpolate an already bounded scalar weight.
        public static float Mix(float a, float b, float t) => a + (b - a) * t;
        // Round the transition ends to avoid a visible ridge at the 128 m band.
        static float Smooth(float t) { t = Clamp(t, 0, 1); return t * t * (3f - 2f * t); }
        // Positive fractional coordinates are essential west and south of the world origin.
        static float Repeat(float x, float n) => x - (float)Math.Floor(x / n) * n;
        // Clamp byte colours without relying on engine conversion functions.
        static Color32 C(float r, float g, float b) => new Color32((byte)Clamp(r, 0, 255), (byte)Clamp(g, 0, 255), (byte)Clamp(b, 0, 255), 255);
        // Colour interpolation is also used by the standalone texture harness.
        static Color32 Lerp(Color32 a, Color32 b, float t) => C(Mix(a.r, b.r, t), Mix(a.g, b.g, t), Mix(a.b, b.b, t));
    }

    // Snapshot access lives beside the private grids rather than reaching into a live plan from worker tasks.
    public static partial class WorldPlan
    {
        // World generation has finished when FarTerrain calls this; clone every mutable input once per rebuild.
        internal static FarDetailData CaptureDetail(float[][] coarse)
        {
            var roads = new List<FarDetailData.Segment>(); var rivers = new List<FarDetailData.Segment>();
            int route = 0;
            foreach (var road in Roads)
            {
                float walked = 0f;
                for (int k = 1; k < road.points.Count; k++)
                {
                    float width = road.kind == RoadClass.Highway ? 3.5f : road.kind == RoadClass.Road ? 2f : 1.5f;
                    roads.Add(new FarDetailData.Segment { ax = road.points[k - 1].x, az = road.points[k - 1].y, bx = road.points[k].x, bz = road.points[k].y, widthA = width, widthB = width, kind = (int)road.kind, route = route, walked = walked });
                    walked += Vector2.Distance(road.points[k - 1], road.points[k]);
                }
                route++;
            }
            foreach (var river in Rivers) for (int k = 1; k < river.points.Count; k++)
                    rivers.Add(new FarDetailData.Segment { ax = river.points[k - 1].x, az = river.points[k - 1].y, bx = river.points[k].x, bz = river.points[k].y, widthA = river.halfWidths[k - 1], widthB = river.halfWidths[k] });
            return new FarDetailData(Seed, (float[])heights.Clone(), (Cover[])cover.Clone(), GridCount, roads.ToArray(), rivers.ToArray(), coarse);
        }
    }
}

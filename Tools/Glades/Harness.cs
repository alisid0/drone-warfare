using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Rasterises the production canopy blocks the streamer would request for RTS cameras at 300 m, 1 km and 2.5 km over a
// glade-rich stretch of the largest forest, plus a 512 m top-down view, into per-pixel G-buffers (render.py shades them).
// It also surveys the glades there: share, outline roundness, edge drop widths and the trees standing in openings.
static class Harness
{
    const int Width = 1920, Height = 1080;
    const float Fov = 45f, Yaw = 35f;

    // Keep errors visible to the serial driver without opening Windows crash dialogs.
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }

    // The view target uses only fields this task leaves alone (wood mask and glade presence), so both phases share it.
    static Vector2 Target(Vector2 forest)
    {
        Vector2 best = forest; float score = -1f;
        for (float dz = -4096f; dz <= 4096f; dz += 128f)
            for (float dx = -4096f; dx <= 4096f; dx += 128f)
            {
                Vector2 c = forest + new Vector2(dx, dz);
                float sum = 0f;
                for (int j = -4; j <= 4; j++)
                    for (int i = -4; i <= 4; i++)
                    {
                        float x = c.x + i * 64f, z = c.y + j * 64f;
                        if (ForestPattern.Edge(x, z) < 0.99f) continue;
                        sum += Mathf.SmoothStep(0f, 1f, (ForestPattern.Noise(x, z, 610f, 120) - 0.36f) / 0.12f);
                    }
                if (sum > score + 0.001f) { score = sum; best = c; }
            }
        return best;
    }

    // One RTS LookAt per review distance, each with its own block levels exactly as Update would choose them.
    static void Run(string[] args)
    {
        string output = args[0];
        Directory.CreateDirectory(output);
        WorldPlan.Generate(1200);
        var forest = ForestHlod.LargestForest();
        var target = Target(forest);
        var plan = WorldSectors.PlanAt(forest, SectorKind.DeepForest, 1037);
        MapLayout.Use(plan);
        FarTerrain.Origin = new Vector2(target.x > 0f ? target.x - 9000f : target.x + 9000f, target.y);
        Console.WriteLine($"forest {forest.x},{forest.y}; glade target {target.x},{target.y}");
        int size = ForestHlod.BlockSize;
        var home = new Vector2(Mathf.FloorToInt((target.x + WorldPlan.Half) / size) * size - WorldPlan.Half,
            Mathf.FloorToInt((target.y + WorldPlan.Half) / size) * size - WorldPlan.Half);
        float ground = FarScatter.GroundAt(target - FarTerrain.Origin);
        var views = new List<object>();
        foreach (float distance in new[] { 300f, 1000f, 2500f })
        {
            // RtsController.ApplyCamera: steeper when zoomed out, then 64-72 degrees in the world view.
            float maxZoom = 110f * Scale.Camera;
            float pitch = distance <= maxZoom ? Mathf.Lerp(42f, 64f, Mathf.InverseLerp(15f * Scale.Camera, maxZoom, distance))
                : Mathf.Lerp(64f, 72f, Mathf.InverseLerp(maxZoom, 12000f, distance));
            float p = pitch * Mathf.Deg2Rad, y = Yaw * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Cos(p) * Mathf.Sin(y), -Mathf.Sin(p), Mathf.Cos(p) * Mathf.Cos(y));
            var right = new Vector3(Mathf.Cos(y), 0f, -Mathf.Sin(y));
            var up = new Vector3(Mathf.Sin(p) * Mathf.Sin(y), Mathf.Cos(p), Mathf.Sin(p) * Mathf.Cos(y));
            var focus = new Vector3(target.x, ground, target.y);
            var eye = focus - forward * distance;
            var blocks = Blocks(home, size, eye, out double buildMs);
            string name = "view" + (int)distance;
            int triangles = Raster(blocks, eye, forward, right, up, target, Width, Height, false, Path.Combine(output, name));
            var steps = new List<int>(); int emitted = 0;
            foreach (var b in blocks) { steps.Add(b.step); emitted += b.triangles.Length / 3; }
            views.Add(new { name, distance, pitch, yaw = Yaw, fov = Fov, width = Width, height = Height, ortho = false,
                eye = new[] { eye.x - target.x, eye.y, eye.z - target.y }, forward = new[] { forward.x, forward.y, forward.z },
                right = new[] { right.x, right.y, right.z }, up = new[] { up.x, up.y, up.z }, steps, triangles, emitted,
                buildMs = Math.Round(buildMs, 1), blocks = blocks.Count });
            Console.WriteLine($"{name}: pitch {pitch:F2}, blocks {blocks.Count}, steps {string.Join(",", steps)}, emitted {emitted}, drawn {triangles}, build {buildMs:F0} ms");
        }
        // Straight down over 512 m at half a metre per pixel, from the 16 m tier a low camera gets.
        {
            var eye = new Vector3(target.x, ground + 600f, target.y);
            var blocks = Blocks(home, size, new Vector3(target.x, ground + 300f, target.y), out double buildMs);
            int triangles = Raster(blocks, eye, Vector3.down, Vector3.right, new Vector3(0f, 0f, 1f), target, 1024, 1024, true, Path.Combine(output, "topdown"));
            views.Add(new { name = "topdown", distance = 600f, pitch = 90f, yaw = 0f, fov = 0f, width = 1024, height = 1024, ortho = true,
                eye = new[] { 0f, eye.y, 0f }, forward = new[] { 0f, -1f, 0f }, right = new[] { 1f, 0f, 0f }, up = new[] { 0f, 0f, 1f },
                steps = new List<int>(), triangles, emitted = 0, buildMs = Math.Round(buildMs, 1), blocks = blocks.Count });
        }
        var survey = Survey(target);
        File.WriteAllText(Path.Combine(output, "views.json"), JsonSerializer.Serialize(new { target = new[] { target.x, target.y }, ground, views, survey },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    // The 3x3 blocks round the target at the levels ForestHlod.Step picks for this eye, timed as one build.
    static List<ForestHlod.Data> Blocks(Vector2 home, int size, Vector3 eye, out double milliseconds)
    {
        var blocks = new List<ForestHlod.Data>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                var corner = home + new Vector2(i * size, j * size);
                var data = new ForestHlod.Data { corner = corner, step = ForestHlod.Step(eye, corner), adaptive = true, focus = eye };
                var build = ForestHlod.Build(data, null); while (build.MoveNext()) { }
                if (data.triangles != null && data.triangles.Length > 0) blocks.Add(data);
            }
        milliseconds = watch.Elapsed.TotalMilliseconds;
        return blocks;
    }

    // Glades in the 2 km square round the target, from the production stand recipe at 2 m.
    static object Survey(Vector2 target)
    {
        const int n = 1000; const float cell = 2f;
        Vector2 origin = target - new Vector2(n * cell * 0.5f, n * cell * 0.5f);
        var cover = new float[n * n]; var wood = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                var s = SectorForest.At(origin.x + (i + 0.5f) * cell, origin.y + (j + 0.5f) * cell);
                cover[j * n + i] = s.cover; wood[j * n + i] = s.wood;
            }
        int woods = 0, open = 0, opening = 0;
        for (int k = 0; k < cover.Length; k++)
        {
            if (wood[k] < 0.99f) continue;
            woods++;
            if (cover[k] < 0.05f) open++;
            if (cover[k] < 0.5f) opening++;
        }
        // Outline roundness per opening: 4 pi area / perimeter^2, with the perimeter in 2 m pixel edges times pi/4, so a
        // smooth circle scores about 1 and a lobed, ragged outline much less.
        var seen = new bool[n * n]; var stack = new Stack<int>(); var roundness = new List<float>(); var areas = new List<float>();
        for (int k = 0; k < cover.Length; k++)
        {
            if (seen[k] || wood[k] < 0.99f || cover[k] >= 0.5f) continue;
            int count = 0, edges = 0; bool touches = false; seen[k] = true; stack.Push(k);
            while (stack.Count > 0)
            {
                int c = stack.Pop(), ci = c % n, cj = c / n; count++;
                if (ci == 0 || cj == 0 || ci == n - 1 || cj == n - 1) touches = true;
                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= n || nj >= n) continue;
                    int m = nj * n + ni;
                    if (cover[m] >= 0.5f) { edges++; continue; }
                    if (!seen[m]) { seen[m] = true; stack.Push(m); }
                }
            }
            if (touches || count < 100) continue;
            float area = count * cell * cell, perimeter = edges * cell * Mathf.PI / 4f;
            roundness.Add(4f * Mathf.PI * area / (perimeter * perimeter)); areas.Add(area);
        }
        // Edge drop: walking into the wood from every cover-0.5 crossing on 16 m rows and columns, the distance over
        // which canopy relief climbs from 10% to 90% of the way from the glade floor to the wood 40 m in.
        var widths = new List<float>();
        for (int axis = 0; axis < 2; axis++)
            for (int line = 8; line < n; line += 8)
                for (int t = 1; t < n; t++)
                {
                    int a = axis == 0 ? line * n + t - 1 : (t - 1) * n + line, b = axis == 0 ? line * n + t : t * n + line;
                    if (wood[a] < 0.99f || wood[b] < 0.99f) continue;
                    bool rising = cover[a] < 0.5f && cover[b] >= 0.5f, falling = cover[a] >= 0.5f && cover[b] < 0.5f;
                    if (!rising && !falling) continue;
                    Vector2 start = origin + new Vector2((axis == 0 ? t : line) + 0.5f, (axis == 0 ? line : t) + 0.5f) * cell;
                    Vector2 step = (axis == 0 ? Vector2.right : Vector2.up) * (rising ? 0.5f : -0.5f);
                    Vector2 back = start - step * 24f;
                    float floor = ForestPattern.Canopy(SectorForest.At(back.x, back.y), back.x, back.y);
                    var profile = new float[81];
                    for (int s = 0; s <= 80; s++)
                    {
                        Vector2 q = start + step * (s - 8);
                        profile[s] = ForestPattern.Canopy(SectorForest.At(q.x, q.y), q.x, q.y);
                    }
                    float top = profile[80];
                    if (top - floor < 6f) continue;
                    float low = floor + 0.1f * (top - floor), high = floor + 0.9f * (top - floor), d10 = float.NaN, d90 = float.NaN;
                    for (int s = 0; s <= 80; s++)
                    {
                        if (float.IsNaN(d10) && profile[s] >= low) d10 = s * 0.5f;
                        if (float.IsNaN(d90) && profile[s] >= high) d90 = s * 0.5f;
                    }
                    if (!float.IsNaN(d10) && !float.IsNaN(d90)) widths.Add(d90 - d10);
                }
        // Trees in the central square kilometre: all accepted stems, those standing where the stand is open, and lone
        // trees taller than 10 m with open ground 9 m away on at least three sides.
        int trees = 0, inOpenings = 0, lone = 0;
        int i0 = Mathf.FloorToInt((target.x - 500f) / ForestPattern.TreeStep), j0 = Mathf.FloorToInt((target.y - 500f) / ForestPattern.TreeStep);
        for (int j = j0; j < j0 + 200; j++)
            for (int i = i0; i < i0 + 200; i++)
            {
                if (!ForestPattern.Tree(i, j, out Vector2 world, out var stand)) continue;
                trees++;
                if (ForestPattern.At(world.x, world.y).cover < 0.5f) inOpenings++;
                if (stand.height < 10f) continue;
                int sides = 0;
                for (int d = 0; d < 4; d++)
                {
                    Vector2 q = world + new Vector2(d == 0 ? 9f : d == 1 ? -9f : 0f, d == 2 ? 9f : d == 3 ? -9f : 0f);
                    if (ForestPattern.At(q.x, q.y).cover < 0.5f) sides++;
                }
                if (sides >= 3) lone++;
            }
        roundness.Sort(); areas.Sort(); widths.Sort();
        float Pick(List<float> v, int percent) => v.Count > 0 ? v[Math.Min(v.Count - 1, v.Count * percent / 100)] : 0f;
        int abrupt = 0; foreach (float w in widths) if (w <= 8f) abrupt++;
        var result = new
        {
            woodSamples = woods, gladeOpenOfWoodland = Math.Round(open / (double)Math.Max(1, woods), 4),
            gladeOpeningOfWoodland = Math.Round(opening / (double)Math.Max(1, woods), 4),
            openings = roundness.Count, roundnessP10P50P90 = new[] { Pick(roundness, 10), Pick(roundness, 50), Pick(roundness, 90) },
            areaP50 = Pick(areas, 50),
            edgeCrossings = widths.Count, edgeDropP10P50P90 = new[] { Pick(widths, 10), Pick(widths, 50), Pick(widths, 90) },
            edgeDropWithin8m = Math.Round(abrupt / (double)Math.Max(1, widths.Count), 3),
            trees, treesInOpenings = inOpenings, loneTrees = lone,
        };
        Console.WriteLine("survey " + JsonSerializer.Serialize(result));
        return result;
    }

    // Perspective-correct (or orthographic) z-buffered rasterisation of every block triangle; attributes are what the
    // shader receives.
    static int Raster(List<ForestHlod.Data> blocks, Vector3 eye, Vector3 forward, Vector3 right, Vector3 up, Vector2 target,
        int width, int height, bool ortho, string prefix)
    {
        int n = width * height;
        // x/z relative to the target (absolute world minus target), absolute height, vertex normal, macro colour.
        var channels = new float[12][];
        for (int c = 0; c < channels.Length; c++) { channels[c] = new float[n]; }
        var depth = new float[n];
        for (int k = 0; k < n; k++) { depth[k] = float.MaxValue; channels[0][k] = float.NaN; }
        float tanY = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad), tanX = tanY * width / height;
        const float pixel = 0.5f;
        int drawn = 0;
        foreach (var data in blocks)
        {
            var v = data.vertices; var t = data.triangles;
            int count = v.Length;
            var sx = new float[count]; var sy = new float[count]; var w = new float[count];
            for (int i = 0; i < count; i++)
            {
                var world = new Vector3(v[i].x + FarTerrain.Origin.x, v[i].y, v[i].z + FarTerrain.Origin.y) - eye;
                float z = Vector3.Dot(world, forward);
                w[i] = z;
                if (ortho)
                {
                    sx[i] = Vector3.Dot(world, right) / pixel + width * 0.5f;
                    sy[i] = height * 0.5f - Vector3.Dot(world, up) / pixel;
                }
                else
                {
                    sx[i] = (Vector3.Dot(world, right) / (z * tanX) * 0.5f + 0.5f) * width;
                    sy[i] = (0.5f - Vector3.Dot(world, up) / (z * tanY) * 0.5f) * height;
                }
            }
            for (int k = 0; k < t.Length; k += 3)
            {
                int a = t[k], b = t[k + 1], c = t[k + 2];
                if (w[a] < 1f || w[b] < 1f || w[c] < 1f) continue;
                float minX = Math.Min(sx[a], Math.Min(sx[b], sx[c])), maxX = Math.Max(sx[a], Math.Max(sx[b], sx[c]));
                float minY = Math.Min(sy[a], Math.Min(sy[b], sy[c])), maxY = Math.Max(sy[a], Math.Max(sy[b], sy[c]));
                if (maxX < 0f || maxY < 0f || minX >= width || minY >= height) continue;
                float det = (sy[b] - sy[c]) * (sx[a] - sx[c]) + (sx[c] - sx[b]) * (sy[a] - sy[c]);
                if (Math.Abs(det) < 1e-9f) continue;
                drawn++;
                int x0 = Math.Max(0, (int)Math.Ceiling(minX - 0.5f)), x1 = Math.Min(width - 1, (int)Math.Floor(maxX - 0.5f));
                int y0 = Math.Max(0, (int)Math.Ceiling(minY - 0.5f)), y1 = Math.Min(height - 1, (int)Math.Floor(maxY - 0.5f));
                for (int py = y0; py <= y1; py++)
                    for (int px = x0; px <= x1; px++)
                    {
                        float x = px + 0.5f, yy = py + 0.5f;
                        float b0 = ((sy[b] - sy[c]) * (x - sx[c]) + (sx[c] - sx[b]) * (yy - sy[c])) / det;
                        float b1 = ((sy[c] - sy[a]) * (x - sx[c]) + (sx[a] - sx[c]) * (yy - sy[c])) / det;
                        float b2 = 1f - b0 - b1;
                        if (b0 < -1e-6f || b1 < -1e-6f || b2 < -1e-6f) continue;
                        // Perspective-correct weights from the view depth at each corner; orthographic needs none.
                        float q0 = ortho ? b0 : b0 / w[a], q1 = ortho ? b1 : b1 / w[b], q2 = ortho ? b2 : b2 / w[c], sum = q0 + q1 + q2;
                        q0 /= sum; q1 /= sum; q2 /= sum;
                        float z = q0 * w[a] + q1 * w[b] + q2 * w[c];
                        int o = py * width + px;
                        if (z >= depth[o]) continue;
                        depth[o] = z;
                        Vector3 pa = v[a], pb = v[b], pc = v[c];
                        channels[0][o] = q0 * pa.x + q1 * pb.x + q2 * pc.x + FarTerrain.Origin.x - target.x;
                        channels[1][o] = q0 * pa.z + q1 * pb.z + q2 * pc.z + FarTerrain.Origin.y - target.y;
                        channels[2][o] = q0 * pa.y + q1 * pb.y + q2 * pc.y;
                        Vector3 normal = data.normals[a] * q0 + data.normals[b] * q1 + data.normals[c] * q2;
                        channels[3][o] = normal.x; channels[4][o] = normal.y; channels[5][o] = normal.z;
                        Vector2 uv = data.uv[a] * q0 + data.uv[b] * q1 + data.uv[c] * q2;
                        Color macro = Macro(data, uv);
                        channels[6][o] = macro.r; channels[7][o] = macro.g; channels[8][o] = macro.b; channels[9][o] = macro.a;
                        channels[10][o] = data.step;
                        channels[11][o] = z;
                    }
            }
        }
        using (var file = File.Create(prefix + ".f32"))
        using (var writer = new BinaryWriter(file))
            foreach (var channel in channels) foreach (float value in channel) writer.Write(value);
        return drawn;
    }

    // Bilinear sample of the block's macro map: sRGB is decoded per texel, as the GPU does for RGBA32.
    static Color Macro(ForestHlod.Data data, Vector2 uv)
    {
        int size = data.textureSize;
        float x = uv.x * size - 0.5f, y = uv.y * size - 0.5f;
        int i = Mathf.Clamp(Mathf.FloorToInt(x), 0, size - 2), j = Mathf.Clamp(Mathf.FloorToInt(y), 0, size - 2);
        float fx = Mathf.Clamp01(x - i), fy = Mathf.Clamp01(y - j);
        Color Texel(int a, int b)
        {
            var c = data.pixels[b * size + a];
            return new Color(Decode(c.r), Decode(c.g), Decode(c.b), c.a / 255f);
        }
        Color Mix(Color p, Color q, float f) => new Color(p.r + (q.r - p.r) * f, p.g + (q.g - p.g) * f, p.b + (q.b - p.b) * f, p.a + (q.a - p.a) * f);
        return Mix(Mix(Texel(i, j), Texel(i + 1, j), fx), Mix(Texel(i, j + 1), Texel(i + 1, j + 1), fx), fy);
    }

    // The sRGB transfer function the texture sampler applies to colour channels (alpha stays linear).
    static float Decode(byte value)
    {
        float c = value / 255f;
        return c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}

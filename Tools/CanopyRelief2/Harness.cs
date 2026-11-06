using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Rasterises the production canopy blocks the streamer would request for three RTS camera distances around the
// 88_world_forest_hlod target into per-pixel G-buffers; render.py replays the canopy shader on them.
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

    // One RTS LookAt per review distance; each gets its own block levels, exactly as Update would choose them.
    static void Run(string[] args)
    {
        string output = args[0];
        Directory.CreateDirectory(output);
        WorldPlan.Generate(1200);
        var target = ForestHlod.LargestForest();
        var plan = WorldSectors.PlanAt(target, SectorKind.DeepForest, 1037);
        MapLayout.Use(plan);
        FarTerrain.Origin = new Vector2(target.x > 0f ? target.x - 9000f : target.x + 9000f, target.y);
        int size = ForestHlod.BlockSize;
        var home = new Vector2(Mathf.FloorToInt((target.x + WorldPlan.Half) / size) * size - WorldPlan.Half,
            Mathf.FloorToInt((target.y + WorldPlan.Half) / size) * size - WorldPlan.Half);
        float ground = FarScatter.GroundAt(target - FarTerrain.Origin);
        var views = new List<object>();
        foreach (float distance in new[] { 400f, 1000f, 2500f })
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
            double buildMs = watch.Elapsed.TotalMilliseconds;
            string name = "view" + (int)distance;
            int triangles = Raster(blocks, eye, forward, right, up, target, Path.Combine(output, name));
            var steps = new List<int>(); foreach (var b in blocks) steps.Add(b.step);
            views.Add(new { name, distance, pitch, yaw = Yaw, fov = Fov, width = Width, height = Height,
                eye = new[] { eye.x - target.x, eye.y, eye.z - target.y }, forward = new[] { forward.x, forward.y, forward.z },
                right = new[] { right.x, right.y, right.z }, up = new[] { up.x, up.y, up.z }, steps, triangles,
                buildMs = Math.Round(buildMs, 1), blocks = blocks.Count });
            Console.WriteLine($"{name}: pitch {pitch:F2}, blocks {blocks.Count}, steps {string.Join(",", steps)}, triangles {triangles}, build {buildMs:F0} ms");
        }
        File.WriteAllText(Path.Combine(output, "views.json"), JsonSerializer.Serialize(new { target = new[] { target.x, target.y }, ground, views },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    // Perspective-correct z-buffered rasterisation of every block triangle; attributes are what the shader receives.
    static int Raster(List<ForestHlod.Data> blocks, Vector3 eye, Vector3 forward, Vector3 right, Vector3 up, Vector2 target, string prefix)
    {
        int n = Width * Height;
        // x/z relative to the target (absolute world minus target), absolute height, vertex normal, macro colour.
        var channels = new float[12][];
        for (int c = 0; c < channels.Length; c++) { channels[c] = new float[n]; }
        var depth = new float[n];
        for (int k = 0; k < n; k++) { depth[k] = float.MaxValue; channels[0][k] = float.NaN; }
        float tanY = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad), tanX = tanY * Width / Height;
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
                sx[i] = (Vector3.Dot(world, right) / (z * tanX) * 0.5f + 0.5f) * Width;
                sy[i] = (0.5f - Vector3.Dot(world, up) / (z * tanY) * 0.5f) * Height;
            }
            for (int k = 0; k < t.Length; k += 3)
            {
                int a = t[k], b = t[k + 1], c = t[k + 2];
                if (w[a] < 1f || w[b] < 1f || w[c] < 1f) continue;
                float minX = Math.Min(sx[a], Math.Min(sx[b], sx[c])), maxX = Math.Max(sx[a], Math.Max(sx[b], sx[c]));
                float minY = Math.Min(sy[a], Math.Min(sy[b], sy[c])), maxY = Math.Max(sy[a], Math.Max(sy[b], sy[c]));
                if (maxX < 0f || maxY < 0f || minX >= Width || minY >= Height) continue;
                float det = (sy[b] - sy[c]) * (sx[a] - sx[c]) + (sx[c] - sx[b]) * (sy[a] - sy[c]);
                if (Math.Abs(det) < 1e-9f) continue;
                drawn++;
                int x0 = Math.Max(0, (int)Math.Ceiling(minX - 0.5f)), x1 = Math.Min(Width - 1, (int)Math.Floor(maxX - 0.5f));
                int y0 = Math.Max(0, (int)Math.Ceiling(minY - 0.5f)), y1 = Math.Min(Height - 1, (int)Math.Floor(maxY - 0.5f));
                for (int py = y0; py <= y1; py++)
                    for (int px = x0; px <= x1; px++)
                    {
                        float x = px + 0.5f, yy = py + 0.5f;
                        float b0 = ((sy[b] - sy[c]) * (x - sx[c]) + (sx[c] - sx[b]) * (yy - sy[c])) / det;
                        float b1 = ((sy[c] - sy[a]) * (x - sx[c]) + (sx[a] - sx[c]) * (yy - sy[c])) / det;
                        float b2 = 1f - b0 - b1;
                        if (b0 < -1e-6f || b1 < -1e-6f || b2 < -1e-6f) continue;
                        // Perspective-correct weights from the view depth at each corner.
                        float q0 = b0 / w[a], q1 = b1 / w[b], q2 = b2 / w[c], sum = q0 + q1 + q2;
                        q0 /= sum; q1 /= sum; q2 /= sum;
                        float z = q0 * w[a] + q1 * w[b] + q2 * w[c];
                        int o = py * Width + px;
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

    // Trilinear-free bilinear sample of the block's macro map: sRGB is decoded per texel, as the GPU does for RGBA32.
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

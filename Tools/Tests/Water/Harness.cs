using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using DroneSandbox;
using UnityEngine;

// Export one scene at a time from production geometry and paint, without running Unity or storing a world image.
static class WaterHarness
{
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;

    // Separate processes release all world and terrain arrays before Blender starts its single render job.
    static int Main(string[] args)
    {
        string output = args[0], scene = args.Length > 1 ? args[1] : "Varrow";
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        if (scene == "Far") Far(output);
        else Local(output, scene);
        Console.WriteLine($"{scene}: {timer.Elapsed.TotalSeconds:F2}s; working set {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MiB");
        return 0;
    }

    // Use the original Varrow recipe: its dam, river, bridge and reservoir are the autotest's free-play location.
    static void Local(string output, string scene)
    {
        MapLayout.Use(SectorPlan.Varrow());
        Ground.Build(new GameObject("Review").transform);
        Vector2 center = MapLayout.BridgePoint;
        var field = MapLayout.RiverField;
        if (scene == "Bend")
        {
            float turn = -1f;
            for (int i = 20; i < field.points.Count - 20; i++)
            {
                if (MapLayout.NearDam(field.points[i], 100f) || Math.Abs(field.points[i].x) > 700f || Math.Abs(field.points[i].y) > 700f) continue;
                Vector2 a = (field.points[i] - field.points[i - 16]).normalized;
                Vector2 b = (field.points[i + 16] - field.points[i]).normalized;
                float bend = Math.Abs(a.x * b.y - a.y * b.x);
                if (bend > turn) { turn = bend; center = field.points[i]; }
            }
        }
        if (scene == "Reservoir")
        {
            Rect bounds = Water.LakeBounds();
            center = bounds.center;
            for (float x = center.x; x < bounds.xMax; x += 1f)
                if (MapLayout.ReservoirDistance(x, center.y) >= 0f) { center.x = x; break; }
        }
        Export(output, center, 640f, 321, 512,
            (x, z) => Ground.HeightAt(new Vector3(x, 0f, z)), Ground.ReviewColour);
        using (var writer = new BinaryWriter(File.Create(Path.Combine(output, "water.bin"))))
        {
            writer.Write(3);
            SaveMesh(writer, (Mesh)typeof(Water).GetMethod("RiverMesh", Private).Invoke(null, null));
            SaveMesh(writer, (Mesh)typeof(Water).GetMethod("PoolMesh", Private).Invoke(null, null));
            SaveMesh(writer, (Mesh)typeof(Water).GetMethod("LakeMesh", Private).Invoke(null, new object[] { null }));
        }
        using (var camera = new StreamWriter(Path.Combine(output, "camera.txt")))
            camera.WriteLine($"{center.x:R} {Ground.HeightAt(new Vector3(center.x, 0f, center.y)):R} {center.y:R} 200 45");
        typeof(Water).GetMethod("BuildRiver", Private).Invoke(null, new object[] { new GameObject("Water settings").transform });
        var water = UnityEngine.Rendering.HighDefinition.WaterSurface.Last;
        using (var optics = new StreamWriter(Path.Combine(output, "optics.txt")))
            optics.WriteLine($"{water.scatteringColor.r:R} {water.scatteringColor.g:R} {water.scatteringColor.b:R} {water.ambientScattering:R} {water.startSmoothness:R}");
#if AFTER
        CheckProfile();
#endif
    }

    // The 2.5 km review paints a real reservoir and river network with a 4 m footprint, like regional pages.
    static void Far(string output)
    {
        WorldPlan.Generate(1200);
        var data = WorldPlan.CaptureDetail(null);
        var lake = WorldPlan.Lakes[1];
        foreach (var candidate in WorldPlan.Lakes) if (candidate.reservoir) { lake = candidate; break; }
        Vector2 lo = lake.shore[0], hi = lo;
        foreach (var p in lake.shore) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
        Vector2 center = (lo + hi) * .5f;
        var nearby = typeof(FarDetailData).GetMethod("Nearby", Private);
        var lanes = (FarDetailData.Segment[])nearby.Invoke(null, new object[] {
            typeof(FarDetailData).GetField("roads", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(data), center.x - 2048f, center.y - 2048f, 4096f });
        var streams = (FarDetailData.Segment[])nearby.Invoke(null, new object[] {
            typeof(FarDetailData).GetField("rivers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(data), center.x - 2048f, center.y - 2048f, 4096f });
        var watch = Stopwatch.StartNew();
        Export(output, center, 4096f, 129, 1024, data.Height,
            (x, z) => data.Paint(x, z, new Vector2(x, z), lanes, streams, 4f));
        Console.WriteLine($"regional-sized paint/export: {watch.Elapsed.TotalSeconds:F3}s");
        using (var camera = new StreamWriter(Path.Combine(output, "camera.txt")))
            camera.WriteLine($"{center.x:R} {lake.level:R} {center.y:R} 1767.767 45");
#if AFTER
        float maxError = 0f;
        foreach (float pixel in new[] { 2f, 4f, 13.671875f })
            foreach (float width in new[] { .5f, 1.5f, 5f, 18f })
            {
                float area = 0f, step = .025f;
                for (float x = -64f; x < 64f; x += step)
                    area += FarDetailData.BandCoverage(width - Math.Abs(x), width, pixel) * step;
                maxError = Math.Max(maxError, Math.Abs(area - 2f * width));
            }
        // This catches accidental half-pixel widening of a narrow stream at regional/world resolution.
        Require(maxError < .06f, "finite-width water coverage changed");
        Console.WriteLine($"river coverage integral max error {maxError:F4} m");
        Vector2 middle = lake.shore[0];
        Color32 first = data.Paint(middle.x, middle.y, middle, footprint: 2f);
        var old = lake.shore[0]; lake.shore[0] = old + new Vector2(100f, 100f);
        Color32 retained = data.Paint(middle.x, middle.y, middle, footprint: 2f); lake.shore[0] = old;
        Require(first.r == retained.r && first.g == retained.g && first.b == retained.b, "worker lake polygon was not copied");
        Console.WriteLine("lake snapshot ownership passed");
#endif
    }

    // Binary heights and row-streamed PPM paint keep the exporter small even for a regional view.
    static void Export(string output, Vector2 center, float size, int n, int pixels,
        Func<float, float, float> height, Func<float, float, Color32> paint)
    {
        using (var writer = new BinaryWriter(File.Create(Path.Combine(output, "terrain.bin"))))
        {
            writer.Write(n); writer.Write(size); writer.Write(center.x); writer.Write(center.y);
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
                writer.Write(height(center.x + (i / (float)(n - 1) - .5f) * size, center.y + (j / (float)(n - 1) - .5f) * size));
        }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(output, "colour.ppm"))))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{pixels} {pixels}\n255\n"));
            // PPM starts at the top; Blender's UV origin is bottom left.
            for (int j = pixels - 1; j >= 0; j--) for (int i = 0; i < pixels; i++)
            {
                Color32 c = paint(center.x + ((i + .5f) / pixels - .5f) * size, center.y + ((j + .5f) / pixels - .5f) * size);
                writer.Write(c.r); writer.Write(c.g); writer.Write(c.b);
            }
        }
    }

    // Store only water geometry, not scene objects, native textures, simulations or reflection data.
    static void SaveMesh(BinaryWriter writer, Mesh mesh)
    {
        writer.Write(mesh.vertices.Length); writer.Write(mesh.triangles.Length);
        foreach (var p in mesh.vertices) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
        foreach (int i in mesh.triangles) writer.Write(i);
    }

#if AFTER
    // Banks must stay within their physical bounds, deepen towards the channel and alternate over long reaches.
    static void CheckProfile()
    {
        var profile = new WatercourseProfile();
        var field = MapLayout.RiverField;
        float smallest = float.MaxValue, largest = 0f, bendDifference = 0f;
        int count = 0;
        for (int i = 20; i < field.points.Count - 20; i += 10)
        {
            Vector2 p = field.points[i];
            if (MapLayout.NearDam(p, 80f) || Math.Abs(p.x) > 950f || Math.Abs(p.y) > 950f) continue;
            Require(profile.Sample(p.x, p.y, out _, out _, out float width, out _, out float depth, out _), "missing river segment");
            Require(width > 0f && width <= MapLayout.RiverHalfWidth, "width exceeded surveyed envelope");
            smallest = Math.Min(smallest, width); largest = Math.Max(largest, width);
            Vector2 direction = field.Direction(i), side = new Vector2(-direction.y, direction.x);
            Vector2 left = p + side * (width + .5f), right = p - side * (width + .5f);
            profile.Sample(left.x, left.y, out _, out _, out _, out float a, out float shallow, out _);
            profile.Sample(right.x, right.y, out _, out _, out _, out float b, out _, out _);
            Require(a >= 1f && a <= 4f && b >= 1f && b <= 4f, "bank outside 1-4 m");
            Require(depth > shallow, "channel not deeper than margin");
            bendDifference = Math.Max(bendDifference, Math.Abs(a - b)); count++;
        }
        Require(count > 10 && largest - smallest > .5f && bendDifference > .3f, "reach/bend variation missing");
        Console.WriteLine($"profile {count} stations: half width {smallest:F2}-{largest:F2} m; largest inner/outer margin difference {bendDifference:F2} m");
    }

    // Fail the lean harness immediately on a geometry or ownership regression.
    static void Require(bool pass, string reason) { if (!pass) throw new Exception(reason); }
#endif
}

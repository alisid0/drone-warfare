using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Compare the exact production painters at fixed world positions, with explicit CPU albedo-only output.
static class TopologyHarness
{
    const int W = 768, H = 432;
    static Color32[] picture;
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };

    // The managed Perlin substitute is shared by both builds; no editor, Library or graphics device is started.
    static int Main(string[] args)
    {
        try
        {
            string output = args[0];
            WorldPlan.Generate(1200);
            if (args.Length > 1 && args[1] == "benchmark") return Benchmark(output);
            var plan = WorldPlan.CaptureDetail(Harness.Coarse());
            if (args.Length > 1 && args[1] == "audit")
            {
                MapLayout.Use(WorldSectors.PlanAt(new Vector2(-9000, -12000), SectorKind.RollingFarmland, 1200));
                MapLayout.Prepare(); TopologyChecks.Run(output, plan); return 0;
            }
            var clock = Stopwatch.StartNew();
            picture = plan.WorldPixels(4096);
            double worldSeconds = clock.Elapsed.TotalSeconds;
            var spots = new List<(string name, Vector2 at)> {
                ("wheat", new Vector2(-9000, -12000)), ("ploughed", new Vector2(-5928, -12000)),
                ("road-farm", RoadFarm()), ("village", new Vector2(-11057.904f, 4400.2354f)),
                ("river-crossing", new Vector2(-12063.079f, 16067.159f)) };
            var metrics = new List<object>();
            foreach (var spot in spots)
            {
                int x = (int)Math.Floor(spot.at.x / 512), z = (int)Math.Floor(spot.at.y / 512);
                var rough = Harness.Rough(x, z); var cover = GroundHarness.Cover(x, z);
                var times = new List<double>(); FarDetailData.Tile tile = null;
                for (int k = 0; k < 4; k++) { tile = plan.Build(x, z, rough, cover); if (k > 0) times.Add(tile.generationMs); }
                var east = plan.Build(x + 1, z, Harness.Rough(x + 1, z), GroundHarness.Cover(x + 1, z));
                for (int j = 0; j < 258; j++) for (int k = 0; k < 2; k++)
                {
                    Equal(tile.pixels[j * 258 + 256 + k], east.pixels[j * 258 + k]);
                    Equal(tile.controls[j * 258 + 256 + k], east.controls[j * 258 + k]);
                }
                times.Sort();
                metrics.Add(new { spot.name, at = new { x = spot.at.x, z = spot.at.y }, medianMs = times[1], worstMs = times[2] });
                Console.WriteLine($"{spot.name}: {times[1]:F2} / {times[2]:F2} ms");
                if (spot.name != "river-crossing") foreach (int altitude in new[] { 300, 2500 })
                    Render(output, spot.name + "-" + altitude, spot.at, 2f * altitude * (float)Math.Tan(Math.PI / 8) * W / H, plan);
            }
            var sector = WorldSectors.PlanAt(spots[0].at, SectorKind.RollingFarmland, 1200);
            MapLayout.Use(sector); clock.Restart(); MapLayout.Prepare();
            double layoutSeconds = clock.Elapsed.TotalSeconds;
            Render(output, "sector-edge", spots[0].at, 3200f, plan, true);
            Render(output, "sector-edge-close", spots[0].at + new Vector2(1000f, 0f), 442f, plan, true);
            File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(new { spots = metrics, worldSeconds, layoutSeconds,
                fields = MapLayout.Fields.Count, fieldTimings = MapLayout.FieldTimings, note = "CPU albedo, 45 degree vertical FOV. Managed Perlin; no HDRP lighting, props or frame-time claim." }, Json));
            Console.WriteLine($"World: {worldSeconds:F3} s; {MapLayout.FieldTimings}");
            TopologyChecks.Run(output, plan);
            LegacyLayouts(output);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Hash the unchanged legacy layouts in each executable; byte-for-byte geometry equality is checked by the report.
    static void LegacyLayouts(string output)
    {
        var results = new List<object>();
        foreach (var sector in new[] { SectorPlan.Varrow(), SectorGenerator.Generate(SectorKind.RollingFarmland, 1200) })
        {
            MapLayout.Use(sector); MapLayout.Prepare();
            ulong hash = 14695981039346656037UL;
            void Add(float value) { unchecked { hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(value)) * 1099511628211UL; } }
            foreach (var f in MapLayout.Fields) { Add(f.center.x); Add(f.center.y); Add(f.along.x); Add(f.along.y); Add(f.halfLength); Add(f.halfWidth); Add(f.taper); Add(f.shear); Add((int)f.crop); }
            foreach (var b in MapLayout.Boundaries) { Add(b.a.x); Add(b.a.y); Add(b.b.x); Add(b.b.y); Add((int)b.kind); }
            results.Add(new { kind = sector.kind.ToString(), fields = MapLayout.Fields.Count, boundaries = MapLayout.Boundaries.Count, geometryHash = hash.ToString("X16") });
        }
        File.WriteAllText(Path.Combine(output, "legacy.json"), JsonSerializer.Serialize(results, Json));
    }

    // Alternate isolated processes to compare cold district construction and warm painting without render work.
    static int Benchmark(string output)
    {
        var results = new List<object>();
        for (int run = 0; run < 3; run++)
        {
            var data = WorldPlan.CaptureDetail(null);
            GC.Collect(); GC.WaitForPendingFinalizers();
            var timer = Stopwatch.StartNew(); picture = data.WorldPixels(4096); double cold = timer.Elapsed.TotalSeconds;
            timer.Restart(); picture = data.WorldPixels(4096); double warm = timer.Elapsed.TotalSeconds;
            results.Add(new { run, cold, warm }); Console.WriteLine($"World run {run}: cold {cold:F3}, warm {warm:F3}");
        }
        File.WriteAllText(Path.Combine(output, "world-timings.json"), JsonSerializer.Serialize(results, Json));
        return 0;
    }

    // Keep the road-aligned case deterministic and selected from real farmland adjoining a real through-road.
    static Vector2 RoadFarm()
    {
        foreach (var road in WorldPlan.Roads)
        {
            if (road.kind == RoadClass.Track) continue;
            for (int i = 1; i < road.points.Count; i++)
            {
                Vector2 a = road.points[i - 1], b = road.points[i], dir = (b - a).normalized;
                Vector2 p = (a + b) * .5f + new Vector2(-dir.y, dir.x) * 90f;
                if (Math.Abs(p.x) > 23000 || Math.Abs(p.y) > 23000) continue;
                if (WorldPlan.CoverAt(p.x, p.y) == Cover.Farmland) return p;
            }
        }
        throw new Exception("No road farmland");
    }

    // Four footprint samples are performed inside Paint; crop rows here are a bounded albedo cue, not mesh shading.
    static void Render(string output, string name, Vector2 eye, float span, FarDetailData plan, bool sector = false)
    {
        var bytes = new byte[W * H * 3];
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var nearby = typeof(FarDetailData).GetMethod("Nearby", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        FarDetailData.Segment[] Local(string field) => (FarDetailData.Segment[])nearby.Invoke(null,
            new object[] { typeof(FarDetailData).GetField(field, flags).GetValue(plan), eye.x - span * .5f, eye.y - span * .5f, span });
        var lanes = Local("roads"); var streams = Local("rivers");
        System.Threading.Tasks.Parallel.For(0, H, j =>
        {
            for (int i = 0; i < W; i++)
            {
                float x = eye.x + ((i + .5f) / W - .5f) * span, z = eye.y + ((j + .5f) / H - .5f) * span * H / W;
                var at = new Vector2(x + 44f * (Mathf.PerlinNoise(x / 70f, z / 70f + 31f) - .5f), z + 44f * (Mathf.PerlinNoise(x / 70f + 57f, z / 70f) - .5f));
                Color32 c = name.EndsWith("-2500") || (sector && name != "sector-edge-close") ? WorldPixel(x, z) : plan.Paint(x, z, at, lanes, streams, 2f);
                Vector2 origin = sector ? MapLayout.Plan.world.center : eye;
                if (sector && Math.Abs(x - origin.x) < 1000 && Math.Abs(z - origin.y) < 1000)
                {
                    c = FarDetailPalette.Encode(FarDetailPalette.Means[0]);
                    Vector2 p = FieldPlot.Unwarp(new Vector2(x - origin.x, z - origin.y));
                    foreach (var f in MapLayout.Fields) if (f.ContainsUnwarped(p, 0f)) { c = FarDetailPalette.Encode(FarDetailPalette.Means[5 + (int)f.crop]); break; }
                }
                int k = ((H - 1 - j) * W + i) * 3;
                bytes[k] = c.r; bytes[k + 1] = c.g; bytes[k + 2] = c.b;
            }
        });
        using var stream = File.Create(Path.Combine(output, name + ".ppm"));
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"P6\n{W} {H}\n255\n"); stream.Write(header); stream.Write(bytes);
    }

    // The 2.5 km view samples the actual 13.7 m world picture in linear space, including its existing resolution limit.
    static Color32 WorldPixel(float x, float z)
    {
        float u = (x + WorldPlan.Half) / WorldPlan.Size * 4096 - .5f, v = (z + WorldPlan.Half) / WorldPlan.Size * 4096 - .5f;
        int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
        Vector3 Sample(int a, int b) => FarDetailPalette.Decode(picture[Mathf.Clamp(b, 0, 4095) * 4096 + Mathf.Clamp(a, 0, 4095)]);
        return FarDetailPalette.Encode(Vector3.Lerp(Vector3.Lerp(Sample(i, j), Sample(i + 1, j), u - i), Vector3.Lerp(Sample(i, j + 1), Sample(i + 1, j + 1), u - i), v - j));
    }

    // Gutter checks compare bytes, including the discrete material IDs and row directions.
    static void Equal(Color32 a, Color32 b)
    { if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) throw new Exception("Tile gutter mismatch"); }
}

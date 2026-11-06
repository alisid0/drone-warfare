using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Runs production data and geometry with managed engine storage; engine and HDRP timing remain player build gates.
static class RegionalHarness
{
    const int W = 1280, H = 720;
    static Color32[] picture;
#if !BEFORE
    static readonly Dictionary<long, Color32[][]> regions = new Dictionary<long, Color32[][]>();
#endif
    // Compare the same world coordinates with the baseline picture and the actual compressed regional pages.
    static int Main(string[] args)
    {
        try
        {
            string output = args[0]; Directory.CreateDirectory(output); WorldPlan.Generate(1200);
            if (args.Length > 1 && args[1] == "benchmark") { Benchmark(output); return 0; }
            var plan = WorldPlan.CaptureDetail(Harness.Coarse());
#if !BEFORE
            if (args.Length > 1 && args[1] == "audit") { Audit(plan, output); SeamChecks(plan, output); return 0; }
            if (args.Length > 1 && args[1] == "stream") { Save(output, "streaming", RegionalStreaming.Run(plan)); return 0; }
            if (args.Length > 1 && args[1] == "crossing") { Crossing(plan, output, new Vector2(-12854.826f, 16772.557f)); return 0; }
#endif
            var timer = Stopwatch.StartNew(); picture = plan.WorldPixels(4096); double worldSeconds = timer.Elapsed.TotalSeconds;
            if (args.Length > 1 && args[1] == "forest")
            {
                var centre = ForestScene();
                foreach (int altitude in new[] { 2500, 6000 }) Render(output, "forest-edge-" + altitude, centre, altitude, plan);
                Save(output, "forest-location", new { x = centre.x, z = centre.y }); return 0;
            }
            var spots = new[] { ("road-farm", new Vector2(-12854.826f, 16772.557f)), ("farmland-forest", new Vector2(-9000f, -12000f)),
                ("village", new Vector2(-11057.904f, 4400.2354f)), ("river", new Vector2(-12063.079f, 16067.159f)) };
            var tiles = new List<object>();
            foreach (var spot in spots)
            {
                int tx = (int)Math.Floor(spot.Item2.x / 512f), tz = (int)Math.Floor(spot.Item2.y / 512f);
                var rough = Harness.Rough(tx, tz); var cover = GroundHarness.Cover(tx, tz); var times = new List<double>(); FarDetailData.Tile tile = null;
                for (int k = 0; k < 5; k++) { tile = plan.Build(tx, tz, rough, cover); times.Add(tile.generationMs); }
                times.Sort(); tiles.Add(new { name = spot.Item1, x = spot.Item2.x, z = spot.Item2.y, medianMs = times[2], worstMs = times[4] });
                var east = plan.Build(tx + 1, tz, Harness.Rough(tx + 1, tz), GroundHarness.Cover(tx + 1, tz));
                for (int j = 0; j < 258; j++) for (int i = 0; i < 2; i++)
                { Equal(tile.pixels[j * 258 + 256 + i], east.pixels[j * 258 + i]); Equal(tile.controls[j * 258 + 256 + i], east.controls[j * 258 + i]); }
                if (!(args.Length > 1 && args[1] == "tiles") && spot.Item1 != "river") foreach (int altitude in new[] { 2500, 6000 }) Render(output, spot.Item1 + "-" + altitude, spot.Item2, altitude, plan);
                Console.WriteLine(spot.Item1 + ": tile median " + times[2].ToString("F1") + " ms");
            }
            if (args.Length > 1 && args[1] == "tiles") { Save(output, "tile-controls", new { tiles, gutterEquality = true }); return 0; }
#if !BEFORE
            Audit(plan, output); SeamChecks(plan, output); Crossing(plan, output, spots[0].Item2);
#endif
            Save(output, "metrics", new { worldSeconds, tiles, note = "CPU albedo, actual BC1 decode after; 45 degree vertical FOV. No HDRP lighting, props, GPU or native upload timing." });
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Isolate three cold and warm paints from render work for the picture-paint budget.
    static void Benchmark(string output)
    {
        var results = new List<object>();
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            var timer = Stopwatch.StartNew(); var plan = WorldPlan.CaptureDetail(null); double snapshot = timer.Elapsed.TotalSeconds;
            picture = plan.WorldPixels(4096); double cold = timer.Elapsed.TotalSeconds;
            timer.Restart(); picture = plan.WorldPixels(4096); double warm = timer.Elapsed.TotalSeconds;
            results.Add(new { cold, warm, snapshot }); Console.WriteLine($"World {i}: cold incl. snapshot {cold:F3}, warm {warm:F3}");
        }
        Save(output, "world-timings", results);
    }

    // Find one fixed world cover boundary with substantial woodland and farmland in the same 2.5 km frame.
    static Vector2 ForestScene()
    {
        for (float z = -22000; z <= 22000; z += 512) for (float x = -22000; x <= 22000; x += 512)
        {
            if (WorldPlan.CoverAt(x, z) != Cover.Forest) continue;
            int forest = 0, farm = 0;
            for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++)
            { var kind = WorldPlan.CoverAt(x + i * 300, z + j * 300); if (kind == Cover.Forest) forest++; if (kind == Cover.Farmland) farm++; }
            if (forest >= 8 && farm >= 6) return new Vector2(x, z);
        }
        throw new Exception("No mixed forest/farm scene");
    }

    // Sample the real world picture before; after includes BC1 quantization and the 7-8 km outer fade.
    static void Render(string output, string name, Vector2 centre, float altitude, FarDetailData plan)
    {
        float span = 2f * altitude * (float)Math.Tan(Math.PI / 8) * W / H;
#if !BEFORE
        Cache(plan, centre.x - span * .5f, centre.y - span * H / W * .5f, centre.x + span * .5f, centre.y + span * H / W * .5f);
#endif
        var bytes = new byte[W * H * 3];
        System.Threading.Tasks.Parallel.For(0, H, j =>
        {
            for (int i = 0; i < W; i++)
            {
                float x = centre.x + ((i + .5f) / W - .5f) * span, z = centre.y + ((j + .5f) / H - .5f) * span * H / W;
                Vector3 c = Picture(x, z);
#if !BEFORE
                float distance = (float)Math.Sqrt(altitude * altitude + (x - centre.x) * (x - centre.x) + (z - centre.y) * (z - centre.y));
                c = Vector3.Lerp(c, Region(x, z, span / W), 1f - Smooth((distance - 7000f) / 1000f));
#endif
                Pixel(bytes, i, j, FarDetailPalette.Encode(c));
            }
        });
        Ppm(output, name, bytes);
    }

#if !BEFORE
    // Cache immutable decoded production pages before a parallel render reads them.
    static void Cache(FarDetailData plan, float x0, float z0, float x1, float z1)
    {
        for (int z = (int)Math.Floor(z0 / 2048f); z <= (int)Math.Floor(z1 / 2048f); z++) for (int x = (int)Math.Floor(x0 / 2048f); x <= (int)Math.Floor(x1 / 2048f); x++)
        {
            long key = ((long)x << 32) ^ (uint)z; if (regions.ContainsKey(key)) continue;
            var page = FarDetailRegionData.Build(plan, x, z); var levels = new Color32[3][];
            for (int m = 0; m < 3; m++) levels[m] = FarDetailRegionData.Decode(page.mips[m], 544 >> m);
            regions.Add(key, levels);
        }
    }

    // Coordinates match the shader's global page and gutter convention.
    static Vector3 Region(float x, float z, float footprint = 4f)
    {
        int cx = (int)Math.Floor(x / 2048f), cz = (int)Math.Floor(z / 2048f);
        var levels = regions[((long)cx << 32) ^ (uint)cz];
        float lod = Math.Max(0f, Math.Min(2f, (float)Math.Log(footprint / 4f, 2))), u = (x - cx * 2048f) / 4f + 16, v = (z - cz * 2048f) / 4f + 16;
        Vector3 Level(int m) => Bilinear(levels[m], 544 >> m, u / (1 << m) - .5f, v / (1 << m) - .5f);
        int a = (int)lod; return Vector3.Lerp(Level(a), Level(Math.Min(a + 1, 2)), lod - a);
    }

    // Compare stored borders at every mip, repeat worker results and verify full radial coverage at awkward cell phases.
    static void SeamChecks(FarDetailData plan, string output)
    {
        int comparisons = 0, coverage = 0; var times = new List<double>();
        foreach (var cell in new[] { (-7, 8), (-1, -1), (0, 0) })
        {
            var a = FarDetailRegionData.Build(plan, cell.Item1, cell.Item2); var b = FarDetailRegionData.Build(plan, cell.Item1 + 1, cell.Item2);
            var c = FarDetailRegionData.Build(plan, cell.Item1, cell.Item2 + 1); var again = FarDetailRegionData.Build(plan, cell.Item1, cell.Item2);
            times.Add(a.milliseconds); times.Add(b.milliseconds); times.Add(c.milliseconds);
            for (int m = 0; m < 3; m++)
            {
                int n = 544 >> m, step = 512 >> m, gutter = 16 >> m;
                var aa = FarDetailRegionData.Decode(a.mips[m], n); var bb = FarDetailRegionData.Decode(b.mips[m], n); var cc = FarDetailRegionData.Decode(c.mips[m], n);
                for (int j = 0; j < n; j++) for (int i = 0; i < 2 * gutter; i++)
                { Equal(aa[j * n + step + i], bb[j * n + i]); Equal(aa[(step + i) * n + j], cc[i * n + j]); comparisons += 2; }
                for (int i = 0; i < a.mips[m].Length; i++) if (a.mips[m][i] != again.mips[m][i]) throw new Exception("Nondeterministic regional page");
            }
        }
        foreach (float phase in new[] { 0f, .01f, 1024f, 2047.99f })
        {
            Vector2 eye = new Vector2(-2048f + phase, -2048f + phase); var slots = new HashSet<int>();
            int cx = (int)Math.Floor(eye.x / 2048f), cz = (int)Math.Floor(eye.y / 2048f);
            for (int z = cz - 4; z <= cz + 4; z++) for (int x = cx - 4; x <= cx + 4; x++) if (!slots.Add(FarDetailRegional.Index(x, z))) throw new Exception("Toroid collision");
            for (int i = 0; i < 720; i++)
            {
                double angle = i * Math.PI / 360; int x = (int)Math.Floor((eye.x + 8000 * Math.Cos(angle)) / 2048), z = (int)Math.Floor((eye.y + 8000 * Math.Sin(angle)) / 2048);
                if (!FarDetailRegional.Wanted(x, z, eye)) throw new Exception("8 km coverage hole"); coverage++;
            }
        }
        if (FarDetailRegional.GpuBytes > 64L * 1024 * 1024) throw new Exception("VRAM cap"); times.Sort();
        Save(output, "seams", new { compressedBorderComparisons = comparisons, circleSamples = coverage, regionalGpuBytes = FarDetailRegional.GpuBytes, pageMedianMs = times[times.Count / 2], pageWorstMs = times[times.Count - 1] });
    }

    // Audit actual post-cover fragments and densely probe the shared canopy formula, including the forest interiors.
    static void Audit(FarDetailData plan, string output)
    {
        var band = typeof(FarDetailData).GetMethod("PeriodicBand", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        foreach (float pitch in new[] { 5f, 10f }) for (int k = -40; k <= 40; k++)
        {
            float actual = (float)band.Invoke(null, new object[] { k * .43f, pitch, pitch * .6f, pitch * 3f });
            if (Math.Abs(actual - .6f) > .00001f) throw new Exception("Coarse garden rows lost their area mean");
        }
        int parcels = 0, idle = 0, gardens = 0, estates = 0, outside = 0, probes = 0, woodSamples = 0;
        double minArea = double.MaxValue, minEdge = double.MaxValue, minAngle = 180, maxForestError = 0;
        float minStrip = float.MaxValue, maxStrip = 0, minHedge = 1, maxHedge = 0; var rng = new Random(1907);
        for (int dindex = 0; dindex < 500; dindex++)
        {
            var d = plan.fields.Get(rng.Next(-33, 34), rng.Next(-33, 34));
            minStrip = Math.Min(minStrip, d.width); maxStrip = Math.Max(maxStrip, d.width); minHedge = Math.Min(minHedge, d.hedgeDensity); maxHedge = Math.Max(maxHedge, d.hedgeDensity);
            foreach (var p in d.parcels)
            {
                float length = p.u1 - p.u0, area = length * (p.a1 - p.a0 + p.b1 - p.b0) * .5f;
                double edge = Math.Min(length, Math.Min(p.a1 - p.a0, p.b1 - p.b0));
                double angle = 90 - Math.Atan(Math.Max(Math.Abs(p.b0 - p.a0), Math.Abs(p.b1 - p.a1)) / length) * 180 / Math.PI;
                minArea = Math.Min(minArea, area); minEdge = Math.Min(minEdge, edge); minAngle = Math.Min(minAngle, angle);
                if (area < 599.9 || edge < 17.99 || angle < 44.99) throw new Exception("Unworkable cover remnant");
                parcels++; if (p.use == 1 || p.use == 2) idle++; if (p.use >= 3) gardens++; if (area >= 80000 && area <= 250000) estates++;
                int nu = (int)Math.Ceiling(length / 4f), nv = (int)Math.Ceiling(Math.Max(p.a1 - p.a0, p.b1 - p.b0) / 4f);
                for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++)
                {
                    float t = i / (float)nu, v = FarDetailData.Mix(FarDetailData.Mix(p.a0, p.b0, t), FarDetailData.Mix(p.a1, p.b1, t), j / (float)nv);
                    Vector2 w = d.site + d.along * FarDetailData.Mix(p.u0, p.u1, t) + new Vector2(-d.along.y, d.along.x) * v;
                    if (plan.cover.At(w.x, w.y).Weight(Cover.Farmland) < .5f || plan.cover.Forest(w.x, w.y) > .2f) outside++; probes++;
                }
            }
            for (int k = 0; k < 200; k++)
            {
                float x = d.site.x + (float)rng.NextDouble() * 700 - 350, z = d.site.y + (float)rng.NextDouble() * 700 - 350;
                var actual = plan.cover.At(x, z);
                float dx = (plan.cover.Noise(x, z, 37f, 73) - .5f) * 12f, dz = (plan.cover.Noise(x, z, 53f, 74) - .5f) * 12f;
                var reference = plan.cover.At(x + dx, z + dz, false);
                for (int kind = 0; kind < 8; kind++) if (Math.Abs(actual.Weight((Cover)kind) - reference.Weight((Cover)kind)) > .000001f) throw new Exception("Uniform cover shortcut changed the mask");
                maxForestError = Math.Max(maxForestError, Math.Abs(plan.cover.Forest(x, z) - ForestPattern.Edge(x, z))); woodSamples++;
            }
        }
        Save(output, "audit", new { districts = 500, parcels, idle, idleFraction = idle / (double)parcels, gardens, estates, minArea, minEdge, minAngle, minStrip, maxStrip, minHedge, maxHedge, outside, probes, woodSamples, maxForestError });
        if (outside != 0 || maxForestError > .00001) throw new Exception("Cover clipping or canopy agreement failed");
        var sector = WorldSectors.PlanAt(new Vector2(-12854.826f, 16772.557f), SectorKind.RollingFarmland, 1200);
        MapLayout.Use(sector); var timer = Stopwatch.StartNew(); MapLayout.Prepare(); double seconds = timer.Elapsed.TotalSeconds; int samples = 0, wrong = 0;
        foreach (var f in MapLayout.Fields) for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++)
        {
            Vector2 p = f.Point(f.halfLength * (i - 1) * .98f, f.halfWidth * (j - 1) * .98f) + sector.world.center;
            var sample = plan.FieldAt(p.x, p.y); samples++;
            if (sample.layer != 5 + (int)f.crop || sample.side < -.03f || sample.end < -.03f)
            { if (wrong < 5) Console.WriteLine($"Sector mismatch {p.x},{p.y}: {sample.layer}/{5 + (int)f.crop}, side {sample.side}, end {sample.end}"); wrong++; }
        }
        Save(output, "sector", new { samples, wrong, seconds, fields = MapLayout.Fields.Count, timings = MapLayout.FieldTimings });
        if (wrong != 0) throw new Exception("Sector crop disagreement");
    }

    // Move the eye across the tile-ring fade on a fixed view; page seams remain visible to the review camera throughout.
    static void Crossing(FarDetailData plan, string output, Vector2 at)
    {
        const float span = 3600f; Cache(plan, at.x, at.y - 700f, at.x + span, at.y + 700f);
        var tiles = new Dictionary<long, Color32[]>();
        for (int z = (int)Math.Floor((at.y - 700) / 512); z <= (int)Math.Floor((at.y + 700) / 512); z++)
            for (int x = (int)Math.Floor(at.x / 512); x <= (int)Math.Floor((at.x + span) / 512); x++)
                tiles.Add(((long)x << 32) ^ (uint)z, plan.Build(x, z, Harness.Rough(x, z), null).pixels);
        for (int frame = 0; frame < 9; frame++)
        {
            var bytes = new byte[W * H * 3]; float eye = at.x + frame * 200f;
            for (int j = 0; j < H; j++) for (int i = 0; i < W; i++)
            {
                float x = at.x + (i + .5f) / W * span, z = at.y + ((j + .5f) / H - .5f) * 1400;
                float d = (float)Math.Sqrt((x - eye) * (x - eye) + (z - at.y) * (z - at.y));
                float u = x - (float)Math.Floor(x / 512f) * 512, v = z - (float)Math.Floor(z / 512f) * 512;
                float edge = Math.Min(Math.Min(u, v), Math.Min(512 - u, 512 - v));
                float weight = (1 - Smooth((d - 900) / 400)) * Smooth(edge / 32); Vector3 c = Region(x, z);
                if (weight > 0)
                {
                    int tx = (int)Math.Floor(x / 512), tz = (int)Math.Floor(z / 512);
                    var pixels = tiles[((long)tx << 32) ^ (uint)tz];
                    c = Vector3.Lerp(c, Bilinear(pixels, 258, u / 2 + .5f, v / 2 + .5f), weight);
                }
                Pixel(bytes, i, j, FarDetailPalette.Encode(c));
            }
            Ppm(output, "crossing-" + frame, bytes);
        }
    }
#endif

    // World and page textures filter in linear light; half-texel offsets match the runtime sampler.
    static Vector3 Picture(float x, float z) => Bilinear(picture, 4096, (x + WorldPlan.Half) / WorldPlan.Size * 4096 - .5f, (z + WorldPlan.Half) / WorldPlan.Size * 4096 - .5f);

    // Managed bilinear interpolation is used only for offline image inspection.
    static Vector3 Bilinear(Color32[] pixels, int n, float x, float y)
    {
        int i = (int)Math.Floor(x), j = (int)Math.Floor(y);
        Vector3 Sample(int a, int b) => FarDetailPalette.Decode(pixels[Math.Max(0, Math.Min(n - 1, b)) * n + Math.Max(0, Math.Min(n - 1, a))]);
        return Vector3.Lerp(Vector3.Lerp(Sample(i, j), Sample(i + 1, j), x - i), Vector3.Lerp(Sample(i, j + 1), Sample(i + 1, j + 1), x - i), y - j);
    }

    // Exact stored-byte equality catches colour and categorical-control seam differences.
    static void Equal(Color32 a, Color32 b)
    { if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) throw new Exception("Gutter mismatch"); }

    // Cubic fades reproduce the shader's transition for the review images.
    static float Smooth(float t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }

    // North is at the top of the saved image.
    static void Pixel(byte[] bytes, int i, int j, Color32 c)
    { int k = ((H - 1 - j) * W + i) * 3; bytes[k] = c.r; bytes[k + 1] = c.g; bytes[k + 2] = c.b; }

    // PPM avoids an image dependency in the C# executable; the report script exports compact deliverables.
    static void Ppm(string output, string name, byte[] bytes)
    { using var file = File.Create(Path.Combine(output, name + ".ppm")); file.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{W} {H}\n255\n")); file.Write(bytes); }

    // Keep measurements alongside the images for repeatable review.
    static void Save(string output, string name, object data) => File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
}

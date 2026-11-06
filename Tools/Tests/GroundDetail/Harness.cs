using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Executes the production workers and exports material inputs for the offline, scale-matched ground renders.
static class GroundHarness
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };

    // No Unity native functions are executed: the established reference Perlin drives both sides of each comparison.
    static int Main(string[] args)
    {
        try
        {
            string output = args[0]; Directory.CreateDirectory(output);
            ExportCrops(output);
            WorldPlan.Generate(1200);
            var coarse = Harness.Coarse(); var plan = WorldPlan.CaptureDetail(coarse);
            var spots = Spots(plan); var metrics = new List<object>();
            if (args.Length > 1 && args[1] == "references")
            {
                foreach (var spot in spots) SectorReference.Export(output, spot.name, spot.at, plan, spot.name == "wheat");
                return 0;
            }
            foreach (var spot in spots)
            {
                int x = (int)Math.Floor(spot.at.x / 512), z = (int)Math.Floor(spot.at.y / 512);
                var samples = Cover(x, z); var rough = Harness.Rough(x, z);
                FarDetailData.Tile tile = null; var times = new List<double>();
                for (int i = 0; i < 4; i++) { tile = plan.Build(x, z, rough, samples); if (i > 0) times.Add(tile.generationMs); }
                Harness.CheckRays(tile); var clipping = Harness.CheckClip(tile, plan);
                var east = plan.Build(x + 1, z, Harness.Rough(x + 1, z), Cover(x + 1, z));
                for (int j = 0; j < FarDetailData.TextureSize; j++)
                {
                    Equal(tile.pixels[j * 258 + 256], east.pixels[j * 258], "colour gutter");
                    Equal(tile.controls[j * 258 + 256], east.controls[j * 258], "control gutter");
                    Equal(tile.pixels[j * 258 + 257], east.pixels[j * 258 + 1], "colour gutter outer");
                }
                foreach (int index in tile.triangles) if (index < 0 || index >= tile.vertices.Length) throw new Exception("Bad mesh index");
                foreach (var t in tile.tangents) if (Math.Abs(t.x * t.x + t.y * t.y - 1) > .0001f || t.w != -1) throw new Exception("Invalid tangent basis");
                Bytes(Path.Combine(output, spot.name + "-macro.rgba"), tile.pixels);
                Bytes(Path.Combine(output, spot.name + "-control.rgba"), tile.controls);
                // Export field metadata at screen-independent 2 m spacing for the matched Farmland reference.
                using (var writer = new BinaryWriter(File.Create(Path.Combine(output, spot.name + "-fields.bin"))))
                    for (int j = 0; j < 258; j++) for (int i = 0; i < 258; i++)
                        {
                            var f = plan.FieldAt(x * 512f + (i - .5f) * 2, z * 512f + (j - .5f) * 2);
                            writer.Write(f.across); writer.Write(f.along); writer.Write(f.width); writer.Write(f.length);
                        }
                times.Sort();
                metrics.Add(new { name = spot.name, center = new { x = spot.at.x, z = spot.at.y }, tileX = x, tileZ = z, medianMs = times[1], worstMs = times[2], clipping,
                    gpuBytes = tile.vertices.Length * 48L + tile.triangles.Length * 2L + 258L * 258 * 4 * 4 / 3 + 258L * 258 * 4 });
                Console.WriteLine($"{spot.name}: worker {times[1]:F2} ms median / {times[2]:F2} ms worst");
            }
            CoverageChecks();
            var stream = StreamChecks.Run(coarse);
            File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(new { spots = metrics, stream,
                note = "Offline CPU material/coverage harness. Native Perlin is substituted. No Unity frame time, GPU, lighting or upload measurement." }, Json));
            foreach (var spot in spots) SectorReference.Export(output, spot.name, spot.at, plan, spot.name == "wheat");
            Console.WriteLine("PASS: gutters, IDs, tangents, raycasts, clipping, coverage and streaming state machine.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Capture the exact gutter locations sampled by the real streamer's time-budgeted main-thread rows.
    public static Vector2[] Cover(int x, int z)
    {
        var result = new Vector2[258 * 258];
        for (int j = 0; j < 258; j++) for (int i = 0; i < 258; i++)
            {
                float wx = x * 512f + (i - .5f) * 2f, wz = z * 512f + (j - .5f) * 2f;
                result[j * 258 + i] = new Vector2(wx + 44f * (Mathf.PerlinNoise(wx / 70f, wz / 70f + 31f) - .5f), wz + 44f * (Mathf.PerlinNoise(wx / 70f + 57f, wz / 70f) - .5f));
            }
        return result;
    }

    // Real generated world locations: crop centres avoid boundaries in the 50 m view; wider views include them.
    static List<(string name, Vector2 at)> Spots(FarDetailData plan)
    {
        Vector2 wheat = default, soil = default, village = default, river = default;
        bool gotWheat = false, gotSoil = false;
        for (int z = -12000; z <= 12000 && !(gotWheat && gotSoil); z += 24)
            for (int x = -12000; x <= 12000 && !(gotWheat && gotSoil); x += 24)
            {
                if (plan.CoverAt(x, z) != DroneSandbox.Cover.Farmland) continue;
                var f = plan.FieldAt(x, z);
                float tx = x - (float)Math.Floor(x / 512f) * 512, tz = z - (float)Math.Floor(z / 512f) * 512;
                if (f.side < 22 || f.end < 35 || f.districtEdge < 60 || tx < 215 || tx > 297 || tz < 215 || tz > 297) continue;
                if (f.layer == 5 && !gotWheat) { wheat = new Vector2(x, z); gotWheat = true; }
                if (f.layer == 7 && !gotSoil) { soil = new Vector2(x, z); gotSoil = true; }
            }
        if (!gotWheat || !gotSoil) throw new Exception("Required crops not found");
        float best = float.MaxValue;
        foreach (var town in WorldPlan.Settlements)
        {
            if (town.town) continue;
            float score = CentreScore(town.center);
            if (score < best) { village = town.center; best = score; }
        }
        best = float.MaxValue;
        foreach (var bridge in WorldPlan.Bridges)
        {
            if (bridge.railway) continue;
            float score = CentreScore(bridge.position);
            if (score < best) { river = bridge.position; best = score; }
        }
        return new List<(string, Vector2)> { ("wheat", wheat), ("ploughed", soil), ("village", village), ("river-crossing", river) };
    }

    // Prefer cases whose 300 m field of view fits within the exported tile.
    static float CentreScore(Vector2 p) => Math.Abs(Mathf.Repeat(p.x, 512) - 256) + Math.Abs(Mathf.Repeat(p.y, 512) - 256);

    // Exact Farmland.CropColor and Ground lattice methods are extracted into the compiled reference unchanged.
    static void ExportCrops(string output)
    {
        for (int crop = 0; crop < 5; crop++)
        {
            var rng = new Random(crop + 17);
            float[] coarse = Ground.Lattice(16, rng), fine = Ground.Lattice(64, rng), grain = Ground.Lattice(128, rng);
            var pixels = new Color32[512 * 512];
            Vector3 mean = Vector3.zero;
            for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
                {
                    float a = Ground.SampleLattice(coarse, 16, x / 32f, y / 32f), b = Ground.SampleLattice(fine, 64, x / 8f, y / 8f), c = Ground.SampleLattice(grain, 128, x / 4f, y / 4f);
                    var p = CropReference.Pixel((Crop)crop, x / 64f, y / 64f, a, b, c); pixels[y * 512 + x] = p;
                    mean += FarDetailPalette.Decode(p);
                }
            mean /= pixels.Length;
            Vector3 expected = FarDetailPalette.Means[crop + 5];
            if (Math.Abs(mean.x / expected.x - 1) > .005f || Math.Abs(mean.y / expected.y - 1) > .005f || Math.Abs(mean.z / expected.z - 1) > .005f)
                throw new Exception("Crop source palette changed; recalibrate layer " + crop);
            Console.WriteLine($"Crop {crop} mean linear: {mean.x:R}, {mean.y:R}, {mean.z:R}");
            Bytes(Path.Combine(output, "crop-" + crop + ".rgba"), pixels);
        }
    }

    // Translation and angle tests check area conservation rather than mirroring one particular edge sample.
    static void CoverageChecks()
    {
        for (int i = 0; i < 100; i++)
        {
            float d = i * .07f - 3.5f;
            float sum = FarDetailData.Coverage(d, 2f) + FarDetailData.Coverage(-d, 2f);
            if (Math.Abs(sum - 1) > 1e-6) throw new Exception("Complementary mask coverage is not conserved");
        }
        var a = new Vector3(.12f, .2f, .07f); var b = new Vector3(.4f, .3f, .1f);
        if ((FarDetailEdge.Blend(a, b, 0) - a).magnitude > 1e-6 || (FarDetailEdge.Blend(a, b, 600) - b).magnitude > 1e-6) throw new Exception("Collar endpoints do not match their owners");
        foreach (float width in new[] { .25f, 1f, 3.5f, 12f }) foreach (float pixel in new[] { 2f, 56000f / 4096 })
        {
            double area = 0;
            for (int k = 0; k < 20000; k++)
            {
                float distance = Math.Abs(-50f + (k + .5f) * .005f + .371f);
                area += FarDetailData.BandCoverage(width - distance, width, pixel) * .005;
            }
            if (Math.Abs(area / (2 * width) - 1) > .001) throw new Exception("Subpixel stream/road coverage changed its physical width");
        }
    }

    // Identical world coordinates must produce identical encoded bytes across independent tile workers.
    static void Equal(Color32 a, Color32 b, string message)
    { if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) throw new Exception("Mismatched " + message); }

    // Raw inputs allow the renderer to reproduce sRGB filtering without lossy intermediates.
    static void Bytes(string path, Color32[] pixels)
    {
        using var file = new BinaryWriter(File.Create(path));
        foreach (var p in pixels) { file.Write(p.r); file.Write(p.g); file.Write(p.b); file.Write(p.a); }
    }
}

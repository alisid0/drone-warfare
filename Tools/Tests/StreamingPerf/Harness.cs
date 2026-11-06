using System;
using System.Diagnostics;
using DroneSandbox;
using UnityEngine;

// Measure only the shared data path; native uploads, physics and frame budgets require Claude's Unity player.
static class StreamingPerfHarness
{
    // Keep one picture alive at a time and two worker threads, avoiding the old render harness's memory peak.
    static int Main()
    {
        WorldPlan.Generate(1200);
        var timer = Stopwatch.StartNew();
        var plan = WorldPlan.CaptureDetail(null);
        Console.WriteLine($"snapshot cold {timer.Elapsed.TotalMilliseconds:F2} ms");
        timer.Restart();
        var next = WorldPlan.CaptureDetail(null);
        Console.WriteLine($"snapshot repeat {timer.Elapsed.TotalMilliseconds:F2} ms; shared {ReferenceEquals(plan, next)}");
        Picture(plan);
        foreach (var pair in new[] { (SectorKind.LakeDistrict, 1296), (SectorKind.Reservoir, 1333) })
        {
            var hotspot = new Hotspot { kind = pair.Item1, seed = pair.Item2 };
            var sector = WorldSectors.PlanAt(WorldSectors.SpotFor(hotspot), hotspot.kind, hotspot.seed);
            MapLayout.Use(sector); timer.Restart(); MapLayout.Prepare();
            ulong hash = 14695981039346656037UL;
            foreach (var f in MapLayout.Fields)
            {
                Hash(ref hash, f.center.x); Hash(ref hash, f.center.y); Hash(ref hash, f.halfLength);
                Hash(ref hash, f.halfWidth); Hash(ref hash, f.taper); Hash(ref hash, f.shear); Hash(ref hash, (int)f.crop);
            }
            Console.WriteLine($"{pair.Item1} layout {timer.Elapsed.TotalMilliseconds:F2} ms; {MapLayout.Timings}; {MapLayout.FieldTimings}; fields {MapLayout.Fields.Count}; hash {hash:X16}");
        }
#if AFTER
        TileChecks(plan);
        var coarse = new float[1][] { new float[] { 7f } };
        var streaming = WorldPlan.CaptureDetail(coarse);
        if (!ReferenceEquals(plan.fields, streaming.fields) || !ReferenceEquals(plan.cover, streaming.cover)
            || !ReferenceEquals(plan.villages, streaming.villages) || !ReferenceEquals(coarse, streaming.coarse)
            || plan.coarse != null) throw new Exception("Snapshot sharing changed sector ownership");
        WorldPlan.Generate(1200);
        if (ReferenceEquals(plan, WorldPlan.CaptureDetail(null))) throw new Exception("Same-seed regeneration reused stale grids");
        Console.WriteLine("snapshot ownership and same-seed invalidation passed");
#endif
        return 0;
    }

    // Hash the final image without retaining another 64 MiB copy.
    static void Picture(FarDetailData plan)
    {
        var timer = Stopwatch.StartNew();
#if AFTER
        plan.PreviewPixels(256);
        Console.WriteLine($"preview {timer.Elapsed.TotalMilliseconds:F2} ms; districts {plan.fields.BuiltDistricts}");
        if (plan.fields.BuiltDistricts != 0) throw new Exception("Preview constructed farm districts");
        timer.Restart();
#endif
        var pixels = plan.WorldPixels(4096);
        double paint = timer.Elapsed.TotalMilliseconds;
        ulong hash = 14695981039346656037UL;
        foreach (var p in pixels) { hash = (hash ^ p.r) * 1099511628211UL; hash = (hash ^ p.g) * 1099511628211UL; hash = (hash ^ p.b) * 1099511628211UL; }
        Console.WriteLine($"picture cold {paint:F2} ms; hash {hash:X16}; working set {Process.GetCurrentProcess().WorkingSet64 / 1048576} MiB");
#if AFTER
        Console.WriteLine($"district count {plan.fields.BuiltDistricts}, construction CPU {plan.fields.BuildMilliseconds:F2} ms");
#endif
        timer.Restart();
        var page = FarDetailRegionData.Build(plan, 0, 0);
        Console.WriteLine($"regional paint + BC1 {timer.Elapsed.TotalMilliseconds:F2} ms; payload {FarDetailRegionData.Bytes} bytes");
    }

    // Include exact float bits so a faster exclusion pass cannot silently move or remove fields.
    static void Hash(ref ulong hash, float value)
    { hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(value)) * 1099511628211UL; }

#if AFTER
    // Recycled slots must reproduce fresh geometry, controls and pixels, including every mip of the odd-sized gutter.
    static void TileChecks(FarDetailData plan)
    {
        var coarse = new float[196][];
        for (int i = 0; i < coarse.Length; i++) coarse[i] = new float[65 * 65];
        var data = new FarDetailData(plan, coarse); var rough = new float[129 * 129];
        var tile = data.Build(10, 10, rough, null); var vertices = tile.vertices; var mips = tile.mips;
        var reused = data.Build(11, 10, rough, null, tile);
        var fresh = data.Build(11, 10, rough, null);
        if (!ReferenceEquals(tile, reused) || !ReferenceEquals(vertices, reused.vertices) || !ReferenceEquals(mips, reused.mips))
            throw new Exception("Tile buffers were not recycled");
        for (int i = 0; i < fresh.vertices.Length; i++)
            if ((fresh.vertices[i] - reused.vertices[i]).sqrMagnitude != 0) throw new Exception("Reused tile geometry changed");
        for (int m = 0; m < fresh.mips.Length; m++) for (int i = 0; i < fresh.mips[m].Length; i++)
            if (!fresh.mips[m][i].Equals(reused.mips[m][i])) throw new Exception("Reused tile colour changed");
        for (int i = 0; i < fresh.controls.Length; i++)
            if (!fresh.controls[i].Equals(reused.controls[i])) throw new Exception("Reused tile controls changed");
        var odd = new Color32[9]; odd[8] = new Color32(255, 255, 255, 255);
        var average = FarDetailMips.Build(odd, 3)[1][0];
        var expected = FarDetailPalette.Encode(Vector3.one / 9f);
        if (!average.Equals(expected)) throw new Exception("Odd mip lost its last row or column");
        var cancelled = new System.Threading.CancellationToken(true);
        try { data.WorldPixels(4096, cancelled); throw new Exception("Cancelled paint started"); }
        catch (OperationCanceledException) { }
        Console.WriteLine("tile reuse, linear odd-size mip filtering and paint cancellation passed");
    }
#endif
}

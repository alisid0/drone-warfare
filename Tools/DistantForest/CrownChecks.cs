using System;
using System.Diagnostics;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Compare the optimized grid traversal against the previous production metre sampler, including negative coordinates.
static class CrownChecks
{
    // Long, short, vertical and above-canopy rays must preserve every existing line-of-sight decision.
    public static object Run()
    {
        ReferenceCanopy.Build(new GameObject("Reference crowns").transform);
        var rng = new Random(187);
        const int count = 4000;
        var a = new Vector3[count]; var b = new Vector3[count]; var expected = new bool[count];
        for (int i = 0; i < count; i++)
        {
            var stem = SectorForest.Stems[rng.Next(SectorForest.Stems.Count)];
            Vector3 p = stem.position + Vector3.up * (float)(rng.NextDouble() * 50);
            float length = i % 3 == 0 ? 2000f : 100f;
            a[i] = p + new Vector3((float)(rng.NextDouble() - .5) * length, (float)(rng.NextDouble() - .5) * 40, 0f);
            b[i] = p + new Vector3(0f, (float)(rng.NextDouble() - .5) * 40, (float)(rng.NextDouble() - .5) * length);
            if (i % 11 == 0) { a[i] = p + Vector3.up * 80f; b[i] = p - Vector3.up * 30f; }
            if (i % 13 == 0) { a[i].x = b[i].x = 8f * (i % 20 - 10); a[i].z = b[i].z = -8f; }
        }
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < count; i++) expected[i] = ReferenceCanopy.Blocks(a[i], b[i]);
        double before = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        for (int i = 0; i < count; i++)
            if (Canopy.Blocks(a[i], b[i]) != expected[i]) throw new Exception("Crown query changed at ray " + i);
        double after = timer.Elapsed.TotalMilliseconds;
        Console.WriteLine($"CROWNS {count} identical rays; before {before:F2}, after {after:F2} ms; sampled {Canopy.Samples}, skipped {Canopy.SkippedSamples}");
        return new { rays = count, beforeMs = before, afterMs = after, Canopy.Samples, Canopy.SkippedSamples, nativeUnityMeasured = false };
    }
}

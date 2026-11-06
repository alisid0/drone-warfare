using System;
using System.Collections.Generic;
using System.Diagnostics;
using DroneSandbox;
using UnityEngine;

// Check mesh continuity and bounded managed work without pretending that native uploads or HDRP ran here.
static class Checks
{
    // Neighbouring tiers must have identical perimeter vertices and normals at every shared sample.
    public static object Run(Vector2 origin)
    {
        Vector2 corner = new Vector2(Mathf.FloorToInt((origin.x + WorldPlan.Half) / 2048f) * 2048f - WorldPlan.Half,
            Mathf.FloorToInt((origin.y + WorldPlan.Half) / 2048f) * 2048f - WorldPlan.Half);
        var fine = Build(corner, 16);
        int matches = 0;
        foreach (int step in new[] { 8, 16, 64, 128, 256 })
        {
            var neighbour = Build(corner + new Vector2(2048f, 0f), step);
            foreach (var p in Boundary(fine, corner.x + 2048f - origin.x))
            {
                bool found = false;
                for (int k = 0; k < neighbour.vertices.Length; k++)
                    if (neighbour.vertices[k].x == p.Value.x && neighbour.vertices[k].z == p.Value.z)
                    {
                        if (Vector3.Distance(neighbour.vertices[k], p.Value) > 0.0001f ||
                            Vector3.Distance(neighbour.normals[k], fine.normals[p.Key]) > 0.0001f)
                            throw new Exception("Mismatched HLOD perimeter");
                        found = true; matches++; break;
                    }
                if (!found) throw new Exception("Missing HLOD perimeter sample");
            }
        }
        // A jump exposes nine nearby coarse districts. Count generation-only 0.30 ms slices at 60 Hz.
        var times = new List<double>(); int frames = 0, firstFrames = 0, worstFrames = 0;
        double longest = 0, total = 0;
        for (int block = 0; block < 9; block++)
        {
            var data = new ForestHlod.Data { corner = corner + new Vector2((block % 3 - 1) * 2048f, (block / 3 - 1) * 2048f), step = 256 };
            var work = ForestHlod.Build(data, null); bool done = false; int count = 0;
            while (!done)
            {
                var timer = Stopwatch.StartNew();
                do { done = !work.MoveNext(); } while (!done && timer.Elapsed.TotalMilliseconds < 0.30);
                double ms = timer.Elapsed.TotalMilliseconds;
                longest = Math.Max(longest, ms); total += ms; times.Add(ms); frames++; count++;
            }
            if (block == 0) firstFrames = frames;
            worstFrames = Math.Max(worstFrames, count);
        }
        double fineLum = Mean(fine), coarseLum = Mean(Build(corner, 128));
        double tierRatio = coarseLum / fineLum;
        if (tierRatio < .90 || tierRatio > 1.10) throw new Exception("HLOD luminance hand-over exceeds 10 percent");
        Console.WriteLine($"SEAMS {matches} matching vertices/normals; tier luminance {tierRatio:F4}; coarse first {firstFrames} frames; generation slices mean {total / frames:F3}, max {longest:F3} ms");
        return new { boundaryMatches = matches, coarseFineLuminance = tierRatio, generationOnly = new { firstFrames, allNineFrames = frames,
            worstBlockFrames = worstFrames, meanMs = total / frames, worstMs = longest, slices = times }, nativeUploadAndGpuMeasured = false };
    }

    // Production builders run unchanged; the managed harness only replaces engine storage.
    static ForestHlod.Data Build(Vector2 corner, int step)
    {
        var data = new ForestHlod.Data { corner = corner, step = step };
        var work = ForestHlod.Build(data, null); while (work.MoveNext()) { }
        return data;
    }

    // Capture the shared east edge, rather than depending on either mesh's vertex ordering.
    static Dictionary<int, Vector3> Boundary(ForestHlod.Data data, float x)
    {
        var result = new Dictionary<int, Vector3>();
        for (int i = 0; i < data.vertices.Length; i++) if (data.vertices[i].x == x) result.Add(i, data.vertices[i]);
        return result;
    }

    // Compare mean linear-light luminance over corresponding world footprints, excluding texture gutters.
    static double Mean(ForestHlod.Data data)
    {
        double sum = 0; int count = 0, n = data.textureSize;
        for (int j = 2; j < n - 2; j++) for (int i = 2; i < n - 2; i++)
        {
            Color c = ((Color)data.pixels[j * n + i]).linear;
            sum += .2126 * c.r + .7152 * c.g + .0722 * c.b; count++;
        }
        return sum / count;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;

// Whole-world cold district publication runs separately from image rendering and sector construction.
static class PictureBench
{
    // Restrict parallelism to repeat player-like CPU availability and record warm versus genuinely cold paints.
    public static void Run(string output)
    {
        WorldPlan.Generate(1200);
        var samples = new List<object>();
        for (int run = 0; run < 3; run++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            var timer = Stopwatch.StartNew(); var data = WorldPlan.CaptureDetail(null);
            double snapshot = timer.Elapsed.TotalMilliseconds;
            var pixels = data.WorldPixels(4096); double cold = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); data.WorldPixels(4096); double warm = timer.Elapsed.TotalMilliseconds;
            samples.Add(new { snapshot, cold, warm, pixels = pixels.Length });
            Console.WriteLine($"Picture {run}: snapshot {snapshot:F1}, cold {cold:F1}, warm {warm:F1} ms");
        }
        File.WriteAllText(Path.Combine(output, "picture.json"), JsonSerializer.Serialize(samples));
    }
}

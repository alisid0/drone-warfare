using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using DroneSandbox;

// Unity ships this Mono JIT; using it exposes costs hidden by the much newer desktop .NET optimizer.
static class PictureMono
{
    // Paint all 16.8 million pixels with fresh snapshots, including first-touch district clipping.
    static void Main(string[] args)
    {
        WorldPlan.Generate(1200); var lines = new List<string>();
        for (int run = 0; run < (args.Length > 1 ? int.Parse(args[1]) : 3); run++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            var process = Process.GetCurrentProcess(); double cpuStart = process.TotalProcessorTime.TotalMilliseconds;
            var timer = Stopwatch.StartNew(); var data = WorldPlan.CaptureDetail(null);
            double snapshot = timer.Elapsed.TotalMilliseconds;
            data.WorldPixels(4096); double cold = timer.Elapsed.TotalMilliseconds;
            double coldCpu = process.TotalProcessorTime.TotalMilliseconds - cpuStart;
            cpuStart = process.TotalProcessorTime.TotalMilliseconds;
            timer.Restart(); data.WorldPixels(4096); double warm = timer.Elapsed.TotalMilliseconds;
            double warmCpu = process.TotalProcessorTime.TotalMilliseconds - cpuStart;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            string line = "{\"run\":" + run + ",\"snapshot\":" + snapshot.ToString("F2", culture)
                + ",\"cold\":" + cold.ToString("F2", culture) + ",\"warm\":" + warm.ToString("F2", culture)
                + ",\"coldCpu\":" + coldCpu.ToString("F2", culture) + ",\"warmCpu\":" + warmCpu.ToString("F2", culture) + "}";
            lines.Add(line); Console.WriteLine(line);
        }
        File.WriteAllText(args[0], "[" + string.Join(",", lines) + "]");
    }
}

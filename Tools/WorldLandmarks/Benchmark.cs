using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Identical cold/warm generation measurements run against the base commit and the current production sources.
static class Benchmark
{
    // Record pre-existing world-generation failures explicitly instead of hiding skipped seeds.
    static int Main(string[] args)
    {
        var results = new List<object>();
        for (int seed = 1199; seed <= 1220; seed++)
        {
            var timer = Stopwatch.StartNew(); string error = null;
            try { WorldPlan.Generate(seed); }
            catch (Exception ex) { error = ex.Message; }
            double ms = timer.Elapsed.TotalMilliseconds;
            results.Add(new { seed, milliseconds = ms, error });
            Console.WriteLine(seed + " " + ms.ToString("F1") + " ms " + error);
        }
        File.WriteAllText(args[0], JsonSerializer.Serialize(results));
        WorldPlan.Generate(1200);
        var sectors = new List<object>();
        foreach (string kind in new[] { "IndustrialZone", "Airfield", "RailJunction" })
        {
            using var data = JsonDocument.Parse(File.ReadAllText("Temp/world-landmarks/results/" + kind + ".json"));
            var c = data.RootElement.GetProperty("center"); var center = new Vector2(c[0].GetSingle(), c[1].GetSingle());
            for (int sample = 0; sample < 3; sample++)
            {
                double planning = 0, building = 0; string error = null;
                try
                {
                    var timer = Stopwatch.StartNew(); var plan = WorldSectors.PlanAt(center, (SectorKind)Enum.Parse(typeof(SectorKind), kind), 7400 + data.RootElement.GetProperty("id").GetInt32());
                    planning = timer.Elapsed.TotalMilliseconds;
                    Art.placed.Clear(); Mesh.All.Clear(); MapLayout.Use(plan); timer.Restart(); World.Build(new GameObject("Benchmark").transform, true); building = timer.Elapsed.TotalMilliseconds;
                }
                catch (Exception ex) { error = ex.Message; }
                sectors.Add(new { kind, sample, planMs = planning, buildMs = building, error });
                Console.WriteLine(kind + " " + sample + " plan " + planning.ToString("F1") + " build " + building.ToString("F1") + " " + error);
            }
        }
        File.WriteAllText(args[0].Replace(".json", "-sectors.json"), JsonSerializer.Serialize(sectors)); return 0;
    }
}

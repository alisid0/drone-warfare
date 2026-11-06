using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using UnityEngine.Rendering;

// Drive the real renderer through loading, steady flight, teleporting and material failure using recorded native calls.
static class RenderChecks
{
    // Synthetic mesh triangle costs are conservative; native shader execution and actual camera frusta are not simulated.
    static void Main(string[] args)
    {
        WorldPlan.Generate(1200);
        var hotspot = new Hotspot { kind = SectorKind.DeepForest, seed = 1037 };
        var origin = WorldSectors.SpotFor(hotspot); FarTerrain.Origin = origin;
        MapLayout.Use(WorldSectors.PlanAt(origin, SectorKind.DeepForest, 1037));
        World.Build(new GameObject("World").transform, true);
        var scatter = new GameObject("Far Terrain").AddComponent<FarScatter>();
        typeof(FarScatter).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scatter, null);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        var stem = SectorForest.Stems[SectorForest.Stems.Count / 2];
        camera.transform.position = stem.position + Vector3.up * 42f;
        for (int frame = 0; frame < 600; frame++) Tick(camera);
        Require(FarScatter.TreesDrawn > 0, "Trees must populate after startup");
        Require(ForestHlod.BlocksDrawn > 0 && ForestHlod.MissingBlocks == 0, "Coverage must settle");
        int firstTrees = FarScatter.TreesDrawn;
        scatter.Clear();
        Tick(camera);
        Require(ForestHlod.BlocksDrawn > 0, "Clear must re-adopt unconsumed sector canopy");
        for (int frame = 0; frame < 600; frame++) Tick(camera);
        FarScatter.ResetTimings();
        var times = new List<float>(); int zeroRecordFrames = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            var p = camera.transform.position; p.x += 40f / 60f; p.y = FarScatter.GroundAt(new Vector2(p.x, p.z)) + 42f;
            camera.transform.position = p; Tick(camera); times.Add(FarScatter.MainThreadMs);
            if (FarScatter.RecordsVisited == 0) zeroRecordFrames++;
            Require(FarScatter.FullTreesDrawn <= ForestBudget.FullLimit && FarScatter.ReducedTreesDrawn <= ForestBudget.ReducedLimit
                && FarScatter.CardTreesDrawn <= ForestBudget.CardLimit && FarScatter.TreeTrianglesDrawn <= ForestBudget.TriangleLimit, "Submitted budgets");
        }
        float mean = FarScatter.MeanMs, worst = FarScatter.WorstMs;
        times.Sort();
        camera.transform.position += new Vector3(2500f, 2458f, 0f); camera.reach = 7000f;
        Tick(camera);
        Require(FarScatter.TreesDrawn == 0 && FarScatter.TierDistances.z == 0f, "Teleport must not reuse old tree bubble");
        int settle = 0;
        while (ForestHlod.MissingBlocks > 0 && settle < 180) { Tick(camera); settle++; }
        Require(ForestHlod.MissingBlocks == 0, "Jump coverage must arrive within 180 simulated frames");
        foreach (int failure in new[] { 0, 1, 2, 3 })
        {
            ForestMaterials.ForceFallback = failure == 0; Shader.Missing = failure == 1;
            Shader.Unsupported = failure == 2; Shader.MissingProperty = failure == 3;
            var material = ForestMaterials.Create("LeavesOak", null, 0);
            Require(material.shader.name == "HDRP/Lit" && material.enableInstancing && !ForestMaterials.Fades(material), "Lit fallback for failure " + failure);
        }
        var result = new { firstTrees, flight = new { frames = 600, meanMs = mean, p95Ms = times[569], worstMs = worst, zeroRecordFrames },
            jumpCoverageFrames = settle, nativeUploadsAndGpuMeasured = false };
        File.WriteAllText(args[0], JsonSerializer.Serialize(result)); Console.WriteLine(JsonSerializer.Serialize(result));
    }

    // Advance a simulated 60 Hz clock while the production stopwatch budgets still measure real managed work.
    static void Tick(Camera camera)
    {
        Time.frameCount++; Time.unscaledTime = Time.frameCount / 60f;
        RenderPipelineManager.Render(camera);
    }

    // Fail the executable immediately instead of publishing an incomplete review as a pass.
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}

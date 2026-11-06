using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Execute the production woodland builders with managed terrain storage, and export matched offline evidence.
static class Harness
{
    // The original tour's Deep Forest seed is kept fixed for both revisions.
    static void Main(string[] args)
    {
        string output = args[0]; Directory.CreateDirectory(output);
        WorldPlan.Generate(1200);
        var hotspot = new Hotspot { kind = SectorKind.DeepForest, seed = 1037 };
        Vector2 origin = WorldSectors.SpotFor(hotspot);
        var plan = WorldSectors.PlanAt(origin, SectorKind.DeepForest, 1037);
        FarTerrain.Origin = origin; MapLayout.Use(plan);
        var timer = Stopwatch.StartNew();
        World.Build(new GameObject("Forest test").transform, true);
        double build = timer.Elapsed.TotalMilliseconds;
        var trees = new List<object>();
        foreach (var tree in Art.placed)
            if (tree.model.StartsWith("Scenery_Tree") || tree.model.StartsWith("Scenery_Birch") || tree.model.StartsWith("Scenery_Pine") || tree.model.StartsWith("Scenery_Spruce") || tree.model.StartsWith("Scenery_Poplar"))
                trees.Add(new { model = tree.model, p = V(tree.root.position), scale = tree.mesh.transform.localScale.x, yaw = V(tree.root.rotation.forward) });
        var logical = new List<object>();
        foreach (var stem in SectorForest.Stems)
            logical.Add(new { p = V(stem.position), stem.stand.species, stem.stand.shade, stem.stand.height, stem.stand.radius, stem.stand.width, stem.stand.cover, yaw = ForestPattern.Unit(stem.i, stem.j, 44) * 360f });
        var heights = new List<float>(); var floor = new List<float>();
        for (int z = 0; z <= 250; z++) for (int x = 0; x <= 250; x++)
        {
            heights.Add(Ground.HeightAt(new Vector3(x * 8f - 1000f, 0f, z * 8f - 1000f)));
            floor.Add(Ground.CanopyAt(x * 8f - 1000f, z * 8f - 1000f));
        }
        var roads = new List<List<float[]>>();
        foreach (var line in plan.roads)
        {
            var points = new List<float[]>(); foreach (var p in line) points.Add(new[] { p.x, p.y }); roads.Add(points);
        }
        Save(output + "/scene.json", new { origin = new[] { origin.x, origin.y }, baseAt = V(plan.basePosition), hq = V(plan.enemyHqPosition), heights, floor, roads, trees, logical, views = Views(plan) });
        Landscape(output, origin);
        if (logical.Count > 0) Bake(output, origin);
#if CANDIDATE
        Save(output + "/checks.json", Checks.Run(origin));
#endif
        Save(output + "/metrics.json", new { buildMs = build, woodsMs = SectorForest.BuildMilliseconds, World.Timings, renderedObjects = trees.Count, logical = logical.Count, pattern = logical.Count > 0 ? Pattern() : null, flight = CheckFlight() });
        Console.WriteLine(World.Timings + "; logical " + logical.Count + "; " + SectorForest.BuildMilliseconds.ToString("F1") + " ms");
        if (logical.Count > 0)
        {
            Canopy.Build(new GameObject("Logical canopy test").transform);
#if CANDIDATE
            Save(output + "/crown-queries.json", CrownChecks.Run());
#endif
            for (int k = 0; k < SectorForest.Stems.Count; k += 317)
            {
                var stem = SectorForest.Stems[k]; var p = stem.position;
                Require(Canopy.TopNear(p, 1f) >= p.y + stem.stand.height, "Drone clearance");
                Vector3 a = p + new Vector3(-10f, stem.stand.height * 0.5f, 0f), b = p + new Vector3(10f, stem.stand.height * 0.5f, 0f);
                Require(Canopy.Blocks(a, b), "Crown line of sight");
            }
            foreach (var stem in SectorForest.Stems)
                if (Math.Max(Math.Abs(stem.position.x), Math.Abs(stem.position.z)) >= MapLayout.Edge)
                {
                    Require(ForestPattern.Tree(stem.i, stem.j, out _, out var outside), "World seam occupancy");
                    Require(outside.height == stem.stand.height && outside.radius == stem.stand.radius && outside.species == stem.stand.species, "World seam stand identity");
                }
            ulong fingerprint = Fingerprint();
            Undergrowth.Reset(); SectorForest.Build(new Random(plan.worldSeed), new List<Vector2>());
            Require(Fingerprint() == fingerprint, "Parallel rebuild determinism");
            Console.WriteLine("PASS: repeat build, logical occupancy, tier identity, 101 flight budgets, density, canopy, glades and proposed Canopy integration");
        }
    }

    // Measure projected crown coverage and stem density, including irregular glades, in a 100 ha interior stand.
    static object Pattern()
    {
        var occupancy = new bool[1000 * 1000]; int stems = 0, covered = 0, glade = 0;
        var positions = new List<Vector2>();
        for (int j = 0; j < 200; j++) for (int i = 0; i < 200; i++)
        {
            if (!ForestPattern.Tree(i, j, 1f, out Vector2 p, out var stand)) continue;
            stems++; positions.Add(p);
            for (int z = Math.Max(0, (int)(p.y - stand.radius)); z <= Math.Min(999, (int)(p.y + stand.radius)); z++)
                for (int x = Math.Max(0, (int)(p.x - stand.radius)); x <= Math.Min(999, (int)(p.x + stand.radius)); x++)
                    if ((new Vector2(x + 0.5f, z + 0.5f) - p).sqrMagnitude <= stand.radius * stand.radius) occupancy[z * 1000 + x] = true;
        }
        for (int z = 0; z < 1000; z++) for (int x = 0; x < 1000; x++)
        {
            if (occupancy[z * 1000 + x]) covered++;
            if (ForestPattern.Glade(x + 0.5f, z + 0.5f) <= 0.01f) glade++;
        }
        Require(stems >= 20000 && stems <= 45000, "Stem density");
        Require(covered >= 650000 && covered <= 850000, "Canopy cover");
        Require(glade >= 50000 && glade <= 120000, "Glade area");
        Console.WriteLine($"PATTERN {stems / 100f:F1}/ha; canopy {covered / 10000f:F1}%; glades {glade / 10000f:F1}%");
        return new { stemsPerHa = stems / 100f, canopyPercent = covered / 10000f, gladePercent = glade / 10000f };
    }

    // Exercise the actual budget selector on a 400 m path at 40 m/s, counting both sides of every fade.
    static object CheckFlight()
    {
        var entries = new List<ForestBudget.Entry>();
        int fullPeak = 0, reducedPeak = 0, cardPeak = 0, trianglePeak = 0;
        int[] fullCost = { 10282, 6780, 23134, 7410 };
        for (int frame = 0; frame <= 100; frame++)
        {
            Vector3 camera = new Vector3(-200f + frame * 4f, 0f, 250f);
            camera.y = Ground.HeightAt(camera) + 15f; entries.Clear();
            foreach (var stem in SectorForest.Stems)
            {
                float distance = Vector3.Distance(camera, stem.position);
                if (distance > 366f) continue;
                entries.Add(new ForestBudget.Entry { distance = distance, fullTriangles = fullCost[stem.stand.species], reducedTriangles = 3000, cardTriangles = 8 });
                Require(Undergrowth.UnderTrees(new Vector2(stem.position.x, stem.position.z), 0.1f), "Logical occupancy");
                Require(SectorForest.Tree(stem.i, stem.j, out _, out var stand) && stand.height == stem.stand.height, "Render/logical identity");
            }
            int guard = 0;
#if CANDIDATE
            guard = 16;
            var bins = new ForestBudget.Bucket[400];
            foreach (var entry in entries)
            {
                int k = Mathf.FloorToInt(entry.distance); var b = bins[k];
                b.full++; b.reduced++; b.cards++;
                b.fullTriangles += entry.fullTriangles; b.reducedTriangles += entry.reducedTriangles; b.cardTriangles += entry.cardTriangles;
                bins[k] = b;
            }
            Vector4 ranges = ForestBudget.SelectBuckets(bins, 350f, guard);
#else
            entries.Sort((a, b) => a.distance.CompareTo(b.distance));
            Vector4 ranges = ForestBudget.Select(entries);
#endif
            int full = 0, reduced = 0, cards = 0, triangles = 0;
            foreach (var entry in entries)
            {
                if (ranges.x > 0f && entry.distance <= ranges.x + guard) { full++; triangles += entry.fullTriangles; }
                if (ranges.y > 0f && entry.distance >= ranges.x - 10f - guard && entry.distance <= ranges.y + guard) { reduced++; triangles += entry.reducedTriangles; }
                if (ranges.z > 0f && entry.distance >= ranges.y - 20f - guard && entry.distance <= ranges.z + guard) { cards++; triangles += 8; }
            }
            Require(full <= ForestBudget.FullLimit && reduced <= ForestBudget.ReducedLimit && cards <= ForestBudget.CardLimit && triangles <= ForestBudget.TriangleLimit, "Camera submission budgets");
            fullPeak = Math.Max(fullPeak, full); reducedPeak = Math.Max(reducedPeak, reduced);
            cardPeak = Math.Max(cardPeak, cards); trianglePeak = Math.Max(trianglePeak, triangles);
        }
        return new { samples = 101, speed = 40, fullPeak, reducedPeak, cardPeak, trianglePeak };
    }

    // Export the real canopy builder and its colours/normals for the matched offline comparison.
    static void Bake(string output, Vector2 origin)
    {
        int count = 0, triangles = 0;
        int x0 = Mathf.FloorToInt((origin.x - 1000f + WorldPlan.Half) / 2048f), z0 = Mathf.FloorToInt((origin.y - 1000f + WorldPlan.Half) / 2048f);
        int x1 = Mathf.FloorToInt((origin.x + 1000f + WorldPlan.Half) / 2048f), z1 = Mathf.FloorToInt((origin.y + 1000f + WorldPlan.Half) / 2048f);
        for (int j = z0; j <= z1; j++) for (int i = x0; i <= x1; i++)
        {
            var data = new ForestHlod.Data { corner = new Vector2(i * 2048f - WorldPlan.Half, j * 2048f - WorldPlan.Half), step = 16 };
            var work = ForestHlod.Build(data, null); while (work.MoveNext()) { }
            var vertices = new List<float[]>(); var uv = new List<float[]>(); var normals = new List<float[]>();
            foreach (var p in data.vertices) vertices.Add(V(p));
            foreach (var p in data.normals) normals.Add(V(p));
            foreach (var p in data.uv) uv.Add(new[] { p.x, p.y });
            Save(output + "/canopy-" + count + ".json", new { vertices, normals, uv, indices = data.triangles });
            using (var stream = new FileStream(output + "/canopy-" + count + ".ppm", FileMode.Create))
            {
                byte[] header = System.Text.Encoding.ASCII.GetBytes($"P6\n{data.textureSize} {data.textureSize}\n255\n"); stream.Write(header);
                foreach (var pixel in data.pixels) { stream.WriteByte(pixel.r); stream.WriteByte(pixel.g); stream.WriteByte(pixel.b); }
            }
            triangles += data.triangles.Length / 3; count++;
        }
        Save(output + "/canopy-budget.json", new { blocks = count, triangles });
#if CANDIDATE
        int closeTriangles = 0; var closeTimer = Stopwatch.StartNew();
        for (int j = z0; j <= z1; j++) for (int i = x0; i <= x1; i++)
        {
            var data = new ForestHlod.Data { corner = new Vector2(i * 2048f - WorldPlan.Half, j * 2048f - WorldPlan.Half), step = 8 };
            var work = ForestHlod.Build(data, null); while (work.MoveNext()) { }
            closeTriangles += data.triangles.Length / 3;
            Export(output + "/close-" + j + "-" + i, data);
        }
        Save(output + "/close-budget.json", new { triangles = closeTriangles, ms = closeTimer.Elapsed.TotalMilliseconds });
#endif
        var budgets = new List<object>();
        for (int j = -3; j <= 3; j++) for (int i = -3; i <= 3; i++)
        {
            var data = new ForestHlod.Data { corner = new Vector2((x0 + i) * 2048f - WorldPlan.Half, (z0 + j) * 2048f - WorldPlan.Half), step = 128 };
            var timer = Stopwatch.StartNew();
            var work = ForestHlod.Build(data, null); while (work.MoveNext()) { }
            budgets.Add(new { x = i, z = j, step = 128, ms = timer.Elapsed.TotalMilliseconds, triangles = data.triangles.Length / 3 });
            Export(output + "/distant-" + (j + 3) + "-" + (i + 3), data);
        }
        Save(output + "/distant-budget.json", budgets);
    }

    // The 9 km camera needs a real surrounding landscape, not a magnified view of a 2 km square crop.
    static void Landscape(string output, Vector2 origin)
    {
        const int n = 225; const float step = 64f, half = (n - 1) * step * 0.5f;
        var heights = new float[n * n]; var covers = new int[n * n];
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
        {
            float x = i * step - half, z = j * step - half;
            heights[j * n + i] = FarScatter.GroundAt(new Vector2(x, z));
            covers[j * n + i] = (int)WorldPlan.CoverAt(x + origin.x, z + origin.y);
        }
        Save(output + "/landscape.json", new { n, step, half, heights, covers });
    }

    // Keep the exported texture and mesh unmodified; Blender performs only coordinate-system conversion.
    static void Export(string path, ForestHlod.Data data)
    {
        var vertices = new List<float[]>(); var normals = new List<float[]>(); var uv = new List<float[]>();
        foreach (var p in data.vertices) vertices.Add(V(p));
        foreach (var p in data.normals) normals.Add(V(p));
        foreach (var p in data.uv) uv.Add(new[] { p.x, p.y });
        Save(path + ".json", new { vertices, normals, uv, indices = data.triangles });
        if (data.pixels == null) return;
        using (var stream = new FileStream(path + ".ppm", FileMode.Create))
        {
            stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{data.textureSize} {data.textureSize}\n255\n"));
            foreach (var pixel in data.pixels) { stream.WriteByte(pixel.r); stream.WriteByte(pixel.g); stream.WriteByte(pixel.b); }
        }
    }

    // Fixed camera positions and the production selector make the requested views repeatable.
    static List<object> Views(SectorPlan plan)
    {
        var result = new List<object>();
        Vector3 back = (plan.basePosition - plan.enemyHqPosition).normalized;
        Vector3 target = Ground.OnGround((plan.basePosition + plan.enemyHqPosition) * 0.5f);
        int[] costs = { 10282, 6780, 23134, 7410 };
        foreach (float altitude in new[] { 300f, 1000f, 2500f, 9000f })
        {
            Vector3 eye = plan.basePosition + back * 60f; eye.y = Ground.HeightAt(eye) + altitude;
            var entries = new List<ForestBudget.Entry>();
            foreach (var stem in SectorForest.Stems)
            {
                float distance = Vector3.Distance(eye, stem.position);
                if (distance <= 350f) entries.Add(new ForestBudget.Entry { distance = distance, fullTriangles = costs[stem.stand.species], reducedTriangles = 3000, cardTriangles = 8 });
            }
            entries.Sort((a, b) => a.distance.CompareTo(b.distance));
            Vector4 ranges = ForestBudget.Select(entries);
            result.Add(new { altitude, eye = V(eye), target = V(target), ranges = new[] { ranges.x, ranges.y, ranges.z } });
        }
        return result;
    }

    // Hash ordered positions and stand dimensions; a different worker schedule must not change a single stem.
    static ulong Fingerprint()
    {
        ulong hash = 14695981039346656037UL;
        foreach (var s in SectorForest.Stems)
            foreach (float f in new[] { s.position.x, s.position.y, s.position.z, s.stand.height, s.stand.radius, s.stand.species })
                hash = unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(f)) * 1099511628211UL);
        return hash;
    }

    // Fail the harness on a broken gameplay or rendering contract, rather than merely logging it.
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Numeric arrays are intentionally engine-independent for Blender's matching camera and transform import.
    static float[] V(Vector3 p) => new[] { p.x, p.y, p.z };

    // Save compact snapshots instead of placing hundreds of thousands of test GameObjects in Unity.
    static void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true }));
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

// Exercise actual forest construction and selection without launching Unity; export geometry for matched offline views.
static class Harness
{
    static Camera feed;
    static int checks;

    // Exit promptly on failure so an unattended review never hangs in Windows crash reporting.
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }

    // Build one sector per process, and use identical camera coordinates for both revisions.
    static void Run(string[] args)
    {
        string output = args[0], kind = args[1]; Directory.CreateDirectory(output);
        SectorPlan plan;
        if (kind == "glade")
        {
            WorldPlan.Generate(1200);
            FarTerrain.Origin = WorldSectors.SpotFor(new Hotspot { kind = SectorKind.DeepForest, seed = 1037 });
            plan = WorldSectors.PlanAt(FarTerrain.Origin, SectorKind.DeepForest, 1037);
        }
        else { plan = SectorPlan.Varrow(); FarTerrain.Active = false; FarTerrain.Origin = Vector2.zero; }
        MapLayout.Use(plan); World.Build(new GameObject("Glade review").transform, true);
        Console.WriteLine(World.Timings);
        string posePath = Path.Combine(Path.GetDirectoryName(output), kind + "-pose.json");
        Vector3 target;
        if (args[2] == "before")
        {
            target = kind == "glade" ? FindGlade() : FindEdge();
            target.y = Ground.HeightAt(target); Save(posePath, V(target));
        }
        else { var p = JsonSerializer.Deserialize<float[]>(File.ReadAllText(posePath)); target = new Vector3(p[0], p[1], p[2]); }
        if (args.Length > 3 && args[3] == "--geometry") { CheckGeometry(target, output); return; }
        var scatter = new GameObject("Far Terrain").AddComponent<FarScatter>();
        typeof(FarScatter).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scatter, null);
        var camera = new GameObject("Review camera").AddComponent<Camera>();
        feed = new GameObject("Drone feed").AddComponent<Camera>(); feed.pixelHeight = 270; feed.reach = 2500;
        foreach (int height in new[] { 140, 300 })
        {
            camera.transform.position = target + new Vector3(0, height, -height);
            camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
            for (int frame = 0; frame < 900; frame++) Tick(camera);
            Export(output + "-" + height + "m", plan, target, camera, scatter);
        }
        CheckGeometry(target, output);
#if CANDIDATE
        CheckRecipe(output);
#endif
        camera.transform.position = target + new Vector3(-27.87f, 50.18f, -48.27f);
        camera.transform.rotation = Quaternion.LookRotation(new Vector3(27.87f, -50.18f, 48.27f));
        for (int frame = 0; frame < 360; frame++) Tick(camera);
        FarScatter.ResetTimings(); ForestHlod.ResetTimings();
        var times = new List<float>(); int peakTree = 0, peakCanopy = 0;
        for (int frame = 0; frame < 360; frame++)
        {
            var p = camera.transform.position; p.x += 4f; p.y = FarScatter.GroundAt(new Vector2(p.x, p.z)) + 50.18f;
            camera.transform.position = p; Tick(camera); times.Add(FarScatter.MainThreadMs);
            if (frame >= 60)
            {
                peakTree = Math.Max(peakTree, FarScatter.TreeTrianglesDrawn); peakCanopy = Math.Max(peakCanopy, ForestHlod.TrianglesDrawn);
                Require(FarScatter.TreeTrianglesDrawn <= 650000 && FarScatter.FullTreesDrawn <= 64
                    && FarScatter.ReducedTreesDrawn <= 192 && FarScatter.CardTreesDrawn <= 1800, "Fast-flight policy exceeded");
            }
            Require(FarScatter.FullTreesDrawn <= ForestBudget.FullLimit && FarScatter.ReducedTreesDrawn <= ForestBudget.ReducedLimit
                && FarScatter.CardTreesDrawn <= ForestBudget.CardLimit && FarScatter.TreeTrianglesDrawn <= ForestBudget.TriangleLimit, "Tree budget exceeded");
        }
        times.Sort();
        var metrics = new { logical = SectorForest.Stems.Count, managedP95Ms = times[341], peakTree, peakCanopy,
            SectorForest.BuildMilliseconds, ForestHlod.BuildMeanMs, ForestHlod.BuildWorstMs, checks, nativeGpuMeasured = false };
        Save(output + "/metrics.json", metrics); Console.WriteLine(JsonSerializer.Serialize(metrics));
    }

    // Choose an existing deterministic glade centre inside dense woodland, away from the crop boundary.
    static Vector3 FindGlade()
    {
        Vector2 best = Vector2.zero; float score = float.MaxValue;
        for (int z = -6; z <= 6; z++) for (int x = -6; x <= 6; x++)
        {
            int i = Mathf.FloorToInt(FarTerrain.Origin.x / 192f) + x, j = Mathf.FloorToInt(FarTerrain.Origin.y / 192f) + z;
            if (ForestPattern.Unit(i, j, 60) > 0.9f) continue;
            var p = new Vector2((i + ForestPattern.Unit(i, j, 61)) * 192f, (j + ForestPattern.Unit(i, j, 62)) * 192f) - FarTerrain.Origin;
            if (Mathf.Abs(p.x) > 550f || Mathf.Abs(p.y) > 550f || SectorForest.Edge(p) < 0.99f) continue;
            if (p.sqrMagnitude < score) { score = p.sqrMagnitude; best = p; }
        }
        Require(score < float.MaxValue, "No glade found"); return new Vector3(best.x, 0, best.y);
    }

    // Find a real wood next to a field rather than constructing a specially planted demonstration grove.
    static Vector3 FindEdge()
    {
        Vector2 best = Vector2.zero; float score = float.MaxValue;
        for (int z = -600; z <= 600; z += 8) for (int x = -600; x <= 600; x += 8)
        {
            var p = new Vector2(x, z);
            if (SectorForest.At(x, z).cover < 0.65f || !MapLayout.InField(p, 30f)) continue;
            float s = (p - new Vector2(-180, -120)).sqrMagnitude;
            if (s < score) { score = s; best = p; }
        }
        Require(score < float.MaxValue, "No farmland edge found"); return new Vector3(best.x, 0, best.y);
    }

    // Check all surface tiers for vertical faces, exposed raised boundaries and consistent shared block heights.
    static void CheckGeometry(Vector3 target, string output)
    {
        var rows = new List<object>();
        Vector2 world = new Vector2(target.x, target.z) + FarTerrain.Origin;
        Vector2 corner = new Vector2(Mathf.FloorToInt((world.x + WorldPlan.Half) / ForestHlod.BlockSize) * ForestHlod.BlockSize - WorldPlan.Half,
            Mathf.FloorToInt((world.y + WorldPlan.Half) / ForestHlod.BlockSize) * ForestHlod.BlockSize - WorldPlan.Half);
        foreach (int step in new[] { 256, 64, 32, 16 })
        {
            var data = new ForestHlod.Data { corner = corner, step = step, local = MapLayout.Plan.world == null };
            var build = ForestHlod.Build(data, null); while (build.MoveNext()) { }
            int vertical = 0; float steepest = 0;
            var edges = new Dictionary<(int, int), int>();
            for (int k = 0; k < data.triangles.Length; k += 3)
            {
                int a = data.triangles[k], b = data.triangles[k + 1], c = data.triangles[k + 2];
                var n = Vector3.Cross(data.vertices[b] - data.vertices[a], data.vertices[c] - data.vertices[a]);
                if (Mathf.Abs(n.y) < 0.001f) vertical++;
                for (int side = 0; side < 3; side++)
                {
                    int u = data.triangles[k + side], v = data.triangles[k + (side + 1) % 3];
                    var key = (Math.Min(u, v), Math.Max(u, v)); edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
                    Vector3 delta = data.vertices[v] - data.vertices[u]; float run = new Vector2(delta.x, delta.z).magnitude;
                    if (run > 0f) steepest = Mathf.Max(steepest, Mathf.Abs(data.crown[v] - data.crown[u]) / run);
                }
            }
#if CANDIDATE
            Require(vertical == 0, "Vertical faces in tier " + step);
            foreach (var edge in edges)
            {
                Require(edge.Value <= 2, "Overlapping canopy faces");
                if (edge.Value == 2) continue;
                int a = edge.Key.Item1, b = edge.Key.Item2;
                Vector3 va = data.vertices[a], vb = data.vertices[b]; Vector2 lo = corner - FarTerrain.Origin;
                if (va.x == vb.x && (va.x == lo.x || va.x == lo.x + ForestHlod.BlockSize)
                    || va.z == vb.z && (va.z == lo.y || va.z == lo.y + ForestHlod.BlockSize)) continue;
                Require(Mathf.Max(data.crown[a], data.crown[b]) < 0.251f, "Raised unmatched canopy edge");
            }
#endif
            rows.Add(new { step, verticalFaces = vertical, steepestRelief = steepest, triangles = data.triangles.Length / 3 });
        }
        Save(output + "/geometry.json", rows);
    }

#if CANDIDATE
    // Independently check opening area, floor height, convexity and retained young crowns on the production recipe.
    static void CheckRecipe(string output)
    {
        int empty = 0, samples = 0, young = 0, leaning = 0;
        for (int z = -1024; z < 1024; z += 4) for (int x = -1024; x < 1024; x += 4)
        {
            var stand = ForestPattern.At(x, z, 1f); samples++;
            if (stand.cover == 0f) { empty++; Require(ForestPattern.Canopy(stand, x, z) == 0f, "Glade retains a raised slab"); }
        }
        for (int k = 1; k < 100; k++) Require(ForestPattern.Rim((k - 1) / 100f) + ForestPattern.Rim((k + 1) / 100f)
            >= 2 * ForestPattern.Rim(k / 100f) - 0.00001f, "Rim is not convex");
        foreach (var stem in SectorForest.Stems)
        {
            if (stem.stand.height < 5f) young++;
            if (stem.stand.lean.sqrMagnitude > 0.0001f) leaning++;
            Require(stem.stand.radius >= 2f, "Conservative fade radius exceeded");
            Require(SectorForest.Edge(new Vector2(stem.position.x, stem.position.z)) > 0, "Tree in protected land");
        }
        // Revisit accepted candidates out of generation order; lean and dimensions must not depend on prior queries.
        for (int k = SectorForest.Stems.Count - 1; k >= 0; k -= Math.Max(1, SectorForest.Stems.Count / 128))
        {
            var stem = SectorForest.Stems[k]; var p = new Vector2(stem.position.x, stem.position.z);
            Require(ForestPattern.Tree(stem.i, stem.j, SectorForest.Edge(p), out _, out var repeated), "Accepted tree did not repeat");
            Require(repeated.height == stem.stand.height && repeated.radius == stem.stand.radius && repeated.species == stem.stand.species
                && (repeated.lean - stem.stand.lean).sqrMagnitude == 0f, "Tree dimensions or lean depend on query order");
        }
        Require(young > 0 && leaning > 0, "No young or leaning margin crowns");
        Save(output + "/recipe.json", new { emptyFraction = empty / (float)samples, young, leaning });
    }
#endif

    // Export only submitted tree tiers and actual canopy meshes; Blender supplies the repository's photo models.
    static void Export(string output, SectorPlan plan, Vector3 target, Camera camera, FarScatter scatter)
    {
        Directory.CreateDirectory(output);
        foreach (string path in Directory.GetFiles(output, "canopy-*")) File.Delete(path);
        var views = (IDictionary)Field(scatter, "views"); object view = views[camera];
        var tiers = new Dictionary<(int, int), int>();
        foreach (object draw in (IEnumerable)Field(view, "draws"))
        {
            int tier = (int)Field(draw, "tier");
            foreach (var matrix in (Matrix4x4[])Field(draw, "matrices"))
            {
                var p = matrix.position; var key = ((int)(p.x * 100f), (int)(p.z * 100f));
                tiers[key] = (tiers.TryGetValue(key, out int mask) ? mask : 0) | (1 << tier);
            }
        }
        var logical = new List<object>(); var limits = (Vector4)Field(view, "distances");
        foreach (var stem in SectorForest.Stems)
        {
            var p = stem.position; int tier = -1;
            if (tiers.TryGetValue(((int)(p.x * 100f), (int)(p.z * 100f)), out int mask))
            {
                float score = FarScatter.CrownScore(p + Vector3.up * (stem.stand.height * .75f), stem.stand.radius,
                    camera.transform.position, camera.transform.forward, (float)Field(view, "focal"));
                int wanted = score < limits.x * .95f ? 0 : score < limits.y * .95f ? 1 : 2;
                if ((mask & (1 << wanted)) != 0) tier = wanted;
                else for (int k = 0; k < 3; k++) if ((mask & (1 << k)) != 0) { tier = k; break; }
            }
#if CANDIDATE
            var rotation = ForestPattern.Rotation(stem.i, stem.j, stem.stand);
#else
            float lean = (1f - stem.stand.cover) * 14f;
            var rotation = Quaternion.Euler((ForestPattern.Unit(stem.i, stem.j, 51) - .5f) * lean,
                ForestPattern.Unit(stem.i, stem.j, 44) * 360f, (ForestPattern.Unit(stem.i, stem.j, 52) - .5f) * lean);
#endif
            logical.Add(new { p = V(p), stem.stand.species, stem.stand.height, stem.stand.width, stem.stand.radius, stem.stand.shade,
                yaw = ForestPattern.Unit(stem.i, stem.j, 44) * 360f, rotation = new[] { V(rotation * Vector3.right), V(rotation * Vector3.up), V(rotation * new Vector3(0, 0, 1)) }, tier });
        }
        var heights = new List<float>(); var floor = new List<float>();
        var fields = new List<float[]>();
        for (int z = 0; z <= 250; z++) for (int x = 0; x <= 250; x++)
        {
            var p = new Vector2(x * 8 - 1000, z * 8 - 1000);
            heights.Add(Ground.HeightAt(new Vector3(p.x, 0, p.y))); floor.Add(Ground.CanopyAt(p.x, p.y));
            if (plan.world != null) continue;
            float[] colour = new float[4];
            foreach (var field in MapLayout.Fields)
                if (field.Contains(p, 0f))
                {
                    var c = FieldAppearance.Colour(5 + (int)field.crop, p.x, p.y, FieldAppearance.Edge(field, p), 8f);
                    colour = new[] { c.x, c.y, c.z, 1f }; break;
                }
            fields.Add(colour);
        }
        var roads = new List<List<float[]>>();
        foreach (var line in plan.roads) { var r = new List<float[]>(); foreach (var p in line) r.Add(new[] { p.x, p.y }); roads.Add(r); }
        Save(output + "/scene.json", new { logical, heights, floor, fields, roads, eye = V(camera.transform.position), target = V(target),
            origin = new[] { FarTerrain.Origin.x, FarTerrain.Origin.y }, thresholds = new[] { limits.x, limits.y, limits.z },
            ready = (float)Field(view, "ready"), focal = (float)Field(view, "focal") });
        var blocks = (IDictionary)Field(Field(scatter, "forest"), "blocks"); int count = 0;
        foreach (DictionaryEntry entry in blocks)
        {
            var block = entry.Value; var mesh = (Mesh)Field(block, "mesh"); if (mesh == null) continue;
            if (mesh.bounds.min.x > 1000 || mesh.bounds.max.x < -1000 || mesh.bounds.min.z > 1000 || mesh.bounds.max.z < -1000) continue;
            var vertices = new List<float[]>(); var normals = new List<float[]>(); var uv = new List<float[]>();
            foreach (var p in mesh.vertices) vertices.Add(V(p)); foreach (var p in mesh.normals) normals.Add(V(p));
            foreach (var p in mesh.uv) uv.Add(new[] { p.x, p.y });
            var texture = (Texture2D)Field(block, "texture"); var opacity = new List<byte>(); foreach (var pixel in texture.pixels) opacity.Add(pixel.a);
            Save(output + "/canopy-" + count + ".json", new { vertices, normals, uv, indices = mesh.triangles, cover = opacity });
            using (var stream = new FileStream(output + "/canopy-" + count + ".ppm", FileMode.Create))
            {
                int size = (int)Math.Sqrt(texture.pixels.Length); stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{size} {size}\n255\n"));
                foreach (var p in texture.pixels) { stream.WriteByte(p.r); stream.WriteByte(p.g); stream.WriteByte(p.b); }
            }
            count++;
        }
    }

    // Reflection reads recording state without adding test-only entry points to runtime classes.
    static object Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);

    // Advance a simulated 60 Hz clock while production slices retain real managed stopwatch timings.
    static void Tick(Camera camera)
    {
        Time.frameCount++; Time.unscaledTime = Time.frameCount / 60f;
        feed.transform.position = camera.transform.position + new Vector3(27.87f, 99.82f, 48.27f);
        feed.transform.rotation = Quaternion.LookRotation(new Vector3(1, -.6f, 0));
        if (Time.frameCount % 2 == 0) RenderPipelineManager.Render(feed);
        RenderPipelineManager.Render(camera);
    }

    // Keep scene coordinates in Unity axes until rendering explicitly converts them.
    static float[] V(Vector3 p) => new[] { p.x, p.y, p.z };

    // Compact exports bound disk use while keeping metrics inspectable.
    static void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value));

    // Fail the process instead of recording a misleading passing report.
    static void Require(bool value, string message) { checks++; if (!value) throw new Exception(message); }
}

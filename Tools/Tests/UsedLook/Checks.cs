using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Managed checks for the used-look pass: landmark surface agreement and cost, hedge shares, village ground use.
// One process per scene keeps memory near the existing harnesses; nothing here starts Unity.
static class UsedLookChecks
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

    // Fail with a short diagnostic rather than a crash dialog when run unattended.
    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Scenes: Landmarks, Hedges, Villages or Checks (all three); a third argument "render" writes top-down PPMs.
    static int Run(string[] args)
    {
        string scene = args.Length > 0 ? args[0] : "Checks";
        string output = args.Length > 1 ? args[1] : "Temp/used-look/review";
        bool render = args.Length > 2 && args[2] == "render";
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        WorldPlan.Generate(1200);
        var data = WorldPlan.CaptureDetail(null);
        Console.WriteLine($"world and detail capture {timer.Elapsed.TotalSeconds:F1} s");
        if (scene == "Checks" || scene == "Landmarks") Landmarks(data, output, render);
        if (scene == "Checks" || scene == "Hedges") Hedges(data, output);
        if (scene == "Checks" || scene == "Villages") Villages(data, output, render);
        if (scene.StartsWith("Sector")) Sector(int.Parse(scene.Substring(6)), output);
        Console.WriteLine($"peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MiB");
        return 0;
    }

    // Times the production mesh builder, checks the painter/mesh/query agreement and optionally renders the yard.
    static void Landmarks(FarDetailData data, string output, bool render)
    {
        var map = WorldPlan.CaptureFeatureMap();
        var report = new List<object>();
        foreach (var f in WorldPlan.Features)
        {
            if (!f.valid) continue;
            var kinds = new Dictionary<string, int>();
            foreach (var s in f.surfaces) { string k = s.kind.ToString(); kinds[k] = kinds.TryGetValue(k, out int n) ? n + 1 : 1; }
            double sector = double.MaxValue, far = double.MaxValue;
            LandmarkMesh.Data near = null, distant = null;
            for (int i = 0; i < 3; i++)
            {
                var t = Stopwatch.StartNew(); near = LandmarkMesh.Build(f, f.center, true); sector = Math.Min(sector, t.Elapsed.TotalMilliseconds);
                t.Restart(); distant = LandmarkMesh.Build(f, f.center + new Vector2(5000f, 0f), false); far = Math.Min(far, t.Elapsed.TotalMilliseconds);
            }
            int surfaceTriangles = 0, buildingTriangles = 0, materials = 0;
            for (int m = 0; m < LandmarkMesh.MaterialCount; m++)
            {
                if (distant.triangles[m].Count > 0 || near.triangles[m].Count > 0) materials++;
                surfaceTriangles += near.triangles[m].Count / 3;
            }
            Visible(near, map, f, out int checkedTriangles, out int mismatched);
            for (int m = 0; m < LandmarkMesh.MaterialCount; m++) buildingTriangles += distant.triangles[m].Count / 3;
            buildingTriangles -= surfaceTriangles;
            int samples = 0, disagree = 0;
            for (float v = -f.halfWidth - f.clearance; v <= f.halfWidth + f.clearance; v += 3f)
                for (float u = -f.halfLength - f.clearance; u <= f.halfLength + f.clearance; u += 3f)
                {
                    Vector2 p = f.Area.Point(u, v); samples++;
                    bool a = map.SurfaceAt(p.x, p.y, out Color c), b = WorldPlan.FeatureSurfaceAt(p.x, p.y, out Color d);
                    if (a != b || a && !Same(c, d)) disagree++;
                }
            report.Add(new { f.id, kind = f.kind.ToString(), surfaces = f.surfaces.Count, kinds, buildings = f.buildings.Count, props = f.props.Count,
                sectorMs = sector, farMs = far, surfaceTriangles, buildingTriangles, materials, checkedTriangles, mismatched, samples, disagree });
            Console.WriteLine($"{f.kind} {f.id}: {f.surfaces.Count} surfaces, {f.buildings.Count} buildings, {f.props.Count} props; sector build {sector:F1} ms, far build {far:F1} ms; {surfaceTriangles} surface tris, {materials} materials; visible mesh/painter mismatches {mismatched}/{checkedTriangles} points, query disagreements {disagree}/{samples}");
            if (render) RenderLandmark(data, f, output);
        }
        File.WriteAllText(Path.Combine(output, "landmarks.json"), JsonSerializer.Serialize(report, Json));
    }

    // What a viewer sees: the highest slab over each 1.5 m sample must carry the painter's colour for that point.
    static void Visible(LandmarkMesh.Data mesh, WorldFeatureMap map, WorldFeature f, out int samples, out int mismatched)
    {
        var cells = new Dictionary<long, List<(int material, int first)>>();
        for (int m = 0; m < LandmarkMesh.MaterialCount; m++)
            for (int i = 0; i < mesh.triangles[m].Count; i += 3)
            {
                Vector3 a = mesh.vertices[mesh.triangles[m][i]], b = mesh.vertices[mesh.triangles[m][i + 1]], c = mesh.vertices[mesh.triangles[m][i + 2]];
                int x0 = (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x)) / 8f), x1 = (int)Math.Floor(Math.Max(a.x, Math.Max(b.x, c.x)) / 8f);
                int z0 = (int)Math.Floor(Math.Min(a.z, Math.Min(b.z, c.z)) / 8f), z1 = (int)Math.Floor(Math.Max(a.z, Math.Max(b.z, c.z)) / 8f);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    long key = ((long)x << 32) ^ (uint)z;
                    if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<(int, int)>());
                    list.Add((m, i));
                }
            }
        samples = mismatched = 0;
        for (float v = -f.halfWidth + .613f; v <= f.halfWidth; v += 1.5f)
            for (float u = -f.halfLength + .371f; u <= f.halfLength; u += 1.5f)
            {
                Vector2 local = f.Area.Point(u, v) - f.center;
                if (!cells.TryGetValue(((long)(int)Math.Floor(local.x / 8f) << 32) ^ (uint)(int)Math.Floor(local.y / 8f), out var list)) continue;
                float best = float.MinValue; int top = -1; bool edge = false;
                foreach (var (m, i) in list)
                {
                    Vector3 a = mesh.vertices[mesh.triangles[m][i]], b = mesh.vertices[mesh.triangles[m][i + 1]], c = mesh.vertices[mesh.triangles[m][i + 2]];
                    float w0 = Cross(b, c, local), w1 = Cross(c, a, local), w2 = Cross(a, b, local), area = w0 + w1 + w2;
                    if (Math.Abs(area) < 1e-6f) continue;
                    w0 /= area; w1 /= area; w2 /= area;
                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                    if (Math.Min(w0, Math.Min(w1, w2)) < .002f) edge = true;
                    float y = a.y * w0 + b.y * w1 + c.y * w2;
                    if (y > best) { best = y; top = m; }
                }
                bool painted = map.SurfaceAt(local.x + f.center.x, local.y + f.center.y, out Color colour);
                if (top < 0 && !painted || edge) continue;
                samples++;
                if (top < 0 || !painted || !Same(colour, LandmarkMesh.Color(top)))
                {
                    if (mismatched++ < 12 && Environment.GetEnvironmentVariable("USED_LOOK_DEBUG") == "1")
                    {
                        string kinds = "";
                        for (int s = f.surfaces.Count - 1; s >= 0; s--) if (f.surfaces[s].Contains(local + f.center)) kinds += s + ":" + f.surfaces[s].kind + "@" + f.surfaces[s].lift.ToString("F3") + " ";
                        Console.WriteLine($"  mismatch f{f.id} u {u:F1} v {v:F1}: top {(top < 0 ? "none" : top.ToString())} y {best:F3}; painter {(painted ? colour.ToString() : "none")}; surfaces {kinds}");
                    }
                }
            }
    }

    // Twice the signed area of the triangle (a, b, p) in the ground plane.
    static float Cross(Vector3 a, Vector3 b, Vector2 p) => (b.x - a.x) * (p.y - a.z) - (b.z - a.z) * (p.x - a.x);

    // Colours compare in sRGB thousandths, as the painter and slabs share one table.
    static bool Same(Color a, Color b) => Math.Abs(a.r - b.r) < .002f && Math.Abs(a.g - b.g) < .002f && Math.Abs(a.b - b.b) < .002f;

    // Feature-aligned top-down picture of the shared painter at 0.5 m, with building footprints darkened.
    static void RenderLandmark(FarDetailData data, WorldFeature f, string output)
    {
        const float res = .5f, pad = 30f;
        int w = (int)((f.halfLength + pad) * 2f / res), h = (int)((f.halfWidth + pad) * 2f / res);
        var pixels = new byte[w * h * 3];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float u = -f.halfLength - pad + (i + .5f) * res, v = f.halfWidth + pad - (j + .5f) * res;
                Vector2 p = f.Area.Point(u, v);
                Color32 c = data.Paint(p.x, p.y, p, null, null, res);
                foreach (var b in f.buildings)
                {
                    Vector2 d = p - b.position, right = new Vector2(b.facing.y, -b.facing.x);
                    if (Math.Abs(Vector2.Dot(d, right)) < b.size.x * .5f && Math.Abs(Vector2.Dot(d, b.facing)) < b.size.z * .5f)
                    { c = new Color32((byte)(c.r / 3 + 20), (byte)(c.g / 3 + 20), (byte)(c.b / 3 + 24), 255); break; }
                }
                foreach (var prop in f.props)
                    if ((p - prop.position).sqrMagnitude < 9f) { c = new Color32(150, 60, 40, 255); break; }
                int k = (j * w + i) * 3; pixels[k] = c.r; pixels[k + 1] = c.g; pixels[k + 2] = c.b;
            }
        Ppm(Path.Combine(output, "landmark-" + f.kind + ".ppm"), w, h, pixels);
    }

    // Edge shares in physical length, per district, for the woody boundary target.
    static void Hedges(FarDetailData data, string output)
    {
        var shares = new List<float>(); double hedged = 0, total = 0, villageHedged = 0, villageTotal = 0;
        var timer = Stopwatch.StartNew();
        for (int z = -14; z < 14; z++)
            for (int x = -14; x < 14; x++)
            {
                var d = data.fields.Get(x, z);
                double h = 0, t = 0;
                foreach (var p in d.parcels)
                {
                    if (!p.valid) continue;
                    for (int e = 0; e < 4; e++)
                    {
                        float length = e % 2 == 0 ? p.u1 - p.u0 : e == 1 ? p.b1 - p.b0 : p.a1 - p.a0;
                        t += length; if (EdgeHedged(p, e)) h += length;
                    }
                }
                if (t <= 0) continue;
                shares.Add((float)(h / t)); hedged += h; total += t;
                if (d.settlement > .18f) { villageHedged += h; villageTotal += t; }
            }
        shares.Sort();
        float Quantile(float q) => shares.Count == 0 ? 0f : shares[Math.Min(shares.Count - 1, (int)(q * shares.Count))];
        var result = new { districts = shares.Count, overall = hedged / Math.Max(1, total), nearVillages = villageHedged / Math.Max(1, villageTotal),
            p10 = Quantile(.1f), median = Quantile(.5f), p90 = Quantile(.9f), buildMs = timer.Elapsed.TotalMilliseconds };
        Console.WriteLine($"hedges: {result.districts} districts, overall {result.overall:P1}, near villages {result.nearVillages:P1}, district p10/median/p90 {result.p10:P1}/{result.median:P1}/{result.p90:P1}; {result.buildMs:F0} ms");
        File.WriteAllText(Path.Combine(output, "hedges.json"), JsonSerializer.Serialize(result, Json));
    }

    // Edge order matches FarFieldPlan.Parcel: 0 lower side, 1 far end, 2 upper side, 3 near end.
    static bool EdgeHedged(FarFieldPlan.Parcel p, int edge) => HedgeProbe.Edge(p, edge);

    // Counts plot boundaries and ground uses, and renders a few village pictures through the shared painter.
    static void Villages(FarDetailData data, string output, bool render)
    {
        int plots = 0, full = 0, partial = 0, open = 0, stains = 0, farms = 0, paddocks = 0, orchards = 0, rough = 0;
        foreach (var p in data.villages.plots)
        {
            if (p.landUse == 6) paddocks++; else if (p.landUse == 7) orchards++; else if (p.landUse == 8) rough++;
            if (p.church || p.square) continue;
            plots++; if (p.farm) farms++;
            int shown = 0, count = p.shape == 2 ? 6 : 4;
#if BEFORE
            if (p.boundary != 0) shown = count;
#else
            for (int e = 0; e < count; e++) if (p.EdgeShown(e)) shown++;
#endif
            if (shown == 0) open++; else if (shown == count) full++; else partial++;
#if !BEFORE
            if (p.HasStain) stains++;
            if (p.HasStain && p.UseAt(p.DriveU, p.setback + 3.2f) != VillagePlot.Stain) throw new Exception($"Stain missing on its drive: use {p.UseAt(p.DriveU, p.setback + 3.2f)} setback {p.setback} depth {p.depth} yardEnd {p.YardEnd} drive {p.DriveU} path {p.pathWidth} frontage {p.frontage} town {p.town} landUse {p.landUse} model {p.model}");
            if (p.farm && p.WorkAt(-p.frontage * .5f + 5f, p.YardStart + 6f) != VillagePlot.Mud) throw new Exception("Muck heap without mud");
#endif
        }
        Console.WriteLine($"villages: {plots} plots: {full} fully bounded, {partial} partly, {open} open; {stains} drive stains, {farms} farms; fringe {paddocks} paddocks, {orchards} orchards, {rough} rough grass");
        File.WriteAllText(Path.Combine(output, "villages.json"), JsonSerializer.Serialize(new { plots, full, partial, open, stains, farms, paddocks, orchards, rough }, Json));
        if (!render) return;
        foreach (int owner in new[] { 4, 9, 14 })
        {
            if (owner >= WorldPlan.Settlements.Count) continue;
            var s = WorldPlan.Settlements[owner];
            const float res = .5f; float half = Math.Min(300f, s.radius + 120f);
            int n = (int)(half * 2f / res); var pixels = new byte[n * n * 3];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = s.center.x - half + (i + .5f) * res, z = s.center.y + half - (j + .5f) * res;
                    Color32 c = data.Paint(x, z, new Vector2(x, z), null, null, res);
                    int k = (j * n + i) * 3; pixels[k] = c.r; pixels[k + 1] = c.g; pixels[k + 2] = c.b;
                }
            Ppm(Path.Combine(output, "village-" + owner + ".ppm"), n, n, pixels);
        }
    }

    // Builds one world sector around a settlement through the production village, margin and landmark builders,
    // then counts the props, boundaries and timings the used-look pass adds.
    static void Sector(int owner, string output)
    {
        var s = WorldPlan.Settlements[owner];
        var sector = WorldSectors.PlanAt(s.center, SectorKind.RiverValley, 3100 + owner);
        var timer = Stopwatch.StartNew();
        MapLayout.Use(sector); MapLayout.Prepare();
        double layout = timer.Elapsed.TotalMilliseconds; timer.Restart();
        var root = new GameObject("Review").transform;
        Ground.Build(root);
        double ground = timer.Elapsed.TotalMilliseconds; timer.Restart();
        Art.placed.Clear();
        typeof(World).GetMethod("BuildVillages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Invoke(null, new object[] { root, new System.Random(sector.worldSeed) });
        double villages = timer.Elapsed.TotalMilliseconds; timer.Restart();
        int blocks = 0; double margins = double.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            FieldMargins.Release(); Mesh.All.Clear(); timer.Restart();
            blocks = FieldMargins.Build(root); margins = Math.Min(margins, timer.Elapsed.TotalMilliseconds);
        }
        var models = new SortedDictionary<string, int>();
        foreach (var item in Art.placed) models[item.model] = models.TryGetValue(item.model, out int n) ? n + 1 : 1;
        double hedge = 0, total = 0; int hedges = 0;
        foreach (var b in MapLayout.Boundaries) if (b.kind == FieldBoundaryKind.Hedge) { hedges++; hedge += Vector2.Distance(b.a, b.b); }
        foreach (var f in MapLayout.Fields) total += 4f * (f.halfLength + f.halfWidth);
        int fringe = 0; foreach (var p in MapLayout.VillagePlots) if (p.landUse >= 6) fringe++;
        int tracks = 0; foreach (var mesh in Mesh.All) if (mesh.name == "Field use 128 m block" && mesh.subs.Count > 2) tracks++;
        var result = new { owner, s.name, houses = World.VillageHouses, renderers = World.VillagePropRenderers, fields = MapLayout.Fields.Count,
            hedgeEdges = hedges, hedgeShareOfFieldPerimeter = hedge / Math.Max(1, total), fringe, marginBlocks = blocks, blocksWithTracks = tracks,
            models, layoutMs = layout, groundMs = ground, villagesMs = villages, marginsMs = margins };
        Console.WriteLine(JsonSerializer.Serialize(result, Json));
        File.WriteAllText(Path.Combine(output, "sector-" + owner + ".json"), JsonSerializer.Serialize(result, Json));
    }

    // Binary PPM keeps the harness free of image libraries; run.py converts to PNG.
    static void Ppm(string path, int w, int h, byte[] rgb)
    {
        using var stream = File.Create(path);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
        stream.Write(header, 0, header.Length); stream.Write(rgb, 0, rgb.Length);
    }
}

// Which parcel edges carry a woody boundary; the baseline marked every edge of a hedged parcel.
static class HedgeProbe
{
    // Shared by the before and after runs so the same length-weighted share is reported.
#if BEFORE
    public static bool Edge(FarFieldPlan.Parcel p, int edge) => p.hedge || p.use == 1 || p.use == 2;
#else
    public static bool Edge(FarFieldPlan.Parcel p, int edge) => FieldAppearance.EdgeUse(p.edges, edge) == (int)FieldEdge.Hedge;
#endif
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Managed checks for the second used-look pass: soft landmark surfaces (mesh/painter agreement, cost, pictures),
// hedge shares, village ground use and the forest floor round lone trees. One process per scene; nothing starts Unity.
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
        string output = args.Length > 1 ? args[1] : "Temp/used-look-2/review";
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
        if (scene.StartsWith("Floor")) Floor(int.Parse(scene.Substring(5)), output, render);
#if !BEFORE
        if (scene == "Profile") Profile();
#endif
        Console.WriteLine($"peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MiB");
        return 0;
    }

    // Times the production mesh builder, checks the painter/mesh/query agreement and optionally renders the yard.
    static void Landmarks(FarDetailData data, string output, bool render)
    {
        var map = WorldPlan.CaptureFeatureMap();
        var report = new List<object>();
        var seen = new HashSet<WorldFeatureKind>();
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
            int surfaceTriangles = 0, buildingTriangles = 0, materials = 0, surfaceDraws = 0, soft = 0;
            for (int m = 0; m < LandmarkMesh.MaterialCount; m++)
            {
                if (distant.triangles[m].Count > 0 || near.triangles[m].Count > 0) materials++;
                if (near.triangles[m].Count > 0) surfaceDraws++;
                surfaceTriangles += near.triangles[m].Count / 3;
            }
            int concave = 0;
#if !BEFORE
            foreach (var s in f.surfaces)
            {
                if (s.soft != null) soft++;
                if (s.outline == null) continue;
                for (int i = 0; i < s.outline.Length; i++)
                {
                    Vector2 a = s.outline[i], b = s.outline[(i + 1) % s.outline.Length], c = s.outline[(i + 2) % s.outline.Length];
                    if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) < -1e-4f) { concave++; break; }
                }
            }
#endif
            Visible(near, map, f, out int checkedTriangles, out int mismatched);
            double slice = 0; int slices = 0;
#if !BEFORE
            var builder = new LandmarkMesh.Builder(f, f.center + new Vector2(5000f, 0f), false);
            for (bool done = false; !done; slices++) { var t = Stopwatch.StartNew(); done = builder.Step(1.0); slice = Math.Max(slice, t.Elapsed.TotalMilliseconds); }
#endif
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
            report.Add(new { f.id, kind = f.kind.ToString(), surfaces = f.surfaces.Count, soft, concave, kinds, buildings = f.buildings.Count, props = f.props.Count,
                sectorMs = sector, farMs = far, slices, worstSliceMs = slice, surfaceTriangles, buildingTriangles, materials, surfaceDraws, checkedTriangles, mismatched, samples, disagree });
            Console.WriteLine($"{f.kind} {f.id}: {f.surfaces.Count} surfaces ({soft} soft, {concave} concave), {f.buildings.Count} buildings, {f.props.Count} props; sector build {sector:F2} ms, far build {far:F2} ms ({slices} slices, worst {slice:F2} ms); {surfaceTriangles} surface tris, {surfaceDraws} surface draws, {materials} far materials; visible mesh/painter mismatches {mismatched}/{checkedTriangles} points, query disagreements {disagree}/{samples}");
            if (render && seen.Add(f.kind)) { RenderLandmark(data, f, output); RenderView(data, f, near, output); }
        }
        File.WriteAllText(Path.Combine(output, "landmarks.json"), JsonSerializer.Serialize(report, Json));
    }

#if !BEFORE
    // Build cost split: the same feature built hard and flat, with soft edges only, with bays only, and with both.
    static void Profile()
    {
        foreach (var f in WorldPlan.Features)
        {
            if (!f.valid || f.id % 10 != 0 && f.id != 22) continue;
            {
                // The single most expensive surface, as LandmarkHlod meets it one slice at a time (after a warm-up build).
                LandmarkMesh.Build(f, f.center + new Vector2(5000f, 0f), false);
                var builder = new LandmarkMesh.Builder(f, f.center + new Vector2(5000f, 0f), false);
                double worst = 0; int at = -1, index = 0;
                for (bool done = false; !done; index++) { var t = Stopwatch.StartNew(); done = builder.Step(0.0); double ms = t.Elapsed.TotalMilliseconds; if (ms > worst) { worst = ms; at = index; } }
                var w = f.surfaces[Math.Min(at, f.surfaces.Count - 1)];
                Console.WriteLine($"{f.kind} {f.id}: worst surface {at} {w.kind} {w.area.halfLength * 2f:F0}x{w.area.halfWidth * 2f:F0} m bays {w.bays:F0} soft {(w.soft != null)} {worst:F2} ms");
            }
            var original = new List<LandmarkSurface>(f.surfaces);
            var line = $"{f.kind} {f.id} ({f.surfaces.Count}):";
            foreach (var (name, soft, bays) in new[] { ("plain", 0, false), ("soft", 1, false), ("decals", 2, false), ("slabs", 3, false), ("both", 1, true) })
            {
                for (int i = 0; i < f.surfaces.Count; i++)
                {
                    var s = original[i]; bool decal = s.lift >= .12f;
                    if (soft == 0 || soft == 2 && !decal || soft == 3 && decal) s.soft = null;
                    if (!bays) s.bays = 0f; f.surfaces[i] = s;
                }
                double best = double.MaxValue;
                for (int k = 0; k < 7; k++) { var t = Stopwatch.StartNew(); LandmarkMesh.Build(f, f.center + new Vector2(5000f, 0f), false); best = Math.Min(best, t.Elapsed.TotalMilliseconds); }
                line += $" {name} {best:F2} ms";
            }
            for (int i = 0; i < f.surfaces.Count; i++) f.surfaces[i] = original[i];
            Console.WriteLine(line);
        }
    }
#endif

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
                bool shown = Top(mesh, list, local, out int top, out Color seen, out bool edge);
                bool painted = map.SurfaceAt(local.x + f.center.x, local.y + f.center.y, out Color colour);
                // At a soft edge's very rim the painter shows bare verge where the mesh has just ended: no disagreement.
                if (!shown && !painted || edge || !shown && Verge(colour)) continue;
                samples++;
                if (top < 0 || !painted || !Close(colour, seen))
                {
                    if (mismatched++ < 12 && Environment.GetEnvironmentVariable("USED_LOOK_DEBUG") == "1")
                    {
                        string kinds = "";
                        for (int s = f.surfaces.Count - 1; s >= 0; s--) if (f.surfaces[s].Contains(local + f.center)) kinds += s + ":" + f.surfaces[s].kind + "@" + f.surfaces[s].lift.ToString("F3") + " ";
                        Console.WriteLine($"  mismatch f{f.id} u {u:F1} v {v:F1}: top {(top < 0 ? "none" : top.ToString())} mesh {seen.r:F3},{seen.g:F3},{seen.b:F3}; painter {(painted ? colour.r.ToString("F3") + "," + colour.g.ToString("F3") + "," + colour.b.ToString("F3") : "none")}; surfaces {kinds}");
                    }
                }
            }
    }

    // The highest slab triangle over a local ground point and the colour it shows there (palette decoded after this pass).
    static bool Top(LandmarkMesh.Data mesh, List<(int material, int first)> list, Vector2 local, out int top, out Color colour, out bool edge)
    {
        float best = float.MinValue; top = -1; edge = false; colour = default;
        foreach (var (m, i) in list)
        {
            int ia = mesh.triangles[m][i], ib = mesh.triangles[m][i + 1], ic = mesh.triangles[m][i + 2];
            Vector3 a = mesh.vertices[ia], b = mesh.vertices[ib], c = mesh.vertices[ic];
            float w0 = Cross(b, c, local), w1 = Cross(c, a, local), w2 = Cross(a, b, local), area = w0 + w1 + w2;
            if (Math.Abs(area) < 1e-6f) continue;
            w0 /= area; w1 /= area; w2 /= area;
            if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
            float y = a.y * w0 + b.y * w1 + c.y * w2;
            if (y <= best) continue;
            best = y; top = m; edge = Math.Min(w0, Math.Min(w1, w2)) < .002f;
#if BEFORE
            colour = LandmarkMesh.Color(m);
#else
            colour = m == LandmarkMesh.SurfaceSlot ? Palette(mesh.uvs[ia] * w0 + mesh.uvs[ib] * w1 + mesh.uvs[ic] * w2) : LandmarkMesh.Color(m);
#endif
        }
        return top >= 0;
    }

#if !BEFORE
    static Color32[] palette;

    // What the GPU's bilinear filter returns from the surface palette: rows are exact, texels blend along a run.
    static Color Palette(Vector2 uv)
    {
        palette ??= LandmarkMesh.PalettePixels();
        float x = uv.x * LandmarkMesh.PaletteWidth - .5f;
        int row = Math.Min(LandmarkMesh.PaletteHeight - 1, (int)(uv.y * LandmarkMesh.PaletteHeight));
        int i = (int)Math.Floor(x); float t = x - i;
        int run = (int)Math.Floor((x + .5f) / 8f) * 8;
        int i0 = Math.Max(run, Math.Min(run + 7, i)), i1 = Math.Max(run, Math.Min(run + 7, i + 1));
        Color a = palette[row * LandmarkMesh.PaletteWidth + i0], b = palette[row * LandmarkMesh.PaletteWidth + i1];
        return Color.Lerp(a, b, t);
    }
#endif

    // The bare grass a soft edge fades to where nothing lies below it.
#if BEFORE
    static bool Verge(Color c) => false;
#else
    static bool Verge(Color c) => Close(c, WorldLandmarkLayout.ToneColor(WorldLandmarkLayout.VergeTone));
#endif

    // Colours agree within five sRGB levels: the palette stores bytes and the GPU filters between them.
    static bool Close(Color a, Color b) => Math.Abs(a.r - b.r) < .02f && Math.Abs(a.g - b.g) < .02f && Math.Abs(a.b - b.b) < .02f;

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

    // Slab triangles indexed by 8 m cell in feature-local metres, for point lookups.
    static Dictionary<long, List<(int material, int first)>> Cells(LandmarkMesh.Data mesh)
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
        return cells;
    }

    // A 1280x720 view framed like the autotest's 89_landmark_*_300m shot (45 degree lens, 50.5 degree pitch, yaw 35):
    // the sector slab mesh where it lies, the shared painter beyond, buildings as flat-shaded boxes; 2x2 samples a pixel.
    static void RenderView(FarDetailData data, WorldFeature f, LandmarkMesh.Data mesh, string output)
    {
        var cells = Cells(mesh);
        View(Path.Combine(output, "view-" + f.kind + ".ppm"), new Vector3(f.center.x, f.elevation, f.center.y), 300f, 50.5f, 35f, f.buildings, (x, z) =>
        {
            Vector2 local = new Vector2(x, z) - f.center;
            if (cells.TryGetValue(((long)(int)Math.Floor(local.x / 8f) << 32) ^ (uint)(int)Math.Floor(local.y / 8f), out var list)
                && Top(mesh, list, local, out _, out Color colour, out _)) return colour;
            return (Color)data.Paint(x, z, new Vector2(x, z), null, null, .4f);
        });
    }

    // Ray-traces a flat ground (and optional box buildings) from an RTS camera: Unity's Euler(pitch, yaw, 0) at distance.
    static void View(string path, Vector3 focus, float distance, float pitchDegrees, float yawDegrees, List<LandmarkBuilding> buildings, Func<float, float, Color> ground)
    {
        const int w = 1280, h = 720;
        float pitch = pitchDegrees * Mathf.Deg2Rad, yaw = yawDegrees * Mathf.Deg2Rad, tan = (float)Math.Tan(22.5 * Math.PI / 180.0);
        var forward = new Vector3(Mathf.Cos(pitch) * Mathf.Sin(yaw), -Mathf.Sin(pitch), Mathf.Cos(pitch) * Mathf.Cos(yaw));
        var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
        var up = new Vector3(Mathf.Sin(pitch) * Mathf.Sin(yaw), Mathf.Cos(pitch), Mathf.Sin(pitch) * Mathf.Cos(yaw));
        Vector3 eye = focus - forward * distance;
        var sun = new Vector3(.45f, .8f, .4f).normalized;
        var pixels = new byte[w * h * 3];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float r = 0f, g = 0f, b = 0f;
                for (int s = 0; s < 4; s++)
                {
                    float x = (2f * (i + .25f + .5f * (s & 1)) / w - 1f) * tan * w / h, y = (1f - 2f * (j + .25f + .5f * (s >> 1)) / h) * tan;
                    Vector3 d = forward + right * x + up * y;
                    float t = d.y < -1e-4f ? (focus.y + .12f - eye.y) / d.y : float.MaxValue;
                    Color c = new Color(.55f, .62f, .7f);
                    float nearest = t; Vector3 normal = Vector3.zero;
                    if (buildings != null) foreach (var bl in buildings) Box(eye, d, bl, focus.y, ref nearest, ref normal);
                    if (nearest < t)
                    {
                        float light = .45f + .55f * Math.Max(0f, Vector3.Dot(normal, sun));
                        c = normal.y > .5f ? new Color(.36f * light, .37f * light, .37f * light) : new Color(.44f * light, .42f * light, .38f * light);
                    }
                    else if (t < float.MaxValue) { Vector3 q = eye + d * t; c = ground(q.x, q.z); }
                    r += c.r; g += c.g; b += c.b;
                }
                int k = (j * w + i) * 3;
                pixels[k] = (byte)Math.Min(255, r * 63.75f); pixels[k + 1] = (byte)Math.Min(255, g * 63.75f); pixels[k + 2] = (byte)Math.Min(255, b * 63.75f);
            }
        Ppm(path, w, h, pixels);
    }

    // Slab test against one building's oriented box; keeps the nearest hit and its face normal.
    static void Box(Vector3 eye, Vector3 d, LandmarkBuilding bl, float floor, ref float nearest, ref Vector3 normal)
    {
        var ax = new Vector3(bl.facing.y, 0f, -bl.facing.x); var az = new Vector3(bl.facing.x, 0f, bl.facing.y);
        Vector3 centre = new Vector3(bl.position.x, floor + bl.size.y * .5f, bl.position.y), o = eye - centre;
        Vector3[] axes = { ax, Vector3.up, az }; float[] half = { bl.size.x * .5f, bl.size.y * .5f, bl.size.z * .5f };
        float t0 = float.MinValue, t1 = float.MaxValue; Vector3 n = Vector3.zero;
        for (int k = 0; k < 3; k++)
        {
            float od = Vector3.Dot(o, axes[k]), dd = Vector3.Dot(d, axes[k]);
            if (Math.Abs(dd) < 1e-6f) { if (Math.Abs(od) > half[k]) return; continue; }
            float a = (-half[k] - od) / dd, b = (half[k] - od) / dd;
            if (a > b) { float swap = a; a = b; b = swap; }
            if (a > t0) { t0 = a; n = dd < 0 ? axes[k] : axes[k] * -1f; }
            t1 = Math.Min(t1, b);
            if (t0 > t1) return;
        }
        if (t0 > 0f && t0 < nearest) { nearest = t0; normal = n; }
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
        int plots = 0, full = 0, partial = 0, open = 0, stains = 0, farms = 0, paddocks = 0, orchards = 0, rough = 0, vegetables = 0;
        foreach (var p in data.villages.plots)
        {
            if (p.landUse == 6) paddocks++; else if (p.landUse == 7) orchards++; else if (p.landUse == 8) rough++; else if (p.landUse == 9) vegetables++;
            if (p.church || p.square) continue;
            plots++; if (p.farm) farms++;
            int shown = 0, count = p.shape == 2 ? 6 : 4;
            for (int e = 0; e < count; e++) if (p.EdgeShown(e)) shown++;
            if (shown == 0) open++; else if (shown == count) full++; else partial++;
            if (p.HasStain) stains++;
            if (p.HasStain && p.UseAt(p.DriveU, p.setback + 3.2f) != VillagePlot.Stain) throw new Exception($"Stain missing on its drive: use {p.UseAt(p.DriveU, p.setback + 3.2f)} setback {p.setback} depth {p.depth} yardEnd {p.YardEnd} drive {p.DriveU} path {p.pathWidth} frontage {p.frontage} town {p.town} landUse {p.landUse} model {p.model}");
            if (p.farm && p.WorkAt(-p.frontage * .5f + 5f, p.YardStart + 6f) != VillagePlot.Mud) throw new Exception("Muck heap without mud");
        }
        Console.WriteLine($"villages: {plots} plots: {full} fully bounded, {partial} partly, {open} open; {stains} drive stains, {farms} farms; fringe {paddocks} paddocks, {vegetables} vegetable plots, {orchards} orchards, {rough} rough grass");
        File.WriteAllText(Path.Combine(output, "villages.json"), JsonSerializer.Serialize(new { plots, full, partial, open, stains, farms, paddocks, vegetables, orchards, rough }, Json));
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
            // The same village as the autotest's 02d_village frames it: 261 m out, 49 degrees down, yaw 200.
            View(Path.Combine(output, "village-view-" + owner + ".ppm"), new Vector3(s.center.x, 0f, s.center.y), 261f, 48.9f, 200f, null,
                (x, z) => (Color)data.Paint(x, z, new Vector2(x, z), null, null, .4f));
        }
    }

    // The forest floor under the kinds of tree group seen round villages: a wood, a seven-stem copse, a pair, a lone field
    // tree and a six-tree orchard of garden trees, on one settlement's sector terrain. Ground.PaintForestFloor fills its
    // grids and stops at the stub alphamaps; the floor is then sampled with the production formula, and the floor lying
    // more than 12 m from any stem of a real stand (four or more stems within 25 m) is the isolated dark blobs.
    static void Floor(int owner, string output, bool render)
    {
        var s = WorldPlan.Settlements[owner];
        var sector = WorldSectors.PlanAt(s.center, SectorKind.RiverValley, 3100 + owner);
        MapLayout.Use(sector); MapLayout.Prepare();
        var root = new GameObject("Review").transform;
        Ground.Build(root);
        var rng = new System.Random(4242);
        var woodland = new List<Vector2>(); var trees = new List<Vector2>();
        for (float x = -200f; x < -80f; x += 5f) for (float z = -40f; z < 40f; z += 5f)
            if (rng.NextDouble() < .5) woodland.Add(new Vector2(x + (float)rng.NextDouble() * 4f, z + (float)rng.NextDouble() * 4f));
        for (int i = 0; i < 7; i++) woodland.Add(new Vector2(-10f + (float)Math.Cos(i * .9) * (3f + i * 1.3f), 60f + (float)Math.Sin(i * .9) * (3f + i * 1.3f)));
        woodland.Add(new Vector2(60f, -50f)); woodland.Add(new Vector2(65f, -47f));
        woodland.Add(new Vector2(110f, 40f));
        trees.AddRange(woodland);
        for (int i = 0; i < 6; i++) trees.Add(new Vector2(150f + (i % 2) * 7f, -20f + (i / 2) * 8f));
        var timer = Stopwatch.StartNew();
        Ground.PaintForestFloor(woodland, trees);
        double floorMs = timer.Elapsed.TotalMilliseconds;
        var grid = typeof(Ground).GetMethod("Grid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var stems = (float[])typeof(Ground).GetField("stems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
        float Smooth(float t) { t = Math.Max(0f, Math.Min(1f, t)); return t * t * (3f - 2f * t); }
        float FloorAt(float x, float z)
        {
            float ragged = (Mathf.PerlinNoise(x * 0.11f + 41f, z * 0.11f + 7f) - 0.5f) * 1.4f;
            float floor = Smooth((Ground.CanopyAt(x, z) + ragged - 1.2f) / 1.8f);
#if !BEFORE
            floor *= Smooth(((float)grid.Invoke(null, new object[] { stems, x, z }) - 0.25f) / 0.5f);
#endif
            return floor;
        }
        // Stems of real stands, and every tree that stands alone or in a group of three or fewer.
        var cells = new Dictionary<long, List<Vector2>>();
        long Key(Vector2 p) => ((long)Math.Floor(p.x / 25f) << 32) ^ (uint)(int)Math.Floor(p.y / 25f);
        foreach (var p in woodland) { if (!cells.TryGetValue(Key(p), out var l)) cells.Add(Key(p), l = new List<Vector2>()); l.Add(p); }
        int Crowd(Vector2 p)
        {
            int n = 0;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                if (cells.TryGetValue(Key(p + new Vector2(dx * 25f, dz * 25f)), out var l)) foreach (var q in l) { float d = (q - p).sqrMagnitude; if (d > .01f && d < 625f) n++; }
            return n;
        }
        var stand = new Dictionary<long, List<Vector2>>(); int lone = 0;
        foreach (var p in woodland)
        {
            if (Crowd(p) < 3) { lone++; continue; }
            long k = ((long)Math.Floor(p.x / 12f) << 32) ^ (uint)(int)Math.Floor(p.y / 12f);
            if (!stand.TryGetValue(k, out var l)) stand.Add(k, l = new List<Vector2>()); l.Add(p);
        }
        bool NearStand(Vector2 p)
        {
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                long k = ((long)Math.Floor(p.x / 12f + dx) << 32) ^ (uint)(int)Math.Floor(p.y / 12f + dz);
                if (stand.TryGetValue(k, out var l)) foreach (var q in l) if ((q - p).sqrMagnitude < 144f) return true;
            }
            return false;
        }
        Vector2 centre = new Vector2(-20f, 0f);
        const float half = 200f, step = 1f;
        double floorArea = 0, isolated = 0;
        int n = (int)(half * 2f / step);
        var pixels = new byte[n * n * 3];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = centre.x - half + (i + .5f) * step, z = centre.y + half - (j + .5f) * step;
                float floor = FloorAt(x, z);
                if (floor > .3f) { floorArea += step * step; if (!NearStand(new Vector2(x, z))) isolated += step * step; }
                var c = new Color(.42f + (.22f - .42f) * floor, .48f + (.30f - .48f) * floor, .30f + (.19f - .30f) * floor);
                int k = (j * n + i) * 3; pixels[k] = (byte)(c.r * 255); pixels[k + 1] = (byte)(c.g * 255); pixels[k + 2] = (byte)(c.b * 255);
            }
        foreach (var t in trees)
        {
            int i = (int)((t.x - centre.x + half) / step), j = (int)((centre.y + half - t.y) / step);
            if (i < 1 || j < 1 || i >= n - 1 || j >= n - 1) continue;
            bool alone = Crowd(t) < 3;
            for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
            {
                int k = ((j + dj) * n + i + di) * 3;
                pixels[k] = alone ? (byte)200 : (byte)30; pixels[k + 1] = alone ? (byte)60 : (byte)70; pixels[k + 2] = alone ? (byte)40 : (byte)30;
            }
        }
        if (render) Ppm(Path.Combine(output, "floor.ppm"), n, n, pixels);
        var result = new { woodland = woodland.Count, trees = trees.Count, loneStems = lone, floorArea, isolatedFloorArea = isolated, floorMs };
        Console.WriteLine(JsonSerializer.Serialize(result, Json));
        File.WriteAllText(Path.Combine(output, "floor.json"), JsonSerializer.Serialize(result, Json));
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
        var uses = new SortedDictionary<int, int>(); foreach (var p in MapLayout.VillagePlots) uses[p.landUse] = uses.TryGetValue(p.landUse, out int u) ? u + 1 : 1;
        int farmsteads = 0; foreach (bool farm in MapLayout.Plan.farmsteads) if (farm) farmsteads++;
        Console.WriteLine($"plots by use: {string.Join(", ", uses)}; villages {MapLayout.Plan.villages.Count}, farmsteads {farmsteads}");
        if (Environment.GetEnvironmentVariable("USED_LOOK_DEBUG") == "3")
        {
            var buildable = typeof(World).GetMethod("Buildable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            foreach (var p in MapLayout.VillagePlots)
            {
                if (p.landUse != 0 || p.square || p.church) continue;
                bool built = false;
                foreach (var item in Art.placed) if (item.root != null && (new Vector2(item.root.position.x, item.root.position.z) - p.House).sqrMagnitude < 9f) { built = true; break; }
                if (built) continue;
                Vector2 h = p.House;
                var clearM = typeof(World).GetMethod("VillagePlotClear", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var others = new List<VillagePlot>(); string hits = "";
                foreach (var q in MapLayout.VillagePlots) if ((q.front - p.front).sqrMagnitude > .0001f && VillagePlan.Intersects(p, q)) { others.Add(q); hits += q.landUse + (q.square ? "s" : "") + " "; }
                Console.WriteLine($"   clear alone {clearM.Invoke(null, new object[] { p, new List<VillagePlot>() })}; intersects {hits}");
                var footM = typeof(World).GetMethod("VillageFootprint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                int index = MapLayout.VillagePlots.IndexOf(p);
                {
                    Vector2 facing = -p.outward, side = new Vector2(-facing.y, facing.x); float wd = 4.6f * p.Scale, dp = 3.9f * p.Scale; int nx = (int)Math.Ceiling(wd), nz = (int)Math.Ceiling(dp);
                    string why = "";
                    for (int z = -nz; z <= nz && why.Length < 300; z++) for (int x = -nx; x <= nx; x++)
                    {
                        Vector2 q = p.House + side * (wd * x / nx) + facing * (dp * z / nz);
                        if ((bool)buildable.Invoke(null, new object[] { q })) continue;
                        why += $"[{x},{z}: works {MapLayout.OnWorks(q, 1.5f)} flat {MapLayout.NearFlatSite(q, 2f)} field {MapLayout.InField(q, 1.5f)} lake {MapLayout.LakeDistance(q.x, q.y):F1} road {MapLayout.RoadField.Distance(q.x, q.y):F1}/{MapLayout.RoadHalfWidth + 1.5f:F1} track {MapLayout.TrackField.Distance(q.x, q.y):F1}] ";
                        break;
                    }
                    Console.WriteLine("   fails: " + why);
                }
                Console.WriteLine($"   footprint {footM.Invoke(null, new object[] { p.House, -p.outward, 4.6f * p.Scale, 3.9f * p.Scale })} index {index} of {MapLayout.VillagePlots.Count}; first fringe at {MapLayout.VillagePlots.FindIndex(q => q.landUse >= 6)}");
                Console.WriteLine($"  missing {p.model} farm {p.farm} at {h.x:F0},{h.y:F0}: field {MapLayout.InField(h, 1.5f)} flat {MapLayout.NearFlatSite(h, 2f)} works {MapLayout.OnWorks(h, 1.5f)} village {MapLayout.NearVillage(h, -4f)} buildable {buildable.Invoke(null, new object[] { h })} road {MapLayout.RoadField.Distance(h.x, h.y):F1}");
            }
        }
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

// Which parcel edges carry a woody boundary (unchanged by this pass; kept as a regression check).
static class HedgeProbe
{
    // Shared by the before and after runs so the same length-weighted share is reported.
    public static bool Edge(FarFieldPlan.Parcel p, int edge) => FieldAppearance.EdgeUse(p.edges, edge) == (int)FieldEdge.Hedge;
}

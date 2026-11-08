using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// The far villages as the flight test's strategy camera sees them: Ashcombe (hd180's village) and Brenmoor (the town),
// outside the sector, from RtsController's 300 m and 2.5 km views. The ground is the production painter (FarDetailData.Paint)
// on the 2 m tile lattice at 300 m and the 4 m regional texel at 2.5 km, filtered bilinearly as the GPU does; buildings and
// trees are the production settlement plan (SettlementPattern.Build, what SettlementHlod and FarScatter draw) as shaded
// shells and crowns with sun shadows. Also measured: the plan's buildings in hd180's 300 m frame (fitted from its 50 m
// shot), each settlement's plots and trees, the ground's colour round each settlement (the pale disc), and costs.
// One process per run; nothing starts Unity.
static class FarVillageReview
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    static readonly Vector3 Sun = new Vector3(-.45f, .8f, -.4f).normalized;

    // Fail with a short diagnostic rather than a crash dialog when run unattended.
    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Arguments: output directory, then "render" for the pictures; the measurements are always written.
    static int Run(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "Temp/far-villages/review";
        bool render = args.Length > 1 && args[1] == "render";
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        WorldPlan.Generate(1200);
        var data = WorldPlan.CaptureDetail(null);
        Console.WriteLine($"world and detail capture {timer.Elapsed.TotalMilliseconds:F0} ms (capture {WorldPlan.DetailCaptureMs:F0} ms)");
        WorldSettlement ashcombe = null, brenmoor = null;
        foreach (var s in WorldPlan.Settlements) { if (s.name == "Ashcombe") ashcombe = s; if (s.name == "Brenmoor") brenmoor = s; }
        var report = new Dictionary<string, object>();
        report["plans"] = new[] { Plan(data, ashcombe), Plan(data, brenmoor) };
        Vector2 focus = Fit(data, ashcombe, report);
        report["disc"] = new[] { Disc(data, ashcombe), Disc(data, brenmoor) };
        report["detailCaptureMs"] = WorldPlan.DetailCaptureMs;
        report["timing"] = Timing(data, new[] { ashcombe, brenmoor });
        File.WriteAllText(Path.Combine(output, "measure.json"), JsonSerializer.Serialize(report, Json));
        if (render)
        {
            var places = new[] { ("ashcombe", focus), ("brenmoor", Anchor(brenmoor)) };
            foreach (var (name, at) in places)
                foreach (float distance in new[] { 300f, 2500f })
                {
                    var t = Stopwatch.StartNew();
                    Picture(data, at, distance, Path.Combine(output, $"{name}-{distance:F0}m.ppm"));
                    Console.WriteLine($"{name} {distance:F0} m picture {t.Elapsed.TotalSeconds:F1} s");
                }
        }
        Console.WriteLine($"peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MiB");
        return 0;
    }

    // The strategy camera of RtsController.ApplyCamera: pitch from the zoom distance, 45 degree lens, 1920x1080.
    sealed class View
    {
        public Vector3 eye, forward, right, up;
        public float focal;
        public const int W = 1920, H = 1080;

        public View(Vector3 focus, float distance, float yaw)
        {
            float camera = 2000f / 480f * (float)(Math.Tan(30.0 * Math.PI / 180.0) / Math.Tan(22.5 * Math.PI / 180.0));
            float maxZoom = 110f * camera, t = Math.Max(0f, Math.Min(1f, (distance - 15f * camera) / (maxZoom - 15f * camera)));
            float pitch = 42f + 22f * t;
            if (distance > maxZoom) pitch = 64f + 8f * Math.Max(0f, Math.Min(1f, (distance - maxZoom) / (12000f - maxZoom)));
            float p = pitch * Mathf.Deg2Rad, y = yaw * Mathf.Deg2Rad;
            forward = new Vector3(Mathf.Cos(p) * Mathf.Sin(y), -Mathf.Sin(p), Mathf.Cos(p) * Mathf.Cos(y));
            right = new Vector3(Mathf.Cos(y), 0f, -Mathf.Sin(y));
            up = new Vector3(Mathf.Sin(p) * Mathf.Sin(y), Mathf.Cos(p), Mathf.Sin(p) * Mathf.Cos(y));
            eye = focus - forward * distance;
            focal = H * .5f / (float)Math.Tan(22.5 * Math.PI / 180.0);
        }

        // Screen pixel of a world point (x right, y down) and its depth along the view; false behind the eye.
        public bool Project(Vector3 point, out Vector2 screen, out float depth)
        {
            Vector3 d = point - eye;
            depth = Vector3.Dot(d, forward);
            float z = Math.Max(.01f, depth);
            screen = new Vector2(W * .5f + focal * Vector3.Dot(d, right) / z, H * .5f - focal * Vector3.Dot(d, up) / z);
            return depth > .5f;
        }

        // Inside the picture.
        public bool Sees(Vector3 point) => Project(point, out var s, out _) && s.x >= 0f && s.x < W && s.y >= 0f && s.y < H;

        // The world direction through a pixel's centre.
        public Vector3 Ray(float px, float py) => (forward * focal + right * (px + .5f - W * .5f) - up * (py + .5f - H * .5f)).normalized;
    }

    // The nearest point on any world road to the settlement's centre, as VillagePlan anchors it.
    static Vector2 Anchor(WorldSettlement s)
    {
        float nearest = float.MaxValue; Vector2 anchor = s.center;
        foreach (var road in WorldPlan.Roads)
            for (int k = 1; k < road.points.Count; k++)
            {
                Vector2 a = road.points[k - 1], d = road.points[k] - a;
                float t = d.sqrMagnitude > .01f ? Math.Max(0, Math.Min(1, Vector2.Dot(s.center - a, d) / d.sqrMagnitude)) : 0f;
                Vector2 q = a + d * t;
                if ((q - s.center).sqrMagnitude < nearest) { nearest = (q - s.center).sqrMagnitude; anchor = q; }
            }
        return anchor;
    }

    // The settlement's index in the world plan, which its plots carry as their owner.
    static int Owner(WorldSettlement s) => WorldPlan.Settlements.IndexOf(s);

    // Plots by use and what the settlement scatter draws: buildings by kind, and trees.
    static object Plan(FarDetailData data, WorldSettlement s)
    {
        int owner = Owner(s);
        int houses = 0, empty = 0, church = 0, fenced = 0;
        var uses = new int[10];
        foreach (var p in data.villages.plots)
        {
            if (p.owner != owner) continue;
            uses[p.landUse]++;
            if (p.church) church++;
            if (p.landUse < 6 && (p.model == null || p.square)) empty++;
            if (p.landUse < 6 && p.model != null && !p.square) houses++;
            if (p.landUse >= 6 && p.boundary != 0) fenced++;
        }
        int buildings = 0, trees = 0, blocks = 0;
        foreach (var block in SettlementPattern.Build(s))
        {
            blocks++;
            foreach (var h in block.houses) { if (h.kind == SettlementPattern.Tree) trees++; else buildings++; }
        }
        int fringe = uses[6] + uses[7] + uses[8] + uses[9];
        Console.WriteLine($"{s.name}: radius {s.radius:F0} m; plots: {houses} built, {empty} empty lots or squares, {fringe} fringe (paddock {uses[6]}, orchard {uses[7]}, rough {uses[8]}, vegetables {uses[9]}; {fenced} fenced); " +
                          $"scatter: {buildings} buildings, {trees} trees in {blocks} blocks");
        return new { s.name, s.town, s.radius, builtPlots = houses, emptyPlots = empty, church, fringe, paddocks = uses[6], orchards = uses[7], rough = uses[8], vegetables = uses[9],
            fencedFringe = fenced, buildings, trees, blocks };
    }

    // hd180's 50 m shot shows three houses with their roofs' middles at these pixels. The focus (the ground under the
    // Swift) is fitted to them, then the plan's buildings in the 300 m frame are listed with their pixels; the feed
    // panel (1518-1910, 826-1070) and the order bar (455-1465, 995-1070) hide some of them in the shot.
    static Vector2 Fit(FarDetailData data, WorldSettlement s, Dictionary<string, object> report)
    {
        var buildings = new List<SettlementPattern.House>();
        foreach (var block in SettlementPattern.Build(s))
            foreach (var h in block.houses) if (h.kind != SettlementPattern.Tree) buildings.Add(h);
        var seen = new[] { new Vector2(700f, 230f), new Vector2(230f, 700f), new Vector2(1430f, 650f) };
        float best = float.MaxValue; Vector2 focus = s.center;
        for (float dz = -150f; dz <= 150f; dz += 1f)
            for (float dx = -150f; dx <= 150f; dx += 1f)
            {
                Vector2 f = s.center + new Vector2(dx, dz);
                var view = new View(new Vector3(f.x, data.Height(f.x, f.y), f.y), 50f, 30f);
                float error = 0f; int inside = 0;
                foreach (var target in seen)
                {
                    float nearest = float.MaxValue;
                    foreach (var h in buildings)
                        if (view.Project(Top(data, h), out var sp, out _)) nearest = Math.Min(nearest, (sp - target).sqrMagnitude);
                    error += nearest;
                }
                foreach (var h in buildings) if (view.Sees(Top(data, h))) inside++;
                error += Math.Max(0, inside - 3) * 1e6f;
                if (error < best) { best = error; focus = f; }
            }
        Console.WriteLine($"hd180 50 m shot: focus fitted at ({focus.x:F0}, {focus.y:F0}), rms {Math.Sqrt(best / 3):F0} px over its three houses");
        var frames = new List<object>();
        foreach (float distance in new[] { 50f, 300f })
        {
            var view = new View(new Vector3(focus.x, data.Height(focus.x, focus.y), focus.y), distance, 30f);
            var list = new List<object>();
            int hidden = 0;
            foreach (var h in buildings)
            {
                var p = Top(data, h);
                if (!view.Sees(p)) continue;
                view.Project(p, out var sp, out _);
                bool panel = sp.x >= 1518f && sp.y >= 826f || sp.x >= 455f && sp.x <= 1465f && sp.y >= 995f;
                if (panel) hidden++;
                list.Add(new { model = SettlementPattern.Models[h.kind], x = h.world.x, z = h.world.y, px = sp.x, py = sp.y, underPanel = panel });
            }
            int plots = 0, built = 0, fringe = 0, fenced = 0;
            foreach (var p in data.villages.plots)
            {
                if (p.owner != Owner(s)) continue;
                Vector2 c = p.front + p.outward * (p.depth * .5f);
                if (!view.Sees(new Vector3(c.x, data.Height(c.x, c.y), c.y))) continue;
                plots++;
                if (p.landUse >= 6) { fringe++; if (p.boundary != 0) fenced++; }
                else if (p.model != null && !p.square) built++;
            }
            Console.WriteLine($"hd180 {distance:F0} m frame: {list.Count} planned buildings in it ({list.Count - hidden} in the open, {hidden} under the HUD panels); " +
                              $"{plots} plots with their middle in it: {built} built, {fringe} fringe ({fenced} fenced)");
            frames.Add(new { distance, buildings = list, plots, builtPlots = built, fringePlots = fringe, fencedFringe = fenced });
        }
        report["hd180"] = new { focusX = focus.x, focusZ = focus.y, rmsPixels = Math.Sqrt(best / 3), frames };
        return focus;
    }

    // A building's roof middle, where the shot's pixels were read.
    static Vector3 Top(FarDetailData data, SettlementPattern.House h) =>
        new Vector3(h.world.x, data.Height(h.world.x, h.world.y) + SettlementPattern.Size[h.kind].y * h.Scale + 1f, h.world.y);

    // The ground's colour at the regional texel (4 m) round a settlement, within 1.4 radii of its centre: its gardens
    // (built plots and town uses), its fringe plots (paddocks, orchards, rough grass, vegetables), and its open ground
    // outside every plot by distance from the nearest plot, against the open land 1.6-2.6 radii out. The soft disc was
    // that open ground painted as one meadow: far from the country's colour, with little spread.
    static object Disc(FarDetailData data, WorldSettlement s)
    {
        int owner = Owner(s);
        var own = new List<VillagePlot>();
        foreach (var p in data.villages.plots) if (p.owner == owner) own.Add(p);
        string[] labels = { "gardens", "fringe plots", "open ground 0-6 m from a plot", "open ground 6-20 m from a plot", "open ground 20-60 m from a plot",
            "open ground 60+ m from a plot", "open land 1.6-2.6 radii out" };
        var sums = new Vector3[labels.Length]; var squares = new Vector3[labels.Length]; var counts = new int[labels.Length];
        float reach = s.radius * 2.6f;
        var gate = new object();
        Parallel.For(0, (int)(reach * 2f / 8f), new ParallelOptions { MaxDegreeOfParallelism = 2 }, j =>
        {
            var localSums = new Vector3[sums.Length]; var localSquares = new Vector3[sums.Length]; var localCounts = new int[sums.Length];
            float z = -reach + j * 8f;
            for (float x = -reach; x < reach; x += 8f)
            {
                float r = (float)Math.Sqrt(x * x + z * z);
                Vector2 at = s.center + new Vector2(x, z);
                if (data.cover.Forest(at.x, at.y) > 0f || data.CoverAt(at.x, at.y) == Cover.Water) continue;
                var kind = data.CoverAt(at.x, at.y);
                bool settled = kind == Cover.Village || kind == Cover.Town;
                int band;
                if (r > s.radius * 1.6f) { if (settled) continue; band = 6; }
                else if (r > s.radius * 1.4f) continue;
                else if (data.villages.At(at, out var plot)) { if (plot.owner != owner) continue; band = plot.landUse >= 6 ? 1 : 0; }
                else
                {
                    if (!settled) continue;
                    float clearance = Clearance(own, at, 60f);
                    band = clearance < 6f ? 2 : clearance < 20f ? 3 : clearance < 60f ? 4 : 5;
                }
                Vector3 c = FarDetailPalette.Decode(data.Paint(at.x, at.y, at, null, null, 4f));
                Vector3 srgb = new Vector3(Srgb(c.x), Srgb(c.y), Srgb(c.z));
                localSums[band] += srgb; localSquares[band] += new Vector3(srgb.x * srgb.x, srgb.y * srgb.y, srgb.z * srgb.z); localCounts[band]++;
            }
            lock (gate) for (int b = 0; b < sums.Length; b++) { sums[b] += localSums[b]; squares[b] += localSquares[b]; counts[b] += localCounts[b]; }
        });
        var bands = new List<object>();
        Vector3 reference = sums[6] / Math.Max(1, counts[6]);
        for (int b = 0; b < sums.Length; b++)
        {
            if (counts[b] == 0) continue;
            Vector3 mean = sums[b] / counts[b];
            Vector3 variance = squares[b] / counts[b] - new Vector3(mean.x * mean.x, mean.y * mean.y, mean.z * mean.z);
            float spread = (float)Math.Sqrt(Math.Max(0f, (variance.x + variance.y + variance.z) / 3f)) * 255f;
            float difference = (mean - reference).magnitude * 255f;
            bands.Add(new { band = labels[b], samples = counts[b], r = mean.x * 255f, g = mean.y * 255f, b = mean.z * 255f, spread, fromOpenLand = difference });
            Console.WriteLine($"{s.name} {labels[b]}: sRGB ({mean.x * 255f:F0}, {mean.y * 255f:F0}, {mean.z * 255f:F0}) spread {spread:F1}, {difference:F1} from the open land ({counts[b]} samples)");
        }
        return new { s.name, bands };
    }

    // How far a point lies outside every one of the given plots, up to reach metres; 0 inside one.
    static float Clearance(List<VillagePlot> plots, Vector2 p, float reach)
    {
        float best = reach;
        foreach (var q in plots)
        {
            Vector2 c = q.front + q.outward * (q.depth * .5f);
            if ((c - p).sqrMagnitude > (reach + q.depth + q.frontage) * (reach + q.depth + q.frontage)) continue;
            q.Coordinates(p, out float u, out float v);
            float du = Math.Max(0f, Math.Abs(u) - q.frontage * .5f), dv = v < 0f ? -v : Math.Max(0f, v - q.depth);
            best = Math.Min(best, (float)Math.Sqrt(du * du + dv * dv));
            if (best <= 0f) return 0f;
        }
        return best;
    }

    // The sRGB transfer of a linear channel.
    static float Srgb(float x) => x <= .0031308f ? x * 12.92f : 1.055f * (float)Math.Pow(x, 1.0 / 2.4) - .055f;

    // Worker and main-thread costs: the settlement plan pass (SettlementHlod's first step on the main thread), the 2 m
    // tile and the 4 m regional page under each place, best of three.
    static object Timing(FarDetailData data, WorldSettlement[] places)
    {
        var result = new Dictionary<string, object>();
        // The whole theatre's village plan, rebuilt from the same roads as the capture builds it (world setup).
        var roads = new List<FarDetailData.Segment>();
        int route = 0;
        foreach (var road in WorldPlan.Roads)
        {
            float walked = 0f;
            for (int k = 1; k < road.points.Count; k++)
            {
                float width = RoadAppearance.HalfWidth(road.kind);
                roads.Add(new FarDetailData.Segment { ax = road.points[k - 1].x, az = road.points[k - 1].y, bx = road.points[k].x, bz = road.points[k].y,
                    widthA = width, widthB = width, kind = (int)road.kind, route = route, walked = walked });
                walked += Vector2.Distance(road.points[k - 1], road.points[k]);
            }
            route++;
        }
        double plan = double.MaxValue;
        for (int i = 0; i < 3; i++) { var t = Stopwatch.StartNew(); new VillagePlan(WorldPlan.Seed, roads.ToArray(), WorldPlan.Settlements); plan = Math.Min(plan, t.Elapsed.TotalMilliseconds); }
        Console.WriteLine($"village plan (all {WorldPlan.Settlements.Count} settlements) {plan:F0} ms");
        result["villagePlanMs"] = plan;
        var roughness = new float[FarDetailData.Samples * FarDetailData.Samples];
        var coarse = new float[14 * 14][];
        for (int i = 0; i < coarse.Length; i++) coarse[i] = new float[65 * 65];
        var tileData = WorldPlan.CaptureDetail(coarse);
        foreach (var s in places)
        {
            double pattern = double.MaxValue, tile = double.MaxValue, page = double.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                var t = Stopwatch.StartNew(); int n = 0;
                foreach (var block in SettlementPattern.Build(s)) n += block.houses.Count;
                if (i > 0) pattern = Math.Min(pattern, t.Elapsed.TotalMilliseconds);
            }
            Vector2 at = Anchor(s);
            int x = (int)Math.Floor(at.x / FarDetailData.Size), z = (int)Math.Floor(at.y / FarDetailData.Size);
            tileData.Build(x, z, roughness, null);
            for (int i = 0; i < 3; i++) { var t = Stopwatch.StartNew(); tileData.Build(x, z, roughness, null); tile = Math.Min(tile, t.Elapsed.TotalMilliseconds); }
            int rx = (int)Math.Floor(at.x / FarDetailRegionData.Metres), rz = (int)Math.Floor(at.y / FarDetailRegionData.Metres);
            for (int i = 0; i < 3; i++) { var t = Stopwatch.StartNew(); FarDetailRegionData.Build(data, rx, rz); page = Math.Min(page, t.Elapsed.TotalMilliseconds); }
            Console.WriteLine($"{s.name}: settlement plan pass {pattern:F2} ms, 2 m tile {tile:F0} ms, 4 m regional page {page:F0} ms");
            result[s.name] = new { settlementPlanMs = pattern, tileMs = tile, regionalPageMs = page };
        }
        return result;
    }

    // The 1920x1080 strategy view at the given zoom distance (yaw 30, as the flight test looks): ground, then shadows,
    // then building shells and tree crowns, depth-tested.
    static void Picture(FarDetailData data, Vector2 focus, float distance, string path)
    {
        const int w = View.W, h = View.H;
        float groundHeight = data.Height(focus.x, focus.y);
        var view = new View(new Vector3(focus.x, groundHeight, focus.y), distance, 30f);
        float texel = distance < 1000f ? 2f : 4f;
        // Where each pixel's ray meets the ground (three refinements on the world heights).
        var hits = new Vector3[w * h];
        var depth = new float[w * h];
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                Vector3 ray = view.Ray(i, j), p = view.eye;
                float level = groundHeight;
                for (int k = 0; k < 4; k++)
                {
                    float t = (view.eye.y - level) / Math.Max(.02f, -ray.y);
                    p = view.eye + ray * t;
                    level = data.Height(p.x, p.z);
                }
                hits[j * w + i] = p; depth[j * w + i] = Vector3.Dot(p - view.eye, view.forward);
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minZ = Math.Min(minZ, p.z); maxZ = Math.Max(maxZ, p.z);
            }
        // The painter's lattice over that footprint (texel centres as the tiles and pages place them), lit by the hills.
        int gx0 = (int)Math.Floor(minX / texel) - 1, gz0 = (int)Math.Floor(minZ / texel) - 1;
        int gw = (int)Math.Ceiling((maxX - minX) / texel) + 4, gh = (int)Math.Ceiling((maxZ - minZ) / texel) + 4;
        var lattice = new Vector3[gw * gh];
        Parallel.For(0, gh, new ParallelOptions { MaxDegreeOfParallelism = 2 }, j =>
        {
            for (int i = 0; i < gw; i++)
            {
                float x = (gx0 + i + .5f) * texel, z = (gz0 + j + .5f) * texel;
                Vector3 c = FarDetailPalette.Decode(data.Paint(x, z, new Vector2(x, z), null, null, texel));
                float wood = data.cover.Forest(x, z);
                if (wood > 0f)
                {
                    float crown = .6f + .4f * data.cover.Noise(x, z, 9f, 5) + .25f * data.cover.Noise(x, z, 3.5f, 6);
                    c = Vector3.Lerp(c, new Vector3(.030f, .048f, .020f) * crown, Math.Min(1f, wood * 1.4f));
                }
                lattice[j * gw + i] = c * Hill(data, x, z);
            }
        });
        var colour = new Vector3[w * h];
        Parallel.For(0, h, new ParallelOptions { MaxDegreeOfParallelism = 2 }, j =>
        {
            for (int i = 0; i < w; i++)
            {
                Vector3 p = hits[j * w + i];
                float fx = p.x / texel - .5f - gx0, fz = p.z / texel - .5f - gz0;
                int ix = Math.Max(0, Math.Min(gw - 2, (int)Math.Floor(fx))), iz = Math.Max(0, Math.Min(gh - 2, (int)Math.Floor(fz)));
                float u = Math.Max(0f, Math.Min(1f, fx - ix)), v = Math.Max(0f, Math.Min(1f, fz - iz));
                colour[j * w + i] = Vector3.Lerp(Vector3.Lerp(lattice[iz * gw + ix], lattice[iz * gw + ix + 1], u),
                    Vector3.Lerp(lattice[(iz + 1) * gw + ix], lattice[(iz + 1) * gw + ix + 1], u), v);
            }
        });
        // Every settlement near the view: its plan's shells and crowns, as triangles.
        var triangles = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 colour, Vector3 normal)>();
        float range = distance < 1000f ? 1500f : 6000f;
        foreach (var s in WorldPlan.Settlements)
        {
            if ((s.center - focus).magnitude > s.radius + range) continue;
            foreach (var block in SettlementPattern.Build(s))
                foreach (var house in block.houses) Shell(data, house, triangles);
        }
        foreach (var f in WorldPlan.Features)
            if (f.valid && (f.center - focus).magnitude < range)
                foreach (var b in f.buildings) Box(data, b, triangles);
        // Shadows first: each triangle cast along the sun onto the ground beneath it, darkening only bare ground.
        var shade = new bool[w * h];
        foreach (var (a, b, c, _, _) in triangles)
        {
            float floor = data.Height(a.x, a.z);
            Vector3 Cast(Vector3 q) => q - Sun * ((q.y - floor) / Sun.y);
            Raster(view, Cast(a) + Vector3.up * .05f, Cast(b) + Vector3.up * .05f, Cast(c) + Vector3.up * .05f, (k, z) =>
            {
                if (Math.Abs(z - depth[k]) < 3f + depth[k] * .004f) shade[k] = true;
            });
        }
        for (int k = 0; k < shade.Length; k++) if (shade[k]) colour[k] *= .58f;
        foreach (var (a, b, c, tint, normal) in triangles)
        {
            Vector3 c0 = tint * (.48f + .62f * Math.Max(0f, Vector3.Dot(normal, Sun)));
            Raster(view, a, b, c, (k, z) => { if (z < depth[k]) { depth[k] = z; colour[k] = c0; } });
        }
        var pixels = new byte[w * h * 3];
        for (int k = 0; k < w * h; k++)
        {
            var b = FarDetailPalette.Encode(colour[k]);
            pixels[k * 3] = b.r; pixels[k * 3 + 1] = b.g; pixels[k * 3 + 2] = b.b;
        }
        Ppm(path, w, h, pixels);
    }

    // SettlementHlod's roof and wall shell of a building in the HLOD palette (walls, two roof planes and gables), or a
    // tree's crown: a low eight-sided double cone in the crown's lit and shaded greens.
    static void Shell(FarDetailData data, SettlementPattern.House house, List<(Vector3, Vector3, Vector3, Vector3, Vector3)> triangles)
    {
        float ground = data.Height(house.world.x, house.world.y) - .2f, scale = house.Scale;
        float yaw = house.yaw * Mathf.Deg2Rad;
        Vector3 ax = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw)), az = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        Vector3 origin = new Vector3(house.world.x, ground, house.world.y);
        Vector3 At(float x, float y, float z) => origin + (ax * x + az * z) * scale + Vector3.up * (y * scale);
        if (house.kind == SettlementPattern.Tree)
        {
            Vector3 size = SettlementPattern.Size[house.kind];
            float radius = size.x * .46f, middle = size.y * .55f, top = size.y, bottom = size.y * .22f;
            Vector3 lit = FarDetailPalette.Decode(SettlementPattern.Palette[12]), dark = FarDetailPalette.Decode(SettlementPattern.Palette[13]);
            Vector3 peak = At(0f, top, 0f), foot = At(0f, bottom, 0f);
            for (int k = 0; k < 8; k++)
            {
                float a0 = k * Mathf.PI / 4f, a1 = (k + 1) * Mathf.PI / 4f;
                Vector3 p0 = At(Mathf.Cos(a0) * radius, middle, Mathf.Sin(a0) * radius), p1 = At(Mathf.Cos(a1) * radius, middle, Mathf.Sin(a1) * radius);
                Add(triangles, peak, p0, p1, lit, At(0f, middle, 0f)); Add(triangles, foot, p1, p0, dark, At(0f, middle, 0f));
            }
            return;
        }
        Vector3 s = SettlementPattern.Size[house.kind];
        float w = s.x * .5f, d = s.z * .5f, hgt = s.y, r = SettlementPattern.Roof[house.kind];
        Vector3 wall = FarDetailPalette.Decode(SettlementPattern.Palette[SettlementPattern.WallTexel(house)]);
        Vector3 roof = FarDetailPalette.Decode(SettlementPattern.Palette[SettlementPattern.RoofTexel(house)]);
        Vector3 a = At(-w, 0f, -d), b = At(w, 0f, -d), c = At(w, 0f, d), e = At(-w, 0f, d);
        Vector3 at = At(-w, hgt, -d), bt = At(w, hgt, -d), ct = At(w, hgt, d), et = At(-w, hgt, d);
        Vector3 ridgeA = At(-w, hgt + r, 0f), ridgeB = At(w, hgt + r, 0f);
        Vector3 centre = At(0f, hgt * .5f, 0f);
        void Face(Vector3 p, Vector3 q, Vector3 t, Vector3 u, Vector3 tint) { Add(triangles, p, q, t, tint, centre); Add(triangles, p, t, u, tint, centre); }
        Face(a, at, bt, b, wall); Face(b, bt, ct, c, wall); Face(c, ct, et, e, wall); Face(e, et, at, a, wall);
        Face(at, ridgeA, ridgeB, bt, roof); Face(ridgeA, et, ct, ridgeB, roof);
        if (r > 0f) { Add(triangles, et, ridgeA, at, wall, centre); Add(triangles, bt, ridgeB, ct, wall, centre); }
    }

    // A landmark building as a grey box.
    static void Box(FarDetailData data, LandmarkBuilding b, List<(Vector3, Vector3, Vector3, Vector3, Vector3)> triangles)
    {
        float ground = data.Height(b.position.x, b.position.y);
        Vector3 f = new Vector3(b.facing.x, 0f, b.facing.y), r = new Vector3(b.facing.y, 0f, -b.facing.x);
        Vector3 o = new Vector3(b.position.x, ground, b.position.y), grey = FarDetailPalette.Decode(new Color32(122, 122, 118, 255));
        Vector3 C(float x, float y, float z) => o + r * (x * b.size.x * .5f) + f * (z * b.size.z * .5f) + Vector3.up * (y * b.size.y);
        Vector3[] p = { C(-1, 0, -1), C(1, 0, -1), C(1, 0, 1), C(-1, 0, 1), C(-1, 1, -1), C(1, 1, -1), C(1, 1, 1), C(-1, 1, 1) };
        int[] q = { 0, 4, 5, 1, 1, 5, 6, 2, 2, 6, 7, 3, 3, 7, 4, 0, 4, 7, 6, 5 };
        Vector3 centre = C(0, .5f, 0);
        for (int k = 0; k < q.Length; k += 4) { Add(triangles, p[q[k]], p[q[k + 1]], p[q[k + 2]], grey, centre); Add(triangles, p[q[k]], p[q[k + 2]], p[q[k + 3]], grey, centre); }
    }

    // One triangle with its normal turned away from the solid's centre.
    static void Add(List<(Vector3, Vector3, Vector3, Vector3, Vector3)> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 colour, Vector3 centre)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        if (Vector3.Dot(normal, (a + b + c) * (1f / 3f) - centre) < 0f) normal = -normal;
        triangles.Add((a, b, c, colour, normal));
    }

    // Scan-convert one world triangle into the picture with perspective-correct depth, calling back per covered pixel.
    static void Raster(View view, Vector3 a, Vector3 b, Vector3 c, Action<int, float> plot)
    {
        if (!view.Project(a, out var pa, out float za) || !view.Project(b, out var pb, out float zb) || !view.Project(c, out var pc, out float zc)) return;
        float area = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
        if (Math.Abs(area) < 1e-6f) return;
        int x0 = Math.Max(0, (int)Math.Floor(Math.Min(pa.x, Math.Min(pb.x, pc.x)))), x1 = Math.Min(View.W - 1, (int)Math.Ceiling(Math.Max(pa.x, Math.Max(pb.x, pc.x))));
        int y0 = Math.Max(0, (int)Math.Floor(Math.Min(pa.y, Math.Min(pb.y, pc.y)))), y1 = Math.Min(View.H - 1, (int)Math.Ceiling(Math.Max(pa.y, Math.Max(pb.y, pc.y))));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + .5f, py = y + .5f;
                float wa = ((pb.x - px) * (pc.y - py) - (pb.y - py) * (pc.x - px)) / area;
                float wb = ((pc.x - px) * (pa.y - py) - (pc.y - py) * (pa.x - px)) / area;
                float wc = 1f - wa - wb;
                if (wa < 0f || wb < 0f || wc < 0f) continue;
                float inverse = wa / za + wb / zb + wc / zc;
                plot(y * View.W + x, 1f / inverse);
            }
    }

    // A mild hillshade from the world heights, so slopes read as they do under the game's sun.
    static float Hill(FarDetailData data, float x, float z)
    {
        const float s = 8f;
        float dx = (data.Height(x + s, z) - data.Height(x - s, z)) / (2f * s), dz = (data.Height(x, z + s) - data.Height(x, z - s)) / (2f * s);
        var n = new Vector3(-dx, 1f, -dz).normalized;
        return .82f + .3f * Math.Max(0f, Vector3.Dot(n, Sun)) - .3f * Sun.y;
    }

    // Binary PPM, converted to PNG by run.py.
    static void Ppm(string path, int w, int h, byte[] rgb)
    {
        using var file = File.Create(path);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
        file.Write(header, 0, header.Length); file.Write(rgb, 0, rgb.Length);
    }
}

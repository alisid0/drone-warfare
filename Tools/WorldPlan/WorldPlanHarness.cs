// Compile the actual production WorldPlan*.cs; only Unity's Vector2 data carrier is replaced.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using DroneSandbox;
using UnityEngine;

namespace UnityEngine
{
    // Matches the only Unity API used in production: Vector2.cs fields and its two-float constructor.
    public struct Vector2
    {
        public float x, y;
        // Store a world-space horizontal coordinate, without any native Unity state.
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
}

// Independent invariant checks, measurements and maps for a headless, unimported worktree.
static class WorldPlanHarness
{
    static readonly Color[] Palette = { Color.FromArgb(147, 164, 112), Color.FromArgb(190, 181, 117), Color.FromArgb(48, 87, 65),
        Color.FromArgb(100, 139, 130), Color.FromArgb(75, 139, 166), Color.FromArgb(175, 151, 130), Color.FromArgb(192, 169, 140), Color.FromArgb(153, 151, 145) };
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    static int checks;

    // Generate seeds separately so the reported memory excludes the renderer and previous world's grids.
    static int Main(string[] args)
    {
        try
        {
            string output = args.Length > 0 ? args[0] : "Temp/WorldPlan";
            Directory.CreateDirectory(output);
            int seed = args.Length > 1 ? int.Parse(args[1]) : 1;
            bool maps = args.Length < 3 || args[2] != "--check";
            var timer = Stopwatch.StartNew();
            WorldPlan.Generate(seed); timer.Stop();
            double retained = GC.GetTotalMemory(true) / 1048576.0;
            double peak = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0;
            var metrics = Inspect(seed, timer.Elapsed.TotalMilliseconds, retained, peak);
            metrics["query_benchmark"] = BenchmarkQueries();
            string signature = Fingerprint(); metrics["sha256"] = signature;
            Console.WriteLine(Json.Serialize(metrics));
            File.WriteAllText(Path.Combine(output, "seed-" + seed + "-metrics.json"), Json.Serialize(metrics));
            if (maps)
            {
                Render(Path.Combine(output, "seed-" + seed + "-atlas.png"), seed, new Vector2(0f, 0f), 56000f, false);
                Render(Path.Combine(output, "seed-" + seed + "-hillshade.png"), seed, new Vector2(0f, 0f), 56000f, true);
                if (seed == 1)
                {
                    WorldLake reservoir = WorldPlan.Lakes[1];
                    Vector2 centre = reservoir.outlet;
                    centre.x = Math.Max(-24000f, Math.Min(24000f, centre.x)); centre.y = Math.Max(-24000f, Math.Min(24000f, centre.y + 1200f));
                    Render(Path.Combine(output, "seed-1-reservoir-8km.png"), seed, centre, 8000f, false);
                    centre = WorldPlan.Settlements[0].center; centre.y -= 1400f;
                    Render(Path.Combine(output, "seed-1-detail-8km.png"), seed, centre, 8000f, false);
                }
                File.WriteAllText(Path.Combine(output, "seed-" + seed + "-vectors.json"), Json.Serialize(new
                {
                    seed = seed, version = WorldPlan.GeneratorVersion, rivers = WorldPlan.Rivers, roads = WorldPlan.Roads,
                    railways = WorldPlan.Railways, settlements = WorldPlan.Settlements, sites = WorldPlan.Sites,
                    lakes = WorldPlan.Lakes, dams = WorldPlan.Dams, bridges = WorldPlan.Bridges, farms = WorldPlan.Farms, front = WorldPlan.FrontLine
                }));
            }
            // Parallel readers must match serial samples, and a full regeneration must match byte for byte.
            CheckParallel();
            WorldPlan.Generate(seed);
            Require(Fingerprint() == signature, "same-seed complete fingerprint");
            Console.WriteLine("PASS " + checks + " invariant checks; deterministic regeneration and parallel readers.");
            File.WriteAllText(Path.Combine(output, "seed-" + seed + "-checks.txt"), "PASS " + checks + " checks; same-seed complete fingerprint and parallel readers.\r\n");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Measure the whole grid and validate meaningful geometry/placement contracts, not implementation details.
    static Dictionary<string, object> Inspect(int seed, double milliseconds, double memory, double peak)
    {
        Require(WorldPlan.Ready, "published world");
        var coverage = new Dictionary<string, double>(); var counts = new long[8];
        float low = float.MaxValue, high = float.MinValue;
        const int n = 1751;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = -28000f + i * 32f, z = -28000f + j * 32f, h = WorldPlan.Height(x, z);
                Require(!float.IsNaN(h) && !float.IsInfinity(h), "finite height");
                low = Math.Min(low, h); high = Math.Max(high, h); counts[(int)WorldPlan.CoverAt(x, z)]++;
            }
        for (int c = 0; c < 8; c++) coverage[((Cover)c).ToString()] = counts[c] * 100.0 / (n * n);
        var riverLengths = new Dictionary<string, double>();
        foreach (var r in WorldPlan.Rivers)
        {
            Require(r.points.Count == r.halfWidths.Count && r.points.Count == r.waterLevels.Count, "river array alignment");
            Require(r.halfWidths[0] > 0f && r.halfWidths[0] <= 20f, "source width");
            double length = 0;
            for (int k = 1; k < r.points.Count; k++)
            {
                Require(r.waterLevels[k] <= r.waterLevels[k - 1] + 0.0001f, "water flows downhill");
                Require(r.halfWidths[k] > 0f && r.halfWidths[k] <= 20f, "river widths");
                length += Distance(r.points[k - 1], r.points[k]);
            }
            riverLengths[r.name] = length / 1000;
        }
        var roadCounts = new Dictionary<string, int>(); var roadLengths = new Dictionary<string, double>();
        foreach (RoadClass kind in Enum.GetValues(typeof(RoadClass)))
        {
            int count = 0; double length = 0;
            foreach (var r in WorldPlan.Roads) if (r.kind == kind) { count++; length += Length(r.points); }
            roadCounts[kind.ToString()] = count; roadLengths[kind.ToString()] = length / 1000;
        }
        var launches = new List<Vector2>(); var depths = new List<float>();
        foreach (var s in WorldPlan.Sites)
        {
            Require(WorldPlan.Inside(s.position.x, s.position.y), "site inside");
            if (s.kind == WorldSiteKind.Landmark) continue;
            Require(WorldPlan.CoverAt(s.position.x, s.position.y) != Cover.Water && WorldPlan.CoverAt(s.position.x, s.position.y) != Cover.Wetland, "dry installation " + s.name);
            Require(Grade(s.position) < (s.kind == WorldSiteKind.Airbase ? 0.019f : 0.056f), "flat installation");
            Require(NearestRoad(s.position) < 2f, "connected installation " + s.name);
            if (s.kind == WorldSiteKind.LaunchSite)
            {
                launches.Add(s.position); float depth = WorldPlan.FrontAt(s.position.y).x - s.position.x; depths.Add(depth / 1000f);
                Require(depth >= 4000f && depth <= 10000f, "launch front offset");
            }
            if (s.kind == WorldSiteKind.EnemyHq)
            {
                float depth = s.position.x - WorldPlan.FrontAt(s.position.y).x;
                Require(depth >= 6000f && depth <= 12000f, "HQ front offset");
            }
        }
        float min = float.MaxValue, max = 0f, nnMin = float.MaxValue, nnMax = 0f;
        for (int k = 0; k < launches.Count; k++)
        {
            float nearest = float.MaxValue;
            for (int j = 0; j < launches.Count; j++) if (j != k) nearest = Math.Min(nearest, Distance(launches[k], launches[j]));
            nnMin = Math.Min(nnMin, nearest); nnMax = Math.Max(nnMax, nearest);
            if (k == 0) continue;
            float d = Distance(launches[k - 1], launches[k]); min = Math.Min(min, d); max = Math.Max(max, d);
        }
        Require(launches.Count >= 8 && launches.Count <= 12 && min >= 8000 && max <= 12000 && nnMin >= 8000, "launch spacing and count");
        foreach (var s in WorldPlan.Settlements)
        {
            Require(WorldPlan.CoverAt(s.center.x, s.center.y) == (s.town ? Cover.Town : Cover.Village), "settlement cover");
            Require(NearestRoad(s.center) < 2f, "connected settlement");
        }
        Require(WorldPlan.FarmedShare >= 0.60f && WorldPlan.FarmedShare <= 0.75f, "eligible farming fraction");
        Require(high >= 400f && high <= 650f, "northern range height");
        var cellKinds = new Dictionary<string, int>();
        for (int z = 0; z < 56; z++)
            for (int x = 0; x < 56; x++)
            {
                WorldCell c = WorldPlan.Cell(x, z);
                Require(c.forest >= 0f && c.fields >= 0f && c.water >= 0f && c.forest + c.fields + c.water <= 1.00001f, "cell shares");
                int expected; unchecked { expected = ((seed * 73856093) ^ (x * 19349663) ^ (z * 83492791) ^ (WorldPlan.GeneratorVersion * 1000003)) & 0x7fffffff; }
                Require(c.seed == expected && c.x == x && c.z == z, "cell identity");
                string name = c.kind.ToString(); if (!cellKinds.ContainsKey(name)) cellKinds[name] = 0; cellKinds[name]++;
            }
        Require(WorldPlan.Height(30000f, 30000f) == WorldPlan.Height(28000f, 28000f), "clamped height boundary");
        var geometry = InspectGeometry();
        return new Dictionary<string, object>
        {
            { "seed", seed }, { "version", WorldPlan.GeneratorVersion }, { "generate_ms", milliseconds }, { "retained_managed_MiB", memory }, { "peak_working_set_MiB", peak },
            { "cover_percent", coverage }, { "river_km", riverLengths }, { "road_counts", roadCounts }, { "road_km", roadLengths },
            { "settlements", WorldPlan.Settlements.Count }, { "towns", 4 }, { "villages", WorldPlan.Settlements.Count - 4 }, { "railways", WorldPlan.Railways.Count },
            { "lakes", WorldPlan.Lakes.Count }, { "dams", WorldPlan.Dams.Count }, { "bridges", WorldPlan.Bridges.Count }, { "farms", WorldPlan.Farms.Count },
            { "launch_sites", launches.Count }, { "launch_adjacent_min_km", min / 1000f }, { "launch_adjacent_max_km", max / 1000f },
            { "launch_nearest_min_km", nnMin / 1000f }, { "launch_nearest_max_km", nnMax / 1000f }, { "launch_depths_km", depths },
            { "height_min_m", low }, { "height_max_m", high }, { "farmable_km2", WorldPlan.FarmableSquareKm }, { "farmable_farmed_share", WorldPlan.FarmedShare }, { "cell_kinds", cellKinds }, { "geometry", geometry }
        };
    }

    // Check water/land consistency and final route geometry independently of route-grid costs.
    static Dictionary<string, object> InspectGeometry()
    {
        float bedAbove = -1000f; int lakeRoadSamples = 0, shoreCrossings = 0, riverCrossings = 0; float maxRoadGrade = 0f, maxRailGrade = 0f;
        Vector2 worstBed = new Vector2(), worstRail = new Vector2(), worstRoad = new Vector2();
        foreach (var river in WorldPlan.Rivers)
            for (int k = 0; k < river.points.Count; k++)
            {
                Vector2 p = river.points[k];
                float difference = WorldPlan.Height(p.x, p.y) - river.waterLevels[k];
                if (difference > bedAbove) { bedAbove = difference; worstBed = p; }
            }
        for (int mode = 0; mode < 2; mode++)
            foreach (var route in mode == 0 ? WorldPlan.Roads : WorldPlan.Railways)
                for (int k = 1; k < route.points.Count; k++)
                {
                    Vector2 a = route.points[k - 1], b = route.points[k];
                    float length = Distance(a, b);
                    if (length < 0.01f) continue;
                    float grade = Math.Abs(WorldPlan.Height(a.x, a.y) - WorldPlan.Height(b.x, b.y)) / length;
                    bool bridge = false;
                    foreach (var crossing in WorldPlan.Bridges) if (Distance(crossing.position, a) < Distance(crossing.a, crossing.b) * 0.5f + 120f) bridge = true;
                    if (!bridge)
                    {
                        if (mode == 0 && grade > maxRoadGrade) { maxRoadGrade = grade; worstRoad = a; }
                        else if (mode == 1 && grade > maxRailGrade) { maxRailGrade = grade; worstRail = a; }
                    }
                    foreach (var lake in WorldPlan.Lakes) if (InPolygon(a, lake.shore)) lakeRoadSamples++;
                }
        foreach (var lake in WorldPlan.Lakes)
            for (int a = 0; a < lake.shore.Count; a++)
                for (int b = a + 2; b < lake.shore.Count; b++)
                {
                    if (a == 0 && b == lake.shore.Count - 1) continue;
                    if (Crosses(lake.shore[a], lake.shore[(a + 1) % lake.shore.Count], lake.shore[b], lake.shore[(b + 1) % lake.shore.Count])) shoreCrossings++;
                }
        for (int a = 0; a < WorldPlan.Rivers.Count; a++)
            for (int b = a + 1; b < WorldPlan.Rivers.Count; b++)
            {
                var first = WorldPlan.Rivers[a]; var second = WorldPlan.Rivers[b];
                for (int i = 1; i < first.points.Count; i++)
                    for (int j = 1; j < second.points.Count; j++)
                        if (Crosses(first.points[i - 1], first.points[i], second.points[j - 1], second.points[j])) riverCrossings++;
            }
        Require(bedAbove <= 0f, "river beds below their water surface");
        Require(lakeRoadSamples == 0, "transport avoids lake interiors");
        Require(shoreCrossings == 0, "simple lake shorelines");
        Require(riverCrossings == 0, "tributaries merge rather than cross the trunk");
        for (int r = 1; r < WorldPlan.Rivers.Count; r++)
        {
            var tributary = WorldPlan.Rivers[r]; Vector2 end = tributary.points[tributary.points.Count - 1];
            bool joined = false;
            for (int k = 0; k < WorldPlan.Rivers[0].points.Count; k++)
                if (Distance(end, WorldPlan.Rivers[0].points[k]) < 0.01f)
                    joined = Math.Abs(tributary.waterLevels[tributary.waterLevels.Count - 1] - WorldPlan.Rivers[0].waterLevels[k]) < 0.001f;
            Require(joined, "confluence positions and elevations agree");
        }
        foreach (var bridge in WorldPlan.Bridges)
        {
            Require(!float.IsNaN(bridge.a.x) && !float.IsNaN(bridge.b.x) && bridge.deckLevel > 0f, "finite bridge span");
            Require(bridge.route >= 0 && bridge.route < (bridge.railway ? WorldPlan.Railways.Count : WorldPlan.Roads.Count), "bridge route index");
        }
        return new Dictionary<string, object> { { "river_bed_above_water_max_m", bedAbove }, { "transport_points_inside_lakes", lakeRoadSamples }, { "shore_self_intersections", shoreCrossings }, { "unjoined_river_crossings", riverCrossings },
            { "max_ground_grade_along_roads_outside_bridges", maxRoadGrade }, { "max_ground_grade_along_rail_outside_bridges", maxRailGrade }, { "worst_bed", worstBed }, { "worst_rail", worstRail }, { "worst_road", worstRoad } };
    }

    // Strict segment intersections exclude ordinary shared polygon endpoints.
    static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        double abx = b.x - a.x, aby = b.y - a.y, cdx = d.x - c.x, cdy = d.y - c.y;
        double det = abx * cdy - aby * cdx;
        if (Math.Abs(det) < 0.0001) return false;
        double t = ((c.x - a.x) * cdy - (c.y - a.y) * cdx) / det;
        double u = ((c.x - a.x) * aby - (c.y - a.y) * abx) / det;
        return t > 0.00001 && t < 0.99999 && u > 0.00001 && u < 0.99999;
    }

    // Ray casting tests exported shores directly, without consulting the generation raster.
    static bool InPolygon(Vector2 p, List<Vector2> polygon)
    {
        bool hit = false;
        for (int a = 0, b = polygon.Count - 1; a < polygon.Count; b = a++)
        {
            Vector2 u = polygon[a], v = polygon[b];
            if ((u.y > p.y) != (v.y > p.y) && p.x < u.x + (v.x - u.x) * (p.y - u.y) / (v.y - u.y)) hit = !hit;
        }
        return hit;
    }

    // Independent segment-distance calculation checks actual network attachment.
    static float NearestRoad(Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var r in WorldPlan.Roads)
            for (int k = 1; k < r.points.Count; k++)
            {
                Vector2 a = r.points[k - 1], b = r.points[k]; float dx = b.x - a.x, dz = b.y - a.y;
                float t = Math.Max(0f, Math.Min(1f, ((p.x - a.x) * dx + (p.y - a.y) * dz) / Math.Max(0.001f, dx * dx + dz * dz)));
                best = Math.Min(best, Distance(p, new Vector2(a.x + dx * t, a.y + dz * t)));
            }
        return best;
    }
    // Independent slope sampling catches placements on steep banks.
    static float Grade(Vector2 p)
    {
        float dx = WorldPlan.Height(p.x + 64f, p.y) - WorldPlan.Height(p.x - 64f, p.y), dz = WorldPlan.Height(p.x, p.y + 64f) - WorldPlan.Height(p.x, p.y - 64f);
        return (float)Math.Sqrt(dx * dx + dz * dz) / 128f;
    }
    // World-space distance for lengths and separation checks.
    static float Distance(Vector2 a, Vector2 b) { float x = a.x - b.x, z = a.y - b.y; return (float)Math.Sqrt(x * x + z * z); }
    // Measure the exported polyline, including its bends.
    static double Length(List<Vector2> line) { double sum = 0; for (int k = 1; k < line.Count; k++) sum += Distance(line[k - 1], line[k]); return sum; }
    // Stop immediately on an invariant violation rather than producing reassuring but invalid previews.
    static void Require(bool condition, string message) { checks++; if (!condition) throw new Exception("FAIL: " + message); }

    // Hash every height/cover sample, cell record and exported feature to catch hidden regeneration drift.
    static string Fingerprint()
    {
        using (var hash = SHA256.Create())
        using (var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write))
        using (var w = new BinaryWriter(stream))
        {
            for (int z = 0; z < 1751; z++)
                for (int x = 0; x < 1751; x++)
                {
                    float wx = -28000f + x * 32f, wz = -28000f + z * 32f;
                    w.Write(WorldPlan.Height(wx, wz)); w.Write((byte)WorldPlan.CoverAt(wx, wz)); w.Write(WorldPlan.RiverDistance(wx, wz)); w.Write(WorldPlan.WaterLevel(wx, wz));
                }
            for (int z = 0; z < 56; z++) for (int x = 0; x < 56; x++)
            {
                var c = WorldPlan.Cell(x, z); w.Write(c.seed); w.Write((int)c.kind); w.Write(c.fields); w.Write(c.forest); w.Write(c.water);
            }
            w.Write(Json.Serialize(new { rivers = WorldPlan.Rivers, roads = WorldPlan.Roads, rail = WorldPlan.Railways, lakes = WorldPlan.Lakes,
                sites = WorldPlan.Sites, settlements = WorldPlan.Settlements, bridges = WorldPlan.Bridges, dams = WorldPlan.Dams, farms = WorldPlan.Farms, front = WorldPlan.FrontLine }));
            w.Flush(); stream.FlushFinalBlock(); return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
        }
    }

    // Concurrent reads exercise the same queries and boundaries that far-terrain workers will use.
    static void CheckParallel()
    {
        var serial = new double[20000]; var parallel = new double[20000];
        for (int k = 0; k < serial.Length; k++) serial[k] = ReadSample(k);
        Parallel.For(0, serial.Length, k => parallel[k] = ReadSample(k));
        for (int k = 0; k < serial.Length; k++) Require(serial[k] == parallel[k], "parallel query equality");
    }
    // Deterministic query points include the outside clamp without relying on a shared random number stream.
    static double ReadSample(int k)
    {
        float x = (k * 7919 % 60000) - 30000f, z = (k * 3571 % 60000) - 30000f;
        return WorldPlan.Height(x, z) + WorldPlan.RiverDistance(x, z) * 2.0 + (int)WorldPlan.CoverAt(x, z) * 3.0 + WorldPlan.FrontAt(z).x;
    }

    // Time a million representative height and cover queries separately from generation and map rendering.
    static Dictionary<string, object> BenchmarkQueries()
    {
        double sink = 0; var watch = Stopwatch.StartNew();
        for (int k = 0; k < 1000000; k++) sink += WorldPlan.Height((k % 1751) * 32f - 28000f, (k / 1751) * 32f - 28000f);
        double height = watch.Elapsed.TotalMilliseconds; watch.Restart();
        for (int k = 0; k < 1000000; k++) sink += (int)WorldPlan.CoverAt((k % 1751) * 32f - 28000f, (k / 1751) * 32f - 28000f);
        return new Dictionary<string, object> { { "height_1m_ms", height }, { "cover_1m_ms", watch.Elapsed.TotalMilliseconds }, { "checksum", sink } };
    }

    // Draw the native 32 m height/cover plan at 2048 pixels, with hillshade and true-width vector overlays.
    static void Render(string path, int seed, Vector2 centre, float span, bool reliefOnly)
    {
        const int pixels = 2048, map = 1800, left = 124, top = 138; float metres = span / map;
        using (var bitmap = new Bitmap(pixels, pixels, PixelFormat.Format24bppRgb))
        {
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, pixels, pixels), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var bytes = new byte[data.Stride * pixels];
            Parallel.For(0, pixels, y =>
            {
                float z = centre.y + span * 0.5f - (y - top + 0.5f) * metres;
                for (int x = 0; x < pixels; x++)
                {
                    int at = y * data.Stride + x * 3;
                    if (x < left || x >= left + map || y < top || y >= top + map)
                    {
                        bytes[at] = 225; bytes[at + 1] = 242; bytes[at + 2] = 245; continue;
                    }
                    float wx = centre.x - span * 0.5f + (x - left + 0.5f) * metres, h = WorldPlan.Height(wx, z);
                    float dx = (WorldPlan.Height(wx + 32f, z) - WorldPlan.Height(wx - 32f, z)) / 64f;
                    float dz = (WorldPlan.Height(wx, z + 32f) - WorldPlan.Height(wx, z - 32f)) / 64f;
                    double shade = Math.Max(0.38, Math.Min(1.22, 0.84 + (-dx + dz) * 2.2));
                    Color c = reliefOnly ? Color.FromArgb((int)Math.Min(225, 122 + h * 0.15f), (int)Math.Min(231, 139 + h * 0.14f), (int)Math.Min(213, 116 + h * 0.16f)) : Palette[(int)WorldPlan.CoverAt(wx, z)];
                    bytes[at] = (byte)Math.Min(255, c.B * shade); bytes[at + 1] = (byte)Math.Min(255, c.G * shade); bytes[at + 2] = (byte)Math.Min(255, c.R * shade);
                }
            });
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length); bitmap.UnlockBits(data);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Func<Vector2, PointF> project = p => new PointF(left + (p.x - centre.x + span * 0.5f) / metres, top + (centre.y + span * 0.5f - p.y) / metres);
                g.SetClip(new Rectangle(left, top, map, map));
                foreach (var lake in WorldPlan.Lakes) using (var brush = new SolidBrush(Palette[4])) g.FillPolygon(brush, Project(lake.shore, project));
                foreach (var river in WorldPlan.Rivers)
                    for (int k = 1; k < river.points.Count; k++)
                        using (var pen = new Pen(Color.FromArgb(65, 134, 166), Math.Max(1.1f, (river.halfWidths[k - 1] + river.halfWidths[k]) / metres)))
                            g.DrawLine(pen, project(river.points[k - 1]), project(river.points[k]));
                if (!reliefOnly)
                {
                    foreach (var road in WorldPlan.Roads)
                    {
                        Color colour = road.kind == RoadClass.Highway ? Color.FromArgb(243, 209, 129) : road.kind == RoadClass.Road ? Color.FromArgb(234, 224, 192) : Color.FromArgb(180, 167, 132);
                        float width = road.kind == RoadClass.Highway ? 3.6f : road.kind == RoadClass.Road ? 1.9f : 0.9f;
                        using (var dark = new Pen(Color.FromArgb(135, 77, 76, 62), width + 1.1f)) g.DrawLines(dark, Project(road.points, project));
                        using (var pen = new Pen(colour, width)) g.DrawLines(pen, Project(road.points, project));
                    }
                    foreach (var rail in WorldPlan.Railways)
                    {
                        using (var pen = new Pen(Color.FromArgb(45, 48, 47), 2.4f)) g.DrawLines(pen, Project(rail.points, project));
                        using (var pen = new Pen(Color.FromArgb(225, 221, 204), 1f)) { pen.DashPattern = new float[] { 3f, 4f }; g.DrawLines(pen, Project(rail.points, project)); }
                    }
                    foreach (var b in WorldPlan.Bridges)
                    {
                        PointF p = project(b.position);
                        using (var pen = new Pen(Color.FromArgb(38, 54, 63), 3.5f)) g.DrawLine(pen, project(b.a), project(b.b));
                        using (var pen = new Pen(Color.White, 1.4f)) g.DrawRectangle(pen, p.X - 3, p.Y - 3, 6, 6);
                    }
                    using (var pen = new Pen(Color.FromArgb(185, 57, 51), 3f)) { pen.DashPattern = new float[] { 7f, 3f }; g.DrawLines(pen, Project(WorldPlan.FrontLine, project)); }
                    foreach (var s in WorldPlan.Settlements)
                    {
                        PointF p = project(s.center); if (!Visible(p)) continue;
                        using (var brush = new SolidBrush(Color.FromArgb(65, 47, 34))) g.FillEllipse(brush, p.X - 3, p.Y - 3, 6, 6);
                        Label(g, s.name, p.X + 8, p.Y - 10, s.town ? 19f : 14f, s.town ? FontStyle.Bold : FontStyle.Regular, Color.FromArgb(40, 36, 26));
                    }
                    foreach (var s in WorldPlan.Sites)
                    {
                        PointF p = project(s.position); if (!Visible(p)) continue;
                        Color c = s.kind == WorldSiteKind.Landmark ? Color.FromArgb(63, 60, 54) : s.norvane ? Color.FromArgb(25, 73, 124) : Color.FromArgb(158, 40, 32);
                        using (var brush = new SolidBrush(c)) g.FillRectangle(brush, p.X - 5, p.Y - 5, 10, 10);
                        Label(g, s.name, p.X + 10, p.Y + 3, 14f, FontStyle.Bold, c);
                    }
                    foreach (var farm in WorldPlan.Farms) { PointF p = project(farm.position); using (var brush = new SolidBrush(Color.FromArgb(109, 83, 53))) g.FillRectangle(brush, p.X - 1.5f, p.Y - 1.5f, 3f, 3f); }
                }
                foreach (var dam in WorldPlan.Dams)
                {
                    using (var pen = new Pen(Color.FromArgb(221, 212, 190), 6f)) g.DrawLine(pen, project(dam.a), project(dam.b));
                    PointF p = project(dam.a); if (Visible(p)) Label(g, dam.name, p.X + 10, p.Y - 25, 17f, FontStyle.Bold, Color.FromArgb(34, 51, 60));
                }
                g.ResetClip();
                using (var brush = new SolidBrush(Color.FromArgb(227, 245, 242, 225))) g.FillRectangle(brush, 24, 24, 710, 102);
                Label(g, "NORVANE / DRAVIN BORDERLAND", 42, 37, 25, FontStyle.Bold, Color.FromArgb(34, 47, 43));
                Label(g, "World seed " + seed + "  |  " + (span / 1000).ToString("0") + " x " + (span / 1000).ToString("0") + " km  |  " + (reliefOnly ? "RELIEF" : "MACRO PLAN") + "  |  v" + WorldPlan.GeneratorVersion, 43, 79, 17, FontStyle.Regular, Color.FromArgb(48, 62, 57));
                Legend(g, span, reliefOnly);
                Label(g, "N", 1975, 40, 26, FontStyle.Bold, Color.FromArgb(35, 48, 44));
                using (var pen = new Pen(Color.FromArgb(35, 48, 44), 3)) { g.DrawLine(pen, 1989, 82, 1989, 132); g.DrawLine(pen, 1989, 82, 1982, 94); g.DrawLine(pen, 1989, 82, 1996, 94); }
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    // Convert a feature once before drawing the complete polyline.
    static PointF[] Project(List<Vector2> line, Func<Vector2, PointF> project) { var result = new PointF[line.Count]; for (int k = 0; k < result.Length; k++) result[k] = project(line[k]); return result; }
    // Skip distant labels on the crop while allowing lines to be clipped by Graphics.
    static bool Visible(PointF p) { return p.X >= 124 && p.X <= 1924 && p.Y >= 138 && p.Y <= 1938; }
    // A pale text halo keeps labels legible over varied farmland and woodland.
    static void Label(Graphics g, string text, float x, float y, float size, FontStyle style, Color colour)
    {
        using (var font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel))
        using (var pale = new SolidBrush(Color.FromArgb(225, 235, 233, 213)))
        using (var ink = new SolidBrush(colour))
        {
            g.DrawString(text, font, pale, x - 1, y - 1); g.DrawString(text, font, pale, x + 1, y + 1);
            g.DrawString(text, font, pale, x - 1, y + 1); g.DrawString(text, font, pale, x + 1, y - 1); g.DrawString(text, font, ink, x, y);
        }
    }

    // State symbol exaggeration and scale explicitly so overview line widths are not mistaken for ground widths.
    static void Legend(Graphics g, float span, bool reliefOnly)
    {
        if (!reliefOnly)
            for (int k = 0; k < 8; k++)
            {
                float x = 44 + k * 185;
                using (var brush = new SolidBrush(Palette[k])) g.FillRectangle(brush, x, 1958, 28, 22);
                Label(g, ((Cover)k).ToString(), x + 37, 1957, 16, FontStyle.Regular, Color.FromArgb(42, 50, 41));
            }
        Label(g, reliefOnly ? "Heightfield at 32 m spacing. Hillshade exaggeration 2.2x. Water surfaces use exported levels." : "Gold: highway   Cream: road   Tan: track   Dashed black: rail   White square: bridge   Red dashed: front", 44, 1988, 16, FontStyle.Regular, Color.FromArgb(42, 50, 41));
        Label(g, "River width minimum 1.1 px; transport and point symbols enlarged. Macro districts; local field parcels are generated separately.", 44, 2015, 14, FontStyle.Regular, Color.FromArgb(64, 70, 58));
        float km = span > 10000 ? 5f : 1f, width = km * 1000f / span * 1800;
        using (var pen = new Pen(Color.FromArgb(38, 48, 42), 4)) g.DrawLine(pen, 1750, 1970, 1750 + width, 1970);
        Label(g, km + " km", 1750, 1984, 19, FontStyle.Bold, Color.FromArgb(38, 48, 42));
    }
}

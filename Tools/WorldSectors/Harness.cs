using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Independent crossing measurements and comparisons execute the real adapter and the production consumers.
static partial class Harness
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
    static int checks;
    static readonly List<object> kindChecks = new List<object>();

    // Seed 1200 matches World.WorldSeed. Named spots exercise contrasting geography and multi-reach cropping.
    static int Main(string[] args)
    {
        try
        {
            string output = args[0];
            Directory.CreateDirectory(output);
            if (args.Length > 1 && args[1] == "--legacy") { LegacyConsumers(output); return 0; }
            var clock = Stopwatch.StartNew(); WorldPlan.Generate(1200);
            Console.WriteLine("World generated in " + clock.Elapsed.TotalSeconds.ToString("F2") + " s");
            var spots = Spots();
            var summaries = new List<object>();
            int seed = 7100;
            foreach (var spot in spots)
            {
                clock.Restart();
                var plan = WorldSectors.PlanAt(spot.center, spot.kind, seed++);
                double ms = clock.Elapsed.TotalMilliseconds;
                Console.WriteLine(spot.name + " PlanAt: " + ms.ToString("F2") + " ms");
                var copy = WorldSectors.PlanAt(spot.center, spot.kind, plan.seed);
                Require(JsonSerializer.Serialize(Plan(plan), Json) == JsonSerializer.Serialize(Plan(copy), Json), "Plan determinism");
                Inspect(plan);
                MapLayout.Use(plan); clock.Restart(); MapLayout.Prepare();
                Console.WriteLine("  Prepare: " + clock.Elapsed.TotalMilliseconds.ToString("F0") + " ms");
                var consumer = Consumers(plan);
                var worldRoads = new List<IList<Vector2>>(); foreach (var road in WorldPlan.Roads) worldRoads.Add(Local(road.points, spot.center));
                var worldRivers = new List<IList<Vector2>>(); foreach (var river in WorldPlan.Rivers) worldRivers.Add(Local(river.points, spot.center));
                var sectorRivers = new List<IList<Vector2>>(); foreach (var river in plan.world.rivers) sectorRivers.Add(river.points);
                var sectorRoads = new List<IList<Vector2>>(); foreach (var road in plan.roads) sectorRoads.Add(road);
                var preparedRoads = new List<IList<Vector2>>(); foreach (var road in MapLayout.RoadLines) preparedRoads.Add(road);
                var legacyRiver = new List<IList<Vector2>> { MapLayout.RiverLine };
                var roadSeam = Seam(worldRoads, sectorRoads, true);
                var preparedSeam = Seam(worldRoads, preparedRoads, true);
                var riverSeam = Seam(worldRivers, sectorRivers, true);
                var legacySeam = Seam(worldRivers, legacyRiver, false);
                var baseRoute = RoadRoute.Find(Flat(plan.basePosition), Flat(plan.enemyHqPosition));
                Require(baseRoute.Count > 2, "Base-HQ graph route, not RoadRoute's straight fallback");
                int missions = 0, straightFallbacks = 0, unitsOverWater = 0;
                foreach (Situation situation in Enum.GetValues(typeof(Situation)))
                {
                    if (!Situations.Fits(situation, plan.Type)) continue;
                    var mission = Situations.Generate(plan, situation, plan.seed, Daylight.Day);
                    Require(mission.units.Count > 0, "Mission has units");
                    if (mission.convoyRoute != null) { Require(mission.convoyRoute.Count > 0, "Convoy route nonempty"); if (mission.convoyRoute.Count <= 2) straightFallbacks++; }
                    foreach (var unit in mission.units)
                    {
                        if (unit.convoy > 0) continue;
                        Vector2 q = Flat(unit.position);
                        if (plan.world.LakeDistance(q, out _) < 0f || plan.world.RiverDistance(q, out float width, out _) < width) unitsOverWater++;
                    }
                    missions++;
                }
                Require(unitsOverWater == 0, "No stationary units over exact world water after query integration: " + unitsOverWater);
                var summary = new { spot.name, center = V(spot.center), kind = plan.kind.ToString(), milliseconds = ms,
                    roads = roadSeam, preparedRoads = preparedSeam, rivers = riverSeam, legacyRiver = legacySeam,
                    missions, straightFallbacks, unitsOverWater, forest = plan.forest, fields = plan.fields,
                    minHeight = plan.world.minimumHeight, maxHeight = plan.world.maximumHeight };
                summaries.Add(summary);
                File.WriteAllText(Path.Combine(output, spot.name + ".json"), JsonSerializer.Serialize(new {
                    summary, consumer, world = WorldView(spot.center), sector = Plan(plan),
                    preparedRoads = Lines(preparedRoads), preparedRiver = Line(MapLayout.RiverLine)
                }, Json));
            }
            FlowChecks(spots[0].center, spots[1].center);
            Hotspots();
            AllKinds();
            var dam = WorldPlan.Dams[0]; var reservoir = WorldPlan.Lakes.Find(l => l.reservoir);
            var varrow = SectorPlan.Varrow(); var vr = MapLayout.Smooth(varrow.river, 2f);
            Vector2 down = (vr[4] - vr[0]).normalized, across = new Vector2(-down.y, down.x);
            Vector2 localArm = down * varrow.lakeLobes[1].center.x + across * varrow.lakeLobes[1].center.y;
            Vector2 worldArm = (reservoir.shore[0] + reservoir.shore[reservoir.shore.Count - 1] - dam.a - dam.b) / 2f;
            float rotation = (Mathf.Atan2(worldArm.y, worldArm.x) - Mathf.Atan2(localArm.y, localArm.x)) * Mathf.Rad2Deg;
            Console.WriteLine("Varrow arm rotation (degrees CCW in X/Z): " + rotation.ToString("F2"));
            var orientation = new { damCenter = V((dam.a + dam.b) / 2f), varrowDown = V(down), varrowArm = V(localArm), worldArm = V(worldArm), rotationDegrees = rotation };
            var snapshot = WorldSectors.PlanAt(spots[0].center, spots[0].kind, 7100);
            string beforeRegeneration = JsonSerializer.Serialize(Plan(snapshot), Json);
            WorldPlan.Generate(1201);
            Require(beforeRegeneration == JsonSerializer.Serialize(Plan(snapshot), Json), "Owned snapshot survives world regeneration");
            File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(new { seed = 1200, checks, spots = summaries, kinds = kindChecks, orientation }, Json));
            Console.WriteLine("PASS " + checks + " checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    // Select features from the actual world instead of baking locations from a different seed.
    static List<(string name, Vector2 center, SectorKind kind)> Spots()
    {
        WorldBridge crossing = null; float closest = float.MaxValue;
        foreach (var b in WorldPlan.Bridges)
        {
            if (b.railway || !WorldSectors.Inside(b.position, 24000f)) continue;
            float d = Mathf.Abs(b.position.x - WorldPlan.FrontAt(b.position.y).x);
            if (d < closest) { closest = d; crossing = b; }
        }
        WorldSettlement town = WorldPlan.Settlements.Find(s => s.town);
        var dam = WorldPlan.Dams[0]; Vector2 damCenter = (dam.a + dam.b) / 2f;
        Vector2 forest = default, fields = default; float bestForest = float.MinValue, bestFields = float.MinValue;
        for (int z = 4; z < 52; z++) for (int x = 4; x < 52; x++)
        {
            var cell = WorldPlan.Cell(x, z); Vector2 c = new Vector2(-27500f + x * 1000f, -27500f + z * 1000f);
            float distance = Mathf.Abs(c.x - WorldPlan.FrontAt(c.y).x) / 100000f;
            if (cell.forest - distance > bestForest && WorldPlan.Height(c.x, c.y) < 200f) { bestForest = cell.forest - distance; forest = c; }
            if (cell.fields - distance > bestFields) { bestFields = cell.fields - distance; fields = c; }
        }
        var tributary = WorldPlan.Rivers[1];
        return new List<(string, Vector2, SectorKind)> {
            ("river-crossing", Snap(crossing.position), SectorKind.RiverCrossing),
            ("town-edge", Snap(town.center + new Vector2(town.radius * 0.85f, 0f)), SectorKind.TownOutskirts),
            ("forest", Snap(forest), SectorKind.DeepForest),
            ("reservoir", Snap(damCenter + (dam.a - dam.b).normalized * 550f), SectorKind.Reservoir),
            ("farmland", Snap(fields), SectorKind.RollingFarmland),
            ("confluence", Snap(tributary.points[tributary.points.Count - 1]), SectorKind.RiverValley),
        };
    }

    // Check exact snapshot queries, mission-critical placement and shape/list invariants before preparation.
    static void Inspect(SectorPlan p)
    {
        Require(p.villages.Count == p.villageNames.Count && p.villages.Count == p.farmsteads.Count, "Aligned village lists");
        Require(p.roads.Count > 0 && p.defences.Count >= 4, "Mission skeleton");
        foreach (var line in p.roads) Require(line.Length >= 2, "Usable road line");
        Require(p.world.DryDistance(Flat(p.basePosition)) > 30f, "Dry base");
        Require(p.world.DryDistance(Flat(p.enemyHqPosition)) > 30f, "Dry HQ");
        foreach (var d in p.defences) Require(p.world.DryDistance(Flat(d.position)) > 8f, "Dry defence");
        foreach (var s in p.sites) Require(p.world.DryDistance(s.position) > Mathf.Max(5f, s.flat), "Dry site");
        var rng = new Random(421);
        for (int i = 0; i < 500; i++)
        {
            float x = (float)rng.NextDouble() * 2000f - 1000f, z = (float)rng.NextDouble() * 2000f - 1000f;
            Require(Mathf.Abs(p.world.Height(x, z) - WorldPlan.Height(x + p.world.center.x, z + p.world.center.y)) < 0.02f, "World height snapshot");
            Require(p.world.CoverAt(x, z) == WorldPlan.CoverAt(x + p.world.center.x, z + p.world.center.y), "World cover snapshot");
            Require(p.world.DetailWeight(1000f, z) == 0f, "Zero edge detail");
        }
        foreach (var r in p.world.rivers)
        {
            Require(r.points.Length == r.halfWidths.Length && r.points.Length == r.waterLevels.Length, "Aligned river attributes");
            for (int i = 1; i < r.points.Length; i++) Require(r.waterLevels[i] <= r.waterLevels[i - 1] + 0.001f, "Downstream levels");
        }
    }

    // Compare independently intersected edge events in both directions, counting omissions rather than hiding them.
    static object Seam(List<IList<Vector2>> world, List<IList<Vector2>> sector, bool exact)
    {
        var a = Crossings(world); var b = Crossings(sector);
        float error = 0f;
        foreach (var p in a) error = Mathf.Max(error, Nearest(p, b));
        foreach (var p in b) error = Mathf.Max(error, Nearest(p, a));
        if (exact && (a.Count != b.Count || error >= 0.05f)) Console.WriteLine(JsonSerializer.Serialize(new { expected = Line(a), actual = Line(b), error }));
        if (exact) { Require(a.Count == b.Count, "Seam crossing count " + a.Count + "/" + b.Count); Require(error < 0.05f, "Seam error " + error); }
        return new { worldCount = a.Count, sectorCount = b.Count, maxMetres = error, world = Line(a), sector = Line(b) };
    }

    // Direct segment-line intersections are independent of the adapter's Liang-Barsky clipping implementation.
    static List<Vector2> Crossings(List<IList<Vector2>> lines)
    {
        var result = new List<Vector2>();
        foreach (var line in lines) for (int i = 1; i < line.Count; i++)
        {
            Vector2 a = line[i - 1], b = line[i];
            for (int axis = 0; axis < 2; axis++) foreach (float edge in new[] { -1000f, 1000f })
            {
                float av = axis == 0 ? a.x : a.y, bv = axis == 0 ? b.x : b.y;
                if (Mathf.Abs(bv - av) < 0.00001f) continue;
                float t = (edge - av) / (bv - av);
                if (t < -0.000001f || t > 1.000001f) continue;
                Vector2 q = Vector2.Lerp(a, b, t);
                if (Mathf.Abs(axis == 0 ? q.y : q.x) > 1000.002f) continue;
                if (Nearest(q, result) > 0.02f) result.Add(q);
            }
        }
        return result;
    }

    // Empty sets produce an explicit missing-crossing distance instead of a deceptive zero.
    static float Nearest(Vector2 p, List<Vector2> points)
    {
        float best = 4000f; foreach (var q in points) best = Mathf.Min(best, Vector2.Distance(p, q)); return best;
    }

    // Reversal, victory advance and repeated calls must not change assignment or make two squares overlap.
    static void Hotspots()
    {
        for (int i = 0; i < 12; i++)
        {
            float x = (i + 0.5f) / 12f;
            Theatre.Hotspots.Add(new Hotspot { seed = i + 331, kind = (SectorKind)(i * 2 + 1), mapPosition = new Vector2(x, Theatre.Front(x) + i * 0.002f) });
        }
        var positions = new Dictionary<Hotspot, Vector2>();
        foreach (var h in Theatre.Hotspots)
        {
            Vector2 p = WorldSectors.SpotFor(h);
            Require(p.x % 250f == 0f && p.y % 250f == 0f, "Hotspot snap");
            foreach (var previous in positions.Values) Require(Mathf.Abs(p.x - previous.x) >= 2000f || Mathf.Abs(p.y - previous.y) >= 2000f, "Hotspot squares do not overlap");
            positions[h] = p;
        }
        Theatre.Hotspots.Reverse(); Theatre.Advance = 0.06f;
        foreach (var h in Theatre.Hotspots) Require(Vector2.Distance(positions[h], WorldSectors.SpotFor(h)) < 0.01f, "Order/advance-independent allocation");
    }

    // Every campaign kind gets a cell and a usable mission skeleton, including fallback kinds absent in WorldCell.
    static void AllKinds()
    {
        Theatre.Advance = 0f;
        for (int group = 0; group < 3; group++)
        {
            Theatre.Hotspots.Clear();
            for (int i = 0; i < 10; i++)
            {
                int kind = 1 + group * 10 + i;
                if (kind >= SectorTypes.All.Length) break;
                float x = (i + 0.5f) / 10f;
                Theatre.Hotspots.Add(new Hotspot { seed = kind * 713 + 31, kind = (SectorKind)kind, mapPosition = new Vector2(x, Theatre.Front(x)) });
            }
            foreach (var spot in Theatre.Hotspots)
            {
                Vector2 center = WorldSectors.SpotFor(spot);
                var cell = WorldPlan.Cell((int)((center.x + WorldPlan.Half) / WorldPlan.CellSize), (int)((center.y + WorldPlan.Half) / WorldPlan.CellSize));
                var clock = Stopwatch.StartNew();
                var plan = WorldSectors.PlanAt(center, spot.kind, spot.seed);
                double ms = clock.Elapsed.TotalMilliseconds;
                Console.WriteLine("  Kind " + spot.kind + " -> " + cell.kind + " at " + center.x + ", " + center.y + ": " + ms.ToString("F1") + " ms");
                Inspect(plan);
                kindChecks.Add(new { hint = spot.kind.ToString(), cell = cell.kind.ToString(), center = V(center), milliseconds = ms });
            }
        }
    }

    // Export a renderer-friendly snapshot of the actual SectorPlan, including its exact optional world inputs.
    static object Plan(SectorPlan p)
    {
        var roads = new List<IList<Vector2>>(); foreach (var r in p.roads) roads.Add(r);
        var rivers = new List<object>(); foreach (var r in p.world.rivers) rivers.Add(new { r.name, r.source, points = Line(r.points), r.halfWidths, r.waterLevels });
        var lakes = new List<object>(); foreach (var l in p.world.lakes) lakes.Add(new { l.name, l.level, l.reservoir, shore = Line(l.shore) });
        var dams = new List<object>(); foreach (var d in p.world.dams) dams.Add(new { d.name, a = V(d.a), b = V(d.b), d.crest });
        var villages = new List<object>(); for (int i = 0; i < p.villages.Count; i++) villages.Add(new { point = V(p.villages[i]), name = p.villageNames[i], farm = p.farmsteads[i] });
        var woods = new List<float[]>(); foreach (var w in p.world.woods) woods.Add(new[] { w.x, w.y, w.width, w.height });
        var defences = new List<float[]>(); foreach (var d in p.defences) defences.Add(V(Flat(d.position)));
        var sites = new List<object>(); foreach (var s in p.sites) sites.Add(new { s.model, position = V(s.position), s.yaw, s.scale, s.flat });
        return new { p.name, p.seed, kind = p.kind.ToString(), p.forest, p.fields, p.hills, p.ridges, p.worldSeed, p.fieldSeed,
            basePosition = V(Flat(p.basePosition)), hq = V(Flat(p.enemyHqPosition)), roads = Lines(roads), rivers, lakes, dams, villages, woods, defences, sites,
            bridges = Line(p.bridges), cover = CoverGrid(p.world.center, p.world), heights = HeightGrid(p.world.center, p.world) };
    }

    // The comparison's left panel reads WorldPlan directly, independently of the adapter's snapshot.
    static object WorldView(Vector2 center)
    {
        var roads = new List<float[][]>(); foreach (var r in WorldPlan.Roads) if (Near(r.points, center)) roads.Add(Line(Local(r.points, center)));
        var rivers = new List<object>(); foreach (var r in WorldPlan.Rivers) if (Near(r.points, center)) rivers.Add(new { points = Line(Local(r.points, center)), r.halfWidths });
        var lakes = new List<object>(); foreach (var l in WorldPlan.Lakes) lakes.Add(new { l.name, l.level, shore = Line(Local(l.shore, center)) });
        var dams = new List<object>(); foreach (var d in WorldPlan.Dams) dams.Add(new { d.name, a = V(d.a - center), b = V(d.b - center), d.crest });
        var villages = new List<object>(); foreach (var s in WorldPlan.Settlements) if (WorldSectors.Inside(s.center - center, 2000f)) villages.Add(new { point = V(s.center - center), name = s.name, farm = false, s.radius });
        foreach (var f in WorldPlan.Farms) if (WorldSectors.Inside(f.position - center, 1000f)) villages.Add(new { point = V(f.position - center), name = "Farm", farm = true, radius = 40f });
        return new { roads, rivers, lakes, dams, villages, cover = CoverGrid(center, null), heights = HeightGrid(center, null) };
    }

    // A coarse relief grid makes height continuity visible without claiming to render Unity terrain.
    static float[] HeightGrid(Vector2 center, WorldSectorData data)
    {
        var values = new float[101 * 101];
        for (int z = 0; z <= 100; z++) for (int x = 0; x <= 100; x++)
            values[z * 101 + x] = data == null ? WorldPlan.Height(center.x - 1000 + x * 20, center.y - 1000 + z * 20) : data.Height(-1000 + x * 20, -1000 + z * 20);
        return values;
    }

    // Matched pixel-centre samples show the cover crop, including forests continuing across the boundary.
    static int[] CoverGrid(Vector2 center, WorldSectorData data)
    {
        var values = new int[200 * 200];
        for (int z = 0; z < 200; z++) for (int x = 0; x < 200; x++)
            values[z * 200 + x] = (int)(data == null ? WorldPlan.CoverAt(center.x - 995 + x * 10, center.y - 995 + z * 10) : data.CoverAt(-995 + x * 10, -995 + z * 10));
        return values;
    }

    // Keep only nearby lines in the JSON; edge measurement still used the complete world vectors above.
    static bool Near(IList<Vector2> line, Vector2 center)
    { foreach (var p in line) if (WorldSectors.Inside(p - center, 2000f)) return true; return false; }
    // Translate without clipping for an independent reference.
    static List<Vector2> Local(IList<Vector2> line, Vector2 center) { var r = new List<Vector2>(); foreach (var p in line) r.Add(p - center); return r; }
    // JSON geometry contains no recursive normalized-vector properties.
    static float[] V(Vector2 p) => new[] { p.x, p.y };
    // Convert a line in its given order for plotting or signatures.
    static float[][] Line(IList<Vector2> points) { var r = new float[points.Count][]; for (int i = 0; i < r.Length; i++) r[i] = V(points[i]); return r; }
    // Convert disconnected lines without introducing a joining segment.
    static List<float[][]> Lines(List<IList<Vector2>> lines) { var r = new List<float[][]>(); foreach (var line in lines) r.Add(Line(line)); return r; }
    // Test positions use the same public snapping contract as campaign locations.
    static Vector2 Snap(Vector2 p) => new Vector2(Mathf.RoundToInt(p.x / 250f) * 250f, Mathf.RoundToInt(p.y / 250f) * 250f);
    // Horizontal view of a mission point.
    static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);
    // Fail fast with the invariant name, while retaining the number of assertions completed.
    static void Require(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
}

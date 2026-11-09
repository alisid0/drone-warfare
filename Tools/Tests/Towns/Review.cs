using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Top-down review pictures and measurements of the towns outside the sector: Brenmoor (the continuous-flight test's town),
// a second town and Ashcombe (a village, for the ground round it), each as the far terrain shows it at 300 m (the 2 m
// tiles, bilinear) and 2.5 km (the 4 m regional pages), with the settlement scatter's buildings (SettlementPattern, the
// plan SettlementHlod and FarScatter draw) as shaded roofs. One process per run; nothing starts Unity.
static class TownReview
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
        string output = args.Length > 0 ? args[0] : "Temp/towns/review";
        bool render = args.Length > 1 && args[1] == "render";
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        WorldPlan.Generate(1200);
        double generate = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var data = WorldPlan.CaptureDetail(null);
        double capture = timer.Elapsed.TotalMilliseconds;
        // The village plan alone, rebuilt from the same roads (the capture above includes it once).
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
        Console.WriteLine($"world {generate:F0} ms, detail capture {capture:F0} ms, village plan {plan:F0} ms (best of 3)");
        var places = Places();
        var report = new Dictionary<string, object> { ["worldMs"] = generate, ["captureMs"] = capture, ["villagePlanMs"] = plan };
        foreach (var place in places) Console.WriteLine($"{place.name}: ({place.center.x:F0}, {place.center.y:F0}) {place.label} radius {place.radius:F0} m");
        report["settlements"] = Settlements(data);
        report["towns"] = Towns(data);
        report["timing"] = Timing(data, places);
        File.WriteAllText(Path.Combine(output, "measure.json"), JsonSerializer.Serialize(report, Json));
        if (render)
            foreach (var place in places)
            {
                var t = Stopwatch.StartNew();
                Picture(data, place, 300f, Path.Combine(output, place.name + "-300m.ppm"));
                Picture(data, place, 2500f, Path.Combine(output, place.name + "-2500m.ppm"));
                Console.WriteLine($"{place.name} pictures {t.Elapsed.TotalSeconds:F1} s");
            }
        Console.WriteLine($"peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MiB");
        return 0;
    }

    // One reviewed place and its label.
    sealed class Place { public string name, label; public Vector2 center, focus; public float radius; }

    // Brenmoor (hd165-168's town), Halvard (the northern town) and Ashcombe (hd166's village). The 300 m view looks at
    // the town's middle: the point of its main road nearest the planned centre, where the market square stands.
    static List<Place> Places()
    {
        var list = new List<Place>();
        foreach (var s in WorldPlan.Settlements)
        {
            string name = s.name == "Brenmoor" ? "brenmoor" : s.name == "Halvard" ? "halvard" : s.name == "Ashcombe" ? "ashcombe" : null;
            if (name == null) continue;
            list.Add(new Place { name = name, label = s.name + (s.town ? " (town)" : " (village)"), center = s.center, focus = Anchor(s), radius = s.radius });
        }
        list.Sort((a, b) => string.CompareOrdinal(b.name, a.name));
        return list;
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

    // Per settlement: plots, buildings by kind, farms, churches and squares.
    static object Settlements(FarDetailData data)
    {
        var list = new List<object>();
        for (int owner = 0; owner < WorldPlan.Settlements.Count; owner++)
        {
            var s = WorldPlan.Settlements[owner];
            int plots = 0, farms = 0, churches = 0, squares = 0, built = 0;
            var kinds = new Dictionary<int, int>();
            foreach (var p in data.villages.plots)
            {
                if (p.owner != owner) continue;
                plots++; if (p.farm) farms++; if (p.church) churches++; if (p.square) squares++;
            }
            foreach (var block in SettlementPattern.Build(s))
                foreach (var h in block.houses) { built++; kinds[h.kind] = kinds.TryGetValue(h.kind, out int n) ? n + 1 : 1; }
            list.Add(new { s.name, s.town, s.radius, plots, farms, churches, squares, built, kinds = string.Join(", ", Kinds(kinds)) });
            if (s.town) Console.WriteLine($"{s.name}: radius {s.radius:F0} m, {plots} plots, {built} buildings ({string.Join(", ", Kinds(kinds))})");
        }
        return list;
    }

    // Kind histogram for the log.
    static IEnumerable<string> Kinds(Dictionary<int, int> kinds)
    {
        var keys = new List<int>(kinds.Keys); keys.Sort();
        foreach (int k in keys) yield return k + ":" + kinds[k];
    }

    // The town plan against ChatGPT's settlement spec: street blocks between junctions (80-180 m), house frontage
    // (15-35 m, attached and clustered), the share of buildings on road-facing plots (70-85%) and of irregular or
    // empty parcels (10-20%), plus the HLOD's triangles (beyond 1.5 km) for each town.
    static object Towns(FarDetailData data)
    {
        var list = new List<object>();
        for (int owner = 0; owner < WorldPlan.Settlements.Count; owner++)
        {
            var s = WorldPlan.Settlements[owner];
            if (!s.town) continue;
            int houses = 0, roadFacing = 0, parcels = 0, irregular = 0, empty = 0, attached = 0;
            var frontages = new List<float>();
            foreach (var p in data.villages.plots)
            {
                if (p.owner != owner || p.church) continue;
                if (p.landUse >= 6 || p.landUse >= 1 && p.landUse <= 3) continue; // fringe land and edge community uses
                parcels++;
                if (p.shape != 0) irregular++;
                if (p.model == null) { empty++; continue; }
                houses++;
                frontages.Add(p.frontage);
#if !BEFORE
                if (!p.estate && !p.backland) roadFacing++;
                if (p.attached) attached++;
#else
                roadFacing++;
#endif
            }
            frontages.Sort();
            float Q(List<float> v, float q) => v.Count == 0 ? 0f : v[Math.Min(v.Count - 1, (int)(q * v.Count))];
            // Street blocks: the run of each town lane between consecutive junctions with another street.
            var blocks = new List<float>();
            var lanes = new List<FarDetailData.Segment>();
            foreach (var l in data.villages.lanes) if (l.route >= 10000 + owner * 128 && l.route < 10000 + (owner + 1) * 128 && l.kind != (int)RoadClass.Track) lanes.Add(l);
#if !BEFORE
            lanes.Clear();
            foreach (var l in data.villages.lanes) if (l.kind != (int)RoadClass.Track && VillagePlan.TownOwner(l.route) == owner) lanes.Add(l);
#endif
            Blocks(lanes, s, blocks);
            blocks.Sort();
            int triangles = 0;
            foreach (var block in SettlementPattern.Build(s))
                foreach (var h in block.houses) triangles += SettlementPattern.Roof[Math.Min(h.kind, SettlementPattern.Roof.Length - 1)] > 0f ? 14 : 12;
            var result = new { s.name, houses, roadFacingShare = roadFacing / (float)Math.Max(1, houses), parcels,
                irregularOrEmptyShare = (irregular + empty) / (float)Math.Max(1, parcels), emptyShare = empty / (float)Math.Max(1, parcels),
                attachedShare = attached / (float)Math.Max(1, houses),
                frontage = new[] { Q(frontages, .1f), Q(frontages, .5f), Q(frontages, .9f) },
                streets = lanes.Count, blockCount = blocks.Count, block = new[] { Q(blocks, .1f), Q(blocks, .5f), Q(blocks, .9f) },
                blockShareIn80to180 = Share(blocks, 80f, 180f), hlodTriangles = triangles };
            Console.WriteLine($"{s.name}: {houses} buildings, {result.roadFacingShare:P0} road-facing, {result.attachedShare:P0} attached; parcels {parcels}, irregular or empty {result.irregularOrEmptyShare:P0} (empty {result.emptyShare:P0}); frontage p10/50/90 {result.frontage[0]:F0}/{result.frontage[1]:F0}/{result.frontage[2]:F0} m; {lanes.Count} street segments, {blocks.Count} blocks p10/50/90 {result.block[0]:F0}/{result.block[1]:F0}/{result.block[2]:F0} m ({result.blockShareIn80to180:P0} in 80-180 m); HLOD {triangles} triangles");
            list.Add(result);
        }
        return list;
    }

    // The share of values within a range.
    static float Share(List<float> values, float lo, float hi)
    {
        int n = 0;
        foreach (float v in values) if (v >= lo && v <= hi) n++;
        return n / (float)Math.Max(1, values.Count);
    }

    // Block faces: walk each street (lane route, joined end to end) in 2 m steps and, on each side separately, cut it where
    // another street or through-road leaves that side (a street within 4 m of the point 9 m out); every run between two
    // cuts of at least 20 m is one block face.
    static void Blocks(List<FarDetailData.Segment> lanes, WorldSettlement s, List<float> blocks)
    {
        var streets = new List<FarDetailData.Segment>(lanes);
        foreach (var road in WorldPlan.Roads)
            for (int k = 1; k < road.points.Count; k++)
                if ((road.points[k] - s.center).magnitude < s.radius * 1.6f)
                    streets.Add(new FarDetailData.Segment { ax = road.points[k - 1].x, az = road.points[k - 1].y, bx = road.points[k].x, bz = road.points[k].y, route = -1 - k });
        var routes = new SortedDictionary<int, List<FarDetailData.Segment>>();
        foreach (var l in lanes) { if (!routes.TryGetValue(l.route, out var r)) routes.Add(l.route, r = new List<FarDetailData.Segment>()); r.Add(l); }
        foreach (var line in routes.Values)
            foreach (int side in new[] { -1, 1 })
            {
                float walked = 0f, last = 0f; bool any = false, inside = false;
                foreach (var seg in line)
                {
                    Vector2 a = new Vector2(seg.ax, seg.az), b = new Vector2(seg.bx, seg.bz);
                    float length = (b - a).magnitude;
                    if (length < .01f) continue;
                    Vector2 dir = (b - a) / length, normal = new Vector2(-dir.y, dir.x) * side;
                    for (float t = 0f; t <= length; t += 2f)
                    {
                        Vector2 p = a + dir * t + normal * 9f;
                        bool junction = false;
                        foreach (var other in streets)
                        {
                            if (other.route == seg.route) continue;
                            Vector2 oa = new Vector2(other.ax, other.az), ob = new Vector2(other.bx, other.bz), d = ob - oa;
                            float u = d.sqrMagnitude > .01f ? Math.Max(0f, Math.Min(1f, Vector2.Dot(p - oa, d) / d.sqrMagnitude)) : 0f;
                            if ((oa + d * u - p).sqrMagnitude < 16f) { junction = true; break; }
                        }
                        float at = walked + t;
                        if (junction && !inside)
                        {
                            if (any && at - last >= 20f) blocks.Add(at - last);
                            any = true;
                        }
                        if (junction) last = at;
                        inside = junction;
                    }
                    walked += length;
                }
            }
    }

    // Construction and painting costs: the 2 m tile under each place and a coarse whole-world picture.
    static object Timing(FarDetailData data, List<Place> places)
    {
        var tiles = new Dictionary<string, double>();
        var roughness = new float[FarDetailData.Samples * FarDetailData.Samples];
        var coarse = new float[14 * 14][];
        for (int i = 0; i < coarse.Length; i++) coarse[i] = new float[65 * 65];
        var tileData = WorldPlan.CaptureDetail(coarse);
        foreach (var place in places)
        {
            int x = (int)Math.Floor(place.focus.x / FarDetailData.Size), z = (int)Math.Floor(place.focus.y / FarDetailData.Size);
            tileData.Build(x, z, roughness, null);
            double best = double.MaxValue;
            for (int i = 0; i < 3; i++) { var t = Stopwatch.StartNew(); tileData.Build(x, z, roughness, null); best = Math.Min(best, t.Elapsed.TotalMilliseconds); }
            tiles[place.name] = best;
        }
        var pattern = new Dictionary<string, double>();
        foreach (var s in WorldPlan.Settlements)
        {
            if (!s.town) continue;
            var t = Stopwatch.StartNew(); int n = 0;
            foreach (var block in SettlementPattern.Build(s)) n += block.houses.Count;
            pattern[s.name] = t.Elapsed.TotalMilliseconds;
        }
        var timer = Stopwatch.StartNew();
        data.WorldPixels(512);
        double world = timer.Elapsed.TotalMilliseconds;
        Console.Write("timing: 2 m tile");
        foreach (var pair in tiles) Console.Write($" {pair.Key} {pair.Value:F0} ms;");
        Console.Write(" settlement pattern");
        foreach (var pair in pattern) Console.Write($" {pair.Key} {pair.Value:F1} ms;");
        Console.WriteLine($" 512-square world picture {world:F0} ms");
        return new { tiles, pattern, worldPicture512Ms = world };
    }

    // A 1200x800 top-down picture: at 300 m the ground is the 2 m tile lattice filtered bilinearly, at 2.5 km the 4 m
    // regional texel; woods are the canopy's coverage, and buildings are shaded roofs with a shadow.
    static void Picture(FarDetailData data, Place place, float distance, string path)
    {
        const int w = 1200, h = 800;
        float pixel = distance < 1000f ? .5f : 4f, texel = distance < 1000f ? 2f : 4f;
        Vector2 center = distance < 1000f ? place.focus : place.center;
        float x0 = center.x - w * pixel * .5f, z1 = center.y + h * pixel * .5f;
        int gx0 = (int)Math.Floor(x0 / texel) - 1, gz0 = (int)Math.Floor((z1 - h * pixel) / texel) - 1;
        int gw = (int)Math.Ceiling(w * pixel / texel) + 3, gh = (int)Math.Ceiling(h * pixel / texel) + 3;
        var lattice = new Vector3[gw * gh];
        System.Threading.Tasks.Parallel.For(0, gh, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 2 }, j =>
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
                lattice[j * gw + i] = c * Hill(data, x, z, texel);
            }
        });
        var houses = new List<SettlementPattern.House>();
        foreach (var s in WorldPlan.Settlements)
        {
            if ((s.center - center).magnitude > s.radius + w * pixel + 900f) continue;
            foreach (var block in SettlementPattern.Build(s)) houses.AddRange(block.houses);
        }
        var boxes = new List<LandmarkBuilding>();
        foreach (var f in WorldPlan.Features) if (f.valid && (f.center - center).magnitude < w * pixel + 800f) boxes.AddRange(f.buildings);
        // Bin the buildings on a 32 m grid, so each pixel looks only at its neighbours.
        var bins = new Dictionary<long, List<int>>();
        for (int i = 0; i < houses.Count; i++)
        {
            int bx = (int)Math.Floor(houses[i].world.x / 32f), bz = (int)Math.Floor(houses[i].world.y / 32f);
            long key = ((long)bx << 32) ^ (uint)bz;
            if (!bins.TryGetValue(key, out var list)) bins.Add(key, list = new List<int>());
            list.Add(i);
        }
        var pixels = new byte[w * h * 3];
        System.Threading.Tasks.Parallel.For(0, h, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 2 }, j =>
        {
            var near = new List<SettlementPattern.House>();
            for (int i = 0; i < w; i++)
            {
                float x = x0 + (i + .5f) * pixel, z = z1 - (j + .5f) * pixel;
                float fx = x / texel - .5f - gx0, fz = z / texel - .5f - gz0;
                int ix = (int)Math.Floor(fx), iz = (int)Math.Floor(fz);
                float u = fx - ix, v = fz - iz;
                Vector3 c = Vector3.Lerp(Vector3.Lerp(lattice[iz * gw + ix], lattice[iz * gw + ix + 1], u),
                    Vector3.Lerp(lattice[(iz + 1) * gw + ix], lattice[(iz + 1) * gw + ix + 1], u), v);
                near.Clear();
                int cx = (int)Math.Floor(x / 32f), cz = (int)Math.Floor(z / 32f);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (bins.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var list))
                            foreach (int k in list) near.Add(houses[k]);
                c = Roofs(c, near, boxes, x, z, pixel);
                // TOWN_RAIL=1 draws the railways (dark red) and world roads (blue shades) over the picture, for checking.
                if (Environment.GetEnvironmentVariable("TOWN_RAIL") == "1")
                    foreach (var rail in WorldPlan.Railways)
                        for (int k = 1; k < rail.points.Count; k++)
                        {
                            Vector2 ra = rail.points[k - 1], rd = rail.points[k] - ra, q = new Vector2(x, z);
                            float rt = rd.sqrMagnitude > .01f ? Math.Max(0f, Math.Min(1f, Vector2.Dot(q - ra, rd) / rd.sqrMagnitude)) : 0f;
                            if ((ra + rd * rt - q).magnitude < Math.Max(1.5f, pixel)) c = new Vector3(.4f, 0f, 0f);
                        }
                if (Environment.GetEnvironmentVariable("TOWN_RAIL") == "1")
                    for (int ri = 0; ri < WorldPlan.Roads.Count; ri++)
                    {
                        var road = WorldPlan.Roads[ri];
                        for (int k = 1; k < road.points.Count; k++)
                        {
                            Vector2 ra = road.points[k - 1], rd = road.points[k] - ra, q = new Vector2(x, z);
                            float rt = rd.sqrMagnitude > .01f ? Math.Max(0f, Math.Min(1f, Vector2.Dot(q - ra, rd) / rd.sqrMagnitude)) : 0f;
                            if ((ra + rd * rt - q).magnitude < Math.Max(.6f, pixel * .5f)) c = new Vector3((ri % 3) * .3f, (ri / 3 % 3) * .3f, .6f);
                        }
                    }
                var b = FarDetailPalette.Encode(c);
                int o = (j * w + i) * 3; pixels[o] = b.r; pixels[o + 1] = b.g; pixels[o + 2] = b.b;
            }
        });
        Ppm(path, w, h, pixels);
    }

    // A mild hillshade from the world heights, so slopes read as they do under the game's sun.
    static float Hill(FarDetailData data, float x, float z, float step)
    {
        float s = Math.Max(8f, step);
        float dx = (data.Height(x + s, z) - data.Height(x - s, z)) / (2f * s), dz = (data.Height(x, z + s) - data.Height(x, z - s)) / (2f * s);
        var n = new Vector3(-dx, 1f, -dz).normalized;
        return .82f + .3f * Math.Max(0f, Vector3.Dot(n, Sun)) - .3f * Sun.y;
    }

    // Roof planes (lit or shaded by their side of the ridge) and walls in the settlement palette; flat roofs uniform;
    // landmark buildings as grey boxes; each casts a short shadow away from the sun. Coverage is box-filtered per pixel.
    static Vector3 Roofs(Vector3 ground, List<SettlementPattern.House> houses, List<LandmarkBuilding> boxes, float x, float z, float pixel)
    {
        int samples = pixel > 1f ? 4 : 2;
        Vector3 sum = Vector3.zero;
        for (int sj = 0; sj < samples; sj++)
            for (int si = 0; si < samples; si++)
            {
                float px = x + ((si + .5f) / samples - .5f) * pixel, pz = z + ((sj + .5f) / samples - .5f) * pixel;
                sum += RoofAt(ground, houses, boxes, px, pz);
            }
        return sum / (samples * samples);
    }

    // The colour of whatever is on top at one point.
    static Vector3 RoofAt(Vector3 ground, List<SettlementPattern.House> houses, List<LandmarkBuilding> boxes, float x, float z)
    {
        Vector2 shadow = new Vector2(-Sun.x, -Sun.z).normalized;
        bool shaded = false;
        foreach (var house in houses)
        {
            Vector3 size = SettlementPattern.Size[house.kind] * house.Scale;
            Vector2 d = new Vector2(x, z) - house.world;
            if (d.sqrMagnitude > 1600f) continue;
            float yaw = house.yaw * Mathf.Deg2Rad;
            Vector2 ax = new Vector2((float)Math.Cos(yaw), -(float)Math.Sin(yaw)), az = new Vector2(-ax.y, ax.x);
            float lx = Vector2.Dot(d, ax), lz = Vector2.Dot(d, az);
            if (Math.Abs(lx) <= size.x * .5f && Math.Abs(lz) <= size.z * .5f)
            {
                Vector3 colour = FarDetailPalette.Decode(RoofColour(house));
                float pitch = SettlementPattern.Roof[Math.Min(house.kind, SettlementPattern.Roof.Length - 1)];
                if (pitch <= 0f) return colour * .95f;
                Vector2 normal = az * Math.Sign(lz == 0f ? 1f : lz);
                float lit = Vector2.Dot(normal, new Vector2(Sun.x, Sun.z));
                return colour * (.8f + .35f * lit) * (Math.Abs(lz) < .25f ? .8f : 1f);
            }
            Vector2 back = d - shadow * (size.y * .55f);
            float bx = Vector2.Dot(back, ax), bz = Vector2.Dot(back, az);
            if (Math.Abs(bx) <= size.x * .5f && Math.Abs(bz) <= size.z * .5f) shaded = true;
        }
        foreach (var b in boxes)
        {
            Vector2 d = new Vector2(x, z) - b.position;
            if (d.sqrMagnitude > 160f * 160f) continue;
            Vector2 right = new Vector2(b.facing.y, -b.facing.x);
            if (Math.Abs(Vector2.Dot(d, right)) < b.size.x * .5f && Math.Abs(Vector2.Dot(d, b.facing)) < b.size.z * .5f)
                return FarDetailPalette.Decode(new Color32(122, 122, 118, 255));
            Vector2 back = d - shadow * (b.size.y * .55f);
            if (Math.Abs(Vector2.Dot(back, right)) < b.size.x * .5f && Math.Abs(Vector2.Dot(back, b.facing)) < b.size.z * .5f) shaded = true;
        }
        return shaded ? ground * .55f : ground;
    }

    // The roof colour SettlementHlod gives this building.
    static Color32 RoofColour(SettlementPattern.House house)
    {
#if BEFORE
        int roof = 4 + (house.kind == 1 || house.kind == 5 ? 1 : house.kind % 3);
        if (house.kind >= 10) return new Color32(104, 92, 80, 255);
        return Palette[roof];
#else
        return SettlementPattern.RoofColour(house);
#endif
    }

#if BEFORE
    // SettlementHlod's palette texels before this change: four walls, then the roofs.
    static readonly Color32[] Palette = { new Color32(178, 168, 146, 255), new Color32(148, 144, 134, 255),
        new Color32(164, 154, 131, 255), new Color32(182, 174, 153, 255), new Color32(138, 79, 58, 255),
        new Color32(83, 91, 94, 255), new Color32(117, 83, 66, 255), new Color32(124, 117, 103, 255) };
#endif

    // Binary PPM, converted to PNG by run.py.
    static void Ppm(string path, int w, int h, byte[] rgb)
    {
        using var file = File.Create(path);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
        file.Write(header, 0, header.Length); file.Write(rgb, 0, rgb.Length);
    }
}

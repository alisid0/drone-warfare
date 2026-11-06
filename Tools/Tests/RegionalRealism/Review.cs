using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Top-down review pictures and measurements of the world outside the sector: a village, a town, a works and a farmland
// block, each seen as the far terrain shows it at 300 m (the 2 m tiles, bilinear) and 2.5 km (the 4 m regional pages),
// with the settlement scatter's houses (SettlementPattern, the plan SettlementHlod and FarScatter draw) and the
// landmark buildings as shaded roofs. One process per run; nothing starts Unity.
static class RegionalReview
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
        string output = args.Length > 0 ? args[0] : "Temp/regional-realism/review";
        bool render = args.Length > 1 && args[1] == "render";
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        WorldPlan.Generate(1200);
        double generate = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var data = WorldPlan.CaptureDetail(null);
        double capture = timer.Elapsed.TotalMilliseconds;
        Console.WriteLine($"world {generate:F0} ms, detail capture {capture:F0} ms");
        var places = Places();
        var report = new Dictionary<string, object> { ["worldMs"] = generate, ["captureMs"] = capture };
        foreach (var place in places) Console.WriteLine($"{place.name}: ({place.center.x:F0}, {place.center.y:F0}) {place.label}");
        report["fields"] = Fields(data, places.Find(p => p.name == "farmland").center);
        report["landUse"] = LandUse(data, places.Find(p => p.name == "farmland").center);
        report["cover"] = CoverShares();
        report["villages"] = Villages(data);
        report["works"] = Works(data);
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
    sealed class Place { public string name, label; public Vector2 center; }

    // The autotest's hd166 village (Ashcombe), hd165's town (Brenmoor), the works nearest the sector it flew from and the
    // most cultivated 2.5 km square away from settlements and landmarks.
    static List<Place> Places()
    {
        var list = new List<Place>();
        foreach (var s in WorldPlan.Settlements)
            if (s.name == "Ashcombe") list.Add(new Place { name = "village", label = s.name, center = s.center });
            else if (s.name == "Brenmoor") list.Add(new Place { name = "town", label = s.name, center = s.center });
        WorldFeature works = null;
        foreach (var f in WorldPlan.Features)
            if (f.valid && f.kind == WorldFeatureKind.IndustrialZone && (works == null || f.center.sqrMagnitude < works.center.sqrMagnitude)) works = f;
        if (works != null) list.Add(new Place { name = "works", label = "IndustrialZone " + works.id, center = works.center });
        // Found once on the 5fb5060 world (the most cultivated 2.5 km square on a 1.25 km grid, 1.6 km clear of
        // settlements and 2 km of landmarks), and kept fixed so later cover changes cannot move the comparison.
        Vector2 best = new Vector2(-18750f, -16250f);
        list.Add(new Place { name = "farmland", label = "the 5fb5060 world's most cultivated square", center = best });
        return list;
    }

    // Parcel shape statistics over the 8 km square round the farmland block: corners under 60 degrees (59.5), wedges (one end
    // under a third of the other), slivers (under 25 m wide or a tenth as wide as long) and the share of field area in them.
    static object Fields(FarDetailData data, Vector2 center)
    {
        int parcels = 0, acute = 0, wedges = 0, slivers = 0, small = 0;
        double area = 0, acuteArea = 0, wedgeArea = 0, sliverArea = 0;
        var widths = new List<float>(); var lengths = new List<float>();
        var seen = new HashSet<FarFieldPlan.District>();
        var timer = Stopwatch.StartNew();
        foreach (var d in Districts(data, center, 4000f))
        {
            if (!seen.Add(d)) continue;
            foreach (var p in d.parcels)
            {
                if (!p.valid) continue;
                float length = p.u1 - p.u0, wa = p.a1 - p.a0, wb = p.b1 - p.b0;
                float slope = Math.Max(Math.Abs(p.b0 - p.a0), Math.Abs(p.b1 - p.a1)) / length;
                float corner = 90f - (float)(Math.Atan(slope) * 180.0 / Math.PI);
                float a = length * (wa + wb) * .5f, narrow = Math.Min(wa, wb), wide = Math.Max(wa, wb);
                parcels++; area += a;
                widths.Add((wa + wb) * .5f); lengths.Add(length);
                if (corner < 59.5f) { acute++; acuteArea += a; }
                if (narrow < wide / 3f) { wedges++; wedgeArea += a; }
                if (narrow < 25f || (wa + wb) * .5f < length * .1f) { slivers++; sliverArea += a; }
                if (a < 6000f) small++;
            }
        }
        widths.Sort(); lengths.Sort();
        float Q(List<float> v, float q) => v.Count == 0 ? 0f : v[Math.Min(v.Count - 1, (int)(q * v.Count))];
        var result = new { districts = seen.Count, parcels, buildMs = timer.Elapsed.TotalMilliseconds,
            acute, acuteShare = acuteArea / Math.Max(1, area), wedges, wedgeShare = wedgeArea / Math.Max(1, area),
            slivers, sliverShare = sliverArea / Math.Max(1, area), small,
            width = new[] { Q(widths, .1f), Q(widths, .5f), Q(widths, .9f) }, length = new[] { Q(lengths, .1f), Q(lengths, .5f), Q(lengths, .9f) } };
        Console.WriteLine($"fields: {seen.Count} districts, {parcels} parcels; acute {acute} ({result.acuteShare:P1} of area), wedges {wedges} ({result.wedgeShare:P1}), slivers {slivers} ({result.sliverShare:P1}); width p10/50/90 {result.width[0]:F0}/{result.width[1]:F0}/{result.width[2]:F0} m, length {result.length[0]:F0}/{result.length[1]:F0}/{result.length[2]:F0} m");
        return result;
    }

    // Every district whose land lies within reach of the point.
    static IEnumerable<FarFieldPlan.District> Districts(FarDetailData data, Vector2 center, float reach)
    {
#if BEFORE
        int x0 = (int)Math.Floor((center.x - reach) / 700f) - 1, x1 = (int)Math.Floor((center.x + reach) / 700f) + 1;
        int z0 = (int)Math.Floor((center.y - reach) / 700f) - 1, z1 = (int)Math.Floor((center.y + reach) / 700f) + 1;
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) yield return data.fields.Get(x, z);
#else
        foreach (var d in data.fields.Districts(center - Vector2.one * reach, center + Vector2.one * reach)) yield return d;
#endif
    }

    // What the 10 km square round the farmland block is used for, sampled every 8 m: crops by kind, meadow, fallow,
    // orchards and market gardens, pasture and other grass, woods; and how far neighbouring crops differ in luma.
    static object LandUse(FarDetailData data, Vector2 center)
    {
        var counts = new Dictionary<string, int>();
        void Add(string k) => counts[k] = counts.TryGetValue(k, out int n) ? n + 1 : 1;
        string[] crops = { "wheat", "young crop", "sunflower", "ploughed", "hay" };
        int total = 0;
        double contrast = 0; int pairs = 0;
        for (float z = center.y - 5000f; z < center.y + 5000f; z += 8f)
            for (float x = center.x - 5000f; x < center.x + 5000f; x += 8f)
            {
                total++;
                if (data.cover.Forest(x, z) > .5f) { Add("woods"); continue; }
                var land = data.cover.At(x, z);
                if (land.Weight(Cover.Farmland) > .5f)
                {
                    var f = data.FieldAt(x, z);
                    if (f.layer >= 5 && f.side >= 0f && f.end >= 0f)
                    {
                        Add(f.use == 1 ? "meadow" : f.use == 2 ? "fallow" : f.use == 3 ? "orchard" : f.use == 4 ? "market garden" : crops[f.layer - 5]);
                        continue;
                    }
#if !BEFORE
                    if (data.fields.At(x, z).pasture) { Add("grazed block"); continue; }
#endif
                    Add("grass between fields"); continue;
                }
                Cover c = land.Dominant();
                Add(c == Cover.Pasture ? "pasture" : c == Cover.Village || c == Cover.Town ? "settlement" : c.ToString().ToLowerInvariant());
            }
        // Neighbouring parcels' painted colour difference, from 300 parcel pairs across 50 m steps.
        var rng = new System.Random(7);
        for (int i = 0; i < 4000 && pairs < 300; i++)
        {
            float x = center.x + (float)(rng.NextDouble() - .5) * 9000f, z = center.y + (float)(rng.NextDouble() - .5) * 9000f;
            var a = data.FieldAt(x, z); var b = data.FieldAt(x + 60f, z);
            if (a.layer < 5 || b.layer < 5 || a.side < 6f || b.side < 6f || a.end < 6f || b.end < 6f) continue;
            if (data.cover.At(x, z).Weight(Cover.Farmland) < 1f || data.cover.At(x + 60f, z).Weight(Cover.Farmland) < 1f) continue;
            if (Math.Abs(a.shade - b.shade) < 1e-6f && a.layer == b.layer) continue; // the same parcel
            contrast += Math.Abs(Luma(data.Field(x, z)) - Luma(data.Field(x + 60f, z))); pairs++;
        }
        var shares = new SortedDictionary<string, double>();
        foreach (var pair in counts) shares[pair.Key] = Math.Round(pair.Value / (double)total, 4);
        double meanContrast = contrast / Math.Max(1, pairs);
        Console.Write("land use:");
        foreach (var pair in shares) Console.Write($" {pair.Key} {pair.Value:P1};");
        Console.WriteLine($" neighbouring crop luma difference {meanContrast:F1} ({pairs} pairs)");
        return new { shares, neighbourLuma = meanContrast, pairs };
    }

    // The world's cover shares on its 32 m grid, and how many 60-260 m woods (copses) it holds.
    static object CoverShares()
    {
        var counts = new Dictionary<string, int>(); int total = 0;
        for (float z = -WorldPlan.Half; z <= WorldPlan.Half; z += 32f)
            for (float x = -WorldPlan.Half; x <= WorldPlan.Half; x += 32f)
            {
                string k = WorldPlan.CoverAt(x, z).ToString();
                counts[k] = counts.TryGetValue(k, out int n) ? n + 1 : 1; total++;
            }
        var shares = new SortedDictionary<string, double>();
        foreach (var pair in counts) shares[pair.Key] = Math.Round(pair.Value / (double)total, 4);
        Console.Write("cover:");
        foreach (var pair in shares) Console.Write($" {pair.Key} {pair.Value:P1};");
        Console.WriteLine();
        return shares;
    }

    // Rec. 709 luma of an sRGB colour, in bytes.
    static float Luma(Color32 c) => .2126f * c.r + .7152f * c.g + .0722f * c.b;

    // Per settlement: plots, houses by kind, farms with a barn, churches; and the painted colour behind houses (the yard).
    static object Villages(FarDetailData data)
    {
        var list = new List<object>();
        var kinds = new Dictionary<int, int>();
        int houses = 0;
        for (int owner = 0; owner < WorldPlan.Settlements.Count; owner++)
        {
            var s = WorldPlan.Settlements[owner];
            int plots = 0, farms = 0, churches = 0, built = 0, barns = 0;
            foreach (var p in data.villages.plots)
            {
                if (p.owner != owner) continue;
                plots++; if (p.farm) farms++; if (p.church) churches++;
            }
            foreach (var block in SettlementPattern.Build(s))
                foreach (var h in block.houses)
                {
                    built++; kinds[h.kind] = kinds.TryGetValue(h.kind, out int n) ? n + 1 : 1;
                    if (h.kind >= 10) barns++;
                }
            houses += built;
            list.Add(new { s.name, s.town, plots, farms, churches, built, barns });
        }
        // Village houses standing gable-end to the street (the rest face it), and plots per village house.
        int villageHouses = 0, gables = 0;
        foreach (var p in data.villages.plots)
        {
            if (p.town || p.square || p.church || p.model == null) continue;
            villageHouses++;
#if !BEFORE
            if (p.gable) gables++;
#endif
        }
        Console.WriteLine($"village houses: {villageHouses}, {gables} gable-end to the street ({gables / (float)Math.Max(1, villageHouses):P0}), the rest facing it");
        // Yard ground behind ordinary village houses, as the 2 m tiles paint it, against the lawn in front.
        double yr = 0, yg = 0, yb = 0, lr = 0, lg = 0, lb = 0; int yards = 0;
        foreach (var p in data.villages.plots)
        {
            if (p.town || p.farm || p.church || p.square || p.landUse != 0 || p.model == null) continue;
            Vector2 yard = p.Point(p.frontage * .06f, (p.YardStart + p.YardEnd) * .5f);
            Vector2 lawn = p.Point(-p.frontage * .3f, p.setback * .5f);
            var a = data.Paint(yard.x, yard.y, yard, null, null, 2f); var b = data.Paint(lawn.x, lawn.y, lawn, null, null, 2f);
            yr += a.r; yg += a.g; yb += a.b; lr += b.r; lg += b.g; lb += b.b; yards++;
        }
        var yardColour = new[] { yr / Math.Max(1, yards), yg / Math.Max(1, yards), yb / Math.Max(1, yards) };
        var lawnColour = new[] { lr / Math.Max(1, yards), lg / Math.Max(1, yards), lb / Math.Max(1, yards) };
        Console.WriteLine($"villages: {houses} scattered buildings; kinds {string.Join(", ", Kinds(kinds))}; yard paint ({yardColour[0]:F0}, {yardColour[1]:F0}, {yardColour[2]:F0}) vs front lawn ({lawnColour[0]:F0}, {lawnColour[1]:F0}, {lawnColour[2]:F0}) over {yards} houses");
        return new { settlements = list, kinds, yardColour, lawnColour, yards, villageHouses, gables };
    }

    // Kind histogram for the log.
    static IEnumerable<string> Kinds(Dictionary<int, int> kinds)
    {
        var keys = new List<int>(kinds.Keys); keys.Sort();
        foreach (int k in keys) yield return k + ":" + kinds[k];
    }

    // Round each works: the painted colour in rings outside its footprint, and how much of each ring is cultivated.
    static object Works(FarDetailData data)
    {
        var list = new List<object>();
        foreach (var f in WorldPlan.Features)
        {
            if (!f.valid) continue;
            var rings = new List<object>();
            foreach (float ring in new[] { 4f, 16f, 40f, 70f, 100f, 160f })
            {
                int n = 0, crops = 0; double r = 0, g = 0, b = 0;
                for (float t = 0f; t < 1f; t += .005f)
                {
                    float perimeter = 4f * (f.halfLength + f.halfWidth + 2f * ring), s = t * perimeter;
                    float u, v, hl = f.halfLength + ring, hw = f.halfWidth + ring;
                    if (s < 2f * hl) { u = -hl + s; v = -hw; }
                    else if (s < 2f * hl + 2f * hw) { u = hl; v = -hw + s - 2f * hl; }
                    else if (s < 4f * hl + 2f * hw) { u = hl - (s - 2f * hl - 2f * hw); v = hw; }
                    else { u = -hl; v = hw - (s - 4f * hl - 2f * hw); }
                    Vector2 p = f.Area.Point(u, v);
                    var c = data.Paint(p.x, p.y, p, null, null, 4f);
                    r += c.r; g += c.g; b += c.b; n++;
                    var field = data.FieldAt(p.x, p.y);
                    if (data.cover.At(p.x, p.y).Weight(Cover.Farmland) > .5f && field.layer >= 5 && field.side >= 0f && field.end >= 0f) crops++;
                }
                rings.Add(new { ring, colour = new[] { r / n, g / n, b / n }, cultivated = crops / (float)n });
            }
            list.Add(new { f.id, kind = f.kind.ToString(), rings });
        }
        return list;
    }

    // Construction and painting costs: the 2 m tile and a coarse whole-world picture (every district is built).
    static object Timing(FarDetailData data, List<Place> places)
    {
        var tiles = new Dictionary<string, double>();
        var roughness = new float[FarDetailData.Samples * FarDetailData.Samples];
        var coarse = new float[14 * 14][];
        for (int i = 0; i < coarse.Length; i++) coarse[i] = new float[65 * 65];
        var tileData = WorldPlan.CaptureDetail(coarse); // a sector's view of the same snapshot, with flat coarse heights
        foreach (var place in places)
        {
            int x = (int)Math.Floor(place.center.x / FarDetailData.Size), z = (int)Math.Floor(place.center.y / FarDetailData.Size);
            tileData.Build(x, z, roughness, null); // warm the districts and villages it touches
            double best = double.MaxValue;
            for (int i = 0; i < 3; i++) { var t = Stopwatch.StartNew(); tileData.Build(x, z, roughness, null); best = Math.Min(best, t.Elapsed.TotalMilliseconds); }
            tiles[place.name] = best;
        }
        var timer = Stopwatch.StartNew();
        data.WorldPixels(512);
        double world = timer.Elapsed.TotalMilliseconds;
        Console.Write("timing: 2 m tile");
        foreach (var pair in tiles) Console.Write($" {pair.Key} {pair.Value:F0} ms;");
        Console.WriteLine($" 512-square world picture (cold districts) {world:F0} ms, district CPU {data.fields.BuildMilliseconds:F0} ms over {data.fields.BuiltDistricts}");
        return new { tiles, worldPicture512Ms = world, districtMs = data.fields.BuildMilliseconds, districts = data.fields.BuiltDistricts };
    }

    // A 1200x800 top-down picture: at 300 m the ground is the 2 m tile lattice filtered bilinearly, at 2.5 km the 4 m
    // regional texel; woods are the canopy's coverage, and houses and landmark buildings are shaded roofs with a shadow.
    static void Picture(FarDetailData data, Place place, float distance, string path)
    {
        const int w = 1200, h = 800;
        float pixel = distance < 1000f ? .5f : 4f, texel = distance < 1000f ? 2f : 4f;
        float x0 = place.center.x - w * pixel * .5f, z1 = place.center.y + h * pixel * .5f;
        // The painter on its own lattice, decoded to linear for filtering.
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
            if ((s.center - place.center).magnitude > s.radius + w * pixel) continue;
            foreach (var block in SettlementPattern.Build(s)) houses.AddRange(block.houses);
        }
        var boxes = new List<LandmarkBuilding>();
        foreach (var f in WorldPlan.Features) if (f.valid && (f.center - place.center).magnitude < w * pixel + 800f) boxes.AddRange(f.buildings);
        var pixels = new byte[w * h * 3];
        System.Threading.Tasks.Parallel.For(0, h, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 2 }, j =>
        {
            for (int i = 0; i < w; i++)
            {
                float x = x0 + (i + .5f) * pixel, z = z1 - (j + .5f) * pixel;
                float fx = x / texel - .5f - gx0, fz = z / texel - .5f - gz0;
                int ix = (int)Math.Floor(fx), iz = (int)Math.Floor(fz);
                float u = fx - ix, v = fz - iz;
                Vector3 c = Vector3.Lerp(Vector3.Lerp(lattice[iz * gw + ix], lattice[iz * gw + ix + 1], u),
                    Vector3.Lerp(lattice[(iz + 1) * gw + ix], lattice[(iz + 1) * gw + ix + 1], u), v);
                c = Roofs(c, houses, boxes, x, z, pixel);
                var b = FarDetailPalette.Encode(c);
                int k = (j * w + i) * 3; pixels[k] = b.r; pixels[k + 1] = b.g; pixels[k + 2] = b.b;
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
        Vector3 c = ground;
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
            if (d.sqrMagnitude > 900f) continue;
            float yaw = house.yaw * Mathf.Deg2Rad;
            Vector2 ax = new Vector2((float)Math.Cos(yaw), -(float)Math.Sin(yaw)), az = new Vector2(-ax.y, ax.x);
            float lx = Vector2.Dot(d, ax), lz = Vector2.Dot(d, az);
            if (Math.Abs(lx) <= size.x * .5f && Math.Abs(lz) <= size.z * .5f)
            {
                int wall = house.kind % 4, roof = 4 + (house.kind == 1 || house.kind == 5 ? 1 : house.kind % 3);
                Vector3 colour = FarDetailPalette.Decode(Palette[house.kind >= 10 ? 6 : roof]);
                if (house.kind >= 10) colour = FarDetailPalette.Decode(new Color32(104, 92, 80, 255));
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

    // SettlementHlod's eight palette texels: four walls, then the roofs.
    static readonly Color32[] Palette = { new Color32(178, 168, 146, 255), new Color32(148, 144, 134, 255),
        new Color32(164, 154, 131, 255), new Color32(182, 174, 153, 255), new Color32(138, 79, 58, 255),
        new Color32(83, 91, 94, 255), new Color32(117, 83, 66, 255), new Color32(124, 117, 103, 255) };

    // Binary PPM, converted to PNG by run.py.
    static void Ppm(string path, int w, int h, byte[] rgb)
    {
        using var file = File.Create(path);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
        file.Write(header, 0, header.Length); file.Write(rgb, 0, rgb.Length);
    }
}

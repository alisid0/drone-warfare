using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Offline review of the near bubble: a CPU model of FarDetailSurface's tile path (2 m colour, the 4 m regional edge
// band, the 2 m control's detail layer) before, and with the near pages (0.5 m BC1 colour decoded as uploaded, 1 m
// control) after, top-down at the pixel size a 45 degree, 1080-line camera has at 50, 100 and 300 m. Albedo only:
// no lighting, normal maps, fog or TAA. One process; pictures on one thread, page colour on two as in the game;
// never Unity or the GPU.
static class NearBubbleReview
{
    const int Width = 1280, Height = 720;
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    static readonly float[] Metres = FarDetailPalette.Metres;
    static FarDetailData data;
    static Vector3[][][] albedo; // [layer][mip] linear, 512 square down to 1
    static readonly Dictionary<long, Color32> tileColour = new Dictionary<long, Color32>(), tileControl = new Dictionary<long, Color32>();
    static readonly Dictionary<long, Color32[]> nearPages = new Dictionary<long, Color32[]>(), nearControls = new Dictionary<long, Color32[]>(), regionalPages = new Dictionary<long, Color32[]>();
    static readonly FarDetailNearData scratch = new FarDetailNearData();

    struct Scene { public string name; public Vector2 centre; public float score; }

    // Fail with a short diagnostic rather than a crash dialog when run unattended.
    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Arguments: output folder, texture folder.
    static int Run(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "Temp/near-bubble/review";
        string textures = args.Length > 1 ? args[1] : "Temp/near-bubble/textures";
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        WorldPlan.Generate(1200);
        data = WorldPlan.CaptureDetail(null);
        Console.WriteLine($"world {timer.Elapsed.TotalSeconds:F1} s");
        var scenes = new[] { Find("field"), Find("road"), Find("village") };
        foreach (var s in scenes) Console.WriteLine($"{s.name}: {s.centre.x:F1}, {s.centre.y:F1} (score {s.score})");
        var result = new Dictionary<string, object>
        {
            ["scenes"] = Array.ConvertAll(scenes, s => new { s.name, x = s.centre.x, z = s.centre.y, s.score }),
            ["timing"] = Timing(scenes),
            ["colourShift"] = Shift(scenes),
            ["bytes"] = new { colourPerPage = FarDetailNearData.ColourBytes, controlPerPage = FarDetailNearData.ControlBytes, pages = 16,
                residentWithStaging = 17 * FarDetailNearData.Bytes }
        };
        File.WriteAllText(Path.Combine(output, "review.json"), JsonSerializer.Serialize(result, Json));
        Console.WriteLine(JsonSerializer.Serialize(result, Json));
        LoadArrays(textures);
        foreach (var s in scenes)
            foreach (float altitude in new[] { 50f, 100f, 300f })
            {
                float mpp = 2f * altitude * (float)Math.Tan(Scale.CameraFov * .5f * Math.PI / 180.0) / 1080f;
                string tag = s.name + "-" + (int)altitude + "m";
                Save(Path.Combine(output, tag + "-before.ppm"), Render(s.centre, mpp, (x, z) => 0f));
                Save(Path.Combine(output, tag + "-after.ppm"), Render(s.centre, mpp, (x, z) => 1f));
            }
        // A page edge inside a tile whose east neighbour has not arrived yet: its 16 m band blends to the 2 m tile with
        // no line (at twice the 50 m pixel, to show the whole band).
        var field = scenes[0].centre;
        int east = (int)Math.Floor(field.x / 512f) * 2 + 1;
        var edge = new Vector2(east * 256f, field.y);
        float near = 2f * 50f * (float)Math.Tan(Scale.CameraFov * .5f * Math.PI / 180.0) / 1080f;
        Save(Path.Combine(output, "page-edge-100m-neighbour-missing.ppm"), Render(edge, near * 2f, (x, z) => x >= east ? 0f : 1f));
        // A tile edge at 100 m: the tile's colour fades to the 4 m regional page and its detail fades out over 32 m each
        // side before; the bubble's pages run straight through after.
        var tileEdge = new Vector2((float)Math.Round(field.x / 512f) * 512f, field.y);
        Save(Path.Combine(output, "tile-edge-100m-before.ppm"), Render(tileEdge, near * 2f, (x, z) => 0f));
        Save(Path.Combine(output, "tile-edge-100m-after.ppm"), Render(tileEdge, near * 2f, (x, z) => 1f));
        Console.WriteLine($"total {timer.Elapsed.TotalSeconds:F1} s");
        return 0;
    }

    // ---- Scenes ----

    // The most varied window of each kind near the first settlements, by distinct surfaces in the 50 m view.
    static Scene Find(string kind)
    {
        var best = new Scene { name = kind, score = -1f };
        int checkedCount = 0;
        for (int s = 0; s < WorldPlan.Settlements.Count && checkedCount < Cap(kind); s++)
        {
            var town = WorldPlan.Settlements[s];
            if (town.town) continue;
            for (float dz = -900f; dz <= 900f && checkedCount < Cap(kind); dz += 24f)
                for (float dx = -900f; dx <= 900f && checkedCount < Cap(kind); dx += 24f)
                {
                    float x = town.center.x + dx, z = town.center.y + dz;
                    if (Math.Abs(x) > WorldPlan.Half - 3000f || Math.Abs(z) > WorldPlan.Half - 3000f || !Interior(x, z) || !Wanted(kind, x, z)) continue;
                    checkedCount++;
                    float score = Variety(kind, x, z);
                    if (score > best.score) { best.score = score; best.centre = new Vector2(x, z); }
                }
        }
        return best;
    }

    // The 300 m view (221 by 124 m) clear of a tile's 32 m edge band, so the pairs compare 2 m with 0.5 m alone.
    static bool Interior(float x, float z)
    {
        float lx = x - (float)Math.Floor(x / 512f) * 512f, lz = z - (float)Math.Floor(z / 512f) * 512f;
        return lx > 143f && lx < 369f && lz > 95f && lz < 417f;
    }

    // Candidates scored per kind; gardens beside open land are rarer than fields and roads.
    static int Cap(string kind) => kind == "village" ? 1500 : 400;

    // A crop field away from roads and gardens; a paved road through fields; a garden boundary beside open land.
    static bool Wanted(string kind, float x, float z)
    {
        var road = data.roadNetwork.At(x, z);
        var land = data.cover.At(x, z);
        bool garden = data.villages.At(new Vector2(x, z), out _);
        if (kind == "field")
        {
            var f = data.FieldAt(x, z);
            return !garden && (!road.found || road.edge > 40f) && land.Weight(Cover.Farmland) >= 1f && f.layer >= 5 && f.use <= 2
                && Math.Min(f.side, f.end) > 6f && Math.Min(f.side, f.end) < 14f;
        }
        if (kind == "road")
            return road.found && road.kind == RoadClass.Road && road.edge < -1f && !garden && land.Weight(Cover.Farmland) + land.Weight(Cover.Pasture) >= 1f;
        if (!garden || road.found && road.edge < 6f) return false;
        for (int k = 0; k < 4; k++)
        {
            float a = k * (float)Math.PI * .5f;
            float ox = x + 18f * (float)Math.Cos(a), oz = z + 18f * (float)Math.Sin(a);
            var open = data.cover.At(ox, oz);
            if (!data.villages.At(new Vector2(ox, oz), out _) && open.Weight(Cover.Farmland) + open.Weight(Cover.Pasture) >= 1f) return true;
        }
        return false;
    }

    // Distinct surface controls and colours over the 50 m view, sampled every 3 m; a village edge also wants about as
    // much garden as open land in it.
    static float Variety(string kind, float x, float z)
    {
        var codes = new HashSet<int>(); var tones = new HashSet<int>();
        int gardens = 0, samples = 0;
        for (float j = -12f; j <= 12f; j += 3f)
            for (float i = -22f; i <= 22f; i += 3f)
            {
                var c = data.Control(x + i, z + j, new Vector2(x + i, z + j));
                codes.Add(c.r * 4 + c.a / 64);
                var p = data.Paint(x + i, z + j, new Vector2(x + i, z + j), null, null, .5f);
                tones.Add(p.r / 12 * 4096 + p.g / 12 * 64 + p.b / 12);
                if (data.villages.At(new Vector2(x + i, z + j), out _)) gardens++;
                samples++;
            }
        float score = codes.Count * 4 + Math.Min(tones.Count, 40) * .25f;
        if (kind == "village") score += 20f * (1f - Math.Abs(gardens / (float)samples - .5f) * 2f);
        return score;
    }

    // ---- Timing and colour agreement ----

    // Near page builds (paint, control, mips and BC1) beside full 2 m tile builds (mesh, collider, colour, control,
    // mips) of the same places, best of three; the control's share is timed alone with the page's own culled vectors.
    static object Timing(Scene[] scenes)
    {
        var pages = new List<object>(); var tiles = new List<double>();
        var nearby = typeof(FarDetailData).GetMethod("Nearby", BindingFlags.NonPublic | BindingFlags.Static);
        var rivers = (FarDetailData.Segment[])typeof(FarDetailData).GetField("rivers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(data);
        var roads = (FarDetailData.Segment[])typeof(FarDetailData).GetField("roads", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(data);
        var tileData = new FarDetailData(data, Coarse());
        var roughness = new float[FarDetailData.Samples * FarDetailData.Samples];
        FarDetailData.Tile reuse = null;
        foreach (var s in scenes)
        {
            int px = (int)Math.Floor(s.centre.x / 256f), pz = (int)Math.Floor(s.centre.y / 256f);
            double best = double.MaxValue, paint = 0;
            for (int k = 0; k < 3; k++)
            {
                scratch.Build(data, px, pz);
                if (scratch.milliseconds < best) { best = scratch.milliseconds; paint = scratch.paintMs; }
            }
            var sw = Stopwatch.StartNew();
            var lanes = (FarDetailData.Segment[])nearby.Invoke(null, new object[] { roads, px * 256f - 8f, pz * 256f - 8f, 272f });
            var streams = (FarDetailData.Segment[])nearby.Invoke(null, new object[] { rivers, px * 256f - 8f, pz * 256f - 8f, 272f });
            int m = FarDetailNearData.ControlResolution;
            for (int j = 0; j < m; j++) for (int i = 0; i < m; i++) { float wx = px * 256f + i - 7.5f, wz = pz * 256f + j - 7.5f; data.Control(wx, wz, new Vector2(wx, wz), lanes, streams); }
            double control = sw.Elapsed.TotalMilliseconds;
            pages.Add(new { s.name, page = new[] { px, pz }, buildMs = Math.Round(best, 1), paintAndControlMs = Math.Round(paint, 1), controlMs = Math.Round(control, 1) });
            int tx = (int)Math.Floor(s.centre.x / 512f), tz = (int)Math.Floor(s.centre.y / 512f);
            double tile = double.MaxValue;
            for (int k = 0; k < 3; k++) { reuse = tileData.Build(tx, tz, roughness, null, reuse); tile = Math.Min(tile, reuse.generationMs); }
            tiles.Add(Math.Round(tile, 1));
        }
        return new { nearPages = pages, tileBuildMs = tiles, note = ".NET 8 RyuJIT; a page paints its colour on two threads, its control and a tile on one; Mono in the player is slower" };
    }

    // Coarse chunk heights sampled from the world grid, as FarTerrain's LOD0 holds them, for full tile builds.
    static float[][] Coarse()
    {
        var coarse = new float[14 * 14][];
        for (int c = 0; c < coarse.Length; c++)
        {
            coarse[c] = new float[65 * 65];
            for (int j = 0; j < 65; j++) for (int i = 0; i < 65; i++)
                    coarse[c][j * 65 + i] = data.Height(-WorldPlan.Half + (c % 14) * 4000f + i * 62.5f, -WorldPlan.Half + (c / 14) * 4000f + j * 62.5f);
        }
        return coarse;
    }

    // The bubble fades out by footprint onto the tile's 2 m colour, and in over time on top of it: its 2 m mip (BC1, as
    // uploaded) must carry the tile's colour. Signed means and spreads in sRGB levels, per 2 m texel and per 8 m block.
    static string debugFolder = Environment.GetEnvironmentVariable("NEAR_DEBUG");
    static object Shift(Scene[] scenes)
    {
        var rows = new List<object>();
        foreach (var s in scenes)
        {
            int px = (int)Math.Floor(s.centre.x / 256f), pz = (int)Math.Floor(s.centre.y / 256f);
            scratch.Build(data, px, pz);
            int n = FarDetailNearData.Resolution >> 2, g = FarDetailNearData.Gutter >> 2;
            var mip2 = FarDetailRegionData.Decode(scratch.mips[2], n);
            var mip0 = FarDetailRegionData.Decode(scratch.mips[0], FarDetailNearData.Resolution);
            double[] sum = new double[3]; var abs = new List<double>(); var blocks = new List<double>();
            var blockSum = new Vector3[32 * 32]; var blockCount = new int[32 * 32];
            var debug = debugFolder != null ? new Color32[384 * 128] : null;
            for (int j = 0; j < 128; j++) for (int i = 0; i < 128; i++)
                {
                    float wx = px * 256f + 2f * i + 1f, wz = pz * 256f + 2f * j + 1f; // odd metres, the tile lattice
                    Vector3 tile = FarDetailPalette.Decode(data.Paint(wx, wz, new Vector2(wx, wz)));
                    Vector3 near = FarDetailPalette.Decode(mip2[(j + g) * n + i + g]);
                    Color32 a = FarDetailPalette.Encode(tile), b = FarDetailPalette.Encode(near);
                    sum[0] += b.r - a.r; sum[1] += b.g - a.g; sum[2] += b.b - a.b;
                    abs.Add(Math.Max(Math.Abs(b.r - a.r), Math.Max(Math.Abs(b.g - a.g), Math.Abs(b.b - a.b))));
                    blockSum[j / 4 * 32 + i / 4] += near - tile; blockCount[j / 4 * 32 + i / 4]++;
                    if (debug != null) { debug[(127 - j) * 384 + i] = a; debug[(127 - j) * 384 + 128 + i] = b; Vector3 e = near - tile; debug[(127 - j) * 384 + 256 + i] = FarDetailPalette.Encode(new Vector3(.2f + e.x * 8f, .2f + e.y * 8f, .2f + e.z * 8f)); }
                }
            if (debug != null)
            {
                using var stream = File.Create(Path.Combine(debugFolder, "shift-" + s.name + ".ppm"));
                var header = System.Text.Encoding.ASCII.GetBytes("P6 384 128 255 "); stream.Write(header, 0, header.Length);
                foreach (var c in debug) { stream.WriteByte(c.r); stream.WriteByte(c.g); stream.WriteByte(c.b); }
            }
            for (int k = 0; k < blockSum.Length; k++)
            {
                Vector3 d = blockSum[k] / blockCount[k];
                blocks.Add(Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z))));
            }
            abs.Sort(); blocks.Sort();
            // BC1's own error at 0.5 m against the uncompressed painter.
            var raw = new Color32[FarDetailNearData.Resolution * FarDetailNearData.Resolution];
            var controls = new Color32[FarDetailNearData.ControlResolution * FarDetailNearData.ControlResolution];
            data.NearPixels(px, pz, raw, controls);
            double bc1 = 0; for (int k = 0; k < raw.Length; k++) bc1 += Math.Abs(raw[k].r - mip0[k].r) + Math.Abs(raw[k].g - mip0[k].g) + Math.Abs(raw[k].b - mip0[k].b);
            rows.Add(new
            {
                s.name,
                meanSignedLevels = new[] { Math.Round(sum[0] / abs.Count, 2), Math.Round(sum[1] / abs.Count, 2), Math.Round(sum[2] / abs.Count, 2) },
                texelAbsP50 = abs[abs.Count / 2], texelAbsP99 = abs[(int)(abs.Count * .99)],
                block8mLinearP99 = Math.Round(blocks[(int)(blocks.Count * .99)], 5), block8mLinearMax = Math.Round(blocks[blocks.Count - 1], 5),
                bc1MeanAbsLevelsPerChannel = Math.Round(bc1 / raw.Length / 3, 2)
            });
        }
        return rows;
    }

    // ---- The shader model ----

    // FarDetailSurface's tile path for one top-down pixel; `ready` gives a near page's readiness (0 before).
    static Vector3 Shade(Vector2 world, float mpp, Func<int, int, float> ready)
    {
        int tx = (int)Math.Floor(world.x / 512f), tz = (int)Math.Floor(world.y / 512f);
        Vector2 local = world - new Vector2(tx * 512f, tz * 512f);
        float farEdge = Math.Min(Math.Min(local.x, local.y), Math.Min(512f - local.x, 512f - local.y));
        float farTileWeight = SmoothStep(0f, 32f, farEdge);
        Vector3 baseColour = Bilinear(world, 2f, 1f, TileColour);
        if (farTileWeight < 1f) baseColour = Vector3.Lerp(Regional(world), baseColour, farTileWeight);
        float nearWeight = NearWeight(world, mpp, ready);
        if (nearWeight > 0f) baseColour = Vector3.Lerp(baseColour, NearColour(world), nearWeight);
        float fade = 1f - Mathf.Clamp01((mpp - 1f) / 2f);
        Vector3 colour = Vector3.one;
        float tileFade = fade * farTileWeight;
        if (nearWeight < 1f && tileFade > 0f) colour = Vector3.Lerp(Vector3.one, Detail(world, mpp, 2f, 1f, TileControl), tileFade);
        if (nearWeight > 0f) colour = Vector3.Lerp(colour, Vector3.Lerp(Vector3.one, Detail(world, mpp, 1f, .5f, NearControl), fade), nearWeight);
        return new Vector3(baseColour.x * colour.x, baseColour.y * colour.y, baseColour.z * colour.z);
    }

    // FarNearCoverage: readiness with 16 m page-edge bands, gated by the screen footprint.
    static float NearWeight(Vector2 world, float mpp, Func<int, int, float> ready)
    {
        int cx = (int)Math.Floor(world.x / 256f), cz = (int)Math.Floor(world.y / 256f);
        float lx = world.x - cx * 256f, lz = world.y - cz * 256f;
        float sharp = 1f - SmoothStep(1.5f, 2.5f, mpp);
        float own = ready(cx, cz);
        if (sharp <= 0f || own <= 0f) return 0f;
        int sx = lx < 128f ? -1 : 1, sz = lz < 128f ? -1 : 1;
        float ex = Math.Min(lx, 256f - lx), ez = Math.Min(lz, 256f - lz);
        if (Math.Min(ex, ez) >= 16f) return own * sharp;
        float tx = SmoothStep(0f, 16f, ex), tz = SmoothStep(0f, 16f, ez);
        float a = Math.Min(own, ready(cx + sx, cz)), b = Math.Min(own, ready(cx, cz + sz));
        float c = Math.Min(Math.Min(a, b), ready(cx + sx, cz + sz));
        return Mathf.Lerp(Mathf.Lerp(c, b, tx), Mathf.Lerp(a, own, tx), tz) * sharp;
    }

    // The four point-loaded controls round a pixel on a lattice of `step` metres whose texel centres sit at `phase`,
    // their sampled surfaces bilinearly blended (one sample when all four agree).
    static Vector3 Detail(Vector2 world, float mpp, float step, float phase, Func<int, int, Color32> control)
    {
        float gx = (world.x - phase) / step, gz = (world.y - phase) / step;
        int x = (int)Math.Floor(gx), z = (int)Math.Floor(gz); float fx = gx - x, fz = gz - z;
        Color32 a = control(x, z), b = control(x + 1, z), c = control(x, z + 1), d = control(x + 1, z + 1);
        Vector3 ca = Sample(a, world, mpp);
        if (Same(a, b) && Same(a, c) && Same(a, d)) return ca;
        Vector3 cb = Sample(b, world, mpp), cc = Sample(c, world, mpp), cd = Sample(d, world, mpp);
        return Vector3.Lerp(Vector3.Lerp(ca, cb, fx), Vector3.Lerp(cc, cd, fx), fz);
    }

    // GroundSample: the layer's photo or crop texture along its rows, over its mean, with windrows and sprayer tracks.
    static Vector3 Sample(Color32 code, Vector2 world, float mpp)
    {
        int layer = Math.Min((int)code.r, 9);
        float angle = (code.g + code.b * 256f) * (float)(Math.PI / 65535.0);
        float s = (float)Math.Sin(angle), c = (float)Math.Cos(angle);
        Vector2 across = layer >= 5 ? new Vector2(-s, c) : new Vector2(1f, 0f), along = layer >= 5 ? new Vector2(c, s) : new Vector2(0f, 1f);
        float metres = Metres[layer];
        float u = (world.x * across.x + world.y * across.y) / metres, v = (world.x * along.x + world.y * along.y) / metres;
        Vector3 mean = FarDetailPalette.Means[layer], albedoSample = Trilinear(layer, u, v, mpp * 512f / metres);
        Vector3 detail = new Vector3(albedoSample.x / mean.x, albedoSample.y / mean.y, albedoSample.z / mean.z);
        float footprint = Math.Max(mpp * (Math.Abs(across.x) + Math.Abs(across.y)), .02f);
        float w = world.x * across.x + world.y * across.y;
        if (layer == 9) detail *= (.96f + .26f * Band(w, 7f, 1.1f, footprint)) / (.96f + .26f * 1.1f / 7f);
        if (layer == 5 || layer == 6 || layer == 8)
        {
            float tracks = Band(w, 24f, .3f, footprint) + Band(w + 1.8f, 24f, .3f, footprint);
            detail *= (1f - .28f * tracks) / (1f - .28f * .6f / 24f);
        }
        return Vector3.Lerp(Vector3.one, detail, code.a / 255f);
    }

    // GroundBand: a box-filtered periodic band.
    static float Band(float at, float period, float width, float footprint)
    {
        float h = Math.Max(footprint, .001f);
        float a = (at - h * .5f) / period, b = (at + h * .5f) / period, w = width / period;
        float ia = (float)Math.Floor(a) * w + Math.Min(a - (float)Math.Floor(a), w);
        float ib = (float)Math.Floor(b) * w + Math.Min(b - (float)Math.Floor(b), w);
        return Mathf.Clamp01((ib - ia) * period / h);
    }

    // Integer-lattice samplers: the 2 m tile colour and control at odd metres, the 1 m near control at half metres,
    // and the near colour from its page, BC1-decoded exactly as uploaded.
    static Vector3 TileColour(int i, int j)
    {
        long key = Key(i, j);
        if (!tileColour.TryGetValue(key, out var c)) { float x = 2f * i + 1f, z = 2f * j + 1f; tileColour[key] = c = data.Paint(x, z, new Vector2(x, z)); }
        return FarDetailPalette.Decode(c);
    }

    static Color32 TileControl(int i, int j)
    {
        long key = Key(i, j);
        if (!tileControl.TryGetValue(key, out var c)) { float x = 2f * i + 1f, z = 2f * j + 1f; tileControl[key] = c = data.Control(x, z, new Vector2(x, z)); }
        return c;
    }

    static Color32 NearControl(int i, int j)
    {
        int px = (int)Math.Floor(i / 256f), pz = (int)Math.Floor(j / 256f);
        Page(px, pz);
        int m = FarDetailNearData.ControlResolution, g = FarDetailNearData.ControlGutter;
        return nearControls[Key(px, pz)][(j - pz * 256 + g) * m + i - px * 256 + g];
    }

    static Vector3 NearColour(Vector2 world)
    {
        int px = (int)Math.Floor(world.x / 256f), pz = (int)Math.Floor(world.y / 256f);
        var pixels = Page(px, pz);
        int n = FarDetailNearData.Resolution, g = FarDetailNearData.Gutter;
        float gx = (world.x - px * 256f) * 2f + g - .5f, gz = (world.y - pz * 256f) * 2f + g - .5f;
        int x = (int)Math.Floor(gx), z = (int)Math.Floor(gz); float fx = gx - x, fz = gz - z;
        Vector3 A = FarDetailPalette.Decode(pixels[z * n + x]), B = FarDetailPalette.Decode(pixels[z * n + x + 1]);
        Vector3 C = FarDetailPalette.Decode(pixels[(z + 1) * n + x]), D = FarDetailPalette.Decode(pixels[(z + 1) * n + x + 1]);
        return Vector3.Lerp(Vector3.Lerp(A, B, fx), Vector3.Lerp(C, D, fx), fz);
    }

    // One near page through the production builder, decoded from its BC1 mip 0.
    static Color32[] Page(int px, int pz)
    {
        long key = Key(px, pz);
        if (nearPages.TryGetValue(key, out var pixels)) return pixels;
        scratch.Build(data, px, pz);
        nearPages[key] = pixels = FarDetailRegionData.Decode(scratch.mips[0], FarDetailNearData.Resolution);
        nearControls[key] = (Color32[])scratch.controls.Clone();
        return pixels;
    }

    // The regional background the tile fades to in its outer 32 m: the 4 m page, BC1-decoded.
    static Vector3 Regional(Vector2 world)
    {
        int px = (int)Math.Floor(world.x / 2048f), pz = (int)Math.Floor(world.y / 2048f);
        long key = Key(px, pz);
        int n = FarDetailRegionData.Resolution;
        if (!regionalPages.TryGetValue(key, out var pixels)) regionalPages[key] = pixels = FarDetailRegionData.Decode(FarDetailRegionData.Build(data, px, pz).mips[0], n);
        float gx = (world.x - px * 2048f) / 4f + FarDetailRegionData.Gutter - .5f, gz = (world.y - pz * 2048f) / 4f + FarDetailRegionData.Gutter - .5f;
        int x = (int)Math.Floor(gx), z = (int)Math.Floor(gz); float fx = gx - x, fz = gz - z;
        Vector3 A = FarDetailPalette.Decode(pixels[z * n + x]), B = FarDetailPalette.Decode(pixels[z * n + x + 1]);
        Vector3 C = FarDetailPalette.Decode(pixels[(z + 1) * n + x]), D = FarDetailPalette.Decode(pixels[(z + 1) * n + x + 1]);
        return Vector3.Lerp(Vector3.Lerp(A, B, fx), Vector3.Lerp(C, D, fx), fz);
    }

    // Bilinear filtering of a lattice in linear light, as the GPU's sRGB sampler does.
    static Vector3 Bilinear(Vector2 world, float step, float phase, Func<int, int, Vector3> lattice)
    {
        float gx = (world.x - phase) / step, gz = (world.y - phase) / step;
        int x = (int)Math.Floor(gx), z = (int)Math.Floor(gz); float fx = gx - x, fz = gz - z;
        return Vector3.Lerp(Vector3.Lerp(lattice(x, z), lattice(x + 1, z), fx), Vector3.Lerp(lattice(x, z + 1), lattice(x + 1, z + 1), fx), fz);
    }

    // ---- The shared ground arrays ----

    // Setup's arrays: the four photos tinted in linear light (layers 0-4), Farmland's crop maps (5-9), with box mips.
    static void LoadArrays(string folder)
    {
        albedo = new Vector3[10][][];
        for (int layer = 0; layer < 10; layer++)
        {
            var top = new Vector3[512 * 512];
            if (layer < 5)
            {
                var bytes = File.ReadAllBytes(Path.Combine(folder, FarDetailPalette.Photos[layer] + ".rgb"));
                Vector3 tint = FarDetailPalette.Tints[layer];
                for (int k = 0; k < top.Length; k++)
                    top[k] = new Vector3(FarDetailPalette.Linear(bytes[k * 3] / 255f) * tint.x, FarDetailPalette.Linear(bytes[k * 3 + 1] / 255f) * tint.y, FarDetailPalette.Linear(bytes[k * 3 + 2] / 255f) * tint.z);
            }
            else
            {
                var pixels = Farmland.CropTexture((Crop)(layer - 5)).pixels32;
                for (int k = 0; k < top.Length; k++) top[k] = FarDetailPalette.Decode(pixels[k]);
            }
            var levels = new List<Vector3[]> { top };
            for (int n = 256; n >= 1; n /= 2)
            {
                var up = levels[levels.Count - 1]; var next = new Vector3[n * n];
                for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
                        next[j * n + i] = (up[2 * j * 2 * n + 2 * i] + up[2 * j * 2 * n + 2 * i + 1] + up[(2 * j + 1) * 2 * n + 2 * i] + up[(2 * j + 1) * 2 * n + 2 * i + 1]) * .25f;
                levels.Add(next);
            }
            albedo[layer] = levels.ToArray();
        }
    }

    // Repeat-wrapped trilinear sampling with the mip from texels per pixel.
    static Vector3 Trilinear(int layer, float u, float v, float texels)
    {
        float lod = Math.Max(0f, (float)Math.Log(Math.Max(texels, 1e-6f), 2));
        int m = Math.Min((int)lod, 9); float t = Math.Min(lod - m, 1f);
        Vector3 a = Wrap(albedo[layer][m], 512 >> m, u, v);
        return m >= 9 ? a : Vector3.Lerp(a, Wrap(albedo[layer][m + 1], 512 >> (m + 1), u, v), t);
    }

    static Vector3 Wrap(Vector3[] level, int n, float u, float v)
    {
        float gx = u * n - .5f, gz = v * n - .5f;
        int x = (int)Math.Floor(gx), z = (int)Math.Floor(gz); float fx = gx - x, fz = gz - z;
        int x0 = ((x % n) + n) % n, z0 = ((z % n) + n) % n, x1 = (x0 + 1) % n, z1 = (z0 + 1) % n;
        return Vector3.Lerp(Vector3.Lerp(level[z0 * n + x0], level[z0 * n + x1], fx), Vector3.Lerp(level[z1 * n + x0], level[z1 * n + x1], fx), fz);
    }

    // ---- Pictures ----

    // One top-down window on one thread; the lattice caches fill on a coarse first pass.
    static Vector3[] Render(Vector2 centre, float mpp, Func<int, int, float> ready)
    {
        var picture = new Vector3[Width * Height];
        float x0 = centre.x - Width * .5f * mpp, z1 = centre.y + Height * .5f * mpp;
        for (int j = 0; j < Height; j += 8) for (int i = 0; i < Width; i += 8) Shade(new Vector2(x0 + (i + .5f) * mpp, z1 - (j + .5f) * mpp), mpp, ready);
        for (int j = 0; j < Height; j++)
            for (int i = 0; i < Width; i++)
                picture[j * Width + i] = Shade(new Vector2(x0 + (i + .5f) * mpp, z1 - (j + .5f) * mpp), mpp, ready);
        return picture;
    }

    // Binary PPM; run.py converts to JPEG.
    static void Save(string path, Vector3[] picture)
    {
        var rgb = new byte[Width * Height * 3];
        for (int k = 0; k < picture.Length; k++) { var c = FarDetailPalette.Encode(picture[k]); rgb[k * 3] = c.r; rgb[k * 3 + 1] = c.g; rgb[k * 3 + 2] = c.b; }
        using var stream = File.Create(path);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n255\n");
        stream.Write(header, 0, header.Length); stream.Write(rgb, 0, rgb.Length);
    }

    static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    static float SmoothStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
}

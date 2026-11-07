using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Top-down review of cultivated land: the sector exactly as built (the terrain splat, Farmland's field meshes with their
// tints and detail maps, FieldMargins and hedges) and the shared far painter (2 m tiles, 4 m regional pages decoded from
// BC1, the 13.7 m world picture) over the same windows, at the pixel sizes a 45 degree, 1080-line camera sees from
// 300 m and 600 m straight down. Albedo with a mild hillshade; one process per scene, never Unity.
static class TerrainColourReview
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    const int Width = 1920, Height = 1080;

    // Fail with a short diagnostic rather than a crash dialog when run unattended.
    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Arguments: owner settlement, output folder, optional "render".
    static int Run(string[] args)
    {
        int owner = args.Length > 0 ? int.Parse(args[0]) : 4;
        string output = args.Length > 1 ? args[1] : "Temp/terrain-colour/review";
        bool render = args.Length > 2 && args[2] == "render";
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        WorldPlan.Generate(1200);
        var data = WorldPlan.CaptureDetail(null);
        double capture = timer.Elapsed.TotalMilliseconds;
        var s = WorldPlan.Settlements[owner];
        var sector = WorldSectors.PlanAt(s.center, SectorKind.RiverValley, 3100 + owner);
        timer.Restart();
        MapLayout.Use(sector); MapLayout.Prepare();
        double layout = timer.Elapsed.TotalMilliseconds;
        var root = new GameObject("Review").transform;
        timer.Restart();
        Ground.Build(root);
        double ground = timer.Elapsed.TotalMilliseconds;
        float[,,] splat = Ground.Current.terrainData.alphamaps;
        // BuildSplat again on its own, best of two, for the splat's share of the sector build.
        var buildSplat = typeof(Ground).GetMethod("BuildSplat", BindingFlags.NonPublic | BindingFlags.Static);
        double splatMs = double.MaxValue;
        for (int i = 0; i < 2; i++)
        {
            GC.Collect(); timer.Restart(); buildSplat.Invoke(null, new object[] { Ground.Current.terrainData });
            splatMs = Math.Min(splatMs, timer.Elapsed.TotalMilliseconds);
        }
        GC.Collect();
        Art.placed.Clear(); MeshRenderer.All.Clear();
        timer.Restart();
        Farmland.Build(root, new System.Random(sector.worldSeed));
        double farmland = timer.Elapsed.TotalMilliseconds;
        double margins = double.MaxValue;
        var keep = new List<MeshRenderer>(MeshRenderer.All);
        for (int i = 0; i < 3; i++)
        {
            FieldMargins.Release(); MeshRenderer.All.Clear(); timer.Restart();
            FieldMargins.Build(root); margins = Math.Min(margins, timer.Elapsed.TotalMilliseconds);
        }
        // Keep one margin build: drop the renderers of the first and the repeats, then rebuild once.
        var renderers = new List<MeshRenderer>();
        foreach (var r in keep) if (r.gameObject.GetComponent<MeshFilter>().sharedMesh?.name != "Field use 128 m block") renderers.Add(r);
        renderers.AddRange(MeshRenderer.All);

        Vector2 origin = sector.world.center;
        var times = new Dictionary<string, double> { ["worldCaptureMs"] = capture, ["layoutMs"] = layout, ["groundBuildMs"] = ground,
            ["splatMs"] = splatMs, ["farmlandBuildMs"] = farmland, ["fieldMarginsMs"] = margins };
        times["tileMs"] = TileTime(data, origin);
        times["regionalPageMs"] = RegionalTime(data, origin);
        times["worldBlockMs"] = WorldBlockTime(data, origin);
        var stats = Stats(data, origin);
#if !BEFORE
        // The sector's own drainage signal under its crops (Ground.LowAt), and the shared damp share it gives.
        {
            var v = new List<double>(); int wet = 0;
            for (float z = -950f; z < 950f; z += 6f) for (float x = -950f; x < 950f; x += 6f)
            {
                var q = new Vector2(x, z);
                if (!MapLayout.InField(q, -3f)) continue;
                float low = Ground.LowAt(q); v.Add(low);
                if (FieldAppearance.Damp(x + origin.x, z + origin.y, low) > .5f) wet++;
            }
            v.Sort();
            Console.WriteLine($"sector crop low: p50 {v[v.Count / 2]:F3} p80 {v[(int)(v.Count * .8)]:F3} p90 {v[(int)(v.Count * .9)]:F3} p97 {v[(int)(v.Count * .97)]:F3} p99 {v[(int)(v.Count * .99)]:F3}; damp share {(double)wet / v.Count:P2}");
        }
#endif
        var result = new { owner, s.name, fields = MapLayout.Fields.Count, layoutTimings = MapLayout.FieldTimings, times, stats };
        Console.WriteLine(JsonSerializer.Serialize(result, Json));
        File.WriteAllText(Path.Combine(output, "sector-" + owner + ".json"), JsonSerializer.Serialize(result, Json));
        if (!render) return 0;

        // A field-rich window: the fields' area-weighted centre, nudged inside the terrain.
        Vector2 middle = FieldCentre();
        foreach (float altitude in new[] { 300f, 600f })
        {
            float mpp = 2f * altitude * (float)Math.Tan(Scale.CameraFov * .5f * Math.PI / 180.0) / Height;
            string tag = owner + "-" + (int)altitude + "m";
            Save(Path.Combine(output, "sector-" + tag + ".ppm"), SectorPicture(splat, renderers, middle, mpp));
            Save(Path.Combine(output, "tiles-" + tag + ".ppm"), FarPicture(data, middle + origin, mpp, 2f, 1f));
        }
        // The whole sector's width at about 1 m a pixel (from 1.3 km up), for staircases in field outlines and seams.
        float overview = Ground.Size / Width;
        Vector2 band = new Vector2(0f, Mathf.Clamp(middle.y, -Ground.Size * .5f + Height * overview * .5f, Ground.Size * .5f - Height * overview * .5f));
        Save(Path.Combine(output, "sector-" + owner + "-overview.ppm"), SectorPicture(splat, renderers, band, overview));
        Save(Path.Combine(output, "tiles-" + owner + "-overview.ppm"), FarPicture(data, band + origin, overview, 2f, 1f));
        // A meadow window: the 300 m view with the most open pasture in the sector.
        float near = 2f * 300f * (float)Math.Tan(Scale.CameraFov * .5f * Math.PI / 180.0) / Height;
        Vector2 meadow = MeadowCentre(data, origin);
        Save(Path.Combine(output, "sector-" + owner + "-meadow-300m.ppm"), SectorPicture(splat, renderers, meadow, near));
        Save(Path.Combine(output, "tiles-" + owner + "-meadow-300m.ppm"), FarPicture(data, meadow + origin, near, 2f, 1f));
        float wide = 2f * 600f * (float)Math.Tan(Scale.CameraFov * .5f * Math.PI / 180.0) / Height;
        Save(Path.Combine(output, "regional-" + owner + "-600m.ppm"), RegionalPicture(data, middle + origin, wide));
        Save(Path.Combine(output, "world-" + owner + "-600m.ppm"), FarPicture(data, middle + origin, wide, WorldPlan.Size / 4096f, .5f));
        return 0;
    }

    // ---- Sector, as built ----

    // The 600 m window with the most crop in it (fields sampled every 25 m on a 125 m search grid), so the pictures show
    // cultivated land rather than a village the harness does not build.
    static Vector2 FieldCentre()
    {
        float half = Ground.Size * .5f, w = 442f, h = 249f, best = -1f;
        Vector2 result = Vector2.zero;
        for (float cz = -half + h + 20f; cz <= half - h - 20f; cz += 125f)
            for (float cx = -half + w + 20f; cx <= half - w - 20f; cx += 125f)
            {
                int inside = 0;
                for (float z = cz - h; z <= cz + h; z += 25f)
                    for (float x = cx - w; x <= cx + w; x += 25f)
                        if (MapLayout.InField(new Vector2(x, z), 0f)) inside++;
                if (inside > best) { best = inside; result = new Vector2(cx, cz); }
            }
        return result;
    }

    // The 300 m window with the most open pasture (no crop, village or road) in the sector, on a 125 m search grid.
    static Vector2 MeadowCentre(FarDetailData data, Vector2 origin)
    {
        float half = Ground.Size * .5f, w = 221f, h = 125f, best = -1f;
        Vector2 result = Vector2.zero;
        for (float cz = -half + h + 20f; cz <= half - h - 20f; cz += 125f)
            for (float cx = -half + w + 20f; cx <= half - w - 20f; cx += 125f)
            {
                int open = 0;
                for (float z = cz - h; z <= cz + h; z += 25f)
                    for (float x = cx - w; x <= cx + w; x += 25f)
                    {
                        var q = new Vector2(x, z);
                        if (MapLayout.InField(q, 3f) || MapLayout.NearVillage(q, 20f) || data.roadNetwork.At(x + origin.x, z + origin.y).edge < 8f) continue;
                        var land = data.cover.At(x + origin.x, z + origin.y);
                        if (land.Weight(Cover.Pasture) + land.Weight(Cover.Farmland) >= 1f) open++;
                    }
                if (open > best) { best = open; result = new Vector2(cx, cz); }
            }
        return result;
    }

    struct Picture { public Vector3[] colour; public Vector2 centre; public float mpp; }

    // Terrain splat colour from the eight layers' linear means, then field meshes, margins and boundaries in their
    // draw order (their lifts above the ground), then a mild hillshade from the terrain's own heights.
    static Picture SectorPicture(float[,,] splat, List<MeshRenderer> renderers, Vector2 centre, float mpp)
    {
        var pic = new Picture { colour = new Vector3[Width * Height], centre = centre, mpp = mpp };
        int res = splat.GetLength(0);
        var means = new Vector3[8];
        for (int l = 0; l < 5; l++) means[l] = FarDetailPalette.Means[l];
        means[5] = Ground.LitterMean; means[6] = WaterAppearance.WetMud; means[7] = WaterAppearance.Gravel;
        for (int j = 0; j < Height; j++)
            for (int i = 0; i < Width; i++)
            {
                Vector2 p = Point(pic, i, j);
                float gx = (p.x + Ground.Size * .5f) / Ground.Size * res - .5f, gz = (p.y + Ground.Size * .5f) / Ground.Size * res - .5f;
                int x0 = Math.Max(0, Math.Min(res - 2, (int)Math.Floor(gx))), z0 = Math.Max(0, Math.Min(res - 2, (int)Math.Floor(gz)));
                float fx = Mathf.Clamp01(gx - x0), fz = Mathf.Clamp01(gz - z0);
                Vector3 c = Vector3.zero;
                for (int l = 0; l < splat.GetLength(2); l++)
                {
                    float w = (splat[z0, x0, l] * (1 - fx) + splat[z0, x0 + 1, l] * fx) * (1 - fz) + (splat[z0 + 1, x0, l] * (1 - fx) + splat[z0 + 1, x0 + 1, l] * fx) * fz;
                    c += means[l] * w;
                }
                pic.colour[j * Width + i] = c;
            }
        // Field surfaces first, then margins, then hedges and banks: their lifts above the terrain.
        foreach (int pass in new[] { 0, 1, 2 })
            foreach (var r in renderers)
            {
                var mesh = r.gameObject.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null) continue;
                bool field = mesh.name.StartsWith("Field ") && mesh.name != "Field use 128 m block", margin = mesh.name == "Field use 128 m block";
                if ((pass == 0) != field || (pass == 1) != margin || pass == 2 && (field || margin)) continue;
                if (field) DrawField(pic, mesh, r.sharedMaterial);
                else if (margin) { for (int sub = 0; sub < mesh.subs.Count; sub++) Draw(pic, mesh, mesh.subs[sub], Linear(r.sharedMaterials[sub].color)); }
                else if (mesh.name.StartsWith("Parcel")) Draw(pic, mesh, mesh.triangles, mesh.name == "Parcel Hedge" ? FarDetailPalette.Decode(new Color32(46, 62, 36, 255)) : Linear(r.sharedMaterial.color));
            }
        Shade(pic);
        return pic;
    }

    // The crop's linear mean times the material tint, with HDRP Lit's detail-map overlay from the field's own map.
    static void DrawField(Picture pic, Mesh mesh, Material material)
    {
        var crop = (Crop)Enum.Parse(typeof(Crop), mesh.name.Substring(6));
        Vector3 baseColour = Mul(FarDetailPalette.Means[5 + (int)crop], Linear(material.color));
        var detail = material.textures.TryGetValue("_DetailMap", out var t) ? t : null;
        material.scales.TryGetValue("_DetailMap", out var scale); material.offsets.TryGetValue("_DetailMap", out var offset);
        Raster(pic, mesh.vertices, mesh.triangles, (a, b, c, u, v, w) =>
        {
            if (detail == null || detail.data == null) return baseColour;
            Vector2 uv = mesh.uv[a] * u + mesh.uv[b] * v + mesh.uv[c] * w;
            float r = Sample(detail, uv.x * scale.x + offset.x, uv.y * scale.y + offset.y);
            float d = r * 2f - 1f, speed = Mathf.Clamp01(Math.Abs(d));
            Vector3 overlay = new Vector3((float)Math.Sqrt(baseColour.x), (float)Math.Sqrt(baseColour.y), (float)Math.Sqrt(baseColour.z));
            Vector3 target = d < 0f ? Vector3.zero : Vector3.one;
            overlay = overlay + (target - overlay) * (speed * speed);
            return new Vector3(Math.Min(1f, overlay.x * overlay.x), Math.Min(1f, overlay.y * overlay.y), Math.Min(1f, overlay.z * overlay.z));
        });
    }

    // A flat-coloured part of a mesh.
    static void Draw(Picture pic, Mesh mesh, int[] triangles, Vector3 colour) => Raster(pic, mesh.vertices, triangles, (a, b, c, u, v, w) => colour);

    // Barycentric top-down fill of every triangle touching the window, one sample per pixel.
    static void Raster(Picture pic, Vector3[] vertices, int[] triangles, Func<int, int, int, float, float, float, Vector3> shade)
    {
        float x0 = pic.centre.x - Width * .5f * pic.mpp, z1 = pic.centre.y + Height * .5f * pic.mpp;
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            float ax = (vertices[a].x - x0) / pic.mpp, ay = (z1 - vertices[a].z) / pic.mpp, bx = (vertices[b].x - x0) / pic.mpp, by = (z1 - vertices[b].z) / pic.mpp;
            float cx = (vertices[c].x - x0) / pic.mpp, cy = (z1 - vertices[c].z) / pic.mpp;
            int i0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), i1 = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            int j0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), j1 = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
            if (i0 > i1 || j0 > j1) continue;
            float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
            if (Math.Abs(area) < 1e-6f) continue;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float px = i + .5f, py = j + .5f;
                    float w0 = ((bx - px) * (cy - py) - (cx - px) * (by - py)) / area, w1 = ((cx - px) * (ay - py) - (ax - px) * (cy - py)) / area, w2 = 1f - w0 - w1;
                    if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                    pic.colour[j * Width + i] = shade(a, b, c, w0, w1, w2);
                }
        }
    }

    // Bilinear sample of an R8 detail map with clamped addressing, as a 0-1 value.
    static float Sample(Texture2D t, float u, float v)
    {
        float gx = Mathf.Clamp(u * t.width - .5f, 0f, t.width - 1.001f), gz = Mathf.Clamp(v * t.height - .5f, 0f, t.height - 1.001f);
        int x = (int)gx, z = (int)gz; float fx = gx - x, fz = gz - z;
        int k = z * t.width + x, x1 = Math.Min(t.width - 1, x + 1) - x, z1 = (Math.Min(t.height - 1, z + 1) - z) * t.width;
        return ((t.data[k] * (1 - fx) + t.data[k + x1] * fx) * (1 - fz) + (t.data[k + z1] * (1 - fx) + t.data[k + z1 + x1] * fx) * fz) / 255f;
    }

    // A gentle low sun from the south-east over the sector terrain, flat ground unchanged.
    static void Shade(Picture pic)
    {
        Vector3 sun = new Vector3(.45f, .8f, -.4f).normalized;
        for (int j = 0; j < Height; j += 1)
            for (int i = 0; i < Width; i++)
            {
                Vector2 p = Point(pic, i, j);
                float dx = Ground.HeightAt(new Vector3(p.x + 2f, 0f, p.y)) - Ground.HeightAt(new Vector3(p.x - 2f, 0f, p.y));
                float dz = Ground.HeightAt(new Vector3(p.x, 0f, p.y + 2f)) - Ground.HeightAt(new Vector3(p.x, 0f, p.y - 2f));
                Vector3 n = new Vector3(-dx, 4f, -dz).normalized;
                float lit = Vector3.Dot(n, sun) / sun.y;
                pic.colour[j * Width + i] *= 1f + .5f * (lit - 1f);
            }
    }

    // ---- The far painter ----

    // Paint the painter's own texel lattice at a footprint, then sample it bilinearly in linear light as the GPU does.
    // `phase` places the lattice: texel centres at odd metres for tiles, half a world-picture pixel for the picture.
    static Picture FarPicture(FarDetailData data, Vector2 centre, float mpp, float texel, float phase)
    {
        var pic = new Picture { colour = new Vector3[Width * Height], centre = centre, mpp = mpp };
        float x0 = centre.x - Width * .5f * mpp - 2f * texel, z0 = centre.y - Height * .5f * mpp - 2f * texel;
        float originX = texel == 2f ? 1f : -WorldPlan.Half + phase * texel, originZ = originX;
        int gx0 = (int)Math.Floor((x0 - originX) / texel), gz0 = (int)Math.Floor((z0 - originZ) / texel);
        int nx = (int)Math.Ceiling(Width * mpp / texel) + 6, nz = (int)Math.Ceiling(Height * mpp / texel) + 6;
        var grid = new Vector3[nx * nz];
        System.Threading.Tasks.Parallel.For(0, nz, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 2 }, j =>
        {
            for (int i = 0; i < nx; i++)
            {
                float x = originX + (gx0 + i) * texel, z = originZ + (gz0 + j) * texel;
                grid[j * nx + i] = FarDetailPalette.Decode(data.Paint(x, z, new Vector2(x, z), null, null, texel));
            }
        });
        Resample(pic, grid, nx, nz, originX + gx0 * texel, originZ + gz0 * texel, texel);
        return pic;
    }

    // Regional pages exactly as uploaded: built by the worker, BC1-encoded, decoded at mip 0.
    static Picture RegionalPicture(FarDetailData data, Vector2 centre, float mpp)
    {
        var pic = new Picture { colour = new Vector3[Width * Height], centre = centre, mpp = mpp };
        int n = FarDetailRegionData.Resolution;
        var pages = new Dictionary<long, Color32[]>();
        for (int j = 0; j < Height; j++)
            for (int i = 0; i < Width; i++)
            {
                Vector2 p = Point(pic, i, j);
                int px = (int)Math.Floor(p.x / 2048f), pz = (int)Math.Floor(p.y / 2048f);
                long key = ((long)px << 32) ^ (uint)pz;
                if (!pages.TryGetValue(key, out var pixels))
                    pages[key] = pixels = FarDetailRegionData.Decode(FarDetailRegionData.Build(data, px, pz).mips[0], n);
                float gx = (p.x - px * 2048f) / 4f + FarDetailRegionData.Gutter - .5f, gz = (p.y - pz * 2048f) / 4f + FarDetailRegionData.Gutter - .5f;
                int x = (int)Math.Floor(gx), z = (int)Math.Floor(gz); float fx = gx - x, fz = gz - z;
                Vector3 A = FarDetailPalette.Decode(pixels[z * n + x]), B = FarDetailPalette.Decode(pixels[z * n + x + 1]);
                Vector3 C = FarDetailPalette.Decode(pixels[(z + 1) * n + x]), D = FarDetailPalette.Decode(pixels[(z + 1) * n + x + 1]);
                pic.colour[j * Width + i] = (A * (1 - fx) + B * fx) * (1 - fz) + (C * (1 - fx) + D * fx) * fz;
            }
        return pic;
    }

    // Bilinear resampling of a painted lattice into the window.
    static void Resample(Picture pic, Vector3[] grid, int nx, int nz, float gx0, float gz0, float texel)
    {
        for (int j = 0; j < Height; j++)
            for (int i = 0; i < Width; i++)
            {
                Vector2 p = Point(pic, i, j);
                float gx = (p.x - gx0) / texel, gz = (p.y - gz0) / texel;
                int x = Math.Max(0, Math.Min(nx - 2, (int)Math.Floor(gx))), z = Math.Max(0, Math.Min(nz - 2, (int)Math.Floor(gz)));
                float fx = Mathf.Clamp01(gx - x), fz = Mathf.Clamp01(gz - z);
                pic.colour[j * Width + i] = (grid[z * nx + x] * (1 - fx) + grid[z * nx + x + 1] * fx) * (1 - fz) + (grid[(z + 1) * nx + x] * (1 - fx) + grid[(z + 1) * nx + x + 1] * fx) * fz;
            }
    }

    // ---- Timings and statistics ----

    // Best of three full 512 m tile builds beside the sector (mesh, collider, 258-square colour and control maps, mips),
    // over coarse chunk heights sampled from the world grid as FarTerrain's LOD0 would hold them.
    static double TileTime(FarDetailData world, Vector2 origin)
    {
        var coarse = new float[14 * 14][];
        for (int c = 0; c < coarse.Length; c++)
        {
            coarse[c] = new float[65 * 65];
            for (int j = 0; j < 65; j++) for (int i = 0; i < 65; i++)
                coarse[c][j * 65 + i] = world.Height(-WorldPlan.Half + (c % 14) * 4000f + i * 62.5f, -WorldPlan.Half + (c / 14) * 4000f + j * 62.5f);
        }
        var data = new FarDetailData(world, coarse);
        var roughness = new float[FarDetailData.Samples * FarDetailData.Samples];
        int tx = (int)Math.Floor((origin.x + Ground.Size * .5f + 100f) / FarDetailData.Size), tz = (int)Math.Floor(origin.y / FarDetailData.Size);
        double best = double.MaxValue; FarDetailData.Tile reuse = null;
        for (int k = 0; k < 3; k++) { reuse = data.Build(tx + k, tz, roughness, null, reuse); best = Math.Min(best, reuse.generationMs); }
        return best;
    }

    // One 2048 m regional page through the production worker path, including BC1 encoding.
    static double RegionalTime(FarDetailData data, Vector2 origin) =>
        FarDetailRegionData.Build(data, (int)Math.Floor(origin.x / 2048f) + 1, (int)Math.Floor(origin.y / 2048f)).milliseconds;

    // A 256-square block of the world picture at its own 13.7 m footprint, on one thread.
    static double WorldBlockTime(FarDetailData data, Vector2 origin)
    {
        float pixel = WorldPlan.Size / 4096f;
        var timer = Stopwatch.StartNew();
        for (int j = 0; j < 256; j++) for (int i = 0; i < 256; i++)
        {
            float x = origin.x + (i - 128 + .5f) * pixel, z = origin.y + 1800f + (j - 128 + .5f) * pixel;
            data.Paint(x, z, new Vector2(x, z), null, null, pixel);
        }
        return timer.Elapsed.TotalMilliseconds;
    }

    // Colour statistics over a 3 km square round the sector at the tiles' 2 m footprint: crop, margin and meadow means,
    // the spread between whole fields, the meadow's 12-35 m variation, and how much crop lies in damp hollows.
    static object Stats(FarDetailData data, Vector2 origin)
    {
        var groups = new Dictionary<string, (double r, double g, double b, int n)>();
        var fieldMeans = new Dictionary<int, List<Vector3>>();
        var meadow = new List<float>();
        var lows = new List<double>();
        int crop = 0, damp = 0;
        void Add(string k, Color32 c) { groups.TryGetValue(k, out var v); groups[k] = (v.r + c.r, v.g + c.g, v.b + c.b, v.n + 1); }
        for (int j = 0; j < 750; j++)
            for (int i = 0; i < 750; i++)
            {
                float x = origin.x - 1500f + i * 4f + 1f, z = origin.y - 1500f + j * 4f + 1f;
                var land = data.cover.At(x, z);
                if (land.Weight(Cover.Farmland) < 1f && land.Weight(Cover.Pasture) < 1f) continue;
                if (data.villages.At(new Vector2(x, z), out _) || data.roadNetwork.At(x, z).edge < 6f) continue;
                Color32 c = data.Paint(x, z, new Vector2(x, z), null, null, 2f);
                if (land.Weight(Cover.Pasture) >= 1f) { Add("pasture", c); meadow.Add(Luma(c)); continue; }
                var f = data.FieldAt(x, z);
                if (f.layer < 5 || f.side < 0f || f.end < 0f) { Add("between", c); continue; }
                if (f.side < f.sideMargin || f.end < f.endMargin)
                {
                    int use = f.side - f.sideMargin < f.end - f.endMargin ? f.sideUse : f.endUse;
                    Add("margin-" + (FieldEdge)use, c); continue;
                }
                if (f.use > 2) continue;
                Add("crop-" + (Crop)(f.layer - 5), c); crop++;
#if !BEFORE
                float low = data.Low(x, z); lows.Add(low);
                if (FieldAppearance.Damp(x, z, low) > .5f) damp++;
#endif
                int key = f.layer * 1000003 + (int)Math.Round(f.length * 10) * 31 + (int)Math.Round(f.width);
                if (!fieldMeans.TryGetValue(key, out var list)) fieldMeans[key] = list = new List<Vector3>();
                list.Add(new Vector3(c.r, c.g, c.b));
            }
        var means = new SortedDictionary<string, object>();
        foreach (var kv in groups) means[kv.Key] = new { r = Math.Round(kv.Value.r / kv.Value.n, 1), g = Math.Round(kv.Value.g / kv.Value.n, 1), b = Math.Round(kv.Value.b / kv.Value.n, 1), samples = kv.Value.n };
        // Spread of whole-field mean luma per crop: how different neighbouring fields of one crop look.
        var spread = new SortedDictionary<string, double>();
        var perCrop = new Dictionary<int, List<double>>();
        foreach (var kv in fieldMeans)
        {
            if (kv.Value.Count < 200) continue;
            Vector3 m = Vector3.zero; foreach (var v in kv.Value) m += v; m /= kv.Value.Count;
            int layer = kv.Key / 1000003;
            if (!perCrop.TryGetValue(layer, out var l)) perCrop[layer] = l = new List<double>();
            l.Add(.2126 * m.x + .7152 * m.y + .0722 * m.z);
        }
        foreach (var kv in perCrop) spread["crop-" + (Crop)(kv.Key - 5)] = Math.Round(Std(kv.Value), 2);
        lows.Sort();
        double Percentile(double q) => lows.Count == 0 ? 0 : Math.Round(lows[Math.Min(lows.Count - 1, (int)(q * lows.Count))], 3);
        return new { means, fieldLumaSpread = spread, meadow = Meadow(data, origin), dampCropShare = crop > 0 ? Math.Round((double)damp / crop, 4) : 0,
            cropLowPercentiles = new { p50 = Percentile(.5), p90 = Percentile(.9), p97 = Percentile(.97), p99 = Percentile(.99) } };
    }

    // The most open pasture square (1 km) within 6 km: its luma spread at 2 m in all, and below 64 m (the grazing and
    // growth drifts) after taking off a 64 m running mean.
    static object Meadow(FarDetailData data, Vector2 origin)
    {
        Vector2 best = origin; int most = -1;
        for (float z = -6000f; z <= 6000f; z += 1000f) for (float x = -6000f; x <= 6000f; x += 1000f)
        {
            int n = 0;
            for (int j = 0; j < 10; j++) for (int i = 0; i < 10; i++)
                if (data.cover.At(origin.x + x + i * 100f - 450f, origin.y + z + j * 100f - 450f).Weight(Cover.Pasture) >= 1f) n++;
            if (n > most) { most = n; best = origin + new Vector2(x, z); }
        }
        const int N = 250; var luma = new float[N * N]; var open = new bool[N * N];
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
        {
            float x = best.x - 500f + i * 4f + 1f, z = best.y - 500f + j * 4f + 1f;
            open[j * N + i] = data.cover.At(x, z).Weight(Cover.Pasture) >= 1f && !data.villages.At(new Vector2(x, z), out _) && data.roadNetwork.At(x, z).edge > 6f;
            luma[j * N + i] = Luma(data.Paint(x, z, new Vector2(x, z), null, null, 2f));
        }
        var all = new List<double>(); var fine = new List<double>();
        for (int j = 8; j < N - 8; j++) for (int i = 8; i < N - 8; i++)
        {
            if (!open[j * N + i]) continue;
            double sum = 0; int n = 0;
            for (int b = -8; b <= 8; b += 2) for (int a = -8; a <= 8; a += 2) { sum += luma[(j + b) * N + i + a]; n++; }
            all.Add(luma[j * N + i]); fine.Add(luma[j * N + i] - sum / n);
        }
        return new { openShare = Math.Round(most / 100.0, 2), samples = all.Count, lumaStd = Math.Round(Std(all), 2), below64mStd = Math.Round(Std(fine), 2) };
    }

    // ---- Helpers ----

    static Vector2 Point(Picture pic, int i, int j) => new Vector2(pic.centre.x + (i + .5f - Width * .5f) * pic.mpp, pic.centre.y + (Height * .5f - j - .5f) * pic.mpp);
    static Vector3 Linear(Color c) => new Vector3(FarDetailPalette.Linear(c.r), FarDetailPalette.Linear(c.g), FarDetailPalette.Linear(c.b));
    static Vector3 Mul(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
    static float Luma(Color32 c) => .2126f * c.r + .7152f * c.g + .0722f * c.b;
    static double Std(List<double> v)
    {
        if (v.Count < 2) return 0;
        double m = 0; foreach (var x in v) m += x; m /= v.Count;
        double s = 0; foreach (var x in v) s += (x - m) * (x - m);
        return Math.Sqrt(s / (v.Count - 1));
    }

    // Binary PPM keeps the harness free of image libraries; run.py converts to PNG.
    static void Save(string path, Picture pic)
    {
        var rgb = new byte[Width * Height * 3];
        for (int k = 0; k < pic.colour.Length; k++) { var c = FarDetailPalette.Encode(pic.colour[k]); rgb[k * 3] = c.r; rgb[k * 3 + 1] = c.g; rgb[k * 3 + 2] = c.b; }
        using var stream = File.Create(path);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n255\n");
        stream.Write(header, 0, header.Length); stream.Write(rgb, 0, rgb.Length);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DroneSandbox;
using UnityEngine;

// Real production tile generation, clipping and ray traversal run without importing a Unity Library.
static class Harness
{
    static int checks;
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };

    // Compare the exact production world painter and detail painter with the same managed native-noise substitute.
    static int Main(string[] args)
    {
        try
        {
            string output = args[0]; Directory.CreateDirectory(output);
            var timer = Stopwatch.StartNew(); WorldPlan.Generate(1200);
            Console.WriteLine($"World seed 1200: {timer.Elapsed.TotalSeconds:F2} s");
            timer.Restart(); FarPicture.Build(); Console.WriteLine($"Production world picture: {timer.Elapsed.TotalSeconds:F2} s");
            var coarse = Coarse(); var plan = WorldPlan.CaptureDetail(coarse);
            var spots = Spots(); var metrics = new List<object>();
            FarDetailData.Tile last = null;
            foreach (var spot in spots)
            {
                int x = (int)Math.Floor(spot.position.x / 512f), z = (int)Math.Floor(spot.position.y / 512f);
                var rough = Rough(x, z); var cover = CoverPositions(x, z);
                var times = new List<double>();
                FarDetailData.Tile tile = null;
                for (int repeat = 0; repeat < 5; repeat++)
                {
                    tile = Task.Run(() => plan.Build(x, z, rough, cover)).GetAwaiter().GetResult();
                    if (repeat > 0) times.Add(tile.generationMs);
                }
                times.Sort();
                InspectTile(tile, plan);
                var east = plan.Build(x + 1, z, Rough(x + 1, z), CoverPositions(x + 1, z)); var north = plan.Build(x, z + 1, Rough(x, z + 1), CoverPositions(x, z + 1));
                float edgeError = 0;
                for (int k = 0; k < 129; k++)
                {
                    edgeError = Math.Max(edgeError, Math.Abs(tile.vertices[k * 129 + 128].y - east.vertices[k * 129].y));
                    edgeError = Math.Max(edgeError, Math.Abs(tile.vertices[128 * 129 + k].y - north.vertices[k].y));
                }
                Require(edgeError < 0.001f, "Neighbours agree at every edge sample");
                CheckRays(tile);
                var clipped = CheckClip(tile, plan);
                WritePpm(Path.Combine(output, spot.name + "-detail.ppm"), tile.pixels);
                var far = CropPicture(x, z); WritePpm(Path.Combine(output, spot.name + "-far.ppm"), far);
                long arrays = tile.vertices.Length * 32L + tile.triangles.Length * 4L + tile.pixels.Length * 4L + tile.collision.Length * 12L + tile.collisionTriangles.Length * 4L;
                long gpu = tile.vertices.Length * 32L + tile.triangles.Length * 2L + 256L * 256 * 4 * 4 / 3;
                metrics.Add(new
                {
                    name = spot.name,
                    tileX = x,
                    tileZ = z,
                    generationMedianMs = times[1],
                    generationMaxMs = times[3],
                    edgeErrorMetres = edgeError,
                    vertexCount = tile.vertices.Length,
                    triangles = tile.triangles.Length / 3,
                    managedArrayBytes = arrays,
                    gpuPayloadBytes = gpu,
                    clipping = clipped
                });
                Console.WriteLine($"{spot.name}: generation {times[1]:F2} ms median, {times[3]:F2} ms max; arrays {arrays / 1048576.0:F2} MiB");
                last = tile;
            }
            // A tile exactly on x=0 touches two 4 km chunks; the outside-only neighbour must stitch too.
            CheckClip(new FarDetailData.Tile { x = 0, z = 13 }, plan, -1);
            var streaming = StreamChecks.Run(coarse);
            // Seed-independent cache safety: a worker snapshot must survive the static plan being regenerated.
            var before = plan.Build(last.x, last.z, Rough(last.x, last.z), CoverPositions(last.x, last.z));
            var concurrent = Task.Run(() => plan.Build(last.x, last.z, Rough(last.x, last.z), CoverPositions(last.x, last.z)));
            WorldPlan.Generate(1201);
            var after = concurrent.GetAwaiter().GetResult();
            for (int i = 0; i < before.vertices.Length; i++) Require(before.vertices[i].y == after.vertices[i].y, "World-reset snapshot height stability");
            for (int i = 0; i < before.pixels.Length; i++) Require(before.pixels[i].r == after.pixels[i].r && before.pixels[i].g == after.pixels[i].g && before.pixels[i].b == after.pixels[i].b, "World-reset snapshot colour stability");
            File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(new
            {
                checks,
                seed = 1200,
                tiles = metrics,
                streaming,
                caveat = "Managed reference Perlin replaces Unity native Perlin in both painters. Runtime Unity sampling uses native Perlin on the main thread. No GPU, upload, collider-cook or frame-time claim is made by this harness."
            }, Json));
            Console.WriteLine($"PASS: {checks} invariant checks. GPU/frame-time gates require Claude's Unity player.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Sample the same 62.5 m macro grid, including canopy, used by FarTerrain's LOD0 meshes.
    static float[][] Coarse()
    {
        var grid = new float[196][];
        for (int z = 0; z < 14; z++) for (int x = 0; x < 14; x++)
            {
                var h = grid[z * 14 + x] = new float[65 * 65];
                for (int j = 0; j < 65; j++) for (int i = 0; i < 65; i++) h[j * 65 + i] = FarPicture.Elevation(-28000 + x * 4000 + i * 62.5f, -28000 + z * 4000 + j * 62.5f);
            }
        return grid;
    }

    // Use four concrete examples chosen from actual seed-1200 cover and exported bridges/settlements.
    static List<(string name, Vector2 position)> Spots()
    {
        Vector2 farmland = default, forest = default;
        float farmScore = float.MinValue, forestScore = float.MinValue;
        for (int z = -40; z < 40; z++) for (int x = -40; x < 40; x++)
            {
                int farm = 0, wood = 0, other = 0;
                for (int j = 1; j < 4; j++) for (int i = 1; i < 4; i++)
                    {
                        var c = WorldPlan.CoverAt(x * 512 + i * 128, z * 512 + j * 128);
                        if (c == Cover.Farmland) farm++; if (c == Cover.Forest) wood++; if (c == Cover.Pasture || c == Cover.Farmland) other++;
                    }
                float fs = farm * 100 - Math.Abs(x) - Math.Abs(z);
                if (fs > farmScore) { farmScore = fs; farmland = new Vector2(x * 512 + 256, z * 512 + 256); }
                float ws = Math.Min(wood, other) * 100 - Math.Abs(x) - Math.Abs(z);
                if (ws > forestScore) { forestScore = ws; forest = new Vector2(x * 512 + 256, z * 512 + 256); }
            }
        Vector2 village = default, crossing = default;
        foreach (var town in WorldPlan.Settlements) if (!town.town) { village = town.center; break; }
        float best = float.MaxValue;
        foreach (var bridge in WorldPlan.Bridges)
        {
            if (bridge.railway) continue;
            float u = bridge.position.x - (float)Math.Floor(bridge.position.x / 512f) * 512f;
            float v = bridge.position.y - (float)Math.Floor(bridge.position.y / 512f) * 512f;
            float d = Math.Abs(u - 256) + Math.Abs(v - 256);
            if (d < best) { best = d; crossing = bridge.position; }
        }
        return new List<(string, Vector2)> { ("farmland", farmland), ("village", village), ("river-crossing", crossing), ("forest-edge", forest) };
    }

    // The test substitutes noise only; production captures these samples incrementally on the main thread.
    static float[] Rough(int x, int z)
    {
        var r = new float[129 * 129];
        for (int j = 0; j < 129; j++) for (int i = 0; i < 129; i++) r[j * 129 + i] = FarDetailData.Roughness(x * 512 + i * 4, z * 512 + j * 4);
        return r;
    }

    // Mirror the main-thread cover sampler; both world/detail painters receive the same reference noise here.
    static Vector2[] CoverPositions(int x, int z)
    {
        var p = new Vector2[256 * 256];
        for (int j = 0; j < 256; j++) for (int i = 0; i < 256; i++)
            {
                float wx = x * 512f + (i + 0.5f) * 2f, wz = z * 512f + (j + 0.5f) * 2f;
                p[j * 256 + i] = new Vector2(wx + 44f * (Mathf.PerlinNoise(wx / 70f, wz / 70f + 31f) - 0.5f), wz + 44f * (Mathf.PerlinNoise(wx / 70f + 57f, wz / 70f) - 0.5f));
            }
        return p;
    }

    // Index validity, winding, the exact core height formula and native-warp table agreement are independent invariants.
    static void InspectTile(FarDetailData.Tile tile, FarDetailData plan)
    {
        foreach (int i in tile.triangles) Require(i >= 0 && i < tile.vertices.Length, "Mesh indices are valid");
        for (int k = 0; k < 128 * 128 * 6; k += 3)
        {
            var a = tile.vertices[tile.triangles[k]]; var b = tile.vertices[tile.triangles[k + 1]]; var c = tile.vertices[tile.triangles[k + 2]];
            Require(Vector3.Cross(b - a, c - a).y > 0, "Surface triangles face upwards");
        }
        for (int j = 32; j <= 96; j++) for (int i = 32; i <= 96; i++)
            {
                float x = tile.x * 512 + i * 4, z = tile.z * 512 + j * 4;
                float expected = WorldPlan.Height(x, z) + (plan.Wet(x, z) ? 0 : FarDetailData.Roughness(x, z));
                Require(Math.Abs(expected - tile.vertices[j * 129 + i].y) < 0.001f, "Tile core matches WorldPlan plus Ground roughness");
            }
        int mismatches = 0;
        for (int j = 0; j < 128; j++) for (int i = 0; i < 128; i++)
            {
                float x = tile.x * 512 + i * 4 + 1, z = tile.z * 512 + j * 4 + 1;
                var exact = FarPicture.ExactField(x, z); var sampled = plan.Field(x, z);
                if (exact.r != sampled.r || exact.g != sampled.g || exact.b != sampled.b) mismatches++;
            }
        Require(mismatches < 164, "Native-warp table matches at least 99% of exact field pixels");
    }

    // Vertical and slanted rays meet rendered triangles; grazing, negative coordinates and upward misses are included.
    static void CheckRays(FarDetailData.Tile tile)
    {
        var rng = new Random(73);
        for (int n = 0; n < 300; n++)
        {
            float x = (float)rng.NextDouble() * 511.9f, z = (float)rng.NextDouble() * 511.9f;
            int i = (int)(x / 4), j = (int)(z / 4), k = j * 129 + i;
            var v = tile.vertices;
            float h = FarDetailData.Triangle(v[k].y, v[k + 1].y, v[k + 129].y, v[k + 130].y, x / 4 - i, z / 4 - j);
            var hitAt = new Vector3(tile.x * 512 + x, h, tile.z * 512 + z);
            var down = new Ray(hitAt + Vector3.up * 300f, Vector3.down);
            Require(FarDetailRay.Hit(tile, Vector2.zero, down, out float distance) && Math.Abs(distance - 300f) < 0.02f, "Vertical ray hits visible triangle");
            var start = hitAt + new Vector3(-70f, 180f, 90f); var oblique = new Ray(start, hitAt - start);
            Require(FarDetailRay.Hit(tile, Vector2.zero, oblique, out distance) && Vector3.Distance(oblique.GetPoint(distance), hitAt) < 0.04f, "Oblique ray stays on its ray and hits visible triangle");
            Require(!FarDetailRay.Hit(tile, Vector2.zero, new Ray(hitAt + Vector3.up * 300f, Vector3.up), out _), "Upward ray cannot hit terrain below it");
        }
    }

    // Subtract two adjacent tiles and measure triangle area; no triangle centroid may remain under either tile.
    static object CheckClip(FarDetailData.Tile tile, FarDetailData plan, int chunkOffset = 0)
    {
        int cx = (int)Math.Floor((tile.x * 512f + 28000f) / 4000f) + chunkOffset, cz = (int)Math.Floor((tile.z * 512f + 28000f) / 4000f);
        var corner = new Vector2(-28000 + cx * 4000, -28000 + cz * 4000);
        var vertices = new Vector3[65 * 65]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length]; var indices = new List<int>();
        for (int j = 0; j < 65; j++) for (int i = 0; i < 65; i++)
            {
                int k = j * 65 + i; vertices[k] = new Vector3(i * 62.5f, plan.coarse[cz * 14 + cx][k], j * 62.5f); normals[k] = Vector3.up;
                uv[k] = new Vector2((corner.x + i * 62.5f + 28000) / 56000, (corner.y + j * 62.5f + 28000) / 56000);
                if (i == 64 || j == 64) continue;
                indices.Add(k); indices.Add(k + 65); indices.Add(k + 1); indices.Add(k + 1); indices.Add(k + 65); indices.Add(k + 66);
            }
        var source = new FarDetailChunks.Geometry { vertices = vertices, normals = normals, uv = uv, triangles = indices.ToArray() };
        var areas = new[] { new FarDetailChunks.Area { x = tile.x, z = tile.z }, new FarDetailChunks.Area { x = tile.x + 1, z = tile.z } };
        var clock = Stopwatch.StartNew(); var cut = FarDetailChunks.Cut(source, corner, areas, plan); double ms = clock.Elapsed.TotalMilliseconds;
        double area = 0; float maxBoundaryError = 0;
        for (int t = 0; t < cut.triangles.Length; t += 3)
        {
            var a = cut.vertices[cut.triangles[t]]; var b = cut.vertices[cut.triangles[t + 1]]; var c = cut.vertices[cut.triangles[t + 2]];
            area += Math.Abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) / 2.0;
            float x = corner.x + (a.x + b.x + c.x) / 3, z = corner.y + (a.z + b.z + c.z) / 3;
            foreach (var r in areas) Require(!(x > r.Left + 0.002f && x < r.Left + 511.998f && z > r.Bottom + 0.002f && z < r.Bottom + 511.998f), "No coarse surface remains under detail tiles");
        }
        var used = new HashSet<int>(cut.triangles);
        foreach (int index in used) foreach (var r in areas)
            {
                var v = cut.vertices[index];
                float x = v.x + corner.x, z = v.z + corner.y;
                bool vertical = (Math.Abs(x - r.Left) < 0.002f || Math.Abs(x - r.Left - 512f) < 0.002f) && z >= r.Bottom && z <= r.Bottom + 512f;
                bool horizontal = (Math.Abs(z - r.Bottom) < 0.002f || Math.Abs(z - r.Bottom - 512f) < 0.002f) && x >= r.Left && x <= r.Left + 512f;
                if (!vertical && !horizontal) continue;
                float at = vertical ? z : x, lo = (float)Math.Floor(at / 4) * 4;
                float h = FarDetailData.Mix(plan.CoarseHeight(vertical ? x : lo, vertical ? lo : z), plan.CoarseHeight(vertical ? x : lo + 4, vertical ? lo + 4 : z), (at - lo) / 4);
                maxBoundaryError = Math.Max(maxBoundaryError, Math.Abs(v.y - h));
            }
        double expected = 16000000;
        foreach (var r in areas) expected -= Math.Max(0, Math.Min(corner.x + 4000, r.Left + 512) - Math.Max(corner.x, r.Left)) * Math.Max(0, Math.Min(corner.y + 4000, r.Bottom + 512) - Math.Max(corner.y, r.Bottom));
        Require(Math.Abs(area - expected) < 3.0, "Clipping preserves all outside area exactly");
        Require(maxBoundaryError < 0.25f, "Coarse cut edges match the tile within 0.25 m");
        return new { milliseconds = ms, vertices = cut.vertices.Length, triangles = cut.triangles.Length / 3, boundaryErrorMetres = maxBoundaryError, areaError = Math.Abs(area - expected) };
    }

    // Bilinear sampling of the actual 4096-pixel image shows the same footprint at its real macro resolution.
    static Color32[] CropPicture(int x, int z)
    {
        var pixels = new Color32[256 * 256]; var world = FarPicture.WorldTexture.pixels;
        for (int j = 0; j < 256; j++) for (int i = 0; i < 256; i++)
            {
                float u = (x * 512 + (i + 0.5f) * 2 + 28000) / 56000f * 4096 - 0.5f, v = (z * 512 + (j + 0.5f) * 2 + 28000) / 56000f * 4096 - 0.5f;
                int a = (int)Math.Floor(u), b = (int)Math.Floor(v);
                pixels[j * 256 + i] = Color32.Lerp(Color32.Lerp(world[b * 4096 + a], world[b * 4096 + a + 1], u - a), Color32.Lerp(world[(b + 1) * 4096 + a], world[(b + 1) * 4096 + a + 1], u - a), v - b);
            }
        return pixels;
    }

    // PPM keeps the harness free of image packages; Python adds labels and exports compact PNG review artifacts.
    static void WritePpm(string path, Color32[] pixels)
    {
        using (var stream = File.Create(path))
        {
            byte[] header = System.Text.Encoding.ASCII.GetBytes("P6\n256 256\n255\n"); stream.Write(header, 0, header.Length);
            for (int j = 255; j >= 0; j--) for (int i = 0; i < 256; i++) { var c = pixels[j * 256 + i]; stream.WriteByte(c.r); stream.WriteByte(c.g); stream.WriteByte(c.b); }
        }
    }

    // Fail the harness immediately with the invariant that needs attention.
    static void Require(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
}

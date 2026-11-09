using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Replays production canopy blocks around the 88_world_forest_hlod target and audits the triangles they emit.
static class Harness
{
    // One canopy level as Update/AdoptSector would request it; the focus is a world-space camera position.
    struct Level
    {
        public string name; public int step; public bool adaptive; public Vector3 focus;
    }

    // Keep errors visible to the serial driver without opening Windows crash dialogs.
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }

    static readonly StringBuilder log = new StringBuilder();
    static void Log(string line) { Console.WriteLine(line); log.AppendLine(line); }

    // Every level of three mutually adjacent blocks, every shared edge between any two levels, then exposure and pattern.
    static void Run(string[] args)
    {
        string output = args[0];
        Directory.CreateDirectory(output);
        WorldPlan.Generate(1200);
        var target = ForestHlod.LargestForest();
        var plan = WorldSectors.PlanAt(target, SectorKind.DeepForest, 1037);
        MapLayout.Use(plan);
        // The free-play sector lies elsewhere; keep the photo target on streamed world terrain, as in the build.
        FarTerrain.Origin = new Vector2(target.x > 0f ? target.x - 9000f : target.x + 9000f, target.y);
        int size = ForestHlod.BlockSize;
        var a = new Vector2(Mathf.FloorToInt((target.x + WorldPlan.Half) / size) * size - WorldPlan.Half,
            Mathf.FloorToInt((target.y + WorldPlan.Half) / size) * size - WorldPlan.Half);
        var corners = new[] { a, a + new Vector2(size, 0f), a + new Vector2(0f, size) };
        Log($"target {target.x},{target.y}; block {a.x},{a.y}");
        Vector3 Focus(Vector2 p, float above) => new Vector3(p.x, WorldPlan.Height(p.x, p.y) + above, p.y);
        Vector2 shared = a + new Vector2(size, size) - new Vector2(300f, 300f);
        var levels = new[]
        {
            new Level { name = "bootstrap", step = 256, adaptive = true, focus = Focus(a, 2500f) },
            new Level { name = "coarse128", step = 128, adaptive = true, focus = Focus(a, 2500f) },
            new Level { name = "coarse64", step = 64, adaptive = true, focus = Focus(a, 2500f) },
            new Level { name = "prepared32", step = 32, adaptive = false, focus = Focus(a, 2500f) },
            new Level { name = "refined32-centre", step = 32, adaptive = true, focus = Focus(a + Vector2.one * 1024f, 2500f) },
            new Level { name = "refined32-corner", step = 32, adaptive = true, focus = Focus(shared, 1400f) },
            new Level { name = "refined16-centre", step = 16, adaptive = true, focus = Focus(a + Vector2.one * 1024f, 300f) },
            new Level { name = "refined16-corner", step = 16, adaptive = true, focus = Focus(shared, 300f) },
        };
        var built = new ForestHlod.Data[corners.Length, levels.Length];
        var rows = new List<object>();
        int internalGaps = 0, flippedTotal = 0, degenerateTotal = 0, verticalTotal = 0;
        for (int b = 0; b < corners.Length; b++)
            for (int l = 0; l < levels.Length; l++)
            {
                var data = new ForestHlod.Data { corner = corners[b], step = levels[l].step, adaptive = levels[l].adaptive, focus = levels[l].focus };
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var build = ForestHlod.Build(data, null); while (build.MoveNext()) { }
                double ms = watch.Elapsed.TotalMilliseconds;
                built[b, l] = data;
                Internal(data, out int gaps, out int flipped, out int degenerate, out int vertical);
                internalGaps += gaps; flippedTotal += flipped; degenerateTotal += degenerate; verticalTotal += vertical;
                var row = new { block = b, level = levels[l].name, gaps, flipped, degenerate, vertical,
                    vertices = data.vertices.Length, triangles = data.triangles.Length / 3, milliseconds = Math.Round(ms, 1) };
                rows.Add(row); Log(JsonSerializer.Serialize(row));
            }
        // Walk every shared block edge for every pair of levels: A|east and A|north, both orders included.
        int sharedGaps = 0, pairs = 0, segments = 0; float worstStep = 0f;
        for (int side = 1; side <= 2; side++)
            for (int i = 0; i < levels.Length; i++)
                for (int j = 0; j < levels.Length; j++)
                {
                    int gaps = Shared(built[0, i], built[side, j], side == 1, corners[side], out int walked, out float step);
                    sharedGaps += gaps; pairs++; segments += walked; worstStep = Mathf.Max(worstStep, step);
                    if (gaps > 0) Log($"shared gap: A {levels[i].name} | {(side == 1 ? "east" : "north")} {levels[j].name}: {gaps}");
                }
        Log($"GAP CHECK internal {internalGaps}, shared {sharedGaps} over {pairs} level pairs and {segments} perimeter units, " +
            $"worst seam height difference {worstStep:F4} m; flipped {flippedTotal}, degenerate {degenerateTotal}, vertical {verticalTotal}");

        // Exposure: where coarse canopy lets either terrain model show through inside the woodland mask.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var stands = Stands(a);
        Log($"stand survey {clock.Elapsed.TotalSeconds:F1} s");
        var exposure = new List<object>();
        foreach (int l in new[] { 0, 2, 3, 4, 6 })
        {
            var e = Exposure(built[0, l], stands, a, Path.Combine(output, "exposure-" + levels[l].name + ".ppm"));
            exposure.Add(new { level = levels[l].name, e.woods, e.holes, e.farPoke, e.detailPoke, e.gladeHoles, e.denseExposed, e.blobsPerKm2 });
            Log($"exposure {levels[l].name}: woodland samples {e.woods}, holes {e.holes / (double)e.woods:P2}, below far chunk {e.farPoke / (double)e.woods:P2}, " +
                $"below detail {e.detailPoke / (double)e.woods:P2}, dense-woodland exposed {e.denseExposed:P3}, exposed blobs {e.blobsPerKm2:F1}/km2");
        }
        Log($"exposure {clock.Elapsed.TotalSeconds:F1} s");
        var pattern = Pattern(a, stands);
        Log($"pattern {clock.Elapsed.TotalSeconds:F1} s");
        File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new { blocks = rows, internalGaps, sharedGaps, pairs, segments, worstStep,
            flipped = flippedTotal, degenerate = degenerateTotal, vertical = verticalTotal, exposure, pattern }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "log.txt"), log.ToString());
    }

    // Unmatched internal edges above the floor are cracks; plan-view winding and area catch flipped or degenerate faces.
    static void Internal(ForestHlod.Data data, out int gaps, out int flipped, out int degenerate, out int vertical)
    {
        gaps = flipped = degenerate = vertical = 0;
        var edges = new Dictionary<long, int>();
        var v = data.vertices; var t = data.triangles;
        for (int k = 0; k < t.Length; k += 3)
        {
            Vector3 p = v[t[k]], q = v[t[k + 1]], r = v[t[k + 2]];
            var normal = Vector3.Cross(q - p, r - p);
            float area = Mathf.Sqrt(normal.x * normal.x + normal.y * normal.y + normal.z * normal.z) * 0.5f;
            if (area < 0.01f) degenerate++;
            else if (normal.y <= 0f) flipped++;
            else if (normal.y < 0.05f * area * 2f) vertical++;
            for (int s = 0; s < 3; s++)
            {
                int u = t[k + s], w = t[k + (s + 1) % 3];
                long key = (long)Math.Min(u, w) << 32 | (uint)Math.Max(u, w);
                edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }
        var lo = data.corner - FarTerrain.Origin;
        float hiX = lo.x + ForestHlod.BlockSize, hiZ = lo.y + ForestHlod.BlockSize;
        foreach (var pair in edges)
        {
            if (pair.Value > 2) { gaps++; continue; }
            if (pair.Value != 1) continue;
            int i = (int)(pair.Key >> 32), j = (int)(pair.Key & 0xffffffffL);
            Vector3 p = v[i], q = v[j];
            bool perimeter = p.x == q.x && (p.x == lo.x || p.x == hiX) || p.z == q.z && (p.z == lo.y || p.z == hiZ);
            if (perimeter) continue;
            // An opening's rim is legitimate only where the canopy has already met the floor on both ends.
            if (Mathf.Max(data.crown[i], data.crown[j]) >= 0.25f) gaps++;
        }
    }

    // Boundary edges on one block side, keyed by their 4 m units along the shared line.
    static Dictionary<int, (float a, float b, float ya, float yb, float ca, float cb)> Side(ForestHlod.Data data, bool vertical, float line)
    {
        var result = new Dictionary<int, (float, float, float, float, float, float)>();
        var edges = new Dictionary<long, int>();
        var t = data.triangles; var v = data.vertices;
        for (int k = 0; k < t.Length; k += 3)
            for (int s = 0; s < 3; s++)
            {
                int u = t[k + s], w = t[k + (s + 1) % 3];
                long key = (long)Math.Min(u, w) << 32 | (uint)Math.Max(u, w);
                edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        foreach (var pair in edges)
        {
            if (pair.Value != 1) continue;
            int i = (int)(pair.Key >> 32), j = (int)(pair.Key & 0xffffffffL);
            Vector3 p = v[i], q = v[j];
            float pi = vertical ? p.x : p.z, qi = vertical ? q.x : q.z;
            if (pi != line || qi != line) continue;
            float pa = vertical ? p.z : p.x, qa = vertical ? q.z : q.x;
            if (pa > qa) { (pa, qa) = (qa, pa); (p, q) = (q, p); (i, j) = (j, i); }
            for (float u = pa; u < qa - 0.5f; u += 4f)
                result[Mathf.RoundToInt(u / 4f)] = (pa, qa, p.y, q.y, data.crown[i], data.crown[j]);
        }
        return result;
    }

    // Compare both sides unit by unit: a raised edge without a partner, a T-junction or a height step is a gap.
    static int Shared(ForestHlod.Data a, ForestHlod.Data b, bool east, Vector2 corner, out int walked, out float worst)
    {
        float line = east ? corner.x - FarTerrain.Origin.x : corner.y - FarTerrain.Origin.y;
        var left = Side(a, east, line); var right = Side(b, east, line);
        int gaps = 0; walked = 0; worst = 0f;
        var units = new HashSet<int>(left.Keys); units.UnionWith(right.Keys);
        foreach (int unit in units)
        {
            walked++;
            bool l = left.TryGetValue(unit, out var p), r = right.TryGetValue(unit, out var q);
            if (l != r)
            {
                var e = l ? p : q;
                if (Mathf.Max(e.ca, e.cb) >= 0.25f) gaps++;
                continue;
            }
            if (p.a != q.a || p.b != q.b) { gaps++; continue; }
            float step = Mathf.Max(Mathf.Abs(p.ya - q.ya), Mathf.Abs(p.yb - q.yb));
            worst = Mathf.Max(worst, step);
            if (step > 0.001f) gaps++;
        }
        return gaps;
    }

    sealed class Sample { public float wood, cover, relief, detail, far; public int species; public ForestPattern.Stand stand; }

    // One production stand query per 2 m sample of the photo block, shared by exposure and pattern statistics.
    static Sample[] Stands(Vector2 corner)
    {
        const int n = 1024;
        var result = new Sample[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = corner.x + i * 2f + 1f, z = corner.y + j * 2f + 1f;
                var stand = SectorForest.At(x, z);
                result[j * n + i] = new Sample { wood = stand.wood, cover = stand.cover, relief = stand.height * 0.7f, species = stand.species, stand = stand,
                    detail = stand.wood >= 0.6f ? FarDetail.Detail(x, z) : 0f, far = stand.wood >= 0.6f ? FarDetail.Chunk(x, z) : 0f };
            }
        return result;
    }

    struct Exposed { public int woods, holes, farPoke, detailPoke, gladeHoles; public double denseExposed, blobsPerKm2; }

    // Rasterise the emitted canopy at 2 m and compare it with the rough detail floor and the visible 62.5 m far chunk.
    static Exposed Exposure(ForestHlod.Data data, Sample[] stands, Vector2 corner, string image)
    {
        const int n = 1024;
        var canopy = new float[n * n];
        for (int k = 0; k < canopy.Length; k++) canopy[k] = float.NaN;
        var v = data.vertices; var t = data.triangles;
        Vector2 lo = corner - FarTerrain.Origin;
        for (int k = 0; k < t.Length; k += 3)
        {
            Vector3 p = v[t[k]], q = v[t[k + 1]], r = v[t[k + 2]];
            float minX = Mathf.Min(p.x, Mathf.Min(q.x, r.x)) - lo.x, maxX = Mathf.Max(p.x, Mathf.Max(q.x, r.x)) - lo.x;
            float minZ = Mathf.Min(p.z, Mathf.Min(q.z, r.z)) - lo.y, maxZ = Mathf.Max(p.z, Mathf.Max(q.z, r.z)) - lo.y;
            float det = (q.z - r.z) * (p.x - r.x) + (r.x - q.x) * (p.z - r.z);
            if (Mathf.Abs(det) < 1e-6f) continue;
            for (int j = Mathf.Max(0, Mathf.CeilToInt((minZ - 1f) / 2f)); j < n && j * 2f + 1f <= maxZ; j++)
                for (int i = Mathf.Max(0, Mathf.CeilToInt((minX - 1f) / 2f)); i < n && i * 2f + 1f <= maxX; i++)
                {
                    float x = lo.x + i * 2f + 1f, z = lo.y + j * 2f + 1f;
                    float w0 = ((q.z - r.z) * (x - r.x) + (r.x - q.x) * (z - r.z)) / det;
                    float w1 = ((r.z - p.z) * (x - r.x) + (p.x - r.x) * (z - r.z)) / det;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f) continue;
                    canopy[j * n + i] = w0 * p.y + w1 * q.y + w2 * r.y;
                }
        }
        var result = new Exposed();
        var pixels = new byte[n * n * 3];
        var exposedMask = new bool[n * n];
        int dense = 0, denseExposed = 0;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i; var s = stands[k];
                byte cr = 90, cg = 90, cb = 90;
                if (s.wood >= 0.6f)
                {
                    result.woods++;
                    float detail = s.detail, far = s.far, y = canopy[k];
                    bool hole = float.IsNaN(y), farPoke = !hole && y < far, detailPoke = !hole && y < detail;
                    if (hole) { result.holes++; if (s.cover < 0.05f) result.gladeHoles++; }
                    if (farPoke) result.farPoke++;
                    if (detailPoke) result.detailPoke++;
                    bool exposed = hole || farPoke || detailPoke;
                    exposedMask[k] = exposed;
                    if (s.cover >= 0.5f) { dense++; if (exposed) denseExposed++; }
                    (cr, cg, cb) = hole ? ((byte)200, (byte)30, (byte)30) : farPoke ? ((byte)230, (byte)200, (byte)20) : detailPoke ? ((byte)40, (byte)80, (byte)220)
                        : ((byte)(30 + 40 * s.cover), (byte)(70 + 90 * s.cover), (byte)40);
                }
                int o = ((n - 1 - j) * n + i) * 3;
                pixels[o] = cr; pixels[o + 1] = cg; pixels[o + 2] = cb;
            }
        result.denseExposed = dense > 0 ? denseExposed / (double)dense : 0;
        // Connected exposed patches, as the camera would see separate slivers and wedges.
        int blobs = 0; var seen = new bool[n * n]; var stack = new Stack<int>();
        for (int k = 0; k < exposedMask.Length; k++)
        {
            if (!exposedMask[k] || seen[k]) continue;
            blobs++; seen[k] = true; stack.Push(k);
            while (stack.Count > 0)
            {
                int c = stack.Pop(), ci = c % n, cj = c / n;
                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= n || nj >= n) continue;
                    int m = nj * n + ni;
                    if (exposedMask[m] && !seen[m]) { seen[m] = true; stack.Push(m); }
                }
            }
        }
        result.blobsPerKm2 = blobs / (ForestHlod.BlockSize * (double)ForestHlod.BlockSize / 1e6);
        using (var file = File.Create(image))
        {
            var header = Encoding.ASCII.GetBytes($"P6\n{n} {n}\n255\n");
            file.Write(header, 0, header.Length); file.Write(pixels, 0, pixels.Length);
        }
        return result;
    }

    // Woodland pattern statistics that the art review targets, measured from the production stand recipe.
    static object Pattern(Vector2 corner, Sample[] stands)
    {
        const int n = 1024;
        int woods = 0, open = 0, opening = 0;
        for (int k = 0; k < stands.Length; k++)
        {
            if (stands[k].wood < 0.99f) continue;
            woods++;
            if (stands[k].cover < 0.05f) open++;
            if (stands[k].cover < 0.5f) opening++;
        }
        // Broad relief: 32 m box average of dense-woodland canopy relief, then its spread.
        var broad = new List<float>();
        var field = new float[n / 16 * (n / 16)];
        for (int bj = 0; bj < n / 16; bj++)
            for (int bi = 0; bi < n / 16; bi++)
            {
                double sum = 0; int count = 0;
                for (int j = bj * 16; j < bj * 16 + 16; j++)
                    for (int i = bi * 16; i < bi * 16 + 16; i++)
                    {
                        var s = stands[j * n + i];
                        if (s.cover >= 0.95f) { sum += s.relief; count++; }
                    }
                field[bj * (n / 16) + bi] = count == 256 ? (float)(sum / count) : float.NaN;
                if (count == 256) broad.Add((float)(sum / count));
            }
        broad.Sort();
        float p5 = broad.Count > 0 ? broad[broad.Count * 5 / 100] : 0f, p95 = broad.Count > 0 ? broad[broad.Count * 95 / 100] : 0f;
        // Regularity: the strongest autocorrelation beyond the first zero crossing, along both axes.
        float Regularity(float[] values, int side, float spacing, out float peakLag)
        {
            double mean = 0; int count = 0;
            foreach (var value in values) if (!float.IsNaN(value)) { mean += value; count++; }
            mean /= Math.Max(1, count);
            float best = 0f; peakLag = 0f; bool crossed = false;
            for (int lag = 1; lag < side / 2; lag++)
            {
                double num = 0, den = 0;
                for (int j = 0; j < side; j++)
                    for (int i = 0; i + lag < side; i++)
                    {
                        float x = values[j * side + i], y = values[j * side + i + lag], u = values[i * side + j], w = values[(i + lag) * side + j];
                        if (!float.IsNaN(x) && !float.IsNaN(y)) { num += (x - mean) * (y - mean); den += (x - mean) * (x - mean); }
                        if (!float.IsNaN(u) && !float.IsNaN(w)) { num += (u - mean) * (w - mean); den += (u - mean) * (u - mean); }
                    }
                float r = den > 0 ? (float)(num / den) : 0f;
                if (r <= 0f) crossed = true;
                else if (crossed && r > best) { best = r; peakLag = lag * spacing; }
            }
            return best;
        }
        float reliefRegularity = Regularity(field, n / 16, 32f, out float reliefLag);
        // Glade spacing: Clark-Evans ratio of opening centroids (1 random, above 1.3 a near-regular lattice, below 1 clustered).
        var centroids = new List<Vector2>(); var visited = new bool[n * n]; var pending = new Stack<int>();
        for (int k = 0; k < stands.Length; k++)
        {
            if (visited[k] || stands[k].wood < 0.99f || stands[k].cover >= 0.5f) continue;
            double cx = 0, cz = 0; int count = 0; visited[k] = true; pending.Push(k);
            while (pending.Count > 0)
            {
                int c = pending.Pop(), ci = c % n, cj = c / n; cx += ci; cz += cj; count++;
                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= n || nj >= n) continue;
                    int m = nj * n + ni;
                    if (!visited[m] && stands[m].wood >= 0.99f && stands[m].cover < 0.5f) { visited[m] = true; pending.Push(m); }
                }
            }
            if (count >= 50) centroids.Add(new Vector2((float)(cx / count) * 2f, (float)(cz / count) * 2f));
        }
        double nearest = 0; int inner = 0;
        foreach (var c in centroids)
        {
            if (c.x < 200f || c.y < 200f || c.x > 1848f || c.y > 1848f) continue;
            float best = float.MaxValue;
            foreach (var o in centroids) if (o.x != c.x || o.y != c.y) best = Mathf.Min(best, (o - c).magnitude);
            nearest += best; inner++;
        }
        double clarkEvans = inner > 0 ? (nearest / inner) / (0.5 / Math.Sqrt(centroids.Count / (2048.0 * 2048.0))) : 0;
        // Species patches: run lengths of one species along rows inside dense woodland.
        double runs = 0; int runCount = 0;
        for (int j = 0; j < n; j += 8)
        {
            int length = 0, last = -1; bool bounded = false;
            for (int i = 0; i < n; i++)
            {
                var s = stands[j * n + i];
                if (s.cover < 0.95f) { length = 0; last = -1; bounded = false; continue; }
                if (s.species == last) length++;
                else { if (bounded && length > 0) { runs += length * 2.0; runCount++; } bounded = last >= 0; length = 1; last = s.species; }
            }
        }
        // Highlight: the macro texture's light term in sRGB levels at fully covered samples, where Surface adds it to Linear(stand).
        var light = new Dictionary<string, object>();
        var total = new Dictionary<string, object>();
        foreach (int footprint in new[] { 16, 32, 64 })
        {
            var values = new List<float>(); var greens = new List<float>();
            for (int j = 0; j < n; j += 4)
                for (int i = 0; i < n; i += 4)
                {
                    var s = stands[j * n + i];
                    if (s.cover < 0.999f) continue;
                    float x = corner.x + i * 2f + 1f, z = corner.y + j * 2f + 1f;
                    float g = ForestPalette.Surface(s.stand, x, z, footprint).g;
                    values.Add((g - ForestPalette.Linear(s.stand).gamma.g) * 255f); greens.Add(g * 255f);
                }
            values.Sort(); greens.Sort();
            light[footprint.ToString()] = Math.Round((values[values.Count * 95 / 100] - values[values.Count * 5 / 100]) / 2f, 2);
            total[footprint.ToString()] = Math.Round((greens[greens.Count * 95 / 100] - greens[greens.Count * 5 / 100]) / 2f, 2);
        }
        // Glade brightness by texel size: sRGB green at open glade floors relative to the dense mean.
        var glade = new Dictionary<string, object>();
        foreach (int footprint in new[] { 16, 32, 64 })
        {
            double gladeGreen = 0, denseGreen = 0; int gladeCount = 0, denseCount = 0;
            for (int j = 0; j < n; j += 4)
                for (int i = 0; i < n; i += 4)
                {
                    var s = stands[j * n + i];
                    if (s.wood < 0.99f) continue;
                    float x = corner.x + i * 2f + 1f, z = corner.y + j * 2f + 1f;
                    float x0 = Mathf.Floor(x / footprint) * footprint + footprint * 0.5f, z0 = Mathf.Floor(z / footprint) * footprint + footprint * 0.5f;
                    float g = ForestPalette.Surface(SectorForest.At(x0, z0), x0, z0, footprint).g * 255f;
                    if (s.cover < 0.05f) { gladeGreen += g; gladeCount++; }
                    else if (s.cover >= 0.95f) { denseGreen += g; denseCount++; }
                }
            glade[footprint.ToString()] = Math.Round(gladeGreen / Math.Max(1, gladeCount) - denseGreen / Math.Max(1, denseCount), 1);
        }
        // Crown size: individual radius against its stand, and how strongly near neighbours share that variation.
        var trees = new List<(Vector2 p, float ratio)>();
        int x0i = Mathf.FloorToInt((corner.x + 512f) / ForestPattern.TreeStep), z0i = Mathf.FloorToInt((corner.y + 512f) / ForestPattern.TreeStep);
        for (int j = z0i; j < z0i + 100; j++)
            for (int i = x0i; i < x0i + 100; i++)
            {
                if (!ForestPattern.Tree(i, j, out Vector2 world, out var stand)) continue;
                var basis = ForestPattern.At(world.x, world.y);
                if (basis.cover < 0.95f || stand.radius >= 4.59f) continue;
                trees.Add((world, stand.radius / Mathf.Max(2f, basis.radius)));
            }
        var ratios = new List<float>(); foreach (var tr in trees) ratios.Add(tr.ratio); ratios.Sort();
        float crownHalfRange = ratios.Count > 0 ? (ratios[ratios.Count * 95 / 100] - ratios[ratios.Count * 5 / 100]) / 2f : 0f;
        double rm = 0; foreach (var tr in trees) rm += tr.ratio; rm /= Math.Max(1, trees.Count);
        double rv = 0; foreach (var tr in trees) rv += (tr.ratio - rm) * (tr.ratio - rm); rv /= Math.Max(1, trees.Count);
        double cnum = 0, cden = 0;
        for (int p = 0; p < trees.Count; p++)
            for (int q = p + 1; q < Math.Min(trees.Count, p + 60); q++)
                if ((trees[p].p - trees[q].p).sqrMagnitude < 100f) { cnum += (trees[p].ratio - rm) * (trees[q].ratio - rm); cden += rv; }
        // Edge: distance over which the wood mask rises from 0.1 to 0.9 on 16 km transects through outer forest boundaries.
        var widths = new List<float>();
        for (int row = -8; row <= 8; row++)
        {
            float z = corner.y + 1024f + row * 512f, rise = float.NaN, previous = 0f;
            for (float x = corner.x - 7000f; x < corner.x + 9000f; x += 2f)
            {
                float w = ForestPattern.Edge(x, z);
                if (previous < 0.1f && w >= 0.1f) rise = x;
                if (!float.IsNaN(rise) && previous < 0.9f && w >= 0.9f) { widths.Add(x - rise); rise = float.NaN; }
                if (w < 0.1f) rise = float.NaN;
                previous = w;
            }
        }
        widths.Sort();
        var result = new
        {
            gladeOpenOfWoodland = Math.Round(open / (double)Math.Max(1, woods), 4),
            gladeOpeningOfWoodland = Math.Round(opening / (double)Math.Max(1, woods), 4),
            broadReliefHalfRange = Math.Round((p95 - p5) / 2f, 2),
            reliefRegularity = Math.Round(reliefRegularity, 3), reliefPeakLag = reliefLag,
            glades = centroids.Count, gladeClarkEvans = Math.Round(clarkEvans, 2),
            speciesRunMetres = Math.Round(runs / Math.Max(1, runCount), 1),
            lightHalfRange = light, denseGreenHalfRange = total, gladeGreenMinusDense = glade,
            crownRatioStd = Math.Round(Math.Sqrt(rv), 3), crownRatioHalfRange = Math.Round(crownHalfRange, 3), crownNeighbourCorrelation = Math.Round(cden > 0 ? cnum / cden : 0, 3), trees = trees.Count,
            edgeMedianMetres = widths.Count > 0 ? widths[widths.Count / 2] : 0f, edgeTransects = widths.Count,
        };
        Log("pattern " + JsonSerializer.Serialize(result));
        return result;
    }
}

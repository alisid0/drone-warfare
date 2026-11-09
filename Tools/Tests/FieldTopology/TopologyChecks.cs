using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Independent polygon measurements catch short edges, thin remnants, acute tips and crowded district junctions.
static class TopologyChecks
{
    // Measure 500 fixed districts in both versions, before cover/feature masks; retain honest baseline limitations.
    public static void Run(string output, FarDetailData data)
    {
        int plots = 0, tips = 0, slivers = 0, shortEdges = 0, smallAreas = 0, thin = 0;
        double area = 0, minimumAngle = 180, minimumEdge = double.MaxValue, minimumWidth = double.MaxValue, minimumArea = double.MaxValue;
        var junctions = new Dictionary<string, (Vector2 point, HashSet<int> districts)>();
        for (int z = -10; z < 10; z++) for (int x = -13; x < 12; x++)
        {
            var outline = Voronoi(x, z, 0f);
            var polygons = new List<List<Vector2>>();
#if BEFORE
            polygons = Legacy(x, z);
#else
            var d = data.fields.Get(x, z);
            foreach (var p in d.parcels)
            {
                var f = p.Plot(d, Vector2.zero);
                polygons.Add(new List<Vector2> { Corner(f, -1, -1), Corner(f, 1, -1), Corner(f, 1, 1), Corner(f, -1, 1) });
            }
#endif
            foreach (var p in polygons)
            {
                double a = Area(p), edge = double.MaxValue, angle = 180, width = double.MaxValue;
                for (int i = 0; i < p.Count; i++)
                {
                    Vector2 v = p[i], prev = p[(i + p.Count - 1) % p.Count] - v, next = p[(i + 1) % p.Count] - v;
                    double length = next.magnitude;
                    edge = Math.Min(edge, length);
                    if (length < .0001 || prev.magnitude < .0001) continue;
                    angle = Math.Min(angle, Math.Acos(Math.Max(-1, Math.Min(1, Vector2.Dot(prev, next) / (prev.magnitude * length)))) * 180 / Math.PI);
                    double high = double.MinValue, low = double.MaxValue;
                    foreach (var q in p) { double t = (-(q.x - v.x) * next.y + (q.y - v.y) * next.x) / length; high = Math.Max(high, t); low = Math.Min(low, t); }
                    width = Math.Min(width, high - low);
                }
                plots++; area += a;
                if (angle < 35 - .03) tips++;
                if (edge < 18 - .03) shortEdges++;
                if (a < 600 - .1) smallAreas++;
                if (width < 12 - .03) thin++;
                if (edge < 18 - .03 || a < 600 - .1 || width < 12 - .03) slivers++;
                minimumAngle = Math.Min(minimumAngle, angle); minimumEdge = Math.Min(minimumEdge, edge); minimumWidth = Math.Min(minimumWidth, width); minimumArea = Math.Min(minimumArea, a);
            }
            foreach (var v in outline)
            {
                string key = Math.Round(v.x / .25f) + ":" + Math.Round(v.y / .25f);
                if (!junctions.TryGetValue(key, out var junction)) junction = (v, new HashSet<int>());
                foreach (var p in polygons) if (Distance(p, v) < 12f) { junction.districts.Add((z + 10) * 25 + x + 13); break; }
                junctions[key] = junction;
            }
        }
        int convergences = 0;
        foreach (var entry in junctions.Values) if (entry.districts.Count >= 3) convergences++;
        var result = new { districts = 500, plots, tips, slivers, shortEdges, smallAreas, thin, convergences, minimumAngle, minimumEdge, minimumWidth, minimumArea, cultivatedSquareMetres = area,
            note = "Planar parcel geometry before cover and feature masks. Baseline reconstructs the e45f400 strip skeleton with 3/2.5 m crop insets and a 4 m district inset; its 30 m strip warp and distance-difference margin are not polygonal and are omitted. After uses actual production parcels. Convergence = crops of at least three districts within 12 m of their common Voronoi vertex." };
        File.WriteAllText(Path.Combine(output, "topology.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"500 districts: {plots} plots, {tips} tips, {slivers} slivers, {convergences} convergences");
#if !BEFORE
        if (tips != 0 || slivers != 0 || convergences != 0) throw new Exception("Unworkable cultivated parcel topology");
        SectorAgreement(data, output);
#endif
    }

#if !BEFORE
    // Crop and row ownership must agree across arbitrary translated sector cuts, including points near all four edges.
    static void SectorAgreement(FarDetailData data, string output)
    {
        int samples = 0, wrongCrop = 0, wrongAngle = 0, outside = 0, works = 0, water = 0, edgeSamples = 0;
        foreach (var f in MapLayout.Fields)
        {
            if (!FarFieldSurface.TryParent(f, out var parent)) throw new Exception("Clipped field lost its material parent");
            Vector2 uv = FarFieldSurface.RowUv(parent, f.center, 8f), next = FarFieldSurface.RowUv(parent, f.center + f.along * 8f, 8f);
            if (Math.Abs(next.x - uv.x) > .001f || Math.Abs(next.y - uv.y - 1f) > .001f) throw new Exception("World rows do not follow physical metres");
            for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++)
            {
                Vector2 local = f.Point(f.halfLength * (i - 1f) * .95f, f.halfWidth * (j - 1f) * .95f), world = local + MapLayout.Plan.world.center;
                var p = data.FieldAt(world.x, world.y); samples++;
                if (p.layer != 5 + (int)f.crop || p.side < -.03f || p.end < -.03f) wrongCrop++;
                float dot = f.along.x * (float)Math.Cos(p.angle) + f.along.y * (float)Math.Sin(p.angle);
                if (Math.Abs(dot) < .99999f) wrongAngle++;
                if (Math.Abs(local.x) > 1000.02f || Math.Abs(local.y) > 1000.02f) outside++;
                if (Math.Max(Math.Abs(local.x), Math.Abs(local.y)) > 975f) edgeSamples++;
                if (MapLayout.OnWorks(local, 0f)) works++;
                if (MapLayout.Plan.world.LakeDistance(local, out _) < 0f || MapLayout.Plan.world.RiverDistance(local, out float riverWidth, out _) < riverWidth) water++;
            }
        }
        int cutSamples = 0, cutMismatches = 0, cutCoverMismatches = 0;
        for (int side = 0; side < 4; side++) for (int k = 0; k < 4000; k++)
        {
            float along = -999.75f + k * .5f;
            Vector2 p = side < 2 ? new Vector2(side == 0 ? -999.9f : 999.9f, along) : new Vector2(along, side == 2 ? -999.9f : 999.9f);
            foreach (var f in MapLayout.Fields)
            {
                if (!f.ContainsUnwarped(p, 0f)) continue;
                var at = p + MapLayout.Plan.world.center; var field = data.FieldAt(at.x, at.y); cutSamples++;
                if (field.layer != 5 + (int)f.crop || field.side < -.03f || field.end < -.03f) cutMismatches++;
                if (data.CoverAt(at.x, at.y) != Cover.Farmland) cutCoverMismatches++;
                break;
            }
        }
        File.WriteAllText(Path.Combine(output, "agreement.json"), JsonSerializer.Serialize(new { samples, edgeSamples, cutSamples, cutMismatches, cutCoverMismatches, wrongCrop, wrongAngle, outside, works, water }));
        if (wrongCrop + wrongAngle + outside + works + water + cutMismatches + cutCoverMismatches != 0) throw new Exception("Sector/world parcel agreement failed");
    }
#endif

    // Preserve the old per-strip lengths, offsets and angles; clip their skeleton against the old district fabric.
    static List<List<Vector2>> Legacy(int x, int z)
    {
        int district = Hash(x, z);
        float angle = (district >> 3 & 1023) / 1023f * (float)Math.PI, ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
        var outline = Voronoi(x, z, 4f); var local = new List<Vector2>();
        float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        foreach (var p in outline) { var q = new Vector2(p.x * ca + p.y * sa, -p.x * sa + p.y * ca); local.Add(q); minU = Math.Min(minU, q.x); maxU = Math.Max(maxU, q.x); minV = Math.Min(minV, q.y); maxV = Math.Max(maxV, q.y); }
        float width = 35f + (district >> 13 & 63) / 63f * 75f;
        var result = new List<List<Vector2>>();
        for (int row = (int)Math.Floor(minV / width); row <= (int)Math.Floor(maxV / width); row++)
        {
            int hs = Hash(row, district); float length = 150f + (hs & 255) / 255f * 250f, offset = hs >> 8 & 1023;
            for (int col = (int)Math.Floor((minU + offset) / length); col <= (int)Math.Floor((maxU + offset) / length); col++)
            {
                float u = col * length - offset, v = row * width;
                var p = Clip(Clip(Clip(Clip(local, -1, 0, -u - 2.5f), 1, 0, u + length - 2.5f), 0, -1, -v - 3f), 0, 1, v + width - 3f);
                if (p.Count < 3) continue;
                for (int k = 0; k < p.Count; k++) { var q = p[k]; p[k] = new Vector2(q.x * ca - q.y * sa, q.x * sa + q.y * ca); }
                if (Area(p) > .01f) result.Add(p);
            }
        }
        return result;
    }

    // Reconstruct district bisectors independently of the production implementation for the audit reference points.
    static List<Vector2> Voronoi(int x, int z, float inset)
    {
        Vector2 site = Site(x, z);
        var polygon = new List<Vector2> { site + new Vector2(-1400,-1400), site + new Vector2(1400,-1400), site + new Vector2(1400,1400), site + new Vector2(-1400,1400) };
        for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++)
        {
            if (i == 0 && j == 0) continue;
            Vector2 delta = Site(x + i, z + j) - site, n = delta.normalized;
            polygon = Clip(polygon, n.x, n.y, Vector2.Dot(n, site) + delta.magnitude * .5f - inset);
        }
        return polygon;
    }

    // Test-only half-plane clipping does not call the implementation under test.
    static List<Vector2> Clip(List<Vector2> polygon, float nx, float ny, float limit)
    {
        var r = new List<Vector2>(); if (polygon.Count == 0) return r;
        Vector2 a = polygon[polygon.Count - 1]; float da = a.x * nx + a.y * ny - limit;
        foreach (var b in polygon) { float db = b.x * nx + b.y * ny - limit; if ((da < 0) != (db < 0)) r.Add(a + (b - a) * (da / (da - db))); if (db <= 0) r.Add(b); a = b; da = db; }
        return r;
    }

    // Translation before shoelace accumulation avoids cancellation far from the world origin.
    static double Area(List<Vector2> polygon)
    { double area = 0; Vector2 origin = polygon[0]; for (int i = 0; i < polygon.Count; i++) { Vector2 a = polygon[i] - origin, b = polygon[(i + 1) % polygon.Count] - origin; area += (double)a.x * b.y - (double)a.y * b.x; } return Math.Abs(area) * .5; }

    // Nearest segment distance measures a meadow junction without assuming a particular strip direction.
    static float Distance(List<Vector2> polygon, Vector2 p)
    { float best = float.MaxValue; for (int i = 0; i < polygon.Count; i++) { Vector2 a = polygon[i], d = polygon[(i + 1) % polygon.Count] - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude); best = Math.Min(best, (p - a - d * t).magnitude); } return best; }

    // Audit the stored polygon without consulting FieldPlot's sector-dependent warp.
    static Vector2 Corner(FieldPlot f, float u, float v) => f.center + f.along * (u * f.halfLength) + f.Across * (v * f.HalfWidthAt(u * f.halfLength) + f.shear * u * f.halfLength);
    // Original district seeds remain fixed in both versions.
    static Vector2 Site(int x, int z) { int h = Hash(x, z); return new Vector2((x + .15f + .7f * (h & 1023) / 1023f) * 700f, (z + .15f + .7f * (h >> 10 & 1023) / 1023f) * 700f); }
    // The baseline world seed is explicit, rather than inherited from sector random state.
    static int Hash(int x, int z) { unchecked { int h = x * 73856093 ^ z * 19349663 ^ 1200 * 83492791; h ^= h >> 13; h *= 0x5bd1e995; return h & 0x7fffffff; } }
}

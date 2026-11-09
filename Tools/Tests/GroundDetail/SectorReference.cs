using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Real sector layouts and Ground's unchanged height/splat methods supply the reference side of the render pairs.
static class SectorReference
{
    const int Width = 768, Height = 432;

    // Only engine resource storage is substituted; crop colours, crop detail and splat weights come from game code.
    public static void Export(string output, string name, Vector2 centre, FarDetailData world, bool edgeCheck)
    {
        var plan = WorldSectors.PlanAt(centre, SectorKind.RollingFarmland, 1200);
        MapLayout.Use(plan);
        Ground.Build(null);
        var data = Ground.Current.terrainData;
        Vector2 eye = Vector2.zero;
        if (name == "wheat" || name == "ploughed")
        {
            Crop crop = name == "wheat" ? Crop.Wheat : Crop.Ploughed;
            float best = float.MaxValue;
            foreach (var f in MapLayout.Fields)
            {
                if (f.crop != crop || f.halfWidth < 15f) continue;
                float score = f.center.magnitude - Math.Min(f.halfWidth, 60f) * 3f;
                if (score < best) { best = score; eye = f.center; }
            }
            if (best == float.MaxValue) throw new Exception("Sector lacks reference " + crop);
        }
        var tints = new List<object>();
        var rng = new Random(MapLayout.Plan.worldSeed ^ 0x31a7);
        foreach (var f in MapLayout.Fields)
        {
            float shade = .9f + (float)rng.NextDouble() * .18f;
            tints.Add(new { layer = 5 + (int)f.crop, tint = new[] { shade, shade, shade * (.94f + (float)rng.NextDouble() * .08f) } });
        }
        File.WriteAllText(Path.Combine(output, name + "-reference.json"), JsonSerializer.Serialize(new { x = eye.x, z = eye.y, fields = tints }));
        foreach (int altitude in new[] { 50, 300 })
        {
            float span = 2f * altitude * (float)Math.Tan(Math.PI / 8) * Width / Height;
            using var weights = new BinaryWriter(File.Create(Path.Combine(output, name + "-reference-" + altitude + "-weights.bin")));
            using var crops = new BinaryWriter(File.Create(Path.Combine(output, name + "-reference-" + altitude + "-fields.bin")));
            for (int j = 0; j < Height; j++) for (int i = 0; i < Width; i++)
                {
                    var at = new Vector2(eye.x + ((i + .5f) / Width - .5f) * span, eye.y + ((j + .5f) / Height - .5f) * span * Height / Width);
                    int ax = Mathf.Clamp((int)((at.x + 1000) / 2000 * 2048), 0, 2047), az = Mathf.Clamp((int)((at.y + 1000) / 2000 * 2048), 0, 2047);
                    for (int k = 0; k < 5; k++) weights.Write(data.maps[az, ax, k]);
                    int field = -1; float u = 0, v = 0, detail = .5f;
                    Vector2 unwarped = FieldPlot.Unwarp(at);
                    for (int k = 0; k < MapLayout.Fields.Count; k++)
                    {
                        var f = MapLayout.Fields[k];
                        if (!f.ContainsUnwarped(unwarped, 0)) continue;
                        field = k;
                        Vector2 d = unwarped - f.center;
                        u = Vector2.Dot(d, f.along);
                        v = (Vector2.Dot(d, f.Across) - f.shear * u) * f.halfWidth / f.HalfWidthAt(u);
                        detail = CropReference.MapValue(f, v + f.halfWidth, u + f.halfLength);
                        break;
                    }
                    if (plan.world.RiverDistance(at, out float riverWidth, out _) < riverWidth || plan.world.LakeDistance(at, out _) < 0f) field = -2;
                    crops.Write((float)field); crops.Write(v / 8f); crops.Write(u / 8f); crops.Write(detail);
                }
        }
        if (edgeCheck) Edge(output, centre, world, data);
        Console.WriteLine("Reference " + name + ": " + MapLayout.Fields.Count + " actual sector fields, 2048 splat resolution");
    }

    // Compare actual splat colour just inside the sector with the final world texture just outside, on all four sides.
    static void Edge(string output, Vector2 centre, FarDetailData world, TerrainData data)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var texture = new Texture2D(4096, 4096, TextureFormat.RGBA32, true);
        texture.SetPixels32(world.WorldPixels(4096));
        Console.WriteLine($"World painter {timer.Elapsed.TotalSeconds:F2} s (reference Perlin)");
        FarDetailEdge.Apply(texture, centre);
        Vector3 inside = Vector3.zero, outside = Vector3.zero;
        var sides = new List<object>();
        for (int side = 0; side < 4; side++)
        {
            Vector3 a = Vector3.zero, b = Vector3.zero;
            for (int sample = 0; sample < 1024; sample++)
            {
                float along = -980f + (sample + .5f) / 1024 * 1960f;
                for (int d = 0; d < 8; d++)
                {
                    float distance = .5f + d;
                    var p = side < 2 ? new Vector2((side == 0 ? -1 : 1) * (1000 - distance), along) : new Vector2(along, (side == 2 ? -1 : 1) * (1000 - distance));
                    a += GroundMean(data, p);
                    p = side < 2 ? new Vector2((side == 0 ? -1 : 1) * (1000 + distance), along) : new Vector2(along, (side == 2 ? -1 : 1) * (1000 + distance));
                    b += TextureMean(texture, p + centre);
                }
            }
            a /= 8192; b /= 8192; inside += a; outside += b;
            float error = Math.Max(Math.Abs(b.x / a.x - 1), Math.Max(Math.Abs(b.y / a.y - 1), Math.Abs(b.z / a.z - 1)));
            sides.Add(new { side, insideLinear = new[] { a.x, a.y, a.z }, outsideLinear = new[] { b.x, b.y, b.z }, maxChannelPercent = error * 100 });
            if (error > .05f) throw new Exception("Sector edge exceeds 5% on side " + side + ": " + error);
        }
        File.WriteAllText(Path.Combine(output, "edge-metrics.json"), JsonSerializer.Serialize(new { sides, worldPaintSeconds = timer.Elapsed.TotalSeconds,
            note = "Actual Ground.BuildSplat and FarDetailEdge output, 8 m strips on all four sides, linear albedo. Managed Perlin; no lighting, geometry or exposure claim." }, new JsonSerializerOptions { WriteIndented = true }));
        var picture = new Color32[800 * 800];
        for (int j = 0; j < 800; j++) for (int i = 0; i < 800; i++)
            {
                var p = new Vector2((i + .5f) * 4 - 1600, (j + .5f) * 4 - 1600);
                Vector3 colour = Math.Max(Math.Abs(p.x), Math.Abs(p.y)) < 1000 ? GroundMean(data, p) : TextureMean(texture, p + centre);
                if (Math.Max(Math.Abs(p.x), Math.Abs(p.y)) < 1000 && MapLayout.InField(p, 0f))
                {
                    Vector2 q = FieldPlot.Unwarp(p);
                    foreach (var f in MapLayout.Fields) if (f.ContainsUnwarped(q, 0f)) { colour = FarDetailPalette.Means[5 + (int)f.crop]; break; }
                }
                picture[j * 800 + i] = FarDetailPalette.Encode(colour);
            }
        using var writer = new BinaryWriter(File.Create(Path.Combine(output, "sector-edge.rgba")));
        foreach (var p in picture) { writer.Write(p.r); writer.Write(p.g); writer.Write(p.b); writer.Write(p.a); }
        // Returning to the same sector must restore the pristine patch before repainting it.
        var before = (Color32[])texture.pixels.Clone(); FarDetailEdge.Apply(texture, centre);
        for (int k = 0; k < before.Length; k++) if (before[k].r != texture.pixels[k].r || before[k].g != texture.pixels[k].g || before[k].b != texture.pixels[k].b) throw new Exception("Sector collar accumulated on rebuild");
    }

    // The source terrain's five layer weights are blended in the same linear space as TerrainLit.
    static Vector3 GroundMean(TerrainData data, Vector2 at)
    {
        int x = Mathf.Clamp((int)((at.x + 1000) / 2000 * 2048), 0, 2047), z = Mathf.Clamp((int)((at.y + 1000) / 2000 * 2048), 0, 2047);
        Vector3 c = Vector3.zero;
        for (int k = 0; k < 5; k++) c += FarDetailPalette.Means[k] * data.maps[z, x, k];
        return c;
    }

    // Bilinear filtering reproduces the world material's base texture at the edge rather than comparing writer inputs.
    static Vector3 TextureMean(Texture2D texture, Vector2 at)
    {
        float x = (at.x + WorldPlan.Half) / WorldPlan.Size * texture.width - .5f, z = (at.y + WorldPlan.Half) / WorldPlan.Size * texture.height - .5f;
        int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
        Vector3 A(int i, int j) => FarDetailPalette.Decode(texture.pixels[Mathf.Clamp(j, 0, 4095) * 4096 + Mathf.Clamp(i, 0, 4095)]);
        return Vector3.Lerp(Vector3.Lerp(A(ix, iz), A(ix + 1, iz), x - ix), Vector3.Lerp(A(ix, iz + 1), A(ix + 1, iz + 1), x - ix), z - iz);
    }
}

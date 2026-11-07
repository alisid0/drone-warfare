using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Time the actual forest recipe and compare all rendered geometry without starting a renderer.
static class WoodsBuildHarness
{
    static readonly List<double> random = new List<double>();
    static readonly List<Vector2> gardens = new List<Vector2>();

    // Record and replay only the grove builder's draws, keeping the production World's RNG sequence intact.
    sealed class GroveRandom : Random
    {
        readonly Random source;
        int cursor;
        // A null source replays the first build after its gardens have been restored.
        public GroveRandom(Random source) { this.source = source; }
        // PlanGroves consumes only NextDouble; no randomness runs on a worker.
        public override double NextDouble()
        {
            if (source == null) return random[cursor++];
            double value = source.NextDouble(); random.Add(value); return value;
        }
    }

    // Capture the real pre-woods occupancy and random draws without changing the production recipe.
    public static void BuildForest(Random rng, List<Vector2> woodland)
    {
        gardens.AddRange(Undergrowth.Trees);
        SectorForest.Build(new GroveRandom(rng), woodland);
    }

    // One world setup and three forest builds keep memory and CPU use small on the shared machine.
    static void Main(string[] args)
    {
        if (args[1] != "preview") WorldPlan.Generate(1200);
        var plan = SectorPlan.Varrow();
        if (args[1] == "deep")
        {
            FarTerrain.Origin = WorldSectors.SpotFor(new Hotspot { kind = SectorKind.DeepForest, seed = 1037 });
            plan = WorldSectors.PlanAt(FarTerrain.Origin, SectorKind.DeepForest, 1037);
        }
        else if (args[1] == "preview") { FarTerrain.Origin = Vector2.zero; FarTerrain.Active = false; }
        else
        {
            var dam = WorldPlan.Dams[0]; var origin = (dam.a + dam.b) / 2f - plan.damPoint;
            FarTerrain.Origin = new Vector2(Mathf.RoundToInt(origin.x / 250f) * 250f, Mathf.RoundToInt(origin.y / 250f) * 250f);
        }
        MapLayout.Use(plan);
        World.Build(new GameObject("Woods timing").transform, true);
        Console.WriteLine(args[0] + " " + args[1] + " managed World: " + World.Timings);
        string expected = Signature();
        Print(0);
        for (int run = 1; run < 3; run++)
        {
            Undergrowth.Reset();
            foreach (var p in gardens) Undergrowth.NoteTree(p);
            GC.Collect(); GC.WaitForPendingFinalizers();
            SectorForest.Build(new GroveRandom(null), new List<Vector2>());
            Print(run);
            if (Signature() != expected) throw new Exception("Forest changed between identical builds");
        }
        Console.WriteLine("signature " + expected);
    }

    // Report phases separately so the native log can distinguish recipes from canopy preparation.
    static void Print(int run)
    {
        int vertices = 0, triangles = 0;
        foreach (var data in SectorForest.PreparedCanopy) { vertices += data.vertices.Length; triangles += data.triangles.Length / 3; }
        Console.WriteLine($"run {run}: total {SectorForest.BuildMilliseconds:F1}, stems {SectorForest.StemsMilliseconds:F1}, indices {SectorForest.IndicesMilliseconds:F1}, canopy {SectorForest.CanopyMilliseconds:F1} ms; stems {SectorForest.Stems.Count}, cells {SectorForest.LocalCells.Count}, vertices {vertices}, triangles {triangles}");
    }

    // Hash stems and every rendered triangle attribute; unused vertices may disappear without changing the image.
    static string Signature()
    {
        using var sha = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
        using var writer = new BinaryWriter(stream);
        foreach (var stem in SectorForest.Stems)
        {
            writer.Write(stem.i); writer.Write(stem.j);
            writer.Write(stem.position.x); writer.Write(stem.position.y); writer.Write(stem.position.z);
            writer.Write(stem.stand.species); writer.Write(stem.stand.height); writer.Write(stem.stand.radius);
        }
        foreach (var data in SectorForest.PreparedCanopy)
        {
            writer.Write(data.triangles.Length);
            foreach (int i in data.triangles)
            {
                writer.Write(data.vertices[i].x); writer.Write(data.vertices[i].y); writer.Write(data.vertices[i].z);
                writer.Write(data.normals[i].x); writer.Write(data.normals[i].y); writer.Write(data.normals[i].z);
                writer.Write(data.uv[i].x); writer.Write(data.uv[i].y);
            }
            if (data.pixels != null) foreach (var p in data.pixels) { writer.Write(p.r); writer.Write(p.g); writer.Write(p.b); writer.Write(p.a); }
        }
        writer.Flush(); stream.FlushFinalBlock();
        return Convert.ToHexString(sha.Hash);
    }
}

using System;
using System.Diagnostics;
using System.Reflection;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Compare only the production layout and village work; terrain splat storage and all unrelated scene builders are omitted.
static class LivedInBenchmark
{
    // Separate processes bound memory and avoid reusing another scene's state or native resources.
    static int Main(string[] args)
    {
        WorldPlan.Generate(1200);
        var kind=args[0]=="TownOutskirts"?SectorKind.TownOutskirts:SectorKind.RiverValley;
        var hotspot=new Hotspot{kind=kind,seed=kind==SectorKind.TownOutskirts?1111:1000};
        MapLayout.Use(WorldSectors.PlanAt(WorldSectors.SpotFor(hotspot),kind,hotspot.seed));
        var timer=Stopwatch.StartNew();MapLayout.Prepare();double layout=timer.Elapsed.TotalMilliseconds;
        var root=new GameObject("Benchmark").transform;Ground.Build(root);timer.Restart();
        typeof(World).GetMethod("BuildVillages",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{root,new Random(MapLayout.Plan.worldSeed)});
        int near=0,far=0;foreach(var mesh in Mesh.All)if(mesh.name!=null&&mesh.name.StartsWith("Village plots LOD"))
            foreach(var indices in mesh.subs.Values){if(mesh.name.EndsWith("0"))near+=indices.Length/3;else far+=indices.Length/3;}
        Console.WriteLine($"{args[0]} layout {layout:F1} ms; villages {timer.Elapsed.TotalMilliseconds:F1} ms; houses {World.VillageHouses}; resident props {World.VillagePropRenderers}; near triangles {near}; far triangles {far}; peak {Process.GetCurrentProcess().PeakWorkingSet64/1048576} MiB");
        return 0;
    }
}

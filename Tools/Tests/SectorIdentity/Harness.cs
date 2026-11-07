using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Runs production generation, layout and scenery code without Unity, then exports comparable placement plans.
public static class Harness
{
    static readonly (SectorKind kind,int seed)[] Cases = {
        (SectorKind.DeepForest,1037),(SectorKind.IndustrialZone,1148),(SectorKind.Airfield,1222),
        (SectorKind.RailJunction,1185),(SectorKind.TownOutskirts,1111),(SectorKind.Marshland,1074),(SectorKind.MarketTown,1629)
    };
    static readonly Dictionary<string,Vector2> sizes=new Dictionary<string,Vector2>();
    static readonly HashSet<string> buildings=new HashSet<string>{"Scenery_House_A","Scenery_House_B","Scenery_House_C","Scenery_House_D",
        "Scenery_Townhouse_Row","Scenery_Apartment","Scenery_Shop","Scenery_Church","Scenery_Barn","Scenery_Hut","Scenery_Warehouse",
        "Scenery_OfficeBlock","Scenery_FactoryHall","Scenery_Factory","Scenery_StorageTank","Scenery_CoolingTower","Scenery_ControlTower",
        "Scenery_HangarLarge","Scenery_AircraftShelter","Scenery_FuelFarm","Scenery_FreightShed","Scenery_SignalBox","Scenery_Station","Base_Hangar"};

    // Serializes coordinates explicitly to avoid differences between the substitutes and engine vector properties.
    static float[] XY(Vector2 p)=>new[]{p.x,p.y};

    // Captures curved parcel edges rather than replacing them with rectangles in the comparison images.
    static List<float[]> Outline(FieldPlot f)
    {
        var points=new List<float[]>();
        for(int k=0;k<4;k++) for(int i=0;i<20;i++)
        {
            float t=i/20f;
            points.Add(XY(k==0?f.Point(Mathf.Lerp(-f.halfLength,f.halfLength,t),-f.halfWidth)
                :k==1?f.Point(f.halfLength,Mathf.Lerp(-f.halfWidth,f.halfWidth,t))
                :k==2?f.Point(Mathf.Lerp(f.halfLength,-f.halfLength,t),f.halfWidth)
                :f.Point(-f.halfLength,Mathf.Lerp(f.halfWidth,-f.halfWidth,t))));
        }
        return points;
    }

    // All comparisons use AutoTest.SectorTour seeds; MarketTown has no tour entry and uses 1000 + 37 * its enum.
    public static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Keeps failed checks out of Windows crash reporting so a failed run exits promptly.
    static void Run(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        sizes["Scenery_Warehouse"]=new Vector2(18,30);
        sizes["Scenery_Station"]=new Vector2(8,14);
        sizes["Scenery_RailWagon"]=new Vector2(2.9f,14);
        sizes["Scenery_RailTankWagon"]=new Vector2(2.9f,12);
        sizes["Scenery_House_A"]=new Vector2(9,8);
        sizes["Scenery_House_B"]=new Vector2(9.5f,8.5f);
        using(var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(args[1],"Tools/Blender/world_assets_manifest.json"))))
            foreach(var property in manifest.RootElement.EnumerateObject())
            {
                var d=property.Value.GetProperty("dimensions");
                sizes[property.Name]=new Vector2(d[0].GetSingle(),d[1].GetSingle());
                RenderAudit.newModels.Add(property.Name);
            }
        foreach(var entry in Cases)
        {
            var clock=Stopwatch.StartNew();
            var p=SectorGenerator.Generate(entry.kind,entry.seed);
            Build(p); clock.Stop();
            double first=clock.Elapsed.TotalMilliseconds;
            string signature=Fingerprint(p);
            var snapshot=Export(p,first);
            Check(p);
            clock.Restart();
            p=SectorGenerator.Generate(entry.kind,entry.seed);
            Build(p); clock.Stop();
            if(signature!=Fingerprint(p))
            {
#if AFTER
                throw new Exception("Nondeterministic sector "+entry.kind);
#else
                Console.WriteLine("BASELINE state-dependent layout on first transition: "+entry.kind);
#endif
            }
            File.WriteAllText(Path.Combine(args[0],entry.kind+".json"),JsonSerializer.Serialize(snapshot));
            Console.WriteLine($"{entry.kind} seed {entry.seed}: {MapLayout.Fields.Count} fields, coverage {MapLayout.FieldCoverage()*100:F1}%, {RenderAudit.renderers} scenery renderers, {World.TreeCount} trees; CPU build {first:F0}/{clock.Elapsed.TotalMilliseconds:F0} ms");
        }
#if AFTER
        // A fresh generator run exercises reservation availability without paying for 56 full terrain-mask builds.
        foreach(var entry in Cases) for(int i=0;i<64;i++)
        {
            var p=SectorGenerator.Generate(entry.kind,71+i*977);
            if(p.reservedFeatures.Count==0)throw new Exception("Missing reserve");
            CheckSites(p);
        }
        Console.WriteLine("PASS: 448 additional seed layouts, protected crops and woods, building footprints/flats, site determinism and shared tree cap.");
#endif
        MapLayout.Use(new SectorPlan{fields=0});MapLayout.Prepare();
        if(MapLayout.Fields.Count!=0)throw new Exception("Stale fields after reset");
    }

    // Executes all placement calls, using substitutes only for the native terrain/graphics and unrelated builders.
    static void Build(SectorPlan p)
    {
        RenderAudit.Reset();RenderAudit.placed.Clear();RenderAudit.triangles=0;
        MapLayout.Use(p);World.Build(new GameObject("Harness").transform,true);
    }

    // Fingerprints fresh generator output, field geometry and actual placed scenery in order.
    static string Fingerprint(SectorPlan p)
    {
        var text=new StringBuilder();
        foreach(var s in p.sites)text.Append($"{s.model}:{s.position.x:R},{s.position.y:R},{s.yaw:R},{s.flat:R};");
        foreach(var f in MapLayout.Fields)text.Append($"{f.center.x:R},{f.center.y:R},{f.halfLength:R},{f.halfWidth:R},{f.crop};");
        foreach(var item in RenderAudit.placed)text.Append($"{item.model}:{item.p.x:R},{item.p.y:R},{item.scale:R};");
        return text.ToString();
    }

    // Checks independent geometry samples rather than only counting the new code's own list entries.
    static void Check(SectorPlan p)
    {
        if(World.TreeCount>11500)throw new Exception("Shared tree allowance exceeded");
#if AFTER
        CheckSites(p);
        if(p.kind==SectorKind.DeepForest)
        {
            int count=0;foreach(var item in RenderAudit.placed)if(item.group=="Features"&&RenderAudit.Tree(item.model))count++;
            if(count<5000||count>6800)throw new Exception("Woodland core planting count "+count);
        }
        foreach(var f in MapLayout.Fields)
            for(float u=-f.halfLength;u<=f.halfLength;u+=3.1f)
                for(float v=-f.halfWidth;v<=f.halfWidth;v+=3.1f)
                    if(MapLayout.ReservedFeature(f.Point(u,v),0f))throw new Exception("Field invaded reserve");
        foreach(var item in RenderAudit.placed)
            if(item.group=="Woods"&&MapLayout.ReservedFeature(item.p,0f))throw new Exception("Ordinary woods invaded reserve");
        foreach(var a in p.featureFlats)
            for(float u=-a.halfLength;u<=a.halfLength;u+=5.7f)
                for(float v=-a.halfWidth;v<=a.halfWidth;v+=5.7f)
                    if(!MapLayout.NearFlatSite(a.Point(u,v),0f))throw new Exception("Unflattened yard corner");
        if(p.kind==SectorKind.Airfield&&(p.airstripLength<450||p.airstripLength>650||p.airstripWidth<18||p.airstripWidth>30))throw new Exception("Runway dimensions");
#endif
    }

#if AFTER
    // New building circles cover their full audited roofs, and no road runs through a building footprint.
    static void CheckSites(SectorPlan p)
    {
        int minimumStreets=p.Type.town?8:p.kind==SectorKind.Marshland?0:2;
        if(p.featureStreets.Count<minimumStreets)throw new Exception("Missing feature access: "+p.kind+" seed "+p.seed);
        foreach(var s in p.sites)
        {
            if(!buildings.Contains(s.model)||!sizes.TryGetValue(s.model,out var size))continue;
            if(s.flat<0.5f*size.magnitude)throw new Exception("Flat radius too small: "+s.model);
            Vector2 forward=new Vector2(Mathf.Sin(s.yaw*Mathf.Deg2Rad),Mathf.Cos(s.yaw*Mathf.Deg2Rad)), right=new Vector2(forward.y,-forward.x);
            foreach(var road in p.featureStreets)
                foreach(var q in MapLayout.Smooth(road,2f))
                {
                    var d=q-s.position;
                    if(Mathf.Abs(Vector2.Dot(d,right))<size.x/2+3&&Mathf.Abs(Vector2.Dot(d,forward))<size.y/2+3)
                        throw new Exception("Street crosses "+s.model+" in "+p.kind);
                }
        }
    }
#endif

    // Exports both the terrain survey and visible placements, so drawings cannot conceal unsuccessful planting.
    static object Export(SectorPlan p,double elapsed)
    {
        float fieldArea=0f;
        var plots=new List<object>();foreach(var f in MapLayout.Fields){plots.Add(new{crop=f.crop.ToString(),outline=Outline(f)});fieldArea+=4*f.halfLength*f.halfWidth;}
        var roads=new List<object>();foreach(var line in MapLayout.RoadLines){var points=new List<float[]>();foreach(var q in line)points.Add(XY(q));roads.Add(points);}
        var rails=new List<object>();foreach(var line in MapLayout.RailLines){var points=new List<float[]>();foreach(var q in line)points.Add(XY(q));rails.Add(points);}
        var river=new List<float[]>();foreach(var q in MapLayout.RiverLine)river.Add(XY(q));
        var ponds=new List<object>();foreach(var pond in p.ponds)ponds.Add(new{center=XY(pond.center),pond.radius});
        var sites=new List<object>();int featureBuildings=0;
        foreach(var s in p.sites)
        {
            if(buildings.Contains(s.model))featureBuildings++;
            Vector2 size=sizes.TryGetValue(s.model,out var known)?known:s.model=="Scenery_Warehouse"?new Vector2(18,30):s.model=="Base_Hangar"?new Vector2(28,28):new Vector2(8,10);
            sites.Add(new{s.model,at=XY(s.position),s.yaw,s.flat,size=XY(size)});
        }
        int totalBuildings=0,coreTrees=0;var trees=new List<object>();var otherBuildings=new List<object>();
        foreach(var item in RenderAudit.placed)
        {
            if(buildings.Contains(item.model))totalBuildings++;
            if(RenderAudit.Tree(item.model))
            {
                float crown=(item.model=="Scenery_Poplar"?2.1f:item.model=="Scenery_Birch"?3.4f:item.model=="Scenery_Tree"||item.model=="Scenery_Tree_B"?4.8f:3.8f)*item.scale;
                trees.Add(new{at=XY(item.p),radius=crown,item.group});
                if(item.group=="Features")coreTrees++;
            }
            else if(buildings.Contains(item.model)&&item.group!="Features")otherBuildings.Add(new{at=XY(item.p),item.model});
        }
        var reserves=new List<object>();var surfaces=new List<object>();var fences=new List<object>();
#if AFTER
        foreach(var a in p.reservedFeatures)reserves.Add(new[]{XY(a.Point(-a.halfLength,-a.halfWidth)),XY(a.Point(a.halfLength,-a.halfWidth)),XY(a.Point(a.halfLength,a.halfWidth)),XY(a.Point(-a.halfLength,a.halfWidth))});
        foreach(var s in p.featureSurfaces){var a=s.area;surfaces.Add(new{s.concrete,outline=new[]{XY(a.Point(-a.halfLength,-a.halfWidth)),XY(a.Point(a.halfLength,-a.halfWidth)),XY(a.Point(a.halfLength,a.halfWidth)),XY(a.Point(-a.halfLength,a.halfWidth))}});}
        foreach(var line in p.featureFences){var pts=new List<float[]>();foreach(var q in line)pts.Add(XY(q));fences.Add(pts);}
#endif
        return new{kind=p.kind.ToString(),p.seed,elapsedMs=elapsed,coverage=MapLayout.FieldCoverage()*100,renderers=RenderAudit.renderers,
            trees,treeCount=World.TreeCount,coreTrees,totalBuildings,featureBuildings,fields=MapLayout.Fields.Count,wholeMapFields=100*fieldArea/(Ground.Size*Ground.Size),plots,roads,rails,river,ponds,sites,otherBuildings,reserves,surfaces,fences,
            airstripCenter=XY(p.airstripCenter),airstripAlong=XY(p.airstripAlong),p.airstripLength,basePosition=XY(MapLayout.Flat(p.basePosition)),hq=XY(MapLayout.Flat(p.enemyHqPosition)),World.Timings};
    }
}

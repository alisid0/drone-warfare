using System;
using Random = System.Random;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// One scene per process keeps managed geometry review far below a Unity terrain/import footprint.
static partial class LivedInHarness
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;

    // Select five fixed, independently planned villages and the two requested sector views without opening Unity.
    static int Main(string[] args)
    {
        try { return Run(args); }
        catch(Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }

    // Fail with a bounded diagnostic rather than invoking Windows crash reporting from an unattended harness.
    static int Run(string[] args)
    {
        string scene = args.Length > 0 ? args[0] : "Village4";
        string output = args.Length > 1 ? args[1] : "Temp/lived-in/review";
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        WorldPlan.Generate(1200);
        var data = WorldPlan.CaptureDetail(null);
        if (scene.StartsWith("Landmark")) { ExportLandmark(scene.Substring(8), output); return 0; }
        if (scene == "Checks") { Check(data, output); return 0; }
        SectorPlan sector;
        if(scene=="VillageLegacy") sector=SectorPlan.Varrow();
        else if (scene.StartsWith("Village"))
        {
            int id = int.Parse(scene.Substring(7));
            sector = WorldSectors.PlanAt(WorldPlan.Settlements[id].center, SectorKind.RiverValley, 2100+id);
        }
        else
        {
            var hotspot = new Hotspot { kind = scene == "Field" ? SectorKind.RiverValley : SectorKind.TownOutskirts, seed = scene == "Field" ? 1000 : 1111 };
            sector = WorldSectors.PlanAt(WorldSectors.SpotFor(hotspot), hotspot.kind, hotspot.seed);
        }
        MapLayout.Use(sector); MapLayout.Prepare();
        double layout = timer.Elapsed.TotalSeconds; timer.Restart();
        var root = new GameObject("Review").transform;
        Ground.Build(root);
        double ground = timer.Elapsed.TotalSeconds; timer.Restart();
        typeof(World).GetMethod("BuildVillages",Private).Invoke(null,new object[]{root,new Random(sector.worldSeed)});
        double villages = timer.Elapsed.TotalSeconds;
        int[] uses = new int[6]; int cores = 0;
        foreach(var p in MapLayout.VillagePlots) { uses[p.landUse]++; if(p.core) cores++; }
        Console.WriteLine("planned core houses " + cores + "; land uses " + string.Join(",", uses));
        if(scene=="Field")
        {
            timer.Restart(); FieldMargins.Build(root); int blocks=0,triangles=0;
            foreach(var mesh in Mesh.All)if(mesh.name=="Field use 128 m block")
            {
                blocks++;
                foreach(var point in mesh.vertices)if(float.IsNaN(point.x)||float.IsNaN(point.y)||float.IsNaN(point.z))throw new Exception("Invalid field margin vertex");
                foreach(var indices in mesh.subs.Values)triangles+=indices.Length/3;
            }
            Console.WriteLine($"field use: {blocks} blocks, {triangles} triangles, {timer.Elapsed.TotalMilliseconds:F1} ms");
        }
        Vector2 target=Vector2.zero;
        if(scene=="Field")
        {
            float area=0f;
            foreach(var f in MapLayout.Fields)
                if(f.crop==Crop.Wheat && f.halfLength*f.halfWidth>area && f.center.sqrMagnitude<450f*450f)
                { target=f.center;area=f.halfLength*f.halfWidth; }
        }
        if(args.Length>2) { Diagnose(); return 0; }
        Export(output,scene,target,data);
        Console.WriteLine($"{scene}: {World.VillageHouses} houses, {World.VillagePropRenderers} resident prop renderers; layout+world {layout:F2}s, height {ground:F2}s, village {villages:F3}s; peak {Process.GetCurrentProcess().PeakWorkingSet64/1048576} MiB");
        return 0;
    }

    // Review-only reasons separate legitimate reserved sites from mismatched road frontage.
    static void Diagnose()
    {
        var clear=typeof(World).GetMethod("VillagePlotClear",Private);
        var ground=typeof(World).GetMethod("VillageGround",Private);
        foreach(var p in MapLayout.VillagePlots)
        {
            if((bool)clear.Invoke(null,new object[]{p,new List<VillagePlot>()}))continue;
            int nu=Mathf.CeilToInt(p.frontage/3f),nv=Mathf.CeilToInt(p.depth/3f);
            bool found=false;
            for(int j=0;j<=nv&&!found;j++)for(int i=0;i<=nu&&!found;i++)
            {
                var q=p.Point(p.frontage*(i/(float)nu-.5f),p.depth*j/nv);
                if(!p.Contains(q))continue;
                float road=MapLayout.RoadField.Query(q.x,q.y,out _,out int segment);
                bool land=(bool)ground.Invoke(null,new object[]{q});float track=MapLayout.TrackField.Distance(q.x,q.y);
                if(land&&road>=MapLayout.RoadWidthAt(q.x,q.y)+.7f&&track>=2.5f)continue;
                found=true;
                Console.WriteLine($"plot {p.front.x:F1},{p.front.y:F1} old {p.old} farm {p.farm} sample {q.x:F1},{q.y:F1} ground {land} road {road:F2}/{MapLayout.RoadWidthAt(q.x,q.y):F1} track {track:F1} field {MapLayout.InField(q,1.5f)} site {MapLayout.NearFlatSite(q,2f)}");
                if(segment>=0){var a=MapLayout.RoadField.points[segment];var b=MapLayout.RoadField.points[segment+1];Console.WriteLine($"nearest {a.x:F2},{a.y:F2} -> {b.x:F2},{b.y:F2}");}
            }
        }
    }

    // Independent counts and sampled areas expose mismatches hidden by rendering a particularly favourable seed.
    static void Check(FarDetailData data,string output)
    {
        foreach(var p in data.villages.plots)CheckAccess(p,data.villages);
        var report=new List<object>();
        for(int owner=4;owner<WorldPlan.Settlements.Count;owner++)
        {
            int n=0,old=0,shaped=0,farm=0,cultivated=0,sheds=0,logs=0,untended=0,roofs=0,overlap=0;
            float used=0f,area=0f;var props=new List<VillagePlot>();
            foreach(var p in data.villages.plots)if(p.owner==owner&&!p.square&&!p.church)props.Add(p);
            foreach(var p in props)
            {
                n++;if(p.old)old++;if(p.shape>0)shaped++;if(p.farm)farm++;if(p.cultivated)cultivated++;
                if(p.shed)sheds++;if(p.logs)logs++;if(p.untended)untended++;if(p.roofChanged)roofs++;
                if(p.pathWidth<1f||p.pathWidth>1.8f||p.driveWidth<2.5f||p.driveWidth>3.5f)throw new Exception("Access width");
                if(p.cultivated&&p.untended)throw new Exception("Untended garden is cultivated");
                if(p.Scale<.89f||p.Scale>1.11f)throw new Exception("Uniform building scale");
                if(!data.villages.At(p.House,out var found)||(found.front-p.front).sqrMagnitude>.1f)throw new Exception("Plot ownership");
                for(float v=.5f;v<p.depth;v+=1f)for(float u=-p.frontage*.5f+.5f;u<p.frontage*.5f;u+=1f)
                {
                    if(!p.Contains(p.Point(u,v)))continue;
                    area++;int use=p.UseAt(u,v);if(use==1||use==2)used++;
                }
            }
            for(int i=0;i<props.Count;i++)for(int j=i+1;j<props.Count;j++)if(VillagePlan.Intersects(props[i],props[j]))overlap++;
            if(overlap!=0)throw new Exception("Overlapping planned properties");
            foreach(var p in props)if(p.core)
            {
                float wall=1.8f+p.setback-3.35f*p.Scale;
                if(p.frontage<8f||p.frontage>16f||wall<2f||wall>6f||!p.shed)throw new Exception("Core frontage/setback/outbuilding");
            }
            report.Add(new{owner,name=WorldPlan.Settlements[owner].name,n,old,shaped,farm,cultivated,sheds,logs,untended,roofs,overlap,worn=used/area});
        }
        File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(report,Json));
        string fingerprint=Fingerprint(data.villages);
        WorldPlan.Generate(1200);
        if(fingerprint!=Fingerprint(WorldPlan.CaptureDetail(null).villages))throw new Exception("Village generation is not repeatable");
        WorldPlan.Generate(1201);
        if(fingerprint!=Fingerprint(data.villages))throw new Exception("An old worker snapshot changed");
        File.WriteAllText(Path.Combine(output,"determinism.txt"),"Repeated seed 1200 matched; retained snapshot survived seed 1201.\n"+fingerprint+"\n");
        Console.WriteLine(JsonSerializer.Serialize(report,Json));
    }

    // Town and village frontages must both recover their original street anchor, including different verge widths.
    static void CheckAccess(VillagePlot p,VillagePlan plan)
    {
        Vector2 access=p.Point(0f,-p.StreetOffset);float roadDistance=float.MaxValue;
        foreach(var road in WorldPlan.Roads)for(int i=1;i<road.points.Count;i++)roadDistance=Math.Min(roadDistance,Distance(access,road.points[i-1],road.points[i]));
        foreach(var lane in plan.lanes)roadDistance=Math.Min(roadDistance,Distance(access,new Vector2(lane.ax,lane.az),new Vector2(lane.bx,lane.bz)));
        if(roadDistance>.05f)throw new Exception("Property is disconnected from the street network: "+p.owner+" at "+roadDistance);
    }

    // Hash every immutable property field, rather than trusting aggregate counts to prove determinism.
    static string Fingerprint(VillagePlan plan)
    {
        var bytes=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan.plots,
            new JsonSerializerOptions { IncludeFields=true, IgnoreReadOnlyProperties=true }));
        using(var hash=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(bytes));
    }

    // Independent finite-segment distance verifies access against actual world roads and connected lane vectors.
    static float Distance(Vector2 p,Vector2 a,Vector2 b)
    {
        Vector2 d=b-a;float t=Math.Max(0f,Math.Min(1f,Vector2.Dot(p-a,d)/Math.Max(.0001f,d.sqrMagnitude)));
        return (p-a-d*t).magnitude;
    }

    // Export actual garden meshes and model placements; no Blender-side layout or synthetic scenery is added.
    static void Export(string output,string name,Vector2 target,FarDetailData data)
    {
        var models=new List<object>();
        foreach(var item in Art.placed)
        {
            Vector3 p=item.root.position,f=item.root.rotation.forward,s=item.mesh.transform.localScale;
            int palette=0,roof=0;
            foreach(var plot in MapLayout.VillagePlots)if((plot.House-new Vector2(p.x,p.z)).sqrMagnitude<1f)
            {palette=(plot.settlement+plot.palette)%5;roof=(plot.settlement+(plot.roofChanged?1+plot.palette%3:0))%4;break;}
            models.Add(new{model=item.model,position=new[]{p.x,p.y,p.z},facing=new[]{f.x,f.y,f.z},scale=new[]{s.x,s.y,s.z},palette,roof});
        }
        var meshes=new List<object>();int nearTriangles=0,farTriangles=0;
        foreach(var m in Mesh.All)if(m.name!=null&&(m.name.StartsWith("Village plots LOD")||m.name=="Field use 128 m block"))
        {
            var v=new List<float[]>();foreach(var p in m.vertices)v.Add(new[]{p.x,p.y,p.z});
            foreach(var indices in m.subs.Values)if(m.name.StartsWith("Village plots LOD")){if(m.name.EndsWith("0"))nearTriangles+=indices.Length/3;else farTriangles+=indices.Length/3;}
            meshes.Add(new{name=m.name,vertices=v,triangles=m.subs});
        }
        var heights=new List<float>();
        for(int z=0;z<=200;z++)for(int x=0;x<=200;x++)heights.Add(Ground.HeightAt(new Vector3(x*10-1000,0,z*10-1000)));
        var roads=new List<List<float[]>>();foreach(var line in MapLayout.RoadLines)
        {var points=new List<float[]>();foreach(var p in line)points.Add(new[]{p.x,p.y});roads.Add(points);}
        var plots=new List<object>();foreach(var p in MapLayout.VillagePlots)plots.Add(new{front=new[]{p.front.x,p.front.y},along=new[]{p.along.x,p.along.y},outward=new[]{p.outward.x,p.outward.y},p.landUse,p.core,p.old,p.farm,p.shape,p.frontage,p.depth,
            accepted=p.landUse>0 && (bool)typeof(World).GetMethod("VillagePlotClear",Private).Invoke(null,new object[]{p,new List<VillagePlot>()})});
        var boundaries=new List<object>();foreach(var e in MapLayout.Boundaries)
        {var a=e.Point(0f);var b=e.Point(1f);var gate=e.hasGate?e.gate:FieldAppearance.Gate(e.a,e.b);
            boundaries.Add(new{a=new[]{a.x,a.y},b=new[]{b.x,b.y},gate=new[]{gate.x,gate.y},kind=e.kind.ToString()});}
        File.WriteAllText(Path.Combine(output,name+".json"),JsonSerializer.Serialize(new{models,meshes,heights,roads,plots,boundaries,target=new[]{target.x,target.y},nearTriangles,farTriangles},Json));
        // A bounded 2 m paint crop uses the real shared village/field appearance at sector coordinates.
        using(var w=new BinaryWriter(File.Create(Path.Combine(output,name+".ppm"))))
        {
            const int n=1000;w.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{n} {n}\n255\n"));
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                Vector2 q=new Vector2(x*2f-999f,z*2f-999f),world=q+(MapLayout.Plan.world!=null?MapLayout.Plan.world.center:Vector2.zero);
                Vector3 colour=FarDetailPalette.Means[0];
                bool field=false;
                foreach(var f in MapLayout.Fields)if(f.Contains(q,0f))
                {
                    FarFieldSurface.TryParent(f,out var parent);
                    colour=FieldAppearance.Colour(5+(int)f.crop,world.x,world.y,FieldAppearance.Edge(parent,q),2f);field=true;break;
                }
                if(!field)
                {
                    if(MapLayout.Plan.world!=null && MapLayout.DetailPlan.villages.At(world,out var p)){p.Coordinates(world,out float u,out float v);colour=p.Colour(u,v,2f,colour);}
                    else if(MapLayout.Plan.world==null)foreach(var plot in MapLayout.VillagePlots)if(plot.Contains(q)) {plot.Coordinates(q,out float u,out float v);colour=plot.Colour(u,v,2f,colour);break;}
                }
                float edge=MapLayout.RoadField.Distance(q.x,q.y)-MapLayout.RoadWidthAt(q.x,q.y);
                float cover=FarDetailData.Coverage(-edge,2f);
                colour+=(FarDetailPalette.Decode(new Color(.35f,.32f,.25f))-colour)*cover;
                var c=FarDetailPalette.Encode(colour);w.Write(c.r);w.Write(c.g);w.Write(c.b);
            }
        }
        Console.WriteLine($"geometry: {nearTriangles} near / {farTriangles} far triangles");
    }
}

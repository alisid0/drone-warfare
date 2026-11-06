using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using Random = System.Random;

// Bounded data checks and real production geometry exports run without a native Unity scene or Library.
static class RoadHarness
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

    // Each invocation exports one scene or a small material/check set, keeping peak memory below a sector editor.
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    // Compile-time engine substitutes retain the game's actual planning, terrain equations and surface meshes.
    static void Run(string[] args)
    {
        string scene = args.Length > 0 ? args[0] : "Checks", output = args.Length > 1 ? args[1] : "Temp/roads/review";
        Directory.CreateDirectory(output);
        if (scene == "Textures") { Textures(output); return; }
        if (scene == "Checks") { Checks(output); return; }
        var timer = Stopwatch.StartNew();
        WorldPlan.Generate(1200);
        var data = WorldPlan.CaptureDetail(null);
        SectorPlan sector;
        if (scene == "Village") sector = WorldSectors.PlanAt(WorldPlan.Settlements[6].center, SectorKind.RiverValley, 2106);
        else if(scene=="Main")
        {
            WorldRoad highway=null;foreach(var r in WorldPlan.Roads)if(r.kind==RoadClass.Highway){highway=r;break;}
            sector=WorldSectors.PlanAt(highway.points[highway.points.Count/2],SectorKind.RiverValley,1000);
        }
        else
        {
            var spot = new Hotspot { kind = SectorKind.RiverValley, seed = 1000 };
            sector = WorldSectors.PlanAt(WorldSectors.SpotFor(spot), spot.kind, spot.seed);
        }
        MapLayout.Use(sector); MapLayout.Prepare();
        double layout = timer.Elapsed.TotalMilliseconds; timer.Restart();
        var root = new GameObject("Road review").transform;
        Ground.Build(root);
        double terrain = timer.Elapsed.TotalMilliseconds;
        RoadSurface.Build(root);
        int allowance=480;Roadside.Build(root,ref allowance);
        typeof(World).GetMethod("BuildVillages", BindingFlags.NonPublic|BindingFlags.Static).Invoke(null, new object[]{root, new Random(sector.worldSeed)});
        var meshes = new List<object>();
        foreach (var m in Mesh.All)
            if (m.name != null && (m.name.StartsWith("Road") || m.name.StartsWith("Village plots LOD")))
            {
                var vertices = new List<float[]>(); foreach (var p in m.vertices) vertices.Add(new[]{p.x,p.y,p.z});
                var uv = new List<float[]>(); if (m.uv != null) foreach(var p in m.uv) uv.Add(new[]{p.x,p.y});
                meshes.Add(new { m.name, vertices, uv, triangles = m.subs });
            }
        var models = new List<object>();
        foreach (var item in Art.placed)
        {
            Vector3 p = item.root.position, f = item.root.rotation.forward, s = item.mesh.transform.localScale;
            models.Add(new{model=item.model,position=new[]{p.x,p.y,p.z},facing=new[]{f.x,f.y,f.z},scale=new[]{s.x,s.y,s.z},palette=0,roof=0});
        }
        var roadside = new List<object>();
        foreach (var p in Roadside.Review) roadside.Add(new{model=p.name,position=new[]{p.p.x,p.p.y,p.p.z},facing=new[]{p.dir.x,0f,p.dir.y},scale=new[]{p.scale,p.scale,p.scale},palette=0,roof=0});
        var heights = new List<float>();
        for (int z=0;z<=400;z++) for(int x=0;x<=400;x++) heights.Add(Ground.HeightAt(new Vector3(x*5-1000,0,z*5-1000)));
        var boundaries = new List<object>();
        foreach(var e in MapLayout.Boundaries)
        { var a=e.Point(0f);var b=e.Point(1f);var g=e.hasGate?e.gate:FieldAppearance.Gate(e.a,e.b);boundaries.Add(new{a=new[]{a.x,a.y},b=new[]{b.x,b.y},gate=new[]{g.x,g.y},kind=e.kind.ToString()}); }
        var targets = Targets(scene);
        var entrances=new List<object>();foreach(var e in RoadSurface.Entrances)entrances.Add(new{gate=new[]{e.gate.x,e.gate.y},road=new[]{e.road.x,e.road.y},e.layby});
        File.WriteAllText(Path.Combine(output,scene+".json"),JsonSerializer.Serialize(new{meshes,models,roadside,heights,boundaries,targets,roads=MapLayout.Roads.segments,entrances},Json));
        Audit(output,scene);
        Console.WriteLine(JsonSerializer.Serialize(new {scene,layoutMs=layout,groundMs=terrain,roadMs=RoadSurface.BuildMilliseconds,
            renderers=RoadSurface.Renderers,triangles=RoadSurface.Triangles,gates=RoadSurface.Entrances.Count,trees=Roadside.Trees,poles=Roadside.Poles,furnitureRenderers=Roadside.ChargedRenderers,peakMiB=Process.GetCurrentProcess().PeakWorkingSet64/1048576},Json));
        if(args.Length>2&&args[2]=="geometry")return;
        // A 2 m base is shared by both reviews; only the road layer changes between before and after.
        foreach (string phase in new[]{"before","after"})
        {
            Console.WriteLine(scene+" painting "+phase);Console.Out.Flush();
            Image(Path.Combine(output,scene+"-"+phase+".ppm"),1000,1000,(x,z)=>
            {
                Vector2 local=new Vector2(x*2f-999f,z*2f-999f), world=local+sector.world.center;
                Vector3 c=FarDetailPalette.Decode(data.Paint(world.x,world.y,world,new FarDetailData.Segment[0],null,2f));
                // Base capture uses the original cover/field/village painter with its road stage suppressed in prepare.py.
                var road=MapLayout.Roads.At(local.x,local.y);
                if(phase=="before")
                {
                    if(road.found&&road.edge<4f)c=FarDetailPalette.Decode(Ground.ReviewBefore(local.x,local.y));
                }
                else c=RoadAppearance.Paint(c,road,2f,data.CoverAt(world.x,world.y)==Cover.Farmland);
                return FarDetailPalette.Encode(c);
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(new {scene,layoutMs=layout,groundMs=terrain,roadMs=RoadSurface.BuildMilliseconds,
            renderers=RoadSurface.Renderers,triangles=RoadSurface.Triangles,gates=RoadSurface.Entrances.Count,trees=Roadside.Trees,poles=Roadside.Poles,furnitureRenderers=Roadside.ChargedRenderers,peakMiB=Process.GetCurrentProcess().PeakWorkingSet64/1048576},Json));
    }

    // Compare clipped routes with their immutable world source and reject inverted or invalid surface triangles.
    static void Audit(string output,string scene)
    {
        float offset=0f,chainage=0f;int shared=0,triangles=0;
        Vector2 origin=MapLayout.Plan.world.center;
        foreach(var s in MapLayout.Roads.segments)
        {
            if(s.route<0)continue;
            Vector2 a=new Vector2(s.ax,s.az),b=new Vector2(s.bx,s.bz),mid=(a+b)*.5f+origin;
            var expected=MapLayout.DetailPlan.roadNetwork.RouteAt(mid.x,mid.y,s.route);
            if(!expected.found)throw new Exception("Missing shared route");
            offset=Math.Max(offset,expected.distance);chainage=Math.Max(chainage,Math.Abs(expected.along-(s.walked+(b-a).magnitude*.5f)));shared++;
        }
        if(offset>.15f||chainage>.16f)throw new Exception($"Sector/world road mismatch: {offset}, {chainage}");
        foreach(var m in Mesh.All)if(m.name!=null&&m.name.StartsWith("Road"))foreach(var indices in m.subs.Values)
            for(int i=0;i<indices.Length;i+=3)
            {
                Vector3 a=m.vertices[indices[i]],b=m.vertices[indices[i+1]],c=m.vertices[indices[i+2]];
                if(float.IsNaN(a.x)||float.IsNaN(a.y)||float.IsNaN(a.z)||Vector3.Cross(b-a,c-a).y<-.0001f)throw new Exception("Invalid road mesh");
                triangles++;
            }
        if(RoadSurface.Renderers>RoadSurface.RendererReserve||Roadside.ChargedRenderers>480||Roadside.Trees>96||Roadside.Poles>96)throw new Exception("Road budget cap");
        var times=new double[2];int checksum=0;
        for(int phase=0;phase<2;phase++)
        {
            var timer=Stopwatch.StartNew();
            for(int z=0;z<250;z++)for(int x=0;x<250;x++)
            {var c=phase==0?Ground.ReviewBefore(x*8f-996,z*8f-996):Ground.ReviewAfter(x*8f-996,z*8f-996);checksum+=c.r;}
            times[phase]=timer.Elapsed.TotalMilliseconds;
        }
        var report=new{sharedSegments=shared,maxOffsetMetres=offset,maxChainageErrorMetres=chainage,triangles,splat62500BeforeMs=times[0],splat62500AfterMs=times[1],checksum};
        File.WriteAllText(Path.Combine(output,scene+"-audit.json"),JsonSerializer.Serialize(report,Json));Console.WriteLine(JsonSerializer.Serialize(report,Json));
    }

    // Fixed production locations provide a main road, a true junction, a track and an inhabited street.
    static Dictionary<string,float[]> Targets(string scene)
    {
        var result=new Dictionary<string,float[]>();
        var scores=new Dictionary<string,float>();
        if(scene=="Village")
        {
            Vector2 sum=Vector2.zero;int n=0;
            foreach(var p in MapLayout.VillagePlots)if(p.core){sum+=p.front;n++;}
            if(n>0)sum/=n;
            result.Add("VillageStreet",new[]{sum.x,sum.y});return result;
        }
        foreach(var s in MapLayout.Roads.segments)
        {
            Vector2 p=new Vector2((s.ax+s.bx)*.5f,(s.az+s.bz)*.5f);
            if(Math.Max(Math.Abs(p.x),Math.Abs(p.y))>700f)continue;
            string name=s.kind==(int)RoadClass.Highway?"MainRoad":s.kind==(int)RoadClass.Track?"FarmTrack":null;
            if(scene=="Main"&&name!="MainRoad")continue;
            float score=p.sqrMagnitude+(s.route<0?1000000f:0f);
            if(name!=null&&(!scores.ContainsKey(name)||score<scores[name])){result[name]=new[]{p.x,p.y};scores[name]=score;}
            if(scene!="Main"&&s.kind!=(int)RoadClass.Track&&MapLayout.Roads.At(p.x,p.y).junction>.5f&&(!scores.ContainsKey("Junction")||score<scores["Junction"]))
            {result["Junction"]=new[]{p.x,p.y};scores["Junction"]=score;}
        }
        return result;
    }

    // Actual production material equations are exported once; Unity samples these small, immutable textures.
    static void Textures(string output)
    {
        foreach(var kind in new[]{RoadClass.Highway,RoadClass.Road,RoadClass.Track})
        {
            string name=kind==RoadClass.Highway?"RoadHighway":kind==RoadClass.Road?"RoadLane":"RoadTrack";
            float half=RoadAppearance.HalfWidth(kind);
            Image(Path.Combine(output,name+".ppm"),128,768,(x,y)=>FarDetailPalette.Encode(RoadAppearance.Surface(kind,(x+.5f)/128f*half*2f-half,(y+.5f)/768f*RoadAppearance.Repeat,half,half*2f/128f)));
        }
    }

    // Width, dash energy, finite endpoints and indexed versus brute-force queries exercise failure-prone contracts.
    static void Checks(string output)
    {
        for(float x=-48f;x<48f;x+=.125f)
            if(Math.Abs(RoadAppearance.Dash(x,12f)-.25f)>.00001f)throw new Exception("Dash duty cycle");
        for(float x=-100f;x<400f;x+=.13f)
        {float w=RoadAppearance.TrackWidth(x)*2f;if(w<2.499f||w>3.501f)throw new Exception("Track width");}
        for(float along=0;along<192;along+=1f)
        {
            if(RoadAppearance.Ruts(0,along,.2f)!=0f)throw new Exception("Grass centre lost");
            float sum=0;for(float x=-2;x<2;x+=.01f)sum+=RoadAppearance.Ruts(x,along,.01f)*.01f;
            if(sum<.799f||sum>1.201f)throw new Exception("Rut width/energy");
        }
        var spans=new[]{new FarDetailData.Segment{ax=-200,az=0,bx=200,bz=0,widthA=3.5f,widthB=3.5f,kind=0,route=1},
            new FarDetailData.Segment{ax=0,az=0,bx=0,bz=100,widthA=2,widthB=2,kind=1,route=2}};
        var network=new RoadNetwork(spans);
        var corner=network.At(2.4f,3.8f);
        if(corner.edge>=0)throw new Exception("Junction has no corner splay");
        if(network.At(220,0).edge<0)throw new Exception("Finite road endpoint leaks");
        if(network.At(-64.01f,2f).kind!=RoadClass.Highway||network.At(-63.99f,2f).kind!=RoadClass.Highway)throw new Exception("Bucket seam");
        var saved=network.RouteAt(-100,0,1);spans[0].az=999;
        if(network.RouteAt(-100,0,1).along!=saved.along)throw new Exception("Caller mutated snapshot");
        float worst=0f;
        foreach(float pixel in new[]{2f,4f,13.671875f})
            for(float across=-8f;across<=8f;across+=2f)
            {
                Vector3 sum=Vector3.zero;const int samples=32;
                for(int z=0;z<samples;z++)for(int x=0;x<samples;x++)
                {
                    var q=network.At(60f+(z+.5f)/samples*pixel-pixel*.5f,across+(x+.5f)/samples*pixel-pixel*.5f);
                    sum+=RoadAppearance.Paint(FarDetailPalette.Means[0],q,pixel/samples,false);
                }
                var coarse=RoadAppearance.Paint(FarDetailPalette.Means[0],network.At(60f,across),pixel,false);
                worst=Math.Max(worst,(coarse-sum/(samples*samples)).magnitude);
            }
        if(worst>.09f)throw new Exception("Filtered road energy: "+worst);
        string report="Passed: 3/9 dash energy (including negative chainage), 2.5–3.5 m track envelope, 0.4–0.6 m rut widths, grass centre, junction splay, finite caps and bucket seam.\n";
        report+=$"Immutable segment snapshot; coarse versus 32x32 quadrature max linear RGB distance {worst:F6} (2 m / 4 m / 13.67 m).\n";
        File.WriteAllText(Path.Combine(output,"checks.txt"),report);Console.WriteLine(report);
    }

    // PPM avoids a native image dependency in the assembly harness; the small Python packaging step writes PNGs.
    static void Image(string path,int width,int height,Func<int,int,Color32> sample)
    {
        using(var w=new BinaryWriter(File.Create(path)))
        {w.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));for(int z=0;z<height;z++)for(int x=0;x<width;x++){var c=sample(x,z);w.Write(c.r);w.Write(c.g);w.Write(c.b);}}
    }
}

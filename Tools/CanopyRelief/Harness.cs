using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;
using UnityEngine.Rendering;

// Execute the production scheduler at a locked strategy view, then export its actual submitted trees and canopy.
static class Harness
{
    static Camera feed;
    static readonly List<Vector3> startupRims=new List<Vector3>();
    // One sector per process bounds terrain memory and makes baseline/candidate runs independent.
    static void Main(string[] args)
    {
        try { Run(args); }
        catch(Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }

    // Keep a failing check from waiting in Windows crash reporting with the harness assembly locked.
    static void Run(string[] args)
    {
        string output = args[0]; Directory.CreateDirectory(output);
        bool preview=args[1]=="preview";
        if(!preview)WorldPlan.Generate(1200);
        else FarTerrain.Active=false;
        SectorPlan plan;
        if (args[1].StartsWith("varrow") || preview)
        {
            plan = SectorPlan.Varrow();
            if(preview)FarTerrain.Origin=Vector2.zero;
            else
            {
                var dam=WorldPlan.Dams[0]; var origin=(dam.a+dam.b)/2f-plan.damPoint;
                FarTerrain.Origin=new Vector2(Mathf.RoundToInt(origin.x/250f)*250f,Mathf.RoundToInt(origin.y/250f)*250f);
            }
        }
        else
        {
            var kind = args[1] == "border" ? SectorKind.BorderLine : SectorKind.DeepForest;
            var origin = WorldSectors.SpotFor(new Hotspot { kind = kind, seed = 1037 });
            FarTerrain.Origin = origin; plan = WorldSectors.PlanAt(origin, kind, 1037);
        }
        MapLayout.Use(plan); World.Build(new GameObject("Forest harness").transform, true);
#if POLISH
        CheckRecipe(output);
#endif
        if(plan.world==null)CheckPrepared(output);
        var scatter = new GameObject("Far Terrain").AddComponent<FarScatter>();
        typeof(FarScatter).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scatter, null);
        Vector3 target = args[1].StartsWith("varrow") || preview ? plan.basePosition + new Vector3(-120f, 0f, 0f)
            : args[1] == "border" ? Vector3.zero : new Vector3(plan.reservedFeatures[0].center.x, 0f, plan.reservedFeatures[0].center.y);
        target.y = Ground.HeightAt(target);
        var camera = new GameObject("Strategy camera").AddComponent<Camera>();
        camera.transform.position = target + new Vector3(0f, 300f, -300f);
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
        if(args[1].StartsWith("varrow") || preview)
        {
            bool bridge=args[1].Contains("bridge"), farmland=args[1].Contains("farmland");
            target=farmland?plan.FromOld(new Vector3(-20f,0,-30f)):bridge?new Vector3(MapLayout.BridgePoint.x,0,MapLayout.BridgePoint.y):plan.basePosition;
            target.y=Ground.HeightAt(target);
            float range=(farmland?100f:bridge?40f:42f)*Scale.Camera;
            float pitch=Mathf.Lerp(42f,64f,Mathf.InverseLerp(15f*Scale.Camera,110f*Scale.Camera,range))*Mathf.PI/180f;
            float yaw=(farmland?40f:bridge?135f:20f)*Mathf.PI/180f;
            camera.transform.position=target+new Vector3(-Mathf.Sin(yaw)*Mathf.Cos(pitch),Mathf.Sin(pitch),-Mathf.Cos(yaw)*Mathf.Cos(pitch))*range;
            camera.transform.rotation=Quaternion.LookRotation(target-camera.transform.position);
        }
        feed = new GameObject("Drone feed").AddComponent<Camera>(); feed.pixelHeight=270; feed.reach=2500;
        if(preview)CheckPreviewRims(camera,scatter);
        int coverage = -1, refined = -1;
        for (int frame = 0; frame < 600; frame++)
        {
            Tick(camera);
            if(frame==29) Export(output+"-early",plan,target,camera,scatter);
            if (coverage < 0 && ForestHlod.MissingBlocks == 0) coverage = frame;
#if CANDIDATE
            if (refined < 0 && ForestHlod.NearCoarseBlocks == 0 && ForestHlod.MissingBlocks == 0) refined = frame;
#endif
        }
        Export(output, plan, target, camera, scatter);
        foreach (int height in args[1].StartsWith("varrow") || preview ? new int[0] : new[] { 100, 300, 2500 })
        {
            camera.transform.position = target + new Vector3(0, height, -height);
            camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
            for (int frame = 0; frame < 600; frame++) Tick(camera);
            Export(output + "-" + height + "m", plan, target, camera, scatter);
        }
        camera.transform.position = target + new Vector3(0, 300, -300);
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
        for (int frame = 0; frame < 180; frame++) Tick(camera);
        int full = FarScatter.FullTreesDrawn, reduced = FarScatter.ReducedTreesDrawn, cards = FarScatter.CardTreesDrawn;
#if CANDIDATE
        Require(full + reduced + cards > 0, "Strategy camera must submit crowns");
        Require(coverage >= 0 && coverage <= 180, "Initial missing blocks settle within 3 simulated seconds");
        // Record refinement even when the baseline does not settle.
        CheckProjection();
#endif
        var flight = new List<float>(); int blank = 0;
        FarScatter.ResetTimings();
        for (int frame = 0; frame < 240; frame++)
        {
            camera.transform.position += new Vector3(40f / 60f, 0f, 0f); Tick(camera);
            flight.Add(FarScatter.MainThreadMs); if (FarScatter.TreesDrawn == 0) blank++;
            Require(FarScatter.FullTreesDrawn <= ForestBudget.FullLimit && FarScatter.ReducedTreesDrawn <= ForestBudget.ReducedLimit
                && FarScatter.CardTreesDrawn <= ForestBudget.CardLimit && FarScatter.TreeTrianglesDrawn <= ForestBudget.TriangleLimit, "Flight submission budgets");
        }
        flight.Sort();
        float lowP95 = 0f; int lowBlank = 0;
#if CANDIDATE
        // The Swift is at 150 m, but Rts.LookAt grounds its focus before applying the 75 m camera range.
        camera.transform.position = target + new Vector3(-27.87f, 50.18f, -48.27f);
        camera.transform.rotation = Quaternion.LookRotation(new Vector3(27.87f, -50.18f, 48.27f));
        for(int frame=0;frame<360;frame++)Tick(camera);
        FarScatter.ResetTimings();
        var low = new List<float>();
        int peakTriangles=0,peakCards=0,peakFull=0,peakReduced=0,peakCanopyTriangles=0,peakCombinedTriangles=0; double buildSum=0;
        for(int frame=0;frame<360;frame++)
        {
            var p=camera.transform.position; p.x+=240f/60f; p.y=FarScatter.GroundAt(new Vector2(p.x,p.z))+50.18f; camera.transform.position=p;
            Tick(camera);low.Add(FarScatter.MainThreadMs);if(FarScatter.TreesDrawn==0)lowBlank++;
            buildSum+=ForestHlod.BuildMs;
            if(frame>60)
            {
                peakTriangles=Math.Max(peakTriangles,FarScatter.TreeTrianglesDrawn);peakCards=Math.Max(peakCards,FarScatter.CardTreesDrawn);
                peakFull=Math.Max(peakFull,FarScatter.FullTreesDrawn);peakReduced=Math.Max(peakReduced,FarScatter.ReducedTreesDrawn);
                peakCanopyTriangles=Math.Max(peakCanopyTriangles,ForestHlod.TrianglesDrawn);
                peakCombinedTriangles=Math.Max(peakCombinedTriangles,FarScatter.TreeTrianglesDrawn+ForestHlod.TrianglesDrawn);
            }
            Require(FarScatter.FullTreesDrawn<=ForestBudget.FullLimit&&FarScatter.ReducedTreesDrawn<=ForestBudget.ReducedLimit
                &&FarScatter.CardTreesDrawn<=ForestBudget.CardLimit&&FarScatter.TreeTrianglesDrawn<=ForestBudget.TriangleLimit,"Low flight budgets");
        }
        low.Sort();lowP95=low[341];
        var flightCounters=new { peakTriangles,peakCards,peakFull,peakReduced,peakCanopyTriangles,peakCombinedTriangles,buildMean=buildSum/360,
            fastFrames=Counter(typeof(FarScatter),"FastFrames"),selectionStarts=Counter(typeof(FarScatter),"SelectionStarts"),
            selectionCancels=Counter(typeof(FarScatter),"SelectionCancels"),selectionCompletes=Counter(typeof(FarScatter),"SelectionCompletes"),
            refinementStarts=Counter(typeof(ForestHlod),"RefinementsStarted"),cancelledBuilds=Counter(typeof(ForestHlod),"CancelledBuilds"),
            deferred=Counter(typeof(ForestHlod),"DeferredFrames"),generationMean=Counter(typeof(FarScatter),"GenerationMeanMs"),
            selectionMean=Counter(typeof(FarScatter),"SelectionMeanMs"),submissionMean=Counter(typeof(FarScatter),"SubmissionMeanMs"),canopyMean=Counter(typeof(FarScatter),"CanopyMeanMs") };
        Save(output+"/flight.json",flightCounters);
#if POLISH
        Require(peakTriangles<=650000 && peakCards<=1800 && peakFull<=64 && peakReduced<=192,"Fast near-ground camera GPU submission limits");
        Require(FarScatter.FastFrames>=350,"Both cameras recognize accelerated movement");
        Require(ForestHlod.RefinementsStarted<=1,"Fast flight does not churn new refinement builds");
#endif
        camera.transform.rotation=Quaternion.LookRotation(new Vector3(0,-1,1));
#endif
        camera.transform.position += new Vector3(2500f, 2200f, 0f); Tick(camera);
        int jumpCoverage = 0, jumpRefined = -1;
        for (int frame = 0; frame < 600; frame++)
        {
            Tick(camera);
            if (ForestHlod.MissingBlocks > 0) jumpCoverage = frame + 1;
#if CANDIDATE
            if (jumpRefined < 0 && ForestHlod.MissingBlocks == 0 && ForestHlod.NearCoarseBlocks == 0) jumpRefined = frame;
#endif
        }
#if CANDIDATE
        Require(jumpCoverage <= 180 && ForestHlod.MissingBlocks == 0, "Jump missing blocks settle within 3 simulated seconds");
        // Record jump refinement for the native checklist.
        // Material diagnostics have their own existing audit.
#endif
        var metrics = new { sector=args[1], logical=SectorForest.Stems.Count, full, reduced, cards, coverageFrames=coverage,
            nearRefinementFrames=refined, jumpCoverageFrames=jumpCoverage, jumpRefinementFrames=jumpRefined,
            flight=new { managedP95Ms=flight[227], blankFrames=blank },lowFlight=new{metresPerSecond=240,swiftHeight=150f,cameraHeight=50.18f,managedP95Ms=lowP95,blankFrames=lowBlank},nativeUploadsAndGpuMeasured=false };
        Save(output + "/metrics.json", metrics); Console.WriteLine(JsonSerializer.Serialize(metrics));
    }

#if POLISH
    // Audit physical scales on the production recipe, independently of lighting and recording-engine timings.
    static void CheckRecipe(string output)
    {
        int glades=0, count=0; float low=100f, high=0f, radiusLow=100f, radiusHigh=0f;
        double adjacent=0, distant=0;
        for(int z=-2048;z<2048;z+=16)for(int x=-2048;x<2048;x+=16)
        {
            var s=ForestPattern.At(x,z,1f);
            if(ForestPattern.Glade(x,z)==0f)glades++;
            if(s.cover>.99999f){low=Math.Min(low,s.height);high=Math.Max(high,s.height);radiusLow=Math.Min(radiusLow,s.radius);radiusHigh=Math.Max(radiusHigh,s.radius);}
            adjacent+=Math.Abs(s.age-ForestPattern.At(x+8,z,1f).age);
            distant+=Math.Abs(s.age-ForestPattern.At(x+96,z,1f).age);count++;
        }
        Save(output+"/recipe.json",new{sampleCount=count,gladeInteriorFraction=glades/(float)count,minimumStandHeight=low,maximumStandHeight=high,
            minimumStandRadius=radiusLow,maximumStandRadius=radiusHigh,ageDelta8m=adjacent/count,ageDelta96m=distant/count});
        Require(glades/(float)count>=.05f&&glades/(float)count<=.09f,"Glade interiors occupy five to nine percent");
        Require(high-low>=10f&&high-low<=12.01f,"Stand height range supplies approximately plus/minus six metres");
        Require(distant>adjacent*3,"Neighbouring crowns share a correlated age zone");
        int persistent=0,decorative=0;
        foreach(var stem in SectorForest.Stems)
        {
            Require(SectorForest.HasFloorCoverage(new Vector2(stem.position.x,stem.position.z)),"Every logical wood retains its floor");
            Require(stem.stand.radius>=2f,"The canopy fade radius must never exceed an individual crown radius");
        }
        foreach(var tree in Undergrowth.Trees) { if(SectorForest.HasFloorCoverage(tree))persistent++;else decorative++; }
        Save(output+"/floor-input.json",new{persistent,decorative,omittedFromVarrowFloor=MapLayout.Plan.kind==SectorKind.VarrowValley?decorative:0});
    }
#endif

    // Export the scheduler's selected roots; the Blender view substitutes repository FBXs for the recording meshes.
    static void Export(string output, SectorPlan plan, Vector3 target, Camera camera, FarScatter scatter)
    {
        Directory.CreateDirectory(output);
        // A rerun can retain fewer blocks; remove only this export's previous generated canopy artifacts.
        foreach(string path in Directory.GetFiles(output,"canopy-*"))File.Delete(path);
        var views = (IDictionary)Field(scatter, "views"); object view = views[camera];
        var tiers = new Dictionary<(int,int), int>();
        foreach (object draw in (IEnumerable)Field(view, "draws"))
        {
            int tier = (int)Field(draw, "tier");
            foreach (var matrix in (Matrix4x4[])Field(draw, "matrices"))
            {
                var p = matrix.position; var key = ((int)(p.x * 100f), (int)(p.z * 100f));
                tiers[key] = (tiers.TryGetValue(key,out int mask)?mask:0) | (1<<tier);
            }
        }
        var logical = new List<object>();
        foreach (var stem in SectorForest.Stems)
        {
            var p = stem.position; int tier = -1;
            if(tiers.TryGetValue(((int)(p.x*100f),(int)(p.z*100f)),out int mask))
            {
                var limits=(Vector4)Field(view,"distances");
#if CANDIDATE
                float score=FarScatter.CrownScore(p+Vector3.up*(stem.stand.height*.75f),stem.stand.radius,camera.transform.position,camera.transform.forward,Optional(view,"focal"));
                int wanted=score<limits.x*.95f?0:score<limits.y*.95f?1:2;
#else
                float score=Vector3.Distance(camera.transform.position,p);
                int wanted=score<limits.x-5f?0:score<limits.y-10f?1:2;
#endif
                if((mask&(1<<wanted))!=0)tier=wanted;
                else for(int k=0;k<3;k++)if((mask&(1<<k))!=0){tier=k;break;}
            }
            logical.Add(new { p=V(p), stem.stand.species, stem.stand.height, stem.stand.width, stem.stand.radius, stem.stand.shade,
                yaw=ForestPattern.Unit(stem.i,stem.j,44)*360f, tier });
        }
        var heights = new List<float>(); var floor = new List<float>();
        for(int z=0;z<=250;z++) for(int x=0;x<=250;x++)
        { heights.Add(Ground.HeightAt(new Vector3(x*8-1000,0,z*8-1000))); floor.Add(Ground.CanopyAt(x*8-1000,z*8-1000)); }
        var roads = new List<List<float[]>>();
        foreach(var line in plan.roads) { var r=new List<float[]>(); foreach(var p in line) r.Add(new[]{p.x,p.y}); roads.Add(r); }
        var d=(Vector4)Field(view,"distances");
        Save(output+"/scene.json",new { logical,heights,floor,roads,eye=V(camera.transform.position),target=V(target),origin=new[]{FarTerrain.Origin.x,FarTerrain.Origin.y},
            thresholds=new[]{d.x,d.y,d.z},ready=Optional(view,"ready"),focal=Optional(view,"focal") });
        var forest=Field(scatter,"forest"); var blocks=(IDictionary)Field(forest,"blocks"); int count=0;
        foreach(DictionaryEntry entry in blocks)
        {
            var block=entry.Value; var mesh=(Mesh)Field(block,"mesh"); if(mesh==null)continue;
            if(mesh.bounds.min.x>1000||mesh.bounds.max.x< -1000||mesh.bounds.min.z>1000||mesh.bounds.max.z< -1000)continue;
            var v=new List<float[]>();var n=new List<float[]>();var uv=new List<float[]>();
            foreach(var p in mesh.vertices)v.Add(V(p)); foreach(var p in mesh.normals)n.Add(V(p)); foreach(var p in mesh.uv)uv.Add(new[]{p.x,p.y});
            var texture=(Texture2D)Field(block,"texture");
            var canopy=(ForestHlod.Data)Field(block,"data");
#if POLISH
            if(canopy.step<256)CheckSeams(mesh,canopy);
#endif
            var opacity = new List<byte>(); foreach(var pixel in texture.pixels)opacity.Add(pixel.a);
            Save(output+"/canopy-"+count+".json",new { step=canopy.step,vertices=v,normals=n,uv,indices=mesh.triangles,cover=opacity });
            using(var stream=new FileStream(output+"/canopy-"+count+".ppm",FileMode.Create))
            {
                int size=(int)Math.Sqrt(texture.pixels.Length);stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{size} {size}\n255\n"));
                foreach(var p in texture.pixels){stream.WriteByte(p.r);stream.WriteByte(p.g);stream.WriteByte(p.b);}
            }
            count++;
        }
    }

    // Every raised interior edge must belong to two faces; unmatched edges reveal cracks or T-junctions.
    static void CheckSeams(Mesh mesh,ForestHlod.Data data)
    {
        var edges=new Dictionary<(int,int),int>();
        var indices=mesh.triangles;var vertices=mesh.vertices;
        for(int i=0;i<indices.Length;i+=3)for(int side=0;side<3;side++)
        {
            int a=indices[i+side],b=indices[i+(side+1)%3];var key=(Math.Min(a,b),Math.Max(a,b));
            edges[key]=edges.TryGetValue(key,out int n)?n+1:1;
        }
        var corner=data.corner-FarTerrain.Origin;
        foreach(var edge in edges)
        {
            Require(edge.Value<=2,"Canopy faces must not overlap");
            if(edge.Value==2)continue;
            var a=vertices[edge.Key.Item1];var b=vertices[edge.Key.Item2];
            if((a.x==b.x&&(a.x==corner.x||a.x==corner.x+ForestHlod.BlockSize))
                ||(a.z==b.z&&(a.z==corner.y||a.z==corner.y+ForestHlod.BlockSize)))continue;
            float top=Math.Max(a.y-FarScatter.GroundAt(new Vector2(a.x,a.z)),b.y-FarScatter.GroundAt(new Vector2(b.x,b.z)));
            Require(top<.251f,"Raised canopy edge must be stitched: "+a.x+","+a.z+" to "+b.x+","+b.z);
        }
    }

    // Sample every dense 50 m woodland patch against the startup mesh, before any camera can refine it.
    static void CheckPrepared(string output)
    {
        var patches=new Dictionary<(int,int),SectorForest.Stem>();
        foreach(var stem in SectorForest.Stems)
        {
            var key=(Mathf.FloorToInt(stem.position.x/50f),Mathf.FloorToInt(stem.position.z/50f));
            if(!patches.TryGetValue(key,out var old)||stem.stand.cover>old.stand.cover)patches[key]=stem;
        }
        int tested=0,covered=0;var missed=new List<object>();
        foreach(var stem in patches.Values)
        {
            if(stem.stand.cover<.5f)continue;
            tested++;var p=stem.position;bool found=false;
            foreach(var data in SectorForest.PreparedCanopy)
            {
                for(int k=0;k<data.triangles.Length;k+=3)
                {
                    var a=data.vertices[data.triangles[k]];var b=data.vertices[data.triangles[k+1]];var c=data.vertices[data.triangles[k+2]];
                    if(p.x<Math.Min(a.x,Math.Min(b.x,c.x))||p.x>Math.Max(a.x,Math.Max(b.x,c.x))
                        ||p.z<Math.Min(a.z,Math.Min(b.z,c.z))||p.z>Math.Max(a.z,Math.Max(b.z,c.z)))continue;
                    float det=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Math.Abs(det)<.0001f)continue;
                    float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/det;
                    float v=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/det;
                    if(u>=0&&v>=0&&u+v<=1&&a.y*u+b.y*v+c.y*(1-u-v)>p.y+.5f){found=true;break;}
                }
                if(found)break;
            }
            if(found)covered++;
            else { missed.Add(new{p=V(p),stem.stand.cover,stem.stand.height});startupRims.Add(p); }
        }
        Save(output+"/startup.json",new{densePatches=tested,patchesWithCanopy=covered,missed,buildMs=SectorForest.BuildMilliseconds});
#if POLISH
        Require(tested>0 && (covered==tested || !WorldPlan.Ready),"Every dense live Varrow wood has a raised startup canopy");
#endif
    }

    // A sparse preview rim may lie outside the continuous sheet; its exact logical tree must still draw.
    static void CheckPreviewRims(Camera camera,FarScatter scatter)
    {
        var eye=camera.transform.position;var rotation=camera.transform.rotation;
        foreach(var p in startupRims)
        {
            camera.transform.position=p+new Vector3(0,180,-180);
            camera.transform.rotation=Quaternion.LookRotation(p-camera.transform.position);
            for(int frame=0;frame<90;frame++)Tick(camera);
            object view=((IDictionary)Field(scatter,"views"))[camera];bool found=false;
            foreach(object draw in (IEnumerable)Field(view,"draws"))
                foreach(var matrix in (Matrix4x4[])Field(draw,"matrices"))
                    if((matrix.position-p).sqrMagnitude<.001f)found=true;
            Require(found,"Preview rim stem must draw where the canopy sheet is sparse");
        }
        camera.transform.position=eye;camera.transform.rotation=rotation;
        Console.WriteLine("Preview rim trees verified: "+startupRims.Count);
    }

#if CANDIDATE
    // Projection checks include altitude, zoom and viewport resolution, independently of the renderer's cache.
    static void CheckProjection()
    {
        var forward=new Vector3(0,-1,1).normalized;var eye=new Vector3(0,300,-300);var crown=new Vector3(0,15,0);
        float score=FarScatter.CrownScore(crown,4,eye,forward,864);
        Require(1000f/score>12f,"A mature crown at the center of the 300 m view exceeds 12 pixels");
        Require(Math.Abs(FarScatter.CrownScore(crown,4,eye,forward,1728)*2-score)<.001f,"Double resolution halves inverse size");
    }

    // Simulate supported diagnostic failures and the exact Blender bark suffix found in the production FBX.
    static void CheckMaterials()
    {
        for(int shade=0;shade<3;shade++)Require(ForestMaterials.Fades(ForestMaterials.Create("BarkPine.001",null,1)),"Known suffix resolves to the serialized forest asset");
        Require(ForestMaterials.RemappedCount==3,"All three pine bark tint variants remapped");
        for(int failure=0;failure<5;failure++)
        {
            ForestMaterials.ForceFallback=failure==0;Shader.Missing=failure==1;Shader.Unsupported=failure==2;
            Shader.MissingProperty=failure==3;Material.IgnoreWrites=failure==4;
            var m=ForestMaterials.Create("LeavesOak",null,0);
            Require(m.shader.name=="HDRP/Lit"&&!ForestMaterials.Fades(m)&&ForestMaterials.LastFallback!="none","Fallback retains visible Lit and a reason");
        }
        ForestMaterials.ForceFallback=false;Shader.Missing=Shader.Unsupported=Shader.MissingProperty=Material.IgnoreWrites=false;
    }
#endif
    // Access only recording state; reflection avoids adding harness-only APIs to production classes.
    static object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(value);
    // Baseline views predate projected fields, so their optional metadata defaults to zero.
    static float Optional(object value,string name)=>(float)(value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(value)??0f);
    // Advance a simulated 60 Hz clock; production slices still use the actual managed stopwatch.
    static void Tick(Camera camera)
    {
        Time.frameCount++;Time.unscaledTime=Time.frameCount/60f;
        feed.transform.position=camera.transform.position+new Vector3(27.87f,99.82f,48.27f);
        feed.transform.rotation=Quaternion.LookRotation(new Vector3(1,-0.6f,0));
        if(Time.frameCount%2==0)RenderPipelineManager.Render(feed);
        RenderPipelineManager.Render(camera);
    }
    // New diagnostic properties are absent in the baseline assembly, so keep its export comparable.
    static object Counter(Type type,string name)=>type.GetProperty(name)?.GetValue(null);
    // Serialize compact arrays so each render process reads only its own sector.
    static void Save(string path,object value)=>File.WriteAllText(path,JsonSerializer.Serialize(value));
    // Export Unity coordinates without silently changing axes before Blender reads them.
    static float[] V(Vector3 p)=>new[]{p.x,p.y,p.z};
    // Failed checks stop the run instead of publishing misleading passing evidence.
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
}

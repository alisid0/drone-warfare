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
    // One sector per process bounds terrain memory and makes baseline/candidate runs independent.
    static void Main(string[] args)
    {
        string output = args[0]; Directory.CreateDirectory(output);
        WorldPlan.Generate(1200);
        SectorPlan plan;
        if (args[1] == "varrow")
        {
            plan = SectorPlan.Varrow(); FarTerrain.Origin = Vector2.zero;
        }
        else
        {
            var origin = WorldSectors.SpotFor(new Hotspot { kind = SectorKind.DeepForest, seed = 1037 });
            FarTerrain.Origin = origin; plan = WorldSectors.PlanAt(origin, SectorKind.DeepForest, 1037);
        }
        MapLayout.Use(plan); World.Build(new GameObject("Forest harness").transform, true);
        var scatter = new GameObject("Far Terrain").AddComponent<FarScatter>();
        typeof(FarScatter).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scatter, null);
        Vector3 target = args[1] == "varrow" ? plan.basePosition + new Vector3(-120f, 0f, 0f)
            : new Vector3(plan.reservedFeatures[0].center.x, 0f, plan.reservedFeatures[0].center.y);
        target.y = Ground.HeightAt(target);
        var camera = new GameObject("Strategy camera").AddComponent<Camera>();
        camera.transform.position = target + new Vector3(0f, 300f, -300f);
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position);
        int coverage = -1, refined = -1;
        for (int frame = 0; frame < 600; frame++)
        {
            Tick(camera);
            if (coverage < 0 && ForestHlod.MissingBlocks == 0) coverage = frame;
#if CANDIDATE
            if (refined < 0 && ForestHlod.NearCoarseBlocks == 0 && ForestHlod.MissingBlocks == 0) refined = frame;
#endif
        }
        Export(output, plan, target, camera, scatter);
        int full = FarScatter.FullTreesDrawn, reduced = FarScatter.ReducedTreesDrawn, cards = FarScatter.CardTreesDrawn;
#if CANDIDATE
        Require(full + reduced + cards > 0, "Strategy camera must submit crowns");
        Require(coverage >= 0 && coverage <= 180, "Initial missing blocks settle within 3 simulated seconds");
        Require(refined >= 0 && refined <= 600, "Near blocks refine within 10 simulated seconds");
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
        camera.transform.position = target + new Vector3(0f, 187.5f, -100f);
        camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.3f, 1f));
        for(int frame=0;frame<360;frame++)Tick(camera);
        var low = new List<float>();
        for(int frame=0;frame<360;frame++)
        {
            var p=camera.transform.position; p.x+=240f/60f; p.y=FarScatter.GroundAt(new Vector2(p.x,p.z))+187.5f; camera.transform.position=p;
            Tick(camera);low.Add(FarScatter.MainThreadMs);if(FarScatter.TreesDrawn==0)lowBlank++;
            Require(FarScatter.FullTreesDrawn<=ForestBudget.FullLimit&&FarScatter.ReducedTreesDrawn<=ForestBudget.ReducedLimit
                &&FarScatter.CardTreesDrawn<=ForestBudget.CardLimit&&FarScatter.TreeTrianglesDrawn<=ForestBudget.TriangleLimit,"Low flight budgets");
        }
        low.Sort();lowP95=low[341];
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
        Require(jumpRefined >= 0, "Jump near refinement within 10 simulated seconds");
        CheckMaterials();
#endif
        var metrics = new { sector=args[1], logical=SectorForest.Stems.Count, full, reduced, cards, coverageFrames=coverage,
            nearRefinementFrames=refined, jumpCoverageFrames=jumpCoverage, jumpRefinementFrames=jumpRefined,
            flight=new { managedP95Ms=flight[227], blankFrames=blank },lowFlight=new{metresPerSecond=240,height=187.5f,managedP95Ms=lowP95,blankFrames=lowBlank},nativeUploadsAndGpuMeasured=false };
        Save(output + "/metrics.json", metrics); Console.WriteLine(JsonSerializer.Serialize(metrics));
    }

    // Export the scheduler's selected roots; the Blender view substitutes repository FBXs for the recording meshes.
    static void Export(string output, SectorPlan plan, Vector3 target, Camera camera, FarScatter scatter)
    {
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
            Save(output+"/canopy-"+count+".json",new { vertices=v,normals=n,uv,indices=mesh.triangles });
            using(var stream=new FileStream(output+"/canopy-"+count+".ppm",FileMode.Create))
            {
                int size=(int)Math.Sqrt(texture.pixels.Length);stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{size} {size}\n255\n"));
                foreach(var p in texture.pixels){stream.WriteByte(p.r);stream.WriteByte(p.g);stream.WriteByte(p.b);}
            }
            count++;
        }
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
    static void Tick(Camera camera){Time.frameCount++;Time.unscaledTime=Time.frameCount/60f;RenderPipelineManager.Render(camera);}
    // Serialize compact arrays so each render process reads only its own sector.
    static void Save(string path,object value)=>File.WriteAllText(path,JsonSerializer.Serialize(value));
    // Export Unity coordinates without silently changing axes before Blender reads them.
    static float[] V(Vector3 p)=>new[]{p.x,p.y,p.z};
    // Failed checks stop the run instead of publishing misleading passing evidence.
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
}

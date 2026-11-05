using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Audits output from the production terrain, water meshes, forest placements and parcel masks.
static partial class Harness
{
    // Exercise centre-aware reuse, exact call order, Varrow and repeated fallback through copied production bodies.
    static void FlowChecks(Vector2 first,Vector2 second)
    {
        SandboxFlowProbe.Reset();
        var a=new SectorPlan {kind=SectorKind.RollingFarmland,seed=731,world=new WorldSectorData(first,SectorKind.RollingFarmland)};
        var b=new SectorPlan {kind=a.kind,seed=a.seed,world=new WorldSectorData(second,a.kind)};
        var game=new SandboxFlowProbe();
        MapLayout.Use(a);
        game.LoadSector(a); Require(SandboxFlowProbe.builds==0,"Same world plan reuses current sector");
        game.LoadSector(b); Require(SandboxFlowProbe.builds==1,"Same kind/seed at a different centre reloads");
        game.LoadSector(b); Require(SandboxFlowProbe.builds==1,"Identical centre reuses current sector");
        var legacy=SectorGenerator.Generate(a.kind,a.seed);
        game.LoadSector(legacy); Require(SandboxFlowProbe.builds==2,"World to generated reloads");
        game.LoadSector(legacy); Require(SandboxFlowProbe.builds==2,"Generated plan still reuses current sector");
        SandboxFlowProbe.next=a; SandboxFlowProbe.center=first;
        var hotspot=new Hotspot {kind=a.kind,seed=a.seed};
        game.Play(hotspot);
        Require(SandboxFlowProbe.origin.Equals(first) && ReferenceEquals(MapLayout.Plan,a),"Campaign loads the requested world crop");
        Require(!game.Loading && game.begun==1,"Campaign reaches briefing after loading");
        SandboxFlowProbe.fail=true; game.Play(hotspot); game.Play(hotspot);
        Require(MapLayout.Plan.world==null && SandboxFlowProbe.origin.Equals(new Vector2(-8000,5000)),"No dry footprint uses old plan and old spot");
        Require(SandboxFlowProbe.warnings==1,"Repeated crop fallback logs once");
        int attempts=SandboxFlowProbe.plans;
        game.Play(new Hotspot {kind=SectorKind.VarrowValley,seed=1});
        Require(SandboxFlowProbe.plans==attempts && MapLayout.Plan.world==null && SandboxFlowProbe.origin.Equals(new Vector2(4000,9000)),"Varrow uses its own spot and legacy plan");
    }

    // Compare exact output hashes with 1016ddc, using the same substitutes and unchanged null-world recipes.
    static void LegacyConsumers(string output)
    {
        var results=new List<object>();
        foreach(var kind in new[]{SectorKind.VarrowValley,SectorKind.RollingFarmland,SectorKind.Reservoir,SectorKind.DeepForest})
        {
            var plan=kind==SectorKind.VarrowValley?SectorPlan.Varrow():SectorGenerator.Generate(kind,7100);
            var root=new GameObject("Legacy").transform;
            var clock=Stopwatch.StartNew();
            MapLayout.Use(plan); MapLayout.Prepare(); Ground.Build(root);
            double terrainMs=clock.Elapsed.TotalMilliseconds;
            using var bytes=new MemoryStream(); using var writer=new BinaryWriter(bytes);
            foreach(var h in Ground.Current.terrainData.heights) writer.Write(h);
            writer.Write(Ground.Current.transform.position.y); writer.Write(Ground.Current.terrainData.size.y);
            Water.Build(root);
            foreach(var o in (List<UnityEngine.Object>)typeof(Water).GetField("made",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null))
                if(o is Mesh m)
                {
                    foreach(var p in m.vertices) {writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
                    foreach(var t in m.triangles) writer.Write(t);
                }
            Art.placed.Clear(); Undergrowth.Reset(); typeof(World).GetProperty("TreeCount").SetValue(null,0);
            typeof(World).GetMethod("BuildWoods",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{root,new System.Random(plan.worldSeed)});
            foreach(var p in Art.placed) {writer.Write(p.model);writer.Write(p.root.position.x);writer.Write(p.root.position.y);writer.Write(p.root.position.z);}
            foreach(var f in MapLayout.Fields) {writer.Write(f.center.x);writer.Write(f.center.y);writer.Write(f.halfWidth);writer.Write(f.halfLength);writer.Write(f.taper);writer.Write(f.shear);}
            writer.Flush();
            string hash=Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
            results.Add(new {kind=kind.ToString(),hash,terrainMs,totalMs=clock.Elapsed.TotalMilliseconds});
            Console.WriteLine($"Legacy {kind}: {hash}");
        }
        File.WriteAllText(Path.Combine(output,"legacy.json"),JsonSerializer.Serialize(results,Json));
    }

    // Execute the actual consumer branches and serialize their output, rather than redrawing the adapter inputs.
    static object Consumers(SectorPlan plan)
    {
        var root = new GameObject("Consumer harness").transform;
        var clock = Stopwatch.StartNew();
        var terrain = Ground.Build(root);
        double groundMs = clock.Elapsed.TotalMilliseconds;
        float maxBoundary = 0f, maxStored = 0f;
        var edgeWorld = new List<float>(); var edgeTerrain = new List<float>();
        // Four samples per terrain interval catch interpolation between boundary vertices too.
        for (int side = 0; side < 4; side++)
            for (int i = 0; i <= 4096; i++)
            {
                float t = -1000f + i * 2000f / 4096;
                var p = side == 0 ? new Vector2(t,-1000) : side == 1 ? new Vector2(1000,t)
                    : side == 2 ? new Vector2(-t,1000) : new Vector2(-1000,-t);
                float expected = WorldPlan.Height(plan.world.center.x + p.x, plan.world.center.y + p.y);
                float actual = Ground.HeightAt(new Vector3(p.x,0,p.y));
                maxBoundary = Mathf.Max(maxBoundary, Mathf.Abs(expected - actual));
                if (i % 16 == 0) { edgeWorld.Add(expected); edgeTerrain.Add(actual); }
            }
        // Along a square edge both surfaces are piecewise linear. Their error can only peak at a terrain
        // vertex (included above) or a world-grid knot, so also measure every 32 m knot for a boundary-wide bound.
        int boundaryChecks=4*4097;
        for(int side=0;side<4;side++)
        {
            float axis=side%2==0?plan.world.center.x:plan.world.center.y;
            float start=Mathf.CeilToInt((axis-1000f+WorldPlan.Half)/WorldPlan.HeightStep)*WorldPlan.HeightStep-WorldPlan.Half;
            for(float world=start;world<=axis+1000f;world+=WorldPlan.HeightStep)
            {
                float t=world-axis;
                var p=side==0?new Vector2(t,-1000):side==1?new Vector2(1000,t):side==2?new Vector2(t,1000):new Vector2(-1000,t);
                float expected=WorldPlan.Height(plan.world.center.x+p.x,plan.world.center.y+p.y);
                maxBoundary=Mathf.Max(maxBoundary,Mathf.Abs(expected-Ground.HeightAt(new Vector3(p.x,0,p.y))));
                boundaryChecks++;
            }
        }
        Require(maxBoundary < 0.5f, "Stored terrain boundary agrees with WorldPlan: " + maxBoundary);
        var actualHeights = (float[,])typeof(Ground).GetField("worldHeights", BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        for (int z=0;z<1025;z++) for (int x=0;x<1025;x++)
        {
            float stored=terrain.terrainData.heights[z,x];
            Require(stored>0f && stored<1f, "Absolute terrain does not clip vertically");
            maxStored=Mathf.Max(maxStored,Mathf.Abs(actualHeights[z,x]-(stored*terrain.terrainData.size.y+terrain.transform.position.y)));
        }
        var pads=(List<Ground.FlatSite>)typeof(Ground).GetField("sites",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        foreach (var site in pads)
        {
            Require(site.center.y==plan.world.Height(site.center.x,site.center.z),"Pad stores its own absolute target");
            bool overlaps=false;
            foreach(var other in pads)
                if(!other.center.Equals(site.center) && Vector2.Distance(Flat(other.center),Flat(site.center))<other.radius+2f) overlaps=true;
            if(!overlaps && plan.world.DetailWeight(site.center.x,site.center.z)==1f)
                Require(Mathf.Abs(Ground.HeightAt(site.center)-site.center.y)<0.2f,"Isolated pad reaches its absolute elevation");
        }
        clock.Restart(); Art.placed.Clear();
        Water.Build(root);
        double waterMs=clock.Elapsed.TotalMilliseconds;
        var resources=(List<UnityEngine.Object>)typeof(Water).GetField("made",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var water=new List<object>(); int riverMeshes=0;
        foreach(var resource in resources)
        {
            if (!(resource is Mesh mesh)) continue;
            var vertices=new float[mesh.vertices.Length][];
            for(int i=0;i<vertices.Length;i++)
            {
                var p=mesh.vertices[i]; vertices[i]=new[]{p.x,p.y,p.z};
                Require(Mathf.Abs(p.x)<=1000.01f && Mathf.Abs(p.z)<=1000.01f,"Water clipped at sector edge");
                Require(!float.IsNaN(p.y),"Water level finite");
            }
            if(riverMeshes<plan.world.rivers.Count)
            {
                // Each mesh can only cover its own exported reach, not a false segment to the next reach.
                var reach=plan.world.rivers[riverMeshes++];
                foreach(var p in mesh.vertices)
                {
                    float distance=float.MaxValue, width=0;
                    for(int j=1;j<reach.points.Length;j++)
                    {
                        float t=WorldSectors.Projection(new Vector2(p.x,p.z),reach.points[j-1],reach.points[j]);
                        float d=Vector2.Distance(new Vector2(p.x,p.z),Vector2.Lerp(reach.points[j-1],reach.points[j],t));
                        if(d<distance) {distance=d;width=Mathf.Lerp(reach.halfWidths[j-1],reach.halfWidths[j],t);}
                    }
                    Require(distance<=width+0.5f,"River mesh belongs to its own reach");
                }
            }
            for(int i=0;i<mesh.triangles.Length;i+=3)
            {
                var a=mesh.vertices[mesh.triangles[i]];var b=mesh.vertices[mesh.triangles[i+1]];var c=mesh.vertices[mesh.triangles[i+2]];
                Require(Vector3.Cross(b-a,c-a).y>=-0.0001f,"Water triangles face up");
            }
            water.Add(new {mesh.name,vertices,mesh.triangles});
        }
        Require(riverMeshes==plan.world.rivers.Count,"One mesh per world reach");
        Require(Art.placed.Count==plan.world.bridges.Count+plan.world.dams.Count,"One model per world structure");
        for(int i=0;i<plan.world.bridges.Count;i++)
        {
            var bridge=plan.world.bridges[i]; var placed=Art.placed[i];
            Require(Vector2.Distance(Flat(placed.root.position),(bridge.a+bridge.b)/2f)<0.01f,"Bridge midpoint");
            Require(Mathf.Abs(placed.root.position.y+0.3f-bridge.deckLevel)<0.001f,"Bridge deck datum");
            Require(Mathf.Abs(placed.mesh.transform.localScale.z*18f-Vector2.Distance(bridge.a,bridge.b))<0.01f,"Bridge span");
            Require(Vector2.Dot(Flat(placed.root.rotation.forward),(bridge.b-bridge.a).normalized)>0.999f,"Bridge orientation");
        }
        for(int i=0;i<plan.world.dams.Count;i++)
        {
            var dam=plan.world.dams[i]; var placed=Art.placed[plan.world.bridges.Count+i];
            Require(Vector2.Distance(Flat(placed.root.position),(dam.a+dam.b)/2f)<0.01f,"Dam midpoint");
            Require(Mathf.Abs(placed.root.position.y+15f*placed.mesh.transform.localScale.y-dam.crest)<0.001f,"Dam crest datum");
            var down=Flat(placed.root.rotation.forward);
            Require(Mathf.Abs(Vector2.Dot(down,(dam.b-dam.a).normalized))<0.001f,"Dam spans banks");
            Require(plan.world.LakeDistance(Flat(placed.root.position)+down*10f,out _)>=plan.world.LakeDistance(Flat(placed.root.position)-down*10f,out _),"Dam faces downstream");
        }
        var structures=new List<object>();
        foreach(var p in Art.placed) structures.Add(new {p.model,position=new[]{p.root.position.x,p.root.position.y,p.root.position.z},
            scale=new[]{p.mesh.transform.localScale.x,p.mesh.transform.localScale.y,p.mesh.transform.localScale.z}});
        Art.placed.Clear(); Undergrowth.Reset();
        typeof(World).GetProperty("TreeCount").SetValue(null,0);
        clock.Restart();
        typeof(World).GetMethod("BuildWoods",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{root,new System.Random(plan.worldSeed)});
        double woodsMs=clock.Elapsed.TotalMilliseconds;
        var trees=new List<float[]>();
        foreach(var p in Art.placed)
        {
            Require(plan.world.CoverAt(p.root.position.x,p.root.position.z)==Cover.Forest,"Woods inside world forest cover");
            trees.Add(new[]{p.root.position.x,p.root.position.z});
        }
        Require(World.TreeCount<=11500,"Shared tree budget");
        var fields=new List<float[][]>();
        foreach(var f in MapLayout.Fields)
        {
            int nu=Mathf.CeilToInt(f.halfLength*2),nv=Mathf.CeilToInt(f.halfWidth*2);
            for(int u=0;u<=nu;u++) for(int v=0;v<=nv;v++)
            {
                var p=f.Point(f.halfLength*(2f*u/nu-1),f.halfWidth*(2f*v/nv-1));
                Require(plan.world.CoverAt(p.x,p.y)==Cover.Farmland,"Entire parcel stays in farmland at 1 m sampling");
            }
            var outline=new List<Vector2>();
            for(int side=0;side<4;side++) for(int i=0;i<20;i++)
            {
                float t=-1f+i*0.1f;
                outline.Add(f.Point(f.halfLength*(side==0?t:side==1?1:side==2?-t:-1),f.halfWidth*(side==0?-1:side==1?t:side==2?1:-t)));
            }
            fields.Add(Line(outline));
        }
        var heights=new float[201*201];
        for(int z=0;z<=200;z++) for(int x=0;x<=200;x++) heights[z*201+x]=Ground.HeightAt(new Vector3(-1000+x*10,0,-1000+z*10));
        Console.WriteLine($"  Consumers: ground {groundMs:F0} ms, water {waterMs:F0} ms, woods {woodsMs:F0} ms; edge {maxBoundary:F4} m; {trees.Count} trees, {fields.Count} parcels");
        return new {groundMs,waterMs,woodsMs,maxBoundary,maxStored,boundaryChecks,bottom=terrain.transform.position.y,range=terrain.terrainData.size.y,
            edgeWorld,edgeTerrain,heights,water,trees,fields,structures};
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Execute production generators and both scatter revisions; export exact submitted geometry for Blender.
public static class Harness
{
    public static string Root, Output;
    public static JsonElement Models;
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields=true };
    static readonly Dictionary<Texture2D,string> textures=new Dictionary<Texture2D,string>();
    static int textureIndex;
    static List<object> metrics=new List<object>();
    static void Require(bool test,string message){if(!test)throw new Exception(message);}
    public static string ExportTexture(Texture2D texture)
    {
        if(textures.TryGetValue(texture,out string path))return path;
        path="texture_"+(textureIndex++)+".ppm";textures.Add(texture,path);
        using(var file=File.Create(Path.Combine(Output,path))) {
            var header=System.Text.Encoding.ASCII.GetBytes("P6\n"+texture.width+" "+texture.height+"\n255\n");file.Write(header);
            foreach(var c in texture.pixels){file.WriteByte(c.r);file.WriteByte(c.g);file.WriteByte(c.b);}
        }
        return path;
    }
    static void Write(string name,object value)=>File.WriteAllText(Path.Combine(Output,name),JsonSerializer.Serialize(value,Json));
    static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
    static float[] V(Vector2 v)=>new[]{v.x,v.y};
    public static void Main(string[] args)
    {
        Root=args[0];Output=args[1];Directory.CreateDirectory(Output);
        Models=JsonDocument.Parse(File.ReadAllText(Root+"/Tools/ForestHlod/Review/models.json")).RootElement;
        WorldPlan.Generate(1);FarTerrain.Origin=new Vector2(-24000,-24000);
        if(args.Length>2 && args[2]=="--checks") {
            foreach(int seed in new[]{1,2,3}){WorldPlan.Generate(seed);Checks();}
            return;
        }
        FarTerrain.Initialize();
        if(args.Length>2 && args[2]=="--calibration"){Calibration(new Vector2(16500,-4500));return;}
        Checks();
        // This interior forest fits the complete 9 km view; WorldCheck independently visits the largest district.
        Vector2 forest=new Vector2(16500,-4500);WorldSettlement town=WorldPlan.Settlements[0];
        foreach(var s in WorldPlan.Settlements)if(s.town && s.radius>town.radius)town=s;
        foreach(var place in new[]{("forest",forest),("town",town.center)})
        {
            foreach(var view in new[]{("above",1200f,90f),("300m",300f,45f),("2500m",2500f,64f),("9000m",9000f,72f)})
            {
                Vector2 p=place.Item2-FarTerrain.Origin;
                var target=new Vector3(p.x,WorldPlan.Height(place.Item2.x,place.Item2.y),p.y);
                float pitch=view.Item3*Mathf.Deg2Rad,az=35*Mathf.Deg2Rad,d=view.Item2;
                Camera.main.target=target;
                Camera.main.transform.position=target+new Vector3(-Mathf.Sin(az)*Mathf.Cos(pitch)*d,Mathf.Sin(pitch)*d,-Mathf.Cos(az)*Mathf.Cos(pitch)*d);
                foreach(bool after in new[]{false,true})
                {
                    object component=after?(object)new FarScatter():new BaselineFarScatter();
                    var late=component.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
                    Graphics.capture=false;
                    var timer=Stopwatch.StartNew();
                    // Warm deterministic streaming to completion; CPU timings exclude all capture/JSON work.
                    int frames=after?1400:400;
                    for(int i=0;i<frames;i++)late.Invoke(component,null);
                    double warm=timer.Elapsed.TotalMilliseconds;
                    timer.Restart();for(int i=0;i<30;i++)late.Invoke(component,null);double ms=timer.Elapsed.TotalMilliseconds/30;
                    Graphics.Reset();Graphics.capture=true;late.Invoke(component,null);Graphics.capture=false;
                    string name=place.Item1+"_"+view.Item1+"_"+(after?"after":"before");
                    int trees=after?FarScatter.TreesDrawn:BaselineFarScatter.TreesDrawn;
                    int houses=after?FarScatter.HousesDrawn:BaselineFarScatter.HousesDrawn;
                    metrics.Add(new{name,trees,meshTrees=after?FarScatter.MeshTreesDrawn:0,cards=after?FarScatter.CardTreesDrawn:trees,houses,
                        forestBlocks=after?ForestHlod.BlocksDrawn:0,forestTriangles=after?ForestHlod.TrianglesDrawn:0,townBlocks=after?SettlementHlod.BlocksDrawn:0,
                        townTriangles=after?SettlementHlod.TrianglesDrawn:0,draws=Graphics.calls,triangles=Graphics.triangles,managedSubmitMs=ms,warmMs=warm});
                    Write(name+".json",new{name,camera=V(Camera.main.transform.position),target=V(target),origin=V(FarTerrain.Origin),after,
                        ground=Terrain(place.Item2,view.Item2,after),worldTexture=ExportTexture(FarTerrain.WorldTexture),instances=Graphics.instances,meshes=Graphics.meshes});
                    Console.WriteLine(name+": trees="+trees+", houses="+houses+", draws="+Graphics.calls+", triangles="+Graphics.triangles+", CPU="+ms.ToString("F3")+" ms");
                    if(after)component.GetType().GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(component,null);
                }
            }
        }
        Write("metrics.json",metrics);
        Calibration(forest);
        NearTierCheck(forest);
    }

    // Hold camera, terrain, species, age and tint fixed while comparing cards alone against full canopy alone.
    static void Calibration(Vector2 world)
    {
        Vector2 p=world-FarTerrain.Origin;var target=new Vector3(p.x,WorldPlan.Height(world.x,world.y),p.y);
        Camera.main.target=target;Camera.main.transform.position=target+new Vector3(0,519.615f,-300);
        var scatter=new FarScatter();var late=typeof(FarScatter).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
        Graphics.capture=false;for(int i=0;i<1000;i++)late.Invoke(scatter,null);
        foreach(bool canopy in new[]{false,true}){
            Graphics.Reset();Graphics.fullCanopy=canopy;Graphics.capture=true;late.Invoke(scatter,null);Graphics.capture=false;
            if(canopy)Graphics.instances.Clear();else Graphics.meshes.Clear();
            string name=canopy?"calibration_canopy":"calibration_cards";
            Write(name+".json",new{name,camera=V(Camera.main.transform.position),target=V(target),origin=V(FarTerrain.Origin),after=true,
                ground=Terrain(world,600,true),worldTexture=ExportTexture(FarTerrain.WorldTexture),instances=Graphics.instances,meshes=Graphics.meshes});
        }
        Graphics.fullCanopy=false;
        typeof(FarScatter).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scatter,null);
    }
    static object Terrain(Vector2 target,float distance,bool after)
    {
        float span=Math.Max(3000,distance*2.5f);int q=160;
        float x0=Math.Max(-WorldPlan.Half,target.x-span*.5f),x1=Math.Min(WorldPlan.Half,target.x+span*.5f);
        float z0=Math.Max(-WorldPlan.Half,target.y-span*.5f),z1=Math.Min(WorldPlan.Half,target.y+span*.5f);
        var vertices=new float[(q+1)*(q+1)][];var uv=new float[vertices.Length][];var triangles=new List<int>();
        for(int j=0;j<=q;j++)for(int i=0;i<=q;i++){
            float x=Mathf.Lerp(x0,x1,i/(float)q),z=Mathf.Lerp(z0,z1,j/(float)q);
            int k=j*(q+1)+i;vertices[k]=new[]{x-FarTerrain.Origin.x,WorldPlan.Height(x,z)+(after?0:FarTerrain.CanopyAt(x,z)),z-FarTerrain.Origin.y};
            uv[k]=new[]{(x+WorldPlan.Half)/WorldPlan.Size,(z+WorldPlan.Half)/WorldPlan.Size};
            if(i<q&&j<q){triangles.Add(k);triangles.Add(k+q+1);triangles.Add(k+1);triangles.Add(k+1);triangles.Add(k+q+1);triangles.Add(k+q+2);}
        }
        return new{vertices,uv,indices=triangles};
    }
    static void Checks()
    {
        int forest=0,glades=0,edge=0;float low=24,high=0;double heights=0,height2=0;int count=0;
        Vector2 focus=ForestHlod.LargestForest();
        for(float z=focus.y-2048;z<focus.y+2048;z+=8)for(float x=focus.x-2048;x<focus.x+2048;x+=8){
            if(WorldPlan.CoverAt(x,z)!=Cover.Forest)continue;forest++;
            if(ForestPattern.Glade(x,z)==0)glades++;
            var stand=ForestPattern.At(x,z);Require(stand.height>=8&&stand.height<=24,"Height outside 8-24 m");
            if(stand.cover<.5)edge++;
            if(stand.cover>.95){low=Math.Min(low,stand.height);high=Math.Max(high,stand.height);heights+=stand.height;height2+=stand.height*stand.height;count++;}
        }
        float share=glades/(float)forest;Require(share>=.05&&share<=.12,"Glades outside 5-12 percent: "+share);
        for(int i=-80;i<80;i++){
            bool a=ForestPattern.Tree(i,i+10,out Vector2 p,out var s);bool b=ForestPattern.Tree(i,i+10,out Vector2 p2,out var s2);
            Require(a==b&&p.x==p2.x&&p.y==p2.y&&s.species==s2.species&&s.height==s2.height,"Non-deterministic tree");
        }
        var corner=new Vector2(-WorldPlan.Half+(int)((focus.x+WorldPlan.Half)/ForestHlod.BlockSize)*ForestHlod.BlockSize,
            -WorldPlan.Half+(int)((focus.y+WorldPlan.Half)/ForestHlod.BlockSize)*ForestHlod.BlockSize);
        var quilt=File.ReadAllBytes(Root+"/Assets/DroneSandbox/Resources/Textures/ForestCrown.bytes");
        var left=new ForestHlod.Data{corner=corner,step=16};var build=ForestHlod.Build(left,quilt);while(build.MoveNext()){}
        var right=new ForestHlod.Data{corner=corner+new Vector2(ForestHlod.BlockSize,0),step=16};build=ForestHlod.Build(right,quilt);while(build.MoveNext()){}
        for(int j=0;j<=128;j++)Require(Math.Abs(left.vertices[j*129+128].y-right.vertices[j*129].y)<.001,"Canopy seam");
        int sloped=0;for(int i=0;i<129*129;i++){Require(!float.IsNaN(left.normals[i].y)&&left.normals[i].y>0,"Invalid canopy normal");if(left.normals[i].y<.995)sloped++;}
        Require(sloped>200,"Canopy has no meaningful surface variation");
        Require(ForestHlod.Step(new Vector3(focus.x,WorldPlan.Height(focus.x,focus.y)+9000,focus.y),corner)==128,"Wide view kept fine canopy");
        var houses=new List<SettlementPattern.House>();
        foreach(var settlement in WorldPlan.Settlements)if(settlement.town){
            foreach(var b in SettlementPattern.Build(settlement))houses.AddRange(b.houses);
            if(houses.Count>800)break;
        }
        for(int i=0;i<houses.Count;i++)for(int j=0;j<i;j++)
            if((houses[i].world-houses[j].world).sqrMagnitude<2000)Require(!SettlementPattern.Overlap(houses[i],houses[j]),"Overlapping roofs");
        var townData=new SettlementHlod.Data();var block=new SettlementPattern.Block();
        for(int kind=0;kind<7;kind++)block.houses.Add(new SettlementPattern.House{world=focus+new Vector2(kind*50,0),kind=kind});
        SettlementHlod.Append(townData,block);
        for(int i=0;i<townData.triangles.Count;i+=3){
            var a=townData.vertices[townData.triangles[i]];var b=townData.vertices[townData.triangles[i+1]];var c=townData.vertices[townData.triangles[i+2]];
            Require(Vector3.Cross(b-a,c-a).sqrMagnitude>.01f,"Degenerate settlement face");
        }
        Write("checks-seed-"+WorldPlan.Seed+".json",new{seed=WorldPlan.Seed,forestFocus=V(focus),gladeShare=share,edgeShare=edge/(float)forest,heightMin=low,heightMax=high,
            checkedHouses=houses.Count,roofOverlaps=0,
            heightSd=Math.Sqrt(height2/count-heights*heights/(count*(double)count)),fineBlockTriangles=left.triangles.Length/3,slopedNormals=sloped,seamError=0});
        Console.WriteLine("CHECKS PASS: glades "+(share*100).ToString("F2")+"%, heights "+low.ToString("F1")+"-"+high.ToString("F1")+", adjacent seam < 1 mm.");
    }

    // Exercise the expensive near tier and cleanup/origin paths, which high-altitude review views never reach.
    static void NearTierCheck(Vector2 forest)
    {
        Vector2 world=forest;ForestPattern.Stand stand=default;bool found=false;
        int gx=Mathf.FloorToInt(forest.x/12),gz=Mathf.FloorToInt(forest.y/12);
        for(int j=-10;j<=10&&!found;j++)for(int i=-10;i<=10&&!found;i++)found=ForestPattern.Tree(gx+i,gz+j,out world,out stand);
        Require(found,"No near-tier reference tree");
        Vector2 p=world-FarTerrain.Origin;
        Camera.main.target=new Vector3(p.x,WorldPlan.Height(world.x,world.y)+stand.height*.5f,p.y);
        Camera.main.transform.position=Camera.main.target+new Vector3(0,35,-45);
        var scatter=new FarScatter();var late=typeof(FarScatter).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
        Graphics.capture=false;for(int i=0;i<600;i++)late.Invoke(scatter,null);
        Require(FarScatter.MeshTreesDrawn>0,"No real tree meshes within 120 m");
        Graphics.Reset();Graphics.capture=true;late.Invoke(scatter,null);Graphics.capture=false;
        Write("forest_near_after.json",new{name="forest_near_after",camera=V(Camera.main.transform.position),target=V(Camera.main.target),origin=V(FarTerrain.Origin),after=true,
            ground=Terrain(world,300,true),worldTexture=ExportTexture(FarTerrain.WorldTexture),instances=Graphics.instances,meshes=Graphics.meshes});
        Write("near-tier.json",new{meshes=FarScatter.MeshTreesDrawn,cards=FarScatter.CardTreesDrawn,draws=Graphics.calls,triangles=Graphics.triangles});
        scatter.Clear();Require(FarScatter.TreesDrawn==0&&ForestHlod.CachedBlocks==0,"Clear retained counters/cache");
        FarTerrain.Origin+=new Vector2(250,-250);late.Invoke(scatter,null);
        typeof(FarScatter).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scatter,null);
        Console.WriteLine("NEAR TIER AND CLEANUP PASS");
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

// Adds the engine signatures used by the complete World and Features builders to the parcel harness substitutes.
namespace UnityEngine
{
    public partial class Mesh
    {
        public int subMeshCount;
        public void SetVertices(List<Vector3> v){vertices=v.ToArray();}
        public void SetNormals(List<Vector3> n){normals=n.ToArray();}
        public void SetUVs(int n,List<Vector2> v){uv=v.ToArray();}
        public void SetTriangles(List<int> t,int sub){foreach(int i in t)if(i<0||i>=vertices.Length)throw new Exception("Bad mesh index");DroneSandbox.RenderAudit.triangles+=t.Count/3;}
        public void RecalculateTangents(){}
    }
}
namespace DroneSandbox
{
    // Records source-verified all-LOD costs and positions; native FBX imports are not available to this harness.
    public static partial class RenderAudit
    {
        public static int triangles;
        public static readonly List<(Vector2 p,string model,float scale,string group)> placed=new List<(Vector2,string,float,string)>();
        public static readonly HashSet<string> newModels=new HashSet<string>();
        public static bool Tree(string m)=>m=="Scenery_Tree"||m=="Scenery_Tree_B"||m=="Scenery_Spruce"||m=="Scenery_Pine"||m=="Scenery_Pine_B"||m=="Scenery_Birch"||m=="Scenery_Poplar";
    }
    public static partial class Ground
    {
        public static object Current=>null;
        public static Vector3 OnGround(Vector3 p)=>new Vector3(p.x,0,p.z);
        static readonly List<(Vector2 a,Vector2 b,float radius)> worn=new List<(Vector2,Vector2,float)>();
        public static void Build(Transform t){worn.Clear();}
        public static void FlushWear(){}
        public static void Wear(Vector2 a,Vector2 b,float w,float strength){worn.Add((a,b,w/2));}
        public static void Paint(Vector2 a,Vector2 b,float w,float strength,int layer){}
        public static bool Worn(Vector2 p){foreach(var w in worn){var d=w.b-w.a;float t=d.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(p-w.a,d)/d.sqrMagnitude):0;if(Vector2.Distance(p,w.a+d*t)<w.radius)return true;}return false;}
    }
    public static partial class Art
    {
        public static void Swap(GameObject o,string a,string b){}
        public static GameObject TryModel(string m,Transform parent,float scale=1f)
        {
            var p=parent.position;string group=parent.parent?.name??"";
            RenderAudit.renderers+=ModelRendererCount(m,p);
            RenderAudit.placed.Add((new Vector2(p.x,p.z),m,scale,group));
            RenderAudit.props.Add(new{model=m,x=p.x,z=p.z,yaw=0f,scale,group});
            return new GameObject(m);
        }
        public static GameObject Part(PrimitiveType type,Transform parent,Vector3 p,Vector3 scale,Color c,Vector3? euler=null)
        {RenderAudit.renderers++;return new GameObject("Part");}
    }
    public static class Undergrowth
    {
        static readonly Dictionary<(int,int),List<Vector2>> cells=new Dictionary<(int,int),List<Vector2>>();
        public static void Reset(){cells.Clear();}
        public static void Build(Transform t){}
        public static void NoteTree(Vector2 p){var key=(Mathf.FloorToInt(p.x/8),Mathf.FloorToInt(p.y/8));if(!cells.TryGetValue(key,out var list)){list=new List<Vector2>();cells.Add(key,list);}list.Add(p);}
        public static bool UnderTrees(Vector2 p,float r){int x=Mathf.FloorToInt(p.x/8),y=Mathf.FloorToInt(p.y/8);for(int i=x-1;i<=x+1;i++)for(int j=y-1;j<=y+1;j++)if(cells.TryGetValue((i,j),out var list))foreach(var q in list)if(Vector2.Distance(p,q)<r)return true;return false;}
    }
    public static class Scans
    {
        public static readonly string[] Boulders={""},Rocks={""},Stumps={""};
        public static GameObject Rock(string[] names,double roll,Vector3 p,float yaw,float scale,Transform root,string fallback,float fallbackScale,float sink)
            =>World.Place(fallback,p,yaw,fallbackScale,sink,root);
    }
    public static class WorldPlan { public static bool Ready=>true;public static void Generate(int seed){} }
    public static class FarTerrain { public static void Build(){} }
    public static class Backdrop { public static void Build(Transform t){} }
    public static class Water { public static void Build(Transform t){} }
    public static class Reservoir { public static void Build(Transform t){} }
    public static class Grass { public static void Plant(object t){} }
    public static class Bases { public static void BuildPlayerBase(Transform t){} public static void BuildEnemyHq(Transform t){} public static void PreviewUnits(Transform t){} }
}

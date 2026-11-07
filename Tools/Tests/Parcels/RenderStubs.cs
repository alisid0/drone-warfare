using System;
using System.Collections.Generic;
using UnityEngine;

// Executes the unchanged Farmland builder without graphics. Renderer and model placement counts include inactive LODs.
namespace UnityEngine
{
    public class Object { public string name; }
    public class Transform { public void SetParent(Transform p, bool b) { } }
    public class GameObject : Object
    {
        public Transform transform = new Transform();
        public GameObject(string n) { name=n; }
        public T AddComponent<T>() where T:new() { var c=new T(); if(c is Renderer) DroneSandbox.RenderAudit.renderers++; return c; }
    }
    public class Renderer : Object { public Material sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; }
    public class MeshRenderer : Renderer { }
    public class MeshFilter { public Mesh sharedMesh; }
    public struct Bounds { public Vector3 center; }
    public class Mesh : Object
    {
        public Rendering.IndexFormat indexFormat; public Vector3[] vertices, normals; public Vector2[] uv; public int[] triangles;
        public Bounds bounds;
        public void RecalculateBounds() { }
        public void RecalculateNormals()
        {
            foreach(int i in triangles) if(i<0 || i>=vertices.Length) throw new Exception("Bad mesh index");
            for(int i=0;i<triangles.Length;i+=3)
            {
                var normal=Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]);
                if(normal.y < -0.0001f) throw new Exception("Boundary faces down");
            }
        }
    }
    public struct LOD { public float threshold; public Renderer[] renderers; public LOD(float t,Renderer[] r){threshold=t;renderers=r;} }
    public class LODGroup
    {
        public Vector3 localReferencePoint; public float size;
        public void SetLODs(LOD[] levels)
        {
            if(levels.Length!=2 || levels[0].threshold<=levels[1].threshold) throw new Exception("Bad hedge LOD");
            if(levels[1].renderers[0].shadowCastingMode!=Rendering.ShadowCastingMode.Off) throw new Exception("Far hedge shadows");
        }
    }
    public static class Resources { public static T Load<T>(string name) where T:new() => new T(); }
    public class Material : Object
    {
        public Color color; public Texture2D mainTexture;
        public Material(){} public Material(Material m){}
        public void SetFloat(string n,float f){} public void SetTexture(string n,Texture2D t){}
        public void SetTextureScale(string n,Vector2 v){} public void SetTextureOffset(string n,Vector2 v){}
    }
    public enum TextureFormat { R8, RGBA32 }
    public enum TextureWrapMode { Repeat, Clamp }
    public enum FilterMode { Trilinear }
    public class Texture2D : Object
    {
        public TextureWrapMode wrapMode; public FilterMode filterMode; public int anisoLevel;
        public Texture2D(){} public Texture2D(int w,int h,TextureFormat f,bool mip,bool linear=false){}
        public void SetPixelData(byte[] data,int mip) { DroneSandbox.RenderAudit.detailTexels+=data.Length; }
        public void SetPixels32(Color32[] data){}
        public void Apply(bool mip,bool unreadable){}
    }
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}
        public static Color Lerp(Color a,Color b,float t)=>new Color(Mathf.Lerp(a.r,b.r,t),Mathf.Lerp(a.g,b.g,t),Mathf.Lerp(a.b,b.b,t));
        public static Color operator *(Color c,float f)=>new Color(c.r*f,c.g*f,c.b*f);
        public static implicit operator Color32(Color c)=>new Color32();
    }
    public struct Color32 { }
}
namespace UnityEngine.Rendering { public enum IndexFormat { UInt32 } public enum ShadowCastingMode { Off, On } }
namespace DroneSandbox
{
    public static class RenderAudit
    {
        public static int renderers, detailTexels;
        public static readonly List<object> props=new List<object>();
        public static readonly List<Vector2> trees=new List<Vector2>();
        public static readonly List<object> oldEdges=new List<object>();
        static List<float[]> oldPoints;
        public static void BeginOldEdge(){oldPoints=new List<float[]>();}
        public static void NoteOldEdge(Vector2 p){oldPoints?.Add(new[]{p.x,p.y});}
        public static void EndOldEdge(){if(oldPoints!=null&&oldPoints.Count>0)oldEdges.Add(new{kind="Hedge",a=oldPoints[0],b=oldPoints[oldPoints.Count-1],points=oldPoints});oldPoints=null;}
        public static void Reset(){renderers=detailTexels=0;props.Clear();trees.Clear();oldEdges.Clear();oldPoints=null;}
    }
    public static partial class Ground
    {
        public static float HeightAt(Vector3 p)=>0;
        public static Vector3 NormalAt(Vector3 p)=>Vector3.up;
        public static float[] Lattice(int n,Random r){var a=new float[n*n];for(int i=0;i<a.Length;i++)a[i]=(float)r.NextDouble();return a;}
        public static float SampleLattice(float[] l,int n,float x,float y)=>l[(Mathf.FloorToInt(y)%n)*n+Mathf.FloorToInt(x)%n];
    }
    public static class Art
    {
        public static void Discard(UnityEngine.Object o){}
        public static Material NewMaterial(Color c,bool glow=false)=>new Material();
        public static Material Shared(Color c,bool glow=false)=>new Material();
        // FBX source and previous model audit: each tree has two mesh LODs and one crown card; fences/bales/bushes one mesh.
        public static int ModelRendererCount(string m,Vector3 p)=>m=="Scenery_Tree"||m=="Scenery_Birch"||m=="Scenery_Poplar"?3:1;
    }
    public static class World
    {
        public static GameObject Place(string model,Vector3 pos,float yaw,float scale,float sink,Transform parent,Action<Transform> fallback=null)
        {
            int cost=Art.ModelRendererCount(model,pos);
            var p=new Vector2(pos.x,pos.z);
            if(cost==3)
            {
                foreach(var q in RenderAudit.trees) if(Vector2.Distance(q,p)<3.2f)return null;
                RenderAudit.trees.Add(p);
            }
            RenderAudit.renderers+=cost;
            RenderAudit.NoteOldEdge(p);
            RenderAudit.props.Add(new{model,x=pos.x,z=pos.z,yaw,scale});
            return new GameObject(model);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using N = System.Numerics;
using DroneSandbox;
using UnityEngine;

// Managed substitutes only for native engine storage/submission; every production generator runs unchanged.
namespace UnityEngine
{
    public class Object { public string name; }
    public class Component : Object { public Transform transform = new Transform(); public GameObject owner; public T GetComponent<T>() where T:class => owner.GetComponent<T>(); }
    public class MonoBehaviour : Component { }
    public class Transform { public Vector3 position; public Matrix4x4 worldToLocalMatrix=Matrix4x4.identity,localToWorldMatrix=Matrix4x4.identity; }
    public class GameObject : Object {
        public Transform transform=new Transform(); public MeshRenderer renderer; public LODGroup group;
        public T GetComponent<T>() where T:class => typeof(T)==typeof(LODGroup)?group as T:typeof(T)==typeof(MeshFilter)?renderer?.filter as T:renderer as T;
        public T[] GetComponentsInChildren<T>() where T:class => new[]{renderer as T};
        public T GetComponentInChildren<T>() where T:class => renderer as T;
    }
    public class Renderer : Component { public Material[] sharedMaterials; }
    public class MeshRenderer : Renderer { public MeshFilter filter; }
    public class MeshFilter { public Mesh sharedMesh; }
    public struct LOD { public Renderer[] renderers; }
    public class LODGroup { public LOD[] lods; public LOD[] GetLODs()=>lods; }
    public struct Plane { }
    public class Camera : Object { public static Camera main=new Camera(); public Transform transform=new Transform(); public Vector3 target; }
    public static class GeometryUtility {
        public static void CalculateFrustumPlanes(Camera c,Plane[] p){}
        public static bool TestPlanesAABB(Plane[] p,Bounds b) {
            var cam=Camera.main; Vector3 v=b.center-cam.transform.position, f=(cam.target-cam.transform.position).normalized;
            float depth=Vector3.Dot(v,f), radius=b.extents.magnitude;
            if(depth+radius<0 || depth-radius>34000)return false;
            Vector3 right=Vector3.Cross(f,Vector3.up).normalized; if(right.sqrMagnitude<.1f)right=Vector3.right;
            Vector3 up=Vector3.Cross(right,f).normalized;
            return Math.Abs(Vector3.Dot(v,right))<depth*.736f+radius && Math.Abs(Vector3.Dot(v,up))<depth*.4143f+radius;
        }
    }
    public struct Bounds {
        public Vector3 center,size; public Vector3 extents=>size*.5f;
        public Bounds(Vector3 c,Vector3 s){center=c;size=s;}
        public float SqrDistance(Vector3 p){Vector3 e=extents,d=p-center;float x=Math.Max(0,Math.Abs(d.x)-e.x),y=Math.Max(0,Math.Abs(d.y)-e.y),z=Math.Max(0,Math.Abs(d.z)-e.z);return x*x+y*y+z*z;}
        public void Encapsulate(Vector3 p){Vector3 min=center-extents,max=center+extents;min=new Vector3(Math.Min(min.x,p.x),Math.Min(min.y,p.y),Math.Min(min.z,p.z));max=new Vector3(Math.Max(max.x,p.x),Math.Max(max.y,p.y),Math.Max(max.z,p.z));center=(min+max)*.5f;size=max-min;}
    }
    public struct Quaternion { public float yaw; public static Quaternion Euler(float x,float y,float z)=>new Quaternion{yaw=y*Mathf.Deg2Rad}; }
    public struct Matrix4x4 {
        public N.Matrix4x4 value;
        public static Matrix4x4 identity=>new Matrix4x4{value=N.Matrix4x4.Identity};
        public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>new Matrix4x4{value=N.Matrix4x4.CreateScale(s.x,s.y,s.z)*N.Matrix4x4.CreateRotationY(q.yaw)*N.Matrix4x4.CreateTranslation(p.x,p.y,p.z)};
        public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>new Matrix4x4{value=b.value*a.value};
        public Vector3 MultiplyPoint3x4(Vector3 p){var v=N.Vector3.Transform(new N.Vector3(p.x,p.y,p.z),value);return new Vector3(v.X,v.Y,v.Z);}
    }
    public class Mesh : Object {
        public Rendering.IndexFormat indexFormat; Vector3[] vertexData=Array.Empty<Vector3>();public Vector3[] originalVertices;public Vector3[] vertices {get=>vertexData;set {vertexData=value;if(originalVertices==null)originalVertices=value;}}public Vector3[] normals;public Vector2[] uv;public int[] triangles=Array.Empty<int>();public Bounds bounds;public int subMeshCount=1,modelTriangles;
        public void SetVertices(List<Vector3> a)=>vertices=a.ToArray();public void SetUVs(int ch,List<Vector2> a)=>uv=a.ToArray();public void SetTriangles(List<int> t,int s,bool bounds=true)=>triangles=t.ToArray();
        public void RecalculateBounds(){if(vertices.Length==0)return;bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);}
        public void RecalculateNormals(){normals=new Vector3[vertices.Length];for(int k=0;k<triangles.Length;k+=3){int a=triangles[k],b=triangles[k+1],c=triangles[k+2];var n=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).normalized;normals[a]=normals[b]=normals[c]=n;}}
        public void MarkDynamic(){}
    }
    public class Material : Object {
        public Color color=Color.white;public bool enableInstancing;public Texture2D mainTexture;
        public Material(){} public Material(Material m){color=m.color;name=m.name;}
        public void SetFloat(string p,float f){}
    }
    public enum TextureFormat { RGBA32 } public enum TextureWrapMode { Clamp } public enum FilterMode { Point,Trilinear }
    public class Texture2D : Object {
        public int width,height,anisoLevel;public Color32[] pixels;public TextureWrapMode wrapMode;public FilterMode filterMode;
        public Texture2D(int w,int h,TextureFormat f,bool mip){width=w;height=h;}
        public void SetPixels32(Color32[] p)=>pixels=p;public void Apply(bool mip,bool unreadable){}
    }
    public class TextAsset : Object { public byte[] bytes; }
    public struct Color {
        public float r,g,b,a; public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}
        public static Color white=>new Color(1,1,1);
        public Color linear=>new Color(L(r),L(g),L(b),a);public Color gamma=>new Color(G(r),G(g),G(b),a);
        static float L(float c)=>c<=.04045f?c/12.92f:(float)Math.Pow((c+.055f)/1.055f,2.4);
        static float G(float c)=>c<=.0031308f?c*12.92f:1.055f*(float)Math.Pow(c,1/2.4)-.055f;
        public static Color operator *(Color c,float f)=>new Color(c.r*f,c.g*f,c.b*f,c.a*f);
        public static Color Lerp(Color a,Color b,float f)=>new Color(Mathf.Lerp(a.r,b.r,f),Mathf.Lerp(a.g,b.g,f),Mathf.Lerp(a.b,b.b,f),Mathf.Lerp(a.a,b.a,f));
    }
    public struct Color32 {
        public byte r,g,b,a;public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}
        public static Color32 Lerp(Color32 a,Color32 b,float t)=>(Color32)Color.Lerp(a,b,t); public static implicit operator Color(Color32 c)=>new Color(c.r/255f,c.g/255f,c.b/255f,c.a/255f);
        public static implicit operator Color32(Color c)=>new Color32((byte)Mathf.Clamp(c.r*255+.5f,0,255),(byte)Mathf.Clamp(c.g*255+.5f,0,255),(byte)Mathf.Clamp(c.b*255+.5f,0,255),(byte)Mathf.Clamp(c.a*255+.5f,0,255));
    }
    public static class Resources {
        static readonly Dictionary<string,GameObject> loaded=new Dictionary<string,GameObject>();
        public static T Load<T>(string path) where T:class {
            if(typeof(T)==typeof(TextAsset))return new TextAsset{bytes=File.ReadAllBytes(Harness.Root+"/Assets/DroneSandbox/Resources/Textures/ForestCrown.bytes")} as T;
            if(typeof(T)==typeof(Material))return new Material{name=path.Substring(path.LastIndexOf('/')+1)} as T;
            string name=path.Substring(path.LastIndexOf('/')+1);
            if(!loaded.TryGetValue(name,out GameObject go)) {
                var json=Harness.Models.GetProperty(name);go=new GameObject{name=name,group=new LODGroup{lods=new LOD[json.GetArrayLength()]}};
                foreach(var row in json.EnumerateArray()) {
                    int lod=row.GetProperty("lod").GetInt32();var min=V(row.GetProperty("lo"));var max=V(row.GetProperty("hi"));
                    var mesh=new Mesh{name=name+"_LOD"+lod,bounds=new Bounds((min+max)*.5f,max-min),modelTriangles=row.GetProperty("triangles").GetInt32(),subMeshCount=row.GetProperty("materials").GetArrayLength()};
                    var r=new MeshRenderer{filter=new MeshFilter{sharedMesh=mesh},sharedMaterials=new Material[mesh.subMeshCount]};int i=0;
                    foreach(var m in row.GetProperty("materials").EnumerateArray())r.sharedMaterials[i++]=new Material{name=m.GetString()};
                    var owner=new GameObject{renderer=r};r.owner=owner;go.group.lods[lod]=new LOD{renderers=new Renderer[]{r}};go.renderer=r;
                }
                loaded[name]=go;
            }
            return go as T;
        }
        static Vector3 V(JsonElement a)=>new Vector3(a[0].GetSingle(),a[1].GetSingle(),a[2].GetSingle());
    }
    public struct RenderParams { public Camera camera;public Bounds worldBounds;public Rendering.ShadowCastingMode shadowCastingMode;public bool receiveShadows;public Material material;public RenderParams(Material m){camera=null;worldBounds=default;shadowCastingMode=default;receiveShadows=false;material=m;} }
    public static class Graphics {
        public static bool capture,fullCanopy;public static int calls,triangles;public static List<object> instances=new List<object>(),meshes=new List<object>();
        public static void Reset(){calls=triangles=0;instances.Clear();meshes.Clear();}
        public static void RenderMeshInstanced(RenderParams rp,Mesh mesh,int sub,Matrix4x4[] matrices,int count,int start=0){
            if(!capture)return;calls++;
            if(sub!=0)return;
            triangles+=mesh.modelTriangles*count;
            for(int i=0;i<count;i++){var m=matrices[start+i].value;instances.Add(new{model=mesh.name,matrix=new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44},tint=rp.material.color.r});}
        }
        public static void RenderMesh(RenderParams rp,Mesh mesh,int sub,Matrix4x4 matrix){
            if(!capture)return;calls++;triangles+=mesh.triangles.Length/3;
            var tex=rp.material.mainTexture;string texture=Harness.ExportTexture(tex);
            var vertices=new float[mesh.vertices.Length][];var normals=new float[mesh.vertices.Length][];var uv=new float[mesh.vertices.Length][];
            for(int i=0;i<vertices.Length;i++){var v=mesh.vertices[i];
                if(fullCanopy&&mesh.name.StartsWith("Forest canopy")){v=mesh.originalVertices[i];}
                vertices[i]=new[]{v.x,v.y,v.z};var n=mesh.normals[i];normals[i]=new[]{n.x,n.y,n.z};uv[i]=new[]{mesh.uv[i].x,mesh.uv[i].y};}
            meshes.Add(new{name=mesh.name,vertices,normals,uv,indices=mesh.triangles,texture});
        }
    }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } public enum IndexFormat { UInt16,UInt32 } }
namespace DroneSandbox {
    public static class FarDetail { public static float ScatterHeight(Vector2 p,bool tree)=>FarTerrain.GroundAt(p); }
    public static class Ground { public const float Size=2000f; }
    public static class Art { public static Material NewMaterial(Color c)=>new Material{color=c};public static void Discard(UnityEngine.Object o){} }
}

using System;
using System.Collections.Generic;

// Managed storage for executing the complete production builders. These types are NEVER used in the API build.
namespace UnityEngine
{
    public class Object { public string name; }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : new() => gameObject.GetComponent<T>();
    }
    public class Transform : Object
    {
        public GameObject gameObject;
        public Transform parent;
        public Vector3 position, localScale, up; public Vector3 lossyScale=>localScale;
        public Quaternion rotation;
        public void SetParent(Transform p, bool stay) { parent = p; }
        public T[] GetComponentsInChildren<T>() where T:new()=>new T[0]; public Vector3 TransformPoint(Vector3 p) => position + p;
    }
    public class GameObject : Object
    {
        public Transform transform;
        readonly Dictionary<Type, object> components = new Dictionary<Type, object>();
        public GameObject(string name) { this.name = name; transform = new Transform { name = name, gameObject = this }; }
        public T AddComponent<T>() where T : new()
        {
            var c = new T(); components[typeof(T)] = c;
            if (c is Component component) component.gameObject = this;
            return c;
        }
        public T GetComponent<T>() where T : new() => components.TryGetValue(typeof(T), out var c) ? (T)c : AddComponent<T>();
    }
    public struct Quaternion
    {
        public static Quaternion operator *(Quaternion a, Quaternion b) => b;
        public Vector3 forward;
        public static Quaternion LookRotation(Vector3 d) => new Quaternion { forward = d.normalized };
        public static Quaternion Euler(float x, float y, float z) => LookRotation(new Vector3(Mathf.Sin(y*Mathf.Deg2Rad),0,Mathf.Cos(y*Mathf.Deg2Rad)));
    }
    public enum PrimitiveType { Cube, Cylinder }
    public class Renderer : Component { public bool enabled; public Material sharedMaterial; public Material[] sharedMaterials; public Rendering.ShadowCastingMode shadowCastingMode; }
    public class MeshRenderer : Renderer { public static readonly List<MeshRenderer> All = new List<MeshRenderer>(); public MeshRenderer() { All.Add(this); } }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class Mesh : Object
    { public static readonly List<Mesh> All=new List<Mesh>(); public Mesh(){All.Add(this);} public readonly Dictionary<int,int[]> subs=new Dictionary<int,int[]>();
        public Rendering.IndexFormat indexFormat; public int subMeshCount; public void SetNormals(List<Vector3> n){normals=n.ToArray();}
        public Vector3[] vertices, normals; public Vector2[] uv; public int[] triangles;
        public void SetVertices(List<Vector3> v) { vertices = v.ToArray(); }
        public void SetUVs(int channel, List<Vector2> v) { uv = v.ToArray(); }
        public void SetTriangles(List<int> t, int sub) { triangles = t.ToArray(); subs[sub]=triangles; }
        public void RecalculateNormals()
        {
            normals = new Vector3[vertices.Length];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a=triangles[i], b=triangles[i+1], c=triangles[i+2];
                var n=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                normals[a]+=n; normals[b]+=n; normals[c]+=n;
            }
            for (int i=0;i<normals.Length;i++) normals[i]=normals[i].normalized;
        }
        public void RecalculateTangents() { }
        public void RecalculateBounds() { }
        public Bounds bounds => new Bounds();
    }
    public struct Bounds { public Vector3 center; }
    public class Shader { public static Shader Find(string name)=>new Shader(); } public class Material : Object { public Color color; public Material(){} public Material(Shader s){} public Material(Material m){ color = m.color; } public Texture2D mainTexture; public readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(); public readonly Dictionary<string, Vector2> scales = new Dictionary<string, Vector2>(), offsets = new Dictionary<string, Vector2>(); public void SetTexture(string n, Texture2D t) { textures[n] = t; } public void SetTextureScale(string n, Vector2 s) { scales[n] = s; } public void SetTextureOffset(string n, Vector2 o) { offsets[n] = o; } public void SetFloat(string n, float v) { } }
    public static class Resources { public static T Load<T>(string path) where T : new() => new T(); }
    public enum TextureFormat { RGBA32, R8 }
    public enum TextureWrapMode { Clamp, Repeat } public enum FilterMode { Point, Bilinear, Trilinear }
    public class Texture2D : Object
    {
        public TextureWrapMode wrapMode; public int anisoLevel; public FilterMode filterMode; public int width, height; public byte[] data;
        public Texture2D() { }
        public Texture2D(int w,int h,TextureFormat f,bool mip,bool linear=false) { width = w; height = h; }
        public void SetPixels(Color[] p) { }
        public Color32[] pixels32; public void SetPixels32(Color32[] p) { pixels32 = p; }
        public void SetPixelData(byte[] p,int mip) { data = p; }
        public void Apply(bool mip=true,bool unreadable=false) { }
    }
    public struct Color { public Color gamma=>this;
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1f) { this.r=r;this.g=g;this.b=b;this.a=a; }
        public Color linear=>new Color((float)Math.Pow(r,2.2),(float)Math.Pow(g,2.2),(float)Math.Pow(b,2.2),a); public static Color operator*(Color c,float f)=>new Color(c.r*f,c.g*f,c.b*f,c.a*f); public static Color white => new Color(1,1,1);
        public static Color Lerp(Color a,Color b,float t)=>new Color(Mathf.Lerp(a.r,b.r,t),Mathf.Lerp(a.g,b.g,t),Mathf.Lerp(a.b,b.b,t));
        public static implicit operator Color32(Color c)=>new Color32((byte)Math.Round(Math.Max(0,Math.Min(1,c.r))*255),(byte)Math.Round(Math.Max(0,Math.Min(1,c.g))*255),(byte)Math.Round(Math.Max(0,Math.Min(1,c.b))*255),255);
        public static explicit operator Vector4(Color c)=>new Vector4();
    }
    public struct Color32 { public byte r,g,b,a; public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;} public static implicit operator Color(Color32 c)=>new Color(c.r/255f,c.g/255f,c.b/255f,c.a/255f); }
    public struct Vector4 { public Vector4(float x,float y,float z,float w){} public static Vector4 zero=>new Vector4(); public static Vector4 one=>new Vector4(); }
    public class AnimationCurve { public static AnimationCurve Constant(float a,float b,float c)=>new AnimationCurve(); }
    public class TerrainLayer : Object { public Texture2D diffuseTexture, normalMapTexture; public Vector2 tileSize; public Vector4 diffuseRemapMin,diffuseRemapMax; }
    public class TerrainData : Object
    {
        public int heightmapResolution, alphamapResolution;
        public Vector3 size; public TerrainLayer[] terrainLayers;
        public float[,] heights;
        public void SetHeights(int x,int z,float[,] h) { heights=h; }
        public float[,,] alphamaps; public void SetAlphamaps(int x,int z,float[,,] h) { if (x == 0 && z == 0) alphamaps = h; }
        public float[,,] GetAlphamaps(int x,int z,int w,int h)=>new float[h,w,5];
        public Vector3 GetInterpolatedNormal(float x,float z)=>Vector3.up;
        public float Height(float x,float z)
        {
            float gx=Mathf.Clamp01(x)*(heightmapResolution-1), gz=Mathf.Clamp01(z)*(heightmapResolution-1);
            int ix=Math.Min((int)gx,heightmapResolution-2), iz=Math.Min((int)gz,heightmapResolution-2);
            return Mathf.Lerp(Mathf.Lerp(heights[iz,ix],heights[iz,ix+1],gx-ix),Mathf.Lerp(heights[iz+1,ix],heights[iz+1,ix+1],gx-ix),gz-iz)*size.y;
        }
    }
    public class Terrain : Component
    {
        public TerrainData terrainData; public float heightmapPixelError,basemapDistance; public bool drawInstanced; public Material materialTemplate;
        public static GameObject CreateTerrainGameObject(TerrainData data) { var go=new GameObject("Terrain");go.AddComponent<Terrain>().terrainData=data;return go; }
        public float SampleHeight(Vector3 p)=>terrainData.Height((p.x-transform.position.x)/terrainData.size.x,(p.z-transform.position.z)/terrainData.size.z);
    }
    public class TerrainCollider : Component { public bool Raycast(Ray ray,out RaycastHit hit,float distance) {hit=default;return false;} }
    public struct Ray { public Vector3 GetPoint(float distance)=>Vector3.zero; }
    public struct RaycastHit { public Vector3 point; }
    public struct Plane { public Plane(Vector3 normal,Vector3 point){} public bool Raycast(Ray r,out float t){t=0;return false;} }
}
namespace UnityEngine.Rendering { public enum IndexFormat { UInt32 } public enum ShadowCastingMode { Off, On } }

namespace UnityEngine { public struct LOD { public float screenRelativeTransitionHeight; public LOD(float h,Renderer[] r){screenRelativeTransitionHeight=h;} } public class LODGroup : Component { public Vector3 localReferencePoint; public float size=100f; public LOD[] GetLODs()=>new LOD[0]; public void SetLODs(LOD[] l){} public void RecalculateBounds(){} } }

namespace UnityEngine { public class MonoBehaviour : Component {} public static class Time { public static float time; } public class Camera : Component {} public struct Matrix4x4 { public static Matrix4x4 TRS(Vector3 p,Quaternion r,Vector3 s)=>new Matrix4x4(); } }
namespace UnityEngine.Rendering { public struct ScriptableRenderContext {} }

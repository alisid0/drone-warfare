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
        public Vector3 position, localScale, up;
        public Quaternion rotation;
        public void SetParent(Transform p, bool stay) { parent = p; }
        public Vector3 TransformPoint(Vector3 p) => position + p;
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
        public Vector3 forward;
        public static Quaternion LookRotation(Vector3 d) => new Quaternion { forward = d.normalized };
        public static Quaternion Euler(float x, float y, float z) => LookRotation(new Vector3(Mathf.Sin(y*Mathf.Deg2Rad),0,Mathf.Cos(y*Mathf.Deg2Rad)));
    }
    public enum PrimitiveType { Cube, Cylinder }
    public class Renderer : Component { public bool enabled; public Material sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; }
    public class MeshRenderer : Renderer { }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class Mesh : Object
    {
        public Rendering.IndexFormat indexFormat;
        public Vector3[] vertices, normals; public Vector2[] uv; public int[] triangles;
        public void SetVertices(List<Vector3> v) { vertices = v.ToArray(); }
        public void SetUVs(int channel, List<Vector2> v) { uv = v.ToArray(); }
        public void SetTriangles(List<int> t, int sub) { triangles = t.ToArray(); }
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
    }
    public class Material : Object { public void SetFloat(string n, float v) { } }
    public static class Resources { public static T Load<T>(string path) where T : new() => new T(); }
    public enum TextureFormat { RGBA32, R8 }
    public enum TextureWrapMode { Clamp, Repeat }
    public class Texture2D : Object
    {
        public TextureWrapMode wrapMode; public int anisoLevel;
        public Texture2D() { }
        public Texture2D(int w,int h,TextureFormat f,bool mip,bool linear=false) { }
        public void SetPixels(Color[] p) { }
        public void SetPixels32(Color32[] p) { }
        public void SetPixelData(byte[] p,int mip) { }
        public void Apply(bool mip=true,bool unreadable=false) { }
    }
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1f) { this.r=r;this.g=g;this.b=b;this.a=a; }
        public static Color white => new Color(1,1,1);
        public static Color Lerp(Color a,Color b,float t)=>new Color(Mathf.Lerp(a.r,b.r,t),Mathf.Lerp(a.g,b.g,t),Mathf.Lerp(a.b,b.b,t));
        public static implicit operator Color32(Color c)=>new Color32();
        public static explicit operator Vector4(Color c)=>new Vector4();
    }
    public struct Color32 { }
    public struct Vector4 { public static Vector4 zero=>new Vector4(); public static Vector4 one=>new Vector4(); }
    public class AnimationCurve { public static AnimationCurve Constant(float a,float b,float c)=>new AnimationCurve(); }
    public class TerrainLayer : Object { public Texture2D diffuseTexture, normalMapTexture; public Vector2 tileSize; public Vector4 diffuseRemapMin,diffuseRemapMax; }
    public class TerrainData : Object
    {
        public int heightmapResolution, alphamapResolution;
        public Vector3 size; public TerrainLayer[] terrainLayers;
        public float[,] heights;
        public void SetHeights(int x,int z,float[,] h) { heights=h; }
        public void SetAlphamaps(int x,int z,float[,,] h) { }
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
namespace UnityEngine.Rendering { public enum IndexFormat { UInt32 } public enum ShadowCastingMode { Off } }

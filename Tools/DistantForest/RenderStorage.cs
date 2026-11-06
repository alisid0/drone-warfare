using System;
using System.Collections.Generic;

// These recording doubles exercise scheduling and API contracts; the separate api.csproj uses real Unity assemblies.
namespace UnityEngine
{
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => default;
        public static Vector4 one => new Vector4(1, 1, 1, 1);
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0);
        public static bool operator ==(Vector4 a, Vector4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(Vector4 a, Vector4 b) => !(a == b);
        public override bool Equals(object b) => b is Vector4 v && this == v;
        public override int GetHashCode() => x.GetHashCode();
    }
    public struct Matrix4x4
    {
        public Vector3 position, scale;
        public static Matrix4x4 identity => new Matrix4x4 { scale = Vector3.one };
        public static Matrix4x4 TRS(Vector3 p, Quaternion r, Vector3 s) => new Matrix4x4 { position = p, scale = s };
        public Vector3 MultiplyPoint3x4(Vector3 p) => position + new Vector3(p.x * scale.x, p.y * scale.y, p.z * scale.z);
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => new Matrix4x4 { position = a.MultiplyPoint3x4(b.position), scale = new Vector3(a.scale.x * b.scale.x, a.scale.y * b.scale.y, a.scale.z * b.scale.z) };
    }
    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 c, Vector3 s) { center = c; size = s; }
        public Vector3 extents => size * .5f;
        public Vector3 min => center - extents;
        public Vector3 max => center + extents;
        public void Encapsulate(Vector3 p)
        {
            var a = min; var b = max;
            a = new Vector3(Math.Min(a.x, p.x), Math.Min(a.y, p.y), Math.Min(a.z, p.z));
            b = new Vector3(Math.Max(b.x, p.x), Math.Max(b.y, p.y), Math.Max(b.z, p.z));
            center = (a + b) * .5f; size = b - a;
        }
        public void Encapsulate(Bounds b) { Encapsulate(b.min); Encapsulate(b.max); }
        public void Expand(float n) { size += Vector3.one * n; }
        public float SqrDistance(Vector3 p)
        {
            var a = min; var b = max;
            return (p - new Vector3(Mathf.Clamp(p.x, a.x, b.x), Mathf.Clamp(p.y, a.y, b.y), Mathf.Clamp(p.z, a.z, b.z))).sqrMagnitude;
        }
    }
    public struct Plane
    {
        public Bounds box;
        public Plane(Vector3 n, Vector3 p) { box = default; }
        public bool Raycast(Ray r, out float t) { t = 0; return false; }
    }
    public static class GeometryUtility
    {
        public static void CalculateFrustumPlanes(Camera c, Plane[] p) { p[0].box = new Bounds(c.transform.position, new Vector3(c.reach * 2, 30000, c.reach * 2)); }
        public static bool TestPlanesAABB(Plane[] p, Bounds b)
        {
            var a = p[0].box;
            return b.min.x <= a.max.x && b.max.x >= a.min.x && b.min.z <= a.max.z && b.max.z >= a.min.z;
        }
    }
    public enum FilterMode { Trilinear }
    public enum CameraType { Game, SceneView }
    public class Camera : Component { public CameraType cameraType; public float reach = 4000f; }
    public static class Time { public static int frameCount; public static float unscaledTime; }
    public class Shader
    {
        static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();
        public static bool Unsupported, Missing, MissingProperty;
        public string name;
        public bool isSupported => name != "DroneSandbox/ForestLit" || !Unsupported;
        public static int PropertyToID(string s) => s.GetHashCode();
        public static Shader Find(string name)
        {
            if (Missing && name == "DroneSandbox/ForestLit") return null;
            if (!shaders.TryGetValue(name, out var shader)) shaders[name] = shader = new Shader { name = name };
            return shader;
        }
    }
    public class Material : Object
    {
        public Color color;
        public Shader shader;
        public bool enableInstancing;
        public Texture2D mainTexture;
        readonly Dictionary<int, float> floats = new Dictionary<int, float>();
        readonly Dictionary<int, Vector4> vectors = new Dictionary<int, Vector4>();
        public Material() { }
        public Material(Shader s) { shader = s; }
        public Material(Material m) { name = m.name; shader = m.shader; color = m.color; enableInstancing = m.enableInstancing; }
        public bool HasProperty(int id) => shader.name == "DroneSandbox/ForestLit" && !Shader.MissingProperty;
        public void SetFloat(string n, float f) => SetFloat(Shader.PropertyToID(n), f);
        public void SetFloat(int n, float f) => floats[n] = f;
        public float GetFloat(int n) => floats.TryGetValue(n, out float f) ? f : 0;
        public void SetVector(int n, Vector4 v) => vectors[n] = v;
        public Vector4 GetVector(int n) => vectors.TryGetValue(n, out var v) ? v : default;
    }
    public class MaterialPropertyBlock
    {
        readonly Dictionary<int, Vector4> vectors = new Dictionary<int, Vector4>();
        readonly Dictionary<int, float> floats = new Dictionary<int, float>();
        public void SetVector(int n, Vector4 v) => vectors[n] = v;
        public Vector4 GetVector(int n) => vectors.TryGetValue(n, out var v) ? v : default;
        public void SetFloat(int n, float f) => floats[n] = f;
        public float GetFloat(int n) => floats.TryGetValue(n, out float f) ? f : 0;
    }
    public struct RenderParams
    {
        public Material material;
        public Camera camera;
        public Bounds worldBounds;
        public MaterialPropertyBlock matProps;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public RenderParams(Material m) { this = default; material = m; }
    }
    public static class Graphics
    {
        public static int instances, calls, meshes;
        public static void RenderMeshInstanced(RenderParams rp, Mesh mesh, int sub, Matrix4x4[] matrices, int count)
        {
            if (!rp.material.enableInstancing || count > 511 || rp.camera == null) throw new Exception("Invalid instancing submission");
            instances += count; calls++;
        }
        public static void RenderMesh(RenderParams rp, Mesh mesh, int sub, Matrix4x4 transform) { meshes++; }
    }
    public static class Resources
    {
        static readonly Dictionary<string, Object> assets = new Dictionary<string, Object>();
        public static T Load<T>(string path)
        {
            if (typeof(T) == typeof(Texture2D)) return (T)(object)new Texture2D();
            if (typeof(T) == typeof(Material))
                return (T)(object)new Material(Shader.Find(path.StartsWith("Materials/Forest/") ? "DroneSandbox/ForestLit" : "HDRP/Lit")) { name = path.Substring(path.LastIndexOf('/') + 1), color = Color.white, enableInstancing = true };
            if (typeof(T) != typeof(GameObject)) return default;
            if (!assets.TryGetValue(path, out var asset))
            {
                var model = new GameObject(path); var lods = new LOD[3];
                for (int tier = 0; tier < 3; tier++)
                {
                    var part = new GameObject("Part"); var renderer = part.AddComponent<MeshRenderer>();
                    string material = tier == 2 ? "ImpostorOak" : "LeavesOak";
                    renderer.sharedMaterials = new[] { new Material(Shader.Find("HDRP/Lit")) { name = material, color = Color.white } };
                    int triangles = tier == 2 ? 8 : tier == 1 || path.Contains("Reduced") ? 3000 : 23000;
                    part.AddComponent<MeshFilter>().sharedMesh = new Mesh { subMeshCount = 1, triangles = new int[triangles * 3] };
                    lods[tier] = new LOD { renderers = new Renderer[] { renderer } };
                }
                model.AddComponent<LODGroup>().lods = lods; assets[path] = asset = model;
            }
            return (T)(object)asset;
        }
    }
}
namespace UnityEngine.Rendering
{
    public struct ScriptableRenderContext { }
    public static class RenderPipelineManager
    {
        public static event Action<ScriptableRenderContext, UnityEngine.Camera> beginCameraRendering;
        public static void Render(UnityEngine.Camera camera) => beginCameraRendering?.Invoke(default, camera);
    }
}

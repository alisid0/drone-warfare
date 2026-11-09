using System;
using System.Collections.Generic;

// These recording doubles exercise scheduling and API contracts; the separate api.csproj uses real Unity assemblies.
namespace UnityEngine
{
    // Store camera parameters so draw isolation and material probes can be checked.
    public struct Vector4
    {
        public float x, y, z, w;
        // Initialize managed storage with the values supplied by production code.
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => default;
        public static Vector4 one => new Vector4(1, 1, 1, 1);
        // Provide the value arithmetic used by camera parameters and cached transforms.
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0);
        // Provide the value arithmetic used by camera parameters and cached transforms.
        public static bool operator ==(Vector4 a, Vector4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        // Provide the value arithmetic used by camera parameters and cached transforms.
        public static bool operator !=(Vector4 a, Vector4 b) => !(a == b);
        // Support exact property comparisons in the material validation path.
        public override bool Equals(object b) => b is Vector4 v && this == v;
        // Keep value-key lookup available to the managed test environment.
        public override int GetHashCode() => x.GetHashCode();
    }
    // Retain translation and scale for test batches; rotation and GPU transforms are outside this double.
    public struct Matrix4x4
    {
        public Vector3 position, scale;
        public static Matrix4x4 identity => new Matrix4x4 { scale = Vector3.one };
        // Cache translation and scale for synthetic trees whose rotation is immaterial to these checks.
        public static Matrix4x4 TRS(Vector3 p, Quaternion r, Vector3 s) => new Matrix4x4 { position = p, scale = s };
        // Transform the synthetic mesh center for batch culling bounds.
        public Vector3 MultiplyPoint3x4(Vector3 p) => position + new Vector3(p.x * scale.x, p.y * scale.y, p.z * scale.z);
        // Provide the value arithmetic used by camera parameters and cached transforms.
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => new Matrix4x4 { position = a.MultiplyPoint3x4(b.position), scale = new Vector3(a.scale.x * b.scale.x, a.scale.y * b.scale.y, a.scale.z * b.scale.z) };
    }
    // Implement the bounds operations used by production culling and cache eviction.
    public struct Bounds
    {
        public Vector3 center, size;
        // Initialize managed storage with the values supplied by production code.
        public Bounds(Vector3 c, Vector3 s) { center = c; size = s; }
        public Vector3 extents => size * .5f;
        public Vector3 min => center - extents;
        public Vector3 max => center + extents;
        // Expand the test bounds using the same min/max rule as Unity.
        public void Encapsulate(Vector3 p)
        {
            var a = min; var b = max;
            a = new Vector3(Math.Min(a.x, p.x), Math.Min(a.y, p.y), Math.Min(a.z, p.z));
            b = new Vector3(Math.Max(b.x, p.x), Math.Max(b.y, p.y), Math.Max(b.z, p.z));
            center = (a + b) * .5f; size = b - a;
        }
        // Expand the test bounds using the same min/max rule as Unity.
        public void Encapsulate(Bounds b) { Encapsulate(b.min); Encapsulate(b.max); }
        // Add the production crown guard to the cached batch bounds.
        public void Expand(float n) { size += Vector3.one * n; }
        // Measure footprint distance for the production cache eviction rules.
        public float SqrDistance(Vector3 p)
        {
            var a = min; var b = max;
            return (p - new Vector3(Mathf.Clamp(p.x, a.x, b.x), Mathf.Clamp(p.y, a.y, b.y), Mathf.Clamp(p.z, a.z, b.z))).sqrMagnitude;
        }
    }
    // Carry a rectangular test view instead of claiming to simulate a native camera frustum.
    public struct Plane
    {
        public Bounds box;
        // Initialize managed storage with the values supplied by production code.
        public Plane(Vector3 n, Vector3 p) { box = default; }
        // Leave unrelated terrain-ray tests to their own harness.
        public bool Raycast(Ray r, out float t) { t = 0; return false; }
    }
    // Select a repeatable world window for coverage scheduling tests.
    public static class GeometryUtility
    {
        // Set the rectangular coverage window used by this scheduler test.
        public static void CalculateFrustumPlanes(Camera c, Plane[] p) { p[0].box = new Bounds(c.transform.position, new Vector3(c.reach * 2, 30000, c.reach * 2)); }
        // Reject blocks outside the test window using axis-aligned overlap.
        public static bool TestPlanesAABB(Plane[] p, Bounds b)
        {
            var a = p[0].box;
            return b.min.x <= a.max.x && b.max.x >= a.min.x && b.min.z <= a.max.z && b.max.z >= a.min.z;
        }
    }
    public enum FilterMode { Trilinear }
    public enum CameraType { Game, SceneView }
    // Give each test observer a distinct transform and view reach.
    public class Camera : Component { public CameraType cameraType; public float reach = 4000f; }
    // Expose controlled shader failures for the production fallback checks.
    public class Shader
    {
        static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();
        public static bool Unsupported, Missing, MissingProperty;
        public string name;
        public bool isSupported => name != "DroneSandbox/ForestLit" || !Unsupported;
        // Give property names stable identities within this test process.
        public static int PropertyToID(string s) => s.GetHashCode();
        // Return stable shader identities or the requested missing-shader failure.
        public static Shader Find(string name)
        {
            if (Missing && name == "DroneSandbox/ForestLit") return null;
            if (!shaders.TryGetValue(name, out var shader)) shaders[name] = shader = new Shader { name = name };
            return shader;
        }
    }
    // Record material properties and simulate failed writes without a graphics device.
    public class Material : Object
    {
        public static bool IgnoreWrites;
        public Color color;
        public Shader shader;
        public bool enableInstancing;
        public Texture2D mainTexture;
        readonly Dictionary<int, float> floats = new Dictionary<int, float>();
        readonly Dictionary<int, Vector4> vectors = new Dictionary<int, Vector4>();
        // Initialize managed storage with the values supplied by production code.
        public Material() { }
        // Initialize managed storage with the values supplied by production code.
        public Material(Shader s) { shader = s; }
        // Initialize managed storage with the values supplied by production code.
        public Material(Material m) { name = m.name; shader = m.shader; color = m.color; enableInstancing = m.enableInstancing; }
        // Expose the missing-property failure independently of shader availability.
        public bool HasProperty(int id) => shader.name == "DroneSandbox/ForestLit" && !Shader.MissingProperty;
        // Record a float so failed property writes can exercise the fallback.
        public void SetFloat(string n, float f) => SetFloat(Shader.PropertyToID(n), f);
        // Record a float so failed property writes can exercise the fallback.
        public void SetFloat(int n, float f) { if (!IgnoreWrites) floats[n] = f; }
        // Read the stored float for the production validation probe.
        public float GetFloat(int n) => floats.TryGetValue(n, out float f) ? f : 0;
        // Record a vector so camera state can be checked at submission.
        public void SetVector(int n, Vector4 v) { if (!IgnoreWrites) vectors[n] = v; }
        // Read the stored camera parameters without substituting shader defaults.
        public Vector4 GetVector(int n) => vectors.TryGetValue(n, out var v) ? v : default;
    }
    // Record per-draw camera values independently of material defaults.
    public class MaterialPropertyBlock
    {
        readonly Dictionary<int, Vector4> vectors = new Dictionary<int, Vector4>();
        readonly Dictionary<int, float> floats = new Dictionary<int, float>();
        // Record a vector so camera state can be checked at submission.
        public void SetVector(int n, Vector4 v) => vectors[n] = v;
        // Read the stored camera parameters without substituting shader defaults.
        public Vector4 GetVector(int n) => vectors.TryGetValue(n, out var v) ? v : default;
        // Record a float so failed property writes can exercise the fallback.
        public void SetFloat(int n, float f) => floats[n] = f;
        // Read the stored float for the production validation probe.
        public float GetFloat(int n) => floats.TryGetValue(n, out float f) ? f : 0;
    }
    // Carry the same draw arguments validated by the separate Unity assembly compilation.
    public struct RenderParams
    {
        public Material material;
        public Camera camera;
        public Bounds worldBounds;
        public MaterialPropertyBlock matProps;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        // Initialize managed storage with the values supplied by production code.
        public RenderParams(Material m) { this = default; material = m; }
    }
    // Validate submissions immediately while leaving uploads and shader execution to Unity.
    public static class Graphics
    {
        public static int instances, calls, meshes;
        // Check batch capacity and camera isolation before recording submitted trees.
        public static void RenderMeshInstanced(RenderParams rp, Mesh mesh, int sub, Matrix4x4[] matrices, int count)
        {
            if (!rp.material.enableInstancing || count > 511 || rp.camera == null) throw new Exception("Invalid instancing submission");
            Vector4 expected = rp.camera.transform.position; expected.w = 1;
            if (rp.matProps.GetVector(Shader.PropertyToID("_ForestCamera")) != expected) throw new Exception("Camera state leaked between draws");
            instances += count; calls++;
        }
        // Count a canopy submission without executing a native upload.
        public static void RenderMesh(RenderParams rp, Mesh mesh, int sub, Matrix4x4 transform) { meshes++; }
    }
    // Supply conservative synthetic tree meshes and serialized-material equivalents.
    public static class Resources
    {
        static readonly Dictionary<string, Object> assets = new Dictionary<string, Object>();
        // Create and cache synthetic assets with deliberately expensive tree triangle counts.
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
    // Provide the unused render-context argument without starting a pipeline.
    public struct ScriptableRenderContext { }
    // Invoke the production camera callback with a deterministic test clock.
    public static class RenderPipelineManager
    {
        public static event Action<ScriptableRenderContext, UnityEngine.Camera> beginCameraRendering;
        // Deliver a camera callback to the real FarScatter component.
        public static void Render(UnityEngine.Camera camera) => beginCameraRendering?.Invoke(default, camera);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

// These managed storage substitutes run only the data harness; the API target uses real Unity assemblies.
namespace UnityEngine
{
    public class Object { }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { public bool enabled; }
    public class GameObject : Object
    {
        readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        public Transform transform; public bool active;
        public GameObject(string name) { transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; components[typeof(T)] = c; return c; }
        public T GetComponent<T>() where T : class => components.TryGetValue(typeof(T), out var c) ? c as T : null;
        public void SetActive(bool on) { active = on; }
    }
    public class Transform : Component { public Vector3 position; public void SetParent(Transform p, bool stay) { } }
    public class Mesh : Object
    {
        public Vector3[] vertices, normals; public Vector2[] uv; public int[] triangles;
        public Rendering.IndexFormat indexFormat; public Bounds bounds;
        public void Clear() { vertices = null; normals = null; uv = null; triangles = null; }
        public void SetVertices(Vector3[] v) { vertices = (Vector3[])v.Clone(); }
        public void SetNormals(Vector3[] n) { normals = (Vector3[])n.Clone(); }
        public void SetUVs(int channel, Vector2[] u) { uv = (Vector2[])u.Clone(); }
        public void SetIndices(int[] t, MeshTopology topology, int sub, bool bounds = true) { triangles = (int[])t.Clone(); }
    }
    public enum MeshTopology { Triangles }
    public struct Bounds { public Vector3 center, size; public Bounds(Vector3 c, Vector3 s) { center = c; size = s; } }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class MeshRenderer : Component { public Material sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; }
    public class Material : Object { public Texture2D mainTexture; public void SetFloat(string key, float value) { } }
    public struct Color { public static Color white => default; }
    public class MeshCollider : Component { public Mesh sharedMesh; public bool enabled; }
    public class LODGroup : Component { public int forced = -1; public void ForceLOD(int n) { forced = n; } }
    public class Camera : Component { public static Camera main; }
    public static class Time { public static float unscaledTime, unscaledDeltaTime = 1f / 60f; }
    public static class Debug { public static void LogException(Exception e) { throw e; } }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Clamp }
    public class Texture2D : Object
    {
        public string name; public TextureWrapMode wrapMode; public int anisoLevel; public Color32[] pixels;
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public void SetPixels32(Color32[] p) { pixels = p; }
        public void SetPixelData<T>(T[] p, int mip) where T : struct { pixels = (Color32[])(object)p.Clone(); }
        public void Apply(bool mip, bool unreadable) { }
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color32 Lerp(Color32 a, Color32 b, float t) => new Color32((byte)Mathf.Lerp(a.r, b.r, t), (byte)Mathf.Lerp(a.g, b.g, t), (byte)Mathf.Lerp(a.b, b.b, t), 255);
    }
    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 origin, Vector3 direction) { this.origin = origin; this.direction = direction.normalized; }
        public Vector3 GetPoint(float distance) => origin + direction * distance;
    }
}
namespace UnityEngine.Rendering { public enum IndexFormat { UInt32 } public enum ShadowCastingMode { Off } }
namespace DroneSandbox
{
    public static class Art { public static void Discard(UnityEngine.Object o) { } public static Material NewMaterial(Color c) => new Material(); }
    public static class Ground { public const float Size = 480f * Scale.Distance; }
    public static class FarTerrain
    {
        public static bool Active = true; public static Vector2 Origin;
        public const float NearReach = 1500f;
        public static float SurfaceAt(Vector2 p) => GroundAt(p);
        public static float GroundAt(Vector2 p) => WorldPlan.Height(p.x + Origin.x, p.y + Origin.y);
        public static float CanopyAt(float x, float z) => FarPicture.CanopyAt(x, z);
        public static bool Raycast(Ray ray, out Vector3 p) { p = default; return false; }
    }
    public class Drone : MonoBehaviour { public bool IsAlive = true; }
    public class DroneFeed { public static DroneFeed Instance; public bool Visible = true; public Drone Source; }
}

using System.Collections.Generic;
using UnityEngine;

// Scene services are omitted, but Ground, Water, World and every planner are complete production sources.
namespace DroneSandbox
{
    public static class Theatre
    {
        public static readonly List<Hotspot> Hotspots = new List<Hotspot>();
        public static float Advance { get; set; }
        public static float Front(float x) => 0.5f + 0.13f * Mathf.Sin(x * 6.3f) + Advance;
    }
    public static class Difficulty { public static bool SpareDrone => false; public static int DecoyAdjustment(int n) => 0; }
    public static class Factions { public const string PlayerBase = "Forward Base Kestrel"; }
    public static class FarTerrain
    { public static Vector2 Origin => Vector2.zero;
        public static bool Active => false;
        public static void Build() { }
        public static float SurfaceAt(Vector2 p) => 0;
        public static bool Raycast(Ray r, out Vector3 p) { p = default; return false; }
    }
    public static class Backdrop { public static void Build(Transform t) { } }
    public static class Reservoir { public static void Build(Transform t) { } }
    public static class Grass { public static void Plant(Terrain t) { } }

    
    public static class Bases
    {
        public static void BuildPlayerBase(Transform t) { }
        public static void BuildEnemyHq(Transform t) { }
        public static void PreviewUnits(Transform t) { }
    }
    public static class Art
    {
        public static readonly List<(string model, Transform root, GameObject mesh)> placed = new List<(string, Transform, GameObject)>();
        public static void Discard(Object o) { }
        public static Material NewMaterial(Color c, bool glow = false) => new Material(Shader.Find("HDRP/Lit")) { color=c }; public static Material Shared(Color c, bool glow = false) => NewMaterial(c);
        public static int ModelRendererCount(string m, Vector3 p) => 3;
        public static GameObject TryModel(string m, Transform t, float scale = 1f)
        {
            var mesh = new GameObject(m); mesh.transform.localScale = Vector3.one * scale;
            placed.Add((m, t, mesh));
            return mesh;
        }
        public static GameObject Part(PrimitiveType type, Transform t, Vector3 p, Vector3 s, Color c, Vector3? euler = null) => new GameObject("Primitive");
        public static void Swap(GameObject o, string a, string b) { }
    }
    // The same 8 m neighbour grid used by the existing scenery harness enforces the production spacing query.
    public static class Undergrowth
    { public static readonly List<Vector2> Trees = new List<Vector2>();
        static readonly Dictionary<(int, int), List<Vector2>> cells = new Dictionary<(int, int), List<Vector2>>();
        public static void Reset() { cells.Clear(); }
        public static void Build(Transform t) { }
        public static void NoteTree(Vector2 p)
        {
            var key = (Mathf.FloorToInt(p.x / 8), Mathf.FloorToInt(p.y / 8));
            if (!cells.TryGetValue(key, out var list)) { list = new List<Vector2>(); cells.Add(key, list); }
            list.Add(p);
        }
        public static bool UnderTrees(Vector2 p, float r)
        {
            int x = Mathf.FloorToInt(p.x / 8), y = Mathf.FloorToInt(p.y / 8);
            for (int i = x - 1; i <= x + 1; i++) for (int j = y - 1; j <= y + 1; j++)
                if (cells.TryGetValue((i, j), out var list)) foreach (var q in list) if (Vector2.Distance(p, q) < r) return true;
            return false;
        }
    }
    public static class Scans
    {
        public static readonly string[] Boulders = { }, Rocks = { }, Stumps = { };
        public static GameObject Rock(string[] names, double roll, Vector3 p, float yaw, float scale, Transform root, string fallback, float fallbackScale, float sink) => null;
    }
}

namespace DroneSandbox {
public static class SectorForest { public static void Build(System.Random r,System.Collections.Generic.List<UnityEngine.Vector2> w){} public static bool HasFloorCoverage(UnityEngine.Vector2 p)=>false; public static UnityEngine.Vector2 EdgeLean(UnityEngine.Vector2 w,float c,bool l)=>default; }
public class FarScatter : UnityEngine.Component { public void Clear(){} public static bool InSector(UnityEngine.Vector2 p)=>false; }
}

namespace DroneSandbox { public sealed class Roadside { public static void Build(UnityEngine.Transform t, ref int allowance) { } } }

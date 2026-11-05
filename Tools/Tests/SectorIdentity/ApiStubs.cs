using UnityEngine;
// Compile-only signatures read from the game's dependencies; actual Unity types come from CoreModule.
namespace DroneSandbox {
public enum EnemyType { Radar,AAGun,Truck,Tank,APC,Artillery,Jammer,Launcher,Command,Fuel,Tanker,Supply,Infantry,SAM }
public static class Ground {
 public const float Size=480f*Scale.Distance;
 public struct FlatSite { public Vector3 center; public float radius; public bool bare; public FlatSite(Vector3 c,float r,bool b){center=c;radius=r;bare=b;} }
 public static object Current=>null;
 public static void Build(Transform t){} public static float HeightAt(Vector3 p)=>0;
 public static Vector3 NormalAt(Vector3 p)=>Vector3.up; public static Vector3 OnGround(Vector3 p)=>p;
 public static float[] Lattice(int n,System.Random r)=>null; public static float SampleLattice(float[] l,int n,float x,float y)=>0;
 public static void Wear(Vector2 a,Vector2 b,float w,float s){} public static void Paint(Vector2 a,Vector2 b,float w,float s,int layer){}
 public static void FlushWear(){} public static bool Worn(Vector2 p)=>false;
}
public static class Art {
 public static void Discard(Object o){} public static Material NewMaterial(Color c,bool glow=false)=>null;
 public static Material Shared(Color c,bool glow=false)=>null; public static int ModelRendererCount(string m,Vector3 p)=>1;
 public static GameObject TryModel(string m,Transform t,float scale=1f)=>null;
 public static GameObject Part(PrimitiveType type,Transform t,Vector3 p,Vector3 s,Color c,Vector3? euler=null)=>null;
 public static void Swap(GameObject o,string a,string b){}
}
public static class Undergrowth { public static void Reset(){} public static void Build(Transform t){} public static void NoteTree(Vector2 p){} public static bool UnderTrees(Vector2 p,float r)=>false; }
public static class Scans {
 public static readonly string[] Boulders={},Rocks={},Stumps={};
 public static GameObject Rock(string[] names,double roll,Vector3 p,float yaw,float scale,Transform root,string fallback,float fallbackScale,float sink)=>null;
}
public static class WorldPlan { public static bool Ready=>true; public static void Generate(int seed){} }
public static class FarTerrain { public static void Build(){} }
public static class Backdrop { public static void Build(Transform t){} }
public static class Water { public static void Build(Transform t){} }
public static class Reservoir { public static void Build(Transform t){} }
public static class Grass { public static void Plant(object t){} }
public static class Bases { public static void BuildPlayerBase(Transform t){} public static void BuildEnemyHq(Transform t){} public static void PreviewUnits(Transform t){} }
}

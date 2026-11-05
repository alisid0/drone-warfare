using UnityEngine;
// Compile-only signatures read from Ground, Art and World. Unity types come from the installed engine assembly;
// this library is never executed and never enters the game's Assets directory.
namespace DroneSandbox {
public enum EnemyType { Radar, AAGun, Truck, Tank, APC, Artillery, Jammer, Launcher, Command, Fuel, Tanker, Supply, Infantry, SAM }
public static class Ground {
public const float Size=480f*Scale.Distance;
public struct FlatSite { public Vector3 center; public float radius; public bool bare; public FlatSite(Vector3 c,float r,bool b){center=c;radius=r;bare=b;} }
public static float HeightAt(Vector3 p)=>0;
public static Vector3 NormalAt(Vector3 p)=>Vector3.up;
public static float[] Lattice(int n,System.Random r)=>null;
public static float SampleLattice(float[] l,int n,float x,float y)=>0;
}
public static class Art {
public static void Discard(Object o){}
public static Material NewMaterial(Color c,bool glow=false)=>null;
public static Material Shared(Color c,bool glow=false)=>null;
public static int ModelRendererCount(string m,Vector3 p)=>1;
}
public static class World {
public static GameObject Place(string model,Vector3 pos,float yaw,float scale,float sink,Transform parent,System.Action<Transform> fallback=null)=>null;
}
}

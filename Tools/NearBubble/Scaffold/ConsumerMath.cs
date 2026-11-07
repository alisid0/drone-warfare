using System;

// Standalone math substitutes: the engine's native PerlinNoise is unavailable outside Unity.
// The fixed reference Perlin permutation below is used for BOTH baseline and candidate plans.
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2();
        public static Vector2 Min(Vector2 a,Vector2 b)=>new Vector2(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y));
        public static Vector2 Max(Vector2 a,Vector2 b)=>new Vector2(Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
        public static Vector2 one => new Vector2(1,1);
        public static Vector2 right => new Vector2(1, 0);
        public static Vector2 up => new Vector2(0, 1);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector2 normalized => magnitude > 0 ? this / magnitude : zero;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float b) => new Vector2(a.x * b, a.y * b);
        public static Vector2 operator *(float b, Vector2 a) => a * b;
        public static Vector2 operator /(Vector2 a, float b) => a * (1 / b);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * Mathf.Clamp01(t);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x,-a.y,-a.z);
        public static Vector3 zero => new Vector3();
        public static Vector3 one => new Vector3(1,1,1);
        public static Vector3 right => new Vector3(1,0,0);
        public static Vector3 down => new Vector3(0,-1,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? this / magnitude : zero;
        public static Vector3 operator *(Vector3 a,float s)=>new Vector3(a.x*s,a.y*s,a.z*s);
        public static Vector3 operator /(Vector3 a,float s)=>a*(1f/s);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*Mathf.Clamp01(t);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    }
    public struct Rect
    {
        public float x, y, width, height;
        public float xMin=>x; public float xMax=>x+width; public float yMin=>y; public float yMax=>y+height;
        public Vector2 center=>new Vector2(x+width/2,y+height/2); public Vector2 size=>new Vector2(width,height);
        public static Rect MinMaxRect(float a,float b,float c,float d)=>new Rect(a,b,c-a,d-b);
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
    }
    public static class Mathf
    {
        public static float Floor(float x)=>(float)Math.Floor(x);
        public static float Pow(float x,float y)=>(float)Math.Pow(x,y);
        public static float Exp(float f)=>(float)System.Math.Exp(f); public const float PI = (float)Math.PI, Deg2Rad = PI / 180, Rad2Deg = 180 / PI;
        public static float Abs(float x) => Math.Abs(x);
        public static float Sign(float x) => x < 0 ? -1 : 1;
        public static float Sqrt(float x) => (float)Math.Sqrt(x);
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Tan(float x) => (float)Math.Tan(x);
        public static float Atan(float x) => (float)Math.Atan(x);
        public static float Atan2(float x, float y) => (float)Math.Atan2(x, y);
        public static int CeilToInt(float x) => (int)Math.Ceiling(x);
        public static int FloorToInt(float x) => (int)Math.Floor(x);
        public static int RoundToInt(float x) => (int)Math.Round(x);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(params float[] x) { float a=x[0]; foreach(float b in x) a=Min(a,b); return a; }
        public static float Max(params float[] x) { float a = x[0]; foreach (float b in x) a = Max(a, b); return a; }
        public static float Clamp(float x, float a, float b) => Max(a, Min(b, x));
        public static int Clamp(int x, int a, int b) => Max(a, Min(b, x));
        public static float Clamp01(float x) => Clamp(x, 0, 1);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float x) => Clamp01((x - a) / (b - a));
        public static float SmoothStep(float a, float b, float t) { t = Clamp01(t); return a + (b - a) * t * t * (3 - 2 * t); }
        public static float Repeat(float x, float n) => Clamp(x - (float)Math.Floor(x / n) * n, 0, n);
        static readonly int[] perm = {151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180};
        static int P(int x) => perm[x & 255];
        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        static float Grad(int h, float x, float y) { h &= 15; float u = h < 8 ? x : y, v = h < 4 ? y : h == 12 || h == 14 ? x : 0; return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v); }
        public static float LinearToGammaSpace(float x) => x <= .0031308f ? 12.92f * x : 1.055f * (float)Math.Pow(x, 1 / 2.4) - .055f;
        public static float PerlinNoise(float x, float y)
        {
            int ix = FloorToInt(x), iy = FloorToInt(y); x -= ix; y -= iy;
            float u = Fade(x), v = Fade(y);
            return .5f + .5f * Lerp(Lerp(Grad(P(P(P(ix) + iy)), x, y), Grad(P(P(P(ix + 1) + iy)), x - 1, y), u),
                Lerp(Grad(P(P(P(ix) + iy + 1)), x, y - 1), Grad(P(P(P(ix + 1) + iy + 1)), x - 1, y - 1), u), v);
        }
    }
}

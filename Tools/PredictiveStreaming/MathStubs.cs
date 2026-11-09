using System;

// The few UnityEngine value types FarDetailRoute uses, with Unity's own formulas, so the harness runs the
// production planner without the engine.
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? new Vector2(x / m, y / m) : zero; } }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }
        public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDelta)
        {
            Vector2 d = target - current; float m = d.magnitude;
            return m <= maxDelta || m == 0f ? target : current + d / m * maxDelta;
        }
        public static Vector2 ClampMagnitude(Vector2 v, float max) { float m = v.magnitude; return m > max ? v / m * max : v; }
    }

    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Abs(float f) => Math.Abs(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
    }
}

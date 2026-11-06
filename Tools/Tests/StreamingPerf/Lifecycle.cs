using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DroneSandbox;
using UnityEngine;

// Execute the two production schedulers with instrumented engine boundaries; this does not measure native time.
static class LifecycleChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // Partial copies must stay hidden, denied commits must do no work, and cancelled painters must drain first.
    static void Main()
    {
        var plan = new FarDetailData(); var cache = new FarDetailRegional(plan);
        var worker = typeof(FarDetailRegional).GetField("worker", Private);
        var eye = new Vector2(-12000, 16000);
        for (int i = 0; i < 2500 && cache.Committed < 81; i++)
        {
            (worker.GetValue(cache) as Task)?.GetAwaiter().GetResult();
            int copies = Graphics.Copies, commits = cache.Committed;
            cache.Tick(eye, false);
            if (Graphics.Copies != copies || cache.Committed != commits) throw new Exception("Denied upload advanced");
            cache.Tick(eye, true);
        }
        if (cache.Committed != 81) throw new Exception("Regional ring did not fill");
        // Invalidate a toroidal slot, copy one band, then teleport away before that page can publish.
        var far = new Vector2(13000, -13000);
        for (int i = 0; i < 7; i++) { (worker.GetValue(cache) as Task)?.GetAwaiter().GetResult(); cache.Tick(far, true); }
        int before = cache.Committed;
        cache.Tick(eye, true);
        if (cache.Committed != before) throw new Exception("Teleported page was published");
        cache.Tick(far, true); var pending = worker.GetValue(cache) as Task;
        cache.Clear(); pending?.GetAwaiter().GetResult();
        for (int i = 0; i < 20; i++) cache.Tick(far, true);
        if (cache.Committed != before) throw new Exception("Cleared region published work");
        Console.WriteLine($"Regional lifecycle passed: 81 pages, {Graphics.Copies} aligned copies, no partial/stale publication");

        var painter = new FarWorldPicture(); var first = new Texture2D(1, 1, TextureFormat.DXT1, 1, false);
        var last = new Texture2D(1, 1, TextureFormat.DXT1, 1, false);
        var update = typeof(FarWorldPicture).GetMethod("Update", Private);
        painter.Begin(plan, first);
        if (!FarDetailData.Started.Wait(3000)) throw new Exception("Painter did not start");
        painter.Begin(new FarDetailData(), last);
        if (FarDetailData.Starts != 1) throw new Exception("Replacement overlapped the old picture allocation");
        FarDetailData.Release.Set();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (painter.Busy && timer.ElapsedMilliseconds < 3000) { update.Invoke(painter, null); Thread.Sleep(1); }
        if (painter.Busy || FarTerrain.Published != last || FarTerrain.Publications != 1 || FarDetailData.Peak != 1)
            throw new Exception("Cancelled world picture published or overlapped");
        typeof(FarWorldPicture).GetMethod("OnDestroy", Private).Invoke(painter, null);
        Console.WriteLine("Picture lifecycle passed: cancellation drains before replacement, only latest texture published");
        FarTerrain.FailUpload = true;
        painter = new FarWorldPicture();
        painter.Begin(new FarDetailData(), last); timer.Restart();
        while (painter.Busy && timer.ElapsedMilliseconds < 3000) { update.Invoke(painter, null); Thread.Sleep(1); }
        update.Invoke(painter, null);
        if (painter.Busy || FarTerrain.Failures != 1 || Debug.Errors != 1) throw new Exception("Failed upload retained or retried its result");
        Console.WriteLine("Picture upload failure releases its result and is not retried");
    }
}

namespace DroneSandbox
{
    // Controlled data workers make lifecycle races reproducible without allocating a full world picture.
    public sealed class FarDetailData
    {
        public static readonly ManualResetEventSlim Started = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
        public static int Starts, Peak, Active;
        public Color32[] WorldPixels(int size, CancellationToken token)
        {
            Interlocked.Increment(ref Starts); int active = Interlocked.Increment(ref Active); Peak = Math.Max(Peak, active); Started.Set();
            try { Release.Wait(); token.ThrowIfCancellationRequested(); return new Color32[1]; }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
    public sealed class FarDetailRegionData
    {
        public const int Resolution = 544, Mips = 3;
        public static long Bytes => 194208;
        public int x, z; public byte[][] mips; public double milliseconds;
        public static FarDetailRegionData Build(FarDetailData p, int x, int z) => new FarDetailRegionData { x = x, z = z,
            mips = new[] { new byte[147968], new byte[36992], new byte[9248] } };
    }
    public static class WorldPlan { public const float Half = 28000f; }
    public static class Art { public static void Discard(object o) { } }
    public static class FarTerrain
    {
        public static Texture2D WorldTexture => null;
        public static Texture2D Published; public static int Publications, Failures; public static bool FailUpload;
        public static void FinishPicture(Texture2D t, Color32[] p, double ms)
        { if (FailUpload) throw new InvalidOperationException("Simulated upload failure"); Published = t; Publications++; }
        public static void PictureFailed() { Failures++; }
    }
}

namespace UnityEngine
{
    // Minimal engine boundaries validate production copy rectangles and shader publication, not graphics performance.
    public class MonoBehaviour { }
    public struct Color32 { }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector4
    {
        public float x, y, z, w; public static Vector4 zero => default;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }
    public enum TextureFormat { DXT1 }
    public enum TextureWrapMode { Clamp }
    public enum FilterMode { Trilinear }
    public class Texture2D
    {
        public string name; public TextureWrapMode wrapMode;
        public Texture2D(int w, int h, TextureFormat f, int m, bool linear) { }
        public void SetPixelData(byte[] p, int m) { }
        public void Apply(bool a, bool b) { }
    }
    public class Texture2DArray
    {
        public string name; public TextureWrapMode wrapMode; public FilterMode filterMode; public int anisoLevel;
        public Texture2DArray(int w, int h, int d, TextureFormat f, int m, bool linear) { }
        public void Apply(bool a, bool b) { }
    }
    public static class SystemInfo
    {
        public static bool supports2DArrayTextures => true;
        public static bool SupportsTextureFormat(TextureFormat f) => true;
        public static Rendering.CopyTextureSupport copyTextureSupport => Rendering.CopyTextureSupport.DifferentTypes;
    }
    public static class Time { public static float unscaledTime => 1f; }
    public static class Debug { public static int Errors; public static void LogWarning(string s) { } public static void LogException(Exception e) { Errors++; } }
    public static class Shader
    {
        public static readonly Vector4[] Pages = new Vector4[81];
        public static void SetGlobalFloat(string n, float v) { }
        public static void SetGlobalTexture(string n, object v) { }
        public static void SetGlobalVectorArray(string n, Vector4[] p)
        {
            for (int i = 0; i < p.Length; i++)
            {
                if (p[i].w > 0f && Pages[i].w == 0f && Graphics.Rows[i] != 544 + 272 + 136)
                    throw new Exception("Page published before every mip row was copied");
                if (p[i].w == 0f && Pages[i].w > 0f) Graphics.Rows[i] = 0;
                Pages[i] = p[i];
            }
        }
    }
    public static class Graphics
    {
        public static readonly int[] Rows = new int[81]; public static int Copies;
        public static void CopyTexture(Texture2D source, int e, int m, int x, int y, int w, int h, Texture2DArray dest, int slot, int dm, int dx, int dy)
        {
            if ((x | y | w | h) % 4 != 0 || h > 128 || y + h > (544 >> m) || m != dm || x != dx || y != dy)
                throw new Exception("BC1 copy is unaligned, oversized or outside its mip");
            if (Shader.Pages[slot].w != 0) throw new Exception("Visible page was modified");
            if (m == 0 && y == 0) Rows[slot] = 0;
            Rows[slot] += h; Copies++;
        }
    }
    public struct Rect { public Rect(float x, float y, float w, float h) { } }
    public static class Screen { public static int height => 1080; }
    public static class GUI { public static void Box(Rect r, string s) { } }
}
namespace UnityEngine.Rendering { [Flags] public enum CopyTextureSupport { DifferentTypes = 1 } }

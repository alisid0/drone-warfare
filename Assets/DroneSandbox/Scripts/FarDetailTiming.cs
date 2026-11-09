using System;
using System.Diagnostics;
using System.Text;

namespace DroneSandbox
{
    // Allocation-free stage counters distinguish native stalls from queue work and collections during a flight.
    public static class FarDetailTiming
    {
        public enum Stage { Plan, Schedule, Sample, TileMesh, TilePixels, TileApply, ControlPixels, ControlApply,
            CollisionMesh, CollisionBakeDispatch, ColliderBind, ChunkMesh, MeshUpload, Publish,
            RegionPlan, RegionPixels, RegionApply, RegionCopy, RegionPublish,
            NearPlan, NearPixels, NearApply, NearCopy, NearPublish, Count }
        static readonly double[] worst = new double[(int)Stage.Count];
        static readonly long[] calls = new long[(int)Stage.Count];
        static readonly string[] names = Enum.GetNames(typeof(Stage));
        public static int GcFrames { get; private set; }
        public static int GcOverruns { get; private set; }
        public static long AllocatedBytes { get; private set; }
        public static long WorstFrameBytes { get; private set; }
        public static Stage WorstFrameStage { get; private set; }
        static double frameWorst;
        static Stage frameStage;
        static long allocated;
        static int collections;

        // Reset alongside FarDetail's existing flight metrics; no strings are formatted during streaming.
        public static void Reset()
        {
            Array.Clear(worst, 0, worst.Length); Array.Clear(calls, 0, calls.Length);
            GcFrames = GcOverruns = 0; AllocatedBytes = WorstFrameBytes = 0;
            WorstFrameStage = Stage.Plan;
        }

        // Collection counts include worker-triggered collections; overlap is evidence, not proof of causation.
        public static void BeginFrame()
        {
            frameWorst = 0; frameStage = Stage.Plan;
            allocated = GC.GetAllocatedBytesForCurrentThread(); collections = GC.CollectionCount(0);
        }

        // Return a timestamp so each call site can measure exactly one API stage without a delegate allocation.
        public static long Begin() => Stopwatch.GetTimestamp();

        // All writers are on the main thread, even when the measured work dispatches a worker.
        public static void End(Stage stage, long start)
        {
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            int i = (int)stage; worst[i] = Math.Max(worst[i], ms); calls[i]++;
            if (ms > frameWorst) { frameWorst = ms; frameStage = stage; }
        }

        // Associate the slowest frame with its largest stage, retaining the original whole-frame budget test.
        public static void EndFrame(double elapsed, bool worstFrame)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            AllocatedBytes += bytes; WorstFrameBytes = Math.Max(WorstFrameBytes, bytes);
            if (worstFrame) WorstFrameStage = frameStage;
            if (GC.CollectionCount(0) != collections) { GcFrames++; if (elapsed > 2.0) GcOverruns++; }
        }

        // Claude can log this after the flight; values are maxima in milliseconds with call counts in brackets.
        public static string Breakdown
        {
            get
            {
                var text = new StringBuilder();
                for (int i = 0; i < worst.Length; i++) text.AppendFormat("{0} {1:F3} ms [{2}], ", names[i], worst[i], calls[i]);
                text.AppendFormat("worst-frame stage {0}, main allocated {1} B, max/frame {2} B, GC frames {3}, GC overruns {4}",
                    WorstFrameStage, AllocatedBytes, WorstFrameBytes, GcFrames, GcOverruns);
                return text.ToString();
            }
        }
    }
}

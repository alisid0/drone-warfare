using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DroneSandbox
{
    // Paint one world at a time on two workers; the player keeps its preview and an animated progress indicator.
    public sealed class FarWorldPicture : MonoBehaviour
    {
        FarDetailData pending, painting;
        Texture2D pendingTexture, paintingTexture;
        Task<Color32[]> worker;
        CancellationTokenSource cancellation;
        double workerMs;
        bool stale;
        static readonly string[] labels = { "Preparing distant landscape  |", "Preparing distant landscape  /",
            "Preparing distant landscape  -", "Preparing distant landscape  \\" };
        public bool Busy => worker != null || pending != null;

        // A replacement waits for the cancelled worker to drain, preventing overlapping 64 MiB paint buffers.
        public void Begin(FarDetailData plan, Texture2D texture)
        {
            pending = plan; pendingTexture = texture;
            if (worker != null) { stale = true; cancellation.Cancel(); }
            else Launch();
        }

        // Capture only immutable data and a cancellation token; no Unity object is accessed by the paint task.
        void Launch()
        {
            painting = pending; paintingTexture = pendingTexture; pending = null; pendingTexture = null;
            var snapshot = painting;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            stale = false;
            worker = Task.Run(() =>
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var pixels = snapshot.WorldPixels(4096, token);
                workerMs = timer.Elapsed.TotalMilliseconds;
                return pixels;
            }, token);
        }

        // Publish only into the texture that requested the job, using the current sector's collar after travel.
        void Update()
        {
            if (worker == null || !worker.IsCompleted) return;
            try
            {
                if (worker.IsFaulted) { Debug.LogException(worker.Exception); if (!stale) FarTerrain.PictureFailed(); }
                else if (!worker.IsCanceled && !stale) FarTerrain.FinishPicture(paintingTexture, worker.Result, workerMs);
            }
            catch (Exception e) { Debug.LogException(e); FarTerrain.PictureFailed(); }
            finally
            {
                // A native upload failure must release the 64 MiB result instead of retrying it every frame.
                worker = null; painting = null; paintingTexture = null; cancellation.Dispose(); cancellation = null;
            }
            if (pending != null) Launch();
        }

        // Keep the loading feedback independent of the HUD and game-flow files owned by the integrating agent.
        void OnGUI()
        {
            if (!Busy) return;
            GUI.Box(new Rect(16f, Screen.height - 52f, 280f, 32f), labels[(int)(Time.unscaledTime * 8f) & 3]);
        }

        // A destroyed host cancels data work; it never waits for or destroys resources from the worker thread.
        void OnDestroy()
        {
            cancellation?.Cancel();
            if (worker != null)
            {
                var source = cancellation;
                worker.ContinueWith(done => { if (done.IsFaulted) _ = done.Exception; source.Dispose(); }, TaskScheduler.Default);
            }
            else cancellation?.Dispose();
        }
    }
}

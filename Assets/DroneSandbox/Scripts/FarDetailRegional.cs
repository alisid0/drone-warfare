using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // A fixed toroidal array follows the camera; its 9 by 9 pages cover the complete 8 km circle at every cell phase.
    public sealed class FarDetailRegional
    {
        public const int Across = 9, Slots = Across * Across;
        public const float Reach = 8000f;
        public static long GpuBytes => (Slots + 1) * FarDetailRegionData.Bytes;
        public int Committed { get; private set; }
        public double LastWorkerMs { get; private set; }
        readonly FarDetailData plan;
        readonly Vector4[] pages = new Vector4[Slots];
        readonly Texture2DArray array;
        readonly Texture2D upload;
        Task<FarDetailRegionData> worker;
        FarDetailRegionData ready;
        int stage;
        float retryAfter;
        bool disposed;

        // Allocate only at sector build; unsupported devices retain the world picture without exceeding the memory cap.
        public FarDetailRegional(FarDetailData plan)
        {
            this.plan = plan;
            Shader.SetGlobalFloat("_FarRegionEnabled", 0f);
            if (!SystemInfo.supports2DArrayTextures || !SystemInfo.SupportsTextureFormat(TextureFormat.DXT1)
                || (SystemInfo.copyTextureSupport & CopyTextureSupport.DifferentTypes) == 0)
            { Debug.LogWarning("Regional ground needs BC1 arrays and cross-type texture copies; using world picture."); return; }
            int n = FarDetailRegionData.Resolution;
            array = new Texture2DArray(n, n, Slots, TextureFormat.DXT1, FarDetailRegionData.Mips, false)
            { name = "Regional land use (81 BC1 pages)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            array.Apply(false, true);
            upload = new Texture2D(n, n, TextureFormat.DXT1, FarDetailRegionData.Mips, false)
            { name = "Regional ground upload", wrapMode = TextureWrapMode.Clamp };
            Shader.SetGlobalTexture("_FarRegionAlbedo", array);
            Shader.SetGlobalTexture("_FarWorldPicture", FarTerrain.WorldTexture);
            Shader.SetGlobalVectorArray("_FarRegionPages", pages);
            Shader.SetGlobalFloat("_FarRegionEnabled", 1f);
        }

        // Old tasks retain only immutable data; clearing never blocks on a worker or lets it publish into the next world.
        public void Clear()
        {
            disposed = true; worker = null; ready = null;
            Shader.SetGlobalFloat("_FarRegionEnabled", 0f);
            Art.Discard(array); Art.Discard(upload);
        }

        // FarDetail owns the one frame timer: a regional upload never shares a frame with a tile/collider API stage.
        public void Tick(Vector2 eye, bool commit)
        {
            if (disposed || array == null) return;
            Shader.SetGlobalFloat("_FarRegionTime", Time.unscaledTime);
            if (!commit) return;
            if (worker != null && worker.IsCompleted)
            {
                if (worker.IsFaulted) { Debug.LogException(worker.Exception); retryAfter = Time.unscaledTime + 2f; }
                else { ready = worker.Result; LastWorkerMs = ready.milliseconds; stage = 0; }
                worker = null;
            }
            if (ready != null)
            {
                // A page made obsolete by a teleport is discarded before touching the visible array.
                if (!Wanted(ready.x, ready.z, eye)) { ready = null; return; }
                Upload(); return;
            }
            if (worker != null || Time.unscaledTime < retryAfter) return;
            int cx = (int)Math.Floor(eye.x / 2048f), cz = (int)Math.Floor(eye.y / 2048f);
            int bx = 0, bz = 0; float best = float.MaxValue;
            for (int z = cz - 4; z <= cz + 4; z++) for (int x = cx - 4; x <= cx + 4; x++)
            {
                if (!Wanted(x, z, eye)) continue;
                var page = pages[Index(x, z)];
                if (page.w > 0f && page.x == x && page.y == z) continue;
                float dx = (x + .5f) * 2048f - eye.x, dz = (z + .5f) * 2048f - eye.y;
                float distance = dx * dx + dz * dz;
                if (distance < best) { best = distance; bx = x; bz = z; }
            }
            if (best == float.MaxValue) return;
            var snapshot = plan;
            worker = Task.Run(() => FarDetailRegionData.Build(snapshot, bx, bz));
        }

        // Three CPU writes, one tiny staging upload, three GPU copies, then one publication; never Apply the array again.
        void Upload()
        {
            int slot = Index(ready.x, ready.z);
            if (stage < 3) upload.SetPixelData(ready.mips[stage], stage);
            else if (stage == 3) upload.Apply(false, false);
            else if (stage < 7)
            {
                if (stage == 4)
                {
                    pages[slot] = Vector4.zero;
                    Shader.SetGlobalVectorArray("_FarRegionPages", pages);
                }
                Graphics.CopyTexture(upload, 0, stage - 4, array, slot, stage - 4);
            }
            else
            {
                pages[slot] = new Vector4(ready.x, ready.z, Time.unscaledTime, 1f);
                Shader.SetGlobalVectorArray("_FarRegionPages", pages);
                ready = null; Committed++;
            }
            stage++;
        }

        // Signed modulo keeps array addressing identical on both sides of the theatre origin.
        public static int Index(int x, int z) => ((z % Across + Across) % Across) * Across + (x % Across + Across) % Across;

        // Square residency guarantees full radial coverage, while the shader fades the last kilometre to the picture.
        public static bool Wanted(int x, int z, Vector2 eye)
        {
            int cx = (int)Math.Floor(eye.x / 2048f), cz = (int)Math.Floor(eye.y / 2048f);
            return Math.Abs(x - cx) <= 4 && Math.Abs(z - cz) <= 4 && x * 2048f < WorldPlan.Half && (x + 1) * 2048f > -WorldPlan.Half
                && z * 2048f < WorldPlan.Half && (z + 1) * 2048f > -WorldPlan.Half;
        }
    }
}

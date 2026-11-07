using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Stage = DroneSandbox.FarDetailTiming.Stage;

namespace DroneSandbox
{
    // The near bubble (ChatGPT round 6): a fixed toroidal array of 4 by 4 pages of 256 m follows a low camera with 0.5 m
    // colour and 1 m surface control from the tiles' own painter. A page covers the 2 m ground only once all of it has
    // arrived, fades in over 0.6 s, and fades out over 0.6 s before its slot is reused, so nothing pops.
    public sealed class FarDetailNear
    {
        public const int Across = 4, Slots = Across * Across;
        // Above this camera height the 2 m tiles' own detail is as sharp as the screen: no new page starts.
        public const float Ceiling = 800f;
        const float FadeSeconds = .6f, Lead = 160f, Margin = .625f;
        // Lets a GPU experiment hide the bubble; resident pages are kept, only the shader stops reading them.
        public static bool Enabled = true;
        public static long GpuBytes => (Slots + 1) * FarDetailNearData.Bytes;
        public int Committed { get; private set; }
        public double LastWorkerMs { get; private set; }
        // A finished page waits for a texture stage, except while its slot's previous page is still fading out.
        public bool Pending => !disposed && albedo != null
            && (worker != null && worker.IsCompleted || ready != null && (stage != CopyStage || Time.unscaledTime >= copyAfter));
        const int CopyStage = 6;
        readonly FarDetailData plan;
        readonly Vector4[] pages = new Vector4[Slots];
        readonly Texture2DArray albedo, control;
        readonly Texture2D albedoUpload, controlUpload;
        readonly FarDetailNearData scratch = new FarDetailNearData();
        Task<FarDetailNearData> worker;
        FarDetailNearData ready;
        Vector2 centre;
        int stage, windowX, windowZ;
        float copyAfter, retryAfter;
        bool hasWindow, shown, showing, disposed;
        Stage operation;

        // Allocate only at sector build: 16 BC1 colour pages, 16 control pages and one staging texture of each.
        public FarDetailNear(FarDetailData plan)
        {
            this.plan = plan;
            Shader.SetGlobalFloat("_FarNearEnabled", 0f);
            if (!SystemInfo.supports2DArrayTextures || !SystemInfo.SupportsTextureFormat(TextureFormat.DXT1)
                || (SystemInfo.copyTextureSupport & CopyTextureSupport.DifferentTypes) == 0)
            { Debug.LogWarning("The near ground bubble needs BC1 arrays and cross-type texture copies; keeping the 2 m tiles."); return; }
            int n = FarDetailNearData.Resolution, m = FarDetailNearData.ControlResolution;
            // Sampled with the regional pages' sampler (Clamp, trilinear, aniso 4), so the shader needs no new sampler.
            albedo = new Texture2DArray(n, n, Slots, TextureFormat.DXT1, FarDetailNearData.Mips, false)
            { name = "Near ground colour (16 BC1 pages)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            albedo.Apply(false, true);
            control = new Texture2DArray(m, m, Slots, TextureFormat.RGBA32, 1, true)
            { name = "Near ground surface and direction (16 pages)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            control.Apply(false, true);
            albedoUpload = new Texture2D(n, n, TextureFormat.DXT1, FarDetailNearData.Mips, false) { name = "Near ground upload", wrapMode = TextureWrapMode.Clamp };
            albedoUpload.Apply(false, false); // allocate the reusable native staging resources before the flight
            controlUpload = new Texture2D(m, m, TextureFormat.RGBA32, false, true) { name = "Near control upload", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            controlUpload.Apply(false, false);
            Shader.SetGlobalTexture("_FarNearAlbedo", albedo);
            Shader.SetGlobalTexture("_FarNearControl", control);
            Shader.SetGlobalVectorArray("_FarNearPages", pages);
        }

        // Old tasks write only this instance's scratch; clearing never blocks on a worker or publishes into the next world.
        public void Clear()
        {
            disposed = true; worker = null; ready = null;
            Shader.SetGlobalFloat("_FarNearEnabled", 0f);
            Art.Discard(albedo); Art.Discard(control); Art.Discard(albedoUpload); Art.Discard(controlUpload);
        }

        // FarDetail owns the frame timer: `commit` grants this frame's one texture stage, and `launch` lets an idle
        // queue start its next page (only while the camera's own 2 m tiles are all live).
        public void Tick(Vector2 eye, Vector2 focus, float height, bool commit, bool launch)
        {
            long stamp = FarDetailTiming.Begin(); operation = Stage.NearPlan;
            TickCore(eye, focus, height, commit, launch);
            FarDetailTiming.End(operation, stamp);
        }

        // Keep queue selection separate from the measured texture stage, as the regional queue does.
        void TickCore(Vector2 eye, Vector2 focus, float height, bool commit, bool launch)
        {
            if (disposed || albedo == null) return;
            Shader.SetGlobalFloat("_FarRegionTime", Time.unscaledTime); // the shared publication clock
            Show(shown && Enabled);
            Follow(eye, focus);
            if (!commit) { if (launch && ready == null && worker == null && Time.unscaledTime >= retryAfter) Next(height); return; }
            if (worker != null && worker.IsCompleted)
            {
                if (worker.IsFaulted) { Debug.LogException(worker.Exception); retryAfter = Time.unscaledTime + 2f; }
                else { ready = worker.Result; LastWorkerMs = ready.milliseconds; stage = 0; copyAfter = 0f; }
                worker = null;
            }
            if (ready != null)
            {
                // A page the camera has left behind is dropped before it touches the visible arrays.
                if (!Wanted(ready.x, ready.z)) { ready = null; return; }
                Upload(); return;
            }
            if (worker != null || !launch || Time.unscaledTime < retryAfter) return;
            Next(height);
        }

        // The window's columns and rows move a page at a time, with a margin so a camera on a boundary never thrashes:
        // its centre stays at least 352 m inside the window.
        void Follow(Vector2 eye, Vector2 focus)
        {
            centre = eye + Vector2.ClampMagnitude(focus - eye, Lead);
            float u = centre.x / FarDetailNearData.Metres - Across * .5f, v = centre.y / FarDetailNearData.Metres - Across * .5f;
            if (!hasWindow) { windowX = Mathf.RoundToInt(u); windowZ = Mathf.RoundToInt(v); hasWindow = true; }
            while (u - windowX > Margin) windowX++;
            while (windowX - u > Margin) windowX--;
            while (v - windowZ > Margin) windowZ++;
            while (windowZ - v > Margin) windowZ--;
        }

        // Launch the wanted page nearest the camera's view that does not yet show, while the camera is low enough.
        void Next(float height)
        {
            if (!Enabled || height > Ceiling || !hasWindow) return;
            int bx = 0, bz = 0; float best = float.MaxValue;
            for (int z = windowZ; z < windowZ + Across; z++) for (int x = windowX; x < windowX + Across; x++)
            {
                if (!Eligible(x, z)) continue;
                var page = pages[Index(x, z)];
                if (page.w > 0f && page.x == x && page.y == z) continue;
                float dx = (x + .5f) * FarDetailNearData.Metres - centre.x, dz = (z + .5f) * FarDetailNearData.Metres - centre.y;
                float distance = dx * dx + dz * dz;
                if (distance < best) { best = distance; bx = x; bz = z; }
            }
            if (best == float.MaxValue) return;
            Launch(bx, bz);
        }

        // Isolate the worker closure from TickCore so idle streaming frames do not allocate a display class.
        void Launch(int x, int z)
        {
            var snapshot = plan; var page = scratch;
            worker = Task.Run(() => page.Build(snapshot, x, z));
        }

        // Fill the staging textures, retire the slot's old page, then copy whole mips and publish, one stage per call.
        // Never Apply an array.
        void Upload()
        {
            int slot = Index(ready.x, ready.z);
            switch (stage)
            {
                case 0: case 1: case 2: operation = Stage.NearPixels; albedoUpload.SetPixelData(ready.mips[stage], stage); break;
                case 3: operation = Stage.NearApply; albedoUpload.Apply(false, false); break;
                case 4: operation = Stage.NearPixels; controlUpload.SetPixelData(ready.controls, 0); break;
                case 5:
                    operation = Stage.NearApply; controlUpload.Apply(false, false);
                    copyAfter = Retire(slot); break;
                case CopyStage:
                    if (Time.unscaledTime < copyAfter) return;
                    operation = Stage.NearPublish; pages[slot] = Vector4.zero;
                    Shader.SetGlobalVectorArray("_FarNearPages", pages); break;
                case 7: case 8: case 9:
                    operation = Stage.NearCopy; Graphics.CopyTexture(albedoUpload, 0, stage - 7, albedo, slot, stage - 7); break;
                case 10: operation = Stage.NearCopy; Graphics.CopyTexture(controlUpload, 0, 0, control, slot, 0); break;
                default:
                    operation = Stage.NearPublish;
                    pages[slot] = new Vector4(ready.x, ready.z, Time.unscaledTime, 1f);
                    Shader.SetGlobalVectorArray("_FarNearPages", pages);
                    ready = null; Committed++; shown = true; Show(Enabled);
                    return;
            }
            stage++;
        }

        // Start fading out whatever the slot still shows, from its present weight; the copy waits until it has gone.
        // The shader mirrors a fading-in page (w 1) as a fading-out one (w -1) from the stamped time.
        float Retire(int slot)
        {
            var page = pages[slot];
            float now = Time.unscaledTime;
            if (page.w == 0f) return now;
            if (page.w < 0f) return page.z + FadeSeconds;
            float start = Mathf.Min(now, 2f * now - page.z - FadeSeconds);
            pages[slot] = new Vector4(page.x, page.y, start, -1f);
            Shader.SetGlobalVectorArray("_FarNearPages", pages);
            return start + FadeSeconds;
        }

        // The shader reads the bubble only while it has a page to show and is enabled.
        void Show(bool on)
        {
            if (on == showing) return;
            showing = on;
            Shader.SetGlobalFloat("_FarNearEnabled", on ? 1f : 0f);
        }

        // A page lies inside exactly one 512 m tile, so it shares that tile's exclusion from the sector and its ring.
        static bool Eligible(int x, int z) => FarDetail.Eligible(x >> 1, z >> 1);

        // Within the current window and outside the sector's ownership.
        bool Wanted(int x, int z) => hasWindow && x >= windowX && x < windowX + Across && z >= windowZ && z < windowZ + Across && Eligible(x, z);

        // Signed modulo keeps array addressing identical on both sides of the theatre origin.
        public static int Index(int x, int z) => ((z % Across + Across) % Across) * Across + (x % Across + Across) % Across;
    }
}

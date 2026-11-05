using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // Speed test. Start the game with  -benchmark <folder>  and it holds the camera still on a set of views and
    // times each one: CPU main and render thread and GPU, the median and the slowest 5% of frames (from
    // FrameTimingManager, which needs frame timing stats on in Player Settings), and what got drawn. Then, at the
    // widest view and the usual play view, it switches HDRP features and kinds of scenery off one at a time to
    // see what each costs. Writes benchmark.log and a picture of each view into the folder, then quits.
    public class Benchmark : MonoBehaviour
    {
        string outDir;
        StreamWriter log;
        readonly FrameTiming[] timing = new FrameTiming[1];
        readonly List<(string name, ProfilerRecorder recorder)> counters = new List<(string, ProfilerRecorder)>();

        struct Times
        {
            public float frame, main, render, gpu, gpuSlow, frameSlow;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-benchmark");
            if (i >= 0 && i + 1 < args.Length) new GameObject("Benchmark").AddComponent<Benchmark>().Begin(args[i + 1]);
        }

        void Begin(string folder)
        {
            SandboxGame.SaveProgress = false;
            Progress.UseTestStore();
            outDir = folder;
            Directory.CreateDirectory(outDir);
            log = new StreamWriter(Path.Combine(outDir, "benchmark.log")) { AutoFlush = true };
            foreach (var name in new[] { "Batches Count", "SetPass Calls Count", "Draw Calls Count", "Triangles Count", "Shadow Casters Count" })
                counters.Add((name, ProfilerRecorder.StartNew(ProfilerCategory.Render, name)));
            foreach (var name in new[] { "Gfx Used Memory", "System Used Memory" })
                counters.Add((name, ProfilerRecorder.StartNew(ProfilerCategory.Memory, name)));
        }

        void OnDestroy()
        {
            foreach (var c in counters) c.recorder.Dispose();
            log?.Dispose();
        }

        IEnumerator Start()
        {
            yield return new WaitForSeconds(1.5f);
            var game = SandboxGame.Instance;
            var rts = game.GetComponent<RtsController>();
            game.StartSandbox();
            DayNight.Instance.SetTime(10f, 0f); // the same light for every view
            yield return new WaitForSeconds(1f);
            log.WriteLine($"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsMemorySize} MB), " +
                          $"{SystemInfo.processorType}, screen {Screen.width}x{Screen.height}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            log.WriteLine($"Upscaling {(Settings.Upscaling ? "on" : "off")}, DLSS {(HDDynamicResolutionPlatformCapabilities.DLSSDetected ? "available" : "not available")}, " +
                          $"drawn at {(DynamicResolutionHandler.instance != null ? DynamicResolutionHandler.instance.GetCurrentScale() * 100f : 100f):F0}% of the screen");
            LogScene();

            var village = MapLayout.Villages[0];
            var bridge = MapLayout.BridgePoint;
            var views = new (string name, Vector3 at, float distance, float yaw)[]
            {
                ("overview", new Vector3(10f, 0f, 10f), 110f, 45f),
                ("play_base", game.basePosition, 55f, 30f),
                ("play_woods", new Vector3(-32f, 0f, -26f), 55f, 45f),
                ("farmland", new Vector3(-20f, 0f, -30f), 100f, 40f),
                ("village", new Vector3(village.x, 0f, village.y), 45f, 200f),
                ("bridge", new Vector3(bridge.x, 0f, bridge.y), 40f, 135f),
                ("enemy_hq", game.enemyHqPosition, 38f, 210f),
                ("closeup", game.basePosition, 18f, 30f),
            };
            log.WriteLine();
            log.WriteLine("view               frame ms (5% slowest)   main   render    gpu (5% slowest)  | batches setpass  tris   shadow casters");
            foreach (var view in views)
            {
                rts.LookAt(view.at, view.distance, view.yaw);
                yield return new WaitForSecondsRealtime(1.5f);
                Times t = default;
                yield return Measure(90, r => t = r);
                Line(view.name, t);
                yield return Shot(view.name);
            }

            // Explosions and smoke in the play view, the photo mode view of the valley (bounce light on), night.
            rts.LookAt(game.basePosition, 55f, 30f);
            yield return new WaitForSecondsRealtime(0.5f);
            Vector3 blastAt = Ground.OnGround(game.basePosition + new Vector3(20f, 0f, 25f));
            for (int i = 0; i < 4; i++) Effects.Explosion(blastAt + new Vector3(i * 6f, 1f, (i % 2) * 5f), 5f);
            yield return new WaitForSecondsRealtime(0.4f);
            {
                Times t = default;
                yield return Measure(60, r => t = r);
                Line("4 explosions", t);
                yield return Shot("explosions");
            }
            yield return new WaitForSecondsRealtime(4f);
            var photo = PhotoMode.Instance;
            photo.PlaceLooking(new Vector3(75f, 70f, -150f), new Vector3(-35f, 0f, 0f), 60f);
            yield return new WaitForSecondsRealtime(1.5f);
            {
                Times t = default;
                yield return Measure(90, r => t = r);
                Line("photo_valley", t);
                yield return Shot("photo_valley");
            }
            photo.Exit();
            DayNight.Instance.SetTime(23.5f, 0f);
            rts.LookAt(game.basePosition, 55f, 30f);
            yield return new WaitForSecondsRealtime(1.5f);
            {
                Times t = default;
                yield return Measure(90, r => t = r);
                Line("night_base", t);
                yield return Shot("night_base");
            }
            DayNight.Instance.SetTime(10f, 0f);

            // What each thing costs: switched off one at a time, at the widest view and the usual play view.
            foreach (var view in new[] { views[0], views[1] })
            {
                rts.LookAt(view.at, view.distance, view.yaw);
                yield return new WaitForSecondsRealtime(1.5f);
                Times baseline = default;
                yield return Measure(90, r => baseline = r);
                log.WriteLine();
                log.WriteLine($"Switched off at {view.name} (baseline gpu {baseline.gpu:F2} ms, main {baseline.main:F2} ms, render {baseline.render:F2} ms):");
                foreach (var toggle in Toggles())
                {
                    var undo = toggle.off();
                    if (undo == null) continue;
                    yield return new WaitForSecondsRealtime(0.5f);
                    Times t = default;
                    yield return Measure(60, r => t = r);
                    undo();
                    log.WriteLine($"  {toggle.name,-26} gpu {t.gpu - baseline.gpu,6:+0.00;-0.00} ms   main {t.main - baseline.main,6:+0.00;-0.00}   " +
                                  $"render {t.render - baseline.render,6:+0.00;-0.00}   batches {Counter("Batches Count")}, tris {Counter("Triangles Count") / 1000}k");
                    yield return new WaitForSecondsRealtime(0.3f);
                }
            }

            log.WriteLine();
            log.WriteLine($"Done at {Time.realtimeSinceStartup:F1}s.");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        // Things to switch off: each returns how to switch it back on, or null when there's nothing to switch.
        IEnumerable<(string name, Func<Action> off)> Toggles()
        {
            var cam = Camera.main;
            var data = cam.GetComponent<HDAdditionalCameraData>();
            foreach (var field in new[]
                     {
                         FrameSettingsField.VolumetricClouds, FrameSettingsField.Volumetrics, FrameSettingsField.AtmosphericScattering,
                         FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.ContactShadows, FrameSettingsField.ShadowMaps,
                         FrameSettingsField.Water, FrameSettingsField.Decals, FrameSettingsField.TransparentObjects,
                         FrameSettingsField.SubsurfaceScattering, FrameSettingsField.Postprocess, FrameSettingsField.OpaqueObjects,
                     })
                yield return (field.ToString(), () => Frame(data, field, false));
            yield return ("upscaling (DLSS) off", () =>
            {
                if (!data.allowDynamicResolution) return null;
                data.allowDynamicResolution = false;
                return () => data.allowDynamicResolution = true;
            });
            yield return ("TAA -> SMAA", () =>
            {
                var was = data.antialiasing;
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                return () => data.antialiasing = was;
            });
            var terrain = Ground.Current;
            if (terrain != null)
            {
                yield return ("grass (terrain detail)", () =>
                {
                    float was = terrain.detailObjectDistance;
                    terrain.detailObjectDistance = 0f;
                    return () => terrain.detailObjectDistance = was;
                });
                yield return ("terrain itself", () =>
                {
                    terrain.drawHeightmap = false;
                    return () => terrain.drawHeightmap = true;
                });
            }
            foreach (var root in new[] { "Woods", "Lake Woods", "Undergrowth", "Rocks", "Features", "Farmland", "Backdrop", "Water", "Reservoir", "Village", "Units" })
                yield return (root, () => Hide(root));
        }

        static Action Frame(HDAdditionalCameraData data, FrameSettingsField field, bool on)
        {
            bool masked = data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field];
            bool was = data.renderingPathCustomFrameSettings.IsEnabled(field);
            data.customRenderingSettings = true;
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            data.renderingPathCustomFrameSettings.SetEnabled(field, on);
            return () =>
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, was);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = masked;
            };
        }

        // Hides every renderer under the objects with this name (or starting with it, for the villages); "Units"
        // is every unit in play.
        static Action Hide(string name)
        {
            var hidden = new List<Renderer>();
            IEnumerable<Transform> roots = name == "Units"
                ? FindObjectsByType<Unit>().Select(u => u.transform)
                : FindObjectsByType<Transform>().Where(t => t.name == name || (name == "Village" && t.name.StartsWith("Village ")));
            foreach (var root in roots)
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                    if (r.enabled)
                    {
                        r.enabled = false;
                        hidden.Add(r);
                    }
            if (hidden.Count == 0) return null;
            return () => { foreach (var r in hidden) if (r != null) r.enabled = true; };
        }

        IEnumerator Measure(int frames, Action<Times> done)
        {
            var frame = new List<float>(frames);
            var main = new List<float>(frames);
            var render = new List<float>(frames);
            var gpu = new List<float>(frames);
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                frame.Add(Time.unscaledDeltaTime * 1000f);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
                {
                    main.Add((float)timing[0].cpuMainThreadFrameTime);
                    render.Add((float)timing[0].cpuRenderThreadFrameTime);
                    gpu.Add((float)timing[0].gpuFrameTime);
                }
            }
            done(new Times
            {
                frame = Median(frame), frameSlow = Slowest(frame), main = Median(main), render = Median(render),
                gpu = Median(gpu), gpuSlow = Slowest(gpu),
            });
        }

        static float Median(List<float> values)
        {
            if (values.Count == 0) return 0f;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        static float Slowest(List<float> values)
        {
            if (values.Count == 0) return 0f;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f))];
        }

        long Counter(string name) => counters.FirstOrDefault(c => c.name == name).recorder.LastValue;

        void Line(string name, Times t)
        {
            log.WriteLine($"{name,-18} {t.frame,6:F2} ({t.frameSlow,6:F2}) = {1000f / Mathf.Max(t.frame, 0.01f),4:F0} fps  {t.main,6:F2} {t.render,6:F2}   " +
                          $"{t.gpu,6:F2} ({t.gpuSlow,6:F2})  | {Counter("Batches Count"),6} {Counter("SetPass Calls Count"),6} " +
                          $"{Counter("Triangles Count") / 1000,6}k {Counter("Shadow Casters Count"),6}   " +
                          $"gfx mem {Counter("Gfx Used Memory") / (1024 * 1024)} MB");
        }

        // What the world is made of: renderers, triangles (the most detailed LOD) and LOD groups under each part.
        void LogScene()
        {
            var world = GameObject.Find("World");
            if (world == null) return;
            log.WriteLine("Scene parts (renderers, triangles at full detail, LOD groups):");
            var parts = new List<Transform>();
            foreach (Transform part in world.transform)
                if (part.name == "Scenery") foreach (Transform s in part) parts.Add(s);
                else parts.Add(part);
            var rows = new Dictionary<string, (int renderers, long tris, int lods)>();
            foreach (var part in parts)
            {
                string key = part.name.StartsWith("Village ") ? "Villages" : part.name;
                rows.TryGetValue(key, out var row);
                foreach (var r in part.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var lod = r.GetComponentInParent<LODGroup>();
                    if (lod != null && !lod.GetLODs()[0].renderers.Contains(r)) continue; // count LOD0 only
                    var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                    row.renderers++;
                    if (mesh != null) for (int s = 0; s < mesh.subMeshCount; s++) row.tris += mesh.GetIndexCount(s) / 3;
                }
                row.lods += part.GetComponentsInChildren<LODGroup>(true).Length;
                rows[key] = row;
            }
            foreach (var row in rows.OrderByDescending(r => r.Value.tris))
                log.WriteLine($"  {row.Key,-22} {row.Value.renderers,6} renderers {row.Value.tris / 1000,7}k tris {row.Value.lods,6} LOD groups");
            var terrain = Ground.Current;
            if (terrain != null)
                log.WriteLine($"  terrain: {terrain.terrainData.heightmapResolution} heightmap, pixel error {terrain.heightmapPixelError}, " +
                              $"{terrain.terrainData.detailPrototypes.Length} detail kinds out to {terrain.detailObjectDistance} m, " +
                              $"{terrain.terrainData.treeInstanceCount} terrain trees");
            log.WriteLine($"  lights: {FindObjectsByType<Light>().Count(l => l.enabled)} on, " +
                          $"{FindObjectsByType<Light>().Count(l => l.enabled && l.shadows != LightShadows.None)} casting shadows; " +
                          $"{FindObjectsByType<ParticleSystem>().Length} particle systems; {FindObjectsByType<Unit>().Length} units");
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(outDir, name + ".jpg"), texture.EncodeToJPG(90));
            Destroy(texture);
        }
    }
}

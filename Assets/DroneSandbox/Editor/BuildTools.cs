using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DroneSandbox.EditorTools
{
    // Editor menu and command-line entry points for building the game.
    // Command line:  Unity.exe -batchmode -quit -projectPath . -executeMethod DroneSandbox.EditorTools.BuildTools.BuildWindows
    public static class BuildTools
    {
        const string MainScene = "Assets/Scenes/Main.unity";
        const string WindowsBuild = "Builds/Windows/DroneWarfare.exe";

        [MenuItem("Drone Sandbox/Build for Windows")]
        public static void BuildWindows() => Build(WindowsBuild, BuildOptions.None);

        // A development build beside the release one, for profiler captures of hitches (the autotest records them to files
        // with Profiler.enableBinaryLog and ProfilerDump reads them back). Command line: -executeMethod
        // DroneSandbox.EditorTools.BuildTools.BuildWindowsDevelopment
        public static void BuildWindowsDevelopment() => Build("Builds/WindowsDev/DroneWarfare.exe", BuildOptions.Development);

        // Builds the enabled scenes for 64-bit Windows to the path, with the given options.
        static void Build(string path, BuildOptions buildOptions)
        {
            EnsureMainScene();
            // CPU render-thread and GPU frame times for the autotest's hitch report (FrameTimingManager); near-free.
            PlayerSettings.enableFrameTimingStats = true;
            // Direct3D 11 first, 12 as the fallback. On the RTX 3050 laptop D3D12 piled up 6.4 GB of upload scratch
            // buffers in long sessions until the device failed (hd173) or the machine thrashed (hd174), and compiled
            // pipeline states mid-flight; D3D11 held shared GPU memory under 0.32 GB over the same run at the same
            // frame rate (hd175).
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11, UnityEngine.Rendering.GraphicsDeviceType.Direct3D12 });
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = buildOptions,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"Build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB, {path}");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        // Checks the editor's map preview from the command line: builds the map in edit mode the way MapPreview
        // does, saves a picture of it to Builds/preview_check.png and logs what it built, then throws it away.
        // Command line:  Unity.exe -batchmode -quit -projectPath . -executeMethod DroneSandbox.EditorTools.BuildTools.CheckMapPreview
        public static void CheckMapPreview()
        {
            EnsureMainScene();
            EditorSceneManager.OpenScene(EditorBuildSettings.scenes.First(s => s.enabled).path, OpenSceneMode.Single);
            // One render only, so compile shaders now instead of drawing placeholders while they compile.
            ShaderUtil.allowAsyncCompilation = false;
            var root = new GameObject("Preview Check") { hideFlags = HideFlags.DontSave };
            World.Build(root.transform, preview: true);
            int renderers = root.GetComponentsInChildren<Renderer>().Length;

            var cameraObject = new GameObject("Check Camera") { hideFlags = HideFlags.DontSave };
            var cam = cameraObject.AddComponent<Camera>();
            cam.transform.position = new Vector3(-95f, 95f, -150f);
            cam.transform.LookAt(new Vector3(-20f, 0f, -20f));
            cam.farClipPlane = 900f;
            cam.allowHDR = true;
            Atmosphere.SetUpCamera(cam);
            var target = new RenderTexture(1600, 900, 24);
            cam.targetTexture = target;
            cam.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory("Builds");
            File.WriteAllBytes("Builds/preview_check.png", image.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);

            Debug.Log($"Map preview check: {renderers} renderers, {MapLayout.Fields.Count} fields, picture in Builds/preview_check.png");
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(root);
        }

        // Opens the main scene in the editor (not batch mode), waits for MapPreview to build the map, saves what
        // the Scene view shows to Builds/sceneview_check.png and quits. A check that the preview works for real.
        // Command line (no -batchmode):  Unity.exe -projectPath . -executeMethod DroneSandbox.EditorTools.BuildTools.CaptureSceneView
        public static void CaptureSceneView()
        {
            EnsureMainScene();
            EditorSceneManager.OpenScene(EditorBuildSettings.scenes.First(s => s.enabled).path, OpenSceneMode.Single);
            double start = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction wait = null;
            wait = () =>
            {
                var preview = Object.FindAnyObjectByType<MapPreview>();
                bool built = preview != null && preview.transform.childCount > 0;
                if (!built && EditorApplication.timeSinceStartup - start < 120.0) return;
                EditorApplication.update -= wait;
                var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
                view.LookAt(new Vector3(-20f, 0f, -20f), Quaternion.Euler(35f, 25f, 0f), 140f, false, true);
                view.Repaint();
                EditorApplication.delayCall += () =>
                {
                    var cam = view.camera;
                    var target = new RenderTexture(1600, 900, 24);
                    var old = cam.targetTexture;
                    cam.targetTexture = target;
                    cam.Render();
                    RenderTexture.active = target;
                    var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                    image.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = old;
                    Directory.CreateDirectory("Builds");
                    File.WriteAllBytes("Builds/sceneview_check.png", image.EncodeToPNG());
                    Debug.Log($"Scene view check: preview built = {built}, picture in Builds/sceneview_check.png");
                    EditorApplication.Exit(0);
                };
            };
            EditorApplication.update += wait;
        }

        // The game builds itself from code, so the scene only needs a camera and a light.
        // Uses the template's scene when there is one; otherwise makes Assets/Scenes/Main.unity.
        [MenuItem("Drone Sandbox/Set Up Main Scene")]
        public static void EnsureMainScene()
        {
            if (EditorBuildSettings.scenes.Any(s => s.enabled && File.Exists(s.path))) return;

            string scenePath = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => !p.StartsWith("Assets/DroneSandbox"));
            if (scenePath == null)
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, MainScene);
                scenePath = MainScene;
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            Debug.Log("Build scene: " + scenePath);
        }
    }
}

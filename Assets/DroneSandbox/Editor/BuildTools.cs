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
        public static void BuildWindows()
        {
            EnsureMainScene();
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = WindowsBuild,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"Build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB, {WindowsBuild}");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
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

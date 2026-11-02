using System.IO;
using UnityEditor;
using UnityEngine;

namespace DroneSandbox.EditorTools
{
    // One-time project configuration: player settings, URP shadow quality, and the material assets the
    // game loads from Resources (terrain material, HDRI sky). Safe to run again.
    // Command line:  Unity.exe -batchmode -quit -projectPath . -executeMethod DroneSandbox.EditorTools.ProjectSetup.Configure
    public static class ProjectSetup
    {
        const string Root = "Assets/DroneSandbox/Resources";

        [MenuItem("Drone Sandbox/Configure Project")]
        public static void Configure()
        {
            BuildTools.EnsureMainScene();

            PlayerSettings.companyName = "alisid0";
            PlayerSettings.productName = "Drone Warfare";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            TuneRenderPipelines();
            CreateBaseMaterials();
            CreateTerrainMaterial();
            ImportTerrainTextures();
            CreateSkyMaterial();

            AssetDatabase.SaveAssets();
            Debug.Log("Drone Sandbox: project configured.");
        }

        // Long shadow distance for the high RTS camera, soft shadows, four cascades.
        static void TuneRenderPipelines()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue; // package assets are read-only
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                var so = new SerializedObject(asset);
                Set(so, "m_ShadowDistance", 260f);
                SetInt(so, "m_ShadowCascadeCount", 4);
                SetBool(so, "m_SoftShadowsSupported", true);
                SetInt(so, "m_MainLightShadowmapResolution", 4096);
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("Tuned " + path);
            }
        }

        // Templates for the game's runtime-coloured materials (see Art.NewMaterial): a plain URP Lit one and an
        // emissive one, so player builds keep the Lit shader and its emission variant.
        static void CreateBaseMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            EnsureMaterialsFolder();
            foreach (bool glow in new[] { false, true })
            {
                string path = Root + (glow ? "/Materials/BaseLitGlow.mat" : "/Materials/BaseLit.mat");
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) continue;
                var material = new Material(shader);
                material.SetFloat("_Smoothness", 0.15f);
                if (glow)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", Color.white);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                AssetDatabase.CreateAsset(material, path);
            }
        }

        static void EnsureMaterialsFolder()
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Materials")) AssetDatabase.CreateFolder(Root, "Materials");
        }

        static void CreateTerrainMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader == null) return;
            EnsureMaterialsFolder();
            string path = Root + "/Materials/TerrainLit.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
                AssetDatabase.CreateAsset(new Material(shader), path);
        }

        // Marks downloaded *_nor textures as normal maps.
        static void ImportTerrainTextures()
        {
            string dir = Root + "/Textures/Terrain";
            if (!Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir))
            {
                if (file.EndsWith(".meta")) continue;
                string path = file.Replace('\\', '/');
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                bool isNormal = Path.GetFileNameWithoutExtension(path).EndsWith("_nor");
                var wanted = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType == wanted && importer.maxTextureSize == 2048) continue; // already set up
                importer.textureType = wanted;
                importer.maxTextureSize = 2048;
                importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }
        }

        // Turns Resources/Sky/sky.hdr (an equirectangular HDRI) into a panoramic skybox material.
        static void CreateSkyMaterial()
        {
            string dir = Root + "/Sky";
            string hdr = dir + "/sky.hdr";
            if (!File.Exists(hdr)) return;
            if (AssetImporter.GetAtPath(hdr) is TextureImporter importer)
            {
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.maxTextureSize = 4096;
                importer.mipmapEnabled = false;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var shader = Shader.Find("Skybox/Panoramic");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(hdr);
            if (shader == null || texture == null) return;
            string path = dir + "/SkyMaterial.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_Mapping", 1f);   // latitude-longitude layout
            material.SetFloat("_ImageType", 0f); // 360 degrees
            material.SetFloat("_Exposure", 1.0f);
            EditorUtility.SetDirty(material);
        }

        static void Set(SerializedObject so, string name, float value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.floatValue = value;
        }

        static void SetInt(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }

        static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
        }
    }
}

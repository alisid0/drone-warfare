using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox.EditorTools
{
    // The High Definition Render Pipeline: its asset, with water, volumetric fog and clouds, screen-space
    // reflections, ambient occlusion and global illumination supported, made the pipeline for every quality level, plus the global
    // settings HDRP keeps. Also small helpers for setting up HDRP Lit materials.
    public static class HdrpSetup
    {
        const string AssetPath = "Assets/Settings/HDRP_Asset.asset";

        public static void UsePipeline()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            var settings = asset.currentPlatformRenderPipelineSettings;
            settings.supportWater = true;
            settings.supportVolumetrics = true;
            settings.supportVolumetricClouds = true;
            settings.supportSSR = true;
            settings.supportSSAO = true;
            settings.supportSSGI = true; // bounce light, in photo mode (PhotoMode.ConfigureFocus)
            asset.currentPlatformRenderPipelineSettings = settings;
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            // Every quality level uses it too (the URP template had its own asset per level).
            var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = quality.FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                var pipeline = level.FindPropertyRelative("customRenderPipeline");
                if (pipeline != null) pipeline.objectReferenceValue = asset;
            }
            quality.ApplyModifiedPropertiesWithoutUndo();

            // HDRP's global settings asset (default frame settings, resources). The editor makes it when HDRP
            // first runs, but a batch-mode build may get there first, so make sure it exists now.
            // (The class is internal to HDRP, so by name.)
            var globalSettings = typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings");
            var ensure = globalSettings?.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic);
            ensure?.Invoke(null, new object[] { true });
            AssetDatabase.SaveAssets();
        }

        // ---- HDRP Lit materials ----

        public static Shader Lit => Shader.Find("HDRP/Lit");

        public static Material LoadOrCreate(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            return material;
        }

        public static void Albedo(Material m, Texture texture, Color tint, Vector2 scale)
        {
            m.SetTexture("_BaseColorMap", texture);
            m.SetColor("_BaseColor", tint);
            m.SetTextureScale("_BaseColorMap", scale);
        }

        public static void Normal(Material m, Texture texture, float strength = 1f)
        {
            m.SetTexture("_NormalMap", texture);
            m.SetFloat("_NormalScale", strength);
        }

        public static void Surface(Material m, float smoothness, float metallic)
        {
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
        }

        // Cut out by the texture's alpha. Both sides drawn for cards; keepNormals leaves the normals as authored on
        // the back (grass lit like the ground), otherwise they are mirrored.
        public static void Cutout(Material m, float cutoff, bool doubleSided, bool keepNormals = false)
        {
            m.SetFloat("_AlphaCutoffEnable", 1f);
            m.SetFloat("_AlphaCutoff", cutoff);
            m.SetFloat("_DoubleSidedEnable", doubleSided ? 1f : 0f);
            m.SetFloat("_DoubleSidedNormalMode", keepNormals ? 2f : 1f); // 0 flip, 1 mirror, 2 none
        }

        // Alpha-blended, fogged, drawn from both sides.
        public static void Transparent(Material m)
        {
            m.SetFloat("_SurfaceType", 1f);
            m.SetFloat("_BlendMode", 0f); // alpha
            m.SetFloat("_EnableFogOnTransparent", 1f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_ZWrite", 0f);
        }

        // Sets the keywords and passes HDRP needs for the properties above, and saves.
        public static void Finish(Material m, bool instancing = true)
        {
            HDMaterial.ValidateMaterial(m);
            m.enableInstancing = instancing;
            EditorUtility.SetDirty(m);
        }
    }
}

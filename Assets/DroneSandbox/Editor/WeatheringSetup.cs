using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox.EditorTools
{
    // The decals that weather the dam (placed by DamWeathering): generated textures, colour with the coverage in
    // alpha, and HDRP decal materials that darken and tint the concrete and change how glossy it is (moss stays
    // matt, damp and algae shine). Saved in Resources so player builds keep the decal shader.
    public static class WeatheringSetup
    {
        const string Root = "Assets/DroneSandbox/Resources";
        const int Size = 512;

        public static void CreateDamDecals()
        {
            var shader = Shader.Find("HDRP/Decal");
            if (shader == null) return;
            foreach (string folder in new[] { Root + "/Textures/Decals", Root + "/Materials/Decals" })
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            Make(shader, "DecalMoss", Moss, 0.25f);
            Make(shader, "DecalDamp", Damp, 0.62f);
            Make(shader, "DecalAlgae", Algae, 0.7f);
            Make(shader, "DecalWaterline", Waterline, 0.3f);

            // Water doesn't take decals: the spillway's sheet would turn green over the algae.
            foreach (string name in new[] { "WhiteWater", "SpillWater", "SpillSheet" })
            {
                var water = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/PBR/" + name + ".mat");
                if (water == null) continue;
                water.SetFloat("_SupportDecals", 0f);
                HDMaterial.ValidateMaterial(water);
                EditorUtility.SetDirty(water);
            }
        }

        static void Make(Shader shader, string name, System.Func<float, float, Color> paint, float smoothness)
        {
            string texturePath = Root + "/Textures/Decals/" + name + ".png";
            if (!File.Exists(texturePath))
            {
                var pixels = new Color[Size * Size];
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        pixels[y * Size + x] = paint((x + 0.5f) / Size, (y + 0.5f) / Size);
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                texture.SetPixels(pixels);
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath);
            }
            if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }

            string materialPath = Root + "/Materials/Decals/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = shader;
            material.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_DecalBlend", 1f);
            material.SetFloat("_MaskBlendSrc", 1f); // gloss follows the colour's alpha
            material.SetFloat("_AffectAlbedo", 1f);
            material.SetFloat("_AffectNormal", 0f);
            material.SetFloat("_AffectMetal", 0f);
            material.SetFloat("_AffectAO", 0f);
            material.SetFloat("_AffectSmoothness", 1f);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
        }

        // ---- the pictures: u across, v up (or down the flow, for the algae) ----

        static float Noise(float u, float v, float scale, float seed) => Mathf.PerlinNoise(u * scale + seed, v * scale + seed * 0.61f);

        // Fades out towards the sides along a wandering line, so the decals' boxes don't show.
        static float SideFade(float u, float v)
        {
            float edge = Mathf.Min(u, 1f - u) + (Noise(u, v, 9f, 3f) - 0.5f) * 0.08f;
            return Mathf.SmoothStep(0f, 1f, edge / 0.14f);
        }

        // Moss and dark grime in clumps where the dam meets the ground, thinning out upwards.
        static Color Moss(float u, float v)
        {
            float broad = Noise(u, v, 5f, 11f), clumps = Noise(u, v, 22f, 37f), fine = Noise(u, v, 90f, 5f);
            float reach = 1f - Mathf.SmoothStep(0.1f, 0.85f, v + (broad - 0.5f) * 0.45f);
            float cover = reach * Mathf.SmoothStep(0.3f, 0.62f, clumps * 0.65f + broad * 0.35f + (1f - v) * 0.25f);
            var colour = Color.Lerp(new Color(0.2f, 0.25f, 0.1f), new Color(0.42f, 0.45f, 0.2f), fine * 0.8f + clumps * 0.2f);
            colour = Color.Lerp(colour, new Color(0.27f, 0.23f, 0.15f), Mathf.SmoothStep(0.5f, 0.2f, broad) * 0.6f); // bare grime
            colour.a = Mathf.Clamp01(cover * 1.4f) * SideFade(u, v) * (0.75f + 0.25f * fine);
            return colour;
        }

        // Concrete wet from the spray: darkest and greenest at the bottom, running up in vertical streaks.
        static Color Damp(float u, float v)
        {
            float streaks = Mathf.PerlinNoise(u * 46f + 2f, v * 1.6f + 9f), broad = Noise(u, v, 4f, 23f);
            float wet = Mathf.Pow(Mathf.Clamp01(1f - v + (broad - 0.5f) * 0.3f), 1.4f) * (0.5f + 0.5f * streaks);
            var colour = Color.Lerp(new Color(0.3f, 0.29f, 0.26f), new Color(0.22f, 0.26f, 0.16f), Mathf.SmoothStep(0.45f, 0.05f, v));
            colour.a = Mathf.Clamp01(wet * 1.25f) * SideFade(u, v) * 0.85f;
            return colour;
        }

        // Dark slime on the spillway's treads, in bands across the flow, worn where the water runs hardest.
        static Color Algae(float u, float v)
        {
            float bands = Mathf.PerlinNoise(u * 6f + 4f, v * 30f + 1f), patches = Noise(u, v, 14f, 51f), fine = Noise(u, v, 70f, 8f);
            float cover = Mathf.SmoothStep(0.25f, 0.7f, bands * 0.5f + patches * 0.5f);
            var colour = Color.Lerp(new Color(0.15f, 0.19f, 0.1f), new Color(0.33f, 0.34f, 0.2f), fine);
            colour.a = (0.35f + 0.55f * cover) * SideFade(u, v);
            return colour;
        }

        // The stained band on the lake side where the water level rises and falls (the lake sits at about 0.45), with
        // drips running down from it.
        static Color Waterline(float u, float v)
        {
            float wobble = (Noise(u, v, 7f, 17f) - 0.5f) * 0.08f;
            float band = Mathf.SmoothStep(0.22f, 0.36f, v + wobble) * (1f - Mathf.SmoothStep(0.5f, 0.66f, v + wobble));
            float drips = Mathf.PerlinNoise(u * 60f + 7f, 3f) * Mathf.SmoothStep(0.45f, 0.75f, v) * (1f - Mathf.SmoothStep(0.75f, 0.95f, v));
            var colour = Color.Lerp(new Color(0.24f, 0.21f, 0.16f), new Color(0.33f, 0.3f, 0.22f), Noise(u, v, 40f, 2f));
            colour.a = Mathf.Clamp01(band * 0.85f + drips * 0.35f) * SideFade(u, v);
            return colour;
        }
    }
}

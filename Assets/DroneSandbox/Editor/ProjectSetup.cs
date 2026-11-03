using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox.EditorTools
{
    // One-time project configuration: player settings, the HDRP pipeline (see HdrpSetup), the assets the game
    // loads from Resources (HDRP Lit materials, the environment and photo-mode profiles, the night sky), and the
    // main scene's lighting and map preview. Safe to run again.
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
            PlayerSettings.enableFrameTimingStats = true; // CPU and GPU times for the benchmark (Benchmark)

            HdrpSetup.UsePipeline();
            CreateBaseMaterials();
            CreateFieldMaterial();
            CreateTerrainMaterial();
            CreatePostProcessingProfile();
            CreatePhotoFocusProfile();
            ImportTerrainTextures();
            ImportNightSky();
            CreateWhiteWater();
            CreateDamConcrete();
            CreateGrassMaterials();
            CreatePatchwork();
            CreateDecalMaterial();
            CreatePbrMaterials();
            CreateBakedMaterials("Wrecks", 0.12f, 0.15f);
            CreateBakedMaterials("Weathered", 0.3f, 0.2f);
            WeatheringSetup.CreateDamDecals();
            WeatheringSetup.CreateScorchDecal();
            CreateRainMaterial();
            CreateEffectMaterials();
            AssetDatabase.SaveAssets();
            SetUpMainScene();

            AssetDatabase.SaveAssets();
            Debug.Log("Drone Sandbox: project configured.");
        }

        // Templates for the game's runtime-coloured materials (see Art.NewMaterial): plain HDRP Lit and an emissive
        // copy, so player builds keep the Lit shader.
        static void CreateBaseMaterials()
        {
            var shader = HdrpSetup.Lit;
            if (shader == null) return;
            EnsureMaterialsFolder();
            foreach (bool glow in new[] { false, true })
            {
                var material = HdrpSetup.LoadOrCreate(Root + (glow ? "/Materials/BaseLitGlow.mat" : "/Materials/BaseLit.mat"), shader);
                HdrpSetup.Surface(material, 0.15f, 0f);
                material.SetColor("_EmissiveColor", glow ? Color.white : Color.black);
                material.SetFloat("_EmissiveExposureWeight", 0f); // glows read the same in any light (see Art.Glow)
                HdrpSetup.Finish(material);
            }
        }

        // The farm fields' material (Farmland): the crop's texture repeating every few metres, darkened and
        // lightened by a detail map the size of the field that Farmland paints at runtime (tramlines, headlands,
        // patchy growth). Only the detail map's brightness counts, not its normal or smoothness. The neutral map
        // saved with it keeps HDRP's detail-map shader variant in player builds.
        static void CreateFieldMaterial()
        {
            var shader = HdrpSetup.Lit;
            if (shader == null) return;
            string path = Root + "/Textures/DetailNeutral.png";
            if (!File.Exists(path))
            {
                var neutral = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
                var grey = new Color32[16];
                for (int i = 0; i < grey.Length; i++) grey[i] = new Color32(128, 128, 128, 128);
                neutral.SetPixels32(grey);
                File.WriteAllBytes(path, neutral.EncodeToPNG());
                Object.DestroyImmediate(neutral);
                AssetDatabase.ImportAsset(path);
            }
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }
            var material = HdrpSetup.LoadOrCreate(Root + "/Materials/FieldLit.mat", shader);
            HdrpSetup.Surface(material, 0f, 0f);
            material.SetTexture("_DetailMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            material.SetFloat("_DetailAlbedoScale", 1f);
            material.SetFloat("_DetailNormalScale", 0f);
            material.SetFloat("_DetailSmoothnessScale", 0f);
            HdrpSetup.Finish(material);
        }

        static void EnsureMaterialsFolder()
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Materials")) AssetDatabase.CreateFolder(Root, "Materials");
        }

        // HDRP's terrain shader. The keyword turns on the layers' normal maps, which the terrain inspector would
        // set in the editor but a terrain built in a player never does.
        static void CreateTerrainMaterial()
        {
            var shader = Shader.Find("HDRP/TerrainLit");
            if (shader == null) return;
            EnsureMaterialsFolder();
            var material = HdrpSetup.LoadOrCreate(Root + "/Materials/TerrainLit.mat", shader);
            material.EnableKeyword("_NORMALMAP");
            HdrpSetup.Finish(material, instancing: false);
        }

        // Saves the post-processing grade as an asset. URP strips post-processing shader variants that no saved
        // profile uses, so a profile made only at runtime could lose effects in player builds. Existing assets are
        // left alone, so tweaks made in the Inspector survive; delete the asset to get the defaults back.
        static void CreatePostProcessingProfile()
        {
            string path = Root + "/PostProcessing.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            Atmosphere.ConfigureProfile(profile); // re-applied each time, so changes to the grade reach the saved profile
            foreach (var component in profile.components)
            {
                if (AssetDatabase.Contains(component)) continue;
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(component);
            }
            foreach (var component in profile.components) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
        }

        // Photo mode's depth of field, saved as its own profile so player builds keep the Bokeh shader.
        static void CreatePhotoFocusProfile()
        {
            string path = Root + "/PhotoFocus.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) return;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            PhotoMode.ConfigureFocus(profile);
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
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

        // The starry night sky (Tools/fetch_textures.py) as a cubemap, for the physically based sky's space
        // emission (see Atmosphere). Day and sunset skies come from HDRP's atmosphere itself.
        static void ImportNightSky()
        {
            string path = Root + "/Sky/sky_night.hdr";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            if (importer.textureShape == TextureImporterShape.TextureCube) return;
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        // Template for painted emblems and flags (Factions): HDRP Lit cut out by the texture's alpha. The game
        // copies it and sets the texture.
        static void CreateDecalMaterial()
        {
            var shader = HdrpSetup.Lit;
            if (shader == null) return;
            EnsureMaterialsFolder();
            var material = HdrpSetup.LoadOrCreate(Root + "/Materials/Decal.mat", shader);
            HdrpSetup.Surface(material, 0.3f, 0f);
            HdrpSetup.Cutout(material, 0.5f, doubleSided: false);
            HdrpSetup.Finish(material);
        }

        // The white water down the dam's spillway: HDRP Lit, alpha-blended foam (white where the generated foam
        // noise is high, see-through between), scrolled downhill by Flow. It goes in Materials/PBR, named after
        // the dam model's material, so Art.TryModel swaps it in. The lake, river and pool are HDRP water surfaces
        // (see Water.cs) and need no material.
        static void CreateWhiteWater()
        {
            var shader = HdrpSetup.Lit;
            if (shader == null) return;
            string pbr = Root + "/Materials/PBR";
            if (!AssetDatabase.IsValidFolder(pbr)) AssetDatabase.CreateFolder(Root + "/Materials", "PBR");
            // The spillway's three kinds of water (see water_sheet in Tools/Blender/make_dam.py). Churned foam where
            // the water lands: lumpy white with thinner, see-through patches.
            var churn = GeneratedTexture(Root + "/Textures/FoamAlpha.png",
                () => FoamPng(1, new Color(0.62f, 0.72f, 0.75f, 0.35f), new Color(0.95f, 0.97f, 0.98f, 1f), 0.25f), clamp: false);
            var spill = HdrpSetup.LoadOrCreate(pbr + "/WhiteWater.mat", shader);
            HdrpSetup.Albedo(spill, churn, new Color(0.82f, 0.9f, 0.92f, 0.9f), new Vector2(1f / 1.2f, 1f / 1.2f));
            HdrpSetup.Surface(spill, 0.35f, 0f);
            HdrpSetup.Transparent(spill);
            HdrpSetup.Finish(spill);

            // The falling sheets. Their colour and see-through-ness come from FallGradient on the sheets' second UV
            // set (glassy and dark at the lip, streaky, then white churn where they land; the model gives each
            // strand its own slice of it), and white streaks moving down the fall from a detail map on the first.
            var fall = GeneratedTexture(Root + "/Textures/FallGradient.png", FallGradientPng, clamp: false);
            if (AssetImporter.GetAtPath(Root + "/Textures/FallGradient.png") is TextureImporter fallImporter)
            {
                fallImporter.wrapModeV = TextureWrapMode.Clamp;
                fallImporter.SaveAndReimport();
            }
            var streaks = GeneratedTexture(Root + "/Textures/SheetStreaks.png", SheetStreaksPng, clamp: false);
            if (AssetImporter.GetAtPath(Root + "/Textures/SheetStreaks.png") is TextureImporter streakImporter)
            {
                streakImporter.sRGBTexture = false; // a detail map: grey albedo, normal and smoothness as data
                streakImporter.SaveAndReimport();
            }
            var sheet = HdrpSetup.LoadOrCreate(pbr + "/SpillSheet.mat", shader);
            HdrpSetup.Albedo(sheet, fall, Color.white, Vector2.one);
            sheet.SetFloat("_UVBase", 1f);
            sheet.SetColor("_UVMappingMask", new Color(0f, 1f, 0f, 0f));
            sheet.SetTexture("_DetailMap", streaks);
            sheet.SetTextureScale("_DetailMap", new Vector2(1f / 1.6f, 1f / 3f));
            sheet.SetFloat("_UVDetail", 0f);
            sheet.SetColor("_UVDetailsMappingMask", new Color(1f, 0f, 0f, 0f));
            sheet.SetFloat("_DetailAlbedoScale", 1.6f);
            sheet.SetFloat("_DetailNormalScale", 0f);
            sheet.SetFloat("_DetailSmoothnessScale", 0.5f);
            HdrpSetup.Surface(sheet, 0.72f, 0f);
            HdrpSetup.Transparent(sheet);
            HdrpSetup.Finish(sheet);

            // The dark, glassy water running across the spillway's treads, rippled by the old river normal map.
            var flow = HdrpSetup.LoadOrCreate(pbr + "/SpillWater.mat", shader);
            HdrpSetup.Albedo(flow, null, new Color(0.025f, 0.1f, 0.12f, 0.88f), new Vector2(1f / 3f, 1f / 4f));
            var ripples = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/RiverRipples.png");
            if (ripples != null) HdrpSetup.Normal(flow, ripples, 0.7f);
            HdrpSetup.Surface(flow, 0.92f, 0f);
            HdrpSetup.Transparent(flow);
            HdrpSetup.Finish(flow);
        }

        // Grass and wildflower clumps for the terrain (see Grass.cs): painted blades on a transparent card, alpha-cut,
        // both sides drawn, GPU instanced (the terrain draws detail meshes with instancing). They sway in the wind
        // with Shaders/Foliage/WindFoliage, Unity's fern shader from the Shader Graph samples (Production Ready
        // Shaders); plain HDRP Lit if it's missing.
        static void CreateGrassMaterials()
        {
            var wind = Shader.Find("Shader Graphs/WindFoliage");
            var shader = wind != null ? wind : HdrpSetup.Lit;
            if (shader == null) return;
            foreach (var (name, kind) in new[] { ("Grass", 0), ("Flowers", 1), ("Reeds", 2) })
            {
                var texture = GeneratedTexture(Root + "/Textures/" + name + "Blades.png", () => BladesPng(kind), clamp: true);
                var material = LoadOrCreateMaterial(Root + "/Materials/" + name + ".mat", shader);
                if (wind != null) WindFoliage(material, texture, kind == 2 ? 0.16f : kind == 1 ? 0.24f : 0.3f);
                else
                {
                    HdrpSetup.Albedo(material, texture, Color.white, Vector2.one);
                    HdrpSetup.Surface(material, 0.08f, 0f);
                    HdrpSetup.Cutout(material, 0.4f, doubleSided: true, keepNormals: true);
                }
                // No shadows from the tufts: in a low sun, leaning cards shaded each other black.
                material.SetShaderPassEnabled("ShadowCaster", false);
                HdrpSetup.Finish(material);
            }
        }

        // The WindFoliage settings (the graph's own property names). The clumps are 0.55 m tall, so their tips get the
        // full sway; reeds, taller and stiffer, sway less. Each fades out between 60 and 110 m from the camera.
        static void WindFoliage(Material m, Texture texture, float sway)
        {
            m.SetTexture("Texture2D_E1B0D043", texture);                        // Base Map
            m.SetFloat("Vector1_a5b8b09028ce49a39f4d090894c89e22", 0.4f);        // Alpha Clip Threshold
            m.SetFloat("Vector1_8651797e3e304e108dbd25f9d5a426ba", 0.08f);       // Smoothness Scale
            m.SetFloat("_GroundFalloff", 0.3f);
            m.SetFloat("Wind_Intensity", sway);
            m.SetFloat("Wind_Speed", 6f);
            m.SetFloat("Wind_Wavelength", 14f);
            m.SetFloat("Wind_Blast", 0.04f);
            m.SetFloat("Wind_Ripples", 0.08f);
            m.SetFloat("Wind_Turbulence", 0.2f);
            m.SetFloat("Wind_Yaw", 200f);
            m.SetFloat("Animation_Cutoff", 400f);
            // Tufts fade into the ground with distance, so zoomed out the meadow reads as colour, not specks.
            m.SetFloat("Distance_Fade_Start", 60f);
            m.SetFloat("Distance_Fade_End", 110f);
        }

        // Blades of grass growing up from the bottom edge, darker at the root and lighter (some dry) at the tip.
        // Kind 1 (flowers): a few blades carry a white, yellow or purple flower head. Kind 2 (reeds): tall, thin,
        // nearly straight stems, some topped with a brown cattail.
        static byte[] BladesPng(int kind)
        {
            bool flowers = kind == 1, reeds = kind == 2;
            const int size = 256;
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.25f, 0.34f, 0.12f, 0f); // fringe colour for mipmaps
            var rng = new System.Random(3 + kind);
            float Rand() => (float)rng.NextDouble();
            void Paint(int x, int y, Color c, float cover)
            {
                if (x < 0 || y < 0 || x >= size || y >= size || cover <= 0f) return;
                var old = pixels[y * size + x];
                var mixed = Color.Lerp(old.a > 0f ? old : c, c, cover);
                mixed.a = Mathf.Max(old.a, cover);
                pixels[y * size + x] = mixed;
            }
            int blades = flowers ? 26 : reeds ? 36 : 52;
            for (int b = 0; b < blades; b++)
            {
                float x0 = size * (0.05f + 0.9f * Rand()), height = size * (reeds ? 0.65f + 0.33f * Rand() : 0.4f + 0.55f * Rand());
                float width = reeds ? 2.5f + 2f * Rand() : 3.5f + 4.5f * Rand();
                float bend = (Rand() - 0.5f) * size * (reeds ? 0.08f : 0.3f), shade = 0.75f + 0.35f * Rand();
                // Reeds are dark at the foot; grass and flowers only a little, as from above you look down into
                // the tussock and a dark heart reads as a hole in the meadow.
                var root = reeds ? new Color(0.09f, 0.15f, 0.05f) : new Color(0.24f, 0.33f, 0.11f);
                var tip = reeds
                    ? Color.Lerp(new Color(0.42f, 0.47f, 0.2f), new Color(0.62f, 0.55f, 0.32f), Rand() * 0.6f)
                    : Color.Lerp(new Color(0.40f, 0.53f, 0.17f), new Color(0.62f, 0.60f, 0.29f), Rand() < 0.2f ? 0.8f : Rand() * 0.3f);
                for (int y = 0; y < height; y++)
                {
                    float t = y / height;
                    float cx = x0 + bend * t * t, half = width * Mathf.Pow(1f - t, 0.8f) * 0.5f + 0.35f;
                    var c = Color.Lerp(root, tip, Mathf.Pow(t, 0.7f)) * shade;
                    for (int x = Mathf.FloorToInt(cx - half - 1f); x <= Mathf.CeilToInt(cx + half + 1f); x++)
                        Paint(x, y, c, Mathf.Clamp01(half + 0.5f - Mathf.Abs(x + 0.5f - cx)));
                }
                if (reeds && Rand() < 0.3f)
                {
                    // A cattail: a brown sausage just below the tip.
                    float cx = x0 + bend * 0.8f, cy = height * 0.82f;
                    for (int y = Mathf.FloorToInt(cy - 14f); y <= cy + 14f; y++)
                        for (int x = Mathf.FloorToInt(cx - 5f); x <= cx + 5f; x++)
                        {
                            float d = new Vector2((x + 0.5f - cx) / 4f, (y + 0.5f - cy) / 13f).magnitude;
                            Paint(x, y, new Color(0.3f, 0.19f, 0.1f) * (1.1f - 0.3f * d), Mathf.Clamp01((1f - d) * 4f));
                        }
                }
                if (!flowers || Rand() > 0.55f) continue;
                var petals = new[] { new Color(0.95f, 0.95f, 0.9f), new Color(0.98f, 0.82f, 0.2f), new Color(0.62f, 0.46f, 0.86f) }[rng.Next(3)];
                float fx = x0 + bend, fy = height, r = 5f + 4f * Rand();
                for (int y = Mathf.FloorToInt(fy - r - 1); y <= fy + r + 1; y++)
                    for (int x = Mathf.FloorToInt(fx - r - 1); x <= fx + r + 1; x++)
                    {
                        float d = new Vector2(x + 0.5f - fx, y + 0.5f - fy).magnitude;
                        Paint(x, y, d < r * 0.3f ? new Color(0.9f, 0.7f, 0.15f) : petals, Mathf.Clamp01(r + 0.5f - d));
                    }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }

        // The dam's concrete: the photo texture laid over a whole 8 x 16 m face with weathering painted in, damp
        // and mossy at the foot, rust-brown streaks running down from the crest, patchy stains and the joints
        // between pours. The dam model's UVs are metres from its foot, so the texture's height spans the dam;
        // it is clamped top and bottom, so below the foot stays mossy and above the crest stays clean.
        static void CreateDamConcrete()
        {
            var shader = HdrpSetup.Lit;
            string diffuse = PbrTextures + "/dam_concrete_diff.png", normal = PbrTextures + "/dam_concrete_nor.png";
            if (shader == null || !File.Exists(PbrTextures + "/concrete_wall_diff.jpg")) return;
            if (!File.Exists(diffuse) || !File.Exists(normal))
            {
                var (colour, bumps) = WeatheredConcrete(PbrTextures + "/concrete_wall_diff.jpg", PbrTextures + "/concrete_wall_nor.jpg");
                File.WriteAllBytes(diffuse, colour);
                File.WriteAllBytes(normal, bumps);
                AssetDatabase.ImportAsset(diffuse);
                AssetDatabase.ImportAsset(normal);
            }
            foreach (var (path, isNormal) in new[] { (diffuse, false), (normal, true) })
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }
            string folder = Root + "/Materials/PBR";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root + "/Materials", "PBR");
            var material = LoadOrCreateMaterial(folder + "/DamConcrete.mat", shader);
            // A shade darker than the photo, so sunlit faces keep their texture.
            HdrpSetup.Albedo(material, AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse), new Color(0.85f, 0.85f, 0.85f), new Vector2(1f / 8f, 1f / 16f));
            HdrpSetup.Normal(material, AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            HdrpSetup.Surface(material, 0.3f, 0f); // dry, weathered concrete (the decals make it wet or mossy)
            HdrpSetup.Finish(material);
        }

        static (byte[] colour, byte[] normal) WeatheredConcrete(string sourcePath, string normalPath)
        {
            const int width = 1024, height = 2048; // 8 m across, 16 m up: the 4 m photo tile repeats 2 x 4 times
            var source = new Texture2D(2, 2);
            source.LoadImage(File.ReadAllBytes(sourcePath));
            var sourceNormal = new Texture2D(2, 2);
            sourceNormal.LoadImage(File.ReadAllBytes(normalPath));
            var src = source.GetPixels();
            var srcN = sourceNormal.GetPixels();
            int sw = source.width, sh = source.height;
            var rng = new System.Random(17);
            // Streaks: where they start near the crest, how wide and how far down they run.
            var streaks = new List<(float x, float width, float top, float length, float strength)>();
            for (int i = 0; i < 46; i++)
                streaks.Add(((float)rng.NextDouble(), 0.004f + 0.02f * (float)rng.NextDouble(), 0.8f + 0.15f * (float)rng.NextDouble(),
                    0.15f + 0.55f * (float)rng.NextDouble(), 0.4f + 0.6f * (float)rng.NextDouble()));
            var colour = new Color[width * height];
            var normals = new Color[width * height];
            var moss = new Color(0.17f, 0.22f, 0.09f);
            var rust = new Color(0.45f, 0.38f, 0.3f);
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    int sx = (x * 2) % sw, sy = (y * 2) % sh;
                    var c = src[sy * sw + sx];
                    // Broad stains.
                    float stain = Mathf.PerlinNoise(u * 6f + 3f, v * 10f + 7f) * 0.7f + Mathf.PerlinNoise(u * 25f, v * 40f) * 0.3f;
                    c *= Mathf.Lerp(0.82f, 1.08f, stain);
                    // Streaks down from the crest, fading as they run.
                    float streak = 0f;
                    foreach (var s in streaks)
                    {
                        float dx = Mathf.Abs(u - s.x);
                        dx = Mathf.Min(dx, 1f - dx); // wraps round the tile
                        if (dx > s.width || v > s.top || v < s.top - s.length) continue;
                        float along = (s.top - v) / s.length;
                        streak = Mathf.Max(streak, s.strength * (1f - dx / s.width) * (1f - along) * Mathf.Clamp01(along * 8f));
                    }
                    c = Color.Lerp(c, c * rust * 1.4f, streak * 0.6f);
                    // Damp and moss towards the foot (the bottom 2-3 m), blotchy.
                    float blot = Mathf.PerlinNoise(u * 30f + 11f, v * 60f + 2f);
                    float wet = 1f - Mathf.SmoothStep(0f, 1f, (v - 0.03f - blot * 0.06f) / 0.13f);
                    c *= Mathf.Lerp(1f, 0.55f, wet);
                    c = Color.Lerp(c, moss * (0.7f + 0.6f * blot), wet * Mathf.SmoothStep(0f, 1f, (blot - 0.35f) * 3f) * 0.8f);
                    // Joints between pours every 1.5 m.
                    float joint = Mathf.Repeat(v * 16f, 1.5f);
                    if (joint < 0.025f || joint > 1.49f) c *= 0.78f;
                    c.a = 1f;
                    colour[y * width + x] = c;
                    normals[y * width + x] = srcN[sy * sw + sx];
                }
            }
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(sourceNormal);
            return (Encode(colour, width, height), Encode(normals, width, height));
        }

        static byte[] Encode(Color[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }

        static Material LoadOrCreateMaterial(string path, Shader shader) => HdrpSetup.LoadOrCreate(path, shader);

        // Rain streaks (Atmosphere.Rain): an unlit, see-through material with a soft vertical streak.
        static void CreateRainMaterial()
        {
            var shader = Shader.Find("HDRP/Unlit");
            if (shader == null) return;
            var streak = GeneratedTexture(Root + "/Textures/RainStreak.png", () =>
            {
                const int w = 8, h = 64;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var pixels = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float across = 1f - Mathf.Abs((x + 0.5f) / w * 2f - 1f), along = Mathf.Sin((y + 0.5f) / h * Mathf.PI);
                        pixels[y * w + x] = new Color(1f, 1f, 1f, across * across * along);
                    }
                t.SetPixels(pixels);
                byte[] png = t.EncodeToPNG();
                Object.DestroyImmediate(t);
                return png;
            }, clamp: true);
            var material = LoadOrCreateMaterial(Root + "/Materials/Rain.mat", shader);
            material.SetTexture("_UnlitColorMap", streak);
            material.SetColor("_UnlitColor", new Color(0.78f, 0.82f, 0.88f, 0.45f));
            HdrpSetup.Transparent(material);
            HdrpSetup.Finish(material, instancing: false);
        }

        // Explosions, smoke and fire (Effects): Unity's Flipbook Blending particle shader from the Shader Graph
        // samples, copied into Shaders/Particles as ParticleSmoke (unlit, alpha-blended) and ParticleFire (unlit,
        // additive). Smoke and fireballs share the puff atlas painted by Tools/make_smoke_puffs.py; flames on wrecks
        // use the flame flipbook from the Visual Effect Graph samples; sparks use the soft dot.
        static void CreateEffectMaterials()
        {
            var smokeShader = Shader.Find("Shader Graphs/ParticleSmoke");
            var fireShader = Shader.Find("Shader Graphs/ParticleFire");
            if (smokeShader == null || fireShader == null)
            {
                Debug.LogError("Drone Sandbox: the particle shaders (Shaders/Particles) are missing.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(Root + "/Materials/FX")) AssetDatabase.CreateFolder(Root + "/Materials", "FX");
            var puffs = EffectTexture("SmokePuffs.png");
            var flame = EffectTexture("Flame03_Temp_16x5.png");
            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/SoftDot.png");
            foreach (var (name, shader, texture) in new[] { ("Smoke", smokeShader, puffs), ("Fire", fireShader, puffs), ("Flame", fireShader, flame), ("Spark", fireShader, dot) })
            {
                var material = LoadOrCreateMaterial(Root + "/Materials/FX/" + name + ".mat", shader);
                material.SetTexture("_BaseMap", texture);
                HdrpSetup.Finish(material, instancing: false);
            }
            CreatePropBlur();
        }

        // The faint disc a spinning propeller makes (the Blur_N discs over the drones' props, see Drone.DressModel):
        // a dark, see-through texture whose U runs from the hub to the tip, densest where the blades are broadest and
        // with a fainter ring at the tips.
        static void CreatePropBlur()
        {
            string path = Root + "/Textures/FX/PropBlur.png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(256, 4, TextureFormat.RGBA32, false);
                for (int x = 0; x < 256; x++)
                {
                    float u = x / 255f;
                    float alpha = Mathf.SmoothStep(0f, 0.2f, Mathf.InverseLerp(0.08f, 0.25f, u)) * Mathf.Lerp(1f, 0.75f, Mathf.InverseLerp(0.3f, 0.85f, u));
                    alpha += 0.1f * Mathf.Clamp01(1f - Mathf.Abs(u - 0.93f) / 0.04f);
                    alpha *= Mathf.InverseLerp(1f, 0.97f, u);
                    for (int y = 0; y < 4; y++) texture.SetPixel(x, y, new Color(0.05f, 0.05f, 0.055f, alpha));
                }
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
            }
            var material = LoadOrCreateMaterial(Root + "/Materials/FX/PropBlur.mat", HdrpSetup.Lit);
            HdrpSetup.Albedo(material, EffectTexture("PropBlur.png"), Color.white, Vector2.one);
            HdrpSetup.Surface(material, 0.2f, 0f);
            HdrpSetup.Transparent(material);
            material.SetFloat("_EnableBlendModePreserveSpecularLighting", 0f); // or the whole disc catches the sun
            HdrpSetup.Finish(material, instancing: false);
        }

        // The burnt-out wrecks (Tools/Blender/make_wrecks.py, Textures/Wrecks) and the weathered vehicles
        // (make_weathered.py, Textures/Weathered) each carry one material, named after the model, with its own baked
        // colour and normal maps, and the weathered ones a mask map (<name>_mask.png) for their metallic, occlusion
        // and smoothness; without one the whole model gets the smoothness and metallic given here. Made in
        // Materials/PBR so Art.TryModel swaps it in by name.
        static void CreateBakedMaterials(string folder, float smoothness, float metallic)
        {
            folder = "Assets/DroneSandbox/Textures/" + folder;
            if (!Directory.Exists(folder)) return;
            var shader = HdrpSetup.Lit;
            EnsureMaterialsFolder();
            if (!AssetDatabase.IsValidFolder(Root + "/Materials/PBR")) AssetDatabase.CreateFolder(Root + "/Materials", "PBR");
            foreach (string file in Directory.GetFiles(folder, "*_diff.jpg"))
            {
                string name = Path.GetFileNameWithoutExtension(file).Replace("_diff", "");
                var albedo = BakedTexture(folder + "/" + name + "_diff.jpg", TextureImporterType.Default, true);
                var normal = BakedTexture(folder + "/" + name + "_nor.jpg", TextureImporterType.NormalMap, false);
                var mask = BakedTexture(folder + "/" + name + "_mask.png", TextureImporterType.Default, false);
                var material = HdrpSetup.LoadOrCreate(Root + "/Materials/PBR/" + name + ".mat", shader);
                HdrpSetup.Albedo(material, albedo, Color.white, Vector2.one);
                HdrpSetup.Surface(material, smoothness, metallic);
                if (normal != null) HdrpSetup.Normal(material, normal);
                if (mask != null) HdrpSetup.Mask(material, mask);
                HdrpSetup.Finish(material);
            }
        }

        static Texture2D BakedTexture(string path, TextureImporterType type, bool colour)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                if (importer.textureType != type || importer.anisoLevel != 8 || importer.sRGBTexture != colour)
                {
                    importer.textureType = type;
                    importer.sRGBTexture = colour; // a mask map holds numbers, not colours
                    importer.anisoLevel = 8;
                    importer.maxTextureSize = 2048;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D EffectTexture(string file)
        {
            string path = Root + "/Textures/FX/" + file;
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // The distant farmland's texture (Backdrop.Patchwork), made once here rather than at every start.
        static void CreatePatchwork()
        {
            string path = Root + "/Textures/Patchwork.png";
            if (File.Exists(path)) return;
            var texture = Backdrop.Patchwork();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.maxTextureSize = 2048;
                importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }
        }

        // Writes a generated PNG once and imports it as a plain colour texture.
        static Texture2D GeneratedTexture(string path, System.Func<byte[]> make, bool clamp)
        {
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, make());
                AssetDatabase.ImportAsset(path);
            }
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = clamp;
                importer.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Tileable white water: noise streaked along the flow (four times finer across it than down it), mostly
        // opaque white with thinner, darker see-through streaks between, like water falling in strands.
        // How a falling sheet changes from its lip (top of the picture) to where it lands (bottom): glassy, dark and
        // see-through, then streaky, then dense white churn. Where each change happens wanders across the picture
        // (each strand of the spillway takes its own slice), and the boundaries are ragged.
        static byte[] FallGradientPng()
        {
            const int size = 256;
            var glassy = new Color(0.2f, 0.34f, 0.38f, 0.5f);
            var streaky = new Color(0.6f, 0.72f, 0.76f, 0.82f);
            var churn = new Color(0.9f, 0.94f, 0.96f, 0.95f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float down = 1f - (y + 0.5f) / size; // 0 at the lip, 1 where it lands
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float first = 0.3f + (Mathf.PerlinNoise(u * 7f, 3.1f) - 0.5f) * 0.3f, second = 0.72f + (Mathf.PerlinNoise(u * 9f, 8.7f) - 0.5f) * 0.3f;
                    float ragged = (Mathf.PerlinNoise(u * 40f, down * 12f) - 0.5f) * 0.12f;
                    var c = Color.Lerp(glassy, streaky, Mathf.SmoothStep(0f, 1f, (down + ragged - first) / 0.18f + 0.5f));
                    c = Color.Lerp(c, churn, Mathf.SmoothStep(0f, 1f, (down + ragged - second) / 0.2f + 0.5f));
                    pixels[y * size + x] = c;
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }

        // A detail map of streaks down a falling sheet: grey albedo in red (brighter strands, darker gaps), flat
        // normal (0.5) in green and alpha, middling smoothness in blue.
        static byte[] SheetStreaksPng()
        {
            var foam = new Texture2D(2, 2);
            foam.LoadImage(FoamPng(4, Color.black, Color.white, 0.4f));
            var source = foam.GetPixels();
            var pixels = new Color[source.Length];
            for (int i = 0; i < source.Length; i++)
                pixels[i] = new Color(Mathf.Lerp(0.3f, 0.72f, source[i].r), 0.5f, Mathf.Lerp(0.4f, 0.6f, source[i].r), 0.5f);
            foam.SetPixels(pixels);
            byte[] png = foam.EncodeToPNG();
            Object.DestroyImmediate(foam);
            return png;
        }

        // Foam: fractal noise, stretched along the flow (stretch times finer across it, for streaks), shaded from
        // low (thin, see-through water) to high (dense foam) above the threshold from.
        static byte[] FoamPng(int stretch, Color low, Color high, float from)
        {
            const int size = 256;
            var rng = new System.Random(9);
            var octaves = new List<(int cells, float amp, float[] lattice)>();
            foreach (var (cells, amp) in new[] { (4, 0.45f), (8, 1f), (16, 0.7f), (32, 0.45f), (64, 0.25f) })
            {
                var lattice = new float[cells * 4 * cells];
                for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rng.NextDouble();
                octaves.Add((cells, amp, lattice));
            }
            var values = new float[size * size];
            float min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float h = 0f;
                    foreach (var o in octaves)
                    {
                        int across = o.cells * stretch;
                        float gx = x * (float)across / size, gy = y * (float)o.cells / size;
                        int x0 = Mathf.FloorToInt(gx), y0 = Mathf.FloorToInt(gy);
                        float fx = gx - x0, fy = gy - y0;
                        fx = fx * fx * (3f - 2f * fx);
                        fy = fy * fy * (3f - 2f * fy);
                        float At(int ix, int iy) => o.lattice[(iy % o.cells) * across + ix % across];
                        float a = Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), fx), b = Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx);
                        h += o.amp * Mathf.Lerp(a, b, fy);
                    }
                    values[y * size + x] = h;
                    min = Mathf.Min(min, h);
                    max = Mathf.Max(max, h);
                }
            }
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float g = (values[i] - min) / (max - min);
                float strand = Mathf.SmoothStep(0f, 1f, (g - from) / 0.4f);
                pixels[i] = Color.Lerp(low, high, strand);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }

        // ---- Photo-textured materials for the models ----

        const string PbrTextures = "Assets/DroneSandbox/Textures/PBR";

        // Blender material name -> texture (Textures/PBR/<texture>_diff and _nor, from Tools/fetch_textures.py,
        // or the generated "foliage"), tint, metres per texture repeat, smoothness, metallic. Art.TryModel swaps
        // these in for the flat-coloured materials that come with each model; materials not listed keep their colour.
        static readonly (string material, string texture, Color tint, float tile, float smooth, float metal)[] PbrMaterials =
        {
            ("Concrete", "concrete", new Color(1.15f, 1.15f, 1.12f), 3f, 0.2f, 0f),
            ("ConcreteStained", "concrete_wall", new Color(0.95f, 0.95f, 0.92f), 4f, 0.15f, 0f),
            ("Galvanized", "corrugated", new Color(1.45f, 1.47f, 1.5f), 2.5f, 0.45f, 0.25f),
            ("SteelDark", "metal_plate", new Color(0.45f, 0.45f, 0.45f), 1.5f, 0.4f, 0.5f),
            ("Gunmetal", "metal_plate", new Color(0.4f, 0.4f, 0.42f), 1.2f, 0.5f, 0.6f),
            ("RustySteel", "rust_paint", new Color(0.55f, 0.45f, 0.4f), 1.5f, 0.25f, 0.2f),
            ("HescoFabric", "hessian", new Color(0.9f, 0.84f, 0.72f), 1.1f, 0.05f, 0f),
            ("Sandbag", "hessian", new Color(0.88f, 0.83f, 0.72f), 0.7f, 0.05f, 0f),
            ("TentCanvas", "hessian", new Color(0.55f, 0.6f, 0.38f), 1.6f, 0.05f, 0f),
            ("TentCanvasDark", "hessian", new Color(0.25f, 0.29f, 0.17f), 1.6f, 0.05f, 0f),
            ("Canvas", "hessian", new Color(0.55f, 0.55f, 0.38f), 1.6f, 0.05f, 0f),
            ("Olive", "green_metal", new Color(0.85f, 0.9f, 0.7f), 2f, 0.3f, 0.2f),
            ("OliveDark", "green_metal", new Color(0.55f, 0.58f, 0.45f), 2f, 0.3f, 0.2f),
            ("ContainerOlive", "green_metal", new Color(0.8f, 0.9f, 0.7f), 3f, 0.3f, 0.3f),
            ("ContainerRust", "rust_paint", Color.white, 3f, 0.3f, 0.3f),
            ("Wood", "rough_wood", new Color(0.65f, 0.6f, 0.55f), 2f, 0.1f, 0f),
            ("WoodLight", "wood", new Color(1.1f, 1.0f, 0.9f), 2f, 0.1f, 0f),
            ("RoofFelt", "asphalt", new Color(0.7f, 0.7f, 0.7f), 3f, 0.15f, 0f),
            ("Bark", "bark", new Color(0.8f, 0.8f, 0.8f), 1.5f, 0.05f, 0f),
            // Bark of the trees from make_trees.py (UVs run round and along the branches, in metres).
            ("BarkOak", "oak_bark", new Color(0.95f, 0.92f, 0.9f), 1.6f, 0.08f, 0f),
            ("BarkBirch", "birch_bark", Color.white, 1.6f, 0.15f, 0f),
            ("BarkPoplar", "willow_bark", new Color(0.9f, 0.9f, 0.88f), 1.6f, 0.08f, 0f),
            ("BarkPine", "pine_bark", Color.white, 1.5f, 0.05f, 0f),
            ("PlasterWhite", "plaster_white", new Color(1.35f, 1.33f, 1.28f), 3f, 0.08f, 0f),
            ("PlasterGrey", "plaster_grey", new Color(0.85f, 0.85f, 0.85f), 3f, 0.08f, 0f),
            ("Stone", "stone_wall", new Color(1.1f, 1.05f, 1.0f), 2.5f, 0.1f, 0f),
            ("RoofTile", "roof_tiles", Color.white, 2.5f, 0.2f, 0f),
            ("RoofMetal", "corrugated", new Color(1.0f, 1.03f, 1.08f), 2f, 0.4f, 0.3f),
            ("RoofBarn", "worn_corrugated", new Color(0.8f, 0.78f, 0.75f), 2.5f, 0.3f, 0.3f),
            ("Spire", "corrugated", new Color(0.35f, 0.37f, 0.4f), 2f, 0.5f, 0.6f),
            ("Brick", "brick", Color.white, 1.5f, 0.1f, 0f),
            ("DoorWood", "rough_wood", new Color(0.45f, 0.33f, 0.25f), 1.5f, 0.1f, 0f),
            ("WeatheredPlanks", "plank_siding", new Color(1.1f, 1.05f, 1.0f), 2.5f, 0.08f, 0f),
            ("WeatheredPlanksDark", "plank_siding", new Color(0.65f, 0.62f, 0.6f), 2.5f, 0.08f, 0f),
            ("FenceWood", "rough_wood", new Color(0.85f, 0.8f, 0.72f), 1.5f, 0.08f, 0f),
            ("Asphalt", "asphalt", new Color(0.8f, 0.8f, 0.8f), 4f, 0.2f, 0f),
            ("Rock", "mossy_rock", new Color(0.9f, 0.9f, 0.9f), 2.5f, 0.15f, 0f),
            ("Moss", "mossy_rock", new Color(0.75f, 0.95f, 0.55f), 1.5f, 0.05f, 0f),
            ("Gravel", "gravel", new Color(0.78f, 0.78f, 0.8f), 2.5f, 0.1f, 0f), // your base's yard (Bases.Yard)
            // Leaves: the generated leaf texture, tinted per tree.
            ("PineNeedles", "foliage", Leaf(0.18f, 0.26f, 0.18f), 1.6f, 0.1f, 0f),
            ("PineNeedlesLight", "foliage", Leaf(0.22f, 0.31f, 0.20f), 1.6f, 0.1f, 0f),
            ("Spruce", "foliage", Leaf(0.15f, 0.23f, 0.17f), 1.4f, 0.1f, 0f),
            ("SpruceLight", "foliage", Leaf(0.19f, 0.27f, 0.19f), 1.4f, 0.1f, 0f),
            ("OakLeaves", "foliage", Leaf(0.23f, 0.33f, 0.16f), 1.8f, 0.12f, 0f),
            ("OakLeavesLight", "foliage", Leaf(0.32f, 0.42f, 0.20f), 1.8f, 0.12f, 0f),
            ("BirchLeaves", "foliage", Leaf(0.35f, 0.47f, 0.22f), 1.5f, 0.12f, 0f),
            ("PoplarLeaves", "foliage", Leaf(0.24f, 0.35f, 0.18f), 1.5f, 0.12f, 0f),
            ("BushLeaves", "foliage", Leaf(0.26f, 0.35f, 0.17f), 1.2f, 0.12f, 0f),
            ("BushLeavesLight", "foliage", Leaf(0.33f, 0.44f, 0.20f), 1.2f, 0.12f, 0f),
        };

        // The leaf texture averages about 0.77, so brighten the wanted colour a little. (These are the dark inner
        // masses of the crowns; the leaf cards around them carry the brighter colour.)
        static Color Leaf(float r, float g, float b) => new Color(r * 1.35f, g * 1.35f, b * 1.35f);

        static void CreatePbrMaterials()
        {
            if (!Directory.Exists(PbrTextures)) return;
            CreateFoliageTextures();
            foreach (string file in Directory.GetFiles(PbrTextures))
            {
                if (file.EndsWith(".meta")) continue;
                string path = file.Replace('\\', '/');
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                string fileName = Path.GetFileNameWithoutExtension(path);
                var wanted = fileName.EndsWith("_nor") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                // Tree atlases (make_trees.py) and scanned plants (import_scans.py, whose colour map carries the
                // leaves' cut-out in its alpha): the alpha cuts out the leaves, and mipmaps keep the same coverage so
                // a far crown or fern doesn't go see-through.
                bool scan = fileName.StartsWith("scan_");
                bool scanPlant = scan && fileName.EndsWith("_diff") && importer.DoesSourceTextureHaveAlpha();
                bool foliage = scanPlant || ((fileName.StartsWith("leaves_") || fileName.StartsWith("impostor_")) && !fileName.EndsWith("_nor"));
                // Scanned rocks come at 2K (they're big and the camera can get close); scanned plants are 1K anyway.
                int maxSize = fileName.StartsWith("leaves_") || scan ? 2048 : 1024;
                if (importer.textureType == wanted && importer.anisoLevel == 8 && importer.mipMapsPreserveCoverage == foliage
                    && importer.maxTextureSize == maxSize) continue;
                importer.textureType = wanted;
                importer.maxTextureSize = maxSize;
                importer.anisoLevel = 8;
                importer.alphaIsTransparency = foliage;
                importer.mipMapsPreserveCoverage = foliage;
                importer.alphaTestReferenceValue = scanPlant ? ScanCutoff : 0.45f;
                importer.SaveAndReimport();
            }

            var shader = HdrpSetup.Lit;
            if (shader == null) return;
            string folder = Root + "/Materials/PBR";
            EnsureMaterialsFolder();
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root + "/Materials", "PBR");
            foreach (var m in PbrMaterials)
            {
                var albedo = LoadTexture(m.texture + "_diff");
                var normal = LoadTexture(m.texture + "_nor");
                if (albedo == null) continue;
                string path = folder + "/" + m.material + ".mat";
                var material = HdrpSetup.LoadOrCreate(path, shader);
                HdrpSetup.Albedo(material, albedo, m.tint, Vector2.one / m.tile);
                HdrpSetup.Surface(material, m.smooth, m.metal);
                if (normal != null) HdrpSetup.Normal(material, normal);
                HdrpSetup.Finish(material);
            }
            CreateLeafCardMaterials(shader, folder);
            CreateCamoMaterials(shader, folder);
            CreateScanMaterials(shader, folder);
        }

        // Camouflage nets (the Blender models' CamoNet, CamoNetA and CamoNetB): a woven net in four muted tones in
        // patches 0.3-0.9 m across, with scrim garnish knotted on and holes between, cut out by the alpha so the
        // shape under it still shows through a little. One 2 m tile, a different tint for each net material.
        static void CreateCamoMaterials(Shader shader, string folder)
        {
            string path = PbrTextures + "/camo_net.png";
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, CamoNetPng());
                AssetDatabase.ImportAsset(path);
            }
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && !importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = 0.4f;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            foreach (var (name, tint) in new[] { ("CamoNet", new Color(0.9f, 0.95f, 0.85f)), ("CamoNetA", new Color(0.85f, 0.9f, 0.8f)), ("CamoNetB", new Color(1f, 0.95f, 0.85f)) })
            {
                var material = HdrpSetup.LoadOrCreate(folder + "/" + name + ".mat", shader);
                HdrpSetup.Albedo(material, texture, tint, Vector2.one / 2f);
                HdrpSetup.Surface(material, 0.05f, 0f);
                HdrpSetup.Cutout(material, 0.4f, doubleSided: true);
                HdrpSetup.Finish(material);
            }
        }

        static byte[] CamoNetPng()
        {
            const int size = 512; // 2 m
            var tones = new[] { new Color(0.13f, 0.16f, 0.07f), new Color(0.29f, 0.33f, 0.15f), new Color(0.33f, 0.27f, 0.14f), new Color(0.47f, 0.44f, 0.28f) };
            var pixels = new Color32[size * size];
            float Tile(float u, float v, float scale, float seed)
            {
                // Tileable noise: blend the four wrapped copies so the 2 m tile repeats seamlessly.
                float a = Mathf.PerlinNoise(u * scale + seed, v * scale + seed), b = Mathf.PerlinNoise((u - 1f) * scale + seed, v * scale + seed);
                float c = Mathf.PerlinNoise(u * scale + seed, (v - 1f) * scale + seed), d = Mathf.PerlinNoise((u - 1f) * scale + seed, (v - 1f) * scale + seed);
                return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
            }
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    // Patches 0.3-0.9 m: two scales of noise pick one of four tones.
                    float n = Tile(u, v, 3f, 11f) * 0.65f + Tile(u, v, 7f, 37f) * 0.35f;
                    var colour = tones[Mathf.Clamp((int)(n * 5f - 0.6f), 0, 3)];
                    // The net: diagonal cords 10 cm apart, and garnish strips tied on in clumps.
                    float cu = Mathf.Abs(Mathf.Repeat((u + v) * 20f, 1f) - 0.5f), cv = Mathf.Abs(Mathf.Repeat((u - v) * 20f, 1f) - 0.5f);
                    bool cord = Mathf.Min(cu, cv) < 0.05f;
                    bool garnish = Tile(u, v, 12f, 73f) * 0.7f + Tile(u, v, 30f, 5f) * 0.3f > 0.53f;
                    float shade = 0.85f + 0.3f * Tile(u, v, 24f, 91f);
                    float alpha = garnish ? 1f : cord ? 0.9f : 0f;
                    pixels[y * size + x] = new Color(colour.r * shade, colour.g * shade, colour.b * shade, alpha);
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }

        const float ScanCutoff = 0.5f; // Poly Haven's own alpha cutoff for its plant scans

        // The photo-scanned rocks, cliffs, deadwood and plants (Tools/fetch_models.py, Tools/Blender/import_scans.py):
        // one material per scan, named Scan_<id> like the material the scan's models carry, so Art.TryModel swaps it
        // in. Made for every Textures/PBR/scan_<id>_diff, with scan_<id>_nor as the normal map. A scan's UVs cover its
        // texture once (0-1), so no tiling. Plants carry their cut-out in the colour map's alpha: cut out and drawn
        // from both sides. Smoothness is low and even rather than from the scans' roughness maps: stone and bark are
        // matt, leaves have a little sheen.
        static void CreateScanMaterials(Shader shader, string folder)
        {
            foreach (string file in Directory.GetFiles(PbrTextures, "scan_*_diff.*"))
            {
                if (file.EndsWith(".meta")) continue;
                string path = file.Replace('\\', '/');
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (albedo == null) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                string id = name.Substring("scan_".Length, name.Length - "scan_".Length - "_diff".Length);
                bool plant = AssetImporter.GetAtPath(path) is TextureImporter importer && importer.DoesSourceTextureHaveAlpha();
                bool wood = id.StartsWith("tree_stump") || id.StartsWith("dead_tree");
                var material = HdrpSetup.LoadOrCreate(folder + "/Scan_" + id + ".mat", shader);
                HdrpSetup.Albedo(material, albedo, Color.white, Vector2.one);
                HdrpSetup.Surface(material, plant ? 0.3f : wood ? 0.1f : 0.15f, 0f);
                var normal = LoadTexture("scan_" + id + "_nor");
                if (normal != null) HdrpSetup.Normal(material, normal);
                if (plant) HdrpSetup.Cutout(material, ScanCutoff, doubleSided: true);
                HdrpSetup.Finish(material);
            }
        }

        // Leaf cards (see leaf_cards in Tools/Blender/common.py): Blender material name -> leaf-cluster texture, tint.
        static readonly (string material, string texture, Color tint)[] LeafCardMaterials =
        {
            ("OakCards", "leafcard_broad", new Color(0.50f, 0.66f, 0.30f)),
            ("BirchCards", "leafcard_broad", new Color(0.62f, 0.80f, 0.36f)),
            ("PoplarCards", "leafcard_broad", new Color(0.46f, 0.64f, 0.30f)),
            ("BushCards", "leafcard_broad", new Color(0.50f, 0.66f, 0.28f)),
            ("PineCards", "leafcard_needle", new Color(0.36f, 0.50f, 0.32f)),
            // make_trees.py's twig atlases and the far billboards' pictures of the whole trees, a little darker than
            // drawn so crowns in low sun stay green instead of turning cream.
            ("LeavesOak", "leaves_oak", new Color(0.82f, 0.82f, 0.82f)),
            ("LeavesBirch", "leaves_birch", new Color(0.82f, 0.82f, 0.82f)),
            ("LeavesPoplar", "leaves_poplar", new Color(0.82f, 0.82f, 0.82f)),
            ("LeavesSpruce", "leaves_spruce", new Color(0.82f, 0.82f, 0.82f)),
            ("LeavesPine", "leaves_pine", new Color(0.82f, 0.82f, 0.82f)),
            ("ImpostorOak", "impostor_oak", new Color(0.82f, 0.82f, 0.82f)),
            ("ImpostorBirch", "impostor_birch", new Color(0.82f, 0.82f, 0.82f)),
            ("ImpostorPoplar", "impostor_poplar", new Color(0.82f, 0.82f, 0.82f)),
            ("ImpostorSpruce", "impostor_spruce", new Color(0.82f, 0.82f, 0.82f)),
            ("ImpostorPine", "impostor_pine", new Color(0.82f, 0.82f, 0.82f)),
        };

        // Alpha-cut, double-sided materials for the leaf cards.
        static void CreateLeafCardMaterials(Shader shader, string folder)
        {
            MakeLeafCard(PbrTextures + "/leafcard_broad.png", false);
            MakeLeafCard(PbrTextures + "/leafcard_needle.png", true);
            foreach (var m in LeafCardMaterials)
            {
                var texture = LoadTexture(m.texture);
                if (texture == null) continue;
                string path = folder + "/" + m.material + ".mat";
                var material = HdrpSetup.LoadOrCreate(path, shader);
                HdrpSetup.Albedo(material, texture, m.tint, Vector2.one);
                HdrpSetup.Surface(material, 0.12f, 0f);
                // The new trees' cards carry normals pointing out of the crown, so the back faces keep them.
                bool crownNormals = m.material.StartsWith("Leaves") || m.material.StartsWith("Impostor");
                HdrpSetup.Cutout(material, 0.45f, doubleSided: true, keepNormals: crownNormals);
                var normal = LoadTexture(m.texture + "_nor");
                if (normal != null) HdrpSetup.Normal(material, normal, 0.8f);
                HdrpSetup.Finish(material);
            }
        }

        // A square texture of one spray of leaves (or pine needles) on a transparent background.
        static void MakeLeafCard(string path, bool needles)
        {
            if (!File.Exists(path))
            {
                const int size = 512;
                var pixels = new Color[size * size];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.45f, 0.52f, 0.36f, 0f); // fringe colour for mipmaps
                var rng = new System.Random(needles ? 2 : 1);
                float Rand() => (float)rng.NextDouble();
                void Blob(float cx, float cy, float length, float width, float angle, Color colour)
                {
                    float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                    int reach = Mathf.CeilToInt(length);
                    for (int dy = -reach; dy <= reach; dy++)
                    {
                        for (int dx = -reach; dx <= reach; dx++)
                        {
                            float u = (dx * ca + dy * sa) / length, v = (-dx * sa + dy * ca) / width;
                            float d = u * u + v * v;
                            int px = Mathf.RoundToInt(cx) + dx, py = Mathf.RoundToInt(cy) + dy;
                            if (d >= 1f || px < 0 || py < 0 || px >= size || py >= size) continue;
                            float light = 0.8f + 0.2f * (1f - d) + 0.1f * u;
                            var c = colour * (Mathf.Abs(v) < 0.1f ? light * 0.85f : light);
                            c.a = 1f;
                            pixels[py * size + px] = c;
                        }
                    }
                }
                if (needles)
                {
                    // Seven sprigs fanning out from the middle, needles either side of each.
                    for (int b = 0; b < 7; b++)
                    {
                        float a = b * Mathf.PI * 2f / 7f + Rand() * 0.4f, reach = size * (0.32f + Rand() * 0.12f);
                        for (float t = 0f; t < reach; t += 3.5f)
                        {
                            float x = size / 2f + Mathf.Cos(a) * t, y = size / 2f + Mathf.Sin(a) * t;
                            float shade = 0.6f + Rand() * 0.4f;
                            var colour = new Color(shade * 0.9f, shade, shade * 0.85f);
                            foreach (float side in new[] { -1f, 1f })
                            {
                                float na = a + side * (0.9f + Rand() * 0.3f), len = 16f + Rand() * 8f;
                                Blob(x + Mathf.Cos(na) * len * 0.5f, y + Mathf.Sin(na) * len * 0.5f, len * 0.5f, 1.6f, na, colour);
                            }
                        }
                    }
                }
                else
                {
                    // Broad leaves crowded towards the middle, pointing roughly outwards.
                    for (int i = 0; i < 230; i++)
                    {
                        float r = Mathf.Sqrt(Rand()) * size * 0.42f, a = Rand() * Mathf.PI * 2f;
                        float shade = 0.58f + Rand() * 0.45f, hue = (Rand() - 0.5f) * 0.1f;
                        var colour = new Color(shade * (0.92f + hue), shade, shade * (0.82f - hue));
                        float length = 20f + Rand() * 16f;
                        Blob(size / 2f + Mathf.Cos(a) * r, size / 2f + Mathf.Sin(a) * r, length, length * 0.45f, a + (Rand() - 0.5f) * 1.4f, colour);
                    }
                }
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
            }
            // Keep the cut-out leaves as dense in the small mipmaps (far away) as up close.
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && !importer.mipMapsPreserveCoverage)
            {
                importer.alphaIsTransparency = true;
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = 0.45f;
                importer.SaveAndReimport();
            }
        }

        static Texture2D LoadTexture(string name)
        {
            foreach (string ext in new[] { ".jpg", ".png" })
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PbrTextures + "/" + name + ext);
                if (texture != null) return texture;
            }
            return null;
        }

        // A tileable texture of overlapping leaves (and its normal map) for the tree crowns: each leaf a shaded
        // ellipse with a midrib, later leaves lying on top of earlier ones. Pale, so each tree can tint it.
        static void CreateFoliageTextures()
        {
            string diffPath = PbrTextures + "/foliage_diff.png", norPath = PbrTextures + "/foliage_nor.png";
            if (File.Exists(diffPath) && File.Exists(norPath)) return;
            const int size = 512, leaves = 2600;
            var height = new float[size * size];
            var albedo = new Color[size * size];
            for (int i = 0; i < albedo.Length; i++) albedo[i] = new Color(0.32f, 0.35f, 0.30f);
            var rng = new System.Random(3);
            for (int i = 0; i < leaves; i++)
            {
                float cx = (float)rng.NextDouble() * size, cy = (float)rng.NextDouble() * size;
                float length = 9f + (float)rng.NextDouble() * 12f, width = length * (0.4f + (float)rng.NextDouble() * 0.15f);
                float angle = (float)rng.NextDouble() * Mathf.PI, ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                float shade = 0.62f + (float)rng.NextDouble() * 0.42f, hue = ((float)rng.NextDouble() - 0.5f) * 0.12f;
                var colour = new Color(shade * (0.93f + hue), shade, shade * (0.84f - hue));
                float top = i / (float)leaves;
                int reach = Mathf.CeilToInt(length);
                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        float u = (dx * ca + dy * sa) / length, v = (-dx * sa + dy * ca) / width;
                        float d = u * u + v * v;
                        if (d >= 1f) continue;
                        int px = ((Mathf.RoundToInt(cx) + dx) % size + size) % size;
                        int py = ((Mathf.RoundToInt(cy) + dy) % size + size) % size;
                        int k = py * size + px;
                        float h = top + 0.25f * (1f - d);
                        if (h <= height[k]) continue;
                        height[k] = h;
                        float light = 0.82f + 0.18f * (1f - d) + 0.08f * u; // lighter towards the tip
                        albedo[k] = colour * (Mathf.Abs(v) < 0.09f ? light * 0.86f : light);
                    }
                }
            }
            var normals = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float hx = height[y * size + (x + 1) % size] - height[y * size + (x + size - 1) % size];
                    float hy = height[((y + 1) % size) * size + x] - height[((y + size - 1) % size) * size + x];
                    var n = new Vector3(-hx * 4f, -hy * 4f, 1f).normalized;
                    normals[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }
            WritePng(diffPath, albedo, size);
            WritePng(norPath, normals, size);
            AssetDatabase.ImportAsset(diffPath);
            AssetDatabase.ImportAsset(norPath);
        }

        static void WritePng(string path, Color[] pixels, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        // Saves the game's sun and environment profile into the main scene, strips components left over from URP,
        // and adds the map preview, so the Scene view shows the map as it looks in play.
        static void SetUpMainScene()
        {
            var entry = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled && File.Exists(s.path));
            if (entry == null) return;
            var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            if (Object.FindAnyObjectByType<MapPreview>() == null) new GameObject("Map Preview").AddComponent<MapPreview>();

            Light sun = Object.FindObjectsByType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            Atmosphere.ApplyLighting(sun);
            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera != null) Atmosphere.SetUpCamera(camera);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/PostProcessing.asset");
            var volumes = Object.FindObjectsByType<Volume>();
            if (volumes.Length == 0) volumes = new[] { new GameObject("Environment").AddComponent<Volume>() };
            foreach (var volume in volumes)
            {
                volume.isGlobal = true;
                volume.sharedProfile = profile;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Set up scene " + entry.path);
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

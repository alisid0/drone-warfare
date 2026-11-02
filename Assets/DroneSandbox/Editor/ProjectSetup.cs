using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox.EditorTools
{
    // One-time project configuration: player settings, URP shadow quality, fog stripping, the assets the game
    // loads from Resources (materials, water, post-processing profile, HDRI sky), and the main scene's lighting
    // and map preview. Safe to run again.
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
            KeepLinearFog();
            CreateBaseMaterials();
            CreateTerrainMaterial();
            CreatePostProcessingProfile();
            ImportTerrainTextures();
            CreateSkyMaterial();
            CreateWaterMaterial();
            CreateDecalMaterial();
            CreatePbrMaterials();
            AssetDatabase.SaveAssets();
            SetUpMainScene();

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
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            // In the editor the terrain inspector sets this from the layers; a terrain built in a player never
            // does. Setting it on the asset turns on the layers' normal maps and keeps that shader variant.
            material.EnableKeyword("_NORMALMAP");
            // The terrain draws without instancing (see Ground.Build), so leave the instanced-only options off.
            material.SetFloat("_EnableInstancedPerPixelNormal", 0f);
            material.DisableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
        }

        // Saves the post-processing grade as an asset. URP strips post-processing shader variants that no saved
        // profile uses, so a profile made only at runtime could lose effects in player builds. Existing assets are
        // left alone, so tweaks made in the Inspector survive; delete the asset to get the defaults back.
        static void CreatePostProcessingProfile()
        {
            string path = Root + "/PostProcessing.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) return;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            Atmosphere.ConfigureProfile(profile);
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
        }

        // The game turns on linear fog at runtime, but the scene has none, so "Automatic" fog stripping would
        // drop the fog shader variants from builds. Keep linear fog explicitly.
        static void KeepLinearFog()
        {
            var settings = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SetInt(settings, "m_FogStripping", 1); // custom
            SetBool(settings, "m_FogKeepLinear", true);
            SetBool(settings, "m_FogKeepExp", false);
            SetBool(settings, "m_FogKeepExp2", false);
            settings.ApplyModifiedPropertiesWithoutUndo();
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

        // Template for painted emblems and flags (Factions): URP Lit with alpha cut-out, so the emblem's shape
        // is cut from its square. The game copies it and sets the texture.
        static void CreateDecalMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            EnsureMaterialsFolder();
            string path = Root + "/Materials/Decal.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Smoothness", 0.25f);
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
        }

        // The river's material: the DroneSandbox/River water shader with a generated ripple normal map.
        static void CreateWaterMaterial()
        {
            var shader = Shader.Find("DroneSandbox/River");
            if (shader == null) return;
            EnsureMaterialsFolder();
            string texturePath = Root + "/Textures/RiverRipples.png";
            if (!File.Exists(texturePath))
            {
                File.WriteAllBytes(texturePath, RipplesPng());
                AssetDatabase.ImportAsset(texturePath);
            }
            if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            string path = Root + "/Materials/Water.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_RippleMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            // A calm, murky lowland river.
            material.SetFloat("_RippleSize", 7f);
            material.SetFloat("_RippleStrength", 0.35f);
            material.SetFloat("_FlowSpeed", 0.4f);
            material.SetColor("_ShallowColor", new Color(0.70f, 0.72f, 0.58f));
            material.SetColor("_DeepColor", new Color(0.085f, 0.13f, 0.10f));
            material.SetFloat("_Clarity", 1.1f);
            material.SetFloat("_Smoothness", 0.9f);
            material.SetFloat("_Refraction", 0.025f);
            material.SetFloat("_ShoreFade", 0.3f);
            EditorUtility.SetDirty(material);
        }

        // Tileable, irregular ripples: several octaves of smooth noise (each octave a whole number of cells
        // across, so the tile wraps), with the small ripples stretched a little across the flow. Turned into
        // a normal map.
        static byte[] RipplesPng()
        {
            const int size = 512;
            var rng = new System.Random(5);
            var octaves = new List<(int cells, float amp, float[] lattice)>();
            foreach (var (cells, amp) in new[] { (4, 1f), (8, 0.55f), (16, 0.3f), (32, 0.16f), (64, 0.08f) })
            {
                var lattice = new float[cells * cells];
                for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rng.NextDouble();
                octaves.Add((cells, amp, lattice));
            }
            var heights = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float h = 0f;
                    foreach (var o in octaves)
                    {
                        float gx = x * (float)o.cells / size, gy = y * (float)o.cells / size;
                        int x0 = Mathf.FloorToInt(gx), y0 = Mathf.FloorToInt(gy);
                        float fx = gx - x0, fy = gy - y0;
                        fx = fx * fx * (3f - 2f * fx);
                        fy = fy * fy * (3f - 2f * fy);
                        float At(int ix, int iy) => o.lattice[(iy % o.cells) * o.cells + ix % o.cells];
                        float a = Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), fx), b = Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx);
                        h += o.amp * Mathf.Lerp(a, b, fy);
                    }
                    heights[y * size + x] = h;
                }
            }
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = heights[y * size + (x + 1) % size] - heights[y * size + (x + size - 1) % size];
                    float dy = heights[((y + 1) % size) * size + x] - heights[((y + size - 1) % size) * size + x];
                    var n = new Vector3(-dx * 14f, -dy * 9f, 1f).normalized;
                    pixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
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
                var wanted = Path.GetFileNameWithoutExtension(path).EndsWith("_nor") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType == wanted && importer.anisoLevel == 8) continue; // already set up
                importer.textureType = wanted;
                importer.maxTextureSize = 1024;
                importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
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
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetTexture("_BaseMap", albedo);
                material.SetColor("_BaseColor", m.tint);
                material.SetTextureScale("_BaseMap", Vector2.one / m.tile);
                material.SetFloat("_Smoothness", m.smooth);
                material.SetFloat("_Metallic", m.metal);
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.SetFloat("_BumpScale", 1f);
                    material.EnableKeyword("_NORMALMAP");
                }
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
            }
            CreateLeafCardMaterials(shader, folder);
        }

        // Leaf cards (see leaf_cards in Tools/Blender/common.py): Blender material name -> leaf-cluster texture, tint.
        static readonly (string material, string texture, Color tint)[] LeafCardMaterials =
        {
            ("OakCards", "leafcard_broad", new Color(0.50f, 0.66f, 0.30f)),
            ("BirchCards", "leafcard_broad", new Color(0.62f, 0.80f, 0.36f)),
            ("PoplarCards", "leafcard_broad", new Color(0.46f, 0.64f, 0.30f)),
            ("BushCards", "leafcard_broad", new Color(0.50f, 0.66f, 0.28f)),
            ("PineCards", "leafcard_needle", new Color(0.36f, 0.50f, 0.32f)),
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
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", m.tint);
                material.SetTextureScale("_BaseMap", Vector2.one);
                material.SetFloat("_Smoothness", 0.12f);
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.45f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_Cull", 0f); // both sides
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
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

        // Saves the game's sky, sun and colour grade into the main scene and adds the map preview, so the
        // Scene view shows the map as it looks in play.
        static void SetUpMainScene()
        {
            var entry = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled && File.Exists(s.path));
            if (entry == null) return;
            var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
            if (Object.FindAnyObjectByType<MapPreview>() == null) new GameObject("Map Preview").AddComponent<MapPreview>();

            Light sun = Object.FindObjectsByType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            Atmosphere.ApplyLighting(sun);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/PostProcessing.asset");
            if (profile != null)
                foreach (var volume in Object.FindObjectsByType<Volume>()) volume.sharedProfile = profile;

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

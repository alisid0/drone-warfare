using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DroneSandbox.EditorTools
{
    // Generate a small adaptation of this project's installed HDRP Lit, without installing or forking a package.
    public static class FarDetailShaderSetup
    {
        const string Root = "Assets/DroneSandbox/Resources";
        const string Lit = "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/";
        const string Shaders = Root + "/Shaders/";

        // Validate ordinary Lit first, then substitute its generated surface hook without resetting its keywords.
        public static void Build()
        {
            Generate();
            var albedo = Array("GroundAlbedo", false, 10);
            var normal = Array("GroundNormal", true, 5);
            for (int layer = 0; layer < 10; layer++)
            {
                Texture2D source = layer < 5 ? Resources.Load<Texture2D>("Textures/Terrain/" + FarDetailPalette.Photos[layer] + "_diff")
                    : Farmland.CropTexture((Crop)(layer - 5));
                if (source == null) throw new InvalidOperationException("Missing ground texture at layer " + layer);
                var pixels = Read(source, false);
                if (layer < 5)
                {
                    Vector3 tint = FarDetailPalette.Tints[layer];
                    for (int k = 0; k < pixels.Length; k++)
                    {
                        var c = pixels[k].linear;
                        pixels[k] = new Color(c.r * tint.x, c.g * tint.y, c.b * tint.z, 1f).gamma;
                    }
                }
                CheckMean(pixels, layer);
                albedo.SetPixels(pixels, layer);
                if (layer >= 5) continue;
                // Read the source RGB normal, before Unity's platform-specific DXT/BC5 swizzle.
                string path = AssetDatabase.GetAssetPath(Resources.Load<Texture2D>("Textures/Terrain/" + FarDetailPalette.Photos[layer] + "_nor"));
                var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!ImageConversion.LoadImage(raw, File.ReadAllBytes(path))) throw new InvalidOperationException("Invalid normal map " + path);
                normal.SetPixels(Read(raw, true), layer);
                Object.DestroyImmediate(raw);
            }
            albedo.Apply(true, false); normal.Apply(true, false);
            EditorUtility.SetDirty(albedo); EditorUtility.SetDirty(normal);
            var material = HdrpSetup.LoadOrCreate(Root + "/Materials/FarDetailLit.mat", HdrpSetup.Lit);
            HdrpSetup.Surface(material, 0f, 0f);
            HdrpSetup.Finish(material, instancing: false);
            // No validation after this point: HDRP does not recognize the custom shader's name.
            material.shader = AssetDatabase.LoadAssetAtPath<Shader>(Shaders + "FarDetailLit.shader");
            if (material.shader == null) throw new InvalidOperationException("Ground detail shader failed to import.");
            if (ShaderUtil.ShaderHasError(material.shader))
            {
                // Say what the compiler said: a bare "failed" left nothing to go on in the batch log.
                var report = new StringBuilder("Ground detail shader has errors:");
                foreach (var m in ShaderUtil.GetShaderMessages(material.shader))
                    report.Append("\n  ").Append(m.severity).Append(' ').Append(m.file).Append(':').Append(m.line).Append(' ').Append(m.message).Append(' ').Append(m.messageDetails);
                throw new InvalidOperationException(report.ToString());
            }
            material.SetTexture("_FarGroundAlbedo", albedo); material.SetTexture("_FarGroundNormal", normal);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        // Catch colour-space/import changes before baking an array whose mip colour disagrees with the world picture.
        static void CheckMean(Color[] pixels, int layer)
        {
            double r = 0, g = 0, b = 0;
            foreach (var pixel in pixels) { var c = pixel.linear; r += c.r; g += c.g; b += c.b; }
            Vector3 wanted = FarDetailPalette.Means[layer];
            double error = Math.Max(Math.Abs(r / pixels.Length / wanted.x - 1),
                Math.Max(Math.Abs(g / pixels.Length / wanted.y - 1), Math.Abs(b / pixels.Length / wanted.z - 1)));
            if (error > .05) throw new InvalidOperationException("Ground layer " + layer + " changed its linear mean by " + (error * 100).ToString("F1") + "%; rerun the palette harness.");
        }

        // Text replacement is deliberately guarded: an HDRP upgrade must fail setup rather than omit our hook.
        static void Generate()
        {
            Directory.CreateDirectory(Shaders);
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(Lit + "Lit.shader");
            if (package == null) throw new InvalidOperationException("HDRP package location could not be resolved.");
            string source = Path.Combine(package.resolvedPath, "Runtime/Material/Lit");
            string shader = File.ReadAllText(Path.Combine(source, "Lit.shader")), data = File.ReadAllText(Path.Combine(source, "LitData.hlsl"));
            const string call = "float alpha = GetSurfaceData(input, layerTexCoord, surfaceData, normalTS, bentNormalTS);";
            if (!data.Contains(call)) throw new InvalidOperationException("HDRP Lit surface signature changed; review FarDetailShaderSetup.");
            data = data.Replace("void GetSurfaceAndBuiltinData(", "#include \"FarDetailSurface.hlsl\"\n\nvoid GetSurfaceAndBuiltinData(");
            data = data.Replace(call, call + "\n#if defined(VARYINGS_NEED_TEXCOORD0) && defined(VARYINGS_NEED_TANGENT_TO_WORLD)\n    FarDetailSurface(input, surfaceData, normalTS);\n#endif");
            // Streaming tiles are raster terrain. Ray-tracing variants cannot use the screen derivatives above.
            int second = shader.IndexOf("    SubShader", shader.IndexOf("    SubShader", StringComparison.Ordinal) + 1, StringComparison.Ordinal);
            if (second < 0) throw new InvalidOperationException("HDRP Lit pass layout changed; review the raster shader adapter.");
            shader = shader.Substring(0, second) + "    CustomEditor \"Rendering.HighDefinition.LitGUI\"\n}\n";
            shader = shader.Replace("Shader \"HDRP/Lit\"", "Shader \"DroneSandbox/GroundDetail\"");
            int properties = shader.IndexOf('{', shader.IndexOf("Properties", StringComparison.Ordinal)) + 1;
            shader = shader.Insert(properties, "\n        _FarGroundControl(\"Surface and direction\", 2D) = \"black\" {}\n        _FarGroundAlbedo(\"Shared ground albedo\", 2DArray) = \"\" {}\n        _FarGroundNormal(\"Shared ground normals\", 2DArray) = \"\" {}\n");
            shader = shader.Replace("HLSLPROGRAM", "HLSLPROGRAM\n            #pragma multi_compile_local_fragment _ _FarRegionChunk");
            shader = shader.Replace(Lit + "LitData.hlsl", Shaders + "FarDetailLitData.hlsl");
            const string notice = "// Generated from the installed HDRP 17.6 Lit by FarDetailShaderSetup. Unity source: see ThirdPartyNotices.txt.\n";
            Write(Shaders + "FarDetailLit.shader", notice + shader);
            Write(Shaders + "FarDetailLitData.hlsl", notice + data);
            var text = new StringBuilder("// Generated from FarDetailPalette; linear means, in the same order as the shared texture arrays.\nstatic const float GroundMetres[10] = {7,6,5,12,8,8,8,8,8,8};\nstatic const float3 GroundMean[10] = {\n");
            foreach (var mean in FarDetailPalette.Means)
                text.AppendFormat(CultureInfo.InvariantCulture, "float3({0:R},{1:R},{2:R}),\n", mean.x, mean.y, mean.z);
            text.Append("};\n"); Write(Shaders + "FarDetailPalette.hlsl", text.ToString());
            AssetDatabase.Refresh();
        }

        // Reuse assets on repeated setup; no array allocations occur while flying.
        static Texture2DArray Array(string name, bool linear, int count)
        {
            string path = Root + "/Textures/" + name + ".asset";
            var array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
            if (array != null) return array;
            array = new Texture2DArray(512, 512, count, TextureFormat.RGBA32, true, linear)
            { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            AssetDatabase.CreateAsset(array, path);
            return array;
        }

        // GPU resampling handles non-readable imported textures and Farmland's generated crop maps alike.
        static Color[] Read(Texture source, bool linear)
        {
            var target = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var old = RenderTexture.active; bool oldWrite = GL.sRGBWrite;
            Texture2D copy = null;
            try
            {
                GL.sRGBWrite = !linear; Graphics.Blit(source, target); RenderTexture.active = target;
                copy = new Texture2D(512, 512, TextureFormat.RGBA32, false, linear);
                copy.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); copy.Apply(false, false);
                return copy.GetPixels();
            }
            finally { RenderTexture.active = old; GL.sRGBWrite = oldWrite; RenderTexture.ReleaseTemporary(target); if (copy != null) Object.DestroyImmediate(copy); }
        }

        // Stable CRLF outputs avoid regenerating imported shaders when setup has no material changes.
        static void Write(string path, string text)
        {
            text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
        }
    }
}

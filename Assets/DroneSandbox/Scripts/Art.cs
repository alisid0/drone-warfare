using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Placeholder visuals built from Unity primitives, so the sandbox needs no art assets.
    // Replace these with real models once the art style is picked.
    public static class Art
    {
        public static readonly Color PlayerBlue = new Color(0.30f, 0.75f, 1.00f);
        public static readonly Color PlayerDeepBlue = new Color(0.20f, 0.45f, 0.90f);
        public static readonly Color KamikazeOrange = new Color(1.00f, 0.55f, 0.15f);
        public static readonly Color EnemyOlive = new Color(0.40f, 0.43f, 0.27f);
        public static readonly Color EnemyOliveDark = new Color(0.30f, 0.32f, 0.20f);
        public static readonly Color EnemyRed = new Color(0.90f, 0.20f, 0.15f);
        public static readonly Color Dark = new Color(0.12f, 0.13f, 0.14f);
        public static readonly Color Metal = new Color(0.75f, 0.78f, 0.80f);

        static Material template, glowTemplate;
        static readonly Dictionary<Color, Material> matte = new Dictionary<Color, Material>();
        static readonly Dictionary<Color, Material> glowing = new Dictionary<Color, Material>();

        // A new material using the project's lit shader, so colours show up correctly
        // (no pink "missing shader" boxes).
        public static Material NewMaterial(Color color, bool glow = false)
        {
            if (template == null) template = LoadTemplate("Materials/BaseLit");
            if (glowTemplate == null) glowTemplate = LoadTemplate("Materials/BaseLitGlow");
            var m = new Material(glow ? glowTemplate : template);
            m.color = color;
            m.SetFloat("_Glossiness", 0.15f); // Built-in Standard shader
            m.SetFloat("_Smoothness", 0.15f); // URP Lit shader
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color);
            }
            return m;
        }

        // URP Lit material assets saved by ProjectSetup. Loading them from Resources keeps the shader (and its
        // emission variant) in player builds, where a primitive's default material is the unsupported
        // Built-in one. Without them, fall back to whatever material a new primitive gets.
        static Material LoadTemplate(string path)
        {
            var m = Resources.Load<Material>(path);
            if (m != null) return m;
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m = probe.GetComponent<Renderer>().sharedMaterial;
            Object.Destroy(probe);
            if (m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            return new Material(shader);
        }

        public static Material Shared(Color color, bool glow = false)
        {
            var cache = glow ? glowing : matte;
            if (!cache.TryGetValue(color, out var m) || m == null)
            {
                m = NewMaterial(color, glow);
                cache[color] = m;
            }
            return m;
        }

        public static GameObject Part(PrimitiveType shape, Transform parent, Vector3 localPosition, Vector3 localScale,
            Color color, Vector3 localEuler = default, bool glow = false)
        {
            var go = GameObject.CreatePrimitive(shape);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = Shared(color, glow);
            return go;
        }

        // Instantiates Resources/Models/<name> (e.g. a Blender FBX) under parent, or returns null
        // when there's no such model, so callers can fall back to primitive shapes.
        public static GameObject TryModel(string name, Transform parent, float scale = 1f)
        {
            var prefab = Resources.Load<GameObject>("Models/" + name);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            go.transform.localScale = Vector3.one * scale;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            return go;
        }

        // Dark olive ground with a faint 10 m grid, like a tactical map.
        public static Material GroundMaterial(float tiles)
        {
            const int size = 128;
            var fill = new Color(0.22f, 0.26f, 0.19f);
            var line = new Color(0.31f, 0.36f, 0.27f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool onLine = x < 2 || y < 2;
                    float grain = 0.96f + Random.value * 0.06f;
                    pixels[y * size + x] = onLine ? line : fill * grain;
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.SetPixels(pixels);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.anisoLevel = 4;

            var m = NewMaterial(Color.white);
            m.mainTexture = tex;
            m.mainTextureScale = new Vector2(tiles, tiles);
            return m;
        }
    }
}

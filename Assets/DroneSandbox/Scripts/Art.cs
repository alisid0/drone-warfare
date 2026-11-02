using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Colours, materials and model loading. Real models come from Resources/Models; the primitive
    // helpers here build stand-ins for anything that has no model yet.
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
            Discard(probe);
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
            Discard(go.GetComponent<Collider>());
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
            foreach (var c in go.GetComponentsInChildren<Collider>()) Discard(c);
            UsePhotoMaterials(go);
            return go;
        }

        static Dictionary<string, Material> photoMaterials;

        // Swaps a model's flat-coloured materials for the photo-textured ones of the same name in
        // Resources/Materials/PBR (made by Drone Sandbox > Configure Project). Models get real-scale UVs from
        // the Blender scripts, so the textures sit at the right size.
        static void UsePhotoMaterials(GameObject go)
        {
            if (photoMaterials == null)
            {
                photoMaterials = new Dictionary<string, Material>();
                foreach (var m in Resources.LoadAll<Material>("Materials/PBR")) photoMaterials[m.name] = m;
            }
            if (photoMaterials.Count == 0) return;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    if (materials[i].name == "WindowGlass" || materials[i].name == "Window")
                    {
                        materials[i] = DayNight.WindowMaterial; // lit up after dark
                        changed = true;
                    }
                    else if (photoMaterials.TryGetValue(materials[i].name, out var photo))
                    {
                        materials[i] = photo;
                        changed = true;
                    }
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        // Destroys something both in Play mode and in the editor's map preview (which can't wait for Destroy).
        public static void Discard(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}

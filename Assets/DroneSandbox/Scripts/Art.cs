using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

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

        // A new material using the project's lit shader (HDRP Lit), so colours show up correctly
        // (no pink "missing shader" boxes).
        public static Material NewMaterial(Color color, bool glow = false)
        {
            if (template == null) template = LoadTemplate("Materials/BaseLit");
            if (glowTemplate == null) glowTemplate = LoadTemplate("Materials/BaseLitGlow");
            var m = new Material(glow ? glowTemplate : template);
            m.color = color;
            m.SetFloat("_Smoothness", 0.15f);
            if (glow) Glow(m, color);
            return m;
        }

        // Makes a material glow this colour. The glow template ignores the camera's exposure, so a glow reads the
        // same at noon and at midnight (muzzle flashes, lit windows, the thermal view's hot vehicles).
        public static void Glow(Material m, Color color) => m.SetColor("_EmissiveColor", color.linear * 1.5f);

        // HDRP Lit material assets saved by ProjectSetup. Loading them from Resources keeps the shader in player
        // builds. Without them, fall back to whatever material a new primitive gets.
        static Material LoadTemplate(string path)
        {
            var m = Resources.Load<Material>(path);
            if (m != null) return m;
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m = probe.GetComponent<Renderer>().sharedMaterial;
            Discard(probe);
            if (m != null) return m;
            m = new Material(Shader.Find("HDRP/Lit"));
            HDMaterial.ValidateMaterial(m);
            return m;
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
            var prefab = Resources.Load<GameObject>("Models/" + Variant(name, parent != null ? parent.position : Vector3.zero));
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            go.transform.localScale = Vector3.one * scale;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Discard(c);
            UsePhotoMaterials(go);
            var lods = go.GetComponent<LODGroup>();
            if (lods != null) TuneLods(lods, name.StartsWith("Scenery_Spruce"));
            return go;
        }

        static readonly Dictionary<string, int> variantCounts = new Dictionary<string, int>();

        // Some models come in several versions, "<name>", "<name>_V2", "<name>_V3".. (the trees, so a wood isn't
        // the same tree over and over). Picks one by where it stands, so the same place always gets the same one.
        static string Variant(string name, Vector3 at)
        {
            if (!variantCounts.TryGetValue(name, out int extra))
            {
                while (Resources.Load<GameObject>("Models/" + name + "_V" + (extra + 2)) != null) extra++;
                variantCounts[name] = extra;
            }
            if (extra == 0) return name;
            int hash = (Mathf.FloorToInt(at.x * 3.7f) * 73856093) ^ (Mathf.FloorToInt(at.z * 3.7f) * 19349663);
            int pick = (hash & 0x7fffffff) % (extra + 1);
            return pick == 0 ? name : name + "_V" + (pick + 1);
        }

        static readonly Dictionary<string, int> rendererCounts = new Dictionary<string, int>();

        // Counts all LOD renderers of the same variant TryModel will use, so decoration budgets include hidden
        // levels too. Missing models cost one renderer for World's primitive fallback. Cached once per asset.
        public static int ModelRendererCount(string name, Vector3 at)
        {
            string variant = Variant(name, at);
            if (!rendererCounts.TryGetValue(variant, out int count))
            {
                var prefab = Resources.Load<GameObject>("Models/" + variant);
                count = prefab != null ? Mathf.Max(1, prefab.GetComponentsInChildren<Renderer>(true).Length) : 1;
                rendererCounts[variant] = count;
            }
            return count;
        }

        // The existing trees have two meshes and a billboard, not a third mesh. At 45 degrees and lodBias 1,
        // keep full detail to 65 m, the lighter mesh to 250 m, then the billboard to 2 km. Spruce's lighter mesh
        // still has up to 12k triangles, so it switches at 200 m. Derive screen heights from each group's world size
        // to keep variants and differently sized trees within the same distance budget.
        // Scans retain screen-size culling, since their smallest leaves and stones disappear much nearer.
        static void TuneLods(LODGroup group, bool heavyTree)
        {
            var lods = group.GetLODs();
            if (lods.Length == 3)
            {
                Vector3 scale = group.transform.lossyScale;
                float size = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                float projected = size / (2f * Mathf.Tan(Scale.CameraFov * 0.5f * Mathf.Deg2Rad));
                lods[0].screenRelativeTransitionHeight = Mathf.Clamp(projected / 65f, 0.002f, 0.9f);
                lods[1].screenRelativeTransitionHeight = Mathf.Min(lods[0].screenRelativeTransitionHeight * 0.9f, projected / (heavyTree ? 200f : 250f));
                lods[2].screenRelativeTransitionHeight = Mathf.Min(lods[1].screenRelativeTransitionHeight * 0.9f, projected / Scale.D(480f));
                for (int tier = 1; tier < lods.Length; tier++)
                    foreach (var renderer in lods[tier].renderers)
                        if (renderer != null) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else if (lods.Length == 2)
            {
                lods[0].screenRelativeTransitionHeight = 0.15f;
                lods[1].screenRelativeTransitionHeight = 0.008f;
            }
            else if (lods.Length == 1) lods[0].screenRelativeTransitionHeight = 0.01f;
            group.SetLODs(lods);
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

        // Gives every part of a model wearing the photo material `from` the photo material `to` instead.
        public static void Swap(GameObject go, string from, string to)
        {
            if (go == null || from == to || photoMaterials == null || !photoMaterials.TryGetValue(from, out var a) || !photoMaterials.TryGetValue(to, out var b)) return;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] == a)
                    {
                        materials[i] = b;
                        changed = true;
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

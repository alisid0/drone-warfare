using UnityEngine;

namespace DroneSandbox
{
    // Forest draws use serialized variants and fail back to the project's photo Lit materials if fading is unavailable.
    public static class ForestMaterials
    {
        static readonly int Enabled = Shader.PropertyToID("_ForestEnabled"), Tier = Shader.PropertyToID("_ForestTier"),
            Build = Shader.PropertyToID("_ForestBuild"), CameraPosition = Shader.PropertyToID("_ForestCamera"),
            Distances = Shader.PropertyToID("_ForestDistances"), Selection = Shader.PropertyToID("_ForestSelection"),
            Projection = Shader.PropertyToID("_ForestProjection"), Crown = Shader.PropertyToID("_ForestCrown"), Radius = Shader.PropertyToID("_ForestRadius"),
            Origin = Shader.PropertyToID("_ForestOrigin"), Viewport = Shader.PropertyToID("_ForestViewport");
        static readonly System.Collections.Generic.HashSet<string> reported = new System.Collections.Generic.HashSet<string>();
        public static int RemappedCount { get; private set; }
        public static string LastFallback { get; private set; } = "none";
        public static bool ForceFallback;
        public static int FallbackCount { get; private set; }

        // Clone an included asset rather than swapping a runtime shader and hoping its variants survived stripping.
        public static Material Create(string name, Material imported, int tier)
        {
            string importedName = name;
            var source = Resources.Load<Material>("Materials/Forest/" + name);
            // Blender can suffix a shared bark on a later export; only accept a matching included forest asset.
            int dot = name.LastIndexOf('.');
            if (source == null && dot > 0 && name.Length - dot == 4 && int.TryParse(name.Substring(dot + 1), out _))
            {
                var canonical = Resources.Load<Material>("Materials/Forest/" + name.Substring(0, dot));
                if (canonical != null)
                {
                    name = name.Substring(0, dot); source = canonical; RemappedCount++;
                    if (reported.Add("remap:" + importedName)) Debug.Log("Forest material remap: " + importedName + " -> " + name + " (Blender duplicate suffix)");
                }
            }
            var shader = Shader.Find("DroneSandbox/ForestLit");
            string reason = ForceFallback ? "forced for test" : source == null ? "missing Resources/Materials/Forest/" + importedName
                : shader == null ? "ForestLit shader not found" : !shader.isSupported ? "ForestLit unsupported on this graphics backend"
                : source.shader != shader ? "serialized shader mismatch: " + (source.shader != null ? source.shader.name : "null") : null;
            if (reason == null)
            {
                var material = new Material(source) { enableInstancing = true };
                if (Configure(material, tier, out reason)) return material;
                Art.Discard(material);
            }
            FallbackCount++;
            LastFallback = importedName + " tier " + tier + ": " + reason;
            if (reported.Add(LastFallback)) Debug.LogWarning("Forest material fallback: " + LastFallback + "; using HDRP/Lit");
            var photo = Resources.Load<Material>(name == "Canopy" ? "Materials/BaseLit" : "Materials/PBR/" + name);
            var fallback = photo != null ? new Material(photo)
                : imported != null && imported.shader != null && imported.shader.name == "HDRP/Lit" && imported.shader.isSupported
                    ? new Material(imported) : Art.NewMaterial(Color.white);
            fallback.enableInstancing = true;
            return fallback;
        }

        // Verify both material and draw-property storage before enabling a shader whose default is full coverage.
        static bool Configure(Material material, int tier, out string reason)
        {
            reason = null;
            foreach (string property in new[] { "_ForestEnabled", "_ForestTier", "_ForestBuild", "_ForestCamera", "_ForestDistances",
                "_ForestSelection", "_ForestProjection", "_ForestCrown", "_ForestRadius", "_ForestOrigin", "_ForestViewport" })
                if (!material.HasProperty(Shader.PropertyToID(property))) { reason = "missing property " + property; return false; }
            var camera = new Vector4(17f, 29f, 43f, 1f);
            var distances = new Vector4(25f, 83f, 333f, 700f);
            material.SetFloat(Tier, tier); material.SetFloat(Build, 1f); material.SetFloat(Enabled, 1f);
            material.SetVector(CameraPosition, camera); material.SetVector(Distances, distances);
            var probe = new MaterialPropertyBlock();
            SetCamera(probe, camera, distances, 1f);
            bool valid = material.GetFloat(Tier) == tier && material.GetFloat(Build) == 1f && material.GetFloat(Enabled) == 1f
                && material.GetVector(CameraPosition) == camera && material.GetVector(Distances) == distances
                && probe.GetVector(CameraPosition) == camera && probe.GetVector(Distances) == distances && probe.GetFloat(Build) == 1f;
            // If a render backend ignores the property block, the material defaults must draw woods without clipping.
            material.SetVector(CameraPosition, Vector4.zero); material.SetVector(Distances, Vector4.zero);
            if (!valid) reason = "material/property-block readback differs from written values";
            return valid;
        }

        // Explicit draws carry camera state in their own property block; they do not depend on the GPU Resident Drawer.
        public static void SetCamera(MaterialPropertyBlock properties, Vector4 camera, Vector4 distances, float build = 1f)
        {
            camera.w = 1f;
            properties.SetVector(CameraPosition, camera); properties.SetVector(Distances, distances);
            properties.SetFloat(Build, build);
        }

        // Per-part anchors reconstruct the actual crown from instanced transforms, including imported children.
        public static void SetShape(Material material, Vector3 crown, Vector3 radius)
        {
            if (material == null || !Fades(material)) return;
            material.SetVector(Crown, crown); material.SetVector(Radius, radius);
        }

        // Freeze projection with its completed CPU selection; camera motion cannot expose an unbudgeted tier.
        public static void SetProjection(MaterialPropertyBlock properties, Vector3 eye, Vector3 forward, float focal, float ready)
        {
            properties.SetVector(Selection, new Vector4(eye.x, eye.y, eye.z, ready));
            properties.SetVector(Projection, new Vector4(forward.x, forward.y, forward.z, focal));
            Vector2 shift = SunShift();
            properties.SetVector(Origin, new Vector4(FarTerrain.Origin.x, FarTerrain.Origin.y, shift.x, shift.y));
        }

        static int sunFrame = -1;
        static Vector2 sunShift;

        // How far a canopy crown's shadow falls sideways per metre of height, once per frame. Shadows shorten as cloud
        // dims the sun below half of noon light (DayNight scales its lux) and vanish under the moon or with the sun down.
        static Vector2 SunShift()
        {
            if (sunFrame == Time.frameCount) return sunShift;
            sunFrame = Time.frameCount;
            var sun = RenderSettings.sun;
            Vector3 travel = sun != null && sun.isActiveAndEnabled ? sun.transform.forward : Vector3.up;
            float strength = sun != null ? Mathf.Clamp01(sun.intensity / 50000f) : 0f;
            sunShift = travel.y < -0.05f ? Vector2.ClampMagnitude(new Vector2(travel.x, travel.z) / -travel.y, 4f) * strength : Vector2.zero;
            return sunShift;
        }

        // A frozen view may still be submitted during a turn; restrict its canopy hole to its original frustum.
        public static void SetViewport(MaterialPropertyBlock properties, Vector2 viewport)
            => properties.SetVector(Viewport, new Vector4(viewport.x, viewport.y, 0f, 0f));

        // CPU tier selection can omit overlap when a whole model is using the unfaded Lit fallback.
        public static bool Fades(Material material) => material.HasProperty(Enabled) && material.GetFloat(Enabled) == 1f;
    }
}

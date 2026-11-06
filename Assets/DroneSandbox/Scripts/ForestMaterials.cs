using UnityEngine;

namespace DroneSandbox
{
    // Forest draws use serialized variants and fail back to the project's photo Lit materials if fading is unavailable.
    public static class ForestMaterials
    {
        static readonly int Enabled = Shader.PropertyToID("_ForestEnabled"), Tier = Shader.PropertyToID("_ForestTier"),
            Build = Shader.PropertyToID("_ForestBuild"), CameraPosition = Shader.PropertyToID("_ForestCamera"),
            Distances = Shader.PropertyToID("_ForestDistances");
        public static bool ForceFallback;
        public static int FallbackCount { get; private set; }

        // Clone an included asset rather than swapping a runtime shader and hoping its variants survived stripping.
        public static Material Create(string name, Material imported, int tier)
        {
            var source = Resources.Load<Material>("Materials/Forest/" + name);
            var shader = Shader.Find("DroneSandbox/ForestLit");
            if (!ForceFallback && source != null && shader != null && shader.isSupported && source.shader == shader)
            {
                var material = new Material(source) { enableInstancing = true };
                if (Configure(material, tier)) return material;
                Art.Discard(material);
            }
            FallbackCount++;
            var photo = Resources.Load<Material>(name == "Canopy" ? "Materials/BaseLit" : "Materials/PBR/" + name);
            return photo != null ? new Material(photo) { enableInstancing = true }
                : imported != null && imported.shader != null && imported.shader.isSupported
                    ? new Material(imported) { enableInstancing = true } : Art.NewMaterial(Color.white);
        }

        // Verify both material and draw-property storage before enabling a shader whose default is full coverage.
        static bool Configure(Material material, int tier)
        {
            if (!material.HasProperty(Enabled) || !material.HasProperty(Tier) || !material.HasProperty(Build)
                || !material.HasProperty(CameraPosition) || !material.HasProperty(Distances)) return false;
            var camera = new Vector4(17f, 29f, 43f, 1f);
            var distances = new Vector4(45f, 125f, 350f, 700f);
            material.SetFloat(Tier, tier); material.SetFloat(Build, 1f); material.SetFloat(Enabled, 1f);
            material.SetVector(CameraPosition, camera); material.SetVector(Distances, distances);
            var probe = new MaterialPropertyBlock();
            SetCamera(probe, camera, distances, 1f);
            bool valid = material.GetFloat(Tier) == tier && material.GetFloat(Build) == 1f && material.GetFloat(Enabled) == 1f
                && material.GetVector(CameraPosition) == camera && material.GetVector(Distances) == distances
                && probe.GetVector(CameraPosition) == camera && probe.GetVector(Distances) == distances && probe.GetFloat(Build) == 1f;
            // If a render backend ignores the property block, the material defaults must draw woods without clipping.
            material.SetVector(CameraPosition, Vector4.zero); material.SetVector(Distances, Vector4.zero);
            return valid;
        }

        // Explicit draws carry camera state in their own property block; they do not depend on the GPU Resident Drawer.
        public static void SetCamera(MaterialPropertyBlock properties, Vector4 camera, Vector4 distances, float build = 1f)
        {
            camera.w = 1f;
            properties.SetVector(CameraPosition, camera); properties.SetVector(Distances, distances);
            properties.SetFloat(Build, build);
        }

        // CPU tier selection can omit overlap when a whole model is using the unfaded Lit fallback.
        public static bool Fades(Material material) => material.HasProperty(Enabled) && material.GetFloat(Enabled) == 1f;
    }
}

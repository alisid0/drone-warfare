using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DroneSandbox
{
    // Sky, sun, haze and a filmic post-processing stack (URP), for the realistic look.
    public static class Atmosphere
    {
        public static readonly Color HazeColor = new Color(0.63f, 0.68f, 0.72f);

        public static void Setup(Camera cam, Light sun)
        {
            ApplyLighting(sun);
            cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = HazeColor;

            cam.farClipPlane = 900f;
            cam.allowHDR = true;
            var cameraData = cam.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.renderShadows = true;

            // Our grade replaces any volume the scene came with (the URP template adds its own).
            foreach (var other in Object.FindObjectsByType<Volume>()) other.enabled = false;

            // Use the saved profile (made by Drone Sandbox > Configure Project, and editable in the Inspector).
            // URP keeps only the post-processing shader variants that saved profiles use, so building the
            // profile here at runtime alone could lose effects such as bloom in a player build.
            var profile = Resources.Load<VolumeProfile>("PostProcessing");
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                ConfigureProfile(profile);
            }
            var volume = new GameObject("Post Processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        // Sky, sun, ambient light and haze. Also saved into the scene by Drone Sandbox > Configure Project, so the
        // editor's Scene view looks like the game.
        public static void ApplyLighting(Light sun)
        {
            // Sky: Resources/Sky/SkyMaterial (an HDRI panorama, made by the editor setup) if there is one,
            // otherwise Unity's procedural sky.
            var sky = Resources.Load<Material>("Sky/SkyMaterial");
            if (sky == null)
            {
                var shader = Shader.Find("Skybox/Procedural");
                if (shader != null)
                {
                    sky = new Material(shader);
                    sky.SetFloat("_SunSize", 0.03f);
                    sky.SetFloat("_AtmosphereThickness", 0.9f);
                    sky.SetFloat("_Exposure", 1.15f);
                    sky.SetColor("_GroundColor", new Color(0.36f, 0.38f, 0.35f));
                }
            }
            if (sky != null) RenderSettings.skybox = sky;

            // Warm afternoon sun, fairly low, for long soft shadows.
            sun.transform.rotation = Quaternion.Euler(36f, -35f, 0f);
            sun.color = new Color(1f, 0.93f, 0.80f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.2f; // a bright summer sky lifts the shadows
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = HazeColor;
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 560f;
        }

        // The filmic grade: ACES tonemapping, a little extra contrast, natural summer colour, soft bloom, vignette.
        public static void ConfigureProfile(VolumeProfile profile)
        {
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.3f);
            color.contrast.Override(12f);
            color.saturation.Override(4f);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.5f);
            bloom.threshold.Override(1.0f);
            bloom.highQualityFiltering.Override(true);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            var whiteBalance = profile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(4f);
        }
    }
}

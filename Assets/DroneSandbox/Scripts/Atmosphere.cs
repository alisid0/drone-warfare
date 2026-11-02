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
            if (sky != null)
            {
                RenderSettings.skybox = sky;
                cam.clearFlags = CameraClearFlags.Skybox;
            }
            else
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = HazeColor;
            }

            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            DynamicGI.UpdateEnvironment();

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = HazeColor;
            RenderSettings.fogStartDistance = 140f;
            RenderSettings.fogEndDistance = 520f;

            cam.farClipPlane = 900f;
            var cameraData = cam.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
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

        // The filmic grade: ACES tonemapping, a little extra contrast, slightly muted colour, soft bloom, vignette.
        public static void ConfigureProfile(VolumeProfile profile)
        {
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.25f);
            color.contrast.Override(14f);
            color.saturation.Override(-14f);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.5f);
            bloom.threshold.Override(1.0f);
            bloom.highQualityFiltering.Override(true);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            var whiteBalance = profile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(6f);
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // How the world is lit and seen, in HDRP: a physically based sky (the atmosphere itself colours sunrise and
    // sunset, with stars at night), volumetric clouds, height fog with volumetric light (sun shafts through the
    // haze and mist), shadows out to the hills, ambient occlusion, reflections, automatic exposure and a filmic
    // grade. The profile is saved by Drone Sandbox > Configure Project (Resources/PostProcessing) and rebuilt here
    // if it's missing. DayNight moves the sun.
    public static class Atmosphere
    {
        public const float NoonLux = 100000f; // the sun at midday, in lux

        public static void Setup(Camera cam, Light sun)
        {
            ApplyLighting(sun);
            SetUpCamera(cam);
            // Our profile replaces any volume the scene came with.
            foreach (var other in Object.FindObjectsByType<Volume>()) other.enabled = false;
            var profile = Resources.Load<VolumeProfile>("PostProcessing");
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                ConfigureProfile(profile);
            }
            var volume = new GameObject("Environment").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        // Temporal anti-aliasing, and water, clouds, volumetric fog, reflections, ambient occlusion, contact shadows
        // and global illumination (only switched on by photo mode's volume) turned on for this camera whatever the
        // default frame settings say.
        public static void SetUpCamera(Camera cam)
        {
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 16000f; // the far ridges (see Backdrop)
            cam.allowHDR = true;
            var data = cam.GetComponent<HDAdditionalCameraData>();
            if (data == null) data = cam.gameObject.AddComponent<HDAdditionalCameraData>();
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            data.customRenderingSettings = true;
            foreach (var field in new[] { FrameSettingsField.Water, FrameSettingsField.VolumetricClouds, FrameSettingsField.Volumetrics,
                         FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.ContactShadows, FrameSettingsField.SSGI })
            {
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                data.renderingPathCustomFrameSettings.SetEnabled(field, true);
            }
        }

        // The sun as an HDRP light in lux, casting soft shadows. Also saved into the scene by the editor setup.
        public static void ApplyLighting(Light sun)
        {
            if (sun.GetComponent<HDAdditionalLightData>() == null) sun.gameObject.AddComponent<HDAdditionalLightData>();
            sun.type = LightType.Directional;
            sun.lightUnit = LightUnit.Lux;
            sun.intensity = NoonLux;
            sun.color = Color.white; // the atmosphere warms it near the horizon
            sun.transform.rotation = Quaternion.Euler(36f, -35f, 0f);
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
        }

        public static void ConfigureProfile(VolumeProfile profile)
        {
            var environment = Get<VisualEnvironment>(profile);
            environment.skyType.Override((int)SkyType.PhysicallyBased);
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = Get<PhysicallyBasedSky>(profile);
            var stars = Resources.Load<Cubemap>("Sky/sky_night");
            if (stars != null)
            {
                sky.spaceEmissionTexture.Override(stars);
                sky.spaceEmissionMultiplier.Override(2f);
            }

            var clouds = Get<VolumetricClouds>(profile);
            clouds.enable.Override(true);
            clouds.cloudPreset = VolumetricClouds.CloudPresets.Sparse; // fair-weather summer cumulus

            // Haze that thickens near the ground and lights up in the sun (volumetric), for depth and sun shafts.
            var fog = Get<Fog>(profile);
            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.SkyColor);
            fog.meanFreePath.Override(900f);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(90f);
            fog.enableVolumetricFog.Override(true);
            fog.albedo.Override(Color.white);
            fog.anisotropy.Override(0.65f);
            fog.depthExtent.Override(180f);

            var exposure = Get<Exposure>(profile);
            exposure.mode.Override(ExposureMode.AutomaticHistogram);
            exposure.limitMin.Override(-1f);
            exposure.limitMax.Override(16f);
            exposure.compensation.Override(0.3f); // keeps the shaded valley readable at sunrise and sunset
            exposure.adaptationSpeedDarkToLight.Override(3f);
            exposure.adaptationSpeedLightToDark.Override(2f);

            var shadows = Get<HDShadowSettings>(profile);
            shadows.maxShadowDistance.Override(350f);
            shadows.cascadeShadowSplitCount.Override(4);
            var ao = Get<ScreenSpaceAmbientOcclusion>(profile);
            ao.intensity.Override(1f);
            ao.radius.Override(1.5f);
            // Reflections traced on screen for anything about as glossy as wet concrete and up, so the lake (glossy
            // out to the far shore, see Water) mirrors the trees and hills across it, not just the sky.
            var ssr = Get<ScreenSpaceReflection>(profile);
            ssr.enabled.Override(true);
            foreach (var parameter in new[] { ("m_MinSmoothness", 0.72f), ("m_SmoothnessFadeStart", 0.8f) })
            {
                var field = typeof(ScreenSpaceReflection).GetField(parameter.Item1,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                (field?.GetValue(ssr) as ClampedFloatParameter)?.Override(parameter.Item2);
            }

            // The grade: ACES, a little extra contrast, slightly muted colour so foliage doesn't glow, warm
            // highlights over cool shadows, gentle bloom and vignette (values from ChatGPT's review of the renders).
            Get<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var color = Get<ColorAdjustments>(profile);
            color.contrast.Override(12f);
            color.saturation.Override(-3f);
            var bloom = Get<Bloom>(profile);
            bloom.intensity.Override(0.1f);
            bloom.threshold.Override(1.2f);
            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.12f);
            vignette.smoothness.Override(0.45f);
            var whiteBalance = Get<WhiteBalance>(profile);
            whiteBalance.temperature.Override(0f); // the sky already warms low sun; more turned dawn brown
            var split = Get<SplitToning>(profile);
            split.shadows.Override(new Color(0.42f, 0.55f, 0.6f));
            split.highlights.Override(new Color(0.75f, 0.62f, 0.48f));
            split.balance.Override(-15f);
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent =>
            profile.TryGet(out T component) ? component : profile.Add<T>(true);
    }
}

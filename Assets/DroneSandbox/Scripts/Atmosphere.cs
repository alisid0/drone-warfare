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
    // Keeps the rain's box level, just above and ahead of the camera.
    public class AboveCamera : MonoBehaviour
    {
        public Transform cam;

        void LateUpdate()
        {
            if (cam == null) return;
            Vector3 ahead = cam.forward;
            ahead.y = 0f;
            transform.SetPositionAndRotation(cam.position + Vector3.up * 16f + ahead.normalized * 14f, Quaternion.identity);
        }
    }

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
            ApplyQuality(cam);
        }

        // The graphics quality setting (Settings.Quality) on the game camera: High draws everything above; Medium
        // drops screen-space reflections, contact shadows and the volumetric fog (plain height fog still hazes the
        // distance) and brings in the tree detail a little; Low also drops ambient occlusion and the volumetric
        // clouds, cuts tree detail further and thins the grass and draws it nearer. The water stays at every level.
        public static void ApplyQuality(Camera cam)
        {
            int q = Settings.Quality;
            var data = cam != null ? cam.GetComponent<HDAdditionalCameraData>() : null;
            if (data != null)
            {
                void Set(FrameSettingsField field, bool on)
                {
                    data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                    data.renderingPathCustomFrameSettings.SetEnabled(field, on);
                }
                Set(FrameSettingsField.SSR, q >= 2);
                Set(FrameSettingsField.ContactShadows, q >= 2);
                Set(FrameSettingsField.SSAO, q >= 1);
                Set(FrameSettingsField.Volumetrics, q >= 2);
                Set(FrameSettingsField.VolumetricClouds, q >= 1);
            }
            QualitySettings.lodBias = q == 0 ? 1f : q == 1 ? 1.5f : 2f; // the project's own setting (2) at High
            if (Ground.Current != null) Ground.Current.detailObjectDensity = GrassDensity;
        }

        // How much of the grass is drawn at this quality.
        public static float GrassDensity => Settings.Quality == 0 ? 0.35f : Settings.Quality == 1 ? 0.7f : 1f;

        // How far out the grass is drawn at this quality (DayNight shortens it further as the sun sets).
        public static float GrassDistance => Settings.Quality == 0 ? 60f : Settings.Quality == 1 ? 90f : Grass.DrawDistance;

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
            // Thin near the ground but reaching higher, so the battlefield stays as clear as before while the far
            // ridges (which rise well above 90 m) fade into a haze instead of standing out sharp.
            fog.meanFreePath.Override(1100f);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(140f);
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

            // No motion blur: panning a strategy camera smeared the whole battlefield (and the HUD reads against it).
            Get<MotionBlur>(profile).intensity.Override(0f);

            var shadows = Get<HDShadowSettings>(profile);
            shadows.maxShadowDistance.Override(350f);
            shadows.cascadeShadowSplitCount.Override(4);
            // Skylight in the shade: on a clear summer day the open sky lights shadows to about a quarter of full
            // sun. The sky's own ambient left them near black under the trees, so it counts for more (sunlit
            // ground barely changes, it's nearly all sun).
            var indirect = Get<IndirectLightingController>(profile);
            indirect.indirectDiffuseLightingMultiplier.Override(2f);
            indirect.reflectionLightingMultiplier.Override(1.2f);
            var ao = Get<ScreenSpaceAmbientOcclusion>(profile);
            ao.intensity.Override(0.8f);
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
            // Contrast and the cool tint in the shadows are gentle, and the shadows lifted a touch: ACES already
            // crushes the darks, and with more the shade under trees went near black and teal from the game camera.
            Get<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var color = Get<ColorAdjustments>(profile);
            color.contrast.Override(5f);
            color.saturation.Override(-3f);
            var tones = Get<ShadowsMidtonesHighlights>(profile);
            tones.shadows.Override(new Vector4(1f, 1f, 1f, 0.12f));
            var bloom = Get<Bloom>(profile);
            bloom.intensity.Override(0.1f);
            bloom.threshold.Override(1.2f);
            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.12f);
            vignette.smoothness.Override(0.45f);
            var whiteBalance = Get<WhiteBalance>(profile);
            whiteBalance.temperature.Override(0f); // the sky already warms low sun; more turned dawn brown
            var split = Get<SplitToning>(profile);
            split.shadows.Override(new Color(0.47f, 0.53f, 0.57f));
            split.highlights.Override(new Color(0.75f, 0.62f, 0.48f));
            split.balance.Override(-15f);
        }

        static Volume weatherVolume;

        // How much of the sun gets through the weather (DayNight multiplies the sunlight by it).
        public static float SunScale { get; private set; } = 1f;

        // Weather on top of the usual sky: more cloud for overcast, dark storm clouds and thicker haze for rain,
        // a low thick fog for fog. Clear takes the overrides away.
        public static Weather Current { get; private set; } = Weather.Clear;

        public static void SetWeather(Weather weather)
        {
            Current = weather;
            if (weatherVolume == null)
            {
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.Add<VolumetricClouds>(true).enable.Override(true);
                profile.Add<Fog>(true).enabled.Override(true);
                weatherVolume = new GameObject("Weather").AddComponent<Volume>();
                weatherVolume.isGlobal = true;
                weatherVolume.priority = 15f;
                weatherVolume.sharedProfile = profile;
            }
            weatherVolume.weight = weather == Weather.Clear ? 0f : 1f;
            Recon.WeatherFactor = weather == Weather.Fog ? 0.6f : weather == Weather.Rain ? 0.8f : weather == Weather.Overcast ? 0.95f : 1f;
            SunScale = weather == Weather.Overcast ? 0.35f : weather == Weather.Rain ? 0.2f : weather == Weather.Fog ? 0.45f : 1f;
            Rain(weather == Weather.Rain);
            if (weather == Weather.Clear) return;
            var p = weatherVolume.sharedProfile;
            p.TryGet(out VolumetricClouds clouds);
            p.TryGet(out Fog fog);
            var preset = weather == Weather.Rain ? VolumetricClouds.CloudPresets.Stormy
                : weather == Weather.Overcast ? VolumetricClouds.CloudPresets.Overcast : VolumetricClouds.CloudPresets.Cloudy;
            clouds.cloudPreset = preset; // fills in the preset's values...
            var field = typeof(VolumetricClouds).GetField("m_CloudPreset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            (field?.GetValue(clouds) as VolumeParameter<VolumetricClouds.CloudPresets>)?.Override(preset); // ...and makes it count
            clouds.shadows.Override(weather != Weather.Fog); // cloud shadows drifting over the ground
            fog.meanFreePath.Override(weather == Weather.Fog ? 110f : weather == Weather.Rain ? 380f : 650f);
            fog.maximumHeight.Override(weather == Weather.Fog ? 60f : 120f);
        }

        static ParticleSystem rain;

        // Rain: thin streaks falling through a box that rides along above the camera (Resources/Materials/Rain).
        static void Rain(bool on)
        {
            if (!on)
            {
                if (rain != null) rain.gameObject.SetActive(false);
                return;
            }
            if (rain == null)
            {
                var cam = Camera.main;
                var material = Resources.Load<Material>("Materials/Rain");
                if (cam == null || material == null) return;
                var go = new GameObject("Rain");
                go.AddComponent<AboveCamera>().cam = cam.transform;
                rain = go.AddComponent<ParticleSystem>();
                rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = rain.main;
                main.startLifetime = 1.7f;
                main.startSpeed = 0f;
                main.startSize = 0.03f;
                main.maxParticles = 22000;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = rain.emission;
                emission.rateOverTime = 12000f;
                var shape = rain.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(55f, 1f, 55f);
                var velocity = rain.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = new ParticleSystem.MinMaxCurve(1.5f);
                velocity.y = new ParticleSystem.MinMaxCurve(-22f);
                velocity.z = new ParticleSystem.MinMaxCurve(0.5f);
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.045f;
                renderer.lengthScale = 1f;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            rain.gameObject.SetActive(true);
            rain.Play();
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent =>
            profile.TryGet(out T component) ? component : profile.Add<T>(true);
    }
}

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

        // Applies lighting and a private runtime profile so saved assets also receive the larger-world distances.
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
            ConfigureScenery(volume.profile); // also update existing saved profiles, without changing the asset
            Profile = volume.profile;
        }

        // The game camera's live copy of the profile (the autotest switches effects in it to compare them).
        public static VolumeProfile Profile { get; private set; }

        // Temporal anti-aliasing, and water, clouds, volumetric fog, reflections, ambient occlusion, contact shadows
        // and global illumination (only switched on by photo mode's volume) turned on for this camera whatever the
        // default frame settings say.
        public static void SetUpCamera(Camera cam)
        {
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 80000f; // the whole 56 km world, corner to corner (see FarTerrain)
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
        // With upscaling on (Settings.Upscaling), DLSS draws the frame smaller and sharpens it back up: its quality
        // mode at High, balanced at Medium, performance at Low.
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
                Set(FrameSettingsField.SSGI, q >= 2);
                data.allowDynamicResolution = Settings.Upscaling;
                data.allowDeepLearningSuperSampling = true;
                data.deepLearningSuperSamplingUseCustomQualitySettings = true;
                data.deepLearningSuperSamplingQuality = (uint)(q == 2 ? UnityEngine.NVIDIA.DLSSQuality.MaximumQuality
                    : q == 1 ? UnityEngine.NVIDIA.DLSSQuality.Balanced : UnityEngine.NVIDIA.DLSSQuality.MaximumPerformance);
            }
            QualitySettings.lodBias = q == 0 ? 0.65f : q == 1 ? 0.85f : 1f; // Art calibrates tree transitions at 45 degrees and bias 1
            if (RenderSettings.sun != null) ApplySunQuality(RenderSettings.sun.GetComponent<HDAdditionalLightData>());
            if (Ground.Current != null) Ground.Current.detailObjectDensity = GrassDensity;
        }

        // How much of the grass is drawn at this quality.
        public static float GrassDensity => Settings.Quality == 0 ? 0.35f : Settings.Quality == 1 ? 0.7f : 1f;

        // How far out the grass is drawn at this quality (DayNight shortens it further as the sun sets).
        public static float GrassDistance => Settings.Quality == 0 ? 90f : Settings.Quality == 1 ? 135f : Grass.DrawDistance;

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
            ApplySunQuality(sun.GetComponent<HDAdditionalLightData>());
        }

        // The sun's shadow map: HDRP's default 512 texels a cascade left a tree's shadow a soft blob and a house's a
        // smudge; 2048 at High draws trunks, eaves and vehicles crisply (1024 at Medium, 512 at Low). It also casts
        // contact shadows (the profile's ContactShadows; drawn at High).
        static void ApplySunQuality(HDAdditionalLightData light)
        {
            if (light == null) return;
            int q = Settings.Quality;
            light.SetShadowResolutionOverride(true);
            light.SetShadowResolution(q == 2 ? 2048 : q == 1 ? 1024 : 512);
            light.useContactShadow.useOverride = true;
            light.useContactShadow.@override = true;
        }

        // Creates the shared editor/runtime environment settings, including the bounded scenery distances.
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

            ConfigureScenery(profile);

            var exposure = Get<Exposure>(profile);
            exposure.mode.Override(ExposureMode.AutomaticHistogram);
            exposure.limitMin.Override(-1f);
            exposure.limitMax.Override(16f);
            exposure.compensation.Override(0.3f); // keeps the shaded valley readable at sunrise and sunset
            exposure.adaptationSpeedDarkToLight.Override(3f);
            exposure.adaptationSpeedLightToDark.Override(2f);

            // No motion blur: panning a strategy camera smeared the whole battlefield (and the HUD reads against it).
            Get<MotionBlur>(profile).intensity.Override(0f);

            // Skylight in the shade: on a clear summer day the open sky lights shadows to about a quarter of full
            // sun. The sky's own ambient left them near black under the trees, so it counts for more (sunlit
            // ground barely changes, it's nearly all sun).
            var indirect = Get<IndirectLightingController>(profile);
            indirect.indirectDiffuseLightingMultiplier.Override(2f);
            indirect.reflectionLightingMultiplier.Override(1.2f);
            // Ambient occlusion out to 2.5 m, so from a drone's height the ground still darkens round a trunk, under
            // a hedge and along a wall's foot (1.5 m showed only up close).
            var ao = Get<ScreenSpaceAmbientOcclusion>(profile);
            ao.intensity.Override(1f);
            ao.radius.Override(2.5f);
            // Contact shadows: the thin dark line where a wheel, a wall or a trunk meets the ground, finer than the
            // sun's shadow map can draw. Traced on screen out to 200 m, for low cameras and photo mode (High only).
            var contact = Get<ContactShadows>(profile);
            contact.enable.Override(true);
            contact.length.Override(0.35f);
            contact.distanceScaleFactor.Override(0.6f);
            contact.maxDistance.Override(200f);
            contact.fadeDistance.Override(40f);
            contact.opacity.Override(0.9f);
            // Normal maps and ambient occlusion shadow themselves in sunlight: the leaves in the forest floor, the
            // ridges of ploughed soil and the grain of rock stand up from the ground instead of lying painted on it.
            Get<MicroShadowing>(profile).enable.Override(true);
            // Sunlight bouncing off the ground into the shade: green light under trees beside a sunlit field, warm
            // fill on a wall facing a bright yard. Traced on screen at half resolution and the low tier, and only at
            // High graphics (ApplyQuality); photo mode adds its own at full quality.
            var bounce = Get<GlobalIllumination>(profile);
            bounce.enable.Override(true);
            bounce.tracing.Override(RayCastingMode.RayMarching);
            bounce.fullResolutionSS.Override(false);
            bounce.quality.Override((int)ScalableSettingLevelParameter.Level.Low);
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

        // A gentle kilometre-scale haze gives the hills depth, with volumetric light reaching the ground from
        // the high play camera. Four bounded cascades cover its far corners without scaling 350 m by Distance.
        // Used by the editor's profile creator and on a runtime copy of existing profiles.
        static void ConfigureScenery(VolumeProfile profile)
        {
            var fog = Get<Fog>(profile);
            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.SkyColor);
            fog.meanFreePath.Override(6000f); // a clear day: hills 10-20 km off still show through the haze
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(450f);
            fog.enableVolumetricFog.Override(true);
            fog.albedo.Override(Color.white);
            fog.anisotropy.Override(0.65f);
            fog.depthExtent.Override(1000f);
            var shadows = Get<HDShadowSettings>(profile);
            shadows.maxShadowDistance.Override(1000f);
            shadows.cascadeShadowSplitCount.Override(4);
            shadows.cascadeShadowSplit0.Override(0.18f);
            shadows.cascadeShadowSplit1.Override(0.42f);
            shadows.cascadeShadowSplit2.Override(0.7f);
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
                profile.Add<Fog>().enabled.Override(true); // inherit the base profile's volumetric light and colour
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
            // Real cloud shadows only take 15-30% off sunlit ground (the sky still lights it): at full opacity they
            // were near-black blotches that hid the fields' colours.
            clouds.shadowOpacity.Override(0.4f);
            fog.meanFreePath.Override(weather == Weather.Fog ? Scale.D(110f) : weather == Weather.Rain ? 2200f : 3800f);
            fog.maximumHeight.Override(weather == Weather.Fog ? Scale.D(60f) : 450f);
        }

        static ParticleSystem rain;

        // Rain stays in a real-size box above the camera, even when it is hundreds of metres from the focus.
        // Increasing the box with the map would waste particles and overdraw (Resources/Materials/Rain).
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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DroneSandbox
{
    // Time of day. A clock moves the sun from east to west (the same light becomes a dim blue moon after dark),
    // dims and tints the sky, ambient light, fog and reflections, and switches night lights on after sunset:
    // vehicle headlights, base floodlights and village windows.
    // Also the thermal view: a white-hot camera where vehicles and drones glow. It needs a Scout in the air.
    public class DayNight : MonoBehaviour
    {
        const float NoonElevation = 50f;   // degrees above the horizon at midday
        const float MoonElevation = 38f;
        const float Sunrise = 5f, Sunset = 21f; // a long summer day

        static readonly Color DuskSun = new Color(1f, 0.55f, 0.3f);
        static readonly Color NoonSun = new Color(1f, 0.93f, 0.8f);
        static readonly Color MoonLight = new Color(0.55f, 0.66f, 0.95f);
        static readonly Color DuskSky = new Color(1f, 0.72f, 0.55f);
        static readonly Color NightSky = new Color(0.45f, 0.55f, 0.95f);
        static readonly Color DuskFog = new Color(0.62f, 0.5f, 0.45f);
        static readonly Color NightFog = new Color(0.06f, 0.08f, 0.12f);
        static readonly Color WindowGlow = new Color(0.95f, 0.66f, 0.3f);

        public static DayNight Instance { get; private set; }

        public float hour = 10f;              // 0 to 24
        public float hoursPerMinute = 1f;     // how fast the clock runs, in game hours per real minute

        public bool Thermal { get; private set; }
        public float Daylight { get; private set; } = 1f; // 0 at night, 1 in full day
        public bool IsDark => Daylight < 0.3f;
        public string Clock => $"{(int)hour:00}:{(int)(hour % 1f * 60f):00}";
        public string PartOfDay => hour < 4f ? "Night" : hour < 6.5f ? "Dawn" : hour < 18.5f ? "Day" : hour < 21.5f ? "Dusk" : "Night";

        static readonly List<Light> nightLights = new List<Light>();
        static Material windowMaterial;

        Light sun;
        Material sky;
        ReflectionProbe probe;
        Volume nightVolume, thermalVolume;
        ColorAdjustments thermalColour;
        Material hot;
        readonly Dictionary<Renderer, Material[]> cooled = new Dictionary<Renderer, Material[]>();
        float heatTimer;

        // Windows in houses and the HQ use this one material (see Art), so they can all light up at night.
        public static Material WindowMaterial
        {
            get
            {
                if (windowMaterial == null)
                {
                    windowMaterial = Art.NewMaterial(new Color(0.02f, 0.025f, 0.03f), glow: true);
                    windowMaterial.SetFloat("_Smoothness", 0.85f);
                    windowMaterial.SetColor("_EmissionColor", Color.black);
                }
                return windowMaterial;
            }
        }

        // Lights that only shine after dark. They start switched off.
        public static Light AddNightLight(Transform parent, Vector3 localPosition, Vector3 localEuler, LightType type,
            Color colour, float intensity, float range, float spotAngle = 60f)
        {
            var go = new GameObject("Night Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = colour;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = spotAngle;
            light.shadows = LightShadows.None;
            light.enabled = Instance != null && Instance.IsDark;
            nightLights.Add(light);
            return light;
        }

        public void Init(Light sunLight)
        {
            Instance = this;
            sun = sunLight;
            // Work on a copy of the sky, so dimming it never touches the saved material.
            if (RenderSettings.skybox != null)
            {
                sky = new Material(RenderSettings.skybox);
                RenderSettings.skybox = sky;
            }
            probe = Object.FindAnyObjectByType<ReflectionProbe>();

            // Night: a little brighter and less colourful than the raw light, so it stays playable.
            var night = ScriptableObject.CreateInstance<VolumeProfile>();
            var nightColour = night.Add<ColorAdjustments>(true);
            nightColour.postExposure.Override(1.2f);
            nightColour.saturation.Override(-45f);
            nightVolume = MakeVolume("Night Grade", night, 15f);

            // Thermal: black and white with hard contrast; hot things are swapped to a glowing material.
            var thermal = ScriptableObject.CreateInstance<VolumeProfile>();
            thermalColour = thermal.Add<ColorAdjustments>(true);
            thermalColour.saturation.Override(-100f);
            thermalColour.contrast.Override(35f);
            thermalColour.postExposure.Override(0f);
            thermalColour.colorFilter.Override(new Color(0.92f, 0.96f, 1f));
            thermalVolume = MakeVolume("Thermal", thermal, 20f);
            thermalVolume.weight = 0f;

            hot = Art.NewMaterial(new Color(0.95f, 0.95f, 0.95f), glow: true);
            hot.SetColor("_EmissionColor", new Color(0.75f, 0.75f, 0.75f));
            Apply();
        }

        Volume MakeVolume(string name, VolumeProfile profile, float priority)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = priority;
            volume.sharedProfile = profile;
            return volume;
        }

        public void SetTime(float newHour, float speed)
        {
            hour = Mathf.Repeat(newHour, 24f);
            hoursPerMinute = speed;
            Apply();
        }

        public void SetThermal(bool on)
        {
            if (on == Thermal) return;
            Thermal = on;
            if (!on) Cool();
            heatTimer = 0f;
            Apply();
        }

        void Update()
        {
            hour = Mathf.Repeat(hour + hoursPerMinute * Time.deltaTime / 60f, 24f);
            Apply();
            if (Thermal)
            {
                heatTimer -= Time.unscaledDeltaTime;
                if (heatTimer <= 0f)
                {
                    heatTimer = 0.25f;
                    Heat();
                }
            }
        }

        // ---- Sky and light ----

        void Apply()
        {
            float dayLength = Sunset - Sunrise, nightLength = 24f - dayLength;
            float sunElevation = NoonElevation * Mathf.Sin(Mathf.PI * (hour - Sunrise) / dayLength);
            float sunYaw = -90f + (hour - Sunrise) / dayLength * 180f;  // rises in the east, sets in the west
            float nightHour = Mathf.Repeat(hour - Sunset, 24f);
            float moonElevation = Mathf.Max(8f, MoonElevation * Mathf.Sin(Mathf.PI * Mathf.Min(nightHour, nightLength) / nightLength));
            float moonYaw = -90f + Mathf.Min(nightHour, nightLength) / nightLength * 180f;

            Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, 12f, sunElevation));
            float sunStrength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 12f, sunElevation));
            float moonStrength = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-12f, -3f, sunElevation));

            if (sun != null)
            {
                if (sunElevation > -4f)
                {
                    sun.transform.rotation = Quaternion.Euler(Mathf.Max(sunElevation, 2f), sunYaw, 0f);
                    sun.color = Color.Lerp(DuskSun, NoonSun, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 25f, sunElevation)));
                    sun.intensity = 1.6f * sunStrength;
                }
                else
                {
                    sun.transform.rotation = Quaternion.Euler(moonElevation, moonYaw, 0f);
                    sun.color = MoonLight;
                    sun.intensity = 0.8f * moonStrength;
                }
                sun.shadowStrength = Mathf.Lerp(0.6f, 0.9f, Daylight);
            }

            if (sky != null)
            {
                float lit = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-10f, 10f, sunElevation));
                sky.SetFloat("_Exposure", Mathf.Lerp(0.035f, 1f, lit));
                Color tint = sunElevation > 0f
                    ? Color.Lerp(DuskSky, Color.white, Mathf.InverseLerp(2f, 20f, sunElevation))
                    : Color.Lerp(NightSky, DuskSky, Mathf.InverseLerp(-10f, 2f, sunElevation));
                sky.SetColor("_Tint", tint * 0.5f); // the panoramic shader's neutral tint is grey 0.5
            }

            RenderSettings.ambientIntensity = Mathf.Lerp(0.35f, 1.2f, Daylight);
            Color dayFog = Atmosphere.HazeColor;
            RenderSettings.fogColor = sunElevation > 0f
                ? Color.Lerp(DuskFog, dayFog, Mathf.InverseLerp(2f, 18f, sunElevation))
                : Color.Lerp(NightFog, DuskFog, Mathf.InverseLerp(-10f, 2f, sunElevation));
            if (probe != null) probe.intensity = Mathf.Lerp(0.06f, 1f, Daylight);

            nightVolume.weight = Thermal ? 0f : 1f - Daylight;
            thermalVolume.weight = Thermal ? 1f : 0f;
            thermalColour.postExposure.value = Mathf.Lerp(2.4f, 0.2f, Daylight); // the camera sees heat, not light

            bool dark = IsDark;
            nightLights.RemoveAll(l => l == null);
            foreach (var light in nightLights) light.enabled = dark;
            if (windowMaterial != null) windowMaterial.SetColor("_EmissionColor", dark ? WindowGlow * 0.9f : Color.black);
        }

        // ---- Thermal ----

        // Swaps every vehicle's and drone's materials for the glowing "hot" one, remembering the originals.
        void Heat()
        {
            var game = SandboxGame.Instance;
            if (game == null) return;
            foreach (var e in game.enemies) if (e != null) HeatUp(e.gameObject);
            foreach (var d in game.drones) if (d != null) HeatUp(d.gameObject);
        }

        void HeatUp(GameObject unit)
        {
            foreach (var r in unit.GetComponentsInChildren<Renderer>(true))
            {
                if (cooled.ContainsKey(r)) continue;
                cooled[r] = r.sharedMaterials;
                var materials = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = hot;
                r.sharedMaterials = materials;
            }
        }

        void Cool()
        {
            foreach (var pair in cooled)
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            cooled.Clear();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // Time of day. A clock moves the sun from east to west (the same light becomes a blue moon after dark) and
    // switches night lights on after sunset: vehicle headlights, base floodlights, the dam's lamps and lit windows.
    // HDRP's physically based sky does the rest from the sun's height: sunrise and sunset colours, the dimming
    // sky and ambient light, haze colour, and automatic exposure (see Atmosphere).
    // Also the thermal view: a white-hot camera where vehicles and drones glow. It needs a drone with a thermal
    // camera in the air (see Drone.HasThermal).
    public class DayNight : MonoBehaviour
    {
        const float NoonElevation = 50f;   // degrees above the horizon at midday
        const float MoonElevation = 38f;
        const float Sunrise = 5f, Sunset = 21f; // a long summer day

        const float MoonLux = 8f;            // far brighter than a real moon (about 0.3 lux), so nights stay playable
        const float LumensPerUnit = 400f;    // night-light intensities are given on the old 0-10 scale
        static readonly Color MoonLight = new Color(0.62f, 0.72f, 1f);
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
        Volume nightVolume, thermalVolume;
        ColorAdjustments thermalColour;
        Material hot, warm; // thermal: engines running, and idle kit
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
                    Art.Glow(windowMaterial, Color.black);
                }
                return windowMaterial;
            }
        }

        // Lights that only shine after dark. They start switched off. Intensity is on a 0-10 scale (about 400
        // lumens a step).
        public static Light AddNightLight(Transform parent, Vector3 localPosition, Vector3 localEuler, LightType type,
            Color colour, float intensity, float range, float spotAngle = 60f)
        {
            var go = new GameObject("Night Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            var light = go.AddComponent<Light>();
            if (go.GetComponent<HDAdditionalLightData>() == null) go.AddComponent<HDAdditionalLightData>();
            light.type = type;
            light.color = colour;
            light.lightUnit = LightUnit.Lumen;
            light.intensity = intensity * LumensPerUnit;
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

            // Night: brighter and less colourful than the raw light (exposure already adapts), so the ground stays
            // readable: dark fields at roughly a sixth of full brightness rather than near black.
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
            Art.Glow(hot, new Color(0.75f, 0.75f, 0.75f));
            warm = Art.NewMaterial(new Color(0.6f, 0.6f, 0.6f), glow: true);
            Art.Glow(warm, new Color(0.3f, 0.3f, 0.3f));
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
            // The thermal camera sees grass as one cool, even field, so the tufts go: hot things stand out alone.
            if (Ground.Current != null) Ground.Current.detailObjectDistance = on ? 0f : Grass.DrawDistance;
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
            float sunStrength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-4f, 3f, sunElevation));
            float moonStrength = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-12f, -3f, sunElevation));

            if (sun != null)
            {
                // White sunlight in lux; the physically based sky reddens it near the horizon and colours the sky.
                if (sunElevation > -4f)
                {
                    sun.transform.rotation = Quaternion.Euler(Mathf.Max(sunElevation, 1f), sunYaw, 0f);
                    sun.color = Color.white;
                    sun.intensity = Atmosphere.NoonLux * sunStrength * Atmosphere.SunScale; // dimmed by cloud, rain or fog
                }
                else
                {
                    sun.transform.rotation = Quaternion.Euler(moonElevation, moonYaw, 0f);
                    sun.color = MoonLight;
                    sun.intensity = MoonLux * moonStrength;
                }
                sun.shadowStrength = Mathf.Lerp(0.6f, 0.95f, Daylight) * Mathf.Lerp(0.45f, 1f, Atmosphere.SunScale);
            }

            nightVolume.weight = Thermal ? 0f : 1f - Daylight;
            thermalVolume.weight = Thermal ? 1f : 0f;
            thermalColour.postExposure.value = Mathf.Lerp(1.2f, -0.3f, Daylight); // the camera sees heat, not light

            bool dark = IsDark;
            nightLights.RemoveAll(l => l == null);
            // A thermal camera sees heat, not light: lamps and lit windows don't show in it.
            foreach (var light in nightLights) light.enabled = dark && !Thermal;
            if (windowMaterial != null) Art.Glow(windowMaterial, dark && !Thermal ? WindowGlow * 0.9f : Color.black);
        }

        // ---- Thermal ----

        // Swaps every vehicle's and drone's materials for the glowing "hot" one, remembering the originals.
        void Heat()
        {
            var game = SandboxGame.Instance;
            if (game == null) return;
            // Engines running (driving, firing, crews on alert) glow hot; parked and idle kit only warm.
            foreach (var e in game.enemies) if (e != null) HeatUp(e.gameObject, e.IsMoving || e.Engaging || e.Alerted ? hot : warm);
            foreach (var d in game.drones) if (d != null) HeatUp(d.gameObject, hot);
        }

        void HeatUp(GameObject unit, Material heat)
        {
            foreach (var r in unit.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial != null && r.sharedMaterial.name == "PropBlur") continue; // the blur of a drone's props
                if (!cooled.ContainsKey(r)) cooled[r] = r.sharedMaterials;
                if (r.sharedMaterial == heat) continue;
                var materials = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = heat;
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

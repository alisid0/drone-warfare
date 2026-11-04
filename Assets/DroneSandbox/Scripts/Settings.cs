using UnityEngine;

namespace DroneSandbox
{
    // The player's comfort settings, kept between sessions: graphics quality, how big the interface is, camera
    // shake, bright explosion flashes, alert sounds and the overall volume. Read from PlayerPrefs on first use.
    public static class Settings
    {
        public static readonly float[] UiScales = { 0.8f, 0.9f, 1f, 1.2f, 1.4f };
        public static readonly float[] Volumes = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        public static readonly string[] QualityNames = { "Low", "Medium", "High" };

        static bool loaded, persist = true;
        static float uiScale = 1f, volume = 1f;
        static bool shake = true, flashes = true, alertSounds = true, tips = true;
        static int quality = 2;
        static DifficultyLevel difficulty = DifficultyLevel.Regular;

        // Keep difficulty with the other preferences; old saves start at Regular.
        public static DifficultyLevel Difficulty
        {
            get { Load(); return difficulty; }
            set { Load(); difficulty = (DifficultyLevel)Mathf.Clamp((int)value, 0, 2); Save(); }
        }

        // 0 low, 1 medium, 2 high (see Atmosphere.ApplyQuality).
        public static int Quality
        {
            get { Load(); return quality; }
            set
            {
                Load();
                quality = Mathf.Clamp(value, 0, QualityNames.Length - 1);
                Save();
                if (Camera.main != null) Atmosphere.ApplyQuality(Camera.main);
            }
        }

        public static float UiScale { get { Load(); return uiScale; } set { Load(); uiScale = value; Save(); } }
        public static float Volume { get { Load(); return volume; } set { Load(); volume = Mathf.Clamp01(value); AudioListener.volume = volume; Save(); } }
        public static bool Shake { get { Load(); return shake; } set { Load(); shake = value; Save(); } }
        public static bool Tips { get { Load(); return tips; } set { Load(); tips = value; Save(); } } // Coach, in the first two Operations
        public static bool Flashes { get { Load(); return flashes; } set { Load(); flashes = value; Save(); } }
        public static bool AlertSounds { get { Load(); return alertSounds; } set { Load(); alertSounds = value; Save(); } }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            uiScale = PlayerPrefs.GetFloat("settings.uiScale", 1f);
            volume = PlayerPrefs.GetFloat("settings.volume", 1f);
            shake = PlayerPrefs.GetInt("settings.shake", 1) == 1;
            tips = PlayerPrefs.GetInt("settings.tips", 1) == 1;
            flashes = PlayerPrefs.GetInt("settings.flashes", 1) == 1;
            alertSounds = PlayerPrefs.GetInt("settings.alertSounds", 1) == 1;
            quality = Mathf.Clamp(PlayerPrefs.GetInt("settings.quality", 2), 0, QualityNames.Length - 1);
            difficulty = (DifficultyLevel)Mathf.Clamp(PlayerPrefs.GetInt("settings.difficulty", (int)DifficultyLevel.Regular), 0, 2);
            AudioListener.volume = volume;
        }

        static void Save()
        {
            if (!persist) return;
            PlayerPrefs.SetFloat("settings.uiScale", uiScale);
            PlayerPrefs.SetFloat("settings.volume", volume);
            PlayerPrefs.SetInt("settings.shake", shake ? 1 : 0);
            PlayerPrefs.SetInt("settings.tips", tips ? 1 : 0);
            PlayerPrefs.SetInt("settings.flashes", flashes ? 1 : 0);
            PlayerPrefs.SetInt("settings.alertSounds", alertSounds ? 1 : 0);
            PlayerPrefs.SetInt("settings.quality", quality);
            PlayerPrefs.SetInt("settings.difficulty", (int)difficulty);
            PlayerPrefs.Save();
        }

        // The defaults, for the automated test: whatever the player chose is left alone, and nothing the test
        // changes is saved.
        public static void UseDefaultsForTest()
        {
            loaded = true;
            persist = false;
            uiScale = 1f;
            volume = 1f;
            shake = flashes = alertSounds = true;
            quality = 2;
            difficulty = DifficultyLevel.Regular;
            AudioListener.volume = 1f;
        }
    }
}

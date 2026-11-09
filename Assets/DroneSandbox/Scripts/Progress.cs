using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DroneSandbox
{
    // The player's progress (the campaign, the tech shop, mission stars) in a save file of its own,
    // <persistentDataPath>/save.json, instead of the Windows registry: it can be backed up, copied to another PC and
    // synced by Steam Cloud. It's written whole to save.tmp and swapped in, with the last good copy kept as
    // save.bak, so a crash mid-save can't lose progress; a damaged save.json falls back to save.bak. The file
    // carries its format version and the map generator's, since a campaign is saved as seeds. The first time the
    // file is made, progress saved in PlayerPrefs by older versions is copied over (and left where it was).
    // Settings stay in PlayerPrefs (Settings.cs). Test runs keep everything in memory (UseTestStore).
    public static class Progress
    {
        public const int FormatVersion = 1;
        public const int GeneratorVersion = 1; // bump when SectorGenerator or Situations change what a seed builds

        [System.Serializable]
        class SaveFile
        {
            public int format;
            public int generator;
            public List<string> keys = new List<string>();
            public List<int> values = new List<int>();
        }

        static Dictionary<string, int> values;
        static bool persist = true;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");
        static string BackupPath => Path.Combine(Application.persistentDataPath, "save.bak");
        static string TempPath => Path.Combine(Application.persistentDataPath, "save.tmp");

        // The generator version the loaded campaign was made with (0 for a save from before versions were kept).
        public static int SavedGenerator { get; private set; } = GeneratorVersion;

        // Test runs: an empty store in memory that's never written.
        public static void UseTestStore()
        {
            values = new Dictionary<string, int>();
            persist = false;
        }

        public static int Get(string key, int fallback = 0)
        {
            Load();
            return values.TryGetValue(key, out int v) ? v : fallback;
        }

        public static void Set(string key, int value)
        {
            Load();
            values[key] = value;
        }

        // Forgets every value whose key starts with prefix (a new campaign forgets "theatre.", "campaign." and so on).
        public static void Forget(string prefix)
        {
            Load();
            var gone = new List<string>();
            foreach (var key in values.Keys) if (key.StartsWith(prefix)) gone.Add(key);
            foreach (var key in gone) values.Remove(key);
        }

        public static void Save()
        {
            Load();
            if (!persist) return;
            var file = new SaveFile { format = FormatVersion, generator = GeneratorVersion };
            foreach (var pair in values)
            {
                file.keys.Add(pair.Key);
                file.values.Add(pair.Value);
            }
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(TempPath, JsonUtility.ToJson(file, true));
                if (File.Exists(FilePath)) File.Replace(TempPath, FilePath, BackupPath);
                else File.Move(TempPath, FilePath);
                SavedGenerator = GeneratorVersion;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Couldn't save progress to {FilePath}: {e.Message}");
            }
        }

        static void Load()
        {
            if (values != null) return;
            values = new Dictionary<string, int>();
            if (Read(FilePath) || Read(BackupPath)) return;
            MigrateFromPlayerPrefs();
            Save();
        }

        static bool Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
                if (file == null || file.keys == null || file.values == null || file.keys.Count != file.values.Count) return false;
                values.Clear();
                for (int i = 0; i < file.keys.Count; i++) values[file.keys[i]] = file.values[i];
                SavedGenerator = file.generator;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Couldn't read the save {path}: {e.Message}");
                return false;
            }
        }

        // Progress older versions kept in PlayerPrefs: the campaign, its bonuses, the tech shop and mission stars.
        // (Which hotspots of the round in progress were won isn't carried over: a new round starts.)
        static void MigrateFromPlayerPrefs()
        {
            var keys = new List<string> { "theatre.seed", "theatre.round", "theatre.wins", "tech.points" };
            foreach (var bonus in new[] { "Intel", "Supply", "Rearm" }) keys.Add("campaign." + bonus);
            foreach (var item in Tech.Items) keys.Add("tech." + item.id);
            for (int i = 0; i < Missions.All.Count; i++) keys.Add($"mission{i}.stars");
            foreach (var key in keys)
                if (PlayerPrefs.HasKey(key)) values[key] = PlayerPrefs.GetInt(key);
            if (values.TryGetValue("theatre.wins", out int wins) && wins > 0)
                values["theatre.round"] = Get("theatre.round") + 1;
            SavedGenerator = 0;
        }
    }
}

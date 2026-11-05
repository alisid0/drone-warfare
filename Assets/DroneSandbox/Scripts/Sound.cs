using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Plays the game's sounds: recorded effects from Resources/Sounds where there are any (made with ElevenLabs:
    // "explosion 1", "explosion 2"... are variants of "explosion"), and Synth's for the rest and for the interface.
    // - Listening: the ears sit partway down the camera's line of sight, a little above the ground being looked
    //   at, so what's on screen is loud and off-screen fights are faint.
    // - Mix, in four groups: ambience about -24 dB, combat -12 dB, warnings -8 dB and the interface -14 dB.
    //   Motors duck a few dB while a warning plays.
    // - Combat sounds come from a pool of sources placed where things happen. Each drone carries its own motor
    //   loop, pitched by how hard it's flying.
    // - Ambience: wind all the time, birds by day and crickets at night, following DayNight; rain and now and then
    //   thunder while it's raining (only with recorded clips).
    public class Sound : MonoBehaviour
    {
        public static Sound Instance { get; private set; }

        enum Group { Ambient, Combat, Warning, Interface }
        static readonly float[] GroupVolume = { Db(-24f), Db(-12f), Db(-8f), Db(-14f) };
        const float MotorVolume = 0.11f; // about -19 dB: a drone overhead is clear, a swarm doesn't drown the fight

        static float Db(float db) => Mathf.Pow(10f, db / 20f);

        readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        readonly List<AudioSource> motors = new List<AudioSource>();
        AudioSource[] pool;
        int nextSource;
        AudioSource ui, wind, birds, crickets, rain;
        float nextThunder;
        Transform ears;
        Camera cam;
        RtsController rts;
        float duckUntil;

        public static void Create(GameObject host, Camera camera, RtsController controller)
        {
            var sound = host.AddComponent<Sound>();
            sound.cam = camera;
            sound.rts = controller;
            sound.Build();
        }

        // Creates the sound pool with falloff distances matched to the enlarged battlefield.
        void Build()
        {
            Instance = this;
            foreach (var listener in FindObjectsByType<AudioListener>()) listener.enabled = false;
            ears = new GameObject("Ears").transform;
            ears.gameObject.AddComponent<AudioListener>();

            Add("explosion", Synth.Explosion(0, 1f), Synth.Explosion(1, 1.2f), Synth.Explosion(2, 0.9f));
            Add("big explosion", Synth.Explosion(3, 2f), Synth.Explosion(4, 2.2f));
            Add("gunshot", Synth.Gunshot(0), Synth.Gunshot(1), Synth.Gunshot(2));
            Add("cannon", Synth.Cannon(0), Synth.Cannon(1));
            Add("missile", Synth.MissileLaunch());
            Add("whistle", Synth.BombWhistle());
            Add("ricochet", Synth.Ricochet(0), Synth.Ricochet(1), Synth.Ricochet(2));
            Add("drone down", Synth.DroneDown());
            Add("select", Synth.Select());
            Add("move", Synth.Move());
            Add("attack", Synth.AttackOrder());
            Add("invalid", Synth.Invalid());
            Add("click", Synth.Click());
            Add("contact", Synth.Contact());
            Add("warning", Synth.Warning());
            Add("alarm", Synth.Alarm());
            Add("objective", Synth.Objective());
            Add("success", Synth.Success());
            Add("failure", Synth.Failure());
            Add("launch", Synth.Launch());
            Add("motor Scout", Synth.Motor("Scout motor", 172f));
            Add("motor Strike", Synth.Motor("Strike motor", 138f));
            Add("motor Kamikaze", Synth.Motor("Kamikaze motor", 215f));
            Add("motor Bomber", Synth.Motor("Nightjar motor", 92f));
            int recorded = LoadRecorded();
            if (recorded > 0) Debug.Log($"Sound: {recorded} recorded clips in use");

            pool = new AudioSource[28];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("Sound " + i);
                go.transform.SetParent(transform, false);
                pool[i] = World3D(go.AddComponent<AudioSource>(), Scale.D(22f), Scale.D(320f));
            }
            ui = ears.gameObject.AddComponent<AudioSource>();
            ui.spatialBlend = 0f;
            ui.ignoreListenerPause = true; // clicks and pips still sound during a tactical pause
            wind = Loop(First("wind") ?? Synth.Wind(), GroupVolume[(int)Group.Ambient] * 1.6f);
            birds = Loop(First("birds") ?? Synth.Birds(), 0f);
            crickets = Loop(First("crickets") ?? Synth.Crickets(), 0f);
            if (First("rain") is AudioClip rainClip) rain = Loop(rainClip, 0f);
        }

        void Add(string name, params AudioClip[] variants) => clips[name] = variants;

        AudioClip First(string name) => clips.TryGetValue(name, out var found) && found.Length > 0 ? found[0] : null;

        // The recorded clips in Resources/Sounds, each replacing the synthesized sound of its name (a trailing
        // number marks a variant). Returns how many there were.
        int LoadRecorded()
        {
            var found = new Dictionary<string, List<AudioClip>>();
            var all = Resources.LoadAll<AudioClip>("Sounds");
            foreach (var clip in all)
            {
                string name = clip.name.Trim();
                int space = name.LastIndexOf(' ');
                if (space > 0 && int.TryParse(name.Substring(space + 1), out _)) name = name.Substring(0, space);
                if (!found.TryGetValue(name, out var list)) found[name] = list = new List<AudioClip>();
                list.Add(clip);
            }
            foreach (var pair in found) clips[pair.Key] = pair.Value.ToArray();
            return all.Length;
        }

        static AudioSource World3D(AudioSource source, float near, float far)
        {
            source.playOnAwake = false;
            source.spatialBlend = 0.85f; // mostly placed, a little everywhere, so the far side of the map isn't silent
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = near;
            source.maxDistance = far;
            source.dopplerLevel = 0f;
            return source;
        }

        AudioSource Loop(AudioClip clip, float volume)
        {
            var source = ears.gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = volume;
            source.Play();
            return source;
        }

        // Moves the listener toward the focus and updates ambience and motor ducking.
        void LateUpdate()
        {
            if (cam == null) return;
            // Leave the ears the old scaled distance above the focus, even with the narrower camera lens.
            float reach = PhotoMode.Active ? 0f : rts.Distance * (1f - 0.3f * Scale.Distance / Scale.Camera);
            ears.SetPositionAndRotation(cam.transform.position + cam.transform.forward * reach, cam.transform.rotation);

            var dayNight = DayNight.Instance;
            float day = dayNight != null ? dayNight.Daylight : 1f;
            float ambient = GroupVolume[(int)Group.Ambient];
            float quiet = SandboxGame.Instance != null && SandboxGame.Instance.Mode == GameMode.Menu ? 0.6f : 1f;
            birds.volume = Mathf.MoveTowards(birds.volume, ambient * 1.3f * Mathf.Clamp01(day * 1.5f - 0.2f) * quiet, Time.unscaledDeltaTime);
            crickets.volume = Mathf.MoveTowards(crickets.volume, ambient * 0.9f * Mathf.Clamp01(1f - day * 2f) * quiet, Time.unscaledDeltaTime);
            bool raining = Atmosphere.Current == Weather.Rain;
            if (rain != null) rain.volume = Mathf.MoveTowards(rain.volume, raining ? ambient * 2.4f * quiet : 0f, Time.unscaledDeltaTime * 0.5f);
            // Thunder now and then in rain.
            if (raining && clips.ContainsKey("thunder") && Time.unscaledTime > nextThunder)
            {
                if (nextThunder > 0f && Ready("thunder", 8f)) ui.PlayOneShot(Pick("thunder"), ambient * 3.2f * quiet);
                nextThunder = Time.unscaledTime + Random.Range(14f, 38f);
            }

            float duck = Time.unscaledTime < duckUntil ? Db(-4f) : 1f;
            motors.RemoveAll(m => m == null);
            foreach (var m in motors) m.volume = MotorVolume * duck * (m.GetComponentInParent<Drone>() is Drone d && d.IsJammed ? 0.5f : 1f);
        }

        // ---- Playing ----

        // A combat sound where it happens. The same sound twice within a few hundredths of a second (a swarm of
        // scouts all firing) plays once.
        public static void At(string name, Vector3 position, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (Instance == null) return;
            Instance.PlayAt(name, position, volume * GroupVolume[(int)Group.Combat], pitchJitter, 0.03f);
        }

        // The interface: selection chirps, order pips, clicks.
        public static void Ui(string name, float volume = 1f)
        {
            if (Instance == null || !Instance.Ready(name, 0.05f)) return;
            Instance.ui.pitch = 1f;
            Instance.ui.PlayOneShot(Instance.Pick(name), volume * GroupVolume[(int)Group.Interface]);
        }

        // Warnings and news (AA lock, contact, objective, mission over): loudest, never more than one every
        // couple of seconds, and the motors duck under them.
        public static void Alert(string name, float minimumGap = 1.5f)
        {
            if (Instance == null || !Settings.AlertSounds || !Instance.Ready(name, minimumGap)) return;
            Instance.ui.PlayOneShot(Instance.Pick(name), GroupVolume[(int)Group.Warning]);
            Instance.duckUntil = Time.unscaledTime + 1f;
        }

        // A drone's motor: a looping source riding along with it.
        public static AudioSource Motor(Drone drone)
        {
            if (Instance == null) return null;
            var source = World3D(drone.gameObject.AddComponent<AudioSource>(), Scale.D(8f), Scale.D(160f));
            source.spatialBlend = 1f;
            source.clip = Instance.Pick("motor " + drone.type);
            source.loop = true;
            source.volume = MotorVolume;
            source.pitch = Random.Range(0.97f, 1.03f);
            source.time = Random.Range(0f, source.clip.length * 0.9f);
            source.Play();
            Instance.motors.Add(source);
            return source;
        }

        void PlayAt(string name, Vector3 position, float volume, float pitchJitter, float minimumGap)
        {
            if (!Ready(name, minimumGap)) return;
            var source = pool[nextSource];
            nextSource = (nextSource + 1) % pool.Length;
            source.transform.position = position;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.clip = Pick(name);
            source.volume = volume;
            source.Play();
        }

        bool Ready(string name, float gap)
        {
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(name, out float last) && now - last < gap) return false;
            lastPlayed[name] = now;
            return true;
        }

        // Every sound in the game one after another, with a short gap, as a 16-bit mono WAV file: a reel to listen
        // to outside the game (the auto-test writes one). Loops play a few seconds each.
        public static void ExportReel(string path)
        {
            if (Instance == null) return;
            string[] order =
            {
                "select", "move", "attack", "invalid", "click", "launch", "contact", "warning", "alarm", "objective",
                "gunshot", "cannon", "missile", "whistle", "ricochet", "explosion", "big explosion", "drone down",
                "motor Scout", "motor Strike", "motor Kamikaze", "motor Bomber", "success", "failure",
            };
            var samples = new List<float>();
            void Append(AudioClip clip, float maxSeconds)
            {
                var data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                // Mono at the reel's rate (recorded clips can be stereo, and at another rate).
                int channels = clip.channels;
                int frames = Mathf.Min(clip.samples, (int)(maxSeconds * clip.frequency));
                int outFrames = (int)((long)frames * Synth.Rate / clip.frequency);
                for (int i = 0; i < outFrames; i++)
                {
                    int f = (int)((long)i * clip.frequency / Synth.Rate);
                    float sum = 0f;
                    for (int c = 0; c < channels; c++) sum += data[f * channels + c];
                    samples.Add(sum / channels);
                }
                samples.AddRange(new float[(int)(0.45f * Synth.Rate)]);
            }
            foreach (var name in order)
                foreach (var clip in Instance.clips[name]) Append(clip, name.StartsWith("motor") ? 2f : 6f);
            Append(Instance.wind.clip, 5f);
            Append(Instance.birds.clip, 6f);
            Append(Instance.crickets.clip, 4f);
            foreach (var name in new[] { "rain", "thunder" })
                if (Instance.clips.TryGetValue(name, out var extra))
                    foreach (var clip in extra) Append(clip, 5f);

            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                int rate = Synth.Rate, bytes = samples.Count * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                foreach (float v in samples) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 32000f));
            }
        }

        AudioClip Pick(string name)
        {
            var variants = clips[name];
            return variants[Random.Range(0, variants.Length)];
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // F3: a developer overlay with the numbers that matter for tuning: frames per second (average over the last
    // two seconds, the slowest 1% and the worst frame), CPU and GPU milliseconds (FrameTimingManager), what's in
    // play, and the mission so far. Also the critical alert: a drone under AA fire outranks everything else on
    // screen (a red strip above the event feed).
    public partial class SandboxHud
    {
        bool showTelemetry;
        readonly Queue<float> frameTimes = new Queue<float>();
        readonly FrameTiming[] timing = new FrameTiming[1];
        float cpuMs, gpuMs;

        public void ToggleTelemetry() => showTelemetry = !showTelemetry;

        void Update()
        {
            frameTimes.Enqueue(Time.unscaledDeltaTime);
            while (frameTimes.Count > 240) frameTimes.Dequeue();
            if (!showTelemetry) return;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                cpuMs = Mathf.Lerp(cpuMs, (float)timing[0].cpuMainThreadFrameTime, 0.1f);
                gpuMs = Mathf.Lerp(gpuMs, (float)timing[0].gpuFrameTime, 0.1f);
            }
        }

        void DrawTelemetry()
        {
            if (!showTelemetry || frameTimes.Count == 0) return;
            float s = Scale;
            var times = new List<float>(frameTimes);
            times.Sort();
            float average = 0f;
            foreach (float t in times) average += t;
            average /= times.Count;
            float slowest1 = times[Mathf.Min(times.Count - 1, (int)(times.Count * 0.99f))], worst = times[times.Count - 1];
            int projectiles = FindObjectsByType<Projectile>().Length;
            string text =
                $"<b>{1f / average:0} fps</b>   1% low {1f / slowest1:0}   worst {worst * 1000f:0} ms\n" +
                $"CPU {cpuMs:0.0} ms   GPU {gpuMs:0.0} ms   {Screen.width}x{Screen.height}\n" +
                $"drones {game.drones.Count}   enemies {game.enemies.Count} ({KnownEnemies()} known)   shots {projectiles}\n" +
                (game.Mode == GameMode.Mission
                    ? $"{game.Phase}  {Clock(game.MissionTime)}   launched {game.DronesUsed}   expended {game.dronesExpended}   lost {game.dronesLost}"
                    : $"free play   destroyed {game.enemiesDestroyed}   lost {game.dronesLost}");
            var r = new Rect(12f * s, StatsPanel().yMax + 8f * s, 380f * s, 74f * s);
            Fill(r, new Color(0f, 0f, 0f, 0.65f));
            GUI.Label(new Rect(r.x + 8f * s, r.y + 4f * s, r.width, r.height), text, Tinted(small, new Color(0.7f, 1f, 0.75f)));
        }

        // The debrief map beside the results: the battlefield from above with each drone's path (coloured by type),
        // the base, enemies destroyed (orange crosses) and drones lost (red dots).
        void DrawDebriefMap(Rect panel)
        {
            float s = Scale;
            Fill(panel, ModalColor);
            GUI.Label(new Rect(panel.x, panel.y + 8f * s, panel.width, 20f * s), "<b>DEBRIEF</b>", Tinted(centered, Teal));
            var map = new Rect(panel.x + 12f * s, panel.y + 34f * s, panel.width - 24f * s, panel.width - 24f * s);
            Fill(map, new Color(0.12f, 0.17f, 0.12f, 1f));
            if (MapSnapshot.Picture != null)
            {
                GUI.DrawTexture(map, MapSnapshot.Picture, ScaleMode.StretchToFill, false);
                Fill(map, new Color(0f, 0f, 0f, 0.2f)); // a touch darker, so the paths stand out
            }
            Outline(map, new Color(1f, 1f, 1f, 0.15f), 1f);
            float half = MapSnapshot.Half;
            Vector2 ToMap(Vector2 w) => new Vector2(map.x + (w.x + half) / (2f * half) * map.width, map.yMax - (w.y + half) / (2f * half) * map.height);
            Color[] typeColours = { Teal, new Color(0.45f, 0.75f, 1f), DravinOrange, new Color(0.85f, 0.85f, 0.95f) };
            var last = new Dictionary<int, Vector2>();
            foreach (var (drone, type, at) in game.Tracks)
            {
                Vector2 p = ToMap(at);
                if (last.TryGetValue(drone, out Vector2 prev)) Dashes(prev, p, typeColours[(int)type], 2f * s, 0f, 0f, 0f);
                last[drone] = p;
            }
            Vector3 home = game.basePosition;
            Vector2 b = ToMap(new Vector2(home.x, home.z));
            Stamp(BadgeTexture(), b, 7f * s, Teal);
            foreach (var (at, enemy) in game.Events)
            {
                Vector2 p = ToMap(at);
                if (enemy)
                {
                    Matrix4x4 m = GUI.matrix;
                    GUIUtility.RotateAroundPivot(45f, p);
                    Fill(new Rect(p.x - 5f * s, p.y - 1f * s, 10f * s, 2f * s), DravinOrange);
                    Fill(new Rect(p.x - 1f * s, p.y - 5f * s, 2f * s, 10f * s), DravinOrange);
                    GUI.matrix = m;
                }
                else Dot(p, 4f * s, Hostile);
            }
            // What's still out there that we'd identified: air defence as red diamonds, anything else as amber rings.
            foreach (var e in game.enemies)
            {
                if (e == null || !e.IsAlive || e.Intel < Intel.Identified) continue;
                Vector2 p = ToMap(new Vector2(e.transform.position.x, e.transform.position.z));
                if (e.type == EnemyType.AAGun || e.type == EnemyType.Radar) Diamond(p, 5f * s, Hostile);
                else Ring(p, 4f * s, Amber);
            }
            var key = Tinted(small, small.normal.textColor);
            key.fontSize = Mathf.RoundToInt(12f * s);
            GUI.Label(new Rect(panel.x + 12f * s, map.yMax + 6f * s, panel.width, 18f * s),
                "<color=#49DFCF>Scout</color>  <color=#73BFFF>Strike</color>  <color=#FF7848>Kamikaze</color>  <color=#D9D9F2>Nightjar</color>", key);
            GUI.Label(new Rect(panel.x + 12f * s, map.yMax + 25f * s, panel.width, 18f * s), "x destroyed   ● drone lost   ◊ AA   ○ enemy left", key);
        }

        // The one alert that beats everything: an AA gun shooting at one of our drones. Returns the strip's height.
        float DrawCriticalAlert()
        {
            if (game.UiBlocking) return 0f;
            Drone victim = null;
            foreach (var e in game.enemies)
                if (e != null && e.type == EnemyType.AAGun && e.Engaging && e.TrackedDrone is Drone d && d != null) { victim = d; break; }
            if (victim == null) return 0f;
            float s = Scale;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 10f);
            var r = new Rect((Screen.width - 420f * s) / 2f, 14f * s, 420f * s, 30f * s);
            Fill(r, new Color(0.35f, 0.04f, 0.03f, 0.85f * pulse));
            Outline(r, Hostile, Mathf.Max(1f, 1.5f * s));
            var style = Tinted(centered, Color.white);
            style.fontSize = Mathf.RoundToInt(16f * s);
            GUI.Label(r, $"<b>AA FIRING ON {victim.unitName.ToUpperInvariant()}</b>", style);
            return r.height + 6f * s;
        }
    }
}

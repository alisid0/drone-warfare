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

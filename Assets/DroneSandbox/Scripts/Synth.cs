using System;
using UnityEngine;

namespace DroneSandbox
{
    // Every sound in the game, synthesized in code when the game starts (no recorded audio): noise shaped by
    // filters and envelopes for guns, rockets, blasts and wind; oscillators for motors, birds, crickets and the
    // interface. Loops are made periodic (noise filtered twice round, oscillators on whole cycles of the loop) so
    // they repeat without a click. Sound plays them.
    public static class Synth
    {
        public const int Rate = 44100;
        const float Tau = Mathf.PI * 2f;

        static System.Random rng = new System.Random(1);
        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        static float Rand(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        // One-pole filter coefficient for a cutoff in Hz.
        static float Alpha(float cutoff) => 1f - Mathf.Exp(-Tau * Mathf.Clamp(cutoff, 5f, Rate * 0.45f) / Rate);

        static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

        static AudioClip Clip(string name, float[] data, float peak = 0.9f)
        {
            float max = 1e-6f;
            foreach (float v in data) max = Mathf.Max(max, Mathf.Abs(v));
            float gain = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // A short fade at both ends so one-shots never click.
        static void Fades(float[] d, float inSeconds = 0.002f, float outSeconds = 0.01f)
        {
            int a = Mathf.Min(d.Length, (int)(inSeconds * Rate)), b = Mathf.Min(d.Length, (int)(outSeconds * Rate));
            for (int i = 0; i < a; i++) d[i] *= i / (float)a;
            for (int i = 0; i < b; i++) d[d.Length - 1 - i] *= i / (float)b;
        }

        static float SoftClip(float x) => (float)Math.Tanh(x);

        // ---- Combat ----

        // A blast: a crack, a rumble whose filter closes as it dies, a falling sub thump and debris pattering down.
        public static AudioClip Explosion(int seed, float size)
        {
            rng = new System.Random(100 + seed);
            float seconds = 1.6f + size * 0.5f;
            var d = Buffer(seconds);
            float lp1 = 0f, lp2 = 0f, hpPrev = 0f, hpOut = 0f, phase = 0f, debris = 0f;
            float debrisEnv = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise();
                float cutoff = 110f + 1900f * Mathf.Exp(-t * 5f) / Mathf.Max(0.6f, size * 0.6f);
                float a = Alpha(cutoff);
                lp1 += a * (n - lp1);
                lp2 += a * (lp1 - lp2);
                float rumble = lp2 * 3.2f * (Mathf.Exp(-t * 2.4f) + 0.35f * Mathf.Exp(-t * 0.7f)) * Mathf.Min(1f, t / 0.004f);
                // The crack: high-passed noise for the first few hundredths of a second.
                hpOut = 0.97f * (hpOut + n - hpPrev);
                hpPrev = n;
                float crack = hpOut * Mathf.Exp(-t * 90f) * 0.9f;
                // The sub: a sine falling from 70 to 30 Hz.
                float f = 30f + 45f * Mathf.Exp(-t * 2f);
                phase += Tau * f / Rate;
                float sub = Mathf.Sin(phase) * Mathf.Exp(-t * 3.2f) * 0.9f;
                // Debris: sparse little ticks thinning out.
                if (t > 0.12f && rng.NextDouble() < 0.0016 * Mathf.Exp(-t * 1.6f) * size) debrisEnv = Rand(0.2f, 0.6f);
                debrisEnv *= 0.993f;
                debris += 0.5f * (n * debrisEnv - debris);
                d[i] = SoftClip((rumble + crack + sub + debris * 0.5f) * 1.4f);
            }
            Fades(d, 0.001f, 0.15f);
            return Clip("Explosion " + seed, d);
        }

        // A light machine-gun round: a sharp crack over a short body, and a click of the action.
        public static AudioClip Gunshot(int seed)
        {
            rng = new System.Random(200 + seed);
            var d = Buffer(0.2f);
            float hpPrev = 0f, hp = 0f, lp = 0f, tail = 0f, phase = 0f;
            float aLp = Alpha(Rand(4200f, 5600f)), aTail = Alpha(700f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise();
                hp = 0.92f * (hp + n - hpPrev);
                hpPrev = n;
                lp += aLp * (hp - lp);
                tail += aTail * (n - tail);
                phase += Tau * (150f - t * 300f) / Rate;
                d[i] = lp * Mathf.Exp(-t * 60f) * 1.1f + Mathf.Sin(phase) * Mathf.Exp(-t * 45f) * 0.6f
                     + tail * Mathf.Exp(-t * 20f) * 0.8f + (i < 120 ? Noise() * 0.5f : 0f);
            }
            Fades(d);
            return Clip("Gunshot " + seed, d);
        }

        // A 35 mm anti-aircraft round: a heavier, lower thump with a longer tail.
        public static AudioClip Cannon(int seed)
        {
            rng = new System.Random(300 + seed);
            var d = Buffer(0.5f);
            float lp = 0f, lp2 = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise();
                lp += Alpha(400f + 2200f * Mathf.Exp(-t * 30f)) * (n - lp);
                lp2 += Alpha(500f) * (lp - lp2);
                phase += Tau * (85f - t * 60f) / Rate;
                d[i] = SoftClip((lp * Mathf.Exp(-t * 22f) * 1.8f + lp2 * Mathf.Exp(-t * 7f) * 1.2f + Mathf.Sin(phase) * Mathf.Exp(-t * 14f)) * 1.3f);
            }
            Fades(d);
            return Clip("Cannon " + seed, d);
        }

        // A missile leaving its rail: a pop, then a rushing burn rising in pitch and fading as it goes.
        public static AudioClip MissileLaunch()
        {
            rng = new System.Random(400);
            var d = Buffer(1.1f);
            float hp = 0f, hpPrev = 0f, lp = 0f, pop = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise();
                float centre = 500f + 1300f * Mathf.Clamp01(t / 0.7f);
                hp = (1f - Alpha(centre * 0.5f)) * (hp + n - hpPrev);
                hpPrev = n;
                lp += Alpha(centre * 1.8f) * (hp - lp);
                float burn = lp * Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-Mathf.Max(0f, t - 0.25f) * 2.6f) * 2.2f;
                pop += Alpha(900f) * (n - pop);
                d[i] = burn + pop * Mathf.Exp(-t * 50f) * 2f;
            }
            Fades(d, 0.001f, 0.1f);
            return Clip("Missile", d);
        }

        // A falling bomb's whistle, rising in loudness as the pitch drops.
        public static AudioClip BombWhistle()
        {
            rng = new System.Random(500);
            var d = Buffer(1.15f);
            float phase = 0f, air = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = 650f + 900f * Mathf.Exp(-t * 1.6f) + 12f * Mathf.Sin(t * Tau * 6f);
                phase += Tau * f / Rate;
                air += Alpha(2500f) * (Noise() - air);
                float env = Mathf.Pow(t / 1.15f, 1.5f);
                d[i] = (Mathf.Sin(phase) * 0.8f + air * 0.25f) * env;
            }
            Fades(d, 0.01f, 0.02f);
            return Clip("Whistle", d, 0.7f);
        }

        // A round hitting armour: a bright tick with a short metallic ring.
        public static AudioClip Ricochet(int seed)
        {
            rng = new System.Random(600 + seed);
            var d = Buffer(0.25f);
            float lp = 0f, f = Rand(1900f, 3200f), phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                lp += Alpha(5000f) * (Noise() - lp);
                phase += Tau * f * (1f - t * 0.15f) / Rate;
                d[i] = lp * Mathf.Exp(-t * 120f) + Mathf.Sin(phase) * Mathf.Exp(-t * 28f) * 0.35f;
            }
            Fades(d);
            return Clip("Ricochet " + seed, d, 0.7f);
        }

        // A drone breaking up: electrical crackle and its motors winding down.
        public static AudioClip DroneDown()
        {
            rng = new System.Random(700);
            var d = Buffer(1.4f);
            float phase = 0f, lp = 0f, gate = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = 70f + 330f * Mathf.Exp(-t * 2.5f);
                phase += Tau * f / Rate;
                float whine = (Mathf.Sin(phase) + 0.4f * Mathf.Sin(phase * 2f) + 0.25f * Mathf.Sin(phase * 3f)) * Mathf.Exp(-t * 1.6f);
                if (rng.NextDouble() < 0.004) gate = 1f;
                gate *= 0.995f;
                lp += Alpha(3000f) * (Noise() - lp);
                float buzz = Mathf.Sign(Mathf.Sin(t * Tau * 95f)) * gate * 0.4f;
                d[i] = whine * 0.6f + (lp * 1.4f + buzz) * gate * Mathf.Exp(-t * 1.2f);
            }
            Fades(d, 0.002f, 0.1f);
            return Clip("Drone down", d);
        }

        // ---- Loops ----

        // A multirotor's hum: the blade-pass tone of four slightly different rotors (so they beat against each
        // other) over the rush of air. One loop of `seconds`, every partial a whole number of cycles in it.
        public static AudioClip Motor(string name, float pitch, float seconds = 2f)
        {
            rng = new System.Random(name.GetHashCode());
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            float step = 1f / seconds;
            float[] detune = { -0.014f, -0.005f, 0.006f, 0.015f };
            foreach (float k in detune)
            {
                float f = Mathf.Round(pitch * (1f + k) / step) * step;
                float offset = Rand(0f, Tau);
                for (int h = 1; h <= 7; h++)
                {
                    float amp = 1f / Mathf.Pow(h, 1.25f) * (h == 2 ? 1.4f : 1f);
                    float w = Tau * f * h / Rate;
                    for (int i = 0; i < n; i++) d[i] += Mathf.Sin(w * i + offset * h) * amp * 0.25f;
                }
            }
            var air = PeriodicNoise(n, pitch * 1.5f, pitch * 6f);
            for (int i = 0; i < n; i++) d[i] = d[i] * 0.8f + air[i] * 0.9f;
            return Clip(name, d, 0.6f);
        }

        // Wind over open country: low rushing noise in slow gusts, a loop of `seconds`.
        public static AudioClip Wind(float seconds = 12f)
        {
            rng = new System.Random(800);
            int n = Mathf.CeilToInt(seconds * Rate);
            var low = PeriodicNoise(n, 20f, 380f);
            var high = PeriodicNoise(n, 600f, 1800f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float x = i / (float)n;
                float gust = 0.55f + 0.3f * Mathf.Sin(x * Tau * 2f) + 0.15f * Mathf.Sin(x * Tau * 5f + 1.3f);
                d[i] = low[i] * gust + high[i] * gust * gust * 0.25f;
            }
            return Clip("Wind", d, 0.6f);
        }

        // Daytime birdsong: a few phrases of quick rising and falling chirps spread over the loop.
        public static AudioClip Birds(float seconds = 14f)
        {
            rng = new System.Random(900);
            var d = new float[Mathf.CeilToInt(seconds * Rate)];
            int phrases = 7;
            for (int p = 0; p < phrases; p++)
            {
                float start = seconds * (p + Rand(0.05f, 0.6f)) / phrases;
                int notes = rng.Next(2, 6);
                float baseF = Rand(2600f, 4200f), gain = Rand(0.25f, 0.8f);
                for (int k = 0; k < notes; k++)
                {
                    float noteStart = start + k * Rand(0.09f, 0.16f), length = Rand(0.05f, 0.11f);
                    float f0 = baseF * Rand(0.9f, 1.15f), f1 = f0 * Rand(0.75f, 1.35f);
                    float phase = 0f;
                    int a = (int)(noteStart * Rate), b = Mathf.Min(d.Length, a + (int)(length * Rate));
                    for (int i = a; i < b; i++)
                    {
                        float u = (i - a) / (float)(b - a);
                        float f = Mathf.Lerp(f0, f1, u) + 180f * Mathf.Sin(u * Tau * 3f);
                        phase += Tau * f / Rate;
                        d[i] += Mathf.Sin(phase) * Mathf.Sin(u * Mathf.PI) * gain;
                    }
                }
            }
            return Clip("Birds", d, 0.5f);
        }

        // Crickets at night: trains of short high pulses from two crickets at different rates.
        public static AudioClip Crickets(float seconds = 6f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            foreach (var (f, every, gain) in new[] { (4300f, 0.5f, 1f), (4700f, 0.75f, 0.6f) })
            {
                float w = Tau * f / Rate;
                for (float chirp = 0.05f; chirp < seconds - 0.1f; chirp += every)
                    for (int pulse = 0; pulse < 3; pulse++)
                    {
                        int a = (int)((chirp + pulse * 0.03f) * Rate), b = Mathf.Min(n, a + (int)(0.016f * Rate));
                        for (int i = a; i < b; i++) d[i] += Mathf.Sin(w * i) * Mathf.Sin((i - a) / (float)(b - a) * Mathf.PI) * gain;
                    }
            }
            return Clip("Crickets", d, 0.5f);
        }

        // White noise band-passed between two cutoffs, periodic over n samples: the filters run round the loop
        // twice and the second pass is kept, so the end flows into the start.
        static float[] PeriodicNoise(int n, float lowCut, float highCut)
        {
            var white = new float[n];
            for (int i = 0; i < n; i++) white[i] = Noise();
            var d = new float[n];
            float aHigh = Alpha(highCut), aLow = Alpha(lowCut), lp = 0f, slow = 0f;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    lp += aHigh * (white[i] - lp);
                    slow += aLow * (lp - slow);
                    if (pass == 1) d[i] = (lp - slow) * 2.5f;
                }
            return d;
        }

        // ---- Interface ----

        // A sequence of soft tones: (frequency, start, length) each, with a bell-like decay.
        static AudioClip Tones(string name, float seconds, (float f, float start, float length)[] notes, float harmonics = 0.2f, float peak = 0.7f)
        {
            var d = Buffer(seconds);
            foreach (var (f, start, length) in notes)
            {
                int a = (int)(start * Rate), b = Mathf.Min(d.Length, a + (int)(length * Rate));
                float w = Tau * f / Rate;
                for (int i = a; i < b; i++)
                {
                    float t = (i - a) / (float)Rate;
                    float env = Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t * 6f / length);
                    float x = w * (i - a);
                    d[i] += (Mathf.Sin(x) + harmonics * Mathf.Sin(x * 3f) + harmonics * 0.5f * Mathf.Sin(x * 2f)) * env;
                }
            }
            Fades(d);
            return Clip(name, d, peak);
        }

        public static AudioClip Select() => Tones("Select", 0.18f, new[] { (880f, 0f, 0.06f), (1320f, 0.055f, 0.1f) });
        public static AudioClip Move() => Sweep("Move", 1250f, 820f, 0.12f, 0.15f);
        public static AudioClip AttackOrder() => Tones("Attack order", 0.2f, new[] { (620f, 0f, 0.06f), (930f, 0.06f, 0.12f) }, 0.6f);
        public static AudioClip Invalid() => Tones("Invalid", 0.2f, new[] { (180f, 0f, 0.16f) }, 0.9f, 0.5f);
        public static AudioClip Click() => Tones("Click", 0.04f, new[] { (2100f, 0f, 0.03f) }, 0f, 0.4f);
        public static AudioClip Contact() => Tones("Contact", 0.5f, new[] { (1850f, 0f, 0.3f), (1850f, 0.16f, 0.25f) }, 0.05f);
        public static AudioClip Warning() => Tones("AA warning", 0.5f, new[] { (1000f, 0f, 0.13f), (1000f, 0.2f, 0.13f) }, 0.9f, 0.8f);
        public static AudioClip Alarm() => Tones("Alarm", 0.75f, new[] { (700f, 0f, 0.12f), (950f, 0.13f, 0.12f), (700f, 0.26f, 0.12f), (950f, 0.39f, 0.2f) }, 0.8f, 0.7f);
        public static AudioClip Objective() => Tones("Objective", 1.3f, new[] { (523.25f, 0f, 0.5f), (659.25f, 0.13f, 0.5f), (783.99f, 0.26f, 0.9f) });

        public static AudioClip Success() => Tones("Success", 4.2f, new[]
        {
            (392f, 0f, 0.6f), (523.25f, 0.18f, 0.6f), (659.25f, 0.36f, 0.6f), (783.99f, 0.54f, 2.8f),
            (523.25f, 0.54f, 3.2f), (659.25f, 0.54f, 3.2f), (261.63f, 0.54f, 3.4f),
        }, 0.15f);

        public static AudioClip Failure() => Tones("Failure", 3.6f, new[]
        {
            (440f, 0f, 0.7f), (349.23f, 0.35f, 0.7f), (293.66f, 0.7f, 2.6f), (220f, 0.7f, 2.8f), (174.61f, 0.7f, 2.8f),
        }, 0.1f);

        // A launch: a rising whoosh of air as the drone climbs off its pad.
        public static AudioClip Launch()
        {
            rng = new System.Random(1000);
            var d = Buffer(0.7f);
            float hp = 0f, hpPrev = 0f, lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float c = 400f + 2400f * t / 0.7f;
                float n = Noise();
                hp = (1f - Alpha(c * 0.6f)) * (hp + n - hpPrev);
                hpPrev = n;
                lp += Alpha(c * 1.6f) * (hp - lp);
                d[i] = lp * Mathf.Sin(Mathf.PI * t / 0.7f);
            }
            Fades(d);
            return Clip("Launch", d, 0.6f);
        }

        static AudioClip Sweep(string name, float from, float to, float seconds, float harmonics)
        {
            var d = Buffer(seconds);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                phase += Tau * Mathf.Lerp(from, to, t / seconds) / Rate;
                d[i] = (Mathf.Sin(phase) + harmonics * Mathf.Sin(phase * 3f)) * Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t * 14f);
            }
            Fades(d);
            return Clip(name, d, 0.7f);
        }
    }
}

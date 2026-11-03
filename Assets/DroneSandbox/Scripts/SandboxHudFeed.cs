using UnityEngine;

namespace DroneSandbox
{
    // The drone camera window (DroneFeed): the picture, framed like a ground-station screen, with a REC light,
    // whose feed it is, altitude, speed and heading, a crosshair, and what it's doing. When the drone it was
    // showing is lost the window fills with static and SIGNAL LOST for a moment.
    public partial class SandboxHud
    {
        static Texture2D[] staticFrames;

        Rect FeedPanel()
        {
            float s = Scale * (DroneFeed.Instance != null && DroneFeed.Instance.Inspecting ? 1.5f : 1f); // bigger while inspecting
            float w = DroneFeed.Width * s, h = DroneFeed.Height * s;
            return new Rect(Screen.width - w - 12f * s, Screen.height - h - 12f * s, w, h);
        }

        // The enemy under the cursor in the drone feed's picture, found by where it sits in the drone camera's view
        // (only what we already know about: picking it out of the picture doesn't identify anything), or null.
        public EnemyUnit FeedPick(Vector2 screenPosition)
        {
            var f = DroneFeed.Instance;
            if (!FeedShowing || f.Source == null || f.FeedCamera == null) return null;
            Rect r = FeedPanel();
            var g = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            if (!r.Contains(g)) return null;
            float u = (g.x - r.x) / r.width, v = 1f - (g.y - r.y) / r.height;
            EnemyUnit best = null;
            float bestPixels = 22f * Scale;
            foreach (var e in game.enemies)
            {
                if (e == null || !e.IsAlive || e.Hidden || e.Intel == Intel.Unknown) continue;
                Vector3 at = f.FeedCamera.WorldToViewportPoint(e.AimPoint);
                if (at.z <= 0f) continue;
                float pixels = new Vector2((at.x - u) * r.width, (at.y - v) * r.height).magnitude;
                if (pixels < bestPixels)
                {
                    best = e;
                    bestPixels = pixels;
                }
            }
            return best;
        }

        bool FeedShowing
        {
            get
            {
                var f = DroneFeed.Instance;
                return f != null && f.Visible && game.Mode != GameMode.Menu && !PhotoMode.Active
                       && (f.Source != null || Time.unscaledTime - f.LostAt < 0.85f);
            }
        }

        void DrawFeed()
        {
            if (!FeedShowing) return;
            var f = DroneFeed.Instance;
            float s = Scale;
            Rect r = FeedPanel();
            Fill(new Rect(r.x - 3f * s, r.y - 20f * s, r.width + 6f * s, r.height + 23f * s), new Color(Ink.r, Ink.g, Ink.b, 0.92f));
            // The last picture holds for a moment, then the static.
            float sinceLost = Time.unscaledTime - f.LostAt;
            bool lost = sinceLost >= 0.15f && sinceLost < 0.85f;
            if (sinceLost < 0.15f && f.Picture != null)
            {
                GUI.DrawTexture(r, f.Picture, ScaleMode.StretchToFill, false);
                return;
            }
            var title = Tinted(small, Teal);
            title.fontStyle = FontStyle.Bold;
            if (lost)
            {
                GUI.DrawTexture(r, StaticFrame());
                var big = Tinted(centered, Hostile);
                big.fontSize = Mathf.RoundToInt(18f * s);
                GUI.Label(r, "<b>SIGNAL LOST</b>", big);
                GUI.Label(new Rect(r.x + 6f * s, r.y - 18f * s, r.width, 16f * s), $"{f.LostName?.ToUpperInvariant()} FEED", title);
                return;
            }
            var d = f.Source;
            if (d == null || f.Picture == null) return;
            GUI.DrawTexture(r, f.Picture, ScaleMode.StretchToFill, false);

            // Header: a blinking REC light, whose feed, the clock.
            bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
            if (blink) Dot(new Vector2(r.x + 10f * s, r.y - 10f * s), 4f * s, Hostile);
            GUI.Label(new Rect(r.x + 18f * s, r.y - 18f * s, r.width, 16f * s), $"{d.unitName.ToUpperInvariant()} FEED", title);
            var clock = Tinted(small, Teal);
            clock.alignment = TextAnchor.UpperRight;
            string time = DayNight.Instance != null ? DayNight.Instance.Clock : "";
            GUI.Label(new Rect(r.x, r.y - 18f * s, r.width - 6f * s, 16f * s), time + (DayNight.Instance != null && DayNight.Instance.Thermal ? "  WHT" : "  EO"), clock);

            // Known enemies the drone can see get brackets and their contact number in the picture, the same number
            // as on the map.
            var cam = f.FeedCamera;
            if (cam != null)
            {
                foreach (var e in game.enemies)
                {
                    if (e == null || !e.Drawn || !e.Known || e.ContactId == null) continue;
                    Vector3 v = cam.WorldToViewportPoint(e.AimPoint);
                    if (v.z <= 0f || v.x < 0.03f || v.x > 0.97f || v.y < 0.06f || v.y > 0.97f) continue;
                    var p = new Vector2(r.x + v.x * r.width, r.y + (1f - v.y) * r.height);
                    float half = Mathf.Clamp(e.radius * 120f / Mathf.Max(4f, v.z), 6f, 30f) * s;
                    Color colour = e.ExposedDecoy ? new Color(0.75f, 0.78f, 0.8f) : e.LooksLikeTarget ? TargetColor : DravinOrange;
                    Brackets(p, half, colour, Mathf.Max(1f, s));
                    var tag = Tinted(small, colour);
                    tag.fontSize = Mathf.RoundToInt(9f * s);
                    GUI.Label(new Rect(p.x - half, p.y - half - 13f * s, 120f * s, 12f * s), $"<b>{e.ContactId}</b> {(e.ExposedDecoy ? "DECOY" : e.unitName.ToUpperInvariant())}", tag);
                }
            }

            // A crosshair, and the corners of the frame.
            Vector2 c = r.center;
            var line = new Color(1f, 1f, 1f, 0.7f);
            Fill(new Rect(c.x - 14f * s, c.y - 0.5f * s, 9f * s, 1f * s), line);
            Fill(new Rect(c.x + 5f * s, c.y - 0.5f * s, 9f * s, 1f * s), line);
            Fill(new Rect(c.x - 0.5f * s, c.y - 14f * s, 1f * s, 9f * s), line);
            Fill(new Rect(c.x - 0.5f * s, c.y + 5f * s, 1f * s, 9f * s), line);
            Brackets(c, r.height * 0.42f, new Color(1f, 1f, 1f, 0.35f), Mathf.Max(1f, s));

            // Telemetry along the bottom.
            float altitude = d.transform.position.y - Ground.HeightAt(d.transform.position);
            float heading = Mathf.Repeat(d.transform.eulerAngles.y, 360f);
            var tele = Tinted(small, new Color(0.9f, 1f, 0.95f));
            var shadow = Tinted(small, new Color(0f, 0f, 0f, 0.8f));
            string text = $"ALT {altitude:0} m   HDG {heading:000}°   HP {Mathf.CeilToInt(d.hp)}   {d.StatusTag}";
            var at = new Rect(r.x + 6f * s, r.yMax - 18f * s, r.width, 16f * s);
            GUI.Label(new Rect(at.x + 1f, at.y + 1f, at.width, at.height), text, shadow);
            GUI.Label(at, text, tele);
            if (f.Inspecting || f.Zoom > 1.01f)
            {
                var zoom = Tinted(small, Teal);
                zoom.alignment = TextAnchor.UpperRight;
                GUI.Label(new Rect(r.x, r.y + 4f * s, r.width - 8f * s, 16f * s), $"<b>INSPECT  {f.Zoom:0.0}x</b>", zoom);
            }
            if (d.UnderFire)
            {
                var warn = Tinted(centered, Hostile);
                GUI.Label(new Rect(r.x, r.y + 6f * s, r.width, 18f * s), "<b>UNDER FIRE</b>", warn);
            }
        }

        // A few frames of grey static, cycled.
        static Texture2D StaticFrame()
        {
            if (staticFrames == null)
            {
                staticFrames = new Texture2D[4];
                var rng = new System.Random(5);
                for (int k = 0; k < staticFrames.Length; k++)
                {
                    const int w = 128, h = 72;
                    var pixels = new Color32[w * h];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        byte v = (byte)(rng.Next(30, 200) * (i / w % 3 == 0 ? 0.7f : 1f));
                        pixels[i] = new Color32(v, v, v, 255);
                    }
                    staticFrames[k] = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                    staticFrames[k].SetPixels32(pixels);
                    staticFrames[k].Apply();
                }
            }
            return staticFrames[(int)(Time.unscaledTime * 24f) % staticFrames.Length];
        }
    }
}

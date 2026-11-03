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
            float s = Scale;
            float w = DroneFeed.Width * s, h = DroneFeed.Height * s;
            return new Rect(Screen.width - w - 12f * s, Screen.height - h - 12f * s, w, h);
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

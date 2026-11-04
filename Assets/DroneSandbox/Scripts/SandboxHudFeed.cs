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

        // The bird nearest the cursor in the drone's picture, if the cursor is over the feed (Mimic).
        public Bird FeedPickBird(Vector2 screenPosition)
        {
            var f = DroneFeed.Instance;
            if (!FeedShowing || f.Source == null || f.FeedCamera == null) return null;
            Rect r = FeedPanel();
            var g = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            if (!r.Contains(g)) return null;
            float u = (g.x - r.x) / r.width, v = 1f - (g.y - r.y) / r.height;
            Bird best = null;
            float bestPixels = 26f * Scale;
            foreach (var b in Wildlife.Birds)
            {
                if (b == null || b.Down || !b.gameObject.activeInHierarchy) continue;
                Vector3 at = f.FeedCamera.WorldToViewportPoint(b.transform.position);
                if (at.z <= 0f || at.z > 150f) continue;
                float pixels = new Vector2((at.x - u) * r.width, (at.y - v) * r.height).magnitude;
                if (pixels < bestPixels)
                {
                    best = b;
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

            // Birds in the picture (Mimic): a faint mark on each within 150 m, the tracked one bracketed with what
            // we know of it.
            if (cam != null && Mimic.Active)
            {
                foreach (var b in Wildlife.Birds)
                {
                    if (b == null || b.Down || !b.gameObject.activeInHierarchy) continue;
                    Vector3 v = cam.WorldToViewportPoint(b.transform.position);
                    if (v.z <= 0f || v.z > 150f || v.x < 0.03f || v.x > 0.97f || v.y < 0.06f || v.y > 0.97f) continue;
                    var p = new Vector2(r.x + v.x * r.width, r.y + (1f - v.y) * r.height);
                    var ev = Mimic.Of(b);
                    if (b != Mimic.Tracked)
                    {
                        Dot(p, 2f * s, ev.Confirmed ? Hostile : ev.Suspicious ? DravinOrange : new Color(1f, 1f, 1f, 0.6f));
                        continue;
                    }
                    Color colour = ev.Confirmed ? Hostile : ev.Suspicious ? DravinOrange : Color.white;
                    float half = Mathf.Clamp(60f / Mathf.Max(4f, v.z), 12f, 26f) * s;
                    Brackets(p, half, colour, Mathf.Max(1f, s));
                    var tag = Tinted(small, colour);
                    tag.fontSize = Mathf.RoundToInt(9f * s);
                    Fill(new Rect(p.x - half - 2f * s, p.y - half - 13f * s, (ev.Occluded ? 78f : 30f) * s, 12f * s), new Color(Ink.r, Ink.g, Ink.b, 0.65f));
                    GUI.Label(new Rect(p.x - half, p.y - half - 13f * s, 200f * s, 12f * s), $"<b>{v.z:0} m</b>" + (ev.Occluded ? "  OCCLUDED" : ""), tag);
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

            // The hunt (Mimic): above the feed, what we know of the tracked contact at a glance: how sure we are, which
            // kinds of evidence are in (lit), how long it's been watched, and once it's confirmed, how steady the aim is.
            if (Mimic.Active)
            {
                var tracked = Mimic.Tracked;
                var ev = tracked != null ? Mimic.Of(tracked) : null;
                var panel = new Rect(r.x - 3f * s, r.y - 80f * s, r.width + 6f * s, 58f * s);
                Fill(panel, new Color(Ink.r, Ink.g, Ink.b, 0.85f));
                float x0 = panel.x + 6f * s, w0 = panel.width - 12f * s;
                var quiet = Tinted(small, Teal);
                quiet.fontSize = Mathf.RoundToInt(10f * s);
                if (ev == null)
                {
                    GUI.Label(new Rect(x0, panel.y + 4f * s, w0, 16f * s), "<b>HUNT</b>   click a bird in the feed to track it", Tinted(small, Color.white));
                    GUI.Label(new Rect(x0, panel.y + 22f * s, w0, 16f * s), "Some of these crows are machines: watch one until it gives itself away", quiet);
                }
                else
                {
                    Color state = ev.Confirmed ? Hostile : ev.Suspicious ? DravinOrange : Color.white;
                    if (ev.Confirmed) Fill(new Rect(panel.x, panel.y, panel.width, 18f * s), new Color(Hostile.r, Hostile.g, Hostile.b, 0.16f));
                    GUI.Label(new Rect(x0, panel.y + 2f * s, w0, 16f * s), $"<b>{ev.Status}</b>", Tinted(small, state));
                    // What's in, against what confirmation needs: 3 points from 2 kinds.
                    var count = Tinted(quiet, Color.white);
                    count.alignment = TextAnchor.UpperRight;
                    GUI.Label(new Rect(x0, panel.y + 3f * s, w0, 16f * s),
                        $"EVIDENCE  <b>{ev.Points}</b>/3 pts   <b>{ev.KindCount}</b>/2 types", count);
                    // The five kinds of evidence, lit as they come in.
                    string[] kinds = { "MOVE", "MECH", "SOUND", "HEAT", "RADIO" };
                    float chipWidth = 42f * s, cx = x0;
                    var chip = Tinted(small, Color.white);
                    chip.fontSize = Mathf.RoundToInt(9f * s);
                    chip.alignment = TextAnchor.MiddleCenter;
                    for (int k = 0; k < kinds.Length; k++)
                    {
                        var box = new Rect(cx, panel.y + 21f * s, chipWidth - 4f * s, 14f * s);
                        bool lit = (ev.Kinds & (1 << k)) != 0, fresh = lit && Time.time - ev.KindAt[k] < 0.6f;
                        if (lit) Fill(box, fresh ? Color.white : ev.Confirmed ? Hostile : DravinOrange);
                        else Outline(box, new Color(1f, 1f, 1f, 0.3f), 1f);
                        chip.normal.textColor = lit ? Ink : new Color(1f, 1f, 1f, 0.45f);
                        GUI.Label(box, $"<b>{kinds[k]}</b>", chip);
                        cx += chipWidth;
                    }
                    GUI.Label(new Rect(cx + 4f * s, panel.y + 21f * s, panel.xMax - cx - 10f * s, 14f * s),
                        $"watched {Mathf.Min(ev.Watched, Evidence.MinWatch):0}/{Evidence.MinWatch:0} s" + (ev.Points > 0 && !ev.Sustained ? "  need a steady clue" : ""), quiet);
                    // The last row: the aim once it's confirmed, otherwise the latest clue.
                    var row = new Rect(x0, panel.y + 39f * s, w0, 16f * s);
                    if (ev.Confirmed)
                    {
                        bool ready = Mimic.WouldHit(tracked, d);
                        GUI.Label(row, "<b>AIM</b>", Tinted(small, Color.white));
                        var aim = new Rect(x0 + 32f * s, row.y + 5f * s, 70f * s, 6f * s);
                        Fill(aim, new Color(1f, 1f, 1f, 0.12f));
                        Fill(new Rect(aim.x, aim.y, aim.width * ev.Aim, aim.height), ready ? Teal : DravinOrange);
                        string advice = Mimic.AimAdvice(tracked, d);
                        GUI.Label(new Rect(aim.xMax + 8f * s, row.y, panel.xMax - aim.xMax - 70f * s, 16f * s),
                            ready ? "<b>CLEAR SHOT: right-click to fire</b>" : advice,
                            Tinted(quiet, ready ? Teal : advice.StartsWith("OBSTRUCTED") ? Hostile : DravinOrange));
                    }
                    else GUI.Label(new Rect(x0, row.y, w0 - 60f * s, row.height), (ev.Found.Count > 0 ? ev.Found[ev.Found.Count - 1] : "no clues yet") + "     right-click to shadow it", quiet);
                    var shots = Tinted(quiet, Color.white);
                    shots.alignment = TextAnchor.UpperRight;
                    GUI.Label(row, $"shots {Mimic.ShotsLeftFor(d)}/{Mimic.Shots}", shots);
                }
                // A robot on the run, whichever one it is: how long before it's gone.
                foreach (var b in Wildlife.Birds)
                {
                    if (b == null || !b.Robot || !b.Spooked || b.Down || b.Escaped) continue;
                    GUI.Label(new Rect(r.x, r.y + 24f * s, r.width, 18f * s), $"<b>CONTACT RUNNING: gone in {b.EscapeLeft:0} s</b>", Tinted(centered, Hostile));
                    break;
                }
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

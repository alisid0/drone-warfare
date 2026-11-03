using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The HUD's danger and planning layer (after ChatGPT's second review: show what threatens you before it hits).
    // - Ground zones: known AA reach as a muted red disc with a dashed edge, search areas and rough contacts as faint
    //   amber discs, and each selected Scout's sensor footprint (what it can see: less at night, more with thermal).
    // - Threat arcs: a ring filling round a drone an AA gun is laying on, amber then orange then red as it gets ready
    //   to fire; a red line from a known gun that's firing; a red marker at the screen edge for a known gun firing
    //   from off screen; a faint pulse from a radar to the guns it's feeding.
    // - Extraction: a teal ring over the base, an arrow to it when it's off screen, and each drone's time home,
    //   amber and then red when it won't make it.
    // - Clutter: drone symbols too close on screen are pushed apart (with a thin line back to where they are), and a
    //   squad with one order shows one route with numbered waypoints and arrows.
    public partial class SandboxHud
    {
        static Texture2D discTexture, dashedDiamondTexture, arrowTexture;
        readonly Dictionary<Drone, Vector2> symbolAt = new Dictionary<Drone, Vector2>();

        // ---- Ground zones ----

        void DrawGroundZones()
        {
            foreach (var e in game.enemies)
            {
                if (e == null) continue;
                if (e.Hidden) GroundDisc(e.searchCenter, SandboxGame.SearchRadius, new Color(Amber.r, Amber.g, Amber.b, 0.1f));
                else if (!e.Drawn && e.Intel == Intel.Contact) GroundDisc(e.ContactPosition, e.ContactRadius, new Color(Amber.r, Amber.g, Amber.b, 0.08f));
                else if (e.type == EnemyType.AAGun && e.Intel >= Intel.Identified && e.Suppressed)
                {
                    // Pinned down: its reach greyed out, and how long it'll keep its head down.
                    GroundDisc(e.transform.position, e.CurrentRange, new Color(0.6f, 0.65f, 0.7f, 0.07f));
                    if (ToGui(e.transform.position + Vector3.up * 4f, out Vector2 g))
                        GUI.Label(new Rect(g.x - 50f * Scale, g.y - 26f * Scale, 100f * Scale, 18f * Scale),
                            $"<b>PINNED {Mathf.CeilToInt(e.SuppressedUntil - Time.time)}</b>", Tinted(centered, Teal));
                }
                else if (e.type == EnemyType.AAGun && e.Intel >= Intel.Identified)
                    GroundDisc(e.transform.position, e.CurrentRange, new Color(Hostile.r, Hostile.g, Hostile.b, game.Paused ? 0.16f : 0.08f));
            }
            // A shadowed column's notice range round the vehicle nearest our drones (the one that matters): amber,
            // going red as the column grows suspicious.
            if (game.IsShadow && game.Phase != MissionPhase.Extract)
            {
                EnemyUnit closest = null;
                float best = float.MaxValue;
                foreach (var e in game.enemies)
                {
                    if (e == null || !e.isTarget || e.Intel < Intel.Identified) continue;
                    foreach (var d in game.drones)
                    {
                        if (d == null) continue;
                        float gap = Vector3.Distance(d.transform.position, e.transform.position);
                        if (gap < best)
                        {
                            best = gap;
                            closest = e;
                        }
                    }
                }
                if (closest != null)
                {
                    Color ring = Color.Lerp(Amber, Hostile, game.ShadowSuspicion);
                    ring.a = 0.6f + 0.4f * game.ShadowSuspicion;
                    GroundRing(closest.transform.position, game.ShadowNoticeRange, ring, 120, 3.5f);
                }
            }
            // What each selected Scout can see.
            float dark = DayNight.Instance != null ? 1f - DayNight.Instance.Daylight : 0f;
            float eyes = (Mathf.Lerp(Recon.IdentifyRange, Recon.NightRange, dark) + (Tech.Active(TechId.Optics) ? Recon.OpticsBonus : 0f)) * Recon.WeatherFactor;
            foreach (var d in rts.selected)
            {
                if (d == null || d.type != DroneType.Scout) continue;
                float reach = d.HasThermal ? Mathf.Max(eyes, Recon.ThermalRange) : eyes;
                GroundRing(d.transform.position, reach, new Color(Teal.r, Teal.g, Teal.b, 0.35f), 40);
            }
        }

        // A filled circle on the ground: a soft disc stretched over where the circle lands on screen.
        void GroundDisc(Vector3 centre, float radius, Color colour)
        {
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            int seen = 0;
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                p.y = Ground.HeightAt(p);
                if (!ToGui(p, out Vector2 g)) continue;
                seen++;
                xMin = Mathf.Min(xMin, g.x); xMax = Mathf.Max(xMax, g.x);
                yMin = Mathf.Min(yMin, g.y); yMax = Mathf.Max(yMax, g.y);
            }
            if (seen < 6) return;
            Color old = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(Rect.MinMaxRect(xMin, yMin, xMax, yMax), DiscTexture());
            GUI.color = old;
        }

        // A dashed ring on the ground.
        void GroundRing(Vector3 centre, float radius, Color colour, int dashes, float dot = 2.5f)
        {
            float size = dot * Scale;
            for (int i = 0; i < dashes; i += 2)
            {
                float a = i * Mathf.PI * 2f / dashes;
                Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                p.y = Ground.HeightAt(p) + 0.1f;
                if (ToGui(p, out Vector2 g)) Fill(new Rect(g.x - size / 2f, g.y - size / 2f, size, size), colour);
            }
        }

        // ---- Threats ----

        void DrawThreats()
        {
            float s = Scale;
            foreach (var e in game.enemies)
            {
                if (e == null || e.type != EnemyType.AAGun || !(e.TrackedDrone is Drone d) || d == null || !d.IsAlive) continue;
                if (!symbolAt.TryGetValue(d, out Vector2 c) && !ToGui(d.AimPoint, out c)) continue;
                // The arc fills as the crew lays its guns: amber, then orange, then red when it's about to fire.
                float p = e.Engaging ? 1f : e.AcquireProgress;
                Color colour = p < 0.5f ? Amber : p < 0.9f ? DravinOrange : Hostile;
                float radius = Mathf.Lerp(22f, 30f, Strategic) * s;
                Arc(c, radius, p, colour);
                if (e.Intel < Intel.Identified) continue; // a gun we haven't found warns, but doesn't say where it is
                bool onScreen = ToGui(e.AimPoint, out Vector2 gun) && gun.x >= 0f && gun.y >= 0f && gun.x <= Screen.width && gun.y <= Screen.height;
                if (e.Engaging && onScreen) Dashes(gun, c, new Color(Hostile.r, Hostile.g, Hostile.b, 0.7f), 1.5f * s, 6f * s, 4f * s, Time.unscaledTime * 60f * s);
                else if (e.Engaging) EdgeArrow(e.AimPoint, Hostile, "AA");
                // A radar feeding it: a faint pulse along the line between them.
                foreach (var r in game.enemies)
                {
                    if (r == null || r.type != EnemyType.Radar || r.Intel < Intel.Identified) continue;
                    Vector3 off = r.transform.position - e.transform.position;
                    off.y = 0f;
                    if (off.magnitude > EnemyUnit.RadarCoverage || !ToGui(r.AimPoint, out Vector2 radar) || !onScreen) continue;
                    float pulse = 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
                    Dashes(radar, gun, new Color(Hostile.r, Hostile.g, Hostile.b, pulse), 1f * s, 3f * s, 5f * s, Time.unscaledTime * 40f * s);
                }
            }
        }

        // A ring of dots round c, filled clockwise from the top to `fraction`.
        void Arc(Vector2 c, float radius, float fraction, Color colour)
        {
            const int dots = 28;
            float size = 3f * Scale;
            int lit = Mathf.CeilToInt(dots * Mathf.Clamp01(fraction));
            for (int i = 0; i < dots; i++)
            {
                float a = i * Mathf.PI * 2f / dots;
                var p = new Vector2(c.x + Mathf.Sin(a) * radius, c.y - Mathf.Cos(a) * radius);
                Fill(new Rect(p.x - size / 2f, p.y - size / 2f, size, size), i < lit ? colour : new Color(colour.r, colour.g, colour.b, 0.15f));
            }
        }

        // A marker at the edge of the screen pointing toward something off screen.
        void EdgeArrow(Vector3 world, Color colour, string label)
        {
            float s = Scale;
            Vector3 v = rts.Cam.WorldToScreenPoint(world);
            var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var dir = new Vector2(v.x, Screen.height - v.y) - centre;
            if (v.z < 0f) dir = -dir;
            if (dir.sqrMagnitude < 1f) return;
            float margin = 34f * s;
            float scale = Mathf.Min((Screen.width / 2f - margin) / Mathf.Max(0.01f, Mathf.Abs(dir.x)), (Screen.height / 2f - margin) / Mathf.Max(0.01f, Mathf.Abs(dir.y)));
            Vector2 at = centre + dir * scale;
            float angle = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg;
            Symbol(at, angle, 26f * s, ArrowTexture(), colour);
            var style = Tinted(small, colour);
            style.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(at.x - 30f * s, at.y + 12f * s, 60f * s, 16f * s), $"<b>{label}</b>", style);
        }

        // ---- Extraction ----

        void DrawExtraction()
        {
            if (game.Mode != GameMode.Mission || game.Phase != MissionPhase.Extract || game.Result != MissionResult.None) return;
            float s = Scale;
            float pulse = 0.55f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f);
            GroundDisc(game.basePosition, SandboxGame.HomeRadius, new Color(Teal.r, Teal.g, Teal.b, 0.12f));
            GroundRing(game.basePosition, SandboxGame.HomeRadius, new Color(Teal.r, Teal.g, Teal.b, pulse), 48);
            bool baseOnScreen = ToGui(game.basePosition + Vector3.up * 3f, out Vector2 b)
                                && b.x > 0f && b.y > 0f && b.x < Screen.width && b.y < Screen.height;
            if (baseOnScreen)
            {
                var style = Tinted(centered, Teal);
                GUI.Label(new Rect(b.x - 80f * s, b.y - 40f * s, 160f * s, 20f * s), "<b>EXTRACT HERE</b>", style);
            }
            else EdgeArrow(game.basePosition, Teal, "BASE");

            // Each drone's time home, coloured by whether it'll make it.
            float left = game.ExtractEndsAt - game.MissionTime;
            foreach (var d in game.drones)
            {
                if (d == null || d.IsKamikaze || !symbolAt.TryGetValue(d, out Vector2 c)) continue;
                Vector3 off = game.basePosition - d.transform.position;
                off.y = 0f;
                if (off.magnitude <= SandboxGame.HomeRadius) continue;
                float eta = off.magnitude / Mathf.Max(1f, d.speed);
                Color colour = eta < left - 10f ? Teal : eta < left ? Amber : Hostile;
                var style = Tinted(small, colour);
                style.alignment = TextAnchor.MiddleLeft;
                GUI.Label(new Rect(c.x + 16f * s, c.y - 8f * s, 60f * s, 16f * s), $"<b>{eta:0} s</b>", style);
            }
        }

        // ---- Decluttering ----

        // Where each drone's symbol goes: on the drone, pushed apart from its neighbours when they'd overlap.
        void PlaceSymbols(float size)
        {
            symbolAt.Clear();
            var drones = new List<Drone>();
            var at = new List<Vector2>();
            var home = new List<Vector2>();
            foreach (var d in game.drones)
            {
                if (d == null || !ToGui(d.AimPoint, out Vector2 c)) continue;
                drones.Add(d);
                at.Add(c);
                home.Add(c);
            }
            float apart = size * 1.15f, maxShift = 22f * Scale;
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 0; i < at.Count; i++)
                    for (int j = i + 1; j < at.Count; j++)
                    {
                        Vector2 v = at[j] - at[i];
                        float d = v.magnitude;
                        if (d >= apart) continue;
                        Vector2 push = (d < 0.01f ? new Vector2(Mathf.Cos(i * 2.4f), Mathf.Sin(i * 2.4f)) : v / d) * (apart - d) * 0.5f;
                        at[i] -= push;
                        at[j] += push;
                    }
                for (int i = 0; i < at.Count; i++) at[i] = home[i] + Vector2.ClampMagnitude(at[i] - home[i], maxShift);
            }
            for (int i = 0; i < drones.Count; i++)
            {
                symbolAt[drones[i]] = at[i];
                if ((at[i] - home[i]).sqrMagnitude > 9f) Dashes(home[i], at[i], new Color(Teal.r, Teal.g, Teal.b, 0.5f), 1f, 3f, 0f, 0f);
            }
        }

        // ---- Generated textures ----

        static Texture2D DiscTexture()
        {
            if (discTexture == null)
                discTexture = Icon(p => (p - new Vector2(0.5f, 0.5f)).magnitude < 0.5f);
            return discTexture;
        }

        static Texture2D DashedDiamondTexture()
        {
            if (dashedDiamondTexture == null)
                dashedDiamondTexture = Icon(p =>
                {
                    float x = p.x - 0.5f, y = p.y - 0.5f;
                    if (Mathf.Abs(Mathf.Abs(x) + Mathf.Abs(y) - 0.42f) >= 0.05f) return false;
                    return Mathf.Repeat((Mathf.Atan2(y, x) + Mathf.PI) / (Mathf.PI * 2f) * 16f, 1f) < 0.55f; // 16 dashes round
                });
            return dashedDiamondTexture;
        }

        static Texture2D ArrowTexture()
        {
            if (arrowTexture == null)
                arrowTexture = Icon(p => InPolygon(p, new[] { new Vector2(0.5f, 0.95f), new Vector2(0.88f, 0.25f), new Vector2(0.5f, 0.42f), new Vector2(0.12f, 0.25f) }));
            return arrowTexture;
        }
    }
}

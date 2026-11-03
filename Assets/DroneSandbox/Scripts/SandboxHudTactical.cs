using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The tactical layer of the HUD: what you need to read the fight at a glance, at any zoom.
    // - Each drone gets a symbol in Norvane teal shaped by its type (Scout a quad cross, Strike a winged arrow,
    //   Kamikaze a dart, Nightjar a broad wing), turned the way it flies, with a thin health bar. It flashes amber
    //   when hit and flickers violet while jammed. Zoomed right in the symbols fade and the models speak for
    //   themselves; zoomed out they grow.
    // - Selected drones show their intent: a dashed line marching to where they're going, or a line to the enemy
    //   they're attacking.
    // - Orders get an instant answer on the ground: a ring for a move, a diamond for an attack.
    // - Zoomed out, every enemy in sight gets a small orange marker, so a column of trucks reads at 100 m.
    public partial class SandboxHud
    {
        static readonly Color Teal = new Color(0x49 / 255f, 0xDF / 255f, 0xCF / 255f);
        static readonly Color DravinOrange = new Color(1f, 0x78 / 255f, 0x48 / 255f);
        static readonly Color Amber = new Color(1f, 0xC1 / 255f, 0x5A / 255f);
        static readonly Color Ink = new Color(0.04f, 0.07f, 0.09f);
        static readonly Color JamViolet = new Color(0.75f, 0.45f, 1f);

        static Texture2D[] droneIcons;
        static Texture2D ringTexture, diamondTexture;
        readonly List<(Vector3 at, bool attack, float time)> acknowledgements = new List<(Vector3, bool, float)>();
        float playStartedAt = -1f;
        bool helpPinned, orderGiven;

        // How far the camera is into the strategic view: 0 at 30 m and below, 1 from 80 m.
        float Strategic => Mathf.InverseLerp(30f, 80f, rts.Distance);

        // An order was given: answer it on the ground at once.
        public void Acknowledge(Vector3 at, bool attack)
        {
            acknowledgements.Add((at, attack, Time.unscaledTime));
            orderGiven = true;
        }

        // H shows or hides the controls; after that they stay as the player left them.
        public void ToggleHelp()
        {
            showHelp = !showHelp;
            helpPinned = true;
        }

        // The controls panel tucks itself away once the player has given an order or played for 15 seconds.
        void AutoHideHelp()
        {
            if (playStartedAt < 0f) playStartedAt = Time.unscaledTime;
            if (showHelp && !helpPinned && (orderGiven || Time.unscaledTime - playStartedAt > 15f)) showHelp = false;
        }

        float SymbolSize => Mathf.Lerp(22f, 32f, Strategic) * Scale;

        void DrawTactical()
        {
            PlaceSymbols(SymbolSize);
            DrawIntentLines();
            DrawAcknowledgements();
            DrawDroneSymbols();
            DrawThreats();
            DrawExtraction();
        }

        // ---- Drones ----

        void DrawDroneSymbols()
        {
            float s = Scale;
            float show = Mathf.InverseLerp(20f, 32f, rts.Distance); // the symbol fades in as the models get small
            float size = SymbolSize;
            foreach (var d in game.drones)
            {
                if (d == null || !ToGui(d.AimPoint, out Vector2 real)) continue;
                Vector2 c = show > 0.01f && symbolAt.TryGetValue(d, out Vector2 placed) ? placed : real;
                bool isSelected = rts.selected.Contains(d);
                float half = PixelRadius(d.AimPoint, d.radius);
                float hit = Mathf.Clamp01(1f - (Time.time - d.LastHitTime) / 0.6f);
                Color colour = Color.Lerp(isSelected ? Color.Lerp(Teal, Color.white, 0.35f) : Teal, Amber, hit);
                if (d.IsJammed) colour = Color.Lerp(colour, JamViolet, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 14f));

                if (show > 0.01f)
                {
                    colour.a = show;
                    Symbol(c, Heading(d), size, DroneIcon(d.type), colour);
                    if (isSelected) Ring(c, size * 0.78f, new Color(1f, 1f, 1f, 0.85f * show));
                }
                if (isSelected && show < 0.99f) Brackets(real, half, new Color(Friendly.r, Friendly.g, Friendly.b, 1f - show));
                float below = Mathf.Max(size * 0.5f * show, half) + 6f * s;
                HealthBar(new Vector2(c.x, c.y + below), d.hp / d.maxHp, hit > 0f ? Amber : Teal);
                // The word under it: for a few selected drones always, for a big group only when it's news.
                if (d.Holding || (isSelected && (rts.selected.Count <= 4 || d.UnderFire || d.IsJammed || d.StatusTag == "REARMING")))
                    StatusTag(d, c, below + 4f * s);
            }
        }

        // Which way the drone is heading, as a clockwise angle on screen from straight up.
        float Heading(Drone d)
        {
            Vector3 p = d.transform.position;
            if (!ToGui(p, out Vector2 a) || !ToGui(p + d.transform.forward * 4f, out Vector2 b)) return 0f;
            Vector2 v = b - a;
            return v.sqrMagnitude < 0.01f ? 0f : Mathf.Atan2(v.x, -v.y) * Mathf.Rad2Deg;
        }

        // An icon with a dark outline, turned about its middle.
        void Symbol(Vector2 c, float angle, float size, Texture2D icon, Color colour)
        {
            Matrix4x4 matrix = GUI.matrix;
            Color old = GUI.color;
            GUIUtility.RotateAroundPivot(angle, c);
            var r = new Rect(c.x - size / 2f, c.y - size / 2f, size, size);
            float t = Mathf.Max(1.5f, 1.5f * Scale);
            GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.85f * colour.a);
            GUI.DrawTexture(new Rect(r.x - t, r.y, size, size), icon);
            GUI.DrawTexture(new Rect(r.x + t, r.y, size, size), icon);
            GUI.DrawTexture(new Rect(r.x, r.y - t, size, size), icon);
            GUI.DrawTexture(new Rect(r.x, r.y + t, size, size), icon);
            GUI.color = colour;
            GUI.DrawTexture(r, icon);
            GUI.color = old;
            GUI.matrix = matrix;
        }

        // ---- Intent and acknowledgement ----

        // The plan: for each drone attacking, a line to its target ending in a diamond; for drones on the move, one
        // route per squad (drones heading for the same place with the same queue), from the squad's middle through
        // numbered waypoints, marching toward where it's going, amber for an attack-move. Selected drones only,
        // except in a tactical pause, when every drone's plan shows.
        void DrawIntentLines()
        {
            float s = Scale;
            var who = game.Paused ? game.drones : rts.selected;
            var squads = new Dictionary<Vector3Int, List<Drone>>();
            foreach (var d in who)
            {
                if (d == null) continue;
                Vector2 from = symbolAt.TryGetValue(d, out Vector2 p) ? p : Vector2.zero;
                if (from == Vector2.zero && !ToGui(d.AimPoint, out from)) continue;
                if (d.AttackTarget != null && d.AttackTarget.IsAlive)
                {
                    if (!ToGui(d.AttackTarget.AimPoint, out Vector2 to)) continue;
                    var colour = new Color(DravinOrange.r, DravinOrange.g, DravinOrange.b, 0.75f);
                    Dashes(from, to, colour, 1.5f * s, 10f * s, 0f, 0f);
                    Diamond(to, 9f * s, colour);
                    continue;
                }
                if (!d.MoveTarget.HasValue) continue;
                Vector3 m = d.MoveTarget.Value;
                var key = new Vector3Int(Mathf.RoundToInt(m.x / 10f), Mathf.RoundToInt(m.z / 10f), d.Waypoints.Count * 2 + (d.AttackMoving ? 1 : 0));
                if (!squads.TryGetValue(key, out var squad)) squads[key] = squad = new List<Drone>();
                squad.Add(d);
            }
            foreach (var squad in squads.Values)
            {
                Vector2 start = Vector2.zero;
                int counted = 0;
                foreach (var d in squad)
                    if (symbolAt.TryGetValue(d, out Vector2 p)) { start += p; counted++; }
                if (counted == 0) continue;
                start /= counted;
                var lead = squad[0];
                Color colour = lead.AttackMoving ? Amber : Teal;
                int legs = 1 + lead.Waypoints.Count;
                Vector2 at = start;
                for (int i = 0; i < legs; i++)
                {
                    Vector3 point = Vector3.zero;
                    foreach (var d in squad) point += i == 0 ? d.MoveTarget.Value : (i - 1 < d.Waypoints.Count ? d.Waypoints[i - 1] : d.MoveTarget.Value);
                    point /= squad.Count;
                    if (!ToGui(Ground.OnGround(point) + Vector3.up * 0.2f, out Vector2 to)) break;
                    Route(at, to, colour);
                    if (legs > 1) WaypointNumber(to, i + 1, colour);
                    else Ring(to, 7f * s, colour);
                    at = to;
                }
            }
        }

        // One leg of a route: a 2 px marching dash with a dark edge, and a small arrow every 70 px.
        void Route(Vector2 a, Vector2 b, Color colour)
        {
            float s = Scale;
            float march = Time.unscaledTime * 30f * s;
            Dashes(a, b, new Color(Ink.r, Ink.g, Ink.b, 0.6f), 4f * s, 8f * s, 4f * s, march);
            Dashes(a, b, new Color(colour.r, colour.g, colour.b, 0.9f), 2f * s, 8f * s, 4f * s, march);
            Vector2 v = b - a;
            float length = v.magnitude;
            if (length < 40f * s) return;
            float angle = Mathf.Atan2(v.x, -v.y) * Mathf.Rad2Deg;
            for (float x = 35f * s; x < length - 20f * s; x += 70f * s)
                Symbol(a + v / length * x, angle, 11f * s, ArrowTexture(), colour);
        }

        // A queued waypoint: a diamond with its number.
        void WaypointNumber(Vector2 c, int number, Color colour)
        {
            float s = Scale;
            Diamond(c, 11f * s, new Color(Ink.r, Ink.g, Ink.b, 0.8f));
            Diamond(c, 10f * s, colour);
            var style = Tinted(small, colour);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(c.x - 10f * s, c.y - 9f * s, 20f * s, 18f * s), number.ToString(), style);
        }

        // A short word under each selected drone: what it's doing (MOVING, ATTACKING, REARMING, UNDER FIRE..).
        void StatusTag(Drone d, Vector2 c, float below)
        {
            if (tagStyle == null || !Mathf.Approximately(tagStyle.fontSize, Mathf.RoundToInt(10f * Scale)))
            {
                tagStyle = new GUIStyle(small) { fontSize = Mathf.RoundToInt(10f * Scale), alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
                tagShadow = new GUIStyle(tagStyle);
                tagShadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            }
            string tag = d.StatusTag;
            tagStyle.normal.textColor = tag == "UNDER FIRE" || tag == "JAMMED" ? Amber : new Color(0.85f, 0.97f, 0.95f);
            var r = new Rect(c.x - 50f * Scale, c.y + below, 100f * Scale, 14f * Scale);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), tag, tagShadow);
            GUI.Label(r, tag, tagStyle);
        }

        GUIStyle tagStyle, tagShadow;

        // While the world is stopped: a banner saying so, and the screen edges dimmed.
        void DrawPauseBanner()
        {
            if (!game.Paused) return;
            float s = Scale;
            float edge = 10f * s;
            var dim = new Color(Ink.r, Ink.g, Ink.b, 0.35f);
            Fill(new Rect(0f, 0f, Screen.width, edge), dim);
            Fill(new Rect(0f, Screen.height - edge, Screen.width, edge), dim);
            Fill(new Rect(0f, 0f, edge, Screen.height), dim);
            Fill(new Rect(Screen.width - edge, 0f, edge, Screen.height), dim);
            var r = new Rect((Screen.width - 460f * s) / 2f, 140f * s, 460f * s, 30f * s); // below the event feed
            Fill(r, PanelColor);
            Outline(r, Amber, Mathf.Max(1f, s));
            GUI.Label(r, "<b>TACTICAL PAUSE</b>   give orders, Tab to resume", Tinted(centered, Amber));
            int waypoints = 0, attacks = 0;
            foreach (var d in game.drones)
            {
                if (d == null) continue;
                if (d.AttackTarget != null) attacks++;
                else if (d.MoveTarget.HasValue) waypoints += 1 + d.Waypoints.Count;
            }
            var summary = new Rect(r.x, r.yMax + 2f * s, r.width, 20f * s);
            Fill(summary, PanelColor);
            GUI.Label(summary, $"{game.drones.Count} drones  \u00b7  {waypoints} waypoints  \u00b7  {attacks} attacking  \u00b7  {rts.selected.Count} selected",
                Tinted(new GUIStyle(small) { alignment = TextAnchor.MiddleCenter }, small.normal.textColor));
        }

        void DrawAcknowledgements()
        {
            float now = Time.unscaledTime, s = Scale;
            acknowledgements.RemoveAll(a => now - a.time > 0.7f);
            foreach (var a in acknowledgements)
            {
                if (!ToGui(a.at, out Vector2 c)) continue;
                float t = now - a.time;
                float radius = Mathf.Lerp(10f, 24f, Mathf.SmoothStep(0f, 1f, t / 0.2f)) * s;
                Color colour = a.attack ? DravinOrange : Teal;
                colour.a = 1f - Mathf.Clamp01((t - 0.25f) / 0.45f);
                if (a.attack) Diamond(c, radius, colour);
                else Ring(c, radius, colour);
            }
        }

        // ---- Enemies ----

        // The mission's current objective, phase by phase.
        string Objective()
        {
            int located = 0;
            foreach (var e in game.enemies) if (e != null && e.isTarget && !e.Hidden && e.Intel >= Intel.Identified) located++;
            if (game.IsShadow && game.Phase != MissionPhase.Extract)
            {
                string state = game.ShadowSpotted ? "<color=#FF5040><b>SEEN</b></color>" : "unseen";
                return $"<color=#FFC15A><b>SHADOW</b></color>  Watch the column: <b>{game.ShadowProgress:0}/{game.Mission.shadowSeconds:0} s</b>  {state}  <color=#8FA3A8>weapons tight</color>";
            }
            switch (game.Phase)
            {
                case MissionPhase.Recon:
                    return $"<color=#FFC15A><b>1 RECON</b></color>  Find the targets: <b>{located + game.TargetsDestroyed}/{game.TargetsTotal}</b> located";
                case MissionPhase.Strike:
                    return $"<color=#FF7848><b>2 STRIKE</b></color>  Destroy the targets: <b>{game.TargetsDestroyed}/{game.TargetsTotal}</b>";
                default:
                    int home = game.CountHome(out int flying);
                    return $"<color=#49DFCF><b>3 EXTRACT</b></color>  Drones home <b>{home}/{flying}</b>  " +
                           $"{Clock(Mathf.Max(0f, game.ExtractEndsAt - game.MissionTime))}  (B)";
            }
        }

        int KnownEnemies()
        {
            int n = 0;
            foreach (var e in game.enemies) if (e != null && !e.Hidden && e.Intel >= Intel.Identified) n++;
            return n;
        }

        // An enemy we can't see right now: a fading amber ghost where a vehicle was last seen (with how long ago),
        // or a grey "?" in a dashed circle of doubt for a mere contact. Mission targets say so.
        void IntelMarker(EnemyUnit e)
        {
            float s = Scale;
            if (e.Intel >= Intel.Identified)
            {
                if (!ToGui(e.LastSeenPosition + Vector3.up * e.aimHeight, out Vector2 c)) return;
                float age = Time.time - e.LastSeenTime;
                float fade = Mathf.Lerp(0.95f, 0.35f, age / Recon.GhostSeconds);
                bool hovered = e == rts.HoveredEnemy;
                Stamp(DashedDiamondTexture(), c, (hovered ? 12f : 9f) * s, new Color(Amber.r, Amber.g, Amber.b, fade)); // stale: dashed
                var style = Tinted(small, new Color(Amber.r, Amber.g, Amber.b, fade));
                style.alignment = TextAnchor.UpperCenter;
                GUI.Label(new Rect(c.x - 60f * s, c.y + 10f * s, 120f * s, 16f * s),
                    (e.ContactId != null ? e.ContactId + "  " : "") + (hovered ? e.unitName + ", " : "") + $"seen {Mathf.FloorToInt(age)} s ago", style);
                if (e.LooksLikeTarget) GUI.Label(new Rect(c.x - 60f * s, c.y - 30f * s, 120f * s, 18f * s), "<b>TARGET?</b>", targetLabel);
            }
            else if (e.Intel == Intel.Contact)
            {
                Vector3 centre = e.ContactPosition;
                var grey = new Color(0.82f, 0.86f, 0.88f, 0.75f);
                const int dashes = 28;
                float size = 3f * s;
                for (int i = 0; i < dashes; i += 2)
                {
                    float a = i * Mathf.PI * 2f / dashes;
                    Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * e.ContactRadius;
                    p.y = Ground.HeightAt(p) + 0.1f;
                    if (ToGui(p, out Vector2 g)) Fill(new Rect(g.x - size / 2f, g.y - size / 2f, size, size), grey);
                }
                if (!ToGui(centre + Vector3.up * 0.5f, out Vector2 c)) return;
                Dot(c, 10f * s, new Color(Ink.r, Ink.g, Ink.b, 0.7f));
                GUI.Label(new Rect(c.x - 10f * s, c.y - 10f * s, 20f * s, 20f * s), "<b>?</b>", Tinted(centered, grey));
                if (e.LooksLikeTarget) GUI.Label(new Rect(c.x - 60f * s, c.y - 30f * s, 120f * s, 18f * s), "<b>TARGET?</b>", targetLabel);
                else if (e == rts.HoveredEnemy)
                    GUI.Label(new Rect(c.x - 60f * s, c.y + 12f * s, 120f * s, 16f * s), $"{e.ContactId} unidentified", Tinted(small, grey));
            }
        }

        // Zoomed out, a marker on each enemy on the map (targets and the hovered one have brackets): a solid orange
        // diamond edged white for one we can see right now, a hollow diamond with its class for a gun we've mapped
        // but aren't watching.
        void EnemyMarker(EnemyUnit e, Vector2 c)
        {
            float strategic = Strategic;
            if (strategic <= 0.01f) return;
            if (!e.Observed)
            {
                float r = Mathf.Lerp(7f, 10f, strategic) * Scale;
                Diamond(c, r + 1f, new Color(Ink.r, Ink.g, Ink.b, 0.7f * strategic));
                Diamond(c, r, new Color(DravinOrange.r, DravinOrange.g, DravinOrange.b, strategic));
                var tag = Tinted(small, new Color(DravinOrange.r, DravinOrange.g, DravinOrange.b, strategic));
                tag.alignment = TextAnchor.UpperCenter;
                tag.fontSize = Mathf.RoundToInt(9f * Scale);
                GUI.Label(new Rect(c.x - 30f * Scale, c.y + r, 60f * Scale, 14f * Scale), ClassTag(e.type), tag);
                return;
            }
            float size = Mathf.Lerp(8f, 12f, strategic) * Scale;
            var colour = new Color(DravinOrange.r, DravinOrange.g, DravinOrange.b, strategic);
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, c);
            Fill(new Rect(c.x - size / 2f - 1.5f, c.y - size / 2f - 1.5f, size + 3f, size + 3f), new Color(1f, 1f, 1f, 0.9f * strategic)); // live: edged white
            Fill(new Rect(c.x - size / 2f, c.y - size / 2f, size, size), colour);
            GUI.matrix = matrix;
        }

        // What matters most stands out at any zoom: the enemy a selected drone is attacking gets a big bright
        // diamond, and an AA gun laying on one of our drones a pulsing red ring.
        void PriorityMarker(EnemyUnit e, Vector2 c)
        {
            float s = Scale;
            foreach (var d in rts.selected)
                if (d != null && d.AttackTarget == e)
                {
                    Diamond(c, 15f * s, new Color(Ink.r, Ink.g, Ink.b, 0.7f));
                    Diamond(c, 14f * s, DravinOrange);
                    break;
                }
            if (e.type == EnemyType.AAGun && e.TrackedDrone != null)
            {
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 8f);
                Ring(c, 12f * s, new Color(Hostile.r, Hostile.g, Hostile.b, pulse));
            }
        }

        static string ClassTag(EnemyType t) => t switch
        {
            EnemyType.AAGun => "AA", EnemyType.Radar => "RDR", EnemyType.Artillery => "ART", EnemyType.Rocket => "RKT",
            EnemyType.Jammer => "JAM", EnemyType.Depot => "DEP", EnemyType.Command => "CP", _ => "",
        };

        // Brackets with a dark edge so they show on bright fields, never smaller than 28 px across, and pulsing
        // once a second for a few seconds after the enemy is first spotted.
        void TargetBrackets(EnemyUnit e, Vector2 c, float half, Color colour)
        {
            float s = Scale;
            half = Mathf.Max(half, 14f * s);
            float sinceSpotted = Time.time - e.RevealedAt;
            if (sinceSpotted < 4f) half *= 1f + 0.18f * Mathf.Max(0f, Mathf.Sin(sinceSpotted * Mathf.PI * 2f));
            float t = Mathf.Max(1f, 2f * s);
            Brackets(c, half + t * 0.5f, new Color(Ink.r, Ink.g, Ink.b, 0.85f), t * 2f);
            Brackets(c, half, colour, t);
        }

        // ---- Shapes ----

        // A dashed line from a to b: dash and gap in pixels, the pattern shifted along by offset (gap 0 is solid).
        static void Dashes(Vector2 a, Vector2 b, Color colour, float thickness, float dash, float gap, float offset)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 1f) return;
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            if (gap <= 0f) Fill(new Rect(a.x, a.y - thickness / 2f, length, thickness), colour);
            else
            {
                float period = dash + gap;
                for (float x = Mathf.Repeat(offset, period) - period; x < length; x += period)
                {
                    float start = Mathf.Max(0f, x), end = Mathf.Min(length, x + dash);
                    if (end > start) Fill(new Rect(a.x + start, a.y - thickness / 2f, end - start, thickness), colour);
                }
            }
            GUI.matrix = matrix;
        }

        static void Ring(Vector2 c, float radius, Color colour) => Stamp(RingTexture(), c, radius, colour);

        static void Diamond(Vector2 c, float radius, Color colour) => Stamp(DiamondTexture(), c, radius, colour);

        static void Stamp(Texture2D texture, Vector2 c, float radius, Color colour)
        {
            Color old = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), texture);
            GUI.color = old;
        }

        // ---- Generated textures ----

        static Texture2D DroneIcon(DroneType type)
        {
            if (droneIcons == null)
            {
                droneIcons = new Texture2D[4];
                // Shapes in a unit square with the nose at the top (y up).
                droneIcons[(int)DroneType.Scout] = Icon(p =>
                {
                    // A quadcopter seen from above: a body, four arms and four rotor discs, and a nose tick.
                    float ax = Mathf.Abs(p.x - 0.5f), ay = Mathf.Abs(p.y - 0.5f);
                    if (ax < 0.13f && ay < 0.13f) return true;
                    if (Mathf.Abs(ax - ay) < 0.05f && ax < 0.3f) return true;
                    if (new Vector2(ax - 0.29f, ay - 0.29f).magnitude < 0.15f) return true;
                    return InPolygon(p, new[] { new Vector2(0.5f, 0.98f), new Vector2(0.6f, 0.72f), new Vector2(0.4f, 0.72f) });
                });
                droneIcons[(int)DroneType.Strike] = Icon(p => InPolygon(p, new[]
                {
                    new Vector2(0.5f, 0.97f), new Vector2(0.61f, 0.62f), new Vector2(0.95f, 0.44f), new Vector2(0.95f, 0.3f),
                    new Vector2(0.6f, 0.38f), new Vector2(0.6f, 0.12f), new Vector2(0.5f, 0.2f), new Vector2(0.4f, 0.12f),
                    new Vector2(0.4f, 0.38f), new Vector2(0.05f, 0.3f), new Vector2(0.05f, 0.44f), new Vector2(0.39f, 0.62f),
                }));
                droneIcons[(int)DroneType.Kamikaze] = Icon(p => InPolygon(p, new[]
                {
                    new Vector2(0.5f, 0.98f), new Vector2(0.72f, 0.1f), new Vector2(0.5f, 0.3f), new Vector2(0.28f, 0.1f),
                }));
                droneIcons[(int)DroneType.Bomber] = Icon(p => InPolygon(p, new[]
                {
                    new Vector2(0.5f, 0.9f), new Vector2(0.98f, 0.36f), new Vector2(0.98f, 0.22f), new Vector2(0.64f, 0.28f),
                    new Vector2(0.5f, 0.1f), new Vector2(0.36f, 0.28f), new Vector2(0.02f, 0.22f), new Vector2(0.02f, 0.36f),
                }));
            }
            return droneIcons[(int)type];
        }

        static Texture2D RingTexture()
        {
            if (ringTexture == null)
                ringTexture = Icon(p => Mathf.Abs((p - new Vector2(0.5f, 0.5f)).magnitude - 0.44f) < 0.045f);
            return ringTexture;
        }

        static Texture2D DiamondTexture()
        {
            if (diamondTexture == null)
                diamondTexture = Icon(p => Mathf.Abs(Mathf.Abs(p.x - 0.5f) + Mathf.Abs(p.y - 0.5f) - 0.42f) < 0.05f);
            return diamondTexture;
        }

        // A white 64 px texture, its alpha the coverage of the shape (4 x 4 samples a pixel).
        static Texture2D Icon(Func<Vector2, bool> inside)
        {
            const int n = 64;
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int count = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (inside(new Vector2((x + (sx + 0.5f) / 4f) / n, (y + (sy + 0.5f) / 4f) / n))) count++;
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)(count * 255 / 16));
                }
            }
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}

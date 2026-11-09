using UnityEngine;

namespace DroneSandbox
{
    // The theatre map's markers and briefing cards (after ChatGPT's second review: say why a sector is worth
    // fighting for, not just that it glows). Each hotspot is a badge: Dravin orange while the enemy holds it, teal
    // once won, with a glyph for the kind of country (forest, farmland, water, town, industry, railway, airfield,
    // hills) and a ring that pulses with how active the enemy is. The card names the main threat, how good the
    // intelligence is, the likely reward and the difficulty.
    public partial class SandboxHud
    {
        enum Country { Forest, Farmland, Water, Town, Industry, Railway, Airfield, Hills }

        static Texture2D[] countryGlyphs;
        static Texture2D badgeTexture;

        static Country CountryOf(SectorKind kind)
        {
            switch (kind)
            {
                case SectorKind.ForestEdge: case SectorKind.DeepForest: case SectorKind.BirchWoods: case SectorKind.LoggingCamp:
                    return Country.Forest;
                case SectorKind.RiverValley: case SectorKind.RiverCrossing: case SectorKind.LakeDistrict: case SectorKind.Reservoir:
                case SectorKind.Marshland: case SectorKind.Wetlands: case SectorKind.Lakeshore: case SectorKind.VarrowValley:
                    return Country.Water;
                case SectorKind.VillageCluster: case SectorKind.TownOutskirts: case SectorKind.MarketTown: case SectorKind.CrossroadsHamlet:
                    return Country.Town;
                case SectorKind.IndustrialZone: case SectorKind.PowerStation:
                    return Country.Industry;
                case SectorKind.RailJunction: case SectorKind.RailLine: case SectorKind.HighwayCorridor:
                    return Country.Railway;
                case SectorKind.Airfield:
                    return Country.Airfield;
                case SectorKind.HillCountry: case SectorKind.RidgePass: case SectorKind.QuarryHills: case SectorKind.Moorland: case SectorKind.BorderLine:
                    return Country.Hills;
                default:
                    return Country.Farmland;
            }
        }

        // What will mostly be shooting back.
        static string ThreatOf(Situation s)
        {
            switch (s)
            {
                case Situation.AAScreen: case Situation.AirDefenceBelt: case Situation.RadarStation:
                    return "AIR DEFENCE: AA guns and radar";
                case Situation.JammingNode: case Situation.DroneLaunchSite:
                    return "ELECTRONIC: jammers, lost links";
                case Situation.ArmouredColumn: case Situation.TankLaager: case Situation.DugInTanks: case Situation.Breakthrough:
                    return "ARMOUR: tanks shrug off gunfire";
                case Situation.HiddenArtillery: case Situation.MortarLine: case Situation.RocketBattery:
                    return "ARTILLERY: camouflaged guns to find";
                case Situation.SupplyConvoy: case Situation.NightResupply: case Situation.RetreatingColumn: case Situation.CommandConvoy:
                case Situation.EngineerColumn: case Situation.TroopRotation:
                    return "MOVING COLUMN: catch it before it's gone";
                case Situation.DecoyField:
                case Situation.IdentificationHunt:
                    return "DECEPTION: decoys among the real thing";
                default:
                    return "LOGISTICS: depots and parked vehicles";
            }
        }

        // How sure the reports are (fog and night make them worse), as a percentage.
        static int IntelConfidence(Hotspot h)
        {
            float c = 0.85f - 0.25f * h.heat;
            if (h.weather == Weather.Fog) c -= 0.25f;
            else if (h.weather == Weather.Rain) c -= 0.12f;
            if (h.daylight == Daylight.Night) c -= 0.15f;
            c += ((h.seed * 7919) % 11 - 5) * 0.01f;
            return Mathf.Clamp(Mathf.RoundToInt(c * 100f), 20, 95);
        }

        // What the theatre around a sector adds (Theatre, WorldActivity): the Dravin positions mapped near it, whether
        // its Dravin are on guard, and for the announced threat, the supply that feeds the build-up.
        static string TheatreNote(Hotspot h)
        {
            if (h.final || Theatre.Done(h)) return "";
            var lines = new System.Collections.Generic.List<string>();
            int mapped = Theatre.Mapped(h), supply = Theatre.Supplies(h);
            if (mapped > 0) lines.Add($"<color=#49DFCF><b>Mapped nearby:</b> {mapped} Dravin position{(mapped == 1 ? "" : "s")}, reports {mapped * 10}% sharper</color>");
            if (Theatre.OnGuard(h)) lines.Add("<color=#FFA040><b>On guard:</b> Dravin nearby saw our drones</color>");
            if (Theatre.IsThreat(h) && !Theatre.ThreatSlipped && supply > 0)
                lines.Add($"<color=#FF8060><b>Its supply:</b> {supply} depot{(supply == 1 ? "" : "s")} or convoy{(supply == 1 ? "" : "s")} within 8 km; hit one and the build-up slips a turn</color>");
            return string.Join("\n", lines);
        }

        // A badge on the theatre map: control colour, country glyph, and an activity ring.
        // Not on offer this turn (dim): faded, no activity ring.
        void HotspotBadge(Vector2 c, Hotspot h, bool done, bool chosenOne, bool hovered, bool dim = false)
        {
            float s = Scale;
            float size = (24f + 12f * h.heat) * s * (hovered ? 1.15f : 1f) * (h.final ? 1.4f : 1f) * (dim ? 0.8f : 1f);
            Color control = done ? Teal : DravinOrange;
            if (dim)
            {
                Stamp(BadgeTexture(), c, size * 0.5f, new Color(control.r, control.g, control.b, 0.35f));
                Stamp(CountryGlyph(CountryOf(h.kind)), c, size * 0.32f, new Color(Ink.r, Ink.g, Ink.b, 0.5f));
                return;
            }
            if (h.final && !done)
            {
                // The final operation: a steady red ring round its badge and its name over it (under it, it ran into
                // the hotspots on the front line).
                Ring(c, size * 0.95f, new Color(Hostile.r, Hostile.g, Hostile.b, 0.9f));
                GUI.Label(new Rect(c.x - 60f * s, c.y - size * 0.6f - 18f * s, 120f * s, 18f * s), "<b>FINAL OPERATION</b>",
                    Tinted(new GUIStyle(small) { alignment = TextAnchor.MiddleCenter }, Hostile));
            }
            if (!done)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 1.5f + h.seed);
                Ring(c, size * (0.75f + 0.12f * pulse), new Color(control.r, control.g, control.b, 0.25f + 0.35f * h.heat * pulse));
            }
            Stamp(BadgeTexture(), c, size * 0.5f + 1.5f * s, new Color(Ink.r, Ink.g, Ink.b, 0.9f));
            Stamp(BadgeTexture(), c, size * 0.5f, control);
            Stamp(CountryGlyph(CountryOf(h.kind)), c, size * 0.32f, new Color(Ink.r, Ink.g, Ink.b, 0.95f));
            if (chosenOne) Brackets(c, size * 0.65f + 4f * s, Color.white);
        }

        // ---- Generated glyphs ----

        static Texture2D BadgeTexture()
        {
            if (badgeTexture == null)
                badgeTexture = Icon(p =>
                {
                    // A rounded hexagon.
                    float x = Mathf.Abs(p.x - 0.5f), y = Mathf.Abs(p.y - 0.5f);
                    return x <= 0.45f * 0.866f && x * 0.5f + y * 0.866f <= 0.45f * 0.866f;
                });
            return badgeTexture;
        }

        static Texture2D CountryGlyph(Country c)
        {
            if (countryGlyphs == null)
            {
                countryGlyphs = new Texture2D[8];
                countryGlyphs[(int)Country.Forest] = Icon(p =>
                    InPolygon(p, new[] { new Vector2(0.5f, 0.95f), new Vector2(0.85f, 0.3f), new Vector2(0.15f, 0.3f) })
                    || (Mathf.Abs(p.x - 0.5f) < 0.07f && p.y > 0.08f && p.y < 0.35f));
                countryGlyphs[(int)Country.Farmland] = Icon(p =>
                    p.x > 0.1f && p.x < 0.9f && p.y > 0.1f && p.y < 0.9f && (Mathf.Repeat(p.x * 4f, 1f) < 0.3f || Mathf.Repeat(p.y * 4f, 1f) < 0.3f));
                countryGlyphs[(int)Country.Water] = Icon(p =>
                {
                    for (int k = 0; k < 3; k++)
                    {
                        float yLine = 0.25f + k * 0.25f + 0.07f * Mathf.Sin(p.x * Mathf.PI * 4f);
                        if (Mathf.Abs(p.y - yLine) < 0.06f && p.x > 0.08f && p.x < 0.92f) return true;
                    }
                    return false;
                });
                countryGlyphs[(int)Country.Town] = Icon(p =>
                    InPolygon(p, new[] { new Vector2(0.5f, 0.92f), new Vector2(0.9f, 0.55f), new Vector2(0.1f, 0.55f) })
                    || (p.x > 0.2f && p.x < 0.8f && p.y > 0.1f && p.y < 0.56f && !(p.x > 0.42f && p.x < 0.58f && p.y < 0.36f)));
                countryGlyphs[(int)Country.Industry] = Icon(p =>
                    (p.y > 0.1f && p.y < 0.45f && p.x > 0.1f && p.x < 0.9f)
                    || (p.x > 0.16f && p.x < 0.3f && p.y < 0.9f && p.y > 0.4f)
                    || InPolygon(p, new[] { new Vector2(0.3f, 0.45f), new Vector2(0.55f, 0.65f), new Vector2(0.55f, 0.45f), new Vector2(0.8f, 0.65f), new Vector2(0.8f, 0.45f) }));
                countryGlyphs[(int)Country.Railway] = Icon(p =>
                    (Mathf.Abs(p.x - 0.32f) < 0.05f || Mathf.Abs(p.x - 0.68f) < 0.05f || (Mathf.Repeat(p.y * 5f, 1f) < 0.3f && p.x > 0.18f && p.x < 0.82f))
                    && p.y > 0.05f && p.y < 0.95f);
                countryGlyphs[(int)Country.Airfield] = Icon(p =>
                    (Mathf.Abs(p.x - 0.5f) < 0.09f && p.y > 0.05f && p.y < 0.95f) || (Mathf.Abs(p.y - 0.55f) < 0.07f && p.x > 0.12f && p.x < 0.88f));
                countryGlyphs[(int)Country.Hills] = Icon(p =>
                    InPolygon(p, new[] { new Vector2(0.05f, 0.15f), new Vector2(0.38f, 0.8f), new Vector2(0.55f, 0.48f), new Vector2(0.68f, 0.65f), new Vector2(0.95f, 0.15f) }));
            }
            return countryGlyphs[(int)c];
        }
    }
}

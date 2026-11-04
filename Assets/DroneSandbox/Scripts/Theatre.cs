using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // One place on the front where fighting is going on: a sector (its kind and seed), what the enemy is doing
    // there, the time of day and the weather, and how hot it is.
    public class Hotspot
    {
        public SectorKind kind;
        public int seed;
        public Situation situation;
        public Daylight daylight;
        public Weather weather;
        public float heat;          // 0-1: how much enemy activity
        public Vector2 mapPosition; // on the theatre map, 0-1 across and up
        public string sectorName;

        public bool final;          // the campaign's last operation
        public SectorPlan Plan() => SectorGenerator.Generate(kind, seed);
        public string Key => $"hotspot.{kind}.{seed}";
    }

    // The theatre of war: the whole fictional front, about 1,200 km of the Norvane-Dravin border, drawn as a 2D
    // map. Hotspots glow along the front line where the enemy is active; each one opens a sector built from its
    // seed. The campaign has a seed of its own (made the first time) and a round that goes up every third win:
    // the front line moves your way, and a new set of hotspots flares up. A lost fight pushes the front back a
    // little. Win WinsForFinal sectors and the final operation opens: a strike on the Dravin front command; win
    // that and the campaign is won. Saved through Progress.
    public static class Theatre
    {
        public const int HotspotCount = 11;
        public const float FrontKilometres = 1200f;
        public const int WinsForFinal = 12;

        static List<Hotspot> hotspots;
        static int builtRound = -1;
        static bool builtFinal;
        static Texture2D map;
        static int seed;

        // Made once (and saved, except in test runs), so the front stays the same between sessions.
        public static int Seed
        {
            get
            {
                if (seed != 0) return seed;
                seed = Progress.Get("theatre.seed");
                if (seed == 0)
                {
                    seed = Random.Range(1, 1000000);
                    Progress.Set("theatre.seed", seed);
                    Progress.Save();
                }
                return seed;
            }
        }

        public static int Round => Progress.Get("theatre.round");
        public static int Wins => Progress.Get("theatre.wins");
        public static int Losses => Progress.Get("theatre.losses");
        public static int DronesLost => Progress.Get("theatre.dronesLost");
        public static int Fights => Progress.Get("theatre.fights");
        public static bool FinalOpen => Wins >= WinsForFinal && !Victory;
        public static bool Victory => Progress.Get("theatre.victory") == 1;

        // How far the front has moved your way: 0 at the start, the full 0.12 (the Dravin lines) once the final
        // operation opens. A loss gives back half a win's ground.
        public static float Advance => Mathf.Clamp((Wins - 0.5f * Losses) * 0.12f / WinsForFinal, -0.04f, 0.12f);

        // The front line's height on the map (0-1) at x (0-1).
        public static float Front(float x)
        {
            float s = Seed % 1000 * 0.01f;
            return 0.5f + 0.13f * Mathf.Sin(x * 6.3f + s) + 0.06f * Mathf.Sin(x * 17.1f + s * 2.3f) + 0.025f * Mathf.Sin(x * 41f + s) + Advance;
        }

        public static List<Hotspot> Hotspots
        {
            get
            {
                if (hotspots == null || builtRound != Round || builtFinal != FinalOpen) Build();
                return hotspots;
            }
        }

        static void Build()
        {
            builtRound = Round;
            hotspots = new List<Hotspot>();
            var rng = new System.Random(Seed * 31 + Round * 7919);
            for (int i = 0; i < HotspotCount; i++)
            {
                float x = (i + 0.5f) / HotspotCount + ((float)rng.NextDouble() - 0.5f) * 0.05f;
                var kind = (SectorKind)rng.Next(1, SectorTypes.All.Length);
                var type = SectorTypes.Get(kind);
                Situation situation;
                do situation = (Situation)rng.Next(Situations.Names.Length);
                while (!Situations.Fits(situation, type));
                int seed = rng.Next(1, 1000000);
                var spot = new Hotspot
                {
                    kind = kind, seed = seed, situation = situation,
                    daylight = (Daylight)rng.Next(4),
                    weather = rng.NextDouble() < 0.55 ? Weather.Clear : (Weather)rng.Next(1, 4),
                    heat = 0.25f + 0.75f * (float)rng.NextDouble(),
                    mapPosition = new Vector2(x, Front(x) + ((float)rng.NextDouble() - 0.5f) * 0.05f),
                };
                if (situation == Situation.NightResupply) spot.daylight = Daylight.Night;
                spot.sectorName = SectorGenerator.Generate(kind, seed).name;
                hotspots.Add(spot);
            }
            builtFinal = FinalOpen;
            if (builtFinal) hotspots.Add(Final());
        }

        // The last operation: the Dravin front command, dug in on the border line a little behind their front, between
        // the middle hotspots, struck at dusk.
        static Hotspot Final()
        {
            int finalSeed = Seed % 900000 + 4242;
            var spot = new Hotspot
            {
                kind = SectorKind.BorderLine, seed = finalSeed, situation = Situation.CommandPost,
                daylight = Daylight.Dusk, weather = Weather.Clear, heat = 1f, final = true,
                mapPosition = new Vector2(0.5f + 0.5f / HotspotCount, Front(0.5f + 0.5f / HotspotCount) + 0.09f),
            };
            spot.sectorName = SectorGenerator.Generate(spot.kind, spot.seed).name;
            return spot;
        }

        public static bool Done(Hotspot h) => Victory || Progress.Get(h.Key + ".won") > 0; // the war won, all of it is

        // ---- What winning buys (campaign bonuses, saved) ----

        public enum Bonus { Intel, Supply, Rearm }

        // What a sector's fight gives you for the rest of the war: beating air defences, jammers or decoys sharpens
        // the reports (tighter contact circles); hitting logistics or columns frees up a Kamikaze for every
        // mission; armour, artillery and airfield sectors give a forward strip (faster rearming).
        public static Bonus BonusOf(Situation s)
        {
            switch (s)
            {
                case Situation.AAScreen: case Situation.AirDefenceBelt: case Situation.RadarStation: case Situation.JammingNode:
                case Situation.DroneLaunchSite: case Situation.DecoyField: case Situation.ObservationPost:
                    return Bonus.Intel;
                case Situation.ArmouredColumn: case Situation.TankLaager: case Situation.DugInTanks: case Situation.Breakthrough:
                case Situation.HiddenArtillery: case Situation.MortarLine: case Situation.RocketBattery: case Situation.AirfieldStrike:
                    return Bonus.Rearm;
                default:
                    return Bonus.Supply;
            }
        }

        static readonly int[] MaxStacks = { 3, 2, 2 };

        public static int Stacks(Bonus b) => Progress.Get("campaign." + b);
        public static float IntelFactor => 1f - 0.15f * Stacks(Bonus.Intel);   // contact circles in briefings
        public static int SupplyBonus => Stacks(Bonus.Supply);                 // extra Kamikazes per mission
        public static float RearmFactor => 1f - 0.15f * Stacks(Bonus.Rearm);   // Strike and Nightjar reload time

        public static string BonusText(Bonus b) => b == Bonus.Intel ? "sharper reports (contact circles 15% tighter)"
            : b == Bonus.Supply ? "+1 Kamikaze in every mission" : "a forward strip (rearming 15% faster)";

        public static bool BonusMaxed(Bonus b) => Stacks(b) >= MaxStacks[(int)b];

        // A win cools the hotspot, pushes the front, banks the sector's bonus and brings the next round of fighting;
        // the final operation won wins the campaign.
        public static void Won(Hotspot h, int dronesLost)
        {
            Record(dronesLost);
            if (h.final)
            {
                Progress.Set("theatre.victory", 1);
                Progress.Save();
                return;
            }
            if (Done(h)) return; // replayed after winning it: nothing more to gain
            var bonus = BonusOf(h.situation);
            Progress.Set("campaign." + bonus, Mathf.Min(MaxStacks[(int)bonus], Stacks(bonus) + 1));
            Progress.Set(h.Key + ".won", 1);
            Progress.Set("theatre.wins", Wins + 1);
            // Every third win starts a new round: the hotspots move.
            if (Wins % 3 == 0) Progress.Set("theatre.round", Round + 1);
            Progress.Save();
        }

        // A lost fight gives the Dravin back a little ground (not below where the war started, give or take).
        public static void Lost(Hotspot h, int dronesLost)
        {
            Record(dronesLost);
            if (!h.final && !Done(h)) Progress.Set("theatre.losses", Losses + 1);
            Progress.Save();
        }

        static void Record(int dronesLost)
        {
            Progress.Set("theatre.fights", Fights + 1);
            Progress.Set("theatre.dronesLost", DronesLost + dronesLost);
        }

        // Starts the war over: a new front (a new seed), no sectors won, no campaign bonuses. Tech-shop purchases
        // and mission stars are kept.
        public static void NewCampaign()
        {
            Progress.Forget("theatre.");
            Progress.Forget("campaign.");
            Progress.Forget("hotspot.");
            seed = 0;
            hotspots = null;
            builtRound = -1;
            mapAdvance = -1f;
            Progress.Set("theatre.seed", Random.Range(1, 1000000));
            Progress.Save();
        }

        // ---- The map picture ----

        // Land shaded by height, with rivers and forests, your side tinted teal and the enemy's orange either side
        // of the front line. Made once per front position.
        static float mapAdvance = -1f;

        public static Texture2D Map()
        {
            if (map != null && Mathf.Approximately(mapAdvance, Advance)) return map;
            mapAdvance = Advance;
            const int w = 1024, h = 560;
            if (map == null) map = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[w * h];
            float s = Seed % 997;
            var land = new Color(0.42f, 0.47f, 0.33f);
            var high = new Color(0.55f, 0.53f, 0.42f);
            var forest = new Color(0.22f, 0.33f, 0.2f);
            var water = new Color(0.17f, 0.3f, 0.38f);
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float height = Mathf.PerlinNoise(u * 5f + s, v * 2.7f + s) * 0.6f + Mathf.PerlinNoise(u * 14f + s, v * 7.6f) * 0.3f
                                   + Mathf.PerlinNoise(u * 40f, v * 22f + s) * 0.1f;
                    var c = Color.Lerp(land, high, Mathf.SmoothStep(0.45f, 0.75f, height));
                    float woods = Mathf.PerlinNoise(u * 22f + 50f + s, v * 12f + 3f);
                    c = Color.Lerp(c, forest, Mathf.SmoothStep(0.58f, 0.66f, woods) * 0.8f);
                    // Rivers: thin wandering lines running down the map.
                    float river = Mathf.Abs(Mathf.PerlinNoise(u * 3f + s, v * 3f) - 0.5f);
                    if (river < 0.006f) c = water;
                    // A sea along the bottom-left corner.
                    if (v < 0.12f + 0.08f * Mathf.Sin(u * 4f + s) - u * 0.25f) c = water;
                    // Sides of the front.
                    float front = Front(u), d = v - front;
                    var tint = d < 0f ? new Color(0.1f, 0.52f, 0.58f) : new Color(0.92f, 0.46f, 0.12f);
                    c = Color.Lerp(c, tint, 0.16f * Mathf.Clamp01(Mathf.Abs(d) * 6f + 0.3f));
                    // The front line itself, and the contested strip either side of it.
                    if (Mathf.Abs(d) < 0.004f) c = new Color(0.95f, 0.85f, 0.4f);
                    else if (Mathf.Abs(d) < 0.03f) c = Color.Lerp(c, new Color(0.35f, 0.3f, 0.22f), 0.35f * (1f - Mathf.Abs(d) / 0.03f));
                    // Map grid every 100 km.
                    float km = u * FrontKilometres;
                    if (Mathf.Abs(km % 100f) < 0.9f || Mathf.Abs((v * h) % (h / 5.6f)) < 0.7f) c *= 0.88f;
                    c.a = 1f;
                    pixels[y * w + x] = c;
                }
            }
            map.SetPixels(pixels);
            map.Apply();
            return map;
        }
    }
}

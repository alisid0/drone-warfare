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
    // the front line moves your way, and a new set of hotspots flares up.
    // The campaign is played in turns, one operation each (the 1.0 design, agreed with ChatGPT): three of the
    // hotspots are on offer, each for a different reward (Intel, Support or Ground). Progress is counted in
    // half-wins: a win +2 (+3 for Ground), a loss -1, an ignored Dravin threat -1, but never more than -1 in a turn,
    // nor below 0. At most one threat is announced at a time, on offer for two turns; left alone, it costs ground and
    // puts another hotspot on alert. At ProgressForFinal the final operation opens: a strike on the Dravin front
    // command; destroy it and the campaign is complete (the ground offensive breaks through: the drones open the
    // way, they don't win the war on their own). Support charges (start 1, at most 3) buy an extra drone before a
    // mission. Saved through Progress.
    public static class Theatre
    {
        public const int HotspotCount = 11;
        public const float FrontKilometres = 1200f;
        public const int ProgressForFinal = 24; // half-wins: twelve clean wins

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
        public static int Turn => Mathf.Max(1, Progress.Get("theatre.turn"));
        public static int Standing => Progress.Get("theatre.progress");  // progress toward the final, in half-wins
        public static bool FinalOpen => Standing >= ProgressForFinal && !Victory;
        public static bool Victory => Progress.Get("theatre.victory") == 1;
        public static string Outcome = "", News = ""; // what the last operation did for the campaign, and what the turn brought

        // How far the front has moved your way: 0 at the start, the full 0.12 (the Dravin lines) once the final
        // operation opens.
        public static float Advance => Mathf.Clamp(Standing * 0.12f / ProgressForFinal, 0f, 0.12f);

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
                Start();
                if (hotspots == null || builtRound != Round || builtFinal != FinalOpen) Build();
                return hotspots;
            }
        }

        // A campaign's first look: one support charge to begin with. A save from before turns gets its progress from
        // the sectors it had won.
        static void Start()
        {
            if (Progress.Get("campaign.started") != 0) return;
            Progress.Set("campaign.started", 1);
            Progress.Set("campaign.charges", 1);
            if (Standing == 0 && Wins > 0) Progress.Set("theatre.progress", Mathf.Max(0, 2 * Wins - Losses));
            Progress.Save();
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

        // ---- What winning buys (saved) ----

        // Each turn's three operations offer one reward each: Intel (sharper reports), Support (a support charge) or
        // Ground (the front moves further, and a forward strip comes closer: faster rearming).
        public enum Reward { Intel, Support, Ground }
        public enum Bonus { Intel, Rearm }
        public const int MaxStacks = 2, MaxCharges = 3;

        public static int Stacks(Bonus b) => Mathf.Min(MaxStacks, Progress.Get("campaign." + b));
        public static int Charges => Progress.Get("campaign.charges");
        static bool IntelBoosted => Progress.Get("campaign.intelBoostTurn") == Turn; // a Support reward won at full charges
        public static float IntelFactor => 1f - 0.15f * (Stacks(Bonus.Intel) + (IntelBoosted ? 1 : 0)); // contact circles in briefings
        public static float RearmFactor => 1f - 0.15f * Stacks(Bonus.Rearm);   // Strike and Nightjar reload time

        public static string RewardText(Reward r) => r == Reward.Intel ? "sharper reports (contact circles 15% tighter)"
            : r == Reward.Support ? (Charges >= MaxCharges ? "charges full, so a one-turn intel boost instead" : "+1 support charge")
            : "the front moves further (+3 progress, not +2) and rearming gets 15% faster";
        public static bool RewardMaxed(Reward r) => r == Reward.Intel && Stacks(Bonus.Intel) >= MaxStacks;

        static void Stack(Bonus b) => Progress.Set("campaign." + b, Mathf.Min(MaxStacks, Stacks(b) + 1));
        static void AddCharge() => Progress.Set("campaign.charges", Mathf.Min(MaxCharges, Charges + 1));
        static void Change(int halfWins) => Progress.Set("theatre.progress", Mathf.Max(0, Standing + halfWins));

        // A support charge spent before a mission: one more drone of a type it already offers (SandboxGame).
        public static bool SpendCharge()
        {
            if (Charges <= 0) return false;
            Progress.Set("campaign.charges", Charges - 1);
            Progress.Save();
            return true;
        }

        // A win banks its offer's reward and progress, deals with the threat if it was the one, and ends the turn;
        // every third win brings a support charge and a new round (the hotspots move). The final operation done
        // completes the campaign.
        public static void Won(Hotspot h, int dronesLost)
        {
            Achievements.CampaignResult(h, true);
            Record(dronesLost);
            Outcome = News = "";
            if (h.final)
            {
                Progress.Set("theatre.victory", 1);
                Progress.Save();
                return;
            }
            if (Done(h))
            {
                Progress.Save(); // replayed after winning it: nothing more to gain
                return;
            }
            var reward = RewardOf(h);
            bool full = Charges >= MaxCharges;
            Change(reward == Reward.Ground ? 3 : 2);
            if (reward == Reward.Intel) Stack(Bonus.Intel);
            else if (reward == Reward.Support && full) Progress.Set("campaign.intelBoostTurn", Turn + 1);
            else if (reward == Reward.Support) AddCharge();
            else Stack(Bonus.Rearm);
            Outcome = $"Campaign: +{(reward == Reward.Ground ? 3 : 2)} progress ({Mathf.Min(Standing, ProgressForFinal)}/{ProgressForFinal})  \u00b7  " +
                      (reward == Reward.Intel ? "sharper reports" : reward == Reward.Support ? (full ? "an intel boost next turn" : "+1 support charge") : "faster rearming");
            if (IsThreat(h))
            {
                SetSpot("theatre.threat", null);
                Progress.Set("theatre.nextThreat", Turn + 2);
                News = $"Threat dealt with at {h.sectorName}.";
            }
            Progress.Set(h.Key + ".won", 1);
            Progress.Set("theatre.wins", Wins + 1);
            if (Wins % 3 == 0 && Charges < MaxCharges)
            {
                AddCharge();
                Outcome += "  \u00b7  +1 support charge for three wins";
            }
            if (Wins % 3 == 0) Progress.Set("theatre.round", Round + 1);
            EndTurn(false);
        }

        // A lost operation costs a half-win and ends the turn (the offers change: there's no retrying it).
        public static void Lost(Hotspot h, int dronesLost)
        {
            Achievements.CampaignResult(h, false);
            Record(dronesLost);
            Outcome = News = "";
            if (h.final || Done(h))
            {
                Progress.Save();
                return;
            }
            Progress.Set("theatre.losses", Losses + 1);
            Change(-1);
            Outcome = $"Campaign: -1 progress ({Standing}/{ProgressForFinal})";
            EndTurn(true);
        }

        // ---- Turns: three operations on offer, and the Dravin threat ----

        static List<(Hotspot spot, Reward reward)> offers;
        static int offersTurn = -1, offersRound = -1;
        static bool offersFinal;

        // This turn's three operations, each with a different reward, from the hotspots not yet won (the same three
        // until the turn is played). The announced threat and an alerted hotspot are always among them, and at least
        // one is of low or medium difficulty, so a player who's behind can always move forward.
        public static List<(Hotspot spot, Reward reward)> Offers
        {
            get
            {
                if (offers == null || offersTurn != Turn || offersRound != Round || offersFinal != FinalOpen) BuildOffers();
                return offers;
            }
        }

        static void BuildOffers()
        {
            offersTurn = Turn;
            offersRound = Round;
            offersFinal = FinalOpen;
            offers = new List<(Hotspot, Reward)>();
            var rng = new System.Random(Seed * 17 + Turn * 104729);
            var open = Hotspots.FindAll(h => !h.final && !Done(h));
            var picks = new List<Hotspot>();
            foreach (var must in new[] { ThreatSpot, AlertedSpot })
                if (must != null && open.Contains(must) && !picks.Contains(must)) picks.Add(must);
            var pool = open.FindAll(h => !picks.Contains(h));
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            if (!picks.Exists(h => h.heat < 0.6f) && pool.Count > 0)
            {
                var easy = pool.Find(h => h.heat < 0.6f);
                if (easy == null) foreach (var h in pool) if (easy == null || h.heat < easy.heat) easy = h;
                picks.Add(easy);
                pool.Remove(easy);
            }
            foreach (var h in pool)
            {
                if (picks.Count >= 3) break;
                picks.Add(h);
            }
            var rewards = new List<Reward> { Reward.Intel, Reward.Support, Reward.Ground };
            for (int i = rewards.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (rewards[i], rewards[j]) = (rewards[j], rewards[i]);
            }
            for (int i = 0; i < picks.Count; i++) offers.Add((picks[i], rewards[i]));
        }

        public static bool Offered(Hotspot h) => h != null && (h.final ? FinalOpen : Offers.Exists(o => o.spot == h));

        public static Reward RewardOf(Hotspot h)
        {
            foreach (var o in Offers) if (o.spot == h) return o.reward;
            return Reward.Ground;
        }

        // The announced threat: a busy hotspot of this round where the Dravin are building up, on offer this turn and
        // the next. And a hotspot on alert after an ignored threat, for up to two turns.
        public static Hotspot ThreatSpot => SpotAt("theatre.threat");
        public static int ThreatDue => Progress.Get("theatre.threatDue");
        public static bool IsThreat(Hotspot h) => h != null && h == ThreatSpot;
        public static Hotspot AlertedSpot => Turn <= Progress.Get("theatre.alertedUntil") ? SpotAt("theatre.alerted") : null;
        public static bool IsAlerted(Hotspot h) => h != null && h == AlertedSpot;

        // A hotspot saved as its place in its round's list (plus one: 0 for none), with that round: a new round moves
        // the hotspots, and the threat and the alert go with them.
        static Hotspot SpotAt(string key)
        {
            int i = Progress.Get(key) - 1;
            if (i < 0 || Progress.Get(key + "Round") != Round) return null;
            var list = Hotspots;
            return i < list.Count ? list[i] : null;
        }

        static void SetSpot(string key, Hotspot h)
        {
            Progress.Set(key, h != null ? Hotspots.IndexOf(h) + 1 : 0);
            Progress.Set(key + "Round", Round);
        }

        // After every campaign operation: a threat still there at the end of its last turn costs a half-win (within
        // the turn's cap of one lost) and puts another hotspot on alert. Then the turn moves on, and once the front
        // has had a quiet turn (and from the third turn), the Dravin announce a new threat.
        static void EndTurn(bool setback)
        {
            int turn = Turn;
            var threat = ThreatSpot;
            if (threat != null && turn >= ThreatDue)
            {
                Achievements.ThreatIgnored();
                if (!setback) Change(-1);
                var others = Hotspots.FindAll(h => !h.final && !Done(h) && h != threat);
                var alerted = others.Count > 0 ? others[new System.Random(Seed + turn * 31).Next(others.Count)] : null;
                SetSpot("theatre.alerted", alerted);
                Progress.Set("theatre.alertedUntil", turn + 2);
                SetSpot("theatre.threat", null);
                Progress.Set("theatre.nextThreat", turn + 2);
                News = $"Threat ignored at {threat.sectorName}: the front gives ground" + (alerted != null ? $", and {alerted.sectorName} is ALERTED." : ".");
                if (!setback) Outcome += (Outcome.Length > 0 ? "  \u00b7  " : "Campaign: ") + "-1 progress for the ignored threat";
            }
            turn++;
            Progress.Set("theatre.turn", turn);
            if (ThreatSpot == null && turn >= Mathf.Max(3, Progress.Get("theatre.nextThreat")) && !FinalOpen)
            {
                var candidates = Hotspots.FindAll(h => !h.final && !Done(h) && h.heat >= 0.45f && !IsAlerted(h));
                if (candidates.Count > 0)
                {
                    var t = candidates[new System.Random(Seed * 7 + turn).Next(candidates.Count)];
                    SetSpot("theatre.threat", t);
                    Progress.Set("theatre.threatDue", turn + 1);
                    News += (News.Length > 0 ? "  " : "") + $"New threat: the Dravin are building up at {t.sectorName}.";
                }
            }
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
            offers = null;
            builtRound = -1;
            Outcome = News = "";
            mapAdvance = -1f;
            Progress.Set("theatre.seed", Random.Range(1, 1000000));
            WorldActivity.Reset(); // the Dravin positions out in the theatre, seeded afresh
            Start(); // a support charge, from the first briefing on
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

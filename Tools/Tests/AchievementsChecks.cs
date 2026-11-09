// Standalone checks for the real Achievements.cs, with small game/Unity stand-ins. No Unity import or player save.
// Compile both files with csc /out:Temp/AchievementsChecks.exe, then run that executable.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    // Advance impact times explicitly to test the coordinated-strike window.
    public static class Time { public static float time; }
    // Only the integer helpers used by the achievement ledger are needed outside Unity.
    public static class Mathf
    {
        // Match Unity's bounded integer counter.
        public static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
        // Match Unity's increment limit.
        public static int Min(int a, int b) { return Math.Min(a, b); }
    }
}

namespace DroneSandbox
{
    // Replace only persistence I/O; production achievement code still reads and writes its real key contract.
    public static class Progress
    {
        public static readonly Dictionary<string, int> Values = new Dictionary<string, int>();
        public static int Saves;
        // Match missing-key defaults.
        public static int Get(string key, int fallback = 0) { int value; return Values.TryGetValue(key, out value) ? value : fallback; }
        // Store a value without implicitly saving.
        public static void Set(string key, int value) { Values[key] = value; }
        // Match New campaign's prefix deletion.
        public static void Forget(string prefix)
        {
            foreach (var key in new List<string>(Values.Keys)) if (key.StartsWith(prefix)) Values.Remove(key);
        }
        // Count writes so idempotence is testable.
        public static void Save() { Saves++; }
    }

    public enum DifficultyLevel { Recruit, Regular, Veteran }
    public enum GameMode { Menu, Sandbox, Mission }
    public enum MissionResult { None, Won, Failed }
    public enum Team { Player, Enemy }
    public enum DroneType { Scout, Strike, Kamikaze, Bomber }
    public enum Situation { Other, IdentificationHunt }

    // Supply the preference the campaign hook reads.
    public static class Settings { public static DifficultyLevel Difficulty; }
    // Supply the mission fields used by the completion hook.
    public class Mission { public string key; public int par; public List<MissionUnit> units = new List<MissionUnit>(); }
    // Only decoy presence is needed from the mission's placement records.
    public struct MissionUnit { public bool decoy; }
    // Match the health fields read before and after the game's unchanged damage call.
    public class Unit
    {
        public float hp = 100f;
        public Team team;
        public bool IsAlive { get { return hp > 0f; } }
    }
    // Keep original orders and sync membership available when capturing a shot.
    public class Drone : Unit
    {
        public DroneType type;
        public Unit AttackTarget;
        public int SyncGroup;
        public bool IsKamikaze { get { return type == DroneType.Kamikaze; } }
    }
    // Provide the recon verdict and objective flags used by actual impact and observation hooks.
    public class EnemyUnit : Unit { public bool isTarget, isDecoy, ExposedDecoy; }
    // Supply final mission results without running the world.
    public class SandboxGame
    {
        public GameMode Mode;
        public Mission Mission;
        public MissionResult Result;
        public int DronesUsed, dronesLost;
        public float BaseIntegrity;
        public bool BriefingOpen;
        public Hotspot Hotspot;
        public readonly List<EnemyUnit> enemies = new List<EnemyUnit>();
    }
    // Model only hotspot identity and completion state.
    public class Hotspot { public bool final, done; public Situation situation; }
    // Expose the state read before Theatre records a campaign result.
    public static class Theatre
    {
        public static bool Victory;
        public static int Fights, Wins, Losses, Turn, Standing, ThreatDue;
        public static Hotspot ThreatSpot;
        // Match the completed-hotspot guard.
        public static bool Done(Hotspot spot) { return spot.done; }
        // Match threat identity rather than comparing names or seeds.
        public static bool IsThreat(Hotspot spot) { return spot != null && spot == ThreatSpot; }
    }

    // Exercise the ledger and campaign disqualifications using the production implementation.
    static class AchievementsChecks
    {
        static int assertions, notices;

        // Fail the process at the first broken invariant and report the total on success.
        static int Main()
        {
            Achievements.Unlocked += Announced;
            CatalogueAndLedger();
            OperationStars();
            MissionResults();
            CampaignHistory();
            DecoyObservations();
            CleanMissions();
            IdentificationHunts();
            CoordinatedStrikes();
            AttemptIsolation();
            Console.WriteLine("Achievements: " + assertions + " assertions passed.");
            return 0;
        }

        // Require notification only after its saved earned flag is visible.
        static void Announced(Achievement entry)
        {
            Check(Achievements.Earned(entry.id), "notice follows saved flag");
            notices++;
        }

        // Start an independent local save and a fresh campaign.
        static void Reset()
        {
            Progress.Values.Clear();
            Progress.Saves = notices = 0;
            Settings.Difficulty = DifficultyLevel.Veteran;
            Theatre.Victory = false;
            Theatre.Fights = Theatre.Wins = Theatre.Losses = Theatre.Standing = 0;
            Theatre.Turn = 1;
            Theatre.ThreatDue = 0;
            Theatre.ThreatSpot = null;
            UnityEngine.Time.time = 0f;
            Achievements.BeginMission(new SandboxGame());
        }

        // Begin a mission with at least one fake, optionally the procedural Identification Hunt.
        static SandboxGame Begin(string key = "mission0", bool identification = false)
        {
            var game = new SandboxGame { Mode = GameMode.Mission, Mission = new Mission { key = key, par = 3 },
                Hotspot = identification ? new Hotspot { situation = Situation.IdentificationHunt } : null };
            game.Mission.units.Add(new MissionUnit { decoy = true });
            Achievements.BeginMission(game);
            return game;
        }

        // Place a real target or a decoy and optionally make the decoy's recon verdict confirmed.
        static EnemyUnit Enemy(SandboxGame game, bool decoy = false, bool confirmed = true)
        {
            var enemy = new EnemyUnit { isDecoy = decoy, isTarget = !decoy, ExposedDecoy = decoy && confirmed, team = Team.Enemy };
            game.enemies.Add(enemy);
            return enemy;
        }

        // Register a launch at the same point as Drone.Create, then give it an attack order.
        static Drone Launch(EnemyUnit target, DroneType type = DroneType.Strike, int group = 1)
        {
            var drone = new Drone { type = type, team = Team.Player, AttackTarget = target, SyncGroup = group };
            Achievements.DroneLaunched(drone);
            return drone;
        }

        // Reproduce TakeDamage's synchronous finish callback between the real tracking scope's entry and exit.
        static void Hit(EnemyUnit enemy, float damage, Achievements.Shot shot = null, Action duringDamage = null)
        {
            using (Achievements.TrackHit(enemy, shot))
            {
                enemy.hp = Math.Max(0f, enemy.hp - damage);
                if (duringDamage != null) duringDamage();
            }
        }

        // Finish with the same result value Finish sets before calling the achievement hook.
        static void Win(SandboxGame game)
        {
            game.Result = MissionResult.Won;
            Achievements.MissionFinished(game);
        }

        // Observations are unique within attempts and use saved high-water marks across retries and campaign resets.
        static void DecoyObservations()
        {
            Reset();
            var game = Begin();
            var fake = Enemy(game, true, false);
            Achievements.DecoyExposed(fake);
            Check(Achievements.Current("decoys") == 0, "unconfirmed decoy does not count");
            fake.ExposedDecoy = true;
            game.BriefingOpen = true;
            Achievements.DecoyExposed(fake);
            game.BriefingOpen = false;
            game.Mode = GameMode.Sandbox;
            Achievements.DecoyExposed(fake);
            game.Mode = GameMode.Mission;
            Check(Achievements.Current("decoys") == 0, "briefings and sandbox cannot farm reveals");
            for (int i = 0; i < 20; i++) Achievements.DecoyExposed(fake);
            Check(Achievements.Current("decoys") == 1, "reacquisition counts once");
            for (int i = 0; i < 3; i++) Achievements.DecoyExposed(Enemy(game, true));
            Check(Achievements.Current("decoys") == 4, "distinct decoys in one attempt count");
            Progress.Forget("campaign."); Progress.Forget("theatre."); Progress.Forget("hotspot.");
            game.enemies.Clear();
            Achievements.BeginMission(game); // same mission object, as on Retry
            for (int i = 0; i < 4; i++) Achievements.DecoyExposed(Enemy(game, true));
            Check(Achievements.Current("decoys") == 4, "retry and new campaign cannot re-count a mission");
            Achievements.DecoyExposed(Enemy(game, true));
            Check(Achievements.Current("decoys") == 5, "better observation attempt adds only its improvement");
            game.Result = MissionResult.Failed;
            Achievements.DecoyExposed(Enemy(game, true));
            Check(Achievements.Current("decoys") == 5, "results cannot farm reveals");
            game = Begin("sector.new.DecoyField");
            for (int i = 0; i < 5; i++) Achievements.DecoyExposed(Enemy(game, true));
            Check(Achievements.Earned("decoys") && Achievements.Current("decoys") == 10, "ten unique observations across missions");
            Achievements.DecoyExposed(Enemy(game, true));
            Check(notices == 1 && Achievements.Current("decoys") == 10, "observation award fires once");
        }

        // Direct shots, misses at fakes and collateral hits all disqualify a clean run, even after a lethal result callback.
        static void CleanMissions()
        {
            Reset();
            var game = Begin();
            Win(game);
            Check(Achievements.Earned("cleanDecoys"), "untouched decoy mission qualifies");
            Reset();
            game = Begin();
            game.Mission.units.Clear();
            Win(game);
            Check(!Achievements.Earned("cleanDecoys"), "mission must contain decoys");
            Reset();
            game = Begin();
            Achievements.Fired(Launch(Enemy(game, true)));
            Win(game);
            Check(!Achievements.Earned("cleanDecoys"), "missed shot aimed at a fake still disqualifies");
            Reset();
            game = Begin();
            var fake = Enemy(game, true);
            Hit(fake, 100f);
            game.enemies.Remove(fake);
            Win(game);
            Check(!Achievements.Earned("cleanDecoys"), "destroyed and removed decoy remains recorded");
            Reset();
            game = Begin();
            var target = Enemy(game);
            fake = Enemy(game, true);
            var shot = Achievements.Fired(Launch(target));
            using (Achievements.TrackBlast(shot))
            {
                Hit(target, 100f, shot, delegate { Win(game); });
                Check(!Achievements.Earned("cleanDecoys"), "lethal callback waits for the whole blast");
                Hit(fake, 10f, shot);
            }
            Check(!Achievements.Earned("cleanDecoys") && Achievements.Earned("par"), "later splash prevents only the clean award");
            Reset();
            game = Begin();
            target = Enemy(game);
            var scope = Achievements.TrackHit(target, null);
            target.hp = 0f;
            Win(game);
            scope.Dispose();
            int saved = Progress.Saves;
            scope.Dispose();
            Check(Achievements.Earned("cleanDecoys") && Progress.Saves == saved, "scope disposal and completion are idempotent");
        }

        // Useful expenditure is recorded after actual damage; misses, interception and fake targets cannot qualify.
        static void IdentificationHunts()
        {
            Reset();
            var game = Begin("sector.test.IdentificationHunt", true);
            var target = Enemy(game);
            var drone = Launch(target, DroneType.Kamikaze);
            var shot = Achievements.Fired(drone);
            drone.hp = 0f;
            using (Achievements.TrackBlast(shot)) Hit(target, 100f, shot, delegate { Win(game); });
            Check(Achievements.Earned("identification"), "final Kamikaze hit is useful before completion is evaluated");
            Reset();
            game = Begin("sector.test.IdentificationHunt", true);
            target = Enemy(game);
            drone = Launch(target, DroneType.Kamikaze);
            drone.hp = 0f;
            Win(game);
            Check(!Achievements.Earned("identification"), "intercepted Kamikaze is waste");
            Reset();
            game = Begin("sector.test.IdentificationHunt", true);
            target = Enemy(game);
            drone = Launch(target, DroneType.Kamikaze);
            shot = Achievements.Fired(drone);
            drone.hp = 0f;
            using (Achievements.TrackBlast(shot)) Hit(target, 0f, shot);
            Win(game);
            Check(!Achievements.Earned("identification"), "no damage means no useful expenditure");
            Reset();
            game = Begin("sector.test.IdentificationHunt", true);
            var fake = Enemy(game, true);
            drone = Launch(fake, DroneType.Kamikaze);
            shot = Achievements.Fired(drone);
            drone.hp = 0f;
            using (Achievements.TrackBlast(shot)) Hit(fake, 100f, shot);
            Win(game);
            Check(!Achievements.Earned("identification"), "Kamikaze spent on a fake is waste");
            Reset();
            game = Begin("sector.test.IdentificationHunt", true);
            target = Enemy(game);
            Launch(target, DroneType.Kamikaze);
            Win(game);
            Check(Achievements.Earned("identification"), "living unused Kamikaze is not lost");
            Reset();
            game = Begin();
            Win(game);
            Check(!Achievements.Earned("identification"), "other missions cannot award Identification Hunt");
        }

        // Only first shots from two different members of one G group, close in impact time, can supply the kill.
        static void CoordinatedStrikes()
        {
            Reset();
            var game = Begin();
            var target = Enemy(game);
            var a = Launch(target);
            var b = Launch(target);
            Achievements.Coordinate(new List<Drone> { a, b });
            Check(!Achievements.Earned("coordinated"), "G alone never awards");
            var first = Achievements.Fired(a);
            var second = Achievements.Fired(b);
            a.AttackTarget = null; a.SyncGroup = 0; // already-fired projectile retains its attribution
            a.hp = 0f;
            Hit(target, 40f, first);
            UnityEngine.Time.time = 1.5f;
            Hit(target, 60f, second);
            Check(Achievements.Earned("coordinated"), "two real hits including a kill at the timing boundary");
            int saved = Progress.Saves;
            Hit(target, 0f, second);
            Check(Progress.Saves == saved, "duplicate impact cannot announce twice");

            Reset();
            game = Begin(); target = Enemy(game); a = Launch(target); b = Launch(target);
            Achievements.Coordinate(new List<Drone> { a, b });
            first = Achievements.Fired(a); second = Achievements.Fired(b);
            Hit(target, 40f, first);
            UnityEngine.Time.time = 1.51f;
            Hit(target, 60f, second);
            Check(!Achievements.Earned("coordinated"), "widely separated hits are not coordinated");

            Reset();
            game = Begin(); target = Enemy(game); a = Launch(target); b = Launch(target);
            Achievements.Coordinate(new List<Drone> { a, b });
            first = Achievements.Fired(a);
            Hit(target, 40f, first);
            Hit(target, 60f, Achievements.Fired(a));
            Check(!Achievements.Earned("coordinated"), "one drone's later volley cannot impersonate the group");

            Reset();
            game = Begin(); target = Enemy(game); a = Launch(target); b = Launch(target);
            Achievements.Coordinate(new List<Drone> { a, b });
            first = Achievements.Fired(a);
            Hit(target, 40f, first);
            b.SyncGroup = 0;
            Hit(target, 60f, Achievements.Fired(b));
            Check(!Achievements.Earned("coordinated"), "cancelled group member cannot supply credit");

            Reset();
            game = Begin(); target = Enemy(game); target.isTarget = false;
            a = Launch(target); b = Launch(target);
            Achievements.Coordinate(new List<Drone> { a, b });
            Hit(target, 40f, Achievements.Fired(a));
            Hit(target, 60f, Achievements.Fired(b));
            Check(!Achievements.Earned("coordinated"), "kill must be a mission objective");

            Reset();
            game = Begin(); target = Enemy(game); a = Launch(target); b = Launch(target);
            Achievements.Coordinate(new List<Drone> { a, b });
            first = Achievements.Fired(a); second = Achievements.Fired(b);
            Hit(target, 10f, first);
            Hit(target, 90f); // unrelated projectile deals the lethal hit
            Hit(target, 0f, second);
            Check(!Achievements.Earned("coordinated"), "an unrelated kill is not attributed to G");

            Reset();
            game = Begin(); target = Enemy(game); a = Launch(target);
            var secondTarget = Enemy(game); b = Launch(secondTarget);
            Achievements.Coordinate(new List<Drone> { a, b });
            first = Achievements.Fired(a); second = Achievements.Fired(b);
            a.hp = b.hp = 0f;
            Hit(target, 100f, first);
            UnityEngine.Time.time = 1f;
            using (Achievements.TrackBlast(second)) Hit(secondTarget, 100f, second, delegate { Win(game); });
            Check(Achievements.Earned("coordinated"), "split targets and the final lethal callback preserve shot attribution");

            Reset();
            game = Begin(); target = Enemy(game); a = Launch(target); b = Launch(target);
            Achievements.Coordinate(new List<Drone> { a, b });
            first = Achievements.Fired(a);
            b.AttackTarget = Enemy(game);
            second = Achievements.Fired(b);
            Hit(target, 40f, first);
            Hit((EnemyUnit)b.AttackTarget, 100f, second);
            Check(!Achievements.Earned("coordinated"), "a changed target cannot reuse stale group membership");

            Reset();
            game = Begin(); target = Enemy(game); a = Launch(target); b = Launch(target);
            Achievements.Coordinate(new List<Drone> { a, b });
            first = Achievements.Fired(a); // misses; its later shots cannot stand in for the missed first volley
            second = Achievements.Fired(b);
            Hit(target, 40f, second);
            Hit(target, 60f, Achievements.Fired(a));
            Check(!Achievements.Earned("coordinated"), "a later volley cannot replace a missed coordinated shot");
        }

        // New runs reject old in-flight evidence, and post-result or sandbox events cannot produce skill awards.
        static void AttemptIsolation()
        {
            Reset();
            var game = Begin();
            var target = Enemy(game);
            var a = Launch(target); var b = Launch(target);
            Achievements.Coordinate(new List<Drone> { a, b });
            var shot = Achievements.Fired(a);
            var oldScope = Achievements.TrackBlast(shot);
            Achievements.BeginMission(game);
            oldScope.Dispose();
            Hit(target, 50f, shot);
            Hit(target, 50f, Achievements.Fired(b));
            Check(!Achievements.Earned("coordinated"), "retry clears groups and rejects earlier projectile evidence");
            Win(game);
            Check(Achievements.Earned("cleanDecoys"), "old scope disposal cannot unbalance the new mission");
            Check(Achievements.Fired(a) == null, "no new shots tracked after results");
            game = Begin();
            game.Mode = GameMode.Sandbox;
            Check(Achievements.Fired(Launch(Enemy(game))) == null, "sandbox shots carry no achievement attribution");
        }

        // Give failures a useful reason without any testing package.
        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }

        // Unknown IDs, repeat calls, overflow and campaign resets cannot corrupt the permanent ledger.
        static void CatalogueAndLedger()
        {
            Reset();
            Check(Achievements.All.Length == 22, "all requested coverage is present");
            var ids = new HashSet<string>();
            foreach (var entry in Achievements.All)
            {
                Check(ids.Add(entry.id), "unique ID: " + entry.id);
                Check(entry.name.Length > 0 && entry.description.Length > 0, "catalogue text");
            }
            Achievements.Unlock("typo");
            Achievements.Count("typo", 10);
            Check(Progress.Values.Count == 0, "unknown IDs ignored");
            Achievements.Unlock("support");
            Achievements.Unlock("support");
            Check(notices == 1 && Progress.Saves == 1, "unlock is idempotent");
            Achievements.Count("decoys", 6);
            Achievements.Count("decoys", -2);
            Check(Achievements.Current("decoys") == 6 && !Achievements.Earned("decoys"), "partial counter");
            Achievements.Count("decoys", int.MaxValue);
            Achievements.Count("decoys", 1);
            Check(Achievements.Current("decoys") == 10 && notices == 2, "counter saturates once");
            Progress.Set("campaign.ach.ignoredThreat", 1);
            Progress.Forget("theatre."); Progress.Forget("campaign."); Progress.Forget("hotspot.");
            Check(Achievements.Earned("support") && Achievements.Current("decoys") == 10, "new campaign keeps achievements");
            Check(Progress.Get("campaign.ach.ignoredThreat") == 0, "new campaign clears its evidence");
            Progress.Values.Clear();
            Check(!Achievements.Earned("support") && Achievements.Current("decoys") == 0, "no stale static save cache");
        }

        // Best stars contribute once, and Mimic stars cannot stand in for the eighth Operation.
        static void OperationStars()
        {
            Reset();
            for (int i = 0; i < 7; i++) Progress.Set("mission" + i + ".stars", 3);
            Progress.Set("mimic.silentWings.stars", 3);
            Achievements.OperationRecords();
            Achievements.OperationRecords();
            Check(Achievements.Current("fullMarks") == 7 && !Achievements.Earned("fullMarks"), "eight named Operations required");
            Check(notices == 7, "operation replay does not duplicate notices");
            Progress.Set("mission7.stars", 2);
            Achievements.OperationRecords();
            Check(Achievements.Earned("mission7") && !Achievements.Earned("fullMarks"), "completion differs from full marks");
            Progress.Set("mission7.stars", 3);
            Achievements.OperationRecords();
            Check(Achievements.Earned("fullMarks") && notices == 9, "all three-star Operations");
        }

        // Check loss, par and the strict greater-than integrity boundary.
        static void MissionResults()
        {
            Reset();
            var game = new SandboxGame { Mode = GameMode.Mission, Mission = new Mission { key = "mimic.hideAndSeek", par = 2 }, DronesUsed = 2, BaseIntegrity = 75f };
            game.Result = MissionResult.Failed;
            Achievements.MissionFinished(game);
            Check(notices == 0, "failed mission awards nothing");
            game.Result = MissionResult.Won;
            game.dronesLost = 1;
            Achievements.MissionFinished(game);
            Check(!Achievements.Earned("par") && !Achievements.Earned("base"), "loss or exactly 75 fails criteria");
            game.dronesLost = 0;
            game.BaseIntegrity = 75.01f;
            Achievements.MissionFinished(game);
            Check(Achievements.Earned("par") && Achievements.Earned("base"), "inclusive par and strict integrity");
            game.Mission.key = "mimic.silentWings";
            Achievements.MissionFinished(game);
            Check(Achievements.Earned("silentWings"), "Mimic uses stable key");
        }

        // Mixed difficulty, old saves, expired threats and round changes must disqualify whole-run awards.
        static void CampaignHistory()
        {
            Reset();
            var spot = new Hotspot();
            var final = new Hotspot { final = true };
            Achievements.CampaignResult(spot, true);
            Theatre.Fights = 1;
            Achievements.CampaignResult(final, true);
            Check(Achievements.Earned("campaign") && Achievements.Earned("veteran") && Achievements.Earned("vigilant"), "tracked Veteran campaign");

            Reset();
            Settings.Difficulty = DifficultyLevel.Regular;
            Achievements.CampaignResult(spot, false);
            Theatre.Fights = 1;
            Settings.Difficulty = DifficultyLevel.Veteran;
            Achievements.CampaignResult(final, true);
            Check(!Achievements.Earned("veteran") && Achievements.Earned("campaign"), "earlier lower difficulty loss persists");

            Reset();
            Theatre.Fights = 5;
            Achievements.CampaignResult(final, true);
            Check(Achievements.Earned("campaign") && !Achievements.Earned("veteran") && !Achievements.Earned("vigilant"), "unknown old campaign history cannot qualify");

            Reset();
            Achievements.CampaignResult(spot, true);
            Theatre.Wins = 2;
            Theatre.ThreatSpot = new Hotspot();
            Theatre.ThreatDue = 10;
            Achievements.CampaignResult(spot, true);
            Check(Progress.Get("campaign.ach.ignoredThreat") == 1, "round transition cannot hide ignored threat");
            Theatre.ThreatSpot = null;
            Achievements.CampaignResult(final, true);
            Check(!Achievements.Earned("vigilant"), "ignored threat persists until campaign ends");

            Reset();
            Theatre.ThreatSpot = spot;
            Theatre.ThreatDue = Theatre.Turn;
            Achievements.CampaignResult(spot, true);
            Check(Progress.Get("campaign.ach.ignoredThreat") == 0, "winning due threat answers it");
            Achievements.ThreatIgnored();
            Check(Achievements.Earned("threat") && Progress.Get("campaign.ach.ignoredThreat") == 0, "regenerated hotspot identity cannot undo an answered threat");
            Reset();
            Theatre.ThreatSpot = spot;
            Theatre.ThreatDue = Theatre.Turn;
            Achievements.CampaignResult(spot, false);
            Check(Progress.Get("campaign.ach.ignoredThreat") == 1, "losing due threat leaves it unanswered");

            Reset();
            Achievements.CampaignResult(spot, true);
            Theatre.ThreatSpot = spot;
            Theatre.ThreatDue = 10;
            Achievements.CampaignResult(final, true);
            Check(!Achievements.Earned("vigilant"), "final operation cannot erase an outstanding threat");
        }
    }
}

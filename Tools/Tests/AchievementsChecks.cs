// Standalone checks for the real Achievements.cs, with small game/Unity stand-ins. No Unity import or player save.
// Compile both files with csc /out:Temp/AchievementsChecks.exe, then run that executable.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
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

    // Supply the preference the campaign hook reads.
    public static class Settings { public static DifficultyLevel Difficulty; }
    // Supply the mission fields used by the completion hook.
    public class Mission { public string key; public int par; }
    // Supply final mission results without running the world.
    public class SandboxGame
    {
        public GameMode Mode;
        public Mission Mission;
        public MissionResult Result;
        public int DronesUsed, dronesLost;
        public float BaseIntegrity;
    }
    // Model only hotspot identity and completion state.
    public class Hotspot { public bool final, done; }
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

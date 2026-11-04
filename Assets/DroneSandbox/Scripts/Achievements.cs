using System;
using UnityEngine;

namespace DroneSandbox
{
    // One local achievement, including the text the HUD shows and the total for its progress counter.
    public sealed class Achievement
    {
        public readonly string id, name, description;
        public readonly bool hidden;
        public readonly int target;

        // Keep the catalogue data together so a later platform adapter uses the same stable IDs.
        public Achievement(string id, string name, string description, bool hidden = false, int target = 1)
        {
            this.id = id;
            this.name = name;
            this.description = description;
            this.hidden = hidden;
            this.target = target;
        }
    }

    // Local achievements live outside campaign reset prefixes; only evidence for the current campaign resets.
    public static class Achievements
    {
        public static readonly Achievement[] All =
        {
            new Achievement("mission0", "First Light", "Complete First Light."),
            new Achievement("mission1", "Open the Sky", "Complete Open the Sky."),
            new Achievement("mission2", "Hidden Guns", "Complete Hidden Guns."),
            new Achievement("mission3", "Convoy", "Complete Convoy."),
            new Achievement("mission4", "Command Post", "Complete Command Post."),
            new Achievement("mission5", "Long Watch", "Complete Long Watch."),
            new Achievement("mission6", "Last Delivery", "Complete Last Delivery."),
            new Achievement("mission7", "Silent Corridor", "Complete Silent Corridor."),
            new Achievement("fullMarks", "Full Marks", "Earn three stars in all eight Operations.", target: 8),
            new Achievement("campaign", "Orders Complete", "Complete the campaign."),
            new Achievement("veteran", "Veteran Service", "Complete a whole campaign on Veteran."),
            new Achievement("threat", "Held Ground", "Complete the operation marked as the turn's threat."),
            new Achievement("vigilant", "Standing Watch", "Complete a campaign without leaving a threat unanswered."),
            new Achievement("support", "Called In", "Spend a support charge in a campaign briefing."),
            new Achievement("par", "Light Footprint", "Complete a mission at or under par drones with none lost."),
            new Achievement("coordinated", "On the Mark", "Destroy a mission target with a coordinated strike."),
            new Achievement("decoys", "Patient Eyes", "Expose ten decoys by watching; repeat reveals do not count.", target: 10),
            new Achievement("cleanDecoys", "Clear Picture", "Complete a decoy mission without striking a decoy."),
            new Achievement("identification", "Positive ID", "Complete an Identification Hunt without wasting a Kamikaze."),
            new Achievement("silentWings", "Silent Wings", "Complete Silent Wings."),
            new Achievement("base", "Firm Base", "Complete Hide and Seek with base integrity above 75%.", hidden: true),
            new Achievement("runner", "Pursuit", "Bring down a spy robot after it bolts.", hidden: true),
        };

        public static event Action<Achievement> Unlocked;

        // Resolve only catalogue IDs so a typo in a hook cannot create an invisible achievement.
        static Achievement Find(string id)
        {
            foreach (var entry in All) if (entry.id == id) return entry;
            return null;
        }

        // Read the save directly, including when the autotest replaces it with an empty in-memory store.
        public static bool Earned(string id)
        {
            return Progress.Get("ach." + id) == 1;
        }

        // Return a bounded counter for the page, with completed achievements always showing their full total.
        public static int Current(string id)
        {
            var entry = Find(id);
            if (entry == null) return 0;
            return Earned(id) ? entry.target : Mathf.Clamp(Progress.Get("ach." + id + ".count"), 0, entry.target);
        }

        // Save before announcing; repeated hooks and replays can never announce or award the same ID twice.
        public static void Unlock(string id)
        {
            var entry = Find(id);
            if (entry == null || Earned(id)) return;
            Progress.Set("ach." + id, 1);
            Progress.Save();
            // Steam: a later adapter calls SteamUserStats.SetAchievement(id) and StoreStats here, after local saving.
            var announce = Unlocked;
            if (announce != null) announce(entry);
        }

        // Add already-deduplicated evidence, saturating before addition so even a large increment cannot wrap.
        public static void Count(string id, int n)
        {
            var entry = Find(id);
            if (entry == null || entry.target <= 1 || n <= 0 || Earned(id)) return;
            int value = Current(id) + Mathf.Min(n, entry.target - Current(id));
            Progress.Set("ach." + id + ".count", value);
            if (value >= entry.target) Unlock(id);
            else Progress.Save();
        }

        // Saved best stars are unique evidence, including Operations completed before achievements were added.
        public static void OperationRecords()
        {
            int full = 0;
            for (int i = 0; i < 8; i++)
            {
                string id = "mission" + i;
                int stars = Progress.Get(id + ".stars");
                if (stars > 0) Unlock(id);
                if (stars >= 3) full++;
            }
            Count("fullMarks", full - Current("fullMarks"));
        }

        // Read the final score only after extraction losses and best stars have been recorded by Finish.
        public static void MissionFinished(SandboxGame game)
        {
            if (game.Mode != GameMode.Mission || game.Mission == null || game.Result != MissionResult.Won) return;
            OperationRecords();
            if (game.DronesUsed <= game.Mission.par && game.dronesLost == 0) Unlock("par");
            if (game.Mission.key == "mimic.silentWings") Unlock("silentWings");
            if (game.Mission.key == "mimic.hideAndSeek" && game.BaseIntegrity > 75f) Unlock("base");
        }

        // Record every campaign attempt before Theatre changes wins, rounds or threats, including abandonments.
        public static void CampaignResult(Hotspot spot, bool won)
        {
            if (spot == null || Theatre.Victory || (!spot.final && Theatre.Done(spot))) return;
            if (Progress.Get("campaign.ach.tracked") == 0)
            {
                bool fresh = Theatre.Fights == 0 && Theatre.Wins == 0 && Theatre.Losses == 0 && Theatre.Turn == 1 && Theatre.Standing == 0;
                Progress.Set("campaign.ach.completeHistory", fresh ? 1 : 0);
                Progress.Set("campaign.ach.tracked", 1);
            }
            if (Settings.Difficulty != DifficultyLevel.Veteran) Progress.Set("campaign.ach.lowerDifficulty", 1);

            // A round change can remove a threat before EndTurn sees it; finishing the campaign also closes it.
            var threat = Theatre.ThreatSpot;
            if (won && Theatre.IsThreat(spot))
            {
                Progress.Set("campaign.ach.answeredThreatTurn", Theatre.Turn);
                Unlock("threat");
            }
            if (threat != null && !(won && Theatre.IsThreat(spot)) &&
                (Theatre.Turn >= Theatre.ThreatDue || (won && (spot.final || (Theatre.Wins + 1) % 3 == 0))))
                ThreatIgnored();

            if (won && spot.final)
            {
                Unlock("campaign");
                if (Progress.Get("campaign.ach.completeHistory") == 1)
                {
                    if (Progress.Get("campaign.ach.lowerDifficulty") == 0) Unlock("veteran");
                    if (Progress.Get("campaign.ach.ignoredThreat") == 0) Unlock("vigilant");
                }
            }
            Progress.Save();
        }

        // Keep this disqualification with campaign state, which New campaign deliberately clears.
        public static void ThreatIgnored()
        {
            // Opening the final can rebuild hotspot objects before Theatre checks the winning one's identity.
            if (Progress.Get("campaign.ach.answeredThreatTurn", -1) != Theatre.Turn)
                Progress.Set("campaign.ach.ignoredThreat", 1);
        }
    }
}

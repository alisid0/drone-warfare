namespace DroneSandbox
{
    // Saved in Settings; Regular preserves the original game rules.
    public enum DifficultyLevel { Recruit, Regular, Veteran }

    // Difficulty changes information and reaction time, never health, damage or access to missions.
    public static class Difficulty
    {
        // The settings choices, in the same order as the saved values.
        public static readonly string[] Names = { "Recruit", "Regular", "Veteran" };

        // Scale only the first reports' uncertainty circles.
        public static float ReportCircleFactor => Settings.Difficulty == DifficultyLevel.Recruit ? 0.75f
            : Settings.Difficulty == DifficultyLevel.Veteran ? 1.25f : 1f;

        // Time spent laying AA guns after the existing warning, including radar-assisted locks.
        public static float AALockTimeFactor => Settings.Difficulty == DifficultyLevel.Recruit ? 1.35f
            : Settings.Difficulty == DifficultyLevel.Veteran ? 0.85f : 1f;

        // Detection is immediate inside a range, so Veteran reacts sooner by judging distance as 80% of normal.
        public static float AlertReactionFactor => Settings.Difficulty == DifficultyLevel.Veteran ? 0.8f : 1f;

        // Recruit hears the unit-alert warning a second before the unit acts; repeated sightings do not extend it.
        public static float AlertWarningSeconds => Settings.Difficulty == DifficultyLevel.Recruit ? 1f : 0f;

        // Recruit gets one spare of an existing strike type, without granting the Nightjar or a new weapon type.
        public static bool SpareDrone => Settings.Difficulty == DifficultyLevel.Recruit;

        // Extra time to bring surviving drones home after the objective is complete.
        public static float ExtraExtractSeconds => Settings.Difficulty == DifficultyLevel.Recruit ? 15f : 0f;

        // Keep at least one false contact; Veteran's extra still needs a fair site and stock in Situations.
        public static int DecoyAdjustment(int count)
        {
            if (Settings.Difficulty == DifficultyLevel.Recruit && count > 1) return -1;
            if (Settings.Difficulty == DifficultyLevel.Veteran && count > 0) return 1;
            return 0;
        }

        // Prefer fast Kamikazes for convoys and reusable Strikes elsewhere, using only what the mission offers.
        public static int SpareStock(Mission mission, DroneType type)
        {
            if (!SpareDrone || mission.shadowSeconds > 0f || mission.huntRobots > 0) return 0;
            DroneType spare = mission.kamikazes > 0 && (mission.strikes <= 0 || mission.convoyRoute != null)
                ? DroneType.Kamikaze : DroneType.Strike;
            return type == spare && mission.Stock(spare) > 0 ? 1 : 0;
        }
    }
}

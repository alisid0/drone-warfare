using System.Collections.Generic;
using UnityEngine;

// Only engine/world-build services are substituted. Planning, preparation, routing and situations are real sources.
namespace DroneSandbox
{
    // Production Ground owns terrain rendering; mission height queries use the crop's actual world height here.
    public static partial class Ground
    {
        public static float HeightAt(Vector3 p) => MapLayout.Plan.world?.Height(p.x, p.z) ?? 0f;
    }

    // The production constant is read by the harness assertion; no scene or native World.Build runs.
    public static class World { public const int WorldSeed = 1200; }

    // The real Hotspot class is extracted by run.ps1; the roster is controlled for order/separation checks.
    public static class Theatre
    {
        public static readonly List<Hotspot> Hotspots = new List<Hotspot>();
        public static float Advance { get; set; }
        public static float Front(float x) => 0.5f + 0.13f * Mathf.Sin(x * 6.3f) + Advance;
    }

    // Vegetation is not built in this harness; mission planning still runs every other placement rule.
    public static class Undergrowth { public static bool UnderTrees(Vector2 p, float margin) => false; }
    // Regular difficulty leaves the real situation recipes' unit counts unchanged.
    public static class Difficulty { public static bool SpareDrone => false; public static int DecoyAdjustment(int count) => 0; }
    // Only the briefing's established base name is needed; flags and materials have no planning role.
    public static class Factions { public const string PlayerBase = "Forward Base Kestrel"; }
}

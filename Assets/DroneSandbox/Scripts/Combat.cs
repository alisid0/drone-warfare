using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Targeting and area damage shared by drones, enemies and projectiles.
    public static class Combat
    {
        // True while a heavy bomb's blast is being dealt out: the only thing a hardened target really feels.
        public static bool Heavy { get; private set; }

        public static void HeavyBlast(Vector3 center, float blastRadius, float damage, Team attacker)
        {
            Heavy = true;
            try { Blast(center, blastRadius, damage, attacker); }
            finally { Heavy = false; }
        }

        static IEnumerable<Unit> HostilesOf(Team team)
        {
            var game = SandboxGame.Instance;
            if (team == Team.Player) return game.enemies;
            return game.drones;
        }

        // Closest living enemy of `team` within `range`. With horizontalOnly the height difference is ignored.
        public static Unit NearestHostile(Vector3 from, Team team, float range, bool horizontalOnly)
        {
            Unit best = null;
            float bestDistance = range;
            foreach (var unit in HostilesOf(team))
            {
                if (unit == null || !unit.IsAlive) continue;
                if (unit is EnemyUnit enemy && (enemy.Hidden || !enemy.Observed)) continue; // can't aim at what you can't see
                Vector3 offset = unit.AimPoint - from;
                if (horizontalOnly) offset.y = 0f;
                float distance = offset.magnitude;
                if (distance <= bestDistance)
                {
                    best = unit;
                    bestDistance = distance;
                }
            }
            return best;
        }

        // Damages every hostile unit caught in the blast, less at the edge.
        public static void Blast(Vector3 center, float blastRadius, float damage, Team attacker)
        {
            // Copy first: units that die are removed from the game's lists mid-loop.
            foreach (var unit in new List<Unit>(HostilesOf(attacker)))
            {
                if (unit == null || !unit.IsAlive) continue;
                float distance = Mathf.Max(0f, Vector3.Distance(center, unit.AimPoint) - unit.radius);
                if (distance > blastRadius) continue;
                float falloff = 1f - 0.5f * (distance / blastRadius);
                unit.TakeDamage(damage * falloff);
            }
        }
    }
}

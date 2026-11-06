using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // A camera lowers detail under load, retaining every logical tree through cards or the canopy surface.
    public static class ForestBudget
    {
        public const int FullLimit = 200, ReducedLimit = 1000, CardLimit = 5000, TriangleLimit = 1800000;

        // Sorted camera distances and measured mesh costs keep the policy independent of Unity render objects.
        public struct Entry
        {
            public float distance;
            public int fullTriangles, reducedTriangles, cardTriangles;
        }

        // Both sides of each fade count against the limits; reserve the complete card allowance before meshes.
        public static Vector4 Select(IReadOnlyList<Entry> entries, float readyReach = 350f)
        {
            float full = Mathf.Min(45f, readyReach), reduced = Mathf.Min(125f, readyReach), card = Mathf.Min(350f, readyReach);
            int fullCount = 0, reducedCount = 0, triangles = 0, cards = 0;
            foreach (var v in entries)
            {
                if (v.distance > full) continue;
                if (fullCount >= FullLimit || triangles + v.fullTriangles > TriangleLimit / 2)
                    full = Mathf.Max(0f, v.distance - 0.01f);
                else { triangles += v.fullTriangles; fullCount++; }
            }
            foreach (var v in entries)
            {
                if (v.distance < full - 10f || v.distance > reduced) continue;
                if (reducedCount >= ReducedLimit || triangles + v.reducedTriangles > TriangleLimit - CardLimit * 8)
                    reduced = Mathf.Max(full, v.distance - 0.01f);
                else { triangles += v.reducedTriangles; reducedCount++; }
            }
            foreach (var v in entries)
                if (v.distance >= reduced - 20f && v.distance <= card && ++cards > CardLimit)
                    card = Mathf.Max(reduced, v.distance - 0.01f);
            return new Vector4(full, reduced, card, 700f);
        }
    }
}

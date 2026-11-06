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

        // One-metre bins bound selection cost without sorting or copying thousands of tree records each frame.
        public struct Bucket
        {
            public int full, reduced, cards, fullTriangles, reducedTriangles, cardTriangles;
        }

        // Prefix sums include the movement guard and both sides of a fade in every instance and triangle allowance.
        public static Vector4 SelectBuckets(Bucket[] bins, float readyReach, int guard)
        {
            for (int i = 1; i < bins.Length; i++)
            {
                var a = bins[i - 1]; var b = bins[i];
                b.full += a.full; b.reduced += a.reduced; b.cards += a.cards;
                b.fullTriangles += a.fullTriangles; b.reducedTriangles += a.reducedTriangles; b.cardTriangles += a.cardTriangles;
                bins[i] = b;
            }
            int card = Mathf.Clamp(Mathf.FloorToInt(readyReach - guard), 0, 350);
            int full = Mathf.Min(45, card), reduced = Mathf.Min(125, card);
            while (full > 0)
            {
                var a = Range(bins, 0, full + guard);
                if (a.full <= FullLimit && a.fullTriangles <= TriangleLimit / 2) break;
                full--;
            }
            int fullCost = full > 0 ? Range(bins, 0, full + guard).fullTriangles : 0;
            while (reduced > full)
            {
                var a = Range(bins, Mathf.Max(0, full - 10 - guard), reduced + guard);
                if (a.reduced <= ReducedLimit && fullCost + a.reducedTriangles <= TriangleLimit - CardLimit * 8) break;
                reduced--;
            }
            int meshCost = fullCost + (reduced > 0 ? Range(bins, Mathf.Max(0, full - 10 - guard), reduced + guard).reducedTriangles : 0);
            while (card > reduced)
            {
                var a = Range(bins, Mathf.Max(0, reduced - 20 - guard), card + guard);
                if (a.cards <= CardLimit && meshCost + a.cardTriangles <= TriangleLimit) break;
                card--;
            }
            return new Vector4(full, reduced, card, 700f);
        }

        // Inclusive bins deliberately round outwards, so every submitted boundary instance is charged to the budget.
        static Bucket Range(Bucket[] bins, int from, int to)
        {
            var a = from > 0 ? bins[Mathf.Min(from - 1, bins.Length - 1)] : default;
            var b = bins[Mathf.Min(to, bins.Length - 1)];
            return new Bucket { full = b.full - a.full, reduced = b.reduced - a.reduced, cards = b.cards - a.cards,
                fullTriangles = b.fullTriangles - a.fullTriangles, reducedTriangles = b.reducedTriangles - a.reducedTriangles,
                cardTriangles = b.cardTriangles - a.cardTriangles };
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

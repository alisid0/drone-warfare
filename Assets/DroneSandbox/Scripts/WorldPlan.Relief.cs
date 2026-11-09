using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Random = System.Random; // System and UnityEngine both have one; the plan uses the seeded kind

namespace DroneSandbox
{
    // Relief is built before any human geography; the routing graph samples this same land.
    public static partial class WorldPlan
    {
        // Warped hills, an incomplete northern range and isolated ridges give long-distance flight landmarks.
        static void PlanRelief(Random rng)
        {
            peaks.Add(new Vector2(Range(rng, -6000f, 1000f), Range(rng, 4000f, 11000f)));
            peaks.Add(new Vector2(Range(rng, 10000f, 18000f), Range(rng, -17000f, -8000f)));
            peaks.Add(new Vector2(Range(rng, 1000f, 13000f), Range(rng, 22000f, 26000f)));
            deepForest = new Vector2(Range(rng, 12000f, 20000f), Range(rng, -1000f, 7000f));
            heights = new float[GridCount * GridCount];
            cover = new Cover[heights.Length];
            waterLevel = new float[heights.Length];
            Parallel.For(0, GridCount, j =>
            {
                float z = -Half + j * HeightStep;
                for (int i = 0; i < GridCount; i++)
                {
                    float x = -Half + i * HeightStep;
                    float u = x + shiftX, v = z + shiftZ;
                    float warpX = (Noise(u / 13000f, v / 13000f, 1) - 0.5f) * 6000f;
                    float warpZ = (Noise(u / 11000f, v / 11000f, 2) - 0.5f) * 6000f;
                    u += warpX; v += warpZ;
                    float east = Smooth((x + 13000f + warpX) / 35000f);
                    float north = Smooth((z - 12000f + warpZ * 0.3f) / 14000f);
                    float hills = Noise(u / 5800f, v / 5800f, 3) * 0.65f + Noise(u / 2300f, v / 2300f, 4) * 0.35f;
                    float ridges = 1f - Math.Abs(Noise((u + v * 0.55f) / 5200f, (v - u * 0.35f) / 8500f, 5) * 2f - 1f);
                    float range = Smooth((x + 14000f) / 16000f) * (0.55f + 0.45f * ridges * ridges);
                    float h = 19f + 13f * (z + Half) / Size + 20f * hills + east * (24f + 145f * hills);
                    h += north * range * 350f;
                    h += (1f + east * 5f) * (Noise(u / 650f, v / 650f, 6) - 0.5f);
                    for (int k = 0; k < peaks.Count; k++)
                    {
                        float dx = x - peaks[k].x, dz = z - peaks[k].y;
                        float along = dx * 0.85f + dz * 0.53f, across = dz * 0.85f - dx * 0.53f;
                        float r = along * along / 12000000f + across * across / 2300000f;
                        h += (k == 2 ? 155f : 220f) * (float)Math.Exp(-r * 0.5f);
                    }
                    if (h > 480f) h = 480f + 120f * (1f - (float)Math.Exp(-(h - 480f) / 120f));
                    heights[j * GridCount + i] = h;
                    waterLevel[j * GridCount + i] = float.NaN;
                }
            });
        }

        // The small routing grid is discarded after generation; terrain queries keep the full 32 m grid.
        static void BuildRouteHeights()
        {
            if (routeHeights == null) routeHeights = new float[RouteCount * RouteCount];
            for (int j = 0; j < RouteCount; j++)
                for (int i = 0; i < RouteCount; i++) routeHeights[j * RouteCount + i] = Height(-Half + i * RouteStep, -Half + j * RouteStep);
        }

        // Heap entries use their node index to resolve equal costs reproducibly across runtimes.
        struct RouteEntry
        {
            public int node;
            public float cost;
        }

        // A small managed binary heap avoids a framework/package dependency for A*.
        sealed class RouteHeap
        {
            RouteEntry[] entries = new RouteEntry[4096];
            public int count;

            // Enqueue a cheaper candidate; stale entries are ignored when their node has already closed.
            public void Push(int node, float cost)
            {
                if (count == entries.Length) Array.Resize(ref entries, count * 2);
                int k = count++;
                var entry = new RouteEntry { node = node, cost = cost };
                while (k > 0)
                {
                    int p = (k - 1) / 2;
                    if (Before(entries[p], entry)) break;
                    entries[k] = entries[p]; k = p;
                }
                entries[k] = entry;
            }

            // Remove the best candidate and repair the heap without sorting or allocating.
            public int Pop()
            {
                int result = entries[0].node;
                RouteEntry last = entries[--count];
                int k = 0;
                while (k * 2 + 1 < count)
                {
                    int c = k * 2 + 1;
                    if (c + 1 < count && Before(entries[c + 1], entries[c])) c++;
                    if (Before(last, entries[c])) break;
                    entries[k] = entries[c]; k = c;
                }
                entries[k] = last;
                return result;
            }

            // Stable tie-breaking is part of the generator version's deterministic contract.
            static bool Before(RouteEntry a, RouteEntry b) { return a.cost < b.cost || (a.cost == b.cost && a.node < b.node); }
        }

        static readonly int[] RouteDx = { -1, 0, 1, -1, 1, -1, 0, 1, -2, -2, 2, 2, -1, 1, -1, 1 };
        static readonly int[] RouteDz = { -1, -1, -1, 0, 0, 1, 1, 1, -1, 1, -1, 1, -2, -2, 2, 2 };

        // Terrain-cost routes: rivers seek low corridors; roads avoid grades and lakes; rail grades cost more.
        // Sixteen directions and corner rounding avoid an eight-direction grid imprint on the final vectors.
        static List<Vector2> FindPath(Vector2 a, Vector2 b, int mode)
        {
            int start = RouteIndex(a), finish = RouteIndex(b), count = RouteCount * RouteCount;
            if (start == finish) return new List<Vector2> { a, b };
            var costs = new float[count];
            var parent = new int[count];
            var closed = new bool[count];
            for (int i = 0; i < count; i++) costs[i] = float.MaxValue;
            var heap = new RouteHeap();
            costs[start] = 0f; parent[start] = -1; heap.Push(start, 0f);
            while (heap.count > 0)
            {
                int k = heap.Pop();
                if (closed[k]) continue;
                closed[k] = true;
                if (k == finish) break;
                int x = k % RouteCount, z = k / RouteCount;
                Vector2 p = RoutePoint(k);
                for (int d = 0; d < RouteDx.Length; d++)
                {
                    int nx = x + RouteDx[d], nz = z + RouteDz[d];
                    if (nx < 0 || nz < 0 || nx >= RouteCount || nz >= RouteCount) continue;
                    int n = nz * RouteCount + nx;
                    if (closed[n]) continue;
                    Vector2 q = RoutePoint(n);
                    float length = Distance(p, q), grade = Math.Abs(routeHeights[n] - routeHeights[k]) / length;
                    float rough = Noise(q.x / 2800f, q.y / 2800f, 41);
                    float cost;
                    if (mode == 0)
                        cost = 1f + routeHeights[n] * 0.018f + rough * 0.55f + Math.Max(0f, routeHeights[n] - routeHeights[k]) / length * 45f;
                    else
                    {
                        // Sample between graph nodes too: a 32 m bank can disappear between 250 m endpoints.
                        float previous = routeHeights[k];
                        for (int sample = 1; sample <= 4; sample++)
                        {
                            Vector2 at = Mix(p, q, sample * 0.25f);
                            float height = sample == 4 ? routeHeights[n] : Height(at.x, at.y);
                            grade = Math.Max(grade, Math.Abs(height - previous) * 4f / length); previous = height;
                        }
                        cost = 1f + rough * 0.35f + grade * grade * (mode == 2 ? 3200f : 850f);
                        Vector2 mid = Mix(p, q, 0.5f);
                        cost += (mode == 2 ? 5f : 0.7f) * (1f - Smooth(RiverDistance(mid.x, mid.y) / 350f));
                        if (CoverAt(q.x, q.y) == Cover.Water || CoverAt(mid.x, mid.y) == Cover.Water)
                            cost += InLake(mid) || InLake(q) ? 400f : 8f;
                        if (grade > (mode == 2 ? 0.035f : 0.10f)) cost += 200f;
                    }
                    float next = costs[k] + length * cost;
                    if (next >= costs[n]) continue;
                    costs[n] = next; parent[n] = k;
                    heap.Push(n, next + Distance(q, RoutePoint(finish)));
                }
            }
            var raw = new List<Vector2>();
            for (int k = finish; k >= 0; k = parent[k]) raw.Add(RoutePoint(k));
            raw.Reverse(); raw[0] = a; raw[raw.Count - 1] = b;
            var path = SmoothPath(raw);
            if (mode != 0) Meander(path, mode == 2 ? 34f : 65f, 900f, 141, mode == 2 ? 0.025f : 0.08f);
            return path;
        }

        // Snap routing endpoints only internally; the exported line always retains the exact requested ends.
        static int RouteIndex(Vector2 p)
        {
            int x = (int)Clamp((float)Math.Round((p.x + Half) / RouteStep), 0f, RouteCount - 1);
            int z = (int)Clamp((float)Math.Round((p.y + Half) / RouteStep), 0f, RouteCount - 1);
            return z * RouteCount + x;
        }
        // Convert the graph index back into absolute world metres.
        static Vector2 RoutePoint(int k) { return new Vector2(-Half + k % RouteCount * RouteStep, -Half + k / RouteCount * RouteStep); }

        // Round corners twice, retaining ends; lake clearance is checked when transport vertices move.
        static List<Vector2> SmoothPath(List<Vector2> points)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var next = new List<Vector2>(); next.Add(points[0]);
                for (int k = 0; k + 1 < points.Count; k++)
                {
                    Vector2 a = Mix(points[k], points[k + 1], 0.25f), b = Mix(points[k], points[k + 1], 0.75f);
                    next.Add(a); next.Add(b);
                }
                next.Add(points[points.Count - 1]); points = next;
            }
            var sampled = new List<Vector2>(); sampled.Add(points[0]);
            for (int k = 1; k < points.Count; k++)
            {
                Vector2 a = sampled[sampled.Count - 1], b = points[k];
                if (Distance(a, b) < 65f && k + 1 < points.Count) continue;
                int n = Math.Max(1, (int)Math.Ceiling(Distance(a, b) / 110f));
                for (int s = 1; s <= n; s++) sampled.Add(Mix(a, b, s / (float)n));
            }
            return sampled;
        }

        // Smooth lateral variation follows the corridor, with zero displacement at junctions and outlets.
        // Rivers meander on a smaller scale than their terrain-guided valleys; roads use much gentler curves.
        static void Meander(List<Vector2> points, float amplitude, float wavelength, int salt, float maxGrade = 0.10f)
        {
            var original = points.ToArray();
            var distance = new float[points.Count];
            for (int k = 1; k < points.Count; k++) distance[k] = distance[k - 1] + Distance(original[k - 1], original[k]);
            float total = distance[points.Count - 1];
            for (int k = 1; k + 1 < points.Count; k++)
            {
                Vector2 p = original[k], a = original[Math.Max(0, k - 6)], b = original[Math.Min(points.Count - 1, k + 6)];
                float dx = b.x - a.x, dz = b.y - a.y, length = Distance(a, b);
                if (length < 1f) continue;
                float t = distance[k] / wavelength;
                float fade = Smooth(distance[k] / 900f) * Smooth((total - distance[k]) / 900f);
                float wander = salt == 141 ? 0f : 2f * (Noise(distance[k] / 3400f, salt, 112) - 0.5f);
                float strength = salt == 141 ? 1f : 0.45f + Noise(distance[k] / 1900f, salt, 113);
                float offset = amplitude * strength * fade * (0.72f * (float)Math.Sin(t + wander + phase + salt) + 0.28f * (float)Math.Sin(t * 1.93f + salt));
                Vector2 q = new Vector2(Clamp(p.x - dz / length * offset, -Half, Half), Clamp(p.y + dx / length * offset, -Half, Half));
                if (salt == 141 && (InLake(q) || Grade(q.x, q.y) > maxGrade)) continue;
                points[k] = q;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Mission access roads use dry ground and exported bridges, without adding a new crossing at the crop edge.
    public static partial class WorldSectors
    {
        // A small metre-grid route tree joins compounds to the clipped network. Water is an obstacle, not a cost.
        sealed class Access
        {
            const float Step = 25f, Reach = 950f;
            const int Count = 77;
            readonly WorldSectorData data;
            readonly bool[] dry = new bool[Count * Count];
            readonly float[] distance = new float[Count * Count];
            readonly int[] previous = new int[Count * Count], crossing = new int[Count * Count];
            readonly bool[] used = new bool[Count * Count];
            readonly List<(int a, int b, WorldBridge bridge)> bridges = new List<(int, int, WorldBridge)>();
            readonly int root;

            // Cache ground suitability once, then flood outward from the HQ through land and real bridge decks.
            public Access(WorldSectorData world, Vector2 hq)
            {
                data = world;
                for (int k = 0; k < dry.Length; k++)
                {
                    Vector2 p = Point(k);
                    dry[k] = !data.FeatureReserved(p, 12f) && data.DryDistance(p) > 5f;
                    distance[k] = float.MaxValue; previous[k] = crossing[k] = -1;
                }
                root = Nearest(hq, false);
                if (root < 0) throw new InvalidOperationException("World square has no dry mission access ground.");
                foreach (var bridge in data.bridges)
                {
                    if (bridge.railway || !Inside(bridge.a, Reach) || !Inside(bridge.b, Reach)) continue;
                    int a = BankNode(bridge.a), b = BankNode(bridge.b);
                    if (a < 0 || b < 0 || a == b) continue;
                    bridges.Add((a, b, bridge));
                }
                var open = new SortedSet<(float cost, int node)>();
                distance[root] = 0f; open.Add((0f, root));
                while (open.Count > 0)
                {
                    var item = open.Min; open.Remove(item);
                    int k = item.node, x = k % Count, z = k / Count;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0 || x + dx < 0 || x + dx >= Count || z + dz < 0 || z + dz >= Count) continue;
                            int next = (z + dz) * Count + x + dx;
                            if (!dry[next] || !dry[z * Count + x + dx] || !dry[(z + dz) * Count + x]) continue;
                            float length = dx != 0 && dz != 0 ? Step * 1.414214f : Step;
                            float rise = Mathf.Abs(data.Height(Point(k).x, Point(k).y) - data.Height(Point(next).x, Point(next).y));
                            float cost = length + rise * 8f;
                            if (distance[k] + cost >= distance[next] || !DryLine(Point(k), Point(next))) continue;
                            Relax(k, next, cost, -1, open);
                        }
                    for (int i = 0; i < bridges.Count; i++)
                    {
                        var b = bridges[i];
                        int next = b.a == k ? b.b : b.b == k ? b.a : -1;
                        if (next >= 0) Relax(k, next, Vector2.Distance(Point(k), Point(next)), i, open);
                    }
                }
                used[root] = true;
            }

            // A deterministic Dijkstra relaxation also records which physical bridge carries a non-grid edge.
            void Relax(int from, int to, float cost, int bridge, SortedSet<(float cost, int node)> open)
            {
                float candidate = distance[from] + cost;
                if (candidate >= distance[to]) return;
                open.Remove((distance[to], to)); distance[to] = candidate; previous[to] = from; crossing[to] = bridge;
                open.Add((candidate, to));
            }

            // Grid centres remain a long way inside the edge, so access tracks cannot create new world crossings.
            static Vector2 Point(int k) => new Vector2(-Reach + (k % Count) * Step, -Reach + (k / Count) * Step);

            // Find the closest dry grid node, optionally restricted to the HQ's connected land component.
            int Nearest(Vector2 p, bool connected)
            {
                int best = -1; float score = float.MaxValue;
                for (int k = 0; k < dry.Length; k++)
                {
                    if (!dry[k] || connected && distance[k] == float.MaxValue) continue;
                    float d = (Point(k) - p).sqrMagnitude;
                    if (d < score) { best = k; score = d; }
                }
                return best;
            }

            // Bridge approaches must stay on their own bank; a coarse water raster must not erase a narrow deck.
            int BankNode(Vector2 bank)
            {
                int best = -1; float score = 100f * 100f;
                for (int k = 0; k < dry.Length; k++)
                {
                    if (!dry[k]) continue;
                    float d = (Point(k) - bank).sqrMagnitude;
                    if (d >= score || !DryLine(Point(k), bank, true)) continue;
                    best = k; score = d;
                }
                return best;
            }

            // Sample short segments as well as endpoints, preventing a 25 m grid step from jumping a stream.
            bool DryLine(Vector2 a, Vector2 b, bool bank = false)
            {
                int steps = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(a, b) / 6f));
                for (int i = 0; i <= steps; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                    if (data.FeatureReserved(p, 8f)) return false;
                    if (bank)
                    {
                        if (data.RiverDistance(p, out float width, out _) < width + 1f || data.LakeDistance(p, out _) < 2f) return false;
                    }
                    else if (data.DryDistance(p) < 3f) return false;
                }
                return true;
            }

            // Compounds need a reachable node nearby, and a dry short connection to it.
            public bool Reaches(Vector2 p)
            {
                int k = Nearest(p, true);
                return k >= 0 && Vector2.Distance(p, Point(k)) <= 40f && DryLine(p, Point(k));
            }

            // Connect a destination to the already built tree, sharing exact vertices at every branch.
            public Vector2[] Join(Vector2 destination, Vector2 hq, bool first = false)
            {
                int k = Nearest(destination, true);
                if (k < 0 || Vector2.Distance(destination, Point(k)) > 70f || !DryLine(destination, Point(k))) return null;
                var points = new List<Vector2> { destination };
                Add(points, Point(k));
                while (!used[k])
                {
                    used[k] = true;
                    int next = previous[k];
                    if (next < 0) break;
                    if (crossing[k] >= 0)
                    {
                        var link = bridges[crossing[k]];
                        if (k == link.a) { Add(points, link.bridge.a); Add(points, link.bridge.position); Add(points, link.bridge.b); }
                        else { Add(points, link.bridge.b); Add(points, link.bridge.position); Add(points, link.bridge.a); }
                    }
                    Add(points, Point(next)); k = next;
                }
                if (k == root) Add(points, hq);
                if (first)
                {
                    TrimBase(points);
                    // Trimming the compound interior also removes those nodes from the built road tree. A later
                    // spur must reach a surviving vertex, rather than end at the now absent base-centre node.
                    for (int i = 0; i < used.Length; i++)
                        if (Vector2.Distance(Point(i), destination) < MapLayout.BaseRadius + 2f) used[i] = false;
                }
                return points.Count > 1 ? points.ToArray() : null;
            }

            // The first route starts at the base gate, as SectorGenerator's route does, and ends inside the HQ.
            static void TrimBase(List<Vector2> points)
            {
                if (points.Count < 2) return;
                Vector2 origin = points[0]; float radius = MapLayout.BaseRadius + 2f;
                while (points.Count > 2 && Vector2.Distance(origin, points[1]) < radius) points.RemoveAt(1);
                points[0] = origin + (points[1] - origin).normalized * radius;
            }

            // Avoid zero-length curve segments where a bridge bank or a compound coincides with a grid node.
            static void Add(List<Vector2> points, Vector2 p)
            {
                if ((points[points.Count - 1] - p).sqrMagnitude > 0.0001f) points.Add(p);
            }

            // Join each world fragment at a dry in-square vertex, then add an interior approach if there are no roads.
            public void ConnectWorld(SectorPlan plan)
            {
                foreach (var road in data.roads)
                {
                    Vector2 candidate = default; float best = float.MaxValue;
                    foreach (var p in road.points)
                    {
                        if (!Inside(p, 925f) || data.DryDistance(p) < 7f) continue;
                        float d = (p - Flat(plan.enemyHqPosition)).sqrMagnitude;
                        if (d < best && Reaches(p)) { candidate = p; best = d; }
                    }
                    if (best == float.MaxValue) continue;
                    var path = Join(candidate, Flat(plan.enemyHqPosition));
                    if (path != null) plan.roads.Add(path);
                }
                if (data.roads.Count > 0) return;
                int far = -1; float farthest = 0f;
                for (int k = 0; k < dry.Length; k++)
                    if (distance[k] != float.MaxValue && distance[k] > farthest && Inside(Point(k), 925f))
                    { far = k; farthest = distance[k]; }
                if (far < 0) return;
                var approach = Join(Point(far), Flat(plan.enemyHqPosition));
                if (approach != null) plan.roads.Add(approach);
            }
        }
    }
}

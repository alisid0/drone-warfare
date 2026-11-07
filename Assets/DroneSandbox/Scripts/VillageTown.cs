using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // A town grows as a small central-European town does: an old core of attached houses on irregular streets round a
    // market square and church on the main road; a ring of 19th- and 20th-century streets of semi-detached and detached
    // houses with gardens; one or two estates of apartment blocks in green space; a working edge of warehouses, a depot
    // and a filling station along the main road; and the allotments, playing field and cemetery round it (TownEdge).
    // Streets branch from the through-roads and then from each other, 70-170 m apart, and end at the town's outline,
    // water, woods or another street, so the plan is a road-based neighbourhood graph of irregular blocks, not a grid.
    public sealed partial class VillagePlan
    {
        const int TownRoute = 20000; // town streets are routes 20000 + owner * 1000 + street
        const float TownStep = 12f, TownSnap = 15f, TownParallel = 34f;

        // The settlement whose town plan laid a street route, or -1 for any other route.
        public static int TownOwner(int route) => route >= TownRoute ? (route - TownRoute) / 1000 : -1;

        // One town's growth: its centre and reach, every street segment near it (through-roads first) in 40 m bins,
        // the grown streets as polylines, and the railway segments it keeps clear of.
        sealed class TownGrowth
        {
            public int owner, streets;
            public Vector2 anchor, center;
            public float reach, core;
            public readonly List<FarDetailData.Segment> segments = new List<FarDetailData.Segment>();
            public readonly Dictionary<long, List<int>> bins = new Dictionary<long, List<int>>();
            public readonly List<List<Vector2>> lines = new List<List<Vector2>>();
            public readonly List<int> generation = new List<int>(), ids = new List<int>();
            public readonly List<FarDetailData.Segment> rails = new List<FarDetailData.Segment>();
            public readonly HashSet<int> seen = new HashSet<int>();
            public VillagePlot square;
            public bool market;
            public Random rng;
        }

        // Plan one town in its fixed order: the square and church, the working edge, the streets in five generations with
        // the estates after the first, the edge uses, then the frontage with its backland, the property uses and avenues.
        void Town(int owner, WorldSettlement s, FarDetailData.Segment[] roads, Vector2 anchor)
        {
            var g = new TownGrowth { owner = owner, anchor = anchor, center = s.center, reach = s.radius * .85f };
            g.core = Math.Max(150f, Math.Min(240f, g.reach * .24f));
            g.rng = new Random(unchecked(seed * 4219 + owner * 69257));
            float bound = g.reach * 1.5f + 200f;
            foreach (var road in roads)
            {
                Vector2 a = new Vector2(road.ax, road.az), b = new Vector2(road.bx, road.bz);
                if (Math.Min((a - s.center).magnitude, (b - s.center).magnitude) > bound + (b - a).magnitude) continue;
                AddTownSegment(g, road);
            }
            foreach (var rail in WorldPlan.Railways)
                for (int k = 1; k < rail.points.Count; k++)
                    if ((rail.points[k] - s.center).magnitude < bound)
                        g.rails.Add(new FarDetailData.Segment { ax = rail.points[k - 1].x, az = rail.points[k - 1].y, bx = rail.points[k].x, bz = rail.points[k].y, widthA = 5.5f, widthB = 5.5f });
            var through = Through(g);
            Market(g, through);
            WorkingEdge(g, through);
            // The first generation branches off the through-roads; estates take whole blocks behind them before the
            // cross streets fill in.
            foreach (var line in through.Values) Branch(g, line, 1);
            Estates(g, s);
            for (int generation = 1; generation < 5; generation++)
            {
                int count = g.lines.Count;
                for (int i = 0; i < count; i++)
                    if (g.generation[i] == generation) Branch(g, Route(g, i), generation + 1);
            }
            int firstLane = lanes.Count;
            for (int i = 0; i < g.lines.Count; i++) AddStreet(g.lines[i], TownRoute + owner * 1000 + g.ids[i], 2f);
            var local = lanes.GetRange(firstLane, lanes.Count - firstLane).ToArray();
            TownEdge(owner, s, Around(roads, anchor, g.reach + 1100f), local, anchor, g.reach, .9f);
            // Frontage: the square's sides and the through-roads' main streets first, then the streets in the order they grew.
            SquareSides(g);
            foreach (var line in through.Values) Frontage(g, line, 0f, float.MaxValue);
            for (int i = 0; i < g.lines.Count; i++) Frontage(g, Route(g, i), 0f, float.MaxValue);
            VillageUse.Assign(plots, owner, seed, anchor);
            Finish(g);
            Avenues(g, through);
        }

        // What a town's special properties keep whatever the stratified uses chose: terraced houses stand at full size
        // with their side walls shared and their fronts on the pavement; estates and yards are open, kept and unfenced
        // (a yard behind a fence of its own).
        void Finish(TownGrowth g)
        {
            for (int i = 0; i < plots.Count; i++)
            {
                var p = plots[i];
                if (p.owner != g.owner) continue;
                if (p.attached) { p.scale = 1f; p.open |= 1 | 2 | 8; }
                if (p.estate || p.yard)
                {
                    p.scale = 1f; p.shape = 0; p.cultivated = p.shed = p.logs = p.untended = false;
                    p.boundary = p.yard && !p.square ? 3 : 0; p.open = 0;
                }
                plots[i] = p;
            }
        }

        // File one street segment in the 40 m bins it crosses.
        static void AddTownSegment(TownGrowth g, FarDetailData.Segment s)
        {
            int index = g.segments.Count;
            g.segments.Add(s);
            float pad = Math.Max(s.widthA, s.widthB) + 2f;
            int x0 = (int)Math.Floor((Math.Min(s.ax, s.bx) - pad) / 40f), x1 = (int)Math.Floor((Math.Max(s.ax, s.bx) + pad) / 40f);
            int z0 = (int)Math.Floor((Math.Min(s.az, s.bz) - pad) / 40f), z1 = (int)Math.Floor((Math.Max(s.az, s.bz) + pad) / 40f);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    long key = ((long)x << 32) ^ (uint)z;
                    if (!g.bins.TryGetValue(key, out var list)) g.bins.Add(key, list = new List<int>());
                    list.Add(index);
                }
        }

        // The nearest street segment to a point within a radius, skipping two routes (the street being grown and its
        // parent) and, given a heading, any whose nearest point lies behind: its distance, nearest point and direction.
        static bool NearestStreet(TownGrowth g, Vector2 p, float radius, int skipA, int skipB, out float distance, out Vector2 hit, out Vector2 direction,
            Vector2 ahead = default)
        {
            distance = float.MaxValue; hit = direction = default;
            int x0 = (int)Math.Floor((p.x - radius) / 40f), x1 = (int)Math.Floor((p.x + radius) / 40f);
            int z0 = (int)Math.Floor((p.y - radius) / 40f), z1 = (int)Math.Floor((p.y + radius) / 40f);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!g.bins.TryGetValue(((long)x << 32) ^ (uint)z, out var list)) continue;
                    foreach (int i in list)
                    {
                        var s = g.segments[i];
                        if (s.route == skipA || s.route == skipB) continue;
                        Vector2 a = new Vector2(s.ax, s.az), d = new Vector2(s.bx - s.ax, s.bz - s.az);
                        float t = d.sqrMagnitude > .01f ? Math.Max(0f, Math.Min(1f, Vector2.Dot(p - a, d) / d.sqrMagnitude)) : 0f;
                        Vector2 q = a + d * t;
                        if (ahead.sqrMagnitude > 0f && Vector2.Dot(q - p, ahead) < -2f) continue;
                        float e = (q - p).magnitude - Math.Max(s.widthA, s.widthB);
                        if (e >= distance || e > radius) continue;
                        distance = e; hit = q; direction = d.sqrMagnitude > .01f ? d.normalized : Vector2.right;
                    }
                }
            return distance <= radius;
        }

        // The town's irregular outline: its own cover, or anywhere within half its reach of the centre, never beyond 1.5.
        static bool InTown(TownGrowth g, Vector2 p)
        {
            float d = (p - g.center).magnitude;
            return d < g.reach * 1.5f && (d < g.reach * .5f || WorldPlan.CoverAt(p.x, p.y) == Cover.Town);
        }

        // Dry, open, unreserved ground clear of rivers, where a street may run: across a railway at a level crossing, but
        // never along it within 14 m.
        static bool StreetGround(TownGrowth g, Vector2 p, Vector2 heading)
        {
            var c = WorldPlan.CoverAt(p.x, p.y);
            if (c == Cover.Water || c == Cover.Wetland || c == Cover.Forest || c == Cover.Rock) return false;
            if (WorldPlan.FeatureReserved(p, 6f) || WorldPlan.RiverDistance(p.x, p.y) < 30f) return false;
            foreach (var r in g.rails)
            {
                Vector2 a = new Vector2(r.ax, r.az), d = new Vector2(r.bx - r.ax, r.bz - r.az);
                float t = d.sqrMagnitude > .01f ? Math.Max(0f, Math.Min(1f, Vector2.Dot(p - a, d) / d.sqrMagnitude)) : 0f;
                if ((a + d * t - p).sqrMagnitude < 14f * 14f && d.sqrMagnitude > .01f && Math.Abs(Vector2.Dot(d.normalized, heading)) > .6f) return false;
            }
            return true;
        }

        // The through-roads crossing the town, each as its route's segments in walking order.
        static SortedDictionary<int, List<FarDetailData.Segment>> Through(TownGrowth g)
        {
            var routes = new SortedDictionary<int, List<FarDetailData.Segment>>();
            foreach (var road in g.segments)
            {
                if (road.kind == (int)RoadClass.Track) continue;
                Vector2 mid = new Vector2((road.ax + road.bx) * .5f, (road.az + road.bz) * .5f);
                if ((mid - g.center).magnitude > g.reach * 1.5f + 60f) continue;
                if (!routes.TryGetValue(road.route, out var line)) routes.Add(road.route, line = new List<FarDetailData.Segment>());
                line.Add(road);
            }
            foreach (var line in routes.Values) line.Sort((a, b) => a.walked.CompareTo(b.walked));
            return routes;
        }

        // A grown street's segments in walking order, as Branch and Frontage read routes.
        List<FarDetailData.Segment> Route(TownGrowth g, int index)
        {
            var line = g.lines[index];
            var route = new List<FarDetailData.Segment>();
            float walked = 0f;
            for (int k = 1; k < line.Count; k++)
            {
                route.Add(new FarDetailData.Segment { ax = line[k - 1].x, az = line[k - 1].y, bx = line[k].x, bz = line[k].y, widthA = 2f, widthB = 2f,
                    walked = walked, route = TownRoute + g.owner * 1000 + g.ids[index], kind = (int)RoadClass.Road });
                walked += (line[k] - line[k - 1]).magnitude;
            }
            return route;
        }

        // Lay a street as lanes, keeping a point only where it turns, with walked stations for frontage.
        void AddStreet(List<Vector2> line, int id, float width)
        {
            var kept = new List<Vector2> { line[0] };
            for (int k = 1; k < line.Count - 1; k++)
            {
                Vector2 a = line[k] - kept[kept.Count - 1], b = line[k + 1] - line[k];
                if (a.sqrMagnitude > 1f && b.sqrMagnitude > 1f && Vector2.Dot(a.normalized, b.normalized) > .9995f && a.magnitude < 60f) continue;
                kept.Add(line[k]);
            }
            kept.Add(line[line.Count - 1]);
            float walked = 0f;
            for (int k = 1; k < kept.Count; k++)
            {
                AddLane(null, kept[k - 1], kept[k], 0, width);
                var lane = lanes[lanes.Count - 1]; lane.route = id; lane.walked = walked; lanes[lanes.Count - 1] = lane;
                walked += (kept[k] - kept[k - 1]).magnitude;
            }
        }

        // Block lengths by zone: the old core's short irregular blocks, the ring's 85-150 m and the outskirts' longer ones.
        static float Spacing(TownGrowth g, Vector2 p, int generation)
        {
            float d = (p - g.anchor).magnitude;
            double r = g.rng.NextDouble();
            if (d < g.core) return 85f + (float)r * 45f;
            if (generation % 2 == 0) return 95f + (float)r * 45f; // the cross streets: two gardens deep
            return d < g.reach * .62f ? 110f + (float)r * 60f : 120f + (float)r * 60f;
        }

        // Branch streets off one street on each side, staggered so most junctions are Ts, then grow each until it meets
        // another street (joining it), the outline, water or woods, or runs parallel and close to another street.
        void Branch(TownGrowth g, List<FarDetailData.Segment> parent, int generation)
        {
            if (parent.Count == 0) return;
            var end = parent[parent.Count - 1];
            float high = end.walked + new Vector2(end.bx - end.ax, end.bz - end.az).magnitude;
            foreach (int side in new[] { -1, 1 })
            {
                float next = parent[0].walked + 25f + (float)g.rng.NextDouble() * 50f;
                while (next < high - 25f)
                {
                    if (!OnRoute(parent, next, out Vector2 at, out Vector2 dir, out float width)) { next += 20f; continue; }
                    float spacing = Spacing(g, at, generation);
                    if (InTown(g, at))
                    {
                        float d = (at - g.anchor).magnitude;
                        float jitter = (float)(g.rng.NextDouble() - .5) * (d < g.core ? .5f : .25f);
                        Vector2 normal = new Vector2(-dir.y, dir.x) * side;
                        Vector2 heading = Rotate(normal, jitter);
                        Vector2 start = at + normal * width;
                        float bend = d < g.core ? .05f : .025f;
                        // A side already joined by a street from the next block needs no second one beside it.
                        bool joined = NearestStreet(g, at + normal * (width + 9f), spacing * .55f, parent[0].route, int.MinValue, out _, out _, out _, normal);
                        var line = joined ? null : Grow(g, start, heading, bend, parent[0].route, generation);
                        if (line != null)
                        {
                            line[0] = at;
                            int id = g.streets++;
                            g.lines.Add(line); g.generation.Add(generation); g.ids.Add(id);
                            int route = TownRoute + g.owner * 1000 + id;
                            for (int k = 1; k < line.Count; k++)
                                AddTownSegment(g, new FarDetailData.Segment { ax = line[k - 1].x, az = line[k - 1].y, bx = line[k].x, bz = line[k].y, widthA = 2f, widthB = 2f, route = route, kind = (int)RoadClass.Road });
                        }
                    }
                    next += spacing;
                }
            }
        }

        // Rotate a direction by a small angle in radians.
        static Vector2 Rotate(Vector2 v, float angle)
        {
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // One street grown in 12 m steps with a gentle wander; null if it ends under 36 m long.
        List<Vector2> Grow(TownGrowth g, Vector2 start, Vector2 heading, float bend, int parent, int generation)
        {
            int self = TownRoute + g.owner * 1000 + g.streets;
            // A street starting beside another junction, or into a reserved plot, is not begun.
            if (NearestStreet(g, start + heading * 6f, 18f, parent, self, out _, out _, out _, heading) || Near(start + heading * 8f, 3f)) return null;
            var line = new List<Vector2> { start };
            Vector2 at = start, dir = heading;
            float walked = 0f, limit = (start - g.anchor).magnitude < g.core ? 220f : 520f;
            int phase = g.rng.Next(1000);
            while (walked < limit)
            {
                dir = Rotate(dir, (Wave(walked / 60f + phase) - .5f) * bend * 2f).normalized;
                Vector2 next = at + dir * TownStep;
                if (!InTown(g, next) || !StreetGround(g, next, dir) || Near(next, 4f)) break;
                if (NearestStreet(g, next, TownParallel, walked < 30f ? parent : self, self, out float distance, out Vector2 hit, out Vector2 other, dir))
                {
                    bool parallel = Math.Abs(Vector2.Dot(other, dir)) > .82f;
                    bool ahead = Vector2.Dot(hit - next, dir) > .7f * (hit - next).magnitude;
                    // Beside a parallel street the block would be a thin sliver: the street ends there instead. Meeting a
                    // street, or the end of one running on, it joins it.
                    if (parallel && !ahead && distance < TownParallel) break;
                    if (distance < TownSnap && (!parallel || ahead))
                    {
                        line.Add(hit); walked += (hit - at).magnitude;
                        return walked >= 36f ? line : null;
                    }
                }
                line.Add(next); at = next; walked += TownStep;
            }
            return walked >= 36f ? line : null;
        }

        // A smooth periodic wander in [0, 1], one value per street phase.
        static float Wave(float t) => .5f + .35f * (float)Math.Sin(t * 1.7f) + .15f * (float)Math.Sin(t * 4.3f + 1.1f);

        // The market square opens off the main road at the town's middle, on whichever side is dry and clear, with the
        // church set back across it facing the road.
        void Market(TownGrowth g, SortedDictionary<int, List<FarDetailData.Segment>> through)
        {
            // The road the town's middle lies on, nearest first.
            var lines = new List<List<FarDetailData.Segment>>(through.Values);
            var gaps = new List<float>();
            foreach (var line in lines) { OnRoute(line, Station(line, g.anchor), out Vector2 q, out _, out _); gaps.Add((q - g.anchor).magnitude); }
            for (int i = 1; i < lines.Count; i++)
                for (int j = i; j > 0 && gaps[j] < gaps[j - 1]; j--)
                {
                    var l = lines[j]; lines[j] = lines[j - 1]; lines[j - 1] = l;
                    float x = gaps[j]; gaps[j] = gaps[j - 1]; gaps[j - 1] = x;
                }
            foreach (var line in lines)
                foreach (float offset in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f })
                    foreach (int side in new[] { (g.owner & 1) == 0 ? 1 : -1, (g.owner & 1) == 0 ? -1 : 1 })
                    {
                        float station = Station(line, g.anchor);
                        if (!OnRoute(line, station + offset, out Vector2 at, out Vector2 dir, out float width)) continue;
                        Vector2 outward = new Vector2(-dir.y, dir.x) * side;
                        var square = new VillagePlot { front = at + outward * (width + 1.4f), along = dir, outward = outward, frontage = 62f + (float)g.rng.NextDouble() * 18f,
                            depth = 42f + (float)g.rng.NextDouble() * 12f, roadWidth = width, owner = g.owner, town = true, square = true, old = true, core = true };
                        if (Overlap(square) || CrossesTown(g, square) || !Dry(square)) continue;
                        var church = new VillagePlot { front = square.front + outward * square.depth, along = dir, outward = outward, frontage = 30f, depth = 46f,
                            setback = 16f, roadWidth = 0f, owner = g.owner, town = true, church = true, old = true, core = true, model = "Scenery_Church" };
                        Add(square);
                        g.square = square; g.market = true;
                        if (!Overlap(church) && !CrossesTown(g, church) && Dry(church)) Add(church);
                        return;
                    }
        }

        // The station on a route nearest a point.
        static float Station(List<FarDetailData.Segment> line, Vector2 p)
        {
            float best = float.MaxValue, station = 0f;
            foreach (var r in line)
            {
                Vector2 a = new Vector2(r.ax, r.az), d = new Vector2(r.bx - r.ax, r.bz - r.az);
                float t = d.sqrMagnitude > .01f ? Math.Max(0f, Math.Min(1f, Vector2.Dot(p - a, d) / d.sqrMagnitude)) : 0f;
                float e = (a + d * t - p).sqrMagnitude;
                if (e < best) { best = e; station = r.walked + d.magnitude * t; }
            }
            return station;
        }

        // Whether a planned plot crosses a street or through-road near the town, or a railway.
        static bool CrossesTown(TownGrowth g, VillagePlot p)
        {
            Vector2 c = p.front + p.outward * (p.depth * .5f);
            float r = (p.depth + p.frontage) * .5f + 6f;
            int x0 = (int)Math.Floor((c.x - r) / 40f), x1 = (int)Math.Floor((c.x + r) / 40f);
            int z0 = (int)Math.Floor((c.y - r) / 40f), z1 = (int)Math.Floor((c.y + r) / 40f);
            var seen = g.seen; seen.Clear();
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!g.bins.TryGetValue(((long)x << 32) ^ (uint)z, out var list)) continue;
                    foreach (int i in list)
                    {
                        if (!seen.Add(i)) continue;
                        if (Crosses(p, g.segments[i])) return true;
                    }
                }
            foreach (var rail in g.rails) if (Crosses(p, rail)) return true;
            return false;
        }

        // One segment against a plot, with its half-width and a little verge.
        static bool Crosses(VillagePlot p, FarDetailData.Segment r)
        {
            Vector2 a = new Vector2(r.ax, r.az), b = new Vector2(r.bx, r.bz);
            p.Coordinates(a, out float au, out float av);
            p.Coordinates(b, out float bu, out float bv);
            float pad = Math.Max(r.widthA, r.widthB) + .4f;
            if (Math.Max(av, bv) < -pad || Math.Min(av, bv) > p.depth + pad || Math.Max(au, bu) < -p.frontage * .5f - pad || Math.Min(au, bu) > p.frontage * .5f + pad)
                return false;
            int n = Math.Max(1, (int)Math.Ceiling((b - a).magnitude / 4f));
            for (int k = 0; k <= n; k++)
                if (p.Contains(a + (b - a) * (k / (float)n), pad)) return true;
            return false;
        }

        // The working edge: along the main road where it leaves the town on one side, a filling station first, then
        // warehouses, a depot and an office block with their yards and parking.
        void WorkingEdge(TownGrowth g, SortedDictionary<int, List<FarDetailData.Segment>> through)
        {
            List<FarDetailData.Segment> main = null; int best = int.MaxValue; float longest = 0f;
            foreach (var line in through.Values)
            {
                float length = 0f; foreach (var r in line) length += new Vector2(r.bx - r.ax, r.bz - r.az).magnitude;
                if (line[0].kind < best || line[0].kind == best && length > longest) { best = line[0].kind; longest = length; main = line; }
            }
            if (main == null) return;
            float centre = Station(main, g.anchor);
            float direction = (g.owner & 2) == 0 ? 1f : -1f;
            int side = (g.owner & 4) == 0 ? 1 : -1, placed = 0;
            for (int attempt = 0; attempt < 2 && placed == 0; attempt++, direction = -direction)
                for (float walk = g.reach * .62f; walk < g.reach * 1.15f && placed < 6; walk += 8f)
                {
                    if (!OnRoute(main, centre + direction * walk, out Vector2 at, out Vector2 dir, out float width)) break;
                    Vector2 outward = new Vector2(-dir.y, dir.x) * side;
                    int kind = placed == 0 ? 0 : placed == 3 ? 2 : placed == 5 ? 3 : 1; // station, warehouses, office, depot
                    var p = new VillagePlot { front = at + outward * (width + 1.4f), along = dir, outward = outward, roadWidth = width, owner = g.owner, town = true,
                        style = g.rng.Next(4) };
                    if (kind == 0) { p.frontage = 38f; p.depth = 30f; p.setback = 13f; p.model = "Scenery_FillingStation"; p.yard = true; }
                    else if (kind == 1) { p.frontage = 46f + (float)g.rng.NextDouble() * 14f; p.depth = 62f + (float)g.rng.NextDouble() * 14f; p.setback = 28f; p.model = "Scenery_Warehouse"; p.yard = true; }
                    else if (kind == 2) { p.frontage = 40f; p.depth = 40f; p.setback = 19f; p.model = "Scenery_OfficeBlock"; p.yard = true; }
                    else { p.frontage = 64f; p.depth = 78f; p.setback = 40f; p.model = "Scenery_Warehouse"; p.landUse = 4; }
                    if (Overlap(p) || CrossesTown(g, p) || !Dry(p)) continue;
                    Add(p); placed++;
                    walk += p.frontage;
                }
        }

        // One or two estates of four-storey apartment blocks in rows behind a first-generation street, 0.4-0.8 of the way
        // out, each a whole block of green space with parking along the street, paths to the stair doors and a play area.
        void Estates(TownGrowth g, WorldSettlement s)
        {
            int wanted = s.radius > 800f ? 2 : 1, made = 0;
            var used = new List<Vector2>();
            for (int i = 0; i < g.lines.Count && made < wanted; i++)
            {
                var line = Route(g, i);
                var end = line[line.Count - 1];
                float high = end.walked + new Vector2(end.bx - end.ax, end.bz - end.az).magnitude;
                for (float station = 40f; station < high - 60f && made < wanted; station += 30f)
                {
                    if (!OnRoute(line, station, out Vector2 at, out Vector2 dir, out float width)) continue;
                    float d = (at - g.anchor).magnitude;
                    if (d < g.reach * .4f || d > g.reach * .8f) continue;
                    bool far = true;
                    foreach (var u in used) far &= (u - at).magnitude > g.reach * .7f;
                    if (!far) continue;
                    int side = ((g.owner + i) & 1) == 0 ? 1 : -1;
                    if (Estate(g, at, dir, side, width)) { used.Add(at); made++; }
                }
            }
        }

        // Lay one estate: three columns of blocks 40 m apart and three or four rows 36 m deep, the first row with parking
        // in front; one cell is the estate's car park and one its green with the play area. It is kept if most cells fit.
        bool Estate(TownGrowth g, Vector2 at, Vector2 dir, int side, float width)
        {
            Vector2 outward = new Vector2(-dir.y, dir.x) * side;
            Vector2 origin = at + outward * (width + 1.4f);
            int rows = 3 + g.rng.Next(2), columns = 3 + g.rng.Next(2);
            var cells = new List<VillagePlot>();
            int park = g.rng.Next(columns), green = (park + 1 + g.rng.Next(columns - 1)) % columns;
            for (int row = 0; row < rows; row++)
            {
                float v = row == 0 ? 0f : 44f + (row - 1) * 36f;
                for (int column = 0; column < columns; column++)
                {
                    float u = (column - (columns - 1) * .5f) * 40f;
                    var p = new VillagePlot { front = origin + dir * u + outward * v, along = dir, outward = outward, frontage = 40f, depth = row == 0 ? 44f : 36f,
                        setback = row == 0 ? 20f : 13f, roadWidth = width, owner = g.owner, town = true, estate = true, style = row == 0 ? 1 : 0,
                        model = "Scenery_Apartment" };
                    if (row == rows - 1 && column == park) { p.model = null; p.square = true; p.style = 2; }
                    else if (row == 1 && column == green) { p.model = null; p.square = true; p.style = 3; }
                    cells.Add(p);
                }
            }
            int fit = 0;
            foreach (var p in cells) if (!Overlap(p) && !CrossesTown(g, p) && Dry(p) && InTown(g, p.Point(0f, p.depth * .5f))) fit++;
            if (fit < cells.Count * .75f) return false;
            foreach (var p in cells) if (!Overlap(p) && !CrossesTown(g, p) && Dry(p) && InTown(g, p.Point(0f, p.depth * .5f))) Add(p);
            return true;
        }

        // Properties down both sides of one street. The old core is a near-continuous run of attached houses, the ring
        // semi-detached and detached houses with gardens, the outskirts looser detached houses with a few unbuilt lots.
        // Gardens run back to meet those of the next street, half way across the block; where the block is deep, a
        // workshop, small house or garage court stands behind instead (backland), as town blocks fill in from behind.
        void Frontage(TownGrowth g, List<FarDetailData.Segment> street, float low, float high)
        {
            if (street.Count == 0) return;
            var end = street[street.Count - 1];
            low = Math.Max(low, street[0].walked);
            high = Math.Min(high, end.walked + new Vector2(end.bx - end.ax, end.bz - end.az).magnitude);
            foreach (int side in new[] { -1, 1 })
            {
                float next = low + 6f + (float)g.rng.NextDouble() * 6f;
                while (next < high - 6f)
                {
                    if (!OnRoute(street, next, out Vector2 at, out Vector2 dir, out float roadWidth)) { next += 6f; continue; }
                    float d = (at - g.anchor).magnitude;
                    int zone = d < g.core ? 0 : d < g.reach * .62f ? 1 : 2;
                    var p = TownHouse(g, zone);
                    float width = p.frontage;
                    if (!OnRoute(street, next + width * .5f, out at, out dir, out roadWidth)) break;
                    Vector2 outward = new Vector2(-dir.y, dir.x) * side;
                    p.front = at + outward * (roadWidth + 1.4f); p.along = dir; p.outward = outward; p.roadWidth = roadWidth;
                    if (InTown(g, p.front))
                    {
                        bool clear = Fits(g, p);
                        // In the old core a narrower house fills a gap a terrace cannot.
                        if (!clear && zone == 0 && p.frontage > 14.5f && OnRoute(street, next + 7f, out at, out dir, out roadWidth))
                        {
                            outward = new Vector2(-dir.y, dir.x) * side;
                            p.model = "Scenery_Shop"; p.frontage = width = 14f; p.setback = 6.6f;
                            p.front = at + outward * (roadWidth + 1.4f); p.along = dir; p.outward = outward; p.roadWidth = roadWidth;
                            clear = Fits(g, p);
                        }
                        // At corners a shallower garden keeps the street frontage.
                        while (!clear && p.depth > 22f)
                        {
                            p.depth = Math.Max(22f, p.depth - 5f);
                            clear = Fits(g, p);
                        }
                        if (clear) Deepen(g, ref p, zone);
                    }
                    next += width + (zone == 0 ? .2f : zone == 1 ? .5f + (float)g.rng.NextDouble() * 2.5f : 1f + (float)g.rng.NextDouble() * 6f);
                }
            }
        }

        // Half the way across the block behind a property, measured from its street along its outward line, or 0 where no
        // street lies within 130 m (the town's edge, where gardens back onto the fields).
        static float HalfBlock(TownGrowth g, VillagePlot p)
        {
            for (float v = 14f; v <= 130f; v += 5f)
            {
                Vector2 q = p.Point(0f, v);
                if (!InTown(g, q)) return 0f;
                if (NearestStreet(g, q, 1.5f, int.MinValue, int.MinValue, out _, out _, out _)) return (v - 1.4f) * .5f;
            }
            return 0f;
        }

        // Whether a town property fits: clear of streets, railways, water and reserved land, and of the properties already
        // laid, with 1.2 m of give at each side and a metre at the back, so a run of houses can follow a curving street.
        bool Fits(TownGrowth g, VillagePlot p)
        {
            var inset = p; inset.frontage -= 2.4f; inset.depth -= 1f;
            return !Overlap(inset) && !CrossesTown(g, p) && Dry(p);
        }

        // Place a cleared property, then fill its block: a building behind it where there is room and the zone builds
        // backland, otherwise its garden run back as far as half the block (58 m at most, 40 m in the old core).
        void Deepen(TownGrowth g, ref VillagePlot p, int zone)
        {
            float half = HalfBlock(g, p), limit = zone == 0 ? 40f : 58f;
            double chance = p.square ? 0 : zone == 0 ? .55 : zone == 1 ? .4 : .3;
            // At the town's edge, with no street behind, a building may stand at the back of the garden in the fields.
            bool edge = half <= 0f && zone > 0;
            if ((half >= 38f || edge) && g.rng.NextDouble() < (edge ? chance * .7 : chance))
            {
                if (!edge) p.depth = Math.Max(20f, Math.Min(p.depth, half - 20f));
                var back = new VillagePlot { owner = g.owner, town = true, backland = true, along = p.along, outward = p.outward,
                    front = p.front + p.outward * (p.depth + .5f), roadWidth = 0f, style = g.rng.Next(4) };
                double roll = g.rng.NextDouble();
                back.model = zone == 0 ? (roll < .6 ? "Scenery_House_D" : "Scenery_House_A") : roll < .5 ? "Scenery_House_A" : roll < .8 ? "Scenery_House_D" : "Scenery_House_B";
                back.frontage = Math.Min(p.frontage, 16f + (float)g.rng.NextDouble() * 6f);
                back.depth = edge ? 22f + (float)g.rng.NextDouble() * 8f : Math.Min(half - p.depth - .5f, 32f);
                back.setback = 7f;
                back.attached = zone == 0;
                Add(p);
                if (Fits(g, back) && InTown(g, back.Point(0f, back.depth * .5f))) Add(back);
                return;
            }
            float target = half > 0f ? Math.Min(half, limit) : p.depth + (float)g.rng.NextDouble() * 10f;
            float depth = p.depth;
            // The whole way, else half of the extra: two trials keep a town's plan within the world build's time.
            foreach (float trial in new[] { target, (target + depth) * .5f })
            {
                if (trial <= depth + 2f) break;
                p.depth = trial;
                if (Fits(g, p)) { Add(p); return; }
            }
            p.depth = depth;
            Add(p);
        }

        // One property for a zone, before it is placed: the house kind, its frontage, setback and garden depth.
        VillagePlot TownHouse(TownGrowth g, int zone)
        {
            var rng = g.rng;
            var p = new VillagePlot { owner = g.owner, town = true, style = rng.Next(4) };
            double roll = rng.NextDouble();
            if (zone == 0)
            {
                // Terraced burgher houses, three-storey rows and corner shops, built to the pavement.
                p.attached = true; p.old = p.core = true;
                p.model = roll < .48 ? "Scenery_Townhouse_Old" : roll < .8 ? "Scenery_Townhouse_Row" : "Scenery_Shop";
                p.frontage = p.model == "Scenery_Townhouse_Old" ? 18.6f : p.model == "Scenery_Townhouse_Row" ? 24.9f : 14f;
                p.setback = p.model == "Scenery_Shop" ? 6.6f : 6f;
                p.depth = 22f + (float)rng.NextDouble() * 14f;
                if (rng.NextDouble() < .05) { p.model = null; p.square = true; p.yard = true; p.attached = false; p.frontage = 12f; }
            }
            else if (zone == 1)
            {
                // The 19th- and 20th-century ring: semi-detached pairs and villas, with front gardens.
                p.model = roll < .45 ? "Scenery_House_Semi" : roll < .8 ? "Scenery_House_B" : roll < .9 ? "Scenery_House_C" : "Scenery_House_A";
                p.frontage = p.model == "Scenery_House_C" ? 23f : 17f + (float)rng.NextDouble() * 7f;
                p.setback = p.model == "Scenery_House_C" ? 11f : 6f + (float)rng.NextDouble() * 3f;
                p.depth = 32f + (float)rng.NextDouble() * 12f;
                if (rng.NextDouble() < .05) { p.model = null; p.square = true; p.landUse = 8; }
            }
            else
            {
                // The outskirts: detached houses on larger plots, and the odd unbuilt lot.
                p.model = roll < .38 ? "Scenery_House_A" : roll < .72 ? "Scenery_House_B" : roll < .84 ? "Scenery_House_D" : "Scenery_House_Semi";
                p.frontage = 20f + (float)rng.NextDouble() * 11f;
                p.setback = 8f + (float)rng.NextDouble() * 4f;
                p.depth = 34f + (float)rng.NextDouble() * 14f;
                if (rng.NextDouble() < .07) { p.model = null; p.square = true; p.landUse = rng.Next(2) == 0 ? 6 : 8; }
            }
            if (p.square && p.landUse >= 6) p.identity = rng.Next();
            return p;
        }

        // Attached houses line the square's two sides, facing it, so the market place is enclosed as an old core's is.
        void SquareSides(TownGrowth g)
        {
            if (!g.market) return;
            var q = g.square;
            foreach (int side in new[] { -1, 1 })
            {
                float v = 3f;
                while (v < q.depth - 6f)
                {
                    var p = TownHouse(g, 0);
                    if (p.square) { v += p.frontage; continue; }
                    if (v + p.frontage > q.depth + 4f) break;
                    p.along = q.outward; p.outward = q.along * side; p.roadWidth = 0f;
                    p.front = q.Point(side * q.frontage * .5f, v + p.frontage * .5f) + p.outward * .5f;
                    if (!Overlap(p) && !CrossesTown(g, p) && Dry(p)) Add(p);
                    v += p.frontage + .2f;
                }
            }
        }

        // Tree-lined main streets: avenues down one or both sides of the through-roads between the old core and the
        // outskirts, and of one first-generation street in four, on the verge in front of the gardens; and a row round
        // the market square. The scatter, the HLOD and the sector all plant them (SettlementPattern, World).
        void Avenues(TownGrowth g, SortedDictionary<int, List<FarDetailData.Segment>> through)
        {
            var streets = new List<List<FarDetailData.Segment>>(through.Values);
            for (int i = 0; i < g.lines.Count; i++) if (g.generation[i] == 1 && g.ids[i] % 4 == 1) streets.Add(Route(g, i));
            foreach (var line in streets)
            {
                if (line.Count == 0) continue;
                int route = line[0].route;
                var end = line[line.Count - 1];
                float high = end.walked + new Vector2(end.bx - end.ax, end.bz - end.az).magnitude;
                foreach (int side in new[] { -1, 1 })
                {
                    uint h = unchecked((uint)(route * 73856093 ^ side * 19349663 ^ seed * 83492791));
                    h = unchecked((h ^ (h >> 13)) * 1274126177u);
                    if ((h >> 8) % 10 >= (route >= TownRoute ? 6 : 8)) continue;
                    for (float station = line[0].walked + 6f; station < high - 6f; station += 11f + (h >> 16 & 3))
                    {
                        if (!OnRoute(line, station, out Vector2 at, out Vector2 dir, out float width)) continue;
                        float d = (at - g.anchor).magnitude;
                        if (d < g.core * .8f || d > g.reach * .85f || !InTown(g, at)) continue;
                        Vector2 p = at + new Vector2(-dir.y, dir.x) * side * (width + .7f);
                        if (NearestStreet(g, p, 9f, route, route, out _, out _, out _) || TreeBlocked(p)) continue;
                        trees.Add(new StreetTree { at = p, owner = g.owner });
                    }
                }
            }
            if (!g.market) return;
            var q = g.square;
            for (float u = -q.frontage * .5f + 4f; u <= q.frontage * .5f - 4f; u += 9f)
                foreach (float v in new[] { 4f, q.depth - 4f })
                    trees.Add(new StreetTree { at = q.Point(u, v), owner = g.owner });
        }

        // A street tree keeps out of every property except open fringe land and the square it shades.
        bool TreeBlocked(Vector2 p)
        {
            long key = ((long)(int)Math.Floor(p.x / 128f) << 32) ^ (uint)(int)Math.Floor(p.y / 128f);
            if (cells.TryGetValue(key, out var list))
                foreach (int i in list)
                    if (plots[i].landUse < 6 && !plots[i].square && plots[i].Contains(p, .6f)) return true;
            return false;
        }
    }

    // The ground of a town's own properties, shared by the painters and the sector like every plot use.
    public partial struct VillagePlot
    {
        // The roof each house wears (0 tile, 1 dark tile, 2 warm tile, 3 slate, as World.Roofs): a village leans to its
        // own roof with a few re-roofed houses; a town's roofs are mixed, about half plain tile.
        public int RoofTone
        {
            get
            {
                if (!town) return (settlement + (roofChanged ? 1 + palette % 3 : 0)) % 4;
                int r = (identity >> 21) % 20;
                return r < 9 ? 0 : r < 13 ? 1 : r < 17 ? 2 : 3;
            }
        }

        // An estate block's green: parking along the street in the first row (a whole car park in one cell), paths to
        // the two stair doors and along the front, lawn round it and a play area in the estate's green; a yard is a
        // paved forecourt or depot with a lawn strip down its sides.
        int TownGround(float u, float v)
        {
            float h = frontage * .5f;
            if (yard) return h - Math.Abs(u) < 2f && v > 2f ? Lawn : Gravel;
            if (style == 2) return h - Math.Abs(u) < 2.5f || v < 2f || depth - v < 2.5f ? Lawn : Gravel;
            if (style == 3)
            {
                float a = u / 7f, b = (v - depth * .5f) / 6f;
                if (a * a + b * b < 1f) return Soil; // the play area's sand and worn ground
                return Math.Abs(u) < 1f || Math.Abs(v - depth * .5f) < .8f ? Gravel : Lawn;
            }
            if (style == 1 && v > 2.5f && v < 10.5f && h - Math.Abs(u) > 2.5f) return Gravel;
            if (model != null && v < setback - 6f && (Math.Abs(Math.Abs(u) - 6f) < .9f || v > setback - 9f && v < setback - 7.6f && Math.Abs(u) < 15f)) return Gravel;
            return (identity >> 5) % 4 == 0 && v > depth - 7f && u > h - 9f ? Rough : Lawn;
        }

        // Behind a terraced house: its paved court with worn patches and a shed, then a narrow garden of beds, lawn or
        // rough grass to the back wall; the house itself stands on the pavement.
        int CoreYard(float u, float v)
        {
            float back = setback + 5.5f;
            if (v < back) return Gravel;
            float court = back + (depth - back) * (.4f + (identity >> 9) % 4 * .1f);
            if (v < court) return Noise((u + identity % 50) / 2.5f, (v + (identity >> 6) % 40) / 2.5f) > .74f ? Soil : Gravel;
            if (cultivated && (v - court) % 2.7f < 1.6f && u < 0f) return Soil;
            return untended ? Rough : Lawn;
        }
    }
}

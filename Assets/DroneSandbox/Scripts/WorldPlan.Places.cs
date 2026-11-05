using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Settlement and military geography use the finished relief and water instead of independent scatter.
    public static partial class WorldPlan
    {
        static readonly string[] TownNames = { "Halvard", "Brenmoor", "Orren Vale", "Velden" };
        static readonly string[] VillageNames =
        {
            "Esterhay", "Marrowfield", "Tolbeck", "Grisham", "Wenlow", "Carrick", "Dunmere", "Ashcombe", "Fallow Cross", "Redwick",
            "Sorrel", "Hythe Green", "Kelmarsh", "Orrin", "Lowfold", "Brackwater", "Merrow", "Alderfen", "Rillford", "Harth"
        };

        // The front remains east of the middle, with broad salients and smaller local dents.
        static void PlanFront()
        {
            for (float z = Half; z >= -Half; z -= 500f)
                FrontLine.Add(new Vector2(4200f + 1300f * (float)Math.Sin(z / 11000f + phase)
                    + 450f * (float)Math.Sin(z / 3700f - phase * 1.3f) + 500f * (Noise(z / 6000f, phase, 81) - 0.5f), z));
        }

        // Four valley/crossing towns anchor the network; twenty villages occupy dispersed gentle catchments.
        static void PlanSettlements(Random rng)
        {
            Vector2 north = Rivers[0].points[ClosestZ(Rivers[0].points, 17000f)];
            Vector2 south = Rivers[0].points[ClosestZ(Rivers[0].points, -15000f)];
            Vector2[] anchors = { new Vector2(north.x - 1200f, north.y), new Vector2(south.x + 1400f, south.y),
                new Vector2(12500f, 11000f), new Vector2(14000f, -17000f) };
            for (int k = 0; k < anchors.Length; k++)
                AddSettlement(rng, anchors[k], TownNames[k], true, Range(rng, 650f, 1150f));
            for (int k = 0; k < VillageNames.Length; k++)
            {
                float x = -22500f + (k % 5) * 10800f + Range(rng, -2200f, 2200f);
                float z = -21000f + (k / 5) * 13500f + Range(rng, -2300f, 2300f);
                AddSettlement(rng, new Vector2(x, z), VillageNames[k], false, Range(rng, 220f, 440f));
            }
        }

        // Candidate footprints must be dry and gentle; nearby low valleys outweigh proximity to the anchor.
        static void AddSettlement(Random rng, Vector2 anchor, string name, bool town, float radius)
        {
            float best = float.MaxValue;
            Vector2 chosen = anchor;
            for (int k = 0; k < 1800; k++)
            {
                float reach = k < 1200 ? 5000f : 11000f;
                Vector2 p = new Vector2(Clamp(anchor.x + Range(rng, -reach, reach), -Half + 2000f, Half - 2000f),
                    Clamp(anchor.y + Range(rng, -reach, reach), -Half + 2000f, Half - 2000f));
                if (!DryFlat(p, town ? 0.035f : 0.055f, radius * 0.65f) || Height(p.x, p.y) > (town ? 180f : 240f)) continue;
                bool crowded = false;
                foreach (var s in Settlements) if (Distance(p, s.center) < radius + s.radius + 1800f) { crowded = true; break; }
                if (crowded) continue;
                float score = Distance(anchor, p) * 0.035f + Grade(p.x, p.y) * 6000f + Height(p.x, p.y) * 0.4f
                    + Math.Abs(RiverDistance(p.x, p.y) - (town ? 1000f : 700f)) * 0.025f;
                if (score < best) { best = score; chosen = p; }
            }
            if (best == float.MaxValue) throw new InvalidOperationException("No dry settlement ground for " + name);
            Settlements.Add(new WorldSettlement { name = name, center = chosen, radius = radius, town = town });
        }

        // Test the centre and four footprint edges so a low centre cannot hide a wet or steep site.
        static bool DryFlat(Vector2 p, float grade, float radius)
        {
            if (!Inside(p.x + radius, p.y + radius) || !Inside(p.x - radius, p.y - radius)) return false;
            for (int k = 0; k < 5; k++)
            {
                float x = p.x + (k == 1 ? radius : k == 2 ? -radius : 0f);
                float z = p.y + (k == 3 ? radius : k == 4 ? -radius : 0f);
                if (CoverAt(x, z) == Cover.Water || RiverDistance(x, z) < 450f || Grade(x, z) > grade || InLake(new Vector2(x, z))) return false;
            }
            return true;
        }

        // Town highways, a connected village network and a rail connection share the terrain-cost router.
        static void PlanRoads(Random rng)
        {
            var towns = new List<WorldSettlement>();
            foreach (var s in Settlements) if (s.town) towns.Add(s);
            towns.Sort((a, b) => b.center.y.CompareTo(a.center.y));
            for (int k = 1; k < towns.Count; k++) AddRoad(towns[k - 1].center, towns[k].center, RoadClass.Highway);
            AddRoad(new Vector2(-Half, towns[0].center.y - 2000f), towns[0].center, RoadClass.Highway);
            AddRoad(towns[towns.Count - 1].center, new Vector2(Half, towns[towns.Count - 1].center.y + 3000f), RoadClass.Highway);
            var waiting = new List<WorldSettlement>();
            foreach (var s in Settlements) if (!s.town) waiting.Add(s);
            while (waiting.Count > 0)
            {
                int best = 0; float distance = float.MaxValue; Vector2 join = new Vector2();
                for (int k = 0; k < waiting.Count; k++)
                {
                    Vector2 q = NearestRoad(waiting[k].center);
                    float d = Distance(q, waiting[k].center);
                    if (d < distance) { distance = d; best = k; join = q; }
                }
                AddRoad(waiting[best].center, join, RoadClass.Road); waiting.RemoveAt(best);
            }
            // Secondary links close useful loops instead of leaving the entire civilian network as a tree.
            for (int k = 4; k < Settlements.Count; k += 4)
            {
                WorldSettlement s = Settlements[k], other = Settlements[(k + 5 - 4) % VillageNames.Length + 4];
                if (Distance(s.center, other.center) < 15000f) AddRoad(s.center, other.center, RoadClass.Road);
            }
            var rail = new WorldRoad { kind = RoadClass.Road };
            Append(rail.points, FindPath(new Vector2(Settlements[0].center.x - 2500f, Half), Settlements[0].center, 2));
            Append(rail.points, FindPath(Settlements[0].center, Settlements[1].center, 2));
            Append(rail.points, FindPath(Settlements[1].center, new Vector2(Settlements[1].center.x + 2500f, -Half), 2));
            Railways.Add(rail);
        }

        // All road endpoints are retained exactly so links attach to their parent road without visual gaps.
        static void AddRoad(Vector2 a, Vector2 b, RoadClass kind)
        {
            if (Distance(a, b) < 2f) return;
            var road = new WorldRoad { kind = kind };
            road.points.AddRange(FindPath(a, b, 1)); Roads.Add(road);
        }

        // Find a junction on a segment, not just its vertices, for natural branch-road placement.
        static Vector2 NearestRoad(Vector2 p)
        {
            float best = float.MaxValue; Vector2 point = p;
            foreach (var road in Roads)
                for (int k = 1; k < road.points.Count; k++)
                {
                    Vector2 q = Mix(road.points[k - 1], road.points[k], Projection(p, road.points[k - 1], road.points[k]));
                    float d = Distance(p, q);
                    if (d < best) { best = d; point = q; }
                }
            return point;
        }

        // Eight staggered launch sites fit the narrow rear area while maintaining 8-12 km neighbour spacing.
        static void PlanSites(Random rng)
        {
            AddSite(rng, WorldSiteKind.Airbase, "Norvane Air Base", new Vector2(-21000f, -2000f), 4000f, true);
            PlanLaunchSites(rng);
            float hqZ = Range(rng, -8000f, 8000f);
            AddSite(rng, WorldSiteKind.EnemyHq, "Dravin Front Command", new Vector2(FrontAt(hqZ).x + 9000f, hqZ), 2000f, false);
            for (int k = 0; k < 4; k++)
            {
                float z = -20000f + k * 13000f + Range(rng, -1500f, 1500f);
                AddSite(rng, WorldSiteKind.EnemyDepot, "Depot " + (k + 1), new Vector2(FrontAt(z).x + Range(rng, 9000f, 15000f), z), 2500f, false);
            }
            string[] names = { "Greywatch Ridge", "The Harth Crown", "North Sentinel" };
            for (int k = 0; k < peaks.Count; k++) Sites.Add(new WorldSite { kind = WorldSiteKind.Landmark, name = names[k], position = peaks[k], norvane = peaks[k].x < FrontAt(peaks[k].y).x });
        }

        // Prefer flat, accessible ground while enforcing the headquarters' actual offset from the local front.
        static void AddSite(Random rng, WorldSiteKind kind, string name, Vector2 anchor, float spread, bool norvane)
        {
            float best = float.MaxValue; Vector2 chosen = anchor;
            for (int k = 0; k < 1000; k++)
            {
                Vector2 p = new Vector2(anchor.x + Range(rng, -spread, spread), anchor.y + Range(rng, -spread, spread));
                float depth = p.x - FrontAt(p.y).x;
                if (!DryFlat(p, kind == WorldSiteKind.Airbase ? 0.018f : 0.045f, kind == WorldSiteKind.Airbase ? 700f : 130f)) continue;
                if (!AwayFromSettlements(p, kind == WorldSiteKind.Airbase ? 900f : 350f)) continue;
                if (kind == WorldSiteKind.EnemyHq && (depth < 6000f || depth > 12000f)) continue;
                if (!norvane && depth < 5000f) continue;
                float score = Grade(p.x, p.y) * 10000f + Distance(anchor, p) * 0.04f;
                if (score < best) { best = score; chosen = p; }
            }
            if (best == float.MaxValue) throw new InvalidOperationException("No dry installation ground for " + name);
            InstallSite(kind, name, chosen, norvane);
        }

        // Candidate chains are solved together so a good early site cannot prevent a later 8 km separation.
        static void PlanLaunchSites(Random rng)
        {
            var options = new List<Vector2>[8];
            var costs = new float[8][]; var parents = new int[8][];
            for (int row = 0; row < 8; row++)
            {
                options[row] = new List<Vector2>();
                float target = 26300f - row * (52600f / 7f);
                for (int t = 0; t < 1600; t++)
                {
                    float z = Clamp(target + Range(rng, -1900f, 1900f), -26800f, 26800f);
                    float depth = row % 2 == 0 ? Range(rng, 4050f, 4900f) : Range(rng, 9100f, 9950f);
                    Vector2 p = new Vector2(FrontAt(z).x - depth, z);
                    if (DryFlat(p, 0.055f, 100f) && AwayFromSettlements(p, 300f)) options[row].Add(p);
                }
                costs[row] = new float[options[row].Count]; parents[row] = new int[options[row].Count];
                for (int k = 0; k < options[row].Count; k++)
                {
                    Vector2 p = options[row][k];
                    float local = Grade(p.x, p.y) * 8000f + Math.Abs(p.y - target) * 0.10f;
                    float best = row == 0 ? 0f : float.MaxValue; int parent = -1;
                    if (row > 0)
                        for (int j = 0; j < options[row - 1].Count; j++)
                        {
                            if (costs[row - 1][j] >= best) continue;
                            float d = Distance(p, options[row - 1][j]);
                            if (d < 8000f || d > 12000f) continue;
                            best = costs[row - 1][j]; parent = j;
                        }
                    costs[row][k] = best == float.MaxValue ? best : best + local; parents[row][k] = parent;
                }
            }
            int chosen = -1; float score = float.MaxValue;
            for (int k = 0; k < options[7].Count; k++) if (costs[7][k] < score) { score = costs[7][k]; chosen = k; }
            if (chosen < 0) throw new InvalidOperationException("No dry 8-12 km launch-site chain");
            var points = new Vector2[8];
            for (int row = 7; row >= 0; row--) { points[row] = options[row][chosen]; chosen = parents[row][chosen]; }
            for (int row = 0; row < 8; row++) InstallSite(WorldSiteKind.LaunchSite, "Strip " + (char)('A' + row), points[row], true);
        }

        // Access spurs make every installation road-served, even before the local detail is loaded.
        static void InstallSite(WorldSiteKind kind, string name, Vector2 p, bool norvane)
        {
            Vector2 join = NearestRoad(p);
            AddRoad(p, join, kind == WorldSiteKind.LaunchSite ? RoadClass.Track : RoadClass.Road);
            Sites.Add(new WorldSite { kind = kind, name = name, position = p, norvane = norvane });
        }

        // Farms occupy dry open ground outside villages, with a short track to the existing road network.
        static void PlanFarms(Random rng)
        {
            foreach (var settlement in Settlements)
                for (int k = 0; k < 2; k++)
                {
                    for (int t = 0; t < 120; t++)
                    {
                        float angle = Range(rng, 0f, 6.283185f), distance = settlement.radius + Range(rng, 700f, 1900f);
                        Vector2 p = new Vector2(settlement.center.x + distance * (float)Math.Cos(angle), settlement.center.y + distance * (float)Math.Sin(angle));
                        if (!DryFlat(p, 0.04f, 90f) || !AwayFromSettlements(p, 220f)) continue;
                        bool close = false;
                        foreach (var f in Farms) if (Distance(f.position, p) < 450f) close = true;
                        if (close) continue;
                        Farms.Add(new WorldFarm { position = p }); AddRoad(p, NearestRoad(p), RoadClass.Track); break;
                    }
                }
        }

        // Intersection parameters locate true centre-line crossings, including streams narrower than a grid cell.
        static bool Intersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out float u)
        {
            float ax = b.x - a.x, az = b.y - a.y, bx = d.x - c.x, bz = d.y - c.y;
            float det = ax * bz - az * bx;
            t = 0f; u = 0f;
            if (Math.Abs(det) < 0.001f) return false;
            float dx = c.x - a.x, dz = c.y - a.y;
            t = (dx * bz - dz * bx) / det; u = (dx * az - dz * ax) / det;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        // Installations and isolated farms must not erase the occupied area of an existing village.
        static bool AwayFromSettlements(Vector2 p, float clearance)
        {
            foreach (var s in Settlements) if (Distance(p, s.center) < s.radius * 1.35f + clearance) return false;
            return true;
        }

        // Export crossing spans from the final smoothed geometry, not the A* grid or raster water mask.
        static void ExportBridges()
        {
            for (int rail = 0; rail < 2; rail++)
            {
                List<WorldRoad> routes = rail == 0 ? Roads : Railways;
                for (int r = 0; r < routes.Count; r++)
                {
                    var route = routes[r];
                    for (int k = 1; k < route.points.Count; k++)
                        for (int ri = 0; ri < Rivers.Count; ri++)
                        {
                            var river = Rivers[ri];
                            Vector2 a = route.points[k - 1], b = route.points[k];
                            for (int s = 1; s < river.points.Count; s++)
                            {
                                Vector2 c = river.points[s - 1], d = river.points[s];
                                if (Math.Max(a.x, b.x) < Math.Min(c.x, d.x) || Math.Min(a.x, b.x) > Math.Max(c.x, d.x)
                                    || Math.Max(a.y, b.y) < Math.Min(c.y, d.y) || Math.Min(a.y, b.y) > Math.Max(c.y, d.y)) continue;
                                float t, u;
                                if (!Intersection(a, b, c, d, out t, out u)) continue;
                                Vector2 p = Mix(a, b, t);
                                bool duplicate = false;
                                foreach (var bridge in Bridges) if (bridge.railway == (rail == 1) && bridge.route == r && Distance(bridge.position, p) < 80f) duplicate = true;
                                if (duplicate) continue;
                                float routeLength = Distance(a, b), riverLength = Distance(c, d);
                                float sine = Math.Abs((b.x - a.x) * (d.y - c.y) - (b.y - a.y) * (d.x - c.x)) / (routeLength * riverLength);
                                float halfSpan = Lerp(river.halfWidths[s - 1], river.halfWidths[s], u) / Math.Max(0.15f, sine) + 24f;
                                Vector2 ba = new Vector2(p.x - (b.x - a.x) / routeLength * halfSpan, p.y - (b.y - a.y) / routeLength * halfSpan);
                                Vector2 bb = new Vector2(p.x + (b.x - a.x) / routeLength * halfSpan, p.y + (b.y - a.y) / routeLength * halfSpan);
                                Bridges.Add(new WorldBridge { position = p, a = ba, b = bb, deckLevel = Math.Max(Lerp(river.waterLevels[s - 1], river.waterLevels[s], u) + 4f,
                                    Math.Max(Height(ba.x, ba.y), Height(bb.x, bb.y)) + 1f), railway = rail == 1, route = r, river = ri });
                            }
                        }
                }
            }
        }
    }
}

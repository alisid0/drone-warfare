using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Gives named sectors a protected, real-size centrepiece after the old layout has been scaled to two km.
    public static class SectorIdentity
    {
        // Plans land use before MapLayout lays out village streets or parcels; other sector recipes stay intact.
        public static void Plan(SectorPlan p)
        {
            if (p.kind == SectorKind.Marshland) { Marsh(p); return; }
            bool forest = p.kind == SectorKind.DeepForest, industry = p.kind == SectorKind.IndustrialZone;
            bool airfield = p.kind == SectorKind.Airfield, rail = p.kind == SectorKind.RailJunction;
            if (!forest && !industry && !airfield && !rail && !p.Type.town) return;
            Vector2 along = (MapLayout.Flat(p.enemyHqPosition) - MapLayout.Flat(p.basePosition)).normalized;
            if ((rail || industry) && p.railways.Count > 0)
            {
                var line = p.railways[0];
                along = (line[line.Length - 1] - line[0]).normalized;
            }
            float length = forest ? 680f : airfield ? 620f : rail ? 440f : industry ? 320f : 350f;
            float width = forest ? 640f : airfield ? 220f : rail ? 150f : industry ? 280f : 310f;
            FeatureArea area = FindArea(p, along, length, width, forest);
            p.reservedFeatures.Add(area);
            if (forest) Forest(p, area);
            else if (airfield) Airfield(p, area);
            else if (industry) Industry(p, area);
            else if (rail) RailYard(p, area);
            else Town(p, area);
            FeatureBridges(p);
        }

        // Scores dry, unobstructed rectangles, retaining through-roads and mission sites instead of moving them.
        static FeatureArea FindArea(SectorPlan p, Vector2 along, float length, float width, bool forest)
        {
            var roads = new List<Vector2>();
            foreach (var line in p.roads) roads.AddRange(MapLayout.Smooth(line, 8f));
            var river = p.river == null ? new List<Vector2>() : MapLayout.Smooth(p.river, 8f);
            var riverPoints = river.ToArray();
            var rails = new List<Vector2>();
            foreach (var line in p.railways) rails.AddRange(MapLayout.Smooth(line, 8f));
            FeatureArea best = default;
            float score = float.MaxValue;
            for (int turn = 0; turn < 2; turn++)
                for (float v = -720f; v <= 720f; v += 40f)
                    for (float u = -720f; u <= 720f; u += 40f)
                    {
                        var a = new FeatureArea { center = new Vector2(u, v), along = turn == 0 ? along : new Vector2(-along.y, along.x),
                            halfLength = length / 2f, halfWidth = width / 2f };
                        bool clear = true;
                        foreach (float x in new[] { -1f, 1f })
                            foreach (float z in new[] { -1f, 1f })
                            {
                                Vector2 q = a.Point(x * a.halfLength, z * a.halfWidth);
                                if (Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.y)) > MapLayout.Edge - 35f) clear = false;
                            }
                        if (!clear || a.Contains(MapLayout.Flat(p.basePosition), 65f) || a.Contains(MapLayout.Flat(p.enemyHqPosition), 65f)) continue;
                        foreach (var d in p.defences) if (a.Contains(MapLayout.Flat(d.position), 20f)) clear = false;
                        foreach (var s in p.sites) if (a.Contains(s.position, s.flat + 15f)) clear = false;
                        foreach (var q in p.villages) if (a.Contains(q, 110f)) clear = false;
                        if (!forest)
                        {
                            foreach (var q in river) if (a.Contains(q, p.riverHalfWidth + 32f)) { clear = false; break; }
                            foreach (var q in roads) if (a.Contains(q, 24f)) { clear = false; break; }
                            foreach (var q in rails) if (a.Contains(q, 12f)) { clear = false; break; }
                        }
                        if (!clear) continue;
                        if (p.world != null && !WorldGround(p, a, forest)) continue;
                        if (!forest && rails.Count > 0)
                        {
                            Vector2 entry = a.Point(-a.halfLength - 8f, p.kind == SectorKind.RailJunction ? -30f : -104f);
                            if (!DrySegment(p, riverPoints, Nearest(rails, entry), entry)) continue;
                        }
                        float distance = a.center.magnitude;
                        if (rails.Count > 0) distance += Vector2.Distance(Nearest(rails, a.center), a.center) * 2f;
                        if (distance >= score) continue;
                        score = distance;
                        best = a;
                    }
            // A failed recipe must be visible in seed validation, never silently become another farmland sector.
            if (score == float.MaxValue) throw new System.InvalidOperationException("No clear landmark footprint for " + p.kind + " seed " + p.seed);
            return best;
        }

        // In a sector cut from the world: no water, wetland or town anywhere in the footprint (sampled every 40 m),
        // and for a built landmark no more than 18 m between its highest and lowest ground, so the levelled pads
        // don't become cliffs. The forest core only has to stay off the water.
        static bool WorldGround(SectorPlan p, FeatureArea a, bool forest)
        {
            float low = float.MaxValue, high = float.MinValue;
            for (float v = -a.halfWidth; v <= a.halfWidth + 0.1f; v += 40f)
                for (float u = -a.halfLength; u <= a.halfLength + 0.1f; u += 40f)
                {
                    Vector2 q = a.Point(u, v);
                    if (p.world.NearWater(q, 15f)) return false;
                    if (forest) continue;
                    if (p.world.CoverAt(q.x, q.y) == Cover.Town) return false;
                    float h = p.world.Height(q.x, q.y);
                    low = Mathf.Min(low, h);
                    high = Mathf.Max(high, h);
                }
            return forest || high - low <= 18f;
        }

        // A mature core, two glades and a through forestry track; ordinary woods fill the unreserved surroundings.
        static void Forest(SectorPlan p, FeatureArea a)
        {
            p.featureStreets.Add(new[] { a.Point(-a.halfLength - 12f, -35f), a.Point(-120f, -15f), a.Point(70f, 28f), a.Point(a.halfLength + 12f, 40f) });
            p.woodlandGlades.Add((a.Point(-130f, 90f), 38f));
            p.woodlandGlades.Add((a.Point(150f, -95f), 48f));
            Site(p, "Scenery_Hut", a.Point(-130f, 90f), a.along, 5f);
            Site(p, "Scenery_LogPile", a.Point(-117f, 90f), a.along, 4f);
            Connect(p, a.Point(-a.halfLength - 12f, -35f));
        }

        // A 560 x 24 m runway, parallel taxiway and apron with correctly separated real-size aviation buildings.
        static void Airfield(SectorPlan p, FeatureArea a)
        {
            p.airstripCenter = a.Point(0f, -65f);
            p.airstripAlong = a.along;
            p.airstripLength = 560f;
            p.airstripWidth = 24f;
            Surface(p, a, 0f, 48f, 460f, 84f, true);
            Surface(p, a, 0f, -26f, 520f, 12f, false);
            foreach (float u in new[] { -205f, -80f, 0f, 85f, 205f }) Surface(p, a, u, -10f, 10f, 40f, true);
            foreach (float u in new[] { -242f, 242f }) Surface(p, a, u, -45f, 12f, 48f, false);
            Site(p, "Scenery_HangarLarge", a.Point(-150f, 50f), -a.Across, 36f);
            for (int i = 0; i < 3; i++) Site(p, "Scenery_AircraftShelter", a.Point(-55f + i * 58f, 55f), -a.Across, 25f);
            Site(p, "Scenery_ControlTower", a.Point(140f, 60f), -a.Across, 10f);
            Site(p, "Scenery_FuelFarm", a.Point(195f, 57f), -a.Across, 18f);
            Site(p, "Scenery_Windsock", a.Point(200f, -5f), a.along, 4f);
            p.featureFlats.Add(a); // one level for runway, taxiways and apron, with no stepped joins
            Connect(p, a.Point(0f, a.halfWidth + 2f));
            p.featureStreets.Add(new[] { a.Point(0f, a.halfWidth + 2f), a.Point(0f, 90f) });
        }

        // Eight large structures surround working yards; pipes, a gated fence and a connected spur give it a purpose.
        static void Industry(SectorPlan p, FeatureArea a)
        {
            Surface(p, a, 0f, 0f, 312f, 272f, false);
            Surface(p, a, 0f, 15f, 265f, 180f, true);
            Site(p, "Scenery_FactoryHall", a.Point(-72f, -40f), -a.Across, 38f);
            Site(p, "Scenery_FactoryHall", a.Point(48f, -40f), -a.Across, 38f);
            Site(p, "Scenery_Warehouse", a.Point(-100f, 60f), -a.Across, 22f);
            Site(p, "Scenery_OfficeBlock", a.Point(65f, 94f), -a.Across, 18f);
            Site(p, "Scenery_CoolingTower", a.Point(115f, 25f), a.along, 30f);
            for (int i = 0; i < 3; i++) Site(p, "Scenery_StorageTank", a.Point(-40f + i * 31f, 58f), a.along, 16f);
            for (int i = 0; i < 5; i++) Site(p, "Scenery_PipeRack", a.Point(-42f + i * 20f, 6f), a.along, 12f);
            Fence(p, a, 12f);
            p.featureFlats.Add(a);
            Connect(p, a.Point(0f, a.halfWidth + 2f));
            p.featureStreets.Add(new[] { a.Point(0f, a.halfWidth + 2f), a.Point(0f, 106f) });
            if (p.railways.Count > 0)
            {
                Vector2 end = a.Point(-a.halfLength - 8f, -104f);
                Vector2 from = NearestRail(p, end);
                p.railways.Add(new[] { from, Vector2.Lerp(from, end, 0.5f), end, a.Point(-90f, -104f), a.Point(125f, -104f) });
                for (int i = 0; i < 4; i++) Site(p, "Scenery_RailTankWagon", a.Point(-30f + i * 17f, -104f), a.along, 9f);
            }
        }

        // Three sidings fan from a connected arrival road; the crane straddles the two tracks at 7 m and 14 m.
        static void RailYard(SectorPlan p, FeatureArea a)
        {
            Surface(p, a, 0f, 0f, 430f, 140f, false);
            p.featureFlats.Add(a);
            Vector2 throat = a.Point(-210f, -30f), from = NearestRail(p, throat);
            p.railways.Add(new[] { from, Vector2.Lerp(from, throat, 0.5f), throat, a.Point(-140f, -30f), a.Point(210f, -30f) });
            for (int track = 1; track <= 3; track++)
            {
                float v = -30f + track * 7f;
                p.railways.Add(new[] { throat, a.Point(-180f + track * 14f, -30f), a.Point(-115f + track * 12f, v), a.Point(-55f, v), a.Point(200f, v) });
                for (int i = 0; i < 9; i++)
                {
                    string model = track == 1 ? "Scenery_RailContainerWagon" : track == 2 ? "Scenery_RailHopper" : i % 2 == 0 ? "Scenery_RailWagon" : "Scenery_RailTankWagon";
                    Site(p, model, a.Point(-20f + i * 19f, v), a.along, 9f);
                }
            }
            Site(p, "Scenery_GantryCrane", a.Point(28f, -19.5f), a.along, 12f);
            Site(p, "Scenery_FreightShed", a.Point(50f, 16f), -a.Across, 26f);
            Site(p, "Scenery_SignalBox", a.Point(-125f, 8f), a.along, 10f);
            Site(p, "Scenery_Station", a.Point(-155f, -22.4f), a.along, 16f);
            Surface(p, a, 55f, 46f, 180f, 32f, true);
            Connect(p, a.Point(0f, a.halfWidth + 2f));
            p.featureStreets.Add(new[] { a.Point(0f, a.halfWidth + 2f), a.Point(0f, 46f), a.Point(30f, 46f) });
        }

        // Thirty-six street-facing buildings: dense shops and terraces inside, detached homes outside and a works edge.
        static void Town(SectorPlan p, FeatureArea a)
        {
            for (int row = -1; row <= 1; row++)
            {
                float v = row * 80f;
                p.featureStreets.Add(new[] { a.Point(-165f, v), a.Point(0f, v), a.Point(165f, v) });
                foreach (float side in new[] { -1f, 1f })
                    for (int col = 0; col < 6; col++)
                    {
                        float u = -125f + col * 50f;
                        bool central = row == 0 && col > 0 && col < 4;
                        string model = central ? (col == 1 ? "Scenery_Townhouse_Row" : col == 2 ? "Scenery_Shop" : "Scenery_Apartment")
                            : "Scenery_House_" + (char)('A' + (col + row + 4 + (side > 0f ? 1 : 0)) % 4);
                        float radius = central ? (col == 3 ? 19f : col == 1 ? 16f : 11f) : 13f;
                        Site(p, model, a.Point(u, v + side * 22f), -a.Across * side, radius);
                    }
            }
            foreach (float u in new[] { -165f, 0f, 165f })
                p.featureStreets.Add(new[] { a.Point(u, -140f), a.Point(u, 0f), a.Point(u, 140f) });
            Site(p, "Scenery_Warehouse", a.Point(-95f, 135f), -a.Across, 22f);
            Site(p, "Scenery_OfficeBlock", a.Point(-40f, 135f), -a.Across, 18f);
            Surface(p, a, -70f, 133f, 108f, 40f, false);
            p.featureFlats.Add(a);
            Connect(p, a.Point(-a.halfLength - 2f, 0f));
            p.featureStreets.Add(new[] { a.Point(-a.halfLength - 2f, 0f), a.Point(-165f, 0f) });
        }

        // A 480 m continuous stream corridor; pools stay in its banks and wet ground joins them into one land use.
        static void Marsh(SectorPlan p)
        {
            if (p.river == null) p.river = new[] { new Vector2(-950f, 0f), new Vector2(950f, 0f) };
            var river = MapLayout.Smooth(p.river, 8f);
            var wet = new List<Vector2>();
            int middle = river.Count / 2;
            float walked = 0f;
            int first = middle;
            while (first > 0 && walked < 240f) { walked += Vector2.Distance(river[first], river[first - 1]); first--; }
            walked = 0f;
            for (int i = first; i < river.Count && walked < 480f; i++)
            {
                if (wet.Count > 0) walked += Vector2.Distance(wet[wet.Count - 1], river[i]);
                wet.Add(river[i]);
            }
            p.wetland = wet.ToArray();
            for (int i = 1; i < wet.Count; i++)
            {
                Vector2 d = wet[i] - wet[i - 1];
                p.reservedFeatures.Add(new FeatureArea { center = (wet[i] + wet[i - 1]) * 0.5f, along = d.normalized, halfLength = d.magnitude / 2f + 4f, halfWidth = 62f });
            }
            for (int i = 3; i < wet.Count - 2; i += 6)
            {
                Vector2 d = (wet[i + 1] - wet[i - 1]).normalized, side = new Vector2(-d.y, d.x);
                Vector2 c = wet[i] + side * ((i / 6 % 2 == 0 ? -1f : 1f) * 30f);
                bool clear = Vector2.Distance(c, MapLayout.Flat(p.basePosition)) > 85f && Vector2.Distance(c, MapLayout.Flat(p.enemyHqPosition)) > 85f;
                foreach (var road in p.roads) if (Distance(c, road) < 36f) clear = false;
                foreach (var site in p.defences) if (Vector2.Distance(c, MapLayout.Flat(site.position)) < 45f) clear = false;
                foreach (var village in p.villages) if (Vector2.Distance(c, village) < 110f) clear = false;
                if (clear) p.ponds.Add(new Pond { center = c, radius = 20f + i % 7, seed = p.seed * 0.01f + i });
            }
        }

        // A structure's footprint is levelled by the existing Ground.FlatSite path; missions still find real sites.
        static void Site(SectorPlan p, string model, Vector2 at, Vector2 facing, float flat)
        {
            p.sites.Add(new Site { model = model, position = at, yaw = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg, scale = 1f, flat = flat });
        }

        // The forestry track is planned after the old bridge pass, so register its stream crossings too.
        static void FeatureBridges(SectorPlan p)
        {
            if (p.river == null) return;
            var river = MapLayout.Smooth(p.river, 2f);
            foreach (var street in p.featureStreets)
            {
                var road = MapLayout.Smooth(street, 2f);
                for (int i = 1; i < road.Count; i++)
                    for (int j = 1; j < river.Count; j++)
                    {
                        Vector2 r = road[i] - road[i - 1], s = river[j] - river[j - 1], d = river[j - 1] - road[i - 1];
                        float cross = r.x * s.y - r.y * s.x;
                        if (Mathf.Abs(cross) < 0.00001f) continue;
                        float t = (d.x * s.y - d.y * s.x) / cross, u = (d.x * r.y - d.y * r.x) / cross;
                        if (t < 0f || t > 1f || u < 0f || u > 1f) continue;
                        Vector2 at = road[i - 1] + r * t;
                        bool known = false;
                        foreach (var bridge in p.bridges) if (Vector2.Distance(at, bridge) < 20f) known = true;
                        if (!known) p.bridges.Add(at);
                    }
            }
        }

        // Records surfaces separately from sites, so missions never choose an invisible yard marker as a building.
        static void Surface(SectorPlan p, FeatureArea a, float u, float v, float length, float width, bool concrete)
        {
            p.featureSurfaces.Add(new FeatureSurface { area = new FeatureArea { center = a.Point(u, v), along = a.along,
                halfLength = length / 2f, halfWidth = width / 2f }, concrete = concrete });
        }

        // One continuous boundary with a road gate and a rail gate on the opposite corner.
        static void Fence(SectorPlan p, FeatureArea a, float gate)
        {
            float u = a.halfLength - 3f, v = a.halfWidth - 3f;
            p.featureFences.Add(new[] { a.Point(-gate, v), a.Point(-u, v), a.Point(-u, -92f) });
            p.featureFences.Add(new[] { a.Point(-u, -116f), a.Point(-u, -v), a.Point(u, -v), a.Point(u, v), a.Point(gate, v) });
        }

        // Routes access round the reserved perimeter when its gate faces away from the nearest road.
        static void Connect(SectorPlan p, Vector2 to)
        {
            var a = p.reservedFeatures[0];
            var nodes = new[] { to, a.Point(-a.halfLength - 18f, -a.halfWidth - 18f), a.Point(a.halfLength + 18f, -a.halfWidth - 18f),
                a.Point(a.halfLength + 18f, a.halfWidth + 18f), a.Point(-a.halfLength - 18f, a.halfWidth + 18f) };
            var river = p.river == null ? null : MapLayout.Smooth(p.river, 4f).ToArray();
            var distances = new float[nodes.Length];
            var next = new int[nodes.Length];
            var visited = new bool[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) { distances[i] = float.MaxValue; next[i] = -1; }
            distances[0] = 0f;
            // Shortest dry route from each outside corner to the gate, using a five-node visibility graph.
            for (int k = 0; k < nodes.Length; k++)
            {
                int at = -1;
                for (int i = 0; i < nodes.Length; i++)
                    if (!visited[i] && distances[i] < float.MaxValue && (at < 0 || distances[i] < distances[at])) at = i;
                if (at < 0) break;
                visited[at] = true;
                for (int i = 0; i < nodes.Length; i++)
                {
                    float d = distances[at] + Vector2.Distance(nodes[at], nodes[i]);
                    if (visited[i] || d >= distances[i] || !DrySegment(p, river, nodes[at], nodes[i])) continue;
                    distances[i] = d;
                    next[i] = at;
                }
            }
            Vector2 best = to;
            float distance = float.MaxValue;
            int corner = -1;
            foreach (var road in p.roads)
                foreach (var q in MapLayout.Smooth(road, 12f))
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        float d = Vector2.Distance(q, nodes[i]) + distances[i];
                        if (d >= distance || !DrySegment(p, river, q, nodes[i])) continue;
                        best = q;
                        distance = d;
                        corner = i;
                    }
            if (corner < 0) throw new System.InvalidOperationException("No dry feature access for " + p.kind + " seed " + p.seed);
            // Separate straight legs retain the tested corner clearance instead of rounding through a fence.
            while (corner >= 0)
            {
                if (Vector2.Distance(best, nodes[corner]) > 0.1f) p.featureStreets.Add(new[] { best, nodes[corner] });
                best = nodes[corner];
                corner = next[corner];
            }
        }

        // Four-metre samples protect even narrow streams, bases and the complete perimeter of a reserved feature.
        static bool DrySegment(SectorPlan p, Vector2[] river, Vector2 from, Vector2 to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / 4f));
            for (int step = 0; step <= steps; step++)
            {
                Vector2 at = Vector2.Lerp(from, to, step / (float)steps);
                if (Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.y)) > MapLayout.Edge - 4f) return false;
                if (Vector2.Distance(at, MapLayout.Flat(p.basePosition)) < MapLayout.BaseRadius + 6f
                    || Vector2.Distance(at, MapLayout.Flat(p.enemyHqPosition)) < MapLayout.HqRadius + 6f) return false;
                if (river != null && Distance(at, river) < p.riverHalfWidth + 8f) return false;
                foreach (var area in p.reservedFeatures) if (area.Contains(at, 1f)) return false;
                foreach (var s in p.sites) if (Vector2.Distance(s.position, at) < s.flat + 4f) return false;
                foreach (var d in p.defences) if (Vector2.Distance(MapLayout.Flat(d.position), at) < MapLayout.DefenceRadius + 4f) return false;
            }
            return true;
        }
        // A connection starts exactly on the same smoothed railway the renderer uses.
        static Vector2 NearestRail(SectorPlan p, Vector2 to)
        {
            var points = new List<Vector2>();
            foreach (var line in p.railways) points.AddRange(MapLayout.Smooth(line, 2f));
            return Nearest(points, to);
        }

        // Stable first-on-tie selection keeps equal-distance placements deterministic.
        static Vector2 Nearest(List<Vector2> points, Vector2 to)
        {
            Vector2 best = to;
            float distance = float.MaxValue;
            foreach (var q in points)
                if ((q - to).sqrMagnitude < distance) { best = q; distance = (q - to).sqrMagnitude; }
            return best;
        }

        // Measures the actual polyline, including a crossing between vertices.
        static float Distance(Vector2 p, Vector2[] line)
        {
            float best = float.MaxValue;
            for (int i = 1; i < line.Length; i++)
            {
                Vector2 a = line[i - 1], d = line[i] - a;
                float t = d.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector2.Distance(p, a + d * t));
            }
            return best;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Lays out a sector from its type's recipe and a seed. The front runs across the map at a random angle: your
    // base on one side, the enemy HQ and its defences on the other, a river (if any) winding between them, roads
    // linking everything and running off the map, villages strung along the roads, then the type's special
    // features (railways, industry, an airstrip, a quarry, pylons, orchards, logging clearings, fortifications,
    // a reservoir). The same type and seed always give the same sector.
    public static class SectorGenerator
    {
        const float Reach = 262f;   // roads, rivers and railways run on past the terrain's edge (240 m out)
        // A village's footprint as the old 480 m map planned round it (the plan is laid out in its metres; villages
        // themselves are built at their real size, MapLayout.VillageRadius).
        const float OldVillageRadius = 22f;
        const float Inside = 150f;  // villages and features stay within this of the centre

        // Builds the seeded landscape, then reserves its landmark in final world metres before parcel planning.
        public static SectorPlan Generate(SectorKind kind, int seed)
        {
            if (kind == SectorKind.VarrowValley) return SectorPlan.Varrow();
            var type = SectorTypes.Get(kind);
            var g = new Builder(type, seed);
            var plan = g.Build();
            SectorIdentity.Plan(plan);
            return plan;
        }

        class Builder
        {
            readonly SectorType type;
            readonly System.Random rng;
            readonly SectorPlan p;
            Vector2 fwd, side;      // from your base towards the enemy, and across the front
            Vector2 basePos, hqPos;
            float townSign;         // the half of the map the town, airstrip or quarry sits in

            public Builder(SectorType sectorType, int seed)
            {
                type = sectorType;
                rng = new System.Random(seed * 7919 + (int)sectorType.kind * 104729 + 13);
                p = new SectorPlan
                {
                    kind = sectorType.kind, seed = seed, worldSeed = seed * 31 + 7, fieldSeed = seed * 17 + 11,
                    features = sectorType.features,
                };
            }

            float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            Vector2 At(float f, float s) => fwd * f + side * s;
            static Vector3 V3(Vector2 v) => new Vector3(v.x, 0f, v.y);

            public SectorPlan Build()
            {
                p.name = PlaceNames.Place(rng);
                p.hills = type.hills * R(0.8f, 1.2f);
                p.ridges = type.ridges * R(0.85f, 1.15f);
                p.noiseOffset = new Vector2(R(0f, 900f), R(0f, 900f));
                p.forest = Mathf.Clamp01(type.forest * R(0.85f, 1.15f));
                p.conifer = type.conifer;
                p.birch = type.birch;
                p.fields = Mathf.Clamp01(type.fields * R(0.9f, 1.1f));

                float angle = R(0f, Mathf.PI * 2f);
                fwd = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                side = new Vector2(-fwd.y, fwd.x);
                townSign = rng.NextDouble() < 0.5 ? -1f : 1f;
                // Base and HQ keep to the other half when something big (a town, an airstrip) takes this one.
                bool bigThing = type.town || p.Has(SectorFeature.Airstrip) || p.Has(SectorFeature.Quarry) || p.Has(SectorFeature.Industry);
                float baseSide = bigThing ? -townSign * R(8f, 25f) : R(-25f, 25f);
                float hqSide = bigThing ? -townSign * R(8f, 28f) : R(-25f, 25f);
                basePos = At(-70f, baseSide);
                hqPos = At(55f, hqSide);
                p.basePosition = V3(basePos);
                p.enemyHqPosition = V3(hqPos);
                Vector2 area = At(32f, hqSide * 0.5f);
                p.enemyArea = new Rect(area.x - 55f, area.y - 55f, 110f, 110f);

                River();
                Water();
                Features();
                Roads();
                Villages();
                Orchards();
                Defences();
                Bridges();
                return p.ScaleLayout(); // laid out in the old map's metres, like Varrow
            }

            // ---- River and water ----

            void River()
            {
                if (type.river == RiverStyle.None) return;
                p.riverHalfWidth = type.river == RiverStyle.Stream ? 2.5f : type.river == RiverStyle.WideRiver ? 8f : 4.5f;
                p.riverName = PlaceNames.River(rng);
                bool reservoir = p.Has(SectorFeature.Reservoir);
                float flow = rng.NextDouble() < 0.5 ? 1f : -1f; // which way it runs across the map
                float amplitude = type.river == RiverStyle.WideRiver ? R(6f, 12f) : R(8f, 18f);
                float frequency = R(0.008f, 0.016f), phase = R(0f, 6.3f);
                // Between the two sides, nearer your own.
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float offset = R(-14f, 6f);
                    var points = new List<Vector2>();
                    for (float t = reservoir ? -150f : -Reach; t <= Reach + 0.1f; t += 34f)
                    {
                        float f = offset + amplitude * Mathf.Sin(t * frequency + phase) + R(-4f, 4f);
                        points.Add(At(f, t * flow));
                    }
                    float hw = p.riverHalfWidth;
                    if (DistanceTo(basePos, points) < MapLayout.BaseRadius + hw + 14f) continue;
                    if (DistanceTo(hqPos, points) < MapLayout.HqRadius + hw + 14f) continue;
                    p.river = points.ToArray();
                    break;
                }
                if (p.river == null) return;
                if (reservoir)
                {
                    // A dam where the river starts, the lake bending away behind it towards the edge.
                    p.hasDam = true;
                    p.damPoint = p.river[0];
                    p.lakeLevel = 12f;
                    float bend = R(60f, 140f) * (rng.NextDouble() < 0.5 ? -1f : 1f), size = R(0.8f, 1.1f);
                    p.lakeLobes = new[]
                    {
                        (new Vector2(-34f, 0f) * size, 0f, 50f * size, 33f * size, R(0f, 6f)),
                        (new Vector2(-72f, 58f * Mathf.Sign(bend)) * size, bend, 62f * size, 38f * size, R(0f, 6f)),
                    };
                    p.islands = rng.NextDouble() < 0.6
                        ? new[] { (new Vector2(-58f, R(-10f, 10f)) * size, R(5f, 9f)) }
                        : new (Vector2, float)[0];
                }
            }

            // Natural lakes and marsh pools, clear of the bases, the river and each other.
            void Water()
            {
                if (type.kind == SectorKind.Marshland) return; // the connected corridor is laid out at real scale
                if (type.kind == SectorKind.Lakeshore)
                {
                    // One great lake reaching off the map on the far flank.
                    float s = townSign * R(150f, 175f);
                    p.ponds.Add(new Pond { center = At(R(-20f, 30f), s), radius = type.lakeSize * R(0.9f, 1.15f), seed = R(0f, 10f) });
                }
                else
                    for (int i = 0; i < type.lakes; i++) AddPond(type.lakeSize * R(0.75f, 1.2f), 40);
                for (int i = 0; i < type.ponds; i++) AddPond(R(6f, 16f), 30);
            }

            void AddPond(float radius, int attempts)
            {
                for (int a = 0; a < attempts; a++)
                {
                    var c = new Vector2(R(-Inside, Inside), R(-Inside, Inside));
                    if (Vector2.Distance(c, basePos) < radius * 1.25f + MapLayout.BaseRadius + 20f) continue;
                    if (Vector2.Distance(c, hqPos) < radius * 1.25f + MapLayout.HqRadius + 20f) continue;
                    if (p.river != null && DistanceTo(c, p.river) < radius * 1.25f + p.riverHalfWidth + 12f) continue;
                    bool clear = true;
                    foreach (var o in p.ponds)
                        if (Vector2.Distance(c, o.center) < (radius + o.radius) * 1.3f + 10f) clear = false;
                    if (!clear) continue;
                    p.ponds.Add(new Pond { center = c, radius = radius, seed = R(0f, 10f) });
                    return;
                }
            }

            bool Wet(Vector2 c, float margin)
            {
                if (p.river != null && DistanceTo(c, p.river) < p.riverHalfWidth + margin) return true;
                foreach (var o in p.ponds)
                    if (Vector2.Distance(c, o.center) < o.radius * 1.25f + margin) return true;
                if (p.hasDam && Vector2.Distance(c, p.damPoint) < 140f) return true;
                return false;
            }

            // ---- Special features (before the roads, so roads can serve them) ----

            readonly List<Vector2> destinations = new List<Vector2>(); // places a road should reach

            void Features()
            {
                if (p.Has(SectorFeature.Rail)) Railways();
                if (p.Has(SectorFeature.Airstrip) && type.kind != SectorKind.Airfield)
                {
                    p.airstripAlong = fwd;
                    p.airstripLength = 230f;
                    p.airstripCenter = At(R(-10f, 15f), townSign * 62f);
                    p.sites.Add(MakeSite("Base_Hangar", p.airstripCenter + side * townSign * 34f + fwd * R(-40f, 40f), Yaw(-side * townSign), 1f, 16f));
                    destinations.Add(p.airstripCenter + side * townSign * 30f);
                }
                if (p.Has(SectorFeature.Quarry))
                {
                    p.quarry = At(R(-5f, 30f), townSign * R(58f, 72f));
                    p.quarryRadius = 36f;
                    destinations.Add(p.quarry - side * townSign * (p.quarryRadius + 6f));
                }
                if (p.Has(SectorFeature.Industry) && type.kind != SectorKind.IndustrialZone) Industry();
                if (p.Has(SectorFeature.PowerLine)) PowerLine();
                if (p.Has(SectorFeature.Logging)) Logging();
                if (p.Has(SectorFeature.Fortifications)) Fortifications();
            }

            void Railways()
            {
                // The main line runs across the map on your side of the river.
                float f0 = R(-44f, -34f), wiggle = R(4f, 10f), phase = R(0f, 6.3f);
                float F(float t) => f0 + wiggle * Mathf.Sin(t * 0.006f + phase);
                var line = new List<Vector2>();
                for (float t = -Reach; t <= Reach + 0.1f; t += 40f) line.Add(At(F(t), t));
                p.railways.Add(line.ToArray());
                if (type.kind == SectorKind.RailJunction) return; // the real-metre yard replaces the old scaled sidings
                if (!p.Has(SectorFeature.RailYard))
                {
                    // A small station somewhere along it.
                    // The station's platform side (its +X) faces the line, 7.6 m from the track's centre, and it
                    // runs along the line (its +Z).
                    float along = R(-60f, 60f);
                    Vector2 at = At(F(along), along) - fwd * 7.6f;
                    p.sites.Add(MakeSite("Scenery_Station", at, Yaw(side), 1f, 15f));
                    destinations.Add(at - fwd * 10f);
                    return;
                }
                // A junction: a branch line runs off from the main one towards the rear, past a station and
                // sidings full of wagons.
                float s = Mathf.Clamp(p.basePosition.x * side.x + p.basePosition.z * side.y + (rng.NextDouble() < 0.5 ? 55f : -55f), -70f, 70f);
                float f = F(s);
                var branch = new List<Vector2>();
                for (float t = f; t >= -Reach; t -= 30f) branch.Add(At(t, s + R(-3f, 3f) * (t < f - 1f ? 1f : 0f)));
                p.railways.Add(branch.ToArray());
                Vector2 junction = At(f, s);
                // A siding alongside the main line, where the wagons stand.
                float dir = s > 0f ? -1f : 1f; // towards the middle of the map
                p.railways.Add(new[] { junction + side * dir * 22f + fwd * 5f, junction + side * dir * 30f + fwd * 5.2f,
                    junction + side * dir * 130f + fwd * 5.2f });
                float stationAt = s + 24f * (s > 0f ? -1f : 1f);
                Vector2 station = At(F(stationAt), stationAt) - fwd * 7.6f;
                p.sites.Add(MakeSite("Scenery_Station", station, Yaw(side), 1f, 15f));
                destinations.Add(station - fwd * 12f);
                for (int k = 0; k < 6; k++)
                {
                    string model = rng.NextDouble() < 0.6 ? "Scenery_RailWagon" : "Scenery_RailTankWagon";
                    Vector2 at = junction + side * dir * (40f + k * 15f) + fwd * 5.2f;
                    p.sites.Add(MakeSite(model, at, Yaw(side), 1f, 0f));
                }
            }

            void Industry()
            {
                for (int attempt = 0; attempt < 120; attempt++)
                {
                    // On the enemy's side of the river, out on the far flank.
                    Vector2 c = At(R(15f, 45f), townSign * R(50f, 80f));
                    if (Wet(c, attempt < 80 ? 28f : 18f) || Vector2.Distance(c, hqPos) < 52f || Vector2.Distance(c, basePos) < 60f) continue;
                    if (OnRail(c, 40f)) continue;
                    Vector2 a = fwd, b = side * townSign;
                    float yaw = Yaw(-b);
                    p.sites.Add(MakeSite("Scenery_Warehouse", c - a * 22f, yaw, 1f, 20f));
                    p.sites.Add(MakeSite("Scenery_Warehouse", c + a * 14f, yaw, 1f, 20f));
                    p.sites.Add(MakeSite("Scenery_Factory", c + b * 28f - a * 10f, yaw, 1f, 16f));
                    p.sites.Add(MakeSite("Scenery_Chimney", c + b * 28f + a * 14f, 0f, 1f, 5f));
                    p.sites.Add(MakeSite("Scenery_Silo", c + b * 4f + a * 40f, yaw, 1f, 9f));
                    p.sites.Add(MakeSite("Scenery_WaterTower", c - b * 20f + a * 38f, 0f, 1f, 6f));
                    destinations.Add(c - b * 22f);
                    return;
                }
            }

            void PowerLine()
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    float turn = R(-25f, 25f) * Mathf.Deg2Rad;
                    Vector2 dir = MapLayout.Rotate(side, turn), normal = new Vector2(-dir.y, dir.x);
                    Vector2 origin = normal * R(-60f, 60f);
                    var line = new[] { origin - dir * Reach, origin + dir * Reach };
                    if (DistanceTo(basePos, line) < 32f || DistanceTo(hqPos, line) < 32f) continue;
                    p.powerLine = line;
                    return;
                }
            }

            void Logging()
            {
                for (int attempt = 0; attempt < 80 && p.clearings.Count < 3; attempt++)
                {
                    var c = new Vector2(R(-120f, 120f), R(-120f, 120f));
                    if (Vector2.Distance(c, basePos) < 50f || Vector2.Distance(c, hqPos) < 50f || Wet(c, 25f)) continue;
                    bool clear = true;
                    foreach (var o in p.clearings) if (Vector2.Distance(c, o) < 70f) clear = false;
                    if (!clear) continue;
                    p.clearings.Add(c);
                    destinations.Add(c);
                    p.sites.Add(MakeSite("Scenery_Hut", c + new Vector2(R(-8f, 8f), R(-8f, 8f)), R(0f, 360f), 1f, 4f));
                    for (int k = 0; k < 3; k++)
                        p.sites.Add(MakeSite("Scenery_LogPile", c + new Vector2(R(-12f, 12f), R(-12f, 12f)), R(0f, 360f), 1f, 0f));
                }
            }

            void Fortifications()
            {
                // A line of dragon's teeth across the front in front of the HQ; a trench runs behind it (Ground digs
                // it along the same line, pushed back).
                float f = R(14f, 24f);
                var line = new List<Vector2>();
                for (float t = -Reach; t <= Reach + 0.1f; t += 30f) line.Add(At(f + R(-3f, 3f), t));
                p.fortLine = line.ToArray();
                p.trenchLine = line.ConvertAll(q => q + fwd * 10f).ToArray();
            }

            // ---- Roads ----

            void Roads()
            {
                // The road from your base to the enemy HQ, the way everything in the war travels.
                Vector2 toHq = (hqPos - basePos).normalized;
                Road(Winding(basePos + toHq * (MapLayout.BaseRadius + 2f), hqPos, 4, 14f));
                switch (type.roads)
                {
                    case RoadStyle.Crossroads:
                    {
                        for (int attempt = 0; attempt < 30; attempt++)
                        {
                            Vector2 x = At(R(-15f, 20f), R(-45f, 45f));
                            var a = Winding(x - fwd * Reach, x + fwd * Reach, 8, 10f, x);
                            var b = Winding(x - side * Reach, x + side * Reach, 8, 10f, x);
                            if (attempt < 29 && (!ClearOfBases(a) || !ClearOfBases(b))) continue;
                            Road(a);
                            Road(b);
                            break;
                        }
                        break;
                    }
                    case RoadStyle.Through:
                    {
                        // One main road right across, bending gently, with the base and HQ roads joining it.
                        Vector2 mid = At(R(0f, 25f), 0f);
                        Road(Winding(mid - side * Reach, mid + side * Reach, 10, 7f));
                        break;
                    }
                    case RoadStyle.Grid:
                        TownGrid();
                        break;
                    case RoadStyle.Tracks:
                    {
                        int count = rng.Next(2, 4), made = 0;
                        for (int attempt = 0; attempt < 40 && made < count; attempt++)
                        {
                            float a = R(0f, Mathf.PI * 2f);
                            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                            Vector2 mid = new Vector2(R(-60f, 60f), R(-60f, 60f));
                            var track = Winding(mid - dir * Reach, mid + dir * Reach, 9, 18f);
                            if (!ClearOfBases(track)) continue;
                            Road(track);
                            made++;
                        }
                        break;
                    }
                }
                // A road out of the back of your base, and tracks to whatever the features need reaching.
                Vector2 back = basePos - toHq * (MapLayout.BaseRadius + 2f);
                Road(Winding(back, back - toHq * Reach + side * R(-60f, 60f), 5, 10f));
                foreach (var d in destinations) Spur(d);
                Spur(hqPos);
            }

            // Clear of both bases, and of the yards, stations and other levelled sites of the special features.
            bool ClearOfBases(Vector2[] road)
            {
                if (DistanceTo(basePos, road) < MapLayout.BaseRadius + 10f || DistanceTo(hqPos, road) < MapLayout.HqRadius + 8f) return false;
                foreach (var s in p.sites)
                    if (s.flat > 0f && DistanceTo(s.position, road) < s.flat + 4f) return false;
                return true;
            }

            // Keeps the through route; SectorIdentity adds compact town streets after scaling the old coordinates.
            void TownGrid()
            {
                Road(Winding(At(0f, -Reach), At(0f, Reach), 8, 8f));
            }

            void Road(Vector2[] points)
            {
                // Keep roads out of lakes and pools: push points out past the shore.
                for (int i = 0; i < points.Length; i++)
                    foreach (var o in p.ponds)
                    {
                        Vector2 d = points[i] - o.center;
                        float need = o.radius * 1.3f + 8f;
                        if (d.magnitude < need) points[i] = o.center + (d.sqrMagnitude > 0.01f ? d.normalized : Vector2.right) * need;
                    }
                p.roads.Add(points);
            }

            // A track from the nearest road point to `to`, unless a road already passes close.
            void Spur(Vector2 to)
            {
                float best = float.MaxValue;
                Vector2 from = Vector2.zero;
                foreach (var road in p.roads)
                    foreach (var q in MapLayout.Smooth(road, 6f))
                    {
                        float d = Vector2.Distance(q, to);
                        if (d < best && Mathf.Abs(q.x) < 230f && Mathf.Abs(q.y) < 230f)
                        {
                            best = d;
                            from = q;
                        }
                    }
                if (best < 22f || best == float.MaxValue) return;
                Road(Winding(from, to, Mathf.Clamp(Mathf.RoundToInt(best / 35f), 1, 6), 6f));
            }

            // Points from a to b with sideways wander, optionally passing through `via`.
            Vector2[] Winding(Vector2 a, Vector2 b, int segments, float wander, Vector2? via = null)
            {
                var pts = new List<Vector2>();
                Vector2 dir = (b - a).normalized, normal = new Vector2(-dir.y, dir.x);
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    Vector2 q = Vector2.Lerp(a, b, t);
                    if (i > 0 && i < segments) q += normal * R(-wander, wander);
                    pts.Add(q);
                }
                if (via.HasValue)
                {
                    // Replace the nearest middle point with the crossing, so both roads meet there exactly.
                    int k = Mathf.Clamp(Mathf.RoundToInt(segments * Vector2.Dot(via.Value - a, dir) / Vector2.Distance(a, b)), 1, segments - 1);
                    pts[k] = via.Value;
                }
                return pts.ToArray();
            }

            // ---- Villages ----

            void Villages()
            {
                var used = new HashSet<string>();
                string Name()
                {
                    for (int i = 0; i < 20; i++)
                    {
                        string n = PlaceNames.Place(rng);
                        if (used.Add(n)) return n;
                    }
                    return PlaceNames.Place(rng) + " " + used.Count;
                }
                var candidates = new List<Vector2>();
                foreach (var road in p.roads)
                    foreach (var q in MapLayout.Smooth(road, 15f))
                        if (Mathf.Abs(q.x) < Inside && Mathf.Abs(q.y) < Inside) candidates.Add(q);
                for (int n = 0; n < type.villages && candidates.Count > 0; n++)
                {
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        var c = candidates[rng.Next(candidates.Count)];
                        if (!VillageSpot(c, 60f)) continue;
                        AddVillage(c, Name(), n == type.villages - 1 && rng.NextDouble() < 0.35);
                        break;
                    }
                }
            }

            bool VillageSpot(Vector2 c, float spacing)
            {
                if (Vector2.Distance(c, basePos) < 48f || Vector2.Distance(c, hqPos) < 48f) return false;
                if (Wet(c, OldVillageRadius + 6f) || OnRail(c, OldVillageRadius + 4f)) return false;
                foreach (var s in p.sites) if (s.flat > 0f && Vector2.Distance(c, s.position) < s.flat + OldVillageRadius) return false;
                if (p.Has(SectorFeature.Airstrip) && Mathf.Abs(Vector2.Dot(c - p.airstripCenter, side)) < 40f) return false;
                if (p.Has(SectorFeature.Quarry) && Vector2.Distance(c, p.quarry) < p.quarryRadius + 30f) return false;
                foreach (var v in p.villages) if (Vector2.Distance(c, v) < spacing) return false;
                return true;
            }

            void AddVillage(Vector2 c, string name, bool farmstead)
            {
                p.villages.Add(c);
                p.villageNames.Add(name);
                p.farmsteads.Add(farmstead);
            }

            // Rows of fruit trees in plots round the villages (Features plants them).
            void Orchards()
            {
                if (!p.Has(SectorFeature.Orchards)) return;
                int want = rng.Next(4, 8);
                for (int attempt = 0; attempt < 120 && p.orchards.Count < want; attempt++)
                {
                    Vector2 near = p.villages.Count > 0 ? p.villages[rng.Next(p.villages.Count)] : Vector2.zero;
                    float a = R(0f, Mathf.PI * 2f);
                    var plot = new FieldPlot
                    {
                        center = near + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(40f, 75f),
                        along = MapLayout.Rotate(fwd, rng.NextDouble() < 0.5 ? 0f : Mathf.PI / 2f),
                        halfLength = R(14f, 22f), halfWidth = R(10f, 16f),
                    };
                    if (OrchardFits(plot)) p.orchards.Add(plot);
                }
            }

            bool OrchardFits(FieldPlot plot)
            {
                foreach (var o in p.orchards)
                    if (Vector2.Distance(o.center, plot.center) < o.halfLength + plot.halfLength + 6f) return false;
                for (float u = -1f; u <= 1f; u += 0.5f)
                    for (float v = -1f; v <= 1f; v += 0.5f)
                    {
                        Vector2 q = plot.Point(u * plot.halfLength, v * plot.halfWidth);
                        if (Mathf.Abs(q.x) > Inside || Mathf.Abs(q.y) > Inside) return false;
                        if (Vector2.Distance(q, basePos) < 30f || Vector2.Distance(q, hqPos) < 30f || Wet(q, 8f) || OnRail(q, 6f)) return false;
                        foreach (var road in p.roads)
                            if (DistanceTo(q, road) < 7f) return false;
                        foreach (var vil in p.villages)
                            if (Vector2.Distance(q, vil) < OldVillageRadius + 4f) return false;
                    }
                return true;
            }

            // ---- Defences round the HQ, a forward post and a flank radar (free play uses these) ----

            void Defences()
            {
                float turn = R(0f, 360f) * Mathf.Deg2Rad;
                Vector2 Around(float x, float z) => hqPos + MapLayout.Rotate(new Vector2(x, z), turn);
                Add(EnemyType.Radar, Around(6f, -8f));
                Add(EnemyType.AAGun, Around(-12f, 0f));
                Add(EnemyType.AAGun, Around(0f, -14f));
                Add(EnemyType.AAGun, Around(10f, 9f));
                Add(EnemyType.AAGun, Spot(At(R(8f, 22f), R(-50f, 50f))));
                float flank = rng.NextDouble() < 0.5 ? -1f : 1f;
                Vector2 radar = Spot(At(R(30f, 48f), flank * R(45f, 65f)));
                Add(EnemyType.Radar, radar);
                Add(EnemyType.AAGun, Spot(radar + fwd * -8f + side * flank * 6f));
            }

            void Add(EnemyType t, Vector2 at) => p.defences.Add((t, V3(at)));

            // Nudges a point off water and roads.
            Vector2 Spot(Vector2 at)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    bool ok = !Wet(at, MapLayout.DefenceRadius + 4f);
                    foreach (var road in p.roads)
                        if (DistanceTo(at, road) < MapLayout.DefenceRadius + 3f) ok = false;
                    if (ok) return at;
                    at += new Vector2(R(-12f, 12f), R(-12f, 12f));
                }
                return at;
            }

            // ---- Bridges: wherever a road crosses the river, put the crossing into both lines ----

            void Bridges()
            {
                if (p.river == null) return;
                var river = new List<Vector2>(p.river);
                for (int r = 0; r < p.roads.Count; r++)
                {
                    var road = new List<Vector2>(p.roads[r]);
                    for (int i = 0; i < road.Count - 1; i++)
                    {
                        for (int j = 0; j < river.Count - 1; j++)
                        {
                            if (!Intersect(road[i], road[i + 1], river[j], river[j + 1], out Vector2 x)) continue;
                            if (Mathf.Abs(x.x) > 225f || Mathf.Abs(x.y) > 225f) continue;
                            if (p.hasDam && Vector2.Distance(x, p.damPoint) < 60f) continue;
                            bool near = false;
                            foreach (var b in p.bridges) if (Vector2.Distance(b, x) < 25f) near = true;
                            road.Insert(i + 1, x);
                            river.Insert(j + 1, x);
                            if (!near) p.bridges.Add(x);
                            i++;
                            break;
                        }
                    }
                    p.roads[r] = road.ToArray();
                }
                p.river = river.ToArray();
            }

            static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 x)
            {
                x = Vector2.zero;
                Vector2 r = b - a, s = d - c;
                float denom = r.x * s.y - r.y * s.x;
                if (Mathf.Abs(denom) < 1e-5f) return false;
                Vector2 ca = c - a;
                float t = (ca.x * s.y - ca.y * s.x) / denom, u = (ca.x * r.y - ca.y * r.x) / denom;
                if (t <= 0.02f || t >= 0.98f || u <= 0.02f || u >= 0.98f) return false;
                x = a + r * t;
                return true;
            }

            // ---- Helpers ----

            bool OnRail(Vector2 q, float margin)
            {
                foreach (var rail in p.railways)
                    if (DistanceTo(q, rail) < MapLayout.RailHalfWidth + margin) return true;
                return false;
            }

            static Site MakeSite(string model, Vector2 at, float yaw, float scale, float flat) =>
                new Site { model = model, position = at, yaw = yaw, scale = scale, flat = flat };

            // The yaw (degrees) that turns a model's +Z to face along dir.
            static float Yaw(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

            static float DistanceTo(Vector2 q, IList<Vector2> line)
            {
                float best = float.MaxValue;
                for (int i = 0; i < line.Count - 1; i++)
                {
                    Vector2 a = line[i], ab = line[i + 1] - a;
                    float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
                    best = Mathf.Min(best, Vector2.Distance(q, a + ab * t));
                }
                return best;
            }
        }
    }
}

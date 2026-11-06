using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Cuts playable sectors from the persistent world, keeping the existing mission recipe and its real sizes.
    public static partial class WorldSectors
    {
        public const float Half = 1000f, Margin = 128f;

        // Generate the world before repeated calls. The cold-start generation is separate from the crop budget.
        // Varrow remains hand-made and is placed by its dam anchor by the caller, not by this procedural adapter.
        public static SectorPlan PlanAt(Vector2 worldCenter, SectorKind kindHint, int seed)
        {
            if (!WorldPlan.Ready) WorldPlan.Generate(World.WorldSeed);
            if (float.IsNaN(worldCenter.x) || float.IsNaN(worldCenter.y) || !Inside(worldCenter, WorldPlan.Half - Half))
                throw new ArgumentOutOfRangeException(nameof(worldCenter), "The entire sector must lie inside WorldPlan.");
            if (kindHint == SectorKind.VarrowValley) throw new ArgumentException("Use SectorPlan.Varrow and its dam anchor for Varrow Valley.", nameof(kindHint));
            var feature = LandmarkFor(worldCenter, kindHint);
            if (IsLandmarkKind(kindHint) && feature == null) kindHint = SectorKind.RollingFarmland;
            var plan = SectorIdentity.WorldRecipe(kindHint, seed);
            plan.world = new WorldSectorData(worldCenter, kindHint);
            plan.river = null; plan.bridges.Clear(); plan.roads.Clear(); plan.railways.Clear();
            plan.villages.Clear(); plan.villageNames.Clear(); plan.farmsteads.Clear(); plan.ponds.Clear();
            plan.hasDam = false; plan.lakeLobes = new (Vector2, float, float, float, float)[0]; plan.islands = new (Vector2, float)[0];
            plan.features &= ~(SectorFeature.Reservoir | SectorFeature.Rail | SectorFeature.RailYard);
            Rivers(plan); Roads(plan); Water(plan); Settlements(plan);
            plan.world.PrepareDryGround();
            if (plan.railways.Count > 0) plan.features |= SectorFeature.Rail;
            if (plan.world.lakes.Exists(l => l.reservoir)) plan.features |= SectorFeature.Reservoir;
            plan.forest = plan.world.forestShare; plan.fields = plan.world.fieldShare;
            // These are compatibility amplitudes only. A scalar recipe cannot reproduce directional relief;
            // Ground must use world.Height and the edge-faded detail described in the integration notes.
            plan.hills = Mathf.Clamp((plan.world.maximumHeight - plan.world.minimumHeight) / Scale.Distance, 0f, 30f);
            plan.ridges = 0f;
            if (plan.world.townShare > 0.35f) plan.kind = SectorKind.MarketTown;
            else if (plan.world.townShare > 0.02f) plan.kind = SectorKind.TownOutskirts;
            // Reserve all intersecting permanent features before placing mission pads and access tracks.
            if (feature != null) plan.kind = feature.SectorKind;
            bool builtKind = IsLandmarkKind(kindHint);
            if (builtKind) plan.sites.Clear();
            plan.features &= ~(SectorFeature.Airstrip | SectorFeature.RailYard);
            foreach (var f in plan.world.features)
            {
                var a = f.Area; a.center -= worldCenter;
                a.halfLength += f.clearance; a.halfWidth += f.clearance;
                plan.reservedFeatures.Add(a);
            }
            PlaceMission(plan);
            SectorIdentity.PlanWorld(plan);
            // Natural and town detail still use their existing recipes where there is no built compound.
            if (plan.world.features.Count == 0 && !builtKind)
            {
                try { SectorIdentity.Plan(plan); }
                catch (InvalidOperationException) { ClearLandmark(plan); }
            }
            return plan;
        }

        // Removes everything SectorIdentity added: its reserved areas and their buildings, yards, fences, streets,
        // levelled pads, glades and wetland.
        static void ClearLandmark(SectorPlan plan)
        {
            for (int i = plan.sites.Count - 1; i >= 0; i--)
                foreach (var area in plan.reservedFeatures)
                    if (area.Contains(plan.sites[i].position, 40f)) { plan.sites.RemoveAt(i); break; }
            plan.reservedFeatures.Clear();
            plan.featureSurfaces.Clear();
            plan.featureFences.Clear();
            plan.featureStreets.Clear();
            plan.featureFlats.Clear();
            plan.woodlandGlades.Clear();
            plan.wetland = null;
        }

        // Settlement centres retain their names; a town extending into the square contributes occupied blocks
        // sampled from its actual cover, even when the town centre is outside. Farms keep their exact world point.
        static void Settlements(SectorPlan plan)
        {
            var data = plan.world;
            foreach (var source in WorldPlan.Settlements)
            {
                Vector2 c = source.center - data.center;
                if (!Inside(c, Half + source.radius * 1.5f)) continue;
                data.settlements.Add(new WorldSettlement { center = c, name = source.name, radius = source.radius, town = source.town });
                if (Inside(c, Half)) AddVillage(plan, c, source.name, false);
                if (!source.town) continue;
                // World-aligned blocks give adjacent crops the same town edge without inventing a new town grid.
                float x0 = (float)Math.Ceiling((data.center.x - 900f) / 180f) * 180f;
                float z0 = (float)Math.Ceiling((data.center.y - 900f) / 180f) * 180f;
                for (float wz = z0; wz <= data.center.y + 900f; wz += 180f)
                    for (float wx = x0; wx <= data.center.x + 900f; wx += 180f)
                    {
                        var q = new Vector2(wx, wz) - data.center;
                        if (data.CoverAt(q.x, q.y) != Cover.Town || Vector2.Distance(q, c) > source.radius * 1.5f) continue;
                        bool near = false;
                        foreach (var v in plan.villages) if (Vector2.Distance(q, v) < 155f) { near = true; break; }
                        if (!near) AddVillage(plan, q, source.name, false);
                    }
                if (Inside(c, Half)) plan.name = source.name;
            }
            for (int i = 0; i < WorldPlan.Farms.Count; i++)
            {
                Vector2 c = WorldPlan.Farms[i].position - data.center;
                if (!Inside(c, Half)) continue;
                var rng = new System.Random(unchecked(data.seed * 7919 + i * 104729));
                AddVillage(plan, c, PlaceNames.Place(rng) + " Farm", true);
            }
        }

        // Keep the three parallel village lists aligned for MapLayout and Situations.
        static void AddVillage(SectorPlan plan, Vector2 point, string name, bool farm)
        {
            plan.villages.Add(point); plan.villageNames.Add(name); plan.farmsteads.Add(farm);
        }

        // Preserve the recipe's compound and defence identities, moving them to dry, modestly sloping ground.
        // HQ-ring offsets stay rigid where possible; only an unsafe outlying post or feature changes its offset.
        static void PlaceMission(SectorPlan plan)
        {
            Vector2 oldBase = Flat(plan.basePosition), oldHq = Flat(plan.enemyHqPosition);
            var occupied = new List<(Vector2 point, float radius)>();
            Vector2 hq = FindPad(plan, oldHq, 38f, 450f, occupied, null, default, 0f);
            occupied.Add((hq, 38f));
            var access = new Access(plan.world, hq);
            Vector2 basePos;
            try { basePos = FindPad(plan, oldBase, 36f, 450f, occupied, access, hq, 350f); }
            // A wet central island may leave usable, connected ground only nearer the edge of this same window.
            catch (InvalidOperationException) { basePos = FindPad(plan, oldBase, 36f, 750f, occupied, access, hq, 350f); }
            occupied.Add((basePos, 36f));
            plan.basePosition = V3(basePos); plan.enemyHqPosition = V3(hq);
            // FromOld uses these only for legacy compound-relative offsets; procedural missions use local positions.
            plan.oldBase = basePos / Scale.Distance; plan.oldHq = hq / Scale.Distance;
            Vector2 hqMove = hq - oldHq, middleMove = (basePos - oldBase + hqMove) / 2f;
            plan.enemyArea = new Rect(plan.enemyArea.x + hqMove.x, plan.enemyArea.y + hqMove.y, plan.enemyArea.width, plan.enemyArea.height);
            for (int i = 0; i < plan.defences.Count; i++)
            {
                var defence = plan.defences[i]; Vector2 old = Flat(defence.position);
                Vector2 p = old + (Vector2.Distance(old, oldHq) < 60f ? hqMove : middleMove);
                if (!PadFits(plan, p, 8f, 850f)) p = FindPad(plan, p, 8f, 750f, occupied, null, default, 0f);
                plan.defences[i] = (defence.type, V3(p)); occupied.Add((p, 8f));
            }
            for (int i = 0; i < plan.sites.Count; i++)
            {
                var site = plan.sites[i]; Vector2 desired = site.position + middleMove;
                float radius = Mathf.Max(5f, site.flat);
                site.position = FindPad(plan, desired, radius, 850f - radius, occupied, null, default, 0f);
                plan.sites[i] = site; occupied.Add((site.position, radius));
            }
            // Local feature footprints still belong to the mission recipe; never keep a fictitious railway route.
            for (int i = 0; i < plan.clearings.Count; i++)
                plan.clearings[i] = FindPad(plan, plan.clearings[i] + middleMove, 30f, 800f, occupied, null, default, 0f);
            for (int i = plan.orchards.Count - 1; i >= 0; i--)
            {
                var field = plan.orchards[i]; field.center += middleMove;
                if (!PadFits(plan, field.center, Mathf.Max(field.halfLength, field.halfWidth) * 1.5f, 850f)) plan.orchards.RemoveAt(i);
                else plan.orchards[i] = field;
            }
            // A quarry and airstrip retain their shape; move the full footprint instead of testing just its centre.
            if (plan.Has(SectorFeature.Quarry))
                plan.quarry = FindPad(plan, plan.quarry + middleMove, plan.quarryRadius + 10f, 800f - plan.quarryRadius, occupied, null, default, 0f);
            if (plan.Has(SectorFeature.Airstrip)) PlaceAirstrip(plan, middleMove, occupied);
            var route = access.Join(basePos, hq, true);
            if (route == null) throw new InvalidOperationException("No dry base-to-HQ route in this world square.");
            plan.roads.Insert(0, route);
            access.ConnectWorld(plan);
        }

        // Search bounded, world-relative candidates in a fixed order; no global RNG or previous crop affects it.
        static Vector2 FindPad(SectorPlan plan, Vector2 desired, float radius, float reach,
            List<(Vector2 point, float radius)> occupied, Access access, Vector2 away, float separation)
        {
            float best = float.MaxValue; Vector2 chosen = default;
            if (Vector2.Distance(desired, away) >= separation && PadFits(plan, desired, radius, reach))
            {
                bool clear = true;
                foreach (var used in occupied)
                    if (Vector2.Distance(desired, used.point) < radius + used.radius + 12f) { clear = false; break; }
                if (clear && (access == null || access.Reaches(desired))) return desired;
            }
            for (float z = -reach; z <= reach; z += 25f)
                for (float x = -reach; x <= reach; x += 25f)
                {
                    var p = new Vector2(x, z);
                    float score = (p - desired).sqrMagnitude;
                    if (score >= best || Vector2.Distance(p, away) < separation || !PadFits(plan, p, radius, reach)) continue;
                    bool clear = true;
                    foreach (var used in occupied)
                        if (Vector2.Distance(p, used.point) < radius + used.radius + 12f) { clear = false; break; }
                    if (!clear || access != null && !access.Reaches(p)) continue;
                    float h = plan.world.Height(x, z);
                    float relief = Mathf.Abs(h - plan.world.Height(x + radius, z)) + Mathf.Abs(h - plan.world.Height(x, z + radius));
                    score += relief * relief * 400f;
                    if (score < best) { best = score; chosen = p; }
                }
            if (best == float.MaxValue) throw new InvalidOperationException("World square has no suitable dry ground for the mission footprint at " + desired.x + ", " + desired.y + ".");
            return chosen;
        }

        // Check the pad and its blending apron against real water, cover and local relief, not just its centre.
        static bool PadFits(SectorPlan plan, Vector2 p, float radius, float reach)
        {
            var data = plan.world;
            if (data.FeatureReserved(p, radius + 20f)) return false;
            if (!Inside(p, Mathf.Min(reach, Half - radius - 30f)) || data.DryDistance(p) < radius + 16f) return false;
            foreach (var v in plan.villages) if (Vector2.Distance(v, p) < MapLayout.VillageReach + radius + 12f) return false;
            foreach (var road in data.roads)
                for (int i = 1; i < road.points.Length; i++)
                    if (Vector2.Distance(p, Vector2.Lerp(road.points[i - 1], road.points[i], Projection(p, road.points[i - 1], road.points[i]))) < radius + 4f) return false;
            float lo = data.Height(p.x, p.y), hi = lo;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius + 10f);
                if (data.DryDistance(q) < 5f) return false;
                float h = data.Height(q.x, q.y); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
            }
            return hi - lo < Mathf.Max(4f, radius * 0.3f);
        }

        // Airstrip missions require their full runway to fit, with the hangar translated by the same offset.
        static void PlaceAirstrip(SectorPlan plan, Vector2 shift, List<(Vector2 point, float radius)> occupied)
        {
            Vector2 original = plan.airstripCenter, desired = original + shift, best = default;
            float score = float.MaxValue;
            for (float z = -650f; z <= 650f; z += 50f)
                for (float x = -650f; x <= 650f; x += 50f)
                {
                    Vector2 c = new Vector2(x, z); float d = (c - desired).sqrMagnitude;
                    if (d >= score) continue;
                    bool fits = true;
                    for (float t = -plan.airstripLength / 2f; t <= plan.airstripLength / 2f + 1f; t += 25f)
                    {
                        Vector2 q = c + plan.airstripAlong * t;
                        if (!PadFits(plan, q, 23f, 900f)) { fits = false; break; }
                        if (Vector2.Distance(q, Flat(plan.basePosition)) < 75f || Vector2.Distance(q, Flat(plan.enemyHqPosition)) < 75f) { fits = false; break; }
                    }
                    if (fits) { best = c; score = d; }
                }
            if (score == float.MaxValue) throw new InvalidOperationException("World square cannot fit this airstrip mission.");
            plan.airstripCenter = best;
            // The generated hangar remains nearby; its final pad is checked independently of the long runway.
            for (int i = 0; i < plan.sites.Count; i++)
                if (plan.sites[i].model == "Base_Hangar")
                {
                    var site = plan.sites[i];
                    site.position = FindPad(plan, site.position + best - desired, site.flat, 850f, occupied, null, default, 0f);
                    plan.sites[i] = site;
                }
        }

        // Map and mission code use X/Z while the plan's horizontal geometry uses Vector2.
        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        // Compounds acquire their absolute Y from Ground when built, just as recipe-generated compounds do.
        static Vector3 V3(Vector2 p) => new Vector3(p.x, 0f, p.y);
    }
}

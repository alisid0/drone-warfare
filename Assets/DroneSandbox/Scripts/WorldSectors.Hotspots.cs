using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Campaign locations are allocated as a set, so opening hotspots in a different order cannot move them.
    public static partial class WorldSectors
    {
        // Assign the current roster in a stable order, reserving complete 2 km squares, including hand-made Varrow.
        public static Vector2 SpotFor(Hotspot hotspot)
        {
            if (hotspot == null) throw new ArgumentNullException(nameof(hotspot));
            if (!WorldPlan.Ready) WorldPlan.Generate(World.WorldSeed);
            var roster = new List<Hotspot>(Theatre.Hotspots);
            bool present = false;
            foreach (var spot in roster) if (SameSpot(spot, hotspot)) { present = true; break; }
            if (!present) roster.Add(hotspot);
            roster.Sort(CompareSpots);
            var used = new List<Vector2>();
            if (WorldPlan.Dams.Count > 0)
            {
                var dam = WorldPlan.Dams[0];
                used.Add((dam.a + dam.b) / 2f - SectorPlan.Varrow().damPoint);
            }
            foreach (var spot in roster)
            {
                Vector2 position = ChooseSpot(spot, used);
                used.Add(position);
                if (SameSpot(spot, hotspot)) return position;
            }
            throw new InvalidOperationException("Hotspot was not in its own allocation roster.");
        }

        // Identity uses saved hotspot fields, not object references or a randomized string hash.
        static bool SameSpot(Hotspot a, Hotspot b) => a.kind == b.kind && a.seed == b.seed && a.final == b.final
            && a.mapPosition.x == b.mapPosition.x && a.mapPosition.y == b.mapPosition.y;

        // North-to-south priority and explicit tie breakers make allocation independent of caller/list order.
        static int CompareSpots(Hotspot a, Hotspot b)
        {
            int order = a.mapPosition.x.CompareTo(b.mapPosition.x);
            if (order == 0) order = a.mapPosition.y.CompareTo(b.mapPosition.y);
            if (order == 0) order = a.kind.CompareTo(b.kind);
            if (order == 0) order = a.seed.CompareTo(b.seed);
            if (order == 0) order = a.final.CompareTo(b.final);
            return order;
        }

        // Prefer matching cells, then explicit related kinds. Within the best available kind, minimize map
        // displacement and distance from the intended front depth; a rare airfield must still be an airfield.
        static Vector2 ChooseSpot(Hotspot spot, List<Vector2> used)
        {
            float z = Mathf.Lerp(WorldPlan.Half - 6000f, -WorldPlan.Half + 6000f, Mathf.Clamp01(spot.mapPosition.x));
            // Subtract the theatre's shaped baseline, not 0.5. The saved map position already includes advance;
            // removing today's Advance from today's Front prevents an existing hotspot drifting each victory.
            float baseline = Theatre.Front(spot.mapPosition.x) - Theatre.Advance;
            float depth = (spot.mapPosition.y - baseline) * 8000f - 2000f;
            var target = new Vector2(WorldPlan.FrontAt(z).x + depth, z);
            if (IsLandmarkKind(spot.kind) && FeatureSpot(spot.kind, target, used, out Vector2 featureSpot)) return featureSpot;
            Vector2 best = default; float score = float.MaxValue, bestKind = float.MaxValue;
            for (int cz = 1; cz < WorldPlan.CellCount - 1; cz++)
                for (int cx = 1; cx < WorldPlan.CellCount - 1; cx++)
                {
                    WorldCell cell = WorldPlan.Cell(cx, cz);
                    Vector2 p = new Vector2(-WorldPlan.Half + (cx + 0.5f) * WorldPlan.CellSize,
                        -WorldPlan.Half + (cz + 0.5f) * WorldPlan.CellSize);
                    if (cell.water > 0.55f || LandmarkWindow(p)) continue;
                    bool occupied = false;
                    foreach (var other in used)
                        if (Mathf.Abs(p.x - other.x) < 2f * Half && Mathf.Abs(p.y - other.y) < 2f * Half) { occupied = true; break; }
                    if (occupied) continue;
                    float kind = KindPenalty(spot.kind, cell.kind);
                    if (kind > bestKind) continue;
                    float offFront = Mathf.Max(0f, Mathf.Abs(p.x - WorldPlan.FrontAt(p.y).x - depth) - 6000f);
                    float cost = Vector2.Distance(p, target) + offFront * 1.5f;
                    if (kind == bestKind && cost >= score || !HasMissionGround(p)) continue;
                    bestKind = kind; score = cost; best = p;
                }
            if (score == float.MaxValue) throw new InvalidOperationException("No unoccupied world square remains for this campaign.");
            return new Vector2(Mathf.RoundToInt(best.x / 250f) * 250f, Mathf.RoundToInt(best.y / 250f) * 250f);
        }

        // Reject inundated mission cores before reserving a square; detailed footprint checks remain in PlanAt.
        static bool HasMissionGround(Vector2 center)
        {
            int dry = 0;
            for (float z = -400f; z <= 400f; z += 200f)
                for (float x = -400f; x <= 400f; x += 200f)
                {
                    Cover c = WorldPlan.CoverAt(center.x + x, center.y + z);
                    if (c != Cover.Water && c != Cover.Wetland && c != Cover.Town && c != Cover.Village) dry++;
                }
            return dry >= 8;
        }

        // Some mission themes are not cell classes (industry, orchard, quarry); map them to their closest setting.
        static float KindPenalty(SectorKind hint, SectorKind actual)
        {
            if (hint == actual) return 0f;
            switch (hint)
            {
                case SectorKind.VarrowValley: case SectorKind.Reservoir:
                    if (actual == SectorKind.Reservoir) return 0f;
                    break;
                case SectorKind.IndustrialZone: case SectorKind.RailJunction:
                    if (actual == SectorKind.RailLine) return 1000f;
                    break;
                case SectorKind.PowerStation:
                    if (actual == SectorKind.RiverCrossing || actual == SectorKind.RiverValley) return 1000f;
                    break;
                case SectorKind.QuarryHills:
                    if (actual == SectorKind.HillCountry || actual == SectorKind.RidgePass) return 1000f;
                    break;
                case SectorKind.OrchardCountry: case SectorKind.Steppe:
                    if (actual == SectorKind.RollingFarmland || actual == SectorKind.VillageCluster) return 1000f;
                    break;
                case SectorKind.LoggingCamp:
                    if (actual == SectorKind.DeepForest || actual == SectorKind.ForestEdge) return 1000f;
                    break;
                case SectorKind.Lakeshore:
                    if (actual == SectorKind.LakeDistrict || actual == SectorKind.Reservoir) return 1000f;
                    break;
                case SectorKind.Wetlands:
                    if (actual == SectorKind.Marshland) return 1000f;
                    break;
            }
            if (Family(hint) == Family(actual)) return 3500f;
            return 18000f;
        }

        // Related land-use/relief families provide an explicit fallback when the requested kind is absent nearby.
        static int Family(SectorKind kind)
        {
            switch (kind)
            {
                case SectorKind.RiverValley: case SectorKind.RiverCrossing: case SectorKind.PowerStation: return 1;
                case SectorKind.ForestEdge: case SectorKind.DeepForest: case SectorKind.BirchWoods: case SectorKind.LoggingCamp: return 2;
                case SectorKind.RollingFarmland: case SectorKind.Steppe: case SectorKind.OrchardCountry: case SectorKind.VillageCluster: case SectorKind.CrossroadsHamlet: return 3;
                case SectorKind.HillCountry: case SectorKind.RidgePass: case SectorKind.QuarryHills: case SectorKind.Moorland: return 4;
                case SectorKind.LakeDistrict: case SectorKind.Lakeshore: case SectorKind.Reservoir: case SectorKind.VarrowValley: return 5;
                case SectorKind.Marshland: case SectorKind.Wetlands: return 6;
                case SectorKind.MarketTown: case SectorKind.TownOutskirts: case SectorKind.IndustrialZone: return 7;
                case SectorKind.RailLine: case SectorKind.RailJunction: return 8;
                default: return 100 + (int)kind;
            }
        }
    }
}

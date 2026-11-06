using System;
using UnityEngine;

namespace DroneSandbox
{
    // Read-only diagnostics for Claude's AutoTest hook and the production-source validation harness.
    public static class LandmarkCheck
    {
        // Return one actionable log line; no scene load, regeneration or hidden mutation is performed.
        public static string Run(SectorPlan sector = null)
        {
            if (!WorldPlan.Ready) return "LANDMARK FAIL: world is not ready";
            int samples = 0, buildings = 0;
            foreach (var f in WorldPlan.Features)
            {
                if (!f.valid || f.buildings.Count == 0 || f.accessRoad < 0 || f.accessRoad >= WorldPlan.Roads.Count)
                    return "LANDMARK FAIL: incomplete feature " + f.id;
                var access = WorldPlan.Roads[f.accessRoad];
                if (!Connected(access.points[access.points.Count - 1], f.accessRoad)) return "LANDMARK FAIL: road link " + f.id;
                if (f.kind == WorldFeatureKind.RailJunction)
                {
                    if (f.rails.Count < 3) return "LANDMARK FAIL: sidings " + f.id;
                    foreach (var line in f.rails)
                        if (LineDistance(line[0], WorldPlan.Railways[0].points) > 0.05f) return "LANDMARK FAIL: rail link " + f.id;
                }
                int nu = Mathf.CeilToInt((f.halfLength + f.clearance) * 2f / 24f), nv = Mathf.CeilToInt((f.halfWidth + f.clearance) * 2f / 24f);
                for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++)
                {
                    Vector2 p = f.Area.Point(Mathf.Lerp(-f.halfLength - f.clearance, f.halfLength + f.clearance, i / (float)nu),
                        Mathf.Lerp(-f.halfWidth - f.clearance, f.halfWidth + f.clearance, j / (float)nv));
                    samples++;
                    if (WorldPlan.NearWater(p, 12f)) return "LANDMARK FAIL: wet footprint " + f.id;
                    Cover c = WorldPlan.CoverAt(p.x, p.y);
                    if (c == Cover.Forest || c == Cover.Farmland || c == Cover.Town || c == Cover.Village) return "LANDMARK FAIL: occupied cover " + f.id;
                    if (f.Area.Contains(p) && Mathf.Abs(WorldPlan.Height(p.x, p.y) - f.elevation) > 0.01f) return "LANDMARK FAIL: ungraded footprint " + f.id;
                    if (!WorldPlan.FeatureReserved(p, 0.1f)) return "LANDMARK FAIL: spatial index " + f.id;
                    if (ForestPattern.Edge(p.x, p.y) > 0f) return "LANDMARK FAIL: forest encroachment " + f.id;
                }
                foreach (var farm in WorldPlan.Farms) if (f.Area.Contains(farm.position, f.clearance + 180f)) return "LANDMARK FAIL: farm clearance " + f.id;
                foreach (var b in f.buildings)
                {
                    buildings++;
                    if (!f.Area.Contains(b.position)) return "LANDMARK FAIL: building outside footprint " + f.id;
                }
            }
            if (sector != null && sector.world != null)
            {
                var data = sector.world;
                if (data.FeatureReserved(new Vector2(sector.basePosition.x, sector.basePosition.z), 36f)
                    || data.FeatureReserved(new Vector2(sector.enemyHqPosition.x, sector.enemyHqPosition.z), 38f)) return "LANDMARK FAIL: mission compound in reserve";
                bool identity = !WorldSectors.IsLandmarkKind(sector.kind);
                foreach (var f in data.features)
                {
                    if (f.SectorKind == sector.kind && f.valid && f.Fits(data.center, WorldSectors.Half - 32f)) identity = true;
                    foreach (var b in f.buildings)
                    {
                        Vector2 at = b.position - data.center;
                        if (Mathf.Abs(at.x) > 995f || Mathf.Abs(at.y) > 995f) continue;
                        bool found = false;
                        foreach (var site in sector.sites)
                            if (site.model == b.model && Vector2.Distance(site.position, at) < 0.01f
                                && Mathf.Abs(site.yaw - Mathf.Atan2(b.facing.x, b.facing.y) * Mathf.Rad2Deg) < 0.01f) { found = true; break; }
                        if (!found) return "LANDMARK FAIL: moved or missing building " + f.id;
                    }
                }
                if (!identity) return "LANDMARK FAIL: empty " + sector.kind;
            }
            return "LANDMARK PASS: seed " + WorldPlan.Seed + ", " + WorldPlan.Features.Count + " features, " + buildings + " buildings, " + samples + " dry/graded/clear samples";
        }

        // The access endpoint must meet an earlier road, which recursively leads to the original network.
        static bool Connected(Vector2 p, int road)
        {
            for (int i = 0; i < road; i++) if (LineDistance(p, WorldPlan.Roads[i].points) < 0.05f) return true;
            return false;
        }

        // Project onto whole segments so a valid junction between vertices is not reported as disconnected.
        static float LineDistance(Vector2 p, System.Collections.Generic.IList<Vector2> points)
        {
            float distance = float.MaxValue;
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = points[i - 1], d = points[i] - a;
                float t = d.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude) : 0f;
                distance = Mathf.Min(distance, Vector2.Distance(p, a + d * t));
            }
            return distance;
        }
    }
}

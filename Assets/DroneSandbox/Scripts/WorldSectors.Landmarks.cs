using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Built sector identities can only be assigned by a permanent, validated world feature.
    public static partial class WorldSectors
    {
        // Keep the identity test shared by explicit PlanAt requests and campaign hotspot allocation.
        public static bool IsLandmarkKind(SectorKind kind) => kind == SectorKind.IndustrialZone
            || kind == SectorKind.Airfield || kind == SectorKind.RailJunction;

        // A partial edge crop may show a neighbour's buildings but cannot claim its mission identity.
        static WorldFeature LandmarkFor(Vector2 center, SectorKind kind)
        {
            foreach (var f in WorldPlan.Features)
                if (f.valid && f.SectorKind == kind && f.Fits(center, Half - 32f)) return f;
            return null;
        }

        // Reserve rare-feature windows from generic hotspots, even when those allocate first in map order.
        static bool LandmarkWindow(Vector2 point)
        {
            foreach (var f in WorldPlan.Features)
                if (Mathf.Abs(point.x - f.center.x) < 2f * Half + 300f && Mathf.Abs(point.y - f.center.y) < 2f * Half + 300f) return true;
            return false;
        }

        // Search a few whole-footprint windows, keeping enough usable ground for both mission compounds.
        static bool FeatureSpot(SectorKind kind, Vector2 target, List<Vector2> used, out Vector2 chosen)
        {
            chosen = default; float best = float.MaxValue;
            foreach (var f in WorldPlan.Features)
            {
                if (!f.valid || f.SectorKind != kind) continue;
                for (int shift = 0; shift < 5; shift++)
                {
                    Vector2 offset = shift == 0 ? Vector2.zero : shift == 1 ? new Vector2(250f, 0f)
                        : shift == 2 ? new Vector2(-250f, 0f) : shift == 3 ? new Vector2(0f, 250f) : new Vector2(0f, -250f);
                    Vector2 p = f.center + offset;
                    if (!f.Fits(p, Half - 32f) || !Inside(p, WorldPlan.Half - Half)) continue;
                    bool occupied = false;
                    foreach (var other in used)
                        if (Mathf.Abs(p.x - other.x) < 2f * Half && Mathf.Abs(p.y - other.y) < 2f * Half) { occupied = true; break; }
                    if (occupied) continue;
                    float cost = Vector2.Distance(p, target) + offset.magnitude * 2f;
                    if (cost >= best || !FeatureMissionGround(p)) continue;
                    chosen = p; best = cost;
                }
            }
            return best < float.MaxValue;
        }

        // Conservative 100 m pads separated by at least 350 m reject cramped, flooded and town-filled windows.
        static bool FeatureMissionGround(Vector2 center)
        {
            var pads = new List<Vector2>();
            for (float z = -400f; z <= 400f; z += 200f) for (float x = -400f; x <= 400f; x += 200f)
            {
                Vector2 p = center + new Vector2(x, z);
                if (WorldPlan.FeatureReserved(p, 65f) || WorldPlan.NearWater(p, 95f)) continue;
                Cover c = WorldPlan.CoverAt(p.x, p.y);
                if (c == Cover.Town || c == Cover.Village || c == Cover.Wetland) continue;
                bool settled = false;
                foreach (var s in WorldPlan.Settlements) if (Vector2.Distance(p, s.center) < s.radius + 180f) settled = true;
                if (settled) continue;
                foreach (var q in pads) if (Vector2.Distance(q, p) >= 350f) return true;
                pads.Add(p);
            }
            return false;
        }
    }
}

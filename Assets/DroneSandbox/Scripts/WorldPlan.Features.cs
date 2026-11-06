using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Permanent built land uses; natural forest and marsh continue to come from the world's cover.
    public enum WorldFeatureKind : byte { IndustrialZone, Airfield, RailJunction }

    // One immutable-after-generation compound, including the layout shared by local detail and distant meshes.
    public sealed class WorldFeature
    {
        public int id, variantSeed, accessRoad;
        public WorldFeatureKind kind;
        public Vector2 center, along;
        public float halfLength, halfWidth, clearance, elevation;
        public bool norvane, valid;
        public readonly List<LandmarkBuilding> buildings = new List<LandmarkBuilding>();
        public readonly List<LandmarkSurface> surfaces = new List<LandmarkSurface>();
        public readonly List<Vector2[]> rails = new List<Vector2[]>();
        public FeatureArea Area => new FeatureArea { center = center, along = along, halfLength = halfLength, halfWidth = halfWidth };
        public SectorKind SectorKind => kind == WorldFeatureKind.Airfield ? SectorKind.Airfield
            : kind == WorldFeatureKind.RailJunction ? SectorKind.RailJunction : SectorKind.IndustrialZone;

        // The full footprint, including its reserve, must fit before a window can claim this identity.
        public bool Fits(Vector2 origin, float half)
        {
            Vector2 c = center - origin;
            float u = halfLength + clearance, v = halfWidth + clearance;
            return Math.Abs(c.x) + Math.Abs(along.x) * u + Math.Abs(along.y) * v <= half
                && Math.Abs(c.y) + Math.Abs(along.y) * u + Math.Abs(along.x) * v <= half;
        }
    }

    // World land use is installed before farming and cover; readers only touch the finished spatial bins.
    public static partial class WorldPlan
    {
        public static readonly List<WorldFeature> Features = new List<WorldFeature>();
        static List<WorldFeature>[] featureBins;
        static Rect[] featureLakeBounds;
        static float featureRiverWidth;
        public static double FeaturePlanMilliseconds { get; private set; }

        // Return the few candidates in this kilometre bin without allocating or scanning the world list.
        public static IReadOnlyList<WorldFeature> FeaturesNear(float x, float z)
            => featureBins == null ? Array.Empty<WorldFeature>() : featureBins[CellIndex(new Vector2(x, z))] ?? (IReadOnlyList<WorldFeature>)Array.Empty<WorldFeature>();

        // Exact oriented reservations also accept extra clearance for a farm, tree crown or mission pad.
        public static bool FeatureReserved(Vector2 point, float margin = 0f)
        {
            // Farm margins can cross a bin boundary; the bins already include a 300 m query guard.
            var nearby = FeaturesNear(point.x, point.y);
            for (int i = 0; i < nearby.Count; i++)
            {
                var f = nearby[i];
                if (f.Area.Contains(point, f.clearance + margin)) return true;
            }
            return false;
        }

        // Colours are sRGB, like the existing terrain palette; callers convert to linear only when needed.
        public static bool FeatureSurfaceAt(float x, float z, out Color color)
        {
            var p = new Vector2(x, z);
            var nearby = FeaturesNear(x, z);
            for (int index = 0; index < nearby.Count; index++)
            {
                var f = nearby[index];
                if (!f.Area.Contains(p, f.clearance)) continue;
                for (int i = f.surfaces.Count - 1; i >= 0; i--)
                    if (f.surfaces[i].area.Contains(p)) { color = WorldLandmarkLayout.SurfaceColor(f.surfaces[i].kind); return true; }
            }
            color = default; return false;
        }

        // Exact vectors catch sub-grid streams; the raster cheaply rejects almost all inland queries.
        public static bool NearWater(Vector2 p, float margin)
        {
            if (!float.IsNaN(WaterLevel(p.x, p.y))) return true;
            if (RiverDistance(p.x, p.y) < margin + featureRiverWidth + 48f)
                foreach (var river in Rivers)
                    for (int i = 1; i < river.points.Count; i++)
                    {
                        Vector2 a = river.points[i - 1], b = river.points[i];
                        float guard = margin + Math.Max(river.halfWidths[i - 1], river.halfWidths[i]);
                        if (p.x < Math.Min(a.x, b.x) - guard || p.x > Math.Max(a.x, b.x) + guard
                            || p.y < Math.Min(a.y, b.y) - guard || p.y > Math.Max(a.y, b.y) + guard) continue;
                        float t = Projection(p, a, b);
                        if (Distance(p, Mix(a, b, t)) <= margin + Lerp(river.halfWidths[i - 1], river.halfWidths[i], t)) return true;
                    }
            for (int li = 0; li < Lakes.Count; li++)
            {
                var bounds = featureLakeBounds[li];
                if (p.x < bounds.xMin - margin || p.x > bounds.xMax + margin || p.y < bounds.yMin - margin || p.y > bounds.yMax + margin) continue;
                var lake = Lakes[li];
                if (InPolygon(p, lake.shore)) return true;
                for (int i = 0; i < lake.shore.Count; i++)
                {
                    Vector2 a = lake.shore[i], b = lake.shore[(i + 1) % lake.shore.Count];
                    if (Distance(p, Mix(a, b, Projection(p, a, b))) <= margin) return true;
                }
            }
            return false;
        }

        // Separate randomness keeps farms and the existing geography independent of feature search retries.
        static void PlanFeatures()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Features.Clear(); featureBins = new List<WorldFeature>[CellCount * CellCount];
            featureLakeBounds = new Rect[Lakes.Count]; featureRiverWidth = 0f;
            foreach (var river in Rivers) foreach (float width in river.halfWidths) featureRiverWidth = Math.Max(featureRiverWidth, width);
            for (int i = 0; i < Lakes.Count; i++)
            {
                float x0 = Half, z0 = Half, x1 = -Half, z1 = -Half;
                foreach (var q in Lakes[i].shore) { x0 = Math.Min(x0, q.x); z0 = Math.Min(z0, q.y); x1 = Math.Max(x1, q.x); z1 = Math.Max(z1, q.y); }
                featureLakeBounds[i] = new Rect(x0, z0, x1 - x0, z1 - z0);
            }
            var rng = new Random(unchecked(Seed * 1597334677 + 719));
            WorldSite airbase = Sites.Find(s => s.kind == WorldSiteKind.Airbase && s.norvane);
            // The pre-existing airbase spur ended at its centre; replace it with access to the apron gate.
            if (airbase != null)
                for (int i = Roads.Count - 1; i >= 0; i--)
                    if (Distance(Roads[i].points[0], airbase.position) < 1f) Roads.RemoveAt(i);
            for (int side = 0; side < 2; side++)
            {
                WorldFeature best = null; float score = float.MaxValue;
                for (int i = 0; i < (side == 0 ? 24 : 3200); i++)
                {
                    if (side == 1 && i >= 700 && best != null) break;
                    Vector2 p = side == 0 ? airbase.position : new Vector2(Range(rng, 11000f, 22000f), Range(rng, -21000f, 21000f));
                    if (side == 1 && i >= 700)
                    {
                        float z = Range(rng, -24000f, 24000f);
                        p = new Vector2(Range(rng, FrontAt(z).x + 1500f, 24500f), z);
                    }
                    float angle = side == 0 ? i * (float)Math.PI / 12f : Range(rng, 0f, 3.141593f);
                    var f = Candidate(WorldFeatureKind.Airfield, p, new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)), side + 1);
                    float cost = Grade(p.x, p.y) * 10000f + Distance(p, NearestRoad(p)) * 0.02f;
                    if (cost >= score || !FeatureGround(f, side == 0 ? airbase : null)) continue;
                    if (cost < score) { best = f; score = cost; }
                }
                InstallFeature(best);
            }
            // Junctions follow a real railway chord, well outside the towns and other feature windows.
            for (int n = 0; n < 4; n++)
            {
                WorldFeature best = null; float score = float.MaxValue;
                var rail = Railways[0];
                float targetZ = 21000f - n * 14000f;
                int nearest = 2;
                for (int i = 2; i < rail.points.Count - 2; i += 2)
                    if (Math.Abs(rail.points[i].y - targetZ) < Math.Abs(rail.points[nearest].y - targetZ)) nearest = i;
                for (int visit = 0; visit < rail.points.Count; visit++)
                {
                    int i = nearest + (visit % 2 == 0 ? visit : -visit - 1);
                    if (i < 2 || i >= rail.points.Count - 2) continue;
                    Vector2 p = rail.points[i], d = rail.points[i + 2] - rail.points[i - 2];
                    float length = Distance(d, Vector2.zero); if (length < 1f) continue;
                    d = new Vector2(d.x / length, d.y / length);
                    var f = Candidate(WorldFeatureKind.RailJunction, p + new Vector2(-d.y, d.x) * 62f, d, 20 + n);
                    float cost = Math.Abs(p.y - targetZ);
                    if (cost >= score || !FeatureGround(f, null)) continue;
                    // Mainline stays in the rail corridor, clear of the warehouses on the positive side.
                    bool fits = true;
                    for (int j = Math.Max(0, i - 5); j <= Math.Min(rail.points.Count - 1, i + 5); j++)
                    {
                        Vector2 q = rail.points[j] - f.center;
                        if (Math.Abs(q.x * d.x + q.y * d.y) < f.halfLength
                            && Math.Abs(q.x * -d.y + q.y * d.x + 62f) > 22f) { fits = false; break; }
                    }
                    if (fits) { best = f; score = cost; }
                }
                InstallFeature(best);
            }
            // Four works use the town/rail corridor without occupying the town's built-up land.
            for (int n = 0; n < 4; n++)
            {
                WorldFeature best = null; float score = float.MaxValue;
                Vector2 anchor = Settlements[n % 2].center;
                for (int i = 0; i < 650; i++)
                {
                    float angle = Range(rng, 0f, 6.283185f), radius = Range(rng, 1700f, 6500f);
                    Vector2 p = anchor + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                    Vector2 join = FeatureRailPoint(p, out Vector2 along);
                    if (Distance(p, join) > 2200f) continue;
                    var f = Candidate(WorldFeatureKind.IndustrialZone, p, along, 10 + n);
                    float cost = Distance(p, join) + radius * 0.35f;
                    if (cost >= score || !FeatureGround(f, null)) continue;
                    best = f; score = cost;
                }
                InstallFeature(best);
            }
            FeaturePlanMilliseconds = timer.Elapsed.TotalMilliseconds;
        }

        // Dimensions include real-sized working yards; a separate rim reserves grass and grading space.
        static WorldFeature Candidate(WorldFeatureKind kind, Vector2 p, Vector2 d, int id)
        {
            return new WorldFeature { id = id, kind = kind, center = p, along = d,
                halfLength = kind == WorldFeatureKind.Airfield ? 510f : kind == WorldFeatureKind.RailJunction ? 400f : 225f,
                halfWidth = kind == WorldFeatureKind.Airfield ? 170f : kind == WorldFeatureKind.RailJunction ? 100f : 175f,
                clearance = 64f, variantSeed = unchecked(Seed * 486187739 ^ id * 16777619), norvane = p.x < FrontAt(p.y).x };
        }

        // Dense dry samples include the entire grading rim and bound earthworks to eighteen metres of relief.
        static bool FeatureGround(WorldFeature f, WorldSite allowedSite)
        {
            if (!f.Fits(Vector2.zero, Half - 1600f)) return false;
            if (Grade(f.center.x, f.center.y) > 0.04f || NearWater(f.center, 100f)) return false;
            var a = f.Area;
            float radius = (float)Math.Sqrt(f.halfLength * f.halfLength + f.halfWidth * f.halfWidth) + f.clearance;
            if (!AwayFromSettlements(f.center, radius + 100f)) return false;
            foreach (var other in Features) if (Distance(other.center, f.center) < 3200f) return false;
            foreach (var dam in Dams) if (Distance((dam.a + dam.b) / 2f, f.center) < 3200f) return false;
            foreach (var site in Sites) if (site != allowedSite && Distance(site.position, f.center) < radius + 220f) return false;
            float low = float.MaxValue, high = float.MinValue, sum = 0f; int count = 0;
            int nu = (int)Math.Ceiling((f.halfLength + f.clearance) * 2f / 48f);
            int nv = (int)Math.Ceiling((f.halfWidth + f.clearance) * 2f / 48f);
            for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++)
            {
                Vector2 q = a.Point(Lerp(-f.halfLength - f.clearance, f.halfLength + f.clearance, i / (float)nu),
                    Lerp(-f.halfWidth - f.clearance, f.halfWidth + f.clearance, j / (float)nv));
                // 48 m samples with a 40 m guard cannot step over a stream or the corner of a lake.
                if (NearWater(q, 40f)) return false;
                float h = Height(q.x, q.y); low = Math.Min(low, h); high = Math.Max(high, h); sum += h; count++;
                if (high - low > 18f || Grade(q.x, q.y) > 0.065f) return false;
            }
            foreach (var road in Roads)
                for (int i = 1; i < road.points.Count; i++)
                    if (CrossesFeature(a, road.points[i - 1], road.points[i], 20f)) return false;
            if (f.kind != WorldFeatureKind.RailJunction)
                foreach (var rail in Railways)
                    for (int i = 1; i < rail.points.Count; i++)
                        if (CrossesFeature(a, rail.points[i - 1], rail.points[i], 16f)) return false;
            f.elevation = sum / count;
            return true;
        }

        // A conservative sampled segment test includes long route segments that have both endpoints outside.
        static bool CrossesFeature(FeatureArea a, Vector2 from, Vector2 to, float margin)
        {
            Vector2 q = Mix(from, to, Projection(a.center, from, to));
            float radius = a.halfLength + a.halfWidth + margin;
            if (Distance(q, a.center) > radius) return false;
            int count = Math.Max(1, (int)Math.Ceiling(Distance(from, to) / 20f));
            for (int i = 0; i <= count; i++) if (a.Contains(Mix(from, to, i / (float)count), margin + 10f)) return true;
            return false;
        }

        // Railway branches meet a projected mainline point exactly; layouts never invent an isolated rail line.
        internal static Vector2 FeatureRailPoint(Vector2 p, out Vector2 along)
        {
            float best = float.MaxValue; Vector2 point = p; along = new Vector2(1f, 0f);
            var rail = Railways[0];
            for (int i = 1; i < rail.points.Count; i++)
            {
                Vector2 a = rail.points[i - 1], b = rail.points[i], q = Mix(a, b, Projection(p, a, b));
                float d = Distance(p, q);
                if (d >= best) continue;
                best = d; point = q; float length = Distance(a, b);
                if (length > 0f) along = new Vector2((b.x - a.x) / length, (b.y - a.y) / length);
            }
            return point;
        }

        // Publish only validated compounds, with one real road link and all sidings in the permanent network.
        static void InstallFeature(WorldFeature f)
        {
            if (f == null) return; // callers select another kind if suitable geography is absent
            var a = f.Area;
            float u = f.halfLength + f.clearance + 32f, v = f.halfWidth + f.clearance + 32f;
            var ring = new[] { a.Point(0f, v), a.Point(u, v), a.Point(u, -v), a.Point(-u, -v), a.Point(-u, v) };
            Vector2 gate = a.Point(0f, f.halfWidth - 10f), join = default;
            int exit = 0; float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
            {
                Vector2 q = NearestRoad(ring[i]); float distance = Distance(q, ring[i]);
                if (distance < best) { best = distance; exit = i; join = q; }
            }
            f.accessRoad = Roads.Count;
            AddRoad(ring[exit], join, RoadClass.Road);
            // AddRoad deliberately ignores sub-two-metre links. The perimeter road still needs its gate.
            if (Roads.Count == f.accessRoad)
            {
                var road = new WorldRoad { kind = RoadClass.Road }; road.points.Add(join); Roads.Add(road);
            }
            var prefix = new List<Vector2> { gate, ring[0] };
            if (exit <= 2) for (int i = 1; i < exit; i++) prefix.Add(ring[i]);
            else for (int i = 4; i > exit; i--) prefix.Add(ring[i]);
            if (exit == 0) prefix.RemoveAt(prefix.Count - 1);
            Roads[f.accessRoad].points.InsertRange(0, prefix);
            WorldLandmarkLayout.Build(f);
            foreach (var line in f.rails)
            {
                var rail = new WorldRoad { kind = RoadClass.Road }; rail.points.AddRange(line); Railways.Add(rail);
            }
            f.valid = true; Features.Add(f);
            StampFeature(f, false);
            float reach = f.halfLength + f.halfWidth + f.clearance + 300f;
            int first = CellIndex(f.center - Vector2.one * reach), last = CellIndex(f.center + Vector2.one * reach);
            for (int z = first / CellCount; z <= last / CellCount; z++) for (int x = first % CellCount; x <= last % CellCount; x++)
            {
                int k = z * CellCount + x;
                if (featureBins[k] == null) featureBins[k] = new List<WorldFeature>();
                featureBins[k].Add(f);
            }
        }

        // Flat through the last bilinear sample under the footprint, then smoothstep into original terrain.
        static void StampFeature(WorldFeature f, bool landUse)
        {
            float reach = f.halfLength + f.halfWidth + f.clearance + 48f;
            for (int j = GridIndex(f.center.y - reach); j <= GridIndex(f.center.y + reach); j++)
                for (int i = GridIndex(f.center.x - reach); i <= GridIndex(f.center.x + reach); i++)
                {
                    Vector2 p = new Vector2(-Half + i * HeightStep, -Half + j * HeightStep), d = p - f.center;
                    float outside = Math.Max(Math.Abs(d.x * f.along.x + d.y * f.along.y) - f.halfLength,
                        Math.Abs(-d.x * f.along.y + d.y * f.along.x) - f.halfWidth);
                    int k = j * GridCount + i;
                    if (landUse) { if (outside <= f.clearance + 24f) cover[k] = Cover.Pasture; }
                    else if (outside < f.clearance + 48f)
                        heights[k] = Lerp(f.elevation, heights[k], Smooth((outside - 48f) / f.clearance));
                }
        }
    }
}

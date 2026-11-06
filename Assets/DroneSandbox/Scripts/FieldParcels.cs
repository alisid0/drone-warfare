using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum FieldBoundaryKind { Hedge, Ditch, Fence, Open }

    // One shared edge, in unwarped metres: both neighbouring parcels use this same line, including at T-junctions.
    public struct FieldBoundary
    {
        public Vector2 a, b;
        public FieldBoundaryKind kind;
        public int first, second;

        // Bends a boundary exactly as the crops on either side are bent.
        public Vector2 Point(float t)
        {
            Vector2 p = Vector2.Lerp(a, b, t);
            return p + FieldPlot.Warp(p);
        }
    }

    // Tiles farm districts before cutting out roads and settlements. Crops keep FieldPlot's fast lookup and mesh path.
    internal static class FieldParcels
    {
        const float Inset = 0.65f, MinimumArea = 500f;
        static readonly List<FieldPlot> lots = new List<FieldPlot>();
        internal static readonly List<FieldPlot> Districts = new List<FieldPlot>();
        static System.Func<Vector2, bool> blocked;
        static System.Random random;
        public static float DistrictCoverage { get; private set; }
        static float districtLand, parcelLand;

        // A continuous lattice of 300-500 m districts, sheared and tapered at their common corners. A district is
        // selected as a whole, so a low fields share leaves woods and marsh rather than thinning every farm.
        public static void Plan(System.Random rng, System.Func<Vector2, bool> isBlocked)
        {
            random = rng;
            var source = MapLayout.Plan.world;
            blocked = source == null ? isBlocked : p => isBlocked(p) || !FarmlandAt(p);
            lots.Clear();
            Districts.Clear();
            districtLand = parcelLand = 0f;
            DistrictCoverage = 0f;
            if (source != null)
            {
                DistrictCoverage = FarFieldSector.Plan(isBlocked);
                return;
            }
            Vector2 along = DominantDirection(), across = new Vector2(-along.y, along.x);
            const int count = 7;
            var rows = new float[count + 1];
            var corners = new float[count + 1, count + 1];
            rows[0] = -1400f;
            for (int i = 1; i <= count; i++) rows[i] = rows[i - 1] + Range(340f, 440f);
            for (int j = 0; j <= count; j++)
                for (int i = 0; i <= count; i++) corners[j, i] = -1400f + i * 400f + Range(-32f, 32f);
            float share = Mathf.Clamp01(MapLayout.Plan.fields / 0.8f);
            var districts = new List<(FieldPlot plot, float land, double order)>();
            float available = 0f;
            for (int j = 0; j < count; j++)
                for (int i = 0; i < count; i++)
                {
                    float u0 = rows[j], u1 = rows[j + 1];
                    float a0 = corners[j, i], a1 = corners[j, i + 1];
                    float b0 = corners[j + 1, i], b1 = corners[j + 1, i + 1];
                    var district = FromCorners(along, across, u0, u1, a0, a1, b0, b1);
                    float land = UsableArea(district);
                    if (land < 3000f) continue;
                    available += land;
                    districts.Add((district, land, rng.NextDouble()));
                }
            districts.Sort((a, b) => a.order.CompareTo(b.order));
            float chosen = 0f;
            foreach (var farm in districts)
            {
                if (chosen >= available * share) break;
                var district = farm.plot;
                // The last farm can be smaller, keeping small field shares from rounding up to a whole district.
                float fraction = Mathf.Clamp01((available * share - chosen) / farm.land);
                if (fraction < 0.75f) district = Slice(district, true, 0f, Mathf.Max(0.18f, fraction));
                float usable = UsableArea(district);
                Districts.Add(district);
                chosen += usable;
                int first = lots.Count;
                Divide(district, 0);
                Consolidate(first);
                districtLand += usable;
            }
            foreach (var lot in lots)
            {
                var crop = Trim(lot);
                if (Area(crop) < MinimumArea) continue;
                crop.crop = PickCrop(lot.center);
                MapLayout.Fields.Add(crop);
                parcelLand += Area(crop);
            }
            // Trim cannot drop a lot: Divide includes the inset in its minimum-area test.
            BuildBoundaries();
            DistrictCoverage = districtLand > 0f ? parcelLand / districtLand : 0f;
        }

        // Samples usable ground, not accepted crops, so district occupancy includes the meadow round obstacles.
        static float UsableArea(FieldPlot f)
        {
            int clear = 0;
            for (int v = 0; v < 30; v++)
                for (int u = 0; u < 30; u++)
                    if (!blocked(f.Point(f.halfLength * ((u + 0.5f) / 15f - 1f), f.halfWidth * ((v + 0.5f) / 15f - 1f)))) clear++;
            return Area(f) * clear / 900f;
        }

        const float TerrainCell = 16f;
        static int terrainCount;
        static bool[] steep;

        // Terrain is built AFTER layout, so Ground.SteepnessAt would read the previous sector. Predict broad slopes
        // from Ground.Hills' recipe on a coarse grid instead; sites, channels and shorelines have their own masks.
        public static void PrepareTerrain()
        {
            terrainCount = Mathf.CeilToInt(Ground.Size / TerrainCell) + 1;
            var height = new float[terrainCount * terrainCount];
            steep = new bool[height.Length];
            for (int z = 0; z < terrainCount; z++)
                for (int x = 0; x < terrainCount; x++) height[z * terrainCount + x] = PlanningHeight(x * TerrainCell - Ground.Size / 2f, z * TerrainCell - Ground.Size / 2f);
            for (int z = 0; z < terrainCount; z++)
                for (int x = 0; x < terrainCount; x++)
                {
                    int x0 = Mathf.Max(x - 1, 0), x1 = Mathf.Min(x + 1, terrainCount - 1), z0 = Mathf.Max(z - 1, 0), z1 = Mathf.Min(z + 1, terrainCount - 1);
                    float dx = (height[z * terrainCount + x1] - height[z * terrainCount + x0]) / ((x1 - x0) * TerrainCell);
                    float dz = (height[z1 * terrainCount + x] - height[z0 * terrainCount + x]) / ((z1 - z0) * TerrainCell);
                    steep[z * terrainCount + x] = dx * dx + dz * dz > 0.075f; // about 15 degrees, broad arable slope
                }
        }

        // Broad relief only: metre-scale roughness does not make an otherwise workable field into a hillside.
        static float PlanningHeight(float x, float z)
        {
            var p = MapLayout.Plan;
            if (p.world != null) return p.world.Height(x, z);
            const float s = Scale.Distance;
            float k = p.hills / 9f, nx = x / s + p.noiseOffset.x, nz = z / s + p.noiseOffset.y;
            float h = (Mathf.PerlinNoise(nx * 0.008f + 11.3f, nz * 0.008f + 3.7f) - 0.42f) * 9f * s * k;
            h += (Mathf.PerlinNoise(nx * 0.024f + 41.1f, nz * 0.024f + 7.9f) - 0.5f) * 3f * s * Mathf.Clamp(k, 0.4f, 1.6f);
            float edge = Mathf.InverseLerp(95f * s, 230f * s, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
            float valley = Mathf.SmoothStep(0f, 1f, (MapLayout.RiverField.Distance(x, z) - 20f * s) / (55f * s));
            return h + edge * edge * p.ridges * s * (0.7f + 0.6f * Mathf.PerlinNoise(nx * 0.015f, nz * 0.015f)) * valley;
        }

        // Cover is a nearest-sample world grid. A small guard also covers the gaps between parcel samples.
        static bool FarmlandAt(Vector2 p)
        {
            var source = MapLayout.Plan.world;
            if (source.CoverAt(p.x, p.y) != Cover.Farmland) return false;
            const float guard = 5f;
            return source.CoverAt(p.x - guard, p.y - guard) == Cover.Farmland
                && source.CoverAt(p.x + guard, p.y - guard) == Cover.Farmland
                && source.CoverAt(p.x - guard, p.y + guard) == Cover.Farmland
                && source.CoverAt(p.x + guard, p.y + guard) == Cover.Farmland;
        }

        // The mask is immutable during MapBlocked's parallel reads.
        public static bool Steep(Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x + Ground.Size / 2f) / TerrainCell), 0, terrainCount - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((p.y + Ground.Size / 2f) / TerrainCell), 0, terrainCount - 1);
            return steep[z * terrainCount + x];
        }

        // Follows the longest through-road; district corners supply local variation without breaking shared edges.
        static Vector2 DominantDirection()
        {
            Vector2 best = Vector2.up;
            float longest = 0f;
            foreach (var line in MapLayout.RoadLines)
            {
                Vector2 d = line[line.Count - 1] - line[0];
                if (d.sqrMagnitude <= longest) continue;
                longest = d.sqrMagnitude;
                best = d.normalized;
            }
            return MapLayout.Rotate(best, 0.12f);
        }

        // Encodes any trapezoid with parallel ends as the same four-sided strip the existing builders understand.
        static FieldPlot FromCorners(Vector2 along, Vector2 across, float u0, float u1, float a0, float a1, float b0, float b1)
        {
            float width = a1 - a0 + b1 - b0;
            return new FieldPlot
            {
                along = along, center = along * ((u0 + u1) * 0.5f) + across * ((a0 + a1 + b0 + b1) * 0.25f),
                halfLength = (u1 - u0) * 0.5f, halfWidth = width * 0.25f,
                taper = (b1 - b0 - a1 + a0) / width, shear = (b0 + b1 - a0 - a1) / (2f * (u1 - u0)),
            };
        }

        // Splits once along or across a strip, keeping the exact old outside edges and one common inside edge.
        static FieldPlot Slice(FieldPlot f, bool along, float low, float high)
        {
            if (along)
            {
                float u = f.halfLength * (low + high - 1f);
                float slope = f.halfWidth * f.taper / f.halfLength;
                f.center += (f.along + f.Across * f.shear) * u;
                f.halfWidth += slope * u;
                f.halfLength *= high - low;
                f.taper = slope * f.halfLength / f.halfWidth;
            }
            else
            {
                float v = f.halfWidth * (low + high - 1f);
                f.center += f.Across * v;
                f.shear += v * f.taper / f.halfLength;
                f.halfWidth *= high - low;
            }
            return f;
        }

        // Insets the same continuous edges by roughly half a metre on each side; taper and shear remain intact.
        static FieldPlot Trim(FieldPlot f)
        {
            float slope = f.halfWidth * f.taper / f.halfLength;
            f.halfLength -= Inset;
            f.halfWidth -= Inset;
            f.taper = slope * f.halfLength / f.halfWidth;
            return f;
        }

        // Typical plots stay 0.3-2 ha; near villages there are smaller paddocks. Obstructed plots are split further
        // rather than slid apart. Dense sampling catches a narrow road crossing a large parcel's middle.
        static void Divide(FieldPlot f, int depth)
        {
            if (Area(Trim(f)) < MinimumArea || f.halfLength < 9f || f.halfWidth < 9f) return;
            int nu = Mathf.Max(2, Mathf.CeilToInt(f.halfLength * 2f / 4f));
            int nv = Mathf.Max(2, Mathf.CeilToInt(f.halfWidth * 2f / 4f));
            int clear = 0, total = (nu + 1) * (nv + 1);
            for (int v = 0; v <= nv; v++)
                for (int u = 0; u <= nu; u++)
                    if (!blocked(f.Point(f.halfLength * (2f * u / nu - 1f), f.halfWidth * (2f * v / nv - 1f)))) clear++;
            if (clear == 0) return;
            bool fits = clear == total;
            float target = MapLayout.NearVillage(f.center, 110f) ? 5500f : 11000f;
            if (fits && Area(f) < target * Range(1.05f, 1.65f)) { lots.Add(f); return; }
            if (depth >= 12) return;
            bool splitAlong = f.halfLength > f.halfWidth * Range(0.85f, 1.8f);
            float cut = fits ? Range(0.38f, 0.62f) : 0.5f;
            Divide(Slice(f, splitAlong, 0f, cut), depth + 1);
            Divide(Slice(f, splitAlong, cut, 1f), depth + 1);
        }

        // Rejoins small obstacle-edge pieces wherever their union is still one trapezoid. This preserves the road
        // setback without turning every subdivision around a bend into a tiny separately coloured crop.
        static void Consolidate(int first)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = first; i < lots.Count; i++)
                    for (int j = i + 1; j < lots.Count; j++)
                    {
                        if (Area(lots[i]) > 3000f && Area(lots[j]) > 3000f) continue;
                        if (Area(lots[i]) + Area(lots[j]) > 19000f) continue;
                        if (!Join(lots[i], lots[j], out var joined)) continue;
                        lots[i] = joined;
                        lots.RemoveAt(j);
                        changed = true;
                        j = i;
                    }
            }
        }

        // Only joins full matching ends or sides, with collinear outside edges: the union cannot cover a blocked gap.
        internal static bool Join(FieldPlot a, FieldPlot b, out FieldPlot joined)
        {
            joined = default;
            Vector2 d = b.center - a.center;
            float du = Vector2.Dot(d, a.along), dv = Vector2.Dot(d, a.Across);
            if (Mathf.Abs(du) < 0.02f && Mathf.Abs(a.halfLength - b.halfLength) < 0.02f)
            {
                if (dv < 0f) return Join(b, a, out joined);
                float near = dv - b.shear * b.halfLength - b.HalfWidthAt(-b.halfLength);
                float far = dv + b.shear * b.halfLength - b.HalfWidthAt(b.halfLength);
                if (Mathf.Abs(near - (-a.shear * a.halfLength + a.HalfWidthAt(-a.halfLength))) > 0.02f
                    || Mathf.Abs(far - (a.shear * a.halfLength + a.HalfWidthAt(a.halfLength))) > 0.02f) return false;
                float u = Vector2.Dot(a.center, a.along), v = Vector2.Dot(a.center, a.Across);
                joined = FromCorners(a.along, a.Across, u - a.halfLength, u + a.halfLength,
                    v - a.shear * a.halfLength - a.HalfWidthAt(-a.halfLength), v + near + 2f * b.HalfWidthAt(-b.halfLength),
                    v + a.shear * a.halfLength - a.HalfWidthAt(a.halfLength), v + far + 2f * b.HalfWidthAt(b.halfLength));
                return true;
            }
            if (du < 0f) return Join(b, a, out joined);
            if (Mathf.Abs(du - a.halfLength - b.halfLength) > 0.02f || Mathf.Abs(a.shear - b.shear) > 0.0001f) return false;
            float slope = a.halfWidth * a.taper / a.halfLength;
            if (Mathf.Abs(slope - b.halfWidth * b.taper / b.halfLength) > 0.0001f
                || Mathf.Abs(dv - a.shear * du) > 0.02f || Mathf.Abs(a.HalfWidthAt(a.halfLength) - b.HalfWidthAt(-b.halfLength)) > 0.02f) return false;
            joined = a;
            joined.center += (a.along + a.Across * a.shear) * b.halfLength;
            joined.halfLength += b.halfLength;
            joined.halfWidth += slope * b.halfLength;
            joined.taper = slope * joined.halfLength / joined.halfWidth;
            return true;
        }

        // Unwarped corners let neighbouring edges match exactly, before either crop is inset or globally warped.
        static Vector2 Corner(FieldPlot f, float u, float v) =>
            f.center + f.along * (u * f.halfLength) + f.Across * (v * f.HalfWidthAt(u * f.halfLength) + f.shear * u * f.halfLength);

        // Matches collinear overlaps, including partial edges at T-junctions. Each parcel pair is visited only once.
        static void BuildBoundaries()
        {
            var edges = new Vector2[lots.Count, 4];
            for (int i = 0; i < lots.Count; i++)
            {
                edges[i, 0] = Corner(lots[i], -1f, -1f); edges[i, 1] = Corner(lots[i], 1f, -1f);
                edges[i, 2] = Corner(lots[i], 1f, 1f); edges[i, 3] = Corner(lots[i], -1f, 1f);
            }
            for (int i = 0; i < lots.Count; i++)
                for (int j = i + 1; j < lots.Count; j++)
                {
                    if ((lots[i].center - lots[j].center).magnitude > lots[i].halfLength + lots[i].halfWidth * 1.3f + lots[j].halfLength + lots[j].halfWidth * 1.3f) continue;
                    for (int e = 0; e < 4; e++)
                    {
                        Vector2 a = edges[i, e], ab = edges[i, (e + 1) % 4] - a, axis = ab.normalized;
                        Vector2 normal = new Vector2(-axis.y, axis.x);
                        for (int k = 0; k < 4; k++)
                        {
                            Vector2 c = edges[j, k] - a, d = edges[j, (k + 1) % 4] - a;
                            if (Mathf.Abs(Vector2.Dot(c, normal)) > 0.03f || Mathf.Abs(Vector2.Dot(d, normal)) > 0.03f) continue;
                            float c0 = Vector2.Dot(c, axis), c1 = Vector2.Dot(d, axis);
                            float low = Mathf.Max(0f, Mathf.Min(c0, c1)), high = Mathf.Min(ab.magnitude, Mathf.Max(c0, c1));
                            if (high - low < 3f) continue;
                            MapLayout.Boundaries.Add(new FieldBoundary { a = a + axis * low, b = a + axis * high, first = i, second = j });
                        }
                    }
                }
            // Shuffle a balanced mix, so even a small marsh farm has ditches and the recipe cannot drift by chance.
            int n = MapLayout.Boundaries.Count;
            var kinds = new FieldBoundaryKind[n];
            for (int i = 0; i < n; i++) kinds[i] = i < n * 0.48f ? FieldBoundaryKind.Hedge : i < n * 0.63f ? FieldBoundaryKind.Ditch : i < n * 0.75f ? FieldBoundaryKind.Fence : FieldBoundaryKind.Open;
            for (int i = n - 1; i > 0; i--) { int j = random.Next(i + 1); var k = kinds[i]; kinds[i] = kinds[j]; kinds[j] = k; }
            for (int i = 0; i < n; i++) { var b = MapLayout.Boundaries[i]; b.kind = kinds[i]; MapLayout.Boundaries[i] = b; }
        }

        // Neighbouring plots mostly share one farm's two crops, with enough rotation for their common edges to read.
        static Crop PickCrop(Vector2 at)
        {
            uint farm = (uint)(Mathf.FloorToInt(at.x / 350f) * 73856093) ^ (uint)(Mathf.FloorToInt(at.y / 350f) * 19349663) ^ (uint)MapLayout.Plan.fieldSeed;
            double r = random.NextDouble();
            return r < 0.5 ? (Crop)(farm % 5) : r < 0.8 ? (Crop)((farm % 5 + 1 + farm / 5 % 4) % 5) : (Crop)random.Next(5);
        }

        // Area is exact before the gentle land warp (taper averages to the middle width).
        static float Area(FieldPlot f) => 4f * f.halfLength * f.halfWidth;

        // Uses only the field planner's own seeded stream.
        static float Range(float low, float high) => low + (float)random.NextDouble() * (high - low);
    }
}

using System;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Reusable site engineering keeps access, surface wear and dressings in the permanent world recipe.
    public static partial class WorldLandmarkLayout
    {
        // Chamfered, skewed outlines share exactly the same vertices with the map query and slab renderer. A bordered
        // yard fades into its soil rim, the rim fades out onto the grass, and concrete is laid in 9-13 m pours.
        static void Region(WorldFeature f, Random rng, float u, float v, float length, float width, LandmarkSurfaceKind kind, bool border = true)
        {
            if (border) { Region(f, rng, u + 1.2f, v - .8f, length + 6f, width + 6f, LandmarkSurfaceKind.Soil, false); Soften(f, 2f); }
            var area = new FeatureArea { center = f.Area.Point(u, v), along = f.along, halfLength = length / 2f, halfWidth = width / 2f };
            f.surfaces.Add(new LandmarkSurface { area = area, outline = Outline(area, rng), kind = kind,
                lift = .055f + f.surfaces.Count * .0001f, fall = area.Across * (.018f / Math.Max(1f, area.halfWidth)),
                bays = kind == LandmarkSurfaceKind.Concrete && length > 40f ? 9f + 4f * Hash(f, f.surfaces.Count, 99) : 0f });
            Soften(f, border ? 1.4f : 1.8f);
        }

        // Gives the surface just added a soft edge: each side fades over its own width, 35-100% of the widest, and never
        // over more than 40% of the surface's narrower half, so a full-strength core always remains.
        static void Soften(WorldFeature f, float widest)
        {
            int index = f.surfaces.Count - 1;
            var s = f.surfaces[index];
            int edges = s.outline != null ? s.outline.Length : 4;
            float cap = .4f * Math.Min(s.area.halfLength, s.area.halfWidth);
            s.soft = new float[edges];
            for (int i = 0; i < edges; i++) s.soft[i] = Math.Min(cap, widest * (.35f + .65f * Hash(f, index, i)));
            f.surfaces[index] = s;
        }

        // Sets the tone variant of the surface just added (1 lighter, 2 darker).
        static void Tint(WorldFeature f, int shade)
        {
            var s = f.surfaces[f.surfaces.Count - 1]; s.shade = (byte)shade; f.surfaces[f.surfaces.Count - 1] = s;
        }

        // A stable 0-1 value per feature, surface and slot, so soft widths never consume the layout's random stream.
        static float Hash(WorldFeature f, int index, int slot)
        {
            uint h = unchecked((uint)f.variantSeed * 2654435761u ^ (uint)index * 2246822519u ^ (uint)slot * 3266489917u);
            h = unchecked((h ^ (h >> 15)) * 668265263u); h ^= h >> 13; h = unchecked(h * 374761393u); h ^= h >> 16;
            return (h & 65535) / 65535f;
        }

        // A convex, counterclockwise blob of up to ten vertices (seven for a small one) inside the area's ellipse, at
        // jittered angles and 78-100% radii: worn ground, spills and weeds read as patches, not as cut rectangles.
        static Vector2[] Irregular(FeatureArea area, Random rng)
        {
            int n = area.halfLength * area.halfWidth < 25f ? 7 : 10;
            var points = new Vector2[n];
            double turn = rng.NextDouble() * Math.PI * 2.0 / n;
            for (int i = 0; i < n; i++)
            {
                double a = turn + (i + (rng.NextDouble() - .5) * .6) * Math.PI * 2.0 / n;
                float r = Range(rng, .78f, 1f);
                points[i] = new Vector2((float)Math.Cos(a) * r * area.halfLength, (float)Math.Sin(a) * r * area.halfWidth);
            }
            // Monotone-chain hull in local axes; Point keeps the counterclockwise order because Across is to the left.
            Array.Sort(points, (p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
            var hull = new Vector2[n * 2]; int k = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int start = k;
                for (int j = 0; j < n; j++)
                {
                    var p = points[pass == 0 ? j : n - 1 - j];
                    while (k >= start + 2 && Turn(hull[k - 2], hull[k - 1], p) <= 0f) k--;
                    hull[k++] = p;
                }
                k--;
            }
            var outline = new Vector2[k];
            for (int i = 0; i < k; i++) outline[i] = area.Point(hull[i].x, hull[i].y);
            return outline;
        }

        // Twice the signed area of a, b, c: positive when c lies to the left of a to b.
        static float Turn(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        // A convex counterclockwise octagon with unequal chamfers; any axis keeps the same handedness as the yard.
        static Vector2[] Outline(FeatureArea area, Random rng)
        {
            float x = area.halfLength, z = area.halfWidth;
            float jitter = Math.Min(6f, Math.Min(x, z) * .22f), minimum = Math.Min(2f, jitter * .5f);
            float a = Range(rng, minimum, jitter), b = Range(rng, minimum, jitter), c = Range(rng, minimum, jitter), d = Range(rng, minimum, jitter);
            float bevel = Math.Min(8f, Math.Min(x, z) * .35f);
            return new[] { area.Point(-x + bevel, -z + a), area.Point(x - bevel, -z + b),
                area.Point(x - b, -z + bevel + b), area.Point(x - c, z - bevel - c),
                area.Point(x - bevel, z - c), area.Point(-x + bevel, z - d),
                area.Point(-x + d, z - bevel - d), area.Point(-x + a, -z + bevel + a) };
        }

        // Paved circulation is 5-8 m wide, with paired 0.4 m wheel wear rather than arbitrary dark splats; the gravel
        // shoulders fade into the yard or the grass.
        static void Lane(WorldFeature f, float u0, float v0, float u1, float v1, float width)
        {
            Strip(f, u0, v0, u1, v1, width + 4f, LandmarkSurfaceKind.Gravel, .09f); Soften(f, 1f);
            Strip(f, u0, v0, u1, v1, width, LandmarkSurfaceKind.Asphalt, .13f);
            Vector2 d = new Vector2(u1 - u0, v1 - v0).normalized, side = new Vector2(-d.y, d.x);
            foreach (float wheel in new[] { -1.05f, 1.05f })
                Strip(f, u0 + side.x * wheel, v0 + side.y * wheel, u1 + side.x * wheel, v1 + side.y * wheel, .4f, LandmarkSurfaceKind.Wear, .15f);
        }

        // Secondary access is a narrow compacted gravel track with worn shoulders, never another broad apron; the soil
        // shoulders fade out over a metre or so and the gravel into them.
        static void Track(WorldFeature f, float u0, float v0, float u1, float v1)
        {
            Strip(f, u0, v0, u1, v1, 6.5f, LandmarkSurfaceKind.Soil, .08f); Soften(f, 1.3f);
            Strip(f, u0, v0, u1, v1, 3.5f, LandmarkSurfaceKind.Gravel, .10f); Soften(f, .6f);
        }

        // Arbitrary route segments use one oriented rectangle, with no extra tessellation per metre.
        static void Strip(WorldFeature f, float u0, float v0, float u1, float v1, float width, LandmarkSurfaceKind kind, float lift)
        {
            Vector2 a = f.Area.Point(u0, v0), b = f.Area.Point(u1, v1), d = b - a;
            f.surfaces.Add(new LandmarkSurface { area = new FeatureArea { center = (a + b) * .5f, along = d.normalized,
                halfLength = d.magnitude * .5f, halfWidth = width * .5f }, kind = kind, lift = lift });
        }

        // The works gets a real siding only if its whole approach is dry and clear of other compounds.
        static void WorksRail(WorldFeature f)
        {
            var a = f.Area;
            Vector2 entry = a.Point(-f.halfLength - 24f, 55f);
            Vector2 join = WorldPlan.FeatureRailPoint(entry, out _);
            for (int i = 0, steps = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(join, entry) / 20f)); i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(join, entry, i / (float)steps);
                if (WorldPlan.NearWater(p, 8f) || WorldPlan.FeatureReserved(p, 8f)) return;
            }
            var corners = new[] { join, entry, a.Point(-190f, 55f), a.Point(15f, 55f) };
            f.rails.Add(DenseRail(corners));
            RailSurface(f, a.Point(-212f, 55f), a.Point(15f, 55f));
            for (int i = 0; i < 2; i++) Building(f, "Scenery_RailWagon", -108f + i * 16f, 55f, true,
                new Vector3(2.9f, 3.9f, 14f), 0f, 3, 1f, true);
        }

        // Eight-metre road gates and rail openings remain physically clear; posts follow the sector terrain later.
        static void Perimeter(WorldFeature f, float x, float bottom, float top, bool rail)
        {
            var a = f.Area;
            f.fences.Add(new[] { a.Point(-4f, top), a.Point(-x, top), a.Point(-x, rail ? (f.kind == WorldFeatureKind.IndustrialZone ? 59f : -2f) : bottom) });
            if (f.kind == WorldFeatureKind.RailJunction)
            {
                f.fences.Add(new[] { a.Point(4f, top), a.Point(x, top), a.Point(x, -2f) });
                f.fences.Add(new[] { a.Point(-x, bottom), a.Point(x, bottom) });
            }
            else f.fences.Add(new[] { a.Point(4f, top), a.Point(x, top), a.Point(x, bottom), a.Point(-x, bottom) });
            if (rail && f.kind == WorldFeatureKind.IndustrialZone)
                f.fences.Add(new[] { a.Point(-x, 51f), a.Point(-x, bottom) });
            // A rail junction has an open throat: a fence across its mainline would be a false barrier.
        }

        // Cracked and oily patches stay around machinery and loading areas, never across the entire site.
        static void Weather(WorldFeature f, Random rng, float u, float v, float length, float width, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Region(f, rng, u + Range(rng, -length / 2f, length / 2f), v + Range(rng, -width / 2f, width / 2f),
                    Range(rng, 4f, 11f), Range(rng, 3f, 7f), i % 4 == 0 ? LandmarkSurfaceKind.Gravel : LandmarkSurfaceKind.Wear, false);
                var s = f.surfaces[f.surfaces.Count - 1]; s.lift = .18f; s.fall = Vector2.zero; s.outline = Irregular(s.area, rng); f.surfaces[f.surfaces.Count - 1] = s;
                Soften(f, 1.6f);
            }
        }

        // Unused corners have unequal tongues of soil and coarse vegetation instead of a uniform grass rim.
        static void Corners(WorldFeature f, Random rng)
        {
            foreach (float side in new[] { -1f, 1f })
                for (int i = 0; i < 5; i++)
                {
                    float u = side * (f.halfLength - Range(rng, 14f, 28f));
                    float v = f.kind == WorldFeatureKind.RailJunction ? 81f : -f.halfWidth + 13f + i * Range(rng, 12f, 20f);
                    Region(f, rng, u, v, Range(rng, 48f, 68f), Range(rng, 20f, 31f),
                        i % 2 == 0 ? LandmarkSurfaceKind.Overgrown : LandmarkSurfaceKind.Soil, false);
                }
        }

        // Low-poly prop roots carry their own seed-derived yaw; they are never added to targetable mission sites.
        static void Prop(WorldFeature f, string model, float u, float v, int group, Random rng, Vector3 scale = default)
        {
            float angle = Range(rng, -.055f, .055f);
            Vector2 facing = -f.Area.Across * (float)Math.Cos(angle) + f.along * (float)Math.Sin(angle);
            f.props.Add(new LandmarkProp { model = model, position = f.Area.Point(u, v), facing = facing,
                scale = scale.sqrMagnitude < .0001f ? Vector3.one : scale, group = group });
        }

        // Two parked civilian utility vehicles form a bay clear of the through lane.
        static void Vehicles(WorldFeature f, Random rng, float u, float v, int group)
            => Prop(f, "Scenery_UtilityVehicles", u, v, group, rng);

        // One local RNG owns every variation, so visiting the sector never changes its engineering or clutter.
        static float Range(Random rng, float min, float max) => min + (max - min) * (float)rng.NextDouble();
    }
}

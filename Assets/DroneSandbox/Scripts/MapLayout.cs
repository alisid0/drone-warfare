using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum Crop { Wheat, YoungCrop, Ploughed, Sunflower, MownHay }

    // A rectangular field, with its rows running along `along`.
    public struct FieldPlot
    {
        public Vector2 center, along;
        public float halfLength, halfWidth;
        public Crop crop;

        public Vector2 Across => new Vector2(-along.y, along.x);

        // True if p is inside the field grown by margin on every side.
        public bool Contains(Vector2 p, float margin)
        {
            Vector2 d = p - center;
            return Mathf.Abs(Vector2.Dot(d, along)) < halfLength + margin && Mathf.Abs(Vector2.Dot(d, Across)) < halfWidth + margin;
        }

        public Vector2 Point(float u, float v) => center + along * u + Across * v;
    }

    // The hand-made layout of the map: bases, defences, the river, roads, villages and farm fields.
    // Terrain, water, fields and scenery are all built from this, in the game and in the editor preview.
    // Positions are (x, z) in metres; the battlefield is about 180 m across, inside a ring of wooded hills.
    public static class MapLayout
    {
        public const float BaseRadius = 18f, HqRadius = 17f, DefenceRadius = 6f;
        public const float RiverHalfWidth = 4.5f, RoadHalfWidth = 2f;
        public const float VillageRadius = 22f;
        const float Edge = 225f; // keep fields and scenery inside the terrain

        public static readonly Vector3 BasePosition = new Vector3(-72f, 0f, -70f);
        public static readonly Vector3 EnemyHqPosition = new Vector3(45f, 0f, 45f);

        // Enemy vehicles spawn and patrol inside this square. The river and villages stay outside it.
        public static readonly Rect EnemyArea = new Rect(-35f, -35f, 115f, 115f);

        // Fixed defences. The ground is levelled under each one.
        public static readonly (EnemyType type, Vector3 position)[] FixedDefences =
        {
            (EnemyType.Radar, EnemyHqPosition + new Vector3(6f, 0f, -8f)),
            (EnemyType.AAGun, EnemyHqPosition + new Vector3(-12f, 0f, 0f)),
            (EnemyType.AAGun, EnemyHqPosition + new Vector3(0f, 0f, -14f)),
            (EnemyType.AAGun, EnemyHqPosition + new Vector3(10f, 0f, 9f)),
            (EnemyType.AAGun, new Vector3(-10f, 0f, -5f)), // forward post with no radar cover, an easier first target
            (EnemyType.Radar, new Vector3(-20f, 0f, 55f)), // second radar covering the north-west flank
            (EnemyType.AAGun, new Vector3(-12f, 0f, 48f)),
        };

        // The river, upstream first. It comes down from the western hills and passes between your base
        // and enemy land.
        static readonly Vector2[] River =
        {
            new Vector2(-250f, 30f), new Vector2(-170f, 22f), new Vector2(-120f, 8f), new Vector2(-85f, -10f),
            new Vector2(-58f, -33f), new Vector2(-42f, -58f), new Vector2(-32f, -95f), new Vector2(-26f, -150f),
            new Vector2(-20f, -250f),
        };

        // The main road crosses the river here (a river point, so both lines pass through it exactly).
        public static readonly Vector2 BridgePoint = new Vector2(-58f, -33f);

        // Dirt roads. Ends outside the terrain run off into the hills.
        static readonly Vector2[][] Roads =
        {
            // Main road: your base, over the bridge, past the forward AA post, to the enemy HQ.
            new[] { new Vector2(-67.5f, -54f), new Vector2(-67f, -42f), BridgePoint, new Vector2(-49f, -25f),
                    new Vector2(-30f, -16f), new Vector2(-5f, -14f), new Vector2(18f, 0f), new Vector2(30f, 18f),
                    new Vector2(36f, 32f) },
            // North-south country road through two villages.
            new[] { new Vector2(30f, -250f), new Vector2(28f, -140f), new Vector2(22f, -80f), new Vector2(8f, -40f),
                    new Vector2(-5f, -14f), new Vector2(-18f, 15f), new Vector2(-36f, 45f), new Vector2(-52f, 70f),
                    new Vector2(-68f, 120f), new Vector2(-90f, 250f) },
            // East road to a village on the far side.
            new[] { new Vector2(18f, 0f), new Vector2(40f, -22f), new Vector2(62f, -50f), new Vector2(85f, -80f),
                    new Vector2(150f, -120f), new Vector2(250f, -150f) },
            // Farm track from the bridge up the far bank to a farmstead.
            new[] { new Vector2(-49f, -25f), new Vector2(-62f, 5f), new Vector2(-78f, 32f), new Vector2(-84f, 46f),
                    new Vector2(-110f, 75f), new Vector2(-170f, 120f), new Vector2(-250f, 150f) },
            // Supply road out of the back of your base.
            new[] { new Vector2(-88.5f, -71f), new Vector2(-110f, -72f), new Vector2(-160f, -82f), new Vector2(-250f, -95f) },
        };

        public static readonly Vector2[] Villages =
        {
            new Vector2(20f, -72f), new Vector2(-52f, 70f), new Vector2(62f, -54f), new Vector2(-81f, 40f),
        };

        public static List<Vector2> RiverLine { get; private set; }
        public static List<List<Vector2>> RoadLines { get; private set; }
        public static PathField RiverField { get; private set; }
        public static PathField RoadField { get; private set; }
        public static readonly List<Ground.FlatSite> FlatSites = new List<Ground.FlatSite>();
        public static readonly List<FieldPlot> Fields = new List<FieldPlot>();

        // Builds the lines, distance fields and field plan. The same every time, so it only runs once.
        public static void Prepare()
        {
            if (RiverField != null) return;

            FlatSites.Clear();
            FlatSites.Add(new Ground.FlatSite(BasePosition, BaseRadius, true));
            FlatSites.Add(new Ground.FlatSite(EnemyHqPosition, HqRadius, true));
            foreach (var d in FixedDefences) FlatSites.Add(new Ground.FlatSite(d.position, DefenceRadius, true));

            RiverLine = Smooth(River, 2f);
            RoadLines = new List<List<Vector2>>();
            foreach (var road in Roads) RoadLines.Add(Smooth(road, 2f));
            // The river field reaches far out so the hills can open into a valley around the river.
            RiverField = new PathField(new[] { RiverLine }, 80f, Ground.Size);
            RoadField = new PathField(RoadLines, 12f, Ground.Size);

            PlanFields(new System.Random(11));
        }

        // ---- Questions the builders ask ----

        public static bool NearFlatSite(Vector2 p, float margin)
        {
            foreach (var s in FlatSites)
                if (Vector2.Distance(p, new Vector2(s.center.x, s.center.z)) < s.radius + margin) return true;
            return false;
        }

        public static bool NearVillage(Vector2 p, float margin)
        {
            foreach (var v in Villages)
                if (Vector2.Distance(p, v) < VillageRadius + margin) return true;
            return false;
        }

        public static bool InField(Vector2 p, float margin)
        {
            foreach (var f in Fields)
                if (f.Contains(p, margin)) return true;
            return false;
        }

        // Open ground for trees and rocks: not on a road, in the river, on a field, in a village or a base.
        public static bool IsWild(Vector2 p, float margin)
        {
            if (Mathf.Abs(p.x) > Edge || Mathf.Abs(p.y) > Edge) return false;
            if (RoadField.Distance(p.x, p.y) < RoadHalfWidth + 1.5f + margin) return false;
            if (RiverField.Distance(p.x, p.y) < RiverHalfWidth + 2.5f + margin) return false;
            return !NearFlatSite(p, 3f + margin) && !NearVillage(p, margin) && !InField(p, 1f + margin);
        }

        // ---- Fields ----

        // Lines fields up along both sides of every road, then fills the open land between them.
        static void PlanFields(System.Random rng)
        {
            Fields.Clear();
            foreach (var line in RoadLines)
            {
                float next = Range(rng, 8f, 20f), walked = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    walked += Vector2.Distance(line[i - 1], line[i]);
                    if (walked < next) continue;
                    next = walked + Range(rng, 26f, 40f);
                    Vector2 along = (line[i] - line[i - 1]).normalized;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        if (rng.NextDouble() < 0.15) continue;
                        float halfLength = Range(rng, 14f, 26f), halfWidth = Range(rng, 12f, 24f);
                        Vector2 dir = Rotate(along, Range(rng, -2f, 2f) * Mathf.Deg2Rad);
                        Vector2 normal = new Vector2(-dir.y, dir.x) * side;
                        // Rows run along the road on some fields and away from it on others.
                        bool rowsAway = rng.NextDouble() < 0.4;
                        float depth = rowsAway ? halfLength : halfWidth; // the field's half-extent away from the road
                        var crop = PickCrop(rng);
                        // Set back from the road by a verge, further if it won't fit (the road may curve).
                        for (float setback = 4f; setback <= 13f; setback += 3f)
                        {
                            var plot = new FieldPlot
                            {
                                center = line[i] + normal * (RoadHalfWidth + setback + depth),
                                along = rowsAway ? normal : dir,
                                halfLength = halfLength,
                                halfWidth = halfWidth,
                                crop = crop,
                            };
                            if (!Fits(plot)) continue;
                            Fields.Add(plot);
                            break;
                        }
                    }
                }
            }

            // Then fill the open valley floor in between, biggest fields first, lined up with the nearest road
            // (or, far from roads, with a slowly turning grid). Some cells are left as meadow, and woods grow
            // wherever fields don't fit, including the hills round the edge.
            for (float gz = -150f; gz <= 150f; gz += 17f)
            {
                for (float gx = -150f; gx <= 150f; gx += 17f)
                {
                    if (rng.NextDouble() < 0.3) continue;
                    var c = new Vector2(gx + Range(rng, -5f, 5f), gz + Range(rng, -5f, 5f));
                    RoadField.Query(c.x, c.y, out _, out int s);
                    Vector2 grid = s >= 0 ? RoadField.Direction(s)
                        : Rotate(Vector2.up, (Mathf.PerlinNoise(c.x * 0.004f + 3f, c.y * 0.004f + 8f) - 0.5f) * 2.4f);
                    Vector2 dir = Rotate(grid, (rng.NextDouble() < 0.5 ? 0f : 90f + Range(rng, -3f, 3f)) * Mathf.Deg2Rad);
                    var crop = PickCrop(rng);
                    foreach (float size in new[] { 1f, 0.75f, 0.55f })
                    {
                        var plot = new FieldPlot
                        {
                            center = c, along = dir, crop = crop,
                            halfLength = Range(rng, 18f, 26f) * size, halfWidth = Range(rng, 14f, 22f) * size,
                        };
                        if (!Fits(plot)) continue;
                        Fields.Add(plot);
                        break;
                    }
                }
            }

            // Finally squeeze smaller fields into the gaps left between roads, at random spots.
            for (int attempt = 0; attempt < 500; attempt++)
            {
                var c = new Vector2(Range(rng, -130f, 130f), Range(rng, -130f, 130f));
                RoadField.Query(c.x, c.y, out _, out int s);
                Vector2 grid = s >= 0 ? RoadField.Direction(s)
                    : Rotate(Vector2.up, (Mathf.PerlinNoise(c.x * 0.004f + 3f, c.y * 0.004f + 8f) - 0.5f) * 2.4f);
                Vector2 dir = Rotate(grid, (rng.NextDouble() < 0.5 ? 0f : 90f) * Mathf.Deg2Rad);
                var plot = new FieldPlot
                {
                    center = c, along = dir, crop = PickCrop(rng),
                    halfLength = Range(rng, 7f, 14f), halfWidth = Range(rng, 6f, 11f),
                };
                if (Fits(plot)) Fields.Add(plot);
            }
        }

        static bool Fits(FieldPlot plot)
        {
            foreach (var other in Fields)
                if (Overlaps(plot, other, 3f)) return false;
            // Sample the plot every few metres, corners included.
            for (float u = -plot.halfLength; u <= plot.halfLength + 0.01f; u += plot.halfLength / 5f)
            {
                for (float v = -plot.halfWidth; v <= plot.halfWidth + 0.01f; v += plot.halfWidth / 4f)
                {
                    Vector2 p = plot.Point(u, v);
                    if (Mathf.Abs(p.x) > Edge - 15f || Mathf.Abs(p.y) > Edge - 15f) return false;
                    if (RoadField.Distance(p.x, p.y) < RoadHalfWidth + 2f) return false;
                    if (RiverField.Distance(p.x, p.y) < RiverHalfWidth + 6f) return false;
                    if (NearFlatSite(p, 4f) || NearVillage(p, 0f)) return false;
                }
            }
            return true;
        }

        // Separating-axis test between two rectangles, grown by margin.
        static bool Overlaps(FieldPlot a, FieldPlot b, float margin)
        {
            foreach (var axis in new[] { a.along, a.Across, b.along, b.Across })
            {
                float ra = Extent(a, axis) + margin, rb = Extent(b, axis);
                if (Mathf.Abs(Vector2.Dot(b.center - a.center, axis)) > ra + rb) return false;
            }
            return true;
        }

        static float Extent(FieldPlot f, Vector2 axis) =>
            Mathf.Abs(Vector2.Dot(f.along, axis)) * f.halfLength + Mathf.Abs(Vector2.Dot(f.Across, axis)) * f.halfWidth;

        static Crop PickCrop(System.Random rng)
        {
            double r = rng.NextDouble();
            if (r < 0.30) return Crop.Wheat;
            if (r < 0.55) return Crop.YoungCrop;
            if (r < 0.75) return Crop.Ploughed;
            if (r < 0.85) return Crop.Sunflower;
            return Crop.MownHay;
        }

        // ---- Helpers ----

        // A Catmull-Rom curve through the points, sampled about every `spacing` metres.
        static List<Vector2> Smooth(Vector2[] pts, float spacing)
        {
            var result = new List<Vector2>();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Length - 1)];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / spacing));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                    result.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                                       + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            result.Add(pts[pts.Length - 1]);
            return result;
        }

        public static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        public static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}

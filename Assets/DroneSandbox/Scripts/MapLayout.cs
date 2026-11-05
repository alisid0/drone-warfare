using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum Crop { Wheat, YoungCrop, Ploughed, Sunflower, MownHay }

    // A four-sided field with its rows running along `along`: its two ends are square to the rows, but it can be
    // wider at one end than the other (taper) and slanted (shear), the way strips cut from one block of land fan
    // out and lean with it. With both zero it's a rectangle. Then the whole thing is bent a little by Warp, so no
    // edge is a ruled line.
    public struct FieldPlot
    {
        public Vector2 center, along;
        public float halfLength, halfWidth; // halfWidth across the middle
        public float taper;                 // the +along end is (1 + taper) times as wide as the middle, the other end (1 - taper)
        public float shear;                 // how far across the middle line drifts per metre along
        public Crop crop;

        public Vector2 Across => new Vector2(-along.y, along.x);

        public float HalfWidthAt(float u) => halfWidth * (1f + taper * u / halfLength);

        // True if p is inside the field grown by margin on every side.
        public bool Contains(Vector2 p, float margin) => ContainsUnwarped(Unwarp(p), margin);

        // The same for a point already taken back through Unwarp (to test one point against many fields).
        public bool ContainsUnwarped(Vector2 p, float margin)
        {
            Vector2 d = p - center;
            float u = Vector2.Dot(d, along);
            float v = Vector2.Dot(d, Across) - shear * u;
            return Mathf.Abs(u) < halfLength + margin && Mathf.Abs(v) < HalfWidthAt(Mathf.Clamp(u, -halfLength, halfLength)) + margin;
        }

        // A point in the field: u metres along from the middle, and v across, where v = +-halfWidth is the edge
        // (scaled to the field's width there).
        public Vector2 Point(float u, float v)
        {
            Vector2 p = center + along * u + Across * (v * HalfWidthAt(u) / halfWidth + shear * u);
            return p + Warp(p);
        }

        // Field edges in aerial photos wander a metre or two over a few tens of metres (an old boundary, a
        // ditch, where the plough turned). Every field is bent by the same gentle warp of the land, so strips that
        // share an edge still share it.
        public const float WarpMax = 1.8f;
        public static Vector2 Warp(Vector2 p)
        {
            // Looked up in a 2 m grid of it (the builders ask millions of times), worked out the first time.
            if (warpGrid == null)
            {
                warpGrid = new Vector2[WarpCount * WarpCount];
                for (int j = 0; j < WarpCount; j++)
                    for (int i = 0; i < WarpCount; i++)
                        warpGrid[j * WarpCount + i] = WarpAt(new Vector2(i * WarpCell - WarpReach, j * WarpCell - WarpReach));
            }
            float gx = (p.x + WarpReach) / WarpCell, gz = (p.y + WarpReach) / WarpCell;
            int x0 = Mathf.FloorToInt(gx), z0 = Mathf.FloorToInt(gz);
            if (x0 < 0 || z0 < 0 || x0 >= WarpCount - 1 || z0 >= WarpCount - 1) return WarpAt(p);
            float fx = gx - x0, fz = gz - z0;
            int k = z0 * WarpCount + x0;
            return Vector2.Lerp(Vector2.Lerp(warpGrid[k], warpGrid[k + 1], fx), Vector2.Lerp(warpGrid[k + WarpCount], warpGrid[k + WarpCount + 1], fx), fz);
        }

        const float WarpCell = 2f, WarpReach = Ground.Size / 2f + 10f;
        static readonly int WarpCount = Mathf.CeilToInt(2f * WarpReach / WarpCell) + 1;
        static Vector2[] warpGrid;

        static Vector2 WarpAt(Vector2 p) => new Vector2(
            2.6f * (Mathf.PerlinNoise(p.x * 0.036f + 13f, p.y * 0.036f + 71f) - 0.5f) + 0.9f * (Mathf.PerlinNoise(p.x * 0.09f + 5f, p.y * 0.09f + 29f) - 0.5f),
            2.6f * (Mathf.PerlinNoise(p.x * 0.036f + 47f, p.y * 0.036f + 3f) - 0.5f) + 0.9f * (Mathf.PerlinNoise(p.x * 0.09f + 61f, p.y * 0.09f + 88f) - 0.5f));

        // Where a point of the bent land was before Warp moved it (near enough: the warp is gentle).
        public static Vector2 Unwarp(Vector2 p) => p - Warp(p - Warp(p));
    }

    // The layout of the map being played: bases, defences, the river, roads, villages, water, farm fields and the
    // sector's special features. It comes from a SectorPlan (the hand-made Varrow Valley until another sector is
    // loaded with Use). Terrain, water, fields and scenery are all built from this, in the game and in the editor
    // preview. Positions are (x, z) in metres; the battlefield is about 750 m across, inside a ring of hills.
    public static class MapLayout
    {
        public const float BaseRadius = 18f, HqRadius = 17f, DefenceRadius = 6f;
        // Fields and orchards are this many times the old map's sizes (real fields run a couple of hundred metres;
        // the old map's were sized to fit 480 m). Less than Scale.Distance, so there are more of them, as there
        // would be.
        public const float FieldScale = 2f;
        public const float RoadHalfWidth = 2f, RailHalfWidth = 2f;
        public const float VillageRadius = 45f;
        public static readonly float Edge = Ground.Size / 2f - 15f; // keep fields and scenery inside the terrain
        const float Far = 100000f;

        static SectorPlan plan;
        static bool prepared;

        public static SectorPlan Plan => plan ?? (plan = SectorPlan.Varrow());

        // Switches to another sector; the next Prepare (and World.Build) lays it out.
        public static void Use(SectorPlan sector)
        {
            plan = sector;
            prepared = false;
        }

        public static float RiverHalfWidth => Plan.riverHalfWidth;

        // How much of the channel holds water here: a river narrows and widens along its course (0.7 to 1 of the
        // full width), leaving gravel and mud on the inside where it narrows.
        public static float RiverWidthAt(float x, float z) =>
            RiverHalfWidth * (0.7f + 0.3f * Mathf.PerlinNoise(x * 0.035f + 41f, z * 0.035f + 17f));
        public static Vector3 BasePosition => Plan.basePosition;
        public static Vector3 EnemyHqPosition => Plan.enemyHqPosition;
        public static Rect EnemyArea => Plan.enemyArea;
        public static IReadOnlyList<(EnemyType type, Vector3 position)> FixedDefences => Plan.defences;
        public static IReadOnlyList<Vector2> Villages => Plan.villages;
        public static bool HasRiver => Plan.river != null;
        public static bool HasDam => Plan.hasDam;
        public static Vector2 DamPoint => Plan.damPoint;
        public static float LakeLevel => Plan.lakeLevel;
        public static float DamHalfSpan => Plan.damHalfSpan;
        public static IReadOnlyList<Vector2> Bridges => Plan.bridges;
        // The main bridge, or a point far off the map when no road crosses a river.
        public static Vector2 BridgePoint => Plan.bridges.Count > 0 ? Plan.bridges[0] : new Vector2(Far, Far);

        // Downstream along the river at the dam, and across the dam.
        public static Vector2 DamDown { get; private set; } = Vector2.right;
        public static Vector2 DamAcross => new Vector2(-DamDown.y, DamDown.x);

        public static List<Vector2> RiverLine { get; private set; }
        public static List<List<Vector2>> RoadLines { get; private set; }
        public static List<List<Vector2>> RailLines { get; private set; }
        public static PathField RiverField { get; private set; }
        public static PathField RoadField { get; private set; }
        public static PathField RailField { get; private set; }
        public static List<List<Vector2>> TrackLines { get; private set; }
        public static PathField TrackField { get; private set; }
        public static readonly List<Ground.FlatSite> FlatSites = new List<Ground.FlatSite>();
        public static readonly List<FieldPlot> Fields = new List<FieldPlot>();
        // Where roads meet or cross, which wheels have worn into a wider patch of bare ground.
        public static readonly List<Vector2> Junctions = new List<Vector2>();
        public static string Timings { get; private set; } = ""; // how long the last Prepare's steps took

        // Builds the lines, distance fields and field plan for the current sector. Only runs once per sector.
        public static void Prepare()
        {
            if (prepared) return;
            prepared = true;
            var p = Plan;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var times = new System.Text.StringBuilder();
            void Lap(string what) { times.Append($"{what} {clock.ElapsedMilliseconds} ms, "); clock.Restart(); }

            FlatSites.Clear();
            FlatSites.Add(new Ground.FlatSite(p.basePosition, BaseRadius, true));
            FlatSites.Add(new Ground.FlatSite(p.enemyHqPosition, HqRadius, true));
            foreach (var d in p.defences) FlatSites.Add(new Ground.FlatSite(d.position, DefenceRadius, true));
            foreach (var s in p.sites)
                if (s.flat > 0f) FlatSites.Add(new Ground.FlatSite(new Vector3(s.position.x, 0f, s.position.y), s.flat, false));
            if (p.Has(SectorFeature.Airstrip))
                for (float t = -p.airstripLength / 2f; t <= p.airstripLength / 2f + 0.1f; t += 24f)
                {
                    Vector2 c = p.airstripCenter + p.airstripAlong * t;
                    FlatSites.Add(new Ground.FlatSite(new Vector3(c.x, 0f, c.y), 21f, false));
                }

            RiverLine = p.river != null ? Smooth(p.river, 2f) : new List<Vector2>();
            DamDown = p.hasDam && RiverLine.Count > 1 ? (RiverLine[Mathf.Min(4, RiverLine.Count - 1)] - RiverLine[0]).normalized : Vector2.right;
            // The river field reaches far out so the hills can open into a valley around the river.
            RiverField = new PathField(p.river != null ? new[] { RiverLine } : new List<Vector2>[0], 80f * Scale.Distance, Ground.Size);
            Lap("river");
            RoadLines = new List<List<Vector2>>();
            foreach (var road in p.roads) RoadLines.Add(Smooth(road, 2f));
            Wander(RoadLines);
            Lap("roads");
            RailLines = new List<List<Vector2>>();
            foreach (var rail in p.railways) RailLines.Add(Smooth(rail, 2f));
            RoadField = new PathField(RoadLines, 12f, Ground.Size);
            RailField = new PathField(RailLines, 12f, Ground.Size);
            TrackLines = PlanTracks();
            TrackField = new PathField(TrackLines, 6f, Ground.Size);
            Lap("road fields");

            PlanFields(new System.Random(p.fieldSeed));
            IndexFields();
            Lap("fields");
            Timings = times.ToString();
        }

        // Country lanes aren't drawn with a French curve: they drift a metre or three either way over a few tens of
        // metres (round an old boundary or a wet patch). Ends, junctions and bridges stay put so the roads still
        // meet and cross the river square, and so does a road beside water, which keeps to its bank.
        static void Wander(List<List<Vector2>> lines)
        {
            Junctions.Clear();
            var anchors = new List<Vector2>(Plan.bridges);
            for (int a = 0; a < lines.Count; a++)
            {
                anchors.Add(lines[a][0]);
                anchors.Add(lines[a][lines[a].Count - 1]);
                for (int b = 0; b < lines.Count; b++)
                {
                    if (b == a) continue;
                    // A road ending on another one, or two crossing: the first point within 3 m of the other road.
                    foreach (var q in lines[a])
                    {
                        bool meets = false;
                        foreach (var r in lines[b])
                            if ((q - r).sqrMagnitude < 9f) { meets = true; break; }
                        if (!meets) continue;
                        bool known = false;
                        foreach (var j in Junctions)
                            if ((j - q).sqrMagnitude < 100f) { known = true; break; }
                        if (!known) Junctions.Add(q);
                    }
                }
            }
            anchors.AddRange(Junctions);
            for (int k = 0; k < lines.Count; k++)
            {
                var line = lines[k];
                var moved = new List<Vector2>(line.Count);
                float walked = 0f;
                for (int i = 0; i < line.Count; i++)
                {
                    if (i > 0) walked += Vector2.Distance(line[i - 1], line[i]);
                    Vector2 q = line[i];
                    float near = Far;
                    foreach (var a in anchors) near = Mathf.Min(near, Vector2.Distance(a, q));
                    float water = LakeDistance(q.x, q.y);
                    if (HasRiver) water = Mathf.Min(water, RiverField.Distance(q.x, q.y) - RiverHalfWidth);
                    float hold = Mathf.SmoothStep(0f, 1f, (near - 14f) / 30f) * Mathf.SmoothStep(0f, 1f, (water - 6f) / 12f);
                    float drift = 5.2f * (Mathf.PerlinNoise(walked / 55f, k * 7.3f + 0.5f) - 0.5f)
                                + 1.4f * (Mathf.PerlinNoise(walked / 19f, k * 3.1f + 40.5f) - 0.5f);
                    Vector2 dir = (line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)]).normalized;
                    moved.Add(q + new Vector2(-dir.y, dir.x) * drift * hold);
                }
                lines[k] = moved;
            }
        }

        // Wheel tracks worn across the grass from each emplacement and works site out to the nearest road, as every
        // position in an aerial photo has: a gentle curve rather than a ruled line. Positions close to the base or
        // the HQ use the compound's own roads, and of two positions close together only the first gets a track.
        static List<List<Vector2>> PlanTracks()
        {
            var tracks = new List<List<Vector2>>();
            var places = new List<(Vector2 at, float radius)>();
            foreach (var d in Plan.defences) places.Add((Flat(d.position), DefenceRadius));
            foreach (var s in Plan.sites)
                if (s.flat > 0f) places.Add((s.position, s.flat));
            var served = new List<Vector2>();
            foreach (var (at, radius) in places)
            {
                if (Vector2.Distance(at, Flat(Plan.basePosition)) < 40f || Vector2.Distance(at, Flat(Plan.enemyHqPosition)) < 40f) continue;
                bool near = false;
                foreach (var other in served)
                    if (Vector2.Distance(other, at) < Scale.D(25f)) near = true;
                if (near) continue;
                Vector2 road = default;
                float best = float.MaxValue;
                foreach (var line in RoadLines)
                    foreach (var q in line)
                    {
                        float d = (q - at).sqrMagnitude;
                        if (d < best) { best = d; road = q; }
                    }
                float distance = Mathf.Sqrt(best);
                if (distance < radius + 6f || distance > Scale.D(90f)) continue;
                Vector2 dir = (road - at) / distance, across = new Vector2(-dir.y, dir.x);
                float bow = distance * 0.12f * (Mathf.Repeat(at.x * 0.37f + at.y * 0.11f, 2f) - 1f);
                var track = Smooth(new[] { at + dir * radius * 0.6f, at + dir * distance * 0.5f + across * bow, road }, 2f);
                bool wet = false;
                foreach (var q in track)
                    if (LakeDistance(q.x, q.y) < 1f || (HasRiver && RiverField.Distance(q.x, q.y) < RiverHalfWidth + 1f)) wet = true;
                if (wet) continue;
                tracks.Add(track);
                served.Add(at);
            }
            return tracks;
        }

        // ---- Questions the builders ask ----

        // How much of the farmed valley floor (the middle 1250 m square, less water, villages and sites) is under
        // fields, sampled every 6 m. Aerial photos of farmland show 55 to 70%.
        public static float FieldCoverage()
        {
            int land = 0, farmed = 0;
            float half = Scale.D(150f);
            for (float z = -half; z <= half; z += 6f)
                for (float x = -half; x <= half; x += 6f)
                {
                    var q = new Vector2(x, z);
                    if (LakeDistance(x, z) < 0f || NearVillage(q, 0f) || NearFlatSite(q, 0f)) continue;
                    if (HasRiver && RiverField.Distance(x, z) < RiverHalfWidth) continue;
                    land++;
                    if (InField(q, 0f)) farmed++;
                }
            return land > 0 ? farmed / (float)land : 0f;
        }

        public static bool NearFlatSite(Vector2 p, float margin)
        {
            foreach (var s in FlatSites)
                if (Vector2.Distance(p, new Vector2(s.center.x, s.center.z)) < s.radius + margin) return true;
            return false;
        }

        public static bool NearVillage(Vector2 p, float margin)
        {
            foreach (var v in Plan.villages)
                if (Vector2.Distance(p, v) < VillageRadius + margin) return true;
            return false;
        }

        public static bool InField(Vector2 p, float margin)
        {
            if (fieldCells != null && margin <= FieldReachPad)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt((p.x + Ground.Size / 2f) / FieldCell), 0, fieldCellCount - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt((p.y + Ground.Size / 2f) / FieldCell), 0, fieldCellCount - 1);
                var near = fieldCells[cz * fieldCellCount + cx];
                if (near == null) return false;
                Vector2 q = FieldPlot.Unwarp(p);
                foreach (int i in near)
                    if ((i < Fields.Count ? Fields[i] : Plan.orchards[i - Fields.Count]).ContainsUnwarped(q, margin)) return true;
                return false;
            }
            Vector2 flat = FieldPlot.Unwarp(p);
            foreach (var f in Fields)
                if (f.ContainsUnwarped(flat, margin)) return true;
            foreach (var f in Plan.orchards)
                if (f.ContainsUnwarped(flat, margin)) return true;
            return false;
        }

        // Which fields and orchards could hold a point, per 8 m square (orchards numbered after the fields), so the
        // builders' hundreds of thousands of "is this in a field?" questions each look at only the few nearby.
        const float FieldCell = 8f, FieldReachPad = 4f;
        static List<int>[] fieldCells;
        static int fieldCellCount;

        static void IndexFields()
        {
            fieldCellCount = Mathf.CeilToInt(Ground.Size / FieldCell);
            fieldCells = new List<int>[fieldCellCount * fieldCellCount];
            int total = Fields.Count + Plan.orchards.Count;
            for (int i = 0; i < total; i++)
            {
                var f = i < Fields.Count ? Fields[i] : Plan.orchards[i - Fields.Count];
                float ex = Extent(f, Vector2.right) + FieldReachPad + FieldPlot.WarpMax, ez = Extent(f, Vector2.up) + FieldReachPad + FieldPlot.WarpMax;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((f.center.x - ex + Ground.Size / 2f) / FieldCell));
                int x1 = Mathf.Min(fieldCellCount - 1, Mathf.FloorToInt((f.center.x + ex + Ground.Size / 2f) / FieldCell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((f.center.y - ez + Ground.Size / 2f) / FieldCell));
                int z1 = Mathf.Min(fieldCellCount - 1, Mathf.FloorToInt((f.center.y + ez + Ground.Size / 2f) / FieldCell));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        (fieldCells[z * fieldCellCount + x] ??= new List<int>()).Add(i);
            }
        }

        // A point relative to the dam: x metres downstream (negative is up the lake), y metres across. Far away
        // from everything when there's no dam.
        public static Vector2 DamLocal(float x, float z)
        {
            if (!Plan.hasDam) return new Vector2(Far, Far);
            var d = new Vector2(x - Plan.damPoint.x, z - Plan.damPoint.y);
            return new Vector2(Vector2.Dot(d, DamDown), Vector2.Dot(d, DamAcross));
        }

        // Signed distance to the nearest shore of any standing water (the reservoir, lakes and pools) in metres:
        // negative on the water, positive on land.
        public static float LakeDistance(float x, float z) => Mathf.Min(ReservoirDistance(x, z), PondDistance(x, z, out _));

        // The same for the reservoir alone. The lobes' wobbly shores give coves and headlands; the dam cuts the
        // lake off.
        public static float ReservoirDistance(float x, float z)
        {
            if (!Plan.hasDam) return Far;
            Vector2 local = DamLocal(x, z);
            float s = float.MaxValue;
            foreach (var lobe in Plan.lakeLobes)
            {
                float a = lobe.angle * Mathf.Deg2Rad;
                var axis = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 d = local - lobe.center;
                float du = Vector2.Dot(d, axis) / lobe.halfLength, dv = (axis.x * d.y - axis.y * d.x) / lobe.halfWidth;
                float angle = Mathf.Atan2(dv, du);
                float wobble = 1f + 0.09f * Mathf.Sin(3f * angle + lobe.seed) + 0.06f * Mathf.Sin(5f * angle + 2.1f * lobe.seed)
                             + 0.035f * Mathf.Sin(11f * angle + 4f + lobe.seed);
                float lobeDistance = (Mathf.Sqrt(du * du + dv * dv) - wobble) * lobe.halfWidth;
                s = s == float.MaxValue ? lobeDistance : SmoothMin(s, lobeDistance, 14f);
            }
            s = Mathf.Max(s, local.x + 0.5f);
            // The lake narrows to the dam's span at the dam (a dam is built where the valley pinches), widening
            // again up the lake.
            s = Mathf.Max(s, Mathf.Abs(local.y) - (Plan.damHalfSpan - 3f) - Mathf.Max(0f, -local.x) * 0.8f);
            foreach (var island in Plan.islands) s = Mathf.Max(s, island.radius - Vector2.Distance(local, island.center));
            return s;
        }

        // Signed distance to the nearest pool or natural lake, and which one it is (-1 for none).
        public static float PondDistance(float x, float z, out int index)
        {
            float best = Far;
            index = -1;
            var ponds = Plan.ponds;
            for (int i = 0; i < ponds.Count; i++)
            {
                var pond = ponds[i];
                Vector2 d = new Vector2(x, z) - pond.center;
                if (d.sqrMagnitude > (pond.radius * 1.4f + 80f) * (pond.radius * 1.4f + 80f)) continue;
                // Stretched along its own axis (ponds lie along the hollow that holds them, not in circles), then
                // ragged: a couple of broad bays and points, and smaller notches round the shore.
                float axisAngle = pond.seed * 2.1f, stretch = 1.3f + 0.7f * Mathf.Repeat(pond.seed * 0.37f, 1f);
                Vector2 axis = new Vector2(Mathf.Cos(axisAngle), Mathf.Sin(axisAngle));
                float root = Mathf.Sqrt(stretch);
                Vector2 q = new Vector2(Vector2.Dot(d, axis) / root, (axis.x * d.y - axis.y * d.x) * root);
                float angle = Mathf.Atan2(q.y, q.x);
                float wobble = 1f + 0.16f * Mathf.Sin(2f * angle + pond.seed) + 0.11f * Mathf.Sin(3f * angle + 2.3f * pond.seed)
                             + 0.07f * Mathf.Sin(5f * angle + 1.7f * pond.seed) + 0.04f * Mathf.Sin(8f * angle + 0.9f * pond.seed)
                             + 0.025f * Mathf.Sin(13f * angle + 3.1f * pond.seed);
                float s = (q.magnitude - pond.radius * wobble) * Mathf.Min(1f, d.magnitude / Mathf.Max(q.magnitude, 0.001f));
                if (s < best)
                {
                    best = s;
                    index = i;
                }
            }
            return best;
        }

        // The surface of the standing water at (x, z), or -infinity on dry land.
        public static float WaterLevelAt(float x, float z)
        {
            if (ReservoirDistance(x, z) < 0f) return Plan.lakeLevel;
            if (PondDistance(x, z, out int i) < 0f) return Plan.ponds[i].level;
            return float.NegativeInfinity;
        }

        static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        public static int IslandCount => Plan.islands.Length;
        public static Vector2 IslandCenter(int i) => Plan.damPoint + DamDown * Plan.islands[i].center.x + DamAcross * Plan.islands[i].center.y;
        public static float IslandRadius(int i) => Plan.islands[i].radius;

        // The gorge below the dam, which stays clear so the spillway can be seen.
        public static bool InGorge(Vector2 p, float margin)
        {
            Vector2 local = DamLocal(p.x, p.y);
            return local.x > -2f && local.x < 48f + margin && Mathf.Abs(local.y) < 22f + margin;
        }

        // The dam and its abutments, with a margin.
        public static bool NearDam(Vector2 p, float margin)
        {
            Vector2 local = DamLocal(p.x, p.y);
            return local.x > -12f - margin && local.x < 14f + margin && Mathf.Abs(local.y) < 52f + margin;
        }

        // The quarry pit, the airstrip and the railway, which trees and fields keep off.
        public static bool OnWorks(Vector2 p, float margin)
        {
            var plan = Plan;
            if (plan.Has(SectorFeature.Quarry) && Vector2.Distance(p, plan.quarry) < plan.quarryRadius + 6f + margin) return true;
            if (plan.Has(SectorFeature.Airstrip))
            {
                Vector2 d = p - plan.airstripCenter;
                Vector2 across = new Vector2(-plan.airstripAlong.y, plan.airstripAlong.x);
                if (Mathf.Abs(Vector2.Dot(d, plan.airstripAlong)) < plan.airstripLength / 2f + 20f + margin
                    && Mathf.Abs(Vector2.Dot(d, across)) < 26f + margin) return true;
            }
            return RailLines != null && RailLines.Count > 0 && RailField.Distance(p.x, p.y) < RailHalfWidth + 2.5f + margin;
        }

        // Open ground for trees and rocks: not on a road, in the river or lake, on a field, in a village or a base.
        public static bool IsWild(Vector2 p, float margin)
        {
            if (Mathf.Abs(p.x) > Edge || Mathf.Abs(p.y) > Edge) return false;
            if (LakeDistance(p.x, p.y) < 1.5f + margin || NearDam(p, margin) || InGorge(p, margin)) return false;
            if (RoadField.Distance(p.x, p.y) < RoadHalfWidth + 1.5f + margin) return false;
            if (TrackField.Distance(p.x, p.y) < 1.5f + margin) return false;
            if (HasRiver && RiverField.Distance(p.x, p.y) < RiverHalfWidth + 2.5f + margin) return false;
            if (OnWorks(p, margin)) return false;
            // Trees keep 10 m back from the base and the HQ, so their walls, pads and vehicles stay in plain view.
            if (Vector2.Distance(p, Flat(Plan.basePosition)) < BaseRadius + 10f + margin
                || Vector2.Distance(p, Flat(Plan.enemyHqPosition)) < HqRadius + 10f + margin) return false;
            return !NearFlatSite(p, 3f + margin) && !NearVillage(p, margin) && !InField(p, 1f + margin);
        }

        // ---- Fields ----

        // Farmland laid out the way aerial photographs of it look: blocks of land along both sides of every road and
        // across the open valley floor, each cut into long strips that share their edges (a metre of grass between)
        // and fan or lean with the block, so the land reads as a patchwork rather than boxes on a lawn. Some blocks
        // stay one big field, and tracks (the gaps between blocks) run between them. How much is farmed follows the
        // sector's fields share (0.7 in the Varrow Valley).
        static void PlanFields(System.Random rng)
        {
            Fields.Clear();
            fieldCells = null; // the last sector's index
            planCells = new List<int>[PlanCellCount * PlanCellCount];
            float share = Plan.fields;
            if (share <= 0f) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            MapBlocked();
            long blockedMs = clock.ElapsedMilliseconds;
            double roadsideSkip = Mathf.Clamp01(0.1f + (0.7f - share) * 1.2f), fillSkip = Mathf.Clamp01(0.85f - share);
            const float F = FieldScale;
            foreach (var line in RoadLines)
            {
                float next = Range(rng, 4f, 14f) * F, walked = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    walked += Vector2.Distance(line[i - 1], line[i]);
                    if (walked < next) continue;
                    float frontage = Range(rng, 30f, 64f) * F;
                    next = walked + frontage + Range(rng, 3f, 7f);
                    Vector2 along = (line[i] - line[i - 1]).normalized;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        if (rng.NextDouble() < roadsideSkip) continue;
                        Vector2 away = Rotate(new Vector2(-along.y, along.x) * side, Range(rng, -4f, 4f) * Mathf.Deg2Rad);
                        float depth = Range(rng, 34f, 76f) * F;
                        // Set back from the road by a verge, further if it won't fit (the road may curve); then smaller.
                        bool placed = false;
                        for (float size = 1f; size >= 0.55f && !placed; size -= 0.225f)
                            for (float setback = 3f; setback <= 12f && !placed; setback += 3f)
                                placed = TryBlock(rng, line[i] + away * (RoadHalfWidth + setback + depth * size / 2f), away, depth * size, frontage * size);
                    }
                }
            }

            // Then the open valley floor between the roads, lined up with the nearest road (or, far from roads, with
            // a slowly turning grid), biggest blocks first.
            float floor = Scale.D(150f);
            for (float gz = -floor; gz <= floor; gz += 20f * F)
            {
                for (float gx = -floor; gx <= floor; gx += 20f * F)
                {
                    if (rng.NextDouble() < fillSkip) continue;
                    var c = new Vector2(gx + Range(rng, -6f, 6f) * F, gz + Range(rng, -6f, 6f) * F);
                    Vector2 away = GridDirection(c, rng);
                    float depth = Range(rng, 44f, 86f) * F, frontage = Range(rng, 36f, 76f) * F;
                    foreach (float size in new[] { 1f, 0.75f, 0.55f, 0.4f })
                        if (TryBlock(rng, c, away, depth * size, frontage * size)) break;
                }
            }

            // Finally odd small plots in the gaps (none under 40 by 36 m: a patch too small for a field stays meadow).
            int attempts = Mathf.RoundToInt(400 * Mathf.Min(1f, share / 0.7f) * (Scale.Distance / F) * (Scale.Distance / F));
            float odd = Scale.D(130f);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var c = new Vector2(Range(rng, -odd, odd), Range(rng, -odd, odd));
                TryBlock(rng, c, GridDirection(c, rng), Range(rng, 20f, 30f) * F, Range(rng, 18f, 28f) * F);
            }
            Lengthen();
            long plannedMs = clock.ElapsedMilliseconds;

            // And what's still open: farmland in aerial photos is fields with tracks and hedges between, not fields
            // on a lawn. So every gap on the valley floor big enough for a real field gets the biggest square-cornered
            // one that fits it, slid about a little to find the room (as much of it as the sector's fields share asks
            // for); smaller leftovers stay meadow.
            double gapSkip = Mathf.Clamp01(0.1f + (0.7f - share) * 1.5f);
            float gaps = Scale.D(148f);
            for (float gz = -gaps; gz <= gaps; gz += 20f * F) // every 40 m (a gap is 44-120 m across)
            {
                for (float gx = -gaps; gx <= gaps; gx += 20f * F)
                {
                    var c = new Vector2(gx, gz);
                    if (Blocked(c) || NearAnyField(c, 4f) || rng.NextDouble() < gapSkip) continue;
                    Vector2 grid = GridDirection(c, rng);
                    bool placed = false;
                    foreach (var gap in GapSizes)
                    {
                        Vector2 size = gap * F;
                        // Either way round: a gap is usually longer one way than the other.
                        for (int turn = 0; turn < 2 && !placed; turn++)
                        {
                            Vector2 away = turn == 0 ? grid : new Vector2(-grid.y, grid.x), side = new Vector2(-away.y, away.x);
                            foreach (var shift in GapShifts)
                            {
                                Vector2 at = c + away * (shift.x * size.x) + side * (shift.y * size.y);
                                if (TryBlock(rng, at, away, size.x, size.y, true)) { placed = true; break; }
                            }
                        }
                        if (placed) break;
                    }
                }
            }
            Lengthen();
            FieldTimings = $"blocked map {blockedMs} ms, blocks {plannedMs - blockedMs} ms, gaps {clock.ElapsedMilliseconds - plannedMs} ms";
        }

        public static string FieldTimings { get; private set; } = "";

        // Every field runs on as far as the land lets it, each end on its own (to 200 m long at most), the way
        // the strips of a block end raggedly, each where it meets a road, a track or the next field.
        static void Lengthen()
        {
            for (int i = 0; i < Fields.Count; i++)
            {
                var f = Fields[i];
                foreach (float end in new[] { 1f, -1f })
                {
                    for (int step = 0; step < 10 && f.halfLength < 50f * FieldScale; step++)
                    {
                        var longer = Lengthened(f, end, 4f * FieldScale);
                        if (!Fits(longer, int.MaxValue, i)) break;
                        f = longer;
                    }
                }
                Fields[i] = f;
                Register(i); // it may reach new squares now
            }
        }

        // The field made `metres` longer at its far end (end 1) or its near end (-1), its edges running straight on.
        static FieldPlot Lengthened(FieldPlot f, float end, float metres)
        {
            float slope = f.halfWidth * f.taper / f.halfLength; // how much wider it gets per metre along
            float half = metres / 2f;
            var g = f;
            g.halfLength = f.halfLength + half;
            g.center = f.center + (f.along + f.Across * f.shear) * (end * half);
            g.halfWidth = f.halfWidth + slope * end * half;
            g.taper = slope * g.halfLength / g.halfWidth;
            return g;
        }

        // Fields tried in the gaps, depth by frontage, biggest first; and how far each is slid (in its own sizes).
        static readonly Vector2[] GapSizes =
        {
            new Vector2(60f, 48f), new Vector2(46f, 38f), new Vector2(36f, 30f), new Vector2(28f, 22f), new Vector2(22f, 18f),
        };
        static readonly Vector2[] GapShifts =
        {
            Vector2.zero, new Vector2(0.3f, 0f), new Vector2(-0.3f, 0f), new Vector2(0f, 0.3f), new Vector2(0f, -0.3f),
        };

        // Whether p is in or within margin of a field planned so far (before the fields are indexed).
        static bool NearAnyField(Vector2 p, float margin)
        {
            Vector2 q = FieldPlot.Unwarp(p);
            var near = planCells[PlanCellAt(p.y) * PlanCellCount + PlanCellAt(p.x)];
            if (near == null) return false;
            foreach (int i in near)
                if (Fields[i].ContainsUnwarped(q, margin)) return true;
            return false;
        }

        // While the fields are planned: which ones reach each 64 m square (a field is listed in every square its
        // reach touches), so Fits and NearAnyField look at the few nearby rather than every field on a 2 km map.
        const float PlanCell = 64f;
        static readonly int PlanCellCount = Mathf.CeilToInt(Ground.Size / PlanCell);
        static List<int>[] planCells;
        static int[] planSeen = new int[0];
        static int planStamp;

        static int PlanCellAt(float coordinate) => Mathf.Clamp(Mathf.FloorToInt((coordinate + Ground.Size / 2f) / PlanCell), 0, PlanCellCount - 1);

        static void Register(int i)
        {
            var f = Fields[i];
            float r = Reach(f) + FieldReachPad;
            for (int cz = PlanCellAt(f.center.y - r); cz <= PlanCellAt(f.center.y + r); cz++)
                for (int cx = PlanCellAt(f.center.x - r); cx <= PlanCellAt(f.center.x + r); cx++)
                {
                    var list = planCells[cz * PlanCellCount + cx] ??= new List<int>();
                    if (!list.Contains(i)) list.Add(i);
                }
        }

        static void Unregister(int i)
        {
            foreach (var list in planCells) list?.Remove(i);
        }

        // Which way a block away from the roads lies: square to the nearest road, or a slowly turning grid.
        static Vector2 GridDirection(Vector2 c, System.Random rng)
        {
            RoadField.Query(c.x, c.y, out _, out int s);
            Vector2 grid = s >= 0 ? RoadField.Direction(s)
                : Rotate(Vector2.up, (Mathf.PerlinNoise(c.x * 0.004f / Scale.Distance + 3f, c.y * 0.004f / Scale.Distance + 8f) - 0.5f) * 2.4f);
            return Rotate(grid, ((rng.NextDouble() < 0.5 ? 0f : 90f) + Range(rng, -5f, 5f)) * Mathf.Deg2Rad);
        }

        // A block of farmland `depth` deep along `away` and `frontage` wide, centred on `center`, cut into strips
        // that run its depth. Its far side is a little wider or narrower than its near side and may lean; every strip
        // shares that. Where the whole block doesn't fit (a road, a village, water in the way), the strips that do
        // fit are kept, as farmers work round such things. False if none fit. A square block (for filling gaps)
        // neither fans nor leans.
        static bool TryBlock(System.Random rng, Vector2 center, Vector2 away, float depth, float frontage, bool square = false)
        {
            float spread = Range(rng, -0.15f, 0.15f); // the far side is (1 + spread) times as wide as the near side
            float slant = Range(rng, -0.15f, 0.15f); // and sits slant metres across for every metre deep
            if (square) spread = slant = 0f;
            var block = new FieldPlot
            {
                center = center, along = away, halfLength = depth / 2f,
                halfWidth = frontage * (2f + spread) / 4f, taper = spread / (2f + spread), shear = slant,
            };
            bool whole = Fits(block);
            int first = Fields.Count;

            // Strip widths, from narrow 12 m strips to 40 m ones; a quarter of blocks stay whole.
            var widths = new List<float>();
            if (frontage < 20f * FieldScale || rng.NextDouble() < 0.25) widths.Add(frontage);
            else
            {
                float total = 0f;
                while (total < frontage)
                {
                    float w = (rng.NextDouble() < 0.45 ? Range(rng, 6f, 10f) : Range(rng, 10f, 20f)) * FieldScale;
                    widths.Add(w);
                    total += w;
                }
                for (int i = 0; i < widths.Count; i++) widths[i] *= frontage / total;
            }
            float baulk = widths.Count > 1 ? 0.5f : 0f; // half the grass strip left between neighbours
            float near = -frontage / 2f;
            var crop = PickCrop(rng, center);
            foreach (float w in widths)
            {
                // The strip's edges across the block, at the near side and (fanned and shifted) at the far side.
                float a0 = near + baulk, a1 = near + w - baulk;
                near += w;
                float b0 = a0 * (1f + spread) + slant * depth / 2f, b1 = a1 * (1f + spread) + slant * depth / 2f;
                a0 -= slant * depth / 2f;
                a1 -= slant * depth / 2f;
                float nearWidth = a1 - a0, farWidth = b1 - b0;
                if (rng.NextDouble() < 0.5) crop = PickCrop(rng, center); // neighbours often grow different things
                var strip = new FieldPlot
                {
                    center = center + block.Across * ((a0 + a1 + b0 + b1) / 4f),
                    along = away,
                    halfLength = depth / 2f,
                    halfWidth = (nearWidth + farWidth) / 4f,
                    taper = (farWidth - nearWidth) / (farWidth + nearWidth),
                    shear = ((b0 + b1) - (a0 + a1)) / (2f * depth),
                    crop = crop,
                };
                if (whole || Fits(strip, first))
                {
                    Fields.Add(strip);
                    Register(Fields.Count - 1);
                }
            }
            // A lone narrow strip left over from a block that mostly didn't fit looks lost in the grass: drop it.
            if (!whole && Fields.Count - first == 1 && Fields[first].halfWidth < 7f * FieldScale)
            {
                Unregister(first);
                Fields.RemoveAt(first);
            }
            return Fields.Count > first;
        }

        // Whether a field (or orchard) fits here: clear of roads, water, sites, villages and other plots.
        // Fields from siblingsFrom on (the strips of the block being laid out), and field number `self` (the one
        // being lengthened), don't count as in the way.
        static bool Fits(FieldPlot plot, int siblingsFrom = int.MaxValue, int self = -1)
        {
            // Sample the plot every few metres, corners included.
            for (float u = -plot.halfLength; u <= plot.halfLength + 0.01f; u += plot.halfLength / 5f)
                for (float v = -plot.halfWidth; v <= plot.halfWidth + 0.01f; v += plot.halfWidth / 4f)
                    if (Blocked(plot.Point(u, v))) return false;
            float reach = Reach(plot);
            if (planSeen.Length < Fields.Count) planSeen = new int[Fields.Count * 2 + 64];
            planStamp++;
            float r = reach + 2f;
            for (int cz = PlanCellAt(plot.center.y - r); cz <= PlanCellAt(plot.center.y + r); cz++)
                for (int cx = PlanCellAt(plot.center.x - r); cx <= PlanCellAt(plot.center.x + r); cx++)
                {
                    var near = planCells[cz * PlanCellCount + cx];
                    if (near == null) continue;
                    foreach (int i in near)
                    {
                        if (i >= siblingsFrom || i == self || planSeen[i] == planStamp) continue;
                        planSeen[i] = planStamp;
                        var other = Fields[i];
                        float apart = reach + Reach(other) + 2f;
                        if ((other.center - plot.center).sqrMagnitude < apart * apart && Overlaps(plot, other, 2f)) return false;
                    }
                }
            foreach (var other in Plan.orchards)
                if (Overlaps(plot, other, 3f)) return false;
            return true;
        }

        // How far a plot reaches from its middle, at most.
        static float Reach(FieldPlot f)
        {
            float across = f.halfWidth * (1f + Mathf.Abs(f.taper)) + Mathf.Abs(f.shear) * f.halfLength;
            return Mathf.Sqrt(f.halfLength * f.halfLength + across * across) + FieldPlot.WarpMax;
        }

        // Which 2 m squares of the map no field may touch (roads and their verges, water, the dam and works, sites,
        // villages, the edge), worked out once per sector so the planner's thousands of tries look it up.
        const float BlockCell = 2f;
        static bool[] blocked;
        static int blockCount;

        static void MapBlocked()
        {
            blockCount = Mathf.CeilToInt(2f * Edge / BlockCell);
            blocked = new bool[blockCount * blockCount];
            // A point of a square is up to 1.4 m from its middle, but every limit here already keeps a metre or two in
            // hand (a road's verge, the shore), so half a metre's slack is enough.
            const float slack = 0.5f;
            System.Threading.Tasks.Parallel.For(0, blockCount, j => // rows in parallel: these only read the lines and plan
            {
                for (int i = 0; i < blockCount; i++)
                {
                    var p = new Vector2(-Edge + (i + 0.5f) * BlockCell, -Edge + (j + 0.5f) * BlockCell);
                    blocked[j * blockCount + i] = Mathf.Abs(p.x) > Edge - 15f - slack || Mathf.Abs(p.y) > Edge - 15f - slack
                        || RoadField.Distance(p.x, p.y) < RoadHalfWidth + 2f + slack || TrackField.Distance(p.x, p.y) < 2.5f + slack
                        || (HasRiver && RiverField.Distance(p.x, p.y) < RiverHalfWidth + 6f + slack)
                        || LakeDistance(p.x, p.y) < 16f + slack || NearDam(p, 15f + slack) || OnWorks(p, 4f + slack)
                        || NearFlatSite(p, 4f + slack) || NearVillage(p, slack);
                }
            });
        }

        static bool Blocked(Vector2 p)
        {
            int i = Mathf.FloorToInt((p.x + Edge) / BlockCell), j = Mathf.FloorToInt((p.y + Edge) / BlockCell);
            return i < 0 || j < 0 || i >= blockCount || j >= blockCount || blocked[j * blockCount + i];
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

        // Half the field's reach along an axis (for a tapered or slanted one, a little more than it needs).
        static float Extent(FieldPlot f, Vector2 axis) =>
            Mathf.Abs(Vector2.Dot(f.along, axis)) * f.halfLength
            + Mathf.Abs(Vector2.Dot(f.Across, axis)) * (f.halfWidth * (1f + Mathf.Abs(f.taper)) + Mathf.Abs(f.shear) * f.halfLength);

        // A crop for a field at `at`. The valley is farmed in districts about 300 m across, one farm's land each,
        // and a farm grows mostly two crops (half its fields the first, a third the second), so neighbouring fields
        // agree more often than not and the land reads as farms rather than confetti.
        static Crop PickCrop(System.Random rng, Vector2 at)
        {
            uint farm = FarmAt(at);
            Crop main = (Crop)(farm % 5), second = (Crop)((farm / 5 + 1 + farm % 4) % 5);
            double r = rng.NextDouble();
            return r < 0.5 ? main : r < 0.83 ? second : PickCrop(rng);
        }

        // Which farm a point belongs to: the nearest of a lattice of farmsteads 300 m apart, each shifted up to 107 m.
        static uint FarmAt(Vector2 at)
        {
            const float spacing = 300f;
            int cx = Mathf.FloorToInt(at.x / spacing), cz = Mathf.FloorToInt(at.y / spacing);
            uint best = 0;
            float bestDistance = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    uint h = Hash(cx + dx, cz + dz);
                    var stead = new Vector2((cx + dx + 0.5f) * spacing + ((h & 255) / 255f - 0.5f) * spacing * 0.7f,
                                            (cz + dz + 0.5f) * spacing + (((h >> 8) & 255) / 255f - 0.5f) * spacing * 0.7f);
                    float d = (stead - at).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; best = h >> 16; }
                }
            return best;
        }

        static uint Hash(int x, int z)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(Plan.fieldSeed * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            return h ^ (h >> 15);
        }

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
        public static List<Vector2> Smooth(Vector2[] pts, float spacing)
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

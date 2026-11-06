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
        public ushort edges;                // world parcels: FieldEdge of each edge (FieldAppearance.EdgeUse); zero is a grass strip

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
            // World parcels already carry their shared boundaries; a sector-local warp would move them at travel.
            if (MapLayout.Plan.world != null) return Vector2.zero;
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
        // How far a village's plots reach back from its streets: the road, a 4-10 m front garden, the house and a
        // garden behind it, 30-65 m in all.
        public const float VillageReach = 55f;
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
            VillageField = null; // the last sector's streets
            DetailPlan = null;
        }

        public static float RiverHalfWidth => Plan.riverHalfWidth;

        // How much of the channel holds water here: a river narrows and widens along its course (0.7 to 1 of the
        // full width), leaving gravel and mud on the inside where it narrows.
        public static float RiverWidthAt(float x, float z)
        {
            if (Plan.world != null)
            {
                Plan.world.RiverDistance(new Vector2(x, z), out float width, out _);
                return width;
            }
            return RiverHalfWidth * (0.7f + 0.3f * Mathf.PerlinNoise(x * 0.035f + 41f, z * 0.035f + 17f));
        }
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
        // World bridges are built from their exact endpoints by Water, not World's legacy bridge builder.
        public static IReadOnlyList<Vector2> Bridges => Plan.world != null ? System.Array.Empty<Vector2>() : Plan.bridges;
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
        static float[] roadWidths;
        public static RoadNetwork Roads { get; private set; }

        // Widths follow the prepared road's segment indices, making highway grading and painting cheap.
        public static float RoadWidth(int segment) => roadWidths != null && segment >= 0 && segment < roadWidths.Length
            ? roadWidths[segment] : RoadHalfWidth;

        // Placement exclusions use the same width as the road's terrain and surface.
        public static float RoadWidthAt(float x, float z)
        {
            if (Plan.world == null) return RoadHalfWidth;
            RoadField.Query(x, z, out _, out int segment);
            return RoadWidth(segment);
        }
        public static PathField RailField { get; private set; }
        public static List<List<Vector2>> TrackLines { get; private set; }
        public static PathField TrackField { get; private set; }
        public static readonly List<Ground.FlatSite> FlatSites = new List<Ground.FlatSite>();
        public static readonly List<FieldPlot> Fields = new List<FieldPlot>();
        public static readonly List<FieldBoundary> Boundaries = new List<FieldBoundary>();
        internal static FarDetailData DetailPlan { get; private set; }
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
            VillageLayout.PrepareRecipe(p);
            DetailPlan = p.world == null ? null : WorldPlan.CaptureDetail(null);
            Lap("shared plan");

            FlatSites.Clear();
            FlatSites.Add(new Ground.FlatSite(p.basePosition, BaseRadius, true));
            FlatSites.Add(new Ground.FlatSite(p.enemyHqPosition, HqRadius, true));
            foreach (var d in p.defences) FlatSites.Add(new Ground.FlatSite(d.position, DefenceRadius, true));
            foreach (var s in p.sites)
                if (s.flat > 0f) FlatSites.Add(new Ground.FlatSite(new Vector3(s.position.x, 0f, s.position.y), s.flat, false));
            // Overlapping discs cover complete yards, not just their centres; Ground levels all of them to zero.
            foreach (var a in p.featureFlats)
            {
                int nu = Mathf.CeilToInt(a.halfLength * 2f / 64f), nv = Mathf.CeilToInt(a.halfWidth * 2f / 64f);
                float du = a.halfLength * 2f / nu, dv = a.halfWidth * 2f / nv;
                float radius = Mathf.Sqrt(du * du + dv * dv) * 0.5f + 3f;
                for (int u = 0; u < nu; u++)
                    for (int v = 0; v < nv; v++)
                    {
                        Vector2 c = a.Point(-a.halfLength + (u + 0.5f) * du, -a.halfWidth + (v + 0.5f) * dv);
                        FlatSites.Add(new Ground.FlatSite(new Vector3(c.x, 0f, c.y), radius, false));
                    }
            }
            if (p.Has(SectorFeature.Airstrip))
                for (float t = -p.airstripLength / 2f; t <= p.airstripLength / 2f + 0.1f; t += 24f)
                {
                    Vector2 c = p.airstripCenter + p.airstripAlong * t;
                    FlatSites.Add(new Ground.FlatSite(new Vector3(c.x, 0f, c.y), 21f, false));
                }

            RiverLine = p.river != null ? Smooth(p.river, 2f) : new List<Vector2>();
            DamDown = p.hasDam && RiverLine.Count > 1 ? (RiverLine[Mathf.Min(4, RiverLine.Count - 1)] - RiverLine[0]).normalized : Vector2.right;
            // The river field reaches far out so the hills can open into a valley around the river.
            RiverField = new PathField(p.world != null ? p.world.RiverLines()
                : (p.river != null ? new[] { RiverLine } : new List<Vector2>[0]), 80f * Scale.Distance, Ground.Size);
            Lap("river");
            RoadLines = new List<List<Vector2>>();
            foreach (var road in p.roads) RoadLines.Add(Smooth(road, 2f));
            Wander(RoadLines);
            VillageLayout.HoldFrontages(RoadLines);
            VillageAccess.Preserve(RoadLines);
            foreach (var street in p.featureStreets) RoadLines.Add(Smooth(street, 2f));
            Lap("roads");
            // Village clearance must query this sector's railways, never the previous sector's cached field.
            RailLines = new List<List<Vector2>>();
            foreach (var rail in p.railways) RailLines.Add(Smooth(rail, 2f));
            RailField = new PathField(RailLines, 12f, Ground.Size);
            PlanVillages();
            Lap("villages");
            RoadField = new PathField(RoadLines, 12f, Ground.Size);
            roadWidths = null;
            if (p.world != null)
            {
                roadWidths = new float[RoadField.points.Count];
                int offset = 0;
                for (int i = 0; i < RoadLines.Count; i++)
                {
                    float width = VillageLayout.RoadWidth(i);
                    for (int j = 0; j < RoadLines[i].Count; j++) roadWidths[offset++] = width;
                }
            }
            TrackLines = PlanTracks();
            VillageAccess.Preserve(TrackLines, true);
            TrackField = new PathField(TrackLines, 6f, Ground.Size);
            Roads = RoadLayout.Capture();
            Lap("road fields");

            PlanFields(new System.Random(p.fieldSeed));
            IndexFields();
            if (p.world != null) FieldTimings += $", farmland coverage {FarmlandCoverage():P1}";
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
                if (s.flat > 0f && !ReservedFeature(s.position, 0f)) places.Add((s.position, s.flat));
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

        // ---- Villages ----

        // Each village's streets: the stretch of its road it lines (250-450 m, cut short by water, a bridge, a site,
        // the map's edge or the next village) and one to three lanes off it, 70-140 m long. The lanes join the roads,
        // so the terrain wears them and every builder keeps off them. A farmstead, or a village off the roads, keeps
        // just its yard. Their own random numbers, so the rest of the map comes out as before.
        public static readonly List<List<Vector2>> VillageStreets = new List<List<Vector2>>();
        public static readonly List<int> VillageOfStreet = new List<int>(); // which village each street belongs to
        public static PathField VillageField { get; private set; }
        public static readonly List<VillagePlot> VillagePlots = new List<VillagePlot>();

        static void PlanVillages()
        {
            VillageStreets.Clear();
            VillageOfStreet.Clear();
            VillageField = null;
            VillagePlots.Clear();
            if (Plan.world != null)
            {
                VillageLayout.Project(DetailPlan.villages);
                VillageField = new PathField(VillageStreets, VillageReach + 120f, Ground.Size);
                return;
            }
            var villages = Plan.villages;
            var lanes = new List<List<Vector2>>();
            for (int v = 0; v < villages.Count; v++)
            {
                Vector2 center = villages[v];
                int road = -1, nearest = -1;
                float best = float.MaxValue;
                for (int k = 0; k < RoadLines.Count; k++)
                    for (int i = 0; i < RoadLines[k].Count; i++)
                    {
                        float d = (RoadLines[k][i] - center).sqrMagnitude;
                        if (d < best) { best = d; road = k; nearest = i; }
                    }
                if (Plan.farmsteads[v])
                {
                    VillageStreets.Add(new List<Vector2> { center, center + new Vector2(0.5f, 0f) });
                    VillageOfStreet.Add(v);
                    continue;
                }
                var rng = new System.Random(Plan.fieldSeed * 7919 + v * 104729);
                List<Vector2> street;
                if (road < 0 || best > 30f * 30f)
                {
                    // Off the roads: a lane of its own, 80 m through the centre.
                    float angle = Range(rng, 0f, Mathf.PI);
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    street = new List<Vector2>();
                    for (float t = -40f; t <= 40f; t += 2f) street.Add(center + dir * t);
                    lanes.Add(street);
                    road = -1;
                }
                else
                {
                    var line = RoadLines[road];
                    int first = StreetEnd(line, nearest, -1, Range(rng, 125f, 225f), v);
                    int last = StreetEnd(line, nearest, 1, Range(rng, 125f, 225f), v);
                    street = line.GetRange(first, last - first + 1);
                    if (street.Count < 2) street.Add(street[0] + new Vector2(0.5f, 0f));
                }
                VillageStreets.Add(street);
                VillageOfStreet.Add(v);

                // Lanes leave the middle of the street at about a right angle, on either side, bending a little.
                int count = 1 + rng.Next(3);
                for (int l = 0; l < count && street.Count > 20; l++)
                {
                    int at = Mathf.RoundToInt(Mathf.Lerp(0.2f, 0.8f, (l + 0.5f + Range(rng, -0.3f, 0.3f)) / count) * (street.Count - 1));
                    Vector2 dir = (street[Mathf.Min(at + 1, street.Count - 1)] - street[Mathf.Max(at - 1, 0)]).normalized;
                    float side = rng.NextDouble() < 0.5 ? 1f : -1f;
                    Vector2 heading = Rotate(new Vector2(-dir.y, dir.x) * side, Range(rng, -15f, 15f) * Mathf.Deg2Rad);
                    float length = Range(rng, 70f, 140f), bend = Range(rng, 2f, 6f), period = Range(rng, 40f, 90f);
                    Vector2 laneSide = new Vector2(-heading.y, heading.x);
                    var lane = new List<Vector2> { street[at] };
                    for (float walked = 2f; walked <= length; walked += 2f)
                    {
                        Vector2 q = street[at] + heading * walked + laneSide * (bend * (1f - Mathf.Cos(Mathf.PI * walked / period)) * .5f);
                        if (StreetBlocked(q, v) || (walked > 12f && NearOtherRoad(q, road, 16f))) break;
                        lane.Add(q);
                    }
                    if (lane.Count < 20) continue; // under 40 m: not worth a lane
                    lanes.Add(lane);
                    VillageStreets.Add(lane);
                    VillageOfStreet.Add(v);
                    Junctions.Add(lane[0]);
                }
            }
            RoadLines.AddRange(lanes); // after the sector's own roads, whose numbers the missions use
            // Reaches far enough for the widest question asked of it (FieldParcels: smaller fields within 110 m).
            VillageField = new PathField(VillageStreets, VillageReach + 120f, Ground.Size);
            VillageLayout.Legacy();
        }

        // The last point a street reaches walking from `from` along the road (step +1 or -1), up to `length` metres.
        // The first 40 m always count: that's the village's own core, which the old 45 m villages kept, even by a
        // bridge or the water.
        static int StreetEnd(List<Vector2> line, int from, int step, float length, int village)
        {
            int i = from;
            float walked = 0f;
            while (i + step >= 0 && i + step < line.Count)
            {
                Vector2 q = line[i + step];
                walked += Vector2.Distance(line[i], q);
                if (walked > length || (walked > 40f && StreetBlocked(q, village))) break;
                i += step;
            }
            return i;
        }

        // Where a village's houses can't go: by water, a bridge or the dam, on a works site, close to a flat site (the
        // base, the HQ, emplacements), near the map's edge or another village's centre.
        static bool StreetBlocked(Vector2 q, int village)
        {
            if (Mathf.Abs(q.x) > Edge - 30f || Mathf.Abs(q.y) > Edge - 30f) return true;
            if (LakeDistance(q.x, q.y) < 12f || (HasRiver && RiverField.Distance(q.x, q.y) < RiverHalfWidth + 12f)) return true;
            if (NearDam(q, 30f) || InGorge(q, 20f) || OnWorks(q, 20f) || NearFlatSite(q, 30f)) return true;
            foreach (var b in Plan.bridges)
                if (Vector2.Distance(q, b) < 30f) return true;
            for (int u = 0; u < Plan.villages.Count; u++)
                if (u != village && Vector2.Distance(q, Plan.villages[u]) < 2f * VillageReach + 20f) return true;
            return false;
        }

        // Whether a road other than `own` passes within `distance` of q (RoadField isn't built yet).
        static bool NearOtherRoad(Vector2 q, int own, float distance)
        {
            float limit = distance * distance;
            for (int k = 0; k < RoadLines.Count; k++)
            {
                if (k == own) continue;
                foreach (var r in RoadLines[k])
                    if ((r - q).sqrMagnitude < limit) return true;
            }
            return false;
        }

        // ---- Questions the builders ask ----

        // How much of the farmed valley floor (the middle 1250 m square, less water, villages and sites) is under
        // fields, sampled every 6 m. Kept as the same survey for before/after comparisons; district occupancy is separate.
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

        // Survey all eligible farmland, including land where parcel fitting failed; exclusions never shrink to
        // the accepted crops. The eight metre grid uses the field index already needed by scenery placement.
        public static float FarmlandCoverage()
        {
            if (Plan.world == null) return FieldCoverage();
            int land = 0, filled = 0;
            for (float z = -Ground.Size * .5f + 3f; z < Ground.Size * .5f; z += 8f)
                for (float x = -Ground.Size * .5f + 3f; x < Ground.Size * .5f; x += 8f)
                {
                    var p = new Vector2(x, z);
                    if (Plan.world.CoverAt(x, z) != Cover.Farmland || Blocked(p)) continue;
                    land++;
                    if (InField(p, 0f)) filled++;
                }
            return land > 0 ? filled / (float)land : 0f;
        }

        public static bool NearFlatSite(Vector2 p, float margin)
        {
            foreach (var s in FlatSites)
                if (Vector2.Distance(p, new Vector2(s.center.x, s.center.z)) < s.radius + margin) return true;
            return false;
        }

        // Within margin of a village's houses and gardens: the strip either side of its streets once they're planned
        // (fields, woods and scrub keep out of it), the old circle round its centre before.
        public static bool NearVillage(Vector2 p, float margin)
        {
            if (Plan.world != null && DetailPlan != null)
            {
                if (DetailPlan.villages.Near(p + Plan.world.center, margin)) return true;
                for (int i = 0; i < Plan.villages.Count; i++)
                    if (Plan.farmsteads[i] && Vector2.Distance(p, Plan.villages[i]) < VillageRadius + margin) return true;
                return false;
            }
            if (VillageField != null) return VillageField.Distance(p.x, p.y) < VillageReach + margin;
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
        public static float LakeDistance(float x, float z) => Plan.world != null
            ? Plan.world.LakeDistance(new Vector2(x, z), out _)
            : Mathf.Min(ReservoirDistance(x, z), PondDistance(x, z, out _));

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
            if (Plan.world != null)
                return Plan.world.LakeDistance(new Vector2(x, z), out float level) <= 0f ? level : float.NegativeInfinity;
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
            if (Plan.world != null)
            {
                foreach (var dam in Plan.world.dams)
                    if (Vector2.Distance(p, Vector2.Lerp(dam.a, dam.b, WorldSectors.Projection(p, dam.a, dam.b))) < 14f + margin) return true;
                return false;
            }
            Vector2 local = DamLocal(p.x, p.y);
            return local.x > -12f - margin && local.x < 14f + margin && Mathf.Abs(local.y) < 52f + margin;
        }

        // The quarry pit, the airstrip and the railway, which trees and fields keep off.
        public static bool OnWorks(Vector2 p, float margin)
        {
            var plan = Plan;
            if (ReservedFeature(p, Mathf.Max(0f, margin))) return true;
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

        // The dominant land use owns its whole footprint before fields, village blocks, rocks or ordinary woods.
        public static bool ReservedFeature(Vector2 p, float margin)
        {
            foreach (var area in Plan.reservedFeatures) if (area.Contains(p, margin)) return true;
            return false;
        }

        // Open ground for trees and rocks: not on a road, in the river or lake, on a field, in a village or a base.
        public static bool IsWild(Vector2 p, float margin)
        {
            if (Mathf.Abs(p.x) > Edge || Mathf.Abs(p.y) > Edge) return false;
            if (LakeDistance(p.x, p.y) < 1.5f + margin || NearDam(p, margin) || InGorge(p, margin)) return false;
            if (RoadField.Distance(p.x, p.y) < RoadWidthAt(p.x, p.y) + 1.5f + margin) return false;
            if (TrackField.Distance(p.x, p.y) < 1.5f + margin) return false;
            if (HasRiver && RiverField.Distance(p.x, p.y) < RiverHalfWidth + 2.5f + margin) return false;
            if (OnWorks(p, margin)) return false;
            // Trees keep 10 m back from the base and the HQ, so their walls, pads and vehicles stay in plain view.
            if (Vector2.Distance(p, Flat(Plan.basePosition)) < BaseRadius + 10f + margin
                || Vector2.Distance(p, Flat(Plan.enemyHqPosition)) < HqRadius + 10f + margin) return false;
            return !NearFlatSite(p, 3f + margin) && !NearVillage(p, margin) && !InField(p, 1f + margin);
        }

        // ---- Fields ----

        // Tiles contiguous farm districts, then indexes the same FieldPlots all existing builders already read.
        static void PlanFields(System.Random rng)
        {
            Fields.Clear();
            Boundaries.Clear();
            FieldParcels.Districts.Clear();
            fieldCells = null;
            FieldTimings = "";
            if (Plan.fields <= 0f && Plan.world == null) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            MapBlocked();
            long blockedMs = clock.ElapsedMilliseconds;
            FieldParcels.Plan(rng, Blocked);
            FieldTimings = $"blocked map {blockedMs} ms, parcels {clock.ElapsedMilliseconds - blockedMs} ms, district occupancy {FieldParcels.DistrictCoverage:P0}";
        }

        public static string FieldTimings { get; private set; } = "";

        // Which 2 m squares of the map no field may touch (roads and their verges, water, the dam and works, sites,
        // villages, the edge), worked out once per sector so the planner's thousands of tries look it up.
        const float BlockCell = 2f;
        static bool[] blocked;
        static int blockCount;
        static float fieldReach;

        static void MapBlocked()
        {
            FieldPlot.Warp(Vector2.zero); // initialise before the parallel orchard queries
            FieldParcels.PrepareTerrain();
            fieldReach = Plan.world != null ? Ground.Size * .5f + BlockCell : Edge;
            blockCount = Mathf.CeilToInt(2f * fieldReach / BlockCell);
            blocked = new bool[blockCount * blockCount];
            // A point of a square is up to 1.4 m from its middle, but every limit here already keeps a metre or two in
            // hand (a road's verge, the shore), so half a metre's slack is enough.
            const float slack = 0.5f;
            System.Threading.Tasks.Parallel.For(0, blockCount, j => // rows in parallel: these only read the lines and plan
            {
                for (int i = 0; i < blockCount; i++)
                {
                    var p = new Vector2(-fieldReach + (i + 0.5f) * BlockCell, -fieldReach + (j + 0.5f) * BlockCell);
                    // Most lake/forest cells cannot grow a field; reject them before shoreline, village and slope queries.
                    blocked[j * blockCount + i] = (Plan.world != null && Plan.world.CoverAt(p.x, p.y) != Cover.Farmland)
                        || (Plan.world == null && (Mathf.Abs(p.x) > Edge - 15f - slack || Mathf.Abs(p.y) > Edge - 15f - slack))
                        || RoadField.Distance(p.x, p.y) < RoadWidthAt(p.x, p.y) + 2f + slack || TrackField.Distance(p.x, p.y) < 2.5f + slack
                        || (HasRiver && RiverField.Distance(p.x, p.y) < RiverHalfWidth + 6f + slack)
                        || LakeDistance(p.x, p.y) < 16f + slack || NearDam(p, 15f + slack) || OnWorks(p, 4f + slack)
                        || NearFlatSite(p, 4f + slack) || NearVillage(p, slack) || FieldParcels.Steep(p);
                    if (!blocked[j * blockCount + i])
                        foreach (var orchard in Plan.orchards)
                            if (orchard.Contains(p, 3f)) { blocked[j * blockCount + i] = true; break; }
                }
            });
        }

        static bool Blocked(Vector2 p)
        {
            int i = Mathf.FloorToInt((p.x + fieldReach) / BlockCell), j = Mathf.FloorToInt((p.y + fieldReach) / BlockCell);
            return i < 0 || j < 0 || i >= blockCount || j >= blockCount || blocked[j * blockCount + i];
        }

        // Half the field's reach along an axis (for a tapered or slanted one, a little more than it needs).
        static float Extent(FieldPlot f, Vector2 axis) =>
            Mathf.Abs(Vector2.Dot(f.along, axis)) * f.halfLength
            + Mathf.Abs(Vector2.Dot(f.Across, axis)) * (f.halfWidth * (1f + Mathf.Abs(f.taper)) + Mathf.Abs(f.shear) * f.halfLength);

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

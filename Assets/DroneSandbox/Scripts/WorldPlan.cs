using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // What covers the ground in the world plan: the far terrain's colours, and what each cell's detail grows from.
    public enum Cover : byte { Pasture, Farmland, Forest, Wetland, Water, Town, Village, Rock }

    public enum RoadClass : byte { Highway, Road, Track }

    public enum WorldSiteKind : byte { Airbase, LaunchSite, EnemyHq, EnemyDepot, Landmark }

    // A river or stream, its centre line running downstream, with its half-width at each point (metres).
    public sealed class WorldRiver
    {
        public string name;
        public readonly List<Vector2> points = new List<Vector2>();
        public readonly List<float> halfWidths = new List<float>();
    }

    public sealed class WorldRoad
    {
        public RoadClass kind;
        public readonly List<Vector2> points = new List<Vector2>();
    }

    public sealed class WorldSettlement
    {
        public string name;
        public Vector2 center;
        public float radius; // how far its houses reach
        public bool town;
    }

    public sealed class WorldSite
    {
        public WorldSiteKind kind;
        public string name;
        public Vector2 position;
        public bool norvane; // whose it is
    }

    // One 1 km cell's character, for the detail generator: the kind of sector it reads as and its own seed.
    public struct WorldCell
    {
        public int x, z;
        public SectorKind kind;
        public int seed;
        public float forest, fields, water; // shares of the cell's ground
    }

    // The whole theatre as one continuous world: the owner's 1,200 square miles, a square about 56 km on a side,
    // in world metres centred on (0, 0), +x east and +z north. Norvane holds the west, the Dravin Coalition the
    // east. Generated once from a seed (the same world every time) and asked by everything that builds or simulates
    // it: the far terrain, the detailed cells, drones far from the camera. Pure data, no Unity objects: once
    // Generate has returned it can be read from any thread.
    //
    // This first version is Claude's stand-in, so the far terrain and flight can be built against the API: noise
    // hills with a northern range, a main river with tributaries carving their valleys, towns and villages joined
    // by roads, forests, a front line and the military sites. Codex's macro generator (drainage-led rivers,
    // planned road network, farm districts) replaces the inside of Generate behind the same members.
    public static class WorldPlan
    {
        public const float Size = 56000f;    // metres on a side: 1,200 square miles (one constant, in case it changes)
        public const float Half = Size / 2f;
        public const float CellSize = 1000f;
        public const int CellCount = 56;     // cells on a side (Size / CellSize)
        public const float HeightStep = 32f; // the elevation grid's spacing
        public const int GeneratorVersion = 1;

        public static int Seed { get; private set; }
        public static bool Ready { get; private set; }

        public static readonly List<WorldRiver> Rivers = new List<WorldRiver>();
        public static readonly List<WorldRoad> Roads = new List<WorldRoad>();
        public static readonly List<WorldSettlement> Settlements = new List<WorldSettlement>();
        public static readonly List<WorldSite> Sites = new List<WorldSite>();
        public static readonly List<Vector2> FrontLine = new List<Vector2>(); // north to south

        const int GridCount = 1751; // Size / HeightStep + 1 samples on a side
        static float[] heights, riverDistance;
        static Cover[] cover;
        static Vector2 noise; // the seed's offset into the noise, so each seed makes its own land

        public static bool Inside(float x, float z) => Mathf.Abs(x) <= Half && Mathf.Abs(z) <= Half;

        // Builds the whole plan (about a second, mostly in parallel). Safe to call again with another seed.
        public static void Generate(int seed)
        {
            Ready = false;
            Seed = seed;
            var rng = new System.Random(seed);
            noise = new Vector2(Range(rng, 0f, 5000f), Range(rng, 0f, 5000f));
            Rivers.Clear();
            Roads.Clear();
            Settlements.Clear();
            Sites.Clear();
            FrontLine.Clear();
            PlanRivers(rng);
            StampRivers();
            PlanHeights();
            PlanSettlements(rng);
            PlanRoads();
            PlanFront(rng);
            PlanSites(rng);
            PlanCover();
            Ready = true;
        }

        // The ground's height in metres at a world point: the 32 m grid, smoothly interpolated. Outside the world it
        // holds the edge's height.
        public static float Height(float x, float z) => SampleGrid(heights, x, z);

        // How far the nearest river's centre line is, out to 2 km.
        public static float RiverDistance(float x, float z) => SampleGrid(riverDistance, x, z);

        public static Cover CoverAt(float x, float z)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt((x + Half) / HeightStep), 0, GridCount - 1);
            int j = Mathf.Clamp(Mathf.RoundToInt((z + Half) / HeightStep), 0, GridCount - 1);
            return cover[j * GridCount + i];
        }

        // The cell at grid position (cx, cz), 0-55 from the south-west corner: what its ground is, the sector kind
        // it reads as and its seed (from the world's seed, the cell and the generator version only).
        public static WorldCell Cell(int cx, int cz)
        {
            var cell = new WorldCell { x = cx, z = cz };
            int forest = 0, fields = 0, water = 0, towns = 0, wet = 0, total = 0;
            float low = float.MaxValue, high = float.MinValue;
            for (float dz = 16f; dz < CellSize; dz += HeightStep)
                for (float dx = 16f; dx < CellSize; dx += HeightStep)
                {
                    float x = -Half + cx * CellSize + dx, z = -Half + cz * CellSize + dz;
                    var c = CoverAt(x, z);
                    if (c == Cover.Forest) forest++;
                    else if (c == Cover.Farmland) fields++;
                    else if (c == Cover.Water) water++;
                    else if (c == Cover.Town || c == Cover.Village) towns++;
                    else if (c == Cover.Wetland) wet++;
                    float h = Height(x, z);
                    low = Mathf.Min(low, h);
                    high = Mathf.Max(high, h);
                    total++;
                }
            cell.forest = forest / (float)total;
            cell.fields = fields / (float)total;
            cell.water = water / (float)total;
            float centerX = -Half + (cx + 0.5f) * CellSize, centerZ = -Half + (cz + 0.5f) * CellSize;
            bool river = RiverDistance(centerX, centerZ) < 450f;
            cell.kind = towns > total / 4 ? SectorKind.MarketTown
                : towns > 0 ? (river ? SectorKind.RiverCrossing : SectorKind.CrossroadsHamlet)
                : cell.forest > 0.6f ? SectorKind.DeepForest
                : cell.forest > 0.3f ? SectorKind.ForestEdge
                : wet > total / 5 ? SectorKind.Marshland
                : river ? SectorKind.RiverValley
                : high - low > 90f ? SectorKind.HillCountry
                : SectorKind.RollingFarmland;
            unchecked { cell.seed = ((Seed * 73856093) ^ (cx * 19349663) ^ (cz * 83492791) ^ (GeneratorVersion * 1000003)) & 0x7fffffff; }
            return cell;
        }

        // ---- Rivers ----

        // The Lissen, winding north to south through the west of the middle, and three tributaries from the
        // eastern hills, each a chain of 200 m steps that wander a little at a time.
        static void PlanRivers(System.Random rng)
        {
            var main = new WorldRiver { name = "Lissen" };
            Vector2 p = new Vector2(Range(rng, -9000f, -3000f), Half + 500f);
            float heading = -Mathf.PI / 2f;
            while (p.y > -Half - 500f)
            {
                main.points.Add(p);
                main.halfWidths.Add(Mathf.Lerp(9f, 24f, Mathf.InverseLerp(Half, -Half, p.y)));
                heading += Range(rng, -0.35f, 0.35f);
                heading = Mathf.Lerp(heading, -Mathf.PI / 2f, 0.25f); // keeps it heading south
                p += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * 200f;
                p.x = Mathf.Clamp(p.x, -16000f, 8000f);
            }
            Rivers.Add(main);
            string[] names = { "Venn", "Oster", "Callow" };
            for (int t = 0; t < 3; t++)
            {
                var tributary = new WorldRiver { name = names[t] };
                Vector2 q = new Vector2(Range(rng, 12000f, Half - 2000f), Range(rng, -18000f, 20000f));
                for (int step = 0; step < 400; step++)
                {
                    tributary.points.Add(q);
                    tributary.halfWidths.Add(Mathf.Lerp(3f, 8f, step / 120f));
                    int nearest = NearestPoint(main.points, q);
                    Vector2 to = main.points[nearest] - q;
                    if (to.magnitude < 200f)
                    {
                        tributary.points.Add(main.points[nearest]);
                        tributary.halfWidths.Add(8f);
                        break;
                    }
                    float aim = Mathf.Atan2(to.y, to.x) + Range(rng, -0.6f, 0.6f);
                    q += new Vector2(Mathf.Cos(aim), Mathf.Sin(aim)) * 200f;
                }
                Rivers.Add(tributary);
            }
        }

        // Distance to the nearest river centre line on the height grid, each segment stamped out to 2 km.
        static void StampRivers()
        {
            const float reach = 2000f;
            riverDistance = new float[GridCount * GridCount];
            for (int k = 0; k < riverDistance.Length; k++) riverDistance[k] = reach;
            foreach (var river in Rivers)
                for (int s = 1; s < river.points.Count; s++)
                {
                    Vector2 a = river.points[s - 1], b = river.points[s];
                    int i0 = GridIndex(Mathf.Min(a.x, b.x) - reach), i1 = GridIndex(Mathf.Max(a.x, b.x) + reach);
                    int j0 = GridIndex(Mathf.Min(a.y, b.y) - reach), j1 = GridIndex(Mathf.Max(a.y, b.y) + reach);
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            float d = SegmentDistance(new Vector2(-Half + i * HeightStep, -Half + j * HeightStep), a, b);
                            if (d < riverDistance[j * GridCount + i]) riverDistance[j * GridCount + i] = d;
                        }
                }
        }

        // ---- Heights ----

        // Rolling lowland of 0-45 m, broad hills rising towards the east (the Dravin uplands), a range along the
        // north edge up to about 500 m, and every river in a valley it has cut: the floor 1-2 km wide, the river at
        // its lowest.
        static void PlanHeights()
        {
            heights = new float[GridCount * GridCount];
            System.Threading.Tasks.Parallel.For(0, GridCount, j =>
            {
                for (int i = 0; i < GridCount; i++)
                {
                    float x = -Half + i * HeightStep, z = -Half + j * HeightStep;
                    float u = x + noise.x, v = z + noise.y;
                    float h = 45f * Fbm(u / 9000f, v / 9000f, 3);
                    float uplands = Mathf.SmoothStep(0f, 1f, (x + 4000f) / 22000f) * (0.6f + 0.4f * Mathf.PerlinNoise(u / 20000f + 3f, v / 20000f));
                    h += uplands * 170f * Ridge(u / 6000f, v / 6000f);
                    float north = Mathf.SmoothStep(0f, 1f, (z - 14000f) / 12000f);
                    h += north * 420f * Ridge(u / 8000f + 11f, v / 8000f + 4f);
                    float river = riverDistance[j * GridCount + i];
                    float valley = 1f - Mathf.SmoothStep(0f, 1f, (river - 150f) / 1400f);
                    h = Mathf.Lerp(h, Mathf.Min(h, 4f + river * 0.004f), valley);
                    heights[j * GridCount + i] = h;
                }
            });
        }

        // ---- Settlements, roads, the front and the sites ----

        static readonly string[] TownNames = { "Halvard", "Brenmoor", "Ostrava Vale" };
        static readonly string[] VillageNames =
        {
            "Esterhay", "Marrowfield", "Tolbeck", "Grisham", "Wenlow", "Carrick", "Dunmere", "Ashcombe", "Fallow Cross",
            "Redwick", "Sorrel", "Hythe Green", "Kelmarsh", "Orrin", "Lowfold", "Brackwater",
        };

        // Three towns and sixteen villages on low, gentle ground, most near a river or within reach of one, none
        // closer than 2.5 km to another.
        static void PlanSettlements(System.Random rng)
        {
            int towns = 0, villages = 0;
            for (int attempt = 0; attempt < 4000 && villages < VillageNames.Length; attempt++)
            {
                bool town = towns < TownNames.Length;
                var c = new Vector2(Range(rng, -Half + 3000f, Half - 3000f), Range(rng, -Half + 3000f, Half - 3000f));
                if (Height(c.x, c.y) > 120f || Slope(c.x, c.y) > 6f) continue;
                if (RiverDistance(c.x, c.y) < 120f || (town && RiverDistance(c.x, c.y) > 900f)) continue;
                bool crowded = false;
                foreach (var s in Settlements)
                    if (Vector2.Distance(s.center, c) < (town || s.town ? 5000f : 2500f)) crowded = true;
                if (crowded) continue;
                Settlements.Add(new WorldSettlement
                {
                    name = town ? TownNames[towns] : VillageNames[villages],
                    center = c,
                    radius = town ? Range(rng, 700f, 1000f) : Range(rng, 220f, 380f),
                    town = town,
                });
                if (town) towns++;
                else villages++;
            }
        }

        // A highway joining the towns north to south, and a road from every village to its nearest neighbour
        // that's already joined, so the network is connected; each road bends a little on the way.
        static void PlanRoads()
        {
            var townList = new List<WorldSettlement>();
            foreach (var s in Settlements)
                if (s.town) townList.Add(s);
            townList.Sort((a, b) => b.center.y.CompareTo(a.center.y));
            if (townList.Count > 0)
            {
                var highway = new WorldRoad { kind = RoadClass.Highway };
                highway.points.Add(new Vector2(townList[0].center.x, Half));
                foreach (var t in townList) highway.points.Add(t.center);
                highway.points.Add(new Vector2(townList[townList.Count - 1].center.x, -Half));
                Roads.Add(Bend(highway, 3000f));
            }
            var joined = new List<WorldSettlement>(townList);
            var waiting = new List<WorldSettlement>();
            foreach (var s in Settlements)
                if (!s.town) waiting.Add(s);
            while (waiting.Count > 0)
            {
                int bestWaiting = 0;
                WorldSettlement bestJoined = null;
                float best = float.MaxValue;
                for (int w = 0; w < waiting.Count; w++)
                    foreach (var j in joined)
                    {
                        float d = Vector2.Distance(waiting[w].center, j.center);
                        if (d < best) { best = d; bestWaiting = w; bestJoined = j; }
                    }
                var road = new WorldRoad { kind = RoadClass.Road };
                road.points.Add(waiting[bestWaiting].center);
                if (bestJoined != null) road.points.Add(bestJoined.center);
                else road.points.Add(waiting[bestWaiting].center + new Vector2(4000f, 0f));
                Roads.Add(Bend(road, 1200f));
                joined.Add(waiting[bestWaiting]);
                waiting.RemoveAt(bestWaiting);
            }
        }

        // The line between the armies, north to south, a few kilometres east of the middle: it bulges and dents
        // where the fighting has gone one way or the other.
        static void PlanFront(System.Random rng)
        {
            float phase = Range(rng, 0f, 10f);
            for (float z = Half; z >= -Half - 1f; z -= 500f)
                FrontLine.Add(new Vector2(4000f + 2500f * Mathf.Sin(z / 9000f + phase) + 900f * (Mathf.PerlinNoise(z / 2500f + phase, 3f) - 0.5f), z));
        }

        // Norvane's main airbase well back in the west, ten forward launch sites 8-12 km apart behind the front,
        // and the Dravin front headquarters and three depots behind theirs.
        static void PlanSites(System.Random rng)
        {
            Sites.Add(new WorldSite { kind = WorldSiteKind.Airbase, name = "Norvane Air Base", position = FlatSpot(rng, -21000f, 0f, 4000f), norvane = true });
            for (int k = 0; k < 10; k++)
            {
                float z = Mathf.Lerp(Half - 4000f, -Half + 4000f, (k + 0.5f) / 10f);
                Vector2 front = FrontAt(z);
                Sites.Add(new WorldSite
                {
                    kind = WorldSiteKind.LaunchSite,
                    name = $"Strip {(char)('A' + k)}",
                    position = FlatSpot(rng, front.x - Range(rng, 5000f, 9000f), z, 1500f),
                    norvane = true,
                });
            }
            Sites.Add(new WorldSite { kind = WorldSiteKind.EnemyHq, name = "Dravin Front Command", position = FlatSpot(rng, FrontAt(0f).x + 9000f, Range(rng, -6000f, 6000f), 3000f) });
            for (int k = 0; k < 3; k++)
                Sites.Add(new WorldSite
                {
                    kind = WorldSiteKind.EnemyDepot,
                    name = $"Depot {k + 1}",
                    position = FlatSpot(rng, FrontAt(0f).x + Range(rng, 5000f, 14000f), Range(rng, -Half + 6000f, Half - 6000f), 3000f),
                });
        }

        public static Vector2 FrontAt(float z)
        {
            for (int k = 1; k < FrontLine.Count; k++)
                if (FrontLine[k].y <= z)
                    return Vector2.Lerp(FrontLine[k - 1], FrontLine[k], Mathf.InverseLerp(FrontLine[k - 1].y, FrontLine[k].y, z));
            return FrontLine.Count > 0 ? FrontLine[FrontLine.Count - 1] : Vector2.zero;
        }

        // ---- Cover ----

        // Towns and villages where they are; rivers wide enough to show; rock on steep high ground; forest in broad
        // drifts, thicker on hills and slopes and thinner near settlements; damp ground on the low flats by rivers;
        // fields on gentle lowland near roads and villages; pasture between.
        static void PlanCover()
        {
            cover = new Cover[GridCount * GridCount];
            System.Threading.Tasks.Parallel.For(0, GridCount, j =>
            {
                for (int i = 0; i < GridCount; i++)
                {
                    float x = -Half + i * HeightStep, z = -Half + j * HeightStep;
                    float h = heights[j * GridCount + i], slope = Slope(x, z), river = riverDistance[j * GridCount + i];
                    float settled = float.MaxValue;
                    Cover c = Cover.Pasture;
                    foreach (var s in Settlements)
                    {
                        float d = Vector2.Distance(s.center, new Vector2(x, z));
                        if (d < s.radius) c = s.town ? Cover.Town : Cover.Village;
                        settled = Mathf.Min(settled, d - s.radius);
                    }
                    if (c == Cover.Pasture)
                    {
                        float u = x + noise.x, v = z + noise.y;
                        float woods = Fbm(u / 4000f + 40f, v / 4000f + 9f, 3) + 0.25f * Mathf.SmoothStep(0f, 1f, (h - 60f) / 150f)
                                    + 0.15f * Mathf.SmoothStep(0f, 1f, (slope - 8f) / 10f) - 0.3f * (1f - Mathf.SmoothStep(0f, 1f, settled / 1500f));
                        if (river < WidthAt(x, z)) c = Cover.Water;
                        else if (slope > 32f || h > 400f) c = Cover.Rock;
                        else if (woods > 0.62f) c = Cover.Forest;
                        else if (river < 500f && slope < 2f && Mathf.PerlinNoise(u / 900f, v / 900f) > 0.62f) c = Cover.Wetland;
                        else if (slope < 7f && h < 160f && Mathf.PerlinNoise(u / 2500f + 7f, v / 2500f + 2f) + 0.3f * (1f - Mathf.SmoothStep(0f, 1f, settled / 4000f)) > 0.5f) c = Cover.Farmland;
                    }
                    cover[j * GridCount + i] = c;
                }
            });
        }

        // ---- Helpers ----

        // The nearest river's half-width here (wide enough to be water on the 32 m grid only where it's broad).
        static float WidthAt(float x, float z)
        {
            float best = float.MaxValue, width = 0f;
            if (RiverDistance(x, z) > 60f) return 0f;
            var p = new Vector2(x, z);
            foreach (var river in Rivers)
            {
                for (int k = 0; k < river.points.Count; k++)
                {
                    float d = (river.points[k] - p).sqrMagnitude;
                    if (d < best) { best = d; width = river.halfWidths[k]; }
                }
            }
            return width;
        }

        // Steepness in degrees, from the height grid's neighbours.
        static float Slope(float x, float z)
        {
            float dx = Height(x + HeightStep, z) - Height(x - HeightStep, z);
            float dz = Height(x, z + HeightStep) - Height(x, z - HeightStep);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / (2f * HeightStep)) * Mathf.Rad2Deg;
        }

        static Vector2 FlatSpot(System.Random rng, float x, float z, float spread)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var p = new Vector2(x + Range(rng, -spread, spread), z + Range(rng, -spread, spread));
                p.x = Mathf.Clamp(p.x, -Half + 2000f, Half - 2000f);
                p.y = Mathf.Clamp(p.y, -Half + 2000f, Half - 2000f);
                if (Slope(p.x, p.y) < 4f && RiverDistance(p.x, p.y) > 300f) return p;
            }
            return new Vector2(x, z);
        }

        // A straight road broken into 250 m steps that drift sideways (up to `wander` metres) and settle at the ends.
        static WorldRoad Bend(WorldRoad road, float wander)
        {
            var bent = new WorldRoad { kind = road.kind };
            for (int k = 1; k < road.points.Count; k++)
            {
                Vector2 a = road.points[k - 1], b = road.points[k];
                float length = Vector2.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / 250f));
                Vector2 side = new Vector2(-(b - a).y, (b - a).x).normalized;
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    float drift = wander * Mathf.Sin(t * Mathf.PI) * (Mathf.PerlinNoise(a.x / 3000f + t * 2f, a.y / 3000f) - 0.5f);
                    bent.points.Add(Vector2.Lerp(a, b, t) + side * drift);
                }
            }
            bent.points.Add(road.points[road.points.Count - 1]);
            return bent;
        }

        static float SampleGrid(float[] grid, float x, float z)
        {
            float gx = Mathf.Clamp((x + Half) / HeightStep, 0f, GridCount - 1.001f);
            float gz = Mathf.Clamp((z + Half) / HeightStep, 0f, GridCount - 1.001f);
            int i = (int)gx, j = (int)gz;
            float fx = gx - i, fz = gz - j;
            float a = Mathf.Lerp(grid[j * GridCount + i], grid[j * GridCount + i + 1], fx);
            float b = Mathf.Lerp(grid[(j + 1) * GridCount + i], grid[(j + 1) * GridCount + i + 1], fx);
            return Mathf.Lerp(a, b, fz);
        }

        static int GridIndex(float coordinate) => Mathf.Clamp(Mathf.RoundToInt((coordinate + Half) / HeightStep), 0, GridCount - 1);

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        static int NearestPoint(List<Vector2> points, Vector2 p)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int k = 0; k < points.Count; k++)
            {
                float d = (points[k] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = k; }
            }
            return best;
        }

        static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amplitude * Mathf.PerlinNoise(x, y);
                total += amplitude;
                x = x * 2.03f + 17.1f;
                y = y * 2.03f + 9.7f;
                amplitude *= 0.5f;
            }
            return sum / total;
        }

        static float Ridge(float x, float y)
        {
            float n = Mathf.PerlinNoise(x, y);
            float fold = n * 2f - 1f;
            float r = Mathf.Clamp01(1.1f - Mathf.Sqrt(fold * fold + 0.02f));
            return r * r * 0.7f + n * 0.3f;
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}

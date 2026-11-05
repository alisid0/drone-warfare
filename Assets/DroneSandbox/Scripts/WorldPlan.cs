using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Ground cover shared by the distant landscape and the detailed cell generators.
    public enum Cover : byte { Pasture, Farmland, Forest, Wetland, Water, Town, Village, Rock }
    // The three road widths and levels of access used throughout the theatre.
    public enum RoadClass : byte { Highway, Road, Track }
    // Installations and landmarks that retain their identity when their cells unload.
    public enum WorldSiteKind : byte { Airbase, LaunchSite, EnemyHq, EnemyDepot, Landmark }

    // A centre line running downstream, with half-widths and absolute water elevations in metres.
    public sealed class WorldRiver
    {
        public string name;
        public readonly List<Vector2> points = new List<Vector2>();
        public readonly List<float> halfWidths = new List<float>();
        public readonly List<float> waterLevels = new List<float>();
        internal readonly List<float> valleyLevels = new List<float>(); // before lake impoundment
    }

    // A connected road centre line, sampled closely enough to preserve its bends when streamed.
    public sealed class WorldRoad
    {
        public RoadClass kind;
        public readonly List<Vector2> points = new List<Vector2>();
    }

    // A settlement's centre and nominal occupied radius; the cover grid supplies its irregular outline.
    public sealed class WorldSettlement
    {
        public string name;
        public Vector2 center;
        public float radius;
        public bool town;
    }

    // A persistent installation or navigational landmark, in global coordinates.
    public sealed class WorldSite
    {
        public WorldSiteKind kind;
        public string name;
        public Vector2 position;
        public bool norvane;
    }

    // A closed shoreline (last point implicitly joins first) around a level water surface.
    public sealed class WorldLake
    {
        public string name;
        public bool reservoir;
        public float level;
        public Vector2 outlet;
        public readonly List<Vector2> shore = new List<Vector2>();
    }

    // A dam joining the reservoir banks; crest and downstream tailwater are absolute elevations.
    public sealed class WorldDam
    {
        public string name;
        public Vector2 a, b;
        public float crest, tailwater;
    }

    // A crossing of an exported road or railway and a river, including the bank-to-bank deck ends.
    public sealed class WorldBridge
    {
        public Vector2 position, a, b;
        public float deckLevel;
        public bool railway;
        public int route, river;
    }

    // An isolated farm served by a track, outside the settlement's occupied ground.
    public sealed class WorldFarm
    {
        public Vector2 position;
    }

    // The sampled ground shares and persistent identity of one logical kilometre cell.
    public struct WorldCell
    {
        public int x, z;
        public SectorKind kind;
        public int seed;
        public float forest, fields, water;
    }

    // One 56 km square theatre, in metres about (0,0), +x east and +z north. Generate between worlds,
    // before starting readers. Afterwards queries allocate nothing and can run concurrently on workers.
    // The original public lists remain mutable for API compatibility: consumers must treat them as read-only.
    public static partial class WorldPlan
    {
        public const float Size = 56000f;
        public const float Half = Size / 2f;
        public const float CellSize = 1000f;
        public const int CellCount = 56;
        public const float HeightStep = 32f;
        public const int GeneratorVersion = 2;

        public static int Seed { get; private set; }
        public static bool Ready { get { return ready; } private set { ready = value; } }
        public static readonly List<WorldRiver> Rivers = new List<WorldRiver>();
        public static readonly List<WorldRoad> Roads = new List<WorldRoad>();
        public static readonly List<WorldSettlement> Settlements = new List<WorldSettlement>();
        public static readonly List<WorldSite> Sites = new List<WorldSite>();
        public static readonly List<Vector2> FrontLine = new List<Vector2>();
        public static readonly List<WorldRoad> Railways = new List<WorldRoad>();
        public static readonly List<WorldLake> Lakes = new List<WorldLake>();
        public static readonly List<WorldDam> Dams = new List<WorldDam>();
        public static readonly List<WorldBridge> Bridges = new List<WorldBridge>();
        public static readonly List<WorldFarm> Farms = new List<WorldFarm>();
        public static float FarmableSquareKm { get; private set; }
        public static float FarmedShare { get; private set; }

        const int GridCount = (int)(Size / HeightStep) + 1;
        const float RouteStep = 250f;
        const int RouteCount = (int)(Size / RouteStep) + 1;
        static readonly object generationLock = new object();
        static volatile bool ready;
        static float[] heights, riverDistance, waterLevel;
        static Cover[] cover;
        static WorldCell[] cells;
        static float[] routeHeights;
        static float phase, shiftX, shiftZ;
        static Vector2 deepForest;
        static readonly List<Vector2> peaks = new List<Vector2>();

        // Builds and publishes all data before readers start; serial regeneration retains no previous grids.
        public static void Generate(int seed)
        {
            lock (generationLock)
            {
                Ready = false;
                Seed = seed;
                var rng = new Random(seed);
                phase = Range(rng, 0f, 6.28f);
                shiftX = Range(rng, -10000f, 10000f);
                shiftZ = Range(rng, -10000f, 10000f);
                Rivers.Clear(); Roads.Clear(); Settlements.Clear(); Sites.Clear(); FrontLine.Clear();
                Railways.Clear(); Lakes.Clear(); Dams.Clear(); Bridges.Clear(); Farms.Clear(); peaks.Clear();
                PlanRelief(rng);
                BuildRouteHeights();
                PlanDrainage(rng);
                CarveDrainage();
                CarveLakes();
                CutChannels();
                BuildRouteHeights();
                PlanFront();
                PlanSettlements(rng);
                PlanRoads(rng);
                PlanSites(rng);
                PlanFarms(rng);
                ExportBridges();
                PlanCover();
                PlanCells();
                routeHeights = null;
                Ready = true;
            }
        }

        // The closed square boundary, shared by flight limits and streaming.
        public static bool Inside(float x, float z) { return Math.Abs(x) <= Half && Math.Abs(z) <= Half; }

        // Bilinear ground elevation, holding the exact edge height outside the theatre.
        public static float Height(float x, float z) { return SampleGrid(heights, x, z); }

        // Bilinear distance to the closest river centre line, capped at two kilometres.
        public static float RiverDistance(float x, float z) { return SampleGrid(riverDistance, x, z); }

        // Water elevation at the nearest grid sample, or NaN on dry land; use river vectors for sub-grid streams.
        public static float WaterLevel(float x, float z) { return waterLevel[GridIndex(z) * GridCount + GridIndex(x)]; }

        // A single grid lookup: cover never searches settlements, rivers or roads during terrain builds.
        public static Cover CoverAt(float x, float z) { return cover[GridIndex(z) * GridCount + GridIndex(x)]; }

        // Cell coordinates run from the south-west corner; out-of-world cells are programmer errors.
        public static WorldCell Cell(int cx, int cz)
        {
            if (cx < 0 || cz < 0 || cx >= CellCount || cz >= CellCount) throw new ArgumentOutOfRangeException("cx/cz");
            return cells[cz * CellCount + cx];
        }

        // Interpolates the north-to-south front in constant time, clamping beyond its ends.
        public static Vector2 FrontAt(float z)
        {
            if (FrontLine.Count == 0) return new Vector2();
            if (FrontLine.Count == 1) return FrontLine[0];
            float t = Clamp((Half - z) / 500f, 0f, FrontLine.Count - 1);
            int i = Math.Min((int)t, FrontLine.Count - 2);
            return Mix(FrontLine[i], FrontLine[i + 1], t - i);
        }

        // Samples both boundary rows exactly instead of stopping just short of the north/east edge.
        static float SampleGrid(float[] grid, float x, float z)
        {
            float gx = Clamp((x + Half) / HeightStep, 0f, GridCount - 1);
            float gz = Clamp((z + Half) / HeightStep, 0f, GridCount - 1);
            int i = Math.Min((int)gx, GridCount - 2), j = Math.Min((int)gz, GridCount - 2);
            int k = j * GridCount + i;
            return Lerp(Lerp(grid[k], grid[k + 1], gx - i), Lerp(grid[k + GridCount], grid[k + GridCount + 1], gx - i), gz - j);
        }

        // The nearest 32 m sample, with the same clamped boundaries as Height.
        static int GridIndex(float x) { return Math.Max(0, Math.Min(GridCount - 1, (int)Math.Floor((x + Half) / HeightStep + 0.5f))); }
        // Bounded interpolation factors keep all procedural masks continuous at their limits.
        static float Clamp(float x, float lo, float hi) { return Math.Max(lo, Math.Min(hi, x)); }
        // Linear interpolation without an implicit clamp, for already bounded grid coordinates.
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        // Smoothstep gives valley shoulders and terrain regions zero slope at their boundaries.
        static float Smooth(float t) { t = Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }
        // Vector interpolation uses only the managed Vector2 data fields and constructor.
        static Vector2 Mix(Vector2 a, Vector2 b, float t) { return new Vector2(Lerp(a.x, b.x, t), Lerp(a.y, b.y, t)); }
        // Euclidean distance is also used by route heuristics and minimum separation rules.
        static float Distance(Vector2 a, Vector2 b) { float x = a.x - b.x, z = a.y - b.y; return (float)Math.Sqrt(x * x + z * z); }
        // Randomness is consumed only while generating, never by queries or parallel workers.
        static float Range(Random rng, float lo, float hi) { return lo + (float)rng.NextDouble() * (hi - lo); }
        // The projection parameter is shared by bridge, road and river distance calculations.
        static float Projection(Vector2 p, Vector2 a, Vector2 b)
        {
            float dx = b.x - a.x, dz = b.y - a.y, d = dx * dx + dz * dz;
            return d > 0f ? Clamp(((p.x - a.x) * dx + (p.y - a.y) * dz) / d, 0f, 1f) : 0f;
        }
        // Ground gradient as rise/run, avoiding degree conversions in terrain suitability tests.
        static float Grade(float x, float z)
        {
            float dx = Height(x + 64f, z) - Height(x - 64f, z), dz = Height(x, z + 64f) - Height(x, z - 64f);
            return (float)Math.Sqrt(dx * dx + dz * dz) / 128f;
        }
        // A stable integer hash makes spatial noise and cell identities independent of query/worker order.
        static uint Hash(int x, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)Seed ^ (uint)x * 374761393u ^ (uint)z * 668265263u ^ (uint)salt * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }
        // Smooth value noise is wholly managed; its seed is not Unity's global random state.
        static float Noise(float x, float z, int salt)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            float fx = Smooth(x - ix), fz = Smooth(z - iz);
            const float scale = 1f / 16777215f;
            return Lerp(Lerp((Hash(ix, iz, salt) & 0xffffff) * scale, (Hash(ix + 1, iz, salt) & 0xffffff) * scale, fx),
                Lerp((Hash(ix, iz + 1, salt) & 0xffffff) * scale, (Hash(ix + 1, iz + 1, salt) & 0xffffff) * scale, fx), fz);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The kinds of sector along the front. Each one is a recipe (SectorType) the generator follows with a seed, so
    // every hotspot on the theatre map is its own playable area; the first is the hand-made Varrow Valley.
    public enum SectorKind
    {
        VarrowValley, RiverValley, RiverCrossing, ForestEdge, DeepForest, BirchWoods, RollingFarmland, Steppe,
        HillCountry, RidgePass, LakeDistrict, Reservoir, Marshland, Wetlands, Lakeshore, VillageCluster,
        TownOutskirts, MarketTown, IndustrialZone, RailJunction, RailLine, HighwayCorridor, CrossroadsHamlet,
        Airfield, QuarryHills, PowerStation, OrchardCountry, LoggingCamp, Moorland, BorderLine,
    }

    public enum RiverStyle { None, Stream, River, WideRiver }
    public enum RoadStyle { Crossroads, Through, Grid, Tracks }

    [System.Flags]
    public enum SectorFeature
    {
        None = 0, Rail = 1, RailYard = 2, Industry = 4, Airstrip = 8, Quarry = 16, PowerLine = 32,
        Orchards = 64, Logging = 128, Fortifications = 256, Reservoir = 512,
    }

    // A kind of sector: what its land looks like and what's built on it.
    public class SectorType
    {
        public SectorKind kind;
        public string name, blurb;
        public float hills = 8f;             // broad hills across the play area, metres
        public float ridges = 35f;           // the ring of higher ground round the edge, metres
        public RiverStyle river = RiverStyle.River;
        public int lakes;                    // natural lakes
        public float lakeSize = 40f;         // their radius, metres
        public int ponds;                    // small pools (marsh)
        public RoadStyle roads = RoadStyle.Crossroads;
        public int villages = 2;
        public bool town;                    // a dense grid of streets and houses
        public float forest = 0.3f;          // how much of the wild land is wooded (0-1)
        public float conifer = 0.35f, birch = 0.15f; // shares of the trees
        public float fields = 0.7f;          // how much of the open land is farmed (0-1)
        public SectorFeature features;
    }

    // The 30 sector types.
    public static class SectorTypes
    {
        public static readonly SectorType[] All =
        {
            T(SectorKind.VarrowValley, "Varrow Valley", "The reservoir valley where it started: a dam, a river, four villages.",
                t => { t.features = SectorFeature.Reservoir; }),
            T(SectorKind.RiverValley, "Farmland River Valley", "A river winding through fields and hedges, villages at the crossings.",
                t => { t.hills = 7f; t.villages = 3; t.forest = 0.25f; t.fields = 0.9f; }),
            T(SectorKind.RiverCrossing, "River Crossing", "A broad river with two bridges everyone wants.",
                t => { t.hills = 5f; t.ridges = 25f; t.river = RiverStyle.WideRiver; t.roads = RoadStyle.Through; t.forest = 0.2f; }),
            T(SectorKind.ForestEdge, "Forest Edge", "Farmland running up against a dark forest that hides everything.",
                t => { t.river = RiverStyle.Stream; t.roads = RoadStyle.Tracks; t.villages = 1; t.forest = 0.6f; t.conifer = 0.5f; t.fields = 0.6f; }),
            T(SectorKind.DeepForest, "Deep Forest", "Spruce and pine as far as you can see, cut by forest tracks.",
                t => { t.hills = 10f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Tracks; t.villages = 0; t.forest = 0.9f; t.conifer = 0.7f; t.fields = 0.05f; }),
            T(SectorKind.BirchWoods, "Birch Woods", "Pale birch groves and clearings, open but full of cover.",
                t => { t.hills = 6f; t.ridges = 20f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Tracks; t.villages = 1; t.forest = 0.6f; t.birch = 0.6f; t.conifer = 0.2f; t.fields = 0.3f; }),
            T(SectorKind.RollingFarmland, "Rolling Farmland", "Big hills of wheat and pasture, few places to hide.",
                t => { t.hills = 12f; t.ridges = 20f; t.river = RiverStyle.None; t.villages = 3; t.forest = 0.15f; t.fields = 1f; }),
            T(SectorKind.Steppe, "Open Steppe", "Flat grassland to the horizon. Nothing moves unseen.",
                t => { t.hills = 3f; t.ridges = 10f; t.river = RiverStyle.None; t.roads = RoadStyle.Through; t.villages = 1; t.forest = 0.03f; t.conifer = 0f; t.fields = 0.5f; }),
            T(SectorKind.HillCountry, "Hill Country", "Steep wooded hills; roads twist through the valleys.",
                t => { t.hills = 22f; t.ridges = 45f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Tracks; t.forest = 0.35f; t.conifer = 0.4f; t.fields = 0.4f; }),
            T(SectorKind.RidgePass, "Ridge Pass", "One road through a pass between high ridges, with defences dug in.",
                t => { t.hills = 15f; t.ridges = 70f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Through; t.villages = 1; t.forest = 0.45f; t.conifer = 0.8f; t.fields = 0.2f; t.features = SectorFeature.Fortifications; }),
            T(SectorKind.LakeDistrict, "Lake District", "Wooded hills around three lakes.",
                t => { t.hills = 10f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Tracks; t.lakes = 3; t.lakeSize = 42f; t.forest = 0.4f; t.conifer = 0.5f; t.fields = 0.4f; }),
            T(SectorKind.Reservoir, "Reservoir", "A dam holds back a lake; below it the river runs through farmland.",
                t => { t.villages = 2; t.forest = 0.4f; t.fields = 0.6f; t.features = SectorFeature.Reservoir; }),
            T(SectorKind.Marshland, "Marshland", "Reeds, pools and birch scrub. Vehicles keep to the causeways.",
                t => { t.hills = 2f; t.ridges = 8f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Tracks; t.ponds = 18; t.villages = 1; t.forest = 0.25f; t.birch = 0.6f; t.conifer = 0.2f; t.fields = 0.15f; }),
            T(SectorKind.Wetlands, "Wetlands", "A wide slow river spreading into pools and channels.",
                t => { t.hills = 1.5f; t.ridges = 6f; t.river = RiverStyle.WideRiver; t.roads = RoadStyle.Tracks; t.ponds = 10; t.lakes = 1; t.villages = 1; t.forest = 0.2f; t.birch = 0.4f; t.fields = 0.3f; }),
            T(SectorKind.Lakeshore, "Lakeshore", "A great lake on one side, holiday villages and boat sheds on the shore.",
                t => { t.hills = 6f; t.ridges = 25f; t.river = RiverStyle.None; t.roads = RoadStyle.Through; t.lakes = 1; t.lakeSize = 110f; t.forest = 0.3f; t.fields = 0.5f; }),
            T(SectorKind.VillageCluster, "Village Cluster", "Six villages close together among orchards and fields.",
                t => { t.hills = 6f; t.ridges = 25f; t.river = RiverStyle.Stream; t.villages = 6; t.forest = 0.2f; t.fields = 0.8f; t.features = SectorFeature.Orchards; }),
            T(SectorKind.TownOutskirts, "Town Outskirts", "The edge of a town: streets, power lines, allotments.",
                t => { t.hills = 4f; t.ridges = 20f; t.roads = RoadStyle.Grid; t.town = true; t.forest = 0.1f; t.fields = 0.5f; t.features = SectorFeature.PowerLine; }),
            T(SectorKind.MarketTown, "Market Town", "A river town with a church square and a grid of streets.",
                t => { t.hills = 5f; t.ridges = 25f; t.roads = RoadStyle.Grid; t.town = true; t.villages = 1; t.forest = 0.1f; t.fields = 0.4f; }),
            T(SectorKind.IndustrialZone, "Industrial Zone", "Warehouses, a factory chimney and a railway by the river.",
                t => { t.hills = 3f; t.ridges = 15f; t.villages = 2; t.forest = 0.1f; t.fields = 0.2f; t.features = SectorFeature.Industry | SectorFeature.Rail | SectorFeature.PowerLine; }),
            T(SectorKind.RailJunction, "Rail Junction", "Two railway lines cross by a station, with wagons in the sidings.",
                t => { t.hills = 4f; t.ridges = 20f; t.river = RiverStyle.Stream; t.forest = 0.2f; t.fields = 0.6f; t.features = SectorFeature.Rail | SectorFeature.RailYard; }),
            T(SectorKind.RailLine, "Rail Line", "A railway across open country, the enemy's supply artery.",
                t => { t.river = RiverStyle.Stream; t.roads = RoadStyle.Tracks; t.forest = 0.3f; t.features = SectorFeature.Rail; }),
            T(SectorKind.HighwayCorridor, "Highway Corridor", "A long straight main road with power lines beside it.",
                t => { t.hills = 6f; t.ridges = 20f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Through; t.villages = 1; t.forest = 0.2f; t.fields = 0.8f; t.features = SectorFeature.PowerLine; }),
            T(SectorKind.CrossroadsHamlet, "Crossroads Hamlet", "Two roads meet at a hamlet in the middle of nowhere.",
                t => { t.hills = 7f; t.ridges = 25f; t.river = RiverStyle.None; t.villages = 2; t.forest = 0.3f; }),
            T(SectorKind.Airfield, "Airfield", "A grass and concrete airstrip the enemy uses for supply.",
                t => { t.hills = 2f; t.ridges = 15f; t.river = RiverStyle.None; t.roads = RoadStyle.Through; t.villages = 1; t.forest = 0.15f; t.fields = 0.6f; t.features = SectorFeature.Airstrip; }),
            T(SectorKind.QuarryHills, "Quarry Hills", "A stone quarry cut into wooded hills.",
                t => { t.hills = 18f; t.ridges = 40f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Tracks; t.villages = 1; t.forest = 0.35f; t.conifer = 0.5f; t.fields = 0.3f; t.features = SectorFeature.Quarry; }),
            T(SectorKind.PowerStation, "Power Station", "A plant by the river feeding pylons across the fields.",
                t => { t.hills = 4f; t.ridges = 20f; t.villages = 1; t.forest = 0.2f; t.fields = 0.5f; t.features = SectorFeature.Industry | SectorFeature.PowerLine; }),
            T(SectorKind.OrchardCountry, "Orchard Country", "Rows of fruit trees around old farms.",
                t => { t.hills = 9f; t.ridges = 20f; t.river = RiverStyle.Stream; t.villages = 3; t.forest = 0.15f; t.fields = 0.5f; t.features = SectorFeature.Orchards; }),
            T(SectorKind.LoggingCamp, "Logging Camp", "Felled clearings, log stacks and huts in a conifer forest.",
                t => { t.hills = 14f; t.ridges = 40f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Tracks; t.villages = 0; t.forest = 0.75f; t.conifer = 0.85f; t.fields = 0f; t.features = SectorFeature.Logging; }),
            T(SectorKind.Moorland, "Moorland", "Bare rolling moor with dark pools and the odd pine.",
                t => { t.hills = 14f; t.ridges = 30f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Tracks; t.ponds = 4; t.villages = 0; t.forest = 0.05f; t.conifer = 1f; t.fields = 0f; }),
            T(SectorKind.BorderLine, "Border Line", "Dragon's teeth, a trench line and the road through the old border post.",
                t => { t.hills = 7f; t.ridges = 25f; t.river = RiverStyle.Stream; t.roads = RoadStyle.Through; t.villages = 1; t.forest = 0.3f; t.fields = 0.5f; t.features = SectorFeature.Fortifications; }),
        };

        public static SectorType Get(SectorKind kind) => All[(int)kind];

        static SectorType T(SectorKind kind, string name, string blurb, System.Action<SectorType> set)
        {
            var t = new SectorType { kind = kind, name = name, blurb = blurb };
            set(t);
            return t;
        }
    }

    // A pool or lake: a wobbly circle of water whose level the ground builder works out.
    public struct Pond
    {
        public Vector2 center;
        public float radius, seed;
        public float level; // set by Ground when it shapes the basin
    }

    // A model placed by the generator (rail, industry, fortifications...), with the ground levelled under it.
    public struct Site
    {
        public string model;
        public Vector2 position;
        public float yaw, scale, flat; // flat: radius of ground levelled for it (0 for none)
    }

    // One sector, ready to build: everything MapLayout answers questions about. Made by SectorGenerator from a
    // type and a seed, or by Varrow() for the hand-made valley.
    public class SectorPlan
    {
        public SectorKind kind;
        public int seed;
        public string name;
        public int worldSeed = 7, fieldSeed = 11;

        public Vector3 basePosition, enemyHqPosition;
        public readonly List<(EnemyType type, Vector3 position)> defences = new List<(EnemyType, Vector3)>();
        public Rect enemyArea;

        // The river, upstream first (null: none), its width, and where roads cross it.
        public Vector2[] river;
        public float riverHalfWidth = 4.5f;
        public string riverName = "Lissen River";
        public readonly List<Vector2> bridges = new List<Vector2>();

        public readonly List<Vector2[]> roads = new List<Vector2[]>();
        public readonly List<Vector2> villages = new List<Vector2>();
        public readonly List<bool> farmsteads = new List<bool>();
        public readonly List<string> villageNames = new List<string>();

        // The reservoir, when there is one: a dam where the river starts, and the lake behind it.
        public bool hasDam;
        public Vector2 damPoint;
        public float lakeLevel = 12f, damHalfSpan = 30f;
        public (Vector2 center, float angle, float halfLength, float halfWidth, float seed)[] lakeLobes = new (Vector2, float, float, float, float)[0];
        public (Vector2 center, float radius)[] islands = new (Vector2, float)[0];
        public readonly List<Pond> ponds = new List<Pond>();

        // The land.
        public float hills = 9f, ridges = 40f;
        public Vector2 noiseOffset;
        public float forest = 0.3f, conifer = 0.3f, birch = 0.12f, fields = 0.7f;

        // Special features.
        public SectorFeature features;
        public readonly List<Vector2[]> railways = new List<Vector2[]>();
        public Vector2[] powerLine, fortLine, trenchLine;
        public Vector2 airstripCenter, airstripAlong;
        public float airstripLength;
        public Vector2 quarry;
        public float quarryRadius;
        public readonly List<Site> sites = new List<Site>();
        public readonly List<FieldPlot> orchards = new List<FieldPlot>();
        public readonly List<Vector2> clearings = new List<Vector2>();

        public bool Has(SectorFeature f) => (features & f) != 0;
        public SectorType Type => SectorTypes.Get(kind);

        // The hand-made Varrow Valley: the map the game started with, unchanged.
        public static SectorPlan Varrow()
        {
            var p = new SectorPlan
            {
                kind = SectorKind.VarrowValley, seed = 0, name = "Varrow Valley",
                basePosition = new Vector3(-72f, 0f, -70f),
                enemyHqPosition = new Vector3(45f, 0f, 45f),
                enemyArea = new Rect(-35f, -35f, 115f, 115f),
                hasDam = true, damPoint = new Vector2(-140f, 15f), lakeLevel = 12f, damHalfSpan = 30f,
                hills = 9f, ridges = 40f, forest = 0.3f, conifer = 0.3f, birch = 0.12f, fields = 0.7f,
                features = SectorFeature.Reservoir,
            };
            var hq = p.enemyHqPosition;
            p.defences.Add((EnemyType.Radar, hq + new Vector3(6f, 0f, -8f)));
            p.defences.Add((EnemyType.AAGun, hq + new Vector3(-12f, 0f, 0f)));
            p.defences.Add((EnemyType.AAGun, hq + new Vector3(0f, 0f, -14f)));
            p.defences.Add((EnemyType.AAGun, hq + new Vector3(10f, 0f, 9f)));
            p.defences.Add((EnemyType.AAGun, new Vector3(-10f, 0f, -5f))); // forward post with no radar cover, an easier first target
            p.defences.Add((EnemyType.Radar, new Vector3(-20f, 0f, 55f))); // second radar covering the north-west flank
            p.defences.Add((EnemyType.AAGun, new Vector3(-12f, 0f, 48f)));
            // The lake, in dam-local metres (x downstream, y across): a broad basin behind the dam and a longer arm
            // bending away north up the valley, each a wobbly ellipse, blended into one shore. Plus two islands.
            p.lakeLobes = new[]
            {
                (new Vector2(-34f, 0f), 0f, 50f, 33f, 0.7f),
                (new Vector2(-72f, 58f), 120f, 62f, 38f, 2.3f),
            };
            p.islands = new[] { (new Vector2(-58f, 8f), 8f), (new Vector2(-96f, 74f), 5f) };
            p.river = new[]
            {
                p.damPoint, new Vector2(-120f, 8f), new Vector2(-85f, -10f),
                new Vector2(-58f, -33f), new Vector2(-42f, -58f), new Vector2(-32f, -95f), new Vector2(-26f, -150f),
                new Vector2(-20f, -250f),
            };
            var bridge = new Vector2(-58f, -33f); // a river point, so both lines pass through it exactly
            p.bridges.Add(bridge);
            p.roads.Add(new[] { new Vector2(-67.5f, -54f), new Vector2(-67f, -42f), bridge, new Vector2(-49f, -25f),
                new Vector2(-30f, -16f), new Vector2(-5f, -14f), new Vector2(18f, 0f), new Vector2(30f, 18f), new Vector2(36f, 32f) });
            p.roads.Add(new[] { new Vector2(30f, -250f), new Vector2(28f, -140f), new Vector2(22f, -80f), new Vector2(8f, -40f),
                new Vector2(-5f, -14f), new Vector2(-18f, 15f), new Vector2(-36f, 45f), new Vector2(-52f, 70f),
                new Vector2(-68f, 120f), new Vector2(-90f, 250f) });
            p.roads.Add(new[] { new Vector2(18f, 0f), new Vector2(40f, -22f), new Vector2(62f, -50f), new Vector2(85f, -80f),
                new Vector2(150f, -120f), new Vector2(250f, -150f) });
            p.roads.Add(new[] { new Vector2(-49f, -25f), new Vector2(-62f, 5f), new Vector2(-78f, 32f), new Vector2(-84f, 46f),
                new Vector2(-110f, 75f), new Vector2(-126f, 128f), new Vector2(-148f, 190f), new Vector2(-165f, 250f) });
            p.roads.Add(new[] { new Vector2(-88.5f, -71f), new Vector2(-110f, -72f), new Vector2(-160f, -82f), new Vector2(-250f, -95f) });
            foreach (var (v, name, farm) in new[]
                     {
                         (new Vector2(20f, -72f), "Tolbeck", false), (new Vector2(-52f, 70f), "Esterhay", false),
                         (new Vector2(62f, -54f), "Marrowfield", false), (new Vector2(-81f, 40f), "Grisham Farm", true),
                     })
            {
                p.villages.Add(v);
                p.villageNames.Add(name);
                p.farmsteads.Add(farm);
            }
            return p;
        }
    }

    // Made-up place names, from syllables, so no sector is named after a real place.
    public static class PlaceNames
    {
        static readonly string[] Starts = { "Tol", "Ester", "Marrow", "Gris", "Var", "Liss", "Hal", "Brem", "Cor", "Dun", "Fen", "Gal",
            "Har", "Kes", "Lor", "Mor", "Nor", "Os", "Pell", "Quen", "Ros", "Sel", "Tarn", "Ulm", "Vel", "Wen", "Ash", "Brack", "Cald", "Dray" };
        static readonly string[] Ends = { "beck", "hay", "field", "ham", "ton", "by", "ford", "wick", "stead", "mere", "holt", "brook",
            "worth", "ley", "dale", "moor", "wood", "gate", "bridge", "well" };
        static readonly string[] Rivers = { "Lissen", "Corra", "Velde", "Ashwater", "Dunne", "Morrow", "Fenn", "Tarrow", "Selby", "Ulla" };

        public static string Place(System.Random rng) => Starts[rng.Next(Starts.Length)] + Ends[rng.Next(Ends.Length)];
        public static string River(System.Random rng) => Rivers[rng.Next(Rivers.Length)] + (rng.NextDouble() < 0.5 ? " River" : " Water");
    }
}

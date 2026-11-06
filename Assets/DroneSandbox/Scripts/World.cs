using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Builds everything on the map that isn't a unit: terrain, river, fields, villages, woods, bridges, the
    // sector's special features and both bases, all from MapLayout. Used by the game (SandboxGame) and by the
    // editor preview (MapPreview).
    public static class World
    {
        const int TreeLimit = 11500; // shared by woods, lake, gardens, field edges and orchards
        public static int TreeCount { get; private set; }

        // Builds one sector and resets the shared scenery budget before any builder plants trees.
        // How long each part of the last Build took, for the test log (a 2 km sector has to load quickly).
        public static string Timings { get; private set; } = "";

        // The seed of the one big world every sector sits in (FarTerrain, WorldPlan).
        public const int WorldSeed = 1200;

        public static Transform Build(Transform parent, bool preview)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var times = new System.Text.StringBuilder();
            void Lap(string what) { times.Append($"{what} {clock.ElapsedMilliseconds}, "); clock.Restart(); }
            MapLayout.Prepare();
            Lap("layout");
            TreeCount = 0;
            Undergrowth.Reset(); // every tree builder shares its spacing grid
            var root = new GameObject("World").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(MapLayout.Plan.worldSeed); // same map every time for a sector

            Ground.Build(root);
            Lap("ground");
            if (!preview)
            {
                // The whole 56 km world round the sector (made the first time), then its terrain out to the horizon.
                if (!WorldPlan.Ready) WorldPlan.Generate(WorldSeed);
                FarTerrain.Build();
                Lap("world");
            }
            Backdrop.Build(root);
            Water.Build(root);
            Reservoir.Build(root);
            Lap("water");
            RoadSurface.Build(root);
            Lap("roads");
            Farmland.Build(root, rng);
            Lap("fields");
            var scenery = new GameObject("Scenery").transform;
            scenery.SetParent(root, false);
            foreach (var bridge in MapLayout.Bridges) BuildBridge(scenery, bridge);
            BuildVillages(scenery, rng);
            Lap("villages");
            Features.Build(scenery); // reserve orchard trees before filling the wild woods
            BuildWoods(scenery, rng);
            Lap("woods");
            Ground.PaintForestFloor(woodland, Undergrowth.Trees);
            Lap("forest floor");
            Grass.Plant(Ground.Current); // after the villages and woods, so it keeps off worn paths, yards and forest floor
            Lap("grass");
            BuildRocks(scenery, rng);
            Undergrowth.Build(scenery);
            Lap("undergrowth");
            Bases.BuildPlayerBase(root);
            Bases.BuildEnemyHq(root);
            if (preview) Bases.PreviewUnits(root);
            Lap("bases");
            Timings = $"{times}{TreeCount} trees, {VillageHouses} houses on {MapLayout.VillageStreets.Count} streets; layout: {MapLayout.Timings}{MapLayout.FieldTimings}";
            return root;
        }

        // Places Resources/Models/<model> on the ground (sunk a little so it doesn't float on slopes),
        // or builds a primitive stand-in when the model is missing. Trees return null when crowded or over budget.
        public static GameObject Place(string model, Vector3 pos, float yaw, float scale, float sink, Transform parent,
            System.Action<Transform> fallback = null)
        {
            bool tree = model == "Scenery_Tree" || model == "Scenery_Tree_B" || model == "Scenery_Birch"
                || model == "Scenery_Poplar" || model == "Scenery_Spruce" || model == "Scenery_Pine" || model == "Scenery_Pine_B";
            var p = new Vector2(pos.x, pos.z);
            if (tree && (TreeCount >= TreeLimit || Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge
                || Undergrowth.UnderTrees(p, 3.2f))) return null;
            var root = new GameObject(model).transform;
            root.SetParent(parent, false);
            float r = 1.2f * scale;
            float ground = Mathf.Min(Ground.HeightAt(pos), Ground.HeightAt(pos + new Vector3(r, 0f, r)),
                Ground.HeightAt(pos - new Vector3(r, 0f, r)), Ground.HeightAt(pos + new Vector3(r, 0f, -r)),
                Ground.HeightAt(pos + new Vector3(-r, 0f, r)));
            root.position = new Vector3(pos.x, ground - sink, pos.z);
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Art.TryModel(model, root, scale) == null) (fallback ?? (t => StandIn(t, scale)))(root);
            if (tree)
            {
                TreeCount++;
                Undergrowth.NoteTree(p);
            }
            return root.gameObject;
        }

        static void StandIn(Transform root, float scale)
        {
            Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.5f * scale, 0f), Vector3.one * scale, new Color(0.4f, 0.4f, 0.38f));
        }

        // ---- Bridge ----

        // Instanced roadside trees consume the same logical tree slots and spacing grid as ordinary scenery.
        public static bool ReserveRoadTree(Vector2 p)
        {
            if (TreeCount >= TreeLimit || Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge || Undergrowth.UnderTrees(p, 3.2f)) return false;
            TreeCount++; Undergrowth.NoteTree(p); return true;
        }

        static void BuildBridge(Transform parent, Vector2 p)
        {
            MapLayout.RoadField.Query(p.x, p.y, out _, out int segment);
            Vector2 dir = MapLayout.RoadField.Direction(segment);
            // The deck sits level with the road where it meets the banks, and spans the dip between them.
            var a = new Vector3(p.x - dir.x * 11f, 0f, p.y - dir.y * 11f);
            var b = new Vector3(p.x + dir.x * 11f, 0f, p.y + dir.y * 11f);
            float deck = Mathf.Max(Ground.HeightAt(a), Ground.HeightAt(b)) + 0.05f;
            float span = 4f;
            for (float s = 2f; s < 20f; s += 0.5f)
            {
                bool low = false;
                foreach (float sign in new[] { -1f, 1f })
                {
                    var q = new Vector3(p.x + dir.x * s * sign, 0f, p.y + dir.y * s * sign);
                    if (Ground.HeightAt(q) < deck - 0.35f) low = true;
                }
                if (low) span = s;
            }
            var bridge = new GameObject("Scenery_Bridge").transform;
            bridge.SetParent(parent, false);
            bridge.position = new Vector3(p.x, deck - 0.3f, p.y);
            bridge.rotation = Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 0f);
            var model = Art.TryModel("Scenery_Bridge", bridge);
            if (model != null) model.transform.localScale = new Vector3(Mathf.Max(1f, MapLayout.RoadWidth(segment) * 2f / 5.2f), 1f, Mathf.Clamp((span * 2f + 3f) / 18f, 0.9f, 1.8f));
        }

        // ---- Villages ----

        // Records the main buildings before gardens are planted, so later rows cannot land on earlier props.
        sealed class VillageBuilding
        {
            public Transform root;
            public Vector2 at, center, facing, pathEnd;
            public string model;
            public VillagePlot plot;
            // A house on a street plot: the street's direction, away from the street, the plot's frontage, and how
            // far its garden runs behind the house (zero for the church and farmsteads).
            public Vector2 along, outward;
            public float frontage, back;
        }

        static readonly List<(Vector2 at, float radius)> villagePlots = new List<(Vector2, float)>();

        // How many houses the last Build put in villages, for the test log.
        public static int VillageHouses { get; private set; }
        public static int VillagePropRenderers { get; private set; }
        static int gardenTrees;

        // Villages the way aerial photos show them: houses on plots down both sides of each street (MapLayout's
        // VillageStreets, a 250-450 m stretch of road and a lane or two off it), a garden behind each. The first
        // village's church takes its centre.
        static void BuildVillages(Transform parent, System.Random rng)
        {
            var plan = MapLayout.Plan;
            villagePlots.Clear();
            Features.ClearVillagePlots();
            VillagePropRenderers = gardenTrees = 0;
            var buildings = new List<VillageBuilding>();
            var roots = new Transform[plan.villages.Count];
            bool needsChurch = true;
            for (int v = 0; v < plan.villages.Count; v++)
            {
                roots[v] = new GameObject("Village " + (v + 1)).transform;
                roots[v].SetParent(parent, false);
                Vector2 center = plan.villages[v];
                MapLayout.RoadField.Query(center.x, center.y, out _, out int segment);
                Vector2 along = MapLayout.RoadField.Direction(segment);
                Vector2 across = new Vector2(-along.y, along.x);
                if (plan.farmsteads[v])
                {
                    BuildFarmstead(roots[v], center, along, across, v, buildings);
                    continue;
                }
                // Reserve the church first and try both verges if water or a junction blocks its first plot.
                if (needsChurch && plan.world == null)
                    foreach (float side in new[] { -1f, 1f })
                        foreach (float s in new[] { 0f, -15f, 15f })
                            if (needsChurch && VillageBuildingAt("Scenery_Church", center + along * s + across * side * 16f,
                                -across * side, 1f, center + along * s, center, roots[v], v, buildings)) needsChurch = false;
            }
            foreach (var b in buildings)
                if(b.model=="Scenery_Church")
                {
                    b.along=new Vector2(-b.facing.y,b.facing.x); b.outward=-b.facing; b.frontage=30f; b.back=30f;
                    b.plot=new VillagePlot {front=b.at+b.facing*16f,along=b.along,outward=b.outward,frontage=30f,depth=46f,setback=16f,church=true,model=b.model};
                }
            var occupiedPlots = new List<VillagePlot>();
            var squares = new List<VillagePlot>();
            foreach (var b in buildings) if (b.frontage > 0f) occupiedPlots.Add(b.plot);
            foreach (var proposed in MapLayout.VillagePlots)
            {
                var plot = proposed;
                bool clear = VillagePlotClear(plot, occupiedPlots);
                while (!clear && plan.world == null && plot.depth > 25f)
                { plot.depth = Mathf.Max(25f, plot.depth - 5f); clear = VillagePlotClear(plot, occupiedPlots); }
                if (!clear) continue;
                if (plot.square) { squares.Add(plot); occupiedPlots.Add(plot); continue; }
                if (!VillageBuildingAt(plot.model, plot.House, -plot.outward, plot.Scale, plot.front, plot.front, roots[plot.owner], plot.owner, buildings, plot)) continue;
                var b = buildings[buildings.Count - 1]; b.plot = plot; b.along = plot.along; b.outward = plot.outward;
                b.frontage = plot.frontage; b.back = plot.depth - plot.setback;
                occupiedPlots.Add(plot);
            }

            // Check paths after every settlement's buildings are reserved: a driveway to about half the houses, a
            // footpath to the rest. Then gardens, on an allowance of resident renderers that grows with the village
            // (counting all three LOD renderers of each fruit tree).
            var houses = new Dictionary<Transform, int>();
            foreach (var b in buildings)
            {
                bool house = b.model.StartsWith("Scenery_House_") || b.model == "Scenery_Townhouse_Row";
                if (b.frontage <= 0f) VillagePath(b.at, b.pathEnd, b.model == "Scenery_Church" ? 3f : house && rng.NextDouble() < 0.45 ? 3f : 1.6f, b.at);
                if (house) houses[b.root] = houses.TryGetValue(b.root, out int n) ? n + 1 : 1;
            }
            VillageHouses = 0;
            foreach (var n in houses.Values) VillageHouses += n;
            var budgets = new Dictionary<Transform, int>();
            // Two batched LOD renderers per settlement replace individual fence panels; pay from the same cap.
            for (int v = 0; v < roots.Length; v++)
            {
                int housesHere = houses.TryGetValue(roots[v], out int n) ? n : 0;
                int budget = Mathf.Min(220, 8 + 4 * housesHere);
                var plots = new List<VillagePlot>();
                foreach (var b in buildings) if (b.root == roots[v] && b.frontage > 0f) plots.Add(b.plot);
                foreach (var plot in squares) if (plot.owner == v) plots.Add(plot);
                int used = Features.VillagePlots(roots[v], plots, budget);
                VillagePropRenderers += used; budgets[roots[v]] = budget - used;
            }
            // Shared fixtures first, so garden clutter cannot spend the allowance they need.
            for (int v = 0; v < roots.Length; v++)
            {
                Utility(roots[v], v, occupiedPlots, houses.TryGetValue(roots[v], out int count) ? count : 0, budgets);
                Orchards(roots[v], v, occupiedPlots, budgets);
            }
            foreach (var b in buildings)
            {
                if (b.model == "Scenery_Church")
                {
                    int allowance = budgets[b.root];
                    Vector2 along = b.frontage > 0f ? b.along : new Vector2(-b.facing.y,b.facing.x), outward = -b.facing;
                    foreach(float side in new[]{-1f,1f})
                        if(gardenTrees<128 && VillageProp("Scenery_Tree",b.at+outward*19f+along*(side*9f),side*45f,.45f,3f,b.root,ref allowance)) gardenTrees++;
                    budgets[b.root]=allowance; continue;
                }
                if (!b.model.StartsWith("Scenery_House_") && b.model != "Scenery_Townhouse_Row") continue;
                int budget = budgets[b.root];
                if (b.frontage > 0f) Garden(b.root, b.plot, ref budget);
                budgets[b.root] = budget;
            }
            foreach (var p in occupiedPlots)
            {
                if (p.landUse != 1 && p.landUse != 4) continue;
                var root = roots[p.owner]; int budget = budgets[root];
                float yaw = Mathf.Atan2(-p.outward.x, -p.outward.y) * Mathf.Rad2Deg;
                if (p.landUse == 1)
                    for (int i = 0; i < 4; i++) VillageProp("Scenery_Hut", p.Point(-p.frontage*.5f+7f+i*16f,p.depth-4f),yaw,.65f,2.3f,root,ref budget,false);
                else
                {
                    for (int i = 0; i < 3; i++) VillageProp("Scenery_FreightGroup",p.Point(-21f+i*21f,13f),yaw,1f,5f,root,ref budget,false);
                    VillageProp("Scenery_MachineryShed",p.Point(21f,58f),yaw,1f,5.5f,root,ref budget,false);
                }
                budgets[root] = budget;
            }
            Ground.FlushWear(); // every path, yard and hedge line into the terrain at once
        }

        // Larger villages have a transformer kiosk, and the largest a pump house, at a plot corner by the street:
        // spare fringe land first, then a garden's front corner away from its drive.
        static void Utility(Transform root, int village, List<VillagePlot> plots, int houses, Dictionary<Transform, int> budgets)
        {
            if (houses < 14 || !budgets.TryGetValue(root, out int budget)) return;
            var models = houses >= 28 ? new[] { "Scenery_TransformerKiosk", "Scenery_PumpHouse" } : new[] { "Scenery_TransformerKiosk" };
            int placed = 0;
            for (int pass = 0; pass < 2 && placed < models.Length; pass++)
                foreach (var p in plots)
                {
                    if (placed >= models.Length) break;
                    if (p.owner != village || (pass == 0 ? p.landUse < 6 : p.square || p.farm || p.landUse != 0)) continue;
                    float radius = models[placed] == "Scenery_PumpHouse" ? 3.4f : 2.2f, u = -p.frontage * .5f + radius + .7f, v = radius + .7f;
                    float yaw = Mathf.Atan2(-p.outward.x, -p.outward.y) * Mathf.Rad2Deg;
                    if (VillageProp(models[placed], p.Point(u, v), yaw, 1f, radius, root, ref budget)) placed++;
                }
            budgets[root] = budget;
        }

        // Small fringe orchards: up to six fruit trees on their painted 7 m rows, from the garden tree allowance; each
        // vegetable plot has its tool shed by the compost corner.
        static void Orchards(Transform root, int village, List<VillagePlot> plots, Dictionary<Transform, int> budgets)
        {
            if (!budgets.TryGetValue(root, out int budget)) return;
            foreach (var p in plots)
            {
                if (p.owner == village && p.landUse == 9)
                    VillageProp("Scenery_Hut", p.Point(-p.frontage * .5f + 2.6f, p.depth - 2.6f), Mathf.Atan2(-p.outward.x, -p.outward.y) * Mathf.Rad2Deg,
                        .55f, 2f, root, ref budget, false);
                if (p.owner != village || p.landUse != 7) continue;
                int planted = 0;
                for (float u = -p.frontage * .5f + 4.8f; u < p.frontage * .5f - 2f && planted < 6; u += 7f)
                    for (float v = 6f; v < p.depth - 3f && planted < 6; v += 8f)
                        if (gardenTrees < 120 && VillageProp("Scenery_Tree", p.Point(u, v), (p.identity + planted * 47) % 360,
                            .32f + (p.identity >> (planted % 8) & 3) * .025f, 2f, root, ref budget)) { gardenTrees++; planted++; }
            }
            budgets[root] = budget;
        }

        // Test entire gardens before building: otherwise a clear roof can leave a hedge crossing a lane or a yard.
        static bool VillagePlotClear(VillagePlot p,List<VillagePlot> occupied)
        {
            foreach(var q in occupied) if(VillagePlan.Intersects(p,q))return false;
            int nu=Mathf.CeilToInt(p.frontage/3f),nv=Mathf.CeilToInt(p.depth/3f);
            for(int j=0;j<=nv;j++)for(int i=0;i<=nu;i++)
            {
                Vector2 q=p.front+p.along*(p.frontage*(i/(float)nu-.5f))+p.outward*(p.depth*j/nv);
                if(!p.Contains(q)) continue;
                if(!VillageGround(q)||MapLayout.RoadField.Distance(q.x,q.y)<MapLayout.RoadWidthAt(q.x,q.y)+.7f||MapLayout.TrackField.Distance(q.x,q.y)<2.5f)return false;
            }
            return true;
        }

        // Conservative pivot-centred radii include roofs, porches and the barn's lean-to, not only wall centres.
        static float VillageRadius(string model)
        {
            switch (model)
            {
                case "Scenery_Church": return 11f;
                case "Scenery_Barn": return 9f;
                case "Scenery_Hut": return 3.5f;
                case "Scenery_Silo": return 7.3f;
                case "Scenery_Townhouse_Row": return 13.3f;
                case "Scenery_OfficeBlock": return 17f;
                case "Scenery_Warehouse": return 18f;
                case "Scenery_MachineryShed": return 5.5f;
                default: return 6.5f;
            }
        }

        // Places one main building only when its full footprint fits, then reserves it for every later plot.
        static bool VillageBuildingAt(string model, Vector2 p, Vector2 facing, float scale, Vector2 pathEnd,
            Vector2 center, Transform root, int village, List<VillageBuilding> buildings, VillagePlot? property = null)
        {
            float radius = VillageRadius(model) * scale;
            bool fits = property.HasValue && model.StartsWith("Scenery_House_") ? VillageFootprint(p, facing, 4.6f * scale, 3.9f * scale)
                : model == "Scenery_Townhouse_Row" ? VillageFootprint(p, facing, 12.4f * scale, 5.4f * scale)
                : model == "Scenery_OfficeBlock" ? VillageFootprint(p, facing, 12.2f * scale, 8.6f * scale)
                : model == "Scenery_Warehouse" ? VillageFootprint(p, facing, 9.4f * scale, 15.4f * scale) : VillageRoom(p, radius, false);
            if (!fits) return false;
            float yaw = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg;
            var house = Place(model, new Vector3(p.x, 0f, p.y), yaw, scale, 0.35f, root);
            if (model != "Scenery_Church") Repaint(house, village, p, property);
            villagePlots.Add((p, radius));
            buildings.Add(new VillageBuilding { root = root, at = p, center = center, facing = facing,
                pathEnd = pathEnd, model = model });
            return true;
        }

        // Long terraced rows need a rectangular roof test: a bounding circle wrongly pushes them off their street.
        static bool VillageFootprint(Vector2 p, Vector2 facing, float width, float depth)
        {
            // These buildings occupy already separated planned rectangles. A second circle overlap test would
            // remove the opposite frontage: two 24 m terraces can face across a street without touching roofs.
            Vector2 side = new Vector2(-facing.y, facing.x);
            int nx = Mathf.CeilToInt(width), nz = Mathf.CeilToInt(depth);
            for (int z = -nz; z <= nz; z++) for (int x = -nx; x <= nx; x++)
                if (!Buildable(p + side * (width * x / nx) + facing * (depth * z / nz))) return false;
            return true;
        }

        // A barn, farmhouse, shed and silo face a shared yard on the less obstructed side of the road.
        static void BuildFarmstead(Transform root, Vector2 center, Vector2 along, Vector2 across, int village,
            List<VillageBuilding> buildings)
        {
            string[] models = { "Scenery_Barn", "Scenery_House_A", "Scenery_MachineryShed", "Scenery_Silo" };
            Vector2[] offsets = { new Vector2(-12f, 0f), new Vector2(12f, 0f), new Vector2(0f, -10f), new Vector2(0f, 12.5f) };
            float side = -1f;
            int best = -1;
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector2 candidate = center + across * sign * 23.5f;
                int fits = 0;
                for (int i = 0; i < models.Length; i++)
                    if (VillageRoom(candidate + along * offsets[i].x + across * sign * offsets[i].y,
                        VillageRadius(models[i]), false)) fits++;
                if (fits > best) { best = fits; side = sign; }
            }
            Vector2 yard = center + across * side * 23.5f;
            for (int i = 0; i < models.Length; i++)
            {
                Vector2 p = yard + along * offsets[i].x + across * side * offsets[i].y;
                VillageBuildingAt(models[i], p, (yard - p).normalized, 1f, yard, center, root, village, buildings);
            }
            VillagePath(yard - along * 6f, yard + along * 6f, 12f, yard);
            // A working yard: churned mud where machinery turns, and the muck heap on whichever side has room.
            Ground.Paint(yard - along * 5f + across * side * 2f, yard + along * 4f - across * side * 1f, 8f, .8f, 6);
            foreach (var offset in new[] { new Vector2(-6f, 10f), new Vector2(6f, -11f), new Vector2(-20f, 8f), new Vector2(19f, 9f) })
            {
                Vector2 heap = yard + along * offset.x + across * side * offset.y;
                if (!VillageRoom(heap, 4.4f, false)) continue;
                Place("Scenery_MuckHeap", new Vector3(heap.x, 0f, heap.y), Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg, 1f, 0.1f, root);
                villagePlots.Add((heap, 4.4f));
                Ground.Paint(heap - along * 4f, heap + along * 4f, 7f, .7f, 6);
                break;
            }
            // The drive goes through the gap beside the front shed.
            Vector2 turn = yard - across * side * 4f, gate = yard + along * 6f - across * side * 8f;
            VillagePath(yard, turn, 2.5f, yard);
            VillagePath(turn, gate, 2.5f, yard);
            VillagePath(gate, center + along * 6f, 2.5f, yard);
        }

        // Checks concentric samples no more than 2 m apart: PathField distances are capped, so a large radius
        // cannot safely be added to one centre query. The 1.5 m point clearance covers gaps between samples.
        // Everything stays inside the village's strip along its streets (MapLayout.NearVillage).
        static bool VillageRoom(Vector2 p, float radius, bool keepPaths)
        {
            if (!MapLayout.NearVillage(p, -radius) && !InFringe(p, radius)) return false;
            foreach (var plot in villagePlots)
                if (Vector2.Distance(p, plot.at) < radius + plot.radius + 0.6f) return false;
            if (!Buildable(p) || (keepPaths && Ground.Worn(p))) return false;
            int rings = Mathf.CeilToInt(radius / 2f);
            for (int ring = 1; ring <= rings; ring++)
            {
                float r = radius * ring / rings;
                int samples = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / 2f));
                for (int k = 0; k < samples; k++)
                {
                    float a = k * 2f * Mathf.PI / samples;
                    Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!Buildable(q) || (keepPaths && Ground.Worn(q))) return false;
                }
            }
            return true;
        }

        // Orchard trees and utility huts may also stand inside the village's paddocks, orchards and rough grass.
        static bool InFringe(Vector2 p, float inset)
        {
            var plan = MapLayout.Plan;
            return plan.world != null && MapLayout.DetailPlan != null && MapLayout.DetailPlan.villages.InFringe(p + plan.world.center, inset);
        }

        // Wears only dry, unfarmed access routes, stopping paths from crossing another building's footprint.
        static void VillagePath(Vector2 from, Vector2 to, float width, Vector2 owner)
        {
            Vector2 line = to - from;
            foreach (var plot in villagePlots)
            {
                if ((plot.at - owner).sqrMagnitude < 0.01f) continue;
                float t = line.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(plot.at - from, line) / line.sqrMagnitude) : 0f;
                if (Vector2.Distance(plot.at, from + line * t) < plot.radius + width * 0.5f) return;
            }
            int steps = Mathf.Max(1, Mathf.CeilToInt(line.magnitude));
            Vector2 side = new Vector2(-line.y, line.x).normalized * width * 0.5f;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)steps);
                if (!VillageGround(p) || !VillageGround(p + side) || !VillageGround(p - side)) return;
            }
            Ground.Wear(from, to, width, 0.75f);
        }

        static readonly string[] Roofs = { "RoofTile", "RoofTileDark", "RoofTileWarm", "RoofSlate" };
        static readonly string[] Walls = { "PlasterWhite", "PlasterCream", "PlasterOchre", "PlasterPink", "PlasterGrey" };

        // Each village leans to its own roofs and walls, and each house varies on that, so a street isn't the same
        // house over and over. Picked by the village and the house's position (not the map's random numbers, which
        // would move everything built after).
        static void Repaint(GameObject house, int village, Vector2 at, VillagePlot? property = null)
        {
            int h = ((Mathf.FloorToInt(at.x * 7.3f) * 73856093) ^ (Mathf.FloorToInt(at.y * 7.3f) * 19349663)) & 0x7fffffff;
            int roof = (village + (h % 3 == 0 ? h / 7 : 0)) % Roofs.Length;
            int wall = (village * 2 + (h % 2 == 0 ? h / 11 : 0)) % Walls.Length;
            if (property.HasValue)
            {
                var plot = property.Value;
                wall = (plot.settlement + plot.palette) % Walls.Length;
                roof = (plot.settlement + (plot.roofChanged ? 1 + plot.palette % 3 : 0)) % Roofs.Length;
            }
            Art.Swap(house, "RoofTile", Roofs[roof]);
            if (property.HasValue && property.Value.roofChanged) Art.Swap(house, "RoofSlate", Roofs[roof]);
            Art.Swap(house, "PlasterWhite", Walls[wall]);
        }

        // The property's use plan owns its small structures, paths and neglected corners.
        static void Garden(Transform root, VillagePlot p, ref int budget)
        {
            float yaw = Mathf.Atan2(-p.outward.x, -p.outward.y) * Mathf.Rad2Deg;
            if (p.farm)
            {
                Vector2 barn = p.Point(p.frontage < 28f ? 0f : -4f, Mathf.Min(p.depth - 9f, p.YardEnd + 5f));
                if (VillageProp("Scenery_Barn", barn, yaw, .8f, 7.2f, root, ref budget, false))
                {
                    Ground.Wear(barn - p.along * 6f, barn + p.along * 6f, 14f, .9f);
                    Ground.Wear(p.Point(p.DriveU, p.YardStart), barn - p.outward * 6f, 3.2f, .9f);
                    // Machinery churns the ground in front of the barn doors (VillagePlot.FarmYard paints the same mud).
                    Ground.Paint(barn - p.outward * 12f, barn - p.outward * 7f, 13f, .85f, 6);
                }
                VillageProp("Scenery_MuckHeap", p.MuckHeap, yaw + 90f, 1f, 4.4f, root, ref budget, false);
            }
            if (p.shed)
            {
                Vector2 at = p.Shed;
                if (VillageProp(p.farm ? "Scenery_MachineryShed" : "Scenery_Hut", at, yaw, p.farm ? .65f : .7f,
                    p.farm ? 3.6f : 2.45f, root, ref budget, false))
                    Ground.Wear(at - p.along * 2.5f, at + p.along * 2.5f, 8f, .85f);
            }
            if (p.logs) VillageProp("Scenery_LogPile", p.Point(-p.frontage * .22f, p.YardEnd + 2f), yaw + 90f, .65f, 2f, root, ref budget, false);
            int trees = p.untended ? 2 : p.cultivated && p.identity % 3 == 0 ? 1 : 0;
            for (int i = 0; i < trees; i++)
            {
                float u = -p.frontage * .15f + i * 3.6f, v = p.depth - 4f - (i % 2) * 5f;
                Vector2 at = p.Point(u, v);
                if (p.Contains(at, -2f) && gardenTrees < 128 && VillageProp("Scenery_Tree", at, p.identity % 360,
                    p.untended ? .18f + i * .03f : .35f, 2f, root, ref budget)) gardenTrees++;
            }
        }

        // Counts all renderers, including hidden LODs, before adding a garden prop and reserving its footprint.
        static bool VillageProp(string model, Vector2 p, float yaw, float scale, float radius,
            Transform root, ref int budget, bool keepPaths = true)
        {
            if (budget <= 0 || !VillageRoom(p, radius, keepPaths)) return false;
            var pos = new Vector3(p.x, 0f, p.y);
            int cost = Art.ModelRendererCount(model, pos);
            if (cost > budget) return false;
            var prop = Place(model, pos, yaw, scale, 0.1f, root);
            if (prop == null) return false;
            if (model == "Scenery_LogPile" || model == "Scenery_Tree") Features.VillagePropLods(prop);
            budget -= cost;
            VillagePropRenderers += cost;
            villagePlots.Add((p, radius));
            return true;
        }

        // Point clearance is used across each full footprint, keeping buildings and props off roads and tracks.
        static bool Buildable(Vector2 p) => VillageGround(p)
            && MapLayout.RoadField.Distance(p.x, p.y) >= MapLayout.RoadHalfWidth + 1.5f
            && MapLayout.TrackField.Distance(p.x, p.y) >= 3f;

        // Shared by building footprints and worn paths; unlike the old centre test this includes standing water.
        static bool VillageGround(Vector2 p)
        {
            if (Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge) return false;
            if (MapLayout.HasRiver && MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverHalfWidth + 2.5f) return false;
            if (MapLayout.LakeDistance(p.x, p.y) < 1.5f || MapLayout.NearDam(p, 1.5f) || MapLayout.InGorge(p, 1.5f)) return false;
            return !MapLayout.OnWorks(p, 1.5f) && !MapLayout.NearFlatSite(p, 2f) && !MapLayout.InField(p, 1.5f);
        }

        // ---- Woods ----

        // Logical woods keep real spacing; FarScatter spends the render allowance around each camera.
        static void BuildWoods(Transform parent, System.Random rng)
        {
            var root = new GameObject("Woods").transform;
            root.SetParent(parent, false);

            SectorForest.Build(rng, woodland);
            if (!FarTerrain.Active) root.gameObject.AddComponent<FarScatter>();
            var plan = MapLayout.Plan;

            // Sample by metres, not vertex count, so changing the river curve's tessellation cannot change
            // the real spacing or pack all the bank trees into one end of the larger map.
            var river = MapLayout.RiverField.points;
            float walked = 0f, next = 0f;
            for (int i = 1; i < river.Count; i++)
            {
                if (plan.world != null && !MapLayout.RiverField.IsValidSegment(i - 1)) { walked = next = 0f; continue; }
                float length = Vector2.Distance(river[i - 1], river[i]);
                if (length < 0.001f) continue;
                Vector2 dir = (river[i] - river[i - 1]) / length, side = new Vector2(-dir.y, dir.x);
                for (; next <= walked + length; next += 4f)
                {
                    Vector2 bank = Vector2.Lerp(river[i - 1], river[i], (next - walked) / length);
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        if (rng.NextDouble() > 0.42) continue;
                        float width = plan.world != null ? MapLayout.RiverWidthAt(bank.x, bank.y) : MapLayout.RiverHalfWidth;
                        Vector2 p = bank + side * sign * (width + Range(rng, 3.2f, 9f));
                        if (plan.world != null && (plan.world.CoverAt(p.x, p.y) != Cover.Forest || InClearing(p))) continue;
                        if (Vector2.Distance(p, MapLayout.BridgePoint) < 14f || !MapLayout.IsWild(p, -2f)) continue;
                        double roll = rng.NextDouble();
                        string model = roll < 0.45 ? "Scenery_Tree" : roll < 0.65 ? "Scenery_Tree_B" : roll < 0.8 ? "Scenery_Birch" : "Scenery_Bush";
                        Place(model, new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), Range(rng, 0.8f, 1.15f), 0.25f, root);
                    }
                }
                walked += length;
            }
        }

        // Where the woods could grow, before the tree budget thinned them: the forest floor covers all of it.
        static readonly List<Vector2> woodland = new List<Vector2>();

        // Logging clearings (Features fills them with stumps and log stacks) stay open.
        static bool InClearing(Vector2 p)
        {
            foreach (var c in MapLayout.Plan.clearings)
                if (Vector2.Distance(p, c) < Scale.D(24f)) return true;
            return false;
        }

        // A tree for a wood at p: the Varrow Valley's own mix, or, elsewhere, the sector's shares of conifers and
        // birches (more conifers on the outer hills). Woods seen from above grow in stands of one kind 15 to 35 m
        // across (a clump of birch, a block of spruce), so three trees in four take the kind of the stand they're
        // in, and the rest are strays from the whole mix.
        static string PickTree(System.Random rng, bool outer, Vector2 p)
        {
            var plan = MapLayout.Plan;
            float stand = Mathf.Clamp01((Mathf.PerlinNoise(p.x * 0.04f + 300f, p.y * 0.04f + 150f) - 0.22f) / 0.56f);
            double r = rng.NextDouble();
            r = r < 0.75 ? stand : (r - 0.75) / 0.25;
            if (plan.kind != SectorKind.VarrowValley)
            {
                float roll = (float)r, conifer = outer ? Mathf.Min(1f, plan.conifer + 0.35f) : plan.conifer;
                if (roll < conifer * 0.25f) return "Scenery_Pine";
                if (roll < conifer * 0.4f) return "Scenery_Pine_B";
                if (roll < conifer) return "Scenery_Spruce";
                if (roll < conifer + plan.birch * (1f - conifer)) return "Scenery_Birch";
                float rest = (float)rng.NextDouble();
                return rest < 0.55f ? "Scenery_Tree" : rest < 0.85f ? "Scenery_Tree_B" : "Scenery_Poplar";
            }
            if (outer)
                return r < 0.4 ? "Scenery_Spruce" : r < 0.6 ? "Scenery_Pine" : r < 0.75 ? "Scenery_Pine_B" : r < 0.9 ? "Scenery_Tree" : "Scenery_Tree_B";
            return r < 0.35 ? "Scenery_Tree" : r < 0.6 ? "Scenery_Tree_B" : r < 0.72 ? "Scenery_Birch" : r < 0.86 ? "Scenery_Pine" : "Scenery_Spruce";
        }

        // ---- Rocks ----

        // Boulders out on the open ground and against the riverbanks by the bridge: photo-scanned ones (see Scans)
        // when they've been imported, else the procedural rocks.
        static void BuildRocks(Transform parent, System.Random rng)
        {
            var root = new GameObject("Rocks").transform;
            root.SetParent(parent, false);
            int placed = 0;
            for (int attempt = 0; attempt < 1200 && placed < 136; attempt++)
            {
                var p = new Vector2(Range(rng, -MapLayout.Edge, MapLayout.Edge), Range(rng, -MapLayout.Edge, MapLayout.Edge));
                if (!MapLayout.IsWild(p, 1f)) continue;
                float scale = Range(rng, 0.6f, 1.8f);
                double roll = rng.NextDouble();
                Scans.Rock(Scans.Boulders, roll, new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), 0.55f + 0.3f * scale, root,
                    roll < 0.5 ? "Scenery_Rock" : "Scenery_Rock_B", scale, 0.3f * scale);
                placed++;
            }
            // Boulders piled against the riverbanks by the bridge.
            Vector2 b = MapLayout.BridgePoint;
            var variety = new System.Random(13); // picks the scans without changing rng's sequence
            for (int k = 0; k < 8; k++)
            {
                var p = b + new Vector2(Range(rng, -11f, 11f), Range(rng, -11f, 11f));
                float d = MapLayout.RiverField.Distance(p.x, p.y);
                if (d < MapLayout.RiverHalfWidth - 0.5f || d > MapLayout.RiverHalfWidth + 3f) continue;
                if (MapLayout.RoadField.Distance(p.x, p.y) < MapLayout.RoadHalfWidth + 3.5f) continue;
                float scale = Range(rng, 0.5f, 0.9f);
                Scans.Rock(Scans.Rocks, variety.NextDouble(), new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), 0.4f + 0.75f * scale, root,
                    "Scenery_Rock_B", scale, 0.25f * scale);
            }
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}

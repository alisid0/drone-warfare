using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Trees and houses on the far terrain round the camera, so flying out over the big world shows woods and villages
    // instead of a painted picture: tree cards on WorldPlan's forest within 3 km, and houses (townhouse rows,
    // apartments and shops in the towns) on its villages and towns within 5 km. Built in 250 m cells as the camera
    // moves (a few a frame), from the world's own seed, so a place always gets the same trees; drawn with GPU
    // instancing, no GameObjects. The detailed sector keeps its own trees and houses: nothing is scattered on it.
    public class FarScatter : MonoBehaviour
    {
        const float CellSize = 250f, TreeReach = 3000f, HouseReach = 5000f;
        const float TreeSpacing = 12f, HouseSpacing = 30f;
        const int CellsPerFrame = 6, Batch = 1023;

        // One drawable piece of a model: a mesh at its last LOD, its photo materials, and where it sits in the model.
        sealed class Part
        {
            public Mesh mesh;
            public Material[] materials;
            public Matrix4x4 local;
        }

        sealed class Cell
        {
            public Vector3 center; // local
            public Matrix4x4[][] instances; // per part
            public int[] counts;
        }

        static readonly string[] TreeModels = { "Scenery_Tree", "Scenery_Birch", "Scenery_Spruce", "Scenery_Pine" };
        static readonly string[] HouseModels = { "Scenery_House_A", "Scenery_House_B", "Scenery_House_C", "Scenery_House_D",
                                                 "Scenery_Townhouse_Row", "Scenery_Apartment", "Scenery_Shop" };
        Part[] parts; // the trees, then the houses
        readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
        readonly List<long> stale = new List<long>();
        Vector2 builtOrigin;
        int builtSeed = -1;

        // Forgets every cell (a new sector moves the world under the camera).
        public void Clear() => cells.Clear();

        // How many trees and houses the last frame drew, for the test log.
        public static int TreesDrawn { get; private set; }
        public static int HousesDrawn { get; private set; }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || !FarTerrain.Active) return;
            if (parts == null) LoadParts();
            if (builtSeed != WorldPlan.Seed || builtOrigin != FarTerrain.Origin)
            {
                cells.Clear();
                builtSeed = WorldPlan.Seed;
                builtOrigin = FarTerrain.Origin;
            }
            Vector2 local = new Vector2(cam.transform.position.x, cam.transform.position.z);
            Vector2 world = local + FarTerrain.Origin;
            int reach = Mathf.CeilToInt(HouseReach / CellSize);
            int ci = Mathf.FloorToInt(world.x / CellSize), cj = Mathf.FloorToInt(world.y / CellSize);

            // Build the missing cells nearest first, a few a frame; drop the ones left well behind.
            int built = 0;
            for (int ring = 0; ring <= reach && built < CellsPerFrame; ring++)
                for (int dj = -ring; dj <= ring && built < CellsPerFrame; dj++)
                    for (int di = -ring; di <= ring && built < CellsPerFrame; di++)
                    {
                        if (Mathf.Max(Mathf.Abs(di), Mathf.Abs(dj)) != ring) continue;
                        long key = Key(ci + di, cj + dj);
                        if (cells.ContainsKey(key)) continue;
                        cells[key] = Build(ci + di, cj + dj);
                        built++;
                    }
            stale.Clear();
            foreach (var pair in cells)
            {
                Vector2 c = new Vector2(pair.Value.center.x, pair.Value.center.z);
                if ((c - local).magnitude > HouseReach + 2f * CellSize) stale.Add(pair.Key);
            }
            foreach (var key in stale) cells.Remove(key);

            // From high up a tree is a pixel or two and a house not much more: leave them to the far terrain's
            // picture and canopy. Nearer, trees thin out with distance (all of them to 1.2 km, half to 2 km, a quarter
            // beyond), a random part of each cell's list, which is shuffled when it's built.
            float height = cam.transform.position.y - FarTerrain.GroundAt(local);
            bool showTrees = height < 2500f, showHouses = height < 4500f;
            int trees = 0, houses = 0;
            foreach (var cell in cells.Values)
            {
                float distance = (new Vector2(cell.center.x, cell.center.z) - local).magnitude;
                float share = distance < 1200f ? 1f : distance < 2000f ? 0.5f : 0.25f;
                for (int p = 0; p < parts.Length; p++)
                {
                    if (parts[p] == null || cell.counts[p] == 0) continue;
                    bool tree = p < TreeModels.Length;
                    if (distance > (tree ? TreeReach : HouseReach) + CellSize || !(tree ? showTrees : showHouses)) continue;
                    int count = tree ? Mathf.CeilToInt(cell.counts[p] * share) : cell.counts[p];
                    if (tree) trees += count;
                    else houses += count;
                    var part = parts[p];
                    for (int sub = 0; sub < part.mesh.subMeshCount; sub++)
                    {
                        var material = part.materials[Mathf.Min(sub, part.materials.Length - 1)];
                        if (material == null) continue;
                        var rp = new RenderParams(material)
                        {
                            camera = cam, // not the drone feed's camera as well: it renders the near world only
                            shadowCastingMode = ShadowCastingMode.Off,
                            receiveShadows = true,
                            worldBounds = new Bounds(cell.center, new Vector3(CellSize + 40f, 400f, CellSize + 40f)),
                        };
                        var batches = cell.instances[p];
                        for (int b = 0; b < count; b += Batch)
                            Graphics.RenderMeshInstanced(rp, part.mesh, sub, batches, Mathf.Min(Batch, count - b), b);
                    }
                }
            }
            TreesDrawn = trees;
            HousesDrawn = houses;
        }

        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

        // Every tree and house in one cell: trees on a jittered 12 m grid where the world is forest (spruce and pine
        // more often on high ground, a birch in four of the broadleaves), crowns standing about 9 m clear of the far
        // terrain's canopy; houses on a jittered 30 m grid in villages (about half the points) and towns (three in
        // four: rows, apartments and shops near the middle), squared to the block they stand in.
        Cell Build(int ci, int cj)
        {
            var lists = new List<Matrix4x4>[parts.Length];
            for (int p = 0; p < lists.Length; p++) lists[p] = new List<Matrix4x4>();
            float x0 = ci * CellSize, z0 = cj * CellSize;
            float edge = Ground.Size / 2f + 10f;
            var rng = new System.Random(Hash(ci, cj));
            for (float z = z0; z < z0 + CellSize; z += TreeSpacing)
                for (float x = x0; x < x0 + CellSize; x += TreeSpacing)
                {
                    float wx = x + (float)rng.NextDouble() * TreeSpacing, wz = z + (float)rng.NextDouble() * TreeSpacing;
                    double pick = rng.NextDouble(), size = rng.NextDouble(), turn = rng.NextDouble();
                    if (!WorldPlan.Inside(wx, wz) || WorldPlan.CoverAt(wx, wz) != Cover.Forest) continue;
                    Vector2 local = new Vector2(wx, wz) - FarTerrain.Origin;
                    if (Mathf.Abs(local.x) < edge && Mathf.Abs(local.y) < edge) continue;
                    float ground = FarTerrain.GroundAt(local);
                    float conifer = Mathf.Clamp01(0.15f + Mathf.SmoothStep(0f, 1f, (WorldPlan.Height(wx, wz) - 120f) / 220f) * 0.7f);
                    int kind = pick < conifer ? (pick < conifer * 0.55 ? 2 : 3) : (pick - conifer < (1.0 - conifer) * 0.25 ? 1 : 0);
                    var part = parts[kind];
                    if (part == null) continue;
                    float lift = Mathf.Max(0f, FarTerrain.CanopyAt(wx, wz) - 9f);
                    var at = new Vector3(local.x, ground + lift, local.y);
                    lists[kind].Add(Matrix4x4.TRS(at, Quaternion.Euler(0f, (float)turn * 360f, 0f), Vector3.one * (0.85f + 0.35f * (float)size)) * part.local);
                }
            for (float z = z0; z < z0 + CellSize; z += HouseSpacing)
                for (float x = x0; x < x0 + CellSize; x += HouseSpacing)
                {
                    float wx = x + 0.5f * HouseSpacing + ((float)rng.NextDouble() - 0.5f) * 8f, wz = z + 0.5f * HouseSpacing + ((float)rng.NextDouble() - 0.5f) * 8f;
                    double chance = rng.NextDouble(), pick = rng.NextDouble();
                    if (!WorldPlan.Inside(wx, wz)) continue;
                    var cover = WorldPlan.CoverAt(wx, wz);
                    bool town = cover == Cover.Town;
                    if (!(town || cover == Cover.Village) || chance > (town ? 0.75 : 0.5)) continue;
                    Vector2 local = new Vector2(wx, wz) - FarTerrain.Origin;
                    if (Mathf.Abs(local.x) < edge && Mathf.Abs(local.y) < edge) continue;
                    int kind = TreeModels.Length + (town ? (pick < 0.4 ? 4 : pick < 0.6 ? 5 : pick < 0.7 ? 6 : (int)(pick * 40) % 4) : (int)(pick * 4) % 4);
                    // Each 300 m block of a settlement has its own street angle; houses face one of its four sides.
                    float block = Mathf.PerlinNoise(Mathf.Floor(wx / 300f) * 0.37f + 5f, Mathf.Floor(wz / 300f) * 0.37f) * 90f;
                    float yaw = block + 90f * (Hash(Mathf.FloorToInt(wx), Mathf.FloorToInt(wz)) % 4);
                    var house = parts[kind];
                    if (house == null) continue;
                    var at = new Vector3(local.x, FarTerrain.GroundAt(local) - 0.2f, local.y);
                    lists[kind].Add(Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one) * house.local);
                }
            var cell = new Cell
            {
                center = new Vector3(x0 + CellSize / 2f - FarTerrain.Origin.x, WorldPlan.Height(x0 + CellSize / 2f, z0 + CellSize / 2f), z0 + CellSize / 2f - FarTerrain.Origin.y),
                instances = new Matrix4x4[parts.Length][],
                counts = new int[parts.Length],
            };
            for (int p = 0; p < parts.Length; p++)
            {
                var array = lists[p].ToArray();
                for (int k = array.Length - 1; k > 0; k--) // shuffled, so the first part of the list is a fair sample
                {
                    int other = rng.Next(k + 1);
                    (array[k], array[other]) = (array[other], array[k]);
                }
                cell.instances[p] = array;
                cell.counts[p] = array.Length;
            }
            return cell;
        }

        static int Hash(int x, int z)
        {
            unchecked
            {
                int h = x * 73856093 ^ z * 19349663 ^ WorldPlan.Seed * 83492791;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                return h & 0x7fffffff;
            }
        }

        // The last LOD of each tree (its crossed cards and crown card) and of each house, with the photo materials
        // the game uses for them, made instancing-capable.
        void LoadParts()
        {
            parts = new Part[TreeModels.Length + HouseModels.Length];
            for (int p = 0; p < parts.Length; p++)
                parts[p] = LoadPart(p < TreeModels.Length ? TreeModels[p] : HouseModels[p - TreeModels.Length], p < TreeModels.Length ? 2 : 1);
        }

        static Part LoadPart(string model, int lod)
        {
            var prefab = Resources.Load<GameObject>("Models/" + model);
            if (prefab == null) return null;
            Renderer renderer = null;
            var group = prefab.GetComponent<LODGroup>();
            if (group != null)
            {
                var lods = group.GetLODs();
                if (lods.Length > 0)
                {
                    var level = lods[Mathf.Min(lod, lods.Length - 1)];
                    if (level.renderers.Length > 0) renderer = level.renderers[0];
                }
            }
            if (renderer == null) renderer = prefab.GetComponentInChildren<MeshRenderer>();
            var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null) return null;
            var materials = renderer.sharedMaterials;
            var used = new Material[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                var m = materials[i];
                if (m == null) continue;
                var photo = Resources.Load<Material>("Materials/PBR/" + m.name);
                m = photo != null ? photo : m;
                if (!m.enableInstancing) m = new Material(m) { enableInstancing = true };
                used[i] = m;
            }
            return new Part
            {
                mesh = filter.sharedMesh,
                materials = used,
                local = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix,
            };
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Streams a small tree bubble and batches photo models by species, tier and stand tint. Larger shapes belong
    // to ForestHlod and SettlementHlod, so a high camera never submits thousands of sub-pixel tree cards.
    public class FarScatter : MonoBehaviour
    {
        public const float FullReach = 45f, MeshReach = 125f, TreeReach = 350f, HouseReach = 1500f;
        public const int FullLimit = ForestBudget.FullLimit, ReducedLimit = ForestBudget.ReducedLimit, CardLimit = ForestBudget.CardLimit, TriangleLimit = ForestBudget.TriangleLimit;
        const float CellSize = 120f;
        const int Batch = 511, CellsPerFrame = 4, CellLimit = 192; // non-uniform scales also need inverse matrices
        static readonly string[] Trees = { "Scenery_Tree", "Scenery_Birch", "Scenery_Spruce", "Scenery_Pine" };
        static readonly string[] Houses = { "Scenery_House_A", "Scenery_House_B", "Scenery_House_C", "Scenery_House_D",
            "Scenery_Townhouse_Row", "Scenery_Apartment", "Scenery_Shop" };

        // A model may have several renderers at one LOD; retain every piece and its imported transform.
        sealed class Part
        {
            public Mesh mesh;
            public Material[] materials;
            public Matrix4x4 local;
        }

        // The model's full height normalizes meshes and cards to the same 5-24 m trees, including young margins.
        sealed class Model
        {
            public Part[] parts;
            public float height;
            public int triangles, tier;
            public readonly List<Matrix4x4> instances = new List<Matrix4x4>();
        }

        // Root transforms stay immutable when the camera changes tiers; the world grid owns the trees.
        struct Tree
        {
            public Vector3 position;
            public ForestPattern.Stand stand;
            public Matrix4x4 mesh, reduced, card;
        }

        // Small cells bound generation work and culling without creating scenery GameObjects.
        sealed class Cell
        {
            public Bounds bounds;
            public Tree[] trees;
        }

        Model[] models;
        readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
        readonly List<long> stale = new List<long>();
        readonly List<Material> ownedMaterials = new List<Material>();
        readonly Matrix4x4[] batch = new Matrix4x4[Batch];
        readonly Plane[] planes = new Plane[6];
        readonly List<Visible> visible = new List<Visible>();
        readonly List<ForestBudget.Entry> costs = new List<ForestBudget.Entry>();
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        readonly ForestHlod forest = new ForestHlod();
        readonly SettlementHlod settlements = new SettlementHlod();
        readonly LandmarkHlod landmarks = new LandmarkHlod();
        Vector2 builtOrigin;
        int builtSeed = -1;
        int builtVersion = -1, generatedFrame = -1;
        bool sectorOnly;

        // Sort by distance with a stable tie-break so budget pressure lowers detail rather than deleting trees.
        struct Visible : System.IComparable<Visible>
        {
            public Tree tree;
            public float distance;
            // Stable distance ties keep budget selection repeatable as dictionary iteration order changes.
            public int CompareTo(Visible other)
            {
                int order = distance.CompareTo(other.distance);
                if (order == 0) order = tree.position.x.CompareTo(other.tree.position.x);
                return order != 0 ? order : tree.position.z.CompareTo(other.tree.position.z);
            }
        }

        public static int MeshTreesDrawn { get; private set; }
        public static int CardTreesDrawn { get; private set; }
        public static int TreesDrawn => MeshTreesDrawn + CardTreesDrawn;
        public static int HousesDrawn { get; private set; }
        public static int InstanceDrawCalls { get; private set; }
        public static int FullTreesDrawn { get; private set; }
        public static int ReducedTreesDrawn { get; private set; }
        public static int TreeTrianglesDrawn { get; private set; }
        public static int CachedTreeRecords { get; private set; }
        public static Vector4 TierDistances { get; private set; } = new Vector4(45f, 125f, 350f, 700f);

        // Explicit camera submission supports photo, scene and gameplay cameras without sharing their allowances.
        void OnEnable()
        {
            sectorOnly = transform.parent != null;
            RenderPipelineManager.beginCameraRendering += Render;
        }

        // Unsubscribe even when the persistent far terrain is temporarily disabled.
        void OnDisable() => RenderPipelineManager.beginCameraRendering -= Render;

        // Release generated resources on sector/seed changes, not just their dictionary entries.
        public void Clear()
        {
            cells.Clear(); forest.Clear(); settlements.Clear(); landmarks.Clear();
            builtVersion = -1; generatedFrame = -1;
            MeshTreesDrawn = CardTreesDrawn = HousesDrawn = InstanceDrawCalls = 0;
            FullTreesDrawn = ReducedTreesDrawn = TreeTrianglesDrawn = CachedTreeRecords = 0;
        }

        // Also release cloned instancing materials when the persistent terrain object is destroyed.
        void OnDestroy()
        {
            Clear();
            foreach (var material in ownedMaterials) Art.Discard(material);
            ownedMaterials.Clear();
        }

        // Keep the ground lookup in one place for FarDetail's independent height-source change.
        // On the streamed detail tile's surface where one is (or will be), else the far terrain's ground. The tiered
        // trees stand on the ground itself (no canopy lift), so this is the bare-ground form of FarDetail's lookup.
        internal static float GroundAt(Vector2 local) => InSector(local) || !FarTerrain.Active
            ? Ground.HeightAt(new Vector3(local.x, 0f, local.y)) : FarDetail.ScatterHeight(local, false);

        // 3D distance includes camera altitude, so the 2.5/9 km views submit no individual trees or houses.
        void Render(ScriptableRenderContext context, Camera cam)
        {
            // The first World.Build can create a local preview renderer before FarTerrain creates its persistent one.
            if (sectorOnly && FarTerrain.Active) return;
            if (cam == null || (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView)) return;
            MeshTreesDrawn = CardTreesDrawn = HousesDrawn = InstanceDrawCalls = 0;
            FullTreesDrawn = ReducedTreesDrawn = TreeTrianglesDrawn = 0;
            if (!SectorForest.Ready) return;
            if (models == null) LoadModels();
            if (builtSeed != WorldPlan.Seed || builtOrigin != FarTerrain.Origin || builtVersion != SectorForest.Version)
            {
                Clear(); builtSeed = WorldPlan.Seed; builtOrigin = FarTerrain.Origin; builtVersion = SectorForest.Version;
                forest.AdoptSector();
            }
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            foreach (var model in models) if (model != null) model.instances.Clear();
            Vector3 position = cam.transform.position;
            Vector2 local = new Vector2(position.x, position.z), world = local + FarTerrain.Origin;
            float altitude = position.y - GroundAt(local);
            if (altitude < TreeReach + 150f && generatedFrame != Time.frameCount)
            {
                generatedFrame = Time.frameCount;
                int ci = Mathf.FloorToInt(world.x / CellSize), cj = Mathf.FloorToInt(world.y / CellSize), built = 0;
                int reach = Mathf.CeilToInt((TreeReach + 180f) / CellSize);
                for (int ring = 0; ring <= reach && built < CellsPerFrame; ring++)
                    for (int dz = -ring; dz <= ring && built < CellsPerFrame; dz++)
                        for (int dx = -ring; dx <= ring && built < CellsPerFrame; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                            long key = Key(ci + dx, cj + dz);
                            if (cells.ContainsKey(key)) continue;
                            cells.Add(key, Build(ci + dx, cj + dz)); built++;
                        }
            }
            stale.Clear();
            visible.Clear();
            foreach (var pair in cells)
            {
                var cell = pair.Value;
                if (cell.bounds.SqrDistance(position) > 780f * 780f) { stale.Add(pair.Key); continue; }
                bool inView = GeometryUtility.TestPlanesAABB(planes, cell.bounds);
                if (!inView && cell.bounds.SqrDistance(position) > MeshReach * MeshReach) continue;
                foreach (var tree in cell.trees)
                {
                    float distance = Vector3.Distance(position, tree.position);
                    if (distance <= TreeReach && (inView || distance <= MeshReach)) visible.Add(new Visible { tree = tree, distance = distance });
                }
            }
            foreach (long key in stale) cells.Remove(key);
            while (cells.Count > CellLimit)
            {
                long oldest = 0; float farthest = -1f;
                foreach (var pair in cells)
                {
                    float distance = pair.Value.bounds.SqrDistance(position);
                    if (distance > farthest) { farthest = distance; oldest = pair.Key; }
                }
                cells.Remove(oldest);
            }
            CachedTreeRecords = 0;
            foreach (var cell in cells.Values) CachedTreeRecords += cell.trees.Length;
            SelectTiers(ReadyReach(world));
            forest.Update(cam, planes);
            if (FarTerrain.Active) { landmarks.Update(cam, planes); settlements.Update(cam, planes, this); }
            Draw(cam);
        }

        // Account for BOTH draws during each fade. Overflow moves the whole boundary inward, leaving canopy coverage.
        void SelectTiers(float readyReach)
        {
            visible.Sort();
            costs.Clear();
            foreach (var v in visible)
            {
                int kind = v.tree.stand.species * 3 + v.tree.stand.shade;
                costs.Add(new ForestBudget.Entry { distance = v.distance,
                    fullTriangles = models[kind] != null ? models[kind].triangles : 0,
                    reducedTriangles = models[12 + kind] != null ? models[12 + kind].triangles : 0,
                    cardTriangles = models[24 + kind] != null ? models[24 + kind].triangles : 0 });
            }
            TierDistances = ForestBudget.Select(costs, readyReach);
            float full = TierDistances.x, reduced = TierDistances.y, card = TierDistances.z;
            foreach (var v in visible)
            {
                int kind = v.tree.stand.species * 3 + v.tree.stand.shade;
                if (v.distance <= full) AddTree(kind, v.tree.mesh);
                if (v.distance >= full - 10f && v.distance <= reduced) AddTree(12 + kind, v.tree.reduced);
                if (v.distance >= reduced - 20f && v.distance <= card) AddTree(24 + kind, v.tree.card);
            }
            MeshTreesDrawn = FullTreesDrawn + ReducedTreesDrawn;
        }

        // Retain canopy until the entire tree bubble is ready; prefetch gives a 40 m/s flight several seconds' lead.
        float ReadyReach(Vector2 world)
        {
            int ci = Mathf.FloorToInt(world.x / CellSize), cj = Mathf.FloorToInt(world.y / CellSize);
            float reach = TreeReach;
            for (int j = cj - 4; j <= cj + 4; j++)
                for (int i = ci - 4; i <= ci + 4; i++)
                {
                    if (cells.ContainsKey(Key(i, j))) continue;
                    float x = Mathf.Max(i * CellSize - world.x, Mathf.Max(0f, world.x - (i + 1) * CellSize));
                    float z = Mathf.Max(j * CellSize - world.y, Mathf.Max(0f, world.y - (j + 1) * CellSize));
                    reach = Mathf.Min(reach, Mathf.Sqrt(x * x + z * z));
                }
            return reach;
        }

        // Counters describe submitted instances and triangles, including transition overlap and all submeshes.
        void AddTree(int kind, Matrix4x4 matrix)
        {
            var model = models[kind];
            if (model == null) return;
            model.instances.Add(matrix); TreeTrianglesDrawn += model.triangles;
            if (kind < 12) FullTreesDrawn++; else if (kind < 24) ReducedTreesDrawn++; else CardTreesDrawn++;
        }

        // Settlement blocks call this only when their corresponding combined roof group is hidden.
        internal void House(SettlementPattern.House house)
        {
            var model = models[36 + house.kind];
            if (model == null) return;
            Vector2 p = house.world - FarTerrain.Origin;
            model.instances.Add(Matrix4x4.TRS(new Vector3(p.x, GroundAt(p) - 0.2f, p.y),
                Quaternion.Euler(0f, house.yaw, 0f), Vector3.one));
            HousesDrawn++;
        }

        // Pack across cells: one draw per populated model/submesh/tint batch, not one per tiny cell.
        void Draw(Camera cam)
        {
            var bounds = new Bounds(cam.transform.position, Vector3.one * 3600f);
            ForestMaterials.SetCamera(properties, cam.transform.position, TierDistances);
            foreach (var model in models)
            {
                if (model == null || model.instances.Count == 0) continue;
                foreach (var part in model.parts)
                    for (int start = 0; start < model.instances.Count; start += Batch)
                    {
                        int count = Mathf.Min(Batch, model.instances.Count - start);
                        for (int i = 0; i < count; i++) batch[i] = model.instances[start + i] * part.local;
                        for (int sub = 0; sub < part.mesh.subMeshCount; sub++)
                        {
                            var material = part.materials[Mathf.Min(sub, part.materials.Length - 1)];
                            if (material == null) continue;
                            var rp = new RenderParams(material) { camera = cam, worldBounds = bounds,
                                matProps = properties, shadowCastingMode = model.tier < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                            Graphics.RenderMeshInstanced(rp, part.mesh, sub, batch, count);
                            InstanceDrawCalls++;
                        }
                    }
            }
        }

        // A unique signed pair avoids collisions on either side of the world's origin.
        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

        // The globally aligned candidate cells fit exactly in a 120 m cell, preventing duplicate boundary trees.
        Cell Build(int ci, int cj)
        {
            var trees = new List<Tree>();
            var center = new Vector2((ci + 0.5f) * CellSize, (cj + 0.5f) * CellSize) - FarTerrain.Origin;
            var bounds = new Bounds(new Vector3(center.x, GroundAt(center), center.y), new Vector3(CellSize + 40f, 240f, CellSize + 40f));
            int count = Mathf.RoundToInt(CellSize / ForestPattern.TreeStep);
            for (int j = cj * count; j < (cj + 1) * count; j++)
                for (int i = ci * count; i < (ci + 1) * count; i++)
                {
                    if (!SectorForest.Tree(i, j, out Vector2 world, out ForestPattern.Stand stand)) continue;
                    Vector2 p = world - FarTerrain.Origin;
                    if (InSector(p) && !SectorForest.Ready) continue;
                    var at = new Vector3(p.x, GroundAt(p) - 0.15f, p.y);
                    var tree = new Tree { position = at, stand = stand };
                    float lean = (1f - stand.cover) * 14f;
                    var rotation = Quaternion.Euler((ForestPattern.Unit(i, j, 51) - 0.5f) * lean,
                        ForestPattern.Unit(i, j, 44) * 360f, (ForestPattern.Unit(i, j, 52) - 0.5f) * lean);
                    // Cache both roots once: flight changes the tier, not thousands of TRS matrices each frame.
                    for (int tier = 0; tier < 3; tier++)
                    {
                        var model = models[tier * 12 + stand.species * 3 + stand.shade];
                        if (model == null) continue;
                        float size = stand.height / model.height;
                        var matrix = Matrix4x4.TRS(at, rotation, new Vector3(size * stand.width, size, size * stand.width));
                        if (tier == 0) tree.mesh = matrix; else if (tier == 1) tree.reduced = matrix; else tree.card = matrix;
                    }
                    trees.Add(tree);
                    bounds.Encapsulate(at); bounds.Encapsulate(at + Vector3.up * 26f);
                }
            return new Cell { trees = trees.ToArray(), bounds = bounds };
        }

        // Half-open ownership joins logical sector stems to the same world candidates without an empty border.
        internal static bool InSector(Vector2 local) =>
            local.x >= -Ground.Size / 2f && local.x < Ground.Size / 2f && local.y >= -Ground.Size / 2f && local.y < Ground.Size / 2f;

        // Keep the photo material path used by Art, with three reusable tint variants for each tree tier.
        void LoadModels()
        {
            models = new Model[43];
            for (int tier = 0; tier < 3; tier++)
                for (int species = 0; species < 4; species++)
                    for (int shade = 0; shade < 3; shade++)
                        models[tier * 12 + species * 3 + shade] = LoadModel(tier == 1 ? Trees[species].Replace("Scenery_", "Forest_") + "_Reduced" : Trees[species],
                            tier == 1 ? 0 : tier, ForestPattern.Tint(shade), tier, species);
            for (int i = 0; i < Houses.Length; i++) models[36 + i] = LoadModel(Houses[i], 1, 1f, 3, -1);
        }

        // Read every renderer of the selected LOD and use its transformed bounds to normalize tree height.
        Model LoadModel(string name, int lod, float tint, int tier, int species)
        {
            var prefab = Resources.Load<GameObject>("Models/" + name);
            if (prefab == null) return null;
            Renderer[] renderers = null;
            var group = prefab.GetComponent<LODGroup>();
            if (group != null)
            {
                var lods = group.GetLODs();
                if (lods.Length > 0) renderers = lods[Mathf.Min(lod, lods.Length - 1)].renderers;
            }
            if (renderers == null || renderers.Length == 0) renderers = prefab.GetComponentsInChildren<MeshRenderer>();
            var parts = new List<Part>();
            float bottom = float.MaxValue, top = float.MinValue;
            int triangles = 0;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || renderer.sharedMaterials.Length == 0) continue;
                var materials = renderer.sharedMaterials;
                var local = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    var photo = Resources.Load<Material>("Materials/PBR/" + materials[i].name);
                    var material = tier < 3 ? ForestMaterials.Create(materials[i].name, photo != null ? photo : materials[i], tier)
                        : new Material(photo != null ? photo : materials[i]) { enableInstancing = true };
                    var colour = material.color; colour.r *= tint; colour.g *= tint; colour.b *= tint;
                    if (species >= 0 && (materials[i].name.StartsWith("Leaves") || materials[i].name.StartsWith("Impostor")))
                    {
                        Color correction = ForestPalette.PhotoTint(species);
                        colour.r *= correction.r; colour.g *= correction.g; colour.b *= correction.b;
                    }
                    material.color = colour; materials[i] = material; ownedMaterials.Add(material);
                }
                var box = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = local.MultiplyPoint3x4(box.center + Vector3.Scale(box.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                    bottom = Mathf.Min(bottom, p.y); top = Mathf.Max(top, p.y);
                }
                parts.Add(new Part { mesh = filter.sharedMesh, materials = materials, local = local });
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) triangles += (int)filter.sharedMesh.GetIndexCount(sub) / 3;
            }
            return parts.Count == 0 ? null : new Model { parts = parts.ToArray(), height = Mathf.Max(1f, top - bottom), triangles = triangles, tier = tier };
        }
    }
}

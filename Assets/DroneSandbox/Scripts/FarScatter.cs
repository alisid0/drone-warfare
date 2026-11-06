using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
        const int Batch = 511, CellLimit = 192, MovementGuard = 16; // non-uniform scales also need inverse matrices
        static readonly string[] Trees = { "Scenery_Tree", "Scenery_Birch", "Scenery_Spruce", "Scenery_Pine" };
        static readonly string[] Houses = { "Scenery_House_A", "Scenery_House_B", "Scenery_House_C", "Scenery_House_D",
            "Scenery_Townhouse_Row", "Scenery_Apartment", "Scenery_Shop" };

        // A model may have several renderers at one LOD; retain every piece and its imported transform.
        sealed class Part
        {
            public Mesh mesh;
            public Material[] materials;
            public Matrix4x4 local;
            public Vector3 center;
        }

        // The model's full height normalizes meshes and cards to the same 5-24 m trees, including young margins.
        sealed class Model
        {
            public Part[] parts;
            public float height;
            public int triangles, tier;
            public bool fades;
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
        readonly Dictionary<Camera, View> views = new Dictionary<Camera, View>();
        readonly List<Camera> staleCameras = new List<Camera>();
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        readonly ForestHlod forest = new ForestHlod();
        readonly SettlementHlod settlements = new SettlementHlod();
        readonly LandmarkHlod landmarks = new LandmarkHlod();
        readonly Stopwatch workTimer = new Stopwatch(), renderTimer = new Stopwatch();
        Vector2 builtOrigin;
        int builtSeed = -1, builtVersion = -1, generatedFrame = -1, cellVersion;
        bool sectorOnly, fallbackMode;
        IEnumerator cellBuilder;
        Cell buildingCell;
        long buildingKey;
        Vector2 buildingFrom;
        Camera buildingCamera;
        static double totalMs, frameMs;
        static int timingFrame = -1;
        int nextCameraSlot;

        // Completed matrix batches are immutable and can be submitted again without walking tree records.
        sealed class DrawBatch
        {
            public Part part;
            public Matrix4x4[] matrices;
            public Bounds bounds;
            public int tier;
        }

        // Each camera keeps its own completed selection while a replacement is assembled in small slices.
        sealed class View
        {
            public readonly List<DrawBatch> draws = new List<DrawBatch>();
            public IEnumerator builder;
            public Vector3 position, buildingFrom;
            public Vector4 distances;
            public float refreshed;
            public int version = -1, full, reduced, cards, triangles, slot, lastFrame;
        }

        // A selection copies only a cell reference and index, never three matrices into a sortable record.
        struct Visible
        {
            public Cell cell;
            public int index;
            public float distance;
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
        public static Vector4 TierDistances { get; private set; } = new Vector4(0f, 0f, 0f, 700f);
        public static float MainThreadMs { get; private set; }
        public static float MeanMs { get; private set; }
        public static float WorstMs { get; private set; }
        public static int Samples { get; private set; }
        public static int RecordsVisited { get; private set; }
        public static int ReadyCells { get; private set; }

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
            cells.Clear(); views.Clear(); cellBuilder = null; buildingCell = null;
            forest.Clear(); settlements.Clear(); landmarks.Clear();
            builtVersion = -1; generatedFrame = -1; nextCameraSlot = 0; cellVersion++;
            MeshTreesDrawn = CardTreesDrawn = HousesDrawn = InstanceDrawCalls = 0;
            FullTreesDrawn = ReducedTreesDrawn = TreeTrianglesDrawn = CachedTreeRecords = ReadyCells = 0;
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

        // Timed generation and selection are independent of draw submission and shared by every render tier.
        void Render(ScriptableRenderContext context, Camera cam)
        {
            if (sectorOnly && FarTerrain.Active) return;
            if (cam == null || (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView)) return;
            if (!SectorForest.Ready) return;
            renderTimer.Restart();
            MeshTreesDrawn = CardTreesDrawn = HousesDrawn = InstanceDrawCalls = 0;
            FullTreesDrawn = ReducedTreesDrawn = TreeTrianglesDrawn = 0;
            if (timingFrame != Time.frameCount) RecordsVisited = 0;
            if (models != null && fallbackMode != ForestMaterials.ForceFallback)
            {
                foreach (var material in ownedMaterials) Art.Discard(material);
                ownedMaterials.Clear(); models = null; Clear();
            }
            if (models == null) { LoadModels(); fallbackMode = ForestMaterials.ForceFallback; }
            if (builtSeed != WorldPlan.Seed || builtOrigin != FarTerrain.Origin || builtVersion != SectorForest.Version)
            {
                Clear(); builtSeed = WorldPlan.Seed; builtOrigin = FarTerrain.Origin; builtVersion = SectorForest.Version;
                forest.AdoptSector();
            }
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 position = cam.transform.position;
            Vector2 local = new Vector2(position.x, position.z), world = local + FarTerrain.Origin;
            if (Time.frameCount % 120 == 0)
            {
                staleCameras.Clear();
                foreach (var pair in views)
                    if (pair.Key == null || Time.frameCount - pair.Value.lastFrame > 300) staleCameras.Add(pair.Key);
                foreach (var staleCamera in staleCameras) views.Remove(staleCamera);
                nextCameraSlot = 0;
                foreach (var observer in views.Values) observer.slot = nextCameraSlot++;
            }
            if (!views.TryGetValue(cam, out var view)) views.Add(cam, view = new View { slot = nextCameraSlot++ });
            view.lastFrame = Time.frameCount;
            // Never reuse a distant camera's tree hole or pending selection after a teleport.
            if ((position - view.position).sqrMagnitude > MovementGuard * MovementGuard) view.draws.Clear();
            if (view.builder != null && (position - view.buildingFrom).sqrMagnitude > MovementGuard * MovementGuard) view.builder = null;
            if (position.y - GroundAt(local) < TreeReach + 150f && generatedFrame != Time.frameCount
                && (cellBuilder != null || Time.frameCount % views.Count == view.slot % views.Count))
            {
                generatedFrame = Time.frameCount;
                Generate(world, position, cam);
            }
            if (view.builder == null && (view.version != cellVersion || Time.unscaledTime - view.refreshed >= 0.12f
                || view.draws.Count == 0 && (position - view.position).sqrMagnitude > 1f))
            {
                view.buildingFrom = position;
                view.builder = Select(view, position, ReadyReach(world));
            }
            workTimer.Restart();
            while (view.builder != null && workTimer.Elapsed.TotalMilliseconds < 0.12)
                if (!view.builder.MoveNext()) view.builder = null;
            TierDistances = view.draws.Count > 0 ? view.distances : new Vector4(0f, 0f, 0f, 700f);
            // The guard bounds camera movement while cached matrices are in use; a missing bubble leaves canopy intact.
            if (view.draws.Count > 0)
            {
                FullTreesDrawn = view.full; ReducedTreesDrawn = view.reduced; CardTreesDrawn = view.cards;
                MeshTreesDrawn = view.full + view.reduced; TreeTrianglesDrawn = view.triangles;
                DrawTrees(cam, view);
            }
            forest.Update(cam, planes);
            if (timingFrame != Time.frameCount)
            {
                if (timingFrame >= 0) { totalMs += frameMs; Samples++; MeanMs = (float)(totalMs / Samples); }
                timingFrame = Time.frameCount; frameMs = 0;
            }
            frameMs += renderTimer.Elapsed.TotalMilliseconds;
            MainThreadMs = (float)frameMs; WorstMs = Mathf.Max(WorstMs, MainThreadMs);
            // Settlement accounting remains outside the forest timer and retains its existing model submission path.
            for (int i = 36; i < models.Length; i++) if (models[i] != null) models[i].instances.Clear();
            if (FarTerrain.Active) { landmarks.Update(cam, planes); settlements.Update(cam, planes, this); }
            Draw(cam);
        }

        // Claude can reset measured CPU samples immediately before the low-flight interval, after initial asset loading.
        public static void ResetTimings()
        {
            totalMs = frameMs = 0; timingFrame = -1; Samples = 0; MainThreadMs = MeanMs = WorstMs = 0f;
        }

        // A candidate cell is published only when complete; a bounded iterator avoids four synchronous builds per frame.
        void Generate(Vector2 world, Vector3 position, Camera camera)
        {
            if (cellBuilder != null && buildingCamera == camera && (world - buildingFrom).sqrMagnitude > 240f * 240f)
                { cellBuilder = null; buildingCell = null; }
            if (cellBuilder == null)
            {
                int ci = Mathf.FloorToInt(world.x / CellSize), cj = Mathf.FloorToInt(world.y / CellSize);
                int reach = Mathf.CeilToInt((TreeReach + 120f) / CellSize);
                for (int ring = 0; ring <= reach && cellBuilder == null; ring++)
                    for (int dz = -ring; dz <= ring && cellBuilder == null; dz++)
                        for (int dx = -ring; dx <= ring && cellBuilder == null; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                            long key = Key(ci + dx, cj + dz);
                            if (cells.ContainsKey(key)) continue;
                            buildingKey = key; buildingFrom = world; buildingCamera = camera; cellBuilder = Build(ci + dx, cj + dz);
                        }
            }
            workTimer.Restart();
            while (cellBuilder != null && workTimer.Elapsed.TotalMilliseconds < 0.12)
                if (!cellBuilder.MoveNext())
                {
                    cells.Add(buildingKey, buildingCell); CachedTreeRecords += buildingCell.trees.Length;
                    cellVersion++; cellBuilder = null; buildingCell = null;
                    // Pruning only runs on a completed cell, not once per camera per frame.
                    stale.Clear();
                    foreach (var pair in cells)
                    {
                        bool keep = false;
                        foreach (var observer in views.Keys)
                            if (observer != null && pair.Value.bounds.SqrDistance(observer.transform.position) <= 780f * 780f) { keep = true; break; }
                        if (!keep) stale.Add(pair.Key);
                    }
                    foreach (long key in stale) { CachedTreeRecords -= cells[key].trees.Length; cells.Remove(key); }
                    while (cells.Count > CellLimit)
                    {
                        long oldest = 0; float farthest = -1f;
                        foreach (var pair in cells)
                        {
                            float distance = float.MaxValue;
                            foreach (var observer in views.Keys)
                                if (observer != null) distance = Mathf.Min(distance, pair.Value.bounds.SqrDistance(observer.transform.position));
                            if (distance > farthest) { farthest = distance; oldest = pair.Key; }
                        }
                        CachedTreeRecords -= cells[oldest].trees.Length; cells.Remove(oldest);
                    }
                    ReadyCells = cells.Count;
                }
        }

        // Scan, budget and assemble a replacement over many frames; dictionary snapshots survive cell arrivals/evictions.
        IEnumerator Select(View target, Vector3 position, float ready)
        {
            int version = cellVersion;
            var snapshot = new List<Cell>(cells.Values);
            var visible = new List<Visible>();
            var bins = new ForestBudget.Bucket[400];
            int visited = 0;
            foreach (var cell in snapshot)
            {
                if (cell.bounds.SqrDistance(position) > (TreeReach + MovementGuard) * (TreeReach + MovementGuard)) continue;
                for (int i = 0; i < cell.trees.Length; i++)
                {
                    var tree = cell.trees[i];
                    float distance = Vector3.Distance(position, tree.position);
                    RecordsVisited++;
                    if (distance <= TreeReach + MovementGuard)
                    {
                        visible.Add(new Visible { cell = cell, index = i, distance = distance });
                        int kind = tree.stand.species * 3 + tree.stand.shade, at = Mathf.FloorToInt(distance);
                        var b = bins[at];
                        var full = models[kind]; var reduced = models[12 + kind]; var card = models[24 + kind];
                        if (full != null) { b.full++; b.fullTriangles += full.triangles; }
                        if (reduced != null) { b.reduced++; b.reducedTriangles += reduced.triangles; }
                        if (card != null) { b.cards++; b.cardTriangles += card.triangles; }
                        bins[at] = b;
                    }
                    if ((++visited & 31) == 0) yield return null;
                }
            }
            Vector4 distances = ForestBudget.SelectBuckets(bins, ready, MovementGuard);
            yield return null;
            var instances = new List<Matrix4x4>[36];
            var next = new View { position = position, distances = distances, version = version };
            foreach (var v in visible)
            {
                var tree = v.cell.trees[v.index]; int kind = tree.stand.species * 3 + tree.stand.shade;
                for (int tier = 0; tier < 3; tier++)
                {
                    int k = tier * 12 + kind; var model = models[k]; if (model == null) continue;
                    float upper = tier == 0 ? distances.x : tier == 1 ? distances.y : distances.z;
                    float lower = tier == 0 ? 0f : tier == 1 ? distances.x : distances.y;
                    int pad = model.fades ? MovementGuard : 0;
                    float overlap = model.fades ? (tier == 1 ? 10f : tier == 2 ? 20f : 0f) : 0f;
                    if (upper <= 0f || v.distance > upper + pad || tier > 0 && v.distance < lower - overlap - pad) continue;
                    if (instances[k] == null) instances[k] = new List<Matrix4x4>();
                    instances[k].Add(tier == 0 ? tree.mesh : tier == 1 ? tree.reduced : tree.card);
                    next.triangles += model.triangles;
                    if (tier == 0) next.full++; else if (tier == 1) next.reduced++; else next.cards++;
                }
                if ((++visited & 31) == 0) yield return null;
            }
            for (int k = 0; k < instances.Length; k++)
            {
                var list = instances[k]; if (list == null) continue;
                foreach (var part in models[k].parts)
                    for (int start = 0; start < list.Count; start += Batch)
                    {
                        int count = Mathf.Min(Batch, list.Count - start);
                        var matrices = new Matrix4x4[count];
                        var bounds = new Bounds((list[start] * part.local).MultiplyPoint3x4(part.center), Vector3.zero);
                        for (int i = 0; i < count; i++)
                        {
                            matrices[i] = list[start + i] * part.local;
                            bounds.Encapsulate(matrices[i].MultiplyPoint3x4(part.center));
                            if ((i & 31) == 31) yield return null;
                        }
                        bounds.Expand(60f);
                        next.draws.Add(new DrawBatch { part = part, matrices = matrices, bounds = bounds, tier = k / 12 });
                    }
            }
            // Selection can finish while the camera moves; publish only while its conservative guard remains valid.
            target.draws.Clear(); target.draws.AddRange(next.draws); target.position = position;
            target.distances = distances; target.version = version; target.refreshed = Time.unscaledTime;
            target.full = next.full; target.reduced = next.reduced; target.cards = next.cards; target.triangles = next.triangles;
        }

        // Render cached arrays directly; native submission is the only per-instance upload left on ordinary frames.
        void DrawTrees(Camera camera, View view)
        {
            ForestMaterials.SetCamera(properties, camera.transform.position, view.distances);
            foreach (var draw in view.draws)
            {
                if (!GeometryUtility.TestPlanesAABB(planes, draw.bounds)) continue;
                for (int sub = 0; sub < draw.part.mesh.subMeshCount; sub++)
                {
                    var material = draw.part.materials[Mathf.Min(sub, draw.part.materials.Length - 1)];
                    if (material == null) continue;
                    var rp = new RenderParams(material) { camera = camera, worldBounds = draw.bounds, matProps = properties,
                        shadowCastingMode = draw.tier < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                    Graphics.RenderMeshInstanced(rp, draw.part.mesh, sub, draw.matrices, draw.matrices.Length);
                    InstanceDrawCalls++;
                }
            }
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
        IEnumerator Build(int ci, int cj)
        {
            var trees = new List<Tree>();
            var center = new Vector2((ci + 0.5f) * CellSize, (cj + 0.5f) * CellSize) - FarTerrain.Origin;
            var bounds = new Bounds(new Vector3(center.x, GroundAt(center), center.y), new Vector3(CellSize + 40f, 240f, CellSize + 40f));
            int count = Mathf.RoundToInt(CellSize / ForestPattern.TreeStep);
            for (int j = cj * count; j < (cj + 1) * count; j++)
                for (int i = ci * count; i < (ci + 1) * count; i++)
                {
                    yield return null;
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
            buildingCell = new Cell { trees = trees.ToArray(), bounds = bounds };
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
            int triangles = 0; bool fades = true;
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
                    if (tier < 3 && !ForestMaterials.Fades(material)) fades = false;
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
                parts.Add(new Part { mesh = filter.sharedMesh, materials = materials, local = local, center = box.center });
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) triangles += (int)filter.sharedMesh.GetIndexCount(sub) / 3;
            }
            return parts.Count == 0 ? null : new Model { parts = parts.ToArray(), height = Mathf.Max(1f, top - bottom), triangles = triangles, tier = tier, fades = fades };
        }
    }
}

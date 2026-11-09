using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Streams visible crowns by projected size and batches photo models by species, tier and stand tint.
    // ForestHlod supplies continuous coverage while the bounded tree cache and selections catch up.
    public class FarScatter : MonoBehaviour
    {
        public const float TreeReach = 3600f, HouseReach = 1500f; // tree cache bound, not a render tier
        public const int FullLimit = ForestBudget.FullLimit, ReducedLimit = ForestBudget.ReducedLimit, CardLimit = ForestBudget.CardLimit, TriangleLimit = ForestBudget.TriangleLimit;
        const float CellSize = SectorForest.RenderCell;
        const int Batch = 511, CellLimit = 192, MovementGuard = 16; // non-uniform scales also need inverse matrices
        static readonly string[] Trees = { "Scenery_Tree", "Scenery_Birch", "Scenery_Spruce", "Scenery_Pine" };
        static readonly string[] Houses = SettlementPattern.Models;

        // A model may have several renderers at one LOD; retain every piece and its imported transform. A building's
        // RoofTile submesh also keeps that material in each of the four roofs, so roofs vary within one instanced batch.
        sealed class Part
        {
            public Mesh mesh;
            public Material[] materials;
            public Material[][] toned; // per submesh: null, or the roof in SettlementPattern.RoofMaterials order
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
            public List<Matrix4x4>[] tones; // a building's instances again by roof, when it has a toned roof
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
        // Made in Awake: Unity forbids creating property blocks while a MonoBehaviour is being constructed (development
        // builds throw, and the far world never started in one).
        MaterialPropertyBlock properties;
        ForestHlod forest;
        SettlementHlod settlements;
        LandmarkHlod landmarks;
        readonly Stopwatch workTimer = new Stopwatch(), renderTimer = new Stopwatch();
        readonly List<Tree> cellTrees = new List<Tree>(); // the one cell builder's working list
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

        // Completed matrix batches are immutable and can be submitted again without walking tree records. Arrays hold a
        // full batch and are recycled per camera, so a selection allocates nothing once warm.
        sealed class DrawBatch
        {
            public Part part;
            public readonly Matrix4x4[] matrices = new Matrix4x4[Batch];
            public Bounds bounds;
            public int tier, count;
        }

        // Each camera keeps its own completed selection while a replacement is assembled in small slices. Only that
        // camera's one selection uses its scratch lists; an abandoned selection never resumes.
        sealed class View
        {
            public readonly List<DrawBatch> draws = new List<DrawBatch>();
            public IEnumerator builder;
            public Vector3 position, buildingFrom, forward, lastPosition;
            public float focal, ready, reach, observedAt, speed, cruiseUntil, altitude;
            public bool fast;
            public Vector2 viewport;
            public Vector4 distances;
            public float refreshed;
            public int version = -1, full, reduced, cards, triangles, slot, lastFrame;
            public readonly List<Cell> snapshot = new List<Cell>();
            public readonly List<Visible> visible = new List<Visible>();
            public readonly ForestBudget.Bucket[] bins = new ForestBudget.Bucket[400];
            public readonly List<Matrix4x4>[] instances = new List<Matrix4x4>[36];
            public readonly Plane[] planes = new Plane[6];
            public readonly List<DrawBatch> building = new List<DrawBatch>(), spare = new List<DrawBatch>();

            // Published batches return to the spare list when replaced; the next selection starts frames later.
            public void Retire()
            {
                spare.AddRange(draws); draws.Clear();
            }

            // A recycled batch, or a new one while the camera's pool is still warming.
            public DrawBatch Take()
            {
                if (spare.Count == 0) return new DrawBatch();
                var batch = spare[spare.Count - 1]; spare.RemoveAt(spare.Count - 1);
                return batch;
            }
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
        public static Vector4 TierPixels { get; private set; }
        public static float CrownReach { get; private set; }
        public static float ReadyRadius { get; private set; }
        public static float CameraSpeed { get; private set; }
        public static int FastFrames { get; private set; }
        public static int PeakFullTrees { get; private set; }
        public static int PeakReducedTrees { get; private set; }
        public static int PeakCards { get; private set; }
        public static int PeakTreeTriangles { get; private set; }
        public static int SelectionStarts { get; private set; }
        public static int SelectionCancels { get; private set; }
        public static int SelectionCompletes { get; private set; }
        // This frame's landmark and settlement passes over every camera, outside MainThreadMs, for the spike log.
        public static float PlacesMs { get; private set; }
        static int placesFrame = -1;
        static double generationMs, selectionMs, submissionMs, canopyMs;
        public static float GenerationMeanMs => (float)(generationMs / Mathf.Max(1, Samples + 1));
        public static float SelectionMeanMs => (float)(selectionMs / Mathf.Max(1, Samples + 1));
        public static float SubmissionMeanMs => (float)(submissionMs / Mathf.Max(1, Samples + 1));
        public static float CanopyMeanMs => (float)(canopyMs / Mathf.Max(1, Samples + 1));
        static int fastFrame = -1;
        static View drawingView;

        // Projection uses vertical render pixels, including zoom, viewport size and orthographic photo cameras.
        public static float FocalPixels(Camera camera) => Mathf.Max(1f, Mathf.Abs(camera.projectionMatrix.m11) * camera.pixelHeight * 0.5f);

        // Inverse crown pixels sort large crowns first without a camera-height-dependent distance cutoff.
        public static float CrownScore(Vector3 crown, float radius, Vector3 eye, Vector3 forward, float focal)
            => 1000f * (focal < 0f ? 1f : Mathf.Max(1f, Vector3.Dot(crown - eye, forward))) / (2f * radius * Mathf.Abs(focal));

        // Canopy uses the same completed selection as the trees, never a stale hole after a jump or new camera.
        public static void SetForestView(MaterialPropertyBlock block, Camera camera, float build = 1f)
        {
            var view = drawingView;
            ForestMaterials.SetCamera(block, camera.transform.position, view != null ? view.distances : new Vector4(0f, 0f, 0f, 700f), build);
            ForestMaterials.SetProjection(block, view != null ? view.position : camera.transform.position,
                view != null ? view.forward : camera.transform.forward, view != null ? view.focal : FocalPixels(camera) * (camera.orthographic ? -1f : 1f),
                view != null ? view.ready : 0f);
            ForestMaterials.SetViewport(block, view != null ? view.viewport : Vector2.zero);
        }

        // The property block and the canopy, settlement and landmark passes, which hold property blocks of their own.
        void Awake()
        {
            properties = new MaterialPropertyBlock();
            forest = new ForestHlod(); settlements = new SettlementHlod(); landmarks = new LandmarkHlod();
        }

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
            // Observe each camera once, and hold fast mode through skipped feed frames and brief motion stalls.
            float elapsed = Time.unscaledTime - view.observedAt;
            if (view.lastFrame > 0 && elapsed > 0f && elapsed < 0.5f)
            {
                view.speed = Vector3.Distance(position, view.lastPosition) / elapsed;
                if (view.speed > 80f && view.speed < 2000f) view.cruiseUntil = Time.unscaledTime + 0.5f;
            }
            bool cruising = Time.unscaledTime < view.cruiseUntil;
            view.altitude = position.y - GroundAt(local);
            bool fast = cruising;
            CameraSpeed = view.speed;
            if (cruising && fastFrame != Time.frameCount) { FastFrames++; fastFrame = Time.frameCount; }
            view.lastPosition = position; view.observedAt = Time.unscaledTime;
            view.lastFrame = Time.frameCount;
            // Never reuse a distant camera's tree hole or pending selection after a teleport.
            if ((position - view.position).sqrMagnitude > 96f * 96f
                || Vector3.Dot(view.forward, cam.transform.forward) < 0.98f) view.Retire();
            if (view.builder != null && ((position - view.buildingFrom).sqrMagnitude > 96f * 96f || fast != view.fast))
                { view.builder = null; SelectionCancels++; }
            if (fast != view.fast) { view.fast = fast; view.version = -1; }
            double phaseAt = renderTimer.Elapsed.TotalMilliseconds;
            if (generatedFrame != Time.frameCount
                && (cellBuilder != null || Time.frameCount % views.Count == view.slot % views.Count))
            {
                generatedFrame = Time.frameCount;
                Generate(world, position, cam);
            }
            generationMs += renderTimer.Elapsed.TotalMilliseconds - phaseAt;
            phaseAt = renderTimer.Elapsed.TotalMilliseconds;
            if (view.builder == null && (view.version != cellVersion || Time.unscaledTime - view.refreshed >= 0.12f
                || view.draws.Count == 0 && (position - view.position).sqrMagnitude > 1f))
            {
                view.buildingFrom = position;
                view.builder = Select(view, position, cam);
                SelectionStarts++;
            }
            workTimer.Restart();
            while (view.builder != null && workTimer.Elapsed.TotalMilliseconds < 0.20)
                if (!view.builder.MoveNext()) view.builder = null;
            selectionMs += renderTimer.Elapsed.TotalMilliseconds - phaseAt;
            phaseAt = renderTimer.Elapsed.TotalMilliseconds;
            drawingView = view.draws.Count > 0 ? view : null;
            CrownReach = drawingView != null ? view.reach : 0f;
            ReadyRadius = drawingView != null ? view.ready : 0f;
            TierPixels = drawingView != null ? new Vector4(view.distances.x > 0f ? 1000f / view.distances.x : 0f,
                view.distances.y > 0f ? 1000f / view.distances.y : 0f, view.distances.z > 0f ? 1000f / view.distances.z : 0f, 0f) : Vector4.zero;
            TierDistances = new Vector4(0f, 0f, CrownReach, 700f);
            // The guard bounds camera movement while cached matrices are in use; a missing bubble leaves canopy intact.
            if (view.draws.Count > 0)
            {
                FullTreesDrawn = view.full; ReducedTreesDrawn = view.reduced; CardTreesDrawn = view.cards;
                MeshTreesDrawn = view.full + view.reduced; TreeTrianglesDrawn = view.triangles;
                PeakFullTrees = Mathf.Max(PeakFullTrees, view.full); PeakReducedTrees = Mathf.Max(PeakReducedTrees, view.reduced);
                PeakCards = Mathf.Max(PeakCards, view.cards); PeakTreeTriangles = Mathf.Max(PeakTreeTriangles, view.triangles);
                DrawTrees(cam, view);
            }
            submissionMs += renderTimer.Elapsed.TotalMilliseconds - phaseAt;
            phaseAt = renderTimer.Elapsed.TotalMilliseconds;
            forest.Update(cam, planes, cruising);
            canopyMs += renderTimer.Elapsed.TotalMilliseconds - phaseAt;
            if (timingFrame != Time.frameCount)
            {
                if (timingFrame >= 0) { totalMs += MainThreadMs; Samples++; MeanMs = (float)(totalMs / Samples); }
                timingFrame = Time.frameCount; frameMs = 0;
            }
            frameMs += renderTimer.Elapsed.TotalMilliseconds;
            MainThreadMs = (float)frameMs + Canopy.QueryMs; WorstMs = Mathf.Max(WorstMs, MainThreadMs);
            // Settlement accounting remains outside the forest timer and retains its existing model submission path.
            for (int i = 36; i < models.Length; i++)
                if (models[i] != null)
                {
                    models[i].instances.Clear();
                    if (models[i].tones != null) foreach (var tone in models[i].tones) tone.Clear();
                }
            if (FarTerrain.Active) TimePlaces(cam);
            Draw(cam);
        }

        // The landmark and settlement passes, timed per frame; first sight of a compound or village lands here.
        void TimePlaces(Camera cam)
        {
            if (placesFrame != Time.frameCount) { placesFrame = Time.frameCount; PlacesMs = 0f; }
            long started = Stopwatch.GetTimestamp();
            landmarks.Update(cam, planes); settlements.Update(cam, planes, this);
            PlacesMs += (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        }

        // Claude can reset measured CPU samples immediately before the low-flight interval, after initial asset loading.
        public static void ResetTimings()
        {
            totalMs = frameMs = 0; timingFrame = -1; Samples = 0; MainThreadMs = MeanMs = WorstMs = 0f;
            FastFrames = PeakFullTrees = PeakReducedTrees = PeakCards = PeakTreeTriangles = 0;
            SelectionStarts = SelectionCancels = SelectionCompletes = 0; fastFrame = -1;
            generationMs = selectionMs = submissionMs = canopyMs = 0;
            ForestHlod.ResetTimings();
        }

        // A candidate cell is published only when complete; a bounded iterator avoids four synchronous builds per frame.
        void Generate(Vector2 world, Vector3 position, Camera camera)
        {
            if (cellBuilder != null && buildingCamera == camera && (world - buildingFrom).sqrMagnitude > 240f * 240f)
                { cellBuilder = null; buildingCell = null; }
            if (cellBuilder == null)
            {
                // Recipe groves already have exact stems. Empty world cells must not delay woods above painted litter.
                float nearest = float.MaxValue; long chosen = 0; bool found = false;
                foreach (var pair in SectorForest.LocalCells)
                {
                    if (cells.ContainsKey(pair.Key)) continue;
                    var bounds = pair.Value.bounds;
                    if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                    float distance = bounds.SqrDistance(position);
                    if (distance < nearest) { nearest = distance; chosen = pair.Key; found = true; }
                }
                if (found)
                {
                    buildingKey = chosen; buildingFrom = world; buildingCamera = camera;
                    cellBuilder = Build((int)(chosen >> 32), (int)chosen);
                }
                int ci = Mathf.FloorToInt(world.x / CellSize), cj = Mathf.FloorToInt(world.y / CellSize);
                float footprint = camera.orthographic ? Mathf.Sqrt(1f + camera.aspect * camera.aspect) / Mathf.Abs(camera.projectionMatrix.m11)
                    : 9.2f * FocalPixels(camera) / 3f;
                int reach = Mathf.CeilToInt(Mathf.Min(TreeReach, footprint + 240f) / CellSize);
                int wanted = 0;
                for (int ring = 0; ring <= reach && cellBuilder == null && wanted <= CellLimit / Mathf.Max(1, views.Count); ring++)
                    for (int dz = -ring; dz <= ring && cellBuilder == null && wanted <= CellLimit / Mathf.Max(1, views.Count); dz++)
                        for (int dx = -ring; dx <= ring && cellBuilder == null; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                            Vector2 center = new Vector2((ci + dx + 0.5f) * CellSize, (cj + dz + 0.5f) * CellSize) - FarTerrain.Origin;
                            var area = new Bounds(new Vector3(center.x, position.y, center.y), new Vector3(CellSize + 64f, 4000f, CellSize + 64f));
                            if (!GeometryUtility.TestPlanesAABB(planes, area)) continue;
                            if (++wanted > CellLimit / Mathf.Max(1, views.Count)) break;
                            long key = Key(ci + dx, cj + dz);
                            if (cells.ContainsKey(key)) continue;
                            buildingKey = key; buildingFrom = world; buildingCamera = camera; cellBuilder = Build(ci + dx, cj + dz);
                        }
            }
            workTimer.Restart();
            while (cellBuilder != null && workTimer.Elapsed.TotalMilliseconds < 0.20)
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
                            if (observer != null && pair.Value.bounds.SqrDistance(observer.transform.position) <= (TreeReach + 240f) * (TreeReach + 240f)) { keep = true; break; }
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
        IEnumerator Select(View target, Vector3 position, Camera camera)
        {
            int version = cellVersion;
            Vector3 forward = camera.transform.forward;
            bool orthographic = camera.orthographic;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            right = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;
            Vector3 up = Vector3.Cross(forward, right);
            Vector2 viewport = new Vector2(camera.aspect / Mathf.Abs(camera.projectionMatrix.m11), 1f / Mathf.Abs(camera.projectionMatrix.m11));
            float focal = FocalPixels(camera) * (orthographic ? -1f : 1f);
            float ready = ReadyReach(new Vector2(position.x, position.z) + FarTerrain.Origin);
            int guard = Mathf.Min(64, Mathf.CeilToInt(1000f * MovementGuard / (4f * Mathf.Max(1f, Mathf.Abs(focal)))));
            // This camera's reused scratch (hd167 allocated about 0.5 MB per selection, 50 selections a second).
            var selectionPlanes = target.planes; GeometryUtility.CalculateFrustumPlanes(camera, selectionPlanes);
            var snapshot = target.snapshot; snapshot.Clear(); snapshot.AddRange(cells.Values);
            var visible = target.visible; visible.Clear();
            var bins = target.bins; System.Array.Clear(bins, 0, bins.Length);
            target.spare.AddRange(target.building); target.building.Clear();
            int visited = 0;
            foreach (var cell in snapshot)
            {
                if (!GeometryUtility.TestPlanesAABB(selectionPlanes, cell.bounds)) continue;
                for (int i = 0; i < cell.trees.Length; i++)
                {
                    var tree = cell.trees[i];
                    Vector3 crown = tree.position + Vector3.up * (tree.stand.height * 0.75f);
                    Vector3 offset = crown - position;
                    float depth = Vector3.Dot(offset, forward), pad = Mathf.Max(tree.stand.radius, tree.stand.height * 0.5f);
                    // Charge only crowns intersecting the view, not all trees in a tall cell touching its frustum.
                    float extent = orthographic ? 1f : Mathf.Max(0f, depth);
                    if (depth < -pad || Mathf.Abs(Vector3.Dot(offset, right)) > viewport.x * extent + pad * (1f + viewport.x)
                        || Mathf.Abs(Vector3.Dot(offset, up)) > viewport.y * extent + pad * (1f + viewport.y)) continue;
                    float distance = CrownScore(crown, tree.stand.radius, position, forward, focal);
                    RecordsVisited++;
                    if (distance <= 333f + guard)
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
            // The feed and fast flight spend fewer HDRP triangles, with extra close detail below 80 m above ground.
            bool small = camera.pixelHeight <= 360;
            bool close = target.altitude < 80f;
            int triangles = small ? 180000 : target.fast ? (close ? 650000 : 450000) : TriangleLimit;
            Vector4 distances = ForestBudget.SelectProjected(bins, guard, triangles,
                small ? 900 : target.fast ? 1800 : CardLimit, small ? 16 : target.fast ? (close ? 64 : 32) : FullLimit,
                small ? 60 : target.fast ? (close ? 192 : 160) : ReducedLimit);
            yield return null;
            var instances = target.instances;
            foreach (var list in instances) list?.Clear();
            int fullCount = 0, reducedCount = 0, cardCount = 0, triangleCount = 0; float reach = 0f;
            foreach (var v in visible)
            {
                var tree = v.cell.trees[v.index]; int kind = tree.stand.species * 3 + tree.stand.shade;
                for (int tier = 0; tier < 3; tier++)
                {
                    int k = tier * 12 + kind; var model = models[k]; if (model == null) continue;
                    float upper = tier == 0 ? distances.x : tier == 1 ? distances.y : distances.z;
                    float lower = tier == 0 ? 0f : tier == 1 ? distances.x : distances.y;
                    int pad = model.fades ? guard : 0;
                    float overlap = model.fades && tier > 0 ? lower * 0.1f : 0f;
                    if (upper <= 0f || v.distance > upper + pad || tier > 0 && v.distance < lower - overlap - pad) continue;
                    if (instances[k] == null) instances[k] = new List<Matrix4x4>();
                    instances[k].Add(tier == 0 ? tree.mesh : tier == 1 ? tree.reduced : tree.card);
                    triangleCount += model.triangles; reach = Mathf.Max(reach, Vector3.Distance(position, tree.position));
                    if (tier == 0) fullCount++; else if (tier == 1) reducedCount++; else cardCount++;
                }
                if ((++visited & 31) == 0) yield return null;
            }
            var building = target.building;
            for (int k = 0; k < instances.Length; k++)
            {
                var list = instances[k]; if (list == null || list.Count == 0) continue;
                foreach (var part in models[k].parts)
                    for (int start = 0; start < list.Count; start += Batch)
                    {
                        int count = Mathf.Min(Batch, list.Count - start);
                        var draw = target.Take(); building.Add(draw);
                        var matrices = draw.matrices;
                        var bounds = new Bounds((list[start] * part.local).MultiplyPoint3x4(part.center), Vector3.zero);
                        for (int i = 0; i < count; i++)
                        {
                            matrices[i] = list[start + i] * part.local;
                            bounds.Encapsulate(matrices[i].MultiplyPoint3x4(part.center));
                            if ((i & 31) == 31) yield return null;
                        }
                        bounds.Expand(60f);
                        draw.part = part; draw.bounds = bounds; draw.tier = k / 12; draw.count = count;
                    }
            }
            // Selection can finish while the camera moves; publish only while its conservative guard remains valid.
            target.Retire(); target.draws.AddRange(building); building.Clear(); target.position = position;
            target.viewport = viewport;
            target.forward = forward; target.focal = focal; target.ready = ready; target.reach = reach;
            target.distances = distances; target.version = version; target.refreshed = Time.unscaledTime;
            target.full = fullCount; target.reduced = reducedCount; target.cards = cardCount; target.triangles = triangleCount;
            SelectionCompletes++;
        }

        // Render cached arrays directly; native submission is the only per-instance upload left on ordinary frames.
        void DrawTrees(Camera camera, View view)
        {
            SetForestView(properties, camera);
            foreach (var draw in view.draws)
            {
                if (!GeometryUtility.TestPlanesAABB(planes, draw.bounds)) continue;
                for (int sub = 0; sub < draw.part.mesh.subMeshCount; sub++)
                {
                    var material = draw.part.materials[Mathf.Min(sub, draw.part.materials.Length - 1)];
                    if (material == null) continue;
                    var rp = new RenderParams(material) { camera = camera, worldBounds = draw.bounds, matProps = properties,
                        shadowCastingMode = draw.tier == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                    Graphics.RenderMeshInstanced(rp, draw.part.mesh, sub, draw.matrices, draw.count);
                    InstanceDrawCalls++;
                }
            }
        }

        // Only completed cells may open the canopy; individual crowns outside this radius can still be drawn.
        float ReadyReach(Vector2 world)
        {
            int ci = Mathf.FloorToInt(world.x / CellSize), cj = Mathf.FloorToInt(world.y / CellSize);
            float reach = TreeReach + MovementGuard;
            int ring = Mathf.CeilToInt(TreeReach / CellSize);
            for (int j = cj - ring; j <= cj + ring; j++)
                for (int i = ci - ring; i <= ci + ring; i++)
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
            var matrix = Matrix4x4.TRS(new Vector3(p.x, GroundAt(p) - 0.2f, p.y), Quaternion.Euler(0f, house.yaw, 0f), Vector3.one * house.Scale);
            model.instances.Add(matrix);
            if (model.tones != null) model.tones[house.roof & 3].Add(matrix);
            HousesDrawn++;
        }

        // Pack across cells: one draw per populated model/submesh/tint batch, not one per tiny cell.
        void Draw(Camera cam)
        {
            var bounds = new Bounds(cam.transform.position, Vector3.one * 3600f);
            ForestMaterials.SetCamera(properties, cam.transform.position, TierDistances);
            Prime(cam);
            foreach (var model in models)
            {
                if (model == null || model.instances.Count == 0) continue;
                foreach (var part in model.parts)
                {
                    for (int start = 0; start < model.instances.Count; start += Batch)
                    {
                        int count = Mathf.Min(Batch, model.instances.Count - start);
                        for (int i = 0; i < count; i++) batch[i] = model.instances[start + i] * part.local;
                        for (int sub = 0; sub < part.mesh.subMeshCount; sub++)
                        {
                            var material = part.materials[Mathf.Min(sub, part.materials.Length - 1)];
                            if (material == null || model.tones != null && part.toned != null && sub < part.toned.Length && part.toned[sub] != null) continue;
                            var rp = new RenderParams(material) { camera = cam, worldBounds = bounds,
                                matProps = properties, shadowCastingMode = model.tier < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                            Graphics.RenderMeshInstanced(rp, part.mesh, sub, batch, count);
                            InstanceDrawCalls++;
                        }
                    }
                    if (model.tones != null && part.toned != null) DrawRoofs(cam, bounds, model, part);
                }
            }
        }

        // A building's roof submesh, one instanced draw per roof its houses wear: at most three more draws a kind.
        void DrawRoofs(Camera cam, Bounds bounds, Model model, Part part)
        {
            for (int sub = 0; sub < part.toned.Length; sub++)
            {
                if (part.toned[sub] == null) continue;
                for (int tone = 0; tone < model.tones.Length; tone++)
                {
                    var list = model.tones[tone];
                    var material = part.toned[sub][tone];
                    if (material == null) continue;
                    for (int start = 0; start < list.Count; start += Batch)
                    {
                        int count = Mathf.Min(Batch, list.Count - start);
                        for (int i = 0; i < count; i++) batch[i] = list[start + i] * part.local;
                        var rp = new RenderParams(material) { camera = cam, worldBounds = bounds,
                            matProps = properties, shadowCastingMode = model.tier < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                        Graphics.RenderMeshInstanced(rp, part.mesh, sub, batch, count);
                        InstanceDrawCalls++;
                    }
                }
            }
        }

        readonly Dictionary<Camera, bool[]> primedByCamera = new Dictionary<Camera, bool[]>();

        // A model's first draw builds its shaders and DX12 pipeline states (hd169's 56-172 ms frames nearing a town): any
        // model not yet drawn is drawn once, two a frame, a fraction of a millimetre across just in front of the camera.
        // Per camera, since the drone feed's camera renders with other frame settings and so other variants (hd174).
        void Prime(Camera cam)
        {
            if (!primedByCamera.TryGetValue(cam, out var primed) || primed.Length != models.Length) primedByCamera[cam] = primed = new bool[models.Length];
            var eye = cam.transform;
            Vector3 front = eye.position + eye.forward * (cam.nearClipPlane + 0.5f);
            var tiny = Matrix4x4.TRS(front, Quaternion.identity, Vector3.one * 0.0001f);
            var near = new Bounds(front, Vector3.one);
            int drawn = 0;
            for (int m = 0; m < models.Length && drawn < 2; m++)
            {
                if (primed[m] || models[m] == null) continue;
                primed[m] = true;
                if (models[m].instances.Count > 0) continue; // drawn for real this frame
                drawn++;
                foreach (var part in models[m].parts)
                    for (int sub = 0; sub < part.mesh.subMeshCount; sub++)
                    {
                        var material = part.materials[Mathf.Min(sub, part.materials.Length - 1)];
                        if (material == null) continue;
                        batch[0] = tiny * part.local;
                        var rp = new RenderParams(material) { camera = cam, worldBounds = near, matProps = properties,
                            shadowCastingMode = models[m].tier < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                        Graphics.RenderMeshInstanced(rp, part.mesh, sub, batch, 1);
                    }
            }
        }

        // A unique signed pair avoids collisions on either side of the world's origin.
        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

        // The globally aligned candidate cells fit exactly in a 120 m cell, preventing duplicate boundary trees.
        IEnumerator Build(int ci, int cj)
        {
            var trees = cellTrees; trees.Clear(); // only the current cell builder runs; an abandoned one never resumes
            var center = new Vector2((ci + 0.5f) * CellSize, (cj + 0.5f) * CellSize) - FarTerrain.Origin;
            var bounds = new Bounds(new Vector3(center.x, GroundAt(center), center.y), new Vector3(CellSize + 40f, 240f, CellSize + 40f));
            int count = Mathf.RoundToInt(CellSize / ForestPattern.TreeStep);
            if (SectorForest.LocalCells.TryGetValue(Key(ci, cj), out var localStems))
            {
                foreach (var stem in localStems.stems)
                {
                    var tree = MakeTree(stem.i, stem.j, stem.position, stem.stand);
                    trees.Add(tree); bounds.Encapsulate(tree.position); bounds.Encapsulate(tree.position + Vector3.up * 26f);
                    yield return null;
                }
                if (Mathf.Abs(center.x) + CellSize * 0.5f <= Ground.Size * 0.5f
                    && Mathf.Abs(center.y) + CellSize * 0.5f <= Ground.Size * 0.5f)
                {
                    buildingCell = new Cell { trees = trees.ToArray(), bounds = bounds };
                    yield break;
                }
            }
            for (int j = cj * count; j < (cj + 1) * count; j++)
                for (int i = ci * count; i < (ci + 1) * count; i++)
                {
                    yield return null;
                    if (!SectorForest.Tree(i, j, out Vector2 world, out ForestPattern.Stand stand)) continue;
                    Vector2 p = world - FarTerrain.Origin;
                    if (localStems != null && InSector(p)) continue;
                    if (InSector(p) && !SectorForest.Ready) continue;
                    var at = new Vector3(p.x, GroundAt(p) - 0.15f, p.y);
                    trees.Add(MakeTree(i, j, at, stand));
                    bounds.Encapsulate(at); bounds.Encapsulate(at + Vector3.up * 26f);
                }
            buildingCell = new Cell { trees = trees.ToArray(), bounds = bounds };
        }

        // Logical recipe stems and streamed world stems use identical transforms, species and fade dimensions.
        Tree MakeTree(int i, int j, Vector3 at, ForestPattern.Stand stand)
        {
            var tree = new Tree { position = at, stand = stand };
            var rotation = ForestPattern.Rotation(i, j, stand);
            // Cache roots once: flight changes the tier, not thousands of TRS matrices each frame.
            for (int tier = 0; tier < 3; tier++)
            {
                var model = models[tier * 12 + stand.species * 3 + stand.shade];
                if (model == null) continue;
                float size = stand.height / model.height;
                var matrix = Matrix4x4.TRS(at, rotation, new Vector3(size * stand.width, size, size * stand.width));
                if (tier == 0) tree.mesh = matrix; else if (tier == 1) tree.reduced = matrix; else tree.card = matrix;
            }
            return tree;
        }

        // Half-open ownership joins logical sector stems to the same world candidates without an empty border.
        internal static bool InSector(Vector2 local) =>
            local.x >= -Ground.Size / 2f && local.x < Ground.Size / 2f && local.y >= -Ground.Size / 2f && local.y < Ground.Size / 2f;

        // Keep the photo material path used by Art, with three reusable tint variants for each tree tier.
        void LoadModels()
        {
            models = new Model[36 + Houses.Length]; // 36 tree tiers, then the house kinds SettlementPattern places
            for (int tier = 0; tier < 3; tier++)
                for (int species = 0; species < 4; species++)
                    for (int shade = 0; shade < 3; shade++)
                        models[tier * 12 + species * 3 + shade] = LoadModel(tier == 1 ? Trees[species].Replace("Scenery_", "Forest_") + "_Reduced" : Trees[species],
                            tier == 1 ? 0 : tier, ForestPattern.Tint(shade), tier, species);
            // Street trees use their crossed-card impostor (LOD2): a town holds a few hundred, seen from above.
            for (int i = 0; i < Houses.Length; i++) models[36 + i] = LoadModel(Houses[i], i == SettlementPattern.Tree ? 2 : 1, 1f, 3, -1);
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
                Material[][] toned = null;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    if (tier == 3 && materials[i].name == SettlementPattern.RoofMaterials[0])
                    {
                        // The other three roofs share the tile's photo texture, each with its own tint (World.Roofs).
                        if (toned == null) toned = new Material[materials.Length][];
                        toned[i] = new Material[SettlementPattern.RoofMaterials.Length];
                        for (int tone = 1; tone < toned[i].Length; tone++)
                        {
                            var roof = Resources.Load<Material>("Materials/PBR/" + SettlementPattern.RoofMaterials[tone]);
                            if (roof == null) continue;
                            toned[i][tone] = new Material(roof) { enableInstancing = true };
                            ownedMaterials.Add(toned[i][tone]);
                        }
                    }
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
                    if (toned != null && toned[i] != null) toned[i][0] = material;
                }
                var box = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = local.MultiplyPoint3x4(box.center + Vector3.Scale(box.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                    bottom = Mathf.Min(bottom, p.y); top = Mathf.Max(top, p.y);
                }
                parts.Add(new Part { mesh = filter.sharedMesh, materials = materials, toned = toned, local = local, center = box.center });
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) triangles += (int)filter.sharedMesh.GetIndexCount(sub) / 3;
            }
            if (species >= 0)
                foreach (var part in parts)
                    foreach (var material in part.materials)
                        ForestMaterials.SetShape(material, part.local.inverse.MultiplyPoint3x4(Vector3.up * ((top - bottom) * 0.75f)),
                            part.local.inverse.MultiplyVector(Vector3.right * ((top - bottom) * ForestPalette.RadiusPerHeight[species])));
            if (parts.Count == 0) return null;
            var model = new Model { parts = parts.ToArray(), height = Mathf.Max(1f, top - bottom), triangles = triangles, tier = tier, fades = fades };
            foreach (var part in parts)
                if (part.toned != null && model.tones == null)
                {
                    model.tones = new List<Matrix4x4>[SettlementPattern.RoofMaterials.Length];
                    for (int tone = 0; tone < model.tones.Length; tone++) model.tones[tone] = new List<Matrix4x4>();
                }
            return model;
        }
    }
}

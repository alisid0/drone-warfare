using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;
using Stage = DroneSandbox.FarDetailTiming.Stage;

namespace DroneSandbox
{
    // A bounded local terrain cache follows the camera and the followed drone's route. Two lanes build tiles ahead
    // on workers while the previous one commits; small main-thread stages upload into hidden pooled resources, then
    // publish the tile and chunk holes together.
    public sealed class FarDetail : MonoBehaviour
    {
        public const int MaxTiles = 25, PrepareLanes = 2;
        const float Reach = 1500f, KeepSeconds = 20f, ColliderReach = 500f;
        const double BudgetMs = 0.35;
        static FarDetail instance;
        public static int RegionalCommitted => instance?.regional?.Committed ?? 0;
        public static double RegionalWorkerMs => instance?.regional?.LastWorkerMs ?? 0;
        public static int Live => instance != null ? instance.live.Count : 0;
        public static int Committed { get; private set; }
        public static int PressureEvictions { get; private set; }
        public static double WorstCommitMs { get; private set; }
        public static double TotalCommitMs { get; private set; }
        public static int CommitFrames { get; private set; }
        public static int BudgetOverruns { get; private set; }
        public static double LastGenerationMs { get; private set; }
        public static double WorstBakeMs { get; private set; }

        // This frame's main-thread time, bytes handed to Mesh/Texture2D (copies plus Apply/UploadMeshData), and stages.
        public static double LastFrameMs { get; private set; }
        public static long UploadBytes { get; private set; }
        public static long WorstUploadBytes { get; private set; }
        public static int StagesThisFrame { get; private set; }
        // Real seconds before the followed eye's ready zone reaches a needed tile that isn't live (infinity when all are).
        public static float ReadyAhead { get; private set; } = float.PositiveInfinity;
        // Whether the tile under the followed eye is live this frame, and the camera zone's missing tiles this frame.
        public static bool FollowedReady { get; private set; } = true;
        public static int CameraMissing { get; private set; }
        // Frames since ResetMetrics with the followed eye over an eligible tile, and those with it missing; the same for
        // the camera's 600 m ready zone.
        public static int FollowedFrames { get; private set; }
        public static int FollowedMissingFrames { get; private set; }
        public static int CameraFrames { get; private set; }
        public static int CameraMissingFrames { get; private set; }
        // Latency from starting a tile to its finished data (p99 of the last 64), tiles in the lanes, metres prefetched.
        public static double GenerationP99Ms { get; private set; } = 400;
        public static int Preparing { get; private set; }
        public static float PrefetchMetres => instance?.route?.Lead ?? 0f;
        // The followed eye and its projected route, which ForestHlod prefetches along too.
        internal static FarDetailRoute Route => instance != null && instance.enabled && instance.plan != null ? instance.route : null;

        // One pooled resource set retains its mesh and CPU-readable texture for reuse without GPU allocation churn.
        sealed class Slot
        {
            public GameObject go;
            public Mesh mesh, collision;
            public MeshCollider collider;
            public Texture2D texture, control;
            public Material material;
            public FarDetailData.Tile data;
            public Task<double> bake;
            public int x, z;
            public float lastWanted;
        }

        // One tile generated ahead of its commit: the main thread samples roughness, then a worker builds the tile
        // into the hidden slot's own arrays.
        sealed class Lane
        {
            public Slot slot;
            public readonly float[] roughness = new float[FarDetailData.Samples * FarDetailData.Samples];
            public int x, z, row;
            public bool sampling;
            public Task<FarDetailData.Tile> build;
            public FarDetailData.Tile tile;
            public float started;
        }

        // A transaction owns snapshots only; a discarded task can finish after a sector rebuild safely.
        sealed class Result
        {
            public FarDetailData.Tile tile;
            public FarDetailChunks.Geometry[] chunks;
        }

        readonly Dictionary<long, Slot> live = new Dictionary<long, Slot>(MaxTiles);
        readonly Stack<Slot> pool = new Stack<Slot>(MaxTiles + 1 + PrepareLanes);
        readonly HashSet<long> wanted = new HashSet<long>();
        readonly List<FarDetailChunks.Chunk> changed = new List<FarDetailChunks.Chunk>();
        readonly Stopwatch frameTimer = new Stopwatch();
        readonly Lane[] lanes = new Lane[PrepareLanes];
        readonly double[] estimate = new double[(int)Stage.Count];
        readonly double[] latencies = new double[64], sorted = new double[64];
        FarDetailData plan;
        FarDetailRegional regional;
        FarDetailRoute route;
        Func<int, int, bool> isLive;
        Task<Result> worker;
        Result ready;
        Slot incoming, outgoing;
        FarDetailData.Tile incomingTile;
        int stage, chunkStage, chunkIndex, mipStage, regionalWait, latencyCount, stages;
        long bytes;
        Vector2 origin, eye, focus, feed, velocity, feedVelocity, previousEye, previousFeed, planEye;
        bool hasPrevious, hasFeed, feedVisible;
        Drone previousDrone, followed;
        float nextPlan;

        // FarTerrain calls this before destroying a sector's children or changing any world inputs.
        public static void Clear()
        {
            ReadyAhead = float.PositiveInfinity; FollowedReady = true; CameraMissing = 0; Preparing = 0;
            if (instance == null) { FarDetailChunks.Clear(); return; }
            instance.enabled = false;
            instance.regional?.Clear(); instance.regional = null;
            instance.worker = null; instance.ready = null; instance.incomingTile = null;
            foreach (var s in instance.live.Values) Release(s);
            foreach (var s in instance.pool) Release(s);
            if (instance.incoming != null) Release(instance.incoming);
            // A lane's worker writes only managed arrays, so its slot is released without waiting for it.
            foreach (var lane in instance.lanes) if (lane != null && lane.slot != null) Release(lane.slot);
            Array.Clear(instance.lanes, 0, PrepareLanes);
            instance.live.Clear(); instance.pool.Clear(); instance.changed.Clear(); instance.wanted.Clear();
            instance.incoming = instance.outgoing = null; instance.plan = null;
            FarDetailChunks.Clear();
        }

        // Build-time allocation is outside flight: 25 resident slots, one hidden replacement and one per lane.
        public static void Initialise(Transform parent)
        {
            instance = parent.GetComponent<FarDetail>() ?? parent.gameObject.AddComponent<FarDetail>();
            instance.origin = FarTerrain.Origin;
            instance.plan = WorldPlan.CaptureDetail(FarDetailChunks.Heights());
            instance.regional = new FarDetailRegional(instance.plan);
            instance.route = new FarDetailRoute(Eligible);
            instance.isLive = instance.IsLive;
            for (int i = 0; i < PrepareLanes; i++) instance.lanes[i] = new Lane();
            // Until a stage has been measured it runs in a frame of its own.
            for (int i = 0; i < instance.estimate.Length; i++) instance.estimate[i] = BudgetMs;
            instance.latencyCount = 0; GenerationP99Ms = 400;
            instance.hasPrevious = false; instance.previousDrone = null; instance.nextPlan = 0f;
            for (int i = 0; i < MaxTiles + 1 + PrepareLanes; i++) instance.pool.Push(instance.MakeSlot(parent, i));
            ResetMetrics();
            instance.enabled = true;
        }

        // WorldCheck resets timing without disturbing the already prefetched ring.
        public static void ResetMetrics()
        {
            Committed = PressureEvictions = CommitFrames = BudgetOverruns = 0;
            WorstCommitMs = TotalCommitMs = LastGenerationMs = WorstBakeMs = 0;
            FollowedFrames = FollowedMissingFrames = CameraFrames = CameraMissingFrames = 0;
            WorstUploadBytes = 0;
            FarDetailTiming.Reset();
        }

        // Precreate reusable engine resources; the extra slots stay hidden until a tile is retired atomically.
        Slot MakeSlot(Transform parent, int number)
        {
            var s = new Slot
            {
                go = new GameObject("Detail tile " + number),
                mesh = new Mesh(),
                collision = new Mesh(),
                texture = new Texture2D(FarDetailData.TextureSize, FarDetailData.TextureSize, TextureFormat.RGBA32, true),
                control = FarDetailMaterial.ControlMap(),
                material = FarDetailMaterial.Create()
            };
            s.go.transform.SetParent(parent, false);
            s.go.AddComponent<MeshFilter>().sharedMesh = s.mesh;
            var renderer = s.go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = s.material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            s.material.mainTexture = s.texture; s.material.SetFloat("_Smoothness", 0f);
            s.texture.wrapMode = TextureWrapMode.Clamp; s.texture.anisoLevel = 8;
            s.texture.filterMode = FilterMode.Trilinear;
            // Force the first native allocation during setup rather than the first measured flight commit.
            s.texture.Apply(false, false); s.control.Apply(false, false); s.mesh.MarkDynamic();
            s.collider = s.go.AddComponent<MeshCollider>(); s.collider.enabled = false;
            s.go.SetActive(false);
            return s;
        }

        // Sector transitions release resources explicitly, including pooled objects that were never made live.
        static void Release(Slot s)
        {
            // BakeMesh owns the native mesh until completion; only teardown may wait, never a streaming frame.
            if (s.bake != null) { try { s.bake.GetAwaiter().GetResult(); } catch (Exception e) { UnityEngine.Debug.LogException(e); } }
            Art.Discard(s.mesh); Art.Discard(s.collision); Art.Discard(s.texture); Art.Discard(s.control); Art.Discard(s.material); Art.Discard(s.go);
        }

        // Streaming continues while paused, using real camera velocity and real seconds for cache retention.
        void LateUpdate()
        {
            var camera = Camera.main;
            LastFrameMs = 0; UploadBytes = 0; StagesThisFrame = 0;
            if (plan == null || camera == null || !FarTerrain.Active) { ReadyAhead = float.PositiveInfinity; return; }
            var timer = frameTimer; timer.Restart();
            bytes = 0; stages = 0;
            FarDetailTiming.BeginFrame(); long stamp = FarDetailTiming.Begin();
            Track(camera);
            if (Time.unscaledTime >= nextPlan) { Plan(); nextPlan = Time.unscaledTime + 0.2f; }
            FarDetailTiming.End(Stage.Plan, stamp);
            Collect();
            // Collision gets a separate stage even when fast workers finish before the next frame; otherwise
            // a continuously busy upload queue could starve collider activation throughout a long flight.
            stamp = FarDetailTiming.Begin();
            bool collisionChanged = timer.Elapsed.TotalMilliseconds < 0.25 && UpdateCollider();
            FarDetailTiming.End(Stage.ColliderBind, stamp);
            // A waiting regional texture stage takes every third frame, since tile work now rarely leaves one idle.
            bool regionalWaiting = regional != null && regional.Pending, regionalTurn = regionalWaiting && regionalWait >= 2;
            bool tileWork = false;
            if (!collisionChanged && !regionalTurn && timer.Elapsed.TotalMilliseconds < BudgetMs)
            {
                tileWork = Commit(timer);
                if (timer.Elapsed.TotalMilliseconds < BudgetMs) tileWork |= Prepare(timer);
            }
            bool regionalCommit = !tileWork && !collisionChanged && timer.Elapsed.TotalMilliseconds < .20;
            regional?.Tick(eye, regionalCommit);
            regionalWait = regionalWaiting && !regionalCommit ? regionalWait + 1 : 0;
            Measure();
            double elapsed = timer.Elapsed.TotalMilliseconds;
            FarDetailTiming.EndFrame(elapsed, elapsed > WorstCommitMs);
            WorstCommitMs = Math.Max(WorstCommitMs, elapsed); TotalCommitMs += elapsed; CommitFrames++;
            if (elapsed > 2.0) BudgetOverruns++;
            LastFrameMs = elapsed; UploadBytes = bytes; StagesThisFrame = stages;
            WorstUploadBytes = Math.Max(WorstUploadBytes, bytes);
        }

        // Camera teleportation resets prediction; the followed drone supplies its own position, velocity and route.
        void Track(Camera camera)
        {
            var view = camera.transform;
            eye = new Vector2(view.position.x, view.position.z) + origin;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            if (hasPrevious && Vector2.Distance(eye, previousEye) < 200f)
                velocity = Vector2.Lerp(velocity, (eye - previousEye) / dt, 0.25f);
            else { velocity = Vector2.zero; nextPlan = 0f; }
            previousEye = eye; hasPrevious = true;
            focus = Focus(view);
            var droneFeed = DroneFeed.Instance;
            var drone = droneFeed != null ? droneFeed.Source : null;
            hasFeed = drone != null && drone.IsAlive;
            feedVisible = droneFeed != null && droneFeed.Visible;
            if (hasFeed)
            {
                feed = new Vector2(drone.transform.position.x, drone.transform.position.z) + origin;
                bool same = drone == previousDrone && Vector2.Distance(feed, previousFeed) < 200f;
                feedVelocity = same ? Vector2.Lerp(feedVelocity, (feed - previousFeed) / dt, 0.25f) : Vector2.zero;
                previousFeed = feed;
            }
            // A newly followed drone replans at once, so ReadyAhead never measures from the previous one.
            if (drone != previousDrone) nextPlan = 0f;
            previousDrone = drone; followed = hasFeed ? drone : null;
        }

        // The ground point the camera looks at, within 4 km: the centre of the 600 m ready zone.
        Vector2 Focus(Transform view)
        {
            Vector3 p = view.position, forward = view.forward;
            Vector2 at = new Vector2(p.x, p.z);
            if (forward.y > -0.05f) return at + origin;
            float metres = Mathf.Min(Mathf.Max(0f, p.y - HeightAt(at)) / -forward.y, 4000f);
            return at + new Vector2(forward.x, forward.z) * metres + origin;
        }

        // Up to 25 priorities from the camera's ready zone, the followed drone's neighbourhood and its queued route.
        void Plan()
        {
            route.cameraEye = eye; route.cameraFocus = focus; route.cameraVelocity = velocity;
            route.hasDrone = hasFeed; route.drone = feed; route.droneVelocity = feedVelocity;
            route.observed = hasFeed && (feedVisible || Vector2.Distance(feed, focus) < FarDetailRoute.Zone + ColliderReach);
            route.droneSpeed = 0f; route.ClearRoute();
            if (followed != null)
            {
                route.droneSpeed = followed.speed * Time.timeScale;
                var target = followed.MoveTarget;
                if (target.HasValue)
                {
                    route.AddRoutePoint(new Vector2(target.Value.x, target.Value.z) + origin);
                    var queued = followed.Waypoints;
                    for (int i = 0; i < queued.Count; i++) route.AddRoutePoint(new Vector2(queued[i].x, queued[i].z) + origin);
                }
            }
            route.generationSeconds = (float)(GenerationP99Ms / 1000.0);
            route.Plan(MaxTiles);
            planEye = route.Eye;
            wanted.Clear();
            foreach (var r in route.requests) wanted.Add(Key(r.x, r.z));
            float now = Time.unscaledTime;
            foreach (var s in live.Values)
                if (wanted.Contains(Key(s.x, s.z)) || DistanceTo(s.x, s.z, eye) < Reach || DistanceTo(s.x, s.z, focus) < FarDetailRoute.Zone
                    || hasFeed && DistanceTo(s.x, s.z, feed) < ColliderReach) s.lastWanted = now;
        }

        // Slab intersection returns time until a moving eye enters the tile's protected 500 m neighbourhood.
        public static float Arrival(int x, int z, Vector2 at, Vector2 speed) => FarDetailRoute.Arrival(x, z, at, speed);

        // Any overlap with the detailed sector or Backdrop excludes the whole tile, leaving their ownership intact.
        public static bool Eligible(int x, int z)
        {
            if ((x + 1) * 512f <= -WorldPlan.Half || (z + 1) * 512f <= -WorldPlan.Half || x * 512f >= WorldPlan.Half || z * 512f >= WorldPlan.Half) return false;
            float edge = Ground.Size / 2f - 0.5f;
            float lx = x * 512f - FarTerrain.Origin.x, lz = z * 512f - FarTerrain.Origin.y;
            // Beyond-edge distance grows in both absolute axes, so the closest point of this rectangle gives
            // a conservative overlap test for Backdrop's actual radial outline, including its rounded corners.
            float nx = lx > 0f ? lx : lx + 512f < 0f ? -(lx + 512f) : 0f;
            float nz = lz > 0f ? lz : lz + 512f < 0f ? -(lz + 512f) : 0f;
            float axis = Mathf.Max(nx, nz);
            return axis > edge && Mathf.Sqrt(nx * nx + nz * nz) * (1f - edge / axis) >= FarTerrain.NearReach;
        }

        // Minimum horizontal distance to the tile, so its whole near edge receives collision and priority.
        public static float DistanceTo(int x, int z, Vector2 at) => FarDetailRoute.DistanceTo(x, z, at);

        // Finished workers: the commit's chunk cuts, and each lane's completed tile with its latency.
        void Collect()
        {
            if (worker != null && worker.IsCompleted)
            {
                if (worker.IsFaulted)
                {
                    UnityEngine.Debug.LogException(worker.Exception);
                    worker = null; Abort();
                }
                else { ready = worker.Result; worker = null; stage = chunkStage = chunkIndex = mipStage = 0; }
            }
            Preparing = 0;
            foreach (var lane in lanes)
            {
                if (lane.slot != null) Preparing++;
                if (lane.build == null || !lane.build.IsCompleted) continue;
                if (lane.build.IsFaulted) { UnityEngine.Debug.LogException(lane.build.Exception); Recycle(lane); continue; }
                lane.tile = lane.build.Result; lane.build = null;
                Latency((Time.unscaledTime - lane.started) * 1000.0);
            }
        }

        // Keep a 64-tile window; with that few samples its p99 is its slowest, a deliberately cautious lead.
        void Latency(double ms)
        {
            latencies[latencyCount++ % latencies.Length] = ms;
            int n = Math.Min(latencyCount, latencies.Length);
            Array.Copy(latencies, sorted, n); Array.Sort(sorted, 0, n);
            GenerationP99Ms = sorted[Math.Min(n - 1, (int)Math.Ceiling(n * 0.99) - 1)];
        }

        // Upload the ready transaction, or begin the next one; only upload stages count as this frame's tile work.
        bool Commit(Stopwatch timer)
        {
            if (ready != null) { UploadSlice(timer); return true; }
            if (worker != null) return false;
            long stamp = FarDetailTiming.Begin();
            Schedule();
            FarDetailTiming.End(Stage.Schedule, stamp);
            return false;
        }

        // Commit the most urgent prepared tile; retention yields under hard-cap pressure, never the immediate bubble.
        void Schedule()
        {
            outgoing = null;
            foreach (var lane in lanes)
                if (lane.tile != null && !wanted.Contains(Key(lane.x, lane.z))) Recycle(lane);
            float now = Time.unscaledTime;
            foreach (var s in live.Values)
                if (!wanted.Contains(Key(s.x, s.z)) && now - s.lastWanted > KeepSeconds) { outgoing = s; break; }
            Lane next = null;
            bool missing = false;
            foreach (var r in route.requests)
            {
                if (live.ContainsKey(Key(r.x, r.z))) continue;
                missing = true;
                next = Prepared(r.x, r.z);
                if (next != null) break;
            }
            // An expired tile leaves with the next arrival while one is still being prepared, saving a chunk recut.
            if (next == null && (outgoing == null || missing)) return;
            if (next != null && live.Count >= MaxTiles && outgoing == null)
            {
                // The least recently wanted tile goes first, so passed tiles stay their 20 s unless the cap needs them.
                float oldest = float.MaxValue, farthest = -1f;
                foreach (var s in live.Values)
                {
                    if (wanted.Contains(Key(s.x, s.z))) continue;
                    float d = DistanceTo(s.x, s.z, eye); if (hasFeed) d = Mathf.Min(d, DistanceTo(s.x, s.z, feed));
                    if (d <= ColliderReach) continue;
                    if (s.lastWanted < oldest || s.lastWanted == oldest && d > farthest) { oldest = s.lastWanted; farthest = d; outgoing = s; }
                }
                if (outgoing == null) return;
                PressureEvictions++;
            }
            if (next != null)
            {
                incoming = next.slot; incomingTile = next.tile; incoming.x = next.x; incoming.z = next.z;
                next.slot = null; next.tile = null;
            }
            Launch();
        }

        // The lane holding a finished tile for this position, if any.
        Lane Prepared(int x, int z)
        {
            foreach (var lane in lanes) if (lane.tile != null && lane.x == x && lane.z == z) return lane;
            return null;
        }

        // An abandoned or faulted lane returns its hidden slot; the arrays stay with the slot for the next tile.
        void Recycle(Lane lane)
        {
            if (lane.slot != null) pool.Push(lane.slot);
            lane.slot = null; lane.tile = null; lane.build = null; lane.sampling = false;
        }

        // Keep both lanes busy: sample the most urgent tile no lane holds within the frame budget, then hand it to a worker.
        bool Prepare(Stopwatch timer)
        {
            Lane active = null;
            foreach (var lane in lanes) if (lane.sampling) { active = lane; break; }
            if (active != null && !wanted.Contains(Key(active.x, active.z))) { Recycle(active); active = null; }
            if (active == null)
            {
                foreach (var lane in lanes) if (lane.slot == null) { active = lane; break; }
                if (active == null || pool.Count == 0 || !Target(out int x, out int z)) return false;
                active.slot = pool.Pop(); active.x = x; active.z = z; active.row = 0; active.sampling = true;
                active.started = Time.unscaledTime;
            }
            long stamp = FarDetailTiming.Begin();
            Sample(active, timer);
            FarDetailTiming.End(Stage.Sample, stamp);
            return true;
        }

        // The first request in deadline order that is neither live, committing nor already in a lane.
        bool Target(out int x, out int z)
        {
            foreach (var r in route.requests)
            {
                if (live.ContainsKey(Key(r.x, r.z)) || incoming != null && incoming.x == r.x && incoming.z == r.z) continue;
                bool held = false;
                foreach (var lane in lanes) if (lane.slot != null && lane.x == r.x && lane.z == r.z) { held = true; break; }
                if (held) continue;
                x = r.x; z = r.z;
                return true;
            }
            x = z = 0;
            return false;
        }

        // Capture the native height-noise inputs in small rows; heavy mesh and colour generation stays on the worker.
        void Sample(Lane lane, Stopwatch timer)
        {
            int n = FarDetailData.Samples;
            do
            {
                if (lane.row < n)
                    for (int i = 0; i < n; i++) lane.roughness[lane.row * n + i] = FarDetailData.Roughness(lane.x * 512f + i * 4f, lane.z * 512f + lane.row * 4f);
                lane.row++;
            } while (lane.row < n && timer.Elapsed.TotalMilliseconds < BudgetMs);
            if (lane.row == n) { lane.sampling = false; Build(lane); }
        }

        // Only this dispatch allocates a closure; the worker reuses the hidden slot's arrays and captures no engine objects.
        void Build(Lane lane)
        {
            var capturedPlan = plan; var rough = lane.roughness; var reuse = lane.slot.data; int x = lane.x, z = lane.z;
            lane.build = Task.Run(() => capturedPlan.Build(x, z, rough, null, reuse));
        }

        // Freeze all transaction inputs before dispatch; the worker only cuts the far chunks round the prepared tile.
        void Launch()
        {
            var areas = new List<FarDetailChunks.Area>();
            foreach (var s in live.Values) if (s != outgoing) areas.Add(new FarDetailChunks.Area { x = s.x, z = s.z });
            if (incoming != null) areas.Add(new FarDetailChunks.Area { x = incoming.x, z = incoming.z });
            changed.Clear();
            foreach (var c in FarDetailChunks.All)
                if (incoming != null && FarDetailChunks.Overlaps(c.corner, new FarDetailChunks.Area { x = incoming.x, z = incoming.z }) ||
                   outgoing != null && FarDetailChunks.Overlaps(c.corner, new FarDetailChunks.Area { x = outgoing.x, z = outgoing.z })) changed.Add(c);
            var sources = new FarDetailChunks.Geometry[changed.Count]; var corners = new Vector2[changed.Count];
            for (int i = 0; i < changed.Count; i++) { sources[i] = changed[i].source; corners[i] = changed[i].corner; }
            var capturedPlan = plan; var areaArray = areas.ToArray(); var tile = incomingTile;
            worker = Task.Run(() =>
            {
                var result = new Result { tile = tile, chunks = new FarDetailChunks.Geometry[sources.Length] };
                for (int i = 0; i < sources.Length; i++) result.chunks[i] = FarDetailChunks.Cut(sources[i], corners[i], areaArray, capturedPlan);
                return result;
            });
        }

        // Upload stages back to back while the next one's learned cost still fits the frame budget. Allocations, Apply,
        // mesh uploads, the bake dispatch and publication always take a frame of their own, as before.
        void UploadSlice(Stopwatch timer)
        {
            while (ready != null)
            {
                var part = UploadStage();
                bool alone = Alone(part);
                if (stages > 0 && (alone || timer.Elapsed.TotalMilliseconds + estimate[(int)part] > BudgetMs)) return;
                long stamp = FarDetailTiming.Begin();
                Upload();
                FarDetailTiming.End(part, stamp);
                double ms = (Stopwatch.GetTimestamp() - stamp) * 1000.0 / Stopwatch.Frequency;
                estimate[(int)part] = Math.Max(ms, estimate[(int)part] * 0.95);
                stages++;
                if (alone) return;
            }
        }

        // Stages whose native cost can stall unpredictably never share a frame with another stage.
        bool Alone(Stage part)
        {
            switch (part)
            {
                case Stage.TileApply: case Stage.ControlApply: case Stage.MeshUpload: case Stage.CollisionBakeDispatch: case Stage.Publish: return true;
                case Stage.ChunkMesh: return FarChunkUpload.IsAllocation(changed[chunkIndex], chunkStage);
                default: return false;
            }
        }

        // One bounded API stage per call: no recalculation, no full-resolution collider cooking, no upload burst.
        void Upload()
        {
            if (incoming != null && stage < 14)
            {
                var d = ready.tile; var s = incoming;
                switch (stage++)
                {
                    case 0: s.mesh.Clear(); s.mesh.SetVertices(d.vertices); bytes += d.vertices.Length * 12L; break;
                    case 1: s.mesh.SetNormals(d.normals); bytes += d.normals.Length * 12L; break;
                    case 2: s.mesh.SetUVs(0, d.uv); bytes += d.uv.Length * 8L; break;
                    case 3: s.mesh.SetIndices(d.triangles, MeshTopology.Triangles, 0, false); s.mesh.bounds = new Bounds(new Vector3(256f, 256f, 256f), new Vector3(512f, 4096f, 512f));
                        bytes += d.triangles.Length * 4L; break;
                    case 4:
                        s.texture.SetPixelData(d.mips[mipStage], mipStage); bytes += d.mips[mipStage].Length * 4L;
                        if (++mipStage < d.mips.Length) stage--; break;
                    case 5: s.texture.Apply(false, false); for (int i = 0; i < d.mips.Length; i++) bytes += d.mips[i].Length * 4L; break;
                    case 6: s.collider.sharedMesh = null; s.collision.Clear(); s.collision.SetVertices(d.collision); bytes += d.collision.Length * 12L; break;
                    case 7: s.collision.SetIndices(d.collisionTriangles, MeshTopology.Triangles, 0); bytes += d.collisionTriangles.Length * 4L; break;
                    case 8: s.mesh.SetTangents(d.tangents); bytes += d.tangents.Length * 16L; break;
                    case 9: s.control.SetPixelData(d.controls, 0); bytes += d.controls.Length * 4L; break;
                    case 10: s.control.Apply(false, false); bytes += d.controls.Length * 4L; break;
                    case 11:
                        s.go.transform.position = new Vector3(s.x * 512f - origin.x, 0f, s.z * 512f - origin.y);
                        FarDetailMaterial.Bind(s.material, s.texture, s.control, s.x, s.z); break;
                    case 12: StartBake(s); break;
                    case 13: s.mesh.UploadMeshData(false); bytes += d.vertices.Length * 48L + d.triangles.Length * 4L; break;
                }
                return;
            }
            if (chunkIndex < changed.Count)
            {
                var c = changed[chunkIndex]; var data = ready.chunks[chunkIndex];
                if (ReferenceEquals(data, c.source)) { chunkIndex++; return; }
                long started = Stopwatch.GetTimestamp();
                bool copying = FarChunkUpload.IsCopy(chunkStage, data);
                for (int slices = 0; slices < 6; slices++)
                {
                    bytes += FarChunkUpload.Bytes(chunkStage, data);
                    if (FarChunkUpload.Step(c, data, ref chunkStage)) { chunkIndex++; chunkStage = 0; break; }
                    // Avoid trading the old upload spike for dozens of extra streaming frames per tile.
                    if (!copying || !FarChunkUpload.IsCopy(chunkStage, data)
                        || (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency >= .20) break;
                }
                return;
            }
            if (incoming?.bake != null)
            {
                if (!incoming.bake.IsCompleted) return;
                if (incoming.bake.IsFaulted) { UnityEngine.Debug.LogException(incoming.bake.Exception); incoming.bake = null; Abort(); return; }
                WorstBakeMs = Math.Max(WorstBakeMs, incoming.bake.Result);
                incoming.bake = null;
            }
            Publish();
        }

        // Only this dispatch allocates a closure; ordinary upload stages stay allocation-free.
        static void StartBake(Slot s)
        {
            var id = s.collision.GetEntityId(); var options = s.collider.cookingOptions;
            s.bake = Task.Run(() =>
            {
                long start = Stopwatch.GetTimestamp();
                Physics.BakeMesh(id, false, options);
                return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            });
        }

        // Label the next bounded operation before entering it, including deferred mesh uploads and publication.
        Stage UploadStage()
        {
            if (incoming != null && stage < 14)
            {
                switch (stage)
                {
                    case 4: return Stage.TilePixels;
                    case 5: return Stage.TileApply;
                    case 6: case 7: return Stage.CollisionMesh;
                    case 9: return Stage.ControlPixels;
                    case 10: return Stage.ControlApply;
                    case 12: return Stage.CollisionBakeDispatch;
                    case 13: return Stage.MeshUpload;
                    default: return Stage.TileMesh;
                }
            }
            return chunkIndex < changed.Count ? (FarChunkUpload.IsUpload(chunkStage, ready.chunks[chunkIndex]) ? Stage.MeshUpload : Stage.ChunkMesh) : Stage.Publish;
        }

        // Swap every ready surface in the same LateUpdate; there is never a frame with a hole but no tile.
        void Publish()
        {
            if (incoming != null && !wanted.Contains(Key(incoming.x, incoming.z))) { Abort(); return; }
            for (int i = 0; i < changed.Count; i++)
            {
                var c = changed[i];
                if (ReferenceEquals(ready.chunks[i], c.source)) continue;
                var old = c.front; c.front = c.back; c.back = old;
                c.filter.sharedMesh = c.front; c.group.ForceLOD(0);
            }
            if (outgoing != null)
            {
                live.Remove(Key(outgoing.x, outgoing.z)); outgoing.go.SetActive(false); outgoing.collider.enabled = false;
                pool.Push(outgoing); // the hidden slot's next worker reuses its arrays
            }
            if (incoming != null)
            {
                incoming.data = ready.tile; incoming.lastWanted = Time.unscaledTime;
                live.Add(Key(incoming.x, incoming.z), incoming); incoming.go.SetActive(true);
                Committed++; LastGenerationMs = ready.tile.generationMs;
            }
            // Empty chunks recover ordinary LOD selection instead of accumulating forced high-detail chunks on long flights.
            foreach (var c in changed)
            {
                bool occupied = false;
                foreach (var s in live.Values) if (FarDetailChunks.Overlaps(c.corner, new FarDetailChunks.Area { x = s.x, z = s.z })) { occupied = true; break; }
                if (!occupied)
                {
                    c.filter.sharedMesh = c.original; c.group.ForceLOD(-1);
                    Art.Discard(c.front); Art.Discard(c.back); c.front = c.back = null;
                }
            }
            incoming = outgoing = null; ready = null; incomingTile = null; changed.Clear();
        }

        // Teleports and worker faults discard staging resources without changing any visible ownership.
        void Abort()
        {
            if (incoming != null) pool.Push(incoming);
            incoming = outgoing = null; ready = null; incomingTile = null; changed.Clear();
        }

        // Enable at most one coarser collider per frame and remove distant ones without recooking.
        bool UpdateCollider()
        {
            foreach (var s in live.Values)
            {
                bool near = DistanceTo(s.x, s.z, eye) <= ColliderReach || hasFeed && DistanceTo(s.x, s.z, feed) <= ColliderReach;
                if (s.collider.enabled == near) continue;
                if (near) s.collider.sharedMesh = s.collision;
                s.collider.enabled = near; return true;
            }
            return false;
        }

        // Readiness every frame: the throttle's ReadyAhead, the followed eye's own tile and the camera zone's tiles.
        void Measure()
        {
            Vector2 at = hasFeed ? feed : focus;
            float speed = (hasFeed ? feedVelocity : velocity).magnitude;
            ReadyAhead = route.ReadyAhead(isLive, Vector2.Distance(at, planEye), speed);
            int fx = Mathf.FloorToInt(at.x / 512f), fz = Mathf.FloorToInt(at.y / 512f);
            bool eligible = Eligible(fx, fz);
            FollowedReady = !eligible || live.ContainsKey(Key(fx, fz));
            if (eligible) { FollowedFrames++; if (!FollowedReady) FollowedMissingFrames++; }
            const float zone = FarDetailRoute.Zone;
            int missing = 0, counted = 0;
            int x0 = Mathf.FloorToInt((focus.x - zone) / 512f), x1 = Mathf.FloorToInt((focus.x + zone) / 512f);
            int z0 = Mathf.FloorToInt((focus.y - zone) / 512f), z1 = Mathf.FloorToInt((focus.y + zone) / 512f);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    if (DistanceTo(x, z, focus) > zone || !Eligible(x, z)) continue;
                    counted++;
                    if (!live.ContainsKey(Key(x, z))) missing++;
                }
            CameraMissing = missing;
            if (counted > 0) { CameraFrames++; if (missing > 0) CameraMissingFrames++; }
        }

        // The planner's view of residency.
        bool IsLive(int x, int z) => live.ContainsKey(Key(x, z));

        // A visible tile's triangle height, used by drone clearance, scatter and far-hit filtering.
        public static bool TryHeight(Vector2 local, out float height)
        {
            height = 0f;
            if (instance == null || instance.plan == null) return false;
            Vector2 world = local + instance.origin;
            int x = Mathf.FloorToInt(world.x / 512f), z = Mathf.FloorToInt(world.y / 512f);
            if (!instance.live.TryGetValue(Key(x, z), out var s)) return false;
            float u = (world.x - x * 512f) / 4f, v = (world.y - z * 512f) / 4f;
            int i = Mathf.Min((int)u, 127), j = Mathf.Min((int)v, 127), k = j * 129 + i;
            var p = s.data.vertices;
            height = FarDetailData.Triangle(p[k].y, p[k + 1].y, p[k + 129].y, p[k + 130].y, u - i, v - j);
            return true;
        }

        // Outside live tiles the established near ring and world surface retain ownership.
        public static float HeightAt(Vector2 local) => TryHeight(local, out float h) ? h : FarTerrain.SurfaceAt(local);

        // Stable pre-placement avoids rebuilding cached scatter matrices on every tile arrival or eviction.
        // Trees cancel the legacy canopy lift at the lookup; the other session can retain just this entry point.
        public static float ScatterHeight(Vector2 local, bool tree)
        {
            Vector2 w = local + FarTerrain.Origin;
            if (instance == null || instance.plan == null || !Eligible(Mathf.FloorToInt(w.x / 512f), Mathf.FloorToInt(w.y / 512f))) return FarTerrain.GroundAt(local);
            float lift = tree ? Mathf.Max(0f, FarTerrain.CanopyAt(w.x, w.y) - 9f) : 0f;
            if (TryHeight(local, out float liveHeight)) return liveHeight - lift;
            // Sample the eventual four triangle corners, exactly as the detail mesh will, even before it arrives.
            float x = Mathf.Floor(w.x / 4f) * 4f, z = Mathf.Floor(w.y / 4f) * 4f;
            float a = PredictedHeight(x, z), b = PredictedHeight(x + 4f, z), c = PredictedHeight(x, z + 4f), d = PredictedHeight(x + 4f, z + 4f);
            float h = FarDetailData.Triangle(a, b, c, d, (w.x - x) / 4f, (w.y - z) / 4f);
            return h - lift;
        }

        // Native noise is sampled here only for main-thread scatter placement, never by a worker.
        static float PredictedHeight(float x, float z) => instance.plan.Surface(x, z, instance.plan.Wet(x, z) ? 0f : FarDetailData.Roughness(x, z));

        // Choose the closest detail collider or far triangle; far hits under live tiles cannot steal an order.
        public static bool Raycast(Ray ray, out Vector3 point)
        {
            float best = float.MaxValue; point = Vector3.zero;
            if (instance != null) foreach (var s in instance.live.Values)
                {
                    if (FarDetailRay.Hit(s.data, instance.origin, ray, out float distance) && distance < best)
                    { best = distance; point = ray.GetPoint(distance); }
                }
            if (FarTerrain.Raycast(ray, out Vector3 far))
            {
                float distance = Vector3.Distance(ray.origin, far);
                if (distance < best) { point = far; best = distance; }
            }
            return best < float.MaxValue;
        }

        // Signed world tile coordinates remain collision-free across all four quadrants.
        static long Key(int x, int z) => FarDetailRoute.Key(x, z);
    }
}

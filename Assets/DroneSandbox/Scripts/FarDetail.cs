using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // A bounded local terrain cache follows the camera and drone feed. Workers build immutable arrays; small
    // main-thread stages upload into hidden pooled resources, then publish the tile and chunk holes together.
    public sealed class FarDetail : MonoBehaviour
    {
        public const int MaxTiles = 25;
        const float Reach = 1500f, KeepSeconds = 15f, ColliderReach = 500f;
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

        // One pooled resource set retains its mesh and CPU-readable texture for reuse without GPU allocation churn.
        sealed class Slot
        {
            public GameObject go;
            public Mesh mesh, collision;
            public MeshCollider collider;
            public Texture2D texture, control;
            public Material material;
            public FarDetailData.Tile data;
            public int x, z;
            public float lastWanted;
        }

        // Requests are always re-sorted by predicted arrival, including while an earlier worker is running.
        struct Request
        {
            public int x, z;
            public float arrival, distance;
        }

        // A transaction owns snapshots only; a discarded task can finish after a sector rebuild safely.
        sealed class Result
        {
            public FarDetailData.Tile tile;
            public FarDetailChunks.Geometry[] chunks;
        }

        readonly Dictionary<long, Slot> live = new Dictionary<long, Slot>();
        readonly Stack<Slot> pool = new Stack<Slot>();
        readonly List<Request> requests = new List<Request>();
        readonly HashSet<long> wanted = new HashSet<long>();
        readonly List<FarDetailChunks.Chunk> changed = new List<FarDetailChunks.Chunk>();
        readonly Stopwatch frameTimer = new Stopwatch();
        FarDetailData plan;
        FarDetailRegional regional;
        Task<Result> worker;
        Result ready;
        Slot incoming, outgoing;
        Request pending;
        float[] roughness;
        int sampleRow, stage, chunkStage, chunkIndex;
        bool sampling;
        Vector2 origin, eye, feed, velocity, feedVelocity, previousEye, previousFeed;
        bool hasPrevious, hasFeed;
        Drone previousDrone;
        float nextPlan;

        // FarTerrain calls this before destroying a sector's children or changing any world inputs.
        public static void Clear()
        {
            if (instance == null) { FarDetailChunks.Clear(); return; }
            instance.enabled = false;
            instance.regional?.Clear(); instance.regional = null;
            instance.worker = null; instance.ready = null; instance.sampling = false;
            instance.roughness = null;
            foreach (var s in instance.live.Values) Release(s);
            foreach (var s in instance.pool) Release(s);
            if (instance.incoming != null) Release(instance.incoming);
            instance.live.Clear(); instance.pool.Clear(); instance.changed.Clear(); instance.wanted.Clear();
            instance.incoming = instance.outgoing = null; instance.plan = null;
            FarDetailChunks.Clear();
        }

        // Build-time allocation is outside flight: 25 resident slots plus one hidden replacement, under the cap.
        public static void Initialise(Transform parent)
        {
            instance = parent.GetComponent<FarDetail>() ?? parent.gameObject.AddComponent<FarDetail>();
            instance.origin = FarTerrain.Origin;
            instance.plan = WorldPlan.CaptureDetail(FarDetailChunks.Heights());
            instance.regional = new FarDetailRegional(instance.plan);
            instance.hasPrevious = false; instance.previousDrone = null; instance.nextPlan = 0f;
            for (int i = 0; i <= MaxTiles; i++) instance.pool.Push(instance.MakeSlot(parent, i));
            ResetMetrics();
            instance.enabled = true;
        }

        // WorldCheck resets timing without disturbing the already prefetched ring.
        public static void ResetMetrics()
        {
            Committed = PressureEvictions = CommitFrames = BudgetOverruns = 0;
            WorstCommitMs = TotalCommitMs = LastGenerationMs = 0;
        }

        // Precreate reusable engine resources; the extra slot is hidden until an old tile is retired atomically.
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
            s.collider = s.go.AddComponent<MeshCollider>(); s.collider.enabled = false;
            s.go.SetActive(false);
            return s;
        }

        // Sector transitions release resources explicitly, including pooled objects that were never made live.
        static void Release(Slot s)
        { Art.Discard(s.mesh); Art.Discard(s.collision); Art.Discard(s.texture); Art.Discard(s.control); Art.Discard(s.material); Art.Discard(s.go); }

        // Streaming continues while paused, using real camera velocity and real seconds for cache retention.
        void LateUpdate()
        {
            var camera = Camera.main;
            if (plan == null || camera == null || !FarTerrain.Active) return;
            var timer = frameTimer; timer.Restart();
            Track(camera);
            if (Time.unscaledTime >= nextPlan) { Plan(); nextPlan = Time.unscaledTime + 0.2f; }
            if (worker != null && worker.IsCompleted)
            {
                if (worker.IsFaulted)
                {
                    UnityEngine.Debug.LogException(worker.Exception);
                    worker = null; Abort();
                }
                else { ready = worker.Result; worker = null; stage = chunkStage = chunkIndex = 0; }
            }
            // Collision gets a separate stage even when fast workers finish before the next frame; otherwise
            // a continuously busy upload queue could starve collider activation throughout a long flight.
            bool collisionChanged = timer.Elapsed.TotalMilliseconds < 0.25 && UpdateCollider();
            bool tileWork = false;
            if (!collisionChanged && timer.Elapsed.TotalMilliseconds < BudgetMs)
            {
                if (sampling) { tileWork = true; Sample(timer); }
                else if (ready != null) { tileWork = true; Upload(); }
                else if (worker == null) Schedule();
            }
            regional?.Tick(eye, !tileWork && !collisionChanged && timer.Elapsed.TotalMilliseconds < .20);
            double elapsed = timer.Elapsed.TotalMilliseconds;
            WorstCommitMs = Math.Max(WorstCommitMs, elapsed); TotalCommitMs += elapsed; CommitFrames++;
            if (elapsed > 2.0) BudgetOverruns++;
        }

        // Camera teleportation resets prediction; a live feed supplies an independent 500 m bubble and velocity.
        void Track(Camera camera)
        {
            eye = new Vector2(camera.transform.position.x, camera.transform.position.z) + origin;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            if (hasPrevious && Vector2.Distance(eye, previousEye) < 200f)
                velocity = Vector2.Lerp(velocity, (eye - previousEye) / dt, 0.25f);
            else velocity = Vector2.zero;
            previousEye = eye; hasPrevious = true;
            var droneFeed = DroneFeed.Instance;
            var drone = droneFeed != null ? droneFeed.Source : null;
            hasFeed = drone != null && drone.IsAlive;
            if (hasFeed)
            {
                feed = new Vector2(drone.transform.position.x, drone.transform.position.z) + origin;
                feedVelocity = drone == previousDrone && Vector2.Distance(feed, previousFeed) < 200f ? (feed - previousFeed) / dt : Vector2.zero;
                previousFeed = feed;
            }
            previousDrone = drone;
        }

        // Bound work to 25 priorities: immediate 500 m coverage first by arrival=0, then the predicted corridor.
        void Plan()
        {
            requests.Clear(); wanted.Clear();
            AddCandidates(eye, velocity);
            if (hasFeed) AddCandidates(feed, feedVelocity);
            requests.Sort(Compare);
            int keep = Math.Min(MaxTiles, requests.Count);
            if (requests.Count > keep) requests.RemoveRange(keep, requests.Count - keep);
            wanted.Clear();
            foreach (var r in requests) wanted.Add(Key(r.x, r.z));
            foreach (var s in live.Values)
                if (DistanceTo(s.x, s.z, eye) < Reach || hasFeed && DistanceTo(s.x, s.z, feed) < ColliderReach) s.lastWanted = Time.unscaledTime;
        }

        // At fast-forward there is no age or distance bonus ahead of ETA; distance only breaks equal arrivals.
        static int Compare(Request a, Request b)
        {
            int time = a.arrival.CompareTo(b.arrival);
            if (time != 0) return time;
            int distance = a.distance.CompareTo(b.distance);
            if (distance != 0) return distance;
            return Key(a.x, a.z).CompareTo(Key(b.x, b.z));
        }

        // The capsule reaches 1.5 km ahead, with the near camera/feed area protected even on sharp turns.
        void AddCandidates(Vector2 at, Vector2 speed)
        {
            int cx = Mathf.FloorToInt(at.x / 512f), cz = Mathf.FloorToInt(at.y / 512f);
            Vector2 heading = speed.normalized;
            for (int z = cz - 6; z <= cz + 6; z++) for (int x = cx - 6; x <= cx + 6; x++)
                {
                    if (!Eligible(x, z)) continue;
                    float distance = DistanceTo(x, z, at);
                    float arrival = Arrival(x, z, at, speed);
                    Vector2 delta = new Vector2((x + 0.5f) * 512f - at.x, (z + 0.5f) * 512f - at.y);
                    float along = Vector2.Dot(delta, heading), side = Mathf.Abs(delta.x * heading.y - delta.y * heading.x);
                    if (distance > Reach && !(speed.sqrMagnitude > 0.01f && along >= 0f && along <= Reach + 256f && side < 512f)) continue;
                    long key = Key(x, z);
                    if (!wanted.Add(key))
                    {
                        for (int i = 0; i < requests.Count; i++) if (requests[i].x == x && requests[i].z == z)
                            {
                                var old = requests[i]; old.arrival = Mathf.Min(old.arrival, arrival); old.distance = Mathf.Min(old.distance, distance); requests[i] = old; break;
                            }
                    }
                    else requests.Add(new Request { x = x, z = z, arrival = arrival, distance = distance });
                }
        }

        // Slab intersection returns time until a moving eye enters the tile's protected 500 m neighbourhood.
        public static float Arrival(int x, int z, Vector2 at, Vector2 speed)
        {
            if (DistanceTo(x, z, at) <= ColliderReach) return 0f;
            float enter = 0f, leave = float.MaxValue;
            if (!Slab(x * 512f - ColliderReach, (x + 1) * 512f + ColliderReach, at.x, speed.x, ref enter, ref leave) ||
                !Slab(z * 512f - ColliderReach, (z + 1) * 512f + ColliderReach, at.y, speed.y, ref enter, ref leave)) return float.MaxValue;
            return enter;
        }

        // Stationary axes must already lie within the prediction slab.
        static bool Slab(float lo, float hi, float at, float speed, ref float enter, ref float leave)
        {
            if (Mathf.Abs(speed) < 0.01f) return at >= lo && at <= hi;
            float a = (lo - at) / speed, b = (hi - at) / speed;
            enter = Mathf.Max(enter, Mathf.Min(a, b)); leave = Mathf.Min(leave, Mathf.Max(a, b));
            return leave >= enter;
        }

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
        public static float DistanceTo(int x, int z, Vector2 at)
        {
            float dx = Mathf.Max(x * 512f - at.x, Mathf.Max(0f, at.x - (x + 1) * 512f));
            float dz = Mathf.Max(z * 512f - at.y, Mathf.Max(0f, at.y - (z + 1) * 512f));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Begin only the best missing request; retention yields under hard-cap pressure, never the immediate bubble.
        void Schedule()
        {
            outgoing = null;
            foreach (var s in live.Values)
                if (!wanted.Contains(Key(s.x, s.z)) && Time.unscaledTime - s.lastWanted > KeepSeconds) { outgoing = s; break; }
            Request? next = null;
            foreach (var r in requests) if (!live.ContainsKey(Key(r.x, r.z))) { next = r; break; }
            if (!next.HasValue && outgoing == null) return;
            if (next.HasValue && live.Count >= MaxTiles && outgoing == null)
            {
                float farthest = -1;
                foreach (var s in live.Values)
                {
                    if (wanted.Contains(Key(s.x, s.z))) continue;
                    float d = DistanceTo(s.x, s.z, eye); if (hasFeed) d = Mathf.Min(d, DistanceTo(s.x, s.z, feed));
                    if (d > ColliderReach && d > farthest) { farthest = d; outgoing = s; }
                }
                if (outgoing == null) return;
                PressureEvictions++;
            }
            if (next.HasValue)
            {
                pending = next.Value; incoming = pool.Pop(); incoming.x = pending.x; incoming.z = pending.z;
                roughness = new float[FarDetailData.Samples * FarDetailData.Samples]; sampleRow = 0; sampling = true;
            }
            else Launch();
        }

        // Capture the native height-noise inputs in small rows; heavy mesh and colour generation stays on the worker.
        void Sample(Stopwatch timer)
        {
            do
            {
                int n = FarDetailData.Samples;
                if (sampleRow < n)
                    for (int i = 0; i < n; i++) roughness[sampleRow * n + i] = FarDetailData.Roughness(pending.x * 512f + i * 4f, pending.z * 512f + sampleRow * 4f);
                sampleRow++;
            } while (sampleRow < FarDetailData.Samples && timer.Elapsed.TotalMilliseconds < BudgetMs);
            if (sampleRow == FarDetailData.Samples) { sampling = false; Launch(); }
        }

        // Freeze all transaction inputs before dispatch; the worker captures no live collections or engine objects.
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
            var capturedPlan = plan; var capturedRough = roughness; var areaArray = areas.ToArray();
            bool add = incoming != null; int x = add ? incoming.x : 0, z = add ? incoming.z : 0;
            worker = Task.Run(() =>
            {
                var result = new Result { tile = add ? capturedPlan.Build(x, z, capturedRough, null) : null, chunks = new FarDetailChunks.Geometry[sources.Length] };
                for (int i = 0; i < sources.Length; i++) result.chunks[i] = FarDetailChunks.Cut(sources[i], corners[i], areaArray, capturedPlan);
                return result;
            });
            roughness = null;
        }

        // One bounded API stage per frame: no recalculation, no full-resolution collider cooking, no upload burst.
        void Upload()
        {
            if (incoming != null && stage < 12)
            {
                var d = ready.tile; var s = incoming;
                switch (stage++)
                {
                    case 0: s.mesh.Clear(); s.mesh.SetVertices(d.vertices); break;
                    case 1: s.mesh.SetNormals(d.normals); break;
                    case 2: s.mesh.SetUVs(0, d.uv); break;
                    case 3: s.mesh.SetIndices(d.triangles, MeshTopology.Triangles, 0, false); s.mesh.bounds = new Bounds(new Vector3(256f, 256f, 256f), new Vector3(512f, 4096f, 512f)); break;
                    case 4: s.texture.SetPixelData(d.pixels, 0); break;
                    case 5: s.texture.Apply(true, false); break;
                    case 6: s.collider.sharedMesh = null; s.collision.Clear(); s.collision.SetVertices(d.collision); break;
                    case 7: s.collision.SetIndices(d.collisionTriangles, MeshTopology.Triangles, 0); break;
                    case 8: s.mesh.SetTangents(d.tangents); break;
                    case 9: s.control.SetPixelData(d.controls, 0); break;
                    case 10: s.control.Apply(false, false); break;
                    case 11:
                        s.go.transform.position = new Vector3(s.x * 512f - origin.x, 0f, s.z * 512f - origin.y);
                        FarDetailMaterial.Bind(s.material, s.texture, s.control, s.x, s.z); break;
                }
                return;
            }
            if (chunkIndex < changed.Count)
            {
                var c = changed[chunkIndex]; var data = ready.chunks[chunkIndex];
                if (ReferenceEquals(data, c.source)) { chunkIndex++; return; }
                if (c.back == null) c.back = new Mesh { indexFormat = IndexFormat.UInt32 };
                switch (chunkStage++)
                {
                    case 0: c.back.Clear(); c.back.SetVertices(data.vertices); break;
                    case 1: c.back.SetNormals(data.normals); break;
                    case 2: c.back.SetUVs(0, data.uv); break;
                    case 3: c.back.SetIndices(data.triangles, MeshTopology.Triangles, 0, false); c.back.bounds = c.original.bounds; chunkIndex++; chunkStage = 0; break;
                }
                return;
            }
            Publish();
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
                outgoing.data = null; pool.Push(outgoing);
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
            incoming = outgoing = null; ready = null; changed.Clear();
        }

        // Teleports and worker faults discard staging resources without changing any visible ownership.
        void Abort()
        {
            if (incoming != null) pool.Push(incoming);
            incoming = outgoing = null; ready = null; sampling = false; roughness = null; coverPositions = null; changed.Clear();
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
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}

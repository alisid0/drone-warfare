using System;
using System.Collections.Generic;
using System.Text;
using DroneSandbox;
using UnityEngine;

// Replays hd167's continuous flight (19.8 km: base, wood, rail junction at 1x, then works and village at 4x, with the
// autotest's 50 m / 300 m / 2.5 km stop views) through the detail streamer at frame resolution. The planner after is
// the production FarDetailRoute; the planner before, and both lane/commit/upload state machines, are ports of
// FarDetail at e666e0f and now. Work is fake and timed: tile generation (p99 about 400 ms in the brief's scenario),
// chunk cuts 10-30 ms, upload stages at assumed typical costs. Deterministic; no Unity, no rendering.
static class Simulation
{
    public const int MaxTiles = 25;
    public const float Budget = 0.35f, RowMs = 0.0135f, PlanMs = 0.15f, FrameSeconds = 0.0125f;
    // The scenario: generation seconds = base + span * u^power for uniform u, and the second half's multiplier.
    public static float BuildBase, BuildSpan, BuildPower, Fast;
    const float Half = 28000f, Edge = 1000f - 0.5f, NearReach = 1500f;
    // The sector is taken as centred on the base (hd167 logs only the base); this affects eligibility near the start.
    public static readonly Vector2 Origin = new Vector2(3339f, -2240f);
    static readonly Vector2[] Stops = { new Vector2(1500f, -2500f), new Vector2(-7086f, -7021f), new Vector2(-5779f, -13366f), new Vector2(-5719f, -15077f) };
    static readonly string[] StopNames = { "wood", "rail junction", "works", "village" };

    // FarDetail.Eligible with the assumed origin: inside the world, clear of the 2 km sector and Backdrop's 1.5 km ring.
    public static bool Eligible(int x, int z)
    {
        if ((x + 1) * 512f <= -Half || (z + 1) * 512f <= -Half || x * 512f >= Half || z * 512f >= Half) return false;
        float lx = x * 512f - Origin.x, lz = z * 512f - Origin.y;
        float nx = lx > 0f ? lx : lx + 512f < 0f ? -(lx + 512f) : 0f;
        float nz = lz > 0f ? lz : lz + 512f < 0f ? -(lz + 512f) : 0f;
        float axis = Mathf.Max(nx, nz);
        return axis > Edge && Mathf.Sqrt(nx * nx + nz * nz) * (1f - Edge / axis) >= NearReach;
    }

    // The strategy camera's pitch at a zoom distance (RtsController.ApplyCamera with Scale.Camera 5.81).
    static float Pitch(float distance)
    {
        const float camera = 5.8077f, maxZoom = 110f * camera;
        return distance <= maxZoom ? Mathf.Lerp(42f, 64f, Mathf.InverseLerp(15f * camera, maxZoom, distance))
            : Mathf.Lerp(64f, 72f, Mathf.InverseLerp(maxZoom, 12000f, distance));
    }

    // One frame's inputs: positions in world metres, velocities in real metres per second.
    public sealed class Frame
    {
        public float now, timeScale;
        public Vector2 eye, focus, drone, droneVelocity;
        public float droneSpeed;
        public readonly List<Vector2> route = new List<Vector2>();
    }

    // Per-run readings, split by speed: frames, frames with the tile under the drone missing, with any missing tile
    // in the camera's 600 m zone, and in its 250 m visible core; ReadyAhead minima and short-horizon frames.
    sealed class Tally
    {
        public int frames, droneMissing, droneFrames, zoneMissing, coreMissing, zoneTiles, zoneTileMissing, readyFrames, readyUnder1, readyUnder3;
        public float readyMin = float.PositiveInfinity;
        public string Line(string name) => $"{name}: {frames} frames; under the drone missing {droneMissing}/{droneFrames} ({Percent(droneFrames - droneMissing, droneFrames)} ready); " +
            $"camera 600 m zone frames with a gap {zoneMissing} ({Percent(frames - zoneMissing, frames)} complete, tile-frames missing {zoneTileMissing}/{zoneTiles}); " +
            $"250 m core frames with a gap {coreMissing}" + (readyFrames == 0 ? "" :
            $"; ReadyAhead min {readyMin:F2} s, frames under 1 s {readyUnder1}, under 3 s {readyUnder3}");
        static string Percent(int a, int b) => b == 0 ? "n/a" : (100.0 * a / b).ToString("F2") + "%";
    }

    // Flies the route once through one streamer; optionally lets ReadyAhead throttle the 4x half (illustration only).
    static string Run(Streamer streamer, bool throttle)
    {
        var frame = new Frame();
        var slow = new Tally(); var fast = new Tally();
        Vector2 drone = Origin, velocity = Vector2.zero;
        int target = 0, reached = 0, frames = 0, throttledFrames = 0;
        float now = 0f, pause = 0f, settle = 5f + 1f, scale = Fast, realFast = 0f;
        var legs = new StringBuilder();
        const float speed = 60f;
        while (reached < Stops.Length && frames < 200000)
        {
            frames++;
            now += FrameSeconds;
            bool warm = now < 5f, fastHalf = reached >= (Stops.Length + 1) / 2;
            float timeScale = warm || pause > 0f ? 0f : fastHalf ? scale : 1f;
            float view = 120f;
            if (pause > 0f)
            {
                float into = 4.6f - pause;
                view = into < 1.2f ? 50f : into < 2.4f ? 300f : 2500f;
                pause -= FrameSeconds;
                if (pause <= 0f) { reached++; settle = now + 2f; }
            }
            // Drone.Fly: steer toward the current waypoint at its speed, accelerating at 2.5x speed per game second.
            float dtGame = FrameSeconds * timeScale;
            if (timeScale > 0f && target < Stops.Length)
            {
                Vector2 desired = Vector2.ClampMagnitude((Stops[target] - drone) * 2f, speed);
                velocity = Vector2.MoveTowards(velocity, desired, speed * 2.5f * dtGame);
                drone += velocity * dtGame;
                if (Vector2.Distance(drone, Stops[target]) < 0.6f) target++;
            }
            if (pause <= 0f && !warm && reached < Stops.Length && Vector2.Distance(drone, Stops[reached]) < 90f)
            {
                legs.Append($" {StopNames[reached]} at {now:F0} s;");
                pause = 4.6f;
            }
            float horizontal = view * Mathf.Cos(Pitch(view) * Mathf.Deg2Rad);
            frame.now = now; frame.timeScale = timeScale; frame.drone = drone; frame.focus = drone;
            frame.eye = drone - new Vector2(0.5f, 0.8660254f) * horizontal;
            frame.droneVelocity = velocity * timeScale;
            frame.droneSpeed = speed * timeScale;
            frame.route.Clear();
            for (int i = target; i < Stops.Length; i++) frame.route.Add(Stops[i]);
            streamer.Step(frame);
            // The illustrative throttle: half speed below 1.5 s of readiness, 1x below 0.5 s, full again above 3 s.
            if (throttle && fastHalf && pause <= 0f)
            {
                float ready = streamer.ReadyAhead;
                if (ready < 0.5f) scale = 1f; else if (ready < 1.5f) scale = Mathf.Min(scale, Fast * 0.5f); else if (ready > 3f) scale = Fast;
                if (scale < Fast) throttledFrames++;
            }
            if (fastHalf && pause <= 0f) realFast += FrameSeconds;
            if (warm || pause > 0f || now < settle) continue;
            var tally = fastHalf ? fast : slow;
            tally.frames++;
            int dx = Mathf.FloorToInt(drone.x / 512f), dz = Mathf.FloorToInt(drone.y / 512f);
            if (Eligible(dx, dz)) { tally.droneFrames++; if (!streamer.IsLive(dx, dz)) tally.droneMissing++; }
            bool zoneGap = false, coreGap = false;
            for (int z = Mathf.FloorToInt((drone.y - 600f) / 512f); z <= Mathf.FloorToInt((drone.y + 600f) / 512f); z++)
                for (int x = Mathf.FloorToInt((drone.x - 600f) / 512f); x <= Mathf.FloorToInt((drone.x + 600f) / 512f); x++)
                {
                    float distance = FarDetailRoute.DistanceTo(x, z, drone);
                    if (distance > 600f || !Eligible(x, z)) continue;
                    tally.zoneTiles++;
                    if (streamer.IsLive(x, z)) continue;
                    tally.zoneTileMissing++; zoneGap = true;
                    if (distance <= 250f) coreGap = true;
                }
            if (zoneGap) tally.zoneMissing++;
            if (coreGap) tally.coreMissing++;
            float ahead = streamer.ReadyAhead;
            if (!float.IsNaN(ahead) && streamer.HasReadyAhead)
            {
                tally.readyFrames++; tally.readyMin = Mathf.Min(tally.readyMin, ahead);
                if (ahead < 1f) tally.readyUnder1++;
                if (ahead < 3f) tally.readyUnder3++;
            }
        }
        var text = new StringBuilder();
        text.AppendLine($"== {streamer.Name}{(throttle ? " + illustrative ReadyAhead throttle" : "")} ==");
        text.AppendLine($"route:{legs} {frames} frames, {Fast:F0}x half {realFast:F0} real s{(throttle ? $", throttled {throttledFrames} frames" : "")}");
        text.AppendLine(slow.Line("1x"));
        text.AppendLine(fast.Line(Fast.ToString("F0") + "x"));
        text.AppendLine(streamer.Summary());
        return text.ToString();
    }

    // Each scenario: before, after, and after with the illustrative throttle.
    static int Main(string[] args)
    {
        Console.WriteLine("Predictive streaming replay of hd167's route; 12.5 ms frames; cap 25 tiles; fake timed work");
        Scenario("A. The brief: generation 180-400 ms (p99 about 391 ms), second half at 4x", 0.18f, 0.22f, 4f, 4f);
        Scenario("B. Calibrated to hd167's ~1.1 s per tile: generation 300-600 ms (mean 400, p99 about 597 ms), 4x", 0.30f, 0.30f, 2f, 4f);
        Scenario("C. Stress: B's generation with the second half at 8x", 0.30f, 0.30f, 2f, 8f);
        return 0;
    }

    // Set one scenario's work and multiplier, then fly it three ways.
    static void Scenario(string title, float buildBase, float buildSpan, float buildPower, float fast)
    {
        BuildBase = buildBase; BuildSpan = buildSpan; BuildPower = buildPower; Fast = fast;
        Console.WriteLine("######## " + title);
        Console.WriteLine(Run(new Before(), false));
        Console.WriteLine(Run(new After(), false));
        Console.WriteLine(Run(new After(), true));
    }
}

// Fake timed work from a fixed seed: tile generation by the scenario's curve (15% slower while both lanes build) and
// far-chunk cuts of 10-30 ms.
sealed class Work
{
    uint state = 0x9E3779B9u;
    public float Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (state & 0xFFFFFF) / 16777216f; }
    public float BuildSeconds(bool contended)
        => (Simulation.BuildBase + Simulation.BuildSpan * (float)Math.Pow(Next(), Simulation.BuildPower)) * (contended ? 1.15f : 1f);
    public float CutSeconds() => 0.010f + 0.020f * Next();
}

// One main-thread upload stage with its assumed typical cost; alone stages never share a frame after the change.
struct UploadStep
{
    public int label;
    public float ms;
    public bool alone;
    public UploadStep(int label, float ms, bool alone) { this.label = label; this.ms = ms; this.alone = alone; }
}

// What both streamers share: residency, the far-chunk back buffers, the regional queue and accounting.
abstract class Streamer
{
    public const int TileMesh = 0, TilePixels = 1, TileApply = 2, CollisionMesh = 3, ControlPixels = 4, ControlApply = 5, Bake = 6, MeshUpload = 7, ChunkMesh = 8, Publish = 9, Labels = 10;
    public sealed class Tile { public int x, z; public float lastWanted; }
    public abstract string Name { get; }
    public virtual float ReadyAhead => float.PositiveInfinity;
    public virtual bool HasReadyAhead => false;
    protected readonly Dictionary<long, Tile> live = new Dictionary<long, Tile>();
    protected readonly HashSet<int> backs = new HashSet<int>();
    protected readonly Work work = new Work();
    protected int committed, pressure, wasted, transactions, transactionFrames, overBudget, regionalCommitted, regionalOwnMissing, regionalBacklog;
    protected double totalMs, worstMs; protected int frames;
    readonly HashSet<long> pages = new HashSet<long>();
    bool regionalWorker, regionalReady, pagesSeeded; float regionalDone; int regionalStage, regionalX, regionalZ;

    public bool IsLive(int x, int z) => live.ContainsKey(FarDetailRoute.Key(x, z));
    public abstract void Step(Simulation.Frame frame);

    // The steps of one transaction: the tile's 22 stages, ten per changed far chunk, then publication.
    protected List<UploadStep> Steps(bool incoming, List<int> chunks)
    {
        var steps = new List<UploadStep>();
        if (incoming)
        {
            steps.Add(new UploadStep(TileMesh, .12f, false)); steps.Add(new UploadStep(TileMesh, .10f, false)); steps.Add(new UploadStep(TileMesh, .07f, false)); steps.Add(new UploadStep(TileMesh, .20f, false));
            foreach (float mip in new[] { .05f, .015f, .006f, .003f, .002f, .002f, .002f, .002f, .002f }) steps.Add(new UploadStep(TilePixels, mip, false));
            steps.Add(new UploadStep(TileApply, .02f, true)); steps.Add(new UploadStep(CollisionMesh, .04f, false)); steps.Add(new UploadStep(CollisionMesh, .03f, false));
            steps.Add(new UploadStep(TileMesh, .12f, false)); steps.Add(new UploadStep(ControlPixels, .06f, false)); steps.Add(new UploadStep(ControlApply, .02f, true));
            steps.Add(new UploadStep(TileMesh, .02f, false)); steps.Add(new UploadStep(Bake, .02f, true)); steps.Add(new UploadStep(MeshUpload, .015f, true));
        }
        foreach (int chunk in chunks)
        {
            bool fresh = !backs.Contains(chunk);
            steps.Add(new UploadStep(ChunkMesh, fresh ? .02f : .001f, fresh)); steps.Add(new UploadStep(ChunkMesh, .01f, false));
            steps.Add(new UploadStep(ChunkMesh, .04f, true)); steps.Add(new UploadStep(ChunkMesh, .03f, true));
            // 23 slices of 1024 vertices or 3072 indices, up to six per call within 0.2 ms.
            for (int slices = 23; slices > 0; slices -= 6) steps.Add(new UploadStep(ChunkMesh, Math.Min(6, slices) * .02f, false));
            steps.Add(new UploadStep(ChunkMesh, .01f, false)); steps.Add(new UploadStep(MeshUpload, .015f, true));
        }
        steps.Add(new UploadStep(Publish, .045f, true));
        return steps;
    }

    // 4 km far chunks touching any of the given tiles (FarDetailChunks.Overlaps).
    protected static void Chunks(List<int> result, Tile a, Tile b)
    {
        result.Clear();
        for (int cz = 0; cz < 14; cz++) for (int cx = 0; cx < 14; cx++)
            {
                float x = -28000f + cx * 4000f, z = -28000f + cz * 4000f;
                if (a != null && Touches(x, z, a) || b != null && Touches(x, z, b)) result.Add(cz * 14 + cx);
            }
    }

    // The same touching test FarDetailChunks uses.
    protected static bool Touches(float x, float z, Tile t) => t.x * 512f <= x + 4000f && t.x * 512f + 512f >= x && t.z * 512f <= z + 4000f && t.z * 512f + 512f >= z;

    // A changed chunk with no tile on it after the transaction keeps its source mesh: no upload stages.
    protected List<int> Uploaded(List<int> changed, Tile incoming, Tile outgoing)
    {
        var result = new List<int>();
        foreach (int chunk in changed)
        {
            float x = -28000f + chunk % 14 * 4000f, z = -28000f + chunk / 14 * 4000f;
            bool occupied = incoming != null && Touches(x, z, incoming);
            foreach (var t in live.Values) if (t != outgoing && Touches(x, z, t)) { occupied = true; break; }
            if (occupied) result.Add(chunk);
        }
        return result;
    }

    // Publication: swap residency, keep a back buffer only on chunks something still stands on.
    protected void Swap(List<int> changed, List<int> uploaded, Tile incoming, Tile outgoing, float now)
    {
        if (outgoing != null) live.Remove(FarDetailRoute.Key(outgoing.x, outgoing.z));
        if (incoming != null) { incoming.lastWanted = now; live.Add(FarDetailRoute.Key(incoming.x, incoming.z), incoming); committed++; }
        foreach (int chunk in uploaded) backs.Add(chunk);
        foreach (int chunk in changed)
        {
            float x = -28000f + chunk % 14 * 4000f, z = -28000f + chunk / 14 * 4000f;
            bool occupied = false;
            foreach (var t in live.Values) if (Touches(x, z, t)) { occupied = true; break; }
            if (!occupied) backs.Remove(chunk);
        }
    }

    // FarDetailRegional's queue: 2 km pages within four of the camera's, one 110 ms worker, eight one-frame stages.
    protected bool RegionalPending => regionalReady || regionalWorker;
    protected bool RegionalWaiting(float now) => regionalReady || regionalWorker && now >= regionalDone;
    protected float Regional(Vector2 eye, float now, bool commit, bool idleLaunch)
    {
        int cx = Mathf.FloorToInt(eye.x / 2048f), cz = Mathf.FloorToInt(eye.y / 2048f);
        if (!pagesSeeded) { pagesSeeded = true; for (int z = cz - 4; z <= cz + 4; z++) for (int x = cx - 4; x <= cx + 4; x++) pages.Add(FarDetailRoute.Key(x, z)); }
        frames++;
        regionalBacklog = 0;
        for (int z = cz - 4; z <= cz + 4; z++) for (int x = cx - 4; x <= cx + 4; x++) if (!pages.Contains(FarDetailRoute.Key(x, z))) regionalBacklog++;
        if (!pages.Contains(FarDetailRoute.Key(cx, cz))) regionalOwnMissing++;
        if (!commit)
        {
            if (idleLaunch && !regionalReady && !regionalWorker) Launch(cx, cz, now);
            return 0f;
        }
        if (regionalWorker && now >= regionalDone) { regionalWorker = false; regionalReady = true; regionalStage = 0; }
        if (regionalReady)
        {
            if (Math.Abs(regionalX - cx) > 4 || Math.Abs(regionalZ - cz) > 4) { regionalReady = false; return 0.005f; }
            if (++regionalStage >= 8) { regionalReady = false; pages.Add(FarDetailRoute.Key(regionalX, regionalZ)); regionalCommitted++; }
            return 0.035f;
        }
        if (!regionalWorker) Launch(cx, cz, now);
        return 0.01f;
    }

    // Start the nearest missing page's worker.
    void Launch(int cx, int cz, float now)
    {
        float best = float.MaxValue;
        for (int z = cz - 4; z <= cz + 4; z++) for (int x = cx - 4; x <= cx + 4; x++)
            {
                if (pages.Contains(FarDetailRoute.Key(x, z))) continue;
                float d = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                if (d < best) { best = d; regionalX = x; regionalZ = z; }
            }
        if (best == float.MaxValue) return;
        regionalWorker = true; regionalDone = now + 0.11f;
    }

    // Record one frame's modelled main-thread streaming time.
    protected void Account(double ms)
    {
        totalMs += ms; worstMs = Math.Max(worstMs, ms);
        if (ms > Simulation.Budget + 0.05) overBudget++;
    }

    // Totals for the report.
    public virtual string Summary() =>
        $"committed {committed} tiles in {transactions} transactions (mean {(transactions > 0 ? transactionFrames / (double)transactions : 0):F1} frames from start to publish), " +
        $"pressure evictions {pressure}, discarded prepared tiles {wasted}; modelled streaming main thread mean {totalMs / Math.Max(1, frames):F3} ms, " +
        $"worst {worstMs:F3} ms, frames over budget+0.05 ms {overBudget}; regional pages {regionalCommitted}, frames with the camera's own 2 km page missing {regionalOwnMissing}";
}

// FarDetail at e666e0f: 1.5 km round the camera plus 1.5 km capsules for camera and feed, one serial transaction
// (sample, worker build + cut, one upload stage per frame), 15 s retention, farthest-first pressure eviction.
sealed class Before : Streamer
{
    struct Request { public int x, z; public float arrival, distance; }
    readonly List<Request> requests = new List<Request>();
    readonly HashSet<long> wanted = new HashSet<long>();
    readonly List<int> changed = new List<int>();
    List<int> uploaded;
    List<UploadStep> steps;
    Tile incoming, outgoing;
    int row, step, started;
    bool sampling, working, hasPrevious;
    float workerDone, nextPlan;
    Vector2 eye, velocity, feed, feedVelocity, previousEye, previousFeed;
    public override string Name => "before (e666e0f scheduler)";

    // One LateUpdate.
    public override void Step(Simulation.Frame f)
    {
        double ms = 0.01;
        float dt = Simulation.FrameSeconds;
        eye = f.eye;
        velocity = hasPrevious && Vector2.Distance(eye, previousEye) < 200f ? Vector2.Lerp(velocity, (eye - previousEye) / dt, 0.25f) : Vector2.zero;
        previousEye = eye;
        feed = f.drone;
        feedVelocity = hasPrevious && Vector2.Distance(feed, previousFeed) < 200f ? (feed - previousFeed) / dt : Vector2.zero;
        previousFeed = feed; hasPrevious = true;
        if (f.now >= nextPlan) { Plan(f.now); nextPlan = f.now + 0.2f; ms += Simulation.PlanMs; }
        if (working && f.now >= workerDone) { working = false; step = 0; steps = Steps(incoming != null, uploaded); }
        bool tileWork = false;
        if (ms < Simulation.Budget)
        {
            if (sampling)
            {
                tileWork = true;
                do { row++; ms += Simulation.RowMs; } while (row < 129 && ms < Simulation.Budget);
                if (row >= 129) { sampling = false; Launch(f.now, true); }
            }
            else if (steps != null)
            {
                tileWork = true; ms += steps[step].ms;
                if (++step == steps.Count) Finish(f.now);
            }
            else if (!working) { ms += 0.02; Schedule(f.now); }
        }
        ms += Regional(eye, f.now, !tileWork && ms < 0.2, false);
        Account(ms);
    }

    // The old Plan with its two AddCandidates passes and arrival sort.
    void Plan(float now)
    {
        requests.Clear(); wanted.Clear();
        Add(eye, velocity); Add(feed, feedVelocity);
        requests.Sort((a, b) =>
        {
            int time = a.arrival.CompareTo(b.arrival); if (time != 0) return time;
            int distance = a.distance.CompareTo(b.distance); if (distance != 0) return distance;
            return FarDetailRoute.Key(a.x, a.z).CompareTo(FarDetailRoute.Key(b.x, b.z));
        });
        if (requests.Count > MaxTiles) requests.RemoveRange(MaxTiles, requests.Count - MaxTiles);
        wanted.Clear();
        foreach (var r in requests) wanted.Add(FarDetailRoute.Key(r.x, r.z));
        foreach (var s in live.Values)
            if (FarDetailRoute.DistanceTo(s.x, s.z, eye) < 1500f || FarDetailRoute.DistanceTo(s.x, s.z, feed) < 500f) s.lastWanted = now;
    }
    const int MaxTiles = Simulation.MaxTiles;

    // AddCandidates at e666e0f.
    void Add(Vector2 at, Vector2 speed)
    {
        int cx = Mathf.FloorToInt(at.x / 512f), cz = Mathf.FloorToInt(at.y / 512f);
        Vector2 heading = speed.normalized;
        for (int z = cz - 6; z <= cz + 6; z++) for (int x = cx - 6; x <= cx + 6; x++)
            {
                if (!Simulation.Eligible(x, z)) continue;
                float distance = FarDetailRoute.DistanceTo(x, z, at);
                float arrival = FarDetailRoute.Arrival(x, z, at, speed);
                Vector2 delta = new Vector2((x + 0.5f) * 512f - at.x, (z + 0.5f) * 512f - at.y);
                float along = Vector2.Dot(delta, heading), side = Mathf.Abs(delta.x * heading.y - delta.y * heading.x);
                if (distance > 1500f && !(speed.sqrMagnitude > 0.01f && along >= 0f && along <= 1500f + 256f && side < 512f)) continue;
                long key = FarDetailRoute.Key(x, z);
                if (!wanted.Add(key))
                {
                    for (int i = 0; i < requests.Count; i++) if (requests[i].x == x && requests[i].z == z)
                        { var old = requests[i]; old.arrival = Mathf.Min(old.arrival, arrival); old.distance = Mathf.Min(old.distance, distance); requests[i] = old; break; }
                }
                else requests.Add(new Request { x = x, z = z, arrival = arrival, distance = distance });
            }
    }

    // Schedule at e666e0f.
    void Schedule(float now)
    {
        outgoing = null;
        foreach (var s in live.Values) if (!wanted.Contains(FarDetailRoute.Key(s.x, s.z)) && now - s.lastWanted > 15f) { outgoing = s; break; }
        Request? next = null;
        foreach (var r in requests) if (!IsLive(r.x, r.z)) { next = r; break; }
        if (!next.HasValue && outgoing == null) return;
        if (next.HasValue && live.Count >= MaxTiles && outgoing == null)
        {
            float farthest = -1f;
            foreach (var s in live.Values)
            {
                if (wanted.Contains(FarDetailRoute.Key(s.x, s.z))) continue;
                float d = Mathf.Min(FarDetailRoute.DistanceTo(s.x, s.z, eye), FarDetailRoute.DistanceTo(s.x, s.z, feed));
                if (d > 500f && d > farthest) { farthest = d; outgoing = s; }
            }
            if (outgoing == null) return;
            pressure++;
        }
        started = frames;
        if (next.HasValue) { incoming = new Tile { x = next.Value.x, z = next.Value.z }; row = 0; sampling = true; }
        else { incoming = null; Launch(now, false); }
    }

    // One worker builds the tile and cuts the chunks.
    void Launch(float now, bool tile)
    {
        Chunks(changed, incoming, outgoing);
        uploaded = Uploaded(changed, incoming, outgoing);
        working = true; workerDone = now + (tile ? work.BuildSeconds(false) : 0f) + work.CutSeconds();
    }

    // Publish at e666e0f, discarding a tile that stopped being wanted.
    void Finish(float now)
    {
        transactions++; transactionFrames += frames - started;
        steps = null;
        if (incoming != null && !wanted.Contains(FarDetailRoute.Key(incoming.x, incoming.z))) { wasted++; incoming = outgoing = null; return; }
        Swap(changed, uploaded, incoming, outgoing, now);
        incoming = outgoing = null;
    }
}

// FarDetail now: FarDetailRoute's plan, two prepare lanes, a commit that only cuts chunks, packed upload stages within
// the same 0.35 ms budget, 20 s retention with least-recently-wanted pressure eviction, regional fairness.
sealed class After : Streamer
{
    sealed class Lane { public bool held, sampling, building, ready; public int x, z, row; public float done, started; }
    readonly FarDetailRoute route = new FarDetailRoute(Simulation.Eligible);
    readonly Lane[] lanes = { new Lane(), new Lane() };
    readonly HashSet<long> wanted = new HashSet<long>();
    readonly List<int> changed = new List<int>();
    readonly double[] estimate = new double[Labels];
    readonly double[] latencies = new double[64], sorted = new double[64];
    readonly Func<int, int, bool> isLive;
    List<int> uploaded;
    List<UploadStep> steps;
    Tile incoming, outgoing;
    int step, started, regionalWait, latencyCount, stages;
    bool cutting, hasPrevious;
    float cutDone, nextPlan, generationP99 = 0.4f, readyAhead = float.PositiveInfinity;
    Vector2 eye, velocity, feed, feedVelocity, previousEye, previousFeed, focus, planEye;
    public override string Name => "after (predictive scheduler)";
    public override float ReadyAhead => readyAhead;
    public override bool HasReadyAhead => true;

    // Stage estimates start at the budget, so each stage's first use runs alone.
    public After() { for (int i = 0; i < Labels; i++) estimate[i] = Simulation.Budget; isLive = IsLive; }

    // One LateUpdate, in FarDetail's order.
    public override void Step(Simulation.Frame f)
    {
        double ms = 0.01;
        float dt = Simulation.FrameSeconds;
        eye = f.eye; focus = f.focus;
        if (hasPrevious && Vector2.Distance(eye, previousEye) < 200f) velocity = Vector2.Lerp(velocity, (eye - previousEye) / dt, 0.25f);
        else { velocity = Vector2.zero; nextPlan = 0f; }
        previousEye = eye;
        feed = f.drone;
        feedVelocity = hasPrevious && Vector2.Distance(feed, previousFeed) < 200f ? Vector2.Lerp(feedVelocity, (feed - previousFeed) / dt, 0.25f) : Vector2.zero;
        previousFeed = feed; hasPrevious = true;
        if (f.now >= nextPlan) { Plan(f); nextPlan = f.now + 0.2f; ms += Simulation.PlanMs; }
        Collect(f.now);
        bool regionalWaiting = RegionalWaiting(f.now), regionalTurn = regionalWaiting && regionalWait >= 2;
        bool tileWork = false;
        stages = 0;
        if (!regionalTurn && ms < Simulation.Budget)
        {
            tileWork = Commit(f.now, ref ms);
            if (ms < Simulation.Budget) tileWork |= Prepare(f.now, ref ms);
        }
        bool regionalCommit = !tileWork && ms < 0.2;
        ms += Regional(eye, f.now, regionalCommit, true);
        regionalWait = regionalWaiting && !regionalCommit ? regionalWait + 1 : 0;
        readyAhead = route.ReadyAhead(isLive, Vector2.Distance(feed, planEye), feedVelocity.magnitude);
        Account(ms);
    }

    // FarDetail.Plan's route inputs, then the production planner.
    void Plan(Simulation.Frame f)
    {
        route.cameraEye = eye; route.cameraFocus = focus; route.cameraVelocity = velocity;
        route.hasDrone = true; route.observed = true; route.drone = feed; route.droneVelocity = feedVelocity;
        route.droneSpeed = f.droneSpeed; route.ClearRoute();
        foreach (var p in f.route) route.AddRoutePoint(p);
        route.generationSeconds = generationP99;
        route.Plan(Simulation.MaxTiles);
        planEye = route.Eye;
        wanted.Clear();
        foreach (var r in route.requests) wanted.Add(FarDetailRoute.Key(r.x, r.z));
        foreach (var s in live.Values)
            if (wanted.Contains(FarDetailRoute.Key(s.x, s.z)) || FarDetailRoute.DistanceTo(s.x, s.z, eye) < 1500f || FarDetailRoute.DistanceTo(s.x, s.z, focus) < 600f
                || FarDetailRoute.DistanceTo(s.x, s.z, feed) < 500f) s.lastWanted = f.now;
    }

    // Finished cut worker and lane builds, with each build's latency.
    void Collect(float now)
    {
        if (cutting && now >= cutDone) { cutting = false; step = 0; steps = Steps(incoming != null, uploaded); }
        foreach (var lane in lanes)
        {
            if (!lane.building || now < lane.done) continue;
            lane.building = false; lane.ready = true;
            latencies[latencyCount++ % 64] = (now - lane.started) * 1000.0;
            int n = Math.Min(latencyCount, 64);
            Array.Copy(latencies, sorted, n); Array.Sort(sorted, 0, n);
            generationP99 = (float)(sorted[Math.Min(n - 1, (int)Math.Ceiling(n * 0.99) - 1)] / 1000.0);
        }
    }

    // Upload the ready transaction or schedule the next.
    bool Commit(float now, ref double ms)
    {
        if (steps != null)
        {
            while (steps != null)
            {
                var s = steps[step];
                if (stages > 0 && (s.alone || ms + estimate[s.label] > Simulation.Budget)) break;
                ms += s.ms; estimate[s.label] = Math.Max(s.ms, estimate[s.label] * 0.95); stages++;
                if (++step == steps.Count) Finish(now);
                if (s.alone) break;
            }
            return true;
        }
        if (cutting) return false;
        ms += 0.02; Schedule(now);
        return false;
    }

    // FarDetail.Schedule now.
    void Schedule(float now)
    {
        outgoing = null;
        foreach (var lane in lanes) if (lane.ready && !wanted.Contains(FarDetailRoute.Key(lane.x, lane.z))) { Recycle(lane); wasted++; }
        foreach (var s in live.Values) if (!wanted.Contains(FarDetailRoute.Key(s.x, s.z)) && now - s.lastWanted > 20f) { outgoing = s; break; }
        Lane next = null; bool missing = false;
        foreach (var r in route.requests)
        {
            if (IsLive(r.x, r.z)) continue;
            missing = true;
            foreach (var lane in lanes) if (lane.ready && lane.x == r.x && lane.z == r.z) { next = lane; break; }
            if (next != null) break;
        }
        if (next == null && (outgoing == null || missing)) return;
        if (next != null && live.Count >= Simulation.MaxTiles && outgoing == null)
        {
            float oldest = float.MaxValue, farthest = -1f;
            foreach (var s in live.Values)
            {
                if (wanted.Contains(FarDetailRoute.Key(s.x, s.z))) continue;
                float d = Mathf.Min(FarDetailRoute.DistanceTo(s.x, s.z, eye), FarDetailRoute.DistanceTo(s.x, s.z, feed));
                if (d <= 500f) continue;
                if (s.lastWanted < oldest || s.lastWanted == oldest && d > farthest) { oldest = s.lastWanted; farthest = d; outgoing = s; }
            }
            if (outgoing == null) return;
            pressure++;
        }
        started = frames;
        incoming = null;
        if (next != null) { incoming = new Tile { x = next.x, z = next.z }; Recycle(next); }
        Chunks(changed, incoming, outgoing);
        uploaded = Uploaded(changed, incoming, outgoing);
        cutting = true; cutDone = now + work.CutSeconds();
    }

    // A lane gives up its slot.
    static void Recycle(Lane lane) { lane.held = lane.sampling = lane.building = lane.ready = false; }

    // FarDetail.Prepare now: one lane samples within the leftover budget, then builds on a worker.
    bool Prepare(float now, ref double ms)
    {
        Lane active = null;
        foreach (var lane in lanes) if (lane.sampling) { active = lane; break; }
        if (active != null && !wanted.Contains(FarDetailRoute.Key(active.x, active.z))) { Recycle(active); active = null; wasted++; }
        if (active == null)
        {
            foreach (var lane in lanes) if (!lane.held) { active = lane; break; }
            if (active == null || !Target(out int x, out int z)) return false;
            active.held = true; active.sampling = true; active.x = x; active.z = z; active.row = 0; active.started = now;
        }
        do { active.row++; ms += Simulation.RowMs; } while (active.row < 129 && ms < Simulation.Budget);
        if (active.row >= 129)
        {
            bool contended = false;
            foreach (var lane in lanes) if (lane != active && lane.building) contended = true;
            active.sampling = false; active.building = true; active.done = now + work.BuildSeconds(contended);
        }
        return true;
    }

    // First request in deadline order not live, committing or in a lane.
    bool Target(out int x, out int z)
    {
        foreach (var r in route.requests)
        {
            if (IsLive(r.x, r.z) || incoming != null && incoming.x == r.x && incoming.z == r.z) continue;
            bool held = false;
            foreach (var lane in lanes) if (lane.held && lane.x == r.x && lane.z == r.z) held = true;
            if (held) continue;
            x = r.x; z = r.z; return true;
        }
        x = z = 0; return false;
    }

    // Publish now, discarding a tile that stopped being wanted.
    void Finish(float now)
    {
        transactions++; transactionFrames += frames - started;
        steps = null;
        if (incoming != null && !wanted.Contains(FarDetailRoute.Key(incoming.x, incoming.z))) { wasted++; incoming = outgoing = null; return; }
        Swap(changed, uploaded, incoming, outgoing, now);
        incoming = outgoing = null;
    }

    // Adds the latency reading.
    public override string Summary() => base.Summary() + $"; generation latency p99 (last 64) {generationP99 * 1000f:F0} ms, prefetch lead {route.Lead:F0} m";
}

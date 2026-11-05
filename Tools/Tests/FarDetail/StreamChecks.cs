using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using DroneSandbox;
using UnityEngine;

// Execute the production streaming state machine with managed engine storage, never claiming GPU timing.
static class StreamChecks
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly MethodInfo Tick = typeof(FarDetail).GetMethod("LateUpdate", Private);
    static readonly FieldInfo Worker = typeof(FarDetail).GetField("worker", Private);
    static int frames, misses, maximum;
    static readonly FieldInfo Live = typeof(FarDetail).GetField("live", Private);
    static readonly Dictionary<object, (FarDetailData.Tile data, int frame)> born = new Dictionary<object, (FarDetailData.Tile, int)>();

    // Warm the ring, fly twenty kilometres at a real 160 m/s (40 m/s at 4x), turn, teleport, then rebuild.
    public static object Run(float[][] heights)
    {
        Register(heights);
        for (int z = -8; z <= 8; z++) for (int x = -8; x <= 8; x++) if (FarDetail.Eligible(x, z))
                    for (int j = 0; j <= 8; j++) for (int i = 0; i <= 8; i++)
                        {
                            float px = x * 512f + i * 64f, pz = z * 512f + j * 64f, edge = Ground.Size / 2f - 0.5f, axis = Math.Max(Math.Abs(px), Math.Abs(pz));
                            if (axis <= edge || Math.Sqrt(px * px + pz * pz) * (1f - edge / axis) < 1499.99)
                                throw new Exception("An eligible tile overlaps Backdrop's radial near ring");
                        }
        if (!FarDetail.Eligible(54, 54) || !FarDetail.Eligible(-55, -55) || FarDetail.Eligible(55, 55))
            throw new Exception("Partial theatre-edge tiles are missing or outside-only tiles are requested");
        if (Math.Abs(FarDetail.Arrival(12, 13, new Vector2(4000f, 7000f), new Vector2(160f, 0f)) - 10.275f) > 0.001f)
            throw new Exception("Prediction does not use real-time 4x velocity");
        if (FarDetail.Arrival(2, 13, new Vector2(4000f, 7000f), new Vector2(160f, 0f)) != float.MaxValue)
            throw new Exception("A tile behind the aircraft incorrectly precedes predicted arrival");
        var host = new GameObject("test world");
        Camera.main = new GameObject("camera").AddComponent<Camera>();
        var drone = new GameObject("feed").AddComponent<Drone>();
        DroneFeed.Instance = new DroneFeed { Source = drone };
        FarDetail.Initialise(host.transform);
        var streamer = host.GetComponent<FarDetail>();
        for (int n = 0; n < 1000; n++) Frame(streamer, new Vector2(6000f, 7000f), drone, false);
        if (FarDetail.Live == 0) throw new Exception("Stream warmup never published a tile");
        int startCommitted = FarDetail.Committed;
        for (int n = 0; n < 7500; n++) Frame(streamer, new Vector2(6000f + n * 160f / 60f, 7000f), drone, true);
        for (int n = 0; n < 900; n++) Frame(streamer, new Vector2(26000f - n * 160f / 60f, 7000f + n * 80f / 60f), drone, true);
        int flightMisses = misses;
        for (int n = 0; n < 1200; n++) Frame(streamer, new Vector2(-16000f, -15000f), drone, n > 600);
        int totalCommitted = FarDetail.Committed;
        // Clear during an actual in-flight transaction. Its task may finish, but its result must never be published.
        Frame(streamer, new Vector2(15000f, -15000f), drone, false);
        FarDetail.Clear();
        if (FarDetail.Live != 0) throw new Exception("World reset retained live tiles");
        Register(heights); FarDetail.Initialise(host.transform);
        for (int n = 0; n < 500; n++) Frame(streamer, new Vector2(15000f, -15000f), drone, false);
        if (!FarDetail.TryHeight(new Vector2(15000f, -15000f), out _)) throw new Exception("Rebuilt sector never populated the new camera tile");
        DroneFeed.Instance.Source = null;
        for (int n = 0; n < 1250; n++) Frame(streamer, Vector2.zero, drone, false);
        if (FarDetail.Live != 0) throw new Exception("Tiles did not expire after 15 seconds plus staged eviction");
        foreach (var c in FarDetailChunks.All) if (c.group.forced != -1 || c.filter.sharedMesh != c.original)
                throw new Exception("Retired chunk failed to recover its original LODs");
        FarDetail.Clear();
        if (flightMisses > 0) throw new Exception($"4x flight crossed {flightMisses} frames without its camera tile");
        if (misses > 0) throw new Exception($"Teleport failed to refill camera tile: {misses} misses");
        return new
        {
            frames,
            maximumLive = maximum,
            committed = totalCommitted,
            startCommitted,
            cameraTileMisses = flightMisses,
            note = "State-machine simulation. Worker completion is synchronized between frames; virtual 60 Hz and managed uploads do not measure runtime throughput."
        };
    }

    // All real coarse chunks are registered so transactions cross 4 km chunk boundaries during the flight.
    static void Register(float[][] heights)
    {
        for (int z = 0; z < 14; z++) for (int x = 0; x < 14; x++)
            {
                var vertices = new Vector3[65 * 65]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length]; var indices = new int[64 * 64 * 6];
                int cursor = 0;
                for (int j = 0; j < 65; j++) for (int i = 0; i < 65; i++)
                    {
                        int k = j * 65 + i; vertices[k] = new Vector3(i * 62.5f, heights[z * 14 + x][k], j * 62.5f); normals[k] = Vector3.up;
                        if (i == 64 || j == 64) continue;
                        indices[cursor++] = k; indices[cursor++] = k + 65; indices[cursor++] = k + 1; indices[cursor++] = k + 1; indices[cursor++] = k + 65; indices[cursor++] = k + 66;
                    }
                var go = new GameObject("chunk"); var filter = go.AddComponent<MeshFilter>(); var group = go.AddComponent<LODGroup>();
                filter.sharedMesh = new Mesh { vertices = vertices, normals = normals, uv = uv, triangles = indices };
                FarDetailChunks.Register(new Vector2(-28000 + x * 4000, -28000 + z * 4000), filter, group);
            }
    }

    // Each frame checks the live cap, collider radius and tile/chunk publication ownership.
    static void Frame(FarDetail streamer, Vector2 camera, Drone drone, bool checkCoverage)
    {
        Time.unscaledTime += 1f / 60f; Camera.main.transform.position = new Vector3(camera.x, 200f, camera.y);
        drone.transform.position = new Vector3(camera.x + 160f, 150f, camera.y + 160f);
        var task = Worker.GetValue(streamer) as Task; task?.GetAwaiter().GetResult();
        int before = FarDetail.Committed;
        Tick.Invoke(streamer, null);
        if (FarDetail.Committed - before > 1) throw new Exception("More than one tile published in a frame");
        if (FarDetail.Live > FarDetail.MaxTiles) throw new Exception("Live tile hard cap exceeded");
        if (checkCoverage && !FarDetail.TryHeight(camera, out _)) misses++;
        if (checkCoverage && DroneFeed.Instance.Source != null && !FarDetail.TryHeight(new Vector2(drone.transform.position.x, drone.transform.position.z), out _))
            throw new Exception("The followed drone left its detail bubble");
        foreach (DictionaryEntry pair in (IDictionary)Live.GetValue(streamer))
        {
            object s = pair.Value; var type = s.GetType();
            var data = (FarDetailData.Tile)type.GetField("data").GetValue(s);
            if (!born.TryGetValue(s, out var age) || age.data != data) { age = (data, frames); born[s] = age; }
            var collider = (MeshCollider)type.GetField("collider").GetValue(s);
            var go = (GameObject)type.GetField("go").GetValue(s);
            if (!go.active) throw new Exception("A live tile is invisible");
            if (frames - age.frame > 20 && FarDetail.DistanceTo(data.x, data.z, camera) < 400f && !collider.enabled)
                throw new Exception("Collider queue starved a nearby live tile");
            if (FarDetail.Committed != before)
                foreach (var c in FarDetailChunks.All) if (FarDetailChunks.Overlaps(c.corner, new FarDetailChunks.Area { x = data.x, z = data.z }) &&
                    (c.group.forced != 0 || c.filter.sharedMesh != c.front)) throw new Exception("Tile published without its clipped chunks");
        }
        maximum = Math.Max(maximum, FarDetail.Live); frames++;
    }
}

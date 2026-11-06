using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Which 512 m detail tiles the streamer wants, and by when: the camera's ready zone and the followed drone's 600 m
    // zone now, then the tiles along the drone's queued route by the real seconds its zone needs to reach them.
    // Pure data with no engine objects, so the offline harness drives exactly this planner.
    public sealed class FarDetailRoute
    {
        public const float Tile = 512f, Zone = 600f, Bubble = 500f, Reach = 1500f;
        public const float MinLead = 2000f, MaxLead = 3000f, Margin = 3f, Stride = 64f;
        public const int MaxPoints = 16;
        const float Ambient = 1e4f, Unobserved = 2e4f;

        // A wanted tile, its deadline in real seconds, and its class: 0 camera zone, 1 followed drone, 2 camera
        // corridor, 3 camera surroundings, 4 an unobserved drone. The deadline sorts first; the class breaks a tie.
        public struct Request
        {
            public int x, z, rank;
            public float due, distance;
        }

        // A tile the followed eye needs, with the route metres its ready zone covers before reaching the tile.
        public struct Need
        {
            public int x, z;
            public float metres;
        }

        public readonly List<Request> requests = new List<Request>(192);
        public readonly List<Need> needs = new List<Need>(96);
        readonly Dictionary<long, int> requestAt = new Dictionary<long, int>(192);
        readonly Dictionary<long, int> needAt = new Dictionary<long, int>(96);
        readonly Vector2[] points = new Vector2[MaxPoints];
        readonly Vector2[] route = new Vector2[MaxPoints];
        static readonly Comparison<Request> order = Compare;
        readonly Func<int, int, bool> eligible;
        int routeCount, pathCount;

        // Inputs, set before Plan: world metres, and real (unscaled) metres per second.
        public Vector2 cameraEye, cameraFocus, cameraVelocity;
        public bool hasDrone, observed;
        public Vector2 drone, droneVelocity;
        public float droneSpeed;              // cruise speed times the time multiplier, when it has somewhere to go
        public float generationSeconds = 0.4f; // p99 latency from starting a tile to its finished data

        // The followed eye (the drone, else the camera focus), its ground speed and zone, and the metres prefetched.
        public Vector2 Eye { get; private set; }
        public float EyeSpeed { get; private set; }
        public float EyeZone { get; private set; } = Zone;
        public float Lead { get; private set; } = MinLead;
        public int PathCount => pathCount;
        public Vector2 PathPoint(int i) => points[i];

        // Tile eligibility (outside the sector and Backdrop, inside the world) comes from the streamer.
        public FarDetailRoute(Func<int, int, bool> eligible) { this.eligible = eligible; }

        // The drone's move target first, then its queued waypoints, in world metres.
        public void ClearRoute() => routeCount = 0;
        public void AddRoutePoint(Vector2 world) { if (routeCount < MaxPoints - 1) route[routeCount++] = world; }

        // Rebuild the sorted request list, capped at the tile budget, and the followed eye's needs.
        public void Plan(int cap)
        {
            requests.Clear(); requestAt.Clear(); needs.Clear(); needAt.Clear();
            Vector2 velocity = hasDrone ? droneVelocity : cameraVelocity;
            Eye = hasDrone ? drone : cameraFocus;
            EyeSpeed = velocity.magnitude;
            EyeZone = Zone; // the same 600 m as the camera's zone, so a following camera's gaps are ReadyAhead's too
            Disc(cameraFocus, Zone, 0f, 1f, 0f, 0, false);
            Disc(cameraEye, Bubble, 0f, 1f, 0f, 0, false);
            // Distance from speed times the multiplier, p99 generation latency and a 3 s margin, kept to 2-3 km.
            float speed = Mathf.Max(EyeSpeed, hasDrone && routeCount > 0 ? droneSpeed : 0f);
            Lead = Mathf.Clamp(speed * (generationSeconds + Margin), MinLead, MaxLead);
            points[0] = Eye; pathCount = 1;
            if (hasDrone) for (int i = 0; i < routeCount; i++) points[pathCount++] = route[i];
            if (pathCount == 1 && EyeSpeed > 1f) points[pathCount++] = Eye + velocity * (Lead / EyeSpeed);
            int rank = !hasDrone ? 0 : observed ? 1 : 4;
            Walk(Mathf.Max(speed, 1f), rank, rank == 4 ? Unobserved : 0f);
            Corridor();
            requests.Sort(order);
            if (requests.Count > cap) requests.RemoveRange(cap, requests.Count - cap);
        }

        // Real seconds before the followed eye's zone reaches the nearest needed tile that isn't live; infinity when all
        // are. travelled is how far the eye has gone since Plan; zone tiles that are missing return zero.
        public float ReadyAhead(Func<int, int, bool> live, float travelled, float speed)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < needs.Count; i++)
            {
                var need = needs[i];
                if (live(need.x, need.z)) continue;
                float metres = need.metres - travelled;
                if (metres <= 0f) return 0f;
                if (speed > 0.5f) best = Mathf.Min(best, metres / speed);
            }
            return best;
        }

        // Every eligible tile within radius of a point, due when the eye's zone reaches it along the route.
        void Disc(Vector2 at, float radius, float metres, float speed, float offset, int rank, bool need)
        {
            int x0 = Mathf.FloorToInt((at.x - radius) / Tile), x1 = Mathf.FloorToInt((at.x + radius) / Tile);
            int z0 = Mathf.FloorToInt((at.y - radius) / Tile), z1 = Mathf.FloorToInt((at.y + radius) / Tile);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    if (DistanceTo(x, z, at) > radius || !eligible(x, z)) continue;
                    Add(x, z, offset + metres / speed, DistanceTo(x, z, Eye), rank);
                    if (need) AddNeed(x, z, metres);
                }
        }

        // Step along the projected route every 64 m up to the lead; each tile takes its earliest route metre, less
        // one step so sampling never makes a tile look later than it is.
        void Walk(float speed, int rank, float offset)
        {
            Disc(Eye, EyeZone, 0f, speed, offset, rank, true);
            float walked = 0f;
            for (int i = 0; i + 1 < pathCount && walked < Lead; i++)
            {
                Vector2 a = points[i], b = points[i + 1];
                float length = Vector2.Distance(a, b);
                if (length < 1f) continue;
                Vector2 direction = (b - a) / length;
                for (float t = Stride; ; t += Stride)
                {
                    float along = Mathf.Min(t, length);
                    if (walked + along > Lead) return;
                    Disc(a + direction * along, EyeZone, Mathf.Max(0f, walked + along - Stride), speed, offset, rank, true);
                    if (along >= length) break;
                }
                walked += length;
            }
        }

        // The camera's own predicted capsule and surroundings, as before: 1.5 km round it, and 1.5 km ahead of a moving
        // camera, due when its 500 m neighbourhood reaches a tile.
        void Corridor()
        {
            int cx = Mathf.FloorToInt(cameraEye.x / Tile), cz = Mathf.FloorToInt(cameraEye.y / Tile);
            float moving = cameraVelocity.magnitude;
            Vector2 heading = moving > 0.1f ? cameraVelocity / moving : Vector2.zero;
            for (int z = cz - 6; z <= cz + 6; z++) for (int x = cx - 6; x <= cx + 6; x++)
                {
                    if (!eligible(x, z)) continue;
                    float distance = DistanceTo(x, z, cameraEye);
                    Vector2 delta = new Vector2((x + 0.5f) * Tile - cameraEye.x, (z + 0.5f) * Tile - cameraEye.y);
                    float along = Vector2.Dot(delta, heading), side = Mathf.Abs(delta.x * heading.y - delta.y * heading.x);
                    bool ahead = moving > 0.1f && along >= 0f && along <= Reach + 256f && side < Tile;
                    if (distance > Reach && !ahead) continue;
                    float arrival = Arrival(x, z, cameraEye, cameraVelocity);
                    if (arrival < float.MaxValue) Add(x, z, arrival, distance, 2);
                    else Add(x, z, Ambient, distance, 3);
                }
        }

        // Keep the earliest deadline (and its class) and the nearest distance for a tile met more than once.
        void Add(int x, int z, float due, float distance, int rank)
        {
            long key = Key(x, z);
            if (requestAt.TryGetValue(key, out int i))
            {
                var old = requests[i];
                if (due < old.due || due == old.due && rank < old.rank) { old.due = due; old.rank = rank; }
                old.distance = Mathf.Min(old.distance, distance);
                requests[i] = old;
                return;
            }
            requestAt.Add(key, requests.Count);
            requests.Add(new Request { x = x, z = z, due = due, distance = distance, rank = rank });
        }

        // A needed tile keeps the fewest route metres at which the eye's zone first covers it.
        void AddNeed(int x, int z, float metres)
        {
            long key = Key(x, z);
            if (needAt.TryGetValue(key, out int i))
            {
                if (metres < needs[i].metres) needs[i] = new Need { x = x, z = z, metres = metres };
                return;
            }
            needAt.Add(key, needs.Count);
            needs.Add(new Need { x = x, z = z, metres = metres });
        }

        // Deadline, then class, then distance from the followed eye, then a stable key.
        static int Compare(Request a, Request b)
        {
            int due = a.due.CompareTo(b.due);
            if (due != 0) return due;
            if (a.rank != b.rank) return a.rank.CompareTo(b.rank);
            int distance = a.distance.CompareTo(b.distance);
            if (distance != 0) return distance;
            return Key(a.x, a.z).CompareTo(Key(b.x, b.z));
        }

        // Slab intersection returns time until a moving eye enters the tile's protected 500 m neighbourhood.
        public static float Arrival(int x, int z, Vector2 at, Vector2 speed)
        {
            if (DistanceTo(x, z, at) <= Bubble) return 0f;
            float enter = 0f, leave = float.MaxValue;
            if (!Slab(x * Tile - Bubble, (x + 1) * Tile + Bubble, at.x, speed.x, ref enter, ref leave) ||
                !Slab(z * Tile - Bubble, (z + 1) * Tile + Bubble, at.y, speed.y, ref enter, ref leave)) return float.MaxValue;
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

        // Minimum horizontal distance to the tile, so its whole near edge receives priority.
        public static float DistanceTo(int x, int z, Vector2 at)
        {
            float dx = Mathf.Max(x * Tile - at.x, Mathf.Max(0f, at.x - (x + 1) * Tile));
            float dz = Mathf.Max(z * Tile - at.y, Mathf.Max(0f, at.y - (z + 1) * Tile));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Signed world tile coordinates remain collision-free across all four quadrants.
        public static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}

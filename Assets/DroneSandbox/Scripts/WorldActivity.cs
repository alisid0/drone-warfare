using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The kinds of Dravin position out in the theatre, beyond the battle sector.
    public enum WorldGroupKind { AirDefence, Battery, Rockets, Depot, Headquarters, Laager, Checkpoint, Patrol, Convoy }

    // One Dravin unit out in the theatre. Where no drone is near it, it lives here as data: where it is, how hurt it is,
    // what Norvane knows of it. A drone coming within WorldActivity.LiveRadius makes it a real EnemyUnit, which Recon
    // sees and which notices drones and shoots at them like any other, and which writes all that back when they leave.
    public sealed class WorldContact
    {
        public int id;
        public EnemyType type;
        public WorldGroup group;
        public Vector2 position;   // world metres
        public float heading;
        public float health = 1f;  // share of full health
        public bool destroyed;
        // Patrols and convoys: how far along their group's road, and which way they're driving.
        public float along, direction = 1f;
        // What Norvane knows, kept while it's only data (EnemyUnit keeps its own while it's live).
        public Intel intel;
        public Vector2 lastSeen, contactPosition;
        public float lastSeenAt = -1000f, contactRadius, alertedAt = -1000f;
        public int seenTurn;       // the campaign turn it was last seen (0: never)
        // The live unit while a drone is near, when it came alive, and the world origin it was placed with (a sector
        // change moves the origin before its units are cleared away).
        public EnemyUnit live;
        public float liveSince;
        public Vector2 liveOrigin;

        public string Label => $"W-{id:000}";
        public bool Mobile => type == EnemyType.Tank || type == EnemyType.Truck || type == EnemyType.APC || type == EnemyType.Tanker;
        public Intel CurrentIntel => live != null ? live.Intel : intel;
        // Where the map marks it: the last sighting, or a mere contact's rough position.
        public Vector2 MarkerPosition => intel >= Intel.Identified ? lastSeen : contactPosition;
    }

    // A Dravin position: a few units together, parked, dug in or driving a stretch of road.
    public sealed class WorldGroup
    {
        public WorldGroupKind kind;
        public Vector2 centre, seededCentre; // where it is, and where it was seeded (the save keeps the difference)
        public string near;                 // the closest settlement, for the news
        public readonly List<WorldContact> members = new List<WorldContact>();
        public List<Vector2> road;           // patrols and convoys: the stretch they drive up and down
        public float[] distances;            // metres along the road at each of its points
        public float speed;                  // m/s along it
        public float relocateAt = -1f;       // Time.time an alerted group packs up and moves; -1 if it isn't going to
        public int relocations;

        public bool OnRoad => road != null;
        public bool CanRelocate => kind == WorldGroupKind.Laager || kind == WorldGroupKind.Checkpoint;
        public float Length => distances[distances.Length - 1];

        // The point `metres` along the road (held at its ends) and the road's direction there.
        public Vector2 PointAt(float metres, out Vector2 tangent)
        {
            int lo = 0, hi = distances.Length - 1;
            metres = Mathf.Clamp(metres, 0f, Length);
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (distances[mid] <= metres) lo = mid; else hi = mid;
            }
            Vector2 a = road[lo], b = road[hi];
            tangent = (b - a).normalized;
            float span = distances[hi] - distances[lo];
            return span > 0.01f ? Vector2.Lerp(a, b, (metres - distances[lo]) / span) : a;
        }

        // How far along the road its nearest point to p is.
        public float Project(Vector2 p)
        {
            float best = float.MaxValue, at = 0f;
            for (int i = 1; i < road.Count; i++)
            {
                Vector2 a = road[i - 1], ab = road[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
                float d = (a + ab * t - p).sqrMagnitude;
                if (d < best) { best = d; at = distances[i - 1] + t * (distances[i] - distances[i - 1]); }
            }
            return at;
        }
    }

    // Dravin activity across the whole theatre, not only in the battle sector: air defence sites, gun and rocket
    // batteries, the depots and the front command, vehicle parks by the villages, checkpoints at the bridges, and
    // patrols and supply convoys on the roads, seeded from the world plan on Dravin's side of the front. ChatGPT
    // (critique 20): if the 56 km world is "only geographically continuous but tactically empty between mission
    // pockets, players will notice".
    // It costs next to nothing where no drone is:
    // - Out of every drone's reach a unit is data. Patrols and convoys still drive their roads, what Norvane knows
    //   still ages (a vehicle's sighting goes cold, then is forgotten), and an alerted vehicle park packs up and moves,
    //   so a careless look leaves the map wrong.
    // - Within LiveRadius of a drone it becomes a real EnemyUnit, carrying what we knew of it and its alert, so Recon,
    //   AA fire, radar warnings and the rest work as they do everywhere. Past ReleaseRadius of every drone, and
    //   unwatched, it goes back to data. The ground under it needn't be streamed in: it stands on whatever height the
    //   world has there and settles again as the detail arrives.
    // - Nothing comes alive in or near the battle sector, so a mission's own enemies are the only ones in it.
    // - Destroyed units stay destroyed and earn no points: points are for objectives.
    // - In a campaign operation the theatre is the campaign's: saved when the operation ends and aged by the turns
    //   between (see "The campaign" below), and it feeds the sectors around it (Theatre). Free play and the Asset
    //   Hunt missions have the seeded theatre, and nothing they do there is kept.
    public class WorldActivity : MonoBehaviour
    {
        public const float LiveRadius = 1500f, ReleaseRadius = 2000f;
        public const float SectorMargin = 1000f;  // nothing comes alive this close to the battle sector
        public const float ColdSeconds = 600f;    // a vehicle unseen this long drops off the map
        public const int MaxLive = 48;
        const int SpawnsPerFrame = 2;
        const float Tick = 0.5f;
        const float FrontDepth = 1500f;           // everything stands at least this far behind the front

        public static readonly List<WorldGroup> Groups = new List<WorldGroup>();
        public static readonly List<WorldContact> Contacts = new List<WorldContact>();
        static int seededFor = int.MinValue;
        // What it has done this session, for the autotest.
        public static int Spawned, Released, Relocated, LiveCount, PeakLive;
        public static float WorstSpawnMs, SeedMs;
        public static EnemyType WorstSpawnType;

        float timer;
        readonly List<(float distance, WorldContact contact)> waiting = new List<(float, WorldContact)>();
        readonly List<Vector2> watchers = new List<Vector2>();

        // Forgets the theatre, to seed it afresh at the next tick (a new campaign).
        public static void Reset()
        {
            ReleaseAll();
            Groups.Clear();
            Contacts.Clear();
            seededFor = int.MinValue;
            holdsCampaign = false;
            Operation = null;
        }

        void Update()
        {
            var game = SandboxGame.Instance;
            if (game == null || !WorldPlan.Ready) return;
            if (seededFor != WorldPlan.Seed)
            {
                Seed();
                if (Operation != null) Restore();
            }
            if (game.Mode == GameMode.Menu || game.Loading) return;
            timer -= Time.deltaTime; // game time: a tactical pause stops the theatre too
            if (timer <= 0f)
            {
                float elapsed = Tick - timer;
                timer = Tick;
                Advance(elapsed);
                Sort(game);
            }
            Spawn(game);
        }

        // ---- Coming alive and going back to data ----

        // Which units a drone is near enough to bring alive, nearest first, and which live ones it has left behind.
        void Sort(SandboxGame game)
        {
            watchers.Clear();
            foreach (var d in game.drones)
                if (d != null) watchers.Add(new Vector2(d.transform.position.x, d.transform.position.z) + FarTerrain.Origin);
            waiting.Clear();
            LiveCount = 0;
            foreach (var c in Contacts)
            {
                if (c.destroyed) continue;
                if (c.live != null)
                {
                    // Left behind, or a patrol driving into the battle sector, where only the mission's units belong.
                    Vector2 at = Flat(c.live.transform.position) + c.liveOrigin;
                    if ((Nearest(at) > ReleaseRadius || NearSector(at)) && !c.live.Observed) Release(game, c);
                    else
                    {
                        LiveCount++;
                        c.live.Resettle(); // onto the detail ground once it has streamed in
                    }
                    continue;
                }
                c.live = null; // a unit destroyed without word reaching us (cleared with the sector)
                float near = Nearest(c.position);
                if (near < LiveRadius && !NearSector(c.position)) waiting.Add((near, c));
            }
            waiting.Sort((a, b) => a.distance.CompareTo(b.distance));
            PeakLive = Mathf.Max(PeakLive, LiveCount);
        }

        float Nearest(Vector2 world)
        {
            float best = float.MaxValue;
            foreach (var w in watchers) best = Mathf.Min(best, (w - world).sqrMagnitude);
            return Mathf.Sqrt(best);
        }

        // A couple a frame, so a drone arriving over a depot doesn't build it all in one frame.
        void Spawn(SandboxGame game)
        {
            int made = 0;
            while (made < SpawnsPerFrame && waiting.Count > 0 && LiveCount < MaxLive)
            {
                var c = waiting[0].contact;
                waiting.RemoveAt(0);
                if (c.live != null || c.destroyed || NearSector(c.position)) continue;
                float began = Time.realtimeSinceStartup;
                Materialise(game, c);
                float ms = (Time.realtimeSinceStartup - began) * 1000f;
                if (ms > WorstSpawnMs) { WorstSpawnMs = ms; WorstSpawnType = c.type; }
                LiveCount++;
                made++;
            }
        }

        // The contact as a real unit where it stands, knowing what it knew, driving on along its road or parked.
        static void Materialise(SandboxGame game, WorldContact c)
        {
            Vector2 origin = FarTerrain.Origin;
            Vector2 local = c.position - origin;
            var unit = game.SpawnEnemy(c.type, new Vector3(local.x, 0f, local.y), c.heading);
            unit.hp = unit.maxHp * c.health;
            c.live = unit;
            c.liveSince = Time.time;
            c.liveOrigin = origin;
            unit.FromWorld(c, origin);
            var g = c.group;
            if (g.OnRoad)
            {
                // The road ahead the way it's driving; at its end it turns round (EnemyUnit.FollowRoute).
                var path = new List<Vector3>();
                if (c.direction > 0f)
                {
                    for (int i = 0; i < g.road.Count; i++)
                        if (g.distances[i] > c.along) path.Add(Local(g.road[i], origin));
                }
                else
                {
                    for (int i = g.road.Count - 1; i >= 0; i--)
                        if (g.distances[i] < c.along) path.Add(Local(g.road[i], origin));
                }
                if (path.Count == 0) path.Add(new Vector3(local.x, 0f, local.y));
                unit.Drive(path, 0, g.speed);
            }
            else unit.Park();
            Spawned++;
        }

        static Vector3 Local(Vector2 world, Vector2 origin) => Ground.OnGround(new Vector3(world.x - origin.x, 0f, world.y - origin.y));

        // Back to data, with what it has come to. A vehicle park that noticed the drone packs up and moves a few minutes
        // later; in a campaign operation, any position that noticed one is remembered (it puts its sector on guard).
        static void Release(SandboxGame game, WorldContact c)
        {
            var u = c.live;
            c.live = null;
            if (u == null) return;
            WriteBack(c, u);
            var g = c.group;
            if (g.CanRelocate && g.relocateAt < 0f && c.alertedAt >= c.liveSince) g.relocateAt = Time.time + Random.Range(60f, 150f);
            if (Operation != null && c.alertedAt >= c.liveSince) sawUs.Add(g);
            if (game != null) game.enemies.Remove(u);
            Destroy(u.gameObject);
            Released++;
        }

        // What a live unit has come to: where it got to, its health, what we know of it and whether it's on alert.
        static void WriteBack(WorldContact c, EnemyUnit u)
        {
            Vector2 o = c.liveOrigin;
            c.position = Flat(u.transform.position) + o;
            c.heading = u.transform.eulerAngles.y;
            c.health = Mathf.Clamp01(u.hp / u.maxHp);
            c.intel = u.Intel;
            c.lastSeen = Flat(u.LastSeenPosition) + o;
            c.lastSeenAt = u.LastSeenTime;
            c.contactPosition = Flat(u.ContactPosition) + o;
            c.contactRadius = u.ContactRadius;
            c.alertedAt = u.AlertedAt;
            if (c.group.OnRoad) c.along = c.group.Project(c.position);
        }

        // Every live unit back to data, before the game clears its units (a new mode or sector).
        public static void ReleaseAll()
        {
            var game = SandboxGame.Instance;
            foreach (var c in Contacts)
            {
                if (c.live != null) Release(game, c);
                c.live = null;
            }
            LiveCount = 0;
        }

        // A live unit destroyed: it stays that way.
        public static void Destroyed(WorldContact c)
        {
            c.destroyed = true;
            c.health = 0f;
            c.live = null;
            if (Operation != null) hit.Add(c);
        }

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        // In or near the battle sector, where only the mission's own units belong.
        public static bool NearSector(Vector2 world)
        {
            Vector2 local = world - FarTerrain.Origin;
            float reach = Ground.Size * 0.5f + SectorMargin;
            return Mathf.Abs(local.x) < reach && Mathf.Abs(local.y) < reach;
        }

        // ---- The theatre while nobody watches ----

        // Patrols and convoys drive on, what we knew of a vehicle goes stale, and alerted groups move.
        static void Advance(float dt)
        {
            float now = Time.time;
            foreach (var g in Groups)
            {
                if (g.relocateAt > 0f && now >= g.relocateAt && !AnyLive(g)) Relocate(g);
                foreach (var c in g.members)
                {
                    if (c.destroyed || c.live != null) continue;
                    if (g.OnRoad)
                    {
                        c.along += c.direction * g.speed * dt;
                        if (c.along > g.Length) { c.along = Mathf.Max(0f, 2f * g.Length - c.along); c.direction = -1f; }
                        else if (c.along < 0f) { c.along = Mathf.Min(g.Length, -c.along); c.direction = 1f; }
                        c.position = g.PointAt(c.along, out Vector2 tangent);
                        c.heading = Mathf.Atan2(tangent.x * c.direction, tangent.y * c.direction) * Mathf.Rad2Deg;
                    }
                    // A sighting ages as it does for a unit in view (EnemyUnit.Observe); guns and radars don't move.
                    if (!c.Mobile) continue;
                    if (c.intel >= Intel.Identified && now - c.lastSeenAt > Recon.GhostSeconds)
                    {
                        c.intel = Intel.Contact;
                        c.contactPosition = c.lastSeen;
                        c.contactRadius = Scale.D(18f);
                    }
                    else if (c.intel == Intel.Contact && now - c.lastSeenAt > ColdSeconds) c.intel = Intel.Unknown;
                }
            }
        }

        static bool AnyLive(WorldGroup g)
        {
            foreach (var c in g.members) if (c.live != null) return true;
            return false;
        }

        // An alerted vehicle park or checkpoint drives off to a new spot 0.8-1.5 km away. Our marker stays where it was seen.
        static void Relocate(WorldGroup g)
        {
            g.relocateAt = -1f;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector2 shift = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(800f, 1500f);
                bool fits = !NearSector(g.centre + shift) && Depth(g.centre + shift) > FrontDepth;
                foreach (var c in g.members) fits &= c.destroyed || Standable(c.position + shift);
                if (!fits) continue;
                g.centre += shift;
                foreach (var c in g.members) c.position += shift;
                g.relocations++;
                Relocated++;
                return;
            }
        }

        // ---- Seeding ----

        static System.Random rng;

        // The whole theatre's Dravin positions, the same every time for a world seed.
        static void Seed()
        {
            float began = Time.realtimeSinceStartup;
            ReleaseAll();
            Groups.Clear();
            Contacts.Clear();
            seededFor = WorldPlan.Seed;
            rng = new System.Random(unchecked(WorldPlan.Seed * 7919 + 104729));
            foreach (var s in WorldPlan.Sites)
            {
                if (s.kind == WorldSiteKind.EnemyHq) Place(WorldGroupKind.Headquarters, s.position, 500f);
                else if (s.kind == WorldSiteKind.EnemyDepot) Place(WorldGroupKind.Depot, s.position, 500f);
            }
            float edge = WorldPlan.Half - 2500f;
            // Air defence: a belt a few kilometres behind the front, and a thinner line deeper in.
            for (float z = -edge; z < edge; z += 4000f) Place(WorldGroupKind.AirDefence, Behind(z + Range(-1000f, 1000f), Range(2500f, 5000f)), 900f);
            for (float z = -edge + 1500f; z < edge; z += 6000f) Place(WorldGroupKind.AirDefence, Behind(z + Range(-1500f, 1500f), Range(10000f, 14000f)), 1500f);
            // Guns within reach of the front; rockets further back.
            for (float z = -edge + 800f; z < edge; z += 4000f) Place(WorldGroupKind.Battery, Behind(z + Range(-1200f, 1200f), Range(3000f, 7000f)), 1000f);
            for (float z = -edge + 3000f; z < edge; z += 8000f) Place(WorldGroupKind.Rockets, Behind(z + Range(-2000f, 2000f), Range(6000f, 10000f)), 1500f);
            // Checkpoints at the road bridges on Dravin's side.
            int checkpoints = 0;
            foreach (var b in WorldPlan.Bridges)
            {
                if (checkpoints >= 12 || b.railway || Depth(b.position) < FrontDepth) continue;
                Vector2 off = (b.b - b.a).normalized * 90f; // off the deck, back from the far bank
                if (Place(WorldGroupKind.Checkpoint, b.b + off, 80f, 600f)) checkpoints++;
            }
            // Vehicle parks at the edges of the villages and towns, and out in the farmland.
            foreach (var s in WorldPlan.Settlements)
            {
                if (Depth(s.center) < FrontDepth + 500f) continue;
                float a = Range(0f, Mathf.PI * 2f);
                Place(WorldGroupKind.Laager, s.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (s.radius + 250f), 300f);
            }
            for (int k = 0; k < 16; k++) Place(WorldGroupKind.Laager, Behind(Range(-edge, edge), Range(3000f, 18000f)), 1500f);
            PlaceOnRoads();
            SeedMs = (Time.realtimeSinceStartup - began) * 1000f;
        }

        static float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        // How far behind the front (east of it, on Dravin's side) a point is; negative on Norvane's.
        static float Depth(Vector2 p) => p.x - WorldPlan.FrontAt(p.y).x;

        static Vector2 Behind(float z, float depth) => new Vector2(WorldPlan.FrontAt(z).x + depth, z);

        // Open, dry, gentle ground inside the theatre, clear of woods, houses, compounds and water.
        static bool Standable(Vector2 p)
        {
            if (Mathf.Abs(p.x) > WorldPlan.Half - 500f || Mathf.Abs(p.y) > WorldPlan.Half - 500f) return false;
            var cover = WorldPlan.CoverAt(p.x, p.y);
            if (cover != Cover.Pasture && cover != Cover.Farmland && cover != Cover.Rock) return false;
            if (WorldPlan.NearWater(p, 40f) || WorldPlan.FeatureReserved(p, 30f)) return false;
            const float step = 16f;
            float dx = WorldPlan.Height(p.x + step, p.y) - WorldPlan.Height(p.x - step, p.y);
            float dz = WorldPlan.Height(p.x, p.y + step) - WorldPlan.Height(p.x, p.y - step);
            return Mathf.Sqrt(dx * dx + dz * dz) / (2f * step) < 0.12f;
        }

        // A group of this kind somewhere near `around`, spaced from the others; false if there's no room for it.
        static bool Place(WorldGroupKind kind, Vector2 around, float spread, float spacing = 1200f)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                float a = Range(0f, Mathf.PI * 2f), r = spread * Mathf.Sqrt((float)rng.NextDouble());
                Vector2 p = around + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (Depth(p) < FrontDepth || !Standable(p)) continue;
                bool crowded = false;
                foreach (var other in Groups) if (!other.OnRoad && (other.centre - p).sqrMagnitude < spacing * spacing) { crowded = true; break; }
                if (crowded) continue;
                var g = NewGroup(kind, p);
                float yaw = Range(0f, 360f);
                foreach (var (type, offset) in Formation(kind))
                {
                    Vector2 at = p + Rotate(offset, yaw);
                    if (!Standable(at)) continue;
                    // Guns, radars and launchers face the front (west); parked vehicles any way.
                    bool vehicle = type == EnemyType.Tank || type == EnemyType.Truck || type == EnemyType.APC || type == EnemyType.Tanker;
                    Add(g, type, at, vehicle ? Range(0f, 360f) : 270f + Range(-30f, 30f));
                }
                if (g.members.Count == 0) { Groups.Remove(g); continue; }
                return true;
            }
            return false;
        }

        static WorldGroup NewGroup(WorldGroupKind kind, Vector2 centre)
        {
            var g = new WorldGroup { kind = kind, centre = centre, seededCentre = centre, near = NearestPlace(centre) };
            Groups.Add(g);
            return g;
        }

        static WorldContact Add(WorldGroup g, EnemyType type, Vector2 at, float heading)
        {
            var c = new WorldContact { id = Contacts.Count + 1, type = type, group = g, position = at, heading = heading };
            g.members.Add(c);
            Contacts.Add(c);
            return c;
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            return new Vector2(v.x * cos + v.y * sin, -v.x * sin + v.y * cos);
        }

        // Who stands where in each kind of position, in metres from its centre.
        static (EnemyType, Vector2)[] Formation(WorldGroupKind kind)
        {
            switch (kind)
            {
                case WorldGroupKind.AirDefence:
                    return rng.NextDouble() < 0.25
                        ? new[] { (EnemyType.AAGun, new Vector2(-60f, 0f)), (EnemyType.AAGun, new Vector2(60f, 20f)), (EnemyType.Radar, new Vector2(0f, -50f)), (EnemyType.Jammer, new Vector2(20f, 90f)) }
                        : new[] { (EnemyType.AAGun, new Vector2(-60f, 0f)), (EnemyType.AAGun, new Vector2(60f, 20f)), (EnemyType.Radar, new Vector2(0f, -50f)) };
                case WorldGroupKind.Battery:
                    return new[] { (EnemyType.Artillery, new Vector2(-35f, 0f)), (EnemyType.Artillery, new Vector2(0f, 4f)), (EnemyType.Artillery, new Vector2(35f, 0f)), (EnemyType.Truck, new Vector2(0f, -70f)) };
                case WorldGroupKind.Rockets:
                    return new[] { (EnemyType.Rocket, new Vector2(-30f, 0f)), (EnemyType.Rocket, new Vector2(30f, 10f)), (EnemyType.Tanker, new Vector2(0f, -60f)) };
                case WorldGroupKind.Depot:
                    return new[] { (EnemyType.Depot, new Vector2(-25f, 0f)), (EnemyType.Depot, new Vector2(25f, 0f)), (EnemyType.AAGun, new Vector2(0f, 90f)),
                        (EnemyType.Truck, new Vector2(-50f, -40f)), (EnemyType.Tanker, new Vector2(50f, -40f)) };
                case WorldGroupKind.Headquarters:
                    return new[] { (EnemyType.AAGun, new Vector2(-100f, 0f)), (EnemyType.AAGun, new Vector2(100f, 30f)), (EnemyType.Radar, new Vector2(0f, 80f)),
                        (EnemyType.APC, new Vector2(-20f, -40f)), (EnemyType.Truck, new Vector2(25f, -45f)) };
                case WorldGroupKind.Laager:
                    return rng.NextDouble() < 0.5
                        ? new[] { (EnemyType.Tank, new Vector2(-12f, 0f)), (EnemyType.Tank, new Vector2(12f, 4f)), (EnemyType.APC, new Vector2(0f, -20f)) }
                        : new[] { (EnemyType.Truck, new Vector2(-10f, 0f)), (EnemyType.Truck, new Vector2(10f, 0f)), (EnemyType.Tanker, new Vector2(0f, -18f)) };
                default: // a checkpoint
                    return new[] { (EnemyType.APC, new Vector2(0f, 0f)), (EnemyType.Truck, new Vector2(6f, -16f)) };
            }
        }

        // Patrols (an APC and a tank) and supply convoys (two trucks and a tanker) driving 2.5-6 km stretches of the
        // roads and highways that run deep enough behind the front.
        static void PlaceOnRoads()
        {
            var stretches = new List<List<Vector2>>();
            foreach (var road in WorldPlan.Roads)
            {
                if (road.kind == RoadClass.Track) continue;
                List<Vector2> run = null;
                foreach (var p in road.points)
                {
                    bool deep = Depth(p) > FrontDepth && Mathf.Abs(p.x) < WorldPlan.Half - 500f && Mathf.Abs(p.y) < WorldPlan.Half - 500f;
                    if (deep) (run ??= new List<Vector2>()).Add(p);
                    else { Cut(run, stretches); run = null; }
                }
                Cut(run, stretches);
            }
            // Shuffled, the same way for the seed.
            for (int i = stretches.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (stretches[i], stretches[j]) = (stretches[j], stretches[i]);
            }
            // Checkpoints beside the road where a dozen of the stretches begin.
            int roadside = 0;
            for (int i = 0; i < stretches.Count && roadside < 12; i += 2)
            {
                Vector2 a = stretches[i][0], ahead = stretches[i][Mathf.Min(1, stretches[i].Count - 1)] - a;
                Vector2 side = new Vector2(-ahead.y, ahead.x).normalized * 25f;
                if (Place(WorldGroupKind.Checkpoint, a + side, 15f, 600f)) roadside++;
            }
            int patrols = 0, convoys = 0;
            foreach (var road in stretches)
            {
                bool convoy = patrols >= 14;
                if (convoy && convoys >= 8) break;
                var g = NewGroup(convoy ? WorldGroupKind.Convoy : WorldGroupKind.Patrol, road[road.Count / 2]);
                g.road = road;
                g.distances = new float[road.Count];
                for (int i = 1; i < road.Count; i++) g.distances[i] = g.distances[i - 1] + Vector2.Distance(road[i - 1], road[i]);
                g.speed = convoy ? 0.7f * 5.5f * Scale.Speed : 0.5f * 4.5f * Scale.Speed; // trucks' and APCs' cruising pace, eased
                float start = Range(0f, g.Length), direction = rng.NextDouble() < 0.5 ? -1f : 1f;
                var types = convoy ? new[] { EnemyType.Truck, EnemyType.Truck, EnemyType.Tanker } : new[] { EnemyType.APC, EnemyType.Tank };
                for (int k = 0; k < types.Length; k++)
                {
                    float along = Mathf.Clamp(start - direction * k * (convoy ? 30f : 40f), 0f, g.Length);
                    Vector2 at = g.PointAt(along, out Vector2 tangent);
                    var c = Add(g, types[k], at, Mathf.Atan2(tangent.x * direction, tangent.y * direction) * Mathf.Rad2Deg);
                    c.along = along;
                    c.direction = direction;
                }
                if (convoy) convoys++; else patrols++;
            }
        }

        // A deep run of road cut into stretches of at most 6 km, keeping those of 2.5 km or more.
        static void Cut(List<Vector2> run, List<List<Vector2>> stretches)
        {
            if (run == null || run.Count < 2) return;
            var piece = new List<Vector2> { run[0] };
            float length = 0f;
            for (int i = 1; i < run.Count; i++)
            {
                length += Vector2.Distance(run[i - 1], run[i]);
                piece.Add(run[i]);
                if (length >= 6000f)
                {
                    stretches.Add(piece);
                    piece = new List<Vector2> { run[i] };
                    length = 0f;
                }
            }
            if (length >= 2500f) stretches.Add(piece);
        }

        static string NearestPlace(Vector2 p)
        {
            string name = "the front";
            float best = float.MaxValue;
            foreach (var s in WorldPlan.Settlements)
            {
                float d = (s.center - p).sqrMagnitude;
                if (d < best) { best = d; name = s.name; }
            }
            return name;
        }

        // ---- The campaign ----

        // The campaign operation the theatre is loaded for, and when it began (Time.time); null in free play and the
        // Asset Hunt missions.
        public static Hotspot Operation { get; private set; }
        public static float OperationStart { get; private set; }
        public const float SectorReach = 8000f;  // a position counts for the nearest open sector this close to its centre
        static bool holdsCampaign;               // the theatre in memory is the campaign's, not the seeded one
        static readonly HashSet<WorldGroup> sawUs = new HashSet<WorldGroup>(); // positions that noticed our drones this operation
        static readonly List<WorldContact> hit = new List<WorldContact>();     // units destroyed this operation
        static List<(Hotspot spot, Vector2 centre)> spots;
        static List<Hotspot> spotsFor;

        // What a campaign operation did out in the theatre, for Theatre.
        public sealed class Report
        {
            public readonly List<Hotspot> seenFrom = new List<Hotspot>(); // sectors whose Dravin noticed our drones
            public bool supplyHit;  // a depot or supply convoy near the announced threat
            public int mapped, destroyed; // positions with something identified this operation, and units destroyed
        }

        // A campaign operation begins: at the next tick the theatre is seeded afresh and given what the campaign saved.
        public static void BeginOperation(Hotspot h)
        {
            ReleaseAll();
            Operation = h;
            OperationStart = Time.time;
            sawUs.Clear();
            hit.Clear();
            seededFor = int.MinValue;
            holdsCampaign = true;
        }

        // Free play or an Asset Hunt mission: the seeded theatre again, if the campaign's is in memory.
        public static void Fresh()
        {
            Operation = null;
            if (holdsCampaign) Reset();
        }

        // The operation is over (won, lost or abandoned): what it did, and the theatre saved with the campaign. Live
        // units stay where they are until the game clears them; what they know is written back first. Null if the
        // theatre wasn't loaded for this operation.
        public static Report EndOperation(Hotspot h)
        {
            if (Operation == null || Operation != h || seededFor != WorldPlan.Seed) return null;
            foreach (var c in Contacts)
            {
                if (c.live == null) continue;
                WriteBack(c, c.live);
                if (c.alertedAt >= c.liveSince) sawUs.Add(c.group);
            }
            var report = new Report { destroyed = hit.Count };
            foreach (var g in sawUs)
            {
                var spot = SectorOf(g);
                if (spot != null && !report.seenFrom.Contains(spot)) report.seenFrom.Add(spot);
            }
            var threat = Theatre.ThreatSpot;
            foreach (var c in hit)
                if (threat != null && Supply(c.group) && SectorOf(c.group) == threat) report.supplyHit = true;
            var mapped = new HashSet<WorldGroup>();
            foreach (var c in Contacts)
                if (!c.destroyed && c.lastSeenAt >= OperationStart && c.intel >= Intel.Identified) mapped.Add(c.group);
            report.mapped = mapped.Count;
            Save();
            Operation = null;
            return report;
        }

        static bool Supply(WorldGroup g) => g.kind == WorldGroupKind.Depot || g.kind == WorldGroupKind.Convoy;

        // The open campaign sector a position counts for: the nearest not yet won, within SectorReach of its centre.
        public static Hotspot SectorOf(WorldGroup g)
        {
            var roster = Theatre.Hotspots;
            if (spotsFor != roster)
            {
                spotsFor = roster;
                try { spots = WorldSectors.AllSpots(roster); }
                catch (System.InvalidOperationException) { spots = new List<(Hotspot, Vector2)>(); }
            }
            Hotspot best = null;
            float nearest = SectorReach * SectorReach;
            foreach (var (spot, centre) in spots)
            {
                if (Theatre.Done(spot)) continue;
                float d = (centre - g.centre).sqrMagnitude;
                if (d < nearest) { nearest = d; best = spot; }
            }
            return best;
        }

        // For the theatre map, saved with the campaign: each open sector's mapped positions (a gun, radar, launcher or
        // depot still standing and identified) and its supply (depots and convoys with something left).
        public static void Tally()
        {
            foreach (var h in Theatre.Hotspots)
            {
                Progress.Set(h.Key + ".mapped", 0);
                Progress.Set(h.Key + ".supply", 0);
            }
            foreach (var g in Groups)
            {
                var spot = SectorOf(g);
                if (spot == null) continue;
                bool mapped = false, standing = false;
                foreach (var c in g.members)
                {
                    if (c.destroyed) continue;
                    standing = true;
                    mapped |= !c.Mobile && c.intel >= Intel.Identified;
                }
                if (mapped) Progress.Set(spot.Key + ".mapped", Progress.Get(spot.Key + ".mapped") + 1);
                if (standing && Supply(g)) Progress.Set(spot.Key + ".supply", Progress.Get(spot.Key + ".supply") + 1);
            }
        }

        static int Layout => Contacts.Count * 1000 + Groups.Count; // a save from a different seeding doesn't fit

        // The theatre into the campaign's save, as its changes from the seeded one. Per unit: destroyed, health, what we
        // know and the turn it was last seen, packed in one number, and where a vehicle was last seen. Per position: how
        // far it has moved, and the turn it began packing up to move.
        static void Save()
        {
            Progress.Forget("campaign.world.");
            Progress.Set("campaign.world.layout", Layout);
            int turn = Theatre.Turn;
            foreach (var c in Contacts)
            {
                if (c.lastSeenAt >= OperationStart) c.seenTurn = turn;
                if (!c.destroyed && c.intel == Intel.Unknown && c.health >= 0.995f) continue;
                string key = "campaign.world." + c.id;
                Progress.Set(key, c.seenTurn * 1024 + Mathf.RoundToInt(c.health * 100f) * 8 + (int)c.intel * 2 + (c.destroyed ? 1 : 0));
                if (c.Mobile && c.intel != Intel.Unknown)
                {
                    Progress.Set(key + ".x", Mathf.RoundToInt(c.MarkerPosition.x));
                    Progress.Set(key + ".z", Mathf.RoundToInt(c.MarkerPosition.y));
                }
            }
            for (int i = 0; i < Groups.Count; i++)
            {
                var g = Groups[i];
                Vector2 shift = g.centre - g.seededCentre;
                if (shift.sqrMagnitude > 1f)
                {
                    Progress.Set($"campaign.world.g{i}.x", Mathf.RoundToInt(shift.x));
                    Progress.Set($"campaign.world.g{i}.z", Mathf.RoundToInt(shift.y));
                }
                if (g.relocateAt > 0f) Progress.Set($"campaign.world.g{i}.move", turn);
            }
        }

        // The campaign's theatre: the seeded one with the save applied, aged by the turns since. Guns, radars and depots
        // stay mapped; a vehicle seen in an earlier operation is only a rough contact where it was, and forgotten after
        // two turns. A position that was packing up has moved, and the patrols and convoys are elsewhere on their roads.
        static void Restore()
        {
            int turn = Theatre.Turn;
            bool saved = Progress.Get("campaign.world.layout") == Layout;
            foreach (var c in Contacts)
            {
                if (!saved) break;
                string key = "campaign.world." + c.id;
                int v = Progress.Get(key, -1);
                if (v < 0) continue;
                c.destroyed = (v & 1) != 0;
                c.health = c.destroyed ? 0f : (v >> 3 & 127) / 100f;
                c.seenTurn = v >> 10;
                var intel = (Intel)(v >> 1 & 3);
                if (c.destroyed || intel == Intel.Unknown) continue;
                c.contactRadius = Scale.D(18f);
                if (!c.Mobile)
                {
                    c.intel = intel;
                    c.lastSeen = c.contactPosition = c.position; // guns, radars and depots don't move
                    c.lastSeenAt = OperationStart - 1f;
                }
                else if (turn - c.seenTurn <= 1)
                {
                    c.intel = Intel.Contact;
                    c.lastSeen = c.contactPosition = new Vector2(Progress.Get(key + ".x"), Progress.Get(key + ".z"));
                    c.lastSeenAt = OperationStart - Recon.GhostSeconds - 1f;
                }
            }
            for (int i = 0; i < Groups.Count; i++)
            {
                var g = Groups[i];
                if (saved)
                {
                    var shift = new Vector2(Progress.Get($"campaign.world.g{i}.x"), Progress.Get($"campaign.world.g{i}.z"));
                    g.centre += shift;
                    foreach (var c in g.members) c.position += shift;
                    int move = Progress.Get($"campaign.world.g{i}.move");
                    if (move > 0 && move < turn) Relocate(g);
                    else if (move > 0) g.relocateAt = OperationStart + Random.Range(60f, 150f); // a won sector replayed: still packing up
                }
                if (g.OnRoad) Wander(g, i, turn);
            }
        }

        // A patrol or convoy in a new turn: somewhere else along its road, the same place for the same turn.
        static void Wander(WorldGroup g, int index, int turn)
        {
            var r = new System.Random(unchecked(Theatre.Seed * 31 + turn * 7919 + index));
            float start = (float)r.NextDouble() * g.Length, direction = r.NextDouble() < 0.5 ? -1f : 1f;
            for (int k = 0; k < g.members.Count; k++)
            {
                var c = g.members[k];
                c.along = Mathf.Clamp(start - direction * k * (g.kind == WorldGroupKind.Convoy ? 30f : 40f), 0f, g.Length);
                c.direction = direction;
                c.position = g.PointAt(c.along, out Vector2 tangent);
                c.heading = Mathf.Atan2(tangent.x * direction, tangent.y * direction) * Mathf.Rad2Deg;
            }
        }

        // ---- For the map and the autotest ----

        // A unit's name while it's only data (EnemyUnit sets its own).
        public static string Name(EnemyType t) => t switch
        {
            EnemyType.AAGun => "AA Gun", EnemyType.Command => "Command post", EnemyType.Rocket => "Rocket launcher",
            EnemyType.Tanker => "Fuel tanker", EnemyType.Depot => "Ammo depot", _ => t.ToString(),
        };

        // How many of each kind of position, and of units, for the log.
        public static string Summary()
        {
            var counts = new int[System.Enum.GetValues(typeof(WorldGroupKind)).Length];
            foreach (var g in Groups) counts[(int)g.kind]++;
            var text = new System.Text.StringBuilder($"{Groups.Count} groups, {Contacts.Count} units (seeded in {SeedMs:F1} ms):");
            for (int i = 0; i < counts.Length; i++) text.Append($" {(WorldGroupKind)i} {counts[i]}");
            return text.ToString();
        }
    }
}

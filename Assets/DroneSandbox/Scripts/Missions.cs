using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // One enemy placed by a mission.
    public struct MissionUnit
    {
        public EnemyType type;
        public Vector3 position;
        public float yaw;
        public bool target;  // counts towards winning
        public bool hidden;  // invisible until one of your drones comes close (see SandboxGame.SpotRange)
        public int convoy;   // > 0: drives the mission's convoy route, this many places back from the lead
        public bool decoy;   // an inflatable fake: worth nothing, and a drone spent on it is wasted
    }

    // An Asset Hunt mission: a list of targets to destroy with a fixed stock of drones.
    public class Mission
    {
        public string name, briefing;
        public int scouts, strikes, kamikazes;
        public int par;                    // drones used for three stars
        public float startHour = 12f;      // time of day the mission starts (0-24)
        public Vector3 cameraFocus;
        public float cameraYaw = 40f, cameraDistance = 80f;
        public List<MissionUnit> units = new List<MissionUnit>();
        public List<Vector3> convoyRoute;  // where convoy units drive; a target reaching its end fails the mission
        public string escapeFailure = "A target got away.";
        public float shadowSeconds;        // > 0: a Shadow mission - watch the targets this long, unseen, instead of destroying them
        public string key;                 // where its best stars are saved (the Asset Hunt missions use their number)
        public float contactRadius = 14f;  // how roughly the briefing places the vehicles (metres)

        // The mission's own drones (the tech shop can add more; see SandboxGame.MissionStock).
        public int Stock(DroneType type)
        {
            switch (type)
            {
                case DroneType.Scout: return scouts;
                case DroneType.Strike: return strikes;
                case DroneType.Kamikaze: return kamikazes;
                default: return 0;
            }
        }
    }

    // The Asset Hunt missions, in play order. Each one adds one new idea: hitting targets, kill order,
    // finding hidden targets, catching a moving convoy, then breaking a defended position.
    public static class Missions
    {
        static List<Mission> all;

        public static List<Mission> All
        {
            get
            {
                if (all == null) all = Build();
                return all;
            }
        }

        static List<Mission> Build()
        {
            MapLayout.Prepare();
            var list = new List<Mission>();

            // The teaching mission, in the three phases every mission now has: find the trucks (they're only rough
            // contacts), get the strike past a known AA gun, then bring the scouts home.
            var firstLight = new Mission
            {
                name = "First Light",
                briefing = "Four reports of Dravin supply trucks parked along the south and east roads, and at least one is an inflatable " +
                           "decoy. Get a Scout [1] over each grey circle and watch a moment to be sure, then spend Kamikazes [3] only on " +
                           "real trucks. A known AA gun covers the direct route. Bring the Scouts home.",
                scouts = 2, strikes = 0, kamikazes = 4, par = 5, startHour = 5.7f, contactRadius = 30f,
                cameraFocus = new Vector3(-28f, 0f, -48f), cameraYaw = 70f, cameraDistance = 85f,
            };
            // Three sites well apart (north by the crossroads, far down the south road, out on the east road) and a
            // decoy south-west of them.
            Add(firstLight, EnemyType.Truck, 12f, -36f, 30f, target: true);
            Add(firstLight, EnemyType.Truck, 24f, -84f, 190f, target: true);
            Add(firstLight, EnemyType.Truck, 62f, -44f, 240f, target: true);
            Add(firstLight, EnemyType.Truck, -4f, -90f, 160f, decoy: true);
            Add(firstLight, EnemyType.AAGun, -20f, -47f, 120f);
            list.Add(firstLight);

            var openSky = new Mission
            {
                name = "Open the Sky",
                briefing = "A Dravin AA gun guards the crossroads, a radar beside it stretches the gun's reach, and two tanks are " +
                           "parked under their cover. Destroy all four. A Scout's gun can't hurt armour: kill the radar, then the " +
                           "Strike drones outrange the gun - or risk Kamikazes through its fire.",
                scouts = 2, strikes = 2, kamikazes = 3, par = 5, startHour = 12.5f,
                cameraFocus = new Vector3(-35f, 0f, -35f), cameraYaw = 55f, cameraDistance = 80f,
            };
            Add(openSky, EnemyType.AAGun, -10f, -5f, 200f, target: true);
            Add(openSky, EnemyType.Radar, 2f, 6f, 0f, target: true);
            Add(openSky, EnemyType.Truck, -1f, 15f, 120f);
            Add(openSky, EnemyType.Tank, 4f, -14f, 200f, target: true);
            Add(openSky, EnemyType.Tank, -3f, -20f, 230f, target: true);
            list.Add(openSky);

            var hiddenGuns = new Mission
            {
                name = "Hidden Guns",
                briefing = "Three Dravin guns under camouflage nets are shelling Forward Base Kestrel. They can't be seen until " +
                           "a drone flies close. Search the yellow circles with Scouts, then strike what you find. " +
                           "An AA gun covers the north.",
                scouts = 3, strikes = 2, kamikazes = 4, par = 5, startHour = 20f,
                cameraFocus = new Vector3(-20f, 0f, 0f), cameraYaw = 30f, cameraDistance = 105f,
            };
            Add(hiddenGuns, EnemyType.Artillery, -30f, 32f, 150f, target: true, hidden: true);
            Add(hiddenGuns, EnemyType.Artillery, 6f, 62f, 190f, target: true, hidden: true);
            Add(hiddenGuns, EnemyType.Artillery, 52f, 8f, 230f, target: true, hidden: true);
            Add(hiddenGuns, EnemyType.AAGun, -12f, 48f, 180f);
            list.Add(hiddenGuns);

            var convoy = new Mission
            {
                name = "Convoy",
                briefing = "Four Dravin fuel trucks are driving up the east road from Marrowfield to their command post, led by a tank. " +
                           "Stop every truck before it arrives. It is night: a Scout with the Wren-T thermal camera, or a Nightjar, " +
                           "lets you press T for thermal view (both are in the Tech shop). " +
                           "An AA gun watches the road junction.",
                scouts = 2, strikes = 2, kamikazes = 5, par = 5, startHour = 23f,
                cameraFocus = new Vector3(20f, 0f, -35f), cameraYaw = 60f, cameraDistance = 100f,
                escapeFailure = "A fuel truck reached the Dravin command post.",
            };
            // The east road in from the far south-east to the junction, then the main road up to the HQ.
            convoy.convoyRoute = RoadPath(2, new Vector2(140f, -114f), new Vector2(18f, 0f));
            convoy.convoyRoute.AddRange(RoadPath(0, new Vector2(18f, 0f), new Vector2(36f, 32f)));
            convoy.units.Add(new MissionUnit { type = EnemyType.Tank, convoy = 1 });
            for (int i = 0; i < 4; i++) convoy.units.Add(new MissionUnit { type = EnemyType.Truck, target = true, convoy = i + 2 });
            Add(convoy, EnemyType.AAGun, 30f, -30f, 40f);
            list.Add(convoy);

            var commandPost = new Mission
            {
                name = "Command Post",
                briefing = "Destroy the Dravin Command Post building. A radar and three AA guns defend the compound: " +
                           "open a way in before you send the heavy hitters.",
                scouts = 2, strikes = 4, kamikazes = 6, par = 8, startHour = 4.4f,
                cameraFocus = new Vector3(10f, 0f, 10f), cameraYaw = 40f, cameraDistance = 95f,
            };
            commandPost.units.Add(new MissionUnit { type = EnemyType.Command, target = true });
            foreach (var d in MapLayout.FixedDefences)
                if (Vector3.Distance(d.position, MapLayout.EnemyHqPosition) < 20f)
                    commandPost.units.Add(new MissionUnit { type = d.type, position = d.position });
            list.Add(commandPost);

            // A Shadow mission: learn where a column is going without being seen. Watching it counts only while it
            // hasn't noticed you (stay outside its 26 m but inside your 46 m); destroying any of it fails.
            var longWatch = new Mission
            {
                name = "Long Watch",
                briefing = "A Dravin column is moving up the east road to their command post. We need its route and escort, not a fight: " +
                           "keep a drone watching it for a minute in all, without being seen. Hang back outside its 26 m, inside your 46 m. " +
                           "If they notice you, the watch pauses until they settle. Don't shoot. Bring the drones home after.",
                scouts = 3, strikes = 0, kamikazes = 0, par = 3, startHour = 13f, shadowSeconds = 60f,
                cameraFocus = new Vector3(40f, 0f, -40f), cameraYaw = 60f, cameraDistance = 100f,
                escapeFailure = "The column reached the command post before we had watched it long enough.",
            };
            longWatch.convoyRoute = RoadPath(2, new Vector2(140f, -114f), new Vector2(18f, 0f));
            longWatch.convoyRoute.AddRange(RoadPath(0, new Vector2(18f, 0f), new Vector2(36f, 32f)));
            longWatch.units.Add(new MissionUnit { type = EnemyType.APC, convoy = 1, target = true });
            for (int i = 0; i < 3; i++) longWatch.units.Add(new MissionUnit { type = EnemyType.Truck, target = true, convoy = i + 2 });
            Add(longWatch, EnemyType.AAGun, 30f, -30f, 40f);
            list.Add(longWatch);

            return list;
        }

        static void Add(Mission m, EnemyType type, float x, float z, float yaw, bool target = false, bool hidden = false, bool decoy = false)
        {
            m.units.Add(new MissionUnit { type = type, position = new Vector3(x, 0f, z), yaw = yaw, target = target, hidden = hidden, decoy = decoy });
        }

        // The stretch of a road between the points nearest `from` and `to`, in driving order, on the ground.
        static List<Vector3> RoadPath(int road, Vector2 from, Vector2 to)
        {
            var line = MapLayout.RoadLines[road];
            int a = Nearest(line, from), b = Nearest(line, to);
            var path = new List<Vector3>();
            int step = a <= b ? 1 : -1;
            for (int i = a; i != b + step; i += step) path.Add(new Vector3(line[i].x, 0f, line[i].y));
            return path;
        }

        static int Nearest(List<Vector2> line, Vector2 p)
        {
            int best = 0;
            for (int i = 1; i < line.Count; i++)
                if ((line[i] - p).sqrMagnitude < (line[best] - p).sqrMagnitude) best = i;
            return best;
        }
    }
}

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
                briefing = "Villagers report Dravin supply trucks parked somewhere along the south road: the grey circles are all we know. " +
                           "Find them [1 Scout], destroy them [3 Kamikaze], and bring the Scouts home. A known AA gun covers the direct route.",
                scouts = 2, strikes = 0, kamikazes = 4, par = 5, startHour = 5.7f, contactRadius = 30f,
                cameraFocus = new Vector3(-28f, 0f, -48f), cameraYaw = 70f, cameraDistance = 85f,
            };
            Add(firstLight, EnemyType.Truck, 14f, -35f, 30f, target: true);
            Add(firstLight, EnemyType.Truck, 18f, -41f, 45f, target: true);
            Add(firstLight, EnemyType.Truck, 24f, -80f, 190f, target: true); // a second stop further down the road
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

            return list;
        }

        static void Add(Mission m, EnemyType type, float x, float z, float yaw, bool target = false, bool hidden = false)
        {
            m.units.Add(new MissionUnit { type = type, position = new Vector3(x, 0f, z), yaw = yaw, target = target, hidden = hidden });
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

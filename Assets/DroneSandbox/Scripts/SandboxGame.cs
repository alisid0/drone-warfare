using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Builds the sandbox world (terrain, scenery, player base, enemy forces) and tracks every unit in it.
    // There is no win or lose yet: destroyed enemies respawn, and you can launch drones any time.
    public class SandboxGame : MonoBehaviour
    {
        public const int MaxDrones = 30;
        public const float MapHalfSize = 90f;
        const float EnemyRespawnSeconds = 30f;

        public static SandboxGame Instance { get; private set; }

        public readonly List<Drone> drones = new List<Drone>();
        public readonly List<EnemyUnit> enemies = new List<EnemyUnit>();
        public int enemiesDestroyed, dronesLost, dronesExpended;

        public readonly Vector3 basePosition = new Vector3(-65f, 0f, -65f);
        public readonly Vector3 enemyHqPosition = new Vector3(45f, 0f, 45f);

        // Enemy vehicles spawn and patrol inside this square (x and z in world units).
        readonly Rect enemyArea = new Rect(-35f, -35f, 115f, 115f);

        // Fixed defences. The ground is levelled under each one.
        (EnemyType type, Vector3 position)[] FixedDefences => new[]
        {
            (EnemyType.Radar, enemyHqPosition + new Vector3(6f, 0f, -8f)),
            (EnemyType.AAGun, enemyHqPosition + new Vector3(-12f, 0f, 0f)),
            (EnemyType.AAGun, enemyHqPosition + new Vector3(0f, 0f, -14f)),
            (EnemyType.AAGun, enemyHqPosition + new Vector3(10f, 0f, 9f)),
            (EnemyType.AAGun, new Vector3(-10f, 0f, -5f)), // forward post with no radar cover, an easier first target
            (EnemyType.Radar, new Vector3(-20f, 0f, 55f)), // second radar covering the north-west flank
            (EnemyType.AAGun, new Vector3(-12f, 0f, 48f)),
        };

        readonly List<Ground.FlatSite> flatSites = new List<Ground.FlatSite>();

        void Awake()
        {
            Instance = this;
            var rng = new System.Random(7); // same scenery every run

            flatSites.Add(new Ground.FlatSite(basePosition, 14f, true));
            flatSites.Add(new Ground.FlatSite(enemyHqPosition, 16f, true));
            foreach (var d in FixedDefences) flatSites.Add(new Ground.FlatSite(d.position, 6f, true));
            Ground.Build(flatSites);

            Camera cam = SetupCamera();
            Atmosphere.Setup(cam, FindOrCreateSun());
            BuildScenery(rng);
            BuildPlayerBase();
            BuildEnemyForces();

            for (int i = 0; i < 3; i++) SpawnDrone(DroneType.Scout);
            for (int i = 0; i < 2; i++) SpawnDrone(DroneType.Strike);
            for (int i = 0; i < 2; i++) SpawnDrone(DroneType.Kamikaze);

            var rts = gameObject.AddComponent<RtsController>();
            var hud = gameObject.AddComponent<SandboxHud>();
            rts.Init(this, hud, cam, basePosition + new Vector3(12f, 0f, 12f));
            hud.Init(this, rts);
        }

        // ---- Units ----

        public Drone SpawnDrone(DroneType type)
        {
            if (drones.Count >= MaxDrones) return null;
            Vector3 pad = basePosition + new Vector3(Random.Range(-5f, 5f), 1f, Random.Range(-5f, 5f));
            var drone = Drone.Create(type, pad);
            drones.Add(drone);
            return drone;
        }

        public EnemyUnit SpawnEnemy(EnemyType type, Vector3 position)
        {
            var enemy = EnemyUnit.Create(type, position);
            enemies.Add(enemy);
            return enemy;
        }

        public void OnUnitDestroyed(Unit unit)
        {
            if (unit is Drone drone)
            {
                drones.Remove(drone);
                dronesLost++;
            }
            else if (unit is EnemyUnit enemy)
            {
                enemies.Remove(enemy);
                enemiesDestroyed++;
                Effects.Wreck(enemy.transform.position, enemy.transform.rotation, enemy.radius);
                // Fixed defences come back at the same spot; vehicles anywhere in enemy territory.
                Vector3 respawnAt = enemy.IsMobile ? RandomEnemyPoint() : enemy.transform.position;
                StartCoroutine(RespawnLater(enemy.type, respawnAt));
            }
        }

        public void OnDroneExpended(Drone drone)
        {
            drones.Remove(drone);
            dronesExpended++;
        }

        IEnumerator RespawnLater(EnemyType type, Vector3 position)
        {
            yield return new WaitForSeconds(EnemyRespawnSeconds);
            SpawnEnemy(type, position);
        }

        public Vector3 RandomEnemyPoint()
        {
            var p = new Vector3(Random.Range(enemyArea.xMin, enemyArea.xMax), 0f, Random.Range(enemyArea.yMin, enemyArea.yMax));
            return Ground.OnGround(p);
        }

        public bool RadarCovers(Vector3 position)
        {
            foreach (var e in enemies)
            {
                if (e.type != EnemyType.Radar || !e.IsAlive) continue;
                Vector3 offset = e.transform.position - position;
                offset.y = 0f;
                if (offset.magnitude <= EnemyUnit.RadarCoverage) return true;
            }
            return false;
        }

        // ---- World building ----

        Camera SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            return cam;
        }

        static Light FindOrCreateSun()
        {
            foreach (var light in Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional) return light;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            return sun;
        }

        void BuildScenery(System.Random rng)
        {
            var world = new GameObject("Scenery").transform;

            // Forests: clusters inside the battlefield, and denser woods on the hills around it.
            for (int f = 0; f < 22; f++)
            {
                bool outer = f >= 10;
                Vector3 center = outer ? OuterSpot(rng) : ScenerySpot(rng);
                int trees = outer ? rng.Next(16, 30) : rng.Next(8, 18);
                float spread = outer ? 16f : 9f;
                bool pines = rng.NextDouble() < 0.6;
                for (int t = 0; t < trees; t++)
                {
                    Vector3 pos = center + new Vector3(Range(rng, -spread, spread), 0f, Range(rng, -spread, spread));
                    if (NearSite(pos, 6f) || Mathf.Abs(pos.x) > 200f || Mathf.Abs(pos.z) > 200f) continue;
                    bool pine = pines ? rng.NextDouble() < 0.85 : rng.NextDouble() < 0.2;
                    float scale = Range(rng, 0.8f, 1.35f);
                    Place(pine ? "Scenery_Pine" : "Scenery_Tree", pos, Range(rng, 0f, 360f), scale, 0.2f, world,
                        root => PrimitiveTree(root, scale));
                }
            }

            // Villages
            string[] buildings = { "Scenery_House_A", "Scenery_House_B", "Scenery_House_A", "Scenery_Barn" };
            for (int v = 0; v < 4; v++)
            {
                Vector3 center = ScenerySpot(rng);
                float streetYaw = rng.Next(0, 4) * 90f + Range(rng, -15f, 15f);
                int houses = rng.Next(4, 8);
                for (int i = 0; i < houses; i++)
                {
                    Vector3 pos = center + new Vector3(Range(rng, -11f, 11f), 0f, Range(rng, -11f, 11f));
                    if (!InsideMap(pos) || NearSite(pos, 8f)) continue;
                    string model = buildings[rng.Next(buildings.Length)];
                    float yaw = streetYaw + rng.Next(0, 4) * 90f + Range(rng, -6f, 6f);
                    Place(model, pos, yaw, 1f, 0.5f, world, root => PrimitiveHouse(root, rng));
                }
            }

            // Boulders
            for (int r = 0; r < 45; r++)
            {
                Vector3 pos = rng.NextDouble() < 0.5 ? ScenerySpot(rng) : OuterSpot(rng);
                if (NearSite(pos, 4f)) continue;
                float scale = Range(rng, 0.6f, 2.2f);
                Place("Scenery_Rock", pos, Range(rng, 0f, 360f), scale, 0.25f * scale, world,
                    root => Art.Part(PrimitiveType.Sphere, root, new Vector3(0f, 0.3f * scale, 0f),
                        new Vector3(1.6f, 0.9f, 1.3f) * scale, new Color(0.45f, 0.44f, 0.42f)));
            }
        }

        // Places Resources/Models/<model> on the ground (sunk a little so it doesn't float on slopes),
        // or builds a primitive stand-in when the model is missing.
        static void Place(string model, Vector3 pos, float yaw, float scale, float sink, Transform parent,
            System.Action<Transform> fallback)
        {
            var root = new GameObject(model).transform;
            root.SetParent(parent, false);
            float ground = Mathf.Min(Ground.HeightAt(pos), Ground.HeightAt(pos + new Vector3(2f, 0f, 2f)),
                Ground.HeightAt(pos - new Vector3(2f, 0f, 2f)));
            root.position = new Vector3(pos.x, ground - sink, pos.z);
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Art.TryModel(model, root, scale) == null) fallback(root);
        }

        static void PrimitiveTree(Transform root, float h)
        {
            Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.8f * h, 0f), new Vector3(0.3f, 0.8f * h, 0.3f),
                new Color(0.30f, 0.22f, 0.14f));
            Art.Part(PrimitiveType.Sphere, root, new Vector3(0f, 2.3f * h, 0f), Vector3.one * 2.4f * h,
                new Color(0.16f, 0.30f, 0.14f));
        }

        static void PrimitiveHouse(Transform root, System.Random rng)
        {
            float w = Range(rng, 3f, 6f), d = Range(rng, 3f, 6f), h = Range(rng, 2f, 4.5f);
            Color wall = Color.Lerp(new Color(0.62f, 0.58f, 0.50f), new Color(0.50f, 0.50f, 0.52f), (float)rng.NextDouble());
            Art.Part(PrimitiveType.Cube, root, new Vector3(0f, h / 2f, 0f), new Vector3(w, h, d), wall);
            Art.Part(PrimitiveType.Cube, root, new Vector3(0f, h + 0.15f, 0f), new Vector3(w + 0.3f, 0.3f, d + 0.3f),
                new Color(0.35f, 0.25f, 0.22f));
        }

        void BuildPlayerBase()
        {
            var root = new GameObject("Player Base").transform;
            root.position = basePosition;
            Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.1f, 0f), new Vector3(16f, 0.2f, 16f), new Color(0.45f, 0.46f, 0.47f));
            for (int i = -1; i <= 1; i++)
                Art.Part(PrimitiveType.Cylinder, root, new Vector3(i * 4.5f, 0.21f, 0f), new Vector3(3.2f, 0.01f, 3.2f), Art.PlayerBlue);
            Art.Part(PrimitiveType.Cube, root, new Vector3(-12f, 2f, 0f), new Vector3(6f, 4f, 10f), new Color(0.38f, 0.40f, 0.42f)); // hangar
            Art.Part(PrimitiveType.Cylinder, root, new Vector3(-6f, 3f, -7f), new Vector3(0.25f, 3f, 0.25f), Art.Dark); // antenna
            Art.Part(PrimitiveType.Sphere, root, new Vector3(-6f, 6.2f, -7f), Vector3.one * 0.5f, Art.PlayerBlue, default, true);
        }

        void BuildEnemyForces()
        {
            // Enemy HQ (scenery) defended by radars and AA guns.
            var hq = new GameObject("Enemy HQ").transform;
            hq.position = enemyHqPosition;
            Art.Part(PrimitiveType.Cube, hq, new Vector3(0f, 1.5f, 0f), new Vector3(9f, 3f, 7f), new Color(0.36f, 0.36f, 0.32f));
            Art.Part(PrimitiveType.Cube, hq, new Vector3(0f, 0.6f, 0f), new Vector3(14f, 1.2f, 0.6f), new Color(0.55f, 0.50f, 0.38f)); // sandbags

            foreach (var d in FixedDefences) SpawnEnemy(d.type, d.position);
            for (int i = 0; i < 5; i++) SpawnEnemy(EnemyType.Tank, RandomEnemyPoint());
            for (int i = 0; i < 4; i++) SpawnEnemy(EnemyType.Truck, RandomEnemyPoint());
        }

        Vector3 ScenerySpot(System.Random rng)
        {
            return new Vector3(Range(rng, -80f, 80f), 0f, Range(rng, -80f, 80f));
        }

        // A spot on the hills outside the battlefield.
        static Vector3 OuterSpot(System.Random rng)
        {
            float angle = Range(rng, 0f, Mathf.PI * 2f), distance = Range(rng, 100f, 185f);
            return new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
        }

        bool NearSite(Vector3 p, float margin)
        {
            foreach (var s in flatSites)
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.center.x, s.center.z)) < s.radius + margin)
                    return true;
            return false;
        }

        static bool InsideMap(Vector3 p) => Mathf.Abs(p.x) < MapHalfSize - 2f && Mathf.Abs(p.z) < MapHalfSize - 2f;

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}

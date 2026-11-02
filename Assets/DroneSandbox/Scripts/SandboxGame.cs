using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Builds the sandbox world (terrain, player base, enemy forces) and tracks every unit in it.
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

        static readonly Color FogColor = new Color(0.55f, 0.62f, 0.66f);

        void Awake()
        {
            Instance = this;
            var rng = new System.Random(7); // same scenery every run

            Camera cam = SetupCameraAndLight();
            BuildTerrain(rng);
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
            return new Vector3(Random.Range(enemyArea.xMin, enemyArea.xMax), 0f, Random.Range(enemyArea.yMin, enemyArea.yMax));
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

        Camera SetupCameraAndLight()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogColor;
            cam.farClipPlane = 600f;

            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(50f, -40f, 0f);
                sun.shadows = LightShadows.Soft;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 120f;
            RenderSettings.fogEndDistance = 320f;
            return cam;
        }

        void BuildTerrain(System.Random rng)
        {
            var world = new GameObject("Terrain").transform;

            var outer = GameObject.CreatePrimitive(PrimitiveType.Plane);
            outer.name = "Outer Ground";
            Destroy(outer.GetComponent<Collider>());
            outer.transform.SetParent(world, false);
            outer.transform.position = new Vector3(0f, -0.03f, 0f);
            outer.transform.localScale = new Vector3(60f, 1f, 60f);
            outer.GetComponent<Renderer>().sharedMaterial = Art.Shared(new Color(0.17f, 0.20f, 0.15f));

            float tiles = MapHalfSize * 2f / 10f; // one grid square per 10 m (a Plane is 10 m wide)
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(world, false);
            ground.transform.localScale = new Vector3(tiles, 1f, tiles);
            ground.GetComponent<Renderer>().sharedMaterial = Art.GroundMaterial(tiles);

            // Forests
            for (int f = 0; f < 9; f++)
            {
                Vector3 center = ScenerySpot(rng);
                int trees = rng.Next(8, 18);
                for (int t = 0; t < trees; t++)
                {
                    Vector3 pos = center + new Vector3(Range(rng, -9f, 9f), 0f, Range(rng, -9f, 9f));
                    if (!InsideMap(pos) || NearBase(pos, 18f)) continue;
                    float h = Range(rng, 0.8f, 1.4f);
                    var tree = new GameObject("Tree").transform;
                    tree.SetParent(world, false);
                    tree.position = pos;
                    Art.Part(PrimitiveType.Cylinder, tree, new Vector3(0f, 0.8f * h, 0f), new Vector3(0.3f, 0.8f * h, 0.3f),
                        new Color(0.30f, 0.22f, 0.14f));
                    Art.Part(PrimitiveType.Sphere, tree, new Vector3(0f, 2.3f * h, 0f), Vector3.one * 2.4f * h,
                        new Color(0.16f, 0.30f, 0.14f));
                }
            }

            // Villages
            for (int v = 0; v < 4; v++)
            {
                Vector3 center = ScenerySpot(rng);
                int houses = rng.Next(4, 8);
                for (int i = 0; i < houses; i++)
                {
                    Vector3 pos = center + new Vector3(Range(rng, -10f, 10f), 0f, Range(rng, -10f, 10f));
                    if (!InsideMap(pos) || NearBase(pos, 18f)) continue;
                    float w = Range(rng, 3f, 6f), d = Range(rng, 3f, 6f), h = Range(rng, 2f, 4.5f);
                    var house = new GameObject("Building").transform;
                    house.SetParent(world, false);
                    house.position = pos;
                    house.rotation = Quaternion.Euler(0f, rng.Next(0, 4) * 90f + Range(rng, -8f, 8f), 0f);
                    Color wall = Color.Lerp(new Color(0.62f, 0.58f, 0.50f), new Color(0.50f, 0.50f, 0.52f), (float)rng.NextDouble());
                    Art.Part(PrimitiveType.Cube, house, new Vector3(0f, h / 2f, 0f), new Vector3(w, h, d), wall);
                    Art.Part(PrimitiveType.Cube, house, new Vector3(0f, h + 0.15f, 0f), new Vector3(w + 0.3f, 0.3f, d + 0.3f),
                        new Color(0.35f, 0.25f, 0.22f));
                }
            }
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
            // Enemy HQ (scenery) defended by a radar and AA guns.
            var hq = new GameObject("Enemy HQ").transform;
            hq.position = enemyHqPosition;
            Art.Part(PrimitiveType.Cube, hq, new Vector3(0f, 1.5f, 0f), new Vector3(9f, 3f, 7f), new Color(0.36f, 0.36f, 0.32f));
            Art.Part(PrimitiveType.Cube, hq, new Vector3(0f, 0.6f, 0f), new Vector3(14f, 1.2f, 0.6f), new Color(0.55f, 0.50f, 0.38f)); // sandbags

            SpawnEnemy(EnemyType.Radar, enemyHqPosition + new Vector3(6f, 0f, -8f));
            SpawnEnemy(EnemyType.AAGun, enemyHqPosition + new Vector3(-12f, 0f, 0f));
            SpawnEnemy(EnemyType.AAGun, enemyHqPosition + new Vector3(0f, 0f, -14f));
            SpawnEnemy(EnemyType.AAGun, enemyHqPosition + new Vector3(10f, 0f, 9f));

            // Forward post: a lone AA gun with no radar cover, an easier first target.
            SpawnEnemy(EnemyType.AAGun, new Vector3(-10f, 0f, -5f));

            // A second radar covering the north-west flank.
            SpawnEnemy(EnemyType.Radar, new Vector3(-20f, 0f, 55f));
            SpawnEnemy(EnemyType.AAGun, new Vector3(-12f, 0f, 48f));

            for (int i = 0; i < 5; i++) SpawnEnemy(EnemyType.Tank, RandomEnemyPoint());
            for (int i = 0; i < 4; i++) SpawnEnemy(EnemyType.Truck, RandomEnemyPoint());
        }

        Vector3 ScenerySpot(System.Random rng)
        {
            return new Vector3(Range(rng, -80f, 80f), 0f, Range(rng, -80f, 80f));
        }

        bool NearBase(Vector3 p, float distance)
        {
            return Vector3.Distance(new Vector3(p.x, 0f, p.z), basePosition) < distance
                || Vector3.Distance(new Vector3(p.x, 0f, p.z), enemyHqPosition) < distance;
        }

        static bool InsideMap(Vector3 p) => Mathf.Abs(p.x) < MapHalfSize - 2f && Mathf.Abs(p.z) < MapHalfSize - 2f;

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}

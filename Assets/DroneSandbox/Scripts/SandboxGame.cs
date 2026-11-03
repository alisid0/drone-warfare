using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum GameMode { Menu, Sandbox, Mission }
    public enum MissionResult { None, Won, Failed }

    // Builds the world once, then runs whichever game mode is picked from the menu, and tracks every unit:
    // - Sandbox: free play. Destroyed enemies respawn and you can launch drones any time.
    // - Mission (Asset Hunt): destroy a mission's targets with a fixed stock of drones. Some targets are
    //   hidden until a drone flies close, a convoy may try to get away, and you earn stars for using few drones.
    //   Every enemy destroyed earns points for the tech shop (see Tech), which adds drones and upgrades.
    public class SandboxGame : MonoBehaviour
    {
        public const int MaxDrones = 30;
        public const float MapHalfSize = 90f;
        public const float SpotRange = 26f;     // a drone this close (across the ground) spots a hidden enemy
        public const float SearchRadius = 24f;  // size of the circle the HUD draws around a hidden target
        const float EnemyRespawnSeconds = 30f;
        const float ConvoySpeed = 3.5f;
        const float ResultsDelay = 1.5f;        // let the last explosion play before the results panel

        public static SandboxGame Instance { get; private set; }
        public static bool SaveProgress = true; // off in test runs

        public readonly List<Drone> drones = new List<Drone>();
        public readonly List<EnemyUnit> enemies = new List<EnemyUnit>();
        public int enemiesDestroyed, dronesLost, dronesExpended;

        public Vector3 basePosition => MapLayout.BasePosition;
        public Vector3 enemyHqPosition => MapLayout.EnemyHqPosition;
        public Hotspot Hotspot { get; private set; } // the theatre hotspot being played, if any
        public bool Loading { get; private set; }

        public GameMode Mode { get; private set; } = GameMode.Menu;
        public Mission Mission { get; private set; }
        public int MissionIndex { get; private set; } = -1;
        public bool BriefingOpen { get; private set; }
        public MissionResult Result { get; private set; }
        public string ResultReason { get; private set; }
        public int Stars { get; private set; }
        public float ResultsAt { get; private set; }   // unscaled time the results panel appears
        public float MissionTime { get; private set; }
        public int DronesUsed { get; private set; }
        public int TargetsTotal { get; private set; }
        public int TargetsDestroyed { get; private set; }
        public int PointsEarned { get; private set; }  // this mission, kills plus the bonus for new stars
        public int StarBonus { get; private set; }
        public string Message { get; private set; }
        public float MessageUntil { get; private set; }

        // Menus, the briefing and the results panel take the mouse and keyboard.
        public bool UiBlocking => Mode == GameMode.Menu || BriefingOpen || Result != MissionResult.None;

        readonly int[] stock = new int[4];
        RtsController rts;
        Transform world;

        void Awake()
        {
            Instance = this;

            Camera cam = SetupCamera();
            Light sun = FindOrCreateSun();
            Atmosphere.Setup(cam, sun);
            world = World.Build(transform, preview: false);
            gameObject.AddComponent<DayNight>().Init(sun);

            rts = gameObject.AddComponent<RtsController>();
            var hud = gameObject.AddComponent<SandboxHud>();
            rts.Init(this, hud, cam, basePosition + new Vector3(12f, 0f, 12f));
            gameObject.AddComponent<ShadowFit>().Init(rts);
            hud.Init(this, rts);
            gameObject.AddComponent<PhotoMode>().Init(this, rts, cam);
            OpenMenu();
        }

        // ---- Modes ----

        public void OpenMenu()
        {
            ClearUnits();
            Mode = GameMode.Menu;
            Mission = null;
            MissionIndex = -1;
            Hotspot = null;
            Atmosphere.SetWeather(Weather.Clear);
            rts.Orbit(true);
            DayNight.Instance.SetTime(19.6f, 0.3f); // golden hour behind the menu
        }

        public void StartSandbox()
        {
            ClearUnits();
            Mode = GameMode.Sandbox;
            rts.Orbit(false);
            DayNight.Instance.SetTime(10f, 1f); // a full day every 24 minutes; N skips ahead
            foreach (var d in MapLayout.FixedDefences) SpawnEnemy(d.type, d.position);
            for (int i = 0; i < 5; i++) SpawnEnemy(EnemyType.Tank, RandomEnemyPoint());
            for (int i = 0; i < 4; i++) SpawnEnemy(EnemyType.Truck, RandomEnemyPoint());
            for (int i = 0; i < 3; i++) SpawnDrone(DroneType.Scout);
            for (int i = 0; i < 2; i++) SpawnDrone(DroneType.Strike);
            for (int i = 0; i < 2; i++) SpawnDrone(DroneType.Kamikaze);
            rts.LookAt(basePosition + new Vector3(12f, 0f, 12f), 55f, 45f);
        }

        // ---- Sectors ----

        // Tears down the map and builds this sector in its place (the terrain, water, villages, everything).
        public void LoadSector(SectorPlan plan)
        {
            var current = MapLayout.Plan;
            if (current.kind == plan.kind && current.seed == plan.seed && world != null) return;
            ClearUnits();
            if (world != null) DestroyImmediate(world.gameObject);
            MapLayout.Use(plan);
            world = World.Build(transform, preview: false);
        }

        // Flies into a theatre hotspot: builds its sector (a moment's pause while the land is laid out), then
        // briefs its mission.
        public void PlayHotspot(Hotspot hotspot) => StartCoroutine(PlayHotspotSoon(hotspot));

        IEnumerator PlayHotspotSoon(Hotspot hotspot)
        {
            Loading = true;
            yield return null; // let the HUD show "loading" first
            yield return null;
            var plan = hotspot.Plan();
            LoadSector(plan);
            var mission = Situations.Generate(MapLayout.Plan, hotspot.situation, hotspot.seed, hotspot.daylight);
            Loading = false;
            Begin(mission, -1, hotspot);
        }

        public void StartMission(int index)
        {
            // The Asset Hunt missions are set in the Varrow Valley.
            if (MapLayout.Plan.kind != SectorKind.VarrowValley) LoadSector(SectorPlan.Varrow());
            Begin(Missions.All[index], index, null);
        }

        void Begin(Mission mission, int index, Hotspot hotspot)
        {
            ClearUnits();
            Mode = GameMode.Mission;
            MissionIndex = index;
            Mission = mission;
            Hotspot = hotspot;
            if (Mission.key == null) Mission.key = $"mission{index}";
            Atmosphere.SetWeather(hotspot != null ? hotspot.weather : Weather.Clear);
            for (int t = 0; t < stock.Length; t++) stock[t] = MissionStock(Mission, (DroneType)t);
            rts.Orbit(false);
            DayNight.Instance.SetTime(Mission.startHour, 0.25f);

            var rng = new System.Random(index * 101 + 7); // search circles land in the same place every try
            foreach (var u in Mission.units)
            {
                Vector3 position = u.position;
                float yaw = u.yaw;
                GameObject building = null;
                int routeStart = 0;
                if (u.type == EnemyType.Command)
                {
                    building = GameObject.Find("HQ_Building");
                    position = building != null ? building.transform.position : enemyHqPosition;
                }
                if (u.convoy > 0)
                {
                    // The lead starts 24 points (about 48 m) along the route, the rest 10 m apart behind it.
                    var route = Mission.convoyRoute;
                    routeStart = Mathf.Clamp(24 - (u.convoy - 1) * 5, 0, route.Count - 2);
                    position = route[routeStart];
                    Vector3 ahead = route[routeStart + 1] - position;
                    yaw = Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
                }

                var enemy = SpawnEnemy(u.type, position, yaw);
                enemy.isTarget = u.target;
                if (u.decoy)
                {
                    enemy.isDecoy = true;
                    enemy.maxHp = enemy.hp = 15f;
                }
                if (building != null) enemy.AttachStructure(building);
                if (u.convoy > 0) enemy.Drive(Mission.convoyRoute, routeStart + 1, ConvoySpeed);
                else if (enemy.IsMobile) enemy.Park();
                if (u.hidden)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f, off = 4f + (float)rng.NextDouble() * 10f;
                    enemy.Hide(position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * off);
                }
                if (u.target) TargetsTotal++;
            }

            BriefingOpen = true;
            Time.timeScale = 0f;
            rts.LookAt(Mission.cameraFocus, Mission.cameraDistance, Mission.cameraYaw);
        }

        public void CloseBriefing()
        {
            BriefingOpen = false;
            Time.timeScale = 1f;
        }

        public void RetryMission()
        {
            if (MissionIndex >= 0) StartMission(MissionIndex);
            else Begin(Mission, -1, Hotspot);
        }

        public bool HasNextMission => Mode == GameMode.Mission && MissionIndex >= 0 && MissionIndex + 1 < Missions.All.Count;

        public void NextMission() => StartMission(MissionIndex + 1);

        // -1 in the sandbox, where launches are unlimited.
        public int StockLeft(DroneType type) => Mode == GameMode.Mission ? stock[(int)type] : -1;

        // A mission's own drones plus what the tech shop adds: the supply contract's extra drones and the Nightjar.
        public static int MissionStock(Mission mission, DroneType type)
        {
            if (type == DroneType.Bomber) return Tech.Owns(TechId.Nightjar) ? 1 : 0;
            return mission.Stock(type) + (Tech.Owns(TechId.Supply) ? 1 : 0);
        }

        // The Nightjar's launch button shows in free play, and in missions once it's bought.
        public bool BomberAvailable => Mode == GameMode.Sandbox || (Mode == GameMode.Mission && Tech.Owns(TechId.Nightjar));

        // How close a drone must fly to spot a hidden enemy; long-range optics push it out.
        public float SpotDistance => Tech.Active(TechId.Optics) ? Tech.OpticsSpotRange : SpotRange;

        public static int BestStars(int missionIndex) => PlayerPrefs.GetInt($"mission{missionIndex}.stars", 0);

        static int BestStars(string key) => PlayerPrefs.GetInt(key + ".stars", 0);

        public static bool Unlocked(int missionIndex) => missionIndex == 0 || BestStars(missionIndex - 1) > 0;

        // Removes every unit, shot and effect, and resets the counters, ready for a new mode.
        void ClearUnits()
        {
            StopAllCoroutines();
            foreach (var d in drones) if (d != null) Destroy(d.gameObject);
            foreach (var e in enemies) if (e != null) Destroy(e.gameObject);
            drones.Clear();
            enemies.Clear();
            foreach (var p in Object.FindObjectsByType<Projectile>()) Destroy(p.gameObject);
            foreach (var fx in Object.FindObjectsByType<Effects>()) Destroy(fx.gameObject);
            Effects.Clear();
            DayNight.Instance?.SetThermal(false);
            rts.selected.Clear();
            enemiesDestroyed = dronesLost = dronesExpended = 0;
            Result = MissionResult.None;
            BriefingOpen = false;
            MissionTime = 0f;
            DronesUsed = TargetsTotal = TargetsDestroyed = PointsEarned = StarBonus = 0;
            Message = null;
            Time.timeScale = 1f;
        }

        // ---- Mission flow ----

        // Thermal view comes from a drone's thermal camera (a Wren-T Scout or a Nightjar), so it needs one in the air.
        bool ThermalInAir => drones.Exists(d => d.HasThermal);

        public void ToggleThermal()
        {
            var dayNight = DayNight.Instance;
            if (dayNight == null || UiBlocking) return;
            if (!dayNight.Thermal && !ThermalInAir)
            {
                bool scouts = Tech.Active(TechId.ThermalCamera), nightjar = BomberAvailable;
                ShowMessage(scouts && nightjar ? "Thermal view needs a Scout or Nightjar in the air"
                    : scouts ? "Thermal view needs a Scout in the air"
                    : nightjar ? "Thermal view needs the Nightjar in the air"
                    : "No thermal camera: buy the Wren-T in the Tech shop", 3.5f);
                return;
            }
            dayNight.SetThermal(!dayNight.Thermal);
        }

        void Update()
        {
            var dayNight = DayNight.Instance;
            if (dayNight != null && dayNight.Thermal && !ThermalInAir)
            {
                dayNight.SetThermal(false);
                ShowMessage("Thermal lost: no thermal camera in the air");
            }
            if (Mode != GameMode.Mission || BriefingOpen || Result != MissionResult.None) return;
            MissionTime += Time.deltaTime;
            SpotHidden();
            bool dronesLeft = drones.Count > 0;
            foreach (int left in stock) dronesLeft |= left > 0;
            if (!dronesLeft && TargetsDestroyed < TargetsTotal) Finish(false, "Out of drones.");
        }

        // A hidden enemy shows itself once any of your drones flies within SpotDistance of it.
        void SpotHidden()
        {
            float spot = SpotDistance;
            foreach (var e in enemies.ToArray())
            {
                if (e == null || !e.Hidden) continue;
                foreach (var d in drones)
                {
                    Vector3 offset = d.transform.position - e.transform.position;
                    offset.y = 0f;
                    if (offset.magnitude > spot) continue;
                    e.Reveal();
                    break;
                }
            }
        }

        public void OnSpotted(EnemyUnit enemy) => ShowMessage(enemy.unitName + " spotted");

        public void OnEnemyEscaped(EnemyUnit enemy)
        {
            enemies.Remove(enemy);
            Destroy(enemy.gameObject);
            if (Mode == GameMode.Mission && enemy.isTarget && Result == MissionResult.None) Finish(false, Mission.escapeFailure);
        }

        void Finish(bool won, string reason)
        {
            Result = won ? MissionResult.Won : MissionResult.Failed;
            ResultReason = reason;
            Stars = !won ? 0 : DronesUsed <= Mission.par ? 3 : DronesUsed <= Mission.par + 2 ? 2 : 1;
            StarBonus = Mathf.Max(0, Stars - BestStars(Mission.key)) * Tech.PointsPerNewStar; // only stars you didn't have
            if (StarBonus > 0)
            {
                Tech.Earn(StarBonus);
                PointsEarned += StarBonus;
            }
            if (SaveProgress && Stars > BestStars(Mission.key))
            {
                PlayerPrefs.SetInt(Mission.key + ".stars", Stars);
                PlayerPrefs.Save();
            }
            if (won && Hotspot != null) Theatre.Won(Hotspot);
            ResultsAt = Time.unscaledTime + ResultsDelay;
            StartCoroutine(FreezeForResults());
        }

        IEnumerator FreezeForResults()
        {
            yield return new WaitForSecondsRealtime(ResultsDelay);
            Time.timeScale = 0f;
        }

        public void ShowMessage(string text, float seconds = 2.5f)
        {
            Message = text;
            MessageUntil = Time.unscaledTime + seconds;
        }

        // ---- Units ----

        public Drone SpawnDrone(DroneType type)
        {
            if (UiBlocking || drones.Count >= MaxDrones) return null;
            if (Mode == GameMode.Mission)
            {
                if (stock[(int)type] <= 0)
                {
                    ShowMessage(type == DroneType.Bomber ? (BomberAvailable ? "The Nightjar is already flying" : "Buy the Nightjar in the Tech shop")
                        : $"No {type} drones left");
                    return null;
                }
                stock[(int)type]--;
                DronesUsed += type == DroneType.Bomber ? 2 : 1; // the Nightjar counts double towards the stars
            }
            Vector3 pad = basePosition + new Vector3(Random.Range(-5f, 5f), 1f, Random.Range(-5f, 5f));
            var drone = Drone.Create(type, pad);
            drones.Add(drone);
            return drone;
        }

        public EnemyUnit SpawnEnemy(EnemyType type, Vector3 position) => SpawnEnemy(type, position, Random.Range(0f, 360f));

        public EnemyUnit SpawnEnemy(EnemyType type, Vector3 position, float yaw)
        {
            var enemy = EnemyUnit.Create(type, position, yaw);
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
                if (enemy.type != EnemyType.Command) Effects.Wreck(enemy);
                if (Mode == GameMode.Sandbox)
                {
                    // Fixed defences come back at the same spot; vehicles anywhere in enemy territory.
                    Vector3 respawnAt = enemy.IsMobile ? RandomEnemyPoint() : enemy.transform.position;
                    StartCoroutine(RespawnLater(enemy.type, respawnAt));
                }
                else if (Mode == GameMode.Mission && enemy.isDecoy)
                {
                    ShowMessage("Decoy! An inflatable " + enemy.unitName.ToLowerInvariant() + " - no points", 3f);
                }
                else if (Mode == GameMode.Mission)
                {
                    int value = Tech.KillValue(enemy.type);
                    Tech.Earn(value);
                    PointsEarned += value;
                    if (enemy.isTarget && Result == MissionResult.None)
                    {
                        TargetsDestroyed++;
                        ShowMessage($"{enemy.unitName} destroyed  ({TargetsDestroyed}/{TargetsTotal})   +{value} pts");
                        if (TargetsDestroyed >= TargetsTotal) Finish(true, "All targets destroyed.");
                    }
                }
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
            var area = MapLayout.EnemyArea;
            var p = new Vector3(Random.Range(area.xMin, area.xMax), 0f, Random.Range(area.yMin, area.yMax));
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

        // ---- Setup ----

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
    }
}

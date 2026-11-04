using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum GameMode { Menu, Sandbox, Mission }
    public enum MissionResult { None, Won, Failed }

    // A mission runs in phases: find the targets (Recon, skipped when the briefing already knows where they all
    // are), destroy them (Strike), then bring the surviving drones home (Extract).
    public enum MissionPhase { Recon, Strike, Extract }

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
        public bool Paused { get; private set; } // tactical pause: the world stops, orders can still be given

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
        public MissionPhase Phase { get; private set; }
        public float ExtractEndsAt { get; private set; } // MissionTime the extraction window closes
        public int DronesHome { get; private set; }      // drones back over the base when the mission ended
        public const float ExtractSeconds = 60f, HomeRadius = 18f;
        public float ShadowProgress { get; private set; } // seconds the column has been watched unseen (Shadow missions)
        public bool ShadowSpotted { get; private set; }   // the column has noticed us (watching doesn't count for now)
        public float ShadowSuspicion { get; private set; } // 0-1: how close the column is to spotting the tail
        public int ShadowDetections { get; private set; }  // the third ends the mission
        public int ShadowIntel { get; private set; }       // milestones logged so far, of three
        public const int ShadowDetectionLimit = 3;
        // How close a drone can get before the column grows suspicious; further while its escort sweeps the flanks.
        public float ShadowNoticeRange => (Time.time < escortSweepUntil ? 34f : 26f) * Recon.WeatherFactor;
        float escortSweepUntil;
        public bool IsShadow => Mode == GameMode.Mission && Mission != null && Mission.shadowSeconds > 0f;
        // Drones don't open fire on their own (a Shadow mission: one stray burst at the column ends it).
        public static bool WeaponsTight => Instance != null && Instance.IsShadow;

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
            Canopy.Build(world);
            gameObject.AddComponent<DayNight>().Init(sun);
            MapSnapshot.Capture(world);

            rts = gameObject.AddComponent<RtsController>();
            var hud = gameObject.AddComponent<SandboxHud>();
            rts.Init(this, hud, cam, basePosition + new Vector3(12f, 0f, 12f));
            gameObject.AddComponent<ShadowFit>().Init(rts);
            hud.Init(this, rts);
            gameObject.AddComponent<PhotoMode>().Init(this, rts, cam);
            Sound.Create(gameObject, cam, rts);
            gameObject.AddComponent<Recon>();
            DroneFeed.Create(gameObject, rts);
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
            foreach (var d in MapLayout.FixedDefences) SpawnEnemy(d.type, d.position).Brief(Intel.Identified); // on the map from earlier recon
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
            var oldTerrain = Ground.Current != null ? Ground.Current.terrainData : null;
            if (world != null) DestroyImmediate(world.gameObject);
            if (oldTerrain != null) Destroy(oldTerrain); // heights, splat and grass maps aren't freed with the GameObject
            Farmland.Release();
            MapLayout.Use(plan);
            world = World.Build(transform, preview: false);
            Canopy.Build(world);
            MapSnapshot.Capture(world);
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
            // Which of two look-alike vehicles is the real one changes from try to try.
            var units = new List<MissionUnit>(Mission.units);
            int empty = units.FindIndex(x => x.empty), real = units.FindIndex(x => x.target && x.convoy > 0 && empty >= 0 && x.type == units[empty].type);
            if (empty >= 0 && real >= 0 && Random.value < 0.5f)
            {
                var a = units[empty];
                var b = units[real];
                (a.convoy, b.convoy) = (b.convoy, a.convoy);
                units[empty] = a;
                units[real] = b;
            }
            Diverted = false;
            // A forked route: which exit the column means to take changes from try to try (four times in ten, north).
            convoyPlan = Mission.convoyBranch != null && Random.value < 0.4f ? Mission.convoyBranch : Mission.convoyRoute;
            foreach (var u in units)
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
                    routeStart = Mathf.Clamp(Mission.convoyStart - (u.convoy - 1) * 5, 0, route.Count - 2);
                    position = route[routeStart];
                    Vector3 ahead = route[routeStart + 1] - position;
                    yaw = Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
                }

                var enemy = SpawnEnemy(u.type, position, yaw);
                enemy.isTarget = u.target;
                if (u.target && Mission.shadowSeconds > 0f) enemy.alertSeconds = 8f;
                if (u.target && Mission.nightjar) enemy.hardened = true;
                if (Mission.nightjar && u.type == EnemyType.AAGun) enemy.Upgun();
                if (u.decoy)
                {
                    enemy.isDecoy = true;
                    if (!u.empty) enemy.maxHp = enemy.hp = 15f;
                }
                if (building != null) enemy.AttachStructure(building);
                if (u.convoy > 0)
                {
                    enemy.Drive(convoyPlan ?? Mission.convoyRoute, routeStart + 1, Mission.convoySpeed);
                    enemy.departAt = Time.time + Mission.convoyDelay;
                    if (Mission.convoyBranch != null) enemy.alertSeconds = 10f; // a fork convoy settles after a scare
                }
                else if (enemy.IsMobile) enemy.Park();
                if (u.hidden)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f, off = 4f + (float)rng.NextDouble() * 10f;
                    enemy.Hide(position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * off);
                }
                // The briefing's intelligence: fixed defences are mapped, vehicles only roughly placed.
                else enemy.Brief(enemy.IsMobile ? Intel.Contact : Intel.Identified, Mission.contactRadius * Theatre.IntelFactor);
                if (u.target) TargetsTotal++;
            }

            Phase = TargetsLocated() ? MissionPhase.Strike : MissionPhase.Recon;
            BriefingOpen = true;
            Time.timeScale = 0f;
            rts.LookAt(Mission.cameraFocus, Mission.cameraDistance, Mission.cameraYaw);
        }

        public void CloseBriefing()
        {
            BriefingOpen = false;
            Time.timeScale = 1f;
            ShowMessage(Phase == MissionPhase.Recon ? "RECON: find the targets. Each drone sees about 45 m around it."
                : "STRIKE: the targets are known. Destroy them.", 5f);
        }

        // Every target is on the map (identified, found, or already destroyed).
        bool TargetsLocated()
        {
            foreach (var e in enemies)
                if (e != null && e.isTarget && (e.Hidden || e.Intel < Intel.Identified)) return false;
            return true;
        }

        // B: the selected drones (or all of them) fly home to the base.
        public void Recall(List<Drone> which)
        {
            var list = which.Count > 0 ? which : drones;
            foreach (var d in list)
                if (d != null && !d.IsKamikaze) d.MoveTo(basePosition + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f)));
            if (list.Count > 0) ShowMessage("Returning to base");
        }

        // Drones back over the base, out of those still flying (kamikazes don't come back).
        public int CountHome(out int flying)
        {
            int home = 0;
            flying = 0;
            foreach (var d in drones)
            {
                if (d == null || d.IsKamikaze) continue;
                flying++;
                Vector3 offset = d.transform.position - basePosition;
                offset.y = 0f;
                if (offset.magnitude <= HomeRadius) home++;
            }
            return home;
        }

        // ---- Coordinated strike ----

        int nextSyncGroup = 1;

        // G: the selected drones that have attack orders strike together. Each one's time to its target is worked
        // out, and the quicker ones hold back so all the hits land within about a second and a half of each other.
        public void CoordinateStrike(List<Drone> selection)
        {
            var group = selection.FindAll(d => d != null && d.IsAlive && d.AttackTarget != null && d.AttackTarget.IsAlive);
            if (group.Count < 2)
            {
                ShowMessage("Coordinated strike: give two or more drones attack orders first, then G");
                Sound.Ui("invalid");
                return;
            }
            float latest = 0f;
            foreach (var d in group) latest = Mathf.Max(latest, d.TimeToStrike());
            int id = nextSyncGroup++;
            float now = Time.time;
            foreach (var d in group)
            {
                d.SyncGroup = id;
                d.StrikeAt = now + latest;
                d.HoldUntil = now + Mathf.Max(0f, latest - d.TimeToStrike() - 0.3f);
            }
            syncWatch[id] = group.Count;
            ShowMessage($"Coordinated strike: {group.Count} drones, hits in {latest:0} s");
            Sound.Alert("objective", 0.3f);
        }

        readonly Dictionary<int, int> syncWatch = new Dictionary<int, int>();

        // A drone lost (or cut off) before its group struck: the strike is compromised, and the rest go anyway.
        void WatchSync(Drone lost)
        {
            if (lost.SyncGroup == 0 || !syncWatch.ContainsKey(lost.SyncGroup) || Time.time > lost.StrikeAt) return;
            syncWatch.Remove(lost.SyncGroup);
            ShowMessage($"Strike compromised: {lost.unitName} lost before the hit", 3.5f);
            foreach (var d in drones)
                if (d != null && d.SyncGroup == lost.SyncGroup) d.HoldUntil = -1f;
        }

        // Tab: stop the world to plan, and start it again. The world drains to a cool grey while it's stopped.
        public void TogglePause()
        {
            if (UiBlocking || Loading) return;
            Paused = !Paused;
            Time.timeScale = Paused ? 0f : 1f;
            AudioListener.pause = Paused;
            if (pauseVolume == null)
            {
                var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                var grade = profile.Add<UnityEngine.Rendering.HighDefinition.ColorAdjustments>();
                grade.saturation.Override(-45f);
                grade.colorFilter.Override(new Color(0.9f, 0.96f, 1.05f));
                pauseVolume = new GameObject("Pause Grade").AddComponent<UnityEngine.Rendering.Volume>();
                pauseVolume.isGlobal = true;
                pauseVolume.priority = 30f;
                pauseVolume.sharedProfile = profile;
            }
        }

        UnityEngine.Rendering.Volume pauseVolume;

        void LateUpdate()
        {
            // The pause grade eases in and out over 0.15 s (in real time: the game clock is stopped).
            if (pauseVolume != null)
                pauseVolume.weight = Mathf.MoveTowards(pauseVolume.weight, Paused ? 1f : 0f, Time.unscaledDeltaTime / 0.15f);
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
            if (mission.shadowSeconds > 0f && type != DroneType.Scout) return 0; // a Shadow mission is eyes only
            if (type == DroneType.Bomber) return Tech.Owns(TechId.Nightjar) || mission.nightjar ? 1 : 0;
            return mission.Stock(type) + (Tech.Owns(TechId.Supply) ? 1 : 0) + (type == DroneType.Kamikaze ? Theatre.SupplyBonus : 0);
        }

        // The Nightjar's launch button shows in free play, and in missions once it's bought.
        public bool BomberAvailable => Mode == GameMode.Sandbox || (Mode == GameMode.Mission && (Tech.Owns(TechId.Nightjar) || (Mission != null && Mission.nightjar)));

        // How close a drone must fly to spot a hidden enemy; long-range optics push it out.
        public float SpotDistance => Tech.Active(TechId.Optics) ? Tech.OpticsSpotRange : SpotRange;

        public static int BestStars(int missionIndex) => Progress.Get($"mission{missionIndex}.stars");

        static int BestStars(string key) => Progress.Get(key + ".stars");

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
            Feed.Clear();
            ShadowProgress = 0f;
            ShadowSpotted = false;
            ShadowSuspicion = 0f;
            ShadowDetections = 0;
            ShadowIntel = 0;
            escortSweepUntil = 0f;
            Tracks.Clear();
            Events.Clear();
            trackIds.Clear();
            EnemyUnit.ResetContactIds();
            Time.timeScale = 1f;
            Paused = false;
            AudioListener.pause = false;
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
            RecordTracks();
            SpotHidden();
            WatchBranch();
            WatchConvoy();
            if (IsShadow && Phase != MissionPhase.Extract)
            {
                bool watched = false, spotted = false;
                float nearest = float.MaxValue;
                Drone tail = null;
                foreach (var e in enemies)
                {
                    if (e == null || !e.isTarget) continue;
                    if (e.Observed) watched = true;
                    if (e.Alerted) spotted = true;
                    foreach (var d in drones)
                    {
                        if (d == null) continue;
                        Vector3 offset = d.transform.position - e.transform.position;
                        offset.y = 0f;
                        if (offset.magnitude < nearest)
                        {
                            nearest = offset.magnitude;
                            tail = d;
                        }
                    }
                }
                // Suspicion builds inside the column's notice range (full in 2.5 s), holds just outside it and drains
                // beyond (gone in about 5 s). Full suspicion is a detection.
                if (!spotted)
                {
                    if (nearest < ShadowNoticeRange) ShadowSuspicion += Time.deltaTime / 2.5f;
                    else if (nearest > ShadowNoticeRange + 4f) ShadowSuspicion -= Time.deltaTime / 5f;
                    ShadowSuspicion = Mathf.Clamp01(ShadowSuspicion);
                    if (ShadowSuspicion >= 1f && tail != null)
                    {
                        ShadowDetected(tail);
                        if (Result != MissionResult.None) return;
                        spotted = true;
                    }
                }
                ShadowSpotted = spotted;
                if (watched && !spotted) ShadowProgress += Time.deltaTime;
                if (Phase == MissionPhase.Recon && watched) Phase = MissionPhase.Strike; // "Strike" is the watch, for a Shadow mission
                ShadowMilestones();
                if (ShadowProgress >= Mission.shadowSeconds) BeginExtract();
                return;
            }
            if (Phase == MissionPhase.Recon && TargetsLocated())
            {
                Phase = MissionPhase.Strike;
                ShowMessage("STRIKE: all targets located. Destroy them.", 4f);
                Sound.Alert("objective", 0.5f);
            }
            if (Phase == MissionPhase.Extract)
            {
                int home = CountHome(out int flying);
                string done = IsShadow ? "Column watched." : "All targets destroyed.";
                // A Shadow mission's intel has to come home with a Scout.
                if (IsShadow && home == 0 && (flying == 0 || MissionTime >= ExtractEndsAt)) Finish(false, "No scout got the intel home.");
                else if (home >= flying) Finish(true, flying > 0 ? done + " Every drone home." : done);
                else if (MissionTime >= ExtractEndsAt) Finish(true, $"{done} {flying - home} drone(s) left behind.");
                return;
            }
            bool dronesLeft = drones.Count > 0;
            foreach (int left in stock) dronesLeft |= left > 0;
            if (!dronesLeft && TargetsDestroyed < TargetsTotal) Finish(false, IsShadow ? "Every scout lost - the column went on unwatched." : "Out of drones.");
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

        float identifiedNewsAt = -100f, alertNewsAt = -100f;

        // Recon has a new enemy on the map.
        public void OnIdentified(EnemyUnit enemy)
        {
            if (Mode == GameMode.Menu) return;
            if (Time.time - identifiedNewsAt > 2f)
            {
                ShowMessage(enemy.unitName + " identified");
                identifiedNewsAt = Time.time;
            }
            Sound.Alert("contact", 1.5f);
        }

        // An enemy we can see has noticed us.
        public void OnEnemyAlerted(EnemyUnit enemy)
        {
            if (Time.time - alertNewsAt < 6f) return;
            alertNewsAt = Time.time;
            ShowMessage(enemy.unitName + " alerted - they know you're here");
            Sound.Alert("alarm", 6f);
        }

        // A long look shows a "vehicle" for what it is.
        public void OnDecoyExposed(EnemyUnit enemy)
        {
            ShowMessage(Mission != null && Mission.units.Exists(u => u.empty)
                ? $"Confirmed: that {enemy.unitName.ToLowerInvariant()} rides high - it's the empty one"
                : $"Decoy: that {enemy.unitName.ToLowerInvariant()} is inflatable - don't waste a drone on it", 4f);
            Sound.Alert("contact", 0.5f);
        }

        public void OnSpotted(EnemyUnit enemy)
        {
            ShowMessage(enemy.unitName + " spotted");
            Sound.Alert("contact", 0.5f);
        }

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
            Paused = false;
            AudioListener.pause = false;
            Sound.Alert(won ? "success" : "failure", 0f);
            int home = CountHome(out int flying);
            DronesHome = home;
            if (won) dronesLost += flying - home; // left behind over enemy country
            // A star each for winning, for efficiency (par or fewer drones launched, kamikazes included) and for
            // bringing every drone that could come home back.
            Stars = !won ? 0 : 1 + (DronesUsed <= Mission.par ? 1 : 0) + (dronesLost == 0 ? 1 : 0);
            StarBonus = Mathf.Max(0, Stars - BestStars(Mission.key)) * Tech.PointsPerNewStar; // only stars you didn't have
            if (StarBonus > 0)
            {
                Tech.Earn(StarBonus);
                PointsEarned += StarBonus;
            }
            if (Stars > BestStars(Mission.key))
            {
                Progress.Set(Mission.key + ".stars", Stars);
                Progress.Save();
            }
            if (Hotspot != null)
            {
                if (won) Theatre.Won(Hotspot, dronesLost);
                else Theatre.Lost(Hotspot, dronesLost);
            }
            ResultsAt = Time.unscaledTime + ResultsDelay;
            StartCoroutine(FreezeForResults());
        }

        // Targets down: the drones still flying have a minute to get home (straight to the results if there are none).
        public bool Diverted { get; private set; }
        List<Vector3> convoyPlan; // the route this try's column means to take

        // Seconds until the column we can see now gets off the map at its normal pace, or -1 if we can't see it.
        public float ConvoyEta { get; private set; } = -1f;

        void WatchConvoy()
        {
            ConvoyEta = -1f;
            if (Mode != GameMode.Mission || Mission == null || Mission.convoyRoute == null || IsShadow) return;
            foreach (var e in enemies)
                if (e != null && e.OnRoute && e.Observed && (e.isTarget || e.isDecoy))
                {
                    float eta = e.SecondsToRouteEnd();
                    if (ConvoyEta < 0f || eta < ConvoyEta) ConvoyEta = eta;
                }
        }

        float lastPinnedMessage = -100f;

        // An AA gun has been pinned down (EnemyUnit.Suppress): say so, unless it was said a moment ago.
        public void OnSuppressed(EnemyUnit gun, float seconds)
        {
            if (Time.time - lastPinnedMessage < 3f) return;
            lastPinnedMessage = Time.time;
            ShowMessage($"{gun.ContactId ?? gun.unitName} pinned down - {seconds:0} s to get through", 3f);
        }

        // A spooked convoy takes the other road at the fork (Mission.convoyBranch), if it hasn't passed it yet.
        void WatchBranch()
        {
            if (Diverted || Mode != GameMode.Mission || Mission == null || Mission.convoyBranch == null) return;
            bool spooked = false;
            foreach (var e in enemies)
                if (e != null && e.OnRoute && e.Alerted) spooked = true;
            if (!spooked) return;
            Diverted = true;
            // The other exit from the one it meant to take.
            var other = convoyPlan == Mission.convoyBranch ? Mission.convoyRoute : Mission.convoyBranch;
            int turned = 0;
            foreach (var e in enemies)
                if (e != null && e.Divert(other, Mission.branchAt)) turned++;
            if (turned > 0)
            {
                ShowMessage(other == Mission.convoyBranch ? "The column's spooked - it'll turn north at the crossroads, for the far exit"
                    : "The column's spooked - it'll turn east at the crossroads instead", 4.5f);
                Sound.Alert("contact", 0.5f);
            }
        }

        // The column has spotted the tail: it speeds up for a few seconds and the watch pauses. The third time, it's
        // over.
        void ShadowDetected(Drone tail)
        {
            ShadowDetections++;
            ShadowSuspicion = 0f;
            if (ShadowDetections >= ShadowDetectionLimit)
            {
                Finish(false, "Spotted three times: the column called it off and went to ground.");
                return;
            }
            foreach (var e in enemies)
                if (e != null && e.isTarget) e.Alert(tail.transform.position, false);
            ShowMessage($"DETECTED {ShadowDetections}/{ShadowDetectionLimit} - they've seen the tail. Watch paused: drop back past {ShadowNoticeRange + 4f:0} m", 4.5f);
            Sound.Alert("warning", 0.5f);
        }

        // What the watch turns up, a quarter of the way in, past halfway and near the end. The second sends the
        // escort sweeping the flanks for 8 s, so a tail sitting close has to back off.
        void ShadowMilestones()
        {
            float[] at = { 0.25f, 0.58f, 0.92f };
            if (ShadowIntel >= at.Length || ShadowProgress < Mission.shadowSeconds * at[ShadowIntel]) return;
            ShadowIntel++;
            if (ShadowIntel == 1) ShowMessage("INTEL 1/3: formation logged - one truck rides low: that's the cargo", 5f);
            else if (ShadowIntel == 2)
            {
                escortSweepUntil = Time.time + 8f;
                ShowMessage("INTEL 2/3: the escort is sweeping its flanks - 34 m for 8 s. Open the gap!", 5f);
            }
            else ShowMessage("INTEL 3/3: route confirmed - they're heading out on the north road", 4f);
            Sound.Alert("objective", 0.3f);
        }

        void BeginExtract()
        {
            CountHome(out int flying);
            if (flying == 0)
            {
                Finish(true, IsShadow ? "Column watched." : "All targets destroyed.");
                return;
            }
            Phase = MissionPhase.Extract;
            ExtractEndsAt = MissionTime + ExtractSeconds;
            ShowMessage(IsShadow ? $"EXTRACT: we have what we need. Bring your drones home (B) within {ExtractSeconds:0} s."
                : $"EXTRACT: targets destroyed. Bring your drones home (B) within {ExtractSeconds:0} s.", 5f);
            Sound.Alert("objective", 0.3f);
        }

        IEnumerator FreezeForResults()
        {
            yield return new WaitForSecondsRealtime(ResultsDelay);
            Time.timeScale = 0f;
        }

        // The mission's story for the debrief map: where each drone flew (sampled every second), and where enemies
        // died and drones were lost.
        public readonly List<(int drone, DroneType type, Vector2 at)> Tracks = new List<(int, DroneType, Vector2)>();
        public readonly List<(Vector2 at, bool enemy)> Events = new List<(Vector2, bool)>();
        float trackTimer;
        readonly Dictionary<Drone, int> trackIds = new Dictionary<Drone, int>();

        void RecordTracks()
        {
            trackTimer -= Time.deltaTime;
            if (trackTimer > 0f) return;
            trackTimer = 1f;
            foreach (var d in drones)
            {
                if (d == null) continue;
                if (!trackIds.TryGetValue(d, out int id)) trackIds[d] = id = trackIds.Count;
                Tracks.Add((id, d.type, new Vector2(d.transform.position.x, d.transform.position.z)));
            }
        }

        // The event feed: the newest few pieces of news, each fading after its time (see SandboxHud.DrawMessage).
        public readonly List<(string text, float until)> Feed = new List<(string, float)>();

        public void ShowMessage(string text, float seconds = 2.5f)
        {
            Message = text;
            MessageUntil = Time.unscaledTime + seconds;
            Feed.RemoveAll(f => f.text == text);
            Feed.Insert(0, (text, Time.unscaledTime + Mathf.Max(seconds, 3.5f)));
            if (Feed.Count > 3) Feed.RemoveAt(Feed.Count - 1); // the newest line big, two older small: no more
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
                    Sound.Ui("invalid");
                    return null;
                }
                stock[(int)type]--;
                DronesUsed += type == DroneType.Bomber ? 2 : 1; // the Nightjar counts double towards the stars
            }
            Vector3 pad = basePosition + new Vector3(Random.Range(-5f, 5f), 1f, Random.Range(-5f, 5f));
            var drone = Drone.Create(type, pad);
            drones.Add(drone);
            Sound.At("launch", pad, 0.8f);
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
                WatchSync(drone);
                if (Mode == GameMode.Mission && Mission != null && Mission.nightjar && drone.type == DroneType.Bomber && Result == MissionResult.None)
                    Finish(false, "The Nightjar is down - and with it the operation.");
                Effects.Breakup(drone.transform.position);
                Sound.At("drone down", drone.transform.position);
                Events.Add((new Vector2(drone.transform.position.x, drone.transform.position.z), false));
                DroneFeed.Instance?.SignalLost(drone);
            }
            else if (unit is EnemyUnit enemy)
            {
                enemies.Remove(enemy);
                enemiesDestroyed++;
                Events.Add((new Vector2(enemy.transform.position.x, enemy.transform.position.z), true));
                if (enemy.type != EnemyType.Command) Effects.Wreck(enemy);
                Aftermath(enemy);
                if (enemy.type == EnemyType.Radar) ShowMessage("Radar down - the guns it fed aim slower now", 3.5f);
                if (Mode == GameMode.Sandbox)
                {
                    // Fixed defences come back at the same spot; vehicles anywhere in enemy territory.
                    Vector3 respawnAt = enemy.IsMobile ? RandomEnemyPoint() : enemy.transform.position;
                    StartCoroutine(RespawnLater(enemy.type, respawnAt));
                }
                else if (Mode == GameMode.Mission && enemy.isDecoy)
                {
                    ShowMessage(Mission.units.Exists(u => u.empty) ? "Wrong one - that " + enemy.unitName.ToLowerInvariant() + " was empty"
                        : "Decoy! An inflatable " + enemy.unitName.ToLowerInvariant() + " - no points", 3f);
                }
                else if (Mode == GameMode.Mission)
                {
                    int value = Tech.KillValue(enemy.type);
                    Tech.Earn(value);
                    PointsEarned += value;
                    if (enemy.isTarget && Result == MissionResult.None && IsShadow)
                        Finish(false, "We were meant to watch the column, not hit it.");
                    else if (enemy.isTarget && Result == MissionResult.None)
                    {
                        TargetsDestroyed++;
                        ShowMessage($"{enemy.unitName} destroyed  ({TargetsDestroyed}/{TargetsTotal})   +{value} pts");
                        if (TargetsDestroyed < TargetsTotal) Sound.Alert("objective", 0.3f);
                        if (TargetsDestroyed >= TargetsTotal) BeginExtract();
                    }
                }
            }
        }

        // Each kind goes its own way: armour cooks off with a couple of late blasts, a radar showers sparks, fuel and
        // ammunition go up big; heavy kills in view nudge the camera.
        void Aftermath(EnemyUnit enemy)
        {
            Vector3 at = enemy.AimPoint;
            bool heavy = enemy.type == EnemyType.Tank || enemy.type == EnemyType.APC || enemy.type == EnemyType.AAGun || enemy.type == EnemyType.Rocket;
            bool huge = enemy.type == EnemyType.Depot || enemy.type == EnemyType.Tanker || enemy.type == EnemyType.Command;
            if (heavy) StartCoroutine(CookOff(at));
            if (enemy.type == EnemyType.Radar || enemy.type == EnemyType.Jammer)
                for (int i = 0; i < 6; i++) Effects.Spark(at + Vector3.up * (1f + i * 0.4f) + Random.insideUnitSphere * 0.6f);
            Vector3 screen = rts.Cam.WorldToViewportPoint(at);
            bool inView = screen.z > 0f && screen.x > 0f && screen.x < 1f && screen.y > 0f && screen.y < 1f;
            if (inView && (heavy || huge)) rts.Impulse(huge ? 2f : 1f);
        }

        IEnumerator CookOff(Vector3 at)
        {
            yield return new WaitForSeconds(Random.Range(0.5f, 0.8f));
            Effects.Explosion(at + Random.insideUnitSphere * 0.8f, 1.6f);
            yield return new WaitForSeconds(Random.Range(0.6f, 1.1f));
            Effects.Explosion(at + Vector3.up * 0.5f + Random.insideUnitSphere * 0.8f, 1.2f);
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

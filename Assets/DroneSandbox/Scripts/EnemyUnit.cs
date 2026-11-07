using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum EnemyType { Tank, Truck, AAGun, Radar, Artillery, Command, APC, Rocket, Tanker, Jammer, Depot }

    // Enemy targets. Tanks, APCs, trucks and fuel tankers patrol (or, in missions, park or drive a convoy route),
    // AA guns shoot at drones, a radar extends the range of nearby AA guns, artillery and rocket launchers shell
    // your lines, a jammer cuts the link of any drone that flies into its circle (see Drone), an ammunition depot
    // just sits there to be blown up, and the command post is the HQ building itself.
    // What Norvane knows of each one is its Intel (see Recon), and each reacts when it notices drones or comes
    // under attack: AA crews take a moment to acquire a drone unless already alerted or helped by radar, a radar
    // warns everything it covers, convoys put their foot down, parked vehicles drive off to cover somewhere else,
    // and patrols go to look where the trouble was.
    public class EnemyUnit : Unit
    {
        // Reaches and speeds are the old 480 m map's, brought over by Scale.
        public const float RadarCoverage = 35f * Scale.Distance; // AA guns this close to a radar get extra range
        public const float RadarRangeBonus = 6f * Scale.Distance;
        public const float JamRadius = 24f * Scale.Distance;     // drones inside a jammer's circle lose their link
        public const float AlertSeconds = 30f;  // how long a unit stays on alert after the last trouble
        public const float AlertShare = 42f * Scale.Distance;    // units this close to one that's hit or destroyed go on alert too
        public static float Wariness = 1f;      // an ALERTED campaign sector (Theatre): drones noticed further off, the alarm spreading wider

        public EnemyType type;
        public bool isTarget;          // a mission objective
        public bool isDecoy;           // an inflatable fake (see Situations: Decoy Field)
        public bool Hidden { get; private set; }
        public Vector3 searchCenter;   // where the HUD draws the search circle while hidden
        float moveSpeed;
        float heading; // degrees; vehicles tilt to follow the ground but steer by this
        Vector3 waypoint;
        float waitTimer;
        Transform turret; // tank turret, AA mount or radar head
        Quaternion turretRest = Quaternion.identity;
        Vector3 muzzleLocal; // where AA rounds leave the barrels, relative to the turret pivot
        float range, damage, fireInterval, cooldown;
        bool parked;
        List<Vector3> route;           // convoy route, driven point by point
        int routeIndex;
        Transform barrel;              // artillery barrel, kicked back when it fires
        Vector3 barrelRest;
        GameObject structure;          // the building a command post stands for

        // ---- What Norvane knows (Recon) ----
        public Intel Intel { get; private set; }
        public bool Observed { get; private set; }  // something of ours is watching it right now
        public Vector3 LastSeenPosition { get; private set; }
        public float LastSeenTime { get; private set; } = -100f;
        public Vector3 ContactPosition { get; private set; } // the rough position of a mere contact
        public float ContactRadius { get; private set; }
        float watchedFor;
        bool drawn = true;
        Renderer[] renderers;

        // ---- What it knows of us ----
        public float AlertedAt { get; private set; } = -100f;
        public float alertSeconds = AlertSeconds; // shorter for a Shadow mission's column, which settles quickly
        public bool Alerted => Time.time >= alertReadyAt && Time.time - AlertedAt < alertSeconds;
        Vector3 trouble;          // where the last threat was
        float acquiring;          // AA: how long it has been laying its guns on the current drone
        float acquireBegan, alertBegan, alertReadyAt;

        // What an AA gun is doing, for the HUD's threat warnings.
        public Unit TrackedDrone { get; private set; }
        public float AcquireProgress { get; private set; } // 0-1 while laying its guns, 1 once firing
        public float LastShotTime { get; private set; } = -100f;
        public bool Engaging => Time.time - LastShotTime < 0.8f;
        Vector3? fleeTo;          // a parked vehicle driving off to somewhere quieter
        bool investigating;       // a patrol going to look where the trouble was

        // How much of a gun round's damage gets through: armour shrugs off a Scout's machine gun, explosives don't care.
        public float BulletFactor => type switch
        {
            EnemyType.Tank => 0.25f, EnemyType.APC => 0.4f, EnemyType.Command => 0.2f,
            EnemyType.AAGun => 0.6f, EnemyType.Depot => 0.6f, _ => 1f,
        };

        public bool IsMobile => type == EnemyType.Tank || type == EnemyType.Truck || type == EnemyType.APC || type == EnemyType.Tanker;

        public float CurrentRange =>
            range + (SandboxGame.Instance.RadarCovers(transform.position) ? RadarRangeBonus : 0f);

        // How far out, horizontally and in one direction, this AA gun can hit a drone cruising flightAltitude
        // above the ground there. The gun's reach is a sphere around its aim point, so a drone over a hill is
        // closer to it and one over a hollow is further away. Starts from the flat-ground answer and refines
        // it with the ground height under the drone (a few steps settle it on these gentle hills).
        public float ThreatRadiusToward(Vector3 direction, float flightAltitude, float currentRange)
        {
            float gunHeight = AimPoint.y;
            float h = flightAltitude - aimHeight;
            float r = Mathf.Sqrt(Mathf.Max(0f, currentRange * currentRange - h * h));
            for (int i = 0; i < 3; i++)
            {
                Vector3 under = transform.position + direction * r;
                h = Ground.HeightAt(under) + flightAltitude - gunHeight;
                r = Mathf.Sqrt(Mathf.Max(0f, currentRange * currentRange - h * h));
            }
            return r;
        }

        public static EnemyUnit Create(EnemyType type, Vector3 position) => Create(type, position, Random.Range(0f, 360f));

        public static EnemyUnit Create(EnemyType type, Vector3 position, float heading)
        {
            var go = new GameObject(type.ToString());
            go.transform.position = Ground.OnGround(position);
            var unit = go.AddComponent<EnemyUnit>();
            unit.heading = heading;
            unit.Setup(type);
            unit.SettleOnGround(1f);
            unit.ApplyVisibility(); // out of sight until something sees it (Recon)
            return unit;
        }

        void Setup(EnemyType enemyType)
        {
            type = enemyType;
            team = Team.Enemy;

            switch (enemyType)
            {
                case EnemyType.Tank:
                    unitName = "Tank";
                    maxHp = 160f; moveSpeed = 3f; radius = 2.0f; aimHeight = 1.0f;
                    break;
                case EnemyType.Truck:
                    unitName = "Truck";
                    maxHp = 60f; moveSpeed = 5.5f; radius = 1.7f; aimHeight = 1.1f;
                    break;
                case EnemyType.AAGun:
                    unitName = "AA Gun";
                    maxHp = 90f; radius = 1.9f; aimHeight = 0.9f;
                    range = 18f; damage = 6f; fireInterval = 0.5f;
                    break;
                case EnemyType.Radar:
                    unitName = "Radar";
                    maxHp = 70f; radius = 1.5f; aimHeight = 1.5f;
                    break;
                case EnemyType.Artillery:
                    unitName = "Artillery";
                    maxHp = 80f; radius = 1.9f; aimHeight = 0.8f;
                    cooldown = Random.Range(2f, 6f);
                    break;
                case EnemyType.Command:
                    unitName = "Command post";
                    maxHp = 320f; radius = 6f; aimHeight = 3.5f;
                    break;
                case EnemyType.APC:
                    unitName = "APC";
                    maxHp = 110f; moveSpeed = 4.5f; radius = 2.0f; aimHeight = 1.1f;
                    break;
                case EnemyType.Rocket:
                    unitName = "Rocket launcher";
                    maxHp = 70f; radius = 2.0f; aimHeight = 1.3f;
                    cooldown = Random.Range(4f, 10f);
                    break;
                case EnemyType.Tanker:
                    unitName = "Fuel tanker";
                    maxHp = 50f; moveSpeed = 5f; radius = 2.0f; aimHeight = 1.3f;
                    break;
                case EnemyType.Jammer:
                    unitName = "Jammer";
                    maxHp = 70f; radius = 1.8f; aimHeight = 1.4f;
                    break;
                case EnemyType.Depot:
                    unitName = "Ammo depot";
                    maxHp = 150f; radius = 3.4f; aimHeight = 0.9f;
                    break;
            }
            moveSpeed *= Scale.Speed;
            range *= Scale.Distance;
            hp = maxHp;
            if (enemyType == EnemyType.Command) return; // the HQ building in the world is its model

            // Use the Blender model from Resources/Models if there is one, muddied and dusty from the field
            // (Tools/Blender/make_weathered.py) where it has been weathered; otherwise primitive shapes. The newer
            // vehicles (Tools/Blender/make_sectors.py) are built at full size; the game's vehicles are drawn at about
            // half, so they're scaled to match the tanks and trucks.
            var model = Art.TryModel("Weathered_" + enemyType, transform, ModelScale(enemyType))
                ?? Art.TryModel("Enemy_" + enemyType, transform, ModelScale(enemyType));
            if (model != null)
            {
                foreach (var t in model.GetComponentsInChildren<Transform>())
                {
                    if (t.name == "Turret") turret = t;
                    if (t.name == "Barrel") barrel = t;
                }
                if (turret != null) turretRest = turret.localRotation;
                if (barrel != null) barrelRest = barrel.localPosition;
                PaintInsignia(enemyType);
                if (IsMobile)
                {
                    Vector3 lamp = Headlights(enemyType);
                    foreach (float side in new[] { -1f, 1f })
                        DayNight.AddNightLight(transform, new Vector3(side * lamp.x, lamp.y, lamp.z), new Vector3(8f, 0f, 0f), LightType.Spot,
                            new Color(1f, 0.92f, 0.75f), 6f, 30f, 70f);
                }
                muzzleLocal = new Vector3(0f, 1.8f, 1.7f);
            }
            else
            {
                BuildPrimitiveModel(enemyType);
            }
            if (IsMobile) waypoint = SandboxGame.Instance.RandomEnemyPoint();
        }

        public static float ModelScale(EnemyType t) =>
            t == EnemyType.APC || t == EnemyType.Rocket || t == EnemyType.Tanker || t == EnemyType.Jammer ? 0.5f
            : t == EnemyType.Depot ? 0.6f : 1f;

        // Where a vehicle's headlights are (one side; mirrored), in the unit's space.
        static Vector3 Headlights(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.APC: return new Vector3(0.47f, 0.77f, 1.7f);
                case EnemyType.Tanker: return new Vector3(0.46f, 0.73f, 1.79f);
                default: return new Vector3(0.6f, 0.95f, 1.9f);
            }
        }

        // Dravin emblems on the model's flanks (positions fit the Blender models).
        void PaintInsignia(EnemyType enemyType)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                switch (enemyType)
                {
                    case EnemyType.Tank:
                        if (turret != null)
                            Factions.Paint(turret, new Vector3(side * 0.7f, 0.24f, -0.3f), new Vector3(side, 0.26f, 0f).normalized, 0.4f, false);
                        break;
                    case EnemyType.Truck:
                        Factions.Paint(transform, new Vector3(side * 0.87f, 1.2f, -0.55f), new Vector3(side, 0f, 0f), 0.7f, false);
                        break;
                    case EnemyType.Radar:
                        Factions.Paint(transform, new Vector3(side * 0.82f, 1.12f, -0.40f), new Vector3(side, 0f, 0f), 0.55f, false);
                        break;
                    // The newer vehicles, at their half scale.
                    case EnemyType.APC:
                        Factions.Paint(transform, new Vector3(side * 0.65f, 0.84f, 0.54f), new Vector3(side * 0.92f, 0.4f, 0f).normalized, 0.5f, false);
                        break;
                    case EnemyType.Rocket:
                        Factions.Paint(transform, new Vector3(side * 0.6f, 0.76f, 0.81f), new Vector3(side, 0f, 0f), 0.4f, false);
                        break;
                    case EnemyType.Tanker:
                        Factions.Paint(transform, new Vector3(side * 0.48f, 1.02f, -1.04f), new Vector3(side, 0f, 0f), 0.6f, false);
                        break;
                    case EnemyType.Jammer:
                        Factions.Paint(transform, new Vector3(side * 0.62f, 1.18f, -0.9f), new Vector3(side, 0f, 0f), 0.55f, false);
                        break;
                }
            }
            if (enemyType == EnemyType.Artillery)
                Factions.Paint(transform, new Vector3(0.6f, 1.12f, 0.67f), new Vector3(0f, 0.17f, 1f).normalized, 0.38f, false);
        }

        void BuildPrimitiveModel(EnemyType enemyType)
        {
            Transform root = transform;
            switch (enemyType)
            {
                case EnemyType.Tank:
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.5f, 0f), new Vector3(2.0f, 0.7f, 3.0f), Art.EnemyOlive);
                    foreach (float side in new[] { -1.05f, 1.05f })
                        Art.Part(PrimitiveType.Cube, root, new Vector3(side, 0.35f, 0f), new Vector3(0.45f, 0.65f, 3.1f), Art.Dark);
                    turret = Pivot(root, new Vector3(0f, 1.05f, -0.1f));
                    Art.Part(PrimitiveType.Cube, turret, Vector3.zero, new Vector3(1.3f, 0.45f, 1.5f), Art.EnemyOliveDark);
                    Art.Part(PrimitiveType.Cylinder, turret, new Vector3(0f, 0f, 1.6f), new Vector3(0.16f, 1.1f, 0.16f),
                        Art.Dark, new Vector3(90f, 0f, 0f));
                    break;

                case EnemyType.Truck:
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.95f, 1.1f), new Vector3(1.6f, 1.2f, 1.2f), Art.EnemyOlive);
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.9f, -0.6f), new Vector3(1.7f, 1.0f, 2.2f),
                        new Color(0.55f, 0.50f, 0.35f));
                    foreach (float x in new[] { -0.85f, 0.85f })
                        foreach (float z in new[] { -1.0f, 1.0f })
                            Art.Part(PrimitiveType.Cylinder, root, new Vector3(x, 0.32f, z), new Vector3(0.6f, 0.12f, 0.6f),
                                Art.Dark, new Vector3(0f, 0f, 90f));
                    break;

                case EnemyType.AAGun:
                    Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.15f, 0f), new Vector3(2.4f, 0.15f, 2.4f), Art.EnemyOliveDark);
                    turret = Pivot(root, new Vector3(0f, 0.7f, 0f));
                    Art.Part(PrimitiveType.Cube, turret, Vector3.zero, new Vector3(1.0f, 0.6f, 1.0f), Art.EnemyOlive);
                    var elevation = Pivot(turret, new Vector3(0f, 0.15f, 0.2f));
                    elevation.localRotation = Quaternion.Euler(-35f, 0f, 0f);
                    foreach (float side in new[] { -0.22f, 0.22f })
                        Art.Part(PrimitiveType.Cylinder, elevation, new Vector3(side, 0f, 0.9f), new Vector3(0.1f, 0.9f, 0.1f),
                            Art.Dark, new Vector3(90f, 0f, 0f));
                    Art.Part(PrimitiveType.Sphere, turret, new Vector3(0f, 0.4f, -0.3f), Vector3.one * 0.2f, Art.EnemyRed, default, true);
                    muzzleLocal = new Vector3(0f, 0.6f, 1.2f);
                    break;

                case EnemyType.Artillery:
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 0.5f, 1.2f), Art.EnemyOlive);
                    Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 1.1f, 1.6f), new Vector3(0.14f, 1.8f, 0.14f),
                        Art.Dark, new Vector3(70f, 0f, 0f));
                    break;

                case EnemyType.APC:
                case EnemyType.Tanker:
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 1.1f, 0f), new Vector3(2.6f, 1.4f, 6.5f),
                        enemyType == EnemyType.Tanker ? new Color(0.25f, 0.26f, 0.22f) : Art.EnemyOlive);
                    foreach (float x in new[] { -1.2f, 1.2f })
                        foreach (float z in new[] { -2.2f, -0.7f, 0.8f, 2.3f })
                            Art.Part(PrimitiveType.Cylinder, root, new Vector3(x, 0.45f, z), new Vector3(0.9f, 0.15f, 0.9f), Art.Dark, new Vector3(0f, 0f, 90f));
                    if (enemyType == EnemyType.APC)
                    {
                        turret = Pivot(root, new Vector3(0f, 2.0f, 0.4f));
                        Art.Part(PrimitiveType.Cube, turret, Vector3.zero, new Vector3(1.1f, 0.5f, 1.3f), Art.EnemyOliveDark);
                    }
                    break;

                case EnemyType.Rocket:
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 1.2f, 2.3f), new Vector3(2.3f, 1.6f, 1.8f), Art.EnemyOlive);
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.8f, -0.6f), new Vector3(2.3f, 0.5f, 4.2f), Art.EnemyOliveDark);
                    turret = Pivot(root, new Vector3(0f, 1.4f, -1f));
                    Art.Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.6f, 0f), new Vector3(2f, 1f, 3.2f), Art.EnemyOlive, new Vector3(-25f, 0f, 0f));
                    break;

                case EnemyType.Jammer:
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 1.3f, 0f), new Vector3(2.4f, 2f, 5.5f), Art.EnemyOlive);
                    turret = Pivot(root, new Vector3(0f, 2.3f, -1.5f));
                    Art.Part(PrimitiveType.Cylinder, turret, new Vector3(0f, 3f, 0f), new Vector3(0.18f, 3f, 0.18f), Art.Dark);
                    Art.Part(PrimitiveType.Cube, turret, new Vector3(0f, 6.1f, 0f), new Vector3(1.6f, 0.4f, 0.1f), Art.Metal);
                    break;

                case EnemyType.Depot:
                    for (int k = 0; k < 6; k++)
                        Art.Part(PrimitiveType.Cube, root, new Vector3((k % 3 - 1) * 2.2f, 0.5f + (k / 3) * 0.9f, (k / 3 - 0.5f) * 1.5f),
                            new Vector3(1.8f, 0.8f, 1.2f), new Color(0.36f, 0.32f, 0.2f));
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 2.6f, 0f), new Vector3(9f, 0.1f, 7f), Art.EnemyOliveDark);
                    break;

                case EnemyType.Radar:
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 0.6f, 1.6f), Art.EnemyOlive);
                    Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 1.5f, 0f), new Vector3(0.2f, 1.2f, 0.2f), Art.Dark);
                    turret = Pivot(root, new Vector3(0f, 2.7f, 0f));
                    Art.Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.4f, 0f), new Vector3(2.6f, 1.2f, 0.12f),
                        Art.Metal, new Vector3(-15f, 0f, 0f));
                    Art.Part(PrimitiveType.Sphere, turret, new Vector3(0f, 1.15f, 0f), Vector3.one * 0.22f, Art.EnemyRed, default, true);
                    break;
            }
        }

        static Transform Pivot(Transform parent, Vector3 localPosition)
        {
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = localPosition;
            return pivot;
        }

        float smokeTimer;

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            // Damage shows: smoke from half health down, thicker as it gets worse (only where we can see it).
            if (hp < maxHp * 0.5f && drawn && type != EnemyType.Command)
            {
                smokeTimer -= dt;
                if (smokeTimer <= 0f)
                {
                    float severity = 1f - hp / (maxHp * 0.5f);
                    smokeTimer = Mathf.Lerp(0.35f, 0.1f, severity);
                    Effects.DamageSmoke(AimPoint + Vector3.up * 0.5f, severity);
                }
            }
            switch (type)
            {
                case EnemyType.Tank:
                case EnemyType.APC:
                    Move(dt);
                    var escortTarget = TrackNearestDrone(Scale.D(30f), 40f, dt);
                    if (type == EnemyType.APC) Escort(escortTarget, dt);
                    break;
                case EnemyType.Truck:
                case EnemyType.Tanker:
                    Move(dt);
                    break;
                case EnemyType.Rocket:
                    Shell(dt);
                    break;
                case EnemyType.Jammer:
                    SpinTurret(25f * dt);
                    break;
                case EnemyType.AAGun:
                    Defend(dt);
                    break;
                case EnemyType.Radar:
                    SpinTurret(90f * dt);
                    break;
                case EnemyType.Artillery:
                    Shell(dt);
                    break;
            }
        }

        // ---- Mission set-up ----

        public void Park() => parked = true;

        // Drives the route from point startIndex on, at speed m/s; the game hears when it gets to the end.
        public void Drive(List<Vector3> path, int startIndex, float speed)
        {
            route = path;
            routeIndex = startIndex;
            moveSpeed = speed;
        }

        public float departAt; // a convoy vehicle waits until this time before it drives off
        public bool hardened;  // only heavy bombs really hurt it (a tenth of anything else gets through)

        // A heavier gun (Silent Corridor's): reaches high enough to catch a Nightjar and hits much harder.
        public void Upgun()
        {
            range = Scale.D(24f);
            damage = 18f; // enough to bring down a Nightjar (182 hp) that flies the corridor without pinning them first
            unitName = "Heavy AA";
        }

        protected override float Absorb(float amount) => hardened && !Combat.Heavy ? amount * 0.1f : amount;

        // ---- Suppression ----
        // An AA gun under fire keeps its head down: two seconds of steady fire (hits no more than 0.7 s apart) pins
        // it for 12 s, and a heavy hit (a missile, a blast nearby) for 25 s. A pinned gun neither aims nor fires.
        public float SuppressedUntil { get; private set; } = -100f;
        public bool Suppressed => Time.time < SuppressedUntil;
        float fireBegan = -100f, lastLightHit = -100f;

        void Suppress(float hit)
        {
            if (type != EnemyType.AAGun && type != EnemyType.APC) return;
            float now = Time.time;
            if (hit >= 25f)
            {
                Pin(25f);
                return;
            }
            if (now - lastLightHit > 0.7f) fireBegan = now;
            lastLightHit = now;
            if (now - fireBegan >= 2f) Pin(12f);
        }

        void Pin(float seconds)
        {
            bool was = Suppressed;
            SuppressedUntil = Mathf.Max(SuppressedUntil, Time.time + seconds);
            if (!was) SandboxGame.Instance.OnSuppressed(this, seconds);
        }
        public bool OnRoute => route != null;
        public List<Vector3> Route => route;

        // Seconds until this vehicle reaches the end of its route at its normal pace (counting any wait to set off).
        public float SecondsToRouteEnd()
        {
            if (route == null || moveSpeed <= 0f) return 0f;
            float metres = 0f;
            Vector3 at = transform.position;
            for (int i = routeIndex; i < route.Count; i++)
            {
                Vector3 step = route[i] - at;
                step.y = 0f;
                metres += step.magnitude;
                at = route[i];
            }
            return Mathf.Max(0f, departAt - Time.time) + metres / moveSpeed;
        }

        // Takes the other road at a fork: `path` matches the current route up to `fork`. False if it's already past.
        public bool Divert(List<Vector3> path, int fork)
        {
            if (route == null || route == path || routeIndex >= fork) return false;
            route = path;
            return true;
        }

        public void AttachStructure(GameObject building) => structure = building;

        // Invisible (and untargetable) until one of your drones comes close; see SandboxGame.SpotHidden.
        public void Hide(Vector3 searchArea)
        {
            Hidden = true;
            searchCenter = searchArea;
            ApplyVisibility();
        }

        public float RevealedAt { get; private set; } = -100f; // Time.time it was spotted, for the HUD's pulse

        public void Reveal()
        {
            if (!Hidden) return;
            Hidden = false;
            RevealedAt = Time.time;
            Intel = Intel.Confirmed;
            Observed = true;
            LastSeenPosition = transform.position;
            LastSeenTime = Time.time;
            ApplyVisibility();
            Effects.Marker(transform.position, new Color(1f, 0.85f, 0.2f));
            SandboxGame.Instance.OnSpotted(this);
        }

        // ---- Intel ----

        // What the briefing says about it: a static defence's position is known (Identified), a vehicle's only
        // roughly (a Contact within `radius` of where it really is).
        public void Brief(Intel level, float radius = 12f * Scale.Distance)
        {
            Intel = level;
            NumberContact();
            LastSeenPosition = transform.position;
            LastSeenTime = Time.time;
            if (level == Intel.Contact)
            {
                Vector2 off = Random.insideUnitCircle * radius * 0.7f;
                ContactPosition = Ground.OnGround(transform.position + new Vector3(off.x, 0f, off.y));
                ContactRadius = radius;
            }
            ApplyVisibility();
        }

        // Recon's verdict for this moment: whether anything of ours can see it, and whether up close.
        public void Observe(bool seen, bool close, float dt)
        {
            if (Hidden)
            {
                Observed = false;
                return; // camouflaged: only a drone right overhead finds it (SandboxGame.SpotHidden)
            }
            if (seen)
            {
                watchedFor += dt;
                if (Intel < Intel.Identified) SandboxGame.Instance.OnIdentified(this);
                bool wasConfirmed = Intel == Intel.Confirmed;
                // Confirmed up close, or after 1.2 s of steady watching.
                Intel = close || watchedFor > 1.2f || wasConfirmed ? Intel.Confirmed : Intel.Identified;
                if (!wasConfirmed && Intel == Intel.Confirmed && isDecoy) SandboxGame.Instance.OnDecoyExposed(this);
                if (!wasConfirmed && Intel == Intel.Confirmed && isDecoy) Achievements.DecoyExposed(this);
                LastSeenPosition = transform.position;
                LastSeenTime = Time.time;
            }
            else
            {
                watchedFor = 0f;
                if (IsMobile && Intel == Intel.Confirmed) Intel = Intel.Identified; // out of sight: no longer sure where
                if (IsMobile && Intel == Intel.Identified && Time.time - LastSeenTime > Recon.GhostSeconds)
                {
                    Intel = Intel.Contact; // the trail has gone cold
                    ContactPosition = LastSeenPosition;
                    ContactRadius = Scale.D(18f);
                }
            }
            Observed = seen;
            NumberContact();
            ApplyVisibility();
        }

        // Something gave its position away (a gun firing): it's on the map from now on.
        void Expose()
        {
            if (Hidden || Intel >= Intel.Identified) return;
            Intel = Intel.Identified;
            LastSeenPosition = transform.position;
            LastSeenTime = Time.time;
            ApplyVisibility();
            SandboxGame.Instance.OnIdentified(this);
        }

        // A flash on the horizon: roughly where something fired from.
        void Flash()
        {
            if (Hidden || Intel >= Intel.Contact) return;
            Vector2 off = Random.insideUnitCircle * Scale.D(8f);
            ContactPosition = Ground.OnGround(transform.position + new Vector3(off.x, 0f, off.y));
            ContactRadius = Scale.D(14f);
            Intel = Intel.Contact;
        }

        // Whether the model is drawn: vehicles only while watched; static defences once found; the command post
        // is a building everyone can see.
        public bool Drawn => drawn;
        bool ShouldDraw => !Hidden && (Recon.ShowAll || Observed || type == EnemyType.Command || (!IsMobile && Intel >= Intel.Identified));

        void ApplyVisibility()
        {
            bool want = ShouldDraw;
            if (renderers != null && want == drawn) return;
            renderers ??= GetComponentsInChildren<Renderer>(true);
            drawn = want;
            foreach (var r in renderers) if (r != null) r.enabled = want;
        }

        // Where the HUD marks it and where an order aimed at it goes: where it really is while we can see it, else
        // where it was last seen, else the rough contact position.
        public Vector3 MarkerPosition => drawn || Observed ? AimPoint
            : Intel >= Intel.Identified ? LastSeenPosition + Vector3.up * aimHeight : ContactPosition;

        public bool Known => !Hidden && Intel >= Intel.Contact;

        // A contact number (C-01, C-02...) given the first time it's reported, the same in every view.
        public string ContactId { get; private set; }
        static int nextContact = 1;
        public static void ResetContactIds() => nextContact = 1;

        void NumberContact()
        {
            if (ContactId == null && Intel >= Intel.Contact) ContactId = $"C-{nextContact++:00}";
        }

        // A decoy passes for the real thing until something has had a good look at it (Confirmed).
        public bool LooksLikeTarget => isTarget || (isDecoy && Intel < Intel.Confirmed);
        public bool ExposedDecoy => isDecoy && Intel == Intel.Confirmed;

        // ---- Reactions ----

        // A drone this close: does this unit notice it? Radars see furthest and warn the guns they cover.
        public void Notice(Drone drone, float distance, float dark)
        {
            if (isTarget && SandboxGame.Instance.IsShadow) return; // a shadowed column grows suspicious instead (SandboxGame)
            float range = type == EnemyType.Radar ? RadarCoverage + Scale.D(10f)
                : type == EnemyType.AAGun ? CurrentRange + Scale.D(6f)
                : Scale.D(IsMobile ? 26f : 20f);
            range *= Mathf.Lerp(1f, 0.7f, dark) * Recon.WeatherFactor * Wariness;
            if (distance * Difficulty.AlertReactionFactor > range) return;
            Alert(drone.transform.position);
            if (type == EnemyType.Radar && Alerted)
            {
                foreach (var e in SandboxGame.Instance.enemies)
                {
                    if (e == null || e == this) continue;
                    Vector3 offset = e.transform.position - transform.position;
                    offset.y = 0f;
                    if (offset.magnitude <= RadarCoverage) e.Alert(drone.transform.position, false);
                }
            }
        }

        // Warn once at the first trouble; Recruit gets a moment before the alarm changes behaviour.
        public void Alert(Vector3 threat, bool loud = true)
        {
            bool was = Time.time - AlertedAt < alertSeconds; // includes the warning grace, so sightings cannot restart it
            AlertedAt = Time.time;
            trouble = threat;
            if (was) return;
            alertBegan = Time.time;
            alertReadyAt = Time.time + Difficulty.AlertWarningSeconds;
            if (loud && Intel >= Intel.Identified) SandboxGame.Instance.OnEnemyAlerted(this);
            if (IsMobile && route == null && parked) fleeTo = FleePoint(threat);
            else if (IsMobile && route == null && !parked) investigating = true;
        }

        // Hit, or a neighbour hit: everyone close by is on alert, looking toward where it came from.
        protected override void OnHit()
        {
            Suppress(LastDamage);
            Vector3 from = transform.position;
            var shooter = Combat.NearestHostile(AimPoint, team, Scale.D(60f), true);
            if (shooter != null) from = shooter.transform.position;
            Alert(from);
            AlertNeighbours(from);
        }

        void AlertNeighbours(Vector3 threat)
        {
            foreach (var e in SandboxGame.Instance.enemies)
            {
                if (e == null || e == this || !e.IsAlive) continue;
                Vector3 offset = e.transform.position - transform.position;
                offset.y = 0f;
                if (offset.magnitude <= AlertShare * Wariness) e.Alert(threat, false);
            }
        }

        // Somewhere 30-45 m away from the threat, inside enemy country, to drive off to and park again.
        Vector3 FleePoint(Vector3 threat)
        {
            Vector3 away = transform.position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
            away.y = 0f;
            away = Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f) * away.normalized;
            Vector3 point = transform.position + away * Random.Range(30f, 45f) * Scale.Distance;
            float limit = SandboxGame.MapHalfSize - Scale.D(10f);
            point.x = Mathf.Clamp(point.x, -limit, limit);
            point.z = Mathf.Clamp(point.z, -limit, limit);
            return Ground.OnGround(point);
        }

        protected override void Die()
        {
            if (structure != null) Effects.Burn(structure, transform.position + Vector3.up * 6f);
            // Fuel and ammunition go up with a much bigger bang.
            if (type == EnemyType.Tanker || type == EnemyType.Depot) Effects.Explosion(AimPoint + Vector3.up, radius * 2.5f);
            AlertNeighbours(transform.position);
            base.Die();
        }

        // ---- Behaviour ----

        // True while the vehicle is driving (bombs miss moving targets more often).
        public bool IsMoving { get; private set; }

        void Move(float dt)
        {
            Vector3 before = transform.position;
            if (route != null) FollowRoute(dt);
            else if (fleeTo.HasValue) Flee(dt);
            else if (!parked) Patrol(dt);
            IsMoving = (transform.position - before).sqrMagnitude > 0.0001f;
        }

        void FollowRoute(float dt)
        {
            if (Time.time < departAt) return;
            if (routeIndex >= route.Count)
            {
                route = null;
                SandboxGame.Instance.OnEnemyEscaped(this);
                return;
            }
            // On alert a convoy spreads out: each vehicle drifts a few metres off the road line, its own way.
            Vector3 point = route[routeIndex];
            if (Alerted && routeIndex + 1 < route.Count)
            {
                Vector3 along = route[routeIndex + 1] - point;
                along.y = 0f;
                point += Vector3.Cross(Vector3.up, along.normalized) * SpreadOffset;
            }
            Vector3 toPoint = point - transform.position;
            toPoint.y = 0f;
            if (toPoint.magnitude < 2.5f)
            {
                routeIndex++;
                return;
            }
            float wanted = Quaternion.LookRotation(toPoint).eulerAngles.y;
            heading = Mathf.MoveTowardsAngle(heading, wanted, 120f * dt);
            float dash = Alerted ? 1.25f + 0.2f * Mathf.Abs(SpreadOffset) / 4f : 1f; // a convoy under attack puts its foot down
            transform.position += Quaternion.Euler(0f, heading, 0f) * Vector3.forward * moveSpeed * dash * dt;
            SettleOnGround(8f * dt);
            if (Alerted) KickUpDust(dt);
        }

        // Each vehicle's own way of spreading out (-4 to 4 m), rolled the first time it's needed (Random can't be
        // called from a field initialiser, which runs in the constructor).
        float SpreadOffset
        {
            get
            {
                if (float.IsNaN(spreadOffset)) spreadOffset = Random.Range(-4f, 4f);
                return spreadOffset;
            }
        }
        float spreadOffset = float.NaN;

        float dustTimer;

        // Dust thrown up behind a vehicle driving hard (only drawn where we can see it).
        void KickUpDust(float dt)
        {
            if (!drawn) return;
            dustTimer -= dt;
            if (dustTimer > 0f) return;
            dustTimer = 0.12f;
            Effects.Dust(transform.position - transform.forward * radius);
        }

        // A parked vehicle that's been spotted or shot at drives off to park somewhere else.
        void Flee(float dt)
        {
            if (Time.time < alertReadyAt) return;
            Vector3 to = fleeTo.Value - transform.position;
            to.y = 0f;
            if (to.magnitude < 2f)
            {
                fleeTo = null;
                return;
            }
            float wanted = Quaternion.LookRotation(to).eulerAngles.y;
            heading = Mathf.MoveTowardsAngle(heading, wanted, 90f * dt);
            if (Mathf.Abs(Mathf.DeltaAngle(heading, wanted)) < 50f)
                transform.position += Quaternion.Euler(0f, heading, 0f) * Vector3.forward * moveSpeed * 1.3f * dt;
            SettleOnGround(8f * dt);
            KickUpDust(dt);
        }

        // Fires a round now and then: a flash and smoke at the muzzle and the barrel kicking back. The flash
        // shows even while the gun is hidden, a clue to where it is.
        void Shell(float dt)
        {
            cooldown -= dt;
            if (barrel != null) barrel.localPosition = Vector3.Lerp(barrel.localPosition, barrelRest, 4f * dt);
            if (cooldown > 0f) return;
            cooldown = Random.Range(6f, 10f);
            Vector3 muzzle = barrel != null ? barrel.TransformPoint(new Vector3(0f, 0f, 4.2f)) : AimPoint + transform.forward * 3f;
            Effects.Explosion(muzzle, 1.1f);
            Flash();
            if (barrel != null) barrel.localPosition = barrelRest - barrel.localRotation * Vector3.forward * 0.6f; // kicks back along the bore
        }

        void Patrol(float dt)
        {
            if (investigating && Time.time >= alertReadyAt)
            {
                // Off to see where the trouble was, then back to the rounds.
                investigating = false;
                waypoint = Ground.OnGround(trouble + Random.insideUnitSphere * 6f);
                waitTimer = 0f;
            }
            if (waitTimer > 0f)
            {
                waitTimer -= dt;
                return;
            }
            Vector3 toWaypoint = waypoint - transform.position;
            toWaypoint.y = 0f;
            if (toWaypoint.magnitude < 1.5f)
            {
                waitTimer = Random.Range(1f, 4f);
                waypoint = SandboxGame.Instance.RandomEnemyPoint();
                return;
            }
            float wanted = Quaternion.LookRotation(toWaypoint).eulerAngles.y;
            heading = Mathf.MoveTowardsAngle(heading, wanted, 50f * dt);
            if (Mathf.Abs(Mathf.DeltaAngle(heading, wanted)) < 25f)
                transform.position += Quaternion.Euler(0f, heading, 0f) * Vector3.forward * moveSpeed * dt;
            SettleOnGround(8f * dt);
        }

        // Sits the unit on the terrain, tilted to match the slope. blend 1 snaps straight there.
        void SettleOnGround(float blend)
        {
            Vector3 p = Ground.OnGround(transform.position);
            transform.position = p;
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, Ground.NormalAt(p));
            Quaternion target = tilt * Quaternion.Euler(0f, heading, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, Mathf.Clamp01(blend));
        }

        // The turret's facing with its rest pose taken out, in world space.
        Quaternion TurretFacing => turret.parent.rotation * turret.localRotation * Quaternion.Inverse(turretRest);

        void SpinTurret(float degrees)
        {
            if (turret == null) return;
            turret.localRotation = Quaternion.AngleAxis(degrees, Vector3.up) * turret.localRotation;
        }

        // Turns the turret toward the closest drone. Returns that drone, or null if none is in range.
        Unit TrackNearestDrone(float trackRange, float turnSpeed, float dt)
        {
            var target = Combat.NearestHostile(AimPoint, team, trackRange, false);
            if (target == null || turret == null) return target;
            Vector3 local = turret.parent.InverseTransformDirection(target.AimPoint - turret.position);
            local.y = 0f;
            if (local.sqrMagnitude > 0.01f)
            {
                Quaternion aim = Quaternion.LookRotation(local) * turretRest;
                turret.localRotation = Quaternion.RotateTowards(turret.localRotation, aim, turnSpeed * dt);
            }
            return target;
        }

        // An APC's machine gun: once alerted it fires on a drone that comes within 16 m (short bursts, half of them
        // hitting), so diving on a convoy it escorts costs something. Pinned like an AA gun by fire.
        void Escort(Unit nearest, float dt)
        {
            cooldown -= dt;
            if (!Alerted || nearest == null || Suppressed || cooldown > 0f) return;
            if (Vector3.Distance(nearest.AimPoint, AimPoint) > Scale.D(16f)) return;
            cooldown = 0.6f;
            Vector3 muzzle = AimPoint + Vector3.up * 0.8f;
            Projectile.Bullet(muzzle, nearest, team, 4f, 0.5f, new Color(1f, 0.6f, 0.3f));
            Sound.At("gunshot", muzzle, 0.5f);
            Effects.Spark(muzzle);
            LastShotTime = Time.time;
        }

        void Defend(float dt)
        {
            cooldown -= dt;
            if (Suppressed)
            {
                acquiring = 0f;
                TrackedDrone = null;
                AcquireProgress = 0f;
                return;
            }
            var target = TrackNearestDrone(CurrentRange, 240f, dt);
            if (target == null)
            {
                SpinTurret(20f * dt); // idle scan
                acquiring = 0f;
                TrackedDrone = null;
                AcquireProgress = 0f;
                return;
            }
            if (acquiring == 0f || target != TrackedDrone) Sound.Alert("warning", 3f); // a gun has started laying on a drone
            TrackedDrone = target;
            // Laying the guns on a drone takes the crew a moment: 1.6 s if it takes them by surprise, 0.5 s if
            // they were already on alert or a radar is feeding them.
            if (acquiring == 0f) acquireBegan = Time.time;
            acquiring += dt;
            bool warned = SandboxGame.Instance.RadarCovers(transform.position) || (Alerted && alertBegan < acquireBegan - 0.5f);
            float needed = (warned ? 0.5f : 1.6f) * Difficulty.AALockTimeFactor;
            AcquireProgress = Mathf.Clamp01(acquiring / needed);
            if (acquiring < needed) return;
            if (!Alerted) Alert(target.transform.position);
            if (cooldown <= 0f)
            {
                cooldown = fireInterval;
                Reveal(); // firing gives a hidden gun away
                Expose(); // and the tracers show where it is
                Vector3 muzzle = turret != null ? turret.position + TurretFacing * muzzleLocal : AimPoint;
                Projectile.Bullet(muzzle, target, team, damage, 0.6f, new Color(1f, 0.45f, 0.2f));
                Sound.At("cannon", muzzle, 0.7f);
                Effects.Spark(muzzle); // the muzzle flash
                LastShotTime = Time.time;
            }
        }
    }
}

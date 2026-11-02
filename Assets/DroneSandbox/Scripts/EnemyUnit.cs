using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum EnemyType { Tank, Truck, AAGun, Radar, Artillery, Command }

    // Enemy targets. Tanks and trucks patrol (or, in missions, park or drive a convoy route), AA guns shoot
    // at drones, a radar extends the range of nearby AA guns, artillery shells your lines, and the command
    // post is the HQ building itself.
    public class EnemyUnit : Unit
    {
        public const float RadarCoverage = 35f; // AA guns this close to a radar get extra range
        public const float RadarRangeBonus = 6f;

        public EnemyType type;
        public bool isTarget;          // a mission objective
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

        public bool IsMobile => type == EnemyType.Tank || type == EnemyType.Truck;

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
            }
            hp = maxHp;
            if (enemyType == EnemyType.Command) return; // the HQ building in the world is its model

            // Use the Blender model from Resources/Models if there is one; otherwise primitive shapes.
            var model = Art.TryModel("Enemy_" + enemyType, transform);
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
                    foreach (float side in new[] { -0.6f, 0.6f })
                        DayNight.AddNightLight(transform, new Vector3(side, 0.95f, 1.9f), new Vector3(8f, 0f, 0f), LightType.Spot,
                            new Color(1f, 0.92f, 0.75f), 6f, 30f, 70f);
                muzzleLocal = new Vector3(0f, 1.8f, 1.7f);
            }
            else
            {
                BuildPrimitiveModel(enemyType);
            }
            if (IsMobile) waypoint = SandboxGame.Instance.RandomEnemyPoint();
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
                        Factions.Paint(transform, new Vector3(side * 0.92f, 0.6f, 0.35f), new Vector3(side, 0f, 0f), 0.55f, false);
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

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            switch (type)
            {
                case EnemyType.Tank:
                    Move(dt);
                    TrackNearestDrone(30f, 40f, dt);
                    break;
                case EnemyType.Truck:
                    Move(dt);
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

        public void AttachStructure(GameObject building) => structure = building;

        // Invisible (and untargetable) until one of your drones comes close; see SandboxGame.SpotHidden.
        public void Hide(Vector3 searchArea)
        {
            Hidden = true;
            searchCenter = searchArea;
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        public void Reveal()
        {
            if (!Hidden) return;
            Hidden = false;
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
            Effects.Marker(transform.position, new Color(1f, 0.85f, 0.2f));
            SandboxGame.Instance.OnSpotted(this);
        }

        protected override void Die()
        {
            if (structure != null) Effects.Burn(structure, transform.position + Vector3.up * 6f);
            base.Die();
        }

        // ---- Behaviour ----

        void Move(float dt)
        {
            if (route != null) FollowRoute(dt);
            else if (!parked) Patrol(dt);
        }

        void FollowRoute(float dt)
        {
            if (routeIndex >= route.Count)
            {
                route = null;
                SandboxGame.Instance.OnEnemyEscaped(this);
                return;
            }
            Vector3 toPoint = route[routeIndex] - transform.position;
            toPoint.y = 0f;
            if (toPoint.magnitude < 2f)
            {
                routeIndex++;
                return;
            }
            float wanted = Quaternion.LookRotation(toPoint).eulerAngles.y;
            heading = Mathf.MoveTowardsAngle(heading, wanted, 120f * dt);
            transform.position += Quaternion.Euler(0f, heading, 0f) * Vector3.forward * moveSpeed * dt;
            SettleOnGround(8f * dt);
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
            if (barrel != null) barrel.localPosition = barrelRest - Vector3.forward * 0.6f;
        }

        void Patrol(float dt)
        {
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

        void Defend(float dt)
        {
            cooldown -= dt;
            var target = TrackNearestDrone(CurrentRange, 240f, dt);
            if (target == null)
            {
                SpinTurret(20f * dt); // idle scan
                return;
            }
            if (cooldown <= 0f)
            {
                cooldown = fireInterval;
                Reveal(); // firing gives a hidden gun away
                Vector3 muzzle = turret != null ? turret.position + TurretFacing * muzzleLocal : AimPoint;
                Projectile.Bullet(muzzle, target, team, damage, 0.6f, new Color(1f, 0.45f, 0.2f));
            }
        }
    }
}

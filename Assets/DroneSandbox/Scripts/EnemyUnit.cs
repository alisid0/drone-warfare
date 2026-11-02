using UnityEngine;

namespace DroneSandbox
{
    public enum EnemyType { Tank, Truck, AAGun, Radar }

    // Enemy targets. Tanks and trucks patrol, AA guns shoot at drones,
    // and a radar extends the range of nearby AA guns.
    public class EnemyUnit : Unit
    {
        public const float RadarCoverage = 35f; // AA guns this close to a radar get extra range
        public const float RadarRangeBonus = 6f;

        public EnemyType type;
        float moveSpeed;
        Vector3 waypoint;
        float waitTimer;
        Transform turret; // tank turret, AA mount or radar dish
        float range, damage, fireInterval, cooldown;

        public bool IsMobile => type == EnemyType.Tank || type == EnemyType.Truck;

        public float CurrentRange =>
            range + (SandboxGame.Instance.RadarCovers(transform.position) ? RadarRangeBonus : 0f);

        // How far out this AA gun can hit a drone flying at the given altitude.
        public float ThreatRadiusAtAltitude(float flightAltitude)
        {
            float r = CurrentRange;
            float h = flightAltitude - aimHeight;
            return Mathf.Sqrt(Mathf.Max(0f, r * r - h * h));
        }

        public static EnemyUnit Create(EnemyType type, Vector3 position)
        {
            var go = new GameObject(type.ToString());
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var unit = go.AddComponent<EnemyUnit>();
            unit.Setup(type);
            return unit;
        }

        void Setup(EnemyType enemyType)
        {
            type = enemyType;
            team = Team.Enemy;
            Transform root = transform;

            switch (enemyType)
            {
                case EnemyType.Tank:
                    unitName = "Tank";
                    maxHp = 160f; moveSpeed = 3f; radius = 1.8f; aimHeight = 0.8f;
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.5f, 0f), new Vector3(2.0f, 0.7f, 3.0f), Art.EnemyOlive);
                    foreach (float side in new[] { -1.05f, 1.05f })
                        Art.Part(PrimitiveType.Cube, root, new Vector3(side, 0.35f, 0f), new Vector3(0.45f, 0.65f, 3.1f), Art.Dark);
                    turret = Pivot(root, new Vector3(0f, 1.05f, -0.1f));
                    Art.Part(PrimitiveType.Cube, turret, Vector3.zero, new Vector3(1.3f, 0.45f, 1.5f), Art.EnemyOliveDark);
                    Art.Part(PrimitiveType.Cylinder, turret, new Vector3(0f, 0f, 1.6f), new Vector3(0.16f, 1.1f, 0.16f),
                        Art.Dark, new Vector3(90f, 0f, 0f));
                    break;

                case EnemyType.Truck:
                    unitName = "Truck";
                    maxHp = 60f; moveSpeed = 5.5f; radius = 1.4f; aimHeight = 0.9f;
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.95f, 1.1f), new Vector3(1.6f, 1.2f, 1.2f), Art.EnemyOlive);
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.9f, -0.6f), new Vector3(1.7f, 1.0f, 2.2f),
                        new Color(0.55f, 0.50f, 0.35f));
                    foreach (float x in new[] { -0.85f, 0.85f })
                        foreach (float z in new[] { -1.0f, 1.0f })
                            Art.Part(PrimitiveType.Cylinder, root, new Vector3(x, 0.32f, z), new Vector3(0.6f, 0.12f, 0.6f),
                                Art.Dark, new Vector3(0f, 0f, 90f));
                    break;

                case EnemyType.AAGun:
                    unitName = "AA Gun";
                    maxHp = 90f; radius = 1.5f; aimHeight = 0.9f;
                    range = 18f; damage = 6f; fireInterval = 0.5f;
                    Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.15f, 0f), new Vector3(2.4f, 0.15f, 2.4f), Art.EnemyOliveDark);
                    turret = Pivot(root, new Vector3(0f, 0.7f, 0f));
                    Art.Part(PrimitiveType.Cube, turret, Vector3.zero, new Vector3(1.0f, 0.6f, 1.0f), Art.EnemyOlive);
                    var elevation = Pivot(turret, new Vector3(0f, 0.15f, 0.2f));
                    elevation.localRotation = Quaternion.Euler(-35f, 0f, 0f);
                    foreach (float side in new[] { -0.22f, 0.22f })
                        Art.Part(PrimitiveType.Cylinder, elevation, new Vector3(side, 0f, 0.9f), new Vector3(0.1f, 0.9f, 0.1f),
                            Art.Dark, new Vector3(90f, 0f, 0f));
                    Art.Part(PrimitiveType.Sphere, turret, new Vector3(0f, 0.4f, -0.3f), Vector3.one * 0.2f, Art.EnemyRed, default, true);
                    break;

                case EnemyType.Radar:
                    unitName = "Radar";
                    maxHp = 70f; radius = 1.4f; aimHeight = 1.5f;
                    Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 0.6f, 1.6f), Art.EnemyOlive);
                    Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 1.5f, 0f), new Vector3(0.2f, 1.2f, 0.2f), Art.Dark);
                    turret = Pivot(root, new Vector3(0f, 2.7f, 0f));
                    Art.Part(PrimitiveType.Cube, turret, new Vector3(0f, 0.4f, 0f), new Vector3(2.6f, 1.2f, 0.12f),
                        Art.Metal, new Vector3(-15f, 0f, 0f));
                    Art.Part(PrimitiveType.Sphere, turret, new Vector3(0f, 1.15f, 0f), Vector3.one * 0.22f, Art.EnemyRed, default, true);
                    break;
            }
            hp = maxHp;
            if (IsMobile) waypoint = SandboxGame.Instance.RandomEnemyPoint();
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
                    Patrol(dt);
                    TrackNearestDrone(30f, 40f, dt);
                    break;
                case EnemyType.Truck:
                    Patrol(dt);
                    break;
                case EnemyType.AAGun:
                    Defend(dt);
                    break;
                case EnemyType.Radar:
                    turret.Rotate(0f, 90f * dt, 0f, Space.Self);
                    break;
            }
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
            if (toWaypoint.magnitude < 1f)
            {
                waitTimer = Random.Range(1f, 4f);
                waypoint = SandboxGame.Instance.RandomEnemyPoint();
                return;
            }
            Quaternion look = Quaternion.LookRotation(toWaypoint);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 50f * dt);
            if (Quaternion.Angle(transform.rotation, look) < 25f)
                transform.position += transform.forward * moveSpeed * dt;
        }

        // Turns the turret toward the closest drone. Returns that drone, or null if none is in range.
        Unit TrackNearestDrone(float trackRange, float turnSpeed, float dt)
        {
            var target = Combat.NearestHostile(AimPoint, team, trackRange, false);
            if (target == null) return null;
            Vector3 flat = target.AimPoint - turret.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
                turret.rotation = Quaternion.RotateTowards(turret.rotation, Quaternion.LookRotation(flat), turnSpeed * dt);
            return target;
        }

        void Defend(float dt)
        {
            cooldown -= dt;
            var target = TrackNearestDrone(CurrentRange, 240f, dt);
            if (target == null)
            {
                turret.Rotate(0f, 20f * dt, 0f, Space.Self); // idle scan
                return;
            }
            if (cooldown <= 0f)
            {
                cooldown = fireInterval;
                Vector3 muzzle = turret.position + turret.forward * 1.2f + Vector3.up * 0.6f;
                Projectile.Bullet(muzzle, target, team, damage, 0.6f, new Color(1f, 0.45f, 0.2f));
            }
        }
    }
}

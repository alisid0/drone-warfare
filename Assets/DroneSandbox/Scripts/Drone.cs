using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum DroneType { Scout, Strike, Kamikaze }

    // A player drone. It flies at its own cruise altitude, follows move and attack
    // orders, and fires on enemies in range by itself when it has nothing to do.
    public class Drone : Unit
    {
        public DroneType type;
        public float speed;
        public float altitude;
        public float weaponRange;
        public float weaponDamage;
        public float fireInterval;

        Vector3? moveTarget;
        Unit attackTarget;
        bool attackOrdered; // false when the drone picked the target itself
        float fireCooldown;
        Vector3 velocity;
        float bobOffset;
        Transform body;
        readonly List<Transform> rotors = new List<Transform>();

        public bool IsKamikaze => type == DroneType.Kamikaze;

        public string Status
        {
            get
            {
                if (attackTarget != null) return (IsKamikaze ? "Diving on " : "Attacking ") + attackTarget.unitName;
                if (moveTarget.HasValue) return "Moving";
                return "Hovering";
            }
        }

        public static Drone Create(DroneType type, Vector3 position)
        {
            var go = new GameObject(type + " Drone");
            go.transform.position = position;
            var drone = go.AddComponent<Drone>();
            drone.Setup(type);
            return drone;
        }

        void Setup(DroneType droneType)
        {
            type = droneType;
            team = Team.Player;
            aimHeight = 0f;
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);

            float modelScale = 1f; // models are drawn bigger than life so they read from the RTS camera
            switch (droneType)
            {
                case DroneType.Scout:
                    unitName = "Scout";
                    maxHp = 40f; speed = 16f; altitude = 7f; radius = 0.8f;
                    weaponRange = 11f; weaponDamage = 5f; fireInterval = 0.25f;
                    modelScale = 1.6f;
                    break;

                case DroneType.Strike:
                    unitName = "Strike";
                    maxHp = 70f; speed = 10f; altitude = 9f; radius = 1.1f;
                    weaponRange = 20f; weaponDamage = 45f; fireInterval = 2.2f;
                    modelScale = 1.4f;
                    break;

                case DroneType.Kamikaze:
                    unitName = "Kamikaze";
                    maxHp = 25f; speed = 20f; altitude = 6f; radius = 0.6f;
                    weaponRange = 0f; weaponDamage = 110f; fireInterval = 0f;
                    modelScale = 2.2f;
                    break;
            }
            hp = maxHp;
            bobOffset = Random.value * 10f;

            // Use the Blender model from Resources/Models if there is one; otherwise primitive shapes.
            var model = Art.TryModel("Drone_" + droneType, body, modelScale);
            if (model != null)
            {
                foreach (var t in model.GetComponentsInChildren<Transform>())
                    if (t.name.StartsWith("Prop")) rotors.Add(t);
            }
            else
            {
                BuildPrimitiveModel(droneType);
            }
        }

        void BuildPrimitiveModel(DroneType droneType)
        {
            switch (droneType)
            {
                case DroneType.Scout:
                    BuildQuadcopter(0.6f, 0.5f, Art.PlayerBlue);
                    Art.Part(PrimitiveType.Sphere, body, new Vector3(0f, -0.15f, 0.25f), Vector3.one * 0.2f, Art.Dark); // camera
                    break;
                case DroneType.Strike:
                    BuildQuadcopter(1.0f, 0.8f, Art.PlayerDeepBlue);
                    foreach (float side in new[] { -0.35f, 0.35f })
                        Art.Part(PrimitiveType.Capsule, body, new Vector3(side, -0.25f, 0.1f), new Vector3(0.16f, 0.35f, 0.16f),
                            Color.white, new Vector3(90f, 0f, 0f)); // missile pods
                    break;
                case DroneType.Kamikaze:
                    BuildQuadcopter(0.45f, 0.4f, Art.KamikazeOrange);
                    Art.Part(PrimitiveType.Capsule, body, new Vector3(0f, -0.05f, 0.4f), new Vector3(0.22f, 0.3f, 0.22f),
                        Art.EnemyRed, new Vector3(90f, 0f, 0f)); // warhead
                    break;
            }
        }

        void BuildQuadcopter(float size, float propSize, Color color)
        {
            Art.Part(PrimitiveType.Cube, body, Vector3.zero, new Vector3(size, size * 0.3f, size), color);
            float arm = size * 0.9f;
            for (int i = 0; i < 4; i++)
            {
                float angle = (45f + 90f * i) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var armPart = Art.Part(PrimitiveType.Cube, body, dir * arm * 0.5f, new Vector3(0.08f, 0.06f, arm), Art.Dark);
                armPart.transform.localRotation = Quaternion.LookRotation(dir);

                var rotor = new GameObject("Rotor").transform;
                rotor.SetParent(body, false);
                rotor.localPosition = dir * arm + Vector3.up * 0.1f;
                Art.Part(PrimitiveType.Cube, rotor, Vector3.zero, new Vector3(propSize, 0.02f, 0.07f), Art.Metal);
                rotors.Add(rotor);
            }
        }

        // ---- Orders ----

        public void MoveTo(Vector3 point)
        {
            moveTarget = point;
            attackTarget = null;
        }

        public void Attack(Unit target)
        {
            attackTarget = target;
            attackOrdered = true;
            moveTarget = null;
        }

        public void Stop()
        {
            moveTarget = null;
            attackTarget = null;
        }

        // ---- Behaviour ----

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            fireCooldown -= dt;

            if (attackTarget != null && !attackTarget.IsAlive) attackTarget = null;

            // Idle gun and missile drones engage the nearest enemy in range on their own.
            if (attackTarget == null && !moveTarget.HasValue && !IsKamikaze)
            {
                var nearest = Combat.NearestHostile(transform.position, team, weaponRange, true);
                if (nearest != null)
                {
                    attackTarget = nearest;
                    attackOrdered = false;
                }
            }

            float bob = Mathf.Sin((Time.time + bobOffset) * 2f) * 0.25f;
            var goal = new Vector3(transform.position.x, CruiseHeight(transform.position) + bob, transform.position.z); // default: hold position
            bool diving = false;

            if (attackTarget != null)
            {
                Vector3 targetPos = attackTarget.AimPoint;
                if (IsKamikaze)
                {
                    goal = targetPos;
                    diving = true;
                    if (Vector3.Distance(transform.position, targetPos) < attackTarget.radius + 0.6f)
                    {
                        Detonate();
                        return;
                    }
                }
                else
                {
                    Vector3 flat = targetPos - transform.position;
                    flat.y = 0f;
                    float distance = flat.magnitude;
                    if (!attackOrdered && distance > weaponRange * 1.3f)
                    {
                        attackTarget = null; // auto-picked target got away
                    }
                    else
                    {
                        if (distance > weaponRange * 0.9f) goal = new Vector3(targetPos.x, CruiseHeight(targetPos) + bob, targetPos.z);
                        if (distance <= weaponRange && fireCooldown <= 0f)
                        {
                            Fire();
                            fireCooldown = fireInterval;
                        }
                    }
                }
            }
            else if (moveTarget.HasValue)
            {
                Vector3 m = moveTarget.Value;
                goal = new Vector3(m.x, CruiseHeight(m) + bob, m.z);
                Vector3 flat = goal - transform.position;
                flat.y = 0f;
                if (flat.magnitude < 0.6f) moveTarget = null;
            }

            Fly(goal, diving, dt);
        }

        void Fly(Vector3 goal, bool diving, float dt)
        {
            Vector3 toGoal = goal - transform.position;
            Vector3 desired = diving ? toGoal.normalized * speed : Vector3.ClampMagnitude(toGoal * 2f, speed);
            if (!diving) desired += Separation() * 4f;
            velocity = Vector3.MoveTowards(velocity, desired, speed * 2.5f * dt);
            transform.position += velocity * dt;

            // Never fly into a hillside (a kamikaze dive is meant to hit the ground).
            float floor = Ground.HeightAt(transform.position) + 1.2f;
            if (!diving && transform.position.y < floor)
            {
                transform.position = new Vector3(transform.position.x, floor, transform.position.z);
                velocity.y = Mathf.Max(velocity.y, 0f);
            }

            // Face the direction of travel, or the target while hovering.
            var flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 facing = flatVelocity;
            if (attackTarget != null && flatVelocity.magnitude < 2f)
            {
                facing = attackTarget.AimPoint - transform.position;
                facing.y = 0f;
            }
            if (facing.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), 360f * dt);

            // Lean into the direction of travel like a real multirotor, and spin the props.
            Vector3 local = transform.InverseTransformDirection(flatVelocity);
            body.localRotation = Quaternion.Euler(local.z / speed * 20f, 0f, -local.x / speed * 20f);
            foreach (var rotor in rotors) rotor.Rotate(body.up, 1800f * dt, Space.World);
        }

        // Cruise altitude is above the ground, taking the higher of here and the destination so the drone
        // climbs before crossing a hill rather than after.
        float CruiseHeight(Vector3 destination) =>
            Mathf.Max(Ground.HeightAt(destination), Ground.HeightAt(transform.position)) + altitude;

        // Pushes drones apart so a group doesn't stack in one spot.
        Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            foreach (var other in SandboxGame.Instance.drones)
            {
                if (other == this) continue;
                Vector3 away = transform.position - other.transform.position;
                away.y = 0f;
                float distance = away.magnitude;
                float minimum = (radius + other.radius) * 1.4f;
                if (distance < minimum && distance > 0.001f) push += away / distance * (minimum - distance);
            }
            return push;
        }

        void Fire()
        {
            Vector3 muzzle = transform.position + transform.forward * radius * 0.6f + Vector3.down * 0.2f;
            if (type == DroneType.Scout)
                Projectile.Bullet(muzzle, attackTarget, team, weaponDamage, 0.8f, new Color(1f, 0.9f, 0.4f));
            else
                Projectile.Missile(muzzle, attackTarget, team, weaponDamage, 3f, transform.forward);
        }

        void Detonate()
        {
            hp = 0f;
            Combat.Blast(transform.position, 5f, weaponDamage, team);
            Effects.Explosion(transform.position, 5f);
            SandboxGame.Instance.OnDroneExpended(this);
            Destroy(gameObject);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    public enum DroneType { Scout, Strike, Kamikaze, Bomber }

    // A player drone. It flies at its own cruise altitude, follows move and attack orders (moves can be queued
    // as a chain of waypoints, and flown as an attack-move that engages whatever comes in range on the way), and
    // fires on enemies in range by itself when it has nothing to do.
    // The Nightjar bomber (from the tech shop) drops bombs instead, then flies home to rearm.
    public class Drone : Unit
    {
        public const int BomberLoad = 3;
        public const int StrikeLoad = 4; // missiles a Strike drone carries before it flies home to reload
        const float RearmSeconds = 15f;
        const float BombHitMoving = 0.7f, BombHitStill = 0.95f; // odds a bomb lands on target

        public DroneType type;
        public float speed;
        public float altitude;
        public float weaponRange;
        public float weaponDamage;
        public float fireInterval;

        Vector3? moveTarget;
        readonly List<Vector3> waypoints = new List<Vector3>(); // queued moves after moveTarget
        bool attackMove;    // engage anything in range while moving
        Unit attackTarget;
        bool attackOrdered; // false when the drone picked the target itself
        float fireCooldown;
        Vector3 velocity;
        float bobOffset;
        AudioSource motor;
        float motorPitch;
        float lastYaw, bank;
        Transform body;
        readonly List<Transform> rotors = new List<Transform>();
        readonly List<GameObject> bombModels = new List<GameObject>();
        readonly List<Renderer> strobes = new List<Renderer>();
        bool returning;    // a bomber out of bombs (or a Strike drone out of missiles), on its way home
        float rearmTimer;  // a bomber loading up over its pad
        Vector3 homePad;

        public bool IsKamikaze => type == DroneType.Kamikaze;
        public bool IsBomber => type == DroneType.Bomber;
        public int BombsLeft { get; private set; }
        public int MissilesLeft { get; private set; }
        bool OutOfAmmo => (IsBomber && BombsLeft == 0) || (type == DroneType.Strike && MissilesLeft == 0);
        float RearmTime => (IsBomber ? RearmSeconds : 10f) * Theatre.RearmFactor; // the campaign's forward strips shorten it
        public Unit AttackTarget => attackTarget;
        public Vector3? MoveTarget => moveTarget;
        public IReadOnlyList<Vector3> Waypoints => waypoints;
        public bool AttackMoving => attackMove && moveTarget.HasValue;
        public bool IsJammed => jammedFor > 0f;
        public bool UnderFire => Time.time - LastHitTime < 2f;

        // ---- Coordinated strike (SandboxGame.CoordinateStrike) ----
        public float HoldUntil { get; set; } = -1f;  // waits (hovering, facing its target) until this time
        public float StrikeAt { get; set; } = -1f;   // when the group's hits are due to land
        public int SyncGroup { get; set; }           // 0 when not part of one
        public bool Holding => attackTarget != null && Time.time < HoldUntil;

        // Seconds until this drone can hit its attack target from where it is (to firing range, or to impact for a
        // Kamikaze, whose dive speeds up).
        public float TimeToStrike()
        {
            if (attackTarget == null) return 0f;
            Vector3 to = attackTarget.AimPoint - transform.position;
            if (IsKamikaze) return to.magnitude / (speed * 1.25f);
            to.y = 0f;
            float reach = IsBomber ? 0.5f : weaponRange * 0.9f;
            return Mathf.Max(0f, to.magnitude - reach) / speed + (type == DroneType.Strike ? 0.6f : 0.2f);
        }

        // One word for the HUD's tag under a selected drone.
        public string StatusTag
        {
            get
            {
                if (IsJammed) return "JAMMED";
                if (OutOfAmmo) return returning ? "RETURNING" : "REARMING";
                if (UnderFire) return "UNDER FIRE";
                if (Holding) return $"SYNC  T-{Mathf.CeilToInt(StrikeAt - Time.time)}";
                if (attackTarget != null) return IsKamikaze ? "DIVING" : IsBomber ? "BOMBING" : "ATTACKING";
                if (moveTarget.HasValue) return attackMove ? "ATTACK-MOVE" : "MOVING";
                return "HOLDING";
            }
        }

        // The Nightjar always carries a thermal camera; Scouts do once the Wren-T camera is bought.
        public bool HasThermal => IsBomber || (type == DroneType.Scout && Tech.Active(TechId.ThermalCamera));

        public string Status
        {
            get
            {
                if (IsJammed) return "Jammed: link lost";
                if (OutOfAmmo) return returning ? "Returning to rearm" : $"Rearming ({Mathf.CeilToInt(rearmTimer)} s)";
                string fire = UnderFire ? "Under fire - " : "";
                string queued = waypoints.Count > 0 ? $" (+{waypoints.Count} queued)" : "";
                if (attackTarget != null) return fire + (IsKamikaze ? "Diving on " : IsBomber ? "Bombing " : "Attacking ") + attackTarget.unitName + queued;
                if (moveTarget.HasValue) return fire + (attackMove ? "Attack-moving" : "Moving") + queued;
                return fire + "Hovering";
            }
        }

        public static Drone Create(DroneType type, Vector3 position)
        {
            var go = new GameObject(type + " Drone");
            go.transform.position = position;
            var drone = go.AddComponent<Drone>();
            drone.Setup(type);
            Achievements.DroneLaunched(drone);
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
                    weaponRange = 11f; weaponDamage = 3.5f; fireInterval = 0.25f; // a truck takes 4-7 s of fire; armour shrugs it off
                    modelScale = 1.6f;
                    break;

                case DroneType.Strike:
                    unitName = "Strike";
                    maxHp = 70f; speed = 10f; altitude = 9f; radius = 1.1f;
                    weaponRange = 20f; weaponDamage = 75f; fireInterval = 2.2f; // one missile kills a light vehicle
                    MissilesLeft = StrikeLoad;
                    modelScale = 1.4f;
                    break;

                case DroneType.Kamikaze:
                    unitName = "Kamikaze";
                    maxHp = 25f; speed = 20f; altitude = 6f; radius = 0.6f;
                    weaponRange = 0f; weaponDamage = 110f; fireInterval = 0f;
                    modelScale = 2.2f;
                    break;

                case DroneType.Bomber:
                    // Drops a bomb when it's over the target (weaponRange is across the ground), one at a time
                    // so the first can land before the next falls.
                    unitName = "Nightjar";
                    maxHp = 130f; speed = 8f; altitude = 12f; radius = 1.6f;
                    weaponRange = 2.5f; weaponDamage = 75f; fireInterval = 1.4f;
                    modelScale = 1.6f;
                    BombsLeft = BomberLoad;
                    break;
            }

            // Upgrades from the tech shop.
            if (Tech.Active(TechId.Armour)) maxHp *= Tech.ArmourBonus;
            if (Tech.Active(TechId.Motors)) speed *= Tech.MotorBonus;
            if (Tech.Active(TechId.Warheads)) weaponDamage *= Tech.WarheadBonus;
            hp = maxHp;
            bobOffset = Random.value * 10f;
            motor = Sound.Motor(this);
            if (motor != null) motorPitch = motor.pitch;

            // Use the Blender model from Resources/Models if there is one; otherwise primitive shapes.
            var model = Art.TryModel(droneType == DroneType.Bomber ? "Drone_Nightjar" : "Drone_" + droneType, body, modelScale);
            if (model != null)
            {
                foreach (var t in model.GetComponentsInChildren<Transform>())
                {
                    if (t.name.StartsWith("Prop")) rotors.Add(t);
                    if (t.name.StartsWith("Bomb_")) bombModels.Add(t.gameObject);
                }
                bombModels.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                DressModel(model);
            }
            else
            {
                BuildPrimitiveModel(droneType);
            }
        }

        // The model's lights glow (red on the left arms, green on the right, a white strobe on top that blinks), and
        // the disc over each propeller becomes the faint blur of its spinning blades.
        void DressModel(GameObject model)
        {
            var blur = Resources.Load<Material>("Materials/FX/PropBlur");
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    switch (materials[i].name)
                    {
                        case "LedRed": materials[i] = Art.Shared(new Color(1f, 0.08f, 0.04f), true); break;
                        case "LedGreen": materials[i] = Art.Shared(new Color(0.1f, 1f, 0.25f), true); break;
                        case "Strobe":
                            materials[i] = Art.Shared(Color.white, true);
                            strobes.Add(renderer);
                            break;
                        case "PropBlur":
                            materials[i] = blur;
                            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            renderer.enabled = blur != null;
                            break;
                    }
                }
                renderer.sharedMaterials = materials;
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
                case DroneType.Bomber:
                    BuildQuadcopter(1.2f, 0.9f, Art.EnemyOliveDark);
                    for (int i = 0; i < BomberLoad; i++)
                        bombModels.Add(Art.Part(PrimitiveType.Capsule, body, new Vector3(i % 2 == 0 ? -0.15f : 0.15f, -0.35f, i < 2 ? 0.2f : -0.2f),
                            new Vector3(0.12f, 0.18f, 0.12f), Art.Dark, new Vector3(90f, 0f, 0f)));
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

        // A bomber out of bombs ignores orders until it has rearmed.
        bool BusyRearming => OutOfAmmo;

        // A move order. queue adds it after the moves already given (Shift); attackMove engages anything that
        // comes in range on the way (Ctrl), then carries on.
        public void MoveTo(Vector3 point, bool queue = false, bool attackMove = false)
        {
            if (BusyRearming) return;
            HoldUntil = -1f;
            SyncGroup = 0;
            if (queue && (moveTarget.HasValue || attackTarget != null))
            {
                waypoints.Add(point);
                this.attackMove |= attackMove;
                return;
            }
            waypoints.Clear();
            moveTarget = point;
            attackTarget = null;
            this.attackMove = attackMove;
        }

        public void Attack(Unit target)
        {
            if (BusyRearming) return;
            HoldUntil = -1f;
            SyncGroup = 0;
            attackTarget = target;
            attackOrdered = true;
            moveTarget = null;
            waypoints.Clear();
            attackMove = false;
        }

        public void Stop()
        {
            if (BusyRearming) return;
            moveTarget = null;
            attackTarget = null;
            waypoints.Clear();
            attackMove = false;
        }

        // On to the next queued waypoint, if there is one.
        void NextWaypoint()
        {
            if (waypoints.Count > 0)
            {
                moveTarget = waypoints[0];
                waypoints.RemoveAt(0);
            }
            else
            {
                moveTarget = null;
                attackMove = false;
            }
        }

        // ---- Behaviour ----

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            fireCooldown -= dt;

            if (attackTarget != null && !attackTarget.IsAlive) attackTarget = null;
            if (BusyRearming)
            {
                Rearm(dt);
                return;
            }
            if (Jammed())
            {
                // The link is gone: the drone drops its orders, hangs where it is and sinks, and comes down after
                // a few seconds unless the jammer is destroyed first.
                if (jammedFor == 0f) SandboxGame.Instance.ShowMessage(unitName + " jammed: link lost");
                jammedFor += dt;
                attackTarget = null;
                moveTarget = null;
                waypoints.Clear();
                Fly(transform.position + Vector3.down * 1.5f, false, dt * 0.5f);
                if (jammedFor > 6f) TakeDamage(maxHp * 0.5f * dt);
                return;
            }
            jammedFor = 0f;

            // After an attack, carry on with any queued moves.
            if (attackTarget == null && !moveTarget.HasValue && waypoints.Count > 0) NextWaypoint();

            // Idle gun and missile drones engage the nearest enemy in range on their own, and so does anything on
            // an attack-move (a kamikaze dives on the first enemy within 18 m).
            if (attackTarget == null && (!moveTarget.HasValue || attackMove) && (!IsKamikaze || attackMove) && !SandboxGame.WeaponsTight)
            {
                var nearest = Combat.NearestHostile(transform.position, team, IsKamikaze ? 18f : weaponRange, true);
                if (nearest != null)
                {
                    attackTarget = nearest;
                    attackOrdered = IsKamikaze; // a kamikaze committed to its dive
                }
            }

            float bob = Mathf.Sin((Time.time + bobOffset) * 2f) * 0.25f;
            var goal = new Vector3(transform.position.x, CruiseHeight(transform.position) + bob, transform.position.z); // default: hold position
            bool diving = false;

            if (attackTarget != null && Holding)
            {
                // Part of a coordinated strike: wait here, facing the target, until it's time to go.
                Vector3 look = attackTarget.AimPoint - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 180f * dt);
            }
            else if (attackTarget != null)
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
                        // A Strike drone turns to line its missiles up before it fires.
                        bool lined = type != DroneType.Strike || Vector3.Angle(transform.forward, flat) < 25f;
                        if (distance <= weaponRange && fireCooldown <= 0f && lined)
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
                if (flat.magnitude < 0.6f) NextWaypoint();
            }

            Fly(goal, diving, dt);
        }

        float jammedFor;

        // Inside a working jammer's circle.
        bool Jammed()
        {
            foreach (var e in SandboxGame.Instance.enemies)
            {
                if (e == null || e.type != EnemyType.Jammer || !e.IsAlive || e.Hidden) continue;
                Vector3 offset = e.transform.position - transform.position;
                offset.y = 0f;
                if (offset.magnitude < EnemyUnit.JamRadius) return true;
            }
            return false;
        }

        void Fly(Vector3 goal, bool diving, float dt)
        {
            Vector3 toGoal = goal - transform.position;
            // A Kamikaze picks up speed through the last 40 m of its dive.
            float dive = diving ? Mathf.Lerp(1.6f, 1f, Mathf.Clamp01(toGoal.magnitude / 40f)) : 1f;
            Vector3 desired = diving ? toGoal.normalized * speed * dive : Vector3.ClampMagnitude(toGoal * 2f, speed);
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

            // Lean into the direction of travel like a real multirotor, bank into turns (light, nimble types more),
            // nose down in a dive, and spin the props.
            Vector3 local = transform.InverseTransformDirection(flatVelocity);
            float yawNow = transform.eulerAngles.y;
            float turnRate = dt > 0f ? Mathf.DeltaAngle(lastYaw, yawNow) / dt : 0f;
            lastYaw = yawNow;
            float maxBank = IsKamikaze ? 24f : type == DroneType.Scout ? 14f : IsBomber ? 6f : 9f;
            bank = Mathf.Lerp(bank, Mathf.Clamp(-turnRate * 0.12f, -maxBank, maxBank), 1f - Mathf.Exp(-dt / 0.25f));
            float pitchDown = diving ? 25f : 0f;
            body.localRotation = Quaternion.Euler(local.z / speed * 20f + pitchDown, 0f, -local.x / speed * 20f + bank);
            foreach (var rotor in rotors) rotor.Rotate(body.up, 1800f * dt, Space.World);
            if (motor != null) motor.pitch = motorPitch * (0.92f + 0.22f * Mathf.Clamp01(velocity.magnitude / speed)) * (diving ? 1.12f : 1f);
            float beat = Mathf.Repeat(Time.time + bobOffset, 1.3f); // the strobe double-flashes every 1.3 s
            bool flash = beat < 0.05f || (beat > 0.16f && beat < 0.21f);
            foreach (var strobe in strobes) strobe.enabled = flash;
        }

        // Cruise altitude is above the ground, taking the higher of here and the destination so the drone
        // climbs before crossing a hill rather than after.
        // Its cruise altitude over the ground, and at least 2.5 m over any tree tops below it or ahead (Canopy).
        float CruiseHeight(Vector3 destination)
        {
            float overGround = Mathf.Max(Ground.HeightAt(destination), Ground.HeightAt(transform.position)) + altitude;
            Vector3 ahead = transform.position + Vector3.ClampMagnitude(new Vector3(velocity.x, 0f, velocity.z) * 1.2f, 12f);
            float trees = Mathf.Max(Canopy.TopNear(transform.position, 5f), Canopy.TopNear(ahead, 5f));
            return Mathf.Max(overGround, trees + 2.5f);
        }

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

        // An empty bomber flies back to its pad, settles low over it and loads a new set of bombs.
        void Rearm(float dt)
        {
            Vector3 pad = Ground.OnGround(homePad);
            if (returning)
            {
                Vector3 flat = pad - transform.position;
                flat.y = 0f;
                if (flat.magnitude < 1f)
                {
                    returning = false;
                    rearmTimer = RearmTime;
                }
                Fly(new Vector3(pad.x, CruiseHeight(pad), pad.z), false, dt);
                return;
            }
            rearmTimer -= dt;
            Fly(pad + Vector3.up * 1.6f, false, dt);
            if (rearmTimer > 0f) return;
            if (IsBomber)
            {
                BombsLeft = BomberLoad;
                foreach (var bomb in bombModels) bomb.SetActive(true);
            }
            else MissilesLeft = StrikeLoad;
        }

        void DropBomb()
        {
            BombsLeft--;
            if (BombsLeft < bombModels.Count) bombModels[BombsLeft].SetActive(false);
            float hitChance = attackTarget is EnemyUnit enemy && enemy.IsMoving ? BombHitMoving : BombHitStill;
            Projectile.Bomb(transform.position + Vector3.down * 0.7f, attackTarget, team, weaponDamage, 4.5f, velocity, hitChance, Achievements.Fired(this));
            Sound.At("whistle", transform.position, 0.8f, 0.03f);
            if (BombsLeft > 0) return;
            GoRearm();
        }

        // Out of ammunition: drop everything and fly home to reload.
        void GoRearm()
        {
            returning = true;
            attackTarget = null;
            moveTarget = null;
            homePad = SandboxGame.Instance.basePosition + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f));
        }

        void Fire()
        {
            if (IsBomber)
            {
                DropBomb();
                return;
            }
            Vector3 muzzle = transform.position + transform.forward * radius * 0.6f + Vector3.down * 0.2f;
            if (type == DroneType.Scout)
            {
                Projectile.Bullet(muzzle, attackTarget, team, weaponDamage, 0.8f, new Color(1f, 0.9f, 0.4f), Achievements.Fired(this));
                Sound.At("gunshot", muzzle, 0.55f);
            }
            else
            {
                Projectile.Missile(muzzle, attackTarget, team, weaponDamage, 3f, transform.forward, Achievements.Fired(this));
                Sound.At("missile", muzzle);
                if (--MissilesLeft == 0) GoRearm();
            }
        }

        void Detonate()
        {
            var shot = Achievements.Fired(this);
            hp = 0f;
            DroneFeed.Instance?.SignalLost(this);
            Combat.Blast(transform.position, 5f, weaponDamage, team, shot);
            Effects.Explosion(transform.position, 5f);
            SandboxGame.Instance.OnDroneExpended(this);
            Destroy(gameObject);
        }
    }
}

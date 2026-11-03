using UnityEngine;

namespace DroneSandbox
{
    // Bullets and missiles. Hits follow their target; misses fly past and vanish.
    public class Projectile : MonoBehaviour
    {
        Unit target;
        Vector3 aimPoint;
        Team team;
        float speed, damage, blastRadius, turnRate;
        float life = 4f;
        bool smokeTrail, heavy;
        float trailTimer;
        Vector3 velocity;

        static Projectile Spawn(Vector3 from, Unit target, Team team, Vector3 scale, Color color)
        {
            var go = Art.Part(PrimitiveType.Capsule, null, from, scale, color, default, true);
            go.name = "Projectile";
            var p = go.AddComponent<Projectile>();
            p.target = target;
            p.team = team;
            p.aimPoint = target.AimPoint;
            return p;
        }

        // A fast tracer round. hitChance is the odds (0-1) that it connects.
        public static void Bullet(Vector3 from, Unit target, Team team, float damage, float hitChance, Color color)
        {
            var p = Spawn(from, target, team, new Vector3(0.08f, 0.3f, 0.08f), color);
            p.speed = 70f;
            p.damage = damage;
            if (Random.value > hitChance)
            {
                // Miss: aim a little off and keep going past the target.
                Vector3 offAim = target.AimPoint + Random.insideUnitSphere * 2.5f;
                p.aimPoint = from + (offAim - from) * 2f;
                p.target = null;
                p.damage = 0f;
            }
            p.velocity = (p.aimPoint - from).normalized * p.speed;
        }

        // A homing missile that explodes on arrival.
        public static void Missile(Vector3 from, Unit target, Team team, float damage, float blastRadius, Vector3 launchDirection)
        {
            var p = Spawn(from, target, team, new Vector3(0.18f, 0.3f, 0.18f), Color.white);
            p.speed = 24f;
            p.damage = damage;
            p.blastRadius = blastRadius;
            p.turnRate = 5f;
            p.life = 8f;
            p.smokeTrail = true;
            p.velocity = (launchDirection + Vector3.down * 0.3f).normalized * p.speed * 0.6f;
        }

        // A bomb dropped from a hovering bomber. It falls with the drone's own speed and its fins steer it
        // onto the target. hitChance is the odds (0-1) it lands on target; a miss lands 6-8 m away.
        public static void Bomb(Vector3 from, Unit target, Team team, float damage, float blastRadius, Vector3 dropVelocity,
            float hitChance)
        {
            var p = Spawn(from, target, team, new Vector3(0.2f, 0.32f, 0.2f), new Color(0.2f, 0.21f, 0.13f));
            if (Random.value > hitChance)
            {
                Vector2 off = Random.insideUnitCircle.normalized * Random.Range(6f, 8f);
                p.aimPoint = Ground.OnGround(target.transform.position + new Vector3(off.x, 0f, off.y));
                p.target = null;
            }
            p.speed = 20f;
            p.damage = damage;
            p.blastRadius = blastRadius;
            p.turnRate = 4f;
            p.life = 6f;
            p.velocity = dropVelocity + Vector3.down * 2f;
            p.heavy = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (target != null && target.IsAlive) aimPoint = target.AimPoint;

            Vector3 toAim = aimPoint - transform.position;
            if (toAim.magnitude <= Mathf.Max(speed * dt, 0.4f) || life <= 0f)
            {
                Impact();
                return;
            }

            if (turnRate > 0f)
                velocity = Vector3.RotateTowards(velocity, toAim.normalized * speed, turnRate * dt, speed * dt); // steer and speed up
            else
                velocity = toAim.normalized * speed;

            transform.position += velocity * dt;
            if (velocity.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(velocity) * Quaternion.Euler(90f, 0f, 0f); // capsule's long axis is Y

            if (smokeTrail)
            {
                trailTimer -= dt;
                if (trailTimer <= 0f)
                {
                    trailTimer = 0.04f;
                    Effects.Puff(transform.position);
                }
            }
        }

        void Impact()
        {
            if (blastRadius > 0f)
            {
                if (heavy) Combat.HeavyBlast(transform.position, blastRadius, damage, team);
                else Combat.Blast(transform.position, blastRadius, damage, team);
                Effects.Explosion(transform.position, blastRadius);
            }
            else if (target != null && target.IsAlive && damage > 0f)
            {
                target.TakeDamage(target is EnemyUnit armoured ? damage * armoured.BulletFactor : damage);
                Effects.Spark(transform.position);
                if (target is EnemyUnit) Sound.At("ricochet", transform.position, 0.35f, 0.15f);
            }
            Destroy(gameObject);
        }
    }
}

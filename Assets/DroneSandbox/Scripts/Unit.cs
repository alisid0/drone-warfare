using UnityEngine;

namespace DroneSandbox
{
    public enum Team { Player, Enemy }

    // Health and team shared by drones and enemy targets.
    public abstract class Unit : MonoBehaviour
    {
        public Team team;
        public string unitName;
        public float maxHp = 100f;
        public float hp = 100f;
        public float radius = 1f;      // rough size, used for hits, mouse picking and explosions
        public float aimHeight = 0.5f; // where shots aim, measured up from the unit's pivot

        public bool IsAlive => hp > 0f;
        public Vector3 AimPoint => transform.position + Vector3.up * aimHeight;
        public float LastHitTime { get; private set; } = -100f; // when it last took damage (Time.time), for the HUD

        public void TakeDamage(float amount)
        {
            if (!IsAlive) return;
            LastHitTime = Time.time;
            hp = Mathf.Max(0f, hp - amount);
            if (hp <= 0f) Die();
            else OnHit();
        }

        protected virtual void OnHit() { }

        protected virtual void Die()
        {
            Effects.Explosion(AimPoint, radius * 1.6f);
            SandboxGame.Instance.OnUnitDestroyed(this);
            Destroy(gameObject);
        }
    }
}

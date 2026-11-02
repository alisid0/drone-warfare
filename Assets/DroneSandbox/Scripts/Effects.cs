using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Short-lived visual effects: explosions, smoke, sparks, order markers and smoking wrecks.
    public class Effects : MonoBehaviour
    {
        enum Mode { Pop, Wreck }

        Mode mode;
        float life, age;
        Vector3 peakScale;
        Color startColor, endColor;
        bool glow;
        Vector3 drift;
        float smokeTimer, smokeFor = 12f;
        Material material;

        static Effects Spawn(PrimitiveType shape, Vector3 position, Vector3 peakScale, float life,
            Color startColor, Color endColor, bool glow)
        {
            var go = GameObject.CreatePrimitive(shape);
            Destroy(go.GetComponent<Collider>());
            go.name = "Effect";
            go.transform.position = position;
            go.transform.localScale = peakScale * 0.2f;
            var fx = go.AddComponent<Effects>();
            fx.peakScale = peakScale;
            fx.life = life;
            fx.startColor = startColor;
            fx.endColor = endColor;
            fx.glow = glow;
            fx.material = Art.NewMaterial(startColor, glow);
            go.GetComponent<Renderer>().sharedMaterial = fx.material;
            return fx;
        }

        public static void Explosion(Vector3 at, float size)
        {
            Spawn(PrimitiveType.Sphere, at, Vector3.one * size, 0.6f,
                new Color(1f, 0.85f, 0.4f), new Color(0.6f, 0.15f, 0.05f), true);
            var smoke = Spawn(PrimitiveType.Sphere, at + Vector3.up * 0.5f, Vector3.one * size * 1.3f, 2.5f,
                new Color(0.25f, 0.25f, 0.25f), new Color(0.55f, 0.55f, 0.55f), false);
            smoke.drift = Vector3.up * 1.2f;
        }

        public static void Puff(Vector3 at)
        {
            var puff = Spawn(PrimitiveType.Sphere, at, Vector3.one * 0.5f, 0.7f,
                new Color(0.85f, 0.85f, 0.85f), new Color(0.5f, 0.5f, 0.5f), false);
            puff.drift = Vector3.up * 0.4f;
        }

        public static void Spark(Vector3 at)
        {
            Spawn(PrimitiveType.Sphere, at, Vector3.one * 0.5f, 0.15f, new Color(1f, 0.95f, 0.6f), new Color(1f, 0.5f, 0.2f), true);
        }

        // Flat disc on the ground where an order was given, tilted to match the slope.
        public static void Marker(Vector3 at, Color color)
        {
            Vector3 ground = Ground.OnGround(at) + Vector3.up * 0.08f;
            var fx = Spawn(PrimitiveType.Cylinder, ground, new Vector3(2.5f, 0.02f, 2.5f), 0.5f, color, color, true);
            fx.transform.rotation = Quaternion.FromToRotation(Vector3.up, Ground.NormalAt(ground));
        }

        static Material charred;
        static readonly List<(Renderer renderer, Material[] materials)> burned = new List<(Renderer, Material[])>();

        static Material Charred => charred != null ? charred : (charred = Art.NewMaterial(new Color(0.045f, 0.04f, 0.035f)));

        // Blackens a building (the command post) with a few blasts and leaves it smoking for a minute.
        // Unburn puts its materials back, for the next mission on the same map.
        public static void Burn(GameObject building, Vector3 smokeAt)
        {
            foreach (var r in building.GetComponentsInChildren<Renderer>())
            {
                burned.Add((r, r.sharedMaterials));
                var materials = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = Charred;
                r.sharedMaterials = materials;
            }
            for (int i = 0; i < 3; i++) Explosion(smokeAt + Random.insideUnitSphere * 4f, 5f);
            var go = new GameObject("Burning");
            go.transform.position = smokeAt;
            var fx = go.AddComponent<Effects>();
            fx.mode = Mode.Wreck;
            fx.life = 60f;
            fx.smokeFor = 57f;
        }

        public static void Unburn()
        {
            foreach (var (renderer, materials) in burned)
                if (renderer != null) renderer.sharedMaterials = materials;
            burned.Clear();
        }

        // Burnt-out hull that smokes for a while, then sinks into the ground: the unit's own model, blackened
        // and knocked askew, or a dark slab if it has none.
        public static void Wreck(EnemyUnit enemy)
        {
            Vector3 at = enemy.transform.position;
            var go = new GameObject("Wreck");
            go.transform.SetPositionAndRotation(at, enemy.transform.rotation * Quaternion.Euler(Random.Range(-5f, 5f), 0f, Random.Range(-7f, 7f)));
            var model = Art.TryModel("Enemy_" + enemy.type, go.transform);
            Effects fx;
            if (model != null)
            {
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = Charred;
                    renderer.sharedMaterials = materials;
                }
                var turret = model.transform.Find("Turret") ?? FindDeep(model.transform, "Turret");
                if (turret != null) turret.localRotation *= Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f);
                fx = go.AddComponent<Effects>();
                fx.life = 28f;
            }
            else
            {
                Art.Discard(go);
                fx = Spawn(PrimitiveType.Cube, at + Vector3.up * 0.2f, new Vector3(enemy.radius * 1.1f, 0.4f, enemy.radius * 1.5f), 28f,
                    Art.Dark, Art.Dark, false);
                fx.transform.rotation = enemy.transform.rotation;
                fx.transform.localScale = fx.peakScale;
            }
            fx.mode = Mode.Wreck;
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (Transform child in t)
            {
                if (child.name == name) return child;
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            float t = Mathf.Clamp01(age / life);

            if (mode == Mode.Pop)
            {
                // Grow fast to full size, then shrink away.
                float s = t < 0.3f ? Mathf.SmoothStep(0.2f, 1f, t / 0.3f) : Mathf.SmoothStep(1f, 0f, (t - 0.3f) / 0.7f);
                transform.localScale = peakScale * s;
                Color c = Color.Lerp(startColor, endColor, t);
                material.color = c;
                if (glow) material.SetColor("_EmissionColor", c);
            }
            else
            {
                smokeTimer -= dt;
                if (age < smokeFor && smokeTimer <= 0f)
                {
                    smokeTimer = 0.3f;
                    Puff(transform.position + Vector3.up * 0.6f + Random.insideUnitSphere * 0.4f);
                }
                if (age > life - 3f) transform.position += Vector3.down * 0.3f * dt;
            }

            transform.position += drift * dt;
            if (age >= life) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}

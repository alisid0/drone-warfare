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
        float smokeTimer;
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

        // Burnt-out hull that smokes for a while, then sinks into the ground.
        public static void Wreck(Vector3 at, Quaternion rotation, float size)
        {
            var fx = Spawn(PrimitiveType.Cube, at + Vector3.up * 0.2f, new Vector3(size * 1.1f, 0.4f, size * 1.5f), 28f,
                Art.Dark, Art.Dark, false);
            fx.mode = Mode.Wreck;
            fx.transform.rotation = rotation;
            fx.transform.localScale = fx.peakScale;
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
                if (age < 12f && smokeTimer <= 0f)
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

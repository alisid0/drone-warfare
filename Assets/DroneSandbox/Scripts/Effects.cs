using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // Short-lived visual effects: explosions, smoke, fire, sparks, order markers and burning wrecks.
    // An explosion is built up in layers: a flash of light, a fireball, sparks, flying debris, a dust ring along the
    // ground, black smoke billowing up, and a scorch mark left behind. The particles all come from a few shared
    // world-space particle systems (see Ready), so a blast is a handful of Emit calls rather than new objects. Their
    // materials are made by Drone Sandbox > Configure Project (Resources/Materials/FX); without them explosions fall
    // back to plain glowing balls.
    public class Effects : MonoBehaviour
    {
        enum Mode { Pop, Wreck, Flash, Scorch }

        Mode mode;
        float life, age;
        Vector3 peakScale;
        Color startColor, endColor;
        bool glow;
        Vector3 drift;
        float smokeTimer, smokeFor = 12f, flameFor = 9f, scale = 1f, top = 0.6f;
        Material material;
        Light flash;
        float flashPeak;
        DecalProjector scorch;

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

        // ---- The shared particle systems ----

        static Transform systems;
        static bool unavailable;
        static ParticleSystem fire, smoke, sparks, debris, flames;
        static readonly List<Effects> scorches = new List<Effects>();
        const int MaxScorches = 24;

        static bool Ready()
        {
            if (systems != null) return true;
            if (unavailable) return false;
            var smokeMaterial = Resources.Load<Material>("Materials/FX/Smoke");
            var fireMaterial = Resources.Load<Material>("Materials/FX/Fire");
            var flameMaterial = Resources.Load<Material>("Materials/FX/Flame");
            var sparkMaterial = Resources.Load<Material>("Materials/FX/Spark");
            if (smokeMaterial == null || fireMaterial == null || flameMaterial == null || sparkMaterial == null)
            {
                unavailable = true;
                Debug.LogWarning("Effects: Resources/Materials/FX is missing; run Drone Sandbox > Configure Project.");
                return false;
            }
            systems = new GameObject("Effect Systems").transform;

            // Fireballs: puffs glowing white-hot, then orange, then dull red as they swell and fade.
            fire = MakeSystem("Fire", fireMaterial, 600, ParticleSystemRenderMode.Billboard, -20f);
            Puffs(fire);
            Fade(fire, new[] { (0f, new Color(1f, 0.93f, 0.78f)), (0.15f, new Color(1f, 0.68f, 0.28f)), (0.45f, new Color(0.85f, 0.3f, 0.07f)), (1f, new Color(0.25f, 0.05f, 0.01f)) },
                new[] { (0f, 1f), (0.5f, 0.85f), (1f, 0f) });
            Grow(fire, (0f, 0.55f), (0.35f, 1f), (1f, 1.25f));
            Spin(fire, 40f);
            Drag(fire, 3f);

            // Smoke: puffs that swell, rise, drift on the wind and thin out. The colour each puff starts with sets how
            // dark it is (black from a blast, grey from a missile, brown dust thrown up from the ground); the shading
            // is painted into the puffs, and Shade dims them with the daylight.
            smoke = MakeSystem("Smoke", smokeMaterial, 2000, ParticleSystemRenderMode.Billboard, 0f);
            Puffs(smoke);
            Fade(smoke, new[] { (0f, new Color(0.85f, 0.85f, 0.85f)), (0.3f, new Color(0.95f, 0.95f, 0.95f)), (1f, Color.white) },
                new[] { (0f, 0f), (0.06f, 1f), (0.5f, 0.7f), (1f, 0f) });
            Grow(smoke, (0f, 0.4f), (0.25f, 0.85f), (1f, 1.6f));
            Spin(smoke, 18f);
            Drag(smoke, 1.2f);
            var rise = smoke.forceOverLifetime;
            rise.enabled = true;
            rise.space = ParticleSystemSimulationSpace.World;
            rise.x = new ParticleSystem.MinMaxCurve(0.35f);
            rise.y = new ParticleSystem.MinMaxCurve(0.5f);
            rise.z = new ParticleSystem.MinMaxCurve(0.2f);
            var swirl = smoke.noise;
            swirl.enabled = true;
            swirl.strength = 0.35f;
            swirl.frequency = 0.3f;
            swirl.scrollSpeed = 0.25f;
            swirl.quality = ParticleSystemNoiseQuality.Medium;

            // Sparks: streaks of hot metal that arc down, bounce and go out.
            sparks = MakeSystem("Sparks", sparkMaterial, 800, ParticleSystemRenderMode.Stretch, -30f);
            var streak = sparks.GetComponent<ParticleSystemRenderer>();
            streak.velocityScale = 0.045f;
            streak.lengthScale = 1.2f;
            Fade(sparks, new[] { (0f, new Color(1f, 0.95f, 0.7f)), (0.4f, new Color(1f, 0.6f, 0.18f)), (1f, new Color(0.7f, 0.15f, 0.02f)) },
                new[] { (0f, 1f), (0.7f, 1f), (1f, 0f) });
            Fall(sparks, 1.2f, 0.4f);
            Drag(sparks, 0.6f);

            // Debris: dark chunks tumbling out and bouncing on the ground, shrinking away at the end.
            debris = MakeSystem("Debris", Art.NewMaterial(new Color(0.13f, 0.11f, 0.09f)), 400, ParticleSystemRenderMode.Mesh, 0f);
            var chunks = debris.GetComponent<ParticleSystemRenderer>();
            chunks.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            chunks.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var debrisMain = debris.main;
            debrisMain.startRotation3D = true;
            Grow(debris, (0f, 1f), (0.85f, 1f), (1f, 0f));
            Fall(debris, 1.8f, 0.3f);

            // Flames on burning wrecks: Unity's flame flipbook played through once per particle, blending frames.
            flames = MakeSystem("Flames", flameMaterial, 400, ParticleSystemRenderMode.Billboard, -10f);
            var sheet = flames.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 16;
            sheet.numTilesY = 5;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            sheet.cycleCount = 1;
            Fade(flames, new[] { (0f, new Color(1f, 0.85f, 0.55f)), (0.4f, new Color(1f, 0.5f, 0.15f)), (1f, new Color(0.7f, 0.18f, 0.04f)) },
                new[] { (0f, 0f), (0.12f, 1f), (0.6f, 0.9f), (1f, 0f) });
            Grow(flames, (0f, 0.7f), (0.4f, 1f), (1f, 0.8f));
            return true;
        }

        static ParticleSystem MakeSystem(string name, Material material, int maxParticles, ParticleSystemRenderMode renderMode, float sortingFudge)
        {
            var go = new GameObject(name);
            go.transform.SetParent(systems, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            var emission = system.emission;
            emission.enabled = false; // particles only come from Emit
            var shape = system.shape;
            shape.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = renderMode;
            renderer.sharedMaterial = material;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.sortingFudge = sortingFudge;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.maxParticleSize = 1f; // up to the height of the screen, for close-ups
            if (material.shader.name.StartsWith("Shader Graphs/Particle")) // the flipbook-blending shaders
                renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                {
                    ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
                    ParticleSystemVertexStream.UV, ParticleSystemVertexStream.UV2, ParticleSystemVertexStream.AnimBlend,
                });
            system.Play();
            return system;
        }

        // Each particle shows one of the four puffs in the atlas, picked at random.
        static void Puffs(ParticleSystem system)
        {
            var sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);
        }

        static void Fade(ParticleSystem system, (float time, Color color)[] colors, (float time, float alpha)[] alphas)
        {
            var gradient = new Gradient();
            var colorKeys = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) colorKeys[i] = new GradientColorKey(colors[i].color, colors[i].time);
            var alphaKeys = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) alphaKeys[i] = new GradientAlphaKey(alphas[i].alpha, alphas[i].time);
            gradient.SetKeys(colorKeys, alphaKeys);
            var over = system.colorOverLifetime;
            over.enabled = true;
            over.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        static void Grow(ParticleSystem system, params (float time, float size)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (time, size) in keys) curve.AddKey(time, size);
            var over = system.sizeOverLifetime;
            over.enabled = true;
            over.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        static void Spin(ParticleSystem system, float degreesPerSecond)
        {
            var over = system.rotationOverLifetime;
            over.enabled = true;
            over.z = new ParticleSystem.MinMaxCurve(-degreesPerSecond * Mathf.Deg2Rad, degreesPerSecond * Mathf.Deg2Rad);
        }

        static void Drag(ParticleSystem system, float drag)
        {
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(1000f);
            limit.drag = new ParticleSystem.MinMaxCurve(drag);
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
        }

        // Gravity, and bouncing off the ground and anything else solid.
        static void Fall(ParticleSystem system, float gravity, float bounce)
        {
            var main = system.main;
            main.gravityModifier = gravity;
            var collision = system.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.bounce = bounce;
            collision.dampen = 0.45f;
            collision.lifetimeLoss = 0.1f;
        }

        static void Emit(ParticleSystem system, Vector3 position, Vector3 velocity, float size, float lifetime, Color color)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
                startColor = color,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false,
            };
            system.Emit(p, 1);
        }

        // Smoke is drawn unlit, so it is darkened here as the light goes: dimmer under cloud, dark at night.
        static Color Shade(Color color)
        {
            float light = DayNight.Instance != null ? Mathf.Lerp(0.12f, 1f, DayNight.Instance.Daylight) : 1f;
            light *= Mathf.Lerp(0.75f, 1f, Atmosphere.SunScale);
            return new Color(color.r * light, color.g * light, color.b * light, color.a);
        }

        // ---- Explosions and smaller effects ----

        public static void Explosion(Vector3 at, float size)
        {
            Sound.At(size >= 4f ? "big explosion" : "explosion", at, Mathf.Clamp(0.55f + size * 0.12f, 0.6f, 1.2f), 0.1f);
            if (!Ready())
            {
                Spawn(PrimitiveType.Sphere, at, Vector3.one * size, 0.6f, new Color(1f, 0.85f, 0.4f), new Color(0.6f, 0.15f, 0.05f), true);
                var ball = Spawn(PrimitiveType.Sphere, at + Vector3.up * 0.5f, Vector3.one * size * 1.3f, 2.5f,
                    new Color(0.25f, 0.25f, 0.25f), new Color(0.55f, 0.55f, 0.55f), false);
                ball.drift = Vector3.up * 1.2f;
                return;
            }
            float s = Mathf.Max(size, 0.5f), punch = Mathf.Sqrt(s / 3f);
            bool big = s >= 1.5f; // smaller ones are muzzle flashes and light hits
            Vector3 ground = Ground.OnGround(at);
            bool low = at.y - ground.y < s * 1.2f;

            if (big) Flash(at + Vector3.up * s * 0.3f, s);

            // The fireball, and a quick white-hot core.
            int count = Mathf.Clamp(Mathf.RoundToInt(4f + s * 1.6f), 4, 16);
            for (int i = 0; i < count; i++)
                Emit(fire, at + Random.insideUnitSphere * s * 0.25f, Random.onUnitSphere * s * Random.Range(0.6f, 1.8f) + Vector3.up * s * 0.6f,
                    s * Random.Range(0.8f, 1.3f), Random.Range(0.4f, 0.75f) + s * 0.02f, Color.white);
            Emit(fire, at, Vector3.zero, s * 2.4f, 0.14f, Color.white);

            // Sparks thrown out and up.
            count = big ? Mathf.Clamp(Mathf.RoundToInt(6f + s * 3f), 6, 30) : 3;
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = (Random.onUnitSphere + Vector3.up * 0.7f).normalized;
                Emit(sparks, at, direction * Random.Range(5f, 14f) * punch, Random.Range(0.1f, 0.22f), Random.Range(0.4f, 1.1f), Color.white);
            }

            // Chunks of whatever blew up.
            if (s >= 2.5f)
            {
                count = Mathf.Clamp(Mathf.RoundToInt(s * 2.5f), 4, 18);
                for (int i = 0; i < count; i++)
                {
                    Vector3 direction = Random.onUnitSphere;
                    direction.y = Mathf.Abs(direction.y) + 0.8f;
                    var p = new ParticleSystem.EmitParams
                    {
                        position = at + Random.insideUnitSphere * s * 0.2f,
                        velocity = direction.normalized * Random.Range(4f, 10f) * punch,
                        startSize = Random.Range(0.06f, 0.22f) * Mathf.Clamp(s / 4f, 0.6f, 1.5f),
                        startLifetime = Random.Range(1.4f, 2.4f),
                        rotation3D = Random.insideUnitSphere * 180f,
                        angularVelocity3D = Random.insideUnitSphere * 540f,
                        applyShapeToPosition = false,
                    };
                    debris.Emit(p, 1);
                }
            }

            // Dust kicked out in a ring along the ground.
            if (low && s >= 2f)
            {
                count = Mathf.Clamp(Mathf.RoundToInt(s * 3f), 8, 24);
                for (int i = 0; i < count; i++)
                {
                    float angle = (i + Random.value * 0.8f) / count * Mathf.PI * 2f;
                    var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Emit(smoke, ground + outward * s * Random.Range(0.2f, 0.4f) + Vector3.up * 0.4f, outward * Random.Range(2.5f, 6f) * punch + Vector3.up * 0.4f,
                        s * Random.Range(0.35f, 0.7f), Random.Range(1.4f, 2.6f), Shade(new Color(0.8f, 0.7f, 0.56f, 0.4f)));
                }
            }

            // Smoke billowing up: black from a big blast, grey from a small one.
            count = Mathf.Clamp(Mathf.RoundToInt(3f + s * 1.4f), 3, 16);
            Color soot = Shade(big ? new Color(0.42f, 0.4f, 0.38f, 0.85f) : new Color(0.85f, 0.85f, 0.85f, 0.55f));
            for (int i = 0; i < count; i++)
            {
                Vector3 sideways = Random.insideUnitSphere * s * 0.3f;
                sideways.y = 0f;
                Emit(smoke, at + Random.insideUnitSphere * s * 0.35f + Vector3.up * s * 0.15f,
                    Vector3.up * Random.Range(0.8f, 1.8f) * Mathf.Sqrt(s) + sideways,
                    s * Random.Range(1.1f, 1.8f), Mathf.Min(Random.Range(3.5f, 6f) * (0.7f + s * 0.08f), 9f), soot);
            }

            if (low && s >= 2f) Scorch(ground, s);
        }

        // A missile's smoke trail.
        public static void Puff(Vector3 at)
        {
            if (!Ready())
            {
                var ball = Spawn(PrimitiveType.Sphere, at, Vector3.one * 0.5f, 0.7f, new Color(0.85f, 0.85f, 0.85f), new Color(0.5f, 0.5f, 0.5f), false);
                ball.drift = Vector3.up * 0.4f;
                return;
            }
            Emit(smoke, at, Vector3.up * 0.3f + Random.insideUnitSphere * 0.2f, Random.Range(0.8f, 1.1f), Random.Range(1.2f, 1.8f), Shade(new Color(0.92f, 0.92f, 0.92f, 0.45f)));
        }

        // A bullet striking metal.
        // A puff of dust from the ground (a vehicle driving hard on a dirt road or across a field).
        public static void Dust(Vector3 at)
        {
            if (!Ready()) return;
            Emit(smoke, at + Vector3.up * 0.4f + Random.insideUnitSphere * 0.4f, Vector3.up * 0.5f + Random.insideUnitSphere * 0.4f,
                Random.Range(1.2f, 1.8f), Random.Range(1.4f, 2.2f), Shade(new Color(0.62f, 0.54f, 0.42f, 0.4f)));
        }

        // Smoke from a damaged vehicle: grey wisps at half health, thick and dark near the end (severity 0-1).
        public static void DamageSmoke(Vector3 at, float severity)
        {
            if (!Ready()) return;
            float grey = Mathf.Lerp(0.7f, 0.22f, severity);
            Emit(smoke, at + Random.insideUnitSphere * 0.3f, Vector3.up * Mathf.Lerp(0.6f, 1.2f, severity) + Random.insideUnitSphere * 0.2f,
                Random.Range(0.9f, 1.3f) * Mathf.Lerp(1f, 1.8f, severity), Random.Range(1.8f, 2.6f),
                Shade(new Color(grey, grey, grey, Mathf.Lerp(0.35f, 0.7f, severity))));
        }

        public static void Spark(Vector3 at)
        {
            if (!Ready())
            {
                Spawn(PrimitiveType.Sphere, at, Vector3.one * 0.5f, 0.15f, new Color(1f, 0.95f, 0.6f), new Color(1f, 0.5f, 0.2f), true);
                return;
            }
            for (int i = 0; i < 4; i++)
                Emit(sparks, at, (Random.onUnitSphere + Vector3.up * 0.5f).normalized * Random.Range(4f, 9f), Random.Range(0.06f, 0.12f), Random.Range(0.2f, 0.45f), Color.white);
            Emit(fire, at, Vector3.zero, 0.6f, 0.08f, Color.white);
        }

        // A flash of light from the blast that lights up the ground around it (mostly seen at night).
        static void Flash(Vector3 at, float s)
        {
            var go = new GameObject("Blast Light");
            go.transform.position = at;
            var light = go.AddComponent<Light>();
            if (go.GetComponent<HDAdditionalLightData>() == null) go.AddComponent<HDAdditionalLightData>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.7f, 0.4f);
            light.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
            light.range = s * 7f;
            light.shadows = LightShadows.None;
            var fx = go.AddComponent<Effects>();
            fx.mode = Mode.Flash;
            fx.life = 0.4f;
            fx.flash = light;
            fx.flashPeak = 3500f * s * s;
            light.intensity = fx.flashPeak;
        }

        // A burnt patch left on the ground, fading after a while. Only the latest few are kept.
        static void Scorch(Vector3 ground, float s)
        {
            var material = Resources.Load<Material>("Materials/Decals/DecalScorch");
            if (material == null) return;
            var go = new GameObject("Scorch");
            go.transform.SetPositionAndRotation(ground + Vector3.up * 1.5f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)); // projecting down
            var decal = go.AddComponent<DecalProjector>();
            decal.material = material;
            float width = s * 1.7f;
            decal.size = new Vector3(width, width, 4f);
            decal.pivot = Vector3.zero;
            decal.fadeFactor = 0.9f;
            var fx = go.AddComponent<Effects>();
            fx.mode = Mode.Scorch;
            fx.life = 45f;
            fx.scorch = decal;
            scorches.Add(fx);
            scorches.RemoveAll(e => e == null);
            while (scorches.Count > MaxScorches)
            {
                if (scorches[0] != null) Destroy(scorches[0].gameObject);
                scorches.RemoveAt(0);
            }
        }

        // Flat disc on the ground where an order was given, tilted to match the slope.
        public static void Marker(Vector3 at, Color color)
        {
            Vector3 ground = Ground.OnGround(at) + Vector3.up * 0.08f;
            var fx = Spawn(PrimitiveType.Cylinder, ground, new Vector3(2.5f, 0.02f, 2.5f), 0.5f, color, color, true);
            fx.transform.rotation = Quaternion.FromToRotation(Vector3.up, Ground.NormalAt(ground));
        }

        // Clears the air and the burnt buildings, for a new mission or sector (the burnt-out wrecks and scorch marks are
        // Effects objects, which the game removes itself).
        public static void Clear()
        {
            if (systems != null)
                foreach (var system in new[] { fire, smoke, sparks, debris, flames })
                    if (system != null) system.Clear();
            Unburn();
        }

        static Material charred;
        static readonly List<(Renderer renderer, Material[] materials)> burned = new List<(Renderer, Material[])>();

        static Material Charred => charred != null ? charred : (charred = Art.NewMaterial(new Color(0.045f, 0.04f, 0.035f)));

        // Blackens a building (the command post) with a few blasts and leaves it burning and smoking for a minute.
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
            fx.flameFor = 40f;
            fx.scale = 2.2f;
        }

        static void Unburn()
        {
            foreach (var (renderer, materials) in burned)
                if (renderer != null) renderer.sharedMaterials = materials;
            burned.Clear();
        }

        // Burnt-out hull that burns, then smokes for a while, then sinks into the ground: the vehicle's wreck model
        // (Tools/Blender/make_wrecks.py: blown apart, crumpled and baked with soot, rust and ash), or else its own
        // model blackened and knocked askew, or a dark slab if it has neither.
        public static void Wreck(EnemyUnit enemy)
        {
            Vector3 at = enemy.transform.position;
            var go = new GameObject("Wreck");
            go.transform.SetPositionAndRotation(at, enemy.transform.rotation * Quaternion.Euler(Random.Range(-3f, 3f), 0f, Random.Range(-4f, 4f)));
            float scale = EnemyUnit.ModelScale(enemy.type);
            var model = Art.TryModel("Wreck_" + enemy.type, go.transform, scale);
            if (model == null && (model = Art.TryModel("Enemy_" + enemy.type, go.transform, scale)) != null)
            {
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = Charred;
                    renderer.sharedMaterials = materials;
                }
                var turret = model.transform.Find("Turret") ?? FindDeep(model.transform, "Turret");
                if (turret != null) turret.localRotation *= Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f);
            }
            Effects fx;
            if (model != null)
            {
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
            fx.scale = Mathf.Clamp(enemy.radius / 2f, 0.7f, 1.4f);
            fx.top = Mathf.Max(0.6f, enemy.AimPoint.y - at.y + 0.3f); // flames lick up from the top of the hull
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

            switch (mode)
            {
                case Mode.Pop:
                {
                    // Grow fast to full size, then shrink away.
                    float s = t < 0.3f ? Mathf.SmoothStep(0.2f, 1f, t / 0.3f) : Mathf.SmoothStep(1f, 0f, (t - 0.3f) / 0.7f);
                    transform.localScale = peakScale * s;
                    Color c = Color.Lerp(startColor, endColor, t);
                    material.color = c;
                    if (glow) Art.Glow(material, c);
                    break;
                }
                case Mode.Flash:
                    flash.intensity = flashPeak * (1f - t) * (1f - t);
                    break;
                case Mode.Scorch:
                    scorch.fadeFactor = 0.9f * Mathf.Clamp01((life - age) / 10f);
                    break;
                case Mode.Wreck:
                    smokeTimer -= dt;
                    if (smokeTimer <= 0f)
                    {
                        smokeTimer = 0.15f;
                        Burning();
                    }
                    if (age > life - 3f) transform.position += Vector3.down * 0.3f * dt;
                    break;
            }

            transform.position += drift * dt;
            if (age >= life) Destroy(gameObject);
        }

        // A wreck's flames die down over the first seconds; its column of smoke thins out after them.
        void Burning()
        {
            Vector3 at = transform.position;
            if (!Ready())
            {
                if (age < smokeFor) Puff(at + Vector3.up * 0.6f + Random.insideUnitSphere * 0.4f);
                return;
            }
            if (age < flameFor && Random.value < 0.85f)
            {
                float strength = 1f - age / flameFor * 0.6f;
                Vector2 spot = Random.insideUnitCircle * 0.8f * scale;
                Emit(flames, at + new Vector3(spot.x, top, spot.y), Vector3.up * Random.Range(0.4f, 1f),
                    Random.Range(1.2f, 2f) * scale * strength, Random.Range(0.8f, 1.2f), Color.white);
            }
            if (age < smokeFor)
            {
                float thin = 1f - age / smokeFor;
                Emit(smoke, at + Vector3.up * (top + 0.5f * scale) + Random.insideUnitSphere * 0.5f * scale,
                    Vector3.up * Random.Range(1.4f, 2.4f) + Random.insideUnitSphere * 0.3f,
                    Random.Range(1.2f, 2f) * scale * (0.6f + 0.4f * thin), Random.Range(4.5f, 6.5f),
                    Shade(new Color(0.4f, 0.38f, 0.36f, 0.35f + 0.5f * thin)));
            }
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Crows over the valley, and (in the Mimic hunt) the Dravin spy robots built to pass for them. A crow flies with
    // its flock: flapping in bursts at its own slightly wandering beat, gliding between, wheeling round a point
    // that drifts over the fields, and now and then the whole flock lands to forage and takes off again. A robot has
    // the same body and the same black feathers (Tools/Blender/make_wildlife.py), but it has a job: it flies the same
    // loop round what it's watching, over and over, holds still in the air at the corners of it, and beats its wings
    // with a motor's perfect regularity, never gliding. Those are the tells a patient observer can pick up (Mimic.cs
    // counts them); none of them shows from far off.
    public class Bird : MonoBehaviour
    {
        public bool Robot { get; private set; }
        public bool Down { get; private set; }      // shot down (robots only)
        public bool Escaped { get; private set; }   // a spooked robot got away with its pictures
        public bool GlintNow { get; private set; }  // a servo catching the light, for a moment
        public bool BurstNow { get; private set; }  // sending its pictures home, this frame
        public bool Spooked { get; private set; }
        public float EscapeLeft => Spooked ? Mathf.Max(0f, 40f - (Time.time - spookedAt)) : 0f; // seconds before it's gone
        float nextGlint, glintUntil, nextBurst, spookedAt;
        Vector3 escapeTo;
        public Vector3 Velocity { get; private set; }
        public bool Hovering { get; private set; }  // holding still in the air
        public bool OnGround => state == State.Grounded;

        enum State { Flying, Landing, Grounded, TakingOff }
        State state = State.Flying;

        public const float Scale = 1.25f;           // a little bigger than life, like the drones, so it reads
        const float Speed = 10f;

        Flock flock;
        Flock mingle;                               // a robot passing as one of this flock for a while
        float mingleUntil, nextMingle;
        public Flock Flock => flock;
        Transform wingL, wingR, handL, handR, body;
        float flap, flapRate, burstUntil, glideUntil, wingSign;
        float handLag = 0.9f, sideSkew;             // how far the wrist trails the shoulder; left-right unevenness
        Quaternion handRestL, handRestR;
        float stateUntil, groundY, altitude;
        Vector3 groundSpot;
        System.Random rng;

        // The robot's beat (Hz) and route: a loop of points it flies round, pausing at each corner.
        public const float RobotFlapRate = 3.8f;
        List<Vector3> route;
        int leg;
        float holdUntil;

        static Material feather, beak, servo, lens, eye;

        public static Bird Create(Vector3 at, bool robot, Flock flock, List<Vector3> route, Transform parent, int seed)
        {
            var go = new GameObject(robot ? "Bird (robot)" : "Bird");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var b = go.AddComponent<Bird>();
            b.Robot = robot;
            b.flock = flock;
            b.route = route;
            b.rng = new System.Random(seed);
            b.altitude = at.y - Ground.HeightAt(at);
            b.flapRate = robot ? RobotFlapRate : 3.2f + (float)b.rng.NextDouble() * 1.0f;
            b.flap = (float)b.rng.NextDouble() * 6.28f;
            b.Build();
            return b;
        }

        void Build()
        {
            body = new GameObject("Model").transform;
            body.SetParent(transform, false);
            var model = Art.TryModel(Robot ? "Wildlife_CrowRobot" : "Wildlife_Crow", body, Scale);
            if (model == null)
            {
                // A stand-in so the game still works without the model: a dark lozenge.
                Art.Part(PrimitiveType.Sphere, body, Vector3.zero, new Vector3(0.18f, 0.12f, 0.45f) * Scale, new Color(0.05f, 0.05f, 0.06f));
                return;
            }
            Materials();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    mats[i] = n.StartsWith("Servo") ? servo : n.StartsWith("Lens") ? lens : n.StartsWith("Eye") ? eye
                            : n.StartsWith("Beak") ? beak : feather;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (var t in model.GetComponentsInChildren<Transform>())
            {
                if (t.name == "Wing_L") wingL = t;
                else if (t.name == "Wing_R") wingR = t;
                else if (t.name == "Hand_L") handRestL = (handL = t).localRotation;
                else if (t.name == "Hand_R") handRestR = (handR = t).localRotation;
            }
            if (wingL != null) wingSign = Mathf.Sign(wingL.localPosition.x);
        }

        static void Materials()
        {
            if (feather != null) return;
            feather = Art.NewMaterial(new Color(0.045f, 0.045f, 0.055f));
            feather.SetFloat("_Smoothness", 0.42f); // the faint blue-black sheen of crow feathers
            beak = Art.NewMaterial(new Color(0.03f, 0.03f, 0.032f));
            beak.SetFloat("_Smoothness", 0.5f);
            servo = Art.NewMaterial(new Color(0.42f, 0.43f, 0.45f));
            servo.SetFloat("_Metallic", 1f);
            servo.SetFloat("_Smoothness", 0.8f);
            lens = Art.NewMaterial(new Color(0.01f, 0.012f, 0.015f));
            lens.SetFloat("_Smoothness", 0.95f);
            eye = Art.NewMaterial(new Color(0.02f, 0.015f, 0.01f));
            eye.SetFloat("_Smoothness", 0.85f);
        }

        void Update()
        {
            if (Down || Escaped) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (Robot)
            {
                Signals();
                if (Spooked) Escape(dt);
                else if (!Mingle(dt)) FlyRoute(dt);
            }
            else FlyWithFlock(dt);
            Wings(dt);
        }

        // ---- Crows ----

        void FlyWithFlock(float dt)
        {
            switch (state)
            {
                case State.Flying:
                {
                    if (flock != null && flock.Landing)
                    {
                        state = State.Landing;
                        groundSpot = flock.LandingSpot + new Vector3(Range(-4f, 4f), 0f, Range(-4f, 4f));
                        break;
                    }
                    Vector3 goal = flock != null ? flock.Point(this) : transform.position + transform.forward * 20f;
                    goal.y = Ground.HeightAt(goal) + altitude;
                    Steer(goal, Speed, dt);
                    break;
                }
                case State.Landing:
                {
                    var spot = Ground.OnGround(groundSpot);
                    Steer(spot + Vector3.up * 0.05f, Mathf.Lerp(3f, Speed, Mathf.Clamp01((transform.position - spot).magnitude / 25f)), dt);
                    if ((transform.position - spot).magnitude < 0.6f)
                    {
                        state = State.Grounded;
                        Velocity = Vector3.zero;
                        groundY = spot.y;
                        stateUntil = Time.time + Range(4f, 9f);
                    }
                    break;
                }
                case State.Grounded:
                {
                    // Pecking about: a short hop now and then.
                    if (Time.time > stateUntil)
                    {
                        stateUntil = Time.time + Range(1.5f, 4f);
                        if (rng.NextDouble() < 0.6)
                        {
                            Vector3 hop = new Vector3(Range(-0.6f, 0.6f), 0f, Range(-0.6f, 0.6f));
                            transform.position = Ground.OnGround(transform.position + hop);
                            if (hop.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(hop);
                        }
                    }
                    if (flock == null || !flock.Landing)
                    {
                        state = State.TakingOff;
                        stateUntil = Time.time + Range(0f, 0.8f); // they go up raggedly, not all at once
                    }
                    break;
                }
                case State.TakingOff:
                {
                    if (Time.time < stateUntil) break;
                    Vector3 up = transform.position + transform.forward * 6f + Vector3.up * 5f;
                    Steer(up, Speed * 0.7f, dt);
                    if (transform.position.y - Ground.HeightAt(transform.position) > altitude * 0.6f) state = State.Flying;
                    break;
                }
            }
        }

        // ---- Robots ----

        // Now and then a servo catches the light, and every half-minute or so it sends what it has seen.
        void Signals()
        {
            if (nextGlint == 0f)
            {
                nextGlint = Time.time + Range(3f, 8f);
                nextBurst = Time.time + Range(15f, 30f);
            }
            GlintNow = Time.time < glintUntil;
            if (Time.time > nextGlint)
            {
                glintUntil = Time.time + 0.4f;
                nextGlint = Time.time + Range(5f, 10f);
            }
            BurstNow = false;
            if (Time.time > nextBurst)
            {
                BurstNow = true;
                nextBurst = Time.time + Range(25f, 35f);
                Sound.At("data burst", transform.position, 0.5f);
            }
        }

        // A miss gives it away: it drops its patrol and runs for the Dravin lines, and 40 s later it's gone.
        public void Spook()
        {
            if (Spooked || Down) return;
            Spooked = true;
            spookedAt = Time.time;
            Vector3 away = new Vector3(transform.position.x, 0f, transform.position.z) - new Vector3(MapLayout.BasePosition.x, 0f, MapLayout.BasePosition.z);
            escapeTo = transform.position + (away.sqrMagnitude > 1f ? away.normalized : Vector3.forward) * 400f;
        }

        void Escape(float dt)
        {
            Vector3 goal = escapeTo;
            goal.y = Ground.HeightAt(goal) + altitude + 10f;
            Steer(goal, 13f, dt); // a Scout (16 m/s) can still run it down
            if (Time.time - spookedAt > 40f)
            {
                Escaped = true;
                gameObject.SetActive(false);
            }
        }

        // A crow moving over to another flock.
        public void JoinFlock(Flock f)
        {
            if (f == null || f == flock) return;
            flock?.Remove(this);
            flock = f;
            f.Add(this);
        }

        // Every minute or so a robot falls in with a flock passing within 90 m, as a crow would, for 15-25 s: no
        // hovering, no loop, just another bird among them, until it peels off back to its patrol.
        bool Mingle(float dt)
        {
            if (nextMingle == 0f) nextMingle = Time.time + Range(40f, 70f);
            if (mingle == null && Time.time > nextMingle)
            {
                nextMingle = Time.time + Range(45f, 75f);
                mingle = Wildlife.FlockNear(transform.position, 90f);
                mingleUntil = Time.time + Range(15f, 25f);
            }
            if (mingle == null) return false;
            if (Time.time > mingleUntil || mingle.Landing)
            {
                mingle = null;
                return false;
            }
            Hovering = false;
            Vector3 goal = mingle.Point(this);
            goal.y = Ground.HeightAt(goal) + altitude;
            Steer(goal, Speed, dt);
            return true;
        }

        // Round and round the same loop, holding still for a few seconds at each corner: a camera platform doing
        // its job, not a bird.
        void FlyRoute(float dt)
        {
            if (route == null || route.Count == 0)
            {
                Steer(transform.position + transform.forward * 20f, Speed, dt);
                return;
            }
            Vector3 goal = route[leg];
            goal.y = Ground.HeightAt(goal) + altitude;
            if (Time.time < holdUntil)
            {
                Hovering = true;
                Velocity = Vector3.MoveTowards(Velocity, Vector3.zero, 12f * dt);
                transform.position += Velocity * dt;
                return;
            }
            Hovering = false;
            Steer(goal, Speed, dt);
            if ((transform.position - goal).magnitude < 2f)
            {
                leg = (leg + 1) % route.Count;
                holdUntil = Time.time + 3.5f;
            }
        }

        void Steer(Vector3 goal, float speed, float dt)
        {
            Vector3 want = goal - transform.position;
            Vector3 desired = want.normalized * Mathf.Min(speed, want.magnitude * 1.5f);
            Velocity = Vector3.MoveTowards(Velocity, desired, 9f * dt);
            transform.position += Velocity * dt;
            if (Velocity.sqrMagnitude > 0.04f)
            {
                // Bank into turns and nose up a little when climbing.
                var face = Quaternion.LookRotation(Velocity);
                float turn = Vector3.SignedAngle(transform.forward, Velocity, Vector3.up);
                face *= Quaternion.Euler(0f, 0f, Mathf.Clamp(-turn * 2.5f, -40f, 40f));
                transform.rotation = Quaternion.Slerp(transform.rotation, face, 4f * dt);
            }
        }

        // ---- Wings ----

        // A crow flaps in bursts of a few seconds, each at its own pace around its usual beat, and glides between;
        // a robot's motor never stops and never changes its beat. The wrist trails the shoulder, so the outer wing
        // flexes down on the downstroke and folds in on the way up: by the same amount on every beat, both sides at
        // once, on a robot; a little differently each burst, and never quite evenly left and right, on a crow.
        // Grounded, the wings fold.
        void Wings(float dt)
        {
            if (wingL == null || wingR == null) return;
            float angle, fold = 1f, hand = 0f;
            if (state == State.Grounded)
            {
                angle = -12f;
                fold = 0.35f;
                hand = -25f;
            }
            else if (Robot)
            {
                flap += dt * RobotFlapRate * 2f * Mathf.PI;
                angle = Hovering ? 30f * Mathf.Sin(flap) + 18f : 38f * Mathf.Sin(flap) + 6f;
                hand = 20f * Mathf.Sin(flap - 0.9f) - 4f;
            }
            else
            {
                if (Time.time > glideUntil && Time.time > burstUntil)
                {
                    burstUntil = Time.time + Range(1.5f, 4f);
                    glideUntil = burstUntil + (state == State.Flying ? Range(0.8f, 2.5f) : 0f);
                    flapRate = Mathf.Clamp(flapRate + Range(-0.35f, 0.35f), 2.8f, 4.6f);
                    handLag = Range(0.6f, 1.2f);
                    sideSkew = Range(-4f, 4f);
                }
                bool gliding = Time.time > burstUntil && state == State.Flying;
                if (!gliding) flap += dt * flapRate * 2f * Mathf.PI;
                angle = gliding ? Mathf.Lerp(38f * Mathf.Sin(flap) + 6f, 7f, 0.9f) : 38f * Mathf.Sin(flap) + 6f;
                hand = gliding ? 2f : 24f * Mathf.Sin(flap - handLag) - 4f;
            }
            wingL.localRotation = Quaternion.Euler(0f, 0f, wingSign * (angle + sideSkew * 0.5f));
            wingR.localRotation = Quaternion.Euler(0f, 0f, -wingSign * (angle - sideSkew * 0.5f));
            if (handL != null) handL.localRotation = Quaternion.AngleAxis(wingSign * hand, Vector3.forward) * handRestL;
            if (handR != null) handR.localRotation = Quaternion.AngleAxis(-wingSign * hand, Vector3.forward) * handRestR;
            var s = new Vector3(fold, 1f, 1f);
            wingL.localScale = s;
            wingR.localScale = s;
        }

        float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // ---- Shot down ----

        // Falls out of the sky in a tumble, trailing sparks; on the ground it's wreckage, not a bird.
        public void ShootDown()
        {
            if (Down) return;
            Down = true;
            Hovering = false;
            Effects.Spark(transform.position);
            StartCoroutine(Fall());
        }

        System.Collections.IEnumerator Fall()
        {
            Vector3 v = Velocity * 0.5f;
            float spin = Range(-400f, 400f);
            while (transform.position.y > Ground.HeightAt(transform.position) + 0.1f)
            {
                v += Physics.gravity * Time.deltaTime;
                transform.position += v * Time.deltaTime;
                transform.Rotate(spin * Time.deltaTime, spin * 0.6f * Time.deltaTime, 0f, Space.Self);
                yield return null;
            }
            transform.position = Ground.OnGround(transform.position);
            if (wingL != null) wingL.localRotation = Quaternion.Euler(0f, 0f, wingSign * -35f);
            if (wingR != null) wingR.localRotation = Quaternion.Euler(0f, 0f, wingSign * 50f);
        }
    }

    // A flock of crows: a point that wanders over the open country for the birds to wheel round, and now and then a
    // field to come down in.
    public class Flock
    {
        public Vector3 Centre;
        public bool Landing { get; private set; }
        public Vector3 LandingSpot { get; private set; }
        readonly List<Bird> birds = new List<Bird>();
        readonly System.Random rng;
        readonly float radius;
        Vector3 home;
        float nextMove, nextLanding, landedUntil, nextCall;

        public Flock(Vector3 home, float radius, int seed)
        {
            this.home = home;
            this.radius = radius;
            Centre = home;
            rng = new System.Random(seed);
            nextLanding = Time.time + Range(25f, 60f);
        }

        public void Add(Bird b) => birds.Add(b);
        public void Remove(Bird b) => birds.Remove(b);

        public void Tick()
        {
            if (birds.Count > 0 && Time.time > nextCall)
            {
                nextCall = Time.time + Range(6f, 20f);
                var caller = birds[rng.Next(birds.Count)];
                if (caller != null) Sound.At("crow", caller.transform.position, 0.8f, 0.1f);
            }
            if (Time.time > nextMove)
            {
                nextMove = Time.time + Range(12f, 25f);
                Centre = home + new Vector3(Range(-radius, radius), 0f, Range(-radius, radius));
            }
            if (!Landing && Time.time > nextLanding)
            {
                // Down into open ground for a while (a field or a meadow, not the woods or the water).
                for (int tries = 0; tries < 20; tries++)
                {
                    var spot = Centre + new Vector3(Range(-30f, 30f), 0f, Range(-30f, 30f));
                    if (MapLayout.LakeDistance(spot.x, spot.z) < 4f || Canopy.TopNear(spot, 2f) > Ground.HeightAt(spot) + 1f) continue;
                    LandingSpot = spot;
                    Landing = true;
                    landedUntil = Time.time + Range(15f, 35f);
                    break;
                }
                if (!Landing) nextLanding = Time.time + 10f;
            }
            else if (Landing && Time.time > landedUntil)
            {
                Landing = false;
                nextLanding = Time.time + Range(40f, 90f);
            }
        }

        // Where a bird of the flock is heading: round the centre on its own circle, so the flock wheels loosely.
        public Vector3 Point(Bird b)
        {
            int i = birds.IndexOf(b);
            float a = Time.time * 0.25f + i * 2.1f;
            return Centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (14f + 4f * (i % 3));
        }

        float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    }

    // The birds on the map, and the flocks that move them.
    public static class Wildlife
    {
        public static readonly List<Bird> Birds = new List<Bird>();
        static readonly List<Flock> flocks = new List<Flock>();
        static Transform root;
        static float nextShuffle;
        static readonly System.Random shuffle = new System.Random(77);

        public static void Clear()
        {
            Mimic.Reset();
            nextShuffle = 0f;
            foreach (var b in Birds) if (b != null) Object.Destroy(b.gameObject);
            Birds.Clear();
            flocks.Clear();
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }

        static Transform Root()
        {
            if (root == null)
            {
                root = new GameObject("Wildlife").transform;
                root.gameObject.AddComponent<Ticker>();
            }
            return root;
        }

        // Moves the flocks along once a frame, and counts what's been seen of the birds (Mimic).
        class Ticker : MonoBehaviour
        {
            void Update()
            {
                Tick();
                Mimic.Tick();
            }
        }

        // A flock of `count` crows wheeling round `home` within `radius` metres.
        public static Flock AddFlock(Vector3 home, float radius, int count, int seed)
        {
            var f = new Flock(home, radius, seed);
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                var at = home + new Vector3((float)rng.NextDouble() * 20f - 10f, 0f, (float)rng.NextDouble() * 20f - 10f);
                at.y = Ground.HeightAt(at) + 12f + (float)rng.NextDouble() * 10f;
                var b = Bird.Create(at, false, f, null, Root(), seed * 31 + i);
                f.Add(b);
                Birds.Add(b);
            }
            flocks.Add(f);
            return f;
        }

        // A spy robot flying its loop round `route`, at `height` metres over the ground.
        public static Bird AddRobot(List<Vector3> route, float height, int seed)
        {
            var at = route[0];
            at.y = Ground.HeightAt(at) + height;
            var b = Bird.Create(at, true, null, route, Root(), seed);
            Birds.Add(b);
            return b;
        }

        public static void Tick()
        {
            foreach (var f in flocks) f.Tick();
            // Every 12-25 s about a third of the crows move over to the nearest other flock within 150 m, so the bird
            // being watched may well leave with others: keeping track of one means following it.
            if (flocks.Count > 1 && Time.time > nextShuffle)
            {
                if (nextShuffle > 0f)
                    foreach (var b in Birds)
                    {
                        if (b == null || b.Robot || b.Down || shuffle.NextDouble() > 0.35) continue;
                        b.JoinFlock(NearestOther(b.Flock, b.transform.position, 150f));
                    }
                nextShuffle = Time.time + 12f + (float)shuffle.NextDouble() * 13f;
            }
        }

        // The nearest flock in the air (not landed) within `range` of p.
        public static Flock FlockNear(Vector3 p, float range) => NearestOther(null, p, range);

        static Flock NearestOther(Flock not, Vector3 p, float range)
        {
            Flock best = null;
            float bestDistance = range;
            foreach (var f in flocks)
            {
                if (f == not || f.Landing) continue;
                float d = Vector2.Distance(new Vector2(f.Centre.x, f.Centre.z), new Vector2(p.x, p.z));
                if (d < bestDistance)
                {
                    best = f;
                    bestDistance = d;
                }
            }
            return best;
        }
    }
}

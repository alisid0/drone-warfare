using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The kinds of evidence that a "bird" is a machine. Confirming one takes two different kinds, so no single
    // sighting (a hover, which a real bird can do; a straight run) is ever enough on its own.
    public enum Clue { Movement, Mechanical, Acoustic, Thermal, Electronic }

    // What's been seen of one contact: the clues found, how much they weigh, and how sure that makes us.
    public class Evidence
    {
        public int Points;
        public int Kinds;                       // a bit per Clue found
        public readonly List<string> Found = new List<string>();
        public float Watched;                   // seconds kept in a drone's camera
        public float Close;                     // seconds watched zoomed in within 45 m
        public bool Tagged;
        public float TooClose;                  // seconds a drone has been within 10 m (robots bolt at 2)
        public bool Sustained;                  // a clue that took watching: a hover, the loop, the wingbeat, a traced burst
        public float HoverWatched;              // seconds watched holding still in the air
        public float Aim;                       // 0-1, how steady the shot is (see Mimic.WouldHit)
        public bool Announced;                  // the confirmation has been called out
        public bool Occluded;                   // trees between the feed's camera and it (held: see Tick)
        public float BlockedFor, ClearFor;      // how long the line to it has been blocked, or clear
        public readonly float[] KindAt = new float[5]; // when each kind of clue last came in (the panel flashes it)
        public const float MinWatch = 8f;       // no confirmation from a glance

        public int KindCount { get { int n = 0; for (int k = Kinds; k != 0; k &= k - 1) n++; return n; } }
        public bool Suspicious => Points >= 2;
        // Three points from two kinds, one of them a clue that took watching, and 8 s watched in all: close-range
        // giveaways (a glint, the servo's whine) raise suspicion but can't confirm on their own.
        public bool Confirmed => Points >= 3 && KindCount >= 2 && Sustained && Watched >= MinWatch;
        public float Confidence => (Mathf.Min(Points / 3f, 1f) + Mathf.Min(KindCount / 2f, 1f) + (Sustained ? 1f : 0f) + Mathf.Min(Watched / MinWatch, 1f)) / 4f;
        public string Status => Confirmed ? "CONFIRMED MACHINE" : Suspicious ? "SUSPICIOUS" : "OBSERVED";

        public bool Add(Clue kind, int points, string what, bool sustained = false)
        {
            if (Found.Contains(what)) return false;
            Found.Add(what);
            Points += points;
            Kinds |= 1 << (int)kind;
            KindAt[(int)kind] = Time.time;
            Sustained |= sustained;
            return true;
        }
    }

    // Mimic, the counter-surveillance hunt: telling the Dravin spy robots from the crows they pass for, and bringing
    // them down. Click a bird in the drone feed to track it (the camera follows it); clues build up while it's
    // watched, from what the camera, its microphone and our receivers can actually pick up:
    // - movement: it holds still in the air (a crow doesn't); it flies the same loop again (watched for 25 s);
    // - mechanical: zoomed in within 45 m for 6 s, its wingbeat is a motor's, perfectly even; a servo glints;
    // - acoustic: within 15 m the drone's microphone picks up a servo whine;
    // - thermal: in the thermal view within 50 m, a small hot spot at the shoulders, not a body's warmth;
    // - electronic: its radio bursts are traced to it when one of our drones is within 100 m.
    // A crow never shows any of these, so it can never be confirmed, and only a confirmed machine can be fired on.
    // Right-click a tracked bird: if it's confirmed, the drone fires (a Scout's hunter loadout: 3 precise shots
    // per sortie); if not, the drone shadows it. No dice: the shot hits if the aim is steady (see WouldHit).
    public static class Mimic
    {
        public const int Shots = 3;
        public static Bird Tracked { get; private set; }
        static readonly Dictionary<Drone, int> shotsLeft = new Dictionary<Drone, int>();
        public static int ShotsLeftFor(Drone d) => d != null && shotsLeft.TryGetValue(d, out int n) ? n : Shots;
        public static readonly Dictionary<Bird, Evidence> Seen = new Dictionary<Bird, Evidence>();
        static Drone shadowing;
        static Bird shadowed;
        static float nextShadowOrder;
        static Drone lastSource;
        static Vector3 lastSourcePosition;

        public static bool Active => Wildlife.Birds.Exists(b => b != null && b.Robot);
        public static Evidence Of(Bird b)
        {
            if (!Seen.TryGetValue(b, out var e)) Seen[b] = e = new Evidence();
            return e;
        }

        public static void Reset()
        {
            Tracked = null;
            shadowing = null;
            shadowed = null;
            shotsLeft.Clear();
            Seen.Clear();
        }

        public static void Track(Bird b)
        {
            Tracked = b;
            if (b != null)
            {
                Of(b).Tagged = true;
                Sound.Ui("click");
            }
        }

        // The right-click on a bird: fire if it's a confirmed machine, otherwise go and keep an eye on it.
        public static void Act(Bird b, Drone drone)
        {
            if (b == null || drone == null) return;
            Track(b);
            if (!Of(b).Confirmed)
            {
                shadowing = drone;
                shadowed = b;
                nextShadowOrder = 0f;
                SandboxGame.Instance?.ShowMessage($"{drone.unitName} shadowing the contact: watch it for clues", 2.5f);
                return;
            }
            Fire(b, drone);
        }

        // Would a shot from this drone hit right now? Only with the aim fully steady and a clear line (never through
        // the trees), and past 25 m only at a robot holding still. What the aim bar shows is what happens.
        public static bool WouldHit(Bird b, Drone drone)
        {
            if (b == null || drone == null) return false;
            float d = Vector3.Distance(drone.transform.position, b.transform.position);
            return Of(b).Aim >= 1f && d <= 40f && (d < 25f || b.Hovering) && !Hidden(b, drone);
        }

        // Trees in the way of this drone's shot: as the feed shows it when this is the feed's drone (the same verdict
        // as the OCCLUDED mark), otherwise straight from the drone.
        static bool Hidden(Bird b, Drone drone)
        {
            var feed = DroneFeed.Instance;
            return feed != null && feed.Source == drone ? Of(b).Occluded : Canopy.Blocks(drone.transform.position, b.transform.position);
        }

        // What's keeping the shot from being a sure one, for the hunt panel.
        public static string AimAdvice(Bird b, Drone drone)
        {
            float d = Vector3.Distance(drone.transform.position, b.transform.position);
            if (Hidden(b, drone)) return "OBSTRUCTED: trees in the way";
            if (d > 40f) return $"out of range ({d:0} m)";
            if (d >= 25f && !b.Hovering) return $"{d:0} m: closer, or wait for a hover";
            return "steadying: match its speed";
        }

        // A shot from the drone's gun at a confirmed machine, out of range past 40 m: it hits if WouldHit says so,
        // and a miss spooks it: it runs for home.
        public static bool Fire(Bird b, Drone drone)
        {
            var game = SandboxGame.Instance;
            if (b == null || b.Down || drone == null) return false;
            if (!Of(b).Confirmed)
            {
                game?.ShowMessage("Not confirmed: two different kinds of evidence are needed before firing", 3f);
                return false;
            }
            if (ShotsLeftFor(drone) <= 0)
            {
                game?.ShowMessage($"{drone.unitName} has no shots left", 2.5f);
                return false;
            }
            float d = Vector3.Distance(drone.transform.position, b.transform.position);
            if (d > 40f)
            {
                game?.ShowMessage($"Out of range ({d:0} m): get within 40 m", 2.5f);
                return false;
            }
            shotsLeft[drone] = ShotsLeftFor(drone) - 1;
            Sound.At("gunshot", drone.transform.position);
            bool hit = WouldHit(b, drone);
            Of(b).Aim = 0f;
            if (hit)
            {
                b.ShootDown();
                game?.Events.Add((new Vector2(b.transform.position.x, b.transform.position.z), true)); // on the debrief map
                Sound.At("robot down", b.transform.position, 1f, 0.03f);
                game?.ShowMessage("Spy robot down", 2.5f);
            }
            else
            {
                b.Spook();
                game?.ShowMessage($"Missed: it's running! ({ShotsLeftFor(drone)} shots left)", 2.5f);
            }
            return hit;
        }

        public static void Tick()
        {
            if (Tracked != null && (Tracked == null || Tracked.Down)) Tracked = null;
            Shadow();
            var feed = DroneFeed.Instance;
            var cam = feed != null ? feed.FeedCamera : null;
            Drone source = feed != null ? feed.Source : null;
            float dt = Time.deltaTime;
            var game = SandboxGame.Instance;
            Vector3 sourceVelocity = Vector3.zero;
            if (source != null && dt > 0f)
            {
                if (source == lastSource) sourceVelocity = (source.transform.position - lastSourcePosition) / dt;
                lastSource = source;
                lastSourcePosition = source.transform.position;
            }
            foreach (var b in Wildlife.Birds)
            {
                if (b == null || b.Down) continue;
                var e = Of(b);
                // A radio burst near one of our drones: traced to the contact if it's the one being tracked, otherwise
                // only a warning that one of the crows out there is transmitting.
                if (b.Robot && b.BurstNow && game != null && game.drones.Exists(d => d != null && Vector3.Distance(d.transform.position, b.transform.position) < 100f))
                {
                    if (b == Tracked && e.Add(Clue.Electronic, 2, "radio burst traced to it", true)) Announce(b, "Signal intercept: the data burst came from this one");
                    else if (b != Tracked) game.ShowMessage("Signal intercept: a data burst nearby. One of these crows is transmitting", 3f);
                }
                // The drone tracking a robot can give itself away: closer than 10 m for 2 s and the robot notices it
                // and bolts. (One that happens to fly past a drone busy with something else doesn't.)
                if (b.Robot && !b.Spooked && game != null && b != Tracked) e.TooClose = 0f;
                else if (b.Robot && !b.Spooked && game != null)
                {
                    bool close = game.drones.Exists(d => d != null && Vector3.Distance(d.transform.position, b.transform.position) < 10f);
                    e.TooClose = close ? e.TooClose + dt : 0f;
                    if (e.TooClose > 2f)
                    {
                        b.Spook();
                        game.ShowMessage("Too close: it's seen the drone and it's running!", 3f);
                        Sound.Alert("warning", 1f);
                    }
                }
                if (b != Tracked) continue;
                bool inView = false;
                float d = 0f;
                if (source != null && cam != null)
                {
                    Vector3 v = cam.WorldToViewportPoint(b.transform.position);
                    d = Vector3.Distance(source.transform.position, b.transform.position);
                    inView = v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f && d <= 150f;
                    // Hidden after two blocked looks 0.1 s apart, back in sight after 0.2 s clear, so the marker doesn't
                    // flicker on a line grazing a crown; the line runs from the camera itself, as the picture does.
                    bool blocked = inView && Canopy.Blocks(cam.transform.position, b.transform.position);
                    e.BlockedFor = blocked ? e.BlockedFor + dt : 0f;
                    e.ClearFor = blocked ? 0f : e.ClearFor + dt;
                    if (e.BlockedFor >= 0.1f) e.Occluded = true;
                    else if (e.ClearFor >= 0.2f) e.Occluded = false;
                }
                // The aim steadies on a bird in view within 40 m: quicker within 25 m, on a bird holding still, from a
                // drone matching its speed (holding still with a hovering one, or flying with it), and slower on one
                // that's running; it slips away otherwise.
                float drift = (sourceVelocity - b.Velocity).magnitude;
                float rate = !inView || e.Occluded || d > 40f ? -1f
                    : (d < 25f ? 1f / 1.5f : 1f / 3.5f) * (b.Hovering ? 2f : 1f) * (drift < 4f ? 1f : 0.35f) * (b.Spooked ? 0.5f : 1f);
                e.Aim = Mathf.Clamp01(e.Aim + rate * dt);
                if (!inView) continue;
                e.Watched += dt;
                bool zoomed = feed.Zoom >= 1.9f;
                if (zoomed && d < 45f) e.Close += dt;
                if (!b.Robot) continue; // a crow shows nothing a machine would
                if (b.Hovering && d < 80f) e.HoverWatched += dt;
                if (e.HoverWatched > 2f && e.Add(Clue.Movement, 1, "holds still in the air", true)) Announce(b, "It's holding still in the air");
                if (e.Watched > 25f && e.Add(Clue.Movement, 1, "flies the same loop again", true)) Announce(b, "Same loop again: it's patrolling");
                if (e.Close > 6f && e.Add(Clue.Mechanical, 2, "wingbeat perfectly even", true)) Announce(b, "Its wingbeat never varies: a motor");
                if (b.GlintNow && zoomed && d < 40f && e.Add(Clue.Mechanical, 1, "metal glint at the shoulder")) Announce(b, "A glint of metal at the wing");
                if (d < 15f && e.Add(Clue.Acoustic, 2, "servo whine")) Announce(b, "Servo whine on the microphone");
                if (DayNight.Instance != null && DayNight.Instance.Thermal && d < 50f && e.Add(Clue.Thermal, 1, "hot spot at the shoulders"))
                    Announce(b, "Thermal: a hot spot at the shoulders, not body heat");
                if (e.Confirmed && !e.Announced)
                {
                    e.Announced = true;
                    game?.ShowMessage("CONFIRMED: a machine. Steady the aim, then right-click to fire", 3.5f);
                    Sound.Alert("contact", 1f);
                }
            }
        }

        static void Announce(Bird b, string text)
        {
            SandboxGame.Instance?.ShowMessage(text, 3f);
            Sound.Ui("click");
        }

        // A drone shadowing a contact keeps 22 m behind it (outside the microphone's 15 m: hearing the servo means
        // closing in on purpose), re-ordered a few times a second.
        static void Shadow()
        {
            if (shadowing == null || shadowed == null || shadowed.Down || !shadowing.IsAlive)
            {
                shadowing = null;
                return;
            }
            if (Time.time < nextShadowOrder) return;
            nextShadowOrder = Time.time + 0.4f;
            Vector3 back = shadowed.Velocity.sqrMagnitude > 1f ? -shadowed.Velocity.normalized : (shadowing.transform.position - shadowed.transform.position).normalized;
            back.y = 0f;
            shadowing.MoveTo(shadowed.transform.position + back.normalized * 22f);
        }
    }
}

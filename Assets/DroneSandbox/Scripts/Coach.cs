using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Teaching through play (the 1.0 design, beats from ChatGPT's review): tips inside the first two Operations instead
    // of a separate tutorial. Each tip waits for its moment, says the one thing to do next, and goes the moment the
    // player does it. A mission already won (any stars) plays without them, and Settings can switch them off.
    public static class Coach
    {
        class Tip
        {
            public string text;
            public Func<SandboxGame, bool> when;  // ready to show
            public Func<SandboxGame, bool> done;  // the player has done it
            public float minimum;                 // seconds it stays up at least (a tip with no action to wait for)
        }

        static readonly List<Tip> tips = new List<Tip>();
        static int step;
        static float shownAt = -1f;

        public static string Current { get; private set; }   // the tip showing now, or null
        public static int Step => step;                       // tips done so far (for the tests)
        public static int Count => tips.Count;

        // Called when a mission begins: the tips for it, if it teaches anything and hasn't been won yet.
        public static void Begin(Mission mission, int index)
        {
            tips.Clear();
            step = 0;
            shownAt = -1f;
            Current = null;
            if (mission == null || index < 0 || !Settings.Tips || SandboxGame.BestStars(index) > 0) return;
            if (mission.name == "First Light") FirstLight();
            else if (mission.name == "Open the Sky") OpenTheSky();
        }

        // Once a frame in a mission: the current tip goes when it's done, and the next shows when its moment comes.
        public static void Tick(SandboxGame game)
        {
            Current = null;
            if (step >= tips.Count || game.BriefingOpen || game.Result != MissionResult.None) return;
            var tip = tips[step];
            if (shownAt < 0f)
            {
                if (!tip.when(game)) return;
                shownAt = Time.unscaledTime;
            }
            bool held = Time.unscaledTime - shownAt < tip.minimum;
            if (!held && tip.done(game))
            {
                step++;
                shownAt = -1f;
                return;
            }
            Current = tip.text;
        }

        static void Add(string text, Func<SandboxGame, bool> when, Func<SandboxGame, bool> done, float minimum = 0f) =>
            tips.Add(new Tip { text = text, when = when, done = done, minimum = minimum });

        static bool Any(SandboxGame g, Predicate<EnemyUnit> match) => g.enemies.Exists(e => e != null && match(e));
        static bool Launched(SandboxGame g, DroneType type) => g.drones.Exists(d => d != null && d.type == type);
        static bool Extracting(SandboxGame g) => g.Phase == MissionPhase.Extract;

        // First Light: launch, move, identify by watching, strike only the real thing, bring them home.
        static void FirstLight()
        {
            Add("Press <b>1</b> to launch a Scout (or click it in the bar below).",
                g => true, g => Launched(g, DroneType.Scout));
            Add("<b>Right-click</b> the map to send it. Try a grey circle: a truck was reported somewhere inside.",
                g => true, g => g.drones.Exists(d => d != null && d.type == DroneType.Scout && (d.MoveTarget.HasValue || Vector3.Distance(d.transform.position, g.basePosition) > Scale.D(30f))));
            Add("Keep the Scout over the contact a moment. Watching it is what <b>identifies</b> it: real truck, or decoy?",
                g => Any(g, e => e.LooksLikeTarget && e.Intel == Intel.Contact && g.drones.Exists(d => d != null && Vector3.Distance(d.transform.position, e.transform.position) < Scale.D(45f))),
                g => Any(g, e => (e.isTarget || e.isDecoy) && e.Intel >= Intel.Identified));
            Add("Identified. Press <b>3</b> for a Kamikaze, then <b>right-click the truck</b> to strike it. Spend them only on real trucks.",
                g => Any(g, e => e.isTarget && e.IsAlive && e.Intel >= Intel.Identified),
                g => g.TargetsDestroyed > 0 || g.drones.Exists(d => d != null && d.type == DroneType.Kamikaze && d.AttackTarget != null));
            Add("All targets down. Press <b>B</b> to bring every drone home before the clock runs out.",
                Extracting, g => false);
        }

        // Open the Sky: pause to read the air defence, take the radar first, strike together.
        static void OpenTheSky()
        {
            Add("The red ring is the AA gun's reach. Press <b>Tab</b> to pause and plan: you can still give orders while paused.",
                g => true, g => g.Paused);
            Add("Kill the <b>radar</b> first: it stretches the gun's reach. A Scout's gun can't hurt the tanks, but Strike drones <b>[2]</b> can, from outside the gun's ring.",
                g => true, g => !Any(g, e => e.type == EnemyType.Radar && e.IsAlive), 6f);
            Add("Two drones with attack orders: select both and press <b>G</b>, so their hits land together.",
                g => g.drones.FindAll(d => d != null && d.AttackTarget != null).Count >= 2, g => g.Coordinated > 0 || g.TargetsDestroyed >= 3);
            Add("All targets down. Press <b>B</b> to bring every drone home before the clock runs out.",
                Extracting, g => false);
        }
    }
}

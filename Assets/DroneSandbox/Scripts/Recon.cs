using UnityEngine;

namespace DroneSandbox
{
    // How much the Norvane side knows about an enemy.
    // - Unknown: nothing; it isn't on the map.
    // - Contact: something is roughly there (a briefing report, a gun flash), a grey "?" in a circle of doubt.
    // - Identified: seen and known for what it is. A vehicle out of sight leaves a fading ghost where it was last
    //   seen; after GhostSeconds the trail goes cold and it drops back to a contact.
    // - Confirmed: seen up close or watched for a couple of seconds.
    public enum Intel { Unknown, Contact, Identified, Confirmed }

    // The fog of war, both ways, worked out a few times a second.
    // - Norvane eyes: every drone sees out to IdentifyRange (less at night, unless it carries a thermal camera,
    //   and less in fog and rain), the forward base's mast sees BaseSensorRange around it, and long-range optics
    //   add a little. Vehicles are drawn only while something is watching them; guns, radars and the like stay on
    //   the map once found (they don't move). Camouflaged targets (EnemyUnit.Hidden) still need a drone right
    //   over them (SandboxGame.SpotHidden).
    // - Dravin eyes: every enemy notices drones within its own detection range, a radar sees a wider circle and
    //   warns everything it covers, and anything hit or near a unit that's hit goes on alert (EnemyUnit.Alert).
    public class Recon : MonoBehaviour
    {
        // The old 480 m map's sight distances, brought over by Scale.
        public const float IdentifyRange = 46f * Scale.Distance, ConfirmRange = 26f * Scale.Distance,
            NightRange = 24f * Scale.Distance, ThermalRange = 52f * Scale.Distance;
        public const float BaseSensorRange = 42f * Scale.Distance, OpticsBonus = 12f * Scale.Distance;
        public const float GhostSeconds = 25f;
        const float Interval = 0.15f;

        // Fog and rain shorten everyone's sight (set with the weather, see Atmosphere.SetWeather).
        public static float WeatherFactor = 1f;

        // Everything drawn whatever we know: photo mode, and the auto-test's pictures of the units.
        public static bool ForceShowAll;
        public static bool ShowAll => ForceShowAll || PhotoMode.Active;

        float timer;

        void Update()
        {
            var game = SandboxGame.Instance;
            if (game == null || game.Mode == GameMode.Menu) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Interval;

            float dark = DayNight.Instance != null ? 1f - DayNight.Instance.Daylight : 0f;
            float eyes = (Mathf.Lerp(IdentifyRange, NightRange, dark) + (Tech.Active(TechId.Optics) ? OpticsBonus : 0f)) * WeatherFactor;
            float thermal = ThermalRange * Mathf.Lerp(1f, WeatherFactor, 0.5f); // heat cuts through haze better than sight
            float confirm = ConfirmRange * WeatherFactor;
            foreach (var e in game.enemies.ToArray())
            {
                if (e == null || !e.IsAlive) continue;
                Vector3 p = e.transform.position;
                float nearest = float.MaxValue, nearestThermal = float.MaxValue;
                Drone nearestDrone = null;
                foreach (var d in game.drones)
                {
                    if (d == null) continue;
                    Vector3 offset = d.transform.position - p;
                    offset.y = 0f;
                    float distance = offset.magnitude / d.SightFactor; // the Swift's wide camera sees further
                    if (distance < nearest)
                    {
                        nearest = distance;
                        nearestDrone = d;
                    }
                    if (d.HasThermal) nearestThermal = Mathf.Min(nearestThermal, distance);
                }
                Vector3 toBase = game.basePosition - p;
                toBase.y = 0f;
                bool seen = nearest <= eyes || nearestThermal <= thermal || toBase.magnitude <= BaseSensorRange;
                e.Observe(seen, nearest <= confirm, Interval);
                if (nearestDrone != null) e.Notice(nearestDrone, nearest, dark);
            }
        }
    }
}

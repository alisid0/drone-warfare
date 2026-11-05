using UnityEngine;

namespace DroneSandbox
{
    // How big the world is. The game was first built on a 480 m map: drones flew 6-12 m up, fights happened 10-25 m
    // apart and the camera hung 15-110 m away, which read as a model railway. It now plays on a 2 km map at real
    // scale. Every distance that says where something is or how far something reaches (the layout, weapon and
    // sensor ranges, flight heights, the camera) is still written in the old map's metres and multiplied by
    // Distance, so the game keeps the balance it was tuned with. Real sizes (a road's width, a house, a tree, a
    // vehicle, a field's tramlines) are not multiplied. Things move only Speed times quicker, so getting anywhere
    // takes TravelTime times as long as it did: that's the feel of distance.
    public static class Scale
    {
        // Every distance of the old 480 m map, in today's 2 km one.
        public const float Distance = 2000f / 480f;

        // How much quicker everything moves (drones, vehicles, shells, birds) than on the old map. Less than
        // Distance, so a trip takes TravelTime times as long as it did.
        public const float Speed = 3f;
        public const float TravelTime = Distance / Speed;

        // The play camera's vertical field of view, narrower than the old 60 degrees: a long lens flattens the
        // land the way an aerial photo does, where a wide one close in looks like a model. Camera distances are
        // the old ones times Camera, which frames the same ground at the narrower angle.
        public const float CameraFov = 45f;
        // How much further a camera has to stand to frame something at CameraFov as it did at 60 degrees (for
        // close-ups of things that are their real size).
        public static readonly float Lens = Mathf.Tan(30f * Mathf.Deg2Rad) / Mathf.Tan(CameraFov * 0.5f * Mathf.Deg2Rad);
        public static readonly float Camera = Distance * Lens; // (after Lens: static fields start in order)

        // A distance written in the old map's metres, in today's.
        public static float D(float oldMetres) => oldMetres * Distance;
    }
}

using UnityEngine;

namespace DroneSandbox
{
    // Shared projection and tier rules keep instanced compounds consistent with Art and LandmarkClutter.
    public static class LandmarkLod
    {
        public const float Near = 850f, Far = 1100f;

        // Sector sites use this same half-open anchor ownership, including the five-metre edge allowance.
        public static bool InSector(Vector2 p) => Mathf.Abs(p.x) <= 995f && Mathf.Abs(p.y) <= 995f;

        // Perspective FOV and quality bias convert eye distance into the reference strategy-camera distance.
        public static Vector2 Projection(Camera camera)
        {
            float reference = Mathf.Tan(Scale.CameraFov * .5f * Mathf.Deg2Rad);
            float bias = Mathf.Max(.01f, QualitySettings.lodBias);
            return new Vector2(Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) / reference / bias,
                camera.orthographic ? camera.orthographicSize / reference / bias : -1f);
        }

        // Buildings retain the 65/250 m tiers; equipment retains the sector's 65/150/275 m limits.
        public static Vector2 Range(int tier, bool detail, bool prop)
        {
            float end = prop ? (tier == 0 ? 150f : 275f) : tier == 0 ? 65f : tier == 1 ? (detail ? 150f : 250f) : (detail ? 275f : Far);
            float start = tier == 0 ? 0f : prop ? 150f : tier == 1 ? 65f : detail ? 150f : 250f;
            return new Vector2(start, end);
        }
    }
}

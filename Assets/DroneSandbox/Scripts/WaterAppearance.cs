using System;
using UnityEngine;

namespace DroneSandbox
{
    // Shared, allocation-free water colours and reach sizes keep the terrain and distant painters in agreement.
    public static class WaterAppearance
    {
        public static readonly Vector3 River = FarDetailPalette.Decode(new Color32(91, 119, 126, 255));
        public static readonly Vector3 Shallows = FarDetailPalette.Decode(new Color32(126, 145, 121, 255));
        public static readonly Vector3 Lake = FarDetailPalette.Decode(new Color32(92, 124, 143, 255));
        public static readonly Vector3 Gravel = FarDetailPalette.Decode(new Color32(151, 143, 121, 255));
        public static readonly Vector3 WetMud = FarDetailPalette.Decode(new Color32(81, 77, 62, 255));

        // Broad 180/270 m patterns make alternating pools and riffles without random state or sector seams.
        public static float Pool(float x, float z) => .5f + .3f * (float)Math.Sin((x + z * .37f) * .034906585f)
            + .2f * (float)Math.Sin((z - x * .29f) * .023271057f + 1.7f);

        // Stay within the surveyed channel envelope; navigation and bridge clearances remain conservative.
        public static float HalfWidth(float surveyed, float x, float z) => surveyed * (.80f + .20f * Pool(x, z));

        // Physical bank widths, in metres: broad depositional bars inside bends and narrow outer cut banks.
        public static float Margin(float inner, float pool) => Math.Max(1f, Math.Min(4f, 2.1f + 1.5f * inner + .4f * pool));

        // A wet mud fringe is separate from the pale, dry gravel immediately above it.
        public static float WetEdge(float shore) => 1f - Smooth((shore - .2f) / 1.6f);

        // Smooth bounded weights avoid terraces in the river bed and hard stripes in the shore paint.
        public static float Smooth(float t) { t = Math.Max(0f, Math.Min(1f, t)); return t * t * (3f - 2f * t); }
    }
}

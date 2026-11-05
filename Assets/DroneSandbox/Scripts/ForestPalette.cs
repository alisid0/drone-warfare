using UnityEngine;

namespace DroneSandbox
{
    // Linear-light averages of opaque top-view impostor pixels, multiplied by ProjectSetup's exact tints.
    // Regenerate with Tools/ForestHlod/make_palette.py when the source pictures or those tints change.
    public static class ForestPalette
    {
        public static readonly Color32[] Colours =
        {
            new Color32(77, 98, 51, 255),
            new Color32(99, 116, 61, 255),
            new Color32(64, 87, 58, 255),
            new Color32(80, 99, 64, 255),
        };

        // The ground between cards belongs in the brightness reference too, especially for narrow conifers.
        public static readonly Color Ground = new Color(54f / 255f, 70f / 255f, 42f / 255f).linear;
        static readonly float[] AreaPerHeight = { 0.49611387f, 0.09716409f, 0.14070420f, 0.14643798f };

        // Expected opaque crown coverage on the jittered 12 m grid, allowing for overlapping crowns.
        public static Color Linear(ForestPattern.Stand stand)
        {
            float area = AreaPerHeight[stand.species] * stand.height * stand.height * stand.width * stand.width /
                (ForestPattern.TreeStep * ForestPattern.TreeStep);
            float coverage = 1f - Mathf.Exp(-area);
            Color crown = ((Color)Colours[stand.species]).linear * Mathf.Pow(ForestPattern.Tint(stand.shade), 2.2f);
            // Crossed side cards add light beyond their top footprint; the 60-degree harness view measures this correction.
            return Color.Lerp(Ground, crown, coverage) * 1.15f;
        }
    }
}

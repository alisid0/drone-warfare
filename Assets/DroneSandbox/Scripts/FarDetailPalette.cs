using System;
using UnityEngine;

namespace DroneSandbox
{
    // Linear averages of the real source textures, with Ground.BuildLayers' linear tints applied.
    // The harness measures these again; the world picture must represent a filtered material, not a guessed swatch.
    public static class FarDetailPalette
    {
        public static readonly string[] Photos = { "meadow", "lush", "dirt", "rock", "meadow" };
        public static readonly float[] Metres = { 7f, 6f, 5f, 12f, 8f, 8f, 8f, 8f, 8f, 8f };
        public static readonly Vector3[] Tints = {
            new Vector3(.40f, .74f, .57f), new Vector3(.21f, .55f, .59f), new Vector3(1.2f, 1.23f, 1.28f),
            new Vector3(1f, 1f, 1f), new Vector3(.68f, .83f, .80f) };
        public static readonly Vector3[] Means = {
            new Vector3(.11790269f, .18573995f, .06983728f), new Vector3(.06715037f, .12965796f, .06262422f),
            new Vector3(.18629560f, .13643593f, .08462692f), new Vector3(.17465124f, .12373705f, .05288204f),
            new Vector3(.20043458f, .20832994f, .09801723f),
            new Vector3(.3996122f, .29260698f, .10530961f), new Vector3(.09635946f, .1163112f, .025008459f),
            new Vector3(.08767767f, .048417587f, .02307883f), new Vector3(.120508745f, .11250197f, .008999671f), new Vector3(.31333017f, .30759597f, .09097056f) };

        // Byte encoding happens only after linear coverage blending, matching an sRGB texture's mip filter.
        public static Color32 Encode(Vector3 c) => new Color32(Byte(c.x), Byte(c.y), Byte(c.z), 255);

        // Decode painted pixels before area averaging; averaging gamma bytes darkens narrow pale features.
        public static Vector3 Decode(Color32 c) => new Vector3(Linear(c.r / 255f), Linear(c.g / 255f), Linear(c.b / 255f));

        // The standard sRGB transfer, with no native engine calls on a tile worker.
        public static float Linear(float x) => x <= .04045f ? x / 12.92f : (float)Math.Pow((x + .055f) / 1.055f, 2.4);

        // Quantize a linear channel once at the end of painting.
        static byte Byte(float x) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(255f * (x <= .0031308f ? 12.92f * x : 1.055f * (float)Math.Pow(x, 1f / 2.4f) - .055f))));
    }
}

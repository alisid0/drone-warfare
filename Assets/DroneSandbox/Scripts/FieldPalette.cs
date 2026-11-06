using System;
using UnityEngine;

namespace DroneSandbox
{
    // Every colour the cultivated-land pass adds, in one place. Grass is a mix of the existing ground layers and crops
    // are linear multipliers around their own means, so rescaling FarDetailPalette.Means (or these few numbers)
    // recalibrates the sector, the 2 m tiles, the regional ring and the world picture together.
    public static class FieldPalette
    {
        // Grass is described as splat weights over pasture (layer 0): damp grass (1), dry rough grass (4), soil (2).
        // A grass strip along a field edge: pasture with damp grass, greener and a shade darker.
        public const float StripWet = .3f;
        // An uncut margin or rough grazing: tussocky, browner and darker than the meadow, never paler.
        public const float RoughWet = .3f, RoughDry = .35f, RoughSoil = .25f;
        // Pasture paddocks: grazed short (a little dry grass shows, so brighter) and hay meadow (paler, yellower).
        public const float GrazedDry = .12f, HayDry = .42f;
        // Thistle and dock patches in rough grazing: damper green over trodden soil.
        public const float ThistleWet = .3f, ThistleSoil = .08f;
        // Grazing and growth drifts move grass this far toward damp or dry grass over 12-35 m (about 5-9 sRGB levels).
        public const float GrassDrift = .9f;
        // Crop vigour drifts over the same 12-35 m, as a fraction of linear albedo either way.
        public const float GrowthContrast = .5f;
        // Each field's own value, either way, in linear albedo.
        public const float FieldValue = .12f;
        // A lighter, poorer soil band grows a slightly paler crop.
        public const float SoilVigour = .06f;
        // A damp hollow in a crop at its wettest: a darker, slightly greener, taller crop (linear multiplier), and the
        // same as one brightness factor for the sector's grey detail map.
        public static readonly Vector3 DampCrop = new Vector3(.84f, .91f, .84f);
        public const float DampBrightness = .89f;
        // The crop thins over its last 2.5 m to a boundary, as a brightness factor.
        public const float EdgeThinning = .9f;

        // Per-crop hue ranges, chosen per field between two linear multipliers that average one, so every crop's mean
        // stays where it is: wheat greener to riper, a green crop bluish to yellowish, ploughland dark brown to
        // grey-brown with the soil, sunflowers, and hay or stubble green-tinged to pale straw.
        static readonly Vector3[] From = {
            new Vector3(.92f, 1.02f, 1.04f), new Vector3(.88f, .99f, 1.16f), new Vector3(.84f, .77f, .66f),
            new Vector3(.94f, 1f, 1.1f), new Vector3(.93f, 1.02f, 1f) };
        static readonly Vector3[] To = {
            new Vector3(1.08f, .98f, .96f), new Vector3(1.12f, 1.01f, .84f), new Vector3(1.16f, 1.23f, 1.34f),
            new Vector3(1.06f, 1f, .9f), new Vector3(1.07f, .98f, 1f) };

        // The grass strip and rough margin as linear colours, for the sector's flat margin meshes and the painters.
        public static Vector3 Strip => Grass(StripWet, 0f, 0f);
        public static Vector3 Rough => Grass(RoughWet, RoughDry, RoughSoil);

        // Splat weights to a linear colour exactly as Ground layers them: soil over damp grass over dry over pasture.
        public static Vector3 Grass(float wet, float rough, float soil)
        {
            var m = FarDetailPalette.Means;
            Vector3 c = m[0] + (m[4] - m[0]) * rough;
            c += (m[1] - c) * wet;
            return c + (m[2] - c) * soil;
        }

        // One field's linear multiplier for crop layer 5-9: its hue within the crop's range, its own value and the
        // soil band (0 dark and heavy, 1 light and grey) under it.
        public static Vector3 CropTint(int layer, float shade, float soil)
        {
            int k = Math.Max(0, Math.Min(4, layer - 5));
            float t = k == 2 ? Math.Max(0f, Math.Min(1f, .65f * soil + .35f * shade)) : shade;
            Vector3 hue = From[k] + (To[k] - From[k]) * t;
            float value = 1f + FieldValue * (2f * Frac(shade * 7.31f) - 1f);
            if (k != 2) value *= 1f + SoilVigour * (soil - .5f);
            return hue * value;
        }

        // Channel-by-channel product of a linear colour and a tint (Vector3.Scale without the engine dependency).
        public static Vector3 Apply(Vector3 c, Vector3 tint) => new Vector3(c.x * tint.x, c.y * tint.y, c.z * tint.z);

        // Positive fractional part, for a second independent draw from one shade.
        static float Frac(float x) => x - (float)Math.Floor(x);
    }
}

using System;
using UnityEngine;

namespace DroneSandbox
{
    // What runs along one field edge: a grass strip, a hedge, a field track, crop to the boundary or a wide uncultivated margin.
    public enum FieldEdge : byte { Strip, Hedge, Track, Bare, Wide }

    // Shared metre-scale soil and growth signals leave the parcel graph untouched at every viewing distance.
    public static class FieldAppearance
    {
        // One edge's use from a parcel's packed four-bit edge list (0 lower side, 1 far end, 2 upper side, 3 near end).
        public static int EdgeUse(ushort edges, int edge) => (edges >> (edge * 4)) & 15;

        // Uncultivated width inside an edge; the painter, the control map and the sector margin meshes all use it.
        // A bare edge takes the crop right to the boundary; strips and rough margins wander over 13-60 m.
        public static float MarginFor(int use, float x, float z)
        {
            if (use == (int)FieldEdge.Bare) return 0f;
            if (use == (int)FieldEdge.Track) return 2.9f;
            if (use == (int)FieldEdge.Wide) return 3.4f + 1.8f * Noise(x / 61f - 17f, z / 61f + 5f) + .9f * (Noise(x / 13f + 29f, z / 13f - 3f) - .5f);
            return Margin(x, z);
        }

        // Which edge of the uncut parent is nearest, using Edge's own distances: 0 lower side, 1 far end, 2 upper, 3 near end.
        public static int NearestEdge(FieldPlot parent, Vector2 point)
        {
            Vector2 d = FieldPlot.Unwarp(point) - parent.center;
            float u = Vector2.Dot(d, parent.along), v = Vector2.Dot(d, parent.Across) - parent.shear * u;
            float slope = parent.halfWidth * parent.taper / parent.halfLength;
            float left = (parent.HalfWidthAt(u) + v) / (float)Math.Sqrt(1f + (parent.shear - slope) * (parent.shear - slope));
            float right = (parent.HalfWidthAt(u) - v) / (float)Math.Sqrt(1f + (parent.shear + slope) * (parent.shear + slope));
            float end = parent.halfLength - Math.Abs(u);
            if (end < left && end < right) return u > 0f ? 1 : 3;
            return left <= right ? 0 : 2;
        }

        // Gate positions use the original edge, so a clipped fragment cannot invent another tractor entrance.
        public static Vector2 Gate(Vector2 a, Vector2 b)
        {
            float length = (b-a).magnitude;
            return a + (b-a) * Math.Min(.22f, 6f / Math.Max(1f,length));
        }

        // Only edge pixels need gate distances; interiors avoid four square roots per crop query.
        public static float GateDistance(float u, float v, float u0, float u1, float a0, float a1, float b0, float b1)
        {
            Vector2 p = new Vector2(u,v), a = new Vector2(u0,a0), b = new Vector2(u1,b0), c = new Vector2(u1,b1), d = new Vector2(u0,a1);
            return Math.Min(Math.Min((p-Gate(a,b)).magnitude,(p-Gate(b,c)).magnitude),
                Math.Min((p-Gate(c,d)).magnitude,(p-Gate(d,a)).magnitude));
        }
        // Connected 12-35 m growth drifts from a 24 m and an 11 m octave. Coarse pixels drop the fine octave (its mean
        // is a half) and average four quarter-pixel samples of the broad one.
        public static float Growth(float x, float z, float pixel = 2f)
        {
            if (pixel <= 4f) return .62f * Noise(x / 24f + 17f, z / 24f - 9f) + .38f * Noise(x / 11f - 31f, z / 11f + 5f);
            float q = pixel * .25f;
            return .19f + .62f * .25f * (Noise((x - q) / 24f + 17f, (z - q) / 24f - 9f) + Noise((x + q) / 24f + 17f, (z - q) / 24f - 9f)
                + Noise((x - q) / 24f + 17f, (z + q) / 24f - 9f) + Noise((x + q) / 24f + 17f, (z + q) / 24f - 9f));
        }

        // Damp hollows in a crop: rare, soft-edged, and only where the ground lies below its surroundings (a field
        // bottom or a broad hollow, `low` metres deep), broken into patches by a 37 m signal. 0 dry, 1 at the wettest.
        public static float Damp(float x, float z, float low)
        {
            if (low <= .1f) return 0f;
            float n = Noise(x / 37f - 61f, z / 37f + 37f);
            return Smooth((low - .26f + .3f * (n - .5f)) / .18f) * Smooth((n - .25f) / .35f);
        }

        // A regional soil band, 0 dark and heavy to 1 light and grey, over 400 m to 1.3 km: ploughland and crop vigour follow it.
        public static float Soil(float x, float z) => .6f * Noise(x / 1300f + 41f, z / 1300f - 17f) + .4f * Noise(x / 410f - 5f, z / 410f + 63f);

        // One field's own variation (0-1) from its parcel's identity, never from a random stream or the sector origin.
        public static float Shade(int a, int b, int c, int d)
        {
            uint h = unchecked((uint)a * 374761393u + (uint)b * 668265263u + (uint)c * 2246822519u + (uint)d * 3266489917u + 1274126177u);
            h = unchecked((h ^ (h >> 13)) * 1274126177u); h ^= h >> 16;
            return (h & 65535) / 65536f;
        }

        // A parcel's margin is fixed in world space; it is never added again at an internal sector clipping seam.
        // 1-2 m of edge vegetation, irregular over 13-60 m.
        public static float Margin(float x, float z) => .9f + .8f * Noise(x / 61f + 3f, z / 61f + 21f) + .7f * (Noise(x / 13f - 7f, z / 13f + 11f) - .5f);

        // One field's crop colour (linear), shared by the distant painters: the crop's mean in this field's hue, value
        // and soil band, its 12-35 m growth drifts, a soft damp hollow, and thinner growth over its last 2.5 m.
        public static Vector3 Crop(int layer, float x, float z, float pixel, float shade, float soil, float damp, float edge)
        {
            Vector3 tint = FieldPalette.CropTint(layer, shade, soil);
            Vector3 c = FieldPalette.Apply(FarDetailPalette.Means[layer], tint) * (1f + FieldPalette.GrowthContrast * (Growth(x, z, pixel) - .5f));
            if (damp > 0f) c = FieldPalette.Apply(c, Vector3.one + (FieldPalette.DampCrop - Vector3.one) * damp);
            return c * (FieldPalette.EdgeThinning + (1f - FieldPalette.EdgeThinning) * Smooth(edge / 2.5f));
        }

        // Shared grass for the sector splat and every distant painter, as splat weights over pasture: damp grass,
        // dry rough grass and soil. Wetness carries drainage; a paddock (0-1, or below 0 for none) shows its use by
        // `amount` (faded at its fences and off open land): grazed short, hay meadow or rough grazing with thistles.
        // 12-35 m grazing and growth drifts move every meadow a few levels lusher or drier.
        public static void Grass(float x, float z, float wetness, float paddock, float amount, float pixel, out float wet, out float rough, out float soil)
        {
            wet = .8f * Smooth((wetness - .55f) / .4f);
            rough = .55f * Smooth((.45f - wetness) / .4f);
            soil = 0f;
            if (paddock >= 0f && amount > 0f)
            {
                if (paddock < .45f) rough += (FieldPalette.GrazedDry - .4f * rough) * amount;
                else if (paddock < .75f) rough += Math.Max(0f, FieldPalette.HayDry - rough) * amount;
                else
                {
                    float thistle = pixel <= 4f ? Smooth((Noise(x / 16f + 23f, z / 16f - 41f) - .66f) / .14f) : .12f;
                    wet += Math.Max(0f, FieldPalette.RoughWet + FieldPalette.ThistleWet * thistle - wet) * amount;
                    rough += Math.Max(0f, FieldPalette.RoughDry - rough) * amount;
                    soil = (.6f * FieldPalette.RoughSoil + FieldPalette.ThistleSoil * thistle) * amount;
                }
            }
            float drift = (Growth(x, z, pixel) - .5f) * FieldPalette.GrassDrift;
            if (drift > 0f) wet = Math.Min(1f, wet + drift); else rough = Math.Min(1f, rough - drift);
        }

        // Sparse damp hollows use a separate broad signal so not every dark crop drift turns into bare ground.
        public static float Patch(float x, float z, float pixel = 2f)
        {
            float a = Noise(x / 26f - 61f, z / 26f + 37f);
            float b = Noise(x / 81f + 5f, z / 81f + 91f);
            return Math.Max(0f, Math.Min(1f, (a - .72f) * 5f)) * Math.Max(0f, Math.Min(1f, (b - .56f) * 5f))
                * Math.Min(1f, 26f / Math.Max(1f, pixel));
        }

        // Linear crop means are shared with the distant painter, including grass margins and damp rough patches.
        public static Vector3 Colour(int layer, float x, float z, float edge, float pixel) => Colour(layer, x, z, edge, pixel, Margin(x, z));

        // The same with the edge's own uncultivated width; zero when the caller has already subtracted it.
        public static Vector3 Colour(int layer, float x, float z, float edge, float pixel, float margin)
        {
            Vector3 crop = FarDetailPalette.Means[layer] * (1f + .55f * (Growth(x, z, pixel) - .5f));
            float patch = Patch(x, z, pixel);
            Vector3 rough = FarDetailPalette.Means[4] * .8f + FarDetailPalette.Means[1] * .2f;
            crop += (rough - crop) * patch;
            float inside = FarDetailData.Coverage(edge - margin, pixel);
            return rough + (crop - rough) * inside;
        }

        // Project into the uncut parent frame before measuring distance to its two ends and sloping sides.
        public static float Edge(FieldPlot parent, Vector2 point)
        {
            Vector2 d = FieldPlot.Unwarp(point) - parent.center;
            float u = Vector2.Dot(d, parent.along), v = Vector2.Dot(d, parent.Across) - parent.shear * u;
            float slope = parent.halfWidth * parent.taper / parent.halfLength;
            float left = (parent.HalfWidthAt(u) + v) / (float)Math.Sqrt(1f + (parent.shear - slope) * (parent.shear - slope));
            float right = (parent.HalfWidthAt(u) - v) / (float)Math.Sqrt(1f + (parent.shear + slope) * (parent.shear + slope));
            return Math.Min(parent.halfLength - Math.Abs(u), Math.Min(left, right));
        }

        // Managed smoothstep, matching Mathf.SmoothStep without a native call on a tile worker.
        static float Smooth(float t) { t = Math.Max(0f, Math.Min(1f, t)); return t * t * (3f - 2f * t); }

        // Integer lattice hashing is worker-safe and deterministic on both Unity and the managed review harness.
        static float Noise(float x, float z)
        {
            int i = (int)Math.Floor(x), j = (int)Math.Floor(z);
            float u = x - i, v = z - j; u = u * u * (3f - 2f * u); v = v * v * (3f - 2f * v);
            float a = Value(i, j) + (Value(i + 1, j) - Value(i, j)) * u;
            float b = Value(i, j + 1) + (Value(i + 1, j + 1) - Value(i, j + 1)) * u;
            return a + (b - a) * v;
        }

        // Unsigned arithmetic avoids overflow-mode differences and keeps the colour field independent of call order.
        static float Value(int x, int z)
        {
            uint h = unchecked((uint)x * 374761393u + (uint)z * 668265263u + 1274126177u);
            h = unchecked((h ^ (h >> 13)) * 1274126177u); h ^= h >> 16;
            return (h & 65535) / 65535f;
        }
    }
}

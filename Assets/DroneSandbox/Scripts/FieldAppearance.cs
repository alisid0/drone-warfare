using System;
using UnityEngine;

namespace DroneSandbox
{
    // Shared metre-scale soil and growth signals leave the parcel graph untouched at every viewing distance.
    public static class FieldAppearance
    {
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
        // Twenty-metre cells form connected 10-30 m growth drifts; coarse pixels average four quarter-pixel samples.
        public static float Growth(float x, float z, float pixel = 2f)
        {
            if (pixel <= 4f) return Noise(x / 20f + 17f, z / 20f - 9f);
            float q = pixel * .25f;
            return (Noise((x - q) / 20f + 17f, (z - q) / 20f - 9f) + Noise((x + q) / 20f + 17f, (z - q) / 20f - 9f)
                + Noise((x - q) / 20f + 17f, (z + q) / 20f - 9f) + Noise((x + q) / 20f + 17f, (z + q) / 20f - 9f)) * .25f;
        }

        // Sparse damp hollows use a separate broad signal so not every dark crop drift turns into bare ground.
        public static float Patch(float x, float z, float pixel = 2f)
        {
            float a = Noise(x / 26f - 61f, z / 26f + 37f);
            float b = Noise(x / 81f + 5f, z / 81f + 91f);
            return Math.Max(0f, Math.Min(1f, (a - .72f) * 5f)) * Math.Max(0f, Math.Min(1f, (b - .56f) * 5f))
                * Math.Min(1f, 26f / Math.Max(1f, pixel));
        }

        // A parcel's margin is fixed in world space; it is never added again at an internal sector clipping seam.
        public static float Margin(float x, float z) => 1f + 1.5f * Noise(x / 93f + 3f, z / 93f + 21f);

        // Linear crop means are shared with the distant painter, including grass margins and damp rough patches.
        public static Vector3 Colour(int layer, float x, float z, float edge, float pixel)
        {
            Vector3 crop = FarDetailPalette.Means[layer] * (1f + .55f * (Growth(x, z, pixel) - .5f));
            float patch = Patch(x, z, pixel);
            Vector3 rough = FarDetailPalette.Means[4] * .8f + FarDetailPalette.Means[1] * .2f;
            crop += (rough - crop) * patch;
            float inside = FarDetailData.Coverage(edge - Margin(x, z), pixel);
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

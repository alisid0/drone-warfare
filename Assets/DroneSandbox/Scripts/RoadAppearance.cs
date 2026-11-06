using System;
using UnityEngine;

namespace DroneSandbox
{
    // Metre-based road wear is shared by sector ribbons and every distant ground resolution.
    public static class RoadAppearance
    {
        public const float Repeat = 192f;
        public static readonly Vector3 Asphalt = FarDetailPalette.Decode(new Color32(92, 91, 87, 255));
        public static readonly Vector3 Soil = FarDetailPalette.Decode(new Color32(110, 94, 68, 255));
        public static readonly Vector3 Gravel = FarDetailPalette.Decode(new Color32(132, 128, 113, 255));
        static readonly Vector3 Line = FarDetailPalette.Decode(new Color32(189, 186, 164, 255));
        static readonly Vector3 Damp = FarDetailPalette.Decode(new Color32(64, 70, 49, 255));

        // Keep the established highway and lane footprints; farm access varies within 2.5–3.5 metres.
        public static float HalfWidth(RoadClass kind) => kind == RoadClass.Highway ? 3.5f : kind == RoadClass.Road ? 2f : 1.5f;

        // Low-frequency variation leaves a grass shoulder outside the two half-metre compacted ruts.
        public static float TrackWidth(float along) => 1.5f + .25f * (float)Math.Sin(along * Math.PI * 2f / Repeat);

        // Box-integrated strips preserve energy when markings and wheel ruts become smaller than a pixel.
        public static float Strip(float at, float centre, float width, float pixel)
        {
            pixel = Math.Max(.001f, pixel);
            return Math.Max(0f, Math.Min(at + pixel * .5f, centre + width * .5f)
                - Math.Max(at - pixel * .5f, centre - width * .5f)) / pixel;
        }

        // Exact periodic integration prevents the 3 m on / 9 m off centre line aliasing into a solid bright stripe.
        public static float Dash(float along, float pixel)
        {
            float a = (along - pixel * .5f) / 12f, b = (along + pixel * .5f) / 12f;
            return (Integral(b) - Integral(a)) * 12f / Math.Max(.001f, pixel);
        }

        // Integral of the first quarter of each twelve-metre period, including negative world chainages.
        static float Integral(float t) => (float)Math.Floor(t) * .25f + Math.Min(t - (float)Math.Floor(t), .25f);

        // A deterministic repeated material retains worn repairs, crown and four main-road wheel paths in one lookup.
        public static Vector3 Surface(RoadClass kind, float across, float along, float halfWidth, float pixel, bool markings = true, float alongPixel = 0f)
        {
            if (alongPixel <= 0f) alongPixel = pixel;
            along -= (float)Math.Floor(along / Repeat) * Repeat;
            float drift = (float)Math.Sin(along * .09817477f);
            if (kind == RoadClass.Track)
                return Soil * (.91f + .08f * drift + .05f * Value((int)(along * 2f), (int)(across * 9f)));
            Vector3 c = Asphalt * (1f + .06f * drift);
            float wheels = kind == RoadClass.Highway
                ? Pair(across, .88f, .5f, pixel) + Pair(across, 2.22f, .5f, pixel)
                : Pair(across, .82f, .55f, pixel);
            c *= 1f - .23f * wheels;
            c += Asphalt * (.12f * Strip(across, 0f, .65f, pixel));
            int cell = (int)(along / 16f);
            float hash = Value(cell, 31), centre = (Value(cell, 19) - .5f) * halfWidth;
            float patch = hash > .49f ? Strip(along, cell * 16f + 7f, 2f + hash * 3.5f, alongPixel)
                * Strip(across, centre, .7f + hash * 1.3f, pixel) : 0f;
            c *= 1f - patch * .32f;
            float crack = Pair(across, halfWidth - .18f + .06f * (float)Math.Sin(along * 2.8f), .06f, pixel);
            c *= 1f - crack * (.18f + .22f * Value((int)(along * 3f), 7));
            if (markings && kind == RoadClass.Highway)
            {
                float wear = .48f + .28f * Value((int)(along * 1.5f), 11);
                float paint = Strip(across, 0f, .13f, pixel) * Dash(along, alongPixel)
                    + Pair(across, halfWidth - .32f, .12f, pixel);
                c += (Line - c) * Math.Min(1f, paint) * wear;
            }
            return c;
        }

        // Opposite lanes both contribute to a coarse footprint, even when its centre falls on the crown.
        static float Pair(float at, float centre, float width, float pixel) => Strip(at, centre, width, pixel) + Strip(at, -centre, width, pixel);

        // The two 0.4–0.6 m ruts leave the centre as live grass; coarse pixels retain their combined area.
        public static float Ruts(float across, float along, float pixel)
        {
            float width = .5f + .1f * (float)Math.Sin(along * .1308997f);
            return Strip(across, -.85f, width, pixel) + Strip(across, .85f, width, pixel);
        }

        // Narrow damp drains, rough verge and the mown shoulder have the same physical widths at every scale.
        public static Vector3 Paint(Vector3 ground, RoadNetwork.Sample road, float pixel, bool farmland)
        {
            if (!road.found || road.edge > 4f + pixel) return ground;
            float verge = 1.4f + .6f * Value(road.route, (int)Math.Floor(road.along / 96f));
            float width = road.width, d = road.distance;
            float outer = FarDetailData.BandCoverage(width + verge - d, width + verge, pixel);
            Vector3 c = ground + (FarDetailPalette.Means[4] * .86f - ground) * outer;
            float mown = FarDetailData.BandCoverage(width + .65f - d, width + .65f, pixel);
            c += (FarDetailPalette.Means[0] * .92f - c) * mown;
            if (farmland && road.kind != RoadClass.Track)
                c += (Damp - c) * (Strip(d, width + verge - .25f, .45f, pixel) * .8f);
            if (road.kind == RoadClass.Track)
            {
                float ruts = Ruts(road.across, road.along, pixel) * road.endFade;
                return c + (Surface(road.kind, road.across, road.along, width, pixel) - c) * ruts;
            }
            float cover = FarDetailData.BandCoverage(-road.edge, width, pixel);
            float lo = Math.Max(-width, road.across - pixel * .5f), hi = Math.Min(width, road.across + pixel * .5f);
            Vector3 surface = hi > lo ? Surface(road.kind, (lo + hi) * .5f, road.along, width, hi - lo, road.junction < .05f, pixel) : Asphalt;
            return c + (surface - c) * cover;
        }

        // Local hashes never advance the random stream used by villages, fields or woods.
        public static float Value(int x, int z)
        {
            uint h = unchecked((uint)x * 374761393u + (uint)z * 668265263u + 1274126177u);
            h = unchecked((h ^ (h >> 13)) * 1274126177u); h ^= h >> 16;
            return (h & 65535) / 65535f;
        }
    }
}

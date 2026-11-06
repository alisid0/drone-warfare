using System;
using UnityEngine;

namespace DroneSandbox
{
    // Shared ground painting for the 2 m tiles and the 13.7 m world picture; all masks carry a pixel footprint.
    public sealed partial class FarDetailData
    {
        // World setup culls vectors once per 32-pixel block and uses the same coverage painter as streaming.
        public Color32[] WorldPixels(int resolution)
        {
            var pixels = new Color32[resolution * resolution];
            float pixel = WorldPlan.Size / resolution;
            int blocks = (resolution + 31) / 32;
            System.Threading.Tasks.Parallel.For(0, blocks * blocks, block =>
            {
                int bx = block % blocks * 32, bz = block / blocks * 32;
                float x0 = -WorldPlan.Half + bx * pixel, z0 = -WorldPlan.Half + bz * pixel;
                var lanes = Nearby(roads, x0, z0, 32f * pixel); var streams = Nearby(rivers, x0, z0, 32f * pixel);
                for (int j = bz; j < Math.Min(bz + 32, resolution); j++) for (int i = bx; i < Math.Min(bx + 32, resolution); i++)
                    {
                        float x = -WorldPlan.Half + (i + .5f) * pixel, z = -WorldPlan.Half + (j + .5f) * pixel;
                        var at = new Vector2(x + 44f * (Mathf.PerlinNoise(x / 70f, z / 70f + 31f) - .5f),
                            z + 44f * (Mathf.PerlinNoise(x / 70f + 57f, z / 70f) - .5f));
                        pixels[j * resolution + i] = Paint(x, z, at, lanes, streams, pixel);
                    }
            });
            return pixels;
        }

        // Field coordinates survive into the control map so crops are sampled along their own district's rows.
        public struct FieldSample
        {
            public int layer;
            public float angle, across, along, width, length, side, end, districtEdge;
            public bool hedge;
        }

        // Four stratified samples integrate crop/cover changes; analytic strip coverage retains subpixel hedges.
        public Color32 Paint(float x, float z, Vector2 coverPosition, Segment[] lanes = null, Segment[] streams = null, float footprint = 2f)
        {
            Vector3 sum = Vector3.zero;
            for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++)
                {
                    float dx = (i - .5f) * footprint * .5f, dz = (j - .5f) * footprint * .5f;
                    sum += PaintPoint(x + dx, z + dz, new Vector2(coverPosition.x + dx, coverPosition.y + dz), lanes ?? roads, streams ?? rivers, footprint * .5f);
                }
            return FarDetailPalette.Encode(sum * .25f);
        }

        // Feature ownership is sampled separately from colour; IDs and angles are never interpolated as numbers.
        public Color32 Control(float x, float z, Vector2 coverPosition, Segment[] lanes = null, Segment[] streams = null)
        {
            Cover cover = CoverAt(coverPosition.x, coverPosition.y);
            float bank = Bank(x, z, streams ?? rivers, out _);
            if (cover == Cover.Water && bank < 64f) cover = Cover.Pasture;
            int layer = GroundLayer(cover, x, z); float angle = 0f; byte strength = 255;
            if (cover == Cover.Farmland)
            {
                var f = FieldAt(x, z); layer = f.layer; angle = f.angle;
                if (f.side < 3f || f.end < 2.5f || f.districtEdge < 4f) layer = 4;
            }
            if (bank < 4f) layer = 2;
            if (bank < 0f || cover == Cover.Water) strength = 0;
            Road(x, z, lanes ?? roads, out float edge, out int kind, out _);
            if (edge < 0f) { layer = 2; strength = kind == 0 ? (byte)50 : (byte)255; }
            int turn = (int)Math.Round(angle / Math.PI * 65535);
            return new Color32((byte)layer, (byte)(turn & 255), (byte)(turn >> 8), strength);
        }

        // Smooth coverage is measured in metres; roads win at crossings and retain their actual vector widths.
        Vector3 PaintPoint(float x, float z, Vector2 at, Segment[] lanes, Segment[] streams, float pixel)
        {
            Cover cover = CoverAt(at.x, at.y);
            float bank = Bank(x, z, streams, out float riverWidth);
            if (cover == Cover.Water && bank < 64f) cover = Cover.Pasture;
            Vector3 c = GroundColour(cover, x, z);
            if (cover == Cover.Farmland) c = FieldColour(FieldAt(x, z), pixel);
            if (cover == Cover.Village || cover == Cover.Town)
            {
                float street = Math.Min(Repeat(x, 90f), Repeat(z, 90f));
                float yard = Coverage(19f - Repeat(x, 30f), pixel) * Coverage(22f - Repeat(z, 30f), pixel);
                c = Blend(c, FarDetailPalette.Means[2], yard * (cover == Cover.Town ? .8f : .45f));
                c = Blend(c, FarDetailPalette.Means[2], Coverage(4f - street, pixel));
            }
            c = Blend(c, FarDetailPalette.Means[2], BandCoverage(3f - bank, riverWidth + 3f, pixel));
            c = Blend(c, WaterColour, BandCoverage(-bank, riverWidth, pixel));
            Road(x, z, lanes, out float edge, out int kind, out float roadWidth);
            c = Blend(c, FarDetailPalette.Means[4], BandCoverage(2f - edge, roadWidth + 2f, pixel));
            Vector3 road = kind == 0 ? FarDetailPalette.Decode(C(78, 78, 80)) : FarDetailPalette.Means[2];
            return Blend(c, road, BandCoverage(-edge, roadWidth, pixel));
        }

        static readonly Vector3 WaterColour = FarDetailPalette.Decode(new Color32(54, 82, 78, 255));

        // Match Ground's grass layering and high-ground bias; the sector collar uses its actual splat weights.
        Vector3 GroundColour(Cover cover, float x, float z)
        {
            if (cover == Cover.Water) return WaterColour;
            if (cover == Cover.Rock) return FarDetailPalette.Means[3];
            if (cover == Cover.Wetland) return FarDetailPalette.Means[1];
            if (cover == Cover.Forest) return FarDetailPalette.Means[1] * .7f;
            Grass(x, z, out float wet, out float rough);
            return Blend(Blend(FarDetailPalette.Means[0], FarDetailPalette.Means[4], rough), FarDetailPalette.Means[1], wet);
        }

        // Dominant fine texture only; the macro colour retains continuous grass mixtures without extra samplers.
        int GroundLayer(Cover cover, float x, float z)
        {
            if (cover == Cover.Rock) return 3;
            if (cover == Cover.Wetland || cover == Cover.Forest) return 1;
            Grass(x, z, out float wet, out float rough);
            return wet > .4f ? 1 : rough > .3f ? 4 : 0;
        }

        // Ground.BuildSplat's wet/rough transfer; broad colour variation stays small and tied to the terrain.
        void Grass(float x, float z, out float wet, out float rough)
        {
            float drift = Noise(x / 80f + 70f, z / 80f + 20f) * .65f + Noise(x / 25f + 9f, z / 25f + 4f) * .35f;
            float high = Smooth((Height(x, z) - 1.5f * Scale.Distance) / (8f * Scale.Distance));
            float wetness = Clamp(.5f - .45f * high - .7f * (drift - .5f), 0, 1);
            wet = .8f * Smooth((wetness - .55f) / .4f); rough = .55f * Smooth((.45f - wetness) / .4f);
        }

        // One district/strip definition supplies both painters and the high-frequency field orientation.
        public FieldSample FieldAt(float x, float z)
        {
            float fx = x / 700f, fz = z / 700f, best = float.MaxValue, second = float.MaxValue;
            int gx = (int)Math.Floor(fx), gz = (int)Math.Floor(fz), district = 0;
            for (int oz = -1; oz <= 1; oz++) for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = gx + ox, cz = gz + oz, h = Hash(cx, cz);
                    float sx = cx + .15f + .7f * ((h & 1023) / 1023f), sz = cz + .15f + .7f * (((h >> 10) & 1023) / 1023f);
                    float d = (sx - fx) * (sx - fx) + (sz - fz) * (sz - fz);
                    if (d < best) { second = best; best = d; district = h; } else if (d < second) second = d;
                }
            float angle = ((district >> 3) & 1023) / 1023f * (float)Math.PI, ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            float u = x * ca + z * sa, v = -x * sa + z * ca;
            float lookup = Clamp((u / 600f + (district & 63) + 68f) / 200f * (warp.Length - 1), 0, warp.Length - 1.001f);
            v += 30f * (Mix(warp[(int)lookup], warp[(int)lookup + 1], lookup - (int)lookup) - .5f);
            float width = 35f + ((district >> 13) & 63) / 63f * 75f;
            int strip = (int)Math.Floor(v / width), hs = Hash(strip, district);
            float length = 150f + (hs & 255) / 255f * 250f;
            float across = Repeat(v, width), along = Repeat(u + ((hs >> 8) & 1023), length);
            int crop = Hash((int)Math.Floor((u + ((hs >> 8) & 1023)) / length), hs) % 6;
            return new FieldSample { layer = crop < 2 ? 5 : crop == 2 ? 6 : crop == 3 ? 8 : crop == 4 ? 7 : 9,
                angle = angle, across = across, along = along, width = width, length = length,
                side = Math.Min(across, width - across), end = Math.Min(along, length - along),
                districtEdge = ((float)Math.Sqrt(second) - (float)Math.Sqrt(best)) * 700f, hedge = ((hs >> 20) & 1) == 0 };
        }

        // The same crop means as the sector, with footprint-integrated boundary strips rather than thresholded pixels.
        Vector3 FieldColour(FieldSample f, float pixel)
        {
            Vector3 c = FarDetailPalette.Means[f.layer];
            float projection = Math.Abs((float)Math.Cos(f.angle)) + Math.Abs((float)Math.Sin(f.angle));
            float side = BandCoverage(3f - f.side, 3f, pixel * projection), end = BandCoverage(2.5f - f.end, 2.5f, pixel * projection);
            Vector3 hedge = FarDetailPalette.Decode(C(46, 62, 36));
            c = Blend(c, f.hedge ? hedge : FarDetailPalette.Means[4], side * (f.hedge ? .6f : .45f));
            c = Blend(c, FarDetailPalette.Means[4], end * (1f - side) * .45f);
            return Blend(c, hedge, BandCoverage(4f - f.districtEdge, 4f, pixel * 1.414214f) * .6f);
        }

        // Exposed for the deterministic field/mask harness.
        public Color32 Field(float x, float z) => FarDetailPalette.Encode(FieldColour(FieldAt(x, z), 2f));

        // Union distances avoid painting a polyline's bends repeatedly and darkening road junctions.
        static void Road(float x, float z, Segment[] lanes, out float edge, out int kind, out float halfWidth)
        {
            edge = float.MaxValue; kind = 0; halfWidth = 0f;
            foreach (var s in lanes)
            {
                float d = Distance(s, x, z, out float width) - width;
                if (d < edge) { edge = d; kind = s.kind; halfWidth = width; }
            }
        }

        // The nearest bank supplies river coverage, including tapered widths and narrow tributaries.
        static float Bank(float x, float z, Segment[] streams, out float halfWidth)
        {
            float bank = float.MaxValue; halfWidth = 0f;
            foreach (var s in streams)
            {
                float d = Distance(s, x, z, out float width) - width;
                if (d < bank) { bank = d; halfWidth = width; }
            }
            return bank;
        }

        // Box-filter approximation for a signed edge distance, bounded for subpixel features.
        public static float Coverage(float inside, float pixel) => Clamp(.5f + inside / Math.Max(.001f, pixel), 0, 1);

        // Subtract the opposite edge of a finite strip: a 1 m stream cannot cover half a 14 m pixel.
        public static float BandCoverage(float inside, float halfWidth, float pixel) => Coverage(inside, pixel) - Coverage(inside - 2f * halfWidth, pixel);

        // Linear interpolation is essential to the colour agreement between near textures and distant mipmaps.
        static Vector3 Blend(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
    }
}

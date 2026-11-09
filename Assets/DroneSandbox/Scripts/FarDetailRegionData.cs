using System;
using UnityEngine;

namespace DroneSandbox
{
    // A 2048 m page has 4 m texels and block-aligned gutters at every stored mip; neighbours encode identical borders.
    public sealed class FarDetailRegionData
    {
        public const int Interior = 512, Gutter = 16, Resolution = Interior + 2 * Gutter, Mips = 3;
        public const float Metres = 2048f, Texel = 4f;
        public int x, z;
        public byte[][] mips;
        public double milliseconds;

        // Exact compressed payload, including gutters and all three mips, used by the hard residency cap.
        public static long Bytes
        {
            get { long bytes = 0; for (int m = 0; m < Mips; m++) { int n = Resolution >> m; bytes += (long)n * n / 2; } return bytes; }
        }

        // Downsample in linear light before BC1 encoding, never with Unity's main-thread texture compressor.
        public static FarDetailRegionData Build(FarDetailData plan, int x, int z)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = new FarDetailRegionData { x = x, z = z, mips = new byte[Mips][] };
            var pixels = plan.RegionPixels(x, z);
            int n = Resolution;
            for (int m = 0; m < Mips; m++)
            {
                result.mips[m] = Encode(pixels, n);
                if (m == Mips - 1) break;
                int next = n / 2;
                var smaller = new Color32[next * next];
                for (int j = 0; j < next; j++) for (int i = 0; i < next; i++)
                {
                    int k = j * 2 * n + i * 2;
                    smaller[j * next + i] = FarDetailPalette.Encode((FarDetailPalette.Decode(pixels[k]) + FarDetailPalette.Decode(pixels[k + 1])
                        + FarDetailPalette.Decode(pixels[k + n]) + FarDetailPalette.Decode(pixels[k + n + 1])) * .25f);
                }
                pixels = smaller; n = next;
            }
            result.milliseconds = timer.Elapsed.TotalMilliseconds;
            return result;
        }

        // A regional page's mip gets its own new array.
        static byte[] Encode(Color32[] pixels, int n)
        {
            var bytes = new byte[n * n / 2];
            Encode(pixels, n, bytes, new Color32[4]);
            return bytes;
        }

        // Deterministic opaque BC1 blocks use the farthest colour pair; no driver compression or packages are needed.
        // Near pages pass their own reused buffer and four-colour scratch.
        internal static void Encode(Color32[] pixels, int n, byte[] bytes, Color32[] colours)
        {
            int offset = 0;
            for (int z = 0; z < n; z += 4) for (int x = 0; x < n; x += 4)
            {
                Color32 lo = pixels[z * n + x], hi = lo;
                int far = -1;
                for (int k = 0; k < 16; k++)
                {
                    var c = pixels[(z + k / 4) * n + x + k % 4]; int d = Distance(c, lo);
                    if (d > far) { far = d; hi = c; }
                }
                far = -1;
                for (int k = 0; k < 16; k++)
                {
                    var c = pixels[(z + k / 4) * n + x + k % 4]; int d = Distance(c, hi);
                    if (d > far) { far = d; lo = c; }
                }
                int a = Pack(hi), b = Pack(lo);
                if (a < b) { int swap = a; a = b; b = swap; }
                if (a == b) { if (a < 65535) a++; else b--; }
                Palette(a, b, colours);
                uint indices = 0;
                for (int k = 0; k < 16; k++)
                {
                    var c = pixels[(z + k / 4) * n + x + k % 4]; int best = 0, error = int.MaxValue;
                    for (int q = 0; q < 4; q++) { int d = Distance(c, colours[q]); if (d < error) { error = d; best = q; } }
                    indices |= (uint)best << (k * 2);
                }
                bytes[offset++] = (byte)a; bytes[offset++] = (byte)(a >> 8); bytes[offset++] = (byte)b; bytes[offset++] = (byte)(b >> 8);
                for (int k = 0; k < 4; k++) bytes[offset++] = (byte)(indices >> (k * 8));
            }
        }

        // The audit decodes actual uploaded blocks, so comparison pictures include compression error.
        public static Color32[] Decode(byte[] bytes, int n)
        {
            var pixels = new Color32[n * n]; var colours = new Color32[4]; int offset = 0;
            for (int z = 0; z < n; z += 4) for (int x = 0; x < n; x += 4)
            {
                int a = bytes[offset] | bytes[offset + 1] << 8, b = bytes[offset + 2] | bytes[offset + 3] << 8;
                uint indices = (uint)(bytes[offset + 4] | bytes[offset + 5] << 8 | bytes[offset + 6] << 16 | bytes[offset + 7] << 24);
                Palette(a, b, colours); offset += 8;
                for (int k = 0; k < 16; k++) pixels[(z + k / 4) * n + x + k % 4] = colours[(indices >> (k * 2)) & 3];
            }
            return pixels;
        }

        // BC1's two 565 endpoints and two opaque interpolants are shared by encoding and the review decoder.
        static void Palette(int a, int b, Color32[] colours)
        {
            colours[0] = Unpack(a); colours[1] = Unpack(b);
            var p = colours[0]; var q = colours[1];
            colours[2] = new Color32((byte)((2 * p.r + q.r) / 3), (byte)((2 * p.g + q.g) / 3), (byte)((2 * p.b + q.b) / 3), 255);
            colours[3] = new Color32((byte)((p.r + 2 * q.r) / 3), (byte)((p.g + 2 * q.g) / 3), (byte)((p.b + 2 * q.b) / 3), 255);
        }

        // Quantize in stored sRGB bytes; the GPU's sRGB array decodes before filtering and lighting.
        static int Pack(Color32 c) => ((c.r * 31 + 127) / 255 << 11) | ((c.g * 63 + 127) / 255 << 5) | (c.b * 31 + 127) / 255;

        // Expand endpoints using the hardware bit-replication convention.
        static Color32 Unpack(int c)
        { int r = c >> 11, g = c >> 5 & 63, b = c & 31; return new Color32((byte)(r << 3 | r >> 2), (byte)(g << 2 | g >> 4), (byte)(b << 3 | b >> 2), 255); }

        // A modest green weight favours visible land-use edges over insignificant blue differences.
        static int Distance(Color32 a, Color32 b)
        { int r = a.r - b.r, g = a.g - b.g, blue = a.b - b.b; return r * r + 2 * g * g + blue * blue; }
    }

    // Regional pages use the exact same coverage painter and field plan as detail tiles and the world picture.
    public sealed partial class FarDetailData
    {
        // Cull vectors per block, keeping a long diagonal road from taxing every pixel in a 2 km page.
        public Color32[] RegionPixels(int x, int z)
        {
            int n = FarDetailRegionData.Resolution;
            var pixels = new Color32[n * n];
            for (int bz = 0; bz < n; bz += 32) for (int bx = 0; bx < n; bx += 32)
            {
                float x0 = x * 2048f + (bx - FarDetailRegionData.Gutter) * 4f, z0 = z * 2048f + (bz - FarDetailRegionData.Gutter) * 4f;
                var lanes = Nearby(roads, x0, z0, 128f); var streams = Nearby(rivers, x0, z0, 128f);
                for (int j = bz; j < Math.Min(bz + 32, n); j++) for (int i = bx; i < Math.Min(bx + 32, n); i++)
                {
                    float wx = x * 2048f + (i - FarDetailRegionData.Gutter + .5f) * 4f;
                    float wz = z * 2048f + (j - FarDetailRegionData.Gutter + .5f) * 4f;
                    pixels[j * n + i] = Paint(wx, wz, new Vector2(wx, wz), lanes, streams, 4f);
                }
            }
            return pixels;
        }
    }
}

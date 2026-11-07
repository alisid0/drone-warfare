using System.Threading.Tasks;
using UnityEngine;

namespace DroneSandbox
{
    // A 256 m near page: 0.5 m colour with 8 m gutters at every stored mip (BC1, like the regional pages), and the
    // 1 m surface control the detail layer reads, the sector splat's own resolution. One instance is reused for
    // every page, so a flight's pages allocate nothing on the worker.
    public sealed class FarDetailNearData
    {
        public const int Interior = 512, Gutter = 16, Resolution = Interior + 2 * Gutter, Mips = 3;
        public const int ControlGutter = 8, ControlResolution = 256 + 2 * ControlGutter;
        public const float Metres = 256f, Texel = .5f;
        public int x, z;
        public readonly byte[][] mips = new byte[Mips][];
        public readonly Color32[] controls = new Color32[ControlResolution * ControlResolution];
        public double milliseconds, paintMs;
        readonly Color32[][] levels = new Color32[Mips][];
        readonly Color32[] palette = new Color32[4];

        // Exact compressed colour payload per page, gutters and all three mips included.
        public static long ColourBytes
        {
            get { long bytes = 0; for (int m = 0; m < Mips; m++) { int n = Resolution >> m; bytes += (long)n * n / 2; } return bytes; }
        }

        // The uncompressed, point-loaded control map per page.
        public static long ControlBytes => (long)ControlResolution * ControlResolution * 4;

        // Everything one array slice holds, used by the hard residency figure.
        public static long Bytes => ColourBytes + ControlBytes;

        // Allocate the page's colour levels and compressed mips once.
        public FarDetailNearData()
        {
            for (int m = 0; m < Mips; m++)
            {
                int n = Resolution >> m;
                levels[m] = new Color32[n * n];
                mips[m] = new byte[n * n / 2];
            }
        }

        // Paint, then downsample in linear light and BC1-encode into this page's own arrays.
        public FarDetailNearData Build(FarDetailData plan, int x, int z)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            this.x = x; this.z = z;
            plan.NearPixels(x, z, levels[0], controls);
            paintMs = timer.Elapsed.TotalMilliseconds;
            int n = Resolution;
            for (int m = 0; m < Mips; m++)
            {
                FarDetailRegionData.Encode(levels[m], n, mips[m], palette);
                if (m == Mips - 1) break;
                Halve(levels[m], n, levels[m + 1]);
                n /= 2;
            }
            milliseconds = timer.Elapsed.TotalMilliseconds;
            return this;
        }

        // Box-filter four texels in linear light, as the GPU's sRGB mips do; the table avoids a Pow per channel.
        static void Halve(Color32[] source, int n, Color32[] target)
        {
            int next = n / 2;
            for (int j = 0; j < next; j++) for (int i = 0; i < next; i++)
                {
                    int k = j * 2 * n + i * 2;
                    Color32 a = source[k], b = source[k + 1], c = source[k + n], d = source[k + n + 1];
                    target[j * next + i] = FarDetailPalette.Encode(new Vector3(
                        (Linear[a.r] + Linear[b.r] + Linear[c.r] + Linear[d.r]) * .25f,
                        (Linear[a.g] + Linear[b.g] + Linear[c.g] + Linear[d.g]) * .25f,
                        (Linear[a.b] + Linear[b.b] + Linear[c.b] + Linear[d.b]) * .25f));
                }
        }

        static readonly float[] Linear = Table();

        // The sRGB byte to linear transfer, built once before any worker reads it.
        static float[] Table()
        {
            var table = new float[256];
            for (int i = 0; i < 256; i++) table[i] = FarDetailPalette.Linear(i / 255f);
            return table;
        }
    }

    // Near pages use the exact painter of the 2 m tiles, at a quarter of their footprint.
    public sealed partial class FarDetailData
    {
        static readonly ParallelOptions NearThreads = new ParallelOptions { MaxDegreeOfParallelism = 2 };

        // Cull vectors once per page; colour at 0.5 m with that footprint, control at 1 m, both over the gutters. The
        // colour rows run on two threads, as the world picture's do: a page is about three tiles' painting.
        public void NearPixels(int x, int z, Color32[] pixels, Color32[] controls)
        {
            float x0 = x * FarDetailNearData.Metres, z0 = z * FarDetailNearData.Metres, pad = FarDetailNearData.ControlGutter;
            var lanes = Nearby(roads, x0 - pad, z0 - pad, FarDetailNearData.Metres + 2f * pad);
            var streams = Nearby(rivers, x0 - pad, z0 - pad, FarDetailNearData.Metres + 2f * pad);
            int n = FarDetailNearData.Resolution;
            Parallel.For(0, n, NearThreads, j =>
            {
                float wz = z0 + (j - FarDetailNearData.Gutter + .5f) * FarDetailNearData.Texel;
                for (int i = 0; i < n; i++)
                {
                    float wx = x0 + (i - FarDetailNearData.Gutter + .5f) * FarDetailNearData.Texel;
                    pixels[j * n + i] = Paint(wx, wz, new Vector2(wx, wz), lanes, streams, FarDetailNearData.Texel);
                }
            });
            int m = FarDetailNearData.ControlResolution;
            for (int j = 0; j < m; j++) for (int i = 0; i < m; i++)
                {
                    float wx = x0 + i - pad + .5f, wz = z0 + j - pad + .5f;
                    controls[j * m + i] = Control(wx, wz, new Vector2(wx, wz), lanes, streams);
                }
        }
    }
}

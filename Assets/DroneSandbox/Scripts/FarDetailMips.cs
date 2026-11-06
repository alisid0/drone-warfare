using System;
using UnityEngine;

namespace DroneSandbox
{
    // Generate tile mipmaps on the data worker, leaving Apply with only a native upload on the main thread.
    public static class FarDetailMips
    {
        static readonly float[] linear = DecodeTable();

        // Preserve the base-level gutter, and area-filter odd dimensions so neither edge gets dropped.
        public static Color32[][] Build(Color32[] pixels, int size, Color32[][] reuse = null)
        {
            int levels = 1; for (int n = size; n > 1; n >>= 1) levels++;
            var result = reuse != null && reuse.Length == levels ? reuse : new Color32[levels][];
            result[0] = pixels;
            for (int m = 1, n = size; m < levels; m++)
            {
                int next = Math.Max(1, n >> 1);
                if (result[m] == null || result[m].Length != next * next) result[m] = new Color32[next * next];
                var source = result[m - 1]; var target = result[m];
                float scale = n / (float)next;
                for (int j = 0; j < next; j++) for (int i = 0; i < next; i++)
                {
                    float x0 = i * scale, x1 = (i + 1) * scale, z0 = j * scale, z1 = (j + 1) * scale;
                    Vector3 sum = Vector3.zero;
                    for (int z = (int)z0; z < Math.Min(n, (int)Math.Ceiling(z1)); z++)
                        for (int x = (int)x0; x < Math.Min(n, (int)Math.Ceiling(x1)); x++)
                        {
                            float weight = (Math.Min(x + 1, x1) - Math.Max(x, x0)) * (Math.Min(z + 1, z1) - Math.Max(z, z0));
                            var c = source[z * n + x];
                            sum += new Vector3(linear[c.r], linear[c.g], linear[c.b]) * weight;
                        }
                    target[j * next + i] = FarDetailPalette.Encode(sum / (scale * scale));
                }
                n = next;
            }
            return result;
        }

        // Byte decoding is exact and shared by all worker mips without repeated Pow calls.
        static float[] DecodeTable()
        {
            var result = new float[256];
            for (int i = 0; i < result.Length; i++) result[i] = FarDetailPalette.Linear(i / 255f);
            return result;
        }
    }
}

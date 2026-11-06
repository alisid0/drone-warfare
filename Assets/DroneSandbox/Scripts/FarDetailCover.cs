using System;
using UnityEngine;

namespace DroneSandbox
{
    // Immutable visual land use: workers share smooth boundaries without consulting the next live world.
    public sealed class FarDetailCover
    {
        readonly Cover[] grid;
        readonly byte[] interior;
        readonly int count, seed;

        // Cache uniform neighbourhoods once per snapshot; their 6 m warp cannot change membership at any sampled point.
        public FarDetailCover(int seed, Cover[] grid, int count)
        {
            this.seed = seed; this.grid = grid; this.count = count;
            interior = new byte[grid.Length];
            for (int j = 1; j < count - 2; j++) for (int i = 1; i < count - 2; i++)
            {
                Cover kind = grid[j * count + i]; bool same = true;
                for (int z = -1; z <= 2 && same; z++) for (int x = -1; x <= 2; x++)
                    if (grid[(j + z) * count + i + x] != kind) { same = false; break; }
                if (same) interior[j * count + i] = (byte)((int)kind + 1);
            }
        }

        // Four categorical corners retain memberships rather than interpolating enum values.
        public struct Sample
        {
            public Cover a, b, c, d;
            public float u, v;

            // Bilinear membership is continuous across the original 32 m sampling lattice.
            public float Weight(Cover kind) => FarDetailData.Mix(FarDetailData.Mix(a == kind ? 1f : 0f, b == kind ? 1f : 0f, u),
                FarDetailData.Mix(c == kind ? 1f : 0f, d == kind ? 1f : 0f, u), v);

            // Discrete fine-texture IDs follow the strongest interpolated membership, not the old nearest grid node.
            public Cover Dominant()
            {
                Cover best = a; float weight = Weight(a), next = Weight(b);
                if (next > weight) { best = b; weight = next; }
                next = Weight(c); if (next > weight) { best = c; weight = next; }
                next = Weight(d); if (next > weight) best = d;
                return best;
            }
        }

        // A 6 m displacement over 37/53 m wavelengths softens every land-use outline without a large domain warp.
        public Sample At(float x, float z, bool ripple = true)
        {
            if (ripple)
            {
                int cx = (int)Math.Floor((x + WorldPlan.Half) / 32f), cz = (int)Math.Floor((z + WorldPlan.Half) / 32f);
                if ((uint)cx < count && (uint)cz < count)
                {
                    int uniform = interior[cz * count + cx];
                    if (uniform != 0) { Cover kind = (Cover)(uniform - 1); return new Sample { a = kind, b = kind, c = kind, d = kind }; }
                }
                float dx = (Noise(x, z, 37f, 73) - .5f) * 12f;
                float dz = (Noise(x, z, 53f, 74) - .5f) * 12f;
                x += dx; z += dz;
            }
            float gx = FarDetailData.Clamp((x + WorldPlan.Half) / 32f, 0f, count - 1f);
            float gz = FarDetailData.Clamp((z + WorldPlan.Half) / 32f, 0f, count - 1f);
            int i = Math.Min((int)gx, count - 2), j = Math.Min((int)gz, count - 2), k = j * count + i;
            return new Sample { a = grid[k], b = grid[k + 1], c = grid[k + count], d = grid[k + count + 1], u = gx - i, v = gz - j };
        }

        // This is ForestPattern.Edge's exact formula and seeded noise, evaluated against the captured cover grid.
        public float Forest(float x, float z)
        {
            if (x < -WorldPlan.Half || z < -WorldPlan.Half || x > WorldPlan.Half || z > WorldPlan.Half) return 0f;
            var s = At(x, z, false);
            Cover nearest = s.v < .5f ? (s.u < .5f ? s.a : s.b) : (s.u < .5f ? s.c : s.d);
            if (nearest != Cover.Forest && nearest != Cover.Pasture && nearest != Cover.Farmland) return 0f;
            float mask = s.Weight(Cover.Forest);
            if (mask == 0f) return 0f;
            float ripple = (Noise(x, z, 7f, 71) - .5f) * .18f + (Noise(x, z, 21f, 72) - .5f) * .16f;
            return Smooth((mask + ripple - .28f) / .66f);
        }

        // A conservative margin keeps cultivated fragments inside the soft farm outline and outside the canopy fringe.
        public bool Farm(float x, float z) => At(x, z).Weight(Cover.Farmland) >= .68f && Forest(x, z) < .05f;

        // Uniform grid rectangles need no noise or per-metre survey; the padding includes the entire visual ripple.
        public bool Uniform(float x0, float z0, float x1, float z1, Cover kind)
        {
            int a = Math.Max(0, (int)Math.Floor((x0 + WorldPlan.Half - 8f) / 32f));
            int b = Math.Min(count - 1, (int)Math.Ceiling((x1 + WorldPlan.Half + 8f) / 32f));
            int c = Math.Max(0, (int)Math.Floor((z0 + WorldPlan.Half - 8f) / 32f));
            int d = Math.Min(count - 1, (int)Math.Ceiling((z1 + WorldPlan.Half + 8f) / 32f));
            for (int j = c; j <= d; j++) for (int i = a; i <= b; i++) if (grid[j * count + i] != kind) return false;
            return true;
        }

        // Snapshot-seeded equivalent of ForestPattern.Noise, with the same 32-bit overflow and channel salts.
        public float Noise(float x, float z, float step, int salt)
        {
            x /= step; z /= step;
            int i = (int)Math.Floor(x), j = (int)Math.Floor(z);
            float u = Smooth(x - i), v = Smooth(z - j);
            return FarDetailData.Mix(FarDetailData.Mix(Unit(i, j, salt), Unit(i + 1, j, salt), u),
                FarDetailData.Mix(Unit(i, j + 1, salt), Unit(i + 1, j + 1, salt), u), v);
        }

        // Keep ForestPattern's hash verbatim in arithmetic so old workers cannot pick up a new WorldPlan.Seed.
        float Unit(int x, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ z * 19349663 ^ seed * 83492791 ^ salt * 1640531513);
                h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
                return (h & 65535) / 65536f;
            }
        }

        // Managed smoothstep matches Mathf.SmoothStep without a native engine dependency.
        static float Smooth(float t) { t = FarDetailData.Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }
    }
}

using UnityEngine;

namespace DroneSandbox
{
    // One world-space woodland recipe shared by trees, canopy geometry and the offline review harness.
    public static class ForestPattern
    {
        public const float TreeStep = 12f;

        // A stand's shared age, species and tint keep neighbouring crowns related across streaming boundaries.
        public struct Stand
        {
            public int species, shade;
            public float height, width, cover;
        }

        // Stable integer noise avoids changing the forest when another cell is generated first.
        public static int Hash(int x, int z, int salt = 0)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ z * 19349663 ^ WorldPlan.Seed * 83492791 ^ salt * 1640531513);
                h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
                return (int)(h & 0x7fffffff);
            }
        }

        // Independent channels of the same seeded recipe, in the closed unit interval.
        public static float Unit(int x, int z, int salt) => (Hash(x, z, salt) & 65535) / 65535f;

        // Smooth value noise supplies broad height waves without depending on native Unity noise in tests.
        public static float Noise(float x, float z, float step, int salt)
        {
            x /= step; z /= step;
            int i = Mathf.FloorToInt(x), j = Mathf.FloorToInt(z);
            float u = Mathf.SmoothStep(0f, 1f, x - i), v = Mathf.SmoothStep(0f, 1f, z - j);
            return Mathf.Lerp(Mathf.Lerp(Unit(i, j, salt), Unit(i + 1, j, salt), u),
                Mathf.Lerp(Unit(i, j + 1, salt), Unit(i + 1, j + 1, salt), u), v);
        }

        // Irregular glades occupy about eight percent of an unbounded forest, with soft young margins.
        public static float Glade(float x, float z)
        {
            const float step = 192f;
            int i = Mathf.FloorToInt(x / step), j = Mathf.FloorToInt(z / step);
            float distance = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int a = i + dx, b = j + dz;
                    float cx = (a + 0.2f + 0.6f * Unit(a, b, 61)) * step;
                    float cz = (b + 0.2f + 0.6f * Unit(a, b, 62)) * step;
                    float rx = 26f + 10f * Unit(a, b, 63), rz = 25f + 11f * Unit(a, b, 64);
                    float u = (x - cx) / rx, v = (z - cz) / rz;
                    distance = Mathf.Min(distance, (Mathf.Sqrt(u * u + v * v) - 1f) * 30f);
                }
            distance += (Noise(x, z, 18f, 65) - 0.5f) * 13f;
            return Mathf.SmoothStep(0f, 1f, distance / 14f);
        }

        // Distance to neighbouring empty cover cells feathers inward over 15-35 m, without widening the forest.
        public static float Edge(float x, float z)
        {
            if (!WorldPlan.Inside(x, z) || WorldPlan.CoverAt(x, z) != CoverType) return 0f;
            float width = 15f + 20f * Noise(x, z, 96f, 71);
            float step = WorldPlan.HeightStep;
            float cx = Mathf.Floor((x + WorldPlan.Half) / step + 0.5f) * step - WorldPlan.Half;
            float cz = Mathf.Floor((z + WorldPlan.Half) / step + 0.5f) * step - WorldPlan.Half;
            float distance = width;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float ax = cx + i * step, az = cz + j * step;
                    if (WorldPlan.Inside(ax, az) && WorldPlan.CoverAt(ax, az) == CoverType) continue;
                    float dx = Mathf.Max(0f, Mathf.Abs(x - ax) - step * 0.5f);
                    float dz = Mathf.Max(0f, Mathf.Abs(z - az) - step * 0.5f);
                    distance = Mathf.Min(distance, Mathf.Sqrt(dx * dx + dz * dz));
                }
            return Mathf.SmoothStep(0f, 1f, distance / width);
        }

        const Cover CoverType = DroneSandbox.Cover.Forest;

        // Jittered 30 m stands give 15-45 m groups; blended ages and crown bumps avoid flat plateaus.
        public static Stand At(float x, float z)
        {
            int i = Mathf.FloorToInt(x / 30f), j = Mathf.FloorToInt(z / 30f), stand = 0;
            float best = float.MaxValue, age = 0f, weight = 0f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int a = i + dx, b = j + dz;
                    float u = (a + 0.2f + 0.6f * Unit(a, b, 11)) * 30f - x;
                    float v = (b + 0.2f + 0.6f * Unit(a, b, 12)) * 30f - z;
                    float d = u * u + v * v;
                    if (d < best) { best = d; stand = Hash(a, b, 13); }
                    float w = Mathf.Max(0f, 1f - d / 1800f); w *= w;
                    age += Unit(a, b, 14) * w; weight += w;
                }
            float edge = Edge(x, z), glade = Glade(x, z);
            float conifer = Mathf.Lerp(0.15f, 0.85f, Mathf.SmoothStep(0f, 1f, (WorldPlan.Height(x, z) - 120f) / 220f));
            float pick = (stand & 65535) / 65535f;
            int species = pick < conifer ? (pick < conifer * 0.55f ? 2 : 3) : (pick < conifer + (1f - conifer) * 0.25f ? 1 : 0);
            if (edge < 0.7f && ((stand >> 16) & 3) != 0) species = 1;
            float height = 8f + 16f * Mathf.Clamp01(age / Mathf.Max(0.001f, weight) * 0.8f + Noise(x, z, 110f, 18) * 0.2f);
            height = Mathf.Lerp(8f, height, Mathf.Min(edge, glade));
            return new Stand { species = species, shade = (stand >> 20) % 3, height = height,
                width = 0.85f + 0.3f * ((stand >> 8) & 255) / 255f, cover = edge * glade };
        }

        // Both tiers use the same ground anchor and height; the final crown bumps live on the HLOD surface.
        public static float Canopy(Stand stand, float x, float z) =>
            stand.cover * Mathf.Clamp(stand.height + (Noise(x, z, 13f, 31) - 0.5f) * 5f, 8f, 24f);

        // One tree candidate per global grid square, independent of cell dimensions and traversal order.
        public static bool Tree(int i, int j, out Vector2 world, out Stand stand)
        {
            world = new Vector2((i + 0.1f + Unit(i, j, 41) * 0.8f) * TreeStep, (j + 0.1f + Unit(i, j, 42) * 0.8f) * TreeStep);
            stand = At(world.x, world.y);
            return WorldPlan.Inside(world.x, world.y) && WorldPlan.CoverAt(world.x, world.y) == CoverType && Unit(i, j, 43) < stand.cover;
        }

        // A subtle stand-wide tint is shared by photo materials and the derived canopy palette.
        public static float Tint(int shade) => 0.96f + shade * 0.04f;
    }
}

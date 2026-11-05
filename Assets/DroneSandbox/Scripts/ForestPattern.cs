using UnityEngine;

namespace DroneSandbox
{
    // One world-space woodland recipe shared by trees, canopy geometry and the offline review harness.
    public static class ForestPattern
    {
        public const float TreeStep = 10f;

        // A stand's shared age, species and tint keep neighbouring crowns related across streaming boundaries.
        public struct Stand
        {
            public int species, shade;
            public float height, width, radius, cover;
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

        // Independent channels of the same seeded recipe; the upper cell boundary is excluded.
        public static float Unit(int x, int z, int salt) => (Hash(x, z, salt) & 65535) / 65536f;

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
            float warpX = (Noise(x, z, 29f, 67) - 0.5f) * 24f;
            float warpZ = (Noise(x, z, 31f, 68) - 0.5f) * 24f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int a = i + dx, b = j + dz;
                    if (Unit(a, b, 60) > 0.9f) continue;
                    float cx = (a + Unit(a, b, 61)) * step;
                    float cz = (b + Unit(a, b, 62)) * step;
                    float rx = 18f + 32f * Unit(a, b, 63), rz = 14f + 38f * Unit(a, b, 64);
                    float angle = Unit(a, b, 66) * Mathf.PI, ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                    float wx = x - cx + warpX;
                    float wz = z - cz + warpZ;
                    float u = (wx * ca - wz * sa) / rx, v = (wx * sa + wz * ca) / rz;
                    distance = Mathf.Min(distance, (Mathf.Sqrt(u * u + v * v) - 1f) * 30f);
                }
            distance += (Noise(x, z, 18f, 65) - 0.5f) * 13f;
            return Mathf.SmoothStep(0f, 1f, distance / 22f);
        }

        // Interpolate cover before adding metre-scale shoreline noise; keep scrub out of water and built land.
        public static float Edge(float x, float z)
        {
            if (!WorldPlan.Inside(x, z)) return 0f;
            Cover cover = WorldPlan.CoverAt(x, z);
            if (cover != Cover.Forest && cover != Cover.Pasture && cover != Cover.Farmland) return 0f;
            float step = WorldPlan.HeightStep;
            float gx = (x + WorldPlan.Half) / step, gz = (z + WorldPlan.Half) / step;
            int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gz);
            float cx = i * step - WorldPlan.Half, cz = j * step - WorldPlan.Half;
            float a = Wood(cx, cz), b = Wood(cx + step, cz), c = Wood(cx, cz + step), d = Wood(cx + step, cz + step);
            float mask = Mathf.Lerp(Mathf.Lerp(a, b, gx - i), Mathf.Lerp(c, d, gx - i), gz - j);
            float ripple = (Noise(x, z, 7f, 71) - 0.5f) * 0.18f + (Noise(x, z, 21f, 72) - 0.5f) * 0.16f;
            return Mathf.SmoothStep(0f, 1f, (mask + ripple - 0.28f) / 0.66f);
        }

        // Cover is categorical in WorldPlan; interpolation belongs only to this visual forest mask.
        static float Wood(float x, float z) => WorldPlan.Inside(x, z) && WorldPlan.CoverAt(x, z) == Cover.Forest ? 1f : 0f;

        // Warped 240 m districts keep species in 100-400 m patches while smaller stands vary in age.
        public static Stand At(float x, float z)
        {
            float sx = x + (Noise(x, z, 190f, 16) - 0.5f) * 130f;
            float sz = z + (Noise(x, z, 170f, 17) - 0.5f) * 130f;
            int i = Mathf.FloorToInt(sx / 240f), j = Mathf.FloorToInt(sz / 240f), patch = 0;
            float best = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int a = i + dx, b = j + dz;
                    float u = (a + Unit(a, b, 11)) * 240f - sx;
                    float v = (b + Unit(a, b, 12)) * 240f - sz;
                    float distance = u * u + v * v;
                    if (distance < best) { best = distance; patch = Hash(a, b, 13); }
                }
            float conifer = Mathf.Lerp(0.18f, 0.72f, Mathf.SmoothStep(0f, 1f, (WorldPlan.Height(x, z) - 120f) / 220f));
            float pick = (patch & 65535) / 65536f;
            int species = pick < conifer ? (pick < conifer * 0.6f ? 2 : 3) : (pick < conifer + (1f - conifer) * 0.28f ? 1 : 0);
            float ax = x + (Noise(x, z, 67f, 19) - 0.5f) * 26f;
            float az = z + (Noise(x, z, 71f, 20) - 0.5f) * 26f;
            float age = Noise(ax, az, 30f, 14);
            age = Mathf.Clamp01((age - 0.15f) / 0.7f);
            float edge = Edge(x, z), glade = Glade(x, z), margin = Mathf.Min(edge, glade);
            float height = Mathf.Lerp(8f, 8f + 16f * age, margin);
            return new Stand { species = species, shade = (patch >> 20) % 3, height = height,
                width = 1f, radius = Mathf.Lerp(2f, 3f + 4f * age, margin), cover = edge * glade };
        }

        // The surface describes connected stands, not isolated bumps on a mesh sampling lattice.
        public static float Canopy(Stand stand, float x, float z) =>
            stand.cover * stand.height * (0.72f + 0.16f * Noise(x, z, 27f, 31));

        // Random candidates fill hashing cells; a priority exclusion gives a reproducible Poisson hard core.
        static Vector2 Candidate(int i, int j) =>
            new Vector2((i + Unit(i, j, 41)) * TreeStep, (j + Unit(i, j, 42)) * TreeStep);

        // Spacing, clustered density, individual age and yaw are shared by cards, meshes and the canopy bake.
        public static bool Tree(int i, int j, out Vector2 world, out Stand stand)
        {
            world = Candidate(i, j); stand = default;
            int priority = Hash(i, j, 46);
            float spacing = 4f + 2f * Noise(world.x, world.y, 35f, 47);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int other = Hash(i + dx, j + dz, 46);
                    if (other > priority || (other == priority && (dz > 0 || (dz == 0 && dx > 0)))) continue;
                    if ((Candidate(i + dx, j + dz) - world).sqrMagnitude < spacing * spacing) return false;
                }
            stand = At(world.x, world.y);
            float cluster = Noise(world.x, world.y, 24f, 48);
            if (Unit(i, j, 43) >= stand.cover * Mathf.Lerp(0.58f, 1f, cluster)) return false;
            float individual = Unit(i, j, 49);
            stand.height = Mathf.Clamp(stand.height + (individual - 0.5f) * 7f, 8f, 24f);
            stand.radius = Mathf.Clamp(stand.radius * (0.7f + individual * 0.6f), 2f, 7f);
            // Isolated old trees and groups of saplings interrupt the stand's average silhouette.
            if (Unit(i, j, 50) > 0.97f && stand.cover > 0.95f) { stand.radius = 7f; stand.height = 23f; }
            stand.width = stand.radius / (stand.height * ForestPalette.RadiusPerHeight[stand.species]);
            return true;
        }
        // A subtle stand-wide tint is shared by photo materials and the derived canopy palette.
        public static float Tint(int shade) => 0.96f + shade * 0.04f;
    }
}

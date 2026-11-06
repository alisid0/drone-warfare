using UnityEngine;

namespace DroneSandbox
{
    // One world-space woodland recipe shared by trees, canopy geometry and the offline review harness.
    public static class ForestPattern
    {
        public const float TreeStep = 5f;

        // A stand's shared age, species and tint keep neighbouring crowns related across streaming boundaries.
        public struct Stand
        {
            public int species, shade, neighbour;
            public float height, width, radius, cover, blend;
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
            return Mathf.SmoothStep(0f, 1f, distance / 30f);
        }

        // Interpolate cover before adding metre-scale shoreline noise; keep scrub out of water and built land.
        public static float Edge(float x, float z)
        {
            if (!WorldPlan.Ready || !WorldPlan.Inside(x, z)) return 0f;
            if (WorldPlan.FeatureReserved(new Vector2(x, z), 8f)) return 0f;
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
        public static Stand At(float x, float z) => At(x, z, Edge(x, z));

        // Sector reserves supply their own boundary, but share species, ages and glades with the world.
        public static Stand At(float x, float z, float edge)
        {
            if (edge <= 0f) return new Stand { height = 5f, radius = 2f, width = 1f };
            float sx = x + (Noise(x, z, 190f, 16) - 0.5f) * 130f;
            float sz = z + (Noise(x, z, 170f, 17) - 0.5f) * 130f;
            int i = Mathf.FloorToInt(sx / 240f), j = Mathf.FloorToInt(sz / 240f), patch = 0, nextPatch = 0;
            float best = float.MaxValue, second = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int a = i + dx, b = j + dz;
                    float u = (a + Unit(a, b, 11)) * 240f - sx;
                    float v = (b + Unit(a, b, 12)) * 240f - sz;
                    float distance = u * u + v * v;
                    if (distance < best)
                    {
                        second = best; nextPatch = patch; best = distance; patch = Hash(a, b, 13);
                    }
                    else if (distance < second) { second = distance; nextPatch = Hash(a, b, 13); }
                }
            float elevation = WorldPlan.Ready ? WorldPlan.Height(x, z) : 120f;
            float conifer = Mathf.Lerp(0.18f, 0.72f, Mathf.SmoothStep(0f, 1f, (elevation - 120f) / 220f));
            int species = Species(patch, conifer), neighbour = Species(nextPatch, conifer);
            float blend = 0.5f * (1f - Mathf.SmoothStep(0f, 1f, (Mathf.Sqrt(second) - Mathf.Sqrt(best)) / 50f));
            float ax = x + (Noise(x, z, 67f, 19) - 0.5f) * 26f;
            float az = z + (Noise(x, z, 71f, 20) - 0.5f) * 26f;
            float age = Noise(ax, az, 30f, 14);
            age = Mathf.Clamp01((age - 0.15f) / 0.7f);
            float glade = Glade(x, z), margin = Mathf.Min(edge, glade);
            float height = Mathf.Lerp(5f, 8f + 16f * age, margin);
            return new Stand { species = species, neighbour = neighbour, blend = blend, shade = (patch >> 20) % 3, height = height,
                width = 1f, radius = Mathf.Lerp(2f, 3.1f + 1.2f * age, margin), cover = edge * glade };
        }

        // Keep the stand surface below the photo crown caps (0.72 height for broadleaf), so arriving cards retain their crowns.
        public static float Canopy(Stand stand, float x, float z) => stand.cover * Mathf.Max(2f,
            stand.height * 0.55f + (Noise(x, z, 31f, 31) - 0.5f) * 5f
            + (Noise(x + 17f, z - 9f, 53f, 32) - 0.5f) * 2f
            + (Noise(x, z, 13f, 33) - 0.5f) * 7f);

        // The same species districts drive a soft canopy mixture and individual trees along the ecotone.
        static int Species(int patch, float conifer)
        {
            float pick = (patch & 65535) / 65536f;
            return pick < conifer ? (pick < conifer * 0.6f ? 2 : 3) : (pick < conifer + (1f - conifer) * 0.28f ? 1 : 0);
        }

        // Random candidates fill hashing cells; a priority exclusion gives a reproducible Poisson hard core.
        public static Vector2 Candidate(int i, int j) =>
            new Vector2((i + Unit(i, j, 41)) * TreeStep, (j + Unit(i, j, 42)) * TreeStep);

        // Spacing, clustered density, individual age and yaw are shared by cards, meshes and the canopy bake.
        public static bool Tree(int i, int j, out Vector2 world, out Stand stand)
            => Tree(i, j, -1f, out world, out stand);

        // A five-metre hashing cell is a candidate, never a planted row; priority thinning removes close pairs.
        public static bool Tree(int i, int j, float edge, out Vector2 world, out Stand stand)
        {
            world = Candidate(i, j); stand = default;
            int priority = Hash(i, j, 46);
            const float spacing = 2.4f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int other = Hash(i + dx, j + dz, 46);
                    if (other > priority || (other == priority && (dz > 0 || (dz == 0 && dx > 0)))) continue;
                    if ((Candidate(i + dx, j + dz) - world).sqrMagnitude < spacing * spacing) return false;
                }
            stand = At(world.x, world.y, edge < 0f ? Edge(world.x, world.y) : edge);
            float cluster = Noise(world.x, world.y, 24f, 48);
            if (Unit(i, j, 43) >= stand.cover * Mathf.Lerp(0.88f, 1f, cluster)) return false;
            float individual = Unit(i, j, 49);
            if (Unit(i, j, 47) < stand.blend) stand.species = stand.neighbour;
            stand.neighbour = stand.species; stand.blend = 0f;
            stand.height = Mathf.Clamp(stand.height + (individual - 0.5f) * 7f, 5f, 24f);
            stand.radius = Mathf.Clamp(stand.radius * (0.85f + individual * 0.3f), 2f, 4.6f);
            // Isolated old trees and groups of saplings interrupt the stand's average silhouette.
            if (Unit(i, j, 50) > 0.97f && stand.cover > 0.95f) { stand.radius = 4.6f; stand.height = 23f; }
            stand.width = stand.radius / (stand.height * ForestPalette.RadiusPerHeight[stand.species]);
            return true;
        }
        // A subtle stand-wide tint is shared by photo materials and the derived canopy palette.
        public static float Tint(int shade) => 0.96f + shade * 0.04f;
    }
}

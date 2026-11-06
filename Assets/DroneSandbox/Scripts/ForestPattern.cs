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
            public float height, width, radius, cover, blend, age, wood;
            public Vector2 lean;
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

        // Warped ellipses retain elongated openings; distance is in metres so every rim has a physical width.
        public static float Glade(float x, float z)
        {
            const float step = 192f;
            int i = Mathf.FloorToInt(x / step), j = Mathf.FloorToInt(z / step);
            float margin = 1f;
            float warpX = (Noise(x, z, 43f, 67) - 0.5f) * 10f;
            float warpZ = (Noise(x, z, 47f, 68) - 0.5f) * 10f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int a = i + dx, b = j + dz;
                    if (Unit(a, b, 60) > 0.9f) continue;
                    float cx = (a + Unit(a, b, 61)) * step;
                    float cz = (b + Unit(a, b, 62)) * step;
                    float radius = 24f + 18f * Unit(a, b, 63);
                    float stretch = Mathf.Sqrt(1.7f + Unit(a, b, 64));
                    float rx = radius * stretch, rz = radius / stretch;
                    float angle = Unit(a, b, 66) * Mathf.PI, ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                    float wx = x - cx + warpX;
                    float wz = z - cz + warpZ;
                    float u = wx * ca - wz * sa, v = wx * sa + wz * ca;
                    float distance = EllipseDistance(u, v, rx, rz);
                    margin = Mathf.Min(margin, Mathf.Clamp01(distance / (10f + 15f * Unit(a, b, 69))));
                }
            return margin;
        }

        // First-order ellipse distance is exact at the rim and along either axis, unlike a fixed radial multiplier.
        public static float EllipseDistance(float x, float z, float rx, float rz)
        {
            float u = x / rx, v = z / rz, length = Mathf.Sqrt(u * u + v * v);
            float gradient = Mathf.Sqrt(u * u / (rx * rx) + v * v / (rz * rz));
            return gradient > 0.0001f ? length * (length - 1f) / gradient : -Mathf.Min(rx, rz);
        }

        // A convex descent reaches ground tangentially instead of retaining an upright five-metre canopy lip.
        public static float Rim(float margin) => Mathf.Clamp01(margin) * Mathf.Clamp01(margin);

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
            return Mathf.Clamp01((mask + ripple - 0.28f) / 0.66f);
        }

        // Cover is categorical in WorldPlan; interpolation belongs only to this visual forest mask.
        static float Wood(float x, float z) => WorldPlan.Inside(x, z) && WorldPlan.CoverAt(x, z) == Cover.Forest ? 1f : 0f;

        // Warped species districts have soft ecotones; the age lattice below makes correlated 40-120 m stands.
        public static Stand At(float x, float z) => At(x, z, Edge(x, z));

        // Sector reserves supply their own boundary, but share species, ages and glades with the world.
        public static Stand At(float x, float z, float edge)
        {
            if (edge <= 0f) return new Stand { radius = 2f, width = 1f };
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
            float age = Mathf.Clamp01((Noise(ax, az, 64f, 14) - 0.22f) / 0.56f);
            float glade = Glade(x, z), margin = Mathf.Min(edge, glade);
            float height = (12f + 12f * age) * Rim(margin);
            float tint = Noise(x, z, 80f, 83) * 0.7f + Noise(x, z, 12f, 84) * 0.3f;
            return new Stand { species = species, neighbour = neighbour, blend = blend,
                shade = Mathf.Clamp(Mathf.FloorToInt(tint * 3f), 0, 2), height = height, age = age, wood = edge,
                width = 1f, radius = Mathf.Lerp(2f, 2.8f + 1.8f * age, margin), cover = edge * glade };
        }

        // Inside the wood mask a glade floor keeps this low apron, so coarse terrain cannot show through the canopy.
        public const float Understorey = 2.5f;

        // Broad relief suits every level; only 4 m recipe surfaces sample crown bumps finely enough not to alias.
        // Relief eases onto the apron with zero slope; fields, tracks and water (wood zero) still meet the real floor.
        public static float Canopy(Stand stand, float x, float z, bool crowns = false)
        {
            float relief = stand.height * 0.70f;
            if (crowns)
            {
                float u = x * 0.8f - z * 0.6f, v = x * 0.6f + z * 0.8f;
                relief += ((Noise(u, v, 9.3f, 123) - 0.5f) * 0.62f + (Noise(z, -x, 14.9f, 124) - 0.5f) * 0.38f)
                    * (3.4f + 2.4f * stand.age) * Rim(stand.cover);
            }
            relief = Mathf.Max(0f, relief);
            float apron = Understorey * stand.wood * stand.wood;
            if (relief >= apron * 2f) return relief;
            float t = 1f - relief / (apron * 2f);
            return relief + apron * t * t;
        }

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
            // A few young trees survive in natural openings; protected roads, fields and reserves still have edge zero.
            float density = Mathf.Max(stand.cover, stand.wood * 0.025f);
            if (Unit(i, j, 43) >= density * Mathf.Lerp(0.88f, 1f, cluster)) return false;
            float individual = Unit(i, j, 49);
            if (Unit(i, j, 47) < stand.blend) stand.species = stand.neighbour;
            stand.neighbour = stand.species; stand.blend = 0f;
            stand.height = Mathf.Clamp(stand.height + (individual - 0.5f) * 5f * stand.cover, 2f + individual * 2f, 26f);
            // The canopy fade uses a conservative two-metre crown, including the youngest edge trees.
            stand.radius = Mathf.Max(2f, stand.radius * (0.78f + individual * 0.44f));
            // Isolated old trees and groups of saplings interrupt the stand's average silhouette.
            if (Unit(i, j, 50) > 0.97f && stand.cover > 0.95f) { stand.radius = 4.6f; stand.height = 23f; }
            stand.width = stand.radius / (stand.height * ForestPalette.RadiusPerHeight[stand.species]);
            stand.lean = SectorForest.EdgeLean(world, stand.cover, edge >= 0f);
            return true;
        }

        // Tilt in world space before applying yaw, so all render tiers lean toward the same opening.
        public static Quaternion Rotation(int i, int j, Stand stand) =>
            Quaternion.Euler(stand.lean.y * Mathf.Rad2Deg, 0f, -stand.lean.x * Mathf.Rad2Deg)
                * Quaternion.Euler(0f, Unit(i, j, 44) * 360f, 0f);

        // A subtle stand-wide tint is shared by photo materials and the derived canopy palette.
        public static float Tint(int shade) => 0.90f + shade * 0.09f;
    }
}

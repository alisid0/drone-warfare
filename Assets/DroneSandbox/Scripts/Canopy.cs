using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // How high the tree tops are across the map, on a 4 m grid, measured from the trees themselves once the world
    // is built. Drones cruise at least a little above it (Drone.CruiseHeight), so they hop over woods instead of
    // flying through the crowns: they stay in sight from the RTS camera, and their cameras see the land.
    public static class Canopy
    {
        const float Cell = 4f;
        static float[,] top;
        // Each tree's crown as an ellipsoid (a broadleaf) or a cone (a spruce or pine: widest at its lowest branches,
        // coming to a point), for seeing past the trees (Blocks), found by the 8 m cell its middle is in.
        struct Crown
        {
            public Vector3 centre;
            public float radius, halfHeight;
            public bool cone;
        }
        const float CrownCell = 8f;
        static readonly Dictionary<Vector2Int, List<Crown>> crowns = new Dictionary<Vector2Int, List<Crown>>();
        static int n;
        static float half;

        public static void Build(Transform world)
        {
            half = Ground.Size / 2f;
            n = Mathf.CeilToInt(Ground.Size / Cell);
            top = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++) top[x, z] = float.MinValue;
            crowns.Clear();
            // Logical woods have no LODGroup: retain the existing crown tests and drone height grid.
            foreach (var stem in SectorForest.Stems)
            {
                float radius = stem.stand.radius, height = stem.stand.height;
                Vector3 p = stem.position;
                float roof = p.y + height;
                int x0 = Index(p.x - radius * 0.8f), x1 = Index(p.x + radius * 0.8f);
                int z0 = Index(p.z - radius * 0.8f), z1 = Index(p.z + radius * 0.8f);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++) top[x, z] = Mathf.Max(top[x, z], roof);
                bool conifer = stem.stand.species >= 2;
                var crown = new Crown { centre = p + Vector3.up * height * 0.675f,
                    radius = radius * (conifer ? 0.75f : 0.65f), halfHeight = height * 0.325f, cone = conifer };
                var key = CrownKey(crown.centre);
                if (!crowns.TryGetValue(key, out var list)) crowns[key] = list = new List<Crown>();
                list.Add(crown);
            }
            foreach (var group in world.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                if (lods.Length < 3) continue; // trees have three levels; rocks and scanned plants fewer
                var bounds = new Bounds(group.transform.position, Vector3.zero);
                bool any = false;
                foreach (var r in lods[0].renderers)
                {
                    if (r == null) continue;
                    if (!any) bounds = r.bounds;
                    else bounds.Encapsulate(r.bounds);
                    any = true;
                }
                if (!any || bounds.size.y < 4f) continue;
                // The crown covers most of the bounds across; the trunk doesn't matter.
                float reach = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.8f;
                int x0 = Index(bounds.center.x - reach), x1 = Index(bounds.center.x + reach);
                int z0 = Index(bounds.center.z - reach), z1 = Index(bounds.center.z + reach);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        if (bounds.max.y > top[x, z]) top[x, z] = bounds.max.y;
                // The crown: the top two thirds of the tree, two thirds as wide as its branches reach (they're not
                // solid to the tips: the autotest's occlusion check calibrates this against the picture).
                float bottom = bounds.min.y + bounds.size.y * 0.35f;
                bool conifer = group.name.StartsWith("Scenery_Spruce") || group.name.StartsWith("Scenery_Pine");
                var crown = new Crown
                {
                    centre = new Vector3(bounds.center.x, (bottom + bounds.max.y) / 2f, bounds.center.z),
                    radius = Mathf.Min((bounds.extents.x + bounds.extents.z) / 2f * (conifer ? 0.75f : 0.65f), CrownCell),
                    halfHeight = (bounds.max.y - bottom) / 2f,
                    cone = conifer,
                };
                var key = CrownKey(crown.centre);
                if (!crowns.TryGetValue(key, out var list)) crowns[key] = list = new List<Crown>();
                list.Add(crown);
            }
        }

        static int Index(float worldCoord) => Mathf.Clamp(Mathf.FloorToInt((worldCoord + half) / Cell), 0, n - 1);

        // Whether tree crowns stand between two points: the line, sampled every metre, has to run through 2 m of
        // crown. The last couple of metres are left out, so a bird perched on a tree is still in sight. (The 4 m
        // height grid is no good for this: a cell knows only its tallest tree, and fills the corners of the square
        // round each crown, so lines passing well clear of the trees counted as blocked.)
        public static bool Blocks(Vector3 from, Vector3 to)
        {
            int steps = Mathf.CeilToInt(Vector3.Distance(from, to)), run = 0;
            for (int i = 1; i < steps - 2; i++)
            {
                run = InCrown(Vector3.Lerp(from, to, i / (float)steps)) ? run + 1 : 0;
                if (run >= 2) return true;
            }
            return false;
        }

        // For the tests: where along the line, and in which crown, Blocks found its 2 m of foliage ("" if none).
        public static string Explain(Vector3 from, Vector3 to)
        {
            int steps = Mathf.CeilToInt(Vector3.Distance(from, to)), run = 0;
            for (int i = 1; i < steps - 2; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)steps);
                var c = CrownAt(p);
                run = c.HasValue ? run + 1 : 0;
                if (run >= 2)
                    return $"{i} m of {steps} from the eye, crown at {c.Value.centre.y - Ground.HeightAt(c.Value.centre):F0} m up, r {c.Value.radius:F1}, " +
                           $"half-height {c.Value.halfHeight:F1}, {Vector2.Distance(new Vector2(p.x, p.z), new Vector2(c.Value.centre.x, c.Value.centre.z)):F1} m off its trunk";
            }
            return "";
        }

        static Vector2Int CrownKey(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CrownCell), Mathf.FloorToInt(p.z / CrownCell));

        static bool InCrown(Vector3 p) => CrownAt(p).HasValue;

        static Crown? CrownAt(Vector3 p)
        {
            var key = CrownKey(p);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!crowns.TryGetValue(new Vector2Int(key.x + dx, key.y + dz), out var list)) continue;
                    foreach (var c in list)
                    {
                        float across = ((p.x - c.centre.x) * (p.x - c.centre.x) + (p.z - c.centre.z) * (p.z - c.centre.z)) / (c.radius * c.radius);
                        float up = (p.y - c.centre.y) / c.halfHeight; // -1 at the crown's foot, 1 at its top
                        if (c.cone)
                        {
                            float width = (1f - up) / 2f; // the cone's radius here, as a share of its foot's
                            if (up > -1f && up < 1f && across < width * width) return c;
                        }
                        else if (across + up * up < 1f) return c;
                    }
                }
            return null;
        }

        // The highest tree top within `radius` of p (minus infinity where there are no trees).
        public static float TopNear(Vector3 p, float radius = 4f)
        {
            if (top == null) return float.MinValue;
            float best = float.MinValue;
            int x0 = Index(p.x - radius), x1 = Index(p.x + radius), z0 = Index(p.z - radius), z1 = Index(p.z + radius);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (top[x, z] > best) best = top[x, z];
            return best;
        }
    }
}

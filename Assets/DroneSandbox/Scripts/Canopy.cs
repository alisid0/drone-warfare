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
        static int n;
        static float half;

        public static void Build(Transform world)
        {
            half = Ground.Size / 2f;
            n = Mathf.CeilToInt(Ground.Size / Cell);
            top = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++) top[x, z] = float.MinValue;
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
            }
        }

        static int Index(float worldCoord) => Mathf.Clamp(Mathf.FloorToInt((worldCoord + half) / Cell), 0, n - 1);

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

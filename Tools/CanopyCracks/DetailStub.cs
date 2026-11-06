using UnityEngine;
namespace DroneSandbox
{
    // The streamer's rough 4 m surface (what HLOD samples) and the 62.5 m far chunk (what is drawn beyond live detail).
    public static class FarDetail
    {
        public static float HeightAt(Vector2 p) => WorldPlan.Height(p.x + FarTerrain.Origin.x, p.y + FarTerrain.Origin.y);
        public static float ScatterHeight(Vector2 p, bool tree) => Detail(p.x + FarTerrain.Origin.x, p.y + FarTerrain.Origin.y);
        public static bool TryHeight(Vector2 p, out float h) { h = 0; return false; }
        public static bool Raycast(Ray r, out Vector3 p) { p = default; return false; }

        // Production predicts the detail mesh from its four 4 m corners, including the shared world roughness.
        public static float Detail(float x, float z)
        {
            float x0 = Mathf.Floor(x / 4f) * 4f, z0 = Mathf.Floor(z / 4f) * 4f;
            return FarDetailData.Triangle(Node(x0, z0), Node(x0 + 4f, z0), Node(x0, z0 + 4f), Node(x0 + 4f, z0 + 4f), (x - x0) / 4f, (z - z0) / 4f);
        }

        static float Node(float x, float z) => WorldPlan.Height(x, z) + FarDetailData.Roughness(x, z);

        // FarTerrain's highest chunk LOD: 64 quads per 4 km chunk, the same diagonal as FarDetailData.Triangle.
        public static float Chunk(float x, float z, float step = 62.5f)
        {
            float gx = (x + WorldPlan.Half) / step, gz = (z + WorldPlan.Half) / step;
            int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gz);
            float x0 = i * step - WorldPlan.Half, z0 = j * step - WorldPlan.Half;
            return FarDetailData.Triangle(WorldPlan.Height(x0, z0), WorldPlan.Height(x0 + step, z0), WorldPlan.Height(x0, z0 + step),
                WorldPlan.Height(x0 + step, z0 + step), gx - i, gz - j);
        }
    }
}

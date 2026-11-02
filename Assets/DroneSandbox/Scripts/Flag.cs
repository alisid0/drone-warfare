using UnityEngine;

namespace DroneSandbox
{
    // A flag on a pole, rippling in the wind: a small grid mesh whose far edge waves more than the edge on the pole.
    public class Flag : MonoBehaviour
    {
        const int Columns = 12, Rows = 7;
        const float ClothWidth = 3.4f, ClothHeight = 2.1f, PoleHeight = 8.5f;

        Mesh mesh;
        Vector3[] rest, moved;
        float phase;

        public static Flag Create(Transform parent, Vector3 position, float yaw, bool player, bool onGround = true)
        {
            var root = new GameObject(player ? "Flag " + Factions.PlayerShort : "Flag " + Factions.EnemyShort).transform;
            root.SetParent(parent, false);
            root.position = onGround ? Ground.OnGround(position) : position;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, PoleHeight / 2f, 0f), new Vector3(0.09f, PoleHeight / 2f, 0.09f),
                new Color(0.55f, 0.56f, 0.57f));
            Art.Part(PrimitiveType.Sphere, root, new Vector3(0f, PoleHeight + 0.05f, 0f), Vector3.one * 0.18f, new Color(0.55f, 0.56f, 0.57f));

            var cloth = new GameObject("Cloth");
            cloth.transform.SetParent(root, false);
            cloth.transform.localPosition = new Vector3(0.05f, PoleHeight - ClothHeight - 0.15f, 0f);
            var flag = cloth.AddComponent<Flag>();
            flag.phase = position.x * 0.37f + position.z * 0.21f;
            flag.Build();
            var renderer = cloth.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Factions.FlagMaterial(player);
            cloth.AddComponent<MeshFilter>().sharedMesh = flag.mesh;
            flag.Wave(0f);
            return flag;
        }

        void Build()
        {
            rest = new Vector3[(Columns + 1) * (Rows + 1)];
            var uvs = new Vector2[rest.Length];
            for (int j = 0; j <= Rows; j++)
            {
                for (int i = 0; i <= Columns; i++)
                {
                    float u = i / (float)Columns, v = j / (float)Rows;
                    rest[j * (Columns + 1) + i] = new Vector3(u * ClothWidth, v * ClothHeight, 0f);
                    uvs[j * (Columns + 1) + i] = new Vector2(u, v);
                }
            }
            var triangles = new int[Columns * Rows * 6];
            int t = 0;
            for (int j = 0; j < Rows; j++)
            {
                for (int i = 0; i < Columns; i++)
                {
                    int a = j * (Columns + 1) + i, b = a + 1, c = a + Columns + 1, d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }
            moved = (Vector3[])rest.Clone();
            mesh = new Mesh { name = "Flag", vertices = moved, uv = uvs, triangles = triangles };
            mesh.MarkDynamic();
        }

        void Update() => Wave(Time.time);

        void Wave(float time)
        {
            for (int k = 0; k < rest.Length; k++)
            {
                Vector3 p = rest[k];
                float along = p.x / ClothWidth; // 0 at the pole, 1 at the free edge
                p.z = Mathf.Sin(time * 3.2f + phase - p.x * 2.1f) * 0.22f * along;
                p.y -= along * along * 0.12f; // the free edge droops a little
                moved[k] = p;
            }
            mesh.vertices = moved;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (mesh != null) Art.Discard(mesh);
        }
    }
}

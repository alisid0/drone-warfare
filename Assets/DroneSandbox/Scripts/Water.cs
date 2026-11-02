using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The river's surface: a ribbon mesh along the river line at the water level Ground worked out, a little
    // wider than the channel so the shoreline is wherever the banks rise out of it. It's drawn with the
    // DroneSandbox/River water shader (Resources/Materials/Water, made by Drone Sandbox > Configure Project),
    // which wants UVs in metres: u across the river, v downstream.
    public static class Water
    {
        public static GameObject Build(Transform parent)
        {
            var field = MapLayout.RiverField;
            var line = field.points;
            float halfWidth = MapLayout.RiverHalfWidth + 1.8f;
            float limit = Ground.Size / 2f - 1f;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float travelled = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0) travelled += Vector2.Distance(line[i - 1], line[i]);
                Vector2 dir = (line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x) * halfWidth;
                Vector2 left = line[i] - side, right = line[i] + side;
                if (Mathf.Abs(line[i].x) > limit || Mathf.Abs(line[i].y) > limit) continue;
                float y = field.values[i];
                int start = vertices.Count;
                vertices.Add(new Vector3(left.x, y, left.y));
                vertices.Add(new Vector3(right.x, y, right.y));
                uvs.Add(new Vector2(-halfWidth, travelled));
                uvs.Add(new Vector2(halfWidth, travelled));
                if (start >= 2)
                {
                    // Wound so the surface faces up (Unity draws clockwise triangles as seen from above).
                    triangles.AddRange(new[] { start - 2, start - 1, start, start - 1, start + 1, start });
                }
            }

            var mesh = new Mesh { name = "River" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var go = new GameObject("River");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var material = Resources.Load<Material>("Materials/Water");
            renderer.sharedMaterial = material != null ? material : Art.Shared(new Color(0.08f, 0.12f, 0.1f));
            return go;
        }
    }
}

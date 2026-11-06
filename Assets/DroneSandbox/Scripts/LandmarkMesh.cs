using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Plain mesh data is shared by the offline renderer, local surface builder and distant GPU submissions.
    public static class LandmarkMesh
    {
        public const int MaterialCount = 9;

        // One material batch per landmark, including every building of that material and all its flat slabs.
        public sealed class Data
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<int>[] triangles = new List<int>[MaterialCount];

            // Empty submeshes are never submitted, keeping small compounds below the nine-draw upper bound.
            public Data() { for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>(); }

            // Independent face vertices preserve deliberate hard edges on roofs, tanks and chimneys.
            public void Triangle(int material, Vector3 a, Vector3 b, Vector3 c)
            {
                int i = vertices.Count;
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
                triangles[material].Add(i); triangles[material].Add(i + 1); triangles[material].Add(i + 2);
            }

            // Counterclockwise axes determine the face normal explicitly, including vertical building walls.
            public void Face(int material, Vector3 c, Vector3 u, Vector3 v)
            {
                Triangle(material, c - u - v, c + u - v, c + u + v);
                Triangle(material, c - u - v, c + u + v, c - u + v);
            }
        }

        // Sector surfaces clip inside the square; distant geometry clips outside that exact same square.
        // Passing clip=false exports the complete feature for an isolated comparison or world-map harness.
        public static Data Build(WorldFeature f, Vector2 origin, bool sector, bool clip = true)
        {
            var mesh = new Data();
            foreach (var s in f.surfaces)
            {
                var a = s.area;
                var polygon = new List<Vector2> { a.Point(-a.halfLength, -a.halfWidth) - origin,
                    a.Point(a.halfLength, -a.halfWidth) - origin, a.Point(a.halfLength, a.halfWidth) - origin,
                    a.Point(-a.halfLength, a.halfWidth) - origin };
                if (!clip) Slab(mesh, polygon, (int)s.kind, f.elevation + s.lift);
                else if (sector) Slab(mesh, Clip(Clip(Clip(Clip(polygon, 0, -1000f, true), 0, 1000f, false), 1, -1000f, true), 1, 1000f, false), (int)s.kind, f.elevation + s.lift);
                else
                {
                    Slab(mesh, Clip(polygon, 0, -1000f, false), (int)s.kind, f.elevation + s.lift);
                    Slab(mesh, Clip(polygon, 0, 1000f, true), (int)s.kind, f.elevation + s.lift);
                    var middle = Clip(Clip(polygon, 0, -1000f, true), 0, 1000f, false);
                    Slab(mesh, Clip(middle, 1, -1000f, false), (int)s.kind, f.elevation + s.lift);
                    Slab(mesh, Clip(middle, 1, 1000f, true), (int)s.kind, f.elevation + s.lift);
                }
            }
            if (!sector) foreach (var b in f.buildings)
            {
                Vector2 p = b.position - origin;
                if (clip && Mathf.Abs(p.x) <= 995f && Mathf.Abs(p.y) <= 995f) continue;
                Building(mesh, b, p, f.elevation - 0.05f);
            }
            return mesh;
        }

        // Polygon clipping shares exact boundary vertices, avoiding doubled slabs or a missing border strip.
        static List<Vector2> Clip(List<Vector2> input, int axis, float edge, bool greater)
        {
            var output = new List<Vector2>();
            if (input.Count == 0) return output;
            Vector2 a = input[input.Count - 1]; float av = axis == 0 ? a.x : a.y;
            bool ai = greater ? av >= edge : av <= edge;
            foreach (var b in input)
            {
                float bv = axis == 0 ? b.x : b.y; bool bi = greater ? bv >= edge : bv <= edge;
                if (ai != bi) output.Add(Vector2.Lerp(a, b, (edge - av) / (bv - av)));
                if (bi) output.Add(b);
                a = b; av = bv; ai = bi;
            }
            return output;
        }

        // Flat graded ground needs only a polygon fan, not thousands of eight-metre yard quads.
        static void Slab(Data mesh, List<Vector2> points, int material, float height)
        {
            if (points.Count < 3) return;
            Vector3 P(Vector2 p) => new Vector3(p.x, height, p.y);
            for (int i = 1; i < points.Count - 1; i++) mesh.Triangle(material, P(points[0]), P(points[i + 1]), P(points[i]));
        }

        // Roof volumes and twelve-sided tanks retain their silhouette at 2.5 km with very few triangles.
        static void Building(Data mesh, LandmarkBuilding b, Vector2 local, float floor)
        {
            Vector3 at = new Vector3(local.x, floor, local.y), forward = new Vector3(b.facing.x, 0f, b.facing.y);
            Vector3 right = new Vector3(b.facing.y, 0f, -b.facing.x);
            Vector3 P(float x, float y, float z) => at + right * x + Vector3.up * y + forward * z;
            float x = b.size.x / 2f, z = b.size.z / 2f, h = b.size.y;
            if (b.shape == 1 || b.shape == 2)
            {
                int material = b.shape == 1 ? 7 : 8;
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6f, c = (i + 1) * Mathf.PI / 6f;
                    Vector3 p = P(Mathf.Cos(a) * x, 0f, Mathf.Sin(a) * z), q = P(Mathf.Cos(c) * x, 0f, Mathf.Sin(c) * z);
                    Vector3 topP = p + Vector3.up * h, topQ = q + Vector3.up * h;
                    mesh.Triangle(material, p, topP, topQ); mesh.Triangle(material, p, topQ, q);
                    mesh.Triangle(material, P(0f, h, 0f), topQ, topP);
                }
                return;
            }
            float wall = b.shape == 0 ? h * 0.78f : h;
            Vector3 center = P(0f, wall / 2f, 0f), vx = right * x, vy = Vector3.up * (wall / 2f), vz = forward * z;
            mesh.Face(5, center + vx, vy, vz); mesh.Face(5, center - vx, vz, vy);
            mesh.Face(5, center + vz, vx, vy); mesh.Face(5, center - vz, vy, vx);
            if (b.shape != 0) { mesh.Face(6, P(0f, wall, 0f), vz, vx); return; }
            mesh.Triangle(5, P(-x, wall, z), P(x, wall, z), P(0f, h, z));
            mesh.Triangle(5, P(x, wall, -z), P(-x, wall, -z), P(0f, h, -z));
            mesh.Triangle(6, P(-x, wall, -z), P(-x, wall, z), P(0f, h, z));
            mesh.Triangle(6, P(-x, wall, -z), P(0f, h, z), P(0f, h, -z));
            mesh.Triangle(6, P(0f, h, -z), P(0f, h, z), P(x, wall, z));
            mesh.Triangle(6, P(0f, h, -z), P(x, wall, z), P(x, wall, -z));
        }

        // Shared lit colours keep the material count bounded; surfaces match FeatureSurfaceAt exactly.
        public static Color Color(int material)
        {
            if (material < 5) return WorldLandmarkLayout.SurfaceColor((LandmarkSurfaceKind)material);
            return material == 5 ? new Color(0.48f, 0.49f, 0.46f) : material == 6 ? new Color(0.31f, 0.34f, 0.33f)
                : material == 7 ? new Color(0.63f, 0.65f, 0.64f) : new Color(0.50f, 0.28f, 0.20f);
        }
    }
}

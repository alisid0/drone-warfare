using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Plain mesh data is shared by the offline renderer, local surface builder and distant GPU submissions.
    public static class LandmarkMesh
    {
        public const int MaterialCount = 12;

        // One material batch per landmark, including every building of that material and all its flat slabs.
        public sealed class Data
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<int>[] triangles = new List<int>[MaterialCount];

            // Empty submeshes are never submitted, keeping small compounds below the twelve-draw upper bound.
            public Data() { for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>(); }

            // Independent face vertices preserve deliberate hard edges on roofs, tanks and chimneys.
            public void Triangle(int material, Vector3 a, Vector3 b, Vector3 c)
            {
                int i = vertices.Count;
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < .00000001f) return; // coincident vertices can remain after clipping a paint edge
                Vector3 n = cross.normalized;
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
            for (int index = 0; index < f.surfaces.Count; index++)
            {
                var s = f.surfaces[index];
                var a = s.area;
                var pieces = new List<List<Vector2>> { Outline(s, origin) };
                for (int next = index + 1; next < f.surfaces.Count && pieces.Count > 0; next++)
                {
                    var later = f.surfaces[next];
                    Vector2 d = later.area.center - a.center;
                    // Cheap oriented separating axes reject almost all unrelated wear and paint patches.
                    if (Separated(a, later.area, d) || Separated(later.area, a, d)) continue;
                    var mask = Outline(later, origin); var remaining = new List<List<Vector2>>();
                    foreach (var piece in pieces) Subtract(piece, mask, remaining);
                    pieces = remaining;
                }
                foreach (var polygon in pieces)
                {
                    if (!clip) Slab(mesh, polygon, s, f.elevation, origin);
                    else if (sector) Slab(mesh, Clip(Clip(Clip(Clip(polygon, 0, -1000f, true), 0, 1000f, false), 1, -1000f, true), 1, 1000f, false), s, f.elevation, origin);
                    else
                    {
                        Slab(mesh, Clip(polygon, 0, -1000f, false), s, f.elevation, origin);
                        Slab(mesh, Clip(polygon, 0, 1000f, true), s, f.elevation, origin);
                        var middle = Clip(Clip(polygon, 0, -1000f, true), 0, 1000f, false);
                        Slab(mesh, Clip(middle, 1, -1000f, false), s, f.elevation, origin);
                        Slab(mesh, Clip(middle, 1, 1000f, true), s, f.elevation, origin);
                    }
                }
            }
            if (!sector) foreach (var b in f.buildings)
            {
                if (b.detail) continue;
                Vector2 p = b.position - origin;
                if (clip && Mathf.Abs(p.x) <= 995f && Mathf.Abs(p.y) <= 995f) continue;
                Building(mesh, b, p, f.elevation - 0.05f);
            }
            return mesh;
        }

        // Surface order is resolved into disjoint polygons, so submeshes cannot fight at a distant depth precision.
        static List<Vector2> Outline(LandmarkSurface s, Vector2 origin)
        {
            var p = new List<Vector2>();
            if (s.outline != null) foreach (var q in s.outline) p.Add(q - origin);
            else
            {
                var a = s.area;
                p.Add(a.Point(-a.halfLength, -a.halfWidth) - origin); p.Add(a.Point(a.halfLength, -a.halfWidth) - origin);
                p.Add(a.Point(a.halfLength, a.halfWidth) - origin); p.Add(a.Point(-a.halfLength, a.halfWidth) - origin);
            }
            return p;
        }

        // Projection bounds avoid polygon work when either rectangle axis separates the two surface footprints.
        static bool Separated(FeatureArea a, FeatureArea b, Vector2 d)
        {
            float u = Mathf.Abs(Vector2.Dot(d, a.along)), v = Mathf.Abs(Vector2.Dot(d, a.Across));
            float c = Mathf.Abs(Vector2.Dot(a.along, b.along)), s = Mathf.Abs(Vector2.Dot(a.along, b.Across));
            return u >= a.halfLength + c * b.halfLength + s * b.halfWidth
                || v >= a.halfWidth + s * b.halfLength + c * b.halfWidth;
        }

        // Each outside half-plane is emitted once while the inside fragment proceeds to the next convex edge.
        static void Subtract(List<Vector2> polygon, List<Vector2> mask, List<List<Vector2>> output)
        {
            var inside = polygon;
            for (int edge = 0; edge < mask.Count && inside.Count >= 3; edge++)
            {
                Vector2 a = mask[edge], d = mask[(edge + 1) % mask.Count] - a;
                var outer = new List<Vector2>(); var inner = new List<Vector2>();
                Vector2 p = inside[inside.Count - 1]; float dp = d.x * (p.y - a.y) - d.y * (p.x - a.x);
                foreach (var q in inside)
                {
                    float dq = d.x * (q.y - a.y) - d.y * (q.x - a.x);
                    if ((dp >= 0f) != (dq >= 0f))
                    {
                        Vector2 hit = Vector2.Lerp(p, q, dp / (dp - dq)); inner.Add(hit); outer.Add(hit);
                    }
                    if (dq >= 0f) inner.Add(q); else outer.Add(q);
                    p = q; dp = dq;
                }
                if (outer.Count >= 3) output.Add(outer);
                inside = inner;
            }
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
        static void Slab(Data mesh, List<Vector2> points, LandmarkSurface surface, float height, Vector2 origin)
        {
            if (points.Count < 3) return;
            Vector3 P(Vector2 p) => new Vector3(p.x, height + surface.lift + Vector2.Dot(p + origin - surface.area.center, surface.fall), p.y);
            for (int i = 1; i < points.Count - 1; i++) mesh.Triangle((int)surface.kind, P(points[0]), P(points[i + 1]), P(points[i]));
        }

        // Roof volumes and twelve-sided tanks retain their silhouette at 2.5 km with very few triangles.
        static void Building(Data mesh, LandmarkBuilding b, Vector2 local, float floor)
        {
            Vector3 at = new Vector3(local.x, floor, local.y), forward = new Vector3(b.facing.x, 0f, b.facing.y);
            Vector3 right = new Vector3(b.facing.y, 0f, -b.facing.x);
            Vector3 P(float x, float y, float z) => at + right * x + Vector3.up * y + forward * z;
            float x = b.size.x / 2f, z = b.size.z / 2f, h = b.size.y;
            if (b.shape == 4)
            {
                float[] levels = { 0f, .1f, .56f, 1f }, radii = { .95f, 1f, .55f, .69f };
                for (int j = 0; j < 3; j++) for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6f, c = (i + 1) * Mathf.PI / 6f;
                    Vector3 Q(float angle, int k) => P(Mathf.Cos(angle) * x * radii[k], h * levels[k], Mathf.Sin(angle) * z * radii[k]);
                    mesh.Triangle(5, Q(a, j), Q(a, j + 1), Q(c, j + 1));
                    mesh.Triangle(5, Q(a, j), Q(c, j + 1), Q(c, j));
                }
                return;
            }
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
            if (material < 5 || material >= 9) return WorldLandmarkLayout.SurfaceColor((LandmarkSurfaceKind)material);
            return material == 5 ? new Color(0.48f, 0.49f, 0.46f) : material == 6 ? new Color(0.31f, 0.34f, 0.33f)
                : material == 7 ? new Color(0.63f, 0.65f, 0.64f) : new Color(0.50f, 0.28f, 0.20f);
        }
    }
}

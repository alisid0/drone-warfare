using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Gardens share four materials and two LOD renderers per settlement, instead of a renderer per fence panel.
    public static partial class Features
    {
        static readonly List<Object> villageMeshes = new List<Object>();
        // Destroy runtime meshes on a rebuild, as destroying a scenery root does not free its shared meshes.
        public static void ClearVillagePlots()
        {
            foreach (var mesh in villageMeshes)
                Art.Discard(mesh);
            villageMeshes.Clear();
        }

        // Ground surfaces and enclosure silhouettes stay visible at 400 m; rail detail drops out sooner.
        public static int VillagePlots(Transform root, List<VillagePlot> plots, int budget)
        {
            if (plots.Count == 0 || budget < 2)
                return 0;
            var materials = new[]
            {
                Art.Shared(new Color(.24f, .29f, .13f)),
                Art.Shared(new Color(.26f, .20f, .12f)),
                Art.Shared(new Color(.43f, .41f, .35f)),
                Art.Shared(new Color(.16f, .22f, .10f))
            };
            var levels = new Renderer[2];
            for (int lod = 0; lod < 2; lod++)
            {
                var mesh = new VillageMesh();
                foreach (var p in plots)
                {
                    float half = p.frontage * .5f;
                    // Front lawns, a worn drive and a rear working yard belong to this exact property.
                    Surface(mesh, p, lod);
                    if (p.square)
                        continue;
                    // Front gates remain open; rear and side boundaries show the complete plot depth.
                    Edge(mesh, p, -half, 0f, -2f, 0f, lod);
                    Edge(mesh, p, 2f, 0f, half, 0f, lod);
                    Edge(mesh, p, -half, 0f, -half, p.depth, lod);
                    Edge(mesh, p, half, 0f, half, p.depth, lod);
                    Edge(mesh, p, -half, p.depth, half, p.depth, lod);
                    // These splats suppress native grass blades on beds, drives and yards.
                    if (lod == 0)
                    {
                        Ground.Wear(p.front, p.front + p.outward * (p.setback + 9f), 2.4f, .85f);
                        Ground.Wear(p.front + p.outward * (p.setback + 8f) - p.along * (half - 2f), p.front + p.outward * (p.setback + 8f) + p.along * (half - 2f), 7f, .8f);
                        if (!p.church && !p.farm)
                            for (float row = p.setback + 15f; row < p.depth - 4f; row += 2.8f)
                            {
                                Vector2 bed = p.front + p.outward * (row + .8f);
                                Ground.Wear(bed - p.along * (half - 2f), bed - p.along, 1.6f, 1f);
                            }

                        Ground.Paint(p.front - p.along * half, p.front - p.along * half + p.outward * p.depth, 1.1f, 1f, 1);
                    }
                }

                levels[lod] = mesh.Finish("Village plots LOD" + lod, root, materials);
            }

            var holder = new GameObject("Village plot LOD");
            holder.transform.SetParent(root, false);
            foreach (var level in levels)
                level.transform.SetParent(holder.transform, true);
            var group = holder.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(.16f, new[] { levels[0] }), new LOD(.006f, new[] { levels[1] }) });
            group.RecalculateBounds();
            float projected = group.size / (2f * Mathf.Tan(Scale.CameraFov * .5f * Mathf.Deg2Rad));
            group.SetLODs(new[] { new LOD(Mathf.Min(.9f, projected / 220f), new[] { levels[0] }), new LOD(Mathf.Min(.1f, projected / 1800f), new[] { levels[1] }) });
            return 2;
        }

        // One shared grid partitions lawn, beds and yards. Overlapping terrain sheets intersect on slopes.
        static void Surface(VillageMesh mesh, VillagePlot p, int lod)
        {
            float half = p.frontage * .5f;
            var us = new SortedSet<float>
            {
                -half + .5f,
                -half + 2f,
                -1.2f,
                -1f,
                1.2f,
                half - 2f,
                half - .5f
            };
            var vs = new SortedSet<float>
            {
                0f,
                p.depth - .5f,
                p.depth - 2f,
                p.setback + 4f,
                p.setback + 8f,
                Mathf.Min(p.depth - 2f, p.setback + (p.farm ? 25f : 12f))
            };
            float step = lod == 0 ? 4f : 8f;
            for (float u = -half + .5f; u < half - .5f; u += step)
                us.Add(u);
            for (float v = 0f; v < p.depth - .5f; v += step)
                vs.Add(v);
            if (!p.church && !p.farm)
                for (float row = p.setback + 15f; row < p.depth - 4f; row += 2.8f)
                {
                    vs.Add(row);
                    vs.Add(Mathf.Min(row + 1.6f, p.depth - 3f));
                }

            var xs = new List<float>(us);
            var zs = new List<float>(vs);
            for (int j = 1; j < zs.Count; j++)
                for (int i = 1; i < xs.Count; i++)
                {
                    float u = (xs[i - 1] + xs[i]) * .5f, v = (zs[j - 1] + zs[j]) * .5f;
                    if (v > p.depth - .5f)
                        continue;
                    int material = p.GroundMaterial(u, v);
                    mesh.Quad(material, At(p, xs[i - 1], zs[j - 1], .08f), At(p, xs[i], zs[j - 1], .08f), At(p, xs[i], zs[j], .08f), At(p, xs[i - 1], zs[j], .08f));
                }
        }

        // Most plots have hedges; stone churchyards and occasional low garden walls give streets a readable rhythm.
        static void Edge(VillageMesh mesh, VillagePlot p, float u0, float v0, float u1, float v1, int lod)
        {
            Vector2 a = p.front + p.along * u0 + p.outward * v0, b = p.front + p.along * u1 + p.outward * v1;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / (lod == 0 ? 4f : 8f)));
            float height = p.church ? 1.15f : p.style == 1 ? .85f : 1.35f, width = p.church ? .55f : p.style == 1 ? .3f : .9f;
            int material = p.church || p.style == 1 ? 2 : 3;
            Vector2 side = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            for (int i = 0; i < steps; i++)
            {
                Vector2 x = Vector2.Lerp(a, b, i / (float)steps), y = Vector2.Lerp(a, b, (i + 1f) / steps);
                Vector3 al = Ground.OnGround(new Vector3(x.x - side.x, 0f, x.y - side.y)), ar = Ground.OnGround(new Vector3(x.x + side.x, 0f, x.y + side.y));
                Vector3 bl = Ground.OnGround(new Vector3(y.x - side.x, 0f, y.y - side.y)), br = Ground.OnGround(new Vector3(y.x + side.x, 0f, y.y + side.y));
                Vector3 up = Vector3.up * height;
                mesh.Quad(material, al, bl, bl + up, al + up);
                mesh.Quad(material, br, ar, ar + up, br + up);
                mesh.Quad(material, al + up, bl + up, br + up, ar + up);
                if (lod == 0 && p.style == 1)
                    mesh.Quad(2, al, ar, ar + up, al + up);
            }
        }

        // Plot-local positions use the same axes as the world painter and preserve the finished terrain height.
        static Vector3 At(VillagePlot p, float u, float v, float lift)
        {
            Vector2 q = p.front + p.along * u + p.outward * v;
            return Ground.OnGround(new Vector3(q.x, 0f, q.y)) + Vector3.up * lift;
        }

        // Explicit front-facing triangles keep garden strips and solid hedges in one renderer per LOD.
        sealed class VillageMesh
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int>[] triangles =
            {
                new List<int>(),
                new List<int>(),
                new List<int>(),
                new List<int>()
            };
            // Flat normals retain low stone walls and hedge crowns without transparent materials.
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int first = vertices.Count;
                Vector3 n = Vector3.Cross(b - a, d - a).normalized;
                if (n.y < -.5f)
                {
                    var t = b;
                    b = d;
                    d = t;
                    n = -n;
                }

                vertices.AddRange(new[] { a, b, c, d });
                normals.AddRange(new[] { n, n, n, n });
                triangles[sub].AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }

            // The Unity API check compiles all mesh and LOD calls against the installed editor assemblies.
            public Renderer Finish(string name, Transform parent, Material[] materials)
            {
                var mesh = new Mesh
                {
                    name = name,
                    indexFormat = IndexFormat.UInt32
                };
                villageMeshes.Add(mesh);
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.subMeshCount = 4;
                for (int i = 0; i < 4; i++)
                    mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                return renderer;
            }
        }
    }
}

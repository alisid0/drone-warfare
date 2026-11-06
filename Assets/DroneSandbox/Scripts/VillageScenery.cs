using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Yard surfaces and mixed boundaries share 96 m blocks; fine garden work disappears at 300 m.
    public static partial class Features
    {
        static readonly List<UnityEngine.Object> villageMeshes = new List<UnityEngine.Object>();

        // Small garden clutter stops at 300 m; barns, houses and sheds remain as the major structures.
        public static void VillagePropLods(GameObject prop)
        {
            foreach (var group in prop.transform.GetComponentsInChildren<LODGroup>())
            {
                var levels = group.GetLODs();
                Vector3 scale = group.transform.lossyScale;
                float projected = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))
                    / (2f * Mathf.Tan(Scale.CameraFov * .5f * Mathf.Deg2Rad));
                float previous = .99f;
                for (int i = 0; i < levels.Length; i++)
                {
                    float distance = 300f * (i + 1f) / levels.Length;
                    levels[i].screenRelativeTransitionHeight = Mathf.Min(previous * .9f, projected / distance);
                    previous = levels[i].screenRelativeTransitionHeight;
                }
                group.SetLODs(levels);
            }
        }

        // Rebuilds release generated meshes independently of the scenery hierarchy.
        public static void ClearVillagePlots()
        {
            foreach (var mesh in villageMeshes) Art.Discard(mesh);
            villageMeshes.Clear();
        }

        // Two renderers per occupied block replace the old settlement-wide enclosure mesh.
        public static int VillagePlots(Transform root, List<VillagePlot> plots, int budget)
        {
            // Gardens are batched before the fringe's paddock rails and orchard hedges, so a tight allowance drops
            // fringe fencing first; open rough grass is ground paint only and needs no renderer at all.
            var blocks = new SortedDictionary<long, List<VillagePlot>>();
            var fringe = new SortedDictionary<long, List<VillagePlot>>();
            foreach (var p in plots)
            {
                PaintUse(p);
                if (p.landUse >= 6 && p.boundary == 0) continue;
                Vector2 c = p.Point(0f, p.depth * .5f);
                int x = Mathf.FloorToInt(c.x / 96f), z = Mathf.FloorToInt(c.y / 96f);
                long key = ((long)x << 32) ^ (uint)z;
                var target = p.landUse >= 6 ? fringe : blocks;
                if (!target.TryGetValue(key, out var list)) target.Add(key, list = new List<VillagePlot>());
                list.Add(p);
            }
            var materials = new[] { Art.Shared(new Color(.24f,.29f,.13f)), Art.Shared(new Color(.26f,.20f,.12f)),
                Art.Shared(new Color(.39f,.36f,.29f)), Art.Shared(new Color(.16f,.22f,.10f)), Art.Shared(new Color(.82f,.82f,.76f)) };
            int used = 0;
            var ordered = new List<List<VillagePlot>>(blocks.Values);
            ordered.AddRange(fringe.Values);
            foreach (var block in ordered)
            {
                if (used + 2 > budget) break;
                var holder = new GameObject("Village 96 m block"); holder.transform.SetParent(root, false);
                var levels = new Renderer[2];
                for (int lod = 0; lod < 2; lod++)
                {
                    var mesh = new VillageMesh();
                    foreach (var p in block)
                    {
                        if (p.landUse > 0 && p.landUse < 5) { TownLandUse(mesh, p, lod); continue; }
                        if (lod == 0 && p.landUse < 6) Surface(mesh, p);
                        // Paddock rails and orchard hedges are drawn; squares, open plots and rough grass have none.
                        if (p.square && p.landUse < 6 || p.boundary == 0) continue;
                        var polygon = p.Outline();
                        for (int edge = 0; edge < polygon.Length; edge++)
                        {
                            if (!p.EdgeShown(edge) || lod == 1 && (edge == 0 || p.boundary == 2)) continue;
                            Vector2 a = polygon[edge], b = polygon[(edge + 1) % polygon.Length];
                            // Missing runs leave open neighbouring gardens; the front also has two deliberate gates.
                            if ((p.identity + edge) % 2 == 0) b = Vector2.Lerp(a, b, .72f);
                            else a = Vector2.Lerp(a, b, .28f);
                            Edge(mesh, p, a, b, lod);
                        }
                    }
                    levels[lod] = mesh.Finish("Village plots LOD" + lod, holder.transform, materials);
                }
                var group = holder.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(.3f, new[] { levels[0] }), new LOD(.01f, new[] { levels[1] }) });
                group.RecalculateBounds();
                float projected = group.size / (2f * Mathf.Tan(Scale.CameraFov * .5f * Mathf.Deg2Rad));
                group.SetLODs(new[] { new LOD(Mathf.Min(.9f, projected / 300f), new[] { levels[0] }),
                    new LOD(Mathf.Min(.1f, projected / 1600f), new[] { levels[1] }) });
                used += 2;
            }
            return used;
        }

        // Paint contiguous row runs once, into Ground's existing deferred batch, instead of allocating terrain maps.
        static void PaintUse(VillagePlot p)
        {
            float step = 1.5f;
            for (float v = step * .5f; v < p.depth; v += step)
            {
                p.Limits(v, out float lo, out float hi);
                int last = -1; float start = lo + .6f, end = hi - .6f;
                for (float u = start; u <= end + step; u += step)
                {
                    int use = u > end ? -1 : p.WorkAt(u, v);
                    if (use == last) continue;
                    float finish = Math.Min(end, u - step * .5f);
                    if (last == VillagePlot.Soil || last == VillagePlot.Gravel) Ground.Wear(p.Point(start, v), p.Point(finish, v), step, .85f);
                    if (last == VillagePlot.Rough) Ground.Paint(p.Point(start, v), p.Point(finish, v), step, .55f, 4);
                    if (last == VillagePlot.Stain || last == VillagePlot.Mud) Ground.Paint(p.Point(start, v), p.Point(finish, v), step, .8f, 6); // wet mud layer
                    start = Math.Max(lo + .6f, u - step * .5f); last = use;
                }
            }
            if (p.landUse >= 6) return;
            // These exact-width approaches connect the road edge, gate and door; the drive runs beside the house.
            Ground.Wear(p.Point(0f, -1.8f), p.Point(0f, p.setback), p.pathWidth > 0f ? p.pathWidth : 1.4f, .85f);
            if (!p.HasDrive) return;
            float drive = p.driveWidth > 0f ? p.driveWidth : 3f;
            Ground.Wear(p.Point(p.DriveU, -1.8f), p.Point(p.DriveU, p.YardEnd), drive, .85f);
            // Wheels cut the corners where the drive meets the street, and a parked car drips by the house.
            Ground.Wear(p.Point(p.DriveU, -1.8f), p.Point(p.DriveU, 1.2f), drive + 2.4f, .7f);
            if (p.HasStain) Ground.Paint(p.Point(p.DriveU, p.setback + 1.6f), p.Point(p.DriveU, p.setback + 4.8f), 1.5f, .8f, 6);
        }

        // Only occupied working ground gets geometry; lawns retain the terrain's continuous grass material.
        static void Surface(VillageMesh mesh, VillagePlot p)
        {
            // Worn yards are terrain splats, avoiding stepped polygon islands and duplicate ground sheets.
            Strip(mesh, p, 0f, 0f, p.setback, p.pathWidth > 0f ? p.pathWidth : 1.4f, 2);
            if (p.HasDrive)
                Strip(mesh, p, p.DriveU, 0f, p.YardEnd, p.driveWidth > 0f ? p.driveWidth : 3f, 2);
            // The parked car's drip patch sits just above the gravel strip, in the darker soil colour.
            if (p.HasStain) Strip(mesh, p, p.DriveU, p.setback + 1.6f, p.setback + 4.8f, 1.4f, 1, .065f);
            if (!p.cultivated) return;
            for (float v = p.YardEnd + 1f; v < p.depth - 3f; v += 2.7f)
            {
                p.Limits(v + .85f, out float lo, out _);
                float hi = -1f; lo += 2.5f;
                if (lo >= hi) continue;
                mesh.Quad(1, At(p.Point(lo, v), .055f), At(p.Point(hi, v), .055f),
                    At(p.Point(hi, v + 1.7f), .055f), At(p.Point(lo, v + 1.7f), .055f));
            }
        }

        // Access strips preserve their physical widths while following the slope at two-metre intervals.
        static void Strip(VillageMesh mesh, VillagePlot p, float u, float start, float end, float width, int material, float lift = .055f)
        {
            for (float v = start; v < end; v += 2f)
            {
                float back = Math.Min(end, v + 2f), half = width * .5f;
                mesh.Quad(material, At(p.Point(u-half,v),lift), At(p.Point(u+half,v),lift),
                    At(p.Point(u+half,back),lift), At(p.Point(u-half,back),lift));
            }
        }

        // Hedges, two-rail wood and low masonry keep distinct silhouettes without adding per-panel renderers.
        static void Edge(VillageMesh mesh, VillagePlot p, Vector2 a, Vector2 b, int lod)
        {
            int n = Math.Max(1, (int)Math.Ceiling((b - a).magnitude / (lod == 0 ? 2f : 6f)));
            for (int i = 0; i < n; i++)
            {
                Vector2 x = Vector2.Lerp(a, b, i / (float)n), y = Vector2.Lerp(a, b, (i + 1f) / n);
                if (Math.Abs(x.y) < .1f && (Math.Min(Math.Abs(x.x), Math.Abs(y.x)) < 1.3f
                    || Math.Min(Math.Abs(x.x - p.DriveU), Math.Abs(y.x - p.DriveU)) < 2f)) continue;
                Vector2 from = p.Point(x.x, x.y), to = p.Point(y.x, y.y);
                float height = p.boundary == 1 ? 1.1f : .85f, width = p.boundary == 1 ? .75f : .3f;
                if (p.boundary == 2)
                {
                    Box(mesh, from, to, .38f, .16f, .11f, 1);
                    Box(mesh, from, to, .74f, .13f, .11f, 1);
                    Box(mesh, from, from + p.outward * .12f, 0f, .95f, .13f, 1);
                }
                else Box(mesh, from, to, 0f, height, width, p.boundary == 1 ? 3 : 2);
                if (lod == 0 && p.boundary == 1) Ground.Paint(from, to, 1f, .7f, 1);
            }
        }

        // A closed opaque strip follows both endpoint heights, including on a sloping garden wall.
        static void Box(VillageMesh mesh, Vector2 a, Vector2 b, float bottom, float height, float width, int material)
        {
            Vector2 side = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            Vector3 al = At(a - side, bottom), ar = At(a + side, bottom), bl = At(b - side, bottom), br = At(b + side, bottom);
            Vector3 up = Vector3.up * height;
            mesh.Quad(material, al, bl, bl + up, al + up); mesh.Quad(material, br, ar, ar + up, br + up);
            mesh.Quad(material, al + up, bl + up, br + up, ar + up);
        }

        // Meshes sample the finished terrain rather than a competing height prediction.
        static Vector3 At(Vector2 q, float lift) => Ground.OnGround(new Vector3(q.x, 0f, q.y)) + Vector3.up * lift;

        // A handful of opaque materials share each block's vertices and draw lists.
        sealed class VillageMesh
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };

            // Front-facing triangles retain yard and hedge silhouettes without transparent materials.
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int first = vertices.Count; Vector3 n = Vector3.Cross(b - a, d - a).normalized;
                if (n.y < -.5f) { var t = b; b = d; d = t; n = -n; }
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                for (int i = 0; i < 4; i++) normals.Add(n);
                var list = triangles[sub]; list.Add(first); list.Add(first + 1); list.Add(first + 2);
                list.Add(first); list.Add(first + 2); list.Add(first + 3);
            }

            // Every generated mesh is registered for disposal on the next sector build.
            public Renderer Finish(string name, Transform parent, Material[] materials)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                villageMeshes.Add(mesh); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateBounds();
                var go = new GameObject(name); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = materials; return renderer;
            }
        }
    }
}

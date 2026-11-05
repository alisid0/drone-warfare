using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Exact rectangle subtraction prevents either coarse triangles or forest canopy poking through a detail tile.
    // Replacement chunk meshes stay hidden until the tile and every affected chunk can be published together.
    public static class FarDetailChunks
    {
        // A retained LOD0 source and two upload buffers allow atomic swaps without editing a visible mesh.
        public sealed class Chunk
        {
            public Vector2 corner;
            public MeshFilter filter;
            public LODGroup group;
            public Mesh original, front, back;
            public Geometry source;
        }

        // The worker manipulates only mesh arrays; it never dereferences a Chunk's Unity objects.
        public sealed class Geometry
        {
            public Vector3[] vertices, normals;
            public Vector2[] uv;
            public int[] triangles;
        }

        // World-space bounds of one live or incoming detail tile.
        public struct Area
        {
            public int x, z;
            public float Left => x * 512f;
            public float Bottom => z * 512f;
        }

        // Interpolating all attributes preserves the original coarse surface and its world-picture alignment.
        struct Vertex
        {
            public Vector3 p, n;
            public Vector2 uv;
        }

        public static readonly List<Chunk> All = new List<Chunk>();

        // World rebuilds release both pooled replacement buffers before FarTerrain destroys its original meshes.
        public static void Clear()
        {
            foreach (var c in All) { Art.Discard(c.front); Art.Discard(c.back); }
            All.Clear();
        }

        // Read engine mesh arrays once at setup, while they are still immutable and on the main thread.
        public static void Register(Vector2 corner, MeshFilter filter, LODGroup group)
        {
            var mesh = filter.sharedMesh;
            All.Add(new Chunk
            {
                corner = corner,
                filter = filter,
                group = group,
                original = mesh,
                source = new Geometry { vertices = mesh.vertices, normals = mesh.normals, uv = mesh.uv, triangles = mesh.triangles }
            });
        }

        // The same sampled LOD0 heights drive each tile's boundary morph and the harness's seam checks.
        public static float[][] Heights()
        {
            var result = new float[All.Count][];
            for (int i = 0; i < All.Count; i++)
            {
                result[i] = new float[65 * 65];
                for (int k = 0; k < result[i].Length; k++) result[i][k] = All[i].source.vertices[k].y;
            }
            return result;
        }

        // Include a touching chunk too: a tile exactly on a 4 km boundary must join its neighbour's LOD0 edge.
        public static bool Overlaps(Vector2 corner, Area a)
        { return a.Left <= corner.x + 4000f && a.Left + 512f >= corner.x && a.Bottom <= corner.y + 4000f && a.Bottom + 512f >= corner.y; }

        // Subtract active tile rectangles from the original triangles, preserving every outside fragment exactly.
        public static Geometry Cut(Geometry source, Vector2 corner, Area[] areas, FarDetailData plan)
        {
            var vertices = new List<Vector3>(source.vertices); var normals = new List<Vector3>(source.normals); var uv = new List<Vector2>(source.uv); var indices = new List<int>(source.triangles.Length);
            var relevant = new List<Area>();
            foreach (var area in areas) if (Overlaps(corner, area)) relevant.Add(area);
            if (relevant.Count == 0) return source;
            var pieces = new List<List<Vertex>>();
            for (int t = 0; t < source.triangles.Length; t += 3)
            {
                // Most of a 4 km chunk is untouched. Keep its shared vertices and original indices, avoiding
                // thousands of polygon allocations and duplicated vertices on every streamed tile.
                int ia = source.triangles[t], ib = source.triangles[t + 1], ic = source.triangles[t + 2];
                var va = source.vertices[ia]; var vb = source.vertices[ib]; var vc = source.vertices[ic];
                float loX = Math.Min(va.x, Math.Min(vb.x, vc.x)) + corner.x, hiX = Math.Max(va.x, Math.Max(vb.x, vc.x)) + corner.x;
                float loZ = Math.Min(va.z, Math.Min(vb.z, vc.z)) + corner.y, hiZ = Math.Max(va.z, Math.Max(vb.z, vc.z)) + corner.y;
                bool touched = false, covered = false;
                foreach (var area in relevant)
                {
                    if (hiX >= area.Left && loX <= area.Left + 512f && hiZ >= area.Bottom && loZ <= area.Bottom + 512f) touched = true;
                    if (loX >= area.Left && hiX <= area.Left + 512f && loZ >= area.Bottom && hiZ <= area.Bottom + 512f) { covered = true; break; }
                }
                if (covered) continue;
                if (!touched) { indices.Add(ia); indices.Add(ib); indices.Add(ic); continue; }
                pieces.Clear();
                var triangle = new List<Vertex>(3);
                for (int k = 0; k < 3; k++)
                {
                    int i = source.triangles[t + k];
                    triangle.Add(new Vertex { p = source.vertices[i], n = source.normals[i], uv = source.uv[i] });
                }
                pieces.Add(triangle);
                foreach (var area in relevant)
                {
                    int oldCount = pieces.Count;
                    for (int k = oldCount - 1; k >= 0; k--)
                    {
                        var polygon = pieces[k];
                        if (!Touches(polygon, area.Left - corner.x, area.Bottom - corner.y)) continue;
                        pieces.RemoveAt(k);
                        var inside = polygon;
                        for (int edge = 0; edge < 4 && inside.Count > 0; edge++)
                        {
                            float limit = edge < 2 ? area.Left - corner.x + edge * 512f : area.Bottom - corner.y + (edge - 2) * 512f;
                            Split(inside, edge, limit, out var outside, out inside);
                            if (outside.Count >= 3) pieces.Add(outside);
                        }
                    }
                }
                foreach (var polygon in pieces)
                {
                    var stitched = Stitch(polygon, corner, relevant, plan);
                    int start = vertices.Count;
                    foreach (var v in stitched) { vertices.Add(v.p); normals.Add(v.n); uv.Add(v.uv); }
                    // A centre fan keeps all collinear boundary samples, including those on the first edge.
                    if (stitched.Count > 3)
                    {
                        var center = new Vertex();
                        foreach (var v in stitched)
                        {
                            center.p.x += v.p.x / stitched.Count; center.p.y += v.p.y / stitched.Count; center.p.z += v.p.z / stitched.Count;
                            center.n.x += v.n.x / stitched.Count; center.n.y += v.n.y / stitched.Count; center.n.z += v.n.z / stitched.Count;
                            center.uv.x += v.uv.x / stitched.Count; center.uv.y += v.uv.y / stitched.Count;
                        }
                        int mid = vertices.Count; vertices.Add(center.p); normals.Add(center.n); uv.Add(center.uv);
                        for (int i = 0; i < stitched.Count; i++) { indices.Add(mid); indices.Add(start + i); indices.Add(start + (i + 1) % stitched.Count); }
                    }
                    else { indices.Add(start); indices.Add(start + 1); indices.Add(start + 2); }
                }
            }
            return new Geometry { vertices = vertices.ToArray(), normals = normals.ToArray(), uv = uv.ToArray(), triangles = indices.ToArray() };
        }

        // Insert the tile's 4 m boundary samples into coarse cut edges. This eliminates T-junction cracks and
        // matches the tile's piecewise-linear edge exactly, including where a 62.5 m coarse diagonal crosses it.
        static List<Vertex> Stitch(List<Vertex> polygon, Vector2 corner, List<Area> areas, FarDetailData plan)
        {
            var result = new List<Vertex>();
            var cuts = new List<float>();
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k]; var b = polygon[(k + 1) % polygon.Count];
                result.Add(MatchBoundary(a, corner, areas, plan));
                cuts.Clear();
                float ax = a.p.x + corner.x, bx = b.p.x + corner.x, az = a.p.z + corner.y, bz = b.p.z + corner.y;
                if (Math.Abs(a.p.y - plan.CoarseHeight(ax, az)) > 2f || Math.Abs(b.p.y - plan.CoarseHeight(bx, bz)) > 2f) continue;
                foreach (var area in areas)
                {
                    if (Math.Abs(ax - bx) < 0.002f && (Math.Abs(ax - area.Left) < 0.002f || Math.Abs(ax - area.Left - 512f) < 0.002f))
                        EdgeCuts(cuts, az, bz, area.Bottom, area.Bottom + 512f);
                    if (Math.Abs(az - bz) < 0.002f && (Math.Abs(az - area.Bottom) < 0.002f || Math.Abs(az - area.Bottom - 512f) < 0.002f))
                        EdgeCuts(cuts, ax, bx, area.Left, area.Left + 512f);
                }
                cuts.Sort();
                float previous = -1f;
                foreach (float t in cuts)
                {
                    if (t <= 0.00001f || t >= 0.99999f || Math.Abs(t - previous) < 0.00001f) continue;
                    var v = new Vertex { p = V3(a.p, b.p, t), n = V3(a.n, b.n, t), uv = new Vector2(a.uv.x + (b.uv.x - a.uv.x) * t, a.uv.y + (b.uv.y - a.uv.y) * t) };
                    result.Add(MatchBoundary(v, corner, areas, plan)); previous = t;
                }
            }
            return result;
        }

        // Split even a partial boundary edge at both tile corners and all intermediate 4 m samples.
        static void EdgeCuts(List<float> cuts, float from, float to, float lo, float hi)
        {
            if (Math.Abs(to - from) < 0.001f) return;
            lo = Math.Max(lo, Math.Min(from, to)); hi = Math.Min(hi, Math.Max(from, to));
            if (hi < lo) return;
            cuts.Add((lo - from) / (to - from)); cuts.Add((hi - from) / (to - from));
            for (float at = (float)Math.Ceiling(lo / 4f) * 4f; at < hi; at += 4f) cuts.Add((at - from) / (to - from));
        }

        // Boundary samples are shared by both sides; leave the lower hanging skirt vertices alone.
        static Vertex MatchBoundary(Vertex v, Vector2 corner, List<Area> areas, FarDetailData plan)
        {
            float x = v.p.x + corner.x, z = v.p.z + corner.y;
            if (Math.Abs(v.p.y - plan.CoarseHeight(x, z)) > 2f) return v;
            foreach (var area in areas)
            {
                if ((Math.Abs(x - area.Left) < 0.002f || Math.Abs(x - area.Left - 512f) < 0.002f) && z >= area.Bottom - 0.002f && z <= area.Bottom + 512.002f)
                    return Match(v, corner, true, plan);
                if ((Math.Abs(z - area.Bottom) < 0.002f || Math.Abs(z - area.Bottom - 512f) < 0.002f) && x >= area.Left - 0.002f && x <= area.Left + 512.002f)
                    return Match(v, corner, false, plan);
            }
            return v;
        }

        // Sampling the same pair of 4 m endpoints as the tile avoids a second interpolation convention.
        static Vertex Match(Vertex v, Vector2 corner, bool vertical, FarDetailData plan)
        {
            float x = v.p.x + corner.x, z = v.p.z + corner.y, at = vertical ? z : x;
            float lo = (float)Math.Floor(at / 4f) * 4f;
            float a = plan.CoarseHeight(vertical ? x : lo, vertical ? lo : z), b = plan.CoarseHeight(vertical ? x : lo + 4f, vertical ? lo + 4f : z);
            v.p.y = FarDetailData.Mix(a, b, (at - lo) / 4f); return v;
        }

        // Reject distant triangles cheaply before any clipping allocations.
        static bool Touches(List<Vertex> p, float x, float z)
        {
            float loX = float.MaxValue, loZ = float.MaxValue, hiX = float.MinValue, hiZ = float.MinValue;
            foreach (var v in p) { loX = Math.Min(loX, v.p.x); hiX = Math.Max(hiX, v.p.x); loZ = Math.Min(loZ, v.p.z); hiZ = Math.Max(hiZ, v.p.z); }
            return hiX > x && loX < x + 512f && hiZ > z && loZ < z + 512f;
        }

        // Splitting against each half-plane returns disjoint outside pieces, never overlapping draw surfaces.
        static void Split(List<Vertex> input, int edge, float limit, out List<Vertex> outside, out List<Vertex> inside)
        {
            outside = new List<Vertex>(6); inside = new List<Vertex>(6);
            Vertex a = input[input.Count - 1]; float da = Signed(a, edge, limit);
            foreach (var b in input)
            {
                float db = Signed(b, edge, limit);
                if ((da < 0) != (db < 0))
                {
                    float t = da / (da - db);
                    var v = new Vertex { p = V3(a.p, b.p, t), n = V3(a.n, b.n, t), uv = new Vector2(a.uv.x + (b.uv.x - a.uv.x) * t, a.uv.y + (b.uv.y - a.uv.y) * t) };
                    outside.Add(v); inside.Add(v);
                }
                if (db < 0) outside.Add(b); else inside.Add(b);
                a = b; da = db;
            }
        }

        // Positive signed distances lie inside the rectangle on the current plane.
        static float Signed(Vertex v, int edge, float limit) => edge == 0 ? v.p.x - limit : edge == 1 ? limit - v.p.x : edge == 2 ? v.p.z - limit : limit - v.p.z;
        // Plain scalar interpolation keeps clipping independent of native engine code.
        static Vector3 V3(Vector3 a, Vector3 b, float t) => new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    }
}

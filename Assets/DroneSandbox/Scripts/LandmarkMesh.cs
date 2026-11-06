using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Plain mesh data is shared by the offline renderer, local surface builder and distant GPU submissions.
    public static class LandmarkMesh
    {
        public const int MaterialCount = 20;
        // Every flat surface shares this submesh and one palette material: its tone and soft edge are texture coordinates.
        public const int SurfaceSlot = 0;
        // Eight texels per tone pair (top over the tone below), 64 pairs to a row.
        public const int PaletteWidth = 512, PaletteHeight = 64;
        const int Runs = PaletteWidth / 8;
        // Later surfaces this much higher are drawn over earlier slabs instead of cutting holes in them.
        const float LayerGap = .035f;

        // One material batch per landmark, including every building of that material and all its flat slabs.
        public sealed class Data
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int>[] triangles = new List<int>[MaterialCount];

            // Empty submeshes are never submitted, keeping small compounds below the twelve-draw upper bound.
            public Data() { for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>(); }

            // Independent face vertices preserve deliberate hard edges on roofs, tanks and chimneys.
            public void Triangle(int material, Vector3 a, Vector3 b, Vector3 c) => Triangle(material, a, b, c, default, default, default);

            // A triangle with its own palette coordinates; building faces leave them at zero.
            public void Triangle(int material, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                int i = vertices.Count;
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < .00000001f) return; // coincident vertices can remain after clipping a paint edge
                Vector3 n = cross.normalized;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
                uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
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
            var builder = new Builder(f, origin, sector, clip);
            builder.Step(double.MaxValue);
            return builder.mesh;
        }

        // Meshes one feature a few surfaces at a time, so LandmarkHlod can spread a large yard's soft edges over several
        // frames instead of one long stage; Build runs it to the end in one go.
        public sealed class Builder
        {
            public readonly Data mesh = new Data();
            readonly WorldFeature f;
            readonly Vector2 origin;
            readonly bool sector, clip, whole;
            readonly List<Vector2>[] outlines; // read-only once made, shared by every cut
            readonly float[] boxes, lifts;
            readonly Scratch scratch = new Scratch();
            readonly System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
            int index;

            // World bounding boxes and lifts go into flat arrays: the pair loops reject most pairs without copying surfaces.
            public Builder(WorldFeature f, Vector2 origin, bool sector, bool clip = true)
            {
                this.f = f; this.origin = origin; this.sector = sector; this.clip = clip;
                // Most compounds lie wholly inside or wholly outside the sector square; those need no clipping at all.
                Vector2 c = f.center - origin; float reach = f.halfLength + f.halfWidth + f.clearance;
                whole = !clip || (sector ? Mathf.Abs(c.x) + reach < 1000f && Mathf.Abs(c.y) + reach < 1000f
                    : Mathf.Abs(c.x) - reach > 1000f || Mathf.Abs(c.y) - reach > 1000f);
                outlines = new List<Vector2>[f.surfaces.Count];
                boxes = new float[f.surfaces.Count * 4]; lifts = new float[f.surfaces.Count];
                for (int i = 0; i < f.surfaces.Count; i++)
                {
                    var area = f.surfaces[i].area; Vector2 across = area.Across;
                    float ex = Mathf.Abs(area.along.x) * area.halfLength + Mathf.Abs(across.x) * area.halfWidth;
                    float ez = Mathf.Abs(area.along.y) * area.halfLength + Mathf.Abs(across.y) * area.halfWidth;
                    boxes[i * 4] = area.center.x - ex; boxes[i * 4 + 1] = area.center.y - ez; boxes[i * 4 + 2] = area.center.x + ex; boxes[i * 4 + 3] = area.center.y + ez;
                    lifts[i] = f.surfaces[i].lift;
                }
            }

            // Meshes surfaces until all are done or the budget is spent (at least one per call); true once the mesh is
            // complete, distant building masses included.
            public bool Step(double milliseconds)
            {
                timer.Restart();
                while (index < f.surfaces.Count)
                {
                    Surface(index++);
                    if (index < f.surfaces.Count && timer.Elapsed.TotalMilliseconds >= milliseconds) return false;
                }
                if (index == f.surfaces.Count)
                {
                    index++;
                    if (!sector) foreach (var b in f.buildings)
                    {
                        if (b.detail) continue;
                        Vector2 p = b.position - origin;
                        if (clip && Mathf.Abs(p.x) <= 995f && Mathf.Abs(p.y) <= 995f) continue;
                        LandmarkMesh.Building(mesh, b, p, f.elevation - 0.05f);
                    }
                }
                return true;
            }

            // One surface: cut by its later level neighbours, clipped to the sector square, then laid as pours and soft edges.
            void Surface(int index)
            {
                var s = f.surfaces[index];
                var a = s.area;
                var layer = new Layer { surface = s, index = index, surfaces = f.surfaces, origin = origin, height = f.elevation, boxes = boxes, scratch = scratch };
                var pieces = new List<List<Vector2>> { outlines[index] ?? (outlines[index] = Outline(s, origin)) };
                // Each cut can split every piece once per mask edge, so many overlapping wear patches multiplied the pieces
                // without bound and hung the main thread when a works came into view (hd147). Raised decals no longer cut
                // the slabs below them, so only level neighbours split a slab; past 96 pieces the rest are left to overlap:
                // a few distant z-fights cost far less than a frozen game.
                for (int next = index + 1; next < f.surfaces.Count && pieces.Count > 0 && pieces.Count <= 96; next++)
                {
                    if (lifts[next] >= s.lift + LayerGap) continue; // a raised decal hides the slab; the painter agrees, as later wins
                    if (!Overlap(boxes, index, next)) continue;
                    var later = f.surfaces[next];
                    Vector2 d = later.area.center - a.center;
                    // Cheap oriented separating axes reject almost all unrelated wear and paint patches.
                    if (Separated(a, later.area, d) || Separated(later.area, a, d)) continue;
                    var mask = outlines[next] ?? (outlines[next] = Outline(later, origin)); var remaining = new List<List<Vector2>>();
                    foreach (var piece in pieces) Subtract(piece, mask, remaining);
                    pieces = remaining;
                }
                if (s.soft != null) layer.Edges(outlines[index]);
                foreach (var polygon in pieces)
                {
                    if (whole) Slab(mesh, polygon, layer);
                    else if (sector) Slab(mesh, Clip(Clip(Clip(Clip(polygon, 0, -1000f, true), 0, 1000f, false), 1, -1000f, true), 1, 1000f, false), layer);
                    else
                    {
                        Slab(mesh, Clip(polygon, 0, -1000f, false), layer);
                        Slab(mesh, Clip(polygon, 0, 1000f, true), layer);
                        var middle = Clip(Clip(polygon, 0, -1000f, true), 0, 1000f, false);
                        Slab(mesh, Clip(middle, 1, -1000f, false), layer);
                        Slab(mesh, Clip(middle, 1, 1000f, true), layer);
                    }
                }
            }
        }

        // Whether two surfaces' world bounding boxes overlap.
        static bool Overlap(float[] boxes, int i, int j)
            => boxes[i * 4] <= boxes[j * 4 + 2] && boxes[j * 4] <= boxes[i * 4 + 2] && boxes[i * 4 + 1] <= boxes[j * 4 + 3] && boxes[j * 4 + 1] <= boxes[i * 4 + 3];

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

        // One surface's edges and the surfaces under it, gathered once and shared by all of that surface's pieces.
        sealed class Layer
        {
            public LandmarkSurface surface;
            public int index;
            public IList<LandmarkSurface> surfaces;
            public Vector2 origin;
            public float height;
            public Vector2[] normals; // inward unit normal of each outline edge, in local metres
            public float[] offsets, widths;
            public List<Vector2> outline;
            public float[] boxes;
            public List<Vector2> core; // inset corners when every edge survives the inset, else null
            public Scratch scratch;
            List<int> below;

            // Local edge lines of the counterclockwise outline; an edge's soft width of zero leaves it hard.
            public void Edges(List<Vector2> outline)
            {
                this.outline = outline;
                int n = outline.Count;
                normals = new Vector2[n]; offsets = new float[n]; widths = new float[n];
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = outline[i], d = outline[(i + 1) % n] - a;
                    float length = d.magnitude;
                    if (length < .001f || i >= surface.soft.Length) continue;
                    normals[i] = new Vector2(-d.y, d.x) / length; offsets[i] = Vector2.Dot(normals[i], a); widths[i] = surface.soft[i];
                }
                core = Inset();
            }

            // The core's corners are where neighbouring inset lines meet; null if a short edge vanishes in the inset, as
            // then an edge's zone is no longer the quad between it and the core.
            List<Vector2> Inset()
            {
                int n = outline.Count;
                var corners = new List<Vector2>(n);
                for (int k = 0; k < n; k++)
                {
                    int j = (k + n - 1) % n;
                    Vector2 a = normals[j], b = normals[k];
                    float det = a.x * b.y - a.y * b.x;
                    if (Mathf.Abs(det) < 1e-4f) return null;
                    float ca = offsets[j] + widths[j], cb = offsets[k] + widths[k];
                    corners.Add(new Vector2((ca * b.y - cb * a.y) / det, (a.x * cb - b.x * ca) / det));
                }
                for (int k = 0; k < n; k++)
                {
                    Vector2 p = corners[k], q = corners[(k + 1) % n], r = corners[(k + 2) % n];
                    if ((q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x) < -1e-5f) return null;
                    for (int j = 0; j < n; j++)
                        if (Vector2.Dot(normals[j], p) - offsets[j] < widths[j] - 1e-3f) return null;
                }
                return corners;
            }

            int[] edgeUnder;

            // The one tone below a soft edge's whole band, read at a few points along it, or -1 where the ground changes
            // along the edge and each piece must look for itself.
            public int EdgeUnder(int edge)
            {
                if (edgeUnder == null) { edgeUnder = new int[widths.Length]; for (int i = 0; i < edgeUnder.Length; i++) edgeUnder[i] = -2; }
                if (edgeUnder[edge] != -2) return edgeUnder[edge];
                Vector2 a = outline[edge], b = outline[(edge + 1) % outline.Count], inward = normals[edge] * (widths[edge] * .3f);
                int samples = (b - a).sqrMagnitude > 144f ? 5 : 3, tone = Under(Vector2.Lerp(a, b, .1f) + inward);
                for (int i = 1; i < samples && tone >= 0; i++)
                    if (Under(Vector2.Lerp(a, b, .1f + .8f * i / (samples - 1)) + inward) != tone) tone = -1;
                return edgeUnder[edge] = tone;
            }

            // The flat tone of the highest earlier surface under a local point, as WorldLandmarkLayout.Shade finds it.
            public int Under(Vector2 local)
            {
                if (below == null)
                {
                    below = new List<int>();
                    for (int j = index - 1; j >= 0; j--) if (Overlap(boxes, index, j)) below.Add(j);
                }
                Vector2 p = local + origin;
                foreach (int j in below)
                    if (p.x >= boxes[j * 4] && p.y >= boxes[j * 4 + 1] && p.x <= boxes[j * 4 + 2] && p.y <= boxes[j * 4 + 3] && surfaces[j].Contains(p))
                        return surfaces[j].Tone(p);
                return WorldLandmarkLayout.VergeTone;
            }
        }

        // Flat graded ground needs only a polygon fan, not thousands of eight-metre yard quads. A bay-laid slab is cut
        // into its pours first, and a soft surface into its fading edge zones; every cut reuses the build's scratch lists.
        static void Slab(Data mesh, List<Vector2> points, Layer layer)
        {
            if (points.Count < 3) return;
            var s = layer.surface;
            if (s.bays <= 0f) { Soften(mesh, points, layer); return; }
            Vector2 axis = s.area.along;
            float start = Vector2.Dot(layer.origin - s.area.center, axis) + s.area.halfLength, lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in points) { float u = Vector2.Dot(p, axis) + start; lo = Mathf.Min(lo, u); hi = Mathf.Max(hi, u); }
            List<Vector2> half = layer.scratch.lists[0], bay = layer.scratch.lists[1];
            for (int i = Mathf.FloorToInt(lo / s.bays), last = Mathf.FloorToInt(hi / s.bays); i <= last; i++)
            {
                Keep(points, axis, start - i * s.bays, half);
                Keep(half, -axis, (i + 1) * s.bays - start, bay);
                Soften(mesh, bay, layer);
            }
        }

        // Splits a piece into the full-strength core and one zone per nearby soft edge, inside which the weight is that
        // edge's distance over its width: linear across each zone, so vertex coordinates carry it exactly.
        static void Soften(Data mesh, List<Vector2> points, Layer layer)
        {
            if (points.Count < 3) return;
            if (layer.surface.soft == null) { Fan(mesh, points, layer, -1); return; }
            if (points == layer.outline && layer.core != null) { Rim(mesh, layer); return; }
            var active = layer.scratch.active; active.Clear();
            for (int k = 0; k < layer.widths.Length; k++)
            {
                if (layer.widths[k] <= 0f) continue;
                foreach (var p in points)
                    if (Vector2.Dot(layer.normals[k], p) - layer.offsets[k] < layer.widths[k]) { active.Add(k); break; }
            }
            if (active.Count == 0) { Fan(mesh, points, layer, -1); return; }
            List<Vector2> a = layer.scratch.lists[2], b = layer.scratch.lists[3], piece = points;
            foreach (int k in active)
            {
                var into = piece == a ? b : a;
                Keep(piece, layer.normals[k], -layer.offsets[k] - layer.widths[k], into); piece = into;
            }
            Fan(mesh, piece, layer, -1);
            int n = layer.widths.Length;
            foreach (int k in active)
            {
                Vector2 nk = layer.normals[k] / layer.widths[k]; float ok = layer.offsets[k] / layer.widths[k];
                Keep(points, -layer.normals[k], layer.offsets[k] + layer.widths[k], a); piece = a;
                // With an intact core only the two neighbouring edges bound a zone; otherwise every active edge may.
                foreach (int j in active)
                    if (j != k && piece.Count >= 3 && (layer.core == null || j == (k + 1) % n || j == (k + n - 1) % n))
                    {
                        var into = piece == a ? b : a;
                        Keep(piece, layer.normals[j] / layer.widths[j] - nk, ok - layer.offsets[j] / layer.widths[j], into); piece = into;
                    }
                Fan(mesh, piece, layer, k);
            }
        }

        // An uncut outline with an intact core needs no clipping: the core, then for each soft edge the quad between that
        // edge and the core, inside which the weight is the edge's distance over its width.
        static void Rim(Data mesh, Layer layer)
        {
            var outline = layer.outline; var core = layer.core; int n = outline.Count;
            Fan(mesh, core, layer, -1);
            var quad = layer.scratch.lists[4];
            for (int k = 0; k < n; k++)
                if (layer.widths[k] > 0f)
                {
                    quad.Clear(); quad.Add(outline[k]); quad.Add(outline[(k + 1) % n]); quad.Add(core[(k + 1) % n]); quad.Add(core[k]);
                    Fan(mesh, quad, layer, k);
                }
        }

        // Emits one convex piece as a fan over shared vertices with one upward normal; edge -1 is full strength, otherwise
        // each vertex fades by its distance from that edge into the ground below. That ground is read along the piece's
        // outer side, where the fade is nearly done; where it changes along that side (a weed tongue running off the yard
        // onto grass) the piece is halved, up to twice, so each part fades to the ground it actually lies on.
        static void Fan(Data mesh, List<Vector2> points, Layer layer, int edge, int splits = 2)
        {
            int count = points.Count;
            if (count < 3) return;
            var s = layer.surface;
            Vector2 centre = Vector2.zero;
            foreach (var p in points) centre += p;
            centre /= count;
            int top = s.Tone(centre + layer.origin), under = edge >= 0 ? layer.EdgeUnder(edge) : top;
            if (under < 0)
            {
                int outer = 0, next = 1; float d0 = float.MaxValue, d1 = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    float d = Vector2.Dot(layer.normals[edge], points[i]);
                    if (d < d0) { d1 = d0; next = outer; d0 = d; outer = i; }
                    else if (d < d1) { d1 = d; next = i; }
                }
                Vector2 a = points[outer], b = points[next], rim = (a + b) * .5f;
                under = layer.Under(rim + (centre - rim) * .1f);
                if (splits > 0 && (b - a).sqrMagnitude > 9f)
                {
                    Vector2 side = (b - a) * .1f;
                    if (layer.Under(a + side + (centre - a) * .1f) != under || layer.Under(b - side + (centre - b) * .1f) != under)
                    {
                        Vector2 cut = (b - a).normalized; float c = -Vector2.Dot(cut, rim);
                        var lower = new List<Vector2>(count + 1); var upper = new List<Vector2>(count + 1);
                        Keep(points, cut, c, lower); Keep(points, -cut, -c, upper);
                        Fan(mesh, lower, layer, edge, splits - 1); Fan(mesh, upper, layer, edge, splits - 1);
                        return;
                    }
                }
            }
            Vector3 P(Vector2 p) => new Vector3(p.x, layer.height + s.lift + Vector2.Dot(p + layer.origin - s.area.center, s.fall), p.y);
            Vector3 normal = Vector3.up;
            if (s.fall.x != 0f || s.fall.y != 0f)
            {
                // A drained yard slab tilts slightly; decals and strips lie level.
                Vector3 first = P(points[0]); normal = Vector3.zero;
                for (int i = 1; i < count - 1; i++) normal += Vector3.Cross(P(points[i + 1]) - first, P(points[i]) - first);
                if (normal.sqrMagnitude < 1e-10f) return; // a sliver left by clipping
                normal = normal.normalized;
            }
            int at = mesh.vertices.Count;
            foreach (var p in points)
            {
                mesh.vertices.Add(P(p)); mesh.normals.Add(normal);
                mesh.uvs.Add(PaletteUv(top, under, edge < 0 ? 1f : (Vector2.Dot(layer.normals[edge], p) - layer.offsets[edge]) / layer.widths[edge]));
            }
            var triangles = mesh.triangles[SurfaceSlot];
            for (int i = 1; i < count - 1; i++) { triangles.Add(at); triangles.Add(at + i + 1); triangles.Add(at + i); }
        }

        // Writes the part of a convex polygon where n.p + c >= 0 into output.
        static void Keep(List<Vector2> input, Vector2 n, float c, List<Vector2> output)
        {
            output.Clear();
            if (input.Count == 0) return;
            Vector2 a = input[input.Count - 1]; float da = Vector2.Dot(n, a) + c;
            foreach (var b in input)
            {
                float db = Vector2.Dot(n, b) + c;
                if ((da >= 0f) != (db >= 0f)) output.Add(Vector2.Lerp(a, b, da / (da - db)));
                if (db >= 0f) output.Add(b);
                a = b; da = db;
            }
        }

        // Lists reused by every cut in one build, so soft edges and pours do not allocate per piece.
        sealed class Scratch
        {
            public readonly List<Vector2>[] lists = { new List<Vector2>(), new List<Vector2>(), new List<Vector2>(), new List<Vector2>(), new List<Vector2>() };
            public readonly List<int> active = new List<int>();
        }

        // Texture coordinates of a tone pair's run at weight t (0 the tone below, 1 the surface's own).
        public static Vector2 PaletteUv(int top, int under, float t)
        {
            int run = top * WorldLandmarkLayout.ToneCount + under;
            return new Vector2(((run % Runs) * 8 + .5f + 7f * Mathf.Clamp01(t)) / PaletteWidth, (run / Runs + .5f) / PaletteHeight);
        }

        // sRGB texels for the surface palette: each run eases from the tone below to the top tone over eight texels.
        public static Color32[] PalettePixels()
        {
            var pixels = new Color32[PaletteWidth * PaletteHeight];
            for (int top = 0; top < WorldLandmarkLayout.ToneCount; top++)
                for (int under = 0; under < WorldLandmarkLayout.ToneCount; under++)
                {
                    int run = top * WorldLandmarkLayout.ToneCount + under;
                    Color a = WorldLandmarkLayout.ToneColor(under), b = WorldLandmarkLayout.ToneColor(top);
                    for (int k = 0; k < 8; k++)
                    {
                        float w = WorldLandmarkLayout.Profile(k / 7f);
                        pixels[(run / Runs) * PaletteWidth + (run % Runs) * 8 + k] = new Color32((byte)Mathf.RoundToInt((a.r + (b.r - a.r) * w) * 255f),
                            (byte)Mathf.RoundToInt((a.g + (b.g - a.g) * w) * 255f), (byte)Mathf.RoundToInt((a.b + (b.b - a.b) * w) * 255f), 255);
                    }
                }
            return pixels;
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
            int roof = b.roof > 0 ? 16 + b.roof : 6;
            if (b.shape != 0) { mesh.Face(roof, P(0f, wall, 0f), vz, vx); return; }
            mesh.Triangle(5, P(-x, wall, z), P(x, wall, z), P(0f, h, z));
            mesh.Triangle(5, P(x, wall, -z), P(-x, wall, -z), P(0f, h, -z));
            mesh.Triangle(roof, P(-x, wall, -z), P(-x, wall, z), P(0f, h, z));
            mesh.Triangle(roof, P(-x, wall, -z), P(0f, h, z), P(0f, h, -z));
            mesh.Triangle(roof, P(0f, h, -z), P(0f, h, z), P(x, wall, z));
            mesh.Triangle(roof, P(0f, h, -z), P(x, wall, z), P(x, wall, -z));
        }

        // Shared lit colours keep the material count bounded; surfaces match FeatureSurfaceAt exactly.
        public static Color Color(int material)
        {
            if (!IsBuilding(material)) return WorldLandmarkLayout.SurfaceColor((LandmarkSurfaceKind)material);
            if (material >= 17) return material == 17 ? new Color(0.25f, 0.24f, 0.22f) : material == 18 ? new Color(0.38f, 0.25f, 0.18f) : new Color(0.46f, 0.48f, 0.48f);
            return material == 5 ? new Color(0.48f, 0.49f, 0.46f) : material == 6 ? new Color(0.31f, 0.34f, 0.33f)
                : material == 7 ? new Color(0.63f, 0.65f, 0.64f) : new Color(0.50f, 0.28f, 0.20f);
        }

        // Walls, roofs, tanks, brick and the three aged roof tints; these proxies fade to the imported models.
        public static bool IsBuilding(int material) => material >= 5 && material <= 8 || material >= 17;

        // Photo material that replaces a metal roof of this age on the imported models, near and instanced alike.
        public static string RoofMaterial(int roof) => roof == 1 ? "RoofBarn" : roof == 2 ? "RustySteel" : roof == 3 ? "Galvanized" : "RoofMetal";
    }
}

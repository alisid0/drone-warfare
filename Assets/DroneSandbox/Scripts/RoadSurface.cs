using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Opaque road ribbons use pre-baked wear textures: no decals, per-frame generation or extra terrain sampler.
    public sealed class RoadSurface : MonoBehaviour
    {
        public const int RendererReserve = 7;
        public static int Renderers { get; private set; }
        public static int Triangles { get; private set; }
        public static double BuildMilliseconds { get; private set; }
        readonly List<Mesh> owned = new List<Mesh>();
        Renderer puddles;
        static bool raining;
        static float lastRain = -1000f;
        float nextWetCheck;
        public static readonly List<RoadGate> Entrances = new List<RoadGate>();
        static readonly float[] Signs = { -1f, 1f };

        // A single material per surface class keeps draw cost independent of individual repairs and markings.
        sealed class Ribbon
        {
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();

            // Every corner samples the graded terrain, with consistent upward winding even on reversed road lines.
            public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, float lift = .18f)
            {
                int k = vertices.Count;
                Vertex(a, lift); Vertex(b, lift); Vertex(c, lift); Vertex(d, lift);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                bool up = Vector3.Cross(vertices[k + 1] - vertices[k], vertices[k + 2] - vertices[k]).y > 0f;
                triangles.Add(k); triangles.Add(k + (up ? 1 : 2)); triangles.Add(k + (up ? 2 : 1));
                triangles.Add(k); triangles.Add(k + (up ? 2 : 3)); triangles.Add(k + (up ? 3 : 2));
            }

            // Append one terrain sample without allocating a temporary corner array for every two-metre strip.
            void Vertex(Vector2 p, float lift)
            {
                vertices.Add(At(p, lift)); normals.Add(Ground.NormalAt(new Vector3(p.x, 0f, p.y)));
            }
        }

        // Called after Ground and bridge placement; all geometry and textures are owned by this sector or Resources.
        public static void Build(Transform parent)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var go = new GameObject("Road surfaces"); go.transform.SetParent(parent, false);
            var owner = go.AddComponent<RoadSurface>();
            var strips = new[] { new Ribbon(), new Ribbon(), new Ribbon(), new Ribbon(), new Ribbon(), new Ribbon(), new Ribbon() };
            Renderers = Triangles = 0; Entrances.Clear();
            var network = MapLayout.Roads;
            foreach (var s in network.segments)
            {
                Vector2 a = new Vector2(s.ax, s.az), b = new Vector2(s.bx, s.bz);
                float length = (b - a).magnitude;
                if (length < .01f) continue;
                Vector2 direction = (b - a) / length, side = new Vector2(-direction.y, direction.x);
                int steps = Math.Max(1, (int)Math.Ceiling(length / 2f));
                for (int i = 0; i < steps; i++)
                {
                    float t0 = i / (float)steps, t1 = (i + 1f) / steps;
                    Vector2 p = a + (b - a) * t0, q = a + (b - a) * t1, mid = (p + q) * .5f;
                    if (Math.Max(Math.Abs(mid.x), Math.Abs(mid.y)) > MapLayout.Edge - 1f || !Dry(mid)) continue;
                    float w0 = s.walked + length * t0, w1 = s.walked + length * t1;
                    RoadClass kind = (RoadClass)s.kind;
                    if (kind == RoadClass.Track)
                    {
                        foreach (float sign in Signs)
                        {
                            float r0 = .25f + .05f * (float)Math.Sin(w0 * .1308997f), r1 = .25f + .05f * (float)Math.Sin(w1 * .1308997f);
                            Band(strips[2], p, q, side, sign * .85f - r0, sign * .85f + r0, sign * .85f - r1, sign * .85f + r1, w0, w1, 1.5f);
                        }
                        continue;
                    }
                    bool junction = network.At(mid.x, mid.y).junction > .05f;
                    int material = junction ? 3 : kind == RoadClass.Highway ? 0 : 1;
                    float half = RoadAppearance.HalfWidth(kind);
                    float left0 = Splay(p, -side, half), left1 = Splay(q, -side, half);
                    float right0 = Splay(p, side, half), right1 = Splay(q, side, half);
                    Band(strips[material], p, q, side, -left0, right0, -left1, right1, w0, w1, half);
                }
            }
            Gateways(strips[4], strips[2]);
            Ditches(strips[5]);
            Puddles(strips[6]);
            string[] names = { "RoadHighway", "RoadLane", "RoadTrack", "RoadJunction", "RoadGravel", "RoadDitch", "RoadPuddle" };
            for (int i = 0; i < strips.Length; i++) owner.Finish(strips[i], names[i], go.transform);
            BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
        }

        // Atmosphere calls this hook from its weather switch; shallow pools linger for three minutes after rain.
        public static void SetRain(bool wet)
        {
            raining = wet;
            if (wet) lastRain = Time.time;
        }

        // Weather changes only toggle a renderer once per second; they never rebuild terrain or streaming textures.
        void Update()
        {
            if (Time.time < nextWetCheck) return;
            nextWetCheck = Time.time + 1f;
            if (raining) lastRain = Time.time;
            if (puddles != null) puddles.enabled = raining || Time.time - lastRain < 180f;
        }

        // Only gently graded local depressions in actual wheel ruts can collect small pools, capped at 48 pairs.
        static void Puddles(Ribbon mesh)
        {
            int count = 0;
            foreach (var s in MapLayout.Roads.segments)
            {
                if (s.kind != (int)RoadClass.Track || count >= 48) continue;
                Vector2 a = new Vector2(s.ax, s.az), b = new Vector2(s.bx, s.bz), dir = (b - a).normalized;
                float length = (b - a).magnitude;
                float first = (float)Math.Ceiling(s.walked / 18f) * 18f;
                for (float v = first; v < s.walked + length && count < 48; v += 18f)
                {
                    Vector2 p = a + dir * (v - s.walked), left = p - dir * 9f, right = p + dir * 9f;
                    float low = At(p, 0f).y, ha = At(left, 0f).y, hb = At(right, 0f).y;
                    if (Math.Min(ha, hb) - low < .025f || Math.Abs(ha - hb) > .6f || !Dry(p)) continue;
                    Vector2 side = new Vector2(-dir.y, dir.x);
                    foreach (float sign in Signs)
                    {
                        Vector2 centre = p + side * (sign * .85f);
                        mesh.Quad(centre - dir * 1.1f - side * .18f, centre + dir * .85f - side * .16f,
                            centre + dir * 1.1f + side * .18f, centre - dir * .85f + side * .16f,
                            Vector2.zero, Vector2.up, Vector2.one, Vector2.right, .20f);
                    }
                    count++;
                }
            }
        }

        // Subpixel cracks and line wear come from UVs in actual route metres, never from mesh segment number.
        static void Band(Ribbon mesh, Vector2 a, Vector2 b, Vector2 side, float lo0, float hi0, float lo1, float hi1, float v0, float v1, float half)
        {
            mesh.Quad(a + side * lo0, b + side * lo1, b + side * hi1, a + side * hi0,
                new Vector2(.5f + lo0 / (half * 2f), v0 / RoadAppearance.Repeat), new Vector2(.5f + lo1 / (half * 2f), v1 / RoadAppearance.Repeat),
                new Vector2(.5f + hi1 / (half * 2f), v1 / RoadAppearance.Repeat), new Vector2(.5f + hi0 / (half * 2f), v0 / RoadAppearance.Repeat));
        }

        // Only the inside corner of two distinct routes grows; ordinary bends keep the class width.
        static float Splay(Vector2 p, Vector2 side, float half)
        {
            Vector2 q = p + side * half;
            var sample = MapLayout.Roads.At(q.x, q.y);
            return half + (sample.junction > 0f ? Math.Min(1f, Math.Max(0f, -sample.edge)) : 0f);
        }

        // Road water crossings only remain visible where an existing bridge actually supplies a deck.
        static bool Dry(Vector2 p)
        {
            if (MapLayout.LakeDistance(p.x, p.y) > 1f && (!MapLayout.HasRiver || MapLayout.RiverField.Distance(p.x, p.y) > MapLayout.RiverWidthAt(p.x, p.y) + .5f)) return true;
            foreach (var bridge in MapLayout.Bridges) if ((bridge - p).sqrMagnitude < 26f * 26f) return true;
            return false;
        }

        // The bridge's measured deck height blends down to the graded approach over its last six metres.
        public static Vector3 At(Vector2 p, float lift)
        {
            float ground = Ground.HeightAt(new Vector3(p.x, 0f, p.y));
            foreach (var bridge in MapLayout.Bridges)
            {
                Vector2 d = p - bridge; if (d.sqrMagnitude > 28f * 28f) continue;
                MapLayout.RoadField.Query(bridge.x, bridge.y, out _, out int segment);
                Vector2 forward = MapLayout.RoadField.Direction(segment), side = new Vector2(-forward.y, forward.x);
                if (Math.Abs(Vector2.Dot(d, side)) > MapLayout.RoadWidth(segment) + 1f) continue;
                Vector2 a = bridge - forward * 11f, b = bridge + forward * 11f;
                float deck = Math.Max(Ground.HeightAt(new Vector3(a.x, 0f, a.y)), Ground.HeightAt(new Vector3(b.x, 0f, b.y))) + .05f;
                float fade = FarDetailData.Clamp((26f - Math.Abs(Vector2.Dot(d, forward))) / 6f, 0f, 1f);
                ground = Math.Max(ground, ground + (deck - ground) * fade);
            }
            return new Vector3(p.x, ground + lift, p.y);
        }

        // Physical hedge openings supply short worn entrances; one in three gets a small gravel lay-by.
        static void Gateways(Ribbon gravel, Ribbon track)
        {
            foreach (var edge in MapLayout.Boundaries)
            {
                if (edge.kind != FieldBoundaryKind.Hedge) continue;
                Vector2 gate = edge.hasGate ? edge.gate : FieldAppearance.Gate(edge.a, edge.b);
                var road = MapLayout.Roads.At(gate.x, gate.y);
                if (!RoadGate.Try(gate, MapLayout.Roads, out var entry) || !Dry(gate)) continue;
                bool duplicate = false;
                foreach (var other in Entrances) if ((gate - other.gate).sqrMagnitude < 1f) duplicate = true;
                if (duplicate) continue;
                Vector2 into = entry.into, along = entry.along;
                Entrances.Add(entry);
                int n = Math.Max(1, (int)Math.Ceiling((gate - entry.road).magnitude / 2f));
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = Vector2.Lerp(entry.road, gate, i / (float)n), b = Vector2.Lerp(entry.road, gate, (i + 1f) / n);
                    Band(gravel, a, b, along, -1.8f, 1.8f, -1.8f, 1.8f, i * 2f, (i + 1) * 2f, 1.8f);
                }
                for (int i = 0; i < 6; i++) foreach (float sign in Signs)
                {
                    float fade0 = Math.Min(1f, (6f - i) / 3f), fade1 = Math.Min(1f, (5f - i) / 3f);
                    Band(track, gate + into * (i * 2f), gate + into * ((i + 1) * 2f), along,
                        sign * .85f - .25f * fade0, sign * .85f + .25f * fade0, sign * .85f - .25f * fade1, sign * .85f + .25f * fade1, i * 2f, (i + 1) * 2f, 1.5f);
                }
                if (!entry.layby) continue;
                Vector2 centre = entry.road + into * 1.25f;
                Band(gravel, centre - road.direction * 7f, centre + road.direction * 7f, into, -.7f, 1.1f, -.7f, 1.1f, 0f, 14f, 1.8f);
            }
        }

        // Drains follow farmland roads only, with gaps at entrances, junctions, bridges and settlements.
        static void Ditches(Ribbon mesh)
        {
            if (MapLayout.DetailPlan == null) return;
            Vector2 origin = MapLayout.Plan.world.center;
            foreach (var s in MapLayout.Roads.segments)
            {
                if (s.kind == (int)RoadClass.Track) continue;
                Vector2 a = new Vector2(s.ax, s.az), b = new Vector2(s.bx, s.bz), mid = (a + b) * .5f;
                if (!Dry(mid) || MapLayout.NearVillage(mid, 5f) || MapLayout.Roads.At(mid.x, mid.y).junction > 0f) continue;
                if (MapLayout.DetailPlan.CoverAt(mid.x + origin.x, mid.y + origin.y) != Cover.Farmland) continue;
                bool entrance = false;
                foreach (var gate in Entrances) if ((gate.road - mid).sqrMagnitude < 10f * 10f) entrance = true;
                if (entrance) continue;
                Vector2 side = new Vector2(-(b - a).y, (b - a).x).normalized;
                float verge = 1.4f + .6f * RoadAppearance.Value(s.route, (int)Math.Floor(s.walked / 96f));
                foreach (float sign in Signs)
                {
                    float centre = sign * (s.widthA + verge - .25f);
                    Band(mesh, a, b, side, centre - .225f, centre + .225f, centre - .225f, centre + .225f, s.walked, s.walked + (b - a).magnitude, 4f);
                }
            }
        }

        // No shadows or colliders are needed on ground paint; materials and textures remain shared Resources assets.
        void Finish(Ribbon source, string name, Transform parent)
        {
            if (source.triangles.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(source.vertices); mesh.SetNormals(source.normals); mesh.SetUVs(0, source.uv); mesh.SetTriangles(source.triangles, 0); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            owned.Add(mesh); Triangles += source.triangles.Count / 3;
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("Materials/" + name);
            renderer.shadowCastingMode = ShadowCastingMode.Off; Renderers++;
            if (name == "RoadPuddle") { puddles = renderer; renderer.enabled = raining || Time.time - lastRain < 180f; }
        }

        // Destroying a sector releases only its generated meshes, leaving the shared road atlas cached by Unity.
        void OnDestroy()
        {
            foreach (var mesh in owned) Art.Discard(mesh);
            owned.Clear();
        }
    }
}

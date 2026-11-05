using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // A sector's special features, from its plan (see SectorGenerator):
    // - the buildings and props the generator placed (station, wagons, warehouses, a factory and its chimney,
    //   silos, a water tower, a hangar, huts, log stacks), from Tools/Blender/make_sectors.py, or plain blocks of
    //   the right size until those models exist;
    // - railways: one mesh per line laid along the curve, gravel ballast, sleepers every 65 cm, two rails;
    // - a power line of pylons with sagging wires;
    // - an airstrip of asphalt with its markings;
    // - orchards in rows, logging clearings full of stumps, dragon's teeth along the fortified line, boulders in
    //   the quarry.
    public static partial class Features
    {
        public static void Build(Transform parent)
        {
            var plan = MapLayout.Plan;
            var root = new GameObject("Features").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(plan.worldSeed + 101); // its own numbers, so the rest of the map stays put
            foreach (var site in plan.sites) PlaceSite(root, site);
            BuildIdentity(root);
            foreach (var line in MapLayout.RailLines) Railway(root, line);
            if (plan.powerLine != null) PowerLine(root, plan.powerLine);
            if (plan.Has(SectorFeature.Airstrip)) Airstrip(root, plan);
            foreach (var orchard in plan.orchards) Orchard(root, orchard, rng);
            foreach (var clearing in plan.clearings) Clearing(root, clearing, rng);
            if (plan.fortLine != null) DragonTeeth(root, plan.fortLine);
            if (plan.Has(SectorFeature.Quarry)) QuarryRocks(root, plan, rng);
        }

        // ---- Placed models ----

        // Rough sizes (width, height, length) for stand-in blocks while a model is missing.
        static readonly Dictionary<string, (Vector3 size, Color colour)> StandIns = new Dictionary<string, (Vector3, Color)>
        {
            { "Scenery_Station", (new Vector3(8f, 7f, 14f), new Color(0.55f, 0.32f, 0.24f)) },
            { "Scenery_RailWagon", (new Vector3(2.9f, 3.9f, 14f), new Color(0.38f, 0.22f, 0.16f)) },
            { "Scenery_RailTankWagon", (new Vector3(2.9f, 3.8f, 12f), new Color(0.15f, 0.15f, 0.15f)) },
            { "Scenery_Warehouse", (new Vector3(18f, 9f, 30f), new Color(0.6f, 0.62f, 0.63f)) },
            { "Scenery_Factory", (new Vector3(16f, 10f, 24f), new Color(0.55f, 0.3f, 0.22f)) },
            { "Scenery_Chimney", (new Vector3(2.6f, 28f, 2.6f), new Color(0.5f, 0.28f, 0.2f)) },
            { "Scenery_Silo", (new Vector3(9f, 18f, 9f), new Color(0.7f, 0.72f, 0.72f)) },
            { "Scenery_WaterTower", (new Vector3(6f, 16f, 6f), new Color(0.5f, 0.52f, 0.5f)) },
            { "Scenery_Hut", (new Vector3(3f, 2.8f, 4f), new Color(0.35f, 0.25f, 0.16f)) },
            { "Scenery_LogPile", (new Vector3(2.2f, 1.4f, 6f), new Color(0.42f, 0.3f, 0.2f)) },
        };

        static void PlaceSite(Transform root, Site site)
        {
            System.Action<Transform> fallback = t =>
            {
                if (!StandIns.TryGetValue(site.model, out var s)) s = (Vector3.one * 3f, new Color(0.5f, 0.5f, 0.48f));
                Art.Part(PrimitiveType.Cube, t, new Vector3(0f, s.size.y / 2f, 0f), s.size, s.colour);
            };
            World.Place(site.model, new Vector3(site.position.x, 0f, site.position.y), site.yaw, site.scale, 0.05f, root, fallback);
        }

        // ---- Railways ----

        static Material Pbr(string name, Color fallback)
        {
            var m = Resources.Load<Material>("Materials/PBR/" + name);
            return m != null ? m : Art.Shared(fallback);
        }

        // Extends real-gauge track to the terrain boundary, combining its sleepers and rails into one mesh.
        static void Railway(Transform root, List<Vector2> line)
        {
            var mesh = new MeshBuilder(3); // ballast, sleepers, rails
            float limit = Ground.Size / 2f - 2f;
            const float ballastTop = 0.3f, gauge = 1.435f / 2f + 0.035f;
            float travelled = 0f, nextSleeper = 0f;
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                if (Mathf.Abs(a.x) > limit || Mathf.Abs(a.y) > limit || Mathf.Abs(b.x) > limit || Mathf.Abs(b.y) > limit)
                {
                    travelled += Vector2.Distance(a, b);
                    nextSleeper = travelled; // do not pile skipped sleepers onto the first point inside the map
                    continue;
                }
                Vector3 pa = Ground.OnGround(new Vector3(a.x, 0f, a.y)), pb = Ground.OnGround(new Vector3(b.x, 0f, b.y));
                Vector3 forward = (pb - pa).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 rightB = right;
                if (i + 2 < line.Count)
                {
                    var c = line[i + 2];
                    Vector3 pc = Ground.OnGround(new Vector3(c.x, 0f, c.y));
                    rightB = Vector3.Cross(Vector3.up, (pc - pb).normalized).normalized;
                }
                float len = Vector3.Distance(pa, pb);
                // Ballast: a flat top with sloping shoulders down to the ground.
                float[] across = { -2.3f, -1.55f, 1.55f, 2.3f }, up = { -0.05f, ballastTop, ballastTop, -0.05f };
                for (int k = 0; k < 3; k++)
                    mesh.Quad(0, pa + right * across[k] + Vector3.up * up[k], pa + right * across[k + 1] + Vector3.up * up[k + 1],
                        pb + rightB * across[k + 1] + Vector3.up * up[k + 1], pb + rightB * across[k] + Vector3.up * up[k],
                        new Vector2(across[k], travelled), new Vector2(across[k + 1], travelled + len));
                // Two rails along the top.
                foreach (float side in new[] { -gauge, gauge })
                {
                    Vector3 lift = Vector3.up * (ballastTop + 0.16f);
                    mesh.Bar(2, pa + right * side + lift, pb + rightB * side + lift, right, 0.07f, 0.15f);
                }
                // Sleepers every 65 cm.
                while (nextSleeper < travelled + len)
                {
                    float t = (nextSleeper - travelled) / len;
                    Vector3 at = Vector3.Lerp(pa, pb, t) + Vector3.up * (ballastTop + 0.08f);
                    Vector3 r = Vector3.Lerp(right, rightB, t).normalized;
                    mesh.Box(1, at, r, Vector3.up, Vector3.Cross(r, Vector3.up), new Vector3(2.6f, 0.16f, 0.25f));
                    nextSleeper += 0.65f;
                }
                travelled += len;
            }
            mesh.Finish("Railway", root, new[] { Pbr("Gravel", new Color(0.45f, 0.43f, 0.4f)), Pbr("Wood", new Color(0.3f, 0.22f, 0.15f)),
                Pbr("SteelDark", new Color(0.2f, 0.2f, 0.2f)) });
        }

        // ---- Power line ----

        // Where the six wires hang from a pylon (the clamps under the insulators at the tips of its three pairs of
        // cross-arms; see make_sectors.py), in its own space: the arms run across the line.
        static readonly Vector3[] Arms =
        {
            new Vector3(-5f, 13.5f, 0f), new Vector3(5f, 13.5f, 0f), new Vector3(-5.8f, 17f, 0f), new Vector3(5.8f, 17f, 0f),
            new Vector3(-4.6f, 20.5f, 0f), new Vector3(4.6f, 20.5f, 0f),
        };

        // Places real-size pylons and wire spans along the sector's longer power corridor.
        static void PowerLine(Transform root, Vector2[] line)
        {
            var group = new GameObject("Power Line").transform;
            group.SetParent(root, false);
            Vector2 a = line[0], b = line[line.Length - 1], dir = (b - a).normalized;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            var pylons = new List<Transform>();
            for (float t = 0f; t <= Vector2.Distance(a, b); t += 55f)
            {
                Vector2 p = a + dir * t;
                if (Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge) continue;
                if (MapLayout.LakeDistance(p.x, p.y) < 4f || MapLayout.RoadField.Distance(p.x, p.y) < 5f) continue;
                if (MapLayout.HasRiver && MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverHalfWidth + 4f) continue;
                if (MapLayout.NearFlatSite(p, 2f)) continue;
                var pylon = World.Place("Scenery_Pylon", new Vector3(p.x, 0f, p.y), yaw, 1f, 0.1f, group, t2 =>
                {
                    foreach (float x in new[] { -1.4f, 1.4f })
                        Art.Part(PrimitiveType.Cube, t2, new Vector3(x, 12f, 0f), new Vector3(0.3f, 24f, 0.3f), new Color(0.45f, 0.47f, 0.48f));
                    Art.Part(PrimitiveType.Cube, t2, new Vector3(0f, 19.5f, 0f), new Vector3(11f, 0.3f, 0.3f), new Color(0.45f, 0.47f, 0.48f));
                });
                pylons.Add(pylon.transform);
            }
            // Wires sag between neighbouring pylons, about 1.5 m at the middle of a span.
            var wire = new Color(0.15f, 0.15f, 0.15f);
            for (int i = 0; i < pylons.Count - 1; i++)
            {
                if (Vector3.Distance(pylons[i].position, pylons[i + 1].position) > 60f) continue; // a pylon was skipped
                foreach (var arm in Arms)
                {
                    Vector3 from = pylons[i].TransformPoint(arm), to = pylons[i + 1].TransformPoint(arm);
                    const int pieces = 6;
                    for (int k = 0; k < pieces; k++)
                    {
                        float t0 = k / (float)pieces, t1 = (k + 1) / (float)pieces;
                        Vector3 p0 = Vector3.Lerp(from, to, t0) + Vector3.down * 1.5f * 4f * t0 * (1f - t0);
                        Vector3 p1 = Vector3.Lerp(from, to, t1) + Vector3.down * 1.5f * 4f * t1 * (1f - t1);
                        var piece = Art.Part(PrimitiveType.Cylinder, group, (p0 + p1) / 2f, new Vector3(0.05f, Vector3.Distance(p0, p1) / 2f, 0.05f), wire);
                        piece.transform.up = (p1 - p0).normalized;
                        piece.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    }
                }
            }
        }

        // ---- Airstrip ----

        // Uses the planned real-metre length and width; terrain sampling keeps paint on the levelled runway.
        static void Airstrip(Transform root, SectorPlan plan)
        {
            var mesh = new MeshBuilder(2); // asphalt, paint
            Vector2 c = plan.airstripCenter, along = plan.airstripAlong, across = new Vector2(-along.y, along.x);
            float half = plan.airstripLength / 2f;
            float width = plan.airstripWidth / 2f;
            Vector3 P(float u, float v, float lift) => Ground.OnGround(new Vector3(c.x + along.x * u + across.x * v, 0f, c.y + along.y * u + across.y * v)) + Vector3.up * lift;
            for (float u = -half; u < half - 0.1f; u += 10f)
                mesh.Quad(0, P(u, -width, 0.08f), P(u, width, 0.08f), P(Mathf.Min(u + 10f, half), width, 0.08f), P(Mathf.Min(u + 10f, half), -width, 0.08f),
                    new Vector2(-width, u), new Vector2(width, u + 10f));
            // Centre line dashes, edge lines and the threshold stripes at both ends.
            for (float u = -half + 30f; u < half - 30f; u += 24f)
                mesh.Quad(1, P(u, -0.3f, 0.10f), P(u, 0.3f, 0.10f), P(u + 12f, 0.3f, 0.10f), P(u + 12f, -0.3f, 0.10f), Vector2.zero, Vector2.one);
            foreach (float v in new[] { -width + 1f, width - 1.4f })
                mesh.Quad(1, P(-half + 2f, v, 0.10f), P(-half + 2f, v + 0.4f, 0.10f), P(half - 2f, v + 0.4f, 0.10f), P(half - 2f, v, 0.10f),
                    Vector2.zero, Vector2.one);
            foreach (float end in new[] { -1f, 1f })
                for (float v = -width + 3f; v <= width - 4f; v += 3f)
                {
                    float u0 = end * (half - 6f), u1 = end * (half - 24f);
                    mesh.Quad(1, P(Mathf.Min(u0, u1), v, 0.10f), P(Mathf.Min(u0, u1), v + 1.6f, 0.10f), P(Mathf.Max(u0, u1), v + 1.6f, 0.10f),
                        P(Mathf.Max(u0, u1), v, 0.10f), Vector2.zero, Vector2.one);
                }
            mesh.Finish("Airstrip", root, new[] { Pbr("Asphalt", new Color(0.15f, 0.15f, 0.15f)), Art.Shared(new Color(0.85f, 0.85f, 0.82f)) });
        }

        // ---- Orchards, logging, fortifications, the quarry ----

        static void Orchard(Transform root, FieldPlot plot, System.Random rng)
        {
            var group = new GameObject("Orchard").transform;
            group.SetParent(root, false);
            for (float u = -plot.halfLength + 2.5f; u <= plot.halfLength - 2.5f; u += 5f)
                for (float v = -plot.halfWidth + 3f; v <= plot.halfWidth - 3f; v += 6f)
                {
                    Vector2 p = plot.Point(u, v) + new Vector2(Range(rng, -0.4f, 0.4f), Range(rng, -0.4f, 0.4f));
                    World.Place(rng.NextDouble() < 0.8 ? "Scenery_Tree" : "Scenery_Tree_B", new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f),
                        Range(rng, 0.36f, 0.48f), 0.1f, group);
                }
        }

        // Spreads a bounded set of real-size stumps over the enlarged logging clearing.
        static void Clearing(Transform root, Vector2 c, System.Random rng)
        {
            var group = new GameObject("Logging Clearing").transform;
            group.SetParent(root, false);
            for (int k = 0; k < 56; k++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f), r = Scale.D(Range(rng, 6f, 22f));
                Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge
                    || MapLayout.RoadField.Distance(p.x, p.y) < 3f) continue;
                Scans.Rock(Scans.Stumps, rng.NextDouble(), new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), Range(rng, 0.7f, 1f), group,
                    "Scenery_Rock_B", 0.35f, 0.1f);
            }
        }

        // Repeats real-size fortifications along the line, leaving road crossings and water open.
        static void DragonTeeth(Transform root, Vector2[] fortLine)
        {
            var group = new GameObject("Fortifications").transform;
            group.SetParent(root, false);
            var line = MapLayout.Smooth(fortLine, 2f);
            float walked = 0f, next = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                walked += Vector2.Distance(line[i - 1], line[i]);
                if (walked < next) continue;
                next = walked + 10.5f;
                Vector2 p = line[i], dir = (line[i] - line[i - 1]).normalized;
                if (Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge || MapLayout.LakeDistance(p.x, p.y) < 3f) continue;
                if (MapLayout.RoadField.Distance(p.x, p.y) < 8f || MapLayout.NearFlatSite(p, 4f)) continue; // gaps for the roads
                if (MapLayout.HasRiver && MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverHalfWidth + 3f) continue;
                World.Place("Scenery_DragonTeeth", new Vector3(p.x, 0f, p.y), Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 1f, 0.15f, group, t =>
                {
                    for (int k = -4; k <= 4; k += 2)
                        Art.Part(PrimitiveType.Cube, t, new Vector3(0f, 0.45f, k), Vector3.one * 0.9f, new Color(0.6f, 0.6f, 0.57f), new Vector3(45f, 45f, 0f));
                });
            }
        }

        // Dresses the quarry at its planned radius without scaling individual boulders.
        static void QuarryRocks(Transform root, SectorPlan plan, System.Random rng)
        {
            var group = new GameObject("Quarry").transform;
            group.SetParent(root, false);
            for (int k = 0; k < 64; k++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f), r = Range(rng, 0f, plan.quarryRadius * 0.85f);
                Vector2 p = plan.quarry + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge) continue;
                float scale = Range(rng, 0.5f, 1.4f);
                Scans.Rock(Scans.Boulders, rng.NextDouble(), new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), 0.5f + 0.4f * scale, group,
                    "Scenery_Rock", scale, 0.3f * scale);
            }
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        // ---- A small mesh builder: quads, boxes and bars into sub-meshes ----

        class MeshBuilder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int>[] triangles;

            public MeshBuilder(int subMeshes)
            {
                triangles = new List<int>[subMeshes];
                for (int i = 0; i < subMeshes; i++) triangles[i] = new List<int>();
            }

            // A quad a-b-c-d (clockwise seen from the side it faces), UVs from uv0 to uv1.
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv0, Vector2 uv1)
            {
                int start = vertices.Count;
                Vector3 n = Vector3.Cross(b - a, d - a).normalized;
                if (Vector3.Dot(n, Vector3.up) < -0.1f) n = -n;
                vertices.AddRange(new[] { a, b, c, d });
                normals.AddRange(new[] { n, n, n, n });
                uvs.AddRange(new[] { uv0, new Vector2(uv1.x, uv0.y), uv1, new Vector2(uv0.x, uv1.y) });
                // Two triangles, wound so their front (along Cross(b - a, c - a) in Unity) faces up, or outwards.
                Vector3 facing = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(facing, n) > 0f) triangles[sub].AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                else triangles[sub].AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            // A box centred at c, with axes right/up/forward and size along them.
            public void Box(int sub, Vector3 c, Vector3 right, Vector3 up, Vector3 forward, Vector3 size)
            {
                Vector3 x = right * size.x / 2f, y = up * size.y / 2f, z = forward * size.z / 2f;
                Face(sub, c + y, z, x, size.z, size.x);
                Face(sub, c - y, x, z, size.x, size.z);
                Face(sub, c + x, y, z, size.y, size.z);
                Face(sub, c - x, z, y, size.z, size.y);
                Face(sub, c + z, x, y, size.x, size.y);
                Face(sub, c - z, y, x, size.y, size.x);
            }

            // A bar of rectangular section from a to b (a rail), `right` across it.
            public void Bar(int sub, Vector3 a, Vector3 b, Vector3 right, float width, float height)
            {
                Vector3 forward = (b - a).normalized, up = Vector3.Cross(forward, right).normalized;
                Box(sub, (a + b) / 2f, right, up, forward, new Vector3(width, height, Vector3.Distance(a, b) + 0.02f));
            }

            // One face of a box: centre, two half-axes (u then v), facing along Cross(u, v).
            void Face(int sub, Vector3 centre, Vector3 u, Vector3 v, float su, float sv)
            {
                int start = vertices.Count;
                Vector3 n = Vector3.Cross(u, v).normalized;
                vertices.AddRange(new[] { centre - u - v, centre + u - v, centre + u + v, centre - u + v });
                normals.AddRange(new[] { n, n, n, n });
                uvs.AddRange(new[] { Vector2.zero, new Vector2(su, 0f), new Vector2(su, sv), new Vector2(0f, sv) });
                triangles[sub].AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            public void Finish(string name, Transform parent, Material[] materials)
            {
                if (vertices.Count == 0) return;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            }
        }
    }
}

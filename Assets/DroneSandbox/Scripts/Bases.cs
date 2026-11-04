using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Lays out your drone base and the enemy command post (after Docs/Concept/base.png and hq.png):
    // a ring of HESCO barriers with a gate wherever a road comes in, and the buildings and kit inside.
    // Layouts are in each compound's own frame: +z points out through its main gate, +x to the right.
    public static class Bases
    {
        const float HescoUnit = 1.12f;

        public static void BuildPlayerBase(Transform parent)
        {
            var c = MapLayout.BasePosition;
            var root = Compound("Player Base", c, parent, out Vector3 f, out Vector3 r);
            Yard(root, c, MapLayout.BaseRadius - 1.2f);
            HescoRing(root, c, 16.5f);

            var pieces = new (string model, float x, float z, float yaw, float scale)[]
            {
                ("Base_Hangar", -7.5f, -5f, 0f, 0.8f),
                ("Base_LaunchPad", 1.5f, 1f, 0f, 0.86f),
                ("Base_LaunchPad", -4f, 9f, 0f, 0.86f),
                ("Base_LaunchPad", 3.5f, 9f, 0f, 0.86f),
                ("Base_FuelTanks", 0f, -12f, 90f, 1f),
                ("Base_Mast", 9.5f, -10f, 0f, 1f),
                ("Base_Dish", 5.5f, -13f, 200f, 1f),
                ("Base_Tent", 11.5f, -3f, -90f, 1f),
                ("Prop_Container_Olive", 12f, 4f, 0f, 1f),
                ("Prop_Container_Rust", 9.4f, 4.2f, 2f, 1f),
                ("Prop_Bunker", -10.5f, 11f, 30f, 1f),
                ("Prop_Bunker", -12f, 4.5f, 80f, 1f),
                ("Prop_Floodlight", -14.8f, 2f, 90f, 1f),
                ("Prop_Floodlight", 7f, 13.5f, 180f, 1f),
                ("Prop_Floodlight", -4f, -14.5f, 0f, 1f),
                ("Prop_Floodlight", 13.5f, -8f, -60f, 1f),
                ("Prop_Crates", -2.3f, -6f, 10f, 1f),
                ("Prop_Crates", -2.4f, -3.6f, -5f, 1f),
                ("Prop_Drums", 4f, -9f, 0f, 1f),
                ("Prop_Generator", 8f, -6.5f, 90f, 1f),
                ("Prop_WoodCrates", 13f, 9f, 20f, 1f),
                ("Prop_SandbagWall", 6.5f, -9.5f, 90f, 1f),
            };
            foreach (var p in pieces)
            {
                var placed = Put(root, c, f, r, p.model, p.x, p.z, p.yaw, p.scale);
                if (p.model == "Prop_Floodlight") Floodlight(placed.transform);
                // Norvane emblem along the top of the hangar's arched roof, where the camera sees it best.
                if (p.model == "Base_Hangar") Factions.Paint(placed.transform, new Vector3(0f, 4.49f, 0f), Vector3.up, 2.4f, true);
            }
            Hedgehogs(root, c, 16.5f);
            Flags(root, c, f, r, 13.5f, true);
        }

        public static void BuildEnemyHq(Transform parent)
        {
            var c = MapLayout.EnemyHqPosition;
            var root = Compound("Enemy HQ", c, parent, out Vector3 f, out Vector3 r);
            HescoRing(root, c, 16f);

            // Keep clear of the AA guns and radar that stand inside the compound.
            var occupied = new List<(Vector2 p, float r)>();
            foreach (var d in MapLayout.FixedDefences)
                if (Vector3.Distance(d.position, c) < 20f) occupied.Add((MapLayout.Flat(d.position), 4.5f));

            var pieces = new (string model, float x, float z, float yaw, float scale)[]
            {
                ("HQ_Building", 1f, -4f, 0f, 1f),
                ("HQ_Watchtower", 9f, -9f, 20f, 1f),
                ("Enemy_Truck", -3f, 6f, -60f, 1.15f),
                ("Enemy_Truck", 0.5f, 7.5f, -55f, 1.15f),
                ("Prop_Container_Olive", 12f, -1f, 0f, 1f),
                ("Prop_Crates", 4f, 4f, 15f, 1f),
                ("Prop_WoodCrates", 5.6f, 3.6f, -10f, 1f),
                ("Prop_Drums", 6.5f, 1f, 0f, 1f),
                ("Prop_Drums", 8.3f, 1.3f, 8f, 1f),
                ("Prop_Bunker", -13f, -5f, 100f, 1f),
                ("Prop_Bunker", 5f, -13.5f, 170f, 1f),
                ("Prop_Floodlight", -8f, -8f, 45f, 1f),
                ("Prop_Generator", -6f, -3f, 0f, 1f),
            };
            foreach (var p in pieces)
            {
                Vector3 at = c + r * p.x + f * p.z;
                if (Blocked(occupied, MapLayout.Flat(at), 2f)) continue;
                var placed = Put(root, c, f, r, p.model, p.x, p.z, p.yaw, p.scale);
                occupied.Add((MapLayout.Flat(at), 3f));
                if (p.model == "Prop_Floodlight") Floodlight(placed.transform);
                if (p.model == "HQ_Building")
                {
                    // A Dravin flag at a front corner of the flat roof and the emblem painted on the roof beside it.
                    Flag.Create(root, placed.transform.TransformPoint(new Vector3(5.2f, 6.25f, 3.3f)), placed.transform.eulerAngles.y + 90f,
                        false, onGround: false);
                    Factions.Paint(placed.transform, new Vector3(-3f, 6.27f, 2.4f), Vector3.up, 2.4f, false);
                }
            }

            // Zig-zag trenches round the back and sides, inside the wall.
            int segments = 22;
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.Lerp(65f, 295f, i / (segments - 1f)) * Mathf.Deg2Rad; // 0 = the gate
                var local = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 13.2f;
                Vector3 at = c + r * local.x + f * local.y;
                if (Blocked(occupied, MapLayout.Flat(at), 2.2f)) continue;
                float tangent = Mathf.Atan2(Mathf.Cos(angle), -Mathf.Sin(angle)) * Mathf.Rad2Deg;
                float zig = (i % 2 == 0 ? 22f : -22f);
                Put(root, c, f, r, "Prop_Trench", local.x, local.y, 90f - tangent + zig, 1f);
            }
            Hedgehogs(root, c, 16f);
            Flags(root, c, f, r, 13f, false);
        }

        // The lamp head of a floodlight shines down and forward after dark.
        static void Floodlight(Transform pole)
        {
            DayNight.AddNightLight(pole, new Vector3(0f, 8f, 0.3f), new Vector3(55f, 0f, 0f), LightType.Spot,
                new Color(0.9f, 0.95f, 1f), 8f, 40f, 110f);
        }

        // A pair of flags just inside the main gate.
        static void Flags(Transform root, Vector3 c, Vector3 f, Vector3 r, float inset, bool player)
        {
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + 90f;
            foreach (float side in new[] { -4.2f, 4.2f })
                Flag.Create(root, c + r * side + f * inset, yaw, player);
        }

        // Static stand-ins for the units the game spawns, so the editor preview shows a populated map.
        public static void PreviewUnits(Transform parent)
        {
            var root = new GameObject("Units (preview)").transform;
            root.SetParent(parent, false);
            foreach (var d in MapLayout.FixedDefences)
                World.Place("Enemy_" + d.type, d.position, 0f, 1f, 0f, root);
            var vehicles = new (string model, Vector3 at, float yaw)[]
            {
                ("Enemy_Tank", new Vector3(10f, 0f, 20f), 30f), ("Enemy_Tank", new Vector3(30f, 0f, -10f), 120f),
                ("Enemy_Tank", new Vector3(60f, 0f, 20f), 250f), ("Enemy_Truck", new Vector3(-5f, 0f, 30f), 200f),
                ("Enemy_Truck", new Vector3(40f, 0f, 70f), 90f),
            };
            foreach (var v in vehicles) World.Place(v.model, v.at, v.yaw, 1f, 0f, root);
            var b = MapLayout.BasePosition;
            string[] drones = { "Drone_Scout", "Drone_Strike", "Drone_Kamikaze" };
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject(drones[i]).transform;
                go.SetParent(root, false);
                go.position = b + new Vector3(-3f + i * 3f, 1.2f, 2f);
                Art.TryModel(drones[i], go);
            }
        }

        // ---- Helpers ----

        // An empty for the compound, plus its frame: f points from the centre to where the first road comes in.
        static Transform Compound(string name, Vector3 center, Transform parent, out Vector3 f, out Vector3 r)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = center;
            var entrances = Entrances(center);
            Vector2 gate = entrances.Count > 0 ? entrances[0] : Vector2.up;
            f = new Vector3(gate.x, 0f, gate.y);
            r = new Vector3(f.z, 0f, -f.x);
            return root;
        }

        // Directions (unit, from the centre) in which roads leave a compound.
        static List<Vector2> Entrances(Vector3 center)
        {
            var result = new List<Vector2>();
            Vector2 c = MapLayout.Flat(center);
            foreach (var line in MapLayout.RoadLines)
            {
                foreach (var end in new[] { line[0], line[line.Count - 1] })
                {
                    float d = Vector2.Distance(end, c);
                    if (d > 12f && d < 22f) result.Add((end - c).normalized);
                }
            }
            return result;
        }

        // The barrier round a compound: not a drawn circle but a dozen straight runs of hesco, each corner up to
        // 3 m out (never in, where the compound's things stand), as laid out on the ground by hand; a gap and a
        // gate where each road comes in.
        static void HescoRing(Transform root, Vector3 center, float radius)
        {
            var gaps = Entrances(center);
            const int corners = 12;
            var rng = new System.Random(Mathf.RoundToInt(center.x * 7f + center.z * 13f));
            var ring = new Vector2[corners];
            for (int k = 0; k < corners; k++)
            {
                float a = 2f * Mathf.PI * (k + ((float)rng.NextDouble() - 0.5f) * 0.4f) / corners;
                ring[k] = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (radius + 0.3f + (float)rng.NextDouble() * 2.7f);
            }
            for (int k = 0; k < corners; k++)
            {
                Vector2 a = ring[k], b = ring[(k + 1) % corners];
                float length = Vector2.Distance(a, b);
                Vector2 outward = new Vector2(b.y - a.y, a.x - b.x) / length;
                if (Vector2.Dot(outward, a + b) < 0f) outward = -outward;
                float yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg;
                int count = Mathf.Max(1, Mathf.RoundToInt(length / HescoUnit));
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, (i + 0.5f) / count);
                    bool gap = false;
                    foreach (var g in gaps)
                        if (Vector2.Angle(p, g) * Mathf.Deg2Rad * p.magnitude < 3.2f) gap = true;
                    if (!gap) World.Place("Prop_Hesco", center + new Vector3(p.x, 0f, p.y), yaw, 1f, 0.05f, root);
                }
            }
            foreach (var g in gaps)
            {
                float reach = RingReach(ring, g, radius);
                var pos = center + new Vector3(g.x, 0f, g.y) * reach;
                World.Place("Prop_Gate", pos, Mathf.Atan2(g.x, g.y) * Mathf.Rad2Deg, 1f, 0f, root);
            }
        }

        // How far from the middle the ring is in direction dir (unit).
        static float RingReach(Vector2[] ring, Vector2 dir, float fallback)
        {
            for (int k = 0; k < ring.Length; k++)
            {
                Vector2 a = ring[k], e = ring[(k + 1) % ring.Length] - a;
                float denom = dir.x * e.y - dir.y * e.x;
                if (Mathf.Abs(denom) < 1e-5f) continue;
                float t = (a.x * e.y - a.y * e.x) / denom, s = (a.x * dir.y - a.y * dir.x) / denom;
                if (t > 0f && s >= 0f && s <= 1f) return t;
            }
            return fallback;
        }

        // Czech hedgehogs scattered outside each gate.
        static void Hedgehogs(Transform root, Vector3 center, float radius)
        {
            var rng = new System.Random(Mathf.RoundToInt(center.x * 10f));
            foreach (var g in Entrances(center))
            {
                var out3 = new Vector3(g.x, 0f, g.y);
                var side = new Vector3(out3.z, 0f, -out3.x);
                foreach (float s in new[] { -5.2f, -3.9f, 3.9f, 5.2f })
                {
                    var pos = center + out3 * (radius + 3f + (float)rng.NextDouble() * 2f) + side * s;
                    World.Place("Prop_Hedgehog", pos, (float)rng.NextDouble() * 360f, 1f, 0.1f, root);
                }
            }
        }

        // A flat disc of gravel over the base's levelled pad (photo gravel, or generated if it's missing).
        static void Yard(Transform root, Vector3 center, float radius)
        {
            const int segments = 64;
            var vertices = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.up * 0.03f;
            uvs[0] = Vector2.zero;
            for (int i = 0; i < segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                // A ragged edge, kept inside the walls: gravel spread by hand and worn back by vehicles.
                float r = radius * Mathf.Min(1f, 0.82f + 0.6f * (Ground.SiteEdge(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius) - 0.95f) + 0.08f);
                vertices[i + 1] = new Vector3(Mathf.Sin(a) * r, 0.03f, Mathf.Cos(a) * r);
                uvs[i + 1] = new Vector2(vertices[i + 1].x, vertices[i + 1].z); // metres, like the models
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }
            var mesh = new Mesh { name = "Yard", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            var go = new GameObject("Yard");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var gravel = Resources.Load<Material>("Materials/PBR/Gravel");
            if (gravel == null)
            {
                gravel = Art.NewMaterial(Color.white);
                gravel.mainTexture = Farmland.GravelTexture();
                gravel.SetFloat("_Smoothness", 0.05f);
            }
            renderer.sharedMaterial = gravel;
        }

        static GameObject Put(Transform root, Vector3 c, Vector3 f, Vector3 r, string model, float x, float z, float yaw, float scale)
        {
            Vector3 at = c + r * x + f * z;
            float frameYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            return World.Place(model, at, frameYaw + yaw, scale, 0f, root);
        }

        static bool Blocked(List<(Vector2 p, float r)> occupied, Vector2 p, float radius)
        {
            foreach (var o in occupied)
                if (Vector2.Distance(o.p, p) < o.r + radius) return true;
            return false;
        }
    }
}

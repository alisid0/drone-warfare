using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Renders reserved sector land uses with shared meshes and the existing World's tree and spacing budget.
    public static partial class Features
    {
        // Keeps surfaces cheap regardless of their area; forest planting runs before World's ordinary woods.
        static void BuildIdentity(Transform root)
        {
            var p = MapLayout.Plan;
            BuildWorldSurfaces(root);
            var yards = new MeshBuilder(2);
            foreach (var surface in p.featureSurfaces)
            {
                var a = surface.area;
                for (float u = -a.halfLength; u < a.halfLength; u += 8f)
                    for (float v = -a.halfWidth; v < a.halfWidth; v += 8f)
                    {
                        float u1 = Mathf.Min(u + 8f, a.halfLength), v1 = Mathf.Min(v + 8f, a.halfWidth);
                        float lift = surface.concrete ? 0.12f : 0.08f;
                        yards.Quad(surface.concrete ? 1 : 0, SurfacePoint(a.Point(u, v), lift), SurfacePoint(a.Point(u1, v), lift),
                            SurfacePoint(a.Point(u1, v1), lift), SurfacePoint(a.Point(u, v1), lift), new Vector2(u / 6f, v / 6f), new Vector2(u1 / 6f, v1 / 6f));
                    }
            }
            yards.Finish("Feature yards", root, new[] { Pbr("Gravel", new Color(0.42f, 0.40f, 0.36f)), Pbr("Concrete", new Color(0.55f, 0.55f, 0.51f)) });
            FeatureFences(root);
            if (p.kind == SectorKind.DeepForest && p.reservedFeatures.Count > 0) WoodlandCore(root, p.reservedFeatures[0]);
            if (p.wetland != null) Wetland(root, p.wetland);
        }

        // Permanent flat slabs use the same clipped polygons as LandmarkHlod, keeping both sides of a seam aligned.
        static void BuildWorldSurfaces(Transform root)
        {
            var data = MapLayout.Plan.world;
            if (data == null) return;
            foreach (var f in data.features)
            {
                var source = LandmarkMesh.Build(f, data.center, true);
                if (source.vertices.Count == 0) continue;
                var mesh = new Mesh { name = "World landmark surfaces", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(source.vertices); mesh.SetNormals(source.normals);
                var materials = new List<Material>();
                for (int i = 0; i < 5; i++) if (source.triangles[i].Count > 0) materials.Add(Art.Shared(LandmarkMesh.Color(i)));
                mesh.subMeshCount = materials.Count;
                int sub = 0;
                for (int i = 0; i < 5; i++) if (source.triangles[i].Count > 0) mesh.SetTriangles(source.triangles[i], sub++);
                mesh.RecalculateBounds();
                var go = new GameObject("World landmark " + f.id + " surfaces"); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            }
        }

        // Samples finished terrain so dressings neither float on hills nor cut through flattened building sites.
        static Vector3 SurfacePoint(Vector2 p, float lift) => Ground.OnGround(new Vector3(p.x, 0f, p.y)) + Vector3.up * lift;

        // A 2 m industrial security fence, batched into one renderer rather than hundreds of separate panels.
        static void FeatureFences(Transform root)
        {
            var mesh = new MeshBuilder(1);
            foreach (var line in MapLayout.Plan.featureFences)
                for (int i = 1; i < line.Length; i++)
                {
                    Vector2 a = line[i - 1], b = line[i], d = (b - a).normalized;
                    Vector3 right = new Vector3(-d.y, 0f, d.x);
                    int steps = Mathf.CeilToInt(Vector2.Distance(a, b) / 4f);
                    for (int k = 0; k <= steps; k++)
                    {
                        Vector3 at = SurfacePoint(Vector2.Lerp(a, b, k / (float)steps), 1f);
                        mesh.Box(0, at, right, Vector3.up, new Vector3(d.x, 0f, d.y), new Vector3(0.10f, 2f, 0.10f));
                        if (k == steps) continue;
                        Vector3 end = SurfacePoint(Vector2.Lerp(a, b, (k + 1f) / steps), 1f);
                        foreach (float h in new[] { -0.6f, 0f, 0.85f }) mesh.Bar(0, at + Vector3.up * h, end + Vector3.up * h, right, 0.04f, 0.05f);
                        // Diagonal wires give the fence a readable mesh silhouette without a transparent material.
                        mesh.Bar(0, at - Vector3.up * 0.8f, end + Vector3.up * 0.8f, right, 0.025f, 0.025f);
                        mesh.Bar(0, at + Vector3.up * 0.8f, end - Vector3.up * 0.8f, right, 0.025f, 0.025f);
                    }
                }
            mesh.Finish("Compound fence", root, new[] { Pbr("Galvanized", new Color(0.36f, 0.38f, 0.37f)) });
        }

        // A bounded mature stand, denser inside and feathered at its perimeter, with glades and a clear forestry track.
        static void WoodlandCore(Transform root, FeatureArea a)
        {
            var rng = new System.Random(MapLayout.Plan.worldSeed ^ 0x4f21);
            var candidates = new List<Vector2>();
            const float spacing = 7.4f;
            for (float u = -a.halfLength; u <= a.halfLength; u += spacing)
                for (float v = -a.halfWidth; v <= a.halfWidth; v += spacing)
                {
                    float x = u / a.halfLength, z = v / a.halfWidth;
                    float edge = Mathf.Sqrt(Mathf.Sqrt(x * x * x * x + z * z * z * z));
                    Vector2 p = a.Point(u + Range(rng, -1.2f, 1.2f), v + Range(rng, -1.2f, 1.2f));
                    if (!a.Contains(p) || edge > 1f) continue;
                    if (edge > 0.86f && rng.NextDouble() < (edge - 0.86f) * 4f) continue;
                    if (!FeatureOpen(p, 4f)) continue;
                    bool glade = false;
                    foreach (var g in MapLayout.Plan.woodlandGlades) if (Vector2.Distance(p, g.center) < g.radius) glade = true;
                    if (!glade) candidates.Add(p);
                }
            // Shuffle across the entire reserve; a budget must never slice the last rows off a wood.
            int count = 0, renderers = 0;
            for (int i = candidates.Count - 1; i >= 0 && count < 6800; i--)
            {
                int index = rng.Next(i + 1);
                Vector2 p = candidates[index]; candidates[index] = candidates[i];
                double roll = rng.NextDouble();
                string model = roll < 0.55 ? "Scenery_Spruce" : roll < 0.8 ? "Scenery_Pine" : roll < 0.9 ? "Scenery_Pine_B" : "Scenery_Tree";
                Vector3 at = new Vector3(p.x, 0f, p.y);
                int cost = Art.ModelRendererCount(model, at);
                if (renderers + cost > 20400) break; // within the existing 8000-tree woods allowance, counting all LODs
                if (World.Place(model, at, Range(rng, 0f, 360f), Range(rng, 1.02f, 1.18f), 0.25f, root) == null) continue;
                renderers += cost;
                count++;
            }
        }

        // Feature vegetation owns its reserve but respects all the ordinary water, road, building and unit clearances.
        static bool FeatureOpen(Vector2 p, float margin)
        {
            if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) > MapLayout.Edge - margin) return false;
            if (MapLayout.LakeDistance(p.x, p.y) < margin || MapLayout.NearFlatSite(p, margin) || MapLayout.NearVillage(p, margin)) return false;
            if (MapLayout.HasRiver && MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverHalfWidth + margin) return false;
            return MapLayout.RoadField.Distance(p.x, p.y) > MapLayout.RoadHalfWidth + margin
                && MapLayout.TrackField.Distance(p.x, p.y) >= 2f + margin && MapLayout.RailField.Distance(p.x, p.y) > 3f + margin;
        }

        // Wet soil connects the pools along both banks; bounded reed clumps share one mesh and cast no shadows.
        static void Wetland(Transform root, Vector2[] line)
        {
            var ground = new MeshBuilder(1);
            var reeds = new MeshBuilder(1);
            var rng = new System.Random(MapLayout.Plan.worldSeed ^ 0x77a1);
            int clumps = 0;
            for (int i = 1; i < line.Length; i++)
            {
                Vector2 d = (line[i] - line[i - 1]).normalized, side = new Vector2(-d.y, d.x);
                for (float v = -58f; v < 58f; v += 4f)
                {
                    Vector2 a = line[i - 1] + side * v, b = line[i] + side * v;
                    Vector2 c = b + side * 4f, e = a + side * 4f;
                    if (!FeatureOpen(a, 1f) || !FeatureOpen(b, 1f) || !FeatureOpen(c, 1f) || !FeatureOpen(e, 1f)) continue;
                    ground.Quad(0, SurfacePoint(a, 0.10f), SurfacePoint(b, 0.10f), SurfacePoint(c, 0.10f), SurfacePoint(e, 0.10f), a / 8f, c / 8f);
                    if (clumps >= 520 || rng.NextDouble() > 0.38) continue;
                    Vector2 p = (a + c) * 0.5f;
                    float water = Mathf.Min(MapLayout.LakeDistance(p.x, p.y), MapLayout.RiverField.Distance(p.x, p.y) - MapLayout.RiverHalfWidth);
                    if (water > 18f) continue;
                    clumps++;
                    for (int k = 0; k < 7; k++)
                    {
                        Vector3 at = SurfacePoint(p + new Vector2(Range(rng, -1.7f, 1.7f), Range(rng, -1.7f, 1.7f)), 0.05f);
                        float height = Range(rng, 1.1f, 2.2f);
                        reeds.Bar(0, at, at + new Vector3(0.2f, height, 0.1f), Vector3.right, 0.08f, 0.08f);
                        reeds.Bar(0, at + Vector3.up * height, at + Vector3.up * (height + 0.3f), Vector3.right, 0.14f, 0.14f);
                    }
                }
            }
            ground.Finish("Wetland soil", root, new[] { Art.Shared(new Color(0.29f, 0.36f, 0.27f)) });
            var plants = new GameObject("Wetland reeds").transform;
            plants.SetParent(root, false);
            reeds.Finish("Reed clumps", plants, new[] { Art.Shared(new Color(0.38f, 0.39f, 0.20f)) });
            foreach (var r in plants.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}

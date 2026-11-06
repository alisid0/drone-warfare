using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Permanent roofs, tanks and yard surfaces: at most twelve material submissions per feature, no scene objects.
    public sealed class LandmarkHlod
    {
        // Native resources live until FarScatter clears its origin/seed cache or the owner is destroyed.
        sealed class Batch
        {
            public Mesh mesh;
            public Bounds bounds;
            public readonly List<int> materials = new List<int>();
            public int triangles;
        }

        readonly Dictionary<int, Batch> batches = new Dictionary<int, Batch>();
        readonly Material[] materials = new Material[LandmarkMesh.MaterialCount];
        public static int DrawCalls { get; private set; }
        public static int TrianglesDrawn { get; private set; }

        // Match ForestHlod's lifecycle, explicitly releasing every mesh and owned material.
        public void Clear()
        {
            foreach (var batch in batches.Values) Art.Discard(batch.mesh);
            batches.Clear();
            for (int i = 0; i < materials.Length; i++) { Art.Discard(materials[i]); materials[i] = null; }
            DrawCalls = TrianglesDrawn = 0;
        }

        // Build only visible compounds; fixed geometry remains useful down to the sector boundary on any visit.
        public void Update(Camera camera, Plane[] planes)
        {
            DrawCalls = TrianglesDrawn = 0;
            foreach (var f in WorldPlan.Features)
            {
                Vector2 p = f.center - FarTerrain.Origin;
                float radius = f.halfLength + f.halfWidth + 100f;
                var bounds = new Bounds(new Vector3(p.x, f.elevation + 35f, p.y), new Vector3(radius * 2f, 90f, radius * 2f));
                if (bounds.SqrDistance(camera.transform.position) > 22000f * 22000f || !GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                if (!batches.TryGetValue(f.id, out Batch batch))
                {
                    batch = Upload(LandmarkMesh.Build(f, FarTerrain.Origin, false)); batches.Add(f.id, batch);
                }
                if (batch.mesh == null) continue;
                for (int sub = 0; sub < batch.materials.Count; sub++)
                {
                    int index = batch.materials[sub];
                    if (materials[index] == null)
                    {
                        materials[index] = Art.NewMaterial(LandmarkMesh.Color(index));
                        materials[index].SetFloat("_Smoothness", 0.12f);
                    }
                    var rp = new RenderParams(materials[index]) { camera = camera, worldBounds = batch.bounds,
                        shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
                    Graphics.RenderMesh(rp, batch.mesh, sub, Matrix4x4.identity); DrawCalls++;
                }
                TrianglesDrawn += batch.triangles;
            }
        }

        // One mesh holds all nonempty submeshes; only small geometry arrays are retained by Unity.
        static Batch Upload(LandmarkMesh.Data data)
        {
            var batch = new Batch();
            if (data.vertices.Count == 0) return batch;
            var mesh = new Mesh { name = "World landmark", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(data.vertices); mesh.SetNormals(data.normals);
            for (int i = 0; i < data.triangles.Length; i++) if (data.triangles[i].Count > 0) batch.materials.Add(i);
            mesh.subMeshCount = batch.materials.Count;
            for (int sub = 0; sub < batch.materials.Count; sub++)
            {
                var triangles = data.triangles[batch.materials[sub]]; mesh.SetTriangles(triangles, sub); batch.triangles += triangles.Count / 3;
            }
            mesh.RecalculateBounds(); batch.mesh = mesh; batch.bounds = mesh.bounds;
            return batch;
        }
    }
}

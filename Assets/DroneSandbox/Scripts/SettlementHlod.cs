using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // A single cheap mesh per visible settlement combines its 120 m roof/building blocks. Near blocks are removed
    // from that mesh's index list exactly when their unchanged plot plan starts submitting individual house models.
    public sealed class SettlementHlod
    {
        // Mesh construction keeps block index ranges, so moving the camera only changes the small index buffer.
        public sealed class Data
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
            public readonly List<SettlementPattern.Block> blocks = new List<SettlementPattern.Block>();
            public readonly List<int> ends = new List<int>();
        }

        // A settlement retains one mesh and a near/far bit for each block, with no per-building GameObjects.
        sealed class Town
        {
            public Data data = new Data();
            public Mesh mesh;
            public bool[] near;
            public int triangleCount;
            public Bounds bounds;
        }

        const int Colours = 10; // palette texels
        readonly Dictionary<int, Town> towns = new Dictionary<int, Town>();
        readonly List<int> indices = new List<int>();
        IEnumerator<SettlementPattern.Block> builder;
        Town pending;
        int pendingIndex;
        Material material;
        Texture2D palette;
        public static int BlocksDrawn { get; private set; }
        public static int TrianglesDrawn { get; private set; }
        public static int DrawCalls { get; private set; }

        // Discard native allocations when a sector or world changes; the palette is owned here too.
        public void Clear()
        {
            foreach (var town in towns.Values) Art.Discard(town.mesh);
            towns.Clear(); builder?.Dispose(); builder = null; pending = null;
            Art.Discard(material); Art.Discard(palette); material = null; palette = null;
            BlocksDrawn = TrianglesDrawn = DrawCalls = 0;
        }

        // Build two small blocks per frame, prioritizing the closest visible settlement, then submit its masses.
        public void Update(Camera camera, Plane[] planes, FarScatter scatter)
        {
            BlocksDrawn = TrianglesDrawn = DrawCalls = 0;
            Vector3 position = camera.transform.position;
            int nearest = -1; float best = float.MaxValue;
            for (int i = 0; i < WorldPlan.Settlements.Count; i++)
            {
                var settlement = WorldPlan.Settlements[i];
                Vector2 p = settlement.center - FarTerrain.Origin;
                var bounds = new Bounds(new Vector3(p.x, WorldPlan.Height(settlement.center.x, settlement.center.y), p.y),
                    new Vector3(settlement.radius * 2f + 240f, 600f, settlement.radius * 2f + 240f));
                float distance = bounds.SqrDistance(position);
                if (distance > 24000f * 24000f || !GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                if (!towns.TryGetValue(i, out Town town))
                {
                    if (distance < best) { nearest = i; best = distance; }
                    continue;
                }
                if (town.mesh != null) Draw(town, camera, planes, scatter);
            }
            if (builder == null && nearest >= 0)
            {
                pendingIndex = nearest; pending = new Town();
                builder = SettlementPattern.Build(WorldPlan.Settlements[nearest]).GetEnumerator();
            }
            for (int b = 0; b < 2 && builder != null; b++)
            {
                if (builder.MoveNext()) Append(pending.data, builder.Current);
                else
                {
                    Finish(pending); towns.Add(pendingIndex, pending);
                    builder.Dispose(); builder = null; pending = null;
                }
            }
        }

        // An opaque palette gives walls and roof planes distinct values without multiplying draw calls: four plasters,
        // four roofs, then a barn's weathered planks and dark sheet roof.
        void Material()
        {
            if (material != null) return;
            palette = new Texture2D(Colours, 1, TextureFormat.RGBA32, false) { name = "Settlement colours", filterMode = FilterMode.Point };
            palette.SetPixels32(new[] { new Color32(178, 168, 146, 255), new Color32(148, 144, 134, 255),
                new Color32(164, 154, 131, 255), new Color32(182, 174, 153, 255), new Color32(138, 79, 58, 255),
                new Color32(83, 91, 94, 255), new Color32(117, 83, 66, 255), new Color32(124, 117, 103, 255),
                new Color32(101, 86, 70, 255), new Color32(84, 82, 78, 255) });
            palette.Apply(false, true);
            material = Art.NewMaterial(Color.white); material.mainTexture = palette; material.SetFloat("_Smoothness", 0.08f);
        }

        // Upload the complete settlement once; subsequent LOD handovers do not rebuild its vertices or normals.
        void Finish(Town town)
        {
            town.near = new bool[town.data.blocks.Count];
            if (town.data.vertices.Count == 0) return;
            Material();
            var mesh = new Mesh { name = "Settlement roof clusters", indexFormat = town.data.vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(town.data.vertices); mesh.SetUVs(0, town.data.uv); mesh.SetTriangles(town.data.triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            town.mesh = mesh; town.bounds = mesh.bounds; town.triangleCount = town.data.triangles.Count / 3;
        }

        // The same distance decision controls both sides of the handover, so there are no missing/duplicate plots.
        void Draw(Town town, Camera camera, Plane[] planes, FarScatter scatter)
        {
            bool changed = false;
            for (int i = 0; i < town.data.blocks.Count; i++)
            {
                var block = town.data.blocks[i];
                Vector2 p = block.center - FarTerrain.Origin;
                bool near = Vector3.Distance(camera.transform.position, new Vector3(p.x, FarScatter.GroundAt(p) + 5f, p.y)) < FarScatter.HouseReach;
                if (near != town.near[i]) { town.near[i] = near; changed = true; }
                if (near)
                {
                    var bounds = new Bounds(new Vector3(p.x, FarScatter.GroundAt(p), p.y), new Vector3(180f, 150f, 180f));
                    if (GeometryUtility.TestPlanesAABB(planes, bounds)) foreach (var house in block.houses) scatter.House(house);
                }
                else if (block.houses.Count > 0) BlocksDrawn++;
            }
            if (changed)
            {
                indices.Clear(); int start = 0;
                for (int i = 0; i < town.near.Length; i++)
                {
                    int end = town.data.ends[i];
                    if (!town.near[i]) for (int k = start; k < end; k++) indices.Add(town.data.triangles[k]);
                    start = end;
                }
                town.mesh.SetTriangles(indices, 0, false); town.triangleCount = indices.Count / 3;
            }
            if (town.triangleCount == 0) return;
            var rp = new RenderParams(material) { camera = camera, worldBounds = town.bounds,
                shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
            Graphics.RenderMesh(rp, town.mesh, 0, Matrix4x4.identity);
            TrianglesDrawn += town.triangleCount; DrawCalls++;
        }

        // Preserve each plot's position, orientation and silhouette in a fourteen-triangle roof/wall shell.
        public static void Append(Data data, SettlementPattern.Block block)
        {
            data.blocks.Add(block);
            foreach (var house in block.houses)
            {
                Vector2 p = house.world - FarTerrain.Origin;
                Vector3 size = SettlementPattern.Size[house.kind];
                float w = size.x * 0.5f, d = size.z * 0.5f, h = size.y, r = SettlementPattern.Roof[house.kind];
                var matrix = Matrix4x4.TRS(new Vector3(p.x, FarScatter.GroundAt(p) - 0.2f, p.y), Quaternion.Euler(0f, house.yaw, 0f), Vector3.one * house.Scale);
                Vector3 a = new Vector3(-w, 0f, -d), b = new Vector3(w, 0f, -d), c = new Vector3(w, 0f, d), e = new Vector3(-w, 0f, d);
                Vector3 at = a + Vector3.up * h, bt = b + Vector3.up * h, ct = c + Vector3.up * h, et = e + Vector3.up * h;
                Vector3 ridgeA = new Vector3(-w, h + r, 0f), ridgeB = new Vector3(w, h + r, 0f);
                int wall = house.kind % 4, roof = 4 + (house.kind == 1 || house.kind == 5 ? 1 : house.kind % 3);
                if (house.kind == SettlementPattern.Barn) { wall = 8; roof = 9; }
                Face(data, matrix, a, at, bt, b, wall); Face(data, matrix, b, bt, ct, c, wall);
                Face(data, matrix, c, ct, et, e, wall); Face(data, matrix, e, et, at, a, wall);
                Face(data, matrix, at, ridgeA, ridgeB, bt, roof); Face(data, matrix, ridgeA, et, ct, ridgeB, roof);
                if (r > 0f) { Triangle(data, matrix, et, ridgeA, at, wall); Triangle(data, matrix, bt, ridgeB, ct, wall); }
            }
            data.ends.Add(data.triangles.Count);
        }

        // Separate face vertices preserve hard roof ridges when Unity calculates normals.
        static void Face(Data data, Matrix4x4 matrix, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int colour)
        {
            Triangle(data, matrix, a, b, c, colour); Triangle(data, matrix, a, c, d, colour);
        }

        // A single palette texel per face keeps all colours in one material and draw call.
        static void Triangle(Data data, Matrix4x4 matrix, Vector3 a, Vector3 b, Vector3 c, int colour)
        {
            int start = data.vertices.Count;
            data.vertices.Add(matrix.MultiplyPoint3x4(a)); data.vertices.Add(matrix.MultiplyPoint3x4(b)); data.vertices.Add(matrix.MultiplyPoint3x4(c));
            for (int i = 0; i < 3; i++) { data.uv.Add(new Vector2((colour + 0.5f) / Colours, 0.5f)); data.triangles.Add(start + i); }
        }
    }
}

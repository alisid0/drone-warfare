using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Separate allocation, bounded copies and publication preparation so a chunk cannot upload all attributes in one frame.
    public static class FarChunkUpload
    {
        const int Slice = 1024;
        const MeshUpdateFlags Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
        static readonly VertexAttributeDescriptor[] Layout = {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 2) };

        // The timing label follows the real final driver upload, independent of the clipped mesh's size.
        public static bool IsUpload(int stage, FarDetailChunks.Geometry data)
            => stage == 5 + 3 * ((data.vertices.Length + Slice - 1) / Slice) + (data.triangles.Length + Slice * 3 - 1) / (Slice * 3);

        // Only raw slices may share a short time window; allocations and the driver upload always stand alone.
        public static bool IsCopy(int stage, FarDetailChunks.Geometry data)
            => stage >= 4 && stage < 4 + 3 * ((data.vertices.Length + Slice - 1) / Slice) + (data.triangles.Length + Slice * 3 - 1) / (Slice * 3);

        // Creating the hidden mesh and sizing its two buffers are the native allocations FarDetail keeps alone.
        public static bool IsAllocation(FarDetailChunks.Chunk chunk, int stage) => stage == 0 && chunk.back == null || stage == 2 || stage == 3;

        // Bytes the next step hands to the mesh, for the per-frame upload counter (the final upload counts the whole mesh).
        public static long Bytes(int stage, FarDetailChunks.Geometry data) => Bytes(stage, data.vertices.Length, data.triangles.Length);

        // The same count for any mesh this uploader fills, by vertex and index count.
        public static long Bytes(int stage, int vertices, int indices)
        {
            int slices = (vertices + Slice - 1) / Slice, parts = (indices + Slice * 3 - 1) / (Slice * 3);
            int part = stage - 4;
            if (part < 0) return 0;
            if (part < slices * 3) return System.Math.Min(Slice, vertices - (part % slices) * Slice) * (part / slices == 2 ? 8L : 12L);
            part -= slices * 3;
            if (part < parts) return System.Math.Min(Slice * 3, indices - part * Slice * 3) * 4L;
            return part == parts ? 0 : vertices * 32L + indices * 4L;
        }

        // Hidden buffers receive at most 12 KiB of attribute or index data per call; the owner still swaps all meshes atomically.
        public static bool Step(FarDetailChunks.Chunk chunk, FarDetailChunks.Geometry data, ref int stage)
        {
            if (stage == 0)
            {
                stage++;
                if (chunk.back == null) { chunk.back = new Mesh { indexFormat = IndexFormat.UInt32 }; chunk.back.MarkDynamic(); }
                return false;
            }
            return Step(chunk.back, data.vertices, data.normals, data.uv, data.triangles, chunk.original.bounds, false, ref stage);
        }

        // Fill an existing mesh from stage 1 on: clear, size both buffers, copy 1024-vertex and 3072-index slices, set the
        // submesh with known bounds, then upload. ForestHlod's canopy blocks use the same steps as the far chunks.
        public static bool Step(Mesh mesh, Vector3[] vertices, Vector3[] normals, Vector2[] uv, int[] triangles, Bounds bounds, bool release, ref int stage)
        {
            int part = stage++;
            if (part <= 0) return false;
            if (part == 1) { mesh.Clear(); return false; }
            if (part == 2) { mesh.SetVertexBufferParams(vertices.Length, Layout); return false; }
            if (part == 3) { mesh.SetIndexBufferParams(triangles.Length, IndexFormat.UInt32); return false; }
            part -= 4;
            int slices = (vertices.Length + Slice - 1) / Slice;
            if (part < slices * 3)
            {
                int stream = part / slices, start = (part % slices) * Slice;
                int count = Mathf.Min(Slice, vertices.Length - start);
                if (stream == 0) mesh.SetVertexBufferData(vertices, start, start, count, 0, Flags);
                else if (stream == 1) mesh.SetVertexBufferData(normals, start, start, count, 1, Flags);
                else mesh.SetVertexBufferData(uv, start, start, count, 2, Flags);
                return false;
            }
            part -= slices * 3;
            int indices = (triangles.Length + Slice * 3 - 1) / (Slice * 3);
            if (part < indices)
            {
                int start = part * Slice * 3;
                mesh.SetIndexBufferData(triangles, start, start, Mathf.Min(Slice * 3, triangles.Length - start), Flags);
                return false;
            }
            if (part == indices)
            {
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, triangles.Length) { bounds = bounds, vertexCount = vertices.Length }, Flags);
                mesh.bounds = bounds;
                return false;
            }
            mesh.UploadMeshData(release);
            return true;
        }
    }
}

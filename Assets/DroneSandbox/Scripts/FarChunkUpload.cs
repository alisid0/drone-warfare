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

        // Hidden buffers receive at most 12 KiB of attribute or index data per call; the owner still swaps all meshes atomically.
        public static bool Step(FarDetailChunks.Chunk chunk, FarDetailChunks.Geometry data, ref int stage)
        {
            int part = stage++;
            if (part == 0)
            {
                if (chunk.back == null) { chunk.back = new Mesh { indexFormat = IndexFormat.UInt32 }; chunk.back.MarkDynamic(); }
                return false;
            }
            var mesh = chunk.back;
            if (part == 1) { mesh.Clear(); return false; }
            if (part == 2) { mesh.SetVertexBufferParams(data.vertices.Length, Layout); return false; }
            if (part == 3) { mesh.SetIndexBufferParams(data.triangles.Length, IndexFormat.UInt32); return false; }
            part -= 4;
            int slices = (data.vertices.Length + Slice - 1) / Slice;
            if (part < slices * 3)
            {
                int stream = part / slices, start = (part % slices) * Slice;
                int count = Mathf.Min(Slice, data.vertices.Length - start);
                if (stream == 0) mesh.SetVertexBufferData(data.vertices, start, start, count, 0, Flags);
                else if (stream == 1) mesh.SetVertexBufferData(data.normals, start, start, count, 1, Flags);
                else mesh.SetVertexBufferData(data.uv, start, start, count, 2, Flags);
                return false;
            }
            part -= slices * 3;
            int indices = (data.triangles.Length + Slice * 3 - 1) / (Slice * 3);
            if (part < indices)
            {
                int start = part * Slice * 3;
                mesh.SetIndexBufferData(data.triangles, start, start, Mathf.Min(Slice * 3, data.triangles.Length - start), Flags);
                return false;
            }
            if (part == indices)
            {
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, data.triangles.Length) { bounds = chunk.original.bounds, vertexCount = data.vertices.Length }, Flags);
                mesh.bounds = chunk.original.bounds;
                return false;
            }
            mesh.UploadMeshData(false);
            return true;
        }
    }
}

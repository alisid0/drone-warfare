using System;
using DroneSandbox;
using UnityEngine;

// Records buffer writes so interrupted transactions and partial slices can be checked without a graphics driver.
static class UploadCheck
{
    // Non-multiple lengths exercise every last slice, while an interrupted reuse must overwrite all stale bytes.
    static void Main()
    {
        var chunk=new FarDetailChunks.Chunk { original=new Mesh() };
        foreach(int length in new[]{1,1024,1025,5303})
        {
            var data=new FarDetailChunks.Geometry { vertices=new Vector3[length],normals=new Vector3[length],uv=new Vector2[length],triangles=new int[length*6] };
            for(int i=0;i<length;i++){data.vertices[i]=new Vector3(i,2,3);data.normals[i]=new Vector3(0,i,1);data.uv[i]=new Vector2(i,2);}
            for(int i=0;i<data.triangles.Length;i++)data.triangles[i]=i%length;
            int stage=0;
            for(int i=0;i<6;i++)FarChunkUpload.Step(chunk,data,ref stage);
            stage=0;
            while(true)
            {
                Mesh.Written=0; bool upload=FarChunkUpload.IsUpload(stage,data);
                bool done=FarChunkUpload.Step(chunk,data,ref stage);
                if(Mesh.Written>12288)throw new Exception("Unbounded native copy");
                if(done!=upload)throw new Exception("Incorrect upload timing label");
                if(done)break;
                if(stage>200)throw new Exception("Upload never finished");
            }
            for(int i=0;i<length;i++)if(chunk.back.positions[i].x!=i||chunk.back.normals[i].y!=i||chunk.back.uv[i].x!=i)throw new Exception("Attribute corruption");
            for(int i=0;i<data.triangles.Length;i++)if(chunk.back.indices[i]!=data.triangles[i])throw new Exception("Index corruption");
            if(chunk.front!=null)throw new Exception("Hidden mesh published early");
            Console.WriteLine(length+" vertices: interrupted reuse preserved all attributes/indices; "+stage+" bounded calls; upload correctly labelled.");
        }
    }
}

namespace UnityEngine
{
    public struct Vector2 { public float x,y; public Vector2(float a,float b){x=a;y=b;} }
    public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} }
    public struct Bounds { }
    public static class Mathf { public static int Min(int a,int b)=>Math.Min(a,b); }
    public class Mesh
    {
        public static int Written;
        public Rendering.IndexFormat indexFormat;
        public int subMeshCount;
        public Bounds bounds;
        public Vector3[] positions,normals;
        public Vector2[] uv;
        public int[] indices;
        public void MarkDynamic(){} public void Clear(){} public void UploadMeshData(bool discard){}
        public void SetVertexBufferParams(int n,Rendering.VertexAttributeDescriptor[] layout){positions=new Vector3[n];normals=new Vector3[n];uv=new Vector2[n];}
        public void SetIndexBufferParams(int n,Rendering.IndexFormat format){indices=new int[n];}
        public void SetVertexBufferData<T>(T[] data,int source,int target,int n,int stream,Rendering.MeshUpdateFlags flags)
        { Array.Copy(data,source,stream==0?(Array)positions:stream==1?normals:uv,target,n);Written+=n*(stream==2?8:12); }
        public void SetIndexBufferData(int[] data,int source,int target,int n,Rendering.MeshUpdateFlags flags){Array.Copy(data,source,indices,target,n);Written+=n*4;}
        public void SetSubMesh(int sub,Rendering.SubMeshDescriptor descriptor,Rendering.MeshUpdateFlags flags){}
    }
}
namespace UnityEngine.Rendering
{
    public enum MeshUpdateFlags { DontRecalculateBounds=1,DontValidateIndices=2 }
    public enum IndexFormat { UInt32 }
    public enum VertexAttribute { Position,Normal,TexCoord0 }
    public enum VertexAttributeFormat { Float32 }
    public struct VertexAttributeDescriptor { public VertexAttributeDescriptor(VertexAttribute a,VertexAttributeFormat f,int n,int s){} }
    public struct SubMeshDescriptor { public Bounds bounds;public int vertexCount;public SubMeshDescriptor(int a,int b){bounds=default;vertexCount=0;} }
}
namespace DroneSandbox
{
    public static class FarDetailChunks
    {
        public class Chunk { public Mesh original,front,back; }
        public class Geometry { public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles; }
    }
}

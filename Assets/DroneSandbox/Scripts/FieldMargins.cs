using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Opaque field margins and a few damp patches share 128 m meshes instead of one renderer per crop.
    public static class FieldMargins
    {
        static readonly List<Mesh> made = new List<Mesh>();

        // Farmland's existing release boundary owns these generated meshes as well.
        public static void Release()
        {
            foreach (var mesh in made) Art.Discard(mesh);
            made.Clear();
        }

        // Only original parcel edges grow a margin; clipped fragments keep continuous cultivation across their joins.
        public static void Build(Transform parent)
        {
            var blocks = new SortedDictionary<long, Batch>();
            Vector2 origin = MapLayout.Plan.world == null ? Vector2.zero : MapLayout.Plan.world.center;
            foreach (var f in MapLayout.Fields)
            {
                FarFieldSurface.TryParent(f, out var whole);
                Vector2[] corners = { f.Point(-f.halfLength,-f.halfWidth), f.Point(f.halfLength,-f.halfWidth),
                    f.Point(f.halfLength,f.halfWidth), f.Point(-f.halfLength,f.halfWidth) };
                for (int edge = 0; edge < 4; edge++)
                {
                    Vector2 a = corners[edge], b = corners[(edge + 1) % 4];
                    int count = Math.Max(1, (int)Math.Ceiling((b-a).magnitude / 5f));
                    Vector2 inward = new Vector2(-(b-a).y,(b-a).x).normalized;
                    if (Vector2.Dot(inward, f.center-(a+b)*.5f) < 0f) inward = -inward;
                    for (int i = 0; i < count; i++)
                    {
                        Vector2 x = Vector2.Lerp(a,b,i/(float)count), y = Vector2.Lerp(a,b,(i+1f)/count), mid = (x+y)*.5f;
                        float existing = FieldAppearance.Edge(whole,mid);
                        Vector2 world = mid + origin;
                        float width = FieldAppearance.Margin(world.x,world.y) - Math.Max(0f,existing);
                        if (width <= .1f || existing < -1f) continue;
                        Get(blocks,mid).Quad(x,y,y+inward*width,x+inward*width,0);
                    }
                }
                // Eight-metre sampling limits extra work to roughly one tenth of the crop surface vertex budget.
                for (float u = -f.halfLength+4f; u < f.halfLength-4f; u += 8f)
                    for (float v = -f.halfWidth+4f; v < f.halfWidth-4f; v += 8f)
                    {
                        Vector2 q = f.Point(u,v), w = q+origin;
                        float patch = FieldAppearance.Patch(w.x,w.y);
                        if (patch < .23f || FieldAppearance.Edge(whole,q) < 8f) continue;
                        float radius = 2f+patch*4f;
                        var batch = Get(blocks,q);
                        for (int k = 0; k < 6; k++)
                        {
                            float a = k*Mathf.PI/3f, b = (k+1)*Mathf.PI/3f;
                            Vector2 x = q+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;
                            Vector2 y = q+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius;
                            batch.Quad(q,x,y,q,1);
                        }
                    }
            }
            Vector3 grass = FarDetailPalette.Means[4]*.8f+FarDetailPalette.Means[1]*.2f;
            var materials = new[] { Art.Shared((Color)FarDetailPalette.Encode(grass)), Art.Shared((Color)FarDetailPalette.Encode(grass*.9f)) };
            foreach (var batch in blocks.Values) batch.Finish(parent,materials);
        }

        // Stable bins cap culling bounds and do not depend on dictionary enumeration order.
        static Batch Get(SortedDictionary<long,Batch> blocks, Vector2 p)
        {
            long key = ((long)Mathf.FloorToInt(p.x/128f)<<32) ^ (uint)Mathf.FloorToInt(p.y/128f);
            if (!blocks.TryGetValue(key,out var batch)) blocks.Add(key,batch=new Batch());
            return batch;
        }

        // Two shared materials carry the margin and damp-ground colour without field-sized textures.
        sealed class Batch
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int>[] triangles = { new List<int>(),new List<int>() };

            // A small lift matches the crop's 0.35 m surface, with opaque margins covering its uncultivated edge.
            public void Quad(Vector2 a,Vector2 b,Vector2 c,Vector2 d,int material)
            {
                int n = vertices.Count;
                foreach (var p in new[] {a,b,c,d})
                {
                    var q = new Vector3(p.x,0f,p.y);
                    vertices.Add(new Vector3(p.x,Ground.HeightAt(q)+.375f,p.y)); normals.Add(Ground.NormalAt(q));
                }
                var list=triangles[material];
                if (Vector3.Cross(vertices[n+1]-vertices[n],vertices[n+3]-vertices[n]).y > 0f)
                    list.AddRange(new[] {n,n+1,n+2,n,n+2,n+3});
                else list.AddRange(new[] {n,n+2,n+1,n,n+3,n+2});
            }

            // Each block owns one renderer and its mesh is released with Farmland's generated resources.
            public void Finish(Transform parent,Material[] materials)
            {
                var mesh = new Mesh {name="Field use 128 m block",indexFormat=IndexFormat.UInt32};
                made.Add(mesh); mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.subMeshCount=2;
                for(int i=0;i<2;i++)mesh.SetTriangles(triangles[i],i);
                mesh.RecalculateBounds();
                var go=new GameObject(mesh.name);go.transform.SetParent(parent,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=materials;
            }
        }
    }
}

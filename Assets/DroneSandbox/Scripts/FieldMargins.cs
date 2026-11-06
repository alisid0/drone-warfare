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
        public static int Build(Transform parent)
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
                    int count = Math.Max(1, (int)Math.Ceiling((b-a).magnitude / 3f));
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
                // Contour the continuous soil signal instead of placing disconnected discs on a regular grid.
                for (float u = -f.halfLength; u < f.halfLength; u += 6f)
                    for (float v = -f.halfWidth; v < f.halfWidth; v += 6f)
                    {
                        float u1=Math.Min(f.halfLength,u+6f),v1=Math.Min(f.halfWidth,v+6f);
                        Vector2 a=f.Point(u,v),b=f.Point(u1,v),c=f.Point(u1,v1),d=f.Point(u,v1);
                        float av=Roughness(a,whole,origin),bv=Roughness(b,whole,origin),cv=Roughness(c,whole,origin),dv=Roughness(d,whole,origin);
                        if(Math.Max(Math.Max(av,bv),Math.Max(cv,dv))<=.35f)continue;
                        var batch=Get(blocks,(a+c)*.5f);
                        Contour(batch,a,b,c,av,bv,cv);Contour(batch,a,c,d,av,cv,dv);
                    }
            }
            Vector3 grass = FarDetailPalette.Means[4]*.8f+FarDetailPalette.Means[1]*.2f;
            var materials = new[] { Art.Shared((Color)FarDetailPalette.Encode(grass)), Art.Shared((Color)FarDetailPalette.Encode(grass*.9f)) };
            foreach (var batch in blocks.Values) batch.Finish(parent,materials);
            return blocks.Count;
        }

        // Keep rough interiors inside their original parcel while allowing neighbouring clipped pieces to agree.
        static float Roughness(Vector2 point,FieldPlot whole,Vector2 origin)
        {
            if(FieldAppearance.Edge(whole,point)<2.5f)return 0f;
            Vector2 world=point+origin;return FieldAppearance.Patch(world.x,world.y);
        }

        // Linear edge intersections join adjacent cells into one irregular patch without overlapping polygon islands.
        static void Contour(Batch batch,Vector2 a,Vector2 b,Vector2 c,float av,float bv,float cv)
        {
            var points=new[]{a,b,c};var values=new[]{av,bv,cv};var polygon=new List<Vector2>(4);
            for(int i=0;i<3;i++)
            {
                int j=(i+1)%3;bool inside=values[i]>.35f,next=values[j]>.35f;
                if(inside)polygon.Add(points[i]);
                if(inside!=next)polygon.Add(Vector2.Lerp(points[i],points[j],(.35f-values[i])/(values[j]-values[i])));
            }
            if(polygon.Count>=3)batch.Quad(polygon[0],polygon[1],polygon[2],polygon.Count==4?polygon[3]:polygon[0],1);
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

            // Close terrain samples and a small lift cover the crop's 0.35 m surface without coplanar edges.
            public void Quad(Vector2 a,Vector2 b,Vector2 c,Vector2 d,int material)
            {
                int n = vertices.Count;
                foreach (var p in new[] {a,b,c,d})
                {
                    var q = new Vector3(p.x,0f,p.y);
                    vertices.Add(new Vector3(p.x,Ground.HeightAt(q)+.425f,p.y)); normals.Add(Ground.NormalAt(q));
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
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
        }
    }
}

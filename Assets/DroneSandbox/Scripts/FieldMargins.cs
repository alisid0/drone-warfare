using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Opaque field margins share 128 m meshes instead of one renderer per crop: per edge a green grass strip, a rough
    // uncut margin (browner and darker) or a field track's grass and wheel lines; a bare edge leaves crop to the boundary.
    // Damp hollows are no longer separate patches here: the crop's own detail map darkens them softly (Farmland).
    public static class FieldMargins
    {
        static readonly List<Mesh> made = new List<Mesh>();

        // Farmland's existing release boundary owns these generated meshes as well.
        public static void Release()
        {
            foreach (var mesh in made) Art.Discard(mesh);
            made.Clear();
        }

        static readonly float[] Ruts = { .55f, 2.3f };

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
                    // A fragment edge follows one parent edge, so its use is looked up once, a little inside its middle.
                    int use = FieldAppearance.EdgeUse(whole.edges, FieldAppearance.NearestEdge(whole, (a+b)*.5f + inward*.5f));
                    if (use == (int)FieldEdge.Bare) continue;
                    int material = use == (int)FieldEdge.Wide ? 1 : 0;
                    float start = Width(use, whole, a, origin);
                    for (int i = 0; i < count; i++)
                    {
                        Vector2 x = Vector2.Lerp(a,b,i/(float)count), y = Vector2.Lerp(a,b,(i+1f)/count), mid = (x+y)*.5f;
                        float existing = FieldAppearance.Edge(whole,mid);
                        // Widths at both ends of each 3 m step, so the margin's inner edge wanders without stepping.
                        float end = Width(use, whole, y, origin), wx = start, wy = end;
                        start = end;
                        if (Math.Max(wx, wy) <= .1f || existing < -1f) continue;
                        var batch = Get(blocks,mid);
                        batch.Quad(x,y,y+inward*Math.Max(0f,wy),x+inward*Math.Max(0f,wx),material);
                        // A field track's two wheel lines sit just above the margin, at the painter's distances from the edge.
                        if (use == (int)FieldEdge.Track)
                            foreach (float line in Ruts)
                            {
                                float a0 = Math.Max(0f, line - Math.Max(0f, existing)), a1 = line + .56f - Math.Max(0f, existing);
                                if (a1 > a0 + .05f) batch.Quad(x+inward*a0,y+inward*a0,y+inward*a1,x+inward*a1,2,.435f);
                            }
                    }
                }
            }
            // The distant painter's grass strip and rough margin (FieldPalette), and bare soil for the wheel lines.
            var materials = new[] { Art.Shared((Color)FarDetailPalette.Encode(FieldPalette.Strip)), Art.Shared((Color)FarDetailPalette.Encode(FieldPalette.Rough)),
                Art.Shared((Color)FarDetailPalette.Encode(FarDetailPalette.Means[2])) };
            foreach (var batch in blocks.Values) batch.Finish(parent,materials);
            return blocks.Count;
        }

        // How far a margin reaches in from the fragment's edge at a point: the parcel edge's own width, less however
        // far inside the parcel the fragment's edge already is (a clipped piece's edge carries no new margin).
        static float Width(int use, FieldPlot whole, Vector2 point, Vector2 origin)
        {
            Vector2 world = point + origin;
            return FieldAppearance.MarginFor(use, world.x, world.y) - Math.Max(0f, FieldAppearance.Edge(whole, point));
        }

        // Stable bins cap culling bounds and do not depend on dictionary enumeration order.
        static Batch Get(SortedDictionary<long,Batch> blocks, Vector2 p)
        {
            long key = ((long)Mathf.FloorToInt(p.x/128f)<<32) ^ (uint)Mathf.FloorToInt(p.y/128f);
            if (!blocks.TryGetValue(key,out var batch)) blocks.Add(key,batch=new Batch());
            return batch;
        }

        // Three shared materials carry the grass strip, the rough margin and track ruts without field-sized textures.
        sealed class Batch
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int>[] triangles = { new List<int>(),new List<int>(),new List<int>() };

            // Close terrain samples and a small lift cover the crop's 0.35 m surface without coplanar edges.
            public void Quad(Vector2 a,Vector2 b,Vector2 c,Vector2 d,int material,float lift = .425f)
            {
                int n = vertices.Count;
                foreach (var p in new[] {a,b,c,d})
                {
                    var q = new Vector3(p.x,0f,p.y);
                    vertices.Add(new Vector3(p.x,Ground.HeightAt(q)+lift,p.y)); normals.Add(Ground.NormalAt(q));
                }
                var list=triangles[material];
                if (Vector3.Cross(vertices[n+1]-vertices[n],vertices[n+3]-vertices[n]).y > 0f)
                    list.AddRange(new[] {n,n+1,n+2,n,n+2,n+3});
                else list.AddRange(new[] {n,n+2,n+1,n,n+3,n+2});
            }

            // Each block owns one renderer and its mesh is released with Farmland's generated resources; empty slots are dropped.
            public void Finish(Transform parent,Material[] materials)
            {
                var mesh = new Mesh {name="Field use 128 m block",indexFormat=IndexFormat.UInt32};
                var used = new List<Material>();
                for(int i=0;i<triangles.Length;i++)if(triangles[i].Count>0)used.Add(materials[i]);
                made.Add(mesh); mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.subMeshCount=Math.Max(1,used.Count);
                for(int i=0,sub=0;i<triangles.Length;i++)if(triangles[i].Count>0)mesh.SetTriangles(triangles[i],sub++);
                mesh.RecalculateBounds();
                var go=new GameObject(mesh.name);go.transform.SetParent(parent,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=used.Count>0?used.ToArray():new[]{materials[0]};
                renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
        }
    }
}

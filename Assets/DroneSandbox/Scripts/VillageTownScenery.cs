using UnityEngine;

namespace DroneSandbox
{
    // Large town-edge uses stay legible at strategy height; their small fittings share the garden block batches.
    public static partial class Features
    {
        // Surface cells follow terrain and repeat the exact grid used by the immutable painter.
        static void TownLandUse(VillageMesh mesh, VillagePlot p, int lod)
        {
            for (float v = 0f; v < p.depth; v += 2f)
                for (float u = -p.frontage * .5f; u < p.frontage * .5f; u += 2f)
                {
                    float end = Mathf.Min(u + 2f, p.frontage * .5f), back = Mathf.Min(v + 2f, p.depth);
                    int material = p.WorkAt((u + end) * .5f, (v + back) * .5f);
                    mesh.Quad(material, At(p.Point(u,v),.065f), At(p.Point(end,v),.065f), At(p.Point(end,back),.065f), At(p.Point(u,back),.065f));
                }
            if (p.landUse == 2)
            {
                float x = p.frontage * .5f - 9f, front = 9f, back = p.depth - 9f;
                TownLine(mesh,p,-x,front,x,front); TownLine(mesh,p,-x,back,x,back);
                TownLine(mesh,p,-x,front,-x,back); TownLine(mesh,p,x,front,x,back);
                TownLine(mesh,p,0f,front,0f,back);
                for (int i = 0; i < 32; i++)
                {
                    float a = i*Mathf.PI/16f, b = (i+1)*Mathf.PI/16f;
                    TownLine(mesh,p,Mathf.Cos(a)*9.15f,p.depth*.5f+Mathf.Sin(a)*9.15f,Mathf.Cos(b)*9.15f,p.depth*.5f+Mathf.Sin(b)*9.15f);
                }
                foreach (float side in new[] {-1f,1f})
                {
                    float edge=side*x, box=side*(x-16.5f), mid=p.depth*.5f;
                    TownLine(mesh,p,edge,mid-20f,box,mid-20f); TownLine(mesh,p,box,mid-20f,box,mid+20f); TownLine(mesh,p,box,mid+20f,edge,mid+20f);
                    foreach(float z in new[]{mid-3.66f,mid+3.66f}) Box(mesh,p.Point(edge,z),p.Point(edge+.14f,z),0f,2.44f,.14f,4);
                    Box(mesh,p.Point(edge,mid-3.66f),p.Point(edge,mid+3.66f),2.3f,.14f,.14f,4);
                }
            }
            else if(p.landUse==3)
            {
                for(float u=-p.frontage*.5f+6f;u<p.frontage*.5f-4f;u+=3.2f)
                    for(float v=9f;v<p.depth-4f;v+=4.8f)
                    {
                        if(Mathf.Abs(u)<4f||Mathf.Abs(v-p.depth*.5f)<4f)continue;
                        Box(mesh,p.Point(u-.45f,v),p.Point(u+.45f,v),0f,.85f,.22f,4);
                        if(lod==0) Box(mesh,p.Point(u,v+.3f),p.Point(u,v+2.2f),0f,.08f,1.1f,2);
                    }
                // The continuous low wall makes the cemetery's ordered rows read as a distinct enclosure.
                Box(mesh,p.Point(-p.frontage*.5f,0f),p.Point(-p.frontage*.5f,p.depth),0f,.8f,.45f,2);
                Box(mesh,p.Point(p.frontage*.5f,0f),p.Point(p.frontage*.5f,p.depth),0f,.8f,.45f,2);
                Box(mesh,p.Point(-p.frontage*.5f,p.depth),p.Point(p.frontage*.5f,p.depth),0f,.8f,.45f,2);
            }
            else if(p.landUse==1)
            {
                for(float u=-p.frontage*.5f+12f;u<p.frontage*.5f-2f;u+=12f)
                    Box(mesh,p.Point(u,3f),p.Point(u,p.depth-3f),0f,.45f,.16f,1);
                for(float v=14f;v<p.depth-2f;v+=14f)
                    Box(mesh,p.Point(-p.frontage*.5f+3f,v),p.Point(p.frontage*.5f-3f,v),0f,.45f,.16f,1);
            }
            else if(p.landUse==4)
            {
                // Three distribution poles and sagging wires run along the main-road depot frontage.
                for(int i=0;i<3;i++)
                {
                    float u=-p.frontage*.5f+4f+i*(p.frontage-8f)*.5f;
                    Box(mesh,p.Point(u,1f),p.Point(u+.22f,1f),0f,9f,.22f,1);
                    Box(mesh,p.Point(u,0f),p.Point(u,2f),8.3f,.15f,.2f,2);
                    if(i==2)continue;
                    for(int k=0;k<8;k++) foreach(float v in new[]{.2f,1.8f})
                    {
                        float t=k/8f, next=(k+1)/8f, length=(p.frontage-8f)*.5f;
                        Vector3 a=At(p.Point(u+length*t,v),8.5f-2.4f*t*(1f-t));
                        Vector3 b=At(p.Point(u+length*next,v),8.5f-2.4f*next*(1f-next));
                        mesh.Quad(1,a,b,b+Vector3.up*.055f,a+Vector3.up*.055f);
                    }
                }
            }
        }

        // Chalk lines are short ground-following strips, with enough lift to survive the turf surface.
        static void TownLine(VillageMesh mesh,VillagePlot p,float u,float v,float x,float z)
        {
            Vector2 a=p.Point(u,v),b=p.Point(x,z);
            int count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/3f));
            for(int i=0;i<count;i++)Box(mesh,Vector2.Lerp(a,b,i/(float)count),Vector2.Lerp(a,b,(i+1f)/count),.08f,.025f,.16f,4);
        }
    }
}

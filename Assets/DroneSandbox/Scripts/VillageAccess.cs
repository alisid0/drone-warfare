using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Mission access can cross a world village planned earlier; short visibility detours preserve both properties and endpoints.
    internal static class VillageAccess
    {
        // Only sector-generated access is rerouted. Authored world roads and their bridges retain their exact ownership.
        public static void Preserve(List<List<Vector2>> lines, bool tracks = false)
        {
            var plan = MapLayout.Plan;
            if (plan.world == null || MapLayout.DetailPlan == null) return;
            var obstacles = new List<VillagePlot>();
            foreach (var source in MapLayout.DetailPlan.villages.plots)
            {
                var p = source; p.front -= plan.world.center;
                if (Math.Abs(p.front.x) < 1100f && Math.Abs(p.front.y) < 1100f) obstacles.Add(p);
            }
            for (int index = 0; index < (tracks ? lines.Count : Math.Min(lines.Count,plan.roads.Count)); index++)
            {
                bool authored = false;
                if (!tracks) foreach (var road in plan.world.roads) if (ReferenceEquals(plan.roads[index],road.points)) { authored = true; break; }
                if (authored) continue;
                var line = lines[index];
                for (int pass = 0; pass < 12; pass++)
                {
                    int first = -1;
                    for (int i = 1; i < line.Count && first < 0; i++)
                        foreach (var p in obstacles) if (Hits(line[i-1],line[i],p,2.5f)) { first = i-1; break; }
                    if (first < 0) break;
                    int last = first+1;
                    while (last < line.Count-1 && !Free(line[last],obstacles)) last++;
                    while (first > 0 && !Free(line[first],obstacles)) first--;
                    if (!Free(line[first],obstacles) || !Free(line[last],obstacles)) break;
                    var route = Detour(line[first],line[last],obstacles);
                    if (route == null) break;
                    line.RemoveRange(first+1,last-first-1);
                    line.InsertRange(first+1,route);
                }
            }
        }

        // Expanded footprints include the 1.5 m track half-width and its dry verge.
        static bool Free(Vector2 point,List<VillagePlot> plots)
        {
            foreach (var p in plots) if (Hits(point,point,p,2.5f)) return false;
            return true;
        }

        // Slab clipping tests a segment continuously, including a narrow corner missed by point sampling.
        static bool Hits(Vector2 a,Vector2 b,VillagePlot p,float pad)
        {
            p.Coordinates(a,out float au,out float av);p.Coordinates(b,out float bu,out float bv);
            float low=0f,high=1f;
            return Slab(au,bu-au,-p.frontage*.5f-pad,p.frontage*.5f+pad,ref low,ref high)
                && Slab(av,bv-av,-pad,p.depth+pad,ref low,ref high);
        }

        // Parallel segments outside a slab are rejected before any division.
        static bool Slab(float p,float d,float lo,float hi,ref float low,ref float high)
        {
            if(Math.Abs(d)<.0001f)return p>=lo&&p<=hi;
            float a=(lo-p)/d,b=(hi-p)/d;
            low=Math.Max(low,Math.Min(a,b));high=Math.Min(high,Math.Max(a,b));return low<=high;
        }

        // At most 64 nearby properties contribute corners; Dijkstra then chooses a short, dry route between unchanged anchors.
        static List<Vector2> Detour(Vector2 a,Vector2 b,List<VillagePlot> all)
        {
            var plots=new List<VillagePlot>();
            Vector2 lo=Vector2.Min(a,b)-Vector2.one*85f,hi=Vector2.Max(a,b)+Vector2.one*85f;
            foreach(var p in all)
            {
                Vector2 c=p.Point(0f,p.depth*.5f);float radius=(p.frontage+p.depth)*.5f;
                if(c.x+radius<lo.x||c.x-radius>hi.x||c.y+radius<lo.y||c.y-radius>hi.y)continue;
                plots.Add(p);
            }
            if(plots.Count>64)return null;
            var nodes=new List<Vector2>{a,b};
            foreach(var p in plots)
            {
                float half=p.frontage*.5f+2.8f;
                foreach(var q in new[]{p.Point(-half,-2.8f),p.Point(half,-2.8f),p.Point(half,p.depth+2.8f),p.Point(-half,p.depth+2.8f),p.Point(0f,-p.StreetOffset)})
                    if(Free(q,all)&&MapLayout.Plan.world.DryDistance(q)>2f&&!MapLayout.Plan.world.FeatureReserved(q,2f))nodes.Add(q);
            }
            var cost=new float[nodes.Count];var previous=new int[nodes.Count];var visited=new bool[nodes.Count];
            for(int i=0;i<cost.Length;i++){cost[i]=float.MaxValue;previous[i]=-1;}cost[0]=0f;
            for(int step=0;step<nodes.Count;step++)
            {
                int best=-1;float distance=float.MaxValue;
                for(int i=0;i<nodes.Count;i++)if(!visited[i]&&cost[i]<distance){best=i;distance=cost[i];}
                if(best<0)break;if(best==1)break;visited[best]=true;
                for(int i=1;i<nodes.Count;i++)
                {
                    if(visited[i])continue;
                    float next=distance+(nodes[i]-nodes[best]).magnitude;
                    if(next>=cost[i])continue;
                    bool clear=true;
                    foreach(var p in plots)if(Hits(nodes[best],nodes[i],p,2.5f)){clear=false;break;}
                    if(!clear)continue;
                    int samples=Math.Max(1,(int)Math.Ceiling((nodes[i]-nodes[best]).magnitude/6f));
                    for(int k=1;k<samples;k++)
                    {
                        Vector2 at=Vector2.Lerp(nodes[best],nodes[i],k/(float)samples);
                        if(MapLayout.Plan.world.DryDistance(at)<2f||MapLayout.Plan.world.FeatureReserved(at,2f)){clear=false;break;}
                    }
                    if(clear){cost[i]=next;previous[i]=best;}
                }
            }
            if(previous[1]<0)return null;
            var result=new List<Vector2>();
            for(int i=previous[1];i>0;i=previous[i])result.Add(nodes[i]);
            result.Reverse();return result;
        }
    }
}

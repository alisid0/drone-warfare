using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Exports the real world recipe outside the sector, without constructing a dummy local compound.
static partial class LivedInHarness
{
    // The before mesh is the unchanged 9667d1f HLOD; after roots come from the instancer's shared placement source.
    static void ExportLandmark(string kind, string output)
    {
        WorldFeature f = null;
        foreach(var candidate in WorldPlan.Features) if(candidate.valid && candidate.kind.ToString()==kind) { f=candidate; break; }
        if(f==null) throw new Exception("No landmark " + kind);
        var placements=new List<object>();var models=new List<object>();
        foreach(var b in f.buildings)
        {
            Vector2 p=b.position-f.center;
            placements.Add(new { b.model,position=new[]{p.x,f.elevation-.05f,p.y},facing=new[]{b.facing.x,0f,b.facing.y},scale=new[]{b.scale,b.scale,b.scale},nearOnly=b.detail,clutter=false });
            models.Add(new { b.model,at=new[]{p.x,p.y},size=new[]{b.size.x,b.size.y,b.size.z} });
        }
        foreach(var b in f.props)
        {
            Vector2 p=b.position-f.center;
            placements.Add(new { b.model,position=new[]{p.x,f.elevation,p.y},facing=new[]{b.facing.x,0f,b.facing.y},scale=new[]{b.scale.x,b.scale.y,b.scale.z},nearOnly=true,clutter=true });
        }
        // Two sector mission seeds independently confirm every site's yaw, root and scale agree with the recipe.
        foreach(int seed in new[]{4101,4102})
        {
            var sector=WorldSectors.PlanAt(f.center,f.SectorKind,seed);
            foreach(var b in f.buildings)
            {
                bool match=false;
                foreach(var s in sector.sites)if(s.model==b.model && Vector2.Distance(s.position,b.position-f.center)<.001f
                    && Math.Abs(s.yaw-Mathf.Atan2(b.facing.x,b.facing.y)*Mathf.Rad2Deg)<.001f && Math.Abs(s.scale-b.scale)<.001f)match=true;
                if(!match)throw new Exception("Site disagreement " + b.model);
            }
        }
        var heights=new List<float>();var cover=new List<int>();const int n=251;const float step=16f;
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            float wx=f.center.x-2000+x*step,wz=f.center.y-2000+z*step;
            heights.Add(WorldPlan.Height(wx,wz));cover.Add((int)WorldPlan.CoverAt(wx,wz));
        }
        var roads=new List<object>();var rails=new List<object>();
        foreach(var r in f.rails){var line=new List<float[]>();foreach(var p in r)line.Add(new[]{p.x-f.center.x,p.y-f.center.y});rails.Add(line);}
        var far=LandmarkMesh.Build(f,f.center,false,false);var surfaces=LandmarkMesh.Build(f,f.center,true,false);
        File.WriteAllText(Path.Combine(output,kind+".json"),JsonSerializer.Serialize(new {kind,id=f.id,along=new[]{f.along.x,f.along.y},elevation=f.elevation,n,step,heights,cover,roads,rails,placements,models,far=LandmarkData(far),surfaces=LandmarkData(surfaces)},Json));
        Console.WriteLine(kind+": "+f.buildings.Count+" buildings, "+f.props.Count+" props; two sector-seed placement checks passed; outside-sector origin offset 5000 m.");
    }

    // Preserve production triangles and material groups, rather than reconstructing proxy geometry in Blender.
    static object LandmarkData(LandmarkMesh.Data data)
    {
        var vertices=new List<float[]>();var colors=new List<float[]>();
        foreach(var p in data.vertices)vertices.Add(new[]{p.x,p.y,p.z});
        for(int i=0;i<LandmarkMesh.MaterialCount;i++){var c=LandmarkMesh.Color(i);colors.Add(new[]{c.r,c.g,c.b});}
        return new { vertices, triangles=data.triangles,colors };
    }
}

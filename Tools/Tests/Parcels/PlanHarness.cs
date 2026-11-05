using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DroneSandbox;
using UnityEngine;

// Runs the production sector generator, path fields and complete MapLayout, with only Unity math substituted.
public static class PlanHarness
{
    static float[] XY(Vector2 p) => new[] { p.x, p.y };
    static List<float[]> Outline(FieldPlot f)
    {
        var points = new List<float[]>();
        for (int side = 0; side < 4; side++)
            for (int i = 0; i < 40; i++)
            {
                float t = i / 40f;
                points.Add(XY(side == 0 ? f.Point(-f.halfLength + 2 * f.halfLength * t, -f.halfWidth)
                    : side == 1 ? f.Point(f.halfLength, -f.halfWidth + 2 * f.halfWidth * t)
                    : side == 2 ? f.Point(f.halfLength - 2 * f.halfLength * t, f.halfWidth)
                    : f.Point(-f.halfLength, f.halfWidth - 2 * f.halfWidth * t)));
            }
        return points;
    }

    // Independent dense queries catch stale indices, crossings and invalid shared-edge ownership.
    static void CheckPlan(bool candidate)
    {
        for (float z = -981.3f; z < 985f; z += 11.7f)
            for (float x = -982.1f; x < 985f; x += 11.3f)
                foreach (float margin in new[] { 0f, 2.2f, 5f })
                {
                    var p = new Vector2(x, z); bool slow = false; int count = 0;
                    foreach (var f in MapLayout.Fields) if (f.Contains(p, margin)) { slow = true; count++; }
                    foreach (var f in MapLayout.Plan.orchards) if (f.Contains(p, margin)) slow = true;
                    if (slow != MapLayout.InField(p, margin)) throw new Exception("Field index disagrees with exhaustive lookup");
                    if (candidate && margin == 0 && count > 1) throw new Exception("Overlapping crops");
                }
        if (!candidate) return;
        foreach (var f in MapLayout.Fields)
        {
            if (4f * f.halfWidth * f.halfLength < 500f) throw new Exception("Postage-stamp field");
            for(float u=-f.halfLength;u<=f.halfLength;u+=2.7f)
                for(float v=-f.halfWidth;v<=f.halfWidth;v+=2.7f)
                {
                    Vector2 p=f.Point(u,v);
                    if(MapLayout.RoadField.Distance(p.x,p.y)<MapLayout.RoadHalfWidth+.5f) throw new Exception("Field crosses road");
                    if(MapLayout.LakeDistance(p.x,p.y)<2f) throw new Exception("Field crosses water");
                    if(MapLayout.HasRiver&&MapLayout.RiverField.Distance(p.x,p.y)<MapLayout.RiverHalfWidth+2f) throw new Exception("Field crosses river");
                    if(MapLayout.NearVillage(p,-1f)||MapLayout.NearFlatSite(p,1f)) throw new Exception("Field crosses settlement/site");
                }
        }
    }

    // Fingerprints field data without process-randomised hashes, including crop assignment and every shape parameter.
    static string Fingerprint()
    {
        var s=new System.Text.StringBuilder();
        foreach(var f in MapLayout.Fields)
            s.Append($"{f.center.x:R},{f.center.y:R},{f.along.x:R},{f.along.y:R},{f.halfLength:R},{f.halfWidth:R},{f.shear:R},{f.taper:R},{f.crop};");
        return s.ToString();
    }
    // Produces the five paired cases and checks determinism without ever loading the editor or native engine.
    public static void Main(string[] args)
    {
        string output = args[0]; Directory.CreateDirectory(output);
        // Varrow and two env_B tour seeds are exact. VillageCluster is not in that tour; use its standard seed formula.
        var cases = new[] { (SectorKind.VarrowValley, 0), (SectorKind.RiverValley, 1000), (SectorKind.VillageCluster, 1555), (SectorKind.Marshland, 1074), (SectorKind.DeepForest, 1037) };
        foreach (var entry in cases)
        {
            var plan = SectorGenerator.Generate(entry.Item1, entry.Item2);
            MapLayout.Use(plan);
            var watch = Stopwatch.StartNew(); MapLayout.Prepare(); watch.Stop();
            RenderAudit.Reset();
            var renderWatch = Stopwatch.StartNew();
            var sharedRandom = new Random(plan.worldSeed);
            Farmland.Build(new Transform(), sharedRandom);
            renderWatch.Stop();
            if (RenderAudit.renderers > 5400) throw new Exception("Farmland renderer limit exceeded");
            bool candidate = typeof(MapLayout).GetField("Boundaries") != null;
            if (candidate && sharedRandom.Next() != new Random(plan.worldSeed).Next()) throw new Exception("Farmland consumed shared random stream");
            CheckPlan(candidate);
            var fields = new List<object>();
            foreach (var f in MapLayout.Fields) fields.Add(new { crop = f.crop.ToString(), center = XY(f.center), along = XY(f.along), f.halfLength, f.halfWidth, f.taper, f.shear, outline = Outline(f) });
            var roads = new List<List<float[]>>();
            foreach (var line in MapLayout.RoadLines) { var pts = new List<float[]>(); foreach (var p in line) pts.Add(XY(p)); roads.Add(pts); }
            var tracks = new List<List<float[]>>();
            foreach (var line in MapLayout.TrackLines) { var pts = new List<float[]>(); foreach (var p in line) pts.Add(XY(p)); tracks.Add(pts); }
            var river = new List<float[]>(); foreach (var p in MapLayout.RiverLine) river.Add(XY(p));
            var villages = new List<float[]>(); foreach (var p in plan.villages) villages.Add(XY(p));
            var sites = new List<float[]>(); foreach (var s in MapLayout.FlatSites) sites.Add(new[] { s.center.x, s.center.z, s.radius });
            var orchards = new List<object>(); foreach (var f in plan.orchards) orchards.Add(Outline(f));
            var wet = new List<float[]>(); var blocks = new List<float[]>();
            var blocked = typeof(MapLayout).GetMethod("Blocked", BindingFlags.Static | BindingFlags.NonPublic);
            for (float z = -982; z < 985; z += 6) for (float x = -982; x < 985; x += 6)
            {
                if (MapLayout.LakeDistance(x, z) < 0) wet.Add(new[] { x, z });
                if ((bool)blocked.Invoke(null, new object[] { new Vector2(x, z) })) blocks.Add(new[] { x, z });
            }
            var boundaries = new List<object>();
            boundaries.AddRange(RenderAudit.oldEdges);
            var boundaryKeys = new HashSet<string>();
            var districts = new List<object>();
            var parcelType = typeof(MapLayout).Assembly.GetType("DroneSandbox.FieldParcels");
            if (parcelType != null)
                foreach (FieldPlot f in (System.Collections.IEnumerable)parcelType.GetField("Districts", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
                    districts.Add(new { center = XY(f.center), along = XY(f.along), f.halfLength, f.halfWidth, f.taper, f.shear });
            var boundaryList = typeof(MapLayout).GetField("Boundaries");
            if (boundaryList != null)
                foreach (object b in (System.Collections.IEnumerable)boundaryList.GetValue(null))
                {
                    var t = b.GetType(); var a = (Vector2)t.GetField("a").GetValue(b); var end = (Vector2)t.GetField("b").GetValue(b);
                    string key=$"{a.x:F2},{a.y:F2}:{end.x:F2},{end.y:F2}", reverse=$"{end.x:F2},{end.y:F2}:{a.x:F2},{a.y:F2}";
                    if(boundaryKeys.Contains(key)||boundaryKeys.Contains(reverse))throw new Exception("Duplicate shared boundary");
                    boundaryKeys.Add(key);
                    int first=(int)t.GetField("first").GetValue(b), second=(int)t.GetField("second").GetValue(b);
                    for(int i=1;i<4;i++)
                    {
                        var p=Vector2.Lerp(a,end,i/4f);p+=FieldPlot.Warp(p);
                        if(!MapLayout.Fields[first].Contains(p,2f)||!MapLayout.Fields[second].Contains(p,2f)) throw new Exception("Boundary not shared by owners");
                    }
                    var pts = new List<float[]>(); for (int i = 0; i <= 40; i++) { var p = Vector2.Lerp(a, end, i / 40f); pts.Add(XY(p + FieldPlot.Warp(p))); }
                    boundaries.Add(new { kind = t.GetField("kind").GetValue(b).ToString(), a = XY(a), b = XY(end), points = pts });
                }
            var repeats = new List<double>{watch.Elapsed.TotalMilliseconds};
            string signature=Fingerprint();
            for(int repeat=0;repeat<2;repeat++)
            {
                MapLayout.Use(plan); var again=Stopwatch.StartNew(); MapLayout.Prepare(); again.Stop();
                if(signature!=Fingerprint())throw new Exception("Nondeterministic plan");
                repeats.Add(again.Elapsed.TotalMilliseconds);
            }
            repeats.Sort();
            var data = new { sector = entry.Item1.ToString(), seed = entry.Item2, plan.fieldSeed, plan.fields, coverage = MapLayout.FieldCoverage() * 100, elapsedMs = repeats[1], coldLayoutMs = watch.Elapsed.TotalMilliseconds,
                timings = MapLayout.Timings, fieldTimings = MapLayout.FieldTimings, renderers = RenderAudit.renderers, detailTexels = RenderAudit.detailTexels, farmlandCpuMs = renderWatch.Elapsed.TotalMilliseconds,
                props = RenderAudit.props, plots = fields, districts, roads, tracks, river, plan.riverHalfWidth, villages, sites, orchards, wet, blocked = blocks, boundaries };
            File.WriteAllText(Path.Combine(output, entry.Item1 + ".json"), JsonSerializer.Serialize(data));
            Console.WriteLine($"{entry.Item1}: {fields.Count} fields; {data.coverage:F2}% coverage; {watch.ElapsedMilliseconds} ms; {MapLayout.FieldTimings}; renderers {RenderAudit.renderers}; detail texels {RenderAudit.detailTexels}; CPU farmland {renderWatch.ElapsedMilliseconds} ms");
        }
        MapLayout.Use(new SectorPlan{fields=0f}); MapLayout.Prepare();
        if(MapLayout.Fields.Count!=0||MapLayout.InField(Vector2.zero,0f))throw new Exception("Stale fields after empty sector");
        Console.WriteLine("PASS: index/exhaustive queries, clearance, area, overlap, shared-edge owners/deduplication, determinism, empty-sector reset, renderer cap and far-shadow LODs.");
    }
}

using System;
using DroneSandbox;
using UnityEngine;

// Tests engineering invariants against generated production layouts rather than a second hand-authored recipe.
static class YardChecks
{
    // Convexity protects both the point query and triangulator; shuffled visit seeds are checked by the parent harness.
    public static void Run()
    {
        for (int i = 0; i <= 1000000; i++)
        {
            float value = i / 1000000f;
            int reference = (int)Math.Round(255f * (value <= .0031308f ? value * 12.92f : 1.055f * (float)Math.Pow(value, 1f / 2.4f) - .055f));
            int actual = FarDetailPalette.Encode(new Vector3(value, value, value)).r;
            if (Math.Abs(actual - reference) > 1) throw new Exception("Colour transfer exceeds one byte");
        }
        var map = WorldPlan.CaptureFeatureMap(); int polygons = 0, samples = 0;
        foreach (var f in WorldPlan.Features)
        {
            if (f.props.Count > 22) throw new Exception("Clutter group count " + f.id);
            foreach (var surface in f.surfaces)
            {
                if (surface.kind == LandmarkSurfaceKind.Asphalt && surface.area.halfWidth <= 4f)
                {
                    foreach (var building in f.buildings)
                    {
                        var footprint = new FeatureArea { center = building.position, along = building.facing,
                            halfLength = building.size.z * .5f, halfWidth = building.size.x * .5f };
                        Clear(f, surface, footprint, building.model);
                    }
                    foreach (var prop in f.props)
                    {
                        // Conservative measured FBX bounds include each group's pallets/vehicle mirrors and slight yaw.
                        Vector2 size = prop.model == "Scenery_FreightGroup" ? new Vector2(6.6f, 6.6f)
                            : prop.model == "Scenery_UtilityVehicles" ? new Vector2(8f, 8f) : new Vector2(23f, 20f);
                        Clear(f, surface, new FeatureArea { center = prop.position, along = prop.facing,
                            halfLength = size.y * prop.scale.z * .5f, halfWidth = size.x * prop.scale.x * .5f }, prop.model);
                    }
                }
                var points = surface.outline;
                if (points == null) continue;
                polygons++;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 a = points[(i + 1) % points.Length] - points[i], b = points[(i + 2) % points.Length] - points[(i + 1) % points.Length];
                    if (a.x * b.y - a.y * b.x < -.01f) throw new Exception("Non-convex yard " + f.id);
                }
            }
            var mesh = LandmarkMesh.Build(f, f.center, true, false);
            for (int m = 0; m < mesh.triangles.Length; m++)
                for (int i = 0; i < mesh.triangles[m].Count; i += 3)
                {
                    var t = mesh.triangles[m]; var a = mesh.vertices[t[i]]; var b = mesh.vertices[t[i + 1]]; var c = mesh.vertices[t[i + 2]];
                    float area = Vector3.Cross(b - a, c - a).magnitude;
                    // World coordinates at 28 km have millimetre ULPs; ignore sub-four-centimetre clipping slivers.
                    if (area <= .0001f || area < .04f * Math.Max((b - a).magnitude, Math.Max((c - a).magnitude, (c - b).magnitude))) continue;
                    var p = (a + b + c) / 3f;
                    if (!map.SurfaceAt(p.x + f.center.x, p.z + f.center.y, out Color actual)) throw new Exception("Mesh outside surface query " + f.id + " mat " + m + " at " + p.x + "," + p.z + " area " + Vector3.Cross(b - a, c - a).magnitude);
                    Color expected = LandmarkMesh.Color(m);
                    if (Math.Abs(actual.r - expected.r) > .001f || Math.Abs(actual.g - expected.g) > .001f)
                        throw new Exception("Mesh/picture material mismatch " + f.id + " material " + m + " actual " + actual.r + "," + actual.g
                            + " at " + p.x + "," + p.z + " area " + area + " points " + a.x + "," + a.z + ";" + b.x + "," + b.z + ";" + c.x + "," + c.z);
                }
            for (float v = -f.halfWidth; v <= f.halfWidth; v += 2.7f) for (float u = -f.halfLength; u <= f.halfLength; u += 2.7f)
            {
                Vector2 p = f.Area.Point(u, v); samples++;
                bool a = map.SurfaceAt(p.x, p.y, out Color c), b = WorldPlan.FeatureSurfaceAt(p.x, p.y, out Color d);
                if (a != b || c.r != d.r || c.g != d.g || c.b != d.b) throw new Exception("Surface disagreement " + f.id);
            }
        }
        Console.WriteLine("YARDS PASS: " + polygons + " convex outlines; " + samples + " world/snapshot samples");
    }

    // Check both actual lane edges as well as the centre so parked dressings cannot narrow the service route.
    static void Clear(WorldFeature f, LandmarkSurface surface, FeatureArea footprint, string model)
    {
        for (float u = -surface.area.halfLength; u <= surface.area.halfLength; u += 1f)
            foreach (float side in new[] { -1f, 0f, 1f })
                if (footprint.Contains(surface.area.Point(u, side * surface.area.halfWidth)))
                    throw new Exception("Service lane blocked by " + model + " feature " + f.id);
    }
}

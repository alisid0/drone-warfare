using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Rivers choose low corridors in the original relief, then cut a graded, continuous drainage network.
    public static partial class WorldPlan
    {
        // Carve-then-flow: select terrain-guided corridors, fix downstream outlets and grade back upstream.
        static void PlanDrainage(Random rng)
        {
            var main = new WorldRiver { name = "Lissen" };
            Vector2 last = new Vector2(Range(rng, -11500f, -6500f), Half);
            for (int k = 1; k <= 7; k++)
            {
                float z = Half - k * 8000f;
                Vector2 end = new Vector2(-9000f + 3300f * (float)Math.Sin(k * 0.9f + phase) + Range(rng, -1200f, 1200f), z);
                Append(main.points, FindPath(last, end, 0)); last = end;
            }
            Meander(main.points, 300f, 510f, 101);
            GradeRiver(main, 10f, 3.5f, 18f);
            Rivers.Add(main);
            MakeLake(main, ClosestZ(main.points, 11500f), 2800f, 580f, false, "Lake Wenlow");
            string[] names = { "Venn", "Callow", "Oster", "Sorrel Beck", "Alder" };
            float[] joins = { 18500f, 6500f, -3000f, -11500f, -20000f };
            for (int k = 0; k < names.Length; k++)
            {
                int join = ClosestZ(main.points, joins[k] + Range(rng, -1700f, 1700f));
                Vector2 end = main.points[join];
                bool east = k % 2 == 0;
                Vector2 source = new Vector2(east ? Range(rng, 17500f, 24500f) : Range(rng, -25000f, -20000f),
                    Clamp(end.y + Range(rng, 2500f, 7000f), -Half + 1500f, Half - 1200f));
                var river = new WorldRiver { name = names[k] };
                Append(river.points, FindPath(source, end, 0));
                Meander(river.points, 170f, 350f, 102 + k);
                float outletLevel = JoinTrunk(river, main);
                GradeRiver(river, outletLevel, 1.5f, Range(rng, 3.5f, 5f));
                Rivers.Add(river);
                if (k == 0) MakeLake(river, river.points.Count / 3, 4000f, 680f, true, "Varrow Reservoir");
                if (k == 1) MakeLake(river, river.points.Count / 2, 2200f, 440f, false, "Lake Dunmere");
            }
        }

        // Join path sections without repeating their shared endpoint.
        static void Append(List<Vector2> to, List<Vector2> from)
        {
            int first = to.Count > 0 && Distance(to[to.Count - 1], from[0]) < 0.01f ? 1 : 0;
            for (int k = first; k < from.Count; k++) to.Add(from[k]);
        }

        // Choose named confluences along a trunk by latitude, retaining an exact shared vertex.
        static int ClosestZ(List<Vector2> points, float z)
        {
            int best = 0;
            for (int k = 1; k < points.Count; k++) if (Math.Abs(points[k].y - z) < Math.Abs(points[best].y - z)) best = k;
            return best;
        }

        // Stop at the first physical confluence: a meander must not cross the trunk and rejoin it later.
        static float JoinTrunk(WorldRiver tributary, WorldRiver main)
        {
            for (int k = 1; k < tributary.points.Count; k++)
            {
                int segment = -1; float best = float.MaxValue, along = 0f;
                for (int s = 1; s < main.points.Count; s++)
                {
                    float t, u;
                    if (Intersection(tributary.points[k - 1], tributary.points[k], main.points[s - 1], main.points[s], out t, out u) && t < best)
                    { segment = s; best = t; along = u; }
                }
                if (segment < 0) continue;
                Vector2 point = Mix(main.points[segment - 1], main.points[segment], along);
                float level = Lerp(main.waterLevels[segment - 1], main.waterLevels[segment], along);
                if (along < 0.0001f) { point = main.points[segment - 1]; level = main.waterLevels[segment - 1]; }
                else if (along > 0.9999f) { point = main.points[segment]; level = main.waterLevels[segment]; }
                else
                {
                    float width = Lerp(main.halfWidths[segment - 1], main.halfWidths[segment], along);
                    float valley = Lerp(main.valleyLevels[segment - 1], main.valleyLevels[segment], along);
                    main.points.Insert(segment, point); main.halfWidths.Insert(segment, width);
                    main.waterLevels.Insert(segment, level); main.valleyLevels.Insert(segment, valley);
                }
                tributary.points.RemoveRange(k, tributary.points.Count - k);
                if (Distance(tributary.points[k - 1], point) > 0.01f) tributary.points.Add(point);
                else tributary.points[k - 1] = point;
                return level;
            }
            // Collinear final reaches already terminate at the requested trunk vertex.
            Vector2 end = tributary.points[tributary.points.Count - 1];
            for (int k = 0; k < main.points.Count; k++) if (Distance(end, main.points[k]) < 0.01f) return main.waterLevels[k];
            throw new InvalidOperationException("Tributary has no trunk outlet");
        }

        // A strictly falling surface with a gentle minimum gradient, limited cuts and widening downstream.
        static void GradeRiver(WorldRiver river, float outlet, float sourceWidth, float outletWidth)
        {
            int n = river.points.Count;
            for (int k = 0; k < n; k++) { river.waterLevels.Add(0f); river.halfWidths.Add(0f); }
            river.waterLevels[n - 1] = outlet;
            float total = 0f;
            for (int k = 1; k < n; k++) total += Distance(river.points[k - 1], river.points[k]);
            float along = total;
            for (int k = n - 1; k >= 0; k--)
            {
                river.halfWidths[k] = Lerp(sourceWidth, outletWidth, (float)Math.Sqrt(Clamp(along / total, 0f, 1f)));
                if (k == 0) continue;
                float length = Distance(river.points[k - 1], river.points[k]);
                Vector2 p = river.points[k - 1];
                float ground = Height(p.x, p.y);
                float headwaterGrade = 0.009f + 0.10f * Smooth((ground - 170f) / 200f);
                river.waterLevels[k - 1] = Math.Max(river.waterLevels[k] + length * 0.0004f,
                    Math.Min(ground - 3f, river.waterLevels[k] + length * headwaterGrade));
                along -= length;
            }
            river.valleyLevels.AddRange(river.waterLevels);
        }

        // Lakes occupy irregular widened reaches of a real river valley, with an inlet and an outlet.
        // Reservoir tailwater stays below the pool; natural lakes have no artificial step at their outlets.
        static void MakeLake(WorldRiver river, int end, float length, float width, bool reservoir, string name)
        {
            end = Math.Max(12, Math.Min(end, river.points.Count - 4));
            int start = end;
            float distance = 0f;
            while (start > 2 && distance < length) { distance += Distance(river.points[start], river.points[start - 1]); start--; }
            float tailwater = river.waterLevels[end + 1];
            float level = river.waterLevels[end] + (reservoir ? 14f : 0f);
            var lake = new WorldLake { name = name, reservoir = reservoir, level = level, outlet = river.points[end] };
            var right = new List<Vector2>();
            for (int k = start; k <= end; k++) river.waterLevels[k] = level;
            for (int k = start - 1; k >= 0; k--)
                river.waterLevels[k] = Math.Max(river.waterLevels[k], river.waterLevels[k + 1] + Distance(river.points[k], river.points[k + 1]) * 0.0004f);
            int step = Math.Max(1, (end - start) / 32);
            Vector2 inlet = river.points[start], outlet = river.points[end];
            float axisX = outlet.x - inlet.x, axisZ = outlet.y - inlet.y, axisLength = Distance(inlet, outlet);
            float sideX = -axisZ / axisLength, sideZ = axisX / axisLength;
            for (int k = start; ; k = Math.Min(end, k + step))
            {
                float t = (k - start) / (float)(end - start);
                Vector2 chord = Mix(inlet, outlet, t), original = river.points[k];
                // Monotone stations along the valley prevent offset banks folding over tight stream bends.
                float lateral = (original.x - chord.x) * sideX + (original.y - chord.y) * sideZ;
                Vector2 p = new Vector2(chord.x + sideX * lateral, chord.y + sideZ * lateral);
                float shape = (float)Math.Pow(Math.Sin(t * Math.PI), 0.65);
                float half = 7f + width * shape * (0.40f + Noise(p.x / 450f, p.y / 450f, 71) * 0.85f);
                if (reservoir) half += 120f * t * t;
                lake.shore.Add(new Vector2(p.x + sideX * half, p.y + sideZ * half));
                float opposite = half * (0.75f + Noise(p.x / 700f, p.y / 700f, 72) * 0.5f);
                right.Add(new Vector2(p.x - sideX * opposite, p.y - sideZ * opposite));
                if (k == end) break;
            }
            if (reservoir)
                Dams.Add(new WorldDam { name = "Varrow Dam", a = lake.shore[lake.shore.Count - 1], b = right[right.Count - 1], crest = level + 3f, tailwater = tailwater });
            for (int k = right.Count - 1; k >= 0; k--) lake.shore.Add(right[k]);
            Lakes.Add(lake);
        }

        // A segment's data is binned into rows once so each parallel worker owns its grid writes.
        struct WaterSegment
        {
            public Vector2 a, b;
            public float widthA, widthB, levelA, levelB;
            public bool valley;
        }

        // Exact segment distances and levels are rasterised once, including variable-width sub-grid streams.
        static void CarveDrainage()
        {
            var segments = new List<WaterSegment>();
            var rows = new List<int>[GridCount];
            for (int j = 0; j < GridCount; j++) rows[j] = new List<int>();
            foreach (var river in Rivers)
            {
                var valleyPoints = new Vector2[river.points.Count];
                var valleyLevels = new float[river.points.Count];
                for (int k = 0; k < river.points.Count; k++)
                {
                    float x = 0f, z = 0f, level = 0f, weight = 0f;
                    for (int s = Math.Max(0, k - 18); s <= Math.Min(river.points.Count - 1, k + 18); s++)
                    {
                        float w = 19f - Math.Abs(s - k);
                        x += river.points[s].x * w; z += river.points[s].y * w; level += river.valleyLevels[s] * w; weight += w;
                    }
                    valleyPoints[k] = new Vector2(x / weight, z / weight); valleyLevels[k] = level / weight;
                }
                for (int k = 1; k < river.points.Count; k++)
                {
                    var s = new WaterSegment { a = river.points[k - 1], b = river.points[k], widthA = river.halfWidths[k - 1], widthB = river.halfWidths[k], levelA = river.waterLevels[k - 1], levelB = river.waterLevels[k] };
                    BinWaterSegment(segments, rows, s);
                }
                // Broad valleys need fewer segments than the narrow channels; avoid eight redundant distance tests.
                for (int k = 0; k + 1 < river.points.Count; k += 8)
                {
                    int end = Math.Min(k + 8, river.points.Count - 1);
                    var s = new WaterSegment { a = valleyPoints[k], b = valleyPoints[end], levelA = valleyLevels[k], levelB = valleyLevels[end], valley = true };
                    BinWaterSegment(segments, rows, s);
                }
            }
            riverDistance = new float[heights.Length];
            Parallel.For(0, GridCount, j =>
            {
                var distances = new float[GridCount];
                var widths = new float[GridCount];
                var levels = new float[GridCount];
                var valley = new float[GridCount];
                var valleyDistances = new float[GridCount];
                for (int i = 0; i < GridCount; i++) { distances[i] = 4000000f; valleyDistances[i] = 4000000f; }
                float z = -Half + j * HeightStep;
                foreach (int index in rows[j])
                {
                    WaterSegment s = segments[index];
                    float dx = s.b.x - s.a.x, dz = s.b.y - s.a.y, lengthSq = dx * dx + dz * dz;
                    int right = GridIndex(Math.Max(s.a.x, s.b.x) + 2000f);
                    for (int i = GridIndex(Math.Min(s.a.x, s.b.x) - 2000f); i <= right; i++)
                    {
                        float x = -Half + i * HeightStep, t = Clamp(((x - s.a.x) * dx + (z - s.a.y) * dz) / Math.Max(0.001f, lengthSq), 0f, 1f);
                        float ex = x - Lerp(s.a.x, s.b.x, t), ez = z - Lerp(s.a.y, s.b.y, t), d = ex * ex + ez * ez;
                        if (s.valley)
                        {
                            if (d < valleyDistances[i]) { valleyDistances[i] = d; valley[i] = Lerp(s.levelA, s.levelB, t); }
                        }
                        else if (d < distances[i]) { distances[i] = d; widths[i] = Lerp(s.widthA, s.widthB, t); levels[i] = Lerp(s.levelA, s.levelB, t); }
                    }
                }
                for (int i = 0; i < GridCount; i++)
                {
                    int k = j * GridCount + i;
                    float d = (float)Math.Sqrt(distances[i]); riverDistance[k] = d;
                    float vd = (float)Math.Sqrt(valleyDistances[i]);
                    if (d >= 1800f && vd >= 1800f) continue;
                    float x = -Half + i * HeightStep;
                    float broad = Noise(x / 1700f, z / 1700f, 110);
                    float reach = 1100f + 650f * broad;
                    float bed = valley[i] + 2f + vd * vd * (0.000025f + broad * 0.00002f);
                    heights[k] = Lerp(bed, heights[k], Smooth(vd / reach));
                    if (d < 180f)
                    {
                        float channel = levels[i] + 2f + d * d * 0.000035f - 5.5f * (1f - Smooth((d - 30f) / 65f));
                        heights[k] = Lerp(channel, heights[k], Smooth((d - 30f) / 150f));
                    }
                    if (d <= widths[i]) { cover[k] = Cover.Water; waterLevel[k] = levels[i]; }
                }
            });
        }

        // Build row ownership once so distance stamping remains race-free without locks or atomic updates.
        static void BinWaterSegment(List<WaterSegment> segments, List<int>[] rows, WaterSegment segment)
        {
            int index = segments.Count; segments.Add(segment);
            for (int j = GridIndex(Math.Min(segment.a.y, segment.b.y) - 2000f); j <= GridIndex(Math.Max(segment.a.y, segment.b.y) + 2000f); j++) rows[j].Add(index);
        }

        // Rasterise level basins and their banks, rather than painting water over unrelated high ground.
        static void CarveLakes()
        {
            foreach (var lake in Lakes)
            {
                float minX = Half, maxX = -Half, minZ = Half, maxZ = -Half;
                foreach (var p in lake.shore) { minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minZ = Math.Min(minZ, p.y); maxZ = Math.Max(maxZ, p.y); }
                int i0 = GridIndex(minX - 350f), i1 = GridIndex(maxX + 350f), j0 = GridIndex(minZ - 350f), j1 = GridIndex(maxZ + 350f);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        var p = new Vector2(-Half + i * HeightStep, -Half + j * HeightStep);
                        bool inside = InPolygon(p, lake.shore);
                        float distance = float.MaxValue;
                        for (int s = 0; s < lake.shore.Count; s++)
                        {
                            Vector2 a = lake.shore[s], b = lake.shore[(s + 1) % lake.shore.Count];
                            distance = Math.Min(distance, Distance(p, Mix(a, b, Projection(p, a, b))));
                        }
                        int k = j * GridCount + i;
                        if (inside)
                        {
                            heights[k] = lake.level - 2f - Math.Min(12f, distance * 0.035f);
                            waterLevel[k] = lake.level; cover[k] = Cover.Water;
                        }
                        else if (distance < 350f && Distance(p, lake.outlet) > 180f && riverDistance[k] > 100f)
                        {
                            float bank = lake.level + 1f + distance * 0.035f;
                            heights[k] = Lerp(bank, heights[k], Smooth(distance / 350f));
                        }
                    }
            }
        }

        // Point-in-polygon keeps lakes navigable by transport planning without any Unity physics calls.
        static bool InPolygon(Vector2 p, List<Vector2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        // Only three shoreline polygons are tested during route generation, never during Height or CoverAt.
        static bool InLake(Vector2 p)
        {
            foreach (var lake in Lakes) if (InPolygon(p, lake.shore)) return true;
            return false;
        }

        // Preserve sub-grid channel depth after lake-bank earthworks; bilinear terrain must stay below water.
        static void CutChannels()
        {
            foreach (var river in Rivers)
                for (int s = 1; s < river.points.Count; s++)
                {
                    Vector2 a = river.points[s - 1], b = river.points[s];
                    float width = Math.Max(river.halfWidths[s - 1], river.halfWidths[s]) + 32f;
                    for (int j = GridIndex(Math.Min(a.y, b.y) - width); j <= GridIndex(Math.Max(a.y, b.y) + width); j++)
                        for (int i = GridIndex(Math.Min(a.x, b.x) - width); i <= GridIndex(Math.Max(a.x, b.x) + width); i++)
                        {
                            var p = new Vector2(-Half + i * HeightStep, -Half + j * HeightStep);
                            float t = Projection(p, a, b), distance = Distance(p, Mix(a, b, t));
                            if (distance > width) continue;
                            float bed = Lerp(river.waterLevels[s - 1], river.waterLevels[s], t) - 2.5f + distance * 0.018f;
                            int k = j * GridCount + i;
                            heights[k] = Math.Min(heights[k], bed);
                        }
                }
        }
    }
}

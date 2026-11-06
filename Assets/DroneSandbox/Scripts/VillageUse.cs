using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Age and daily use belong to the property, so scenery and every map resolution tell the same story.
    public partial struct VillagePlot
    {
        public float Scale => scale > 0f ? scale : 1f;
        public Vector2 Gate => front + along * DriveU;
        public float DriveU => frontage * .5f - (farm ? 5f : 3.8f);
        public float YardStart => setback + (farm ? 7f : 5f);
        public float YardEnd => Math.Min(depth - (farm ? 12f : 3f), YardStart + (farm ? 29f : 11f));
        public Vector2 Shed => Point(DriveU - 3f, Math.Min(depth - 5f, YardEnd + 1f));

        // A taper and a missing rear corner retain street frontage while breaking the rectangular enclosure.
        public void Limits(float v, out float lo, out float hi)
        {
            lo = -frontage * .5f; hi = frontage * .5f;
            if (shape == 1) { lo += Math.Max(0f, v / depth) * frontage * .19f; hi -= Math.Max(0f, v / depth) * frontage * .06f; }
            if (shape == 2 && v > depth * .64f) lo += frontage * .32f;
        }

        // Local use coordinates stay unchanged when the whole settlement is cropped into a sector.
        public Vector2 Point(float u, float v) => front + along * u + outward * v;

        // Boundary vertices also drive collision and paint; an L-shaped garden never regains its missing corner.
        public Vector2[] Outline()
        {
            float h = frontage * .5f;
            if (shape == 2) return new[] { new Vector2(-h, 0), new Vector2(h, 0), new Vector2(h, depth),
                new Vector2(-h + frontage * .32f, depth), new Vector2(-h + frontage * .32f, depth * .64f), new Vector2(-h, depth * .64f) };
            Limits(depth, out float lo, out float hi);
            return new[] { new Vector2(-h, 0), new Vector2(h, 0), new Vector2(hi, depth), new Vector2(lo, depth) };
        }

        // Work yards use a softened oval and a side drive; doors have a separate narrow approach.
        public int UseAt(float u, float v)
        {
            if (square) return 2;
            if (Math.Abs(u) < pathWidth * .5f && v < setback) return 2;
            if ((farm || shed || (identity & 1) == 0) && Math.Abs(u - DriveU) < driveWidth * .5f && v < YardEnd) return 2;
            return WorkAt(u, v);
        }

        // Ground wear batches working areas separately from exact-width access strips.
        public int WorkAt(float u, float v)
        {
            if (square) return 2;
            float mid = (YardStart + YardEnd) * .5f, ry = (YardEnd - YardStart) * .5f;
            float rx = frontage * (farm ? .38f : .29f);
            float a = (u - (farm ? 0f : frontage * .06f)) / rx, b = (v - mid) / Math.Max(1f, ry);
            if (a * a + b * b < 1f + .08f * (float)Math.Sin(v * .72f + identity % 19)) return farm ? 2 : 1;
            if (cultivated && v > YardEnd + 1f && v < depth - 2f && u < -1f && u > -frontage * .5f + 2.5f)
                return (v - YardEnd) % 2.7f < 1.7f ? 1 : 0;
            if (shed && Math.Abs(u - DriveU + 3f) < 3.8f && Math.Abs(v - Math.Min(depth - 5f, YardEnd + 1f)) < 4f) return 1;
            return untended ? 3 : 0;
        }

        static readonly Vector3 grass = FarDetailPalette.Decode(new Color(.24f, .29f, .13f));
        static readonly Vector3 soil = FarDetailPalette.Decode(new Color(.26f, .20f, .12f));
        static readonly Vector3 gravel = FarDetailPalette.Decode(new Color(.39f, .36f, .29f));
        static readonly Vector3 rough = FarDetailPalette.Decode(new Color(.22f, .26f, .12f));
        static readonly Vector3 hedge = FarDetailPalette.Decode(new Color(.16f, .22f, .10f));

        // Vertex access avoids allocating an outline for each pixel in the regional or world picture.
        Vector2 Corner(int i)
        {
            float h = frontage * .5f;
            if (i == 0) return new Vector2(-h, 0f);
            if (i == 1) return new Vector2(h, 0f);
            Limits(depth, out float lo, out float hi);
            if (i == 2) return new Vector2(hi, depth);
            if (i == 3) return new Vector2(lo, depth);
            return i == 4 ? new Vector2(lo, depth * .64f) : new Vector2(-h, depth * .64f);
        }

        // Match the geometry's partial runs and leave the same front gate and door-path openings in distant paint.
        float BoundaryDistance(Vector2 point)
        {
            int count = shape == 2 ? 6 : 4;
            float nearest = float.MaxValue;
            for (int edge = 0; edge < count; edge++)
            {
                if (edge == 0 && (Math.Abs(point.x) < 1.3f || Math.Abs(point.x - DriveU) < 2f)) continue;
                Vector2 a = Corner(edge), b = Corner((edge + 1) % count);
                if ((identity + edge) % 2 == 0) b = Vector2.Lerp(a, b, .72f);
                else a = Vector2.Lerp(a, b, .28f);
                Vector2 d = b - a;
                float t = Math.Max(0f, Math.Min(1f, Vector2.Dot(point-a,d) / Math.Max(.01f,d.sqrMagnitude)));
                nearest = Math.Min(nearest,(point-a-d*t).magnitude);
            }
            return nearest;
        }

        // Analytic strips retain narrow paths in coarse pixels; larger yard and bed uses are sampled at pixel scale.
        public Vector3 Colour(float u, float v, float pixel, Vector3? lawn = null)
        {
            Vector3 sum = Vector3.zero;
            float q = Math.Min(pixel, 14f) * .25f;
            for (int j = -1; j <= 1; j += 2) for (int i = -1; i <= 1; i += 2)
            {
                int use = WorkAt(u + i * q, v + j * q);
                sum += use == 1 ? soil : use == 2 ? gravel : use == 3 ? rough : lawn ?? grass;
            }
            Vector3 colour = sum * .25f;
            float door = FarDetailData.BandCoverage(pathWidth * .5f - Math.Abs(u), pathWidth * .5f, pixel)
                * FarDetailData.Coverage(setback-v,pixel);
            float drive = farm || shed || (identity & 1) == 0 ? FarDetailData.BandCoverage(driveWidth * .5f - Math.Abs(u-DriveU),driveWidth*.5f,pixel)
                * FarDetailData.Coverage(YardEnd-v,pixel) : 0f;
            colour += (gravel-colour) * Math.Max(door,drive);
            if (boundary != 0)
            {
                float half = boundary == 1 ? .375f : .15f;
                float cover = FarDetailData.BandCoverage(half-BoundaryDistance(new Vector2(u,v)),half,pixel);
                colour += ((boundary == 1 ? hedge : boundary == 2 ? soil : gravel)-colour)*cover;
            }
            return colour;
        }
    }

    // Stratified choices keep small settlements within the requested mixes without changing any other random stream.
    public static class VillageUse
    {
        // Rank independent uses separately: a shed does not imply a vegetable garden or a log pile.
        public static void Assign(List<VillagePlot> plots, int owner, int seed, Vector2 core)
        {
            var ids = new List<int>();
            for (int i = 0; i < plots.Count; i++)
                if (plots[i].owner == owner && (plots[i].church || plots[i].square))
                {
                    var special = plots[i]; special.pathWidth = 1.6f; special.driveWidth = 3f;
                    special.boundary = special.church ? 3 : 0; special.settlement = owner; plots[i] = special;
                }
            for (int i = 0; i < plots.Count; i++) if (plots[i].owner == owner && !plots[i].square && !plots[i].church) ids.Add(i);
            var rng = new Random(unchecked(seed * 7919 + owner * 104729));
            ids.Sort((a, b) => plots[a].old != plots[b].old ? (plots[a].old ? -1 : 1)
                : (plots[a].front - core).sqrMagnitude.CompareTo((plots[b].front - core).sqrMagnitude));
            for (int k = 0; k < ids.Count; k++)
            {
                var p = plots[ids[k]];
                p.old = k < Math.Max(1, (int)Math.Round(ids.Count * .28f));
                p.identity = rng.Next(); p.palette = k % (3 + (owner & 1)); p.settlement = owner;
                p.scale = .89f + (float)rng.NextDouble() * .22f;
                p.pathWidth = 1f + (float)rng.NextDouble() * .8f;
                p.driveWidth = 2.5f + (float)rng.NextDouble();
                if (!p.farm && !p.town) p.setback = p.old ? 5.8f + (float)rng.NextDouble() * 2.1f : 10f + (float)rng.NextDouble() * 6f;
                p.boundary = k % 5 == 0 ? 0 : 1 + k % 3;
                plots[ids[k]] = p;
            }
            for (int use = 0; use < 6; use++)
            {
                for (int i = ids.Count - 1; i > 0; i--) { int j = rng.Next(i + 1), t = ids[i]; ids[i] = ids[j]; ids[j] = t; }
                float rate = use == 0 ? .43f : use == 1 ? .28f : use == 2 ? .15f : use == 3 ? .12f : use == 4 ? .16f : .14f;
                int count = (int)Math.Round(ids.Count * rate);
                for (int k = 0, chosen = 0; k < ids.Count && chosen < count; k++)
                {
                    var p = plots[ids[k]];
                    if (use == 3 && p.cultivated) continue;
                    if (use == 4 && p.farm) continue;
                    chosen++;
                    if (use == 0) p.cultivated = true;
                    if (use == 1) p.shed = true;
                    if (use == 2) p.logs = true;
                    if (use == 3) p.untended = true;
                    if (use == 4) p.shape = 1 + k % 2;
                    if (use == 5) p.roofChanged = true;
                    plots[ids[k]] = p;
                }
            }
        }
    }
}

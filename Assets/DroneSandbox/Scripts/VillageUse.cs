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
        public float StreetOffset => roadWidth + (town ? 1.4f : 1.8f);
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

        // Ground use codes shared by the painters and the sector: 0 lawn, 1 soil, 2 gravel, 3 rough, 4 oil stain, 5 mud,
        // 6 uncut hay meadow (paler than a grazed lawn).
        public const int Lawn = 0, Soil = 1, Gravel = 2, Rough = 3, Stain = 4, Mud = 5, Meadow = 6;

        // Farms, sheds and about half the houses have a drive; town land uses and the village fringe have none of their own.
        public bool HasDrive => (landUse == 0 || landUse == 5) && (farm || shed || (identity & 1) == 0);
        // About three drives in five have a car standing by the house, with a dark drip patch on the gravel.
        public bool HasStain => HasDrive && !farm && (identity >> 11) % 5 < 3;
        // The farm's muck heap stands on the far side of the yard from the drive.
        public Vector2 MuckHeap => Point(-frontage * .5f + 5f, YardStart + 6f);
        float RutU => DriveU - 2.5f;

        // The drive widens where vehicles swing in from the street.
        float DriveHalf(float v) => driveWidth * .5f + Math.Max(0f, 1.8f - v) * .7f;

        // Work yards use a softened oval and a side drive; doors have a separate narrow approach.
        public int UseAt(float u, float v)
        {
            if (landUse > 0 && landUse < 5) return TownUse(u, v);
            if (landUse >= 6) return FringeUse(u, v);
            if (square) return Gravel;
            if (Math.Abs(u) < pathWidth * .5f && v < setback) return Gravel;
            if (HasDrive && Math.Abs(u - DriveU) < DriveHalf(v) && v < YardEnd) return StainAt(u, v) ? Stain : Gravel;
            return WorkAt(u, v);
        }

        // Ground wear batches working areas separately from exact-width access strips.
        public int WorkAt(float u, float v)
        {
            if (landUse > 0 && landUse < 5) return TownUse(u, v);
            if (landUse >= 6) return FringeUse(u, v);
            if (square) return Gravel;
            if (StainAt(u, v)) return Stain;
            if (farm && (Point(u, v) - MuckHeap).sqrMagnitude < 30f) return Mud;
            float mid = (YardStart + YardEnd) * .5f, ry = (YardEnd - YardStart) * .5f;
            float rx = frontage * (farm ? .38f : .29f);
            float a = (u - (farm ? 0f : frontage * .06f)) / rx, b = (v - mid) / Math.Max(1f, ry);
            if (a * a + b * b < 1f + .08f * (float)Math.Sin(v * .72f + identity % 19)) return farm ? FarmYard(u, v) : Soil;
            // Tractors leave by the back of the yard for the fields: two wheel lines to the rear boundary.
            if (farm && v > YardEnd - 2f && Math.Abs(Math.Abs(u - RutU) - .95f) < .32f) return Mud;
            if (cultivated && v > YardEnd + 1f && v < depth - 2f && u < -1f && u > -frontage * .5f + 2.5f)
                return (v - YardEnd) % 2.7f < 1.7f ? Soil : Lawn;
            if (shed && Math.Abs(u - DriveU + 3f) < 3.8f && Math.Abs(v - Math.Min(depth - 5f, YardEnd + 1f)) < 4f) return Soil;
            if (shed && !farm && Trodden(u, v)) return Soil;
            if (untended) return Rough;
            // Gardens are not all kept alike: some let a back corner go.
            return !farm && !cultivated && (identity >> 13) % 10 < 3 && v > depth * .72f && u < -frontage * .1f ? Rough : Lawn;
        }

        // Gravel yard with mud where machinery turns at the gate, in front of the barn and in damp hollows.
        int FarmYard(float u, float v)
        {
            float du = (u - DriveU) / 5f, dv = (v - YardStart) / 6f;
            if (du * du + dv * dv < 1f) return Mud;
            float barnU = frontage < 28f ? 0f : -4f, barnV = Math.Min(depth - 9f, YardEnd + 5f);
            if (Math.Abs(u - barnU) < 6.5f && v > barnV - 13f) return Mud;
            return Math.Sin(u * .31f + identity % 7) * Math.Sin(v * .27f + identity % 5) > .45 ? Mud : Gravel;
        }

        // A parked car's drip patch on the drive beside the house.
        bool StainAt(float u, float v)
        {
            if (!HasStain) return false;
            float a = (u - DriveU) / .9f, b = (v - setback - 3.2f) / 2.3f;
            return a * a + b * b < 1f;
        }

        // A worn line across the lawn from the back door to the shed.
        bool Trodden(float u, float v)
        {
            Vector2 a = new Vector2(0f, setback + 4f), b = new Vector2(DriveU - 3f, Math.Min(depth - 5f, YardEnd + 1f) - 3f), d = b - a;
            float t = Math.Max(0f, Math.Min(1f, Vector2.Dot(new Vector2(u, v) - a, d) / Math.Max(.01f, d.sqrMagnitude)));
            return (new Vector2(u, v) - a - d * t).sqrMagnitude < .2f;
        }

        // Paddocks are grazed short with tussocks, left for hay (paler, cut late) or rough grazing gone to thistle, each
        // with a trodden gateway; orchards keep rough grass under their rows; vegetable plots are beds of bare soil between
        // grass paths with a compost corner; rough grass is an irregular patch that fades out before the plot's edge.
        int FringeUse(float u, float v)
        {
            float h = frontage * .5f, x = u + (identity % 97) * 3.1f, z = v + (identity >> 8) % 89 * 2.7f;
            if (landUse == 6)
            {
                Vector2 gate = new Vector2(h - 4f, 0f);
                if ((new Vector2(u, v) - gate).sqrMagnitude < 26f) return Noise(x * .6f, z * .6f) < .45f ? Mud : Soil;
                if (h - Math.Abs(u) < 1.2f || depth - v < 1.2f) return Rough;
                int kind = (identity >> 5) % 3;
                if (kind == 1) return Noise(x / 7f, z / 7f) > .78f ? Rough : Meadow;
                if (kind == 2) return Noise(x / 3.4f, z / 3.4f) * .6f + Noise(x / 9f, z / 9f) * .4f > .48f ? Rough : Lawn;
                return Noise(x / 2.6f, z / 2.6f) > .7f ? Rough : Lawn;
            }
            if (landUse == 9)
            {
                if (h - Math.Abs(u) < 1.5f || v < 1.5f || depth - v < 1.5f) return Lawn;
                if (u < -h + 4.5f && v > depth - 5f) return Mud; // the compost heap and the shed's trodden corner
                if (Math.Abs(u - (identity % 5 - 2) * 1.5f) < .6f) return Soil; // the path down the middle
                float bed = (v - 1.5f) % 2.6f;
                return bed < 1.7f && Noise(x / 5f, z / 5f) > .22f ? Soil : Lawn;
            }
            if (landUse == 7) return (u + h - 4f) % 7f < 1.6f && u + h > 2.5f && v > 2f && v < depth - 2f ? Rough : Lawn;
            float edge = Math.Min(h - Math.Abs(u), Math.Min(v, depth - v));
            return Noise(x / 11f, z / 11f) * .7f + Noise(x / 4f, z / 4f) * .3f - .4f * Math.Max(0f, 1f - edge / 10f) > .42f ? Rough : Lawn;
        }

        // Smooth lattice noise in plot metres; integer hashing keeps workers and the sector in agreement.
        static float Noise(float x, float z)
        {
            int i = (int)Math.Floor(x), j = (int)Math.Floor(z);
            float a = x - i, b = z - j; a = a * a * (3f - 2f * a); b = b * b * (3f - 2f * b);
            float top = Lattice(i, j) + (Lattice(i + 1, j) - Lattice(i, j)) * a, bottom = Lattice(i, j + 1) + (Lattice(i + 1, j + 1) - Lattice(i, j + 1)) * a;
            return top + (bottom - top) * b;
        }

        // One lattice value in [0, 1].
        static float Lattice(int x, int z)
        {
            uint h = unchecked((uint)x * 374761393u + (uint)z * 668265263u + 2246822519u);
            h = unchecked((h ^ (h >> 13)) * 1274126177u); h ^= h >> 16;
            return (h & 65535) / 65535f;
        }

        // Whether one boundary run is built: edge 0 is the frontage, 1 and the last are the sides, others the rear.
        public bool EdgeShown(int edge)
        {
            if (boundary == 0) return false;
            int last = shape == 2 ? 5 : 3;
            int bit = edge == 0 ? 1 : edge == 1 ? 2 : edge == last ? 8 : 4;
            return (open & bit) == 0;
        }

        static readonly Vector3 grass = FarDetailPalette.Decode(new Color(.24f, .29f, .13f));
        static readonly Vector3 soil = FarDetailPalette.Decode(new Color(.26f, .20f, .12f));
        static readonly Vector3 gravel = FarDetailPalette.Decode(new Color(.39f, .36f, .29f));
        // Rough ground is the sector's dry meadow layer (Ground.Paint layer 4), so near and far agree.
        static readonly Vector3 rough = FarDetailPalette.Means[4];
        static readonly Vector3 hedge = FarDetailPalette.Decode(new Color(.16f, .22f, .10f));
        static readonly Vector3 stain = FarDetailPalette.Decode(new Color(.19f, .17f, .14f));
        static readonly Vector3 mud = FarDetailPalette.Decode(new Color(.21f, .16f, .10f));

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
                if (!EdgeShown(edge) || edge == 0 && (Math.Abs(point.x) < 1.3f || Math.Abs(point.x - DriveU) < 2f)) continue;
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
                sum += use == Soil ? soil : use == Gravel ? gravel : use == Rough ? rough : use == Stain ? stain : use == Mud ? mud
                    : use == Meadow ? ((lawn ?? grass) + rough) * .5f : lawn ?? grass;
            }
            Vector3 colour = sum * .25f;
            float door = landUse < 6 ? FarDetailData.BandCoverage(pathWidth * .5f - Math.Abs(u), pathWidth * .5f, pixel)
                * FarDetailData.Coverage(setback-v,pixel) : 0f;
            float flare = DriveHalf(v);
            float drive = HasDrive ? FarDetailData.BandCoverage(flare - Math.Abs(u-DriveU),flare,pixel)
                * FarDetailData.Coverage(YardEnd-v,pixel) : 0f;
            colour += (gravel-colour) * Math.Max(door,drive);
            if (HasStain)
            {
                float a = (u - DriveU) / .9f, b = (v - setback - 3.2f) / 2.3f;
                colour += (stain - colour) * FarDetailData.Coverage((1f - (float)Math.Sqrt(a * a + b * b)) * .9f, pixel);
            }
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
        // A fifth leave the frontage open and a third let the garden run into the land behind; farms always open at the
        // back for the tractor. Side runs are decided by where they meet the street, so neighbours drop the same one
        // and their gardens merge.
        static int Openings(VillagePlot p)
        {
            int open = 0, h = p.identity >> 7;
            if (h % 5 == 0) open |= 1;
            if (p.farm || (h >> 3) % 3 == 0) open |= 4;
            if (Merged(p.Point(p.frontage * .5f, 0f))) open |= 2;
            if (Merged(p.Point(-p.frontage * .5f, 0f))) open |= 8;
            return open;
        }

        // One shared street corner, on a 6 m grid in plan coordinates: about one in four is left unfenced.
        static bool Merged(Vector2 corner)
        {
            uint h = unchecked((uint)Mathf.FloorToInt(corner.x / 6f) * 73856093u ^ (uint)Mathf.FloorToInt(corner.y / 6f) * 19349663u);
            h = unchecked((h ^ (h >> 13)) * 0x5bd1e995u);
            return (h >> 7) % 4 == 0;
        }

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
                p.old = p.core || k < Math.Max(1, (int)Math.Round(ids.Count * .28f));
                p.identity = rng.Next(); p.palette = k % (3 + (owner & 1)); p.settlement = owner;
                p.scale = .89f + (float)rng.NextDouble() * .22f;
                if (p.landUse == 4) p.scale = 1f;
                p.pathWidth = 1f + (float)rng.NextDouble() * .8f;
                p.driveWidth = 2.5f + (float)rng.NextDouble();
                if (!p.farm && !p.town) p.setback = p.core ? 4.6f + (float)rng.NextDouble() * 1.5f
                    : p.old ? 5.8f + (float)rng.NextDouble() * 2.1f : 10f + (float)rng.NextDouble() * 6f;
                if (p.core || p.landUse == 5) p.shed = true;
                p.boundary = (p.identity >> 3) % 10 < 3 ? 0 : 1 + k % 3;
                p.open = Openings(p);
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

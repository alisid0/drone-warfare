using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // One downstream reach, with interpolated widths and absolute water levels at every clipped vertex.
    public sealed class SectorRiver
    {
        public string name;
        public int source;
        public Vector2[] points;
        public float[] halfWidths, waterLevels;
    }

    // A world road fragment, retaining its class and source even when the terrain boundary splits it.
    public sealed class SectorRoad
    {
        public RoadClass kind;
        public int source;
        public Vector2[] points;
    }

    // Owned snapshot of a world square. All vectors are sector-local; elevations remain absolute metres.
    // Consumers must treat this data as read-only. It survives a later WorldPlan.Generate without changing.
    public sealed class WorldSectorData
    {
        public readonly Vector2 center;
        public readonly int seed;
        public readonly SectorKind kindHint;
        public readonly List<SectorRiver> rivers = new List<SectorRiver>();
        public readonly List<SectorRoad> roads = new List<SectorRoad>();
        public readonly List<WorldLake> lakes = new List<WorldLake>();
        public readonly List<WorldBridge> bridges = new List<WorldBridge>();
        public readonly List<WorldDam> dams = new List<WorldDam>();
        public readonly List<WorldSettlement> settlements = new List<WorldSettlement>();
        public readonly List<WorldFeature> features = new List<WorldFeature>();
        public readonly List<Rect> woods = new List<Rect>();
        public float minimumHeight, maximumHeight, forestShare, fieldShare, townShare;
        public int primaryRiver = -1;

        readonly Vector2 gridOrigin;
        readonly int columns, rows;
        readonly float[] heights, water;
        readonly Cover[] cover;
        float[] dryClearance;

        // Align the snapshot with the world's 32 m samples, preserving its interpolation and cover boundaries.
        internal WorldSectorData(Vector2 origin, SectorKind hint)
        {
            center = origin; seed = WorldPlan.Seed; kindHint = hint;
            // Features are immutable after generation, so retaining these objects also survives regeneration.
            foreach (var f in WorldPlan.Features)
            {
                float featureReach = WorldSectors.Half + f.halfLength + f.halfWidth + f.clearance;
                if (Mathf.Abs(f.center.x - origin.x) < featureReach && Mathf.Abs(f.center.y - origin.y) < featureReach) features.Add(f);
            }
            float reach = WorldSectors.Half + WorldSectors.Margin + WorldPlan.HeightStep;
            float x0 = (float)Math.Floor((origin.x - reach + WorldPlan.Half) / WorldPlan.HeightStep) * WorldPlan.HeightStep - WorldPlan.Half;
            float z0 = (float)Math.Floor((origin.y - reach + WorldPlan.Half) / WorldPlan.HeightStep) * WorldPlan.HeightStep - WorldPlan.Half;
            gridOrigin = new Vector2(x0 - origin.x, z0 - origin.y);
            columns = (int)Math.Ceiling((origin.x + reach - x0) / WorldPlan.HeightStep) + 1;
            rows = (int)Math.Ceiling((origin.y + reach - z0) / WorldPlan.HeightStep) + 1;
            heights = new float[columns * rows]; water = new float[heights.Length]; cover = new Cover[heights.Length];
            minimumHeight = float.MaxValue; maximumHeight = float.MinValue;
            float surveyed = 0f, forest = 0f, fields = 0f, town = 0f;
            for (int z = 0; z < rows; z++)
                for (int x = 0; x < columns; x++)
                {
                    int k = z * columns + x;
                    float wx = x0 + x * WorldPlan.HeightStep, wz = z0 + z * WorldPlan.HeightStep;
                    heights[k] = WorldPlan.Height(wx, wz); cover[k] = WorldPlan.CoverAt(wx, wz); water[k] = WorldPlan.WaterLevel(wx, wz);
                    float lx = wx - origin.x, lz = wz - origin.y;
                    float left = Math.Max(-WorldSectors.Half, lx - 16f), right = Math.Min(WorldSectors.Half, lx + 16f);
                    float bottom = Math.Max(-WorldSectors.Half, lz - 16f), top = Math.Min(WorldSectors.Half, lz + 16f);
                    if (left >= right || bottom >= top) continue;
                    float area = (right - left) * (top - bottom);
                    surveyed += area;
                    if (cover[k] == Cover.Forest) { forest += area; woods.Add(new Rect(left, bottom, right - left, top - bottom)); }
                    if (cover[k] == Cover.Farmland) fields += area;
                    if (cover[k] == Cover.Town) town += area;
                    minimumHeight = Math.Min(minimumHeight, heights[k]); maximumHeight = Math.Max(maximumHeight, heights[k]);
                }
            forestShare = forest / surveyed; fieldShare = fields / surveyed; townShare = town / surveyed;
        }

        // Bilinear absolute ground height, including the margin used for roads and their bank approaches.
        public float Height(float x, float z)
        {
            float gx = Mathf.Clamp((x - gridOrigin.x) / WorldPlan.HeightStep, 0f, columns - 1);
            float gz = Mathf.Clamp((z - gridOrigin.y) / WorldPlan.HeightStep, 0f, rows - 1);
            int ix = Math.Min((int)gx, columns - 2), iz = Math.Min((int)gz, rows - 2), k = iz * columns + ix;
            return Mathf.Lerp(Mathf.Lerp(heights[k], heights[k + 1], gx - ix),
                Mathf.Lerp(heights[k + columns], heights[k + columns + 1], gx - ix), gz - iz);
        }

        // Cover uses the world's nearest-sample convention, including negative coordinates and exact half steps.
        public Cover CoverAt(float x, float z) => cover[Index(x, z)];

        // The grid's standing/channel water is useful for coarse surveys; vectors resolve sub-grid streams.
        public float WaterLevel(float x, float z) => water[Index(x, z)];

        // Convert a local position to its nearest stored world sample without allocating or consulting globals.
        int Index(float x, float z)
        {
            int ix = Mathf.Clamp((int)Math.Floor((x - gridOrigin.x) / WorldPlan.HeightStep + 0.5f), 0, columns - 1);
            int iz = Mathf.Clamp((int)Math.Floor((z - gridOrigin.y) / WorldPlan.HeightStep + 0.5f), 0, rows - 1);
            return iz * columns + ix;
        }

        // Exact nearest centre line and its width/level; separate reaches never gain fictitious connecting segments.
        public float RiverDistance(Vector2 p, out float width, out float level)
        {
            float best = float.MaxValue; width = 0f; level = float.NaN;
            foreach (var river in rivers)
                for (int i = 1; i < river.points.Length; i++)
                {
                    float t = WorldSectors.Projection(p, river.points[i - 1], river.points[i]);
                    float d = (p - Vector2.Lerp(river.points[i - 1], river.points[i], t)).sqrMagnitude;
                    if (d >= best) continue;
                    best = d; width = Mathf.Lerp(river.halfWidths[i - 1], river.halfWidths[i], t);
                    level = Mathf.Lerp(river.waterLevels[i - 1], river.waterLevels[i], t);
                }
            return best == float.MaxValue ? 100000f : Mathf.Sqrt(best);
        }

        // Build separate, linearly resampled reaches for PathField; smoothing again would move world channels.
        public List<List<Vector2>> RiverLines(float spacing = 2f)
        {
            if (spacing <= 0f || float.IsNaN(spacing)) throw new ArgumentOutOfRangeException(nameof(spacing));
            var lines = new List<List<Vector2>>();
            foreach (var river in rivers)
            {
                var line = new List<Vector2>();
                for (int i = 1; i < river.points.Length; i++)
                {
                    int count = Math.Max(1, Mathf.CeilToInt(Vector2.Distance(river.points[i - 1], river.points[i]) / spacing));
                    for (int k = 0; k < count; k++) line.Add(Vector2.Lerp(river.points[i - 1], river.points[i], k / (float)count));
                }
                line.Add(river.points[river.points.Length - 1]); lines.Add(line);
            }
            return lines;
        }

        // The original shoreline is retained beyond the crop, so the crop edge is never mistaken for a shore.
        public float LakeDistance(Vector2 p, out float level)
        {
            float best = 100000f; level = float.NaN;
            foreach (var lake in lakes)
            {
                float d = ShoreDistance(p, lake.shore);
                if (d < best) { best = d; level = lake.level; }
            }
            return best;
        }

        // Signed polygon distance handles concave shores and squares entirely covered by a large lake.
        internal static float ShoreDistance(Vector2 p, IList<Vector2> shore)
        {
            float best = float.MaxValue; bool inside = false;
            for (int i = 0, j = shore.Count - 1; i < shore.Count; j = i++)
            {
                Vector2 a = shore[j], b = shore[i];
                float t = WorldSectors.Projection(p, a, b);
                best = Mathf.Min(best, (p - Vector2.Lerp(a, b, t)).sqrMagnitude);
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return Mathf.Sqrt(best) * (inside ? -1f : 1f);
        }

        // A conservative dry-ground distance for compounds and access tracks; marsh cover also needs avoiding.
        public float DryDistance(Vector2 p)
        {
            Cover c = CoverAt(p.x, p.y);
            if (c == Cover.Water || c == Cover.Wetland) return 0f;
            if (dryClearance != null)
            {
                int k = Index(p.x, p.y);
                Vector2 sample = gridOrigin + new Vector2(k % columns, k / columns) * WorldPlan.HeightStep;
                // A conservative two-metres-per-metre bound includes width gradients at river joins. Only use
                // it well away from water; footprint and routing decisions near shores remain exact.
                float bound = dryClearance[k] - 2f * Vector2.Distance(p, sample);
                if (bound > 32f) return bound;
            }
            return VectorClearance(p);
        }

        // Whether p is water or within `margin` of it, cheaply: the snapshot's cover and the precomputed clearance
        // only, never the exact vector distance. Ground asks this of a million terrain samples to keep its small
        // roughness off the water; near a reservoir the exact fallback made the terrain take 4.5 s.
        public bool NearWater(Vector2 p, float margin)
        {
            Cover c = CoverAt(p.x, p.y);
            if (c == Cover.Water || c == Cover.Wetland) return true;
            if (dryClearance == null) return DryDistance(p) <= margin;
            int k = Index(p.x, p.y);
            Vector2 sample = gridOrigin + new Vector2(k % columns, k / columns) * WorldPlan.HeightStep;
            return dryClearance[k] - 2f * Vector2.Distance(p, sample) <= margin;
        }

        // Precompute a cheap rejection bound once after all water geometry is copied, saving route-grid queries.
        internal void PrepareDryGround()
        {
            dryClearance = new float[heights.Length];
            for (int k = 0; k < dryClearance.Length; k++)
                dryClearance[k] = VectorClearance(gridOrigin + new Vector2(k % columns, k / columns) * WorldPlan.HeightStep);
        }

        // Nearest bank, rather than nearest centre line, is essential where a wide trunk meets a narrow stream.
        float VectorClearance(Vector2 p)
        {
            float best = LakeDistance(p, out _);
            foreach (var river in rivers)
                for (int i = 1; i < river.points.Length; i++)
                {
                    float t = WorldSectors.Projection(p, river.points[i - 1], river.points[i]);
                    float d = Vector2.Distance(p, Vector2.Lerp(river.points[i - 1], river.points[i], t))
                        - Mathf.Lerp(river.halfWidths[i - 1], river.halfWidths[i], t);
                    best = Mathf.Min(best, d);
                }
            return best;
        }

        // Detail and local earthworks fade out before the terrain edge, where the global height must win exactly.
        public float DetailWeight(float x, float z)
        {
            float weight = Mathf.SmoothStep(0f, 1f, (WorldSectors.Half - Mathf.Max(Mathf.Abs(x), Mathf.Abs(z))) / 100f);
            Vector2 p = new Vector2(x, z) + center;
            foreach (var f in features)
            {
                Vector2 d = p - f.center;
                float outside = Mathf.Max(Mathf.Abs(Vector2.Dot(d, f.along)) - f.halfLength,
                    Mathf.Abs(Vector2.Dot(d, f.Area.Across)) - f.halfWidth);
                weight *= Mathf.SmoothStep(0f, 1f, (outside - f.clearance) / 48f);
            }
            return weight;
        }

        // Mission placement and route searches use the snapshot, never mutable world-generation state.
        public bool FeatureReserved(Vector2 local, float margin)
        {
            foreach (var f in features) if (f.Area.Contains(local + center, f.clearance + margin)) return true;
            return false;
        }
    }
}

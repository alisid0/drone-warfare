using System;
using UnityEngine;

namespace DroneSandbox
{
    // Lake polygons retain their real shoreline at every painting scale, independently of the 32 m cover grid.
    public sealed partial class FarDetailData
    {
        readonly LakeShore[] lakes;

        // Copy the few small polygons once so background workers never read the next world's mutable plan.
        sealed class LakeShore
        {
            public readonly Vector2[] points;
            public readonly float minX, minZ, maxX, maxZ;
            public readonly bool reservoir;
            const float Cell = 16f;
            readonly int columns, rows;
            readonly float[] distances;

            // Bounds reject almost all land pixels before a polygon edge is visited.
            public LakeShore(WorldLake lake)
            {
                points = lake.shore.ToArray(); reservoir = lake.reservoir;
                minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue;
                foreach (var p in points)
                {
                    minX = Math.Min(minX, p.x); minZ = Math.Min(minZ, p.y);
                    maxX = Math.Max(maxX, p.x); maxZ = Math.Max(maxZ, p.y);
                }
                columns = Math.Max(2, (int)Math.Ceiling((maxX - minX) / Cell) + 1);
                rows = Math.Max(2, (int)Math.Ceiling((maxZ - minZ) / Cell) + 1);
                distances = new float[columns * rows];
                for (int j = 0; j < rows; j++) for (int i = 0; i < columns; i++)
                    distances[j * columns + i] = Exact(minX + i * Cell, minZ + j * Cell);
            }

            // A signed distance is Lipschitz: beyond this bound neither shore colour nor coverage can change.
            public float Distance(float x, float z, float pixel)
            {
                int i = Math.Max(0, Math.Min(columns - 1, (int)Math.Floor((x - minX) / Cell + .5f)));
                int j = Math.Max(0, Math.Min(rows - 1, (int)Math.Floor((z - minZ) / Cell + .5f)));
                float cached = distances[j * columns + i];
                float error = Math.Abs(x - minX - i * Cell) + Math.Abs(z - minZ - j * Cell);
                if (Math.Abs(cached) > 8f + pixel + error) return cached;
                return Exact(x, z);
            }

            // Only shoreline pixels need the exact polygon distance; concave shores and islands stay correct.
            float Exact(float x, float z)
            {
                float distance = float.MaxValue; bool inside = false;
                for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                {
                    Vector2 a = points[j], b = points[i];
                    float dx = b.x - a.x, dz = b.y - a.y, len = dx * dx + dz * dz;
                    float t = len > 0f ? Clamp(((x - a.x) * dx + (z - a.y) * dz) / len, 0f, 1f) : 0f;
                    float ex = x - a.x - t * dx, ez = z - a.y - t * dz;
                    distance = Math.Min(distance, ex * ex + ez * ez);
                    if ((a.y > z) != (b.y > z) && x < dx * (z - a.y) / dz + a.x) inside = !inside;
                }
                return (float)Math.Sqrt(distance) * (inside ? -1f : 1f);
            }
        }

        // A signed shore distance prevents coarse water-cover pixels bleeding into dry land or river mouths.
        float LakeBank(float x, float z, float pixel, out bool reservoir)
        {
            float best = float.MaxValue; reservoir = false;
            float pad = 8f + pixel;
            foreach (var lake in lakes)
            {
                if (x < lake.minX - pad || x > lake.maxX + pad || z < lake.minZ - pad || z > lake.maxZ + pad) continue;
                float shore = lake.Distance(x, z, pixel);
                if (shore < best) { best = shore; reservoir = lake.reservoir; }
            }
            return best;
        }

        // Integrate both sides of the shallow strip; narrow streams never acquire an exaggerated blue width.
        static Vector3 PaintRiver(Vector3 ground, float bank, float width, float pixel)
        {
            if (bank > 4f + pixel) return ground;
            float margin = 3f;
            ground = Blend(ground, WaterAppearance.Gravel, BandCoverage(margin - bank, width + margin, pixel));
            ground = Blend(ground, WaterAppearance.WetMud, BandCoverage(1.6f - bank, width + 1.6f, pixel));
            float wet = BandCoverage(-bank, width, pixel);
            float shallowWidth = Math.Min(2.2f, width * .45f);
            float deep = BandCoverage(-bank - shallowWidth, Math.Max(0f, width - shallowWidth), pixel);
            return ground * (1f - wet) + WaterAppearance.River * deep + WaterAppearance.Shallows * (wet - deep);
        }

        // Reservoirs expose a pale draw-down strip, natural lakes a narrower gravel shore, then green shallows.
        static Vector3 PaintLake(Vector3 ground, float bank, bool reservoir, float pixel)
        {
            if (bank > 8f + pixel) return ground;
            float margin = reservoir ? 5f : 2.5f;
            ground = Blend(ground, WaterAppearance.Gravel, Coverage(margin - bank, pixel));
            ground = Blend(ground, WaterAppearance.WetMud, Coverage(1.6f - bank, pixel));
            float wet = Coverage(-bank, pixel), deep = Coverage(-bank - 7f, pixel);
            return ground * (1f - wet) + WaterAppearance.Lake * deep + WaterAppearance.Shallows * (wet - deep);
        }

        // The sector collar paints land only; preserve water and its margins where a river leaves the sector.
        public bool NearWaterPaint(float x, float z, float pixel)
        {
            float bank = float.MaxValue;
            long key = ((long)(int)Math.Floor(x / Size) << 32) ^ (uint)(int)Math.Floor(z / Size);
            if (riverCells.TryGetValue(key, out var nearby)) bank = Bank(x, z, nearby, out _);
            return bank < 5f + pixel || LakeBank(x, z, pixel, out _) < 8f + pixel;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Answers "how far is this point from the river / the nearest road?" quickly. Each line segment is
    // stamped once into a 1 m grid of cells around it, and a query then measures the exact distance to
    // the segments stored at the four surrounding cells. Every line point can carry a value (the river's
    // water level, a road's height), which queries interpolate along the nearest segment.
    // A field that reaches far (the river's, which the hills open into a valley round) is only stamped at 1 m for
    // its first FineReach metres; further out a coarse 8 m grid stamped to the full reach answers, near enough for
    // the shape of a valley, so a 2 km map's river doesn't stamp half a billion cells.
    public class PathField
    {
        const float Cell = 1f, CoarseCell = 8f, FineReach = 24f;

        public readonly List<Vector2> points = new List<Vector2>();
        public float[] values; // one per point, filled in by whoever builds the field

        readonly float maxDistance, fineReach, half;
        readonly int n, coarseN;
        readonly float[] distance, coarseDistance;
        readonly int[] segment, coarseSegment;
        readonly List<bool> segmentValid = new List<bool>(); // false where one line ends and the next starts

        public PathField(IEnumerable<List<Vector2>> lines, float maxDistance, float size)
        {
            this.maxDistance = maxDistance;
            fineReach = Mathf.Min(maxDistance, FineReach);
            half = size / 2f;
            n = Mathf.CeilToInt(size / Cell) + 1;
            distance = new float[n * n];
            segment = new int[n * n];
            for (int i = 0; i < distance.Length; i++) distance[i] = fineReach;
            if (maxDistance > FineReach)
            {
                coarseN = Mathf.CeilToInt(size / CoarseCell) + 1;
                coarseDistance = new float[coarseN * coarseN];
                coarseSegment = new int[coarseN * coarseN];
                for (int i = 0; i < coarseDistance.Length; i++) coarseDistance[i] = maxDistance;
            }

            foreach (var line in lines)
            {
                int start = points.Count;
                points.AddRange(line);
                for (int i = 0; i < line.Count; i++) segmentValid.Add(i < line.Count - 1);
                for (int i = 0; i < line.Count - 1; i++) Stamp(start + i);
            }
            values = new float[points.Count];
        }

        void Stamp(int s)
        {
            Stamp(s, Cell, n, fineReach, distance, segment);
            if (coarseDistance != null) Stamp(s, CoarseCell, coarseN, maxDistance + CoarseCell, coarseDistance, coarseSegment);
        }

        void Stamp(int s, float cell, int count, float reach, float[] distances, int[] segments)
        {
            Vector2 a = points[s], b = points[s + 1];
            int x0 = Index(Mathf.Min(a.x, b.x) - reach, cell), x1 = Index(Mathf.Max(a.x, b.x) + reach, cell) + 1;
            int z0 = Index(Mathf.Min(a.y, b.y) - reach, cell), z1 = Index(Mathf.Max(a.y, b.y) + reach, cell) + 1;
            for (int cz = Mathf.Max(z0, 0); cz <= Mathf.Min(z1, count - 1); cz++)
            {
                for (int cx = Mathf.Max(x0, 0); cx <= Mathf.Min(x1, count - 1); cx++)
                {
                    var c = new Vector2(-half + cx * cell, -half + cz * cell);
                    float d = SegmentDistance(c, s, out _);
                    int k = cz * count + cx;
                    if (d < distances[k])
                    {
                        distances[k] = d;
                        segments[k] = s;
                    }
                }
            }
        }

        int Index(float coordinate, float cell) => Mathf.FloorToInt((coordinate + half) / cell);

        float SegmentDistance(Vector2 p, int s, out float t)
        {
            Vector2 a = points[s], ab = points[s + 1] - a;
            float len2 = ab.sqrMagnitude;
            t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        public float Distance(float x, float z) => Query(x, z, out _, out _);

        // Distance to the nearest line (capped at maxDistance), the value there, and which segment it is.
        public float Query(float x, float z, out float value, out int nearestSegment)
        {
            value = 0f;
            nearestSegment = -1;
            float best = Nearest(x, z, Cell, n, fineReach, distance, segment, ref value, ref nearestSegment);
            if (best < fineReach || coarseDistance == null) return nearestSegment < 0 ? maxDistance : best;
            return Nearest(x, z, CoarseCell, coarseN, maxDistance, coarseDistance, coarseSegment, ref value, ref nearestSegment);
        }

        // The nearest of the segments stored at the four grid points round (x, z), closer than cap.
        float Nearest(float x, float z, float cell, int count, float cap, float[] distances, int[] segments, ref float value, ref int nearestSegment)
        {
            var p = new Vector2(x, z);
            int ix = Mathf.Clamp(Index(x, cell), 0, count - 2), iz = Mathf.Clamp(Index(z, cell), 0, count - 2);
            float best = cap;
            for (int dz = 0; dz <= 1; dz++)
            {
                for (int dx = 0; dx <= 1; dx++)
                {
                    int k = (iz + dz) * count + ix + dx;
                    if (distances[k] >= cap) continue;
                    int s = segments[k];
                    float d = SegmentDistance(p, s, out float t);
                    if (d < best)
                    {
                        best = d;
                        value = Mathf.Lerp(values[s], values[s + 1], t);
                        nearestSegment = s;
                    }
                }
            }
            return best;
        }

        // Unit direction of a segment returned by Query.
        public Vector2 Direction(int s) => s < 0 ? Vector2.up : (points[s + 1] - points[s]).normalized;

        public bool IsValidSegment(int s) => s >= 0 && s < segmentValid.Count && segmentValid[s];
    }
}

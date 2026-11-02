using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Answers "how far is this point from the river / the nearest road?" quickly. Each line segment is
    // stamped once into a 1 m grid of cells around it, and a query then measures the exact distance to
    // the segments stored at the four surrounding cells. Every line point can carry a value (the river's
    // water level, a road's height), which queries interpolate along the nearest segment.
    public class PathField
    {
        const float Cell = 1f;

        public readonly List<Vector2> points = new List<Vector2>();
        public float[] values; // one per point, filled in by whoever builds the field

        readonly float maxDistance, half;
        readonly int n;
        readonly float[] distance;
        readonly int[] segment;
        readonly List<bool> segmentValid = new List<bool>(); // false where one line ends and the next starts

        public PathField(IEnumerable<List<Vector2>> lines, float maxDistance, float size)
        {
            this.maxDistance = maxDistance;
            half = size / 2f;
            n = Mathf.CeilToInt(size / Cell) + 1;
            distance = new float[n * n];
            segment = new int[n * n];
            for (int i = 0; i < distance.Length; i++) distance[i] = maxDistance;

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
            Vector2 a = points[s], b = points[s + 1];
            int x0 = Index(Mathf.Min(a.x, b.x) - maxDistance), x1 = Index(Mathf.Max(a.x, b.x) + maxDistance) + 1;
            int z0 = Index(Mathf.Min(a.y, b.y) - maxDistance), z1 = Index(Mathf.Max(a.y, b.y) + maxDistance) + 1;
            for (int cz = Mathf.Max(z0, 0); cz <= Mathf.Min(z1, n - 1); cz++)
            {
                for (int cx = Mathf.Max(x0, 0); cx <= Mathf.Min(x1, n - 1); cx++)
                {
                    var c = new Vector2(-half + cx * Cell, -half + cz * Cell);
                    float d = SegmentDistance(c, s, out _);
                    int k = cz * n + cx;
                    if (d < distance[k])
                    {
                        distance[k] = d;
                        segment[k] = s;
                    }
                }
            }
        }

        int Index(float coordinate) => Mathf.FloorToInt((coordinate + half) / Cell);

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
            var p = new Vector2(x, z);
            int ix = Mathf.Clamp(Index(x), 0, n - 2), iz = Mathf.Clamp(Index(z), 0, n - 2);
            float best = maxDistance;
            value = 0f;
            nearestSegment = -1;
            for (int dz = 0; dz <= 1; dz++)
            {
                for (int dx = 0; dx <= 1; dx++)
                {
                    int k = (iz + dz) * n + ix + dx;
                    if (distance[k] >= maxDistance) continue;
                    int s = segment[k];
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

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Immutable spatial buckets avoid scanning a whole world's roads for each narrow road pixel.
    public sealed class RoadNetwork
    {
        const float Cell = 64f;
        readonly Dictionary<long, int[]> cells = new Dictionary<long, int[]>();
        public readonly FarDetailData.Segment[] segments;
        readonly FarDetailData.Segment[] queries;
        readonly int[] sourceIndices;
        readonly float[] lengths;

        // A road query carries chainage and its signed cross section, including a small junction splay.
        public struct Sample
        {
            public bool found;
            public RoadClass kind;
            public int route, segment;
            public float width, distance, across, along, edge, junction, endFade;
            public Vector2 point, direction;
        }

        // Segments include a generous filter border, so tile gutters and coarse world pixels see the same roads.
        public RoadNetwork(FarDetailData.Segment[] source)
        {
            segments = (FarDetailData.Segment[])source.Clone();
            queries = Compact(source, out sourceIndices);
            lengths = new float[queries.Length];
            var lists = new Dictionary<long, List<int>>();
            for (int i = 0; i < queries.Length; i++)
            {
                var s = queries[i]; float dx = s.bx - s.ax, dz = s.bz - s.az;
                lengths[i] = (float)Math.Sqrt(dx * dx + dz * dz);
                float pad = Math.Max(s.widthA, s.widthB) + 40f;
                for (int z = Index(Math.Min(s.az, s.bz) - pad); z <= Index(Math.Max(s.az, s.bz) + pad); z++)
                    for (int x = Index(Math.Min(s.ax, s.bx) - pad); x <= Index(Math.Max(s.ax, s.bx) + pad); x++)
                    {
                        long key = Key(x, z);
                        if (!lists.TryGetValue(key, out var list)) lists.Add(key, list = new List<int>());
                        list.Add(i);
                    }
            }
            foreach (var pair in lists) cells.Add(pair.Key, pair.Value.ToArray());
        }

        // Sector roads have two-metre terrain vertices; straight runs need only one distance query per 64 metres.
        static FarDetailData.Segment[] Compact(FarDetailData.Segment[] input, out int[] indices)
        {
            var result = new List<FarDetailData.Segment>(); var starts = new List<int>();
            for (int i = 0; i < input.Length; i++)
            {
                var b = input[i]; bool merge = false;
                if (result.Count > 0)
                {
                    var a = result[result.Count - 1];
                    float dx = a.bx - a.ax, dz = a.bz - a.az, ex = b.bx - b.ax, ez = b.bz - b.az;
                    float la = dx * dx + dz * dz, lb = ex * ex + ez * ez, cross = dx * ez - dz * ex;
                    merge = a.route == b.route && a.kind == b.kind && a.bx == b.ax && a.bz == b.az
                        && a.widthA == a.widthB && a.widthB == b.widthA && b.widthA == b.widthB
                        && la > .001f && lb > .001f && dx * ex + dz * ez > 0f && cross * cross < la * lb * .000001f
                        && (b.bx - a.ax) * (b.bx - a.ax) + (b.bz - a.az) * (b.bz - a.az) <= 4096f
                        && Math.Abs(a.walked + (float)Math.Sqrt(la) - b.walked) < .02f;
                    if (merge) { a.bx = b.bx; a.bz = b.bz; result[result.Count - 1] = a; }
                }
                if (!merge) { result.Add(b); starts.Add(i); }
            }
            indices = starts.ToArray(); return result.ToArray();
        }

        // The nearest physical edge wins; independent crossing directions supply a bounded two-metre corner splay.
        public Sample At(float x, float z)
        {
            var hit = new Sample { edge = float.MaxValue, endFade = 1f };
            if (!cells.TryGetValue(Key(Index(x), Index(z)), out var nearby)) return hit;
            foreach (int i in nearby)
            {
                var candidate = Measure(i, x, z);
                if (candidate.edge < hit.edge) hit = candidate;
            }
            if (!hit.found || hit.kind == RoadClass.Track || hit.edge > 4f) return hit;
            float second = float.MaxValue;
            foreach (int i in nearby)
            {
                var s = queries[i];
                if (s.route == hit.route || s.kind == (int)RoadClass.Track || lengths[i] < .01f) continue;
                float dot = Math.Abs(((s.bx - s.ax) * hit.direction.x + (s.bz - s.az) * hit.direction.y) / lengths[i]);
                if (dot > .92f) continue;
                var other = Measure(i, x, z);
                second = Math.Min(second, other.edge);
            }
            hit.junction = FarDetailData.Clamp((4f - second) / 4f, 0f, 1f);
            float h = Math.Max(0f, 1f - Math.Abs(hit.edge - second) / 4f);
            hit.edge -= h * h; // polynomial union, maximum one metre added at the inside corner
            return hit;
        }

        // Route-specific projection recovers clipped chainage even when the fragment starts exactly on a junction.
        public Sample RouteAt(float x, float z, int route)
        {
            var hit = new Sample { edge = float.MaxValue };
            if (!cells.TryGetValue(Key(Index(x), Index(z)), out var nearby)) return hit;
            foreach (int i in nearby)
            {
                if (queries[i].route != route) continue;
                var candidate = Measure(i, x, z);
                if (candidate.edge < hit.edge) hit = candidate;
            }
            return hit;
        }

        // Finite endpoints and carried chainage prevent paint leaking beyond a gateway or restarting at a tile seam.
        Sample Measure(int i, float x, float z)
        {
            var s = queries[i]; float length = lengths[i];
            if (length < .001f) return new Sample { edge = float.MaxValue };
            float dx = (s.bx - s.ax) / length, dz = (s.bz - s.az) / length;
            float projected = (x - s.ax) * dx + (z - s.az) * dz;
            float t = FarDetailData.Clamp(projected, 0f, length);
            float px = s.ax + dx * t, pz = s.az + dz * t;
            float rx = x - px, rz = z - pz, d = (float)Math.Sqrt(rx * rx + rz * rz);
            float along = s.walked + t, width = FarDetailData.Mix(s.widthA, s.widthB, t / length);
            if (s.kind == (int)RoadClass.Track) width = RoadAppearance.TrackWidth(along);
            return new Sample { found = true, route = s.route, segment = sourceIndices[i], kind = (RoadClass)s.kind, width = width,
                distance = d, edge = d - width, across = rx * -dz + rz * dx, along = along,
                point = new Vector2(px, pz), direction = new Vector2(dx, dz),
                endFade = projected < 0f || projected > length ? Math.Max(0f, 1f - Math.Abs(projected - t) / 1.5f) : 1f };
        }

        // Bucket coordinates stay stable west and south of the world origin.
        static int Index(float x) => (int)Math.Floor(x / Cell);

        // Signed cell pairs have unique keys without allocation.
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}

using System;
using UnityEngine;

namespace DroneSandbox
{
    // Walk just the heightfield cells a ray crosses. This also serves live tiles beyond the collider bubble,
    // so far map orders hit the visible triangle rather than the old chunk or the fallback sea-level plane.
    public static class FarDetailRay
    {
        // Horizontal slab clipping bounds the traversal to at most 258 cells, even for a nearly level ray.
        public static bool Hit(FarDetailData.Tile tile, Vector2 origin, Ray ray, out float distance)
        {
            distance = float.MaxValue;
            float x0 = tile.x * 512f - origin.x, z0 = tile.z * 512f - origin.y;
            float start = 0f, end = 80000f;
            if (!Slab(x0, x0 + 512f, ray.origin.x, ray.direction.x, ref start, ref end) ||
               !Slab(z0, z0 + 512f, ray.origin.z, ray.direction.z, ref start, ref end)) return false;
            float px = ray.origin.x + ray.direction.x * start - x0, pz = ray.origin.z + ray.direction.z * start - z0;
            int i = Math.Max(0, Math.Min(127, (int)Math.Floor((px + ray.direction.x * 0.001f) / 4f)));
            int j = Math.Max(0, Math.Min(127, (int)Math.Floor((pz + ray.direction.z * 0.001f) / 4f)));
            int di = ray.direction.x > 0 ? 1 : -1, dj = ray.direction.z > 0 ? 1 : -1;
            float tx = Math.Abs(ray.direction.x) < 1e-7f ? float.MaxValue : (x0 + (i + (di > 0 ? 1 : 0)) * 4f - ray.origin.x) / ray.direction.x;
            float tz = Math.Abs(ray.direction.z) < 1e-7f ? float.MaxValue : (z0 + (j + (dj > 0 ? 1 : 0)) * 4f - ray.origin.z) / ray.direction.z;
            float dx = Math.Abs(ray.direction.x) < 1e-7f ? float.MaxValue : 4f / Math.Abs(ray.direction.x);
            float dz = Math.Abs(ray.direction.z) < 1e-7f ? float.MaxValue : 4f / Math.Abs(ray.direction.z);
            Vector3 localOrigin = new Vector3(ray.origin.x - x0, ray.origin.y, ray.origin.z - z0);
            for (int visits = 0; visits < 260 && i >= 0 && i < 128 && j >= 0 && j < 128; visits++)
            {
                int a = j * 129 + i, b = a + 1, c = a + 129, d = c + 1;
                float stop = Math.Min(end, Math.Min(tx, tz));
                var v = tile.vertices;
                float first = Triangle(localOrigin, ray.direction, v[a], v[c], v[b]);
                float second = Triangle(localOrigin, ray.direction, v[b], v[c], v[d]);
                float hit = Math.Min(first, second);
                if (hit >= start - 0.01f && hit <= stop + 0.01f) { distance = hit; return true; }
                if (stop >= end) break;
                if (tx < tz) { start = tx; tx += dx; i += di; }
                else { start = tz; tz += dz; j += dj; }
            }
            return false;
        }

        // Parallel axes must lie within the tile's closed extent, including a straight-down ray on its edge.
        static bool Slab(float lo, float hi, float at, float speed, ref float enter, ref float leave)
        {
            if (Math.Abs(speed) < 1e-7f) return at >= lo && at <= hi;
            float a = (lo - at) / speed, b = (hi - at) / speed;
            enter = Math.Max(enter, Math.Min(a, b)); leave = Math.Min(leave, Math.Max(a, b));
            return leave >= enter;
        }

        // Two-sided Moller-Trumbore intersection agrees with mesh topology without calling the physics engine.
        static float Triangle(Vector3 o, Vector3 dir, Vector3 a, Vector3 b, Vector3 c)
        {
            float ex = b.x - a.x, ey = b.y - a.y, ez = b.z - a.z, fx = c.x - a.x, fy = c.y - a.y, fz = c.z - a.z;
            float px = dir.y * fz - dir.z * fy, py = dir.z * fx - dir.x * fz, pz = dir.x * fy - dir.y * fx;
            float determinant = ex * px + ey * py + ez * pz;
            if (Math.Abs(determinant) < 1e-8f) return float.MaxValue;
            float inv = 1f / determinant, tx = o.x - a.x, ty = o.y - a.y, tz = o.z - a.z;
            float u = (tx * px + ty * py + tz * pz) * inv;
            if (u < -1e-5f || u > 1.00001f) return float.MaxValue;
            float qx = ty * ez - tz * ey, qy = tz * ex - tx * ez, qz = tx * ey - ty * ex;
            float v = (dir.x * qx + dir.y * qy + dir.z * qz) * inv;
            if (v < -1e-5f || u + v > 1.00001f) return float.MaxValue;
            float t = (fx * qx + fy * qy + fz * qz) * inv;
            return t >= 0f ? t : float.MaxValue;
        }
    }
}

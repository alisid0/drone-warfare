using UnityEngine;

namespace DroneSandbox
{
    // A small immutable river profile is built before terrain workers, never during a frame or a pixel query.
    public sealed class WatercourseProfile
    {
        readonly PathField field;
        readonly float[] widths, bends, pools;

        // Cache bend handedness and broad reach variation once, respecting breaks between independent rivers.
        public WatercourseProfile()
        {
            field = MapLayout.RiverField;
            int n = field.points.Count;
            widths = new float[n]; bends = new float[n]; pools = new float[n];
            var source = MapLayout.Plan.world;
            Vector2 origin = source != null ? source.center : Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = field.points[i], world = p + origin;
                pools[i] = WaterAppearance.Pool(world.x, world.y);
                float width = MapLayout.RiverHalfWidth;
                if (source != null) source.RiverDistance(p, out width, out _);
                widths[i] = MapLayout.HasDam && source == null && MapLayout.NearDam(p, 60f) ? MapLayout.RiverWidthAt(p.x, p.y)
                    : WaterAppearance.HalfWidth(width, world.x, world.y);
                int a = i, b = i;
                while (a > 0 && i - a < 16 && field.IsValidSegment(a - 1)) a--;
                while (b + 1 < n && b - i < 16 && field.IsValidSegment(b)) b++;
                Vector2 incoming = (p - field.points[a]).normalized, outgoing = (field.points[b] - p).normalized;
                bends[i] = Mathf.Clamp((incoming.x * outgoing.y - incoming.y * outgoing.x) * 4f, -1f, 1f);
            }
        }

        // The existing spatial field supplies one nearest segment; all other work is a few interpolations.
        public bool Sample(float x, float z, out float distance, out float level, out float width,
            out float margin, out float depth, out float slope)
        {
            distance = field.Query(x, z, out level, out int s);
            width = margin = depth = slope = 0f;
            if (!field.IsValidSegment(s)) return false;
            Vector2 a = field.points[s], ab = field.points[s + 1] - a, offset = new Vector2(x, z) - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(offset, ab) / ab.sqrMagnitude) : 0f;
            float side = Mathf.Sign(ab.x * offset.y - ab.y * offset.x);
            float inner = side * Mathf.Lerp(bends[s], bends[s + 1], t);
            float pool = Mathf.Lerp(pools[s], pools[s + 1], t);
            width = Mathf.Lerp(widths[s], widths[s + 1], t);
            margin = WaterAppearance.Margin(inner, pool);
            float k = Mathf.Clamp01(distance / Mathf.Max(.1f, width));
            depth = .20f + (1f - k * k) * (1.05f + 1.25f * pool - .3f * inner);
            slope = Mathf.Lerp(.62f, .25f, (inner + 1f) * .5f);
            return true;
        }
    }
}

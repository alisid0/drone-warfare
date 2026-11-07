using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Retain each clipped mesh's original parcel so Farmland can keep rows and headlands continuous at sector cuts.
    public static class FarFieldSurface
    {
        static readonly Dictionary<FieldPlot, FieldPlot> parents = new Dictionary<FieldPlot, FieldPlot>();
        static readonly Dictionary<FieldPlot, float> shades = new Dictionary<FieldPlot, float>();

        // A new world sector replaces the mapping; no reference to an older sector or its resources is retained.
        internal static void Clear() { parents.Clear(); shades.Clear(); }

        // Multiple geometry fragments can share one physical field and therefore one detail-map coordinate frame,
        // and one shade (FarFieldPlan.Shade), so the crop mesh matches the distant painter's colour for that parcel.
        internal static void Register(FieldPlot fragment, FieldPlot parent, float shade)
        {
            parents[fragment] = parent;
            shades[parent] = shade;
        }

        // Legacy sectors keep their current material coordinates; only registered world fragments use a parent.
        public static bool TryParent(FieldPlot fragment, out FieldPlot parent)
        {
            if (MapLayout.Plan.world != null && parents.TryGetValue(fragment, out parent)) return true;
            parent = fragment;
            return false;
        }

        // A field's own shade (0-1): its world parcel's, or for a legacy field one hashed from where it lies.
        public static float Shade(FieldPlot fragment)
        {
            if (TryParent(fragment, out var parent) && shades.TryGetValue(parent, out float shade)) return shade;
            return FieldAppearance.Shade((int)Math.Floor(parent.center.x * 4f), (int)Math.Floor(parent.center.y * 4f), (int)parent.crop, 17);
        }

        // Physical world metres keep the fine rows straight even when a parcel's outline tapers or shears.
        public static Vector2 RowUv(FieldPlot plot, Vector2 point, float metres)
        {
            Vector2 world = point + MapLayout.Plan.world.center;
            return new Vector2(Vector2.Dot(world, plot.Across), Vector2.Dot(world, plot.along)) / metres;
        }
    }
}

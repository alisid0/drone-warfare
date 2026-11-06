using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Retain each clipped mesh's original parcel so Farmland can keep rows and headlands continuous at sector cuts.
    public static class FarFieldSurface
    {
        static readonly Dictionary<FieldPlot, FieldPlot> parents = new Dictionary<FieldPlot, FieldPlot>();

        // A new world sector replaces the mapping; no reference to an older sector or its resources is retained.
        internal static void Clear() => parents.Clear();

        // Multiple geometry fragments can share one physical field and therefore one detail-map coordinate frame.
        internal static void Register(FieldPlot fragment, FieldPlot parent) => parents[fragment] = parent;

        // Legacy sectors keep their current material coordinates; only registered world fragments use a parent.
        public static bool TryParent(FieldPlot fragment, out FieldPlot parent)
        {
            if (MapLayout.Plan.world != null && parents.TryGetValue(fragment, out parent)) return true;
            parent = fragment;
            return false;
        }

        // Physical world metres keep the fine rows straight even when a parcel's outline tapers or shears.
        public static Vector2 RowUv(FieldPlot plot, Vector2 point, float metres)
        {
            Vector2 world = point + MapLayout.Plan.world.center;
            return new Vector2(Vector2.Dot(world, plot.Across), Vector2.Dot(world, plot.along)) / metres;
        }
    }
}

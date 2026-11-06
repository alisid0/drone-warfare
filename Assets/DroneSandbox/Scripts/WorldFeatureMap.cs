using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Immutable, indexed surface/grade snapshot for tile workers that can outlive the next world generation.
    public sealed class WorldFeatureMap
    {
        // Copy value data so no worker reads the mutable WorldPlan lists or a later feature layout.
        sealed class Entry
        {
            public FeatureArea area;
            public float clearance;
            public LandmarkSurface[] surfaces;
        }

        readonly Entry[][] bins = new Entry[WorldPlan.CellCount * WorldPlan.CellCount][];

        // Called once alongside FarDetailData's height and cover capture, before tile workers are started.
        internal WorldFeatureMap(IList<WorldFeature> features)
        {
            var lists = new List<Entry>[bins.Length];
            foreach (var f in features)
            {
                var entry = new Entry { area = f.Area, clearance = f.clearance, surfaces = f.surfaces.ToArray() };
                float reach = f.halfLength + f.halfWidth + f.clearance + 64f;
                int first = Index(f.center.x - reach, f.center.y - reach), last = Index(f.center.x + reach, f.center.y + reach);
                for (int z = first / WorldPlan.CellCount; z <= last / WorldPlan.CellCount; z++)
                    for (int x = first % WorldPlan.CellCount; x <= last % WorldPlan.CellCount; x++)
                    {
                        int k = z * WorldPlan.CellCount + x;
                        if (lists[k] == null) lists[k] = new List<Entry>();
                        lists[k].Add(entry);
                    }
            }
            for (int i = 0; i < bins.Length; i++) if (lists[i] != null) bins[i] = lists[i].ToArray();
        }

        // Surface colour uses unwarped world metres and sRGB, matching WorldPlan.FeatureSurfaceAt.
        public bool SurfaceAt(float x, float z, out Color color)
        {
            var entries = bins[Index(x, z)]; var p = new Vector2(x, z);
            if (entries != null) foreach (var entry in entries)
            {
                if (!entry.area.Contains(p, entry.clearance)) continue;
                for (int i = entry.surfaces.Length - 1; i >= 0; i--)
                    if (entry.surfaces[i].area.Contains(p)) { color = WorldLandmarkLayout.SurfaceColor(entry.surfaces[i].kind); return true; }
            }
            color = default; return false;
        }

        // Zero selects the captured world height; one permits ordinary tile roughness and coarse-edge blending.
        public float DetailWeight(float x, float z)
        {
            var entries = bins[Index(x, z)]; if (entries == null) return 1f;
            float weight = 1f; var p = new Vector2(x, z);
            foreach (var entry in entries)
            {
                Vector2 d = p - entry.area.center;
                float outside = Math.Max(Math.Abs(Vector2.Dot(d, entry.area.along)) - entry.area.halfLength,
                    Math.Abs(Vector2.Dot(d, entry.area.Across)) - entry.area.halfWidth);
                float t = Math.Max(0f, Math.Min(1f, (outside - entry.clearance) / 48f));
                weight *= t * t * (3f - 2f * t);
            }
            return weight;
        }

        // Closed boundary handling matches the world's kilometre index without consulting generation state.
        static int Index(float x, float z)
        {
            int i = Math.Max(0, Math.Min(WorldPlan.CellCount - 1, (int)((x + WorldPlan.Half) / WorldPlan.CellSize)));
            int j = Math.Max(0, Math.Min(WorldPlan.CellCount - 1, (int)((z + WorldPlan.Half) / WorldPlan.CellSize)));
            return j * WorldPlan.CellCount + i;
        }
    }

    // Expose the worker snapshot without requiring any edits in the other session's terrain files.
    public static partial class WorldPlan
    {
        // Capture once while the world is ready; all later queries are allocation-free and independent of globals.
        public static WorldFeatureMap CaptureFeatureMap() => new WorldFeatureMap(Features);
    }
}

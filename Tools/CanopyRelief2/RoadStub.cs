using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // Road ribbons and roadside instances only make Unity objects; the forest replay needs none of them.
    public static class RoadSurface
    {
        public static int Renderers => 0;
        public static readonly List<RoadGate> Entrances = new List<RoadGate>();
        public static void Build(Transform parent) { }
        public static void SetRain(bool wet) { }
    }

    // See RoadSurface: no roadside trees, poles or gates are built offline.
    public static class Roadside
    {
        public static void Build(Transform parent, ref int allowance) { }
    }
}

using System.Collections.Generic;
using UnityEngine;

// HDRP itself needs Unity's native runtime. These are the existing Water.cs property signatures, not engine API substitutes.
namespace UnityEngine.Rendering.HighDefinition
{
    public enum WaterSurfaceType { River, OceanSeaLake }
    public enum WaterGeometryType { Custom }
    public enum WaterPropertyOverrideMode { Inherit }
    public class WaterSurface : Component
    {
        public WaterSurfaceType surfaceType;
        public WaterGeometryType geometryType;
        public WaterPropertyOverrideMode ripplesMotionMode;
        public readonly List<MeshRenderer> meshRenderers = new List<MeshRenderer>();
        public Color refractionColor, scatteringColor;
        public float absorptionDistance, largeWindSpeed, largeChaos, largeBand0Multiplier, largeBand1Multiplier,
            ripplesWindSpeed, ripplesChaos, largeCurrentSpeedValue, largeCurrentMapInfluence, simulationFoamAmount,
            foamPersistenceMultiplier, repetitionSize, startSmoothness, endSmoothness, smoothnessFadeStart, smoothnessFadeDistance;
        public bool foam, supportSimulationFoamMask;
        public AnimationCurve simulationFoamWindCurve;
        public Texture2D largeCurrentMap, simulationFoamMask, waterMask;
        public Vector2 largeCurrentRegionExtent, largeCurrentRegionOffset, simulationFoamMaskExtent, simulationFoamMaskOffset,
            waterMaskExtent, waterMaskOffset;
    }
}

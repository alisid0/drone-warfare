// Complementary world-stable coverage intervals fade meshes, cards and canopy in every HDRP raster pass.
float4 _ForestCamera;
float4 _ForestDistances;
float _ForestTier;
float _ForestBuild;

void ForestFade(float3 positionRWS)
{
    float3 world = GetAbsolutePositionWS(positionRWS);
    float3 pivot = GetAbsolutePositionWS(GetObjectToWorldMatrix()._m03_m13_m23);
    float d = distance(_ForestTier < 3.0 ? pivot : world, _ForestCamera.xyz);
    float full = 1.0 - smoothstep(max(0.0, _ForestDistances.x - 10.0), max(0.01, _ForestDistances.x), d);
    float mesh = 1.0 - smoothstep(max(0.0, _ForestDistances.y - 20.0), max(0.01, _ForestDistances.y), d);
    float card = 1.0 - smoothstep(max(0.0, _ForestDistances.z - 35.0), max(0.01, _ForestDistances.z), d);
    float low = _ForestTier < 0.5 ? 0.0 : _ForestTier < 1.5 ? full : _ForestTier < 2.5 ? mesh : card;
    float high = _ForestTier < 0.5 ? full : _ForestTier < 1.5 ? mesh : _ForestTier < 2.5 ? card : 1.0;
    // Quantized world noise remains attached to foliage during a flight and is shared by the depth/shadow passes.
    float3 cell = floor(world * 20.0);
    float noise = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
    if (_ForestBuild >= 0.0) clip(_ForestBuild - noise);
    else clip(noise + _ForestBuild);
    clip(noise - low - 0.00001);
    clip(high - noise - 0.00001);
}

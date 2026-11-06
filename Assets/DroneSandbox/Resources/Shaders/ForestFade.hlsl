// Complementary world-stable coverage intervals fade meshes, cards and canopy in every HDRP raster pass.
void ForestFade(float3 positionRWS)
{
    // Invalid/unbound draw parameters fail open. Material properties live in UnityPerMaterial in every pass.
    if (_ForestEnabled < 0.5 || _ForestDistances.w < 1.0 || _ForestDistances.z < _ForestDistances.y
        || _ForestDistances.y < _ForestDistances.x) return;
    float3 world = GetAbsolutePositionWS(positionRWS);
    // HDRP applies camera translation to UNITY_MATRIX_M in ordinary, instanced AND DOTS variants.
    // Undo it exactly once, just as for positionRWS; _ForestCamera is an absolute Unity scene position.
    float3 pivot = GetAbsolutePositionWS(GetObjectToWorldMatrix()._m03_m13_m23);
    float d = distance(_ForestTier < 3.0 ? pivot : world, _ForestCamera.xyz);
    float full = 1.0 - smoothstep(max(0.0, _ForestDistances.x - 10.0), max(0.01, _ForestDistances.x), d);
    float mesh = 1.0 - smoothstep(max(0.0, _ForestDistances.y - 20.0), max(0.01, _ForestDistances.y), d);
    float card = 1.0 - smoothstep(max(0.0, _ForestDistances.z - 35.0), max(0.01, _ForestDistances.z), d);
    float low = _ForestTier < 0.5 ? 0.0 : _ForestTier < 1.5 ? full : _ForestTier < 2.5 ? mesh : card;
    float high = _ForestTier < 0.5 ? full : _ForestTier < 1.5 ? mesh : _ForestTier < 2.5 ? card : 1.0;
    // Quantized world noise remains attached to foliage during a flight and is shared by the depth/shadow passes.
    uint3 cell = asuint((int3)floor(world * 20.0));
    uint hash = cell.x * 73856093u ^ cell.y * 19349663u ^ cell.z * 83492791u;
    hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15;
    float noise = ((hash & 65535u) + 0.5) / 65536.0;
    if (_ForestBuild >= 0.0) clip(_ForestBuild - noise);
    else clip(noise + _ForestBuild);
    clip(noise - low);
    clip(high - noise);
}

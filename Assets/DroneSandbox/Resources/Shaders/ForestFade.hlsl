// Complementary world-stable coverage intervals fade meshes, cards and canopy in every HDRP raster pass.
void ForestFade(float3 positionRWS)
{
    // Invalid/unbound draw parameters fail open. Material properties live in UnityPerMaterial in every pass.
    if (_ForestEnabled < 0.5 || _ForestCamera.w < 0.5 || _ForestDistances.w < 1.0 || _ForestDistances.z < _ForestDistances.y
        || _ForestDistances.y < _ForestDistances.x) return;
    float3 world = GetAbsolutePositionWS(positionRWS);
    // HDRP applies camera translation to UNITY_MATRIX_M in ordinary, instanced AND DOTS variants.
    // Undo it exactly once, just as for positionRWS; _ForestCamera is an absolute Unity scene position.
    if (abs(_ForestProjection.w) < 0.01) return;
    float3 center = _ForestTier < 3.0 ? GetAbsolutePositionWS(mul(GetObjectToWorldMatrix(), float4(_ForestCrown.xyz, 1.0)).xyz) : world;
    float radius = _ForestTier < 3.0 ? length(mul((float3x3)GetObjectToWorldMatrix(), _ForestRadius.xyz)) : 2.0;
    float depth = _ForestProjection.w < 0.0 ? 1.0 : max(1.0, dot(center - _ForestSelection.xyz, _ForestProjection.xyz) + (_ForestTier < 3.0 ? 0.0 : 32.0));
    float d = 1000.0 * depth / max(0.01, 2.0 * radius * abs(_ForestProjection.w));
    float full = _ForestDistances.x > 0.0 ? 1.0 - smoothstep(_ForestDistances.x * 0.9, _ForestDistances.x, d) : 0.0;
    float mesh = _ForestDistances.y > 0.0 ? 1.0 - smoothstep(_ForestDistances.y * 0.9, _ForestDistances.y, d) : 0.0;
    float card = _ForestDistances.z > 0.0 ? 1.0 - smoothstep(_ForestDistances.z * 0.9, _ForestDistances.z, d) : 0.0;
    // Only completed cells may open the canopy. Crowns beyond them can arrive independently without shrinking the entire view.
    if (_ForestTier >= 3.0)
    {
        card *= 1.0 - smoothstep(max(0.0, _ForestSelection.w - 32.0), max(0.01, _ForestSelection.w), distance(world.xz, _ForestSelection.xz));
        float3 delta = world - _ForestSelection.xyz;
        float3 right = cross(float3(0.0, 1.0, 0.0), _ForestProjection.xyz);
        right = dot(right, right) > 0.001 ? normalize(right) : float3(1.0, 0.0, 0.0);
        float3 up = cross(_ForestProjection.xyz, right);
        float viewDepth = dot(delta, _ForestProjection.xyz);
        float2 margin = (_ForestProjection.w < 0.0 ? 1.0 : viewDepth) * _ForestViewport.xy - abs(float2(dot(delta, right), dot(delta, up)));
        card *= saturate(min(margin.x, margin.y) / 24.0) * step(0.0, viewDepth);
    }
    float low = _ForestTier < 0.5 ? 0.0 : _ForestTier < 1.5 ? full : _ForestTier < 2.5 ? mesh : card;
    float high = _ForestTier < 0.5 ? full : _ForestTier < 1.5 ? mesh : _ForestTier < 2.5 ? card : 1.0;
    // Most foliage pixels are outside a transition; skip dither hashing in colour, depth and shadow passes there.
    if (_ForestBuild >= 1.0 && low <= 0.0 && high >= 1.0) return;
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

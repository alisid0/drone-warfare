// Hash a world-space block, independently of the streamed mesh and terrain origin.
uint ForestBlockHash(int2 block)
{
    uint h = asuint(block.x) * 73856093u ^ asuint(block.y) * 19349663u;
    h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
    return h;
}

// Two independent crown maps get a stable rotation and offset per block; rotate slopes back to world axes.
float3 ForestBlockDetail(float2 world, int2 block, float2 dx, float2 dy)
{
    uint hash = ForestBlockHash(block);
    float angle = (hash & 65535u) * (6.2831853 / 65536.0);
    float sn, cs; sincos(angle, sn, cs);
    float2x2 rotation = float2x2(cs, -sn, sn, cs);
    float2 uv = mul(rotation, world / 128.0) + float2((hash >> 16) & 255u, (hash >> 24) & 255u) / 256.0;
    float4 sampleValue;
    if ((hash & 65536u) == 0u)
        sampleValue = SAMPLE_TEXTURE2D_GRAD(_ForestDetail, sampler_ForestDetail, uv, mul(rotation, dx), mul(rotation, dy));
    else
        sampleValue = SAMPLE_TEXTURE2D_GRAD(_ForestDetailB, sampler_ForestDetail, uv, mul(rotation, dx), mul(rotation, dy));
    return float3(sampleValue.r, mul(transpose(rotation), sampleValue.gb * 2.0 - 1.0));
}

// Three triangular weights hide block boundaries without the four texture reads of a square-grid blend.
float3 ForestCrownDetail(float2 world)
{
    float2 grid = world / 96.0;
    int2 cell = (int2)floor(grid);
    float2 f = frac(grid);
    int2 corner = f.x > f.y ? int2(1, 0) : int2(0, 1);
    float3 weights = float3(1.0 - max(f.x, f.y), abs(f.x - f.y), min(f.x, f.y));
    weights *= weights; weights /= dot(weights, float3(1.0, 1.0, 1.0));
    float2 dx = 0.0, dy = 0.0;
#if !defined(SHADER_STAGE_RAY_TRACING)
    dx = ddx(world) / 128.0; dy = ddy(world) / 128.0;
#endif
    return ForestBlockDetail(world, cell, dx, dy) * weights.x
        + ForestBlockDetail(world, cell + corner, dx, dy) * weights.y
        + ForestBlockDetail(world, cell + int2(1, 1), dx, dy) * weights.z;
}

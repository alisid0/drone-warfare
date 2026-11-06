// Global resources are shared by chunk and tile materials; every new symbol has the Far prefix for HDRP.
TEXTURE2D_ARRAY(_FarRegionAlbedo);
SAMPLER(sampler_FarRegionAlbedo);
TEXTURE2D(_FarWorldPicture);
SAMPLER(sampler_FarWorldPicture);
float4 _FarRegionPages[81];
float _FarRegionEnabled, _FarRegionTime;

// Signed modulo agrees with the CPU's toroidal slot selection, including negative world coordinates.
int FarRegionIndex(int2 cell)
{
    int2 slot = (int2)((float2)cell - 9.0 * floor((float2)cell / 9.0));
    return slot.y * 9 + slot.x;
}

// Publication age fades a completed page in; stale or not-yet-uploaded pages contribute only the world picture.
float FarRegionReady(int2 cell)
{
    float4 page = _FarRegionPages[FarRegionIndex(cell)];
    return page.w * (all(page.xy == (float2)cell) ? smoothstep(0, 0.75, _FarRegionTime - page.z) : 0);
}

// Equal edge weights on both pages hide asynchronous arrivals without a line at an unavailable neighbour.
float FarRegionWeight(int2 cell, float2 local)
{
    float own = FarRegionReady(cell);
    int2 side = int2(local.x < 1024 ? -1 : 1, local.y < 1024 ? -1 : 1);
    float2 edge = min(local, 2048 - local);
    if (own <= 0 || min(edge.x, edge.y) >= 64) return own;
    float2 t = smoothstep(0, 64, edge);
    float a = min(own, FarRegionReady(cell + int2(side.x, 0)));
    float b = min(own, FarRegionReady(cell + int2(0, side.y)));
    float c = min(min(a, b), FarRegionReady(cell + side));
    return lerp(lerp(c, b, t.x), lerp(a, own, t.x), t.y);
}

// One regional sample replaces the coarse picture from the near ground out to 7 km, fading away by 8 km.
float3 FarRegionColour(float2 world, float2 dx, float2 dy, float3 picture, float distance)
{
    if (_FarRegionEnabled < 0.5 || distance >= 8000) return picture;
    int2 cell = (int2)floor(world / 2048);
    float2 local = world - cell * 2048;
    float weight = FarRegionWeight(cell, local) * (1 - smoothstep(7000, 8000, distance));
    if (weight <= 0) return picture;
    float2 uv = (local / 4 + 16) / 544;
    float3 regional = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FarRegionAlbedo, sampler_FarRegionAlbedo, uv, FarRegionIndex(cell), dx / 2176, dy / 2176).rgb;
    return lerp(picture, regional, weight);
}

// Tile edges and 900-1300 m distance crossfades use this same background, even when the regional cache is cold.
float3 FarRegionBackground(float2 world, float2 dx, float2 dy, float distance)
{
    float2 uv = (world + 28000) / 56000;
    float3 picture = SAMPLE_TEXTURE2D_GRAD(_FarWorldPicture, sampler_FarWorldPicture, uv, dx / 56000, dy / 56000).rgb;
    return FarRegionColour(world, dx, dy, picture, distance);
}

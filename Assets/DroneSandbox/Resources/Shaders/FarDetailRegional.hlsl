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

// The near bubble (FarDetailNear): 4 by 4 toroidal 256 m pages of 0.5 m colour and 1 m surface control round a low
// camera. Its colour uses the regional pages' sampler (Clamp, trilinear, aniso 4), so no pass gains a sampler.
TEXTURE2D_ARRAY(_FarNearAlbedo);
TEXTURE2D_ARRAY(_FarNearControl);
float4 _FarNearPages[16];
float _FarNearEnabled;

// Signed modulo agrees with FarDetailNear.Index on both sides of the theatre origin.
int FarNearIndex(int2 cell)
{
    int2 slot = (int2)((float2)cell - 4.0 * floor((float2)cell / 4.0));
    return slot.y * 4 + slot.x;
}

// A page fades in over 0.6 s once complete (w 1) and out over 0.6 s before its slot is reused (w -1).
float FarNearReady(int2 cell)
{
    float4 page = _FarNearPages[FarNearIndex(cell)];
    if (any(page.xy != (float2)cell)) return 0;
    float fade = smoothstep(0, 0.6, _FarRegionTime - page.z);
    return page.w > 0 ? fade : (page.w < 0 ? 1 - fade : 0);
}

// Equal edge weights on both pages hide a neighbour that has not arrived, as FarRegionWeight does, over 16 m.
float FarNearWeight(int2 cell, float2 local)
{
    float own = FarNearReady(cell);
    float2 edge = min(local, 256 - local);
    if (own <= 0 || min(edge.x, edge.y) >= 16) return own;
    int2 side = int2(local.x < 128 ? -1 : 1, local.y < 128 ? -1 : 1);
    float2 t = smoothstep(0, 16, edge);
    float a = min(own, FarNearReady(cell + int2(side.x, 0)));
    float b = min(own, FarNearReady(cell + int2(0, side.y)));
    float c = min(min(a, b), FarNearReady(cell + side));
    return lerp(lerp(c, b, t.x), lerp(a, own, t.x), t.y);
}

// How much of a pixel the bubble owns: its page's weight, fading out once a screen pixel spans about 2 m (the page's
// coarsest mip), where the tile's full mip chain carries the same colour. The footprint is anisotropic filtering's
// minor axis, at most 4:1.
float FarNearCoverage(float2 world, float2 dx, float2 dy, out int2 cell, out float2 local)
{
    cell = (int2)floor(world / 256);
    local = world - cell * 256;
    if (_FarNearEnabled < 0.5) return 0;
    float a = length(dx), b = length(dy);
    float sharp = 1 - smoothstep(1.5, 2.5, max(min(a, b), max(a, b) * 0.25));
    return sharp > 0 ? FarNearWeight(cell, local) * sharp : 0;
}

// The page's colour, trilinear over its three mips with explicit world gradients; texel centres every 0.5 m inside an
// 8 m gutter.
float3 FarNearColour(int2 cell, float2 local, float2 dx, float2 dy)
{
    float2 uv = (local * 2 + 16) / 544;
    return SAMPLE_TEXTURE2D_ARRAY_GRAD(_FarNearAlbedo, sampler_FarRegionAlbedo, uv, FarNearIndex(cell), dx / 272, dy / 272).rgb;
}

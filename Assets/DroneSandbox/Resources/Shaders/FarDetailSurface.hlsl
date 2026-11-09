// HDRP Lit owns lighting, fog, decals, depth and motion vectors. This hook supplies only ground albedo/gradient.
TEXTURE2D(_FarGroundControl);
TEXTURE2D_ARRAY(_FarGroundAlbedo);
TEXTURE2D_ARRAY(_FarGroundNormal);
SAMPLER(sampler_FarGroundAlbedo);
SAMPLER(sampler_FarGroundNormal); // its own: a pass that reads only normals must not hold the albedo's sampler alone

#include "FarDetailPalette.hlsl"
#include "FarDetailRegional.hlsl"

// A box-filtered periodic band has stable area even when a row becomes smaller than one screen pixel.
float GroundBand(float at, float period, float width, float footprint)
{
    float h = max(footprint, 0.001);
    float a = (at - h * 0.5) / period, b = (at + h * 0.5) / period;
    float w = width / period;
    float ia = floor(a) * w + min(frac(a), w);
    float ib = floor(b) * w + min(frac(b), w);
    return saturate((ib - ia) * period / h);
}

// Explicit gradients come from world metres, never the discontinuous ID/angle texture or a wrapped UV.
void GroundSample(float4 code, float2 world, float2 dx, float2 dy, out float3 detail, out float2 slope)
{
    uint layer = min((uint)round(code.r * 255.0), 9u);
    float angle = (round(code.g * 255.0) + round(code.b * 255.0) * 256.0) * (3.14159265359 / 65535.0);
    float s, c; sincos(angle, s, c);
    float2 across = layer >= 5 ? float2(-s, c) : float2(1, 0);
    float2 along = layer >= 5 ? float2(c, s) : float2(0, 1);
    float metres = GroundMetres[layer];
    float2 uv = float2(dot(world, across), dot(world, along)) / metres;
    float2 du = float2(dot(dx, across), dot(dx, along)) / metres;
    float2 dv = float2(dot(dy, across), dot(dy, along)) / metres;
    float3 albedo = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FarGroundAlbedo, sampler_FarGroundAlbedo, uv, layer, du, dv).rgb;
    detail = albedo / max(GroundMean[layer], 0.001);
    float footprint = max(abs(dot(dx, across)) + abs(dot(dy, across)), 0.02);
    float v = dot(world, across);
    // Farmland's hay windrows are 7 m apart. Normalize their area so filtering cannot change the palette.
    if (layer == 9)
        detail *= (0.96 + 0.26 * GroundBand(v, 7.0, 1.1, footprint)) / (0.96 + 0.26 * 1.1 / 7.0);
    // Sprayer tracks: paired wheel marks 1.8 m apart every 24 m, using Farmland's physical dimensions.
    if (layer == 5 || layer == 6 || layer == 8)
    {
        float tracks = GroundBand(v, 24.0, 0.3, footprint) + GroundBand(v + 1.8, 24.0, 0.3, footprint);
        detail *= (1.0 - 0.28 * tracks) / (1.0 - 0.28 * 0.6 / 24.0);
    }
    slope = 0;
    if (layer < 5)
    {
        float3 n = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FarGroundNormal, sampler_FarGroundNormal, uv, layer, du, dv).xyz * 2 - 1;
        float2 q = n.xy / max(n.z, 0.15);
        slope = (across * q.x + along * q.y) * code.a;
    }
    detail = lerp(1, detail, code.a);
}

// Four point-loaded controls round a pixel: bilinear weights blend their sampled surfaces, not IDs or orientations.
// Interiors need only one surface sample.
void GroundDetail(float4 a, float4 b, float4 c, float4 d, float2 f, float2 world, float2 dx, float2 dy, out float3 colour, out float2 slope)
{
    GroundSample(a, world, dx, dy, colour, slope);
    if (any(a != b) || any(a != c) || any(a != d))
    {
        float3 cb, cc, cd; float2 nb, nc, nd;
        GroundSample(b, world, dx, dy, cb, nb);
        GroundSample(c, world, dx, dy, cc, nc);
        GroundSample(d, world, dx, dy, cd, nd);
        colour = lerp(lerp(colour, cb, f.x), lerp(cc, cd, f.x), f.y);
        slope = lerp(lerp(slope, nb, f.x), lerp(nc, nd, f.x), f.y);
    }
}

// The tile's 2 m colour and control, or inside the near bubble its pages' 0.5 m colour and 1 m control, which are
// tile-independent and so run through the tile's edge band; both blend by the page's weight, never switch.
void FarDetailSurface(FragInputs input, inout SurfaceData surface, inout float3 gradient)
{
    gradient = 0;
#if defined(_FarRegionChunk)
    float2 world = input.texCoord0.xy * 56000 - 28000;
#else
    float2 world = input.texCoord0.xy * 512.0 + _BaseColorMap_ST.zw;
#endif
    float farDistance = distance(GetAbsolutePositionWS(input.positionRWS), _WorldSpaceCameraPos);
    float2 dx = ddx(world), dy = ddy(world);
    int2 nearCell; float2 nearLocal;
    float nearWeight = FarNearCoverage(world, dx, dy, nearCell, nearLocal);
#if defined(_FarRegionChunk)
        surface.baseColor = FarRegionColour(world, dx, dy, surface.baseColor, farDistance);
        // A chunk beside a live tile shows the same near colour, so a tile's edge inside the bubble has no seam.
        if (nearWeight > 0) surface.baseColor = lerp(surface.baseColor, FarNearColour(nearCell, nearLocal, dx, dy), nearWeight);
        surface.perceptualSmoothness = 0;
        return;
#else
    float2 uv = (input.texCoord0.xy * 256.0 + 1.0) / 258.0;
    surface.baseColor = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, uv).rgb;
    float2 farLocal = input.texCoord0.xy * 512;
    float farEdge = min(min(farLocal.x, farLocal.y), min(512 - farLocal.x, 512 - farLocal.y));
    float farTileWeight = (1 - smoothstep(900, 1300, farDistance)) * smoothstep(0, 32, farEdge);
    // A whole near page replaces the edge band's regional background too, so it need not be sampled.
    if (_FarRegionEnabled > 0.5 && farTileWeight < 1 && nearWeight < 1)
        surface.baseColor = lerp(FarRegionBackground(world, dx, dy, farDistance), surface.baseColor, farTileWeight);
    if (nearWeight > 0) surface.baseColor = lerp(surface.baseColor, FarNearColour(nearCell, nearLocal, dx, dy), nearWeight);
    surface.perceptualSmoothness = 0;
    float fade = 1.0 - saturate((max(length(dx), length(dy)) - 1.0) / 2.0);
    // Once a screen pixel spans 3 m, only the shared mean remains; skip discrete maps and array samples.
    if (fade <= 0) return;
    float3 colour = 1; float2 slope = 0;
    float tileFade = fade * farTileWeight;
    if (nearWeight < 1 && tileFade > 0)
    {
        float2 p = input.texCoord0.xy * 256.0 + 0.5;
        int2 cell = (int2)floor(p);
        GroundDetail(LOAD_TEXTURE2D(_FarGroundControl, cell), LOAD_TEXTURE2D(_FarGroundControl, cell + int2(1, 0)),
            LOAD_TEXTURE2D(_FarGroundControl, cell + int2(0, 1)), LOAD_TEXTURE2D(_FarGroundControl, cell + 1), frac(p), world, dx, dy, colour, slope);
        colour = lerp(1, colour, tileFade);
        slope *= tileFade;
    }
    if (nearWeight > 0)
    {
        // 1 m texels, the sector splat's own resolution, centred every metre inside an 8 m gutter.
        float2 q = nearLocal + 7.5;
        int2 texel = (int2)floor(q);
        int slice = FarNearIndex(nearCell);
        float3 nearColour; float2 nearSlope;
        GroundDetail(LOAD_TEXTURE2D_ARRAY(_FarNearControl, texel, slice), LOAD_TEXTURE2D_ARRAY(_FarNearControl, texel + int2(1, 0), slice),
            LOAD_TEXTURE2D_ARRAY(_FarNearControl, texel + int2(0, 1), slice), LOAD_TEXTURE2D_ARRAY(_FarNearControl, texel + 1, slice), frac(q), world, dx, dy, nearColour, nearSlope);
        colour = lerp(colour, lerp(1, nearColour, fade), nearWeight);
        slope = lerp(slope, nearSlope * fade, nearWeight);
    }
    surface.baseColor *= colour;
    gradient = -slope.x * input.tangentToWorld[0] - slope.y * input.tangentToWorld[1];
#endif
}

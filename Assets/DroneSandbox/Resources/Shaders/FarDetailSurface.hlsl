// HDRP Lit owns lighting, fog, decals, depth and motion vectors. This hook supplies only ground albedo/gradient.
TEXTURE2D(_FarGroundControl);
TEXTURE2D_ARRAY(_FarGroundAlbedo);
TEXTURE2D_ARRAY(_FarGroundNormal);
SAMPLER(sampler_FarGroundAlbedo);
SAMPLER(sampler_FarGroundNormal); // its own: a pass that reads only normals must not hold the albedo's sampler alone

#include "FarDetailPalette.hlsl"

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

// Bilinear weights blend sampled surfaces, not IDs or orientations. Interiors need only one surface sample.
void FarDetailSurface(FragInputs input, inout SurfaceData surface, inout float3 gradient)
{
    float2 world = input.texCoord0.xy * 512.0 + _BaseColorMap_ST.zw;
    float2 dx = ddx(world), dy = ddy(world);
    float2 uv = (input.texCoord0.xy * 256.0 + 1.0) / 258.0;
    surface.baseColor = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, uv).rgb;
    surface.perceptualSmoothness = 0;
    float fade = 1.0 - saturate((max(length(dx), length(dy)) - 1.0) / 2.0);
    gradient = 0;
    // Once a screen pixel spans 3 m, only the shared mean remains; skip discrete maps and array samples.
    if (fade <= 0) return;
    float2 p = input.texCoord0.xy * 256.0 + 0.5;
    int2 cell = (int2)floor(p);
    float2 f = frac(p);
    float4 a = LOAD_TEXTURE2D(_FarGroundControl, cell);
    float4 b = LOAD_TEXTURE2D(_FarGroundControl, cell + int2(1, 0));
    float4 c = LOAD_TEXTURE2D(_FarGroundControl, cell + int2(0, 1));
    float4 d = LOAD_TEXTURE2D(_FarGroundControl, cell + 1);
    float3 colour; float2 slope;
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
    surface.baseColor *= lerp(1, colour, fade);
    gradient = (-slope.x * input.tangentToWorld[0] - slope.y * input.tangentToWorld[1]) * fade;
}

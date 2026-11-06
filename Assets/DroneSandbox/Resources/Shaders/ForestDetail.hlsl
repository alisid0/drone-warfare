// World-stable crowns for the far canopy (tier 3). Two nested jittered lattices in 60 m stands give emergent
// 15-20 m crowns or 7-10 m ones; each crown is a lit cap with its own shade, dark gaps and a sun shadow. The
// photo crown map adds foliage clumps. Everything fades to the stand mean before a crown spans three pixels.
// Tools/CanopyRelief2/crowns.py mirrors this file for the offline review; keep the two in step.

static const float ForestCrownCell = 15.0;
static const float ForestCrownJitter = 0.45;
static const float2 ForestCrownAxis = float2(0.8944272, 0.4472136);

// A salted integer hash, so the two lattices and the stands draw independent values.
uint ForestCrownHash(int2 cell, uint salt)
{
    uint h = asuint(cell.x) * 73856093u ^ asuint(cell.y) * 19349663u ^ salt * 83492791u;
    h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
    return h;
}

// A second independent 32 bits from one hash.
uint ForestCrownRemix(uint h)
{
    h = (h ^ (h >> 15)) * 0x2c1b3c6du;
    return h ^ (h >> 12);
}

// Eight bits of a hash as a value in [0, 1].
float ForestCrownUnit(uint h, uint shift)
{
    return ((h >> shift) & 255u) / 255.0;
}

// Two hash bits as a -1/0/0/+1 cell offset, so stand edges follow whole crowns instead of straight lines.
int ForestCrownNudge(uint h, uint shift)
{
    uint b = (h >> shift) & 3u;
    return b == 0u ? -1 : (b == 3u ? 1 : 0);
}

// Share of a stand's cells holding one emergent crown instead of four smaller ones: young, mixed or old.
float ForestCrownShare(uint stand)
{
    float u = ForestCrownUnit(stand, 0u);
    return u < 0.35 ? 0.05 + 0.3 * u : (u < 0.7 ? 0.35 + 0.8 * (u - 0.35) : 0.75 + 0.5 * (u - 0.7));
}

// The highest crown cap found so far at one pixel.
struct ForestCrownBest
{
    float z, second, q, radius;
    float2 slope;
    uint hash, index;
    bool big;
};

// An elliptical paraboloid cap; the higher surface wins, and its outward slope is depth times the gradient of q.
void ForestCrownOffer(inout ForestCrownBest best, bool present, uint index, uint h, float2 centre, float radius,
    float top, float depth, float2 p, bool big)
{
    float s = (ForestCrownUnit(h, 0u) - 0.5) * 0.4, e = (ForestCrownUnit(h, 8u) - 0.5) * 0.4;
    float2 d = p - centre;
    float inverse = 1.0 / (radius * radius);
    float q = ((1.0 + s) * d.x * d.x + (1.0 - s) * d.y * d.y + e * d.x * d.y) * inverse;
    float z = top - depth * q;
    bool covers = present && q < 1.0;
    best.second = covers ? max(best.second, min(z, best.z)) : best.second;
    if (covers && z > best.z)
    {
        best.z = z; best.q = q; best.radius = radius; best.hash = h; best.index = index; best.big = big;
        best.slope = depth * inverse * float2(2.0 * (1.0 + s) * d.x + e * d.y, 2.0 * (1.0 - s) * d.y + e * d.x);
    }
}

// Shade from one emergent crown onto lower surfaces, displaced away from the sun by at most `longest` and 3/4 of its
// radius wide, so no shadow reaches past the four cells searched. Its own cap only shades its soft rim.
float ForestCrownShadow(float4 crown, bool present, bool own, float2 p, float surface, float2 sun, float longest,
    float edge, float cover)
{
    float lift = min(max(0.0, crown.w - surface), longest);
    float r = crown.z * 0.75;
    float2 d = p - crown.xy - sun * lift;
    float inside = 1.0 - smoothstep(max(r - edge, 0.0) * max(r - edge, 0.0), r * r, dot(d, d));
    return present ? inside * (own ? 1.0 - cover : 1.0) : 0.0;
}

// Crown surface in rotated lattice metres: albedo factor, outward slope, edge cover, stand strength and the
// emergent share, whether the winner is emergent, and a hash for its foliage clumps. p returns warped.
float ForestCrownField(inout float2 p, float footprint, float broadleaf, float2 sun, out float2 slope, out float cover,
    out float strength, out bool big, out uint tone)
{
    // A gentle warp bends the lattice rows; its slope stays near a quarter, so crowns keep their shape.
    p += 1.5 * float2(sin(p.y * 0.13 + 1.3 * sin(p.x * 0.071)), sin(p.x * 0.12 + 1.1 * sin(p.y * 0.083)));
    float2 q = p / ForestCrownCell;
    float2 base = floor(q - 0.5);
    float2 f = q - 0.5 - base;
    int2 cell = (int2)base;
    ForestCrownBest best;
    best.z = -1e9; best.second = -1e9; best.q = 1.0; best.radius = 1.0; best.slope = 0.0; best.hash = 0u; best.index = 8u; best.big = false;
    float4 crowns[4];
    uint presentMask = 0u;
    strength = 0.0;
    [unroll] for (uint k = 0u; k < 4u; k++)
    {
        int2 c = cell + int2(k & 1u, k >> 1);
        uint h = ForestCrownHash(c, 1u);
        uint stand = ForestCrownHash((c + int2(ForestCrownNudge(h, 24u), ForestCrownNudge(h, 26u))) >> 2, 2u);
        float wx = (k & 1u) != 0u ? f.x : 1.0 - f.x, wz = (k >> 1) != 0u ? f.y : 1.0 - f.y;
        strength += wx * wz * (0.75 + 0.5 * ForestCrownUnit(stand, 8u));
        bool present = ForestCrownUnit(h, 16u) < ForestCrownShare(stand);
        presentMask |= present ? 1u << k : 0u;
        uint g = ForestCrownRemix(h);
        float2 centre = (float2(c) + 0.5 + (float2(ForestCrownUnit(h, 0u), ForestCrownUnit(h, 8u)) - 0.5) * ForestCrownJitter) * ForestCrownCell;
        float radius = ForestCrownCell * (0.50 + 0.14 * ForestCrownUnit(g, 16u));
        float top = 3.5 + 2.5 * ForestCrownUnit(g, 24u);
        ForestCrownOffer(best, present, k, g, centre, radius, top, radius * (0.50 + 0.15 * (1.0 - broadleaf)), p, true);
        crowns[k] = float4(centre, radius, top);
    }
    int2 fine = (int2)floor(q * 2.0 - 0.5);
    float halfCell = ForestCrownCell * 0.5;
    [unroll] for (uint m = 0u; m < 4u; m++)
    {
        int2 c = fine + int2(m & 1u, m >> 1);
        // The parent big cell is always one of the four above; an emergent parent leaves no room for small crowns.
        int2 parent = (c >> 1) - cell;
        bool present = ((presentMask >> uint(parent.x + 2 * parent.y)) & 1u) == 0u;
        uint h = ForestCrownHash(c, 3u);
        float2 centre = (float2(c) + 0.5 + (float2(ForestCrownUnit(h, 0u), ForestCrownUnit(h, 8u)) - 0.5) * ForestCrownJitter) * halfCell;
        float radius = halfCell * (0.50 + 0.12 * ForestCrownUnit(h, 16u)) * (0.94 + 0.12 * broadleaf);
        float top = 2.0 * (ForestCrownUnit(h, 24u) - 0.5);
        ForestCrownOffer(best, present, 4u + m, ForestCrownRemix(h), centre, radius, top, radius * (0.55 + 0.30 * (1.0 - broadleaf)), p, false);
    }
    bool inside = best.index < 8u;
    float edge = max(0.3, 1.6 * footprint);
    // (1 - q) R / 2 approximates the distance inside the ellipse's rim in metres; where a lower crown meets the
    // winner, the height gap over twice the slope is the distance to that crease. Both soften over 1.6 pixels.
    float crease = (best.z - best.second) / (2.0 * length(best.slope) + 0.05);
    cover = inside ? smoothstep(0.0, edge, min((1.0 - best.q) * best.radius * 0.5, crease)) : 0.0;
    tone = ForestCrownRemix(best.hash);
    float value = (0.84 + 0.32 * ForestCrownUnit(tone, 0u)) * (1.0 - 0.20 * (inside ? best.q : 1.0));
    // Wide edges blend each rim with its gap; lifting the gap as they widen keeps the mean steady with distance.
    float gap = lerp(0.6 + 0.08 * broadleaf, 0.9, 0.55 * saturate((edge - 0.8) / 4.0));
    float factor = gap + (value - gap) * cover;
    // Emergent crowns shade what lies below them on the side away from the sun, onto gaps and lower neighbours; a
    // shadow moves at most a quarter cell.
    float surface = inside ? best.z : -4.0;
    float longest = 0.25 * ForestCrownCell / max(length(sun), 0.001), shade = 0.0;
    [unroll] for (uint n = 0u; n < 4u; n++)
        shade = max(shade, ForestCrownShadow(crowns[n], ((presentMask >> n) & 1u) != 0u, n == best.index, p, surface, sun,
            longest, edge, cover));
    slope = best.slope * cover;
    big = inside && best.big;
    return factor * (1.0 - 0.42 * shade);
}

// Albedo factor and world-space outward slope for the canopy macro surface at one pixel. dx/dy are the screen
// derivatives of world x/z; weight is the macro cover weight. Far away everything settles to 0.989, the mip mean
// of the photo crown map this replaces, so distant woods keep their calibrated tone.
float ForestCrownDetail(float2 world, float2 dx, float2 dy, float3 macro, float weight, float2 sunShift, out float2 slope)
{
    float footprint = max(length(dx), length(dy));
    slope = 0.0;
    float factor = 0.989;
    // Beyond about four metres a pixel no longer resolves any crown: keep the mean and skip the lattice entirely.
    [branch] if (footprint < 3.6 && weight > 0.0)
    {
        float2 axis = ForestCrownAxis;
        float2x2 rotate = float2x2(axis.x, -axis.y, axis.y, axis.x);
        float2 p = mul(rotate, world);
        float broadleaf = smoothstep(0.50, 0.58, macro.r / max(macro.g, 1e-4));
        float2 crownSlope; float cover, strength; bool big; uint tone;
        float raw = ForestCrownField(p, footprint, broadleaf, mul(rotate, sunShift), crownSlope, cover, strength, big, tone);
        // Foliage clumps: the photo crown map at 2.5 m, offset per crown so its 40 m repeat never lines up.
        float2 uv = p / 40.0 + float2(ForestCrownUnit(tone, 24u), ForestCrownUnit(tone, 12u));
        float3 clump = SAMPLE_TEXTURE2D_GRAD(_ForestDetail, sampler_ForestDetail, uv, mul(rotate, dx) / 40.0, mul(rotate, dy) / 40.0).rgb;
        raw *= (1.0 + (clump.r - 0.5) * 0.8 * (0.5 + 0.5 * cover)) * (1.331 - 0.098 * broadleaf);
        crownSlope += (clump.gb * 2.0 - 1.0) * 0.35 * cover;
        float fade = big ? 1.0 - smoothstep(2.2, 3.6, footprint) : 1.0 - smoothstep(1.9, 3.5, footprint);
        float k = fade * strength;
        factor += (raw - factor) * k;
        // Lattice slopes are in rotated metres; turn them back to world x/z.
        slope = mul(transpose(rotate), crownSlope) * k * weight;
    }
    return 1.0 + (factor - 1.0) * weight;
}

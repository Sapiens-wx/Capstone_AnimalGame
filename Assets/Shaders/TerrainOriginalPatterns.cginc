// Only coverage is baked. Pattern pixels are sampled from the original assets.
#include "RockPatternEdgeFade.cginc"

UNITY_DECLARE_TEX2DARRAY(_TerrainCoverage);
UNITY_DECLARE_TEX2D(_TerrainPattern1);
UNITY_DECLARE_TEX2D_NOSAMPLER(_TerrainPattern2);
UNITY_DECLARE_TEX2D_NOSAMPLER(_TerrainPattern3);
UNITY_DECLARE_TEX2D_NOSAMPLER(_TerrainPattern5);
UNITY_DECLARE_TEX2D_NOSAMPLER(_TerrainPattern6);
UNITY_DECLARE_TEX2D_NOSAMPLER(_TerrainPattern7);
UNITY_DECLARE_TEX2D_NOSAMPLER(_TerrainPattern8);
UNITY_DECLARE_TEX2D_NOSAMPLER(_TerrainPattern9);
UNITY_DECLARE_TEX2D_NOSAMPLER(_TerrainPattern10);
UNITY_DECLARE_TEX2D_NOSAMPLER(_TerrainPattern11);
float4 _TerrainSettings[12];
float4 _TerrainTints[12];
float4 _TerrainOrigin;
float _TerrainSeed;

float TerrainHash(float2 p)
{
    float3 f = frac(float3(p.xyx) * .1031);
    f += dot(f, f.yzx + 33.33);
    return frac((f.x + f.y) * f.z);
}

#if defined(UNITY_SEPARATE_TEXTURE_SAMPLER)
    #define SAMPLE_TERRAIN_GRAD(texture, uv, dx, dy) texture.SampleGrad(sampler_TerrainPattern1, uv, dx, dy)
#else
    #define SAMPLE_TERRAIN_GRAD(texture, uv, dx, dy) tex2Dgrad(texture, uv, dx, dy)
#endif

float4 TerrainOriginal(int id, float2 uv, float2 dx, float2 dy)
{
    if (id == 1) return SAMPLE_TERRAIN_GRAD(_TerrainPattern1, uv, dx, dy);
    if (id == 2) return SAMPLE_TERRAIN_GRAD(_TerrainPattern2, uv, dx, dy);
    if (id == 3) return SAMPLE_TERRAIN_GRAD(_TerrainPattern3, uv, dx, dy);
    if (id == 5) return SAMPLE_TERRAIN_GRAD(_TerrainPattern5, uv, dx, dy);
    if (id == 6) return SAMPLE_TERRAIN_GRAD(_TerrainPattern6, uv, dx, dy);
    if (id == 7) return SAMPLE_TERRAIN_GRAD(_TerrainPattern7, uv, dx, dy);
    if (id == 8) return SAMPLE_TERRAIN_GRAD(_TerrainPattern8, uv, dx, dy);
    if (id == 9) return SAMPLE_TERRAIN_GRAD(_TerrainPattern9, uv, dx, dy);
    if (id == 11) return SAMPLE_TERRAIN_GRAD(_TerrainPattern11, uv, dx, dy);
    return SAMPLE_TERRAIN_GRAD(_TerrainPattern10, uv, dx, dy);
}
#undef SAMPLE_TERRAIN_GRAD

float2 RotateSandUv(float2 value, float sine, float cosine)
{
    return float2(cosine * value.x - sine * value.y, sine * value.x + cosine * value.y);
}

float4 TerrainRandomPattern(int id, float2 p, float2 dx, float2 dy)
{
    float2 cell = floor(p), blend = frac(p);
    bool isGravel = id == 5;
    bool isRock = id == 6;
    blend = isRock ? RockPatternBlend(blend)
        : isGravel ? smoothstep(.475, .525, blend) : blend * blend * (3 - 2 * blend);
    float4 result = 0;
    [unroll] for (int y = 0; y <= 1; y++)
    [unroll] for (int x = 0; x <= 1; x++)
    {
        float weight = (x == 0 ? 1 - blend.x : blend.x) * (y == 0 ? 1 - blend.y : blend.y);
        [branch] if (weight <= 0) continue;
        float2 corner = float2(x, y), seed = cell + corner + _TerrainSeed;
        float s, c; sincos(TerrainHash(seed) * 6.2831853, s, c);
        float inverseScale = isGravel ? rcp(lerp(.9, 1.1, TerrainHash(seed + 139.3))) : 1;
        float2 uv = RotateSandUv(p - cell - corner, s, c) * inverseScale
            + float2(TerrainHash(seed + 17.1), TerrainHash(seed + 73.7));
        // The random cell and offset select the pattern. Only the rotation transforms its footprint.
        float4 sampleColor = TerrainOriginal(id, uv,
            RotateSandUv(dx, s, c) * inverseScale, RotateSandUv(dy, s, c) * inverseScale);
        if (isRock) sampleColor.a *= RockPatternEdgeFade(uv);
        result += float4(sampleColor.rgb * sampleColor.a, sampleColor.a) * weight;
    }
    result.rgb = result.a > .00001 ? result.rgb / result.a : 0;
    if (isRock) result.a = RockPatternSparseAlpha(result.a);
    return result;
}

float4 SampleOriginalTerrain(float2 tileUv, float2 mapDx, float2 mapDy)
{
    float2 meters = tileUv * _MapSizeMeters.xy + _TerrainOrigin.xy;
    float4 result = 0;
    [loop] for (int id = 1; id <= 11; id++)
    {
        if (id == 4 || _TerrainSettings[id].x <= 0) continue; // Grassland uses its blade renderer.
        float coverage = UNITY_SAMPLE_TEX2DARRAY(_TerrainCoverage, float3(tileUv, id)).r;
        if (coverage < .0001) continue;
        float inverseTileSize = rcp(max(.01, _TerrainSettings[id].x));
        float2 uv = meters * inverseTileSize;
        float2 dx = mapDx * inverseTileSize, dy = mapDy * inverseTileSize;
        float4 pattern = (id == 5 || id == 6) ? TerrainRandomPattern(id, uv, dx, dy)
            : TerrainOriginal(id, uv, dx, dy);
        if (id == 11)
        {
            float sandScale = rcp(max(.01, _TerrainSettings[9].x));
            float4 sand = TerrainRandomPattern(9, meters * sandScale,
                mapDx * sandScale, mapDy * sandScale);
            sand.a = saturate(sand.a * 2);
            // Composite source patterns first; the shared coverage fades the combined layer once.
            float combinedAlpha = pattern.a + sand.a * (1 - pattern.a);
            pattern.rgb = combinedAlpha > .00001
                ? (pattern.rgb * pattern.a + sand.rgb * sand.a * (1 - pattern.a)) / combinedAlpha : 0;
            pattern.a = combinedAlpha;
        }
        if (id == 9)
        {
            float4 overlay = TerrainRandomPattern(9, uv, dx, dy);
            float alpha = overlay.a + pattern.a * (1 - overlay.a);
            pattern.rgb = alpha > .00001
                ? (overlay.rgb * overlay.a + pattern.rgb * pattern.a * (1 - overlay.a)) / alpha : 0;
            pattern.a = saturate(alpha);
        }
        // Finish random pattern placement before applying the shared palette/contour opacity.
        float alpha = pattern.a * coverage;
        result += float4(pattern.rgb * _TerrainTints[id].rgb * alpha, alpha);
    }
    result.rgb = result.a > .00001 ? result.rgb / result.a : 0;
    result.a = saturate(result.a);
    return result;
}

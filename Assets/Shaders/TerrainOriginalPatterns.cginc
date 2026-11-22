// Only coverage is baked. Pattern pixels are sampled from the original assets.
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
float4 _TerrainSettings[11];
float4 _TerrainTints[11];
float4 _TerrainOrigin;
float _TerrainSeed;

float TerrainHash(float2 p)
{
    float3 f = frac(float3(p.xyx) * .1031);
    f += dot(f, f.yzx + 33.33);
    return frac((f.x + f.y) * f.z);
}

float4 TerrainOriginal(int id, float2 uv)
{
    uv = frac(uv);
    if (id == 1) return UNITY_SAMPLE_TEX2D(_TerrainPattern1, uv);
    if (id == 2) return UNITY_SAMPLE_TEX2D_SAMPLER(_TerrainPattern2, _TerrainPattern1, uv);
    if (id == 3) return UNITY_SAMPLE_TEX2D_SAMPLER(_TerrainPattern3, _TerrainPattern1, uv);
    if (id == 5) return UNITY_SAMPLE_TEX2D_SAMPLER(_TerrainPattern5, _TerrainPattern1, uv);
    if (id == 6) return UNITY_SAMPLE_TEX2D_SAMPLER(_TerrainPattern6, _TerrainPattern1, uv);
    if (id == 7) return UNITY_SAMPLE_TEX2D_SAMPLER(_TerrainPattern7, _TerrainPattern1, uv);
    if (id == 8) return UNITY_SAMPLE_TEX2D_SAMPLER(_TerrainPattern8, _TerrainPattern1, uv);
    if (id == 9) return UNITY_SAMPLE_TEX2D_SAMPLER(_TerrainPattern9, _TerrainPattern1, uv);
    return UNITY_SAMPLE_TEX2D_SAMPLER(_TerrainPattern10, _TerrainPattern1, uv);
}

float4 TerrainSandOverlay(float2 p)
{
    float2 cell = floor(p), blend = frac(p);
    blend = blend * blend * (3 - 2 * blend);
    float4 result = 0;
    [unroll] for (int y = 0; y <= 1; y++)
    [unroll] for (int x = 0; x <= 1; x++)
    {
        float2 corner = float2(x, y), seed = cell + corner + _TerrainSeed;
        float s, c; sincos(TerrainHash(seed) * 6.2831853, s, c);
        float2 d = p - cell - corner;
        float2 uv = float2(c * d.x - s * d.y, s * d.x + c * d.y)
            + float2(TerrainHash(seed + 17.1), TerrainHash(seed + 73.7));
        float4 sampleColor = TerrainOriginal(9, uv);
        float weight = (x == 0 ? 1 - blend.x : blend.x) * (y == 0 ? 1 - blend.y : blend.y);
        result += float4(sampleColor.rgb * sampleColor.a, sampleColor.a) * weight;
    }
    result.rgb = result.a > .00001 ? result.rgb / result.a : 0;
    return result;
}

float4 SampleOriginalTerrain(float2 tileUv)
{
    float2 meters = tileUv * _MapSizeMeters.xy + _TerrainOrigin.xy;
    float4 result = 0;
    [loop] for (int id = 1; id <= 10; id++)
    {
        if (id == 4 || _TerrainSettings[id].x <= 0) continue; // Grassland uses its blade renderer.
        float coverage = UNITY_SAMPLE_TEX2DARRAY(_TerrainCoverage, float3(tileUv, id)).r;
        if (coverage < .0001) continue;
        float2 uv = meters / max(.01, _TerrainSettings[id].x);
        float4 pattern = TerrainOriginal(id, uv);
        if (id == 9)
        {
            float4 overlay = TerrainSandOverlay(uv);
            float alpha = overlay.a + pattern.a * (1 - overlay.a);
            pattern.rgb = alpha > .00001
                ? (overlay.rgb * overlay.a + pattern.rgb * pattern.a * (1 - overlay.a)) / alpha : 0;
            pattern.a = saturate(alpha);
        }
        float alpha = pattern.a * coverage;
        result += float4(pattern.rgb * _TerrainTints[id].rgb * alpha, alpha);
    }
    result.rgb = result.a > .00001 ? result.rgb / result.a : 0;
    result.a = saturate(result.a);
    return result;
}

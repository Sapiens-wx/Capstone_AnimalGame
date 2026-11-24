#ifndef ANIMAL_GAME_MAP_WATER
#define ANIMAL_GAME_MAP_WATER
sampler2D _WaterMaskTex, _WaterUvTex, _WaterPatternTex;
float4 _WaterMaskTex_TexelSize, _WaterMaskBounds, _WaterOriginMeters;
float4 _WaterLayerOneSpeed, _WaterLayerTwoSpeed;
float _WaterEnabled, _WaterTileSizeMeters, _WaterLayerTwoScale;
float _WaterWaveDistortion, _WaterWaveSpeed, _WaterWaveLengthMeters;
float _WaterFlowSpeed;
float2 RotateWaterUv(float2 value)
{
    // A fixed 23-degree turn prevents the second layer from
    // repeatedly lining up with the first layer's sparse dashes.
    return float2(
        value.x * 0.920505 - value.y * 0.390731,
        value.x * 0.390731 + value.y * 0.920505);
}

float2 DecodeWaterFlow(float4 data)
{
    // Mask packing: R = range, G = depth, B = angle, A = speed.
    // Authoring uses 256 distinct angles, with +X at byte zero.
    float angle = data.b * (255.0 / 256.0) * 6.283185307;
    return float2(cos(angle), sin(angle)) * data.a;
}

float2 SampleWaterFlow(float2 uv)
{
    // Sample the nearest texel at its centre so the mask's bilinear
    // filter does not blend authored flow angles or speeds.
    float2 pixel = floor(uv * _WaterMaskTex_TexelSize.zw);
    float2 centreUv = (pixel + 0.5) * _WaterMaskTex_TexelSize.xy;
    return DecodeWaterFlow(tex2Dlod(_WaterMaskTex, float4(centreUv, 0, 0)));
}

float2 SampleBakedWaterUv(float2 uv)
{
    // Shares the mask's dimensions/bounds. Keep zero pixels invalid, including at shorelines.
    float2 pixel = uv * _WaterMaskTex_TexelSize.zw - 0.5;
    float2 basePixel = floor(pixel);
    float2 f = frac(pixel);
    float2 origin = (basePixel + 0.5) * _WaterMaskTex_TexelSize.xy;
    float2 a = tex2Dlod(_WaterUvTex, float4(origin, 0, 0)).rg;
    float2 b = tex2Dlod(_WaterUvTex, float4(origin + float2(_WaterMaskTex_TexelSize.x, 0), 0, 0)).rg;
    float2 c = tex2Dlod(_WaterUvTex, float4(origin + float2(0, _WaterMaskTex_TexelSize.y), 0, 0)).rg;
    float2 d = tex2Dlod(_WaterUvTex, float4(origin + _WaterMaskTex_TexelSize.xy, 0, 0)).rg;
    float2 nearest = f.y < 0.5 ? (f.x < 0.5 ? a : b) : (f.x < 0.5 ? c : d);
    if (all(nearest == 0.0)) return 0.0;
    float4 weights = float4((1.0-f.x)*(1.0-f.y), f.x*(1.0-f.y), (1.0-f.x)*f.y, f.x*f.y);
    weights *= float4(any(a != 0.0), any(b != 0.0), any(c != 0.0), any(d != 0.0));
    return (a*weights.x + b*weights.y + c*weights.z + d*weights.w)
        / dot(weights, float4(1, 1, 1, 1));
}

fixed4 SampleWaterPattern(
    float2 mapMeters,
    float normalizedDepth,
    float2 localFlow,
    float animationTime,
    float2 bakedUv,
    float patternScale)
{
    if (any(bakedUv != 0.0))
    {
        // Baked coordinates are unwrapped meters, with +U pointing downstream.
        mapMeters = bakedUv;
        localFlow = float2(length(localFlow), 0.0);
    }
    float depth = saturate(normalizedDepth);
    float tileSize = max(0.01, _WaterTileSizeMeters) * patternScale;
    float waveLength = max(0.1, _WaterWaveLengthMeters) * patternScale;
    float2 waveCoordinate = mapMeters / waveLength;
    float2 distortion = float2(
        sin(waveCoordinate.y * 6.283185
            + animationTime * _WaterWaveSpeed),
        cos(waveCoordinate.x * 6.283185
            - animationTime * _WaterWaveSpeed * 0.83))
        * _WaterWaveDistortion;

    // Let the repeat sampler wrap continuous UVs so mip selection stays stable at the wrap.
    float2 primaryUv = (mapMeters + _WaterLayerOneSpeed.xy * animationTime) / tileSize + distortion;
    float2 secondaryUv = RotateWaterUv(
            mapMeters / tileSize
            * max(0.1, _WaterLayerTwoScale))
        + _WaterLayerTwoSpeed.xy * animationTime / tileSize
        - distortion * 0.73;
    // Continuous local-flow displacement, added to the existing
    // fixed-direction animation without phase resets or blending.
    float2 flowUv = localFlow * max(0.0, _WaterFlowSpeed) * animationTime / tileSize;
    fixed4 primary = tex2D(_WaterPatternTex, primaryUv - flowUv);
    // Match the existing second layer's fixed coordinate transform.
    // Its orientation never depends on local flow direction.
    fixed4 secondary = tex2D(_WaterPatternTex, secondaryUv
        - RotateWaterUv(flowUv * max(0.1, _WaterLayerTwoScale)));
    float secondaryStrength = 0.65;
    float combinedAlpha = 1.0
        - (1.0 - primary.a)
        * (1.0 - secondary.a * secondaryStrength);
    float3 combinedRgb = max(
        primary.rgb,
        secondary.rgb * secondaryStrength);
    float depthOpacity = lerp(0.06, 0.3, depth);
    return fixed4(
        combinedRgb,
        combinedAlpha * depthOpacity);
}


fixed4 ReadMapWater(float2 tileUv, out float2 waterUv, out float insideBounds)
{
    waterUv = (tileUv - _WaterMaskBounds.xy) / _WaterMaskBounds.zw;
    insideBounds = step(0, waterUv.x) * step(0, waterUv.y)
        * step(waterUv.x, 1) * step(waterUv.y, 1);
    return _WaterEnabled > .5 ? tex2D(_WaterMaskTex, waterUv) * insideBounds : 0;
}
// Blur only the authored water range, in physical metres, to soften land patterns
// outside the shoreline. Bounds checks prevent clamped mask edges from bleeding.
float MapShoreTerrainOpacity(float2 tileUv, float2 mapSizeMeters)
{
    if (_WaterEnabled < .5) return 1;
    float width = 1.5;
    float2 uv = (tileUv - _WaterMaskBounds.xy) / _WaterMaskBounds.zw;
    float2 radius = width / max(float2(.001, .001), mapSizeMeters * _WaterMaskBounds.zw);
    if (any(uv < -radius) || any(uv > 1 + radius)) return 1;
    float coverage = 0;
    [unroll] for (int ring = 1; ring <= 3; ring++)
    {
        float weight = ring == 1 ? .5 : (ring == 2 ? .3 : .2);
        [unroll] for (int direction = 0; direction < 8; direction++)
        {
            float angle = direction * .785398163;
            float2 sampleUv = uv + float2(cos(angle), sin(angle)) * radius * (ring / 3.0);
            float inside = step(0, sampleUv.x) * step(0, sampleUv.y)
                * step(sampleUv.x, 1) * step(sampleUv.y, 1);
            coverage += tex2Dlod(_WaterMaskTex, float4(sampleUv, 0, 0)).r * inside * weight / 8;
        }
    }
    // Reach full transparency before the actual water edge, including narrow shores.
    float landOpacity = 1 - smoothstep(0, .25, coverage);
    return landOpacity * landOpacity;
}

float3 ApplyMapWater(float3 background, float2 tileUv, float2 tileSize, float revealMask,
    float2 waterUv, float insideBounds, fixed4 waterData, out float waterBlend)
{
    float range = smoothstep(.02, .98, waterData.r);
    [branch] if (range <= 0)
    {
        waterBlend = 0;
        return background;
    }

    float2 mapMeters = tileUv * tileSize + _WaterOriginMeters.xy;
    float2 bakedUv = SampleBakedWaterUv(waterUv) * insideBounds;
    fixed4 water;
    [branch] if (revealMask <= 0)
    {
        water = SampleWaterPattern(mapMeters, waterData.g, 0, 0, bakedUv, 1);
    }
    else
    {
        float2 flow = SampleWaterFlow(waterUv);
        water = SampleWaterPattern(mapMeters, waterData.g, flow, _Time.y, bakedUv, 1);
        [branch] if (revealMask < 1)
        {
            fixed4 still = SampleWaterPattern(mapMeters, waterData.g, 0, 0, bakedUv, 1);
            water = lerp(still, water, revealMask);
        }
    }
    waterBlend = saturate(water.a * range);
    return lerp(background, water.rgb, waterBlend);
}

#endif

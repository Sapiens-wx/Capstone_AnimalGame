#ifndef ANIMAL_GAME_MAP_WATER_SYMBOLS
#define ANIMAL_GAME_MAP_WATER_SYMBOLS
#include "MapWater.cginc"

float MapWaterRange(float2 tileUv)
{
    // Don't treat the edge of a tile as a bank when water extends across it.
    float2 uv = (saturate(tileUv) - _WaterMaskBounds.xy) / _WaterMaskBounds.zw;
    float2 inside = step(0, uv) * step(uv, 1);
    return tex2Dlod(_WaterMaskTex, float4(saturate(uv), 0, 0)).r * inside.x * inside.y;
}

float WaterStroke(float position, float repeatPixels, float halfWidth)
{
    float distanceToCenter = abs(frac(position / repeatPixels + .5) - .5) * repeatPixels;
    return 1 - smoothstep(halfWidth - .45, halfWidth + .45, distanceToCenter);
}

float DrawMapWater(float brightness, float submerged,
    float2 tileUv, float2 mapPosition, float2 tileSize, float2 metresPerPixel, float2 viewZoom, float waterTime,
    bool useBakedUv,
    float hatchSpacing, float hatchOpacity, float bankOpacity)
{
    // At higher zoom keep the shoreline footprint at its opening map-space size.
    float2 bankSample = 2 * metresPerPixel * max(viewZoom, 1) / tileSize;
    float2 boundsMin = _WaterMaskBounds.xy;
    float2 boundsMax = boundsMin + _WaterMaskBounds.zw;
    if (any(tileUv < boundsMin - bankSample) || any(tileUv > boundsMax + bankSample))
        return brightness;

    float range = MapWaterRange(tileUv);
    if (range > .42)
    {
        float2 patternMeters = mapPosition;
        float2 flow = 0;
        if (useBakedUv)
        {
            float2 waterUv = (tileUv - _WaterMaskBounds.xy) / _WaterMaskBounds.zw;
            if (waterTime > 0)
                flow = SampleWaterFlow(waterUv);
            ApplyBakedWaterCoordinates(SampleBakedWaterUv(waterUv), patternMeters, flow);
        }
        float2 hatchPosition = (patternMeters - WaterFlowDisplacement(flow, waterTime)) / metresPerPixel;
        if (waterTime > 0)
        {
            float stillness = 1 - smoothstep(.000001, .001, dot(flow, flow));
            if (stillness > 0)
            {
                // A small elliptical motion gives still water sway in every direction.
                float phase = dot(mapPosition, float2(.063, -.041)) + .85 * waterTime;
                float sine, cosine;
                sincos(phase, sine, cosine);
                float fade = smoothstep(0, 1, waterTime);
                hatchPosition -= float2(sine * 1.5, cosine * 1.2) * (stillness * fade);
            }
        }
        float acrossHatch = (hatchPosition.x - hatchPosition.y) * .70710678;
        float alongHatch = (hatchPosition.x + hatchPosition.y) * .70710678;
        float zoom = sqrt(viewZoom.x * viewZoom.y);
        float hatchPeriod = hatchSpacing * zoom;
        float hatch = WaterStroke(acrossHatch, hatchPeriod, 1.3 * zoom);
        float hatchIndex = floor(acrossHatch / hatchPeriod + .5);
        float seed = frac(hatchIndex * .1031);
        seed *= seed + 33.33;
        float stagger = 32 * (frac(seed * seed * 2) - .5);
        float hatchGap = WaterStroke(alongHatch + stagger * zoom, 32 * zoom, 6 * zoom);
        float fill = lerp(submerged, 1, hatchOpacity * hatch * (1 - hatchGap));
        brightness = lerp(brightness, fill, smoothstep(.42, .58, range));
    }

    if (bankOpacity <= 0) return brightness;
    float2 dx = float2(bankSample.x, 0);
    float2 dy = float2(0, bankSample.y);
    float2 lower = float2(MapWaterRange(tileUv - dx), MapWaterRange(tileUv - dy));
    float2 upper = float2(MapWaterRange(tileUv + dx), MapWaterRange(tileUv + dy));
    float2 gradient = upper - lower;
    float2 slope = .5 * max(abs(range - lower), abs(range - upper));
    float distanceToBank = abs(range - .5) / max(length(slope), 1e-5);
    float lowest = min(range, min(min(lower.x, lower.y), min(upper.x, upper.y)));
    float highest = max(range, max(max(lower.x, lower.y), max(upper.x, upper.y)));
    float bank = lowest < .5 && highest >= .5 ? 1 - smoothstep(.6, 2.2, distanceToBank) : 0;

    // Mask-only captures blend the two axis projections at diagonal banks.
    float2 position = mapPosition / metresPerPixel;
    float2 tangent = abs(gradient.yx);
    float2 weight = smoothstep(.72, .98, tangent / max(max(tangent.x, tangent.y), 1e-5));
    // Keep distant dashes long enough to read as strokes on a diagonal bank.
    float2 dashZoom = max(viewZoom, .4);
    float dashX = WaterStroke(position.x, 40 * dashZoom.x, 11 * dashZoom.x);
    float dashY = WaterStroke(position.y, 40 * dashZoom.y, 11 * dashZoom.y);
    float dashedBank = bank * max(weight.x * dashX, weight.y * dashY);
    return lerp(brightness, 1, bankOpacity * dashedBank);
}

#endif

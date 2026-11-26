#ifndef ANIMAL_GAME_DIRT_PATTERN_OVERLAY_INCLUDED
#define ANIMAL_GAME_DIRT_PATTERN_OVERLAY_INCLUDED

float2 DirtOverlayUv(float2 uv)
{
    return float2(uv.y, -uv.x);
}

float4 CompositeDirtPattern(float4 baseLayer, float4 overlay)
{
    float overlayAlpha = overlay.a * .3;
    float baseWeight = baseLayer.a * (1 - overlayAlpha);
    float alpha = overlayAlpha + baseWeight;
    float3 color = alpha > .00001 ? (overlay.rgb * overlayAlpha + baseLayer.rgb * baseWeight) / alpha : 0;
    return float4(color, alpha);
}

#endif

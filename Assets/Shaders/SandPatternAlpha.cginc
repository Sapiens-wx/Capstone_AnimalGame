#ifndef ANIMAL_GAME_SAND_PATTERN_ALPHA_INCLUDED
#define ANIMAL_GAME_SAND_PATTERN_ALPHA_INCLUDED

float4 CompositeSandPattern(float4 baseLayer, float4 overlay)
{
    float baseWeight = baseLayer.a * (1 - overlay.a);
    float alpha = overlay.a + baseWeight;
    float3 color = alpha > .00001 ? (overlay.rgb * overlay.a + baseLayer.rgb * baseWeight) / alpha : 0;
    // Lift the sparse grains after filtering and before applying terrain coverage.
    return float4(color, saturate(alpha * 2));
}

#endif

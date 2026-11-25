#ifndef ANIMAL_GAME_ROCK_PATTERN_EDGE_FADE_INCLUDED
#define ANIMAL_GAME_ROCK_PATTERN_EDGE_FADE_INCLUDED

float RockPatternEdgeFade(float2 uv)
{
    float2 repeatedUv = frac(uv);
    float2 edgeDistance = min(repeatedUv, 1 - repeatedUv);
    // Fade the outer 12% of each rotated texture repeat to 70% of its source alpha.
    return lerp(.7, 1, smoothstep(0, .12, min(edgeDistance.x, edgeDistance.y)));
}

float2 RockPatternBlend(float2 cellUv)
{
    // Keep a single rotated repeat away from the transition strip so gaps remain visible.
    return smoothstep(.38, .62, cellUv);
}

float RockPatternSparseAlpha(float alpha)
{
    // Clear the low-alpha fill left by overlapping repeats, keeping the brighter grains.
    return alpha * smoothstep(.02, .065, alpha);
}

#endif

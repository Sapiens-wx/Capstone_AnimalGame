#ifndef ANIMAL_GAME_ROCK_PATTERN_EDGE_FADE_INCLUDED
#define ANIMAL_GAME_ROCK_PATTERN_EDGE_FADE_INCLUDED

float RockPatternEdgeFade(float2 uv)
{
    float2 repeatedUv = frac(uv);
    float2 edgeDistance = min(repeatedUv, 1 - repeatedUv);
    // Fade the outer 12% of each rotated texture repeat to 70% of its source alpha.
    return lerp(.7, 1, smoothstep(0, .12, min(edgeDistance.x, edgeDistance.y)));
}

#endif

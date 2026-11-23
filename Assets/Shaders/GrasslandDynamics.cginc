sampler2D _GrassMaskTex;
float4 _GrassMaskTex_TexelSize;
sampler2D _GrassPatternTex;
sampler2D _GrassOverlay1, _GrassOverlay2, _GrassOverlay3;
float4 _GrassOverlaySettings;
float4 _GrassContourOpacity; // baked edge/interior multipliers, target edge/interior opacity
float _GrassEnabled;
float _GrassTileSize;
float4 _GrassTileOrigin;
float4x4 _GrassMetersToWorld;
float4 _GrassRects[8];
float4 _GrassAlphaNormalization;
float4 _GrassLayout; // seed, position jitter, direction region scale, per-blade angle variation
float4 _GrassShape; // density relative to eight blades per repeat, blade length scale
float _GrassActive;
float _GrassNow;
float4 _GrassFootprints[8];
float4 _GrassFootprintDirections[8];
float4 _GrassDynamics; // wind tip offset, pressed tip offset, pressed length, recovery seconds
float4 _GrassWindPeriods;

#ifndef GRASS_NEIGHBOUR_RADIUS
#define GRASS_NEIGHBOUR_RADIUS 1
#endif

float GrassHash(float2 p)
{
    float3 q = frac(float3(p.x, p.y, p.x) * .1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float GrassSpacing()
{
    return _GrassTileSize * rsqrt(8 * _GrassShape.x);
}

// Estimate how much of the screen pixel is inside this blade's texture rectangle.
// Restrict the sample to that covered footprint to reduce bleed from nearby atlas blades.
float GrassClipFootprint(inout float2 uv, inout float2 dx, inout float2 dy, float2 lower, float2 upper, float limitDistantSpread)
{
    // Measure the footprint along each texture axis. Its projected length is
    // invariant under camera roll, unlike the bounds of a rotated screen square.
    float2 projectedWidth = max(sqrt(dx * dx + dy * dy), .000001);
    // Broaden edge integration for undersampled blades and taper it off at
    // overview scales, where widening would fill the gaps between blades.
    float pixelToBladeWidth = projectedWidth.x / (upper.x - lower.x);
    float thinBlade = limitDistantSpread * smoothstep(1, 3, pixelToBladeWidth)
        * (1 - smoothstep(3.05, 3.65, pixelToBladeWidth));
    float2 width = projectedWidth * (1 + thinBlade);
    float2 insideMin = max(lower, uv - width * .5);
    float2 insideMax = min(upper, uv + width * .5);
    float2 covered = saturate((insideMax - insideMin) / width);
    float edgeWeight = 1;
    if (limitDistantSpread > .5)
    {
        float2 extent = upper - lower;
        float2 outside = max(lower - uv, uv - upper);
        float broadPixel = smoothstep(3, 6, pixelToBladeWidth);
        // At map scale, keep the thin blade silhouettes from washing over all the gaps.
        edgeWeight = 1 - broadPixel * smoothstep(0, extent.x * .5, outside.x);
    }
    uv = (insideMin + insideMax) * .5;
    // These equivalent axis-aligned gradients also keep the isotropic mip level
    // stable when screen X and Y rotate relative to the leaf.
    float2 clippedWidth = projectedWidth * covered;
    dx = float2(clippedWidth.x, 0);
    dy = float2(0, clippedWidth.y);
    return covered.x * covered.y * edgeWeight;
}

float GrassDirection(float2 meters)
{
    float2 p = meters / (_GrassTileSize * _GrassLayout.z);
    float2 cell = floor(p);
    float2 f = frac(p);
    f = f * f * (3 - 2 * f);
    float2 seed = _GrassLayout.x * float2(.73, 1.37) + float2(157, 311);
    float value = lerp(lerp(GrassHash(cell + seed), GrassHash(cell + float2(1, 0) + seed), f.x),
        lerp(GrassHash(cell + float2(0, 1) + seed), GrassHash(cell + 1 + seed), f.x), f.y);
    return (value * 2 - 1) * 3.14159265;
}

// The unbent midpoint and source slice are cheap enough to reject distant blades
// before evaluating regional direction noise or trigonometry.
float3 GrassPlacement(float2 cell)
{
    float2 key = cell + _GrassLayout.x * float2(1.13, .79);
    float2 jitter = float2(GrassHash(key + 17.3), GrassHash(key + 43.7)) * 2 - 1;
    float2 centre = (cell + .5 + jitter * _GrassLayout.y) * GrassSpacing();
    return float3(centre, floor(GrassHash(key + 123.4) * 8));
}

float GrassBladeAngle(float2 cell, float2 centre)
{
    float2 key = cell + _GrassLayout.x * float2(1.13, .79);
    return GrassDirection(centre) + (GrassHash(key + 91.1) * 2 - 1) * _GrassLayout.w;
}

// Expose the same instance data to the GPU validation probe.
float4 GrassInstance(float2 cell)
{
    float3 placement = GrassPlacement(cell);
    return float4(placement.xy, GrassBladeAngle(cell, placement.xy), placement.z);
}

float GrassValueNoise(float2 p)
{
    float2 cell = floor(p);
    float2 f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(GrassHash(cell), GrassHash(cell + float2(1, 0)), f.x),
        lerp(GrassHash(cell + float2(0, 1)), GrassHash(cell + 1), f.x), f.y);
}

// The lattice only bounds the search. Widely jittered centres and domain-warped,
// two-scale noise remove fixed patch intervals and create irregular growing regions.
float2 GrassOverlayGroupCentre(float2 key, float2 cell, float groupSpacing)
{
    float2 jitter = float2(GrassHash(key + 31), GrassHash(key + 57)) * 2 - 1;
    return (cell + .5 + jitter * .45) * groupSpacing;
}

float GrassOverlayGroupCount(float2 centre, float2 key, float2 seed, float groupSpacing)
{
    float2 p = centre / groupSpacing;
    float2 warp = (float2(GrassValueNoise(p * .47 + seed + 13.7),
        GrassValueNoise(p * .47 + seed + 71.3)) - .5) * 1.8;
    float noise = .72 * GrassValueNoise(p * .68 + warp + seed)
        + .28 * GrassValueNoise(p * 1.7 + seed + 91.1);
    return noise < .55 * _GrassOverlaySettings.x
        ? 4 + floor(GrassHash(key + 83) * 5) : 0;
}

float3 GrassOverlayGroup(float2 cell, float groupSpacing)
{
    float2 seed = _GrassLayout.x * float2(.37, 1.91) + 521.7;
    float2 key = cell + seed;
    float2 centre = GrassOverlayGroupCentre(key, cell, groupSpacing);
    return float3(centre, GrassOverlayGroupCount(centre, key, seed, groupSpacing));
}

float2 GrassOverlayClusterCentre(float2 cell, float3 group, int index, float spacing)
{
    float2 key = cell + _GrassLayout.x + 739.1;
    float angle = GrassHash(key + 113) * 6.2831853 + index * 2.39996323
        + (GrassHash(key + index * 19.7 + 137) - .5) * .9;
    float radius = spacing * sqrt((index + .5) / 3.14159265)
        * lerp(.85, 1.15, GrassHash(key + index * 31.3 + 173));
    float2 shape = float2(lerp(.8, 1.2, GrassHash(key + 197)),
        lerp(.8, 1.2, GrassHash(key + 211)));
    return group.xy + float2(cos(angle), sin(angle)) * radius * shape;
}

float4 SampleGrassOverlay(float2 meters, float2 pixelDx, float2 pixelDy, float animate)
{
    if (_GrassOverlaySettings.z < .5 || _GrassOverlaySettings.x <= 0) return 0;
    float spacing = _GrassTileSize * .8;
    float groupSpacing = spacing * 7.5;
    float2 cell = floor(meters / groupSpacing);
    float size = _GrassTileSize * _GrassOverlaySettings.y;
    float4 result = 0;
    int bits = (int)_GrassOverlaySettings.z;
    int count = (bits & 1) + ((bits >> 1) & 1) + ((bits >> 2) & 1);
    float pixelRadius = sqrt(.5 * (dot(pixelDx, pixelDx) + dot(pixelDy, pixelDy)));
    float groupSupport = spacing * 2.15 + size * 2.88 + pixelRadius;
    float2 groupSeed = _GrassLayout.x * float2(.37, 1.91) + 521.7;
    // Include neighbouring patch centres, so tall rotated grass is not clipped
    // at the search-cell or map-tile boundary.
    [loop] for (int y = -1; y <= 1; y++)
    [loop] for (int x = -1; x <= 1; x++)
    {
        float2 groupCell = cell + float2(x, y);
        float2 groupKey = groupCell + groupSeed;
        float2 displacement = meters - (groupCell + .5) * groupSpacing;
        float2 nearest = max(0, abs(displacement) - groupSpacing * .45);
        [branch] if (dot(nearest, nearest) > groupSupport * groupSupport) continue;
        float2 groupCentre = GrassOverlayGroupCentre(groupKey, groupCell, groupSpacing);
        float2 groupDelta = meters - groupCentre;
        [branch] if (dot(groupDelta, groupDelta) > groupSupport * groupSupport) continue;
        float3 group = float3(groupCentre, GrassOverlayGroupCount(groupCentre, groupKey, groupSeed, groupSpacing));
        if (group.z < .5) continue;
        [loop] for (int instance = 0; instance < 8; instance++)
        {
            if (instance >= (int)group.z) break;
            float2 key = groupCell + _GrassLayout.x + 739.1 + instance * float2(17.73, 37.31);
            float2 instanceSize = size * float2(lerp(.8, 1.2, GrassHash(key + 107)),
                lerp(.96, 2.4, GrassHash(key + 149)));
            float2 centre = GrassOverlayClusterCentre(groupCell, group, instance, spacing);
            float2 delta = meters - centre;
            float support = instanceSize.y * .95 + instanceSize.x * .5 + pixelRadius;
            if (dot(delta, delta) > support * support) continue;
            float s, c;
            sincos(GrassDirection(centre), s, c);
            float2 side = float2(c, -s), up = float2(s, c);
            float2 centreWorld = mul(_GrassMetersToWorld, float4(centre, 0, 1)).xy;
            float2 sideWorld = mul((float3x3)_GrassMetersToWorld, float3(side, 0)).xy;
            float pressure = 0, direction = 0;
            if (animate > .5)
            {
                [unroll] for (int contact = 0; contact < 8; contact++)
                {
                    float4 footprint = _GrassFootprints[contact];
                    float2 travel = _GrassFootprintDirections[contact].xy;
                    if (dot(travel, travel) < .0001) continue;
                    float age = max(0, _GrassNow - footprint.z);
                    float2 offset = centreWorld - footprint.xy;
                    float distanceSquared = dot(offset, offset);
                    float reach = max(.001, footprint.w);
                    [branch] if (age >= max(.01, _GrassDynamics.w) || distanceSquared >= reach * reach) continue;
                    float influence = (1 - smoothstep(footprint.w * .65, reach, sqrt(distanceSquared)))
                        * saturate(1 - age / max(.01, _GrassDynamics.w));
                    if (influence > pressure)
                    {
                        pressure = influence;
                        direction = -dot(travel, normalize(sideWorld));
                    }
                }
            }
            // Stable per-cluster wind uses the same pause-aware clock as small grass.
            float windRandom = GrassHash(key + 271.9);
            float windPeriod = lerp(_GrassWindPeriods.x, _GrassWindPeriods.y, windRandom) * 1.5;
            float wind = animate * lerp(.025, .04, GrassHash(key + 293.7))
                * sin(_GrassNow * 6.2831853 / max(.1, windPeriod) + windRandom * 6.2831853);
            // Root-anchored wind and contact sway, without shortening tall grass.
            float lengthScale = 1;
            float height = (dot(delta, up) / instanceSize.y + .5) / lengthScale;
            float tipOffset = wind * (1 - pressure) + pressure * direction * .30;
            float bendHeight = saturate(height);
            float2 uv = float2(dot(delta, side) / instanceSize.x + .5
                - tipOffset * instanceSize.y / instanceSize.x * bendHeight * bendHeight, height);
            float shear = 2 * tipOffset * bendHeight / lengthScale;
            float2 dx = float2((dot(pixelDx, side) - shear * dot(pixelDx, up)) / instanceSize.x,
                dot(pixelDx, up) / (instanceSize.y * lengthScale));
            float2 dy = float2((dot(pixelDy, side) - shear * dot(pixelDy, up)) / instanceSize.x,
                dot(pixelDy, up) / (instanceSize.y * lengthScale));
            // Stable per-instance mirroring; mirror sampling derivatives with the UV.
            float flipX = GrassHash(key + 233) < .5 ? -1 : 1;
            uv.x = .5 + (uv.x - .5) * flipX;
            dx.x *= flipX;
            dy.x *= flipX;
            float coverage = GrassClipFootprint(uv, dx, dy, 0, 1, 0);
            if (coverage <= 0) continue;
            int variant = 0;
            if (count > 1)
            {
                int choice = min(count - 1, (int)floor(GrassHash(key + 83) * count));
                [unroll] for (int i = 0; i < 3; i++)
                    if ((bits & (1 << i)) != 0) { if (choice == 0) { variant = i; break; } choice--; }
            }
            else
            {
                variant = bits == 1 ? 0 : bits == 2 ? 1 : 2;
            }
            float4 sample = variant == 0 ? tex2Dgrad(_GrassOverlay1, uv, dx, dy)
                : variant == 1 ? tex2Dgrad(_GrassOverlay2, uv, dx, dy) : tex2Dgrad(_GrassOverlay3, uv, dx, dy);
            sample.a *= coverage;
            result.rgb = sample.rgb * sample.a + result.rgb * (1 - sample.a);
            result.a = sample.a + result.a * (1 - sample.a);
        }
    }
    result.rgb = result.a > .0001 ? result.rgb / result.a : 0;
    return result;
}

float4 SampleGrassBlades(float2 meters, float2 pixelDx, float2 pixelDy, float animate)
{
    float spacing = GrassSpacing();
    float2 cell = floor(meters / spacing);
    float pixelRadius = sqrt(.5 * (dot(pixelDx, pixelDx) + dot(pixelDy, pixelDy)));
    // At broad zooms the footprint can reach one extra row of candidate blades.
    int searchRadius = max(GRASS_NEIGHBOUR_RADIUS, 1 + (int)(pixelRadius > spacing * .35));
    float4 result = 0;
    [loop] for (int y = -searchRadius; y <= searchRadius; y++)
    [loop] for (int x = -searchRadius; x <= searchRadius; x++)
    {
        float2 bladeCell = cell + float2(x, y);
        float3 placement = GrassPlacement(bladeCell);
        float4 rect = _GrassRects[(int)placement.z];
        float bladeLength = rect.w * _GrassTileSize * _GrassShape.y;
        float2 delta = meters - placement.xy;
        // Includes the largest configured bend and blade width. With <=40% jitter
        // this support fits the 3x3 neighbourhood even at cell corners.
        float radius = bladeLength * .72 + rect.z * _GrassTileSize * .5 + pixelRadius;
        if (dot(delta, delta) > radius * radius) continue;
        float s, c;
        sincos(GrassBladeAngle(bladeCell, placement.xy), s, c);
        float2 side = float2(c, -s), up = float2(s, c);
        float2 q = float2(dot(delta, side), dot(delta, up) + bladeLength * .5);
        if (q.y < -pixelRadius || q.y > bladeLength + pixelRadius) continue;
        float2 centreWorld = mul(_GrassMetersToWorld, float4(placement.xy, 0, 1)).xy;
        float2 sideWorld = mul((float3x3)_GrassMetersToWorld, float3(side, 0)).xy;
        float pressure = 0;
        float direction = 1;
        if (animate > .5)
        {
            [unroll] for (int contact = 0; contact < 8; contact++)
            {
                float4 footprint = _GrassFootprints[contact];
                float age = max(0, _GrassNow - footprint.z);
                float2 offset = centreWorld - footprint.xy;
                float distanceSquared = dot(offset, offset);
                float reach = max(.001, footprint.w);
                [branch] if (age >= max(.01, _GrassDynamics.w) || distanceSquared >= reach * reach) continue;
                float influence = (1 - smoothstep(footprint.w * .65, reach, sqrt(distanceSquared)))
                    * saturate(1 - age / max(.01, _GrassDynamics.w));
                if (influence > pressure)
                {
                    pressure = influence;
                    direction = dot(centreWorld - footprint.xy, sideWorld) >= 0 ? 1 : -1;
                }
            }
        }
        float random = GrassHash(bladeCell + _GrassLayout.x + 271.9);
        float period = lerp(_GrassWindPeriods.x, _GrassWindPeriods.y, random);
        float wind = animate * _GrassDynamics.x * lerp(.65, 1, random)
            * sin(_GrassNow * 6.2831853 / max(.1, period) + random * 6.2831853);
        float lengthScale = lerp(1, _GrassDynamics.z, pressure);
        float height = saturate(q.y / (bladeLength * lengthScale));
        float tipOffset = wind * (1 - pressure) + direction * pressure * _GrassDynamics.y;
        q.x -= tipOffset * bladeLength * height * height;
        float2 sourceUv = rect.xy + float2(rect.z * .5, 0)
            + float2(q.x, q.y / (lengthScale * _GrassShape.y)) / _GrassTileSize;
        // Rotate and deform the sampling footprint too, preserving sharp edges and
        // stable mip selection rather than differentiating random cell identities.
        float shear = 2 * tipOffset * height / lengthScale;
        float2 dx = float2(dot(pixelDx, side) - shear * dot(pixelDx, up), dot(pixelDx, up) / (lengthScale * _GrassShape.y)) / _GrassTileSize;
        float2 dy = float2(dot(pixelDy, side) - shear * dot(pixelDy, up), dot(pixelDy, up) / (lengthScale * _GrassShape.y)) / _GrassTileSize;
        float coverage = GrassClipFootprint(sourceUv, dx, dy, rect.xy, rect.xy + rect.zw, 1);
        if (coverage <= 0) continue;
        float4 grass = tex2Dgrad(_GrassPatternTex, sourceUv, dx, dy);
        grass.a = lerp(grass.a, saturate(grass.a / max(.001, _GrassAlphaNormalization.x)),
            _GrassAlphaNormalization.y) * coverage;
        result.rgb += grass.rgb * grass.a;
        result.a += grass.a;
    }
    result.rgb = result.a > .0001 ? result.rgb / result.a : 0;
    result.a = saturate(result.a);
    return result;
}

float4 SampleGrassland(float2 tileUv, float2 pixelDx, float2 pixelDy, float animate)
{
    // Smooth the low-alpha 8-bit bake before mapping it to the target opacity range.
    // Filter coverage only; grass silhouettes and their source texture stay sharp.
    float4 mask = tex2D(_GrassMaskTex, tileUv);
    float2 maskStep = _GrassMaskTex_TexelSize.xy * 1.5;
    mask.a = (mask.a * 4
        + tex2D(_GrassMaskTex, tileUv + float2(maskStep.x, 0)).a
        + tex2D(_GrassMaskTex, tileUv - float2(maskStep.x, 0)).a
        + tex2D(_GrassMaskTex, tileUv + float2(0, maskStep.y)).a
        + tex2D(_GrassMaskTex, tileUv - float2(0, maskStep.y)).a) * .125;
    float2 meters = tileUv * _MapSizeMeters.xy + _GrassTileOrigin.xy;
    if (mask.a < .0001) return 0;

    // Keep empty space transparent at every zoom; a whole-atlas average creates a grey floor.
    float4 result = SampleGrassBlades(meters, pixelDx, pixelDy, animate);

    result.rgb *= mask.rgb;
    // Decode contour fading from the baked mask separately from terrain blending.
    float baseOpacity = max(.001, _GrassOverlaySettings.w);
    float contourCoverage = mask.a / baseOpacity;
    float interior = saturate((_GrassContourOpacity.x - contourCoverage)
        / max(.001, _GrassContourOpacity.x - _GrassContourOpacity.y));
    float contourMultiplier = lerp(_GrassContourOpacity.x, _GrassContourOpacity.y, interior);
    float terrainCoverage = saturate(contourCoverage / max(.001, contourMultiplier));
    // Use the full smooth transition without squaring away the intermediate opacity range.
    float edgeInfluence = 1 - smoothstep(0, 1, interior);
    float grassOpacity = terrainCoverage * lerp(_GrassContourOpacity.w, _GrassContourOpacity.z,
        edgeInfluence);
    result.a *= grassOpacity;
    float4 overlay = SampleGrassOverlay(meters, pixelDx, pixelDy, animate);
    overlay.a *= grassOpacity;
    float alpha = overlay.a + result.a * (1 - overlay.a);
    result.rgb = alpha > .0001 ? (overlay.rgb * overlay.a + result.rgb * result.a * (1 - overlay.a)) / alpha : 0;
    result.a = min(alpha, grassOpacity);
    return result;
}

float4 CombineGrassland(float4 ground, float4 grass)
{
    float alpha = ground.a + grass.a;
    return float4(alpha > .0001 ? (ground.rgb * ground.a + grass.rgb * grass.a) / alpha : 0,
        saturate(alpha));
}

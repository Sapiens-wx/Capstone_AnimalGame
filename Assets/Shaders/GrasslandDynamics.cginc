sampler2D _GrassMaskTex;
sampler2D _GrassPatternTex;
sampler2D _GrassOverlay1, _GrassOverlay2, _GrassOverlay3;
float4 _GrassOverlaySettings;
float4 _GrassContourOpacity; // baked edge/interior multipliers, small/tall interior opacity
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
float3 GrassOverlayGroup(float2 cell, float groupSpacing)
{
    float2 seed = _GrassLayout.x * float2(.37, 1.91) + 521.7;
    float2 key = cell + seed;
    float2 jitter = float2(GrassHash(key + 31), GrassHash(key + 57)) * 2 - 1;
    float2 centre = (cell + .5 + jitter * .45) * groupSpacing;
    float2 p = centre / groupSpacing;
    float2 warp = (float2(GrassValueNoise(p * .47 + seed + 13.7),
        GrassValueNoise(p * .47 + seed + 71.3)) - .5) * 1.8;
    float noise = .72 * GrassValueNoise(p * .68 + warp + seed)
        + .28 * GrassValueNoise(p * 1.7 + seed + 91.1);
    float count = noise < .55 * _GrassOverlaySettings.x
        ? 4 + floor(GrassHash(key + 83) * 5) : 0;
    return float3(centre, count);
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
    // Include neighbouring patch centres, so tall rotated grass is not clipped
    // at the search-cell or map-tile boundary.
    [loop] for (int y = -1; y <= 1; y++)
    [loop] for (int x = -1; x <= 1; x++)
    {
        float2 groupCell = cell + float2(x, y);
        float3 group = GrassOverlayGroup(groupCell, groupSpacing);
        if (group.z < .5) continue;
        float groupSupport = spacing * 2.15 + size * 2.88;
        float2 groupDelta = meters - group.xy;
        if (dot(groupDelta, groupDelta) > groupSupport * groupSupport) continue;
        [loop] for (int instance = 0; instance < 8; instance++)
        {
            if (instance >= (int)group.z) break;
            float2 key = groupCell + _GrassLayout.x + 739.1 + instance * float2(17.73, 37.31);
            float2 instanceSize = size * float2(lerp(.8, 1.2, GrassHash(key + 107)),
                lerp(.96, 2.4, GrassHash(key + 149)));
            float2 centre = GrassOverlayClusterCentre(groupCell, group, instance, spacing);
            float2 delta = meters - centre;
            float support = instanceSize.y * .95 + instanceSize.x * .5;
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
                    float influence = (1 - smoothstep(footprint.w * .65, max(.001, footprint.w),
                        distance(centreWorld, footprint.xy)))
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
            float2 uv = float2(dot(delta, side) / instanceSize.x + .5
                - tipOffset * instanceSize.y / instanceSize.x * height * height, height);
            if (any(uv < 0) || any(uv > 1)) continue;
            float shear = 2 * tipOffset * height / lengthScale;
            float2 dx = float2((dot(pixelDx, side) - shear * dot(pixelDx, up)) / instanceSize.x,
                dot(pixelDx, up) / (instanceSize.y * lengthScale));
            float2 dy = float2((dot(pixelDy, side) - shear * dot(pixelDy, up)) / instanceSize.x,
                dot(pixelDy, up) / (instanceSize.y * lengthScale));
            // Stable per-instance mirroring; mirror sampling derivatives with the UV.
            float flipX = GrassHash(key + 233) < .5 ? -1 : 1;
            uv.x = .5 + (uv.x - .5) * flipX;
            dx.x *= flipX;
            dy.x *= flipX;
            int choice = min(count - 1, (int)floor(GrassHash(key + 83) * count));
            int variant = 0;
            [unroll] for (int i = 0; i < 3; i++)
                if ((bits & (1 << i)) != 0) { if (choice == 0) { variant = i; break; } choice--; }
            float4 sample = variant == 0 ? tex2Dgrad(_GrassOverlay1, uv, dx, dy)
                : variant == 1 ? tex2Dgrad(_GrassOverlay2, uv, dx, dy) : tex2Dgrad(_GrassOverlay3, uv, dx, dy);
            result.rgb = sample.rgb * sample.a + result.rgb * (1 - sample.a);
            result.a = sample.a + result.a * (1 - sample.a);
        }
    }
    result.rgb = result.a > .0001 ? result.rgb / result.a : 0;
    return result;
}

float4 SampleGrassBlades(float2 meters, float2 pixelDx, float2 pixelDy, float animate)
{
    float2 cell = floor(meters / GrassSpacing());
    float4 result = 0;
    [loop] for (int y = -GRASS_NEIGHBOUR_RADIUS; y <= GRASS_NEIGHBOUR_RADIUS; y++)
    [loop] for (int x = -GRASS_NEIGHBOUR_RADIUS; x <= GRASS_NEIGHBOUR_RADIUS; x++)
    {
        float2 bladeCell = cell + float2(x, y);
        float3 placement = GrassPlacement(bladeCell);
        float4 rect = _GrassRects[(int)placement.z];
        float bladeLength = rect.w * _GrassTileSize * _GrassShape.y;
        float2 delta = meters - placement.xy;
        // Includes the largest configured bend and blade width. With <=40% jitter
        // this support fits the 3x3 neighbourhood even at cell corners.
        float radius = bladeLength * .72 + rect.z * _GrassTileSize * .5;
        if (dot(delta, delta) > radius * radius) continue;
        float s, c;
        sincos(GrassBladeAngle(bladeCell, placement.xy), s, c);
        float2 side = float2(c, -s), up = float2(s, c);
        float2 q = float2(dot(delta, side), dot(delta, up) + bladeLength * .5);
        if (q.y < 0 || q.y > bladeLength) continue;
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
                float distanceToPlayer = distance(centreWorld, footprint.xy);
                float influence = (1 - smoothstep(footprint.w * .65, max(.001, footprint.w), distanceToPlayer))
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
        float height = q.y / (bladeLength * lengthScale);
        float tipOffset = wind * (1 - pressure) + direction * pressure * _GrassDynamics.y;
        q.x -= tipOffset * bladeLength * height * height;
        float2 sourceUv = rect.xy + float2(rect.z * .5, 0)
            + float2(q.x, q.y / (lengthScale * _GrassShape.y)) / _GrassTileSize;
        if (sourceUv.x < rect.x || sourceUv.x > rect.x + rect.z ||
            sourceUv.y < rect.y || sourceUv.y > rect.y + rect.w) continue;
        // Rotate and deform the sampling footprint too, preserving sharp edges and
        // stable mip selection rather than differentiating random cell identities.
        float shear = 2 * tipOffset * height / lengthScale;
        float2 dx = float2(dot(pixelDx, side) - shear * dot(pixelDx, up), dot(pixelDx, up) / (lengthScale * _GrassShape.y));
        float2 dy = float2(dot(pixelDy, side) - shear * dot(pixelDy, up), dot(pixelDy, up) / (lengthScale * _GrassShape.y));
        float4 grass = tex2Dgrad(_GrassPatternTex, sourceUv, dx / _GrassTileSize, dy / _GrassTileSize);
        grass.a = lerp(grass.a, saturate(grass.a / max(.001, _GrassAlphaNormalization.x)),
            _GrassAlphaNormalization.y);
        result.rgb += grass.rgb * grass.a;
        result.a += grass.a;
    }
    result.rgb = result.a > .0001 ? result.rgb / result.a : 0;
    result.a = saturate(result.a);
    return result;
}

float4 SampleGrassland(float2 tileUv, float animate)
{
    float4 mask = tex2D(_GrassMaskTex, tileUv);
    float2 meters = tileUv * _MapSizeMeters.xy + _GrassTileOrigin.xy;
    float2 pixelDx = ddx(meters), pixelDy = ddy(meters);
    if (mask.a < .0001) return 0;

    // Once a pixel spans a blade's width, a single sample can miss the blade.
    // The last atlas mip is the area average of all eight blade slices per repeat.
    float footprint = max(length(pixelDx), length(pixelDy));
    float bladeWidth = _GrassRects[0].z * _GrassTileSize;
    float averageWeight = smoothstep(.5, 1.5, footprint / max(.0001, bladeWidth));
    float4 result = 0;
    [branch] if (averageWeight < 1)
        result = SampleGrassBlades(meters, pixelDx, pixelDy, animate);
    [branch] if (averageWeight > 0)
    {
        float4 average = tex2Dlod(_GrassPatternTex, float4(.5, .5, 0, 16));
        average.a = saturate(average.a * _GrassShape.x * _GrassShape.y);
        float3 premultiplied = lerp(result.rgb * result.a, average.rgb * average.a, averageWeight);
        result.a = lerp(result.a, average.a, averageWeight);
        result.rgb = result.a > .0001 ? premultiplied / result.a : 0;
    }

    result.rgb *= mask.rgb;
    // Keep the palette opacity as the full-strength endpoint of the small blades.
    // Lift faint baked coverage before compositing thin blades over the black map.
    float baseOpacity = max(.001, _GrassOverlaySettings.w);
    float contourCoverage = mask.a / baseOpacity;
    float interior = saturate((_GrassContourOpacity.x - contourCoverage)
        / max(.001, _GrassContourOpacity.x - _GrassContourOpacity.y));
    float smallInteriorScale = _GrassContourOpacity.z
        / max(.001, baseOpacity * sqrt(max(0, _GrassContourOpacity.y)));
    result.a *= sqrt(mask.a * max(mask.a, _GrassOverlaySettings.w))
        * lerp(1, smallInteriorScale, interior);
    float4 overlay = SampleGrassOverlay(meters, pixelDx, pixelDy, animate);
    // Remove the base palette opacity from coverage: overlay retains source alpha at full strength.
    float tallInteriorScale = _GrassContourOpacity.w / max(.001, _GrassContourOpacity.y);
    overlay.a *= saturate(contourCoverage) * lerp(1, tallInteriorScale, interior);
    float alpha = overlay.a + result.a * (1 - overlay.a);
    result.rgb = alpha > .0001 ? (overlay.rgb * overlay.a + result.rgb * result.a * (1 - overlay.a)) / alpha : 0;
    result.a = alpha;
    return result;
}

float4 CombineGrassland(float4 ground, float4 grass)
{
    float alpha = ground.a + grass.a;
    return float4(alpha > .0001 ? (ground.rgb * ground.a + grass.rgb * grass.a) / alpha : 0,
        saturate(alpha));
}

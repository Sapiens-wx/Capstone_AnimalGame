sampler2D _GrassMaskTex;
sampler2D _GrassPatternTex;
sampler2D _GrassOverlay1, _GrassOverlay2, _GrassOverlay3;
float4 _GrassOverlaySettings;
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

// Each occupied world-space block contains one connected group of 5–10 clusters.
bool GrassOverlayCluster(float2 cell)
{
    float2 block = floor(cell / 10);
    float2 key = block + _GrassLayout.x * float2(.37, 1.91) + 521.7;
    if (GrassHash(key) >= _GrassOverlaySettings.x) return false;
    float2 local = cell - block * 10;
    if (GrassHash(key + 17) < .5) local = local.yx;
    if (GrassHash(key + 31) < .5) local.x = 9 - local.x;
    if (GrassHash(key + 57) < .5) local.y = 9 - local.y;
    local -= 3;
    if (local.x < 0 || local.x > 3 || local.y < 0 || local.y > 2) return false;
    float column = fmod(local.y, 2) < .5 ? local.x : 3 - local.x;
    float rank = local.y * 4 + column;
    float clusterCount = 5 + floor(GrassHash(key + 83) * 6);
    return rank < clusterCount;
}

float4 SampleGrassOverlay(float2 meters, float2 pixelDx, float2 pixelDy, float animate)
{
    if (_GrassOverlaySettings.z < .5 || _GrassOverlaySettings.x <= 0) return 0;
    float spacing = _GrassTileSize * .8;
    float2 cell = floor(meters / spacing);
    float size = _GrassTileSize * _GrassOverlaySettings.y;
    float4 result = 0;
    int bits = (int)_GrassOverlaySettings.z;
    int count = (bits & 1) + ((bits >> 1) & 1) + ((bits >> 2) & 1);
    // Preserve full rotated support with the denser layout.
    [loop] for (int y = -5; y <= 5; y++)
    [loop] for (int x = -5; x <= 5; x++)
    {
        float2 key = cell + float2(x, y) + _GrassLayout.x + 739.1;
        if (!GrassOverlayCluster(cell + float2(x, y))) continue;
        float2 instanceSize = size * float2(lerp(.8, 1.2, GrassHash(key + 107)),
            lerp(1.2, 3.0, GrassHash(key + 149)));
        float2 centre = (cell + float2(x, y) + .5
            + (float2(GrassHash(key + 31), GrassHash(key + 57)) * 2 - 1) * .12) * spacing;
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
    result.rgb = result.a > .0001 ? result.rgb / result.a : 0;
    return result;
}

float4 SampleGrassland(float2 tileUv, float animate)
{
    float4 mask = tex2D(_GrassMaskTex, tileUv);
    if (mask.a < .0001) return 0;
    float2 meters = tileUv * _MapSizeMeters.xy + _GrassTileOrigin.xy;
    float2 pixelDx = ddx(meters), pixelDy = ddy(meters);
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
    result.rgb *= mask.rgb;
    result.a = saturate(result.a) * mask.a;
    float4 overlay = SampleGrassOverlay(meters, pixelDx, pixelDy, animate);
    // Remove the base palette opacity from coverage: overlay retains source alpha at full strength.
    overlay.a *= saturate(mask.a / max(.001, _GrassOverlaySettings.w));
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

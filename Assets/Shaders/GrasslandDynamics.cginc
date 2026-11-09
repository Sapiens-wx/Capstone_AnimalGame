sampler2D _GrassMaskTex;
sampler2D _GrassPatternTex;
float _GrassEnabled;
float _GrassTileSize;
float4 _GrassTileOrigin;
float4x4 _GrassMetersToWorld;
float4 _GrassRects[4];
float4 _GrassAlphaNormalization;
float _GrassActive;
float _GrassNow;
float4 _GrassFootprints[8];
float4 _GrassDynamics; // wind tip offset, pressed tip offset, pressed length, recovery seconds
float4 _GrassWindPeriods;

float GrassHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float4 SampleGrassland(float2 tileUv, float animate)
{
    float4 mask = tex2D(_GrassMaskTex, tileUv);
    if (mask.a < .0001) return 0;
    float2 meters = tileUv * _MapSizeMeters.xy + _GrassTileOrigin.xy;
    float2 cell = floor(meters / _GrassTileSize);
    float2 uv = frac(meters / _GrassTileSize);
    float4 result = 0;
    [unroll] for (int blade = 0; blade < 4; blade++)
    {
        float4 rect = _GrassRects[blade];
        float2 root = rect.xy + float2(rect.z * .5, 0);
        // Sample the nearest instance of this blade, including a neighbouring cell.
        // Strong bends may cross a repeat edge; their seed and root must cross with them.
        float neighbour = floor(uv.x - root.x + .5);
        float2 bladeCell = cell + float2(neighbour, 0);
        float2 centreMeters = (bladeCell + root + float2(0, rect.w * .5)) * _GrassTileSize;
        float2 centreWorld = mul(_GrassMetersToWorld, float4(centreMeters, 0, 1)).xy;
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
                    float2 sideWorld = mul((float3x3)_GrassMetersToWorld, float3(1, 0, 0)).xy;
                    direction = dot(centreWorld - footprint.xy, sideWorld) >= 0 ? 1 : -1;
                }
            }
        }
        float random = GrassHash(bladeCell + float2(blade * 17.7, blade * 43.3));
        float period = lerp(_GrassWindPeriods.x, _GrassWindPeriods.y, random);
        float wind = animate * _GrassDynamics.x * lerp(.65, 1, random)
            * sin(_GrassNow * 6.2831853 / max(.1, period) + random * 6.2831853);
        float lengthScale = lerp(1, _GrassDynamics.z, pressure);
        float2 q = uv - root;
        q.x -= neighbour;
        float height = q.y / (rect.w * lengthScale);
        float tipOffset = wind * (1 - pressure) + direction * pressure * _GrassDynamics.y;
        q.x -= tipOffset * rect.w * height * height;
        float2 sourceUv = root + float2(q.x, q.y / lengthScale);
        float inside = step(rect.x, sourceUv.x) * step(sourceUv.x, rect.x + rect.z)
            * step(rect.y, sourceUv.y) * step(sourceUv.y, rect.y + rect.w);
        float4 grass = tex2Dgrad(_GrassPatternTex, sourceUv, ddx(uv), ddy(uv));
        grass.a = lerp(grass.a, saturate(grass.a / max(.001, _GrassAlphaNormalization.x)),
            _GrassAlphaNormalization.y) * inside;
        result.rgb += grass.rgb * grass.a;
        result.a += grass.a;
    }
    result.rgb = result.a > .0001 ? result.rgb / result.a : 0;
    result.rgb *= mask.rgb;
    result.a = saturate(result.a) * mask.a;
    return result;
}

float4 CombineGrassland(float4 ground, float4 grass)
{
    float alpha = ground.a + grass.a;
    return float4(alpha > .0001 ? (ground.rgb * ground.a + grass.rgb * grass.a) / alpha : 0,
        saturate(alpha));
}

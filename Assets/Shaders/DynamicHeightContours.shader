Shader "AnimalGame/Dynamic Height Contours"
{
    Properties
    {
        [PerRendererData] _MainTex ("Base Map", 2D) = "white" {}
        [HideInInspector] _PhotoContoursOnly ("Photo Contours Only", Float) = 0
        _HeightTex ("Height Map", 2D) = "black" {}
        _SurfaceTex ("Editor-Baked Terrain Surface", 2D) = "black" {}
        _SurfaceEnabled ("Surface Enabled", Float) = 0
        _GrassMaskTex ("Grassland Coverage", 2D) = "black" {}
        _GrassPatternTex ("Grassland Blades", 2D) = "black" {}
        _GrassEnabled ("Grassland Enabled", Float) = 0
        _GrassTileSize ("Grassland Tile Size", Float) = 3.2
        _SurfaceRevealEnabled ("Surface UI Reveal Enabled", Float) = 0
        _SurfaceRevealCenterPixels ("Surface UI Centre", Vector) = (0, 0, 0, 0)
        _SurfaceRevealRadiusPixels ("Surface UI Radius", Float) = 430
        _SurfaceRevealEdgePixels ("Surface UI Edge", Float) = 4
        _OutsideRevealDarkening ("Outside Reveal Darkening", Range(0, 1)) = 0.3
        _WaterMaskTex ("Static Water Range And Depth", 2D) = "black" {}
        _WaterUvTex ("Baked Water UV (RGHalf)", 2D) = "black" {}
        _WaterMaskBounds ("Water Mask Map UV Bounds", Vector) = (0, 0, 1, 1)
        _WaterPatternTex ("Animated Water Pattern", 2D) = "black" {}
        _WaterEnabled ("Animated Water Enabled", Float) = 0
        _MapSizeMeters ("Map Size Meters", Vector) = (250, 250, 0, 0)
        _WaterOriginMeters ("Water Pattern Origin Meters", Vector) = (0, 0, 0, 0)
        _WaterTileSizeMeters ("Water Tile Size Meters", Float) = 8
        _WaterLayerOneSpeed ("Water Primary Speed", Vector) = (0.36, 0.04, 0, 0)
        _WaterLayerTwoSpeed ("Water Secondary Speed", Vector) = (-0.14, 0.22, 0, 0)
        _WaterLayerTwoScale ("Water Secondary Scale", Float) = 1.35
        _WaterWaveDistortion ("Water Wave Distortion", Float) = 0.03
        _WaterWaveSpeed ("Water Wave Speed", Float) = 0.8
        _WaterWaveLengthMeters ("Water Wave Length Meters", Float) = 12
        _WaterDeepSpeedMultiplier ("Water Deep Speed Multiplier", Float) = 0.65
        _WaterFlowSpeed ("Local Water Flow Speed (m/s)", Float) = 10
        _ContourColor ("Contour Color", Color) = (1, 1, 1, 1)
        _MinimumHeight ("Minimum Height", Float) = 0
        _MaximumHeight ("Maximum Height", Float) = 200
        _VisibleMinimumHeight ("Visible Minimum Contour", Float) = 0
        _VisibleMaximumHeight ("Visible Maximum Contour", Float) = 200
        _ContourInterval ("Contour Interval", Float) = 10
        _MinimumLineWidth ("Minimum Line Width", Float) = 0.75
        _MaximumLineWidth ("Maximum Line Width", Float) = 3
        _MinimumOpacity ("Minimum Opacity", Range(0, 1)) = 0.15
        _MaximumOpacity ("Maximum Opacity", Range(0, 1)) = 1
        _MaximumCoverage ("Maximum Contour Coverage", Range(0.1, 0.7)) = 0.45
        _EdgeSoftness ("Edge Softness", Range(0.1, 1.5)) = 0.4
        _ElevationFilterEnabled ("Relative Elevation Filter Enabled", Float) = 0
        _ElevationFilterProgress ("Relative Elevation Filter Progress", Range(0, 1)) = 0
        _PlayerHeightMeters ("Player Height Meters", Float) = 0
        _LowerElevationColor ("Lower Elevation Color", Color) = (0.12, 0.54, 0.78, 1)
        _HigherElevationColor ("Higher Elevation Color", Color) = (1, 0.82, 0.38, 1)
        _ElevationFilterMaxOpacity ("Elevation Filter Maximum Opacity", Range(0, 1)) = 0.44
        _ElevationFilterRangeMeters ("Elevation Filter Range Meters", Float) = 15
        _ElevationFilterNeutralBandMeters ("Elevation Filter Neutral Band Meters", Float) = 1.5
        _ElevationFilterResponseExponent ("Elevation Filter Response Exponent", Float) = 1.15
        _PlayerHeightReferenceColor ("Player Height Reference Color", Color) = (1, 0.96, 0.7, 1)
        _PlayerHeightReferenceLineWidth ("Player Height Reference Line Width", Float) = 2.4
        _PlayerHeightReferenceOpacity ("Player Height Reference Opacity", Range(0, 1)) = 0.95
        _ElevationFilterHillshadeStrength ("Elevation Filter Hillshade Strength", Range(0, 0.5)) = 0.16
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float4 screenPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            float _PhotoContoursOnly;
            sampler2D _HeightTex;
            sampler2D _SurfaceTex;
            sampler2D _WaterMaskTex;
            sampler2D _WaterUvTex;
            float4 _WaterMaskTex_TexelSize;
            float4 _WaterMaskBounds;
            sampler2D _WaterPatternTex;
            float4 _HeightTex_TexelSize;
            float _SurfaceEnabled;
            float _SurfaceRevealEnabled;
            float4 _SurfaceRevealCenterPixels;
            float _SurfaceRevealRadiusPixels;
            float _SurfaceRevealEdgePixels;
            float _OutsideRevealDarkening;
            float _WaterEnabled;
            float4 _MapSizeMeters;
            float4 _WaterOriginMeters;
            float _WaterTileSizeMeters;
            float4 _WaterLayerOneSpeed;
            float4 _WaterLayerTwoSpeed;
            float _WaterLayerTwoScale;
            float _WaterWaveDistortion;
            float _WaterWaveSpeed;
            float _WaterWaveLengthMeters;
            float _WaterDeepSpeedMultiplier;
            float _WaterFlowSpeed;
            fixed4 _ContourColor;
            float _MinimumHeight;
            float _MaximumHeight;
            float _VisibleMinimumHeight;
            float _VisibleMaximumHeight;
            float _ContourInterval;
            float _MinimumLineWidth;
            float _MaximumLineWidth;
            float _MinimumOpacity;
            float _MaximumOpacity;
            float _MaximumCoverage;
            float _EdgeSoftness;
            float _ElevationFilterEnabled;
            float _ElevationFilterProgress;
            float _PlayerHeightMeters;
            fixed4 _LowerElevationColor;
            fixed4 _HigherElevationColor;
            float _ElevationFilterMaxOpacity;
            float _ElevationFilterRangeMeters;
            float _ElevationFilterNeutralBandMeters;
            float _ElevationFilterResponseExponent;
            fixed4 _PlayerHeightReferenceColor;
            float _PlayerHeightReferenceLineWidth;
            float _PlayerHeightReferenceOpacity;
            float _ElevationFilterHillshadeStrength;

            #include "GrasslandDynamics.cginc"
            #include "HeightContourLines.cginc"

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                output.screenPosition = ComputeScreenPos(output.vertex);
                return output;
            }

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
                float2 bakedUv)
            {
                if (any(bakedUv != 0.0))
                {
                    // Baked coordinates are unwrapped meters, with +U pointing downstream.
                    mapMeters = bakedUv;
                    localFlow = float2(length(localFlow), 0.0);
                }
                float depth = saturate(normalizedDepth);
                float tileSize = max(0.01, _WaterTileSizeMeters);
                float waveLength = max(0.1, _WaterWaveLengthMeters);
                float2 waveCoordinate = mapMeters / waveLength;
                float2 distortion = float2(
                    sin(waveCoordinate.y * 6.283185
                        + animationTime * _WaterWaveSpeed),
                    cos(waveCoordinate.x * 6.283185
                        - animationTime * _WaterWaveSpeed * 0.83))
                    * _WaterWaveDistortion;

                float2 primaryUv = frac(
                    (mapMeters
                        + _WaterLayerOneSpeed.xy * animationTime)
                    / tileSize
                    + distortion);
                float2 secondaryUv = frac(
                    RotateWaterUv(
                        mapMeters / tileSize
                        * max(0.1, _WaterLayerTwoScale))
                    + _WaterLayerTwoSpeed.xy * animationTime / tileSize
                    - distortion * 0.73);
                // Continuous local-flow displacement, added to the existing
                // fixed-direction animation without phase resets or blending.
                float2 flowUv = localFlow * max(0.0, _WaterFlowSpeed) * animationTime / tileSize;
                fixed4 primary = tex2D(_WaterPatternTex, frac(primaryUv - flowUv));
                // Match the existing second layer's fixed coordinate transform.
                // Its orientation never depends on local flow direction.
                fixed4 secondary = tex2D(_WaterPatternTex, frac(secondaryUv
                    - RotateWaterUv(flowUv * max(0.1, _WaterLayerTwoScale))));
                float secondaryStrength = 0.65;
                float combinedAlpha = 1.0
                    - (1.0 - primary.a)
                    * (1.0 - secondary.a * secondaryStrength);
                float3 combinedRgb = max(
                    primary.rgb,
                    secondary.rgb * secondaryStrength);
                float depthOpacity = lerp(0.2, 1.0, depth);
                return fixed4(
                    combinedRgb,
                    combinedAlpha * depthOpacity);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // The sprite only supplies geometry; its carrier texture must not control map opacity.
                fixed4 baseColor = fixed4(0, 0, 0, input.color.a);
                if (_PhotoContoursOnly > 0.5) baseColor = fixed4(0, 0, 0, 1);
                float revealMask = 1.0;
                if (_SurfaceRevealEnabled > 0.5)
                {
                    float2 screenUv = input.screenPosition.xy
                                      / max(input.screenPosition.w, 0.00001);
                    float2 pixelPosition = screenUv * _ScreenParams.xy;
                    float distanceFromUiCentre = distance(
                        pixelPosition,
                        _SurfaceRevealCenterPixels.xy);
                    float revealRadius = max(1.0, _SurfaceRevealRadiusPixels);
                    float revealEdge = min(
                        max(0.0, _SurfaceRevealEdgePixels),
                        revealRadius);
                    revealMask = revealEdge > 0.0001
                        ? 1.0 - smoothstep(
                            revealRadius - revealEdge,
                            revealRadius,
                            distanceFromUiCentre)
                        : step(distanceFromUiCentre, revealRadius);
                }

                // Terrain remains one Editor-baked texture and is revealed only
                // inside the player UI. Water is a persistent natural feature:
                // it stays visible everywhere, while this same reveal mask only
                // selects whether its pattern is static or animated.
                if (_SurfaceEnabled > 0.5 || _WaterEnabled > 0.5)
                {
                    float2 waterUv = (input.uv - _WaterMaskBounds.xy)
                                     / _WaterMaskBounds.zw;
                    float insideWaterBounds = step(0.0, waterUv.x)
                        * step(0.0, waterUv.y)
                        * step(waterUv.x, 1.0) * step(waterUv.y, 1.0);
                    fixed4 waterData = _WaterEnabled > 0.5
                        ? tex2D(_WaterMaskTex, waterUv) * insideWaterBounds
                        : fixed4(0.0, 0.0, 0.0, 0.0);
                    float waterRange = _WaterEnabled > 0.5
                        ? smoothstep(0.02, 0.98, waterData.r)
                        : 0.0;

                    fixed4 terrainSurface = _SurfaceEnabled > 0.5
                        ? tex2D(_SurfaceTex, input.uv)
                        : fixed4(0.0, 0.0, 0.0, 0.0);
                    if (_GrassEnabled > .5)
                        terrainSurface = CombineGrassland(terrainSurface,
                            SampleGrassland(input.uv, _GrassActive * step(.001, revealMask)));
                    // Authored water owns its complete range, so terrain never
                    // leaks through transparent gaps in the water pattern.
                    float terrainBlend = saturate(
                        terrainSurface.a
                        * revealMask
                        * (1.0 - waterRange));
                    baseColor.rgb = lerp(
                        baseColor.rgb,
                        terrainSurface.rgb,
                        terrainBlend);

                    if (_WaterEnabled > 0.5)
                    {
                        // R/G retain range/depth; B/A provide local angle/speed.
                        float2 mapMeters = input.uv * _MapSizeMeters.xy
                                           + _WaterOriginMeters.xy;
                        float2 localFlow = SampleWaterFlow(waterUv);
                        float2 bakedWaterUv = SampleBakedWaterUv(waterUv) * insideWaterBounds;
                        fixed4 staticWater = SampleWaterPattern(
                            mapMeters,
                            waterData.g,
                            localFlow,
                            0.0,
                            bakedWaterUv);
                        fixed4 animatedWater = SampleWaterPattern(
                            mapMeters,
                            waterData.g,
                            localFlow,
                            _Time.y,
                            bakedWaterUv);
                        fixed4 visibleWater = lerp(
                            staticWater,
                            animatedWater,
                            revealMask);

                        // Water visibility is intentionally independent from the
                        // reveal mask. Outside the player's view it uses the fixed
                        // time-zero sample; inside it crossfades to moving water.
                        float waterBlend = saturate(
                            visibleWater.a * waterRange);
                        baseColor.rgb = lerp(
                            baseColor.rgb,
                            visibleWater.rgb,
                            waterBlend);
                    }
                }

                // This texture is the same normalized physical surface sampled by
                // movement and traversal UI. Only screen-space antialiasing below may
                // alter presentation; contour positions never use a separate blur/LOD.
                float normalizedHeight = saturate(tex2D(_HeightTex, input.uv).r);
                float heightMeters = lerp(_MinimumHeight, _MaximumHeight, normalizedHeight);

                float elevationFilterMask = 0.0;
                float elevationFilterProgress = saturate(
                    _ElevationFilterProgress);
                if (_ElevationFilterEnabled > 0.5
                    && elevationFilterProgress > 0.0001)
                {
                    float2 screenUv = input.screenPosition.xy
                                      / max(input.screenPosition.w, 0.00001);
                    float2 pixelPosition = screenUv * _ScreenParams.xy;
                    float distanceFromUiCentre = distance(
                        pixelPosition,
                        _SurfaceRevealCenterPixels.xy);
                    float fullRadius = max(1.0, _SurfaceRevealRadiusPixels);
                    float revealRadius = max(
                        1.0,
                        fullRadius * elevationFilterProgress);
                    float revealEdge = min(
                        max(0.0, _SurfaceRevealEdgePixels),
                        revealRadius);
                    elevationFilterMask = revealEdge > 0.0001
                        ? 1.0 - smoothstep(
                            revealRadius - revealEdge,
                            revealRadius,
                            distanceFromUiCentre)
                        : step(distanceFromUiCentre, revealRadius);

                    float relativeHeight = heightMeters
                                           - _PlayerHeightMeters;
                    float filterRange = max(
                        0.1,
                        _ElevationFilterRangeMeters);
                    float neutralBand = min(
                        max(0.0, _ElevationFilterNeutralBandMeters),
                        filterRange - 0.01);
                    float relativeStrength = saturate(
                        (abs(relativeHeight) - neutralBand)
                        / max(0.01, filterRange - neutralBand));
                    relativeStrength = pow(
                        relativeStrength,
                        max(0.25, _ElevationFilterResponseExponent));
                    fixed4 elevationTint = relativeHeight < 0.0
                        ? _LowerElevationColor
                        : _HigherElevationColor;
                    float elevationBlend = saturate(
                        relativeStrength
                        * _ElevationFilterMaxOpacity
                        * elevationTint.a
                        * elevationFilterMask
                        * elevationFilterProgress);
                    baseColor.rgb = lerp(
                        baseColor.rgb,
                        elevationTint.rgb,
                        elevationBlend);

                    // A tiny four-sample normal reconstruction adds readable
                    // terrain shape without generating another runtime texture.
                    float2 texel = max(
                        _HeightTex_TexelSize.xy,
                        float2(0.000001, 0.000001));
                    float heightLeft = lerp(
                        _MinimumHeight,
                        _MaximumHeight,
                        saturate(tex2D(
                            _HeightTex,
                            saturate(input.uv - float2(texel.x, 0.0))).r));
                    float heightRight = lerp(
                        _MinimumHeight,
                        _MaximumHeight,
                        saturate(tex2D(
                            _HeightTex,
                            saturate(input.uv + float2(texel.x, 0.0))).r));
                    float heightDown = lerp(
                        _MinimumHeight,
                        _MaximumHeight,
                        saturate(tex2D(
                            _HeightTex,
                            saturate(input.uv - float2(0.0, texel.y))).r));
                    float heightUp = lerp(
                        _MinimumHeight,
                        _MaximumHeight,
                        saturate(tex2D(
                            _HeightTex,
                            saturate(input.uv + float2(0.0, texel.y))).r));
                    float2 metersPerTexel = max(
                        _MapSizeMeters.xy * texel,
                        float2(0.0001, 0.0001));
                    float2 slope = float2(
                        (heightRight - heightLeft)
                            / (2.0 * metersPerTexel.x),
                        (heightUp - heightDown)
                            / (2.0 * metersPerTexel.y));
                    float3 terrainNormal = normalize(
                        float3(-slope.x, -slope.y, 1.0));
                    float3 terrainLight = normalize(
                        float3(-0.55, 0.65, 0.8));
                    float flatLighting = terrainLight.z;
                    float hillshade = clamp(
                        (dot(terrainNormal, terrainLight) - flatLighting)
                            * 2.0,
                        -1.0,
                        1.0);
                    float hillshadeAmount = hillshade
                        * _ElevationFilterHillshadeStrength
                        * elevationFilterMask
                        * elevationFilterProgress;
                    baseColor.rgb *= 1.0 + hillshadeAmount;
                }

                float blendAmount = saturate(HeightContourBlend(heightMeters) * _ContourColor.a);
                baseColor.rgb = lerp(baseColor.rgb, _ContourColor.rgb, blendAmount);

                if (_ElevationFilterEnabled > 0.5
                    && elevationFilterProgress > 0.0001)
                {
                    float referenceDistance = abs(
                        heightMeters - _PlayerHeightMeters);
                    float heightDerivative = max(
                        fwidth(heightMeters),
                        0.00001);
                    float referenceHalfWidth = heightDerivative
                        * max(0.25, _PlayerHeightReferenceLineWidth)
                        * 0.5;
                    float referenceAntiAlias = max(
                        heightDerivative * 0.75,
                        0.00002);
                    float referenceLine = 1.0 - smoothstep(
                        referenceHalfWidth,
                        referenceHalfWidth + referenceAntiAlias,
                        referenceDistance);
                    // A mathematically flat plateau has no single contour path.
                    // Suppress the otherwise fully filled plateau while retaining
                    // the line on even very gentle, non-zero gradients.
                    float hasVisibleGradient = smoothstep(
                        0.00005,
                        0.002,
                        heightDerivative);
                    referenceLine *= hasVisibleGradient
                        * elevationFilterMask
                        * elevationFilterProgress;
                    float referenceBlend = saturate(
                        referenceLine
                        * _PlayerHeightReferenceOpacity
                        * _PlayerHeightReferenceColor.a);
                    baseColor.rgb = lerp(
                        baseColor.rgb,
                        _PlayerHeightReferenceColor.rgb,
                        referenceBlend);
                }
                // Dim the complete map, including water and contour lines,
                // outside the reveal circle without changing transparency.
                baseColor.rgb *= 1.0
                    - saturate(_OutsideRevealDarkening) * (1.0 - revealMask);
                return baseColor;
            }
            ENDCG
        }
    }
}

Shader "AnimalGame/World Map Contours"
{
    Properties
    {
        _HeightTex ("Height", 2D) = "black" {}
        _CheckpointFogTex ("Checkpoint Fog", 2D) = "black" {}
        [HideInInspector] _PreviousCheckpointFogTex ("Previous Checkpoint Fog", 2D) = "black" {}
        _MinimumLineWidth ("Minimum Line Width", Float) = 0.75
        _MaximumLineWidth ("Maximum Line Width", Float) = 3
        _MinimumOpacity ("Minimum Opacity", Float) = 0.15
        _MaximumOpacity ("Maximum Opacity", Float) = 1
        _MaximumCoverage ("Maximum Coverage", Float) = 0.45
        _EdgeSoftness ("Edge Softness", Float) = 0.4
        [HideInInspector] _MainTex ("Map Image", 2D) = "black" {}
        [HideInInspector] _FaultProgress ("Data Fault Progress", Range(0, 1)) = 1
        [HideInInspector] _FaultTint ("Data Fault Tint", Color) = (0.83137255, 0.72156863, 0.32941176, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        CGINCLUDE
        #include "UnityCG.cginc"

        float SurveyPattern(float2 mapPosition, float2 metresPerPixel, float spacing)
        {
            float2 dotOffset = abs(frac(mapPosition / spacing + .5) - .5) * spacing / metresPerPixel;
            float dotMask = 1 - smoothstep(.3, 1.2, length(dotOffset));

            float crossSpacing = spacing * 8;
            float2 crossOffset = abs(frac(mapPosition / crossSpacing + .5) - .5)
                * crossSpacing / metresPerPixel;
            float horizontalArm = max(crossOffset.x - 3.5, crossOffset.y - .55);
            float verticalArm = max(crossOffset.x - .55, crossOffset.y - 3.5);
            float crossMask = 1 - smoothstep(-.45, .45, min(horizontalArm, verticalArm));
            return .008 + max(.036 * dotMask, .075 * crossMask);
        }

        float UnknownRegionColor(float2 mapPosition, float2 metresPerPixel)
        {
            // The coarser grid shares every other position with the finer one as the map zooms out.
            float level = log2(max(metresPerPixel.x, metresPerPixel.y) * 22);
            float spacing = exp2(floor(level));
            return lerp(SurveyPattern(mapPosition, metresPerPixel, spacing),
                SurveyPattern(mapPosition, metresPerPixel, spacing * 2), smoothstep(0, 1, frac(level)));
        }
        ENDCG

        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            sampler2D _HeightTex;
            sampler2D _CheckpointFogTex;
            float2 _MapSizeMeters;
            float4 _TileRect, _ViewRect;
            float _MinimumHeight, _MaximumHeight;
            float _VisibleMinimumHeight, _VisibleMaximumHeight;
            float _MinimumLineWidth, _MaximumLineWidth;
            float _MinimumOpacity, _MaximumOpacity, _MaximumCoverage, _EdgeSoftness;
            static const float _ContourInterval = 14;
            #include "HeightContourLines.cginc"
            #include "MapWater.cginc"
            float _DashSubmergedContours, _MapWaterTime;
            float2 _MapMetresPerPixel, _MapViewZoom;

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

            float2 MapWaterFlow(float2 tileUv)
            {
                float2 uv = (tileUv - _WaterMaskBounds.xy) / _WaterMaskBounds.zw;
                float2 texel = _WaterMaskTex_TexelSize.xy;
                float2 pixel = uv * _WaterMaskTex_TexelSize.zw - .5;
                float2 origin = (floor(pixel) + .5) * texel;
                float2 weight = frac(pixel);
                // Blend decoded vectors so directions across the angle wrap remain continuous.
                float2 a = DecodeWaterFlow(tex2Dlod(_WaterMaskTex, float4(origin, 0, 0)));
                float2 b = DecodeWaterFlow(tex2Dlod(_WaterMaskTex, float4(origin + float2(texel.x, 0), 0, 0)));
                float2 c = DecodeWaterFlow(tex2Dlod(_WaterMaskTex, float4(origin + float2(0, texel.y), 0, 0)));
                float2 d = DecodeWaterFlow(tex2Dlod(_WaterMaskTex, float4(origin + texel, 0, 0)));
                return lerp(lerp(a, b, weight.x), lerp(c, d, weight.x), weight.y);
            }

            float DrawMapWater(float brightness, float submerged,
                float2 tileUv, float2 mapPosition)
            {
                float2 bankSample = 2 * _MapMetresPerPixel / _TileRect.zw;
                float2 boundsMin = _WaterMaskBounds.xy;
                float2 boundsMax = boundsMin + _WaterMaskBounds.zw;
                if (any(tileUv < boundsMin - bankSample) || any(tileUv > boundsMax + bankSample))
                    return brightness;

                float range = MapWaterRange(tileUv);
                float2 dx = float2(bankSample.x, 0);
                float2 dy = float2(0, bankSample.y);
                float2 lower = float2(MapWaterRange(tileUv - dx), MapWaterRange(tileUv - dy));
                float2 upper = float2(MapWaterRange(tileUv + dx), MapWaterRange(tileUv + dy));
                float2 gradient = upper - lower;
                float2 slope = .5 * max(abs(range - lower), abs(range - upper));
                float distanceToBank = abs(range - .5) / max(length(slope), 1e-5);
                float bank = 1 - smoothstep(.6, 2.2, distanceToBank);

                if (range > .42)
                {
                    float2 hatchPosition = mapPosition / _MapMetresPerPixel;
                    if (_MapWaterTime > 0)
                    {
                        float2 flow = MapWaterFlow(tileUv);
                        hatchPosition -= flow * (12 * _MapWaterTime);
                        float stillness = 1 - smoothstep(.000001, .001, dot(flow, flow));
                        if (stillness > 0)
                        {
                            // A small elliptical motion gives still water sway in every direction.
                            float phase = dot(mapPosition, float2(.063, -.041)) + .85 * _MapWaterTime;
                            float sine, cosine;
                            sincos(phase, sine, cosine);
                            float fade = smoothstep(0, 1, _MapWaterTime);
                            hatchPosition -= float2(sine * 1.5, cosine * 1.2) * (stillness * fade);
                        }
                    }
                    float acrossHatch = (hatchPosition.x - hatchPosition.y) * .70710678;
                    float alongHatch = (hatchPosition.x + hatchPosition.y) * .70710678;
                    float zoom = sqrt(_MapViewZoom.x * _MapViewZoom.y);
                    float hatch = WaterStroke(acrossHatch, 8 * zoom, 1.3 * zoom);
                    float hatchIndex = floor(acrossHatch / (8 * zoom) + .5);
                    float seed = frac(hatchIndex * .1031);
                    seed *= seed + 33.33;
                    float stagger = 32 * (frac(seed * seed * 2) - .5);
                    float hatchGap = WaterStroke(alongHatch + stagger * zoom, 32 * zoom, 6 * zoom);
                    float fill = lerp(submerged, 1, .12 * hatch * (1 - hatchGap));
                    brightness = lerp(brightness, fill, smoothstep(.42, .58, range));
                }

                // Overlap the two dash phases near diagonal banks so a turn cannot cut a dash.
                float2 position = mapPosition / _MapMetresPerPixel;
                float2 tangent = abs(gradient.yx);
                float2 weight = smoothstep(.72, .98, tangent / max(max(tangent.x, tangent.y), 1e-5));
                float dashX = WaterStroke(position.x, 40 * _MapViewZoom.x, 5.5 * _MapViewZoom.x);
                float dashY = WaterStroke(position.y, 40 * _MapViewZoom.y, 5.5 * _MapViewZoom.y);
                float dashedBank = bank * max(weight.x * dashX, weight.y * dashY);
                return lerp(brightness, 1, .50 * dashedBank);
            }

            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_img input)
            {
                v2f output;
                output.uv = input.vertex.xy;
                float2 mapPosition = _TileRect.xy + output.uv * _TileRect.zw;
                float2 clip = (mapPosition - _ViewRect.xy) / _ViewRect.zw * 2 - 1;
                #if UNITY_UV_STARTS_AT_TOP
                clip.y = -clip.y;
                #endif
                output.vertex = float4(clip, 0, 1);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float heightMeters = lerp(_MinimumHeight, _MaximumHeight, saturate(tex2D(_HeightTex, input.uv).r));
                float contourBlend = HeightContourBlend(heightMeters);
                float2 mapPosition = _TileRect.xy + input.uv * _TileRect.zw;
                float2 metresPerPixel = fwidth(mapPosition);
                float brightness = contourBlend;
                if (_WaterEnabled > .5)
                {
                    float submerged = 0;
                    if (_DashSubmergedContours > .5)
                        submerged = .25 * contourBlend * SubmergedContourDash(mapPosition, heightMeters);
                    brightness = DrawMapWater(brightness, submerged, input.uv, mapPosition);
                }
                float unknown = tex2D(_CheckpointFogTex, mapPosition / _MapSizeMeters).r;
                if (unknown > 0)
                    brightness = lerp(brightness, UnknownRegionColor(mapPosition, metresPerPixel), unknown);
                return fixed4(brightness.xxx, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "DATA_FAULT"
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragFault
            #pragma target 3.0
            sampler2D _MainTex;
            float4 _FaultTint;
            float _FaultProgress;

            float FaultHash(float value)
            {
                return frac(sin(value * 12.9898) * 43758.5453);
            }

            fixed4 fragFault(v2f_img input) : SV_Target
            {
                float severity = 1 - smoothstep(.12, 1, _FaultProgress);
                float phase = _FaultProgress * 13;
                float band = input.uv.y * 17;
                float row = frac(band);
                float key = floor(band) + floor(phase) * 29;
                float error = FaultHash(key + 1);
                float dropped = step(.69, error);
                float activeRows = step(.07, row) * step(row, .78);
                float shift = (FaultHash(key + 51) - .5) * .2 * severity;
                float2 sampleUv = input.uv + float2(shift * activeRows * step(.43, error), 0);
                float visible = step(0, sampleUv.x) * step(sampleUv.x, 1);
                float3 color = tex2D(_MainTex, saturate(sampleUv)).rgb * visible;
                color *= 1 - dropped * activeRows * severity * .94;

                // Broken data strokes pulse at the edge of lost bands.
                float cell = input.uv.x * lerp(9, 21, FaultHash(key + 17));
                float segment = step(.38, FaultHash(key + floor(cell) * 13)) * step(frac(cell), .76);
                float stroke = 1 - smoothstep(fwidth(band), 2.5 * fwidth(band), abs(row - .78));
                float pulse = 1 - .65 * smoothstep(.12, .95, frac(phase));
                color += _FaultTint.rgb * (.38 * dropped * segment * stroke * severity * pulse);
                return fixed4(color, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "REGION_REVEAL"
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragReveal
            #pragma target 3.0

            sampler2D _MainTex, _CheckpointFogTex, _PreviousCheckpointFogTex;
            float4 _MainTex_TexelSize, _ViewRect, _RevealWave, _FaultTint;
            float2 _MapSizeMeters;

            fixed4 fragReveal(v2f_img input) : SV_Target
            {
                fixed4 mapColor = tex2D(_MainTex, input.uv);
                if (mapColor.a == 0) return mapColor;

                float2 mapPosition = _ViewRect.xy + input.uv * _ViewRect.zw;
                float2 fogUv = mapPosition / _MapSizeMeters;
                float currentFog = tex2D(_CheckpointFogTex, fogUv).r;
                float change = tex2D(_PreviousCheckpointFogTex, fogUv).r - currentFog;
                if (change <= 0) return mapColor;

                float2 metresPerPixel = abs(_MainTex_TexelSize.xy) * _ViewRect.zw;
                float2 offset = mapPosition - _RevealWave.xy;
                float distance = length(offset);
                float radialPixel = max(1e-5, length(metresPerPixel * offset / max(1e-5, distance)));
                float edgePixels = (distance - _RevealWave.z) / radialPixel;
                float front = smoothstep(-.65, .65, edgePixels);
                float progress = _RevealWave.w;
                if (progress <= 0) front = 1;

                // The cached image already contains current fog. Replace only the fraction
                // of its remaining terrain needed to reconstruct the old fog before the wave.
                float remainingTerrain = max(1e-5, 1 - currentFog);
                float fogRestore = saturate(change / remainingTerrain) * front;
                float3 color = lerp(mapColor.rgb, UnknownRegionColor(mapPosition, metresPerPixel), fogRestore);
                float stroke = 1 - smoothstep(.35, 1, abs(edgePixels));
                float envelope = smoothstep(0, .06, progress) * (1 - smoothstep(.84, 1, progress));
                color = lerp(color, _FaultTint.rgb, .8 * change * stroke * envelope);
                return fixed4(color, mapColor.a);
            }
            ENDCG
        }
    }
}

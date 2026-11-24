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
        [HideInInspector] _MapBankOpacity ("Map Bank Opacity", Float) = .5
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
            #include "MapWaterSymbols.cginc"
            float _DashSubmergedContours, _MapWaterTime, _MapBankOpacity;
            float2 _MapMetresPerPixel, _MapViewZoom;

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
                    brightness = DrawMapWater(brightness, submerged, input.uv, mapPosition,
                        _TileRect.zw, _MapMetresPerPixel, _MapViewZoom, _MapWaterTime, 8, .12, _MapBankOpacity);
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

Shader "Hidden/AnimalGame/World Map Shoreline"
{
    SubShader
    {
        Pass
        {
            Name "WATER_SHORELINE"
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, Zero One

            CGPROGRAM
            #pragma vertex ShoreVertex
            #pragma fragment ShoreFragment
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _CheckpointFogTex;
            float2 _MapSizeMeters, _MapMetresPerPixel;
            float4x4 _ShorelineClipFromMap;
            float _ShorelinePatternMetresPerPixel, _ShorelineHalfWidth, _ShorelineOpacity;

            struct ShoreInput
            {
                float4 vertex : POSITION;
                float2 join : TEXCOORD0;
                float3 stroke : TEXCOORD1; // normalized arc length, perimeter, side
            };

            struct ShoreOutput
            {
                float4 vertex : SV_POSITION;
                float2 mapPosition : TEXCOORD0;
                float3 stroke : TEXCOORD1; // dash phase, cycles per pixel, side
            };

            float ShoreMetresPerPixel()
            {
                return sqrt(_MapMetresPerPixel.x * _MapMetresPerPixel.y);
            }

            float ShoreDashCoverage(float phase, float cyclesPerPixel)
            {
                // Integrate one pixel of the periodic dash, even when it spans more than one repeat.
                const float dashFill = .55;
                float start = frac(phase) + .5 * (dashFill - cyclesPerPixel);
                float end = start + cyclesPerPixel;
                float wholeCycles = floor(end) - floor(start);
                float covered = wholeCycles * dashFill + min(frac(end), dashFill) - min(frac(start), dashFill);
                return saturate(covered / cyclesPerPixel);
            }

            ShoreOutput ShoreVertex(ShoreInput input)
            {
                ShoreOutput output;
                float metresPerPixel = ShoreMetresPerPixel();
                output.mapPosition = input.vertex.xy + input.join * (_ShorelineHalfWidth + .5) * _MapMetresPerPixel;
                output.vertex = mul(_ShorelineClipFromMap, float4(output.mapPosition, 0, 1));
                // Fit the closed loop at its reference scale, independent of camera zoom.
                float dashCount = max(1, round(input.stroke.y / (40 * _ShorelinePatternMetresPerPixel)));
                output.stroke = float3(input.stroke.x * dashCount, metresPerPixel * dashCount / input.stroke.y, input.stroke.z);
                return output;
            }

            fixed4 ShoreFragment(ShoreOutput input) : SV_Target
            {
                float dash = ShoreDashCoverage(input.stroke.x, input.stroke.y);
                float halfWidth = _ShorelineHalfWidth;
                float edge = abs(input.stroke.z) * (halfWidth + .5);
                // Integrate subpixel widths so the minimum-width stroke keeps its brightness as it moves between pixels.
                float boxCoverage = saturate(halfWidth + .5 - edge) - saturate(.5 - halfWidth - edge);
                float softCoverage = smoothstep(-.5, .5, halfWidth - edge) - smoothstep(-.5, .5, -halfWidth - edge);
                float coverage = lerp(boxCoverage, softCoverage, saturate(2 * halfWidth - 1));
                float known = 1 - tex2D(_CheckpointFogTex, input.mapPosition / _MapSizeMeters).r;
                return fixed4(1, 1, 1, _ShorelineOpacity * known * coverage * dash);
            }
            ENDCG
        }
    }
}

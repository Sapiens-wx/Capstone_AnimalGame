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
            float2 _MapSizeMeters, _MapMetresPerPixel, _MapViewZoom;
            float4x4 _ShorelineClipFromMap;
            float _ShorelineOpacity;

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
                float3 stroke : TEXCOORD1;
            };

            float ShoreZoom()
            {
                return sqrt(_MapViewZoom.x * _MapViewZoom.y);
            }

            ShoreOutput ShoreVertex(ShoreInput input)
            {
                ShoreOutput output;
                float halfWidth = 1.4 * max(1, ShoreZoom());
                output.mapPosition = input.vertex.xy + input.join * (halfWidth + .5) * _MapMetresPerPixel;
                output.vertex = mul(_ShorelineClipFromMap, float4(output.mapPosition, 0, 1));
                output.stroke = input.stroke;
                return output;
            }

            fixed4 ShoreFragment(ShoreOutput input) : SV_Target
            {
                float zoom = ShoreZoom();
                float perimeterPixels = input.stroke.y / sqrt(_MapMetresPerPixel.x * _MapMetresPerPixel.y);
                // Fit a whole number of dashes so the closed loop has a continuous phase.
                float dashCount = max(1, round(perimeterPixels / max(16, 40 * zoom)));
                float period = perimeterPixels / dashCount;
                float distanceToDash = abs(frac(input.stroke.x * dashCount + .5) - .5) * period;
                float halfLength = period * .275;
                float dash = 1 - smoothstep(halfLength - .45, halfLength + .45, distanceToDash);
                float halfWidth = 1.4 * max(1, zoom);
                float edge = abs(input.stroke.z) * (halfWidth + .5);
                float coverage = 1 - smoothstep(halfWidth - .5, halfWidth + .5, edge);
                float known = 1 - tex2D(_CheckpointFogTex, input.mapPosition / _MapSizeMeters).r;
                return fixed4(1, 1, 1, _ShorelineOpacity * known * coverage * dash);
            }
            ENDCG
        }
    }
}

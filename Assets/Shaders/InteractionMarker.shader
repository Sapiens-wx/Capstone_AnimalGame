Shader "AnimalGame/Interaction Marker"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float4 screenPosition : TEXCOORD0;
            };

            fixed4 _Color;
            // Shared view geometry is supplied by the gameplay HUD, never by this material.
            float _PlayerUiClipEnabled;
            float4 _PlayerUiClipCenterPixels;
            float _PlayerUiClipRadiusPixels;
            float _PlayerUiClipSoftnessPixels;

            Varyings Vert(AppData input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _Color;
                output.screenPosition = ComputeScreenPos(output.vertex);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                // No valid gameplay view means no marker, including HUD/menu transitions.
                if (_PlayerUiClipEnabled <= 0.5 || _PlayerUiClipRadiusPixels <= 0.0)
                    discard;

                float2 screenUv = input.screenPosition.xy / max(0.0001, input.screenPosition.w);
                float2 screenPixels = screenUv * _ScreenParams.xy;
                float distanceFromCenter = length(screenPixels - _PlayerUiClipCenterPixels.xy);
                float radius = max(0.0, _PlayerUiClipRadiusPixels);
                float softness = max(0.001, _PlayerUiClipSoftnessPixels);
                float visibility = 1.0 - smoothstep(max(0.0, radius - softness),
                    radius + softness, distanceFromCenter);
                fixed4 color = input.color;
                color.a *= visibility;
                clip(color.a - 0.001);
                return color;
            }
            ENDCG
        }
    }
}

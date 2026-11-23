Shader "AnimalGame/Communications Signal Wave"
{
    Properties
    {
        _Color ("Signal Color", Color) = (0.83137255, 0.72156863, 0.32941176, 1)
        _Front ("Wave Front", Range(0, 1)) = 0.06666667
        _Opacity ("Opacity", Range(0, 1)) = 0.64
        [NoScaleOffset] _CurveProfile ("Generated Curve Profile", 2D) = "white" {}
        _LineWidthPixels ("Line Width In Pixels", Float) = 1.45
        _EchoSpacingPixels ("Echo Spacing In Pixels", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent+80" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct AppData { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            fixed4 _Color;
            sampler2D _CurveProfile;
            float4 _CurveProfile_TexelSize;
            float _Front, _Opacity, _LineWidthPixels, _EchoSpacingPixels;

            Varyings Vert(AppData input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            float CurveLine(float signedDistance, float widthPixels)
            {
                float pixelSize = max(length(float2(ddx(signedDistance), ddy(signedDistance))), .000001);
                float halfWidth = widthPixels * pixelSize * .5;
                return 1.0 - smoothstep(max(0.0, halfWidth - pixelSize * .5),
                    halfWidth + pixelSize * .5, abs(signedDistance));
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 samplePoint = (input.uv - .5) * 2.0;
                float radius = length(samplePoint);
                float pixelSize = max(length(float2(ddx(radius), ddy(radius))), .000001);
                float angle = atan2(samplePoint.y, samplePoint.x) / 6.28318530718;
                // Texel centres at i / resolution preserve the periodic join at +/- pi.
                float profile = tex2D(_CurveProfile,
                    float2(angle + _CurveProfile_TexelSize.x * .5, .5)).r;
                float depth = min(_EchoSpacingPixels * pixelSize, _Front * .2);
                float shape = CurveLine(radius - _Front * profile, _LineWidthPixels);
                shape = max(shape, .46875 * CurveLine(radius - (_Front - depth) * profile,
                    _LineWidthPixels * (1.1 / 1.45)));
                fixed4 color = _Color;
                color.a *= _Opacity * shape;
                clip(color.a - .002);
                return color;
            }
            ENDCG
        }
    }
}

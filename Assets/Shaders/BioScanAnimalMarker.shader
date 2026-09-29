Shader "UI/Biological Scan Animal Marker"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [Toggle] _Filled ("Solid Star", Float) = 0
        _OutlinePixels ("Minimum Outline Width In Screen Pixels", Float) = 0.85
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                float4 localPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;
            float _Filled;
            float _OutlinePixels;

            Varyings Vert(AppData input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.localPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                // Four concave quarter-ellipse arcs reproduce the reference
                // sparkle. Keep the former marker's 45% frame height, with a
                // slightly narrower width and no texture minification.
                float2 starPosition = (input.texcoord - 0.5) / float2(0.205, 0.225);
                float2 quadrant = abs(starPosition);
                float arcDistance = 1.0 - length(quadrant - 1.0);
                float boxDistance = max(quadrant.x, quadrant.y) - 1.0;
                float signedDistance = max(arcDistance, boxDistance);
                float pixelWidth = max(fwidth(signedDistance), 0.0001);
                // Preserve an open centre at small sizes while keeping the
                // outline readable; the solid state uses the same silhouette.
                float strokeWidth = min(0.25,
                    max(0.09, _OutlinePixels * pixelWidth));
                float outlineDistance = max(signedDistance,
                    -signedDistance - strokeWidth);
                float distance = lerp(outlineDistance, signedDistance,
                    saturate(_Filled));
                float coverage = 1.0 - smoothstep(-pixelWidth * 0.5,
                    pixelWidth * 0.5, distance);
                fixed4 color = input.color;
                color.a *= coverage;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.localPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}

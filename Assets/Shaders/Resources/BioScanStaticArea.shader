Shader "UI/Biological Scan Static Area"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _AbsoluteScreenPlacement ("Absolute Screen Placement", Float) = 0
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
            "CanUseSpriteAtlas" = "False"
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
                float2 barPoint : TEXCOORD0;
                float4 barSizeAndScreenPoint : TEXCOORD1;
                float4 viewport : TEXCOORD2;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 barPoint : TEXCOORD0;
                float4 barSizeAndScreenPoint : TEXCOORD1;
                float4 viewport : TEXCOORD2;
                float4 localPosition : TEXCOORD3;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;
            float _AbsoluteScreenPlacement;

            Varyings Vert(AppData input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                if (_AbsoluteScreenPlacement > 0.5)
                {
                    // These are final screen pixels, not coordinates in a UI
                    // Transform. The world direction was already projected by
                    // the presenter; a UI transform must not rotate it again.
                    // Read the dimensions of the GPU's current output target,
                    // rather than a Canvas rect captured before camera rendering.
                    float2 normalized = input.barSizeAndScreenPoint.zw
                        / max(_ScreenParams.xy, float2(1.0, 1.0));
                    float2 clipPosition = normalized * 2.0 - 1.0;
                    clipPosition.y *= _ProjectionParams.x;
                    output.vertex = float4(clipPosition, UNITY_NEAR_CLIP_VALUE, 1.0);
                }
                output.localPosition = input.vertex;
                output.barPoint = input.barPoint;
                output.barSizeAndScreenPoint = input.barSizeAndScreenPoint;
                output.viewport = input.viewport;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                // A finite rectangle has a full, straight square cap at each
                // end. Its one-pixel antialias fringe never uses a target mask.
                float2 boxDistance = abs(input.barPoint) - input.barSizeAndScreenPoint.xy;
                float signedDistance = length(max(boxDistance, 0.0))
                    + min(max(boxDistance.x, boxDistance.y), 0.0);
                float coverage = 1.0 - smoothstep(-0.5, 0.5, signedDistance);
                fixed4 color = input.color;
                color.a *= coverage;
                // Only the rectangular game viewport clips the finite bars.
                // Absolute screen coordinates support non-fullscreen cameras.
                float2 screenPoint = input.barSizeAndScreenPoint.zw;
                float2 insideMinimum = step(input.viewport.xy, screenPoint);
                float2 insideMaximum = step(screenPoint, input.viewport.zw);
                color.a *= insideMinimum.x * insideMinimum.y * insideMaximum.x * insideMaximum.y;
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

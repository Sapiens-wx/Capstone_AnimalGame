Shader "AnimalGame/Animal Turn Deformation"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
        [HideInInspector] _TurnHeadTail ("Head and Tail in Visual Space", Vector) = (0, 0.5, 0, -0.5)
        [HideInInspector] _TurnBendRadians ("Turn Bend Radians", Float) = 0
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
            // Deformation matrices require each renderer's original local space.
            "DisableBatching" = "True"
        }

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
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "UnitySprites.cginc"

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                float4 screenPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4x4 _TurnRendererToVisual;
            float4x4 _TurnVisualToRenderer;
            float4 _TurnHeadTail;
            float _TurnBendRadians;
            float _PlayerUiClipEnabled;
            float4 _PlayerUiClipCenterPixels;
            float _PlayerUiClipRadiusPixels;
            float _PlayerUiClipSoftnessPixels;

            float4 DeformVertex(float4 rendererPoint)
            {
                // A material without controller matrices is still a normal sprite.
                if (abs(_TurnBendRadians) <= 0.000001)
                    return rendererPoint;

                float4 visualPoint = mul(_TurnRendererToVisual, rendererPoint);
                float2 head = _TurnHeadTail.xy;
                float2 headToTail = head - _TurnHeadTail.zw;
                float axisLength = max(length(headToTail), 0.0001);
                float2 axis = headToTail / axisLength;
                float distanceFromHead = dot(head - visualPoint.xy, axis);
                float t = saturate(distanceFromHead / axisLength);
                float weight = t * t * (3.0 - 2.0 * t);
                float angle = _TurnBendRadians * weight;
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                float2 offset = visualPoint.xy - head;
                visualPoint.xy = head + float2(
                    cosine * offset.x - sine * offset.y,
                    sine * offset.x + cosine * offset.y);

                // A two-dimensional bend must not move the sprite's depth plane.
                rendererPoint.xy = mul(_TurnVisualToRenderer, visualPoint).xy;
                return rendererPoint;
            }

            Varyings Vert(appdata_t input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 rendererPoint = input.vertex;
                // Match UnityFlipSprite before evaluating the shared visual axis.
                // CPU bounds use the same mirrored local vertex coordinates.
                rendererPoint.xy *= _Flip;
                output.vertex = UnityObjectToClipPos(DeformVertex(rendererPoint));
                #ifdef PIXELSNAP_ON
                output.vertex = UnityPixelSnap(output.vertex);
                #endif
                output.texcoord = input.texcoord;
                output.color = input.color * _Color * _RendererColor;
                output.screenPosition = ComputeScreenPos(output.vertex);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 color = SampleSpriteTexture(input.texcoord)
                               * input.color;
                float2 screenUv = input.screenPosition.xy
                                  / max(0.0001, input.screenPosition.w);
                float2 screenPixels = screenUv * _ScreenParams.xy;
                float distanceFromCenter = length(
                    screenPixels - _PlayerUiClipCenterPixels.xy);
                float radius = max(0.0, _PlayerUiClipRadiusPixels);
                float softness = max(
                    0.001,
                    _PlayerUiClipSoftnessPixels);
                float insideVisibility = 1.0 - smoothstep(
                    max(0.0, radius - softness),
                    radius + softness,
                    distanceFromCenter);
                color.a *= lerp(
                    1.0,
                    insideVisibility,
                    saturate(_PlayerUiClipEnabled));
                clip(color.a - 0.001);
                return color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}

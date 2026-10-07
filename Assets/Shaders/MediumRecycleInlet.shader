Shader "AnimalGame/Medium Recycle Inlet"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1, 1, 1, 1)
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
        [HideInInspector] _RecycleInletPlane ("Inlet world plane", Vector) = (0, 1, 0, 0)
        [HideInInspector] _RecycleInletClipEnabled ("Inlet clip enabled", Float) = 0
        [HideInInspector] _RecycleVisualOffset ("Visual world offset", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent" "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline" "CanUseSpriteAtlas" = "True"
            "IgnoreProjector" = "True" "DisableBatching" = "True"
        }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        struct Attributes
        {
            float3 positionOS : POSITION;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            UNITY_SKINNED_VERTEX_INPUTS
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_AlphaTex);
        SAMPLER(sampler_AlphaTex);
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float4 _RecycleInletPlane;
            float4 _RecycleVisualOffset;
            float _RecycleInletClipEnabled;
            float _EnableExternalAlpha;
        CBUFFER_END

        Varyings InletVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            UNITY_SKINNED_VERTEX_COMPUTE(input);
            SetUpSpriteInstanceProperties();
            input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
            output.positionWS = TransformObjectToWorld(input.positionOS) + _RecycleVisualOffset.xyz;
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.uv = input.uv;
            output.color = input.color * _Color * unity_SpriteColor;
            return output;
        }

        half4 InletFragment(Varyings input) : SV_Target
        {
            if (_RecycleInletClipEnabled > .5)
                clip(dot(float4(input.positionWS, 1), _RecycleInletPlane));
            half4 sample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            #if defined(ETC1_EXTERNAL_ALPHA)
                half alpha = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, input.uv).r;
                sample.a = lerp(sample.a, alpha, _EnableExternalAlpha);
            #endif
            return sample * input.color;
        }
        ENDHLSL

        Pass
        {
            Name "RecycleInlet2D"
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex InletVertex
            #pragma fragment InletFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #pragma multi_compile _ SKINNED_SPRITE
            ENDHLSL
        }

        Pass
        {
            Name "RecycleInletForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex InletVertex
            #pragma fragment InletFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #pragma multi_compile _ SKINNED_SPRITE
            ENDHLSL
        }
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent" "RenderType" = "Transparent"
            "CanUseSpriteAtlas" = "True" "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "RecycleInletBuiltin"
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex InletVertex
            #pragma fragment InletFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float4 _RecycleInletPlane;
            float4 _RecycleVisualOffset;
            float _RecycleInletClipEnabled;

            struct InletVaryings
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            InletVaryings InletVertex(appdata_t input)
            {
                InletVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                input.vertex = UnityFlipSprite(input.vertex, _Flip);
                output.positionWS = mul(unity_ObjectToWorld, input.vertex).xyz + _RecycleVisualOffset.xyz;
                output.vertex = mul(UNITY_MATRIX_VP, float4(output.positionWS, 1));
                #ifdef PIXELSNAP_ON
                    output.vertex = UnityPixelSnap(output.vertex);
                #endif
                output.texcoord = input.texcoord;
                output.color = input.color * _Color * _RendererColor;
                return output;
            }

            fixed4 InletFragment(InletVaryings input) : SV_Target
            {
                if (_RecycleInletClipEnabled > .5)
                    clip(dot(float4(input.positionWS, 1), _RecycleInletPlane));
                fixed4 color = SampleSpriteTexture(input.texcoord) * input.color;
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}

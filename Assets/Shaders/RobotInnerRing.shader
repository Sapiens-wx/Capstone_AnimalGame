Shader "AnimalGame/Robot Inner Ring"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _ArcDegrees ("Arc length in degrees", Range(10, 120)) = 65
        _ArmBlend ("Arm bracket blend", Range(0, 1)) = 0
        _BracketOffset ("Bracket control offset", Vector) = (0,0,0,0)
        _BracketGrip ("Bracket grip", Range(0, 1)) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
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
            UNITY_VERTEX_OUTPUT_STEREO
        };
        CBUFFER_START(UnityPerMaterial)
            float _ArcDegrees;
            float _ArmBlend;
            float4 _BracketOffset;
            float _BracketGrip;
        CBUFFER_END
        Varyings RingVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            UNITY_SKINNED_VERTEX_COMPUTE(input);
            SetUpSpriteInstanceProperties();
            input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.uv = input.uv;
            output.color = input.color * unity_SpriteColor;
            return output;
        }
        float BracketCoverage(float2 p, float side, float feather)
        {
            float radius = length(p);
            float radial = 1 - smoothstep(17 - feather, 17 + feather, abs(radius - 156));
            float angle = atan2(p.y, p.x * side) * 57.2957795;
            float angularAA = max(feather * 57.2957795 / max(radius, 1), .01);
            return radial * (1 - smoothstep(60 - angularAA, 60 + angularAA, abs(angle)));
        }
        half4 RingFragment(Varyings input) : SV_Target
        {
            // Original 1024px canvas: radius 156px, stroke width 34px.
            float2 p = (input.uv - .5) * 1024;
            float radialDistance = abs(length(p) - 156);
            // Keep the entire feather below one screen pixel, even on the small idle ring.
            float pixelFootprint = max(length(ddx(p)), length(ddy(p)));
            float edgeFeather = max(pixelFootprint * .35, .01);
            float radialAA = edgeFeather;
            float radial = 1 - smoothstep(17 - radialAA, 17 + radialAA, radialDistance);
            float angle = atan2(p.y, p.x) * 57.2957795;
            // Three equal arcs, centred 120 degrees apart, grow at both ends.
            float sector = abs(frac((angle - 40) / 120 + .5) * 120 - 60);
            // Derive angular coverage from UV derivatives to avoid the atan2 seam.
            float angularAA = max(edgeFeather * 57.2957795 / max(length(p), 1), .01);
            float arc = _ArcDegrees >= 120 ? 1 : 1 - smoothstep(_ArcDegrees * .5 - angularAA,
                _ArcDegrees * .5 + angularAA, sector);
            // Grip squashes both arcs vertically around their shared centre; X stays unchanged.
            float2 bracketPoint = p - _BracketOffset.xy;
            bracketPoint.y /= lerp(1, .55, _BracketGrip);
            float bracketFeather = max(max(length(ddx(bracketPoint)), length(ddy(bracketPoint))) * .35, .01);
            float brackets = max(BracketCoverage(bracketPoint, 1, bracketFeather),
                BracketCoverage(bracketPoint, -1, bracketFeather));
            float coverage = lerp(radial * arc, brackets, _ArmBlend);
            return half4(input.color.rgb, input.color.a * coverage);
        }
        ENDHLSL
        Pass
        {
            Name "InnerRing2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex RingVertex
            #pragma fragment RingFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE
            ENDHLSL
        }
        Pass
        {
            Name "InnerRingForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex RingVertex
            #pragma fragment RingFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE
            ENDHLSL
        }
    }
}

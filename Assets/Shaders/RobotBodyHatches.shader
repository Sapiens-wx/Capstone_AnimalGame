Shader "AnimalGame/Robot Body Hatches"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _BodyCenter ("Body centre UV", Vector) = (.5,.5,1024,1024)
        _HingeAngle ("Hinge angle", Float) = 30
        _ArcDegrees ("Door arc", Float) = 50
        _Part ("Body -1, left 0, right 1", Float) = -1
        _Open01 ("Open progress", Range(0,1)) = 0
        _DoorShape ("Radius, stroke, half layer spacing", Vector) = (216.2,12,22,0)
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
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _BodyCenter;
            float _HingeAngle;
            float _ArcDegrees;
            float _Part;
            float _Open01;
            float4 _DoorShape;
        CBUFFER_END
        Varyings HatchVertex(Attributes input)
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
        float DoorMask(float angle)
        {
            return step(_HingeAngle, angle) * step(angle, _HingeAngle + _ArcDegrees);
        }
        float SegmentDistance(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            return length(p - a - ab * saturate(dot(p - a, ab) / dot(ab, ab)));
        }
        float DoorOutline(float2 p)
        {
            float start = radians(_HingeAngle), end = radians(_HingeAngle + _ArcDegrees);
            float angle = clamp(atan2(p.y, p.x), start, end);
            float2 direction = float2(cos(angle), sin(angle));
            float inner = _DoorShape.x - _DoorShape.z, outer = _DoorShape.x + _DoorShape.z;
            float arcs = min(length(p - direction * inner), length(p - direction * outer));
            float2 a = float2(cos(start), sin(start)), b = float2(cos(end), sin(end));
            float caps = min(SegmentDistance(p, a * inner, a * outer), SegmentDistance(p, b * inner, b * outer));
            float distance = min(arcs, caps) - _DoorShape.y * .5;
            float feather = max(max(length(ddx(p)), length(ddy(p))) * .35, .01);
            return 1 - smoothstep(-feather, feather, distance);
        }
        half4 HatchFragment(Varyings input) : SV_Target
        {
            float2 p = (input.uv - _BodyCenter.xy) * _BodyCenter.zw;
            float angle = atan2(p.y, p.x) * 57.2957795;
            float right = DoorMask(angle), left = DoorMask(180 - angle);
            half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            if (_Part < -.5)
            {
                clip(.5 - max(left, right));
                // Remove the isolated front bridge as the two doors open; restore it when closed.
                float front = step(_HingeAngle + _ArcDegrees, angle)
                    * step(angle, 180 - _HingeAngle - _ArcDegrees);
                source.a *= 1 - front * _Open01;
                return source * input.color;
            }
            float oldAlpha = source.a * (_Part < .5 ? left : right) * (1 - _Open01);
            float outline = DoorOutline(float2(_Part < .5 ? -p.x : p.x, p.y));
            float newAlpha = outline * _Open01;
            float alpha = oldAlpha + newAlpha;
            half3 colour = (source.rgb * oldAlpha + newAlpha) / max(alpha, .00001);
            return half4(colour * input.color.rgb, alpha * input.color.a);
        }
        ENDHLSL
        Pass
        {
            Name "BodyHatches2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HatchVertex
            #pragma fragment HatchFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE
            ENDHLSL
        }
        Pass
        {
            Name "BodyHatchesForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HatchVertex
            #pragma fragment HatchFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE
            ENDHLSL
        }
    }
}

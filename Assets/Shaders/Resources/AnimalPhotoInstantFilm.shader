Shader "AnimalGame/Photos/InstantFilm"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _OriginalTex ("Original Source", 2D) = "white" {}
        _Crop ("Crop", Vector) = (0,0,1,1)
        [HideInInspector] _Seed ("Grain Seed", Integer) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _OriginalTex;
        float4 _Crop;
        float4 _PhotoSize;
        float4 _Modules;
        float4 _Tone;
        float4 _ToneLimits;
        float4 _Color;
        float4 _Grain;
        float _VignetteEV;
        float _Strength;
        float _Softness;
        float _Softened;
        int _Seed;

        float3 ToLinear(float3 sampled)
        {
            #if defined(UNITY_COLORSPACE_GAMMA)
                return GammaToLinearSpace(sampled);
            #else
                return sampled;
            #endif
        }

        float3 FromLinear(float3 value)
        {
            #if defined(UNITY_COLORSPACE_GAMMA)
                return LinearToGammaSpace(max(value, 0));
            #else
                return value;
            #endif
        }

        float3 SampleLinear(float2 photoUV)
        {
            return ToLinear(tex2D(_MainTex, _Crop.xy + saturate(photoUV) * _Crop.zw).rgb);
        }

        float4 Soften(v2f_img input) : SV_Target
        {
            // Crop first, then blur in a fixed photo coordinate system. Grain is added later.
            float2 stepUV = 1.5 / max(_PhotoSize.xy, 1);
            float3 center = SampleLinear(input.uv);
            float3 softened = center * 4;
            softened += 2 * SampleLinear(input.uv + float2(stepUV.x, 0));
            softened += 2 * SampleLinear(input.uv - float2(stepUV.x, 0));
            softened += 2 * SampleLinear(input.uv + float2(0, stepUV.y));
            softened += 2 * SampleLinear(input.uv - float2(0, stepUV.y));
            softened += SampleLinear(input.uv + stepUV);
            softened += SampleLinear(input.uv - stepUV);
            softened += SampleLinear(input.uv + float2(stepUV.x, -stepUV.y));
            softened += SampleLinear(input.uv + float2(-stepUV.x, stepUV.y));
            float alpha = tex2D(_MainTex, _Crop.xy + input.uv * _Crop.zw).a;
            return float4(FromLinear(lerp(center, softened / 16, _Softness)), alpha);
        }

        // Integer hashing avoids a time dependency and remains fixed for this photograph's seed.
        uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            return value ^ (value >> 16);
        }

        float Random01(uint value)
        {
            return (Hash(value) & 0x00ffffffu) / 16777216.0;
        }

        float GrainNoise(uint value)
        {
            // Four uniforms approximate a Gaussian with unit standard deviation.
            return (Random01(value) + Random01(value + 0x68bc21ebu) +
                Random01(value + 0x02e5be93u) + Random01(value + 0x967a889bu) - 2) * 1.7320508;
        }

        float3 ApplyTone(float3 perceptual)
        {
            float luminance = dot(perceptual, float3(0.2126, 0.7152, 0.0722));
            float shoulder = max(0, luminance - 0.6);
            float shaped = min(luminance, 0.6) + shoulder / (1 + _ToneLimits.y * 4 * shoulder);
            shaped = saturate(shaped);
            // Each adjustment is monotonic within the constrained artist parameter ranges.
            shaped += (_Tone.z - 1) * (shaped - 0.5) * 2 * shaped * (1 - shaped);
            shaped += _Tone.w * 4 * shaped * (1 - shaped);
            float3 curve = perceptual * (max(0, shaped) / max(luminance, 0.00001));
            curve = lerp(_ToneLimits.x.xxx, curve, 1 - _ToneLimits.x);
            return lerp(perceptual, curve, _Tone.y);
        }

        float4 Grade(v2f_img input) : SV_Target
        {
            float2 sourceUV = _Crop.xy + input.uv * _Crop.zw;
            float4 original = tex2D(_OriginalTex, sourceUV);
            float3 originalLinear = ToLinear(original.rgb);
            if (_Strength <= 0) return original;

            float2 processingUV = lerp(sourceUV, input.uv, _Softened);
            float3 sourceLinear = ToLinear(tex2D(_MainTex, processingUV).rgb);
            if (_Modules.x > 0.5) sourceLinear *= exp2(_Tone.x);

            // Tone, split tone and grain live in display RGB. Exposure and final mixing are linear.
            float3 color = LinearToGammaSpace(max(0, sourceLinear));
            if (_Modules.x > 0.5) color = ApplyTone(color);
            if (_Modules.y > 0.5)
            {
                float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
                float chroma = max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b));
                float castProtection = 1 - _Color.w * smoothstep(0.12, 0.5, chroma);
                color = lerp(luminance.xxx, color, _Color.x);
                float highlightWeight = smoothstep(0.35, 0.85, luminance);
                // The deepest blacks stay neutral; only readable lower midtones take the cool bias.
                float shadowWeight = smoothstep(0.025, 0.18, luminance) * (1 - smoothstep(0.25, 0.6, luminance));
                color += castProtection * (highlightWeight * _Color.y * float3(0.32, 0.06, -0.22) +
                    shadowWeight * _Color.z * float3(-0.3, 0.08, 0.25));
            }
            if (_Modules.z > 0.5 && _Grain.x > 0)
            {
                uint2 cell = (uint2)floor(saturate(input.uv) * _PhotoSize.xy / max(_Grain.y, 0.5));
                uint key = Hash(cell.x + Hash(cell.y) + (uint)_Seed);
                float mono = GrainNoise(key);
                float3 chromatic = float3(GrainNoise(key + 31u), GrainNoise(key + 73u), GrainNoise(key + 127u));
                float luminance = saturate(dot(color, float3(0.2126, 0.7152, 0.0722)));
                float weight = 0.35 + 0.65 * 4 * luminance * (1 - luminance);
                color += lerp(mono.xxx, chromatic, _Grain.z) * _Grain.x * weight;
            }

            float3 resultLinear = GammaToLinearSpace(saturate(color));
            if (_Modules.w > 0.5)
            {
                float2 position = input.uv * 2 - 1;
                float falloff = smoothstep(0.2, 1, sqrt(dot(position, position) * 0.5));
                resultLinear *= exp2(-_VignetteEV * falloff * falloff);
            }
            return float4(FromLinear(lerp(originalLinear, resultLinear, _Strength)), original.a);
        }
        ENDHLSL

        // Pass zero is used only when optical softness is enabled.
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Soften
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Grade
            ENDHLSL
        }
    }
    Fallback Off
}

Shader "AnimalGame/Photos/DamagedPrime"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _OriginalTex ("Original Source", 2D) = "white" {}
        _Crop ("Crop", Vector) = (0,0,1,1)
        [HideInInspector] _Seed ("Sensor Noise Seed", Integer) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _OriginalTex;
        float4 _MainTex_TexelSize;
        float4 _Crop;
        float4 _PhotoSize;
        float4 _SourcePhotoHalfTexel;
        float4 _Tone;
        float4 _Grain;
        float _BlurRadius;
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

        float2 SourceUV(float2 photoUV)
        {
            // Clamp in the crop, including its boundary texels, rather than in the full image.
            float2 bounded = clamp(photoUV, _SourcePhotoHalfTexel.xy, 1 - _SourcePhotoHalfTexel.xy);
            return _Crop.xy + bounded * _Crop.zw;
        }

        float3 SampleHorizontal(float2 photoUV)
        {
            return ToLinear(tex2D(_MainTex, SourceUV(photoUV)).rgb);
        }

        float3 SampleVertical(float2 photoUV)
        {
            float2 halfTexel = abs(_MainTex_TexelSize.xy) * 0.5;
            return ToLinear(tex2D(_MainTex, clamp(photoUV, halfTexel, 1 - halfTexel)).rgb);
        }

        // Nine samples across +/- radius; Gaussian sigma is 0.43 times the radius.
        float4 BlurHorizontal(v2f_img input) : SV_Target
        {
            float2 stepUV = float2(_BlurRadius * 0.25 / max(_PhotoSize.x, 1), 0);
            float3 value = SampleHorizontal(input.uv) * 0.233814;
            value += (SampleHorizontal(input.uv + stepUV) + SampleHorizontal(input.uv - stepUV)) * 0.197458;
            value += (SampleHorizontal(input.uv + stepUV * 2) + SampleHorizontal(input.uv - stepUV * 2)) * 0.118907;
            value += (SampleHorizontal(input.uv + stepUV * 3) + SampleHorizontal(input.uv - stepUV * 3)) * 0.051078;
            value += (SampleHorizontal(input.uv + stepUV * 4) + SampleHorizontal(input.uv - stepUV * 4)) * 0.015650;
            return float4(FromLinear(value), 1);
        }

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
            // Fixed for the exposure; never samples time or Unity's gameplay random state.
            return (Random01(value) + Random01(value + 0x68bc21ebu) +
                Random01(value + 0x02e5be93u) + Random01(value + 0x967a889bu) - 2) * 1.7320508;
        }

        float SensorNoise(float2 photoUV)
        {
            float2 sensorPosition = saturate(photoUV) * _PhotoSize.xy / max(_Grain.y, 0.25);
            uint2 fineCell = (uint2)floor(sensorPosition);
            uint2 coarseCell = (uint2)floor(sensorPosition / 2.8);
            uint fineKey = Hash(fineCell.x + Hash(fineCell.y) + (uint)_Seed);
            uint coarseKey = Hash(coarseCell.x + Hash(coarseCell.y) + (uint)_Seed + 0xa511e9b3u);
            return (GrainNoise(fineKey) * 0.82 + GrainNoise(coarseKey) * 0.18) / 0.839524;
        }

        float4 BlurVerticalAndGrade(v2f_img input) : SV_Target
        {
            float2 stepUV = float2(0, _BlurRadius * 0.25 / max(_PhotoSize.y, 1));
            float3 value = SampleVertical(input.uv) * 0.233814;
            value += (SampleVertical(input.uv + stepUV) + SampleVertical(input.uv - stepUV)) * 0.197458;
            value += (SampleVertical(input.uv + stepUV * 2) + SampleVertical(input.uv - stepUV * 2)) * 0.118907;
            value += (SampleVertical(input.uv + stepUV * 3) + SampleVertical(input.uv - stepUV * 3)) * 0.051078;
            value += (SampleVertical(input.uv + stepUV * 4) + SampleVertical(input.uv - stepUV * 4)) * 0.015650;

            float luminance = dot(max(value, 0), float3(0.2126, 0.7152, 0.0722)) * exp2(_Tone.x);
            float gray = LinearToGammaSpace(luminance.xxx).r;
            gray = saturate((gray - 0.5) * _Tone.y + 0.5);
            gray = lerp(_Tone.z, 1, gray);
            if (_Grain.x > 0)
            {
                // Strong neutral noise remains visible in shadows, with no colored sensor flecks.
                float weight = 0.65 + 0.35 * 4 * gray * (1 - gray);
                gray += SensorNoise(input.uv) * _Grain.x * weight;
            }

            float3 result = GammaToLinearSpace(saturate(gray).xxx);
            float2 position = input.uv * 2 - 1;
            float falloff = smoothstep(0.2, 1, sqrt(dot(position, position) * 0.5));
            result *= exp2(-_Tone.w * falloff * falloff);
            // Blur and sensor damage never change the source silhouette or its transparency.
            float alpha = tex2D(_OriginalTex, SourceUV(input.uv)).a;
            return float4(FromLinear(result), alpha);
        }
        ENDHLSL

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment BlurHorizontal
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment BlurVerticalAndGrade
            ENDHLSL
        }
    }
    Fallback Off
}

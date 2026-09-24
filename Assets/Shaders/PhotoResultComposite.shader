Shader "AnimalGame/UI/Photo Result Composite"
{
    Properties
    {
        _MainTex ("Image", 2D) = "white" {}
        _Crop ("Image crop", Vector) = (0,0,1,1)
        _Tilt ("Pitch / Yaw radians", Vector) = (0,0,0,0)
        _Mode ("0: perspective card, 1: snapshot circle", Float) = 0
        _Reveal ("Clockwise sector reveal", Range(0,1)) = 1
        _ImageAspect ("Processed image aspect ratio", Float) = 1
    }
    SubShader
    {
        Tags {"Queue" = "Transparent"}
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Crop, _Tilt;
            float _Mode, _Reveal, _ImageAspect;

            float4 frag(v2f_img input) : SV_Target
            {
                float2 p = input.uv * 2 - 1;
                if (_Mode > 0.5)
                {
                    float edge = max(fwidth(length(p)), 0.001);
                    float mask = 1 - smoothstep(1 - edge, 1, length(p));
                    // UV y points up: clockwise from left goes through the top first.
                    float angle = atan2(p.y, -p.x);
                    float turn = frac(angle / UNITY_TWO_PI + 1);
                    mask *= step(turn, _Reveal) * step(0.0001, _Reveal);
                    float3 color = tex2D(_MainTex, input.uv).rgb;
                    return float4(color, mask);
                }

                float sx, cx, sy, cy;
                sincos(_Tilt.x, sx, cx);
                sincos(_Tilt.y, sy, cy);
                float3 right = float3(cy, 0, -sy);
                float3 up = float3(sy*sx, cx, cy*sx);
                float3 normal = cross(right, up);
                float3 origin = float3(0,0,4);
                float3 ray = float3(p * 1.18, -4);
                float t = -dot(normal, origin) / dot(normal, ray);
                float3 hit = origin + ray*t;
                float2 local = float2(dot(hit,right), dot(hit,up));
                float2 q = abs(local) - 0.955;
                float distance = length(max(q,0)) + min(max(q.x,q.y),0)-0.045;
                float aa = max(fwidth(distance), 0.001);
                float mask = 1 - smoothstep(-aa,aa,distance);
                float border = smoothstep(-0.012-aa, -0.012+aa, distance);
                border=0;
                float2 uv = saturate(local * 0.5 + 0.5);
                float aspect = max(_ImageAspect, 0.0001);
                float2 fit = float2(min(1, aspect), min(1, 1 / aspect));
                uv = (uv - 0.5) / fit + 0.5;
                float inside = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
                uv = saturate(uv);
                float4 photo = tex2D(_MainTex, _Crop.xy + uv * _Crop.zw);
                photo.rgb = lerp(float3(0.06,0.065,0.06), photo.rgb, inside * photo.a);
                float light = 0.96 + 0.04 * saturate(dot(normal,normalize(float3(-0.3,0.4,1))));
                return float4(lerp(photo.rgb * light, float3(0.96,0.97,0.95),border), mask);
            }
            ENDHLSL
        }
    }
}

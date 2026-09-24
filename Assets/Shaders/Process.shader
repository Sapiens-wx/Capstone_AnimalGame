Shader "AnimalGame/Photos/Process"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Crop ("Crop", Vector) = (0,0,1,1)
        _Saturation ("Saturation", Range(0,2)) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Crop;
            float _Saturation;
            float4 frag(v2f_img input) : SV_Target
            {
                float4 color = tex2D(_MainTex, _Crop.xy + input.uv * _Crop.zw);
                float luminance = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
                color.rgb = saturate(lerp(luminance.xxx, color.rgb, _Saturation));
                return color;
            }
            ENDHLSL
        }
    }
}

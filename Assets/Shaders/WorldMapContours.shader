Shader "AnimalGame/World Map Contours"
{
    Properties
    {
        _HeightTex ("Height", 2D) = "black" {}
        _CheckpointFogTex ("Checkpoint Fog", 2D) = "black" {}
        _MinimumLineWidth ("Minimum Line Width", Float) = 0.75
        _MaximumLineWidth ("Maximum Line Width", Float) = 3
        _MinimumOpacity ("Minimum Opacity", Float) = 0.15
        _MaximumOpacity ("Maximum Opacity", Float) = 1
        _MaximumCoverage ("Maximum Coverage", Float) = 0.45
        _EdgeSoftness ("Edge Softness", Float) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _HeightTex;
            sampler2D _CheckpointFogTex;
            float2 _MapSizeMeters;
            float4 _TileRect, _ViewRect;
            float _MinimumHeight, _MaximumHeight;
            float _VisibleMinimumHeight, _VisibleMaximumHeight;
            float _MinimumLineWidth, _MaximumLineWidth;
            float _MinimumOpacity, _MaximumOpacity, _MaximumCoverage, _EdgeSoftness;
            static const float _ContourInterval = 14;
            #include "HeightContourLines.cginc"
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_img input)
            {
                v2f output;
                float2 mapPosition = _TileRect.xy + input.vertex.xy * _TileRect.zw;
                float2 clip = (mapPosition - _ViewRect.xy) / _ViewRect.zw * 2 - 1;
                #if UNITY_UV_STARTS_AT_TOP
                clip.y = -clip.y;
                #endif
                output.vertex = float4(clip, 0, 1);
                output.uv = input.texcoord;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                float heightMeters = lerp(_MinimumHeight, _MaximumHeight, saturate(tex2D(_HeightTex, input.uv).r));
                float contourBlend = HeightContourBlend(heightMeters);
                float2 mapPosition = _TileRect.xy + input.uv * _TileRect.zw;
                contourBlend *= 1 - tex2D(_CheckpointFogTex, mapPosition / _MapSizeMeters).r;
                return fixed4(contourBlend, contourBlend, contourBlend, 1);
            }
            ENDCG
        }
    }
}

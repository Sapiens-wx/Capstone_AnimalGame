Shader "Hidden/AnimalGame/Grassland Render Probe"
{
    Properties
    {
        _GrassMaskTex ("Mask", 2D) = "white" {}
        _GrassPatternTex ("Pattern", 2D) = "black" {}
        _GrassTileSize ("Tile", Float) = 1
        _MapSizeMeters ("Map size", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            float4 _MapSizeMeters;
            #include "../Shaders/GrasslandDynamics.cginc"
            float4 frag(v2f_img input) : SV_Target { return SampleGrassland(input.uv, _GrassActive); }
            ENDCG
        }
    }
}

Shader "Hidden/AnimalGame/Grassland Render Probe"
{
    Properties
    {
        _GrassMaskTex ("Mask", 2D) = "white" {}
        _GrassPatternTex ("Pattern", 2D) = "black" {}
        _GrassTileSize ("Tile", Float) = 1
        _GrassLayout ("Layout", Vector) = (0, 0.4, 20, 0.17453293)
        _GrassShape ("Density And Length", Vector) = (0.5, 1, 0, 0)
        _GrassContourOpacity ("Baked Edge, Interior, Target Edge Alpha, Interior Alpha", Vector) = (0.235, 0.05, 0.3, 0.1)
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
            #pragma multi_compile_local _ GRASS_WIDE_NEIGHBOURHOOD
            #include "UnityCG.cginc"
            float4 _MapSizeMeters;
            float _GrassProbeMode;
            #ifdef GRASS_WIDE_NEIGHBOURHOOD
            #define GRASS_NEIGHBOUR_RADIUS 2
            #endif
            #include "../../../Shaders/GrasslandDynamics.cginc"
            float4 frag(v2f_img input) : SV_Target
            {
                float2 meters = input.uv * _MapSizeMeters.xy + _GrassTileOrigin.xy;
                if (_GrassProbeMode > 1.5) return GrassDirection(meters);
                if (_GrassProbeMode > .5)
                    return GrassInstance(floor(meters / GrassSpacing()));
                return SampleGrassland(input.uv, _GrassActive);
            }
            ENDCG
        }
    }
}

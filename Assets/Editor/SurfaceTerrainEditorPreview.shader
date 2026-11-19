Shader "Hidden/AnimalGame/SurfaceTerrainEditorPreview"
{
    Properties
    {
        _MainTex ("Terrain Colors", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            v2f vert(appdata_img input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                return tex2D(_MainTex, input.uv);
            }
            ENDCG
        }
    }
}

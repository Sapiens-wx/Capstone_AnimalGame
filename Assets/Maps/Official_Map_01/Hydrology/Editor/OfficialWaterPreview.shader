Shader "Hidden/AnimalGame/Official Water Preview"
{
    Properties { _Color ("Color", Color) = (0.08, 0.56, 0.8, 0.7) }

    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            float4 Vert(float4 position : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(position);
            }

            fixed4 Frag() : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}

Shader "AnimalGame/Player UI Clipped Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _RequirePlayerUiClip ("Require Active Player Disk", Float) = 0
        [Toggle(TOWER_INTERACTION_LIGHTS)] _TowerInteractionLights ("Blink Gold Interaction Strips", Float) = 0
        [PerRendererData] _ConnectedSocketUV ("Seated Plug UV Centre and Half Size", Vector) = (0,0,0,0)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #pragma multi_compile_local _ VEGETATION_DEFORMATION
            #pragma shader_feature_local_fragment _ TOWER_INTERACTION_LIGHTS

            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                #ifdef VEGETATION_DEFORMATION
                float2 spriteLocal : TEXCOORD1;
                #endif
                fixed4 color : COLOR;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                float4 screenPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _AlphaTex;
            fixed4 _Color;
            float _EnableExternalAlpha;
            float _RequirePlayerUiClip;
            float _WeatherTowerInteractionDim;
            float4 _ConnectedSocketUV;
            float _PlayerUiClipEnabled;
            float4 _PlayerUiClipCenterPixels;
            float _PlayerUiClipRadiusPixels;
            float _PlayerUiClipSoftnessPixels;

            #ifdef VEGETATION_DEFORMATION
            float4x4 _VegetationLocalToWorld;
            float4 _VegetationCenterRadius;
            float4 _VegetationDirectionPull;
            float4 _VegetationGripPinchOffset;

            float4 VegetationToClip(float2 vertex)
            {
                float4 world = mul(_VegetationLocalToWorld, float4(vertex, 0, 1));
                float2 relative = world.xy - _VegetationCenterRadius.xy;
                float radius = _VegetationCenterRadius.z;
                float2 direction = _VegetationDirectionPull.xy;
                float2 across = float2(-direction.y, direction.x);
                float along = dot(relative, direction);
                float side = dot(relative, across) - _VegetationGripPinchOffset.x;
                float front = smoothstep(0.05 * radius, 0.85 * radius, along);
                float width = 1.0 - smoothstep(0.0, 0.9 * radius, abs(side));
                float2 movement = direction * _VegetationDirectionPull.z
                    - across * (side * (1.0 - front) * _VegetationGripPinchOffset.y);
                world.xy += movement * (front * width) + _VegetationGripPinchOffset.zw;
                return mul(UNITY_MATRIX_VP, world);
            }
            #endif

            Varyings Vert(AppData input)
            {
                Varyings output;
                #ifdef VEGETATION_DEFORMATION
                output.vertex = VegetationToClip(input.spriteLocal);
                #else
                output.vertex = UnityObjectToClipPos(input.vertex);
                #endif
                #ifdef PIXELSNAP_ON
                output.vertex = UnityPixelSnap(output.vertex);
                #endif
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                output.screenPosition = ComputeScreenPos(output.vertex);
                return output;
            }

            fixed4 SampleSpriteTexture(float2 uv)
            {
                fixed4 color = tex2D(_MainTex, uv);
                #if ETC1_EXTERNAL_ALPHA
                fixed4 alpha = tex2D(_AlphaTex, uv);
                color.a = lerp(color.a, alpha.r, _EnableExternalAlpha);
                #endif
                return color;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 color = SampleSpriteTexture(input.texcoord);
                #ifdef TOWER_INTERACTION_LIGHTS
                // Only the interaction strips have chroma in the station artwork.
                half gold = smoothstep(0.01h, 0.18h, color.r - color.b);
                bool seated = all(abs(input.texcoord - _ConnectedSocketUV.xy) < _ConnectedSocketUV.zw);
                if (!seated)
                    color.rgb *= 1.0h - gold * _WeatherTowerInteractionDim;
                #endif
                color *= input.color;
                float2 screenUv = input.screenPosition.xy
                                  / max(0.0001, input.screenPosition.w);
                float2 screenPixels = screenUv * _ScreenParams.xy;
                float distanceFromCenter = length(
                    screenPixels - _PlayerUiClipCenterPixels.xy);
                float radius = max(0.0, _PlayerUiClipRadiusPixels);
                float softness = max(
                    0.001,
                    _PlayerUiClipSoftnessPixels);
                if (_RequirePlayerUiClip > 0.5 && (_PlayerUiClipEnabled <= 0.5 || radius <= 0.0))
                    discard;
                float insideVisibility = 1.0 - smoothstep(
                    max(0.0, radius - softness),
                    radius + (_RequirePlayerUiClip > 0.5 ? 0.0 : softness),
                    distanceFromCenter);
                color.a *= lerp(
                    1.0,
                    insideVisibility,
                    saturate(_PlayerUiClipEnabled));
                clip(color.a - 0.001);
                return color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}

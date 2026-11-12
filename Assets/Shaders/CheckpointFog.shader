Shader "Hidden/AnimalGame/Checkpoint Fog"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            StructuredBuffer<float4> _Checkpoints;
            int _CheckpointCount;
            float2 _MapSize;
            float _TransitionWidth, _NoiseScale, _NoiseAmplitude;
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(Hash(cell), Hash(cell+float2(1,0)), f.x),
                    lerp(Hash(cell+float2(0,1)), Hash(cell+1), f.x), f.y);
            }
            float4 frag(v2f_img input) : SV_Target
            {
                float2 p = input.uv * _MapSize;
                float reached = 1e30, hidden = 1e30;
                bool hasReached = false, hasHidden = false;
                for (int i = 0; i < _CheckpointCount; i++)
                {
                    float4 seed = _Checkpoints[i];
                    float distance = dot(p-seed.xy,p-seed.xy);
                    if (seed.z > .5) { reached = min(reached,distance); hasReached = true; }
                    else { hidden = min(hidden,distance); hasHidden = true; }
                }
                if (!hasReached) return 1;
                if (!hasHidden) return 0;
                float2 n = p / _NoiseScale;
                float noise = (Noise(n)*.533333 + Noise(n*2)*.266667
                    + Noise(n*4)*.133333 + Noise(n*8)*.066667) * 2 - 1;
                float margin = sqrt(hidden) - sqrt(reached) + noise * _NoiseAmplitude;
                return 1 - smoothstep(-_TransitionWidth*.5, _TransitionWidth*.5, margin);
            }
            ENDHLSL
        }
    }
}

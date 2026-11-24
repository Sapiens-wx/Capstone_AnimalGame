Shader "AnimalGame/World Map Contours"
{
    Properties
    {
        _WaterFlowSpeed ("Local Water Flow Speed (m/s)", Float) = 10
        _HeightTex ("Height", 2D) = "black" {}
        _CheckpointFogTex ("Checkpoint Fog", 2D) = "black" {}
        [HideInInspector] _PreviousCheckpointFogTex ("Previous Checkpoint Fog", 2D) = "black" {}
        _MinimumLineWidth ("Minimum Line Width", Float) = 0.75
        _MaximumLineWidth ("Maximum Line Width", Float) = 3
        _MinimumOpacity ("Minimum Opacity", Float) = 0.15
        _MaximumOpacity ("Maximum Opacity", Float) = 1
        _MaximumCoverage ("Maximum Coverage", Float) = 0.45
        _EdgeSoftness ("Edge Softness", Float) = 0.4
        [HideInInspector] _MainTex ("Map Image", 2D) = "black" {}
        [HideInInspector] _FaultProgress ("Data Fault Progress", Range(0, 1)) = 1
        [HideInInspector] _FaultTint ("Data Fault Tint", Color) = (0.83137255, 0.72156863, 0.32941176, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        CGINCLUDE
        #include "UnityCG.cginc"

        float SurveyPattern(float2 mapPosition, float2 metresPerPixel, float spacing)
        {
            float2 dotOffset = abs(frac(mapPosition / spacing + .5) - .5) * spacing / metresPerPixel;
            float dotMask = 1 - smoothstep(.3, 1.2, length(dotOffset));

            float crossSpacing = spacing * 8;
            float2 crossOffset = abs(frac(mapPosition / crossSpacing + .5) - .5)
                * crossSpacing / metresPerPixel;
            float horizontalArm = max(crossOffset.x - 3.5, crossOffset.y - .55);
            float verticalArm = max(crossOffset.x - .55, crossOffset.y - 3.5);
            float crossMask = 1 - smoothstep(-.45, .45, min(horizontalArm, verticalArm));
            return .008 + max(.036 * dotMask, .075 * crossMask);
        }

        float UnknownRegionColor(float2 mapPosition, float2 metresPerPixel)
        {
            // The coarser grid shares every other position with the finer one as the map zooms out.
            float level = log2(max(metresPerPixel.x, metresPerPixel.y) * 22);
            float spacing = exp2(floor(level));
            return lerp(SurveyPattern(mapPosition, metresPerPixel, spacing),
                SurveyPattern(mapPosition, metresPerPixel, spacing * 2), smoothstep(0, 1, frac(level)));
        }
        ENDCG

        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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
            #include "MapWater.cginc"
            float _MapWaterTime;
            float _DashSubmergedContours;

            float WaterAtScale(float2 mapMeters, float2 bakedUv, float depth, float2 flow,
                float scale, float backing, float opacityGain)
            {
                fixed4 ripple = SampleWaterPattern(mapMeters, depth, flow, _MapWaterTime, bakedUv, scale);
                return lerp(backing, LinearRgbToLuminance(ripple.rgb), saturate(ripple.a * opacityGain));
            }

            float BankSamples(float2 uv, float2 offset)
            {
                return tex2Dlod(_WaterMaskTex, float4(uv + offset, 0, 0)).r
                     + tex2Dlod(_WaterMaskTex, float4(uv - offset, 0, 0)).r;
            }

            // Four fixed orientations let long ripples turn with the river without
            // rotating distant map coordinates by a different angle at every pixel.
            float RiverOrientation(float2 uv, float2 flow, float metresPerPixel)
            {
                float2 axis;
                if (dot(flow, flow) > .0025)
                    axis = float2(flow.x * flow.x - flow.y * flow.y, 2 * flow.x * flow.y);
                else
                {
                    // In unpainted current fields the longer wet axis follows the banks.
                    float2 radius = 12 * metresPerPixel / (_TileRect.zw * _WaterMaskBounds.zw);
                    float2 diagonal = radius * .707107;
                    axis.x = BankSamples(uv, float2(radius.x, 0)) - BankSamples(uv, float2(0, radius.y)) + .2;
                    axis.y = BankSamples(uv, diagonal) - BankSamples(uv, float2(diagonal.x, -diagonal.y));
                }
                return dot(axis, axis) < .0001 ? 0 : frac(atan2(axis.y, axis.x) / 6.283185 + 1) * 4;
            }

            float OverviewInk(float2 uv, float pixelsPerRepeat)
            {
                // The source has four staggered rows of thin dashes. Keep the rows
                // close enough for tributaries, and filter across each stroke to give it body.
                uv.y *= 2.5;
                float2 dx = float2(1, 0) / pixelsPerRepeat;
                float2 dy = float2(0, 8) / pixelsPerRepeat;
                float4 main = tex2Dgrad(_WaterPatternTex, uv, dx, dy);
                float4 offset = tex2Dgrad(_WaterPatternTex, uv + float2(.50, .045), dx, dy);
                return max(LinearRgbToLuminance(main.rgb) * main.a,
                    .8 * LinearRgbToLuminance(offset.rgb) * offset.a);
            }

            float OrientedInk(float2 position, float direction, float spacing, float metresPerPixel)
            {
                float angle = direction * .78539816;
                float2 along = float2(cos(angle), sin(angle));
                float2 uv = float2(dot(position, along), dot(position, float2(-along.y, along.x))) / spacing;
                uv.y += _WaterWaveDistortion * sin(uv.x * 4.19 + _MapWaterTime * _WaterWaveSpeed);
                return OverviewInk(uv, spacing / metresPerPixel);
            }

            float OverviewAtScale(float2 position, float orientation, float spacing, float metresPerPixel)
            {
                float first = floor(orientation);
                float ink = OrientedInk(position, first, spacing, metresPerPixel);
                float blend = smoothstep(0, 1, frac(orientation));
                [branch] if (blend > 0)
                    ink = lerp(ink, OrientedInk(position, fmod(first + 1, 4), spacing, metresPerPixel), blend);
                return ink;
            }

            float OverviewRipples(float2 mapMeters, float2 bakedUv, float2 flow, float2 waterUv,
                float metresPerPixel, float spacing, float blend)
            {
                float orientation = 0;
                if (any(bakedUv != 0))
                {
                    mapMeters = bakedUv;
                    flow = float2(length(flow), 0);
                }
                else orientation = RiverOrientation(waterUv, flow, metresPerPixel);

                float2 position = mapMeters + _WaterLayerOneSpeed.xy * _MapWaterTime;
                // Crossfade two short periods of advection. Variable local speeds can
                // otherwise shear large marks apart after the map has been open a while.
                float phase = frac(_MapWaterTime * .25);
                float2 travel = flow * _WaterFlowSpeed * 4;
                float2 p0 = position - (phase - .5) * travel;
                float ink = lerp(OverviewAtScale(p0, orientation, spacing, metresPerPixel),
                    OverviewAtScale(p0, orientation, spacing * 2, metresPerPixel), blend);
                [branch] if (any(flow != 0))
                {
                    float2 p1 = position - (frac(phase + .5) - .5) * travel;
                    float second = lerp(OverviewAtScale(p1, orientation, spacing, metresPerPixel),
                        OverviewAtScale(p1, orientation, spacing * 2, metresPerPixel), blend);
                    ink = lerp(ink, second, abs(2 * phase - 1));
                }
                return smoothstep(.048, .125, ink);
            }

            float MapRipples(float2 mapMeters, float metresPerPixel, float2 waterUv, float depth, float backing)
            {
                float2 bakedUv = SampleBakedWaterUv(waterUv);
                float2 flow = SampleWaterFlow(waterUv);
                // Keep the scene's metre scale up close. When its fine strokes become
                // subpixel, blend world-anchored repeats around 36 pixels per tile.
                float level = max(0, log2(36 * metresPerPixel / _WaterTileSizeMeters));
                float scale = exp2(floor(level));
                float blend = smoothstep(0, 1, frac(level));
                float gain = 1 + .55 * smoothstep(0, 2, level);
                float water = WaterAtScale(mapMeters, bakedUv, depth, flow, scale, backing, gain);
                [branch] if (level > 0)
                    water = lerp(water, WaterAtScale(mapMeters, bakedUv, depth, flow, scale * 2, backing, gain),
                        blend);
                float overview = smoothstep(.55, 2, level);
                [branch] if (overview > 0)
                {
                    float ink = OverviewRipples(mapMeters, bakedUv, flow, waterUv, metresPerPixel,
                        4 * _WaterTileSizeMeters * scale, blend);
                    // Keep just the brighter tips of the fine ripples between the white strokes.
                    float tips = .35 * max(0, water - .065);
                    water = lerp(water, max(tips, lerp(backing, 1, ink)), overview);
                }
                return water;
            }

            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_img input)
            {
                v2f output;
                // The animated redraw uses the same tile coordinates on the cropped water quad.
                output.uv = mul(unity_ObjectToWorld, input.vertex).xy;
                float2 mapPosition = _TileRect.xy + output.uv * _TileRect.zw;
                float2 clip = (mapPosition - _ViewRect.xy) / _ViewRect.zw * 2 - 1;
                #if UNITY_UV_STARTS_AT_TOP
                clip.y = -clip.y;
                #endif
                output.vertex = float4(clip, 0, 1);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float heightMeters = lerp(_MinimumHeight, _MaximumHeight, saturate(tex2D(_HeightTex, input.uv).r));
                float contourBlend = HeightContourBlend(heightMeters);
                float2 mapPosition = _TileRect.xy + input.uv * _TileRect.zw;
                float2 metresPerPixel = fwidth(mapPosition);
                float brightness = contourBlend;
                if (_WaterEnabled > .5)
                {
                    float submerged = 0;
                    if (_DashSubmergedContours > .5)
                        submerged = .25 * contourBlend * SubmergedContourDash(mapPosition, heightMeters);
                    float2 waterUv;
                    float insideBounds;
                    fixed4 waterData = ReadMapWater(input.uv, waterUv, insideBounds);
                    float range = smoothstep(.02, .98, waterData.r);
                    [branch] if (range > 0)
                        brightness = lerp(brightness, MapRipples(mapPosition, max(metresPerPixel.x, metresPerPixel.y),
                            waterUv, waterData.g, submerged), range);
                }
                float unknown = tex2D(_CheckpointFogTex, mapPosition / _MapSizeMeters).r;
                if (unknown > 0)
                    brightness = lerp(brightness, UnknownRegionColor(mapPosition, metresPerPixel), unknown);
                return fixed4(brightness.xxx, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "DATA_FAULT"
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragFault
            #pragma target 3.0
            sampler2D _MainTex;
            float4 _FaultTint;
            float _FaultProgress;

            float FaultHash(float value)
            {
                return frac(sin(value * 12.9898) * 43758.5453);
            }

            fixed4 fragFault(v2f_img input) : SV_Target
            {
                float severity = 1 - smoothstep(.12, 1, _FaultProgress);
                float phase = _FaultProgress * 13;
                float band = input.uv.y * 17;
                float row = frac(band);
                float key = floor(band) + floor(phase) * 29;
                float error = FaultHash(key + 1);
                float dropped = step(.69, error);
                float activeRows = step(.07, row) * step(row, .78);
                float shift = (FaultHash(key + 51) - .5) * .2 * severity;
                float2 sampleUv = input.uv + float2(shift * activeRows * step(.43, error), 0);
                float visible = step(0, sampleUv.x) * step(sampleUv.x, 1);
                float3 color = tex2D(_MainTex, saturate(sampleUv)).rgb * visible;
                color *= 1 - dropped * activeRows * severity * .94;

                // Broken data strokes pulse at the edge of lost bands.
                float cell = input.uv.x * lerp(9, 21, FaultHash(key + 17));
                float segment = step(.38, FaultHash(key + floor(cell) * 13)) * step(frac(cell), .76);
                float stroke = 1 - smoothstep(fwidth(band), 2.5 * fwidth(band), abs(row - .78));
                float pulse = 1 - .65 * smoothstep(.12, .95, frac(phase));
                color += _FaultTint.rgb * (.38 * dropped * segment * stroke * severity * pulse);
                return fixed4(color, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "REGION_REVEAL"
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragReveal
            #pragma target 3.0

            sampler2D _MainTex, _CheckpointFogTex, _PreviousCheckpointFogTex;
            float4 _MainTex_TexelSize, _ViewRect, _RevealWave, _FaultTint;
            float2 _MapSizeMeters;

            fixed4 fragReveal(v2f_img input) : SV_Target
            {
                fixed4 mapColor = tex2D(_MainTex, input.uv);
                if (mapColor.a == 0) return mapColor;

                float2 mapPosition = _ViewRect.xy + input.uv * _ViewRect.zw;
                float2 fogUv = mapPosition / _MapSizeMeters;
                float currentFog = tex2D(_CheckpointFogTex, fogUv).r;
                float change = tex2D(_PreviousCheckpointFogTex, fogUv).r - currentFog;
                if (change <= 0) return mapColor;

                float2 metresPerPixel = abs(_MainTex_TexelSize.xy) * _ViewRect.zw;
                float2 offset = mapPosition - _RevealWave.xy;
                float distance = length(offset);
                float radialPixel = max(1e-5, length(metresPerPixel * offset / max(1e-5, distance)));
                float edgePixels = (distance - _RevealWave.z) / radialPixel;
                float front = smoothstep(-.65, .65, edgePixels);
                float progress = _RevealWave.w;
                if (progress <= 0) front = 1;

                // The cached image already contains current fog. Replace only the fraction
                // of its remaining terrain needed to reconstruct the old fog before the wave.
                float remainingTerrain = max(1e-5, 1 - currentFog);
                float fogRestore = saturate(change / remainingTerrain) * front;
                float3 color = lerp(mapColor.rgb, UnknownRegionColor(mapPosition, metresPerPixel), fogRestore);
                float stroke = 1 - smoothstep(.35, 1, abs(edgePixels));
                float envelope = smoothstep(0, .06, progress) * (1 - smoothstep(.84, 1, progress));
                color = lerp(color, _FaultTint.rgb, .8 * change * stroke * envelope);
                return fixed4(color, mapColor.a);
            }
            ENDCG
        }
    }
}

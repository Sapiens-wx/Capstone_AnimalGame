Shader "AnimalGame/Dynamic Height Contours"
{
    Properties
    {
        [PerRendererData] _MainTex ("Base Map", 2D) = "white" {}
        [HideInInspector] _PhotoContoursOnly ("Photo Contours Only", Float) = 0
        [HideInInspector] _PhotoBankOpacity ("Photo Bank Opacity", Float) = .2
        [HideInInspector] _DashSubmergedContours ("Dash Submerged Contours", Float) = 0
        [HideInInspector] _SmoothContourEdges ("Smooth Contour Edges", Float) = 0
        [HideInInspector] _OpeningMapScan ("Opening Map Scan", Vector) = (2, 2, 0, 0)
        _HeightTex ("Height Map", 2D) = "black" {}
        _SurfaceEnabled ("Surface Enabled", Float) = 0
        _GrassMaskTex ("Grassland Coverage", 2D) = "black" {}
        _GrassPatternTex ("Grassland Blades", 2D) = "black" {}
        _GrassEnabled ("Grassland Enabled", Float) = 0
        _GrassOverlay1 ("Grass1", 2D) = "black" {}
        _GrassOverlay2 ("Grass2", 2D) = "black" {}
        _GrassOverlay3 ("Grass3", 2D) = "black" {}
        _GrassOverlaySettings ("Overlay Density, Size, Variants, Base Opacity", Vector) = (0.18, 0.65, 0, 0.2)
        _GrassContourOpacity ("Baked Edge, Interior, Target Edge Alpha, Interior Alpha", Vector) = (0.3, 0.06, 0.3, 0.06)
        _GrassTileSize ("Grassland Tile Size", Float) = 3.2
        _GrassLayout ("Grass Layout: Seed, Jitter, Region Scale, Angle", Vector) = (0, 0.4, 20, 0.17453293)
        _GrassShape ("Grass Density And Length", Vector) = (0.5, 1, 0, 0)
        _SurfaceRevealEnabled ("Surface UI Reveal Enabled", Float) = 0
        _SurfaceRevealCenterPixels ("Surface UI Centre", Vector) = (0, 0, 0, 0)
        _SurfaceRevealRadiusPixels ("Surface UI Radius", Float) = 430
        _SurfaceRevealEdgePixels ("Surface UI Edge", Float) = 4
        _OutsideRevealDarkening ("Outside Reveal Darkening", Range(0, 1)) = 0.3
        _WaterMaskTex ("Static Water Range And Depth", 2D) = "black" {}
        _WaterUvTex ("Baked Water UV (RGHalf)", 2D) = "black" {}
        _WaterMaskBounds ("Water Mask Map UV Bounds", Vector) = (0, 0, 1, 1)
        _WaterPatternTex ("Animated Water Pattern", 2D) = "black" {}
        _WaterEnabled ("Animated Water Enabled", Float) = 0
        _MapSizeMeters ("Map Size Meters", Vector) = (250, 250, 0, 0)
        _WaterOriginMeters ("Water Pattern Origin Meters", Vector) = (0, 0, 0, 0)
        _WaterTileSizeMeters ("Water Tile Size Meters", Float) = 8
        _WaterLayerOneSpeed ("Water Primary Speed", Vector) = (0.36, 0.04, 0, 0)
        _WaterLayerTwoSpeed ("Water Secondary Speed", Vector) = (-0.14, 0.22, 0, 0)
        _WaterLayerTwoScale ("Water Secondary Scale", Float) = 1.35
        _WaterWaveDistortion ("Water Wave Distortion", Float) = 0.03
        _WaterWaveSpeed ("Water Wave Speed", Float) = 0.8
        _WaterWaveLengthMeters ("Water Wave Length Meters", Float) = 12
        _WaterFlowSpeed ("Local Water Flow Speed (m/s)", Float) = 10
        _ContourColor ("Contour Color", Color) = (1, 1, 1, 1)
        _MinimumHeight ("Minimum Height", Float) = 0
        _MaximumHeight ("Maximum Height", Float) = 200
        _VisibleMinimumHeight ("Visible Minimum Contour", Float) = 0
        _VisibleMaximumHeight ("Visible Maximum Contour", Float) = 200
        _ContourInterval ("Contour Interval", Float) = 10
        _MinimumLineWidth ("Minimum Line Width", Float) = 0.75
        _MaximumLineWidth ("Maximum Line Width", Float) = 3
        _MinimumOpacity ("Minimum Opacity", Range(0, 1)) = 0.15
        _MaximumOpacity ("Maximum Opacity", Range(0, 1)) = 1
        _MaximumCoverage ("Maximum Contour Coverage", Range(0.1, 0.7)) = 0.45
        _EdgeSoftness ("Edge Softness", Range(0.1, 1.5)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float4 screenPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            float _PhotoContoursOnly, _PhotoBankOpacity;
            float2 _PhotoWaterMetresPerPixel;
            float _DashSubmergedContours;
            float _SmoothContourEdges;
            float4 _OpeningMapScan;
            sampler2D _HeightTex;
            float _SurfaceEnabled;
            float _EnclosedRoomSurfaceFade;
            float _SurfaceRevealEnabled;
            float4 _SurfaceRevealCenterPixels;
            float _SurfaceRevealRadiusPixels;
            float _SurfaceRevealEdgePixels;
            float _OutsideRevealDarkening;
            float4 _MapSizeMeters;
            fixed4 _ContourColor;
            float _MinimumHeight;
            float _MaximumHeight;
            float _VisibleMinimumHeight;
            float _VisibleMaximumHeight;
            float _ContourInterval;
            float _MinimumLineWidth;
            float _MaximumLineWidth;
            float _MinimumOpacity;
            float _MaximumOpacity;
            float _MaximumCoverage;
            float _EdgeSoftness;

            #include "GrasslandDynamics.cginc"
            #include "TerrainOriginalPatterns.cginc"
            #include "HeightContourLines.cginc"
            #include "MapWaterSymbols.cginc"
            #include "OpeningMapScan.cginc"

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                output.screenPosition = ComputeScreenPos(output.vertex);
                return output;
            }

            float SampleContourHeight(float2 uv, float center, float radiusMeters)
            {
                // Collapse taps across a tile edge while allowing smoothing parallel to it.
                // Adjacent tiles therefore sample the same shared border.
                float2 offset = min(radiusMeters / _MapSizeMeters.xy, max(0.0, min(uv, 1.0 - uv)));
                float2 x = float2(offset.x, 0.0);
                float2 y = float2(0.0, offset.y);
                float axial = tex2D(_HeightTex, uv - x).r + tex2D(_HeightTex, uv + x).r
                    + tex2D(_HeightTex, uv - y).r + tex2D(_HeightTex, uv + y).r;
                float diagonal = tex2D(_HeightTex, uv - x - y).r + tex2D(_HeightTex, uv + x - y).r
                    + tex2D(_HeightTex, uv - x + y).r + tex2D(_HeightTex, uv + x + y).r;
                return lerp(_MinimumHeight, _MaximumHeight, saturate((4.0 * center + 2.0 * axial + diagonal) / 16.0));
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // The sprite only supplies geometry; its carrier texture must not control map opacity.
                fixed4 baseColor = fixed4(0, 0, 0, input.color.a);
                if (_PhotoContoursOnly > 0.5) baseColor = fixed4(0, 0, 0, 1);

                // The shared material advances contours and surface from hidden to scanning to ready.
                bool openingScan = _PhotoContoursOnly < .5 && any(_OpeningMapScan.xy < 1.5);
                if (openingScan && _OpeningMapScan.x < .5) return baseColor;

                float2 screenUv = input.screenPosition.xy / max(input.screenPosition.w, 0.00001);
                float2 contourUv = input.uv;
                float2 surfaceUv = input.uv;
                float contourVisibility = 1;
                float surfaceVisibility = openingScan ? step(.5, _OpeningMapScan.y) : 1;
                bool surfaceScanning = openingScan && _OpeningMapScan.y > .5 && _OpeningMapScan.y < 1.5;
                if (openingScan)
                {
                    float2 scanPoint = float2(screenUv.x, 1 - screenUv.y);
                    float2 readOffset;
                    if (_OpeningMapScan.x < 1.5)
                    {
                        contourVisibility = OpeningContourScan(scanPoint, _OpeningMapScan.z, readOffset);
                        contourUv = OpeningScanUv(input.uv, scanPoint, readOffset, contourVisibility);
                    }
                    else if (surfaceScanning)
                    {
                        surfaceVisibility = OpeningSurfaceScan(scanPoint, _OpeningMapScan.z, _OpeningMapScan.w, readOffset);
                        surfaceUv = OpeningScanUv(input.uv, scanPoint, readOffset, surfaceVisibility);
                    }
                }

                float revealMask = 1.0;
                float landVisibility = 1.0;
                float rippleTransmission = 1.0;
                float2 mapMeters = contourUv * _MapSizeMeters.xy + _WaterOriginMeters.xy;
                // Use the continuous tile UV for the pattern footprint across scan band edges.
                float2 terrainDx = ddx(input.uv) * _MapSizeMeters.xy;
                float2 terrainDy = ddy(input.uv) * _MapSizeMeters.xy;
                if (_SurfaceRevealEnabled > 0.5)
                {
                    float2 pixelPosition = screenUv * _ScreenParams.xy;
                    float distanceFromUiCentre = distance(
                        pixelPosition,
                        _SurfaceRevealCenterPixels.xy);
                    float revealRadius = max(1.0, _SurfaceRevealRadiusPixels);
                    float revealEdge = min(
                        max(0.0, _SurfaceRevealEdgePixels),
                        revealRadius);
                    revealMask = revealEdge > 0.0001
                        ? 1.0 - smoothstep(
                            revealRadius - revealEdge,
                            revealRadius,
                            distanceFromUiCentre)
                        : step(distanceFromUiCentre, revealRadius);
                }

                if (_PhotoContoursOnly < .5 && (_SurfaceEnabled > 0.5 || _WaterEnabled > 0.5))
                {
                    float2 waterUv;
                    float insideWaterBounds;
                    fixed4 waterData = ReadMapWater(contourUv, waterUv, insideWaterBounds);
                    // Fade only on the dry side of the mask's midpoint.
                    landVisibility -= _DashSubmergedContours * smoothstep(0.02, 0.5, waterData.r);

                    // Only the live player view fades its terrain texture. Contours are drawn separately below.
                    float roomSurfaceVisibility = _SurfaceRevealEnabled > .5
                        ? 1.0 - saturate(_EnclosedRoomSurfaceFade) : 1.0;
                    [branch] if (surfaceVisibility > .5 && roomSurfaceVisibility > 0.0)
                    {
                        float surfaceLandVisibility = landVisibility;
                        if (surfaceScanning)
                        {
                            // Read the shifted surface without shifting the already restored contours.
                            waterData = ReadMapWater(surfaceUv, waterUv, insideWaterBounds);
                            surfaceLandVisibility = 1 - _DashSubmergedContours * smoothstep(.02, .5, waterData.r);
                        }
                        float waterRange = smoothstep(.02, .98, waterData.r);
                        float landVisibilityFactor = min(1.0 - waterRange, surfaceLandVisibility);
                        // Land patterns appear inside the player UI where water leaves them visible.
                        [branch] if (revealMask > 0 && landVisibilityFactor > 0)
                        {
                            fixed4 terrainSurface = _SurfaceEnabled > 0.5
                                ? SampleOriginalTerrain(surfaceUv, terrainDx, terrainDy)
                                : fixed4(0.0, 0.0, 0.0, 0.0);
                            if (_GrassEnabled > .5 && revealMask > .0001)
                                terrainSurface = CombineGrassland(terrainSurface,
                                    SampleGrassland(surfaceUv, terrainDx, terrainDy, _GrassActive * step(.001, revealMask)));
                            if (terrainSurface.a * revealMask > .0001)
                                terrainSurface.a *= MapShoreTerrainOpacity(surfaceUv, _MapSizeMeters.xy);
                            float terrainBlend = saturate(terrainSurface.a * revealMask * landVisibilityFactor);
                            baseColor.rgb = lerp(baseColor.rgb, terrainSurface.rgb, terrainBlend);
                        }

                        if (_WaterEnabled > 0.5)
                        {
                            float waterBlend;
                            baseColor.rgb = ApplyMapWater(baseColor.rgb, surfaceUv, _MapSizeMeters.xy,
                                revealMask, waterUv, insideWaterBounds, waterData, waterBlend);
                            rippleTransmission = 1.0 - waterBlend;
                        }
                        baseColor.rgb *= roomSurfaceVisibility;
                        rippleTransmission = lerp(1.0, rippleTransmission, roomSurfaceVisibility);
                    }
                }

                // This texture is the same normalized physical surface sampled by
                // movement and traversal UI.
                float normalizedHeight = saturate(tex2D(_HeightTex, contourUv).r);
                float heightMeters = lerp(_MinimumHeight, _MaximumHeight, normalizedHeight);

                float contourHeight = heightMeters;
                if (_SmoothContourEdges > 0.5)
                    contourHeight = SampleContourHeight(contourUv, normalizedHeight, 0.65);
                float blendAmount = saturate(HeightContourBlend(contourHeight, _SmoothContourEdges > 0.5) * _ContourColor.a)
                    * contourVisibility;
                if (_PhotoContoursOnly > .5)
                {
                    float brightness = blendAmount;
                    if (_WaterEnabled > .5)
                    {
                        float submerged = _DashSubmergedContours > .5
                            ? .25 * blendAmount * SubmergedContourDash(mapMeters, heightMeters) : 0;
                        brightness = DrawMapWater(brightness, submerged, contourUv, mapMeters,
                            _MapSizeMeters.xy, _PhotoWaterMetresPerPixel, _PhotoWaterMetresPerPixel,
                            0, 16, .03, 1, _PhotoBankOpacity);
                    }
                    return fixed4(brightness, brightness, brightness, 1);
                }
                if (_DashSubmergedContours > 0.5 && _WaterEnabled > 0.5)
                {
                    float submergedBlend = blendAmount * (1.0 - landVisibility)
                        * SubmergedContourDash(mapMeters, heightMeters) * 0.25;
                    // The submerged line tints the black backing; the ripple blend above it stays independent.
                    baseColor.rgb += _ContourColor.rgb * submergedBlend * rippleTransmission;
                }
                baseColor.rgb = lerp(baseColor.rgb, _ContourColor.rgb, blendAmount * landVisibility);

                // Dim the complete map, including water and contour lines,
                // outside the reveal circle without changing transparency.
                baseColor.rgb *= 1.0
                    - saturate(_OutsideRevealDarkening) * (1.0 - revealMask);
                return baseColor;
            }
            ENDCG
        }
    }
}

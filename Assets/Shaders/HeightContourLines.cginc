#ifndef ANIMAL_GAME_HEIGHT_CONTOUR_LINES
#define ANIMAL_GAME_HEIGHT_CONTOUR_LINES

// Shared screen-space contour width, coverage and elevation emphasis.
float HeightContourBlend(float heightMeters, bool smoothCoverage)
{
    float interval = max(0.0001, _ContourInterval);
    float contourCoordinate = (heightMeters - _MinimumHeight) / interval;
    float nearestContourDistance = abs(frac(contourCoordinate + 0.5) - 0.5);
    float contourHeight = round(contourCoordinate) * interval + _MinimumHeight;

    float visibleHeightRange = _VisibleMaximumHeight - _VisibleMinimumHeight;
    float visibleHeightProgress = visibleHeightRange > 0.0001
        ? saturate((contourHeight - _VisibleMinimumHeight) / visibleHeightRange)
        : 1.0;

    // The camera-visible lowest line is always 15% and thinnest;
    // the camera-visible highest line is always 100% and thickest.
    float lineWidth = lerp(
        _MinimumLineWidth,
        _MaximumLineWidth,
        visibleHeightProgress);
    float lineOpacity = lerp(
        _MinimumOpacity,
        _MaximumOpacity,
        visibleHeightProgress);
    float derivativeWidth = max(smoothCoverage
        ? length(float2(ddx(contourCoordinate), ddy(contourCoordinate)))
        : fwidth(contourCoordinate), 0.00001);
    float requestedHalfWidth = derivativeWidth * lineWidth * 0.5;
    float maximumHalfWidth = saturate(_MaximumCoverage) * 0.5;
    float widestRequestedHalfWidth =
        derivativeWidth * max(0.0001, _MaximumLineWidth) * 0.5;
    float sharedWidthScale = min(
        1.0,
        maximumHalfWidth / max(0.00001, widestRequestedHalfWidth));
    float halfWidth = requestedHalfWidth * sharedWidthScale;
    float antiAliasWidth = min(derivativeWidth * (smoothCoverage ? 0.65 : _EdgeSoftness), 0.035);
    float lineMask = 1.0 - smoothstep(max(0.0, halfWidth - (smoothCoverage ? antiAliasWidth * 0.5 : 0.0)),
        min(halfWidth + antiAliasWidth, 0.48), nearestContourDistance);

    return saturate(lineMask * lineOpacity);
}

float HeightContourBlend(float heightMeters)
{
    return HeightContourBlend(heightMeters, false);
}

// Submerged contours share the scene's world-anchored dash length and orientation.
float SubmergedContourDash(float2 mapMeters, float heightMeters)
{
    float2 mapDx = ddx(mapMeters);
    float2 mapDy = ddy(mapMeters);
    float2 tangent = mapDx * ddy(heightMeters) - mapDy * ddx(heightMeters);
    float2 weights = abs(tangent);
    float2 axis = weights.x >= weights.y ? float2(1, 0) : float2(0, 1);
    // Pick a fixed world axis near the tangent, so bending the line does not rotate its phase scale.
    const float diagonalUnit = 0.70710678;
    if ((weights.x + weights.y) * diagonalUnit > max(weights.x, weights.y))
        axis = float2(1, tangent.x * tangent.y >= 0 ? 1 : -1) * diagonalUnit;

    float projectedMeters = dot(mapMeters, axis);
    float distanceToDashCenter = abs(frac(projectedMeters / 8.0 + 0.5) - 0.5) * 8.0;
    float pixelMeters = clamp(abs(dot(mapDx, axis)) + abs(dot(mapDy, axis)), 0.001, 2.5);
    return saturate(0.5 + (2.5 - distanceToDashCenter) / pixelMeters);
}

#endif

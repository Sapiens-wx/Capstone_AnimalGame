#ifndef ANIMAL_GAME_HEIGHT_CONTOUR_LINES
#define ANIMAL_GAME_HEIGHT_CONTOUR_LINES

// Shared screen-space contour width, coverage and elevation emphasis.
float HeightContourBlend(float heightMeters)
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
    float derivativeWidth = max(fwidth(contourCoordinate), 0.00001);
    float requestedHalfWidth = derivativeWidth * lineWidth * 0.5;
    float maximumHalfWidth = saturate(_MaximumCoverage) * 0.5;
    float widestRequestedHalfWidth =
        derivativeWidth * max(0.0001, _MaximumLineWidth) * 0.5;
    float sharedWidthScale = min(
        1.0,
        maximumHalfWidth / max(0.00001, widestRequestedHalfWidth));
    float halfWidth = requestedHalfWidth * sharedWidthScale;
    float antiAliasWidth = min(derivativeWidth * _EdgeSoftness, 0.035);
    float lineMask = 1.0 - smoothstep(
        halfWidth,
        min(halfWidth + antiAliasWidth, 0.48),
        nearestContourDistance);

    return saturate(lineMask * lineOpacity);
}
#endif

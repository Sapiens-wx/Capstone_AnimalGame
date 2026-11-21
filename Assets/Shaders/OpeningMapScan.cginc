#ifndef ANIMAL_GAME_OPENING_MAP_SCAN
#define ANIMAL_GAME_OPENING_MAP_SCAN

// Each band stores left, right, top and slope in top-left screen coordinates.
float OpeningScanGap(float2 screenPoint, float4 band, float2 depths, float recovered)
{
    float position = (screenPoint.x - band.x) / (band.y - band.x);
    float top = band.z + band.w * position;
    float bottom = top + lerp(depths.x, depths.y, position);
    return recovered < 1 && position >= recovered && position <= 1
        && screenPoint.y >= top && screenPoint.y <= bottom ? 1 : 0;
}

void OpeningScanMisread(float2 screenPoint, float gap, float2 bounds, float2 offset,
    float time, float starts, inout float visibility, inout float2 readOffset)
{
    if (gap > .5 && time >= starts && screenPoint.x >= bounds.x && screenPoint.x <= bounds.y)
    {
        visibility = 1;
        readOffset = offset;
    }
}

float OpeningContourScan(float2 screenPoint, float time, out float2 readOffset)
{
    float upper = OpeningScanGap(screenPoint, float4(0, .89, .21, .033), float2(.084, .037), step(4.31, time));
    float recoveredMiddle = time >= 4.50 ? .76 : time >= 4.39 ? .39 : 0;
    float middle = OpeningScanGap(screenPoint, float4(.12, 1, .44, -.019), float2(.054, .125), recoveredMiddle);
    float lower = OpeningScanGap(screenPoint, float4(0, .93, .71, .019), float2(.071, .042), step(4.43, time));
    float visibility = 1 - max(upper, max(middle, lower));
    readOffset = 0;
    if (time < 4.18)
    {
        OpeningScanMisread(screenPoint, upper, float2(.21, .47), float2(.15, -.22), time, 3.96, visibility, readOffset);
        OpeningScanMisread(screenPoint, middle, float2(.28, .55), float2(-.36, .13), time, 3.98, visibility, readOffset);
        OpeningScanMisread(screenPoint, middle, float2(.56, .82), float2(.36, -.21), time, 4.04, visibility, readOffset);
        OpeningScanMisread(screenPoint, lower, float2(.61, .87), float2(-.12, .18), time, 4.07, visibility, readOffset);
    }
    return visibility;
}

float OpeningSurfaceScan(float2 screenPoint, float time, float dropTime, out float2 readOffset)
{
    float upper = OpeningScanGap(screenPoint, float4(.16, .86, .38, .013), float2(.058, .076), step(5.12, time));
    float lower = OpeningScanGap(screenPoint, float4(.26, .95, .615, -.023), float2(.083, .106), .54 * step(5.20, time));
    float visibility = 1 - max(upper, lower);
    readOffset = 0;
    if (time < dropTime)
    {
        OpeningScanMisread(screenPoint, upper, float2(.32, .61), float2(-.26, -.21), time, 4.93, visibility, readOffset);
        OpeningScanMisread(screenPoint, lower, float2(.48, .83), float2(.11, -.12), time, 4.98, visibility, readOffset);
    }
    return visibility;
}

float2 OpeningScanUv(float2 uv, float2 screenPoint, float2 readOffset, inout float visibility)
{
    // Translate a scan fragment using the real tile samples behind it. Leave
    // portions without source data black instead of repeating the tile edge.
    float2 readPoint = screenPoint - readOffset;
    float2 readUv = uv - ddx(uv) * (readOffset.x / ddx(screenPoint.x))
        - ddy(uv) * (readOffset.y / ddy(screenPoint.y));
    if (any(readOffset != 0) && (any(readPoint < 0) || any(readPoint > 1) || any(readUv < 0) || any(readUv > 1)))
        visibility = 0;
    return readUv;
}

#endif

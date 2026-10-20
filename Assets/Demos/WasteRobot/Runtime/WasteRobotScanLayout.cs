using UnityEngine;

namespace AnimalGame.Demos.WasteRobot
{
    /// <summary>
    /// Whole-vehicle convex envelope, measured on the shared 1779 x 884 canvas.
    /// Includes the wheels, both arms and rear bays, excluding transparent canvas
    /// corners. Arm-to-hull gaps are intentionally filled, as in static garbage.
    /// This is scan presentation geometry and never creates collision.
    /// </summary>
    public static class WasteRobotScanLayout
    {
        // Image pixels, origin top-left, Y down. Fresh arrays keep the authored
        // data immutable to callers without runtime image reads or mesh changes.
        public static Vector2[] CreatePixelHull() => new[]
        {
            new Vector2(8, 270), new Vector2(28, 228), new Vector2(160, 94),
            new Vector2(653, 4), new Vector2(1358, 4), new Vector2(1768, 209),
            new Vector2(1770, 718), new Vector2(1359, 881), new Vector2(654, 881),
            new Vector2(112, 856), new Vector2(45, 817), new Vector2(23, 772)
        };

        public static Vector2[] CreateNormalizedHull()
        {
            Vector2[] hull = CreatePixelHull();
            Vector2 size = WasteRobotCollisionLayout.SharedImageSize;
            for (int i = 0; i < hull.Length; i++)
                hull[i] = new Vector2(hull[i].x / size.x - .5f, .5f - hull[i].y / size.y);
            return hull;
        }
    }
}

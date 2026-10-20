using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Demos.WasteRobot
{
    public enum WasteRobotWallView
    {
        Exterior,
        Interior
    }

    /// <summary>
    /// Hand-authored solid geometry measured in PNG pixels, with the origin at the top left.
    /// A segment describes a rotated box from A to B: Thickness extends perpendicular to
    /// the segment, and its end faces stop exactly at A and B (there are no round caps).
    /// Circle shapes use A and B as the same centre. These shapes are not trigger geometry.
    /// </summary>
    public readonly struct WasteRobotWallShape
    {
        public readonly string Name;
        public readonly Vector2 A;
        public readonly Vector2 B;
        public readonly float Thickness;
        public readonly float Radius;

        public bool IsCircle => Radius > 0f;

        private WasteRobotWallShape(string name, Vector2 a, Vector2 b, float thickness, float radius)
        {
            Name = name;
            A = a;
            B = b;
            Thickness = thickness;
            Radius = radius;
        }

        public static WasteRobotWallShape Segment(string name, Vector2 a, Vector2 b, float thickness)
        {
            return new WasteRobotWallShape(name, a, b, thickness, 0f);
        }

        public static WasteRobotWallShape Circle(string name, Vector2 center, float radius)
        {
            return new WasteRobotWallShape(name, center, center, 0f, radius);
        }
    }

    /// <summary>
    /// Collision data for exterior-solar-v1.png and interior-aligned-v3.png.
    /// Both presentations share one canvas, entrance origin and pixel-to-world scale.
    /// The interior's right bay openings are y=276..405 and y=530..646.
    /// Both images are 1779 x 884. This file does not sample textures, create objects,
    /// register callbacks, change scenes, or modify any project or gameplay settings.
    /// The consumer should create non-grabbable, non-pushable WorldInteraction Collision
    /// geometry from these explicit boxes/circles, independently for each active view.
    /// </summary>
    public static class WasteRobotCollisionLayout
    {
        public static readonly Vector2 SharedImageSize = new Vector2(1779f, 884f);
        public static readonly Vector2 SharedEntranceCenter = new Vector2(991.5f, 205f);
        public const float SharedEntranceWidth = 289f;

        // Keep the original public aliases for existing demo callers and validators.
        public static readonly Vector2 ExteriorImageSize = SharedImageSize;
        public static readonly Vector2 InteriorImageSize = SharedImageSize;
        public static readonly Vector2 ExteriorEntranceCenter = SharedEntranceCenter;
        public static readonly Vector2 InteriorEntranceCenter = SharedEntranceCenter;
        public const float ExteriorEntranceWidth = SharedEntranceWidth;
        public const float InteriorEntranceWidth = SharedEntranceWidth;

        public static WasteRobotWallShape[] Create(WasteRobotWallView view)
        {
            return view == WasteRobotWallView.Interior ? CreateInterior() : CreateExterior();
        }

        private static WasteRobotWallShape S(string name, float ax, float ay, float bx, float by, float thickness)
        {
            return WasteRobotWallShape.Segment(name, new Vector2(ax, ay), new Vector2(bx, by), thickness);
        }

        private static WasteRobotWallShape C(string name, float x, float y, float radius)
        {
            return WasteRobotWallShape.Circle(name, new Vector2(x, y), radius);
        }

        private static WasteRobotWallShape[] CreateExterior()
        {
            return new[]
            {
                // Four solid tyres and their necks. Bevels are simplified by at most ~20 px.
                S("Exterior.Wheel.UpperLeft", 637, 98, 835, 98, 156),
                S("Exterior.Wheel.UpperLeftNeck", 683, 203, 783, 203, 54),
                S("Exterior.Wheel.UpperRight", 1176, 98, 1375, 98, 156),
                S("Exterior.Wheel.UpperRightNeck", 1224, 203, 1325, 203, 54),
                S("Exterior.Wheel.LowerLeft", 637, 791, 835, 791, 153),
                S("Exterior.Wheel.LowerLeftNeck", 683, 695, 783, 695, 40),
                S("Exterior.Wheel.LowerRight", 1176, 791, 1375, 791, 153),
                S("Exterior.Wheel.LowerRightNeck", 1224, 695, 1325, 695, 40),

                // The full dashed entrance x=847..1136 is intentionally absent from this list.
                // Its approach/crossing lane stays clear between y=132 and y=278.
                // At an entrance width of 2.4 world units, this covers local y=-0.6..0.6.
                S("Exterior.Hull.TopLeft", 630, 205, 847, 205, 12),
                S("Exterior.Hull.TopRight", 1136, 205, 1371, 205, 12),
                S("Exterior.Hull.TopRightBevel", 1371, 205, 1386, 222, 12),
                S("Exterior.Hull.Rear", 1386, 222, 1386, 684, 14),
                S("Exterior.Hull.BottomRightBevel", 1386, 684, 1375, 695, 14),
                S("Exterior.Hull.BottomRight", 1085, 695, 1375, 695, 14),
                S("Exterior.Hull.BottomMiddleRightBevel", 1065, 713, 1085, 695, 14),
                S("Exterior.Hull.BottomMiddle", 927, 713, 1065, 713, 14),
                S("Exterior.Hull.BottomMiddleLeftBevel", 905, 695, 927, 713, 14),
                S("Exterior.Hull.BottomLeft", 631, 695, 905, 695, 14),
                S("Exterior.Hull.LowerShoulder", 593, 674, 631, 695, 14),
                S("Exterior.Hull.LowerFrontBevel", 552, 621, 593, 674, 14),
                S("Exterior.Hull.Front", 550, 326, 550, 623, 20),
                S("Exterior.Hull.UpperFrontLedge", 524, 326, 550, 326, 14),
                S("Exterior.Hull.UpperFront", 524, 255, 524, 326, 14),
                S("Exterior.Hull.UpperShoulder", 596, 225, 630, 205, 14),
                S("Exterior.Hull.LowerMountLeft", 538, 648, 576, 682, 39),
                S("Exterior.Hull.LowerTabLeft", 852, 712, 898, 712, 30),
                S("Exterior.Hull.LowerTabRight", 1093, 725, 1144, 725, 53),

                // Collector casing and three solid rollers; the mouth is not an entrance.
                C("Exterior.Collector.UpperHinge", 242, 253, 13),
                C("Exterior.Collector.LowerHinge", 241, 668, 13),
                S("Exterior.Collector.FrontRim", 241, 253, 241, 668, 12),
                S("Exterior.Collector.UpperOuterBlade", 244, 251, 542, 330, 14),
                S("Exterior.Collector.UpperInnerBlade", 244, 266, 540, 419, 12),
                S("Exterior.Collector.UpperBladeEnd", 540, 328, 540, 420, 14),
                S("Exterior.Collector.LowerOuterBlade", 244, 668, 540, 615, 14),
                S("Exterior.Collector.LowerInnerBlade", 244, 653, 540, 551, 12),
                S("Exterior.Collector.LowerBladeEnd", 540, 551, 540, 617, 14),
                S("Exterior.Collector.RollerLeft", 336, 335, 336, 606, 33),
                S("Exterior.Collector.RollerMiddle", 401, 371, 401, 581, 33),
                S("Exterior.Collector.RollerRight", 467, 404, 467, 558, 33),

                // Complete upper arm: broad beams and joints, with an open claw gap.
                C("Exterior.UpperArm.Root", 562, 229, 34),
                S("Exterior.UpperArm.UpperBeam", 548, 207, 452, 113, 48),
                C("Exterior.UpperArm.Elbow", 435, 101, 35),
                S("Exterior.UpperArm.MiddleBeam", 414, 106, 214, 141, 55),
                C("Exterior.UpperArm.WristJoint", 198, 142, 32),
                S("Exterior.UpperArm.WristBeam", 181, 159, 109, 237, 43),
                S("Exterior.UpperArm.Palm", 88, 213, 104, 250, 34),
                S("Exterior.UpperArm.ClawLeftRoot", 77, 236, 43, 248, 20),
                S("Exterior.UpperArm.ClawLeftMiddle", 43, 248, 27, 278, 19),
                S("Exterior.UpperArm.ClawLeftTip", 27, 278, 48, 320, 17),
                S("Exterior.UpperArm.ClawRightRoot", 110, 250, 139, 277, 20),
                S("Exterior.UpperArm.ClawRightMiddle", 139, 277, 134, 314, 20),
                S("Exterior.UpperArm.ClawRightTip", 134, 314, 106, 340, 17),

                // Detached lower arm: there is deliberately NO segment to the hull mount.
                S("Exterior.LowerArm.BrokenBeam", 350, 799, 444, 747, 48),
                C("Exterior.LowerArm.Elbow", 336, 803, 35),
                S("Exterior.LowerArm.MiddleBeam", 315, 788, 184, 710, 48),
                C("Exterior.LowerArm.WristJoint", 168, 705, 32),
                S("Exterior.LowerArm.WristBeam", 147, 718, 96, 751, 40),
                C("Exterior.LowerArm.Palm", 92, 752, 22),
                S("Exterior.LowerArm.ClawLeftRoot", 80, 731, 52, 747, 20),
                S("Exterior.LowerArm.ClawLeftMiddle", 52, 747, 39, 777, 20),
                S("Exterior.LowerArm.ClawLeftTip", 39, 777, 60, 809, 17),
                S("Exterior.LowerArm.ClawRightRoot", 121, 747, 158, 769, 20),
                S("Exterior.LowerArm.ClawRightMiddle", 158, 769, 151, 804, 20),
                S("Exterior.LowerArm.ClawRightTip", 151, 804, 122, 836, 17),

                // Closed external storage roofs/solar arrays are solids, not hollow rooms.
                S("Exterior.Storage.UpperRoof", 1405, 337, 1750, 337, 240),
                S("Exterior.Storage.LowerRoof", 1405, 604, 1750, 604, 216),
                S("Exterior.Storage.MiddleConnector", 1384, 476, 1738, 476, 38),
                S("Exterior.Storage.UpperSideConnector", 1374, 330, 1405, 330, 48),
                S("Exterior.Storage.LowerSideConnector", 1375, 577, 1405, 577, 36),

                // Roof equipment is genuinely solid but is beyond the entry-switch lane.
                C("Exterior.Compression.Housing", 966, 483, 180),
                S("Exterior.Compression.TopHood", 917, 310, 1057, 310, 58),
                S("Exterior.Compression.RightHood", 1063, 335, 1080, 407, 43),
                S("Exterior.RoofEquipment.RightCabinet", 1204, 519, 1329, 519, 245)
            };
        }

        private static WasteRobotWallShape[] CreateInterior()
        {
            var walls = new List<WasteRobotWallShape>();
            foreach (WasteRobotWallShape shape in CreateExterior())
            {
                // Reuse the exact original shape values and stable names. These exposed
                // wheels, arms, collector rims and hull edges never change position.
                if (IsSharedInteriorSolid(shape.Name)) walls.Add(shape);
            }

            walls.AddRange(new[]
            {
                // The supplied interior still has both continuous collector rear walls.
                // Shared Hull.Front supplies x=550; this supplies the second x=539 wall.
                S("Interior.Collector.RearWall", 539, 328, 539, 617, 8),
                S("Interior.FrontFrame.UpperHorizontal", 524, 287, 641, 287, 8),
                S("Interior.FrontFrame.UpperRear", 638, 287, 638, 379, 8),
                S("Interior.FrontFrame.UpperLowerRim", 555, 379, 638, 379, 8),
                S("Interior.FrontFrame.LowerUpperRim", 555, 578, 638, 578, 8),
                S("Interior.FrontFrame.LowerRear", 638, 578, 638, 650, 8),
                S("Interior.FrontFrame.LowerRim", 615, 650, 638, 650, 8),
                S("Interior.FrontFrame.SmallMount", 555, 481, 574, 481, 52),

                // Split the old solid rear wall only at the two actual doorways.
                // Frame leaves stop at flat end faces; do not add round end caps.
                S("Interior.Rear.UpperPost", 1386, 222, 1386, 276, 14),
                S("Interior.Rear.MiddlePost", 1386, 405, 1386, 455, 14),
                S("Interior.Rear.LowerPost", 1386, 646, 1386, 684, 14),
                S("Interior.Rear.CentralMount", 1365, 478, 1408, 478, 46),
                S("Interior.Rear.BaySeparator", 1407, 476, 1734, 476, 38),

                // Upper bay outer frame: clear through y=276..405 across every leaf.
                S("Interior.UpperBay.Top", 1405, 217, 1750, 217, 10),
                S("Interior.UpperBay.Right", 1750, 217, 1750, 457, 10),
                S("Interior.UpperBay.Bottom", 1405, 457, 1750, 457, 10),
                S("Interior.UpperBay.LeftUpper", 1403, 217, 1403, 276, 8),
                S("Interior.UpperBay.LeftLower", 1403, 405, 1403, 457, 8),
                S("Interior.UpperBay.MiddleTop", 1447, 233, 1735, 233, 6),
                S("Interior.UpperBay.MiddleRight", 1735, 233, 1735, 439, 6),
                S("Interior.UpperBay.MiddleBottom", 1447, 439, 1735, 439, 6),
                S("Interior.UpperBay.TopLeftBevel", 1433, 217, 1447, 233, 6),
                S("Interior.UpperBay.TopRightBevel", 1735, 233, 1750, 217, 6),
                S("Interior.UpperBay.BottomRightBevel", 1735, 439, 1750, 457, 6),
                S("Interior.UpperBay.MiddleLeftUpperStub", 1447, 233, 1447, 252, 6),
                S("Interior.UpperBay.MiddleLeftLowerStub", 1447, 423, 1447, 439, 6),
                S("Interior.UpperBay.InnerTop", 1459, 247, 1722, 247, 4),
                S("Interior.UpperBay.InnerRight", 1722, 247, 1722, 424, 4),
                S("Interior.UpperBay.InnerBottom", 1459, 424, 1722, 424, 4),
                S("Interior.UpperBay.InnerTopLeftHook", 1447, 249, 1459, 247, 4),
                S("Interior.UpperBay.InnerBottomLeftHook", 1447, 424, 1459, 424, 4),

                // Lower bay outer frame: the small upper left post really ends at y=503.
                // Its inner upper rim ends at y=529, leaving all of y=530..646 clear.
                S("Interior.LowerBay.Top", 1405, 495, 1750, 495, 10),
                S("Interior.LowerBay.Right", 1750, 495, 1750, 712, 10),
                S("Interior.LowerBay.Bottom", 1405, 712, 1750, 712, 10),
                S("Interior.LowerBay.LeftUpper", 1403, 495, 1403, 503, 8),
                S("Interior.LowerBay.LeftLower", 1403, 646, 1403, 712, 8),
                S("Interior.LowerBay.MiddleTop", 1447, 514, 1735, 514, 6),
                S("Interior.LowerBay.MiddleRight", 1735, 514, 1735, 697, 6),
                S("Interior.LowerBay.MiddleBottom", 1447, 711, 1735, 711, 6),
                S("Interior.LowerBay.TopLeftBevel", 1433, 495, 1447, 511, 6),
                S("Interior.LowerBay.TopRightBevel", 1735, 514, 1750, 495, 6),
                S("Interior.LowerBay.BottomLeftBevel", 1435, 712, 1447, 699, 6),
                S("Interior.LowerBay.BottomRightBevel", 1735, 697, 1750, 712, 6),
                S("Interior.LowerBay.MiddleLeftUpperStub", 1447, 511, 1447, 527, 4),
                S("Interior.LowerBay.MiddleLeftLowerStub", 1447, 679, 1447, 702, 6),
                S("Interior.LowerBay.InnerTop", 1460, 527, 1722, 527, 4),
                S("Interior.LowerBay.InnerRight", 1722, 529, 1722, 695, 4),
                S("Interior.LowerBay.InnerBottom", 1460, 695, 1722, 695, 4),
                S("Interior.LowerBay.InnerTopLeftHook", 1447, 525, 1460, 527, 4),
                S("Interior.LowerBay.InnerBottomLeftHook", 1447, 681, 1460, 695, 4)
            });
            return walls.ToArray();
        }

        private static bool IsSharedInteriorSolid(string name)
        {
            return name.StartsWith("Exterior.Wheel.", System.StringComparison.Ordinal)
                || name.StartsWith("Exterior.UpperArm.", System.StringComparison.Ordinal)
                || name.StartsWith("Exterior.LowerArm.", System.StringComparison.Ordinal)
                || (name.StartsWith("Exterior.Hull.", System.StringComparison.Ordinal)
                    && name != "Exterior.Hull.Rear")
                || (name.StartsWith("Exterior.Collector.", System.StringComparison.Ordinal)
                    && !name.StartsWith("Exterior.Collector.Roller", System.StringComparison.Ordinal));
        }
    }
}

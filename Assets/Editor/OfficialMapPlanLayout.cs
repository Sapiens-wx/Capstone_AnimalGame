using System;
using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Editor
{
    /// <summary>Five adjoining planning regions traced from the concept sketch; no Terrain is created.</summary>
    public static class OfficialMapPlanLayout
    {
        private const double SourceMinX = 157.0;
        private const double SourceMaxX = 1395.0;
        private const double SourceMinY = 240.0;
        private const double SourceMaxY = 943.0;
        private const double MinimumMarginMeters = 100.0;
        private const double TileSizeMeters = 250.0;

        public static OfficialMapRegion[] Create(out Vector2 worldSize, out Vector2 referenceOrigin)
        {
            // Every shared border is defined once, then reused in the opposite direction.
            Vector2[] outerMountains = Chain(
                323, 398, 375, 355, 450, 309, 516, 282, 565, 267, 590, 255,
                621, 260, 672, 243, 703, 240, 732, 250, 752, 273);
            Vector2[] outerForest = Chain(
                752, 273, 800, 267, 853, 269, 915, 266, 958, 275, 1018, 279,
                1075, 291, 1114, 308, 1144, 337, 1158, 378, 1168, 431, 1170, 472, 1158, 510);
            Vector2[] outerWetland = Chain(
                1158, 510, 1231, 535, 1300, 563, 1367, 588, 1384, 612, 1395, 665,
                1390, 725, 1375, 786, 1355, 838, 1326, 869, 1275, 889, 1210, 909,
                1149, 923, 1097, 943, 1072, 938, 1044, 925, 1016, 917, 980, 911, 939, 900);
            Vector2[] outerPlains = Chain(
                939, 900, 910, 915, 860, 921, 810, 919, 757, 919, 711, 913, 681, 903);
            Vector2[] outerDesert = Chain(
                681, 903, 653, 913, 622, 910, 593, 914, 565, 910, 535, 900,
                485, 891, 449, 893, 411, 875, 377, 853, 333, 845, 287, 842,
                251, 844, 214, 831, 188, 812, 178, 789, 176, 747, 171, 723,
                158, 690, 157, 657, 160, 618, 168, 563, 172, 510, 180, 468,
                193, 441, 219, 420, 247, 407, 280, 397, 323, 398);

            Vector2[] mountainsForest = Chain(
                752, 273, 760, 292, 758, 318, 751, 334, 718, 343, 700, 352, 692, 365);
            Vector2[] mountainsPlains = Chain(
                692, 365, 696, 405, 690, 447, 670, 472, 639, 508, 611, 551,
                593, 592, 588, 624, 593, 664, 623, 685, 651, 710, 680, 746);
            Vector2[] mountainsDesert = Chain(
                323, 398, 320, 418, 333, 448, 349, 475, 353, 516, 361, 548,
                383, 573, 404, 606, 427, 639, 452, 668, 479, 684, 514, 688,
                552, 698, 584, 704, 625, 718, 651, 734, 680, 746);
            Vector2[] forestPlains = Chain(
                692, 365, 710, 355, 740, 347, 780, 349, 823, 359, 865, 390,
                905, 431, 946, 442, 975, 465, 991, 492, 995, 518, 986, 557,
                950, 605, 912, 644, 934, 710, 944, 753, 947, 795);
            Vector2[] forestWetland = Chain(
                947, 795, 987, 768, 1023, 728, 1054, 685, 1067, 641, 1095, 598, 1136, 554, 1158, 510);
            Vector2[] plainsWetland = Chain(947, 795, 955, 854, 953, 880, 939, 900);
            Vector2[] desertPlains = Chain(680, 746, 696, 770, 707, 802, 716, 831, 706, 860, 687, 886, 681, 903);

            Vector2[] plains = Polygon(forestPlains, plainsWetland, outerPlains,
                Reverse(desertPlains), Reverse(mountainsPlains));
            double scale = Math.Sqrt(OfficialMapPlan.TargetPlainsAreaSquareMeters / OfficialMapPlan.PolygonArea(plains));
            double width = (SourceMaxX - SourceMinX) * scale;
            double height = (SourceMaxY - SourceMinY) * scale;
            worldSize = new Vector2(
                (float)(Math.Ceiling((width + MinimumMarginMeters * 2) / TileSizeMeters) * TileSizeMeters),
                (float)(Math.Ceiling((height + MinimumMarginMeters * 2) / TileSizeMeters) * TileSizeMeters));
            double offsetX = (worldSize.x - width) * 0.5;
            double offsetZ = (worldSize.y - height) * 0.5;
            referenceOrigin = new Vector2(-350f, 200f);

            OfficialMapRegion Region(string id, string name, Color color, Vector2[] polygon,
                Vector2 label, Vector2 elevation)
            {
                Vector2 Map(Vector2 point) => new Vector2(
                    (float)((point.x - SourceMinX) * scale + offsetX),
                    (float)((SourceMaxY - point.y) * scale + offsetZ));
                var points = new Vector2[polygon.Length];
                for (int i = 0; i < points.Length; i++)
                    points[i] = Map(polygon[i]);
                return new OfficialMapRegion
                {
                    Id = id,
                    DisplayName = name,
                    Color = color,
                    PointsMeters = points,
                    LabelPositionMeters = Map(label),
                    ElevationRangeMeters = elevation
                };
            }

            return new[]
            {
                Region("plains", "大平原", new Color(0.59f, 0.68f, 0.38f, 1f), plains,
                    new Vector2(760, 630), new Vector2(60, 100)),
                Region("mountains", "落基山脉", new Color(0.51f, 0.55f, 0.57f, 1f),
                    Polygon(outerMountains, mountainsForest, mountainsPlains, Reverse(mountainsDesert)),
                    new Vector2(510, 480), new Vector2(100, 300)),
                Region("forest", "落叶林", new Color(0.33f, 0.51f, 0.37f, 1f),
                    Polygon(outerForest, Reverse(forestWetland), Reverse(forestPlains), Reverse(mountainsForest)),
                    new Vector2(1000, 405), new Vector2(70, 150)),
                Region("desert", "沙漠", new Color(0.76f, 0.65f, 0.42f, 1f),
                    Polygon(mountainsDesert, desertPlains, outerDesert),
                    new Vector2(310, 750), new Vector2(45, 100)),
                Region("wetland", "沼泽", new Color(0.34f, 0.57f, 0.59f, 1f),
                    Polygon(outerWetland, Reverse(plainsWetland), forestWetland),
                    new Vector2(1165, 795), new Vector2(30, 55))
            };
        }

        private static Vector2[] Chain(params float[] coordinates)
        {
            var points = new Vector2[coordinates.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2(coordinates[i * 2], coordinates[i * 2 + 1]);
            return points;
        }

        private static Vector2[] Reverse(Vector2[] chain)
        {
            var result = (Vector2[])chain.Clone();
            Array.Reverse(result);
            return result;
        }

        private static Vector2[] Polygon(params Vector2[][] chains)
        {
            var points = new List<Vector2>();
            foreach (Vector2[] chain in chains)
            {
                if (points.Count > 0 && points[points.Count - 1] != chain[0])
                    throw new InvalidOperationException("Official map planning boundary chains do not meet.");
                for (int i = points.Count == 0 ? 0 : 1; i < chain.Length; i++)
                    points.Add(chain[i]);
            }
            if (points[0] != points[points.Count - 1])
                throw new InvalidOperationException("Official map planning polygon is not closed.");
            points.RemoveAt(points.Count - 1);
            return points.ToArray();
        }
    }
}

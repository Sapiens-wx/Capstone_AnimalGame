using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    public static class OfficialMap01PlanBuilder
    {
        public const string ScenePath = "Assets/Scenes/GameMaps/Official_Map_01.unity";
        public const string RootName = "Official Map 01 - Region Plan";
        private const string ReportDirectory = "Temp/OfficialMap01";

        [MenuItem("Animal Game/Official Map 01/Create or Show Region Plan")]
        public static void CreateOrShow()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before editing the region plan.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == ScenePath, "Open Assets/Scenes/GameMaps/Official_Map_01.unity first. Other scenes are not switched or saved automatically.");
            OfficialMapPlan[] existing = Components<OfficialMapPlan>(scene);
            Require(existing.Length <= 1, "The official map has more than one planning component.");
            OfficialMapPlan plan = existing.SingleOrDefault();
            if (plan == null)
            {
                OfficialMapRegion[] regions = OfficialMapPlanLayout.Create(out Vector2 size, out Vector2 references);
                var root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "Create official map region plan");
                plan = Undo.AddComponent<OfficialMapPlan>(root);
                plan.Configure(size, 250f, 350f, references, regions);
                EditorUtility.SetDirty(plan);
                Validate(plan);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene), "Could not save Official_Map_01.");
            }
            // Existing edited outlines are never replaced by the initial sketch layout.
            Validate(plan);
            Selection.activeGameObject = plan.gameObject;
            OfficialMapPlanSceneDrawer.FramePlan(plan);
        }

        [MenuItem("Animal Game/Official Map 01/Frame Region Plan")]
        private static void FramePlan() => OfficialMapPlanSceneDrawer.FramePlan(FindPlan());

        [MenuItem("Animal Game/Official Map 01/Frame Height References")]
        private static void FrameHeights() => OfficialMapPlanSceneDrawer.FrameHeightReferences(FindPlan());

        [MenuItem("Animal Game/Official Map 01/Frame Area References")]
        private static void FrameAreas() => OfficialMapPlanSceneDrawer.FrameAreaReferences(FindPlan());

        [MenuItem("Animal Game/Official Map 01/Frame Human Scale")]
        private static void FrameHuman() => OfficialMapPlanSceneDrawer.FrameHumanScale(FindPlan());

        [MenuItem("Animal Game/Official Map 01/Validate Region Plan")]
        public static void ValidateMenu() => Validate(FindPlan());

        public static void Validate(OfficialMapPlan plan)
        {
            Require(plan != null && plan.gameObject.scene.path == ScenePath, "Select the official map's planning component.");
            Require(plan.HasMetricTransform(out string transformIssue), transformIssue);
            Require(plan.WorldSizeMeters.x > 0 && plan.WorldSizeMeters.y > 0 && plan.TileSizeMeters == 250f,
                "Use a positive world size and the 250 m planning grid.");
            Require(Mathf.Abs(plan.WorldSizeMeters.x % 250f) < 0.001f
                && Mathf.Abs(plan.WorldSizeMeters.y % 250f) < 0.001f, "The envelope must fit the 250 m grid.");
            Require(plan.MaximumReferenceHeightMeters == 350f, "Keep the 350 m height-difference ruler.");
            Require(IsFinite(plan.ReferenceBaseHeightMeters), "The reference base height must be finite.");
            Require(plan.Regions.Count == 5 && plan.Regions.Select(region => region.Id).Distinct().Count() == 5,
                "Expected the five different planning regions.");
            var edges = new Dictionary<string, List<Vector2[]>>();
            double totalArea = 0;
            foreach (OfficialMapRegion region in plan.Regions)
            {
                Require(region != null && region.PointsMeters != null && region.PointsMeters.Length >= 3,
                    "A region has no closed polygon.");
                Vector2[] points = region.PointsMeters;
                Require(region.AreaSquareMeters > 1, "A region has zero area: " + region.DisplayName);
                Require(Contains(points, region.LabelPositionMeters), "A label is outside its region: " + region.DisplayName);
                List<int> triangles = OfficialMapPlanSceneDrawer.Triangulate(points);
                double filledArea = 0d;
                for (int index = 0; index + 2 < triangles.Count; index += 3)
                    filledArea += Math.Abs(Cross(points[triangles[index]], points[triangles[index + 1]], points[triangles[index + 2]])) * 0.5d;
                Require(triangles.Count > 0 && triangles.Count % 3 == 0 && Math.Abs(filledArea - region.AreaSquareMeters) < 1d,
                    "The concave region fill does not match its outline: " + region.DisplayName);
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 a = points[i], b = points[(i + 1) % points.Length];
                    Require(IsFinite(a.x) && IsFinite(a.y) && a.x >= 0 && a.y >= 0
                        && a.x <= plan.WorldSizeMeters.x && a.y <= plan.WorldSizeMeters.y,
                        "A boundary lies outside the envelope: " + region.DisplayName);
                    Require((a - b).sqrMagnitude > 0.000001f, "A boundary has a repeated point.");
                    string ka = PointKey(a), kb = PointKey(b);
                    string key = string.CompareOrdinal(ka, kb) < 0 ? ka + "|" + kb : kb + "|" + ka;
                    if (!edges.TryGetValue(key, out List<Vector2[]> owners)) edges.Add(key, owners = new List<Vector2[]>());
                    owners.Add(new[] { a, b });
                    for (int j = i + 1; j < points.Length; j++)
                        Require(!ProperlyCross(a, b, points[j], points[(j + 1) % points.Length]),
                            "A boundary intersects itself: " + region.DisplayName);
                }
                totalArea += region.AreaSquareMeters;
            }
            for (int i = 0; i < plan.Regions.Count; i++)
                for (int j = i + 1; j < plan.Regions.Count; j++)
                {
                    OfficialMapRegion a = plan.Regions[i], b = plan.Regions[j];
                    Require(!Contains(a.PointsMeters, b.LabelPositionMeters) && !Contains(b.PointsMeters, a.LabelPositionMeters),
                        "Region interiors overlap.");
                    for (int p = 0; p < a.PointsMeters.Length; p++)
                        for (int q = 0; q < b.PointsMeters.Length; q++)
                            Require(!ProperlyCross(a.PointsMeters[p], a.PointsMeters[(p + 1) % a.PointsMeters.Length],
                                b.PointsMeters[q], b.PointsMeters[(q + 1) % b.PointsMeters.Length]), "Region boundaries cross.");
                }
            Require(edges.Values.All(owners => owners.Count <= 2), "More than two regions share a boundary segment.");
            var outsideEdges = edges.Values.Where(owners => owners.Count == 1).Select(owners => owners[0]).ToArray();
            var perimeter = TracePerimeter(outsideEdges);
            Require(Math.Abs(OfficialMapPlan.PolygonArea(perimeter) - totalArea) < 1d,
                "The five regions do not exactly fill their shared outer outline.");
            OfficialMapRegion plains = plan.FindRegion("plains");
            Require(plains != null && Math.Abs(plains.AreaSquareMeters - OfficialMapPlan.TargetPlainsAreaSquareMeters) < 1d,
                "The plains must be calibrated to 1,562,500 m2 within 1 m2.");
            int authoringTerrainCount = Components<Terrain>(plan.gameObject.scene).Length;

            var report = new StringBuilder("PASS: Official_Map_01 region planning\n");
            report.AppendLine($"Envelope: {plan.WorldSizeMeters.x:F0} x {plan.WorldSizeMeters.y:F0} m; grid: 250 m.");
            foreach (OfficialMapRegion region in plan.Regions)
                report.AppendLine($"{region.Id} / {region.DisplayName}: {region.AreaSquareMeters:F3} m2; "
                    + $"{region.AreaSquareKilometers:F9} km2; equivalent {region.TileEquivalent(250):F6} V1 tiles.");
            report.AppendLine($"Plains calibration error: {plains.AreaSquareMeters - OfficialMapPlan.TargetPlainsAreaSquareMeters:F6} m2.");
            report.AppendLine($"Total outlined area: {totalArea / 1000000d:F9} km2; one closed perimeter; "
                + $"{edges.Values.Count(owners => owners.Count == 2)} exact shared edges; no crossings or gaps.");
            report.AppendLine($"Height-difference references: 350 m and 70 m; bottom world Y={plan.transform.position.y + plan.ReferenceBaseHeightMeters:F4} m. "
                + "Also 1 m cube / 2 m figure, 250 m square and 1250 m square.");
            report.AppendLine("Plan lies at Y=0; region elevation bands are suggestions, not generated or measured terrain.");
            report.AppendLine($"Authoring Terrain objects currently in this scene: {authoringTerrainCount}. Region areas are independent of their rectangular coverage.");
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(ReportDirectory + "/Validation.txt", report.ToString());
            File.WriteAllText(ReportDirectory + "/Plan.json", JsonUtility.ToJson(new PlanDocument
                { worldSizeMeters = plan.WorldSizeMeters, regions = plan.Regions.ToArray() }, true));
            Debug.Log(report.ToString(), plan);
        }

        public static OfficialMapPlan FindPlan()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == ScenePath, "Open Official_Map_01 first.");
            OfficialMapPlan[] plans = Components<OfficialMapPlan>(scene);
            Require(plans.Length == 1, "Create the region plan first.");
            return plans[0];
        }

        private static Vector2[] TracePerimeter(Vector2[][] edges)
        {
            Require(edges.Length > 2, "No outer perimeter.");
            var remaining = new List<Vector2[]>(edges);
            var points = new List<Vector2> { remaining[0][0] };
            Vector2 next = remaining[0][1];
            remaining.RemoveAt(0);
            while (next != points[0])
            {
                points.Add(next);
                int index = remaining.FindIndex(edge => edge[0] == next || edge[1] == next);
                Require(index >= 0, "The outer perimeter is open.");
                Vector2[] edge = remaining[index];
                next = edge[0] == next ? edge[1] : edge[0];
                remaining.RemoveAt(index);
                Require(points.Count <= edges.Length, "The outer perimeter does not close.");
            }
            Require(remaining.Count == 0, "There are holes or separate islands in the planning outline.");
            return points.ToArray();
        }

        private static bool Contains(Vector2[] points, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                if ((points[i].y > point.y) != (points[j].y > point.y)
                    && point.x < (double)(points[j].x - points[i].x) * (point.y - points[i].y)
                    / (points[j].y - points[i].y) + points[i].x) inside = !inside;
            return inside;
        }

        private static double Cross(Vector2 a, Vector2 b, Vector2 c) =>
            ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);

        private static bool ProperlyCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
            Cross(a, b, c) * Cross(a, b, d) < -0.000001d && Cross(c, d, a) * Cross(c, d, b) < -0.000001d;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string PointKey(Vector2 point) => point.x.ToString("R", CultureInfo.InvariantCulture)
            + "," + point.y.ToString("R", CultureInfo.InvariantCulture);
        private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable]
        private sealed class PlanDocument
        {
            public Vector2 worldSizeMeters;
            public OfficialMapRegion[] regions;
        }
    }
}

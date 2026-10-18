using System;
using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.Editor
{
    [CustomEditor(typeof(OfficialMapPlan))]
    public sealed class OfficialMapPlanEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var plan = (OfficialMapPlan)target;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("只显示高度参照 / Heights Only"))
                    OfficialMapPlanSceneDrawer.ShowOnlyHeightReferences(plan);
                bool canAlign = OfficialMapPlanHeightAlignment.TrySamplePlainsGround(plan, out float groundHeight, out string alignmentIssue);
                using (new EditorGUI.DisabledScope(!canAlign))
                    if (GUILayout.Button("高度尺底端对齐草原地面 / Align To Plains"))
                        OfficialMapPlanHeightAlignment.AlignToPlains(plan);
                if (!canAlign) EditorGUILayout.HelpBox(alignmentIssue, MessageType.Info);
                else EditorGUILayout.LabelField("草原参照点地面 Y", $"{plan.transform.position.y + groundHeight:F3} m");
            }
            EditorGUILayout.Space();
            if (DrawDefaultInspector()) SceneView.RepaintAll();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "区域规划与尺寸参照，仅在 Scene 视图的编辑模式显示，独立于 Gizmos 开关。"
                + "Show Plan 只控制区域边界、填色和规划网格；高度与面积参照可独立显示。"
                + "高度尺刻度表示相对底端的高度差，底端世界 Y 单独标出；对齐不会移动地形。"
                + "1 Unity 单位 = 1 米，面积为俯视投影面积。",
                MessageType.Info);

            if (!plan.HasMetricTransform(out string transformIssue))
                EditorGUILayout.HelpBox(transformIssue, MessageType.Error);

            DrawStatistics(plan);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("区域规划总览 / Frame Plan"))
                    OfficialMapPlanSceneDrawer.FramePlan(plan);
                if (GUILayout.Button("70 m 与 350 m 高度参照 / Frame Heights"))
                    OfficialMapPlanSceneDrawer.FrameHeightReferences(plan);
                if (GUILayout.Button("250 m 与 1500 m 面积参照 / Frame Areas"))
                    OfficialMapPlanSceneDrawer.FrameAreaReferences(plan);
                if (GUILayout.Button("1 m 方块与 2 m 人形 / Frame Human Scale"))
                    OfficialMapPlanSceneDrawer.FrameHumanScale(plan);
            }
        }

        private static void DrawStatistics(OfficialMapPlan plan)
        {
            Vector2 size = plan.WorldSizeMeters;
            EditorGUILayout.LabelField("制作包络 / Envelope", $"{size.x:N0} × {size.y:N0} m");
            EditorGUILayout.LabelField("包络面积 / Envelope Area", $"{(double)size.x * size.y / 1000000d:F3} km²");
            EditorGUILayout.LabelField("标准区块 / Tile", $"{plan.TileSizeMeters:N0} × {plan.TileSizeMeters:N0} m");
            EditorGUILayout.LabelField("高度差尺 / Height Ruler", $"0–{plan.MaximumReferenceHeightMeters:N0} m");
            EditorGUILayout.LabelField("高度尺底端 / Base World Y", $"{plan.transform.position.y + plan.ReferenceBaseHeightMeters:F3} m");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("区域俯视面积 / Region Footprints", EditorStyles.boldLabel);
            OfficialMapRegion plains = null;
            double totalArea = 0d;
            foreach (OfficialMapRegion region in plan.Regions)
            {
                if (region == null) continue;
                double area = region.AreaSquareMeters;
                totalArea += area;
                EditorGUILayout.LabelField(region.DisplayName,
                    $"{area / 1000000d:F3} km² · {TileEquivalent(plan, area):F2} 块");
                using (new EditorGUI.IndentLevelScope())
                    EditorGUILayout.LabelField("建议海拔 / Reference Only",
                        $"{region.ElevationRangeMeters.x:N0}–{region.ElevationRangeMeters.y:N0} m");

                if ((!string.IsNullOrEmpty(region.Id)
                     && region.Id.IndexOf("plain", StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(region.DisplayName)
                        && (region.DisplayName.Contains("草原") || region.DisplayName.Contains("大平原"))))
                    plains = region;
            }
            EditorGUILayout.LabelField("区域面积合计 / Sum", $"{totalArea / 1000000d:F3} km²");
            if (plains != null)
            {
                double difference = plains.AreaSquareMeters - OfficialMapPlan.TargetPlainsAreaSquareMeters;
                double relativeError = difference / OfficialMapPlan.TargetPlainsAreaSquareMeters * 100d;
                EditorGUILayout.HelpBox(
                    $"草原目标：2.250 km² = 36 个 250 m 标准区块\n"
                    + $"当前：{plains.AreaSquareMeters:N2} m²，偏差 {difference:+0.00;-0.00;0.00} m² ({relativeError:+0.0000;-0.0000;0.0000}%)。"
                    + "\n1500 × 1500 m 方框只用于等面积比较；草原保留不规则轮廓。",
                    Math.Abs(relativeError) <= 0.1d ? MessageType.Info : MessageType.Warning);
            }
        }

        private static double TileEquivalent(OfficialMapPlan plan, double area)
        {
            double tile = plan.TileSizeMeters;
            return tile > 0d ? area / (tile * tile) : 0d;
        }
    }

    /// <summary>
    /// Editor-only drawing. No runtime geometry, renderer, Terrain, material, or global rendering
    /// settings are created. References remain visible when Scene view Gizmos are switched off.
    /// </summary>
    [InitializeOnLoad]
    public static class OfficialMapPlanSceneDrawer
    {
        private static readonly Color DimensionColor = new Color(0.82f, 0.91f, 1f, 1f);
        private static readonly Color HeightColor = new Color(0.35f, 0.92f, 1f, 1f);
        private static readonly Color V1Color = new Color(1f, 0.83f, 0.28f, 1f);
        private static GUIStyle labelStyle;
        private static GUIStyle titleStyle;
        private static GUIStyle smallStyle;

        static OfficialMapPlanSceneDrawer()
        {
            SceneView.duringSceneGui += DrawScene;
        }

        public static void FramePlan(OfficialMapPlan plan)
        {
            if (!CanFrame(plan)) return;
            EnableDisplay(plan, "showPlan");
            Vector2 size = plan.WorldSizeMeters;
            Vector2 center = new Vector2(size.x * 0.5f, size.y * 0.5f + 90f);
            SceneView view = GetView(plan);
            view.in2DMode = false;
            float aspect = Mathf.Max(0.5f, view.position.width / Mathf.Max(1f, view.position.height - 80f));
            float frameSize = Mathf.Max(size.y + 1000f, (size.x + 800f) / aspect) * 0.57f;
            view.LookAt(plan.CoordinateToWorld(center), Quaternion.Euler(90f, 0f, 0f), frameSize, true);
            FinishFrame(view);
        }

        public static void FrameHeightReferences(OfficialMapPlan plan)
        {
            if (!CanFrame(plan)) return;
            EnableDisplay(plan, "showHeightReferences");
            SceneView view = GetView(plan);
            view.in2DMode = false;
            float height = plan.MaximumReferenceHeightMeters;
            Vector3 center = plan.CoordinateToWorld(plan.ReferenceOriginMeters + new Vector2(60f, 15f),
                plan.ReferenceBaseHeightMeters + height * 0.48f);
            view.LookAt(center, Quaternion.Euler(15f, -20f, 0f), Mathf.Max(230f, height * 0.79f), false);
            FinishFrame(view);
        }

        public static void FrameAreaReferences(OfficialMapPlan plan)
        {
            if (!CanFrame(plan)) return;
            EnableDisplay(plan, "showAreaReferences");
            SceneView view = GetView(plan);
            view.in2DMode = false;
            Vector2 center = new Vector2(plan.WorldSizeMeters.x + 1100f, 1025f);
            view.LookAt(plan.CoordinateToWorld(center), Quaternion.Euler(90f, 0f, 0f), 1350f, true);
            FinishFrame(view);
        }

        public static void FrameHumanScale(OfficialMapPlan plan)
        {
            if (!CanFrame(plan)) return;
            EnableDisplay(plan, "showHeightReferences");
            SceneView view = GetView(plan);
            view.in2DMode = false;
            Vector2 position = HumanReferenceOrigin(plan) + new Vector2(2f, 0f);
            view.LookAt(plan.CoordinateToWorld(position, plan.ReferenceBaseHeightMeters + 1.25f),
                Quaternion.Euler(14f, -25f, 0f), 4.2f, false);
            FinishFrame(view);
        }

        public static void ShowOnlyHeightReferences(OfficialMapPlan plan)
        {
            if (plan == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var serialized = new SerializedObject(plan);
            serialized.FindProperty("showPlan").boolValue = false;
            serialized.FindProperty("showHeightReferences").boolValue = true;
            serialized.FindProperty("showAreaReferences").boolValue = false;
            serialized.FindProperty("showLabels").boolValue = true;
            serialized.ApplyModifiedProperties();
            SceneView.RepaintAll();
        }

        private static bool CanFrame(OfficialMapPlan plan)
        {
            return plan != null && plan.gameObject.scene.IsValid() && plan.gameObject.scene.isLoaded
                && !EditorApplication.isPlayingOrWillChangePlaymode && plan.HasMetricTransform(out _);
        }

        private static SceneView GetView(OfficialMapPlan plan)
        {
            Selection.activeGameObject = plan.gameObject;
            return SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
        }

        private static void EnableDisplay(OfficialMapPlan plan, string propertyName)
        {
            var serialized = new SerializedObject(plan);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null && !property.boolValue)
            {
                property.boolValue = true;
                serialized.ApplyModifiedProperties();
            }
        }

        private static void FinishFrame(SceneView view)
        {
            view.Focus();
            view.Repaint();
        }

        private static void DrawScene(SceneView view)
        {
            if (Event.current == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            OfficialMapPlan overlayPlan = null;
            foreach (OfficialMapPlan plan in UnityEngine.Object.FindObjectsByType<OfficialMapPlan>(FindObjectsSortMode.None))
            {
                if (plan == null || !plan.isActiveAndEnabled
                    || !plan.gameObject.scene.IsValid() || !plan.gameObject.scene.isLoaded
                    || plan.gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene()) continue;
                if (overlayPlan == null || plan.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                    overlayPlan = plan;
                if (Event.current.type == EventType.Repaint && plan.HasMetricTransform(out _))
                    DrawPlan(view, plan);
            }
            if (overlayPlan != null) DrawViewControls(view, overlayPlan);
        }

        private static void DrawPlan(SceneView view, OfficialMapPlan plan)
        {
            Color oldColor = Handles.color;
            Matrix4x4 oldMatrix = Handles.matrix;
            CompareFunction oldDepth = Handles.zTest;
            Handles.matrix = Matrix4x4.identity;
            Handles.zTest = CompareFunction.Always;
            try
            {
                if (plan.ShowPlan)
                {
                    DrawRegions(view, plan);
                    if (plan.ShowGrid) DrawGrid(plan);
                    DrawEnvelope(view, plan);
                }
                if (plan.ShowHeightReferences) DrawHeightReferences(view, plan);
                if (plan.ShowAreaReferences) DrawAreaReferences(view, plan);
            }
            finally
            {
                Handles.color = oldColor;
                Handles.matrix = oldMatrix;
                Handles.zTest = oldDepth;
            }
        }

        private static void DrawRegions(SceneView view, OfficialMapPlan plan)
        {
            foreach (OfficialMapRegion region in plan.Regions)
            {
                if (region == null || region.PointsMeters == null || region.PointsMeters.Length < 3) continue;
                Vector2[] points = region.PointsMeters;
                List<int> triangles = Triangulate(points);
                Color fill = region.Color;
                fill.a = 0.20f;
                Handles.color = fill;
                for (int index = 0; index + 2 < triangles.Count; index += 3)
                    Handles.DrawAAConvexPolygon(plan.CoordinateToWorld(points[triangles[index]]),
                        plan.CoordinateToWorld(points[triangles[index + 1]]),
                        plan.CoordinateToWorld(points[triangles[index + 2]]));

                Color outline = region.Color;
                outline.a = 1f;
                Handles.color = outline;
                var closed = new Vector3[points.Length + 1];
                for (int index = 0; index < points.Length; index++)
                    closed[index] = plan.CoordinateToWorld(points[index], 0.1f);
                closed[points.Length] = closed[0];
                Handles.DrawAAPolyLine(2.5f, closed);

                if (plan.ShowLabels)
                {
                    double tileArea = (double)plan.TileSizeMeters * plan.TileSizeMeters;
                    double count = tileArea > 0d ? region.AreaSquareMeters / tileArea : 0d;
                    DrawLabel(view, plan.CoordinateToWorld(region.LabelPositionMeters, 0.2f),
                        $"{region.DisplayName}\n{region.AreaSquareMeters / 1000000d:F3} km² · {count:F1} 块", outline);
                }
            }
        }

        private static void DrawGrid(OfficialMapPlan plan)
        {
            Vector2 size = plan.WorldSizeMeters;
            float step = plan.TileSizeMeters;
            if (step < 1f || Mathf.Ceil(size.x / step) + Mathf.Ceil(size.y / step) > 2000f) return;
            for (int column = 0; column <= Mathf.FloorToInt(size.x / step); column++)
            {
                float x = column * step;
                Handles.color = new Color(0.85f, 0.9f, 1f, column % 4 == 0 ? 0.23f : 0.11f);
                Handles.DrawLine(plan.CoordinateToWorld(new Vector2(x, 0f), 0.12f),
                    plan.CoordinateToWorld(new Vector2(x, size.y), 0.12f));
            }
            for (int row = 0; row <= Mathf.FloorToInt(size.y / step); row++)
            {
                float z = row * step;
                Handles.color = new Color(0.85f, 0.9f, 1f, row % 4 == 0 ? 0.23f : 0.11f);
                Handles.DrawLine(plan.CoordinateToWorld(new Vector2(0f, z), 0.12f),
                    plan.CoordinateToWorld(new Vector2(size.x, z), 0.12f));
            }
        }

        private static void DrawEnvelope(SceneView view, OfficialMapPlan plan)
        {
            Vector2 size = plan.WorldSizeMeters;
            DrawRectangle(plan, Vector2.zero, size, new Color(0.8f, 0.85f, 0.94f, 0.5f), 1.2f);
            DrawDimension(view, plan, new Vector2(0f, size.y + 110f), new Vector2(size.x, size.y + 110f),
                $"{size.x:N0} m · 制作包络宽度", DimensionColor);
            DrawDimension(view, plan, new Vector2(-110f, 0f), new Vector2(-110f, size.y),
                $"{size.y:N0} m", DimensionColor);
            if (!plan.ShowLabels) return;
            DrawLabel(view, plan.CoordinateToWorld(new Vector2(size.x * 0.5f, size.y + 500f)),
                $"Official_Map_01 · 区域规划\n250 m 网格 · 1 单位 = 1 m · 平面边界 Y = 0", Color.white, true);
            Vector2 northBase = new Vector2(-250f, size.y - 300f);
            Vector2 northTop = northBase + Vector2.up * 180f;
            Handles.color = DimensionColor;
            Handles.DrawAAPolyLine(2f, plan.CoordinateToWorld(northBase), plan.CoordinateToWorld(northTop));
            Handles.DrawLine(plan.CoordinateToWorld(northTop), plan.CoordinateToWorld(northTop + new Vector2(-25f, -45f)));
            Handles.DrawLine(plan.CoordinateToWorld(northTop), plan.CoordinateToWorld(northTop + new Vector2(25f, -45f)));
            DrawLabel(view, plan.CoordinateToWorld(northTop + Vector2.up * 55f), "北 +Z", DimensionColor);
        }

        private static void DrawHeightReferences(SceneView view, OfficialMapPlan plan)
        {
            Vector2 origin = plan.ReferenceOriginMeters;
            DrawRuler(view, plan, origin, plan.MaximumReferenceHeightMeters, 25f,
                $"高度差参照\n0–{plan.MaximumReferenceHeightMeters:N0} m", HeightColor);
            DrawRuler(view, plan, origin + new Vector2(110f, 0f), OfficialMapPlan.V1ReferenceHeightMeters, 10f,
                "V1 高度差参照\n0–70 m", V1Color);
            Vector2 humanOrigin = HumanReferenceOrigin(plan);
            Vector3 cubeBottom = plan.CoordinateToWorld(humanOrigin, plan.ReferenceBaseHeightMeters);
            Vector3 figureBottom = cubeBottom + Vector3.right * 4f;
            Handles.color = HeightColor;
            Handles.DrawWireCube(cubeBottom + Vector3.up * 0.5f, Vector3.one);
            Handles.color = Color.white;
            Handles.DrawWireDisc(figureBottom + Vector3.up * 1.8f, Vector3.forward, 0.2f);
            Handles.DrawLine(figureBottom + Vector3.up * 1.6f, figureBottom + Vector3.up * 0.8f);
            Handles.DrawLine(figureBottom + new Vector3(-0.45f, 1.45f, 0f), figureBottom + new Vector3(0.45f, 1.45f, 0f));
            Handles.DrawLine(figureBottom + Vector3.up * 0.8f, figureBottom - Vector3.right * 0.3f);
            Handles.DrawLine(figureBottom + Vector3.up * 0.8f, figureBottom + Vector3.right * 0.3f);
            if (plan.ShowLabels && ScreenDistance(view, cubeBottom, cubeBottom + Vector3.up * 2f) > 40f)
            {
                DrawLabel(view, cubeBottom + Vector3.down * 0.5f, "1 × 1 × 1 m 方块", HeightColor);
                DrawLabel(view, figureBottom + Vector3.up * 2.4f, "2 m 人形", Color.white);
            }
        }

        private static Vector2 HumanReferenceOrigin(OfficialMapPlan plan)
        {
            return plan.ReferenceOriginMeters + new Vector2(170f, 25f);
        }

        private static void DrawRuler(SceneView view, OfficialMapPlan plan, Vector2 origin, float height,
            float majorInterval, string title, Color color)
        {
            if (height <= 0f) return;
            Vector3 bottom = plan.CoordinateToWorld(origin, plan.ReferenceBaseHeightMeters);
            Vector3 top = bottom + Vector3.up * height;
            float screenHeight = ScreenDistance(view, bottom, top);
            Handles.color = color;
            Handles.DrawAAPolyLine(3f, bottom, top);
            Handles.DrawLine(bottom - Vector3.right * 18f, bottom + Vector3.right * 18f);
            Handles.DrawLine(top - Vector3.right * 18f, top + Vector3.right * 18f);
            if (screenHeight < 18f) return;
            if (screenHeight < 100f)
            {
                // The 70 m ruler stays identifiable beside the much taller 350 m reference.
                if (plan.ShowLabels)
                {
                    DrawLabel(view, top + Vector3.up * 55f + Vector3.right * 35f, title, color);
                    DrawLabel(view, bottom - Vector3.up * 24f + Vector3.right * 35f,
                        $"相对 0 m · Y = {bottom.y:F2} m", color, false, true);
                }
                return;
            }
            float interval = majorInterval;
            if (screenHeight / Mathf.Max(1f, height / interval) < 24f)
                interval *= Mathf.Ceil(24f / Mathf.Max(1f, screenHeight / Mathf.Max(1f, height / interval)));
            for (float meter = 0f; meter < height + 0.01f; meter += interval)
            {
                Vector3 point = bottom + Vector3.up * meter;
                Handles.DrawLine(point - Vector3.right * 9f, point + Vector3.right * 9f);
                if (plan.ShowLabels) DrawLabel(view, point - Vector3.right * 20f, $"{meter:N0} m", color, false, true);
            }
            if (plan.ShowLabels)
            {
                DrawLabel(view, top + Vector3.up * 60f, title, color);
                DrawLabel(view, bottom - Vector3.up * 28f,
                    $"尺底相对 0 m · 世界 Y = {bottom.y:F2} m", color, false, true);
            }
        }

        private static void DrawAreaReferences(SceneView view, OfficialMapPlan plan)
        {
            Vector2 largeOrigin = new Vector2(plan.WorldSizeMeters.x + 350f, 0f);
            Vector2 smallOrigin = largeOrigin + Vector2.up * 1750f;
            Color squareColor = new Color(0.56f, 1f, 0.66f, 1f);
            DrawRectangle(plan, largeOrigin, Vector2.one * 1500f, squareColor, 2f);
            Handles.color = new Color(squareColor.r, squareColor.g, squareColor.b, 0.28f);
            for (int index = 1; index < 6; index++)
            {
                float offset = index * 250f;
                Handles.DrawLine(plan.CoordinateToWorld(largeOrigin + new Vector2(offset, 0f)),
                    plan.CoordinateToWorld(largeOrigin + new Vector2(offset, 1500f)));
                Handles.DrawLine(plan.CoordinateToWorld(largeOrigin + new Vector2(0f, offset)),
                    plan.CoordinateToWorld(largeOrigin + new Vector2(1500f, offset)));
            }
            DrawRectangle(plan, smallOrigin, Vector2.one * 250f, V1Color, 2.5f);
            if (plan.ShowLabels)
            {
                float squareWidth = ScreenDistance(view,
                    plan.CoordinateToWorld(largeOrigin + new Vector2(0f, 750f)),
                    plan.CoordinateToWorld(largeOrigin + new Vector2(1500f, 750f)));
                if (squareWidth >= 220f)
                    DrawLabel(view, plan.CoordinateToWorld(largeOrigin + Vector2.one * 750f),
                        "草原等面积参照\n1500 × 1500 m = 2.25 km²\n36 个 250 m 区块 · 方框不代表草原形状", squareColor, true);
                DrawLabel(view, plan.CoordinateToWorld(smallOrigin + new Vector2(125f, 340f)),
                    "单个 V1 区块\n250 × 250 m = 0.0625 km²", V1Color);
            }
        }

        private static void DrawRectangle(OfficialMapPlan plan, Vector2 origin, Vector2 size, Color color, float width)
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(width, plan.CoordinateToWorld(origin),
                plan.CoordinateToWorld(origin + Vector2.right * size.x),
                plan.CoordinateToWorld(origin + size), plan.CoordinateToWorld(origin + Vector2.up * size.y),
                plan.CoordinateToWorld(origin));
        }

        private static void DrawDimension(SceneView view, OfficialMapPlan plan, Vector2 from, Vector2 to,
            string text, Color color)
        {
            Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized * 22f;
            Handles.color = color;
            Handles.DrawLine(plan.CoordinateToWorld(from), plan.CoordinateToWorld(to));
            Handles.DrawLine(plan.CoordinateToWorld(from - normal), plan.CoordinateToWorld(from + normal));
            Handles.DrawLine(plan.CoordinateToWorld(to - normal), plan.CoordinateToWorld(to + normal));
            if (plan.ShowLabels && ScreenDistance(view, plan.CoordinateToWorld(from), plan.CoordinateToWorld(to)) > 75f)
                DrawLabel(view, plan.CoordinateToWorld((from + to) * 0.5f + normal * 1.6f), text, color);
        }

        private static void DrawLabel(SceneView view, Vector3 worldPosition, string text, Color color,
            bool title = false, bool small = false)
        {
            if (view.camera == null || view.camera.WorldToViewportPoint(worldPosition).z <= 0f) return;
            Vector2 point = HandleUtility.WorldToGUIPoint(worldPosition);
            if (point.x < -300f || point.y < -120f || point.x > view.position.width + 300f
                || point.y > view.position.height + 120f) return;
            EnsureStyles();
            GUIStyle style = title ? titleStyle : small ? smallStyle : labelStyle;
            var content = new GUIContent(text);
            Vector2 size = style.CalcSize(content) + new Vector2(14f, 8f);
            var rectangle = new Rect(point.x - size.x * 0.5f, point.y - size.y * 0.5f, size.x, size.y);
            Handles.BeginGUI();
            Color previous = GUI.color;
            GUI.color = new Color(0.06f, 0.075f, 0.09f, 0.88f);
            GUI.DrawTexture(rectangle, EditorGUIUtility.whiteTexture);
            GUI.color = color;
            GUI.Label(rectangle, content, style);
            GUI.color = previous;
            Handles.EndGUI();
        }

        private static float ScreenDistance(SceneView view, Vector3 from, Vector3 to)
        {
            if (view.camera == null) return 0f;
            return Vector2.Distance(HandleUtility.WorldToGUIPoint(from), HandleUtility.WorldToGUIPoint(to));
        }

        private static void EnsureStyles()
        {
            if (labelStyle != null) return;
            labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                padding = new RectOffset(7, 7, 4, 4),
                normal = { textColor = Color.white }
            };
            titleStyle = new GUIStyle(labelStyle) { fontSize = 13 };
            smallStyle = new GUIStyle(labelStyle) { fontSize = 11 };
        }

        private static void DrawViewControls(SceneView view, OfficialMapPlan plan)
        {
            Handles.BeginGUI();
            float top = Mathf.Max(65f, view.position.height - 210f);
            GUILayout.BeginArea(new Rect(12f, top, 285f, 174f), GUIContent.none, EditorStyles.helpBox);
            GUILayout.Label("Official_Map_01 · 区域与尺度", EditorStyles.boldLabel);
            var serialized = new SerializedObject(plan);
            GUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            serialized.FindProperty("showPlan").boolValue = GUILayout.Toggle(plan.ShowPlan, "区域规划");
            serialized.FindProperty("showHeightReferences").boolValue = GUILayout.Toggle(plan.ShowHeightReferences, "高度尺");
            serialized.FindProperty("showAreaReferences").boolValue = GUILayout.Toggle(plan.ShowAreaReferences, "面积框");
            if (EditorGUI.EndChangeCheck())
            {
                serialized.ApplyModifiedProperties();
                SceneView.RepaintAll();
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("只显示高度参照", GUILayout.Height(22f))) ShowOnlyHeightReferences(plan);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("区域总览", GUILayout.Height(25f))) FramePlan(plan);
            if (GUILayout.Button("高度参照", GUILayout.Height(25f))) FrameHeightReferences(plan);
            if (GUILayout.Button("面积参照", GUILayout.Height(25f))) FrameAreaReferences(plan);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("近看 1 m 方块 / 2 m 人形", GUILayout.Height(22f))) FrameHumanScale(plan);
            GUILayout.Label($"尺底 Y = {plan.transform.position.y + plan.ReferenceBaseHeightMeters:F2} m · 刻度为高度差", EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        /// <summary>Ear clipping supports concave outlines without filling across their indentations.</summary>
        internal static List<int> Triangulate(Vector2[] points)
        {
            var result = new List<int>();
            if (points == null || points.Length < 3) return result;
            var remaining = new List<int>(points.Length);
            for (int index = 0; index < points.Length; index++)
            {
                int previous = (index + points.Length - 1) % points.Length;
                if ((points[index] - points[previous]).sqrMagnitude > 0.000001f)
                    remaining.Add(index);
            }
            if (remaining.Count < 3) return result;
            double signedArea = 0d;
            for (int index = 0; index < remaining.Count; index++)
            {
                Vector2 a = points[remaining[index]];
                Vector2 b = points[remaining[(index + 1) % remaining.Count]];
                signedArea += (double)a.x * b.y - (double)b.x * a.y;
            }
            double orientation = signedArea >= 0d ? 1d : -1d;
            int guard = remaining.Count * remaining.Count;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool removed = false;
                for (int cursor = 0; cursor < remaining.Count; cursor++)
                {
                    int a = remaining[(cursor + remaining.Count - 1) % remaining.Count];
                    int b = remaining[cursor];
                    int c = remaining[(cursor + 1) % remaining.Count];
                    double cross = Cross(points[a], points[b], points[c]) * orientation;
                    if (Math.Abs(cross) < 0.000001d)
                    {
                        remaining.RemoveAt(cursor);
                        removed = true;
                        break;
                    }
                    if (cross < 0d) continue;
                    bool containsVertex = false;
                    for (int other = 0; other < remaining.Count; other++)
                    {
                        int candidate = remaining[other];
                        if (candidate == a || candidate == b || candidate == c) continue;
                        if (InsideTriangle(points[candidate], points[a], points[b], points[c], orientation))
                        {
                            containsVertex = true;
                            break;
                        }
                    }
                    if (containsVertex) continue;
                    result.Add(a);
                    result.Add(b);
                    result.Add(c);
                    remaining.RemoveAt(cursor);
                    removed = true;
                    break;
                }
                if (!removed) return new List<int>();
            }
            if (remaining.Count == 3)
            {
                result.Add(remaining[0]);
                result.Add(remaining[1]);
                result.Add(remaining[2]);
            }
            return result;
        }

        private static double Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            return ((double)b.x - a.x) * ((double)c.y - a.y)
                - ((double)b.y - a.y) * ((double)c.x - a.x);
        }

        private static bool InsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c, double orientation)
        {
            const double epsilon = 0.000001d;
            return Cross(a, b, point) * orientation >= -epsilon
                && Cross(b, c, point) * orientation >= -epsilon
                && Cross(c, a, point) * orientation >= -epsilon;
        }
    }
}

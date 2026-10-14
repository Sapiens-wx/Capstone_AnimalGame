using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimalGame.Editor
{
    [CustomEditor(typeof(TerrainHeightReference))]
    public sealed class TerrainHeightReferenceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var reference = (TerrainHeightReference)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "V1: 1 Unity world unit = 1 meter. The reference ruler is always 70 m high. "
                + "References appear in the Scene view in Edit Mode, independently of Gizmos switches. "
                + "Use Show Reference or the Show and Frame button to control visibility. "
                + "Measurements are cached and do not refresh while sculpting.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Refresh Measurements / 刷新实际高差"))
                {
                    Undo.RecordObject(reference, "Refresh Terrain Height Measurements");
                    reference.RefreshMeasurements();
                    EditorUtility.SetDirty(reference);
                    if (reference.gameObject.scene.IsValid() && reference.gameObject.scene.isLoaded)
                        EditorSceneManager.MarkSceneDirty(reference.gameObject.scene);
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Show and Frame Height Reference / 显示并聚焦高度参照"))
                    TerrainHeightReferenceGizmoDrawer.ShowAndFrame(reference);
            }

            EditorGUILayout.Space();
            if (reference.HasMeasurements)
            {
                EditorGUILayout.LabelField("Absolute minimum / 最低海拔", $"{reference.CachedMinHeightMeters:F3} m");
                EditorGUILayout.LabelField("Absolute maximum / 最高海拔", $"{reference.CachedMaxHeightMeters:F3} m");
                EditorGUILayout.LabelField("Actual height difference / 高差", $"{reference.CachedHeightDifferenceMeters:F3} m");
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector3Field("Minimum world point", reference.CachedMinWorldPosition);
                    EditorGUILayout.Vector3Field("Maximum world point", reference.CachedMaxWorldPosition);
                }
                EditorGUILayout.LabelField("Measured Terrain count", reference.CachedTerrainCount.ToString());
                EditorGUILayout.LabelField("Height samples", reference.CachedSampleCount.ToString("N0"));
                EditorGUILayout.LabelField("Last measurement", reference.MeasurementTimeUtc);
            }

            EditorGUILayout.HelpBox(reference.MeasurementStatus,
                reference.HasMeasurements ? MessageType.Info : MessageType.Warning);
        }
    }

    [InitializeOnLoad]
    public static class TerrainHeightReferenceGizmoDrawer
    {
        private static readonly Color RulerColor = new Color(0.15f, 0.95f, 1f, 1f);
        private static readonly Color MinColor = new Color(0.25f, 1f, 0.4f, 1f);
        private static readonly Color MaxColor = new Color(1f, 0.45f, 0.18f, 1f);
        private static readonly Color DifferenceColor = new Color(1f, 0.88f, 0.15f, 1f);

        static TerrainHeightReferenceGizmoDrawer()
        {
            SceneView.duringSceneGui += DrawSceneReferences;
        }

        [MenuItem("Animal Game/Terrain Grid V1/Show Height Reference")]
        private static void ShowGridReference()
        {
            TerrainHeightReference chosen = null;
            foreach (TerrainHeightReference reference in Object.FindObjectsByType<TerrainHeightReference>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (reference.gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                    continue;
                if (chosen == null || reference.name == "Height Reference - Four Tile Grid")
                    chosen = reference;
            }
            if (chosen == null)
            {
                Debug.LogWarning("No height reference in the active scene. Open 3DMapTestScene first.");
                return;
            }
            ShowAndFrame(chosen);
        }

        internal static void ShowAndFrame(TerrainHeightReference reference)
        {
            if (reference == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Undo.RecordObject(reference, "Show terrain height reference");
            Undo.RecordObject(reference.gameObject, "Activate terrain height reference");
            reference.gameObject.SetActive(true);
            reference.enabled = true;
            var settings = new SerializedObject(reference);
            settings.FindProperty("showReference").boolValue = true;
            settings.ApplyModifiedProperties();
            EditorUtility.SetDirty(reference);
            EditorSceneManager.MarkSceneDirty(reference.gameObject.scene);
            Selection.activeGameObject = reference.gameObject;
            SceneView view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            view.in2DMode = false;
            view.LookAt(reference.RulerBasePosition + Vector3.up * 35f,
                Quaternion.Euler(15f, -35f, 0f), 52f, false);
            view.Focus();
            view.Repaint();
        }

        private static void DrawSceneReferences(SceneView view)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint
                || EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (TerrainHeightReference reference in Object.FindObjectsByType<TerrainHeightReference>(
                         FindObjectsSortMode.None))
                if (reference.gameObject.scene.IsValid() && reference.gameObject.scene.isLoaded)
                    DrawHeightReference(reference);
        }

        private static void DrawHeightReference(TerrainHeightReference reference)
        {
            if (reference == null || !reference.isActiveAndEnabled || !reference.ShowReference
                || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Color previousColor = Handles.color;
            Matrix4x4 previousMatrix = Handles.matrix;
            UnityEngine.Rendering.CompareFunction previousDepthTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.matrix = Matrix4x4.identity;

            try
            {
                Vector3 rulerBase = reference.RulerBasePosition;
                Vector3 side = GetViewSide();
                Vector3 rulerTop = rulerBase + Vector3.up * TerrainHeightReference.V1ReferenceHeightMeters;
                Handles.color = RulerColor;
                Handles.DrawAAPolyLine(3f, rulerBase, rulerTop);

                for (int meter = 0; meter <= TerrainHeightReference.V1ReferenceHeightMeters; meter++)
                {
                    bool major = meter % 10 == 0;
                    if (!major && !reference.ShowOneMeterTicks)
                        continue;
                    Vector3 position = rulerBase + Vector3.up * meter;
                    float length = major ? 2f : 0.65f;
                    Handles.DrawLine(position - side * length, position);
                    if (major && reference.ShowLabels)
                        DrawLabel(position - side * 3.5f, $"{meter} m", RulerColor);
                }

                if (reference.ShowLabels)
                {
                    DrawLabel(rulerTop + Vector3.up * 2.5f,
                        "V1 height reference: 70 m / V1 高度尺", RulerColor);
                    DrawLabel(rulerBase - Vector3.up * 2f,
                        $"World Y datum: {rulerBase.y:F1} m / 1 unit = 1 m", RulerColor);
                }

                if (reference.ShowSizeReferences)
                    DrawSizeReferences(reference, rulerBase, side);

                if (reference.HasMeasurements && reference.ShowMeasuredExtrema)
                    DrawMeasurements(reference, rulerBase, side);
            }
            finally
            {
                Handles.color = previousColor;
                Handles.matrix = previousMatrix;
                Handles.zTest = previousDepthTest;
            }
        }

        private static void DrawSizeReferences(TerrainHeightReference reference, Vector3 rulerBase, Vector3 side)
        {
            Vector3 cubeBottom = rulerBase - side * 9f;
            Handles.color = RulerColor;
            Handles.DrawWireCube(cubeBottom + Vector3.up * 0.5f, Vector3.one);

            Vector3 figureBottom = rulerBase - side * 13f;
            Handles.color = Color.white;
            // The head, torso, and legs together have an exact two-meter vertical extent.
            Handles.DrawWireDisc(figureBottom + Vector3.up * 1.8f, Vector3.Cross(side, Vector3.up).normalized, 0.2f);
            Handles.DrawLine(figureBottom + Vector3.up * 1.6f, figureBottom + Vector3.up * 0.8f);
            Handles.DrawLine(figureBottom + Vector3.up * 1.45f - side * 0.45f,
                figureBottom + Vector3.up * 1.45f + side * 0.45f);
            Handles.DrawLine(figureBottom + Vector3.up * 0.8f, figureBottom - side * 0.3f);
            Handles.DrawLine(figureBottom + Vector3.up * 0.8f, figureBottom + side * 0.3f);

            if (reference.ShowLabels)
            {
                DrawLabel(cubeBottom - Vector3.up * 1.5f, "1 m cube / 方块", RulerColor);
                DrawLabel(figureBottom + Vector3.up * 3f, "2 m figure / 人形参照", Color.white);
            }
        }

        private static void DrawMeasurements(TerrainHeightReference reference, Vector3 rulerBase, Vector3 side)
        {
            Vector3 minPoint = reference.CachedMinWorldPosition;
            Vector3 maxPoint = reference.CachedMaxWorldPosition;
            Vector3 arrowBase = rulerBase + side * 6f;
            Vector3 projectedMin = new Vector3(arrowBase.x, reference.CachedMinHeightMeters, arrowBase.z);
            Vector3 projectedMax = new Vector3(arrowBase.x, reference.CachedMaxHeightMeters, arrowBase.z);

            DrawPoint(minPoint, MinColor);
            DrawPoint(maxPoint, MaxColor);
            if (reference.ShowProjectionLines)
            {
                Handles.color = MinColor;
                Handles.DrawDottedLine(minPoint, projectedMin, 6f);
                Handles.color = MaxColor;
                Handles.DrawDottedLine(maxPoint, projectedMax, 6f);
            }

            Handles.color = DifferenceColor;
            Handles.DrawAAPolyLine(3f, projectedMin, projectedMax);
            float arrowHeadLength = Mathf.Min(2f, reference.CachedHeightDifferenceMeters * 0.2f);
            if (arrowHeadLength > 0.001f)
            {
                float arrowHeadWidth = arrowHeadLength * 0.45f;
                Handles.DrawLine(projectedMin, projectedMin + Vector3.up * arrowHeadLength + side * arrowHeadWidth);
                Handles.DrawLine(projectedMin, projectedMin + Vector3.up * arrowHeadLength - side * arrowHeadWidth);
                Handles.DrawLine(projectedMax, projectedMax - Vector3.up * arrowHeadLength + side * arrowHeadWidth);
                Handles.DrawLine(projectedMax, projectedMax - Vector3.up * arrowHeadLength - side * arrowHeadWidth);
            }

            Handles.color = MinColor;
            Handles.DrawLine(projectedMin - side * 1.5f, projectedMin + side * 1.5f);
            Handles.color = MaxColor;
            Handles.DrawLine(projectedMax - side * 1.5f, projectedMax + side * 1.5f);

            if (reference.ShowLabels)
            {
                DrawLabel(minPoint + Vector3.up * 2f, $"Min / 最低点: {reference.CachedMinHeightMeters:F3} m", MinColor);
                DrawLabel(maxPoint + Vector3.up * 2f, $"Max / 最高点: {reference.CachedMaxHeightMeters:F3} m", MaxColor);
                DrawLabel(projectedMin + side * 2f, $"Min Y {reference.CachedMinHeightMeters:F3} m", MinColor);
                DrawLabel(projectedMax + side * 2f, $"Max Y {reference.CachedMaxHeightMeters:F3} m", MaxColor);
                DrawLabel((projectedMin + projectedMax) * 0.5f + side * 2f,
                    $"ΔH / 实际高差: {reference.CachedHeightDifferenceMeters:F3} m\nCached: refresh after sculpting / 雕刻后刷新", DifferenceColor);
            }
        }

        private static void DrawPoint(Vector3 point, Color color)
        {
            Handles.color = color;
            float radius = Mathf.Clamp(HandleUtility.GetHandleSize(point) * 0.05f, 0.4f, 3f);
            Handles.SphereHandleCap(0, point, Quaternion.identity, radius, EventType.Repaint);
            Handles.DrawLine(point, point + Vector3.up * radius * 2f);
        }

        private static Vector3 GetViewSide()
        {
            SceneView view = SceneView.currentDrawingSceneView;
            if (view == null || view.camera == null)
                return Vector3.right;

            Vector3 side = Vector3.ProjectOnPlane(view.camera.transform.right, Vector3.up);
            return side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;
        }

        private static void DrawLabel(Vector3 position, string label, Color color)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = color }
            };
            Handles.Label(position, label, style);
        }
    }
}

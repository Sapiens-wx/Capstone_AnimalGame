using System.Collections.Generic;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>SerializedProperty-based authoring retains Unity Undo, prefab and mixed-value handling.</summary>
    [CustomEditor(typeof(TutorialTextContent))]
    [CanEditMultipleObjects]
    public sealed class TutorialTextContentEditor : UnityEditor.Editor
    {
        private bool panelExpanded;
        private bool drivingExpanded = true;
        private bool terrainExpanded = true;
        private bool drivingStatusesExpanded;
        private bool terrainStatusesExpanded;
        private bool placeholdersExpanded;
        private readonly HashSet<string> initializedSteps = new HashSet<string>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            EditorGUILayout.HelpBox("按章节展开步骤，优先编辑「教学正文」。步骤顺序保持固定，修改文案不改变教学判定。", MessageType.Info);
            EditorGUILayout.Space(3);
            panelExpanded = EditorGUILayout.Foldout(panelExpanded, "任务栏固定文字", true, EditorStyles.foldoutHeader);
            if (panelExpanded) DrawPanel(serializedObject.FindProperty("Panel"));
            EditorGUILayout.Space(5);
            drivingExpanded = EditorGUILayout.Foldout(drivingExpanded, "01 · 驾驶教程", true, EditorStyles.foldoutHeader);
            if (drivingExpanded) DrawDriving(serializedObject.FindProperty("Driving"));
            EditorGUILayout.Space(5);
            terrainExpanded = EditorGUILayout.Foldout(terrainExpanded, "02 · 地形与坡度教程", true, EditorStyles.foldoutHeader);
            if (terrainExpanded) DrawTerrain(serializedObject.FindProperty("Terrain"));
            EditorGUILayout.Space(7);
            placeholdersExpanded = EditorGUILayout.Foldout(placeholdersExpanded, "动态占位符与排版提示", true);
            if (placeholdersExpanded)
            {
                EditorGUILayout.HelpBox("可在正文、操作、补充说明或反馈中使用以下占位符；运行时会显示真实进度与当前手柄按键。\n"
                    + "未知占位符保留原文，不会使教程中断。", MessageType.Info);
                EditorGUILayout.LabelField("驾驶进度", "{value:F1}   {target:F0}", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("行进转向", "{movingDistance:F1}   {movingTarget:F1}", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("地点与坡度", "{distance:F1}   {slope:F0}", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("阅读时间", "{reading:F1}   {readingTarget:F0}", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("手柄按键", "{rt}   {lt}   {lb}", EditorStyles.wordWrappedLabel);
                EditorGUILayout.HelpBox("正文建议控制在 4 行左右，可按 Enter 换行。操作指令与底部提示尽量简短；这里不会截断或限制保存的文字。\n"
                    + "收起标签较窄，纵排标题可为每个字母单独换行。", MessageType.None);
            }
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawPanel(SerializedProperty panel)
        {
            if (panel == null) return;
            using (new EditorGUI.IndentLevelScope())
            {
                Field(panel, "Header", "顶部标题");
                Field(panel, "CurrentLesson", "当前教学标签");
                Field(panel, "CurrentAction", "当前操作标签");
                EditorGUILayout.Space(3);
                Field(panel, "CollapseHint", "展开时的操作提示", "固定显示在任务栏底部，独立于当前教学状态。");
                Field(panel, "CollapseTooltip", "收起按钮悬停说明");
                Field(panel, "ExpandHint", "收起时的操作提示");
                Field(panel, "ExpandArrows", "收起时的方向符号");
                Field(panel, "GuideLabel", "收起标签纵排标题", "可按 Enter 换行，例如每个字母各占一行。");
                Field(panel, "ExpandTooltip", "展开按钮悬停说明");
            }
        }

        private void DrawDriving(SerializedProperty driving)
        {
            if (driving == null) return;
            using (new EditorGUI.IndentLevelScope())
            {
                Field(driving, "Title", "章节标题");
                DrawStep(driving, "TurnLeft", "1 · 左转", initiallyExpanded: true, showCompleteFeedback: true);
                DrawStep(driving, "TurnRight", "2 · 右转", showCompleteFeedback: true);
                DrawStep(driving, "Forward", "3 · 前进", showCompleteFeedback: true);
                DrawStep(driving, "Reverse", "4 · 后退", showCompleteFeedback: true);
                DrawStep(driving, "SteerWhileMoving", "5 · 行进中转向", showCompleteFeedback: true);
                DrawStep(driving, "Stop", "6 · 停车", showCompleteFeedback: true);
                DrawStep(driving, "Completed", "驾驶教学完成");
                drivingStatusesExpanded = EditorGUILayout.Foldout(drivingStatusesExpanded, "通用状态反馈", true);
                if (drivingStatusesExpanded)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Field(driving, "WaitingStatus", "等待操作");
                        Field(driving, "BlockedStatus", "暂时无法驾驶");
                        Field(driving, "CompleteStatus", "教学完成");
                    }
            }
        }

        private void DrawTerrain(SerializedProperty terrain)
        {
            if (terrain == null) return;
            using (new EditorGUI.IndentLevelScope())
            {
                Field(terrain, "Title", "章节标题");
                DrawStep(terrain, "ReachSurvey", "1 · 抵达地形练习区");
                DrawStep(terrain, "ScanTerrain", "2 · 扫描地形");
                DrawStep(terrain, "ReadSigns", "3 · 阅读通行标记");
                DrawStep(terrain, "FollowRoute", "4 · 跟随路线");
                DrawStep(terrain, "Completed", "地形教学完成");
                terrainStatusesExpanded = EditorGUILayout.Foldout(terrainStatusesExpanded, "通用状态反馈", true);
                if (terrainStatusesExpanded)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Field(terrain, "FollowMarkerStatus", "跟随地点指引");
                        Field(terrain, "WaitingScanStatus", "等待地形扫描");
                        Field(terrain, "WaitingMarkersStatus", "等待扫描标记");
                        Field(terrain, "StopToReadStatus", "停车阅读");
                        Field(terrain, "ObserveStatus", "观察扫描结果");
                        Field(terrain, "BlockedStatus", "暂时无法驾驶");
                        Field(terrain, "CompleteStatus", "教学完成");
                    }
            }
        }

        private void DrawStep(SerializedProperty chapter, string field, string caption,
            bool initiallyExpanded = false, bool showCompleteFeedback = false)
        {
            SerializedProperty step = chapter.FindPropertyRelative(field);
            if (step == null) return;
            if (initializedSteps.Add(step.propertyPath) && initiallyExpanded) step.isExpanded = true;
            step.isExpanded = EditorGUILayout.Foldout(step.isExpanded, caption, true);
            if (!step.isExpanded) return;
            using (new EditorGUI.IndentLevelScope())
            {
                // Message comes first so the author does not have to hunt beneath input/status data.
                Field(step, "Message", "教学正文", "对话栏中部的主要文字；可按 Enter 换行，建议约 4 行。");
                Field(step, "Action", "操作指令", "按键图标右侧的短指令，例如 Push left stick。");
                Field(step, "ControlGlyph", "操作图标文字", "图标内的短文字；可使用 {rt}、{lt} 或 {lb}。");
                Field(step, "Detail", "进度 / 补充说明", "操作指令下方的文字，可用动态占位符显示数值。");
                if (showCompleteFeedback)
                    Field(step, "CompleteFeedback", "步骤完成反馈", "完成当前驾驶步骤后短暂显示的反馈。");
            }
            EditorGUILayout.Space(3);
        }

        private static void Field(SerializedProperty parent, string name, string caption, string tooltip = null)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(caption, tooltip), true);
        }
    }

    /// <summary>Keep all lesson settings visible and provide a direct route to their text asset.</summary>
    public abstract class TutorialLessonTextInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty script = serializedObject.FindProperty("m_Script");
            if (script != null)
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(script);
            SerializedProperty tutorialText = serializedObject.FindProperty("tutorialText");
            if (tutorialText != null)
                EditorGUILayout.PropertyField(tutorialText, new GUIContent("教程文案", "编辑此资产即可修改教学正文、操作提示和状态文字。"));
            DrawTextAssetButton(tutorialText);
            EditorGUILayout.Space(7);
            DrawPropertiesExcluding(serializedObject, "m_Script", "tutorialText");
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTextAssetButton(SerializedProperty tutorialText)
        {
            var assets = new List<Object>();
            if (tutorialText != null && !tutorialText.hasMultipleDifferentValues)
            {
                if (tutorialText.objectReferenceValue != null) assets.Add(tutorialText.objectReferenceValue);
            }
            else
                foreach (Object selected in targets)
                {
                    using (var data = new SerializedObject(selected))
                    {
                        SerializedProperty text = data.FindProperty("tutorialText");
                        Object asset = text != null ? text.objectReferenceValue : null;
                        if (asset != null && !assets.Contains(asset)) assets.Add(asset);
                    }
                }
            EditorGUILayout.Space(5);
            using (new EditorGUI.DisabledScope(assets.Count == 0))
                if (GUILayout.Button("编辑教程文案", GUILayout.Height(26)))
                {
                    serializedObject.ApplyModifiedProperties();
                    Selection.objects = assets.ToArray();
                    EditorGUIUtility.PingObject(assets[0]);
                }
            if (assets.Count == 0)
                EditorGUILayout.HelpBox("指定教程文案资产后，可点击上方按钮直接编辑正文、操作提示与状态反馈。", MessageType.Info);
        }
    }

    [CustomEditor(typeof(TutorialDrivingLesson))]
    [CanEditMultipleObjects]
    public sealed class TutorialDrivingLessonTextEditor : TutorialLessonTextInspector { }

    [CustomEditor(typeof(TutorialTerrainLesson))]
    [CanEditMultipleObjects]
    public sealed class TutorialTerrainLessonTextEditor : TutorialLessonTextInspector { }
}

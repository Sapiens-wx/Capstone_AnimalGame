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
        private bool balanceExpanded = true;
        private bool armsExpanded = true;
        private bool discoveryExpanded = true;
        private bool photographyExpanded = true;
        private bool drivingStatusesExpanded;
        private bool terrainStatusesExpanded;
        private bool balanceStatusesExpanded;
        private bool armsStatusesExpanded;
        private bool armsRemindersExpanded = true;
        private bool discoveryStatusesExpanded;
        private bool photographyStatusesExpanded;
        private bool photographyRemindersExpanded;
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
            EditorGUILayout.Space(5);
            balanceExpanded = EditorGUILayout.Foldout(balanceExpanded, "03 · 重心控制", true, EditorStyles.foldoutHeader);
            if (balanceExpanded) DrawBalance(serializedObject.FindProperty("Balance"));
            EditorGUILayout.Space(5);
            armsExpanded = EditorGUILayout.Foldout(armsExpanded, "04 · 机械臂基础操作", true, EditorStyles.foldoutHeader);
            if (armsExpanded) DrawArms(serializedObject.FindProperty("Arms"));
            EditorGUILayout.Space(5);
            discoveryExpanded = EditorGUILayout.Foldout(discoveryExpanded, "05 · 探索扫描", true, EditorStyles.foldoutHeader);
            if (discoveryExpanded) DrawDiscovery(serializedObject.FindProperty("Discovery"));
            EditorGUILayout.Space(5);
            photographyExpanded = EditorGUILayout.Foldout(photographyExpanded, "06 · 野生动物拍照", true, EditorStyles.foldoutHeader);
            if (photographyExpanded) DrawPhotography(serializedObject.FindProperty("Photography"));
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
                EditorGUILayout.LabelField("重心阅读进度", "{value:F1} / {target:F0} s；调整步骤按实际重心变化完成。", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("机械臂进度", "{value:F0} / {target:F0} cm 或 °；停留时间使用 F1 秒。", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("探索扫描", "{value:F0}% 充能。完成操作后按 A 逐条继续。", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("拍照按键", "{photo} 拍照模式；{rb} 快门；{save} 保存；{cancel} 返回。", EditorStyles.wordWrappedLabel);
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
                Field(panel, "ContinuePrompt", "步骤完成后的继续提示", "完成当前操作后显示；按 A 才进入下一条教学。");
                Field(panel, "ContinueWaitingPrompt", "步骤尚未完成时的提示");
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

        private void DrawBalance(SerializedProperty balance)
        {
            if (balance == null) return;
            using (new EditorGUI.IndentLevelScope())
            {
                Field(balance, "Title", "章节标题");
                DrawStep(balance, "ReadIndicator", "1 · 认识重心点与支撑圆环");
                DrawStep(balance, "AdjustWeight", "2 · 右摇杆调整重心");
                DrawStep(balance, "ReadRisk", "3 · 认识侧翻风险");
                DrawStep(balance, "Completed", "重心教学完成");
                Field(balance, "ToggleHint", "持续操作提醒", "仅本章节显示的右摇杆提示；底部方向键提示保持不变。");
                Field(balance, "NeedStowMessage", "请先收起机械臂的正文");
                Field(balance, "NeutralMessage", "请先松开右摇杆的正文", "进入调整步骤时摇杆已经偏转，提示回中后再主动推动。");
                Field(balance, "StopMessage", "请先停车的正文", "释放扳机并停车后再练习，避免行驶惯性被计为主动调整重心。");
                balanceStatusesExpanded = EditorGUILayout.Foldout(balanceStatusesExpanded, "通用状态反馈", true);
                if (balanceStatusesExpanded)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Field(balance, "WaitingStatus", "等待操作");
                        Field(balance, "ReadingStatus", "阅读提示");
                        Field(balance, "BlockedStatus", "暂时无法练习");
                        Field(balance, "NeedStowStatus", "请先收起机械臂");
                        Field(balance, "NeutralStatus", "请先松开右摇杆");
                        Field(balance, "StopStatus", "请先停车");
                        Field(balance, "CompleteStatus", "教学完成");
                    }
            }
        }

        private void DrawArms(SerializedProperty arms)
        {
            if (arms == null) return;
            using (new EditorGUI.IndentLevelScope())
            {
                Field(arms, "Title", "章节标题");
                DrawStep(arms, "Deploy", "1 · 展开机械臂");
                DrawStep(arms, "Extend", "2 · 向前伸出");
                DrawStep(arms, "RetractReach", "3 · 向内收回");
                DrawStep(arms, "SwingLeft", "4 · 向左摆动");
                DrawStep(arms, "SwingRight", "5 · 向右摆动");
                DrawStep(arms, "HoldStill", "6 · 松开摇杆并保持");
                DrawStep(arms, "Stow", "7 · 收起机械臂");
                DrawStep(arms, "Completed", "机械臂教学完成");
                armsRemindersExpanded = EditorGUILayout.Foldout(armsRemindersExpanded, "展开 / 收起提示", true);
                if (armsRemindersExpanded)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Field(arms, "ToggleHint", "持续操作提醒", "仅机械臂章节显示的顶部一行提醒；底部方向键提示保持不变。");
                        Field(arms, "ReopenMessage", "中途收起后继续教学", "练习尚未结束时收起机械臂，使用此正文提示重新展开。");
                        Field(arms, "InitialStowMessage", "进入教程时已经展开", "先收起再重新展开，确保玩家实际完成一次展开操作。");
                        Field(arms, "InitialStowAction", "首次收起的操作指令");
                        Field(arms, "StowRecoveryMessage", "自动收起后的恢复提示", "拍照或翻倒等系统行为自动收起机械臂时，提示玩家重新展开后亲自收起。");
                    }
                armsStatusesExpanded = EditorGUILayout.Foldout(armsStatusesExpanded, "通用状态反馈", true);
                if (armsStatusesExpanded)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Field(arms, "WaitingStatus", "等待操作");
                        Field(arms, "BlockedStatus", "暂时无法练习");
                        Field(arms, "ReopenStatus", "请重新展开");
                        Field(arms, "EmptyHandsStatus", "请保持空手");
                        Field(arms, "CompleteStatus", "教学完成");
                    }
            }
        }

        private static void Field(SerializedProperty parent, string name, string caption, string tooltip = null)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(caption, tooltip), true);
        }

        private void DrawDiscovery(SerializedProperty discovery)
        {
            if (discovery == null) return;
            using (new EditorGUI.IndentLevelScope())
            {
                Field(discovery, "Title", "章节标题");
                DrawStep(discovery, "Charge", "1 · 长按充能");
                DrawStep(discovery, "Release", "2 · 充满后松开发出扫描");
                DrawStep(discovery, "FindAnimal", "3 · 寻找未见过的啄木鸟");
                DrawStep(discovery, "ApproachAnimal", "目标超出扫描范围时的指引");
                DrawStep(discovery, "ConfirmAnimal", "4 · 亲眼确认并识别实心标记");
                DrawStep(discovery, "ReadObject", "5 · 识别可交互物标记");
                DrawStep(discovery, "Completed", "生物与交互物扫描完成");
                Field(discovery, "ToggleHint", "持续操作提醒", "本章顶部的长按 / 松开说明，{lb} 会替换为当前手柄按键。");
                Field(discovery, "NeedRescanMessage", "尚未找到目标时的恢复正文", "只在尚未观察到教学目标时提供恢复操作，不包含刷新标记教学。");
                Field(discovery, "NeedStowMessage", "请先收起机械臂的正文", "机械臂展开或持物时，提示先恢复扫描可用状态。");
                discoveryStatusesExpanded = EditorGUILayout.Foldout(discoveryStatusesExpanded, "通用状态反馈", true);
                if (discoveryStatusesExpanded)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Field(discovery, "WaitingStatus", "等待操作");
                        Field(discovery, "PreparingStatus", "正在准备附近目标");
                        Field(discovery, "CancelledStatus", "扫描已取消");
                        Field(discovery, "WrongGestureStatus", "手势不正确");
                        Field(discovery, "NeedRescanStatus", "请重新扫描");
                        Field(discovery, "NeedEmptyHandsStatus", "请保持空手");
                        Field(discovery, "BlockedStatus", "暂时无法练习");
                        Field(discovery, "CompleteStatus", "教学完成");
                    }
            }
        }

        private void DrawPhotography(SerializedProperty photography)
        {
            if (photography == null) return;
            using (new EditorGUI.IndentLevelScope())
            {
                Field(photography, "Title", "章节标题");
                DrawStep(photography, "ObserveMovement", "1 · 观察啄木鸟");
                DrawStep(photography, "EnterPhoto", "2 · 进入拍照模式");
                DrawStep(photography, "FrameSubject", "3 · 调整车辆位置与构图");
                DrawStep(photography, "Capture", "4 · 长按快门完成拍摄");
                DrawStep(photography, "SavePhoto", "5 · 保存照片后的返回提示", initiallyExpanded: false);
                EditorGUILayout.HelpBox("照片结算界面中完全隐藏教学任务栏，使用照片系统本身的操作提示。保存步骤文案只用于离开结算界面后的恢复提示。", MessageType.None);
                DrawStep(photography, "ExitPhoto", "6 · 返回驾驶");
                DrawStep(photography, "Completed", "拍照教学完成");
                Field(photography, "ToggleHint", "持续操作提醒", "本章顶部的一行提醒；手柄按键占位符会随当前手柄替换。");
                photographyRemindersExpanded = EditorGUILayout.Foldout(photographyRemindersExpanded, "恢复与重试正文", true);
                if (photographyRemindersExpanded)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Field(photography, "TargetMissingMessage", "寻找目标的正文");
                        Field(photography, "NeedStowMessage", "请先收起机械臂的正文");
                        Field(photography, "NeedRescanMessage", "重新扫描的正文");
                        Field(photography, "RetryPhotoMessage", "未拍到教学啄木鸟的正文");
                        Field(photography, "CapturedMessage", "拍摄成功与鸟飞走的正文");
                        Field(photography, "RecoverySaveMessage", "关闭结算界面后补存照片的正文");
                    }
                photographyStatusesExpanded = EditorGUILayout.Foldout(photographyStatusesExpanded, "通用状态反馈", true);
                if (photographyStatusesExpanded)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        Field(photography, "WaitingStatus", "等待操作");
                        Field(photography, "BlockedStatus", "暂时无法练习");
                        Field(photography, "TargetMissingStatus", "跟随啄木鸟指引");
                        Field(photography, "NeedStowStatus", "请先收起机械臂");
                        Field(photography, "NeedRescanStatus", "请重新扫描");
                        Field(photography, "RetryPhotoStatus", "请重拍教学啄木鸟");
                        Field(photography, "SavedStatus", "照片已保存");
                        Field(photography, "CapturedStatus", "已拍到啄木鸟");
                        Field(photography, "CompleteStatus", "教学完成");
                    }
            }
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

    [CustomEditor(typeof(TutorialArmLesson))]
    [CanEditMultipleObjects]
    public sealed class TutorialArmLessonTextEditor : TutorialLessonTextInspector { }

    [CustomEditor(typeof(TutorialBalanceLesson))]
    [CanEditMultipleObjects]
    public sealed class TutorialBalanceLessonTextEditor : TutorialLessonTextInspector { }

    [CustomEditor(typeof(TutorialDiscoveryLesson))]
    [CanEditMultipleObjects]
    public sealed class TutorialDiscoveryLessonTextEditor : TutorialLessonTextInspector { }

    [CustomEditor(typeof(TutorialPhotographyLesson))]
    [CanEditMultipleObjects]
    public sealed class TutorialPhotographyLessonTextEditor : TutorialLessonTextInspector { }
}

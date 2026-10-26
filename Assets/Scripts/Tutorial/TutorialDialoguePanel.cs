using System;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Tutorial
{
    /// <summary>
    /// Scene-owned tutorial presentation. It neither advances lessons nor owns gameplay input or pause.
    /// All visual resources and the document are created as children and released with this component.
    /// </summary>
    [DefaultExecutionOrder(1800)]
    [DisallowMultipleComponent]
    public sealed class TutorialDialoguePanel : MonoBehaviour
    {
        private const float DesignWidth = 1920f;
        private const float DesignHeight = 1080f;
        private const float PanelWidth = 355f;
        private const float PanelHeight = 565f;
        private static readonly Color Gold = new Color(212f / 255f, 184f / 255f, 84f / 255f);
        private static readonly Color Muted = new Color(.72f, .74f, .74f);

        [Tooltip("Optional; copied at runtime. The shared HUD settings are never changed.")]
        [SerializeField] private PanelSettings panelSettingsTemplate;
        [Tooltip("Optional CJK-capable font. Windows uses Microsoft YaHei when this is unassigned.")]
        [SerializeField] private Font dialogueFont;
        [SerializeField] private bool startCollapsed;

        private GameObject documentObject;
        private UIDocument document;
        private PanelSettings runtimeSettings;
        private Font runtimeFont;
        private VisualElement root, expanded, collapsedTab, progressFill;
        private PanelFrame frame;
        private ScanStarGlyph controlStar;
        private Label titleLabel, messageLabel, controlLabel, actionLabel, detailLabel;
        private Label statusLabel, chapterLabel, tabProgressLabel, percentLabel;
        private Label headerLabel, currentLessonLabel, currentActionLabel, toggleHintLabel;
        private Label tabArrowsLabel, tabHintLabel, guideLabel;
        private Label lessonHintLabel;
        private Label continueLabel;
        private bool continueReady;
        private int continueReadyFrame;
        private int reopenedFrame;
        private string continuePrompt = "Press A to continue";
        private VisualElement collapseControl;
        private TutorialPanelText panelText = new TutorialPanelText();
        private string lessonHint = string.Empty;
        private string contextLabel = string.Empty;
        private bool visible = true;
        private bool showDuringPhotoMode;
        private bool showDuringTutorialPause;
        private bool collapsed;
        private bool successful;
        private float progress;
        private int chapter = 1, chapters = 8;
        private string title = "Driving Practice", message = "Let's practice driving your robot. Follow each prompt to get started.";
        private string controlGlyph = "L", action = "Push left stick", detail = "Follow the prompts", status = "Awaiting input";

        public bool IsCollapsed => collapsed;
        public bool IsVisible => visible;
        public UIDocument Document => document;
        public VisualElement Root => root;
        public bool CanContinue => continueReady && visible && !collapsed && isActiveAndEnabled;
        public bool ContinueWasPresented => CanContinue && Time.frameCount > continueReadyFrame && Time.frameCount > reopenedFrame;
        public bool ShowDuringPhotoMode => showDuringPhotoMode;
        public bool ShowDuringTutorialPause => showDuringTutorialPause;
        public event Action<bool> CollapsedChanged;

        /// <summary>Content only; presentation visibility and confirmation input are restored separately.</summary>
        public sealed class ContentSnapshot
        {
            internal string Title, Message, ControlGlyph, Action, Detail, Status, ContextLabel, LessonHint;
            internal int Chapter, Chapters;
            internal float Progress;
            internal bool Successful, ShowDuringPhotoMode;
            internal TutorialPanelText PanelText;
        }

        public ContentSnapshot CaptureContent() => new ContentSnapshot
        {
            Title = title, Message = message, ControlGlyph = controlGlyph, Action = action,
            Detail = detail, Status = status, ContextLabel = contextLabel, LessonHint = lessonHint,
            Chapter = chapter, Chapters = chapters, Progress = progress,
            Successful = successful, ShowDuringPhotoMode = showDuringPhotoMode, PanelText = panelText
        };

        public void RestoreContent(ContentSnapshot snapshot)
        {
            if (snapshot == null) return;
            title = snapshot.Title; message = snapshot.Message; controlGlyph = snapshot.ControlGlyph;
            action = snapshot.Action; detail = snapshot.Detail; status = snapshot.Status;
            contextLabel = snapshot.ContextLabel; lessonHint = snapshot.LessonHint;
            chapter = snapshot.Chapter; chapters = snapshot.Chapters; panelText = snapshot.PanelText;
            showDuringPhotoMode = snapshot.ShowDuringPhotoMode;
            // A content snapshot cannot retain permission to display over a later pause.
            showDuringTutorialPause = false;
            SetContinuePrompt(false);
            RefreshPanelText(); RefreshContent(); RefreshLessonHint();
            SetProgress(snapshot.Progress); SetSuccess(snapshot.Successful); RefreshVisibility();
        }

        private void Awake()
        {
            collapsed = startCollapsed || AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels;
            BuildDocument();
        }

        private void OnEnable()
        {
            if (documentObject != null) documentObject.SetActive(true);
            TutorialContinueInput.Register(this);
        }

        private void OnDisable()
        {
            if (documentObject != null) documentObject.SetActive(false);
            TutorialContinueInput.Unregister(this);
        }

        /// <summary>A completed action stays on its own card until the player confirms it.</summary>
        public void SetContinuePrompt(bool ready, string text = null)
        {
            if (ready && !continueReady) continueReadyFrame = Time.frameCount;
            continueReady = ready;
            continuePrompt = text ?? "Press A to continue";
            RefreshContinuePrompt();
        }

        /// <summary>Replace the current instruction. New content keeps the player's collapsed preference.</summary>
        public void SetContent(string title, string message, string controlGlyph, string action,
            string detail, string status, int chapter = 1, int chapters = 8)
        {
            this.title = title ?? string.Empty;
            this.message = message ?? string.Empty;
            this.controlGlyph = controlGlyph ?? string.Empty;
            this.action = action ?? string.Empty;
            this.detail = detail ?? string.Empty;
            this.status = status ?? string.Empty;
            this.chapters = Mathf.Max(1, chapters);
            this.chapter = Mathf.Clamp(chapter, 1, this.chapters);
            contextLabel = string.Empty;
            RefreshContent();
        }

        /// <summary>A temporary contextual lesson replaces the numeric chapter without changing the main sequence.</summary>
        public void SetContextLabel(string value)
        {
            contextLabel = value ?? string.Empty;
            RefreshChapterLabels();
        }

        public void SetProgress(float normalized)
        {
            progress = Mathf.Clamp01(normalized);
            if (progressFill != null) progressFill.style.width = Length.Percent(progress * 100f);
            if (percentLabel != null) percentLabel.text = Mathf.RoundToInt(progress * 100f) + "%";
            if (!string.IsNullOrEmpty(contextLabel)) RefreshChapterLabels();
        }

        /// <summary>Update fixed panel copy without replacing the document or any lesson/fold state.</summary>
        public void SetPanelText(TutorialPanelText text)
        {
            panelText = text ?? new TutorialPanelText();
            RefreshPanelText();
        }

        /// <summary>Show an optional chapter-specific reminder without changing the standard footer.</summary>
        public void SetLessonHint(string text)
        {
            lessonHint = text ?? string.Empty;
            RefreshLessonHint();
        }

        public void SetSuccess(bool value)
        {
            successful = value;
            if (statusLabel != null) statusLabel.style.color = successful ? Gold : Muted;
            if (frame != null) { frame.Successful = value; frame.MarkDirtyRepaint(); }
        }

        public void SetVisible(bool value)
        {
            if (value && !collapsed && AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels) SetCollapsed(true);
            if (value && !visible) reopenedFrame = Time.frameCount;
            visible = value;
            RefreshVisibility();
        }

        public void SetPhotoModeVisible(bool value)
        {
            showDuringPhotoMode = value;
            RefreshVisibility();
        }

        /// <summary>Only this scene's owned landing lesson can remain visible during its world pause.</summary>
        public void SetTutorialPauseVisible(bool value)
        {
            showDuringTutorialPause = value;
            RefreshVisibility();
        }

        public void ToggleCollapsed() => SetCollapsed(!collapsed);

        public void SetCollapsed(bool value)
        {
            if (!value && AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels) return;
            if (collapsed == value) return;
            collapsed = value;
            if (!value) reopenedFrame = Time.frameCount;
            RefreshVisibility();
            CollapsedChanged?.Invoke(value);
        }

        private void BuildDocument()
        {
            // Use a private document and a private settings instance; this never changes MainUI's tree,
            // PanelSettings resource, time scale, cursor lock, EventSystem or action maps.
            PanelSettings template = panelSettingsTemplate != null ? panelSettingsTemplate
                : Resources.Load<PanelSettings>("UI/Main/MainUISetting");
            runtimeSettings = template != null ? Instantiate(template) : ScriptableObject.CreateInstance<PanelSettings>();
            runtimeSettings.name = "Tutorial Dialogue (Runtime Settings)";
            runtimeSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            runtimeSettings.scale = 1f;
            runtimeSettings.targetTexture = null;
            runtimeSettings.sortingOrder = 35;
            runtimeSettings.hideFlags = HideFlags.DontSave;

            documentObject = new GameObject("Tutorial Dialogue (Runtime)");
            documentObject.transform.SetParent(transform, false);
            document = documentObject.AddComponent<UIDocument>();
            document.panelSettings = runtimeSettings;
            document.sortingOrder = 0;
            BuildTree();
        }

        private void BuildTree()
        {
            root = document.rootVisualElement;
            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.overflow = Overflow.Hidden;

            Font font = dialogueFont != null ? dialogueFont : Resources.Load<Font>("UI/Font/Orbitron-Regular");
            if (font == null)
            {
                // This is scene-local and does not import or modify any project font resources.
                if (runtimeFont == null)
                    runtimeFont = Font.CreateDynamicFontFromOSFont(new[]
                    {
                        "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Noto Sans CJK SC",
                        "PingFang SC", "Arial Unicode MS", "Arial"
                    }, 28);
                font = runtimeFont;
            }
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromFont(font);

            expanded = new VisualElement { name = "TutorialPanel", pickingMode = PickingMode.Position };
            SetRect(expanded, 0, 0, PanelWidth, PanelHeight);
            expanded.style.transformOrigin = new TransformOrigin(0, 0, 0);
            // Only the card and its small collapsed tab participate in pointer picking.
            expanded.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            root.Add(expanded);
            frame = new PanelFrame();
            FillParent(frame);
            expanded.Add(frame);

            var header = new VisualElement { name = "CollapseTutorial", pickingMode = PickingMode.Position,
                tooltip = panelText.CollapseTooltip };
            collapseControl = header;
            SetRect(header, 16, 16, PanelWidth - 32, 86);
            header.RegisterCallback<ClickEvent>(evt => { SetCollapsed(true); evt.StopPropagation(); });
            expanded.Add(header);
            headerLabel = AddLabel(header, panelText.Header, 11, 27, 244, 41, 27, Gold, true);
            headerLabel.name = "TutorialHeader";
            AddLabel(header, "›", 287, 16, 24, 46, 30, Gold);
            // The existing space below the header fits a single optional reminder without moving
            // the lesson body, input instructions, standard D-pad footer, or the outer frame.
            lessonHintLabel = AddLabel(expanded, lessonHint, 27, 88, 301, 21, 14, Muted);
            lessonHintLabel.name = "TutorialLessonHint";

            AddLine(expanded, 27, 112, PanelWidth - 54, new Color(.8f, .82f, .81f, .65f));
            currentLessonLabel = AddLabel(expanded, panelText.CurrentLesson, 27, 128, 180, 26, 16, Muted);
            currentLessonLabel.name = "TutorialCurrentLesson";
            chapterLabel = AddLabel(expanded, "01 / 08", 221, 128, 107, 27, 18, Color.white);
            chapterLabel.name = "TutorialChapter";
            chapterLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            titleLabel = AddLabel(expanded, title, 27, 163, 301, 48, 32, Gold, true);
            titleLabel.name = "TutorialTitle";
            messageLabel = AddLabel(expanded, message, 27, 231, 301, 137, 24, Color.white);
            messageLabel.name = "TutorialMessage";
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            AddLine(expanded, 27, 376, PanelWidth - 54, new Color(.8f, .82f, .81f, .65f));
            currentActionLabel = AddLabel(expanded, panelText.CurrentAction, 27, 390, 220, 26, 16, Muted);
            currentActionLabel.name = "TutorialCurrentAction";

            var key = new ControlBadge();
            SetRect(key, 29, 420, 74, 70);
            expanded.Add(key);
            controlLabel = AddLabel(expanded, controlGlyph, 33, 422, 66, 65, 22, Color.white, true);
            controlLabel.name = "TutorialControl";
            controlLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            controlStar = new ScanStarGlyph { name = "TutorialControlStar" };
            SetRect(controlStar, 47, 437, 38, 38);
            expanded.Add(controlStar);
            actionLabel = AddLabel(expanded, action, 121, 421, 207, 34, 23, Color.white, true);
            actionLabel.name = "TutorialAction";
            detailLabel = AddLabel(expanded, detail, 121, 457, 207, 45, 16, Muted);
            detailLabel.name = "TutorialDetail";
            detailLabel.style.whiteSpace = WhiteSpace.Normal;
            var statusDot = new VisualElement { pickingMode = PickingMode.Ignore };
            SetRect(statusDot, 28, 515, 9, 9);
            statusDot.style.backgroundColor = Gold;
            statusDot.style.borderTopLeftRadius = statusDot.style.borderTopRightRadius = 5;
            statusDot.style.borderBottomLeftRadius = statusDot.style.borderBottomRightRadius = 5;
            expanded.Add(statusDot);
            statusLabel = AddLabel(expanded, status, 47, 505, 231, 25, 17, Muted);
            statusLabel.name = "TutorialStatus";
            continueLabel = AddLabel(expanded, continuePrompt, 47, 505, 231, 25, 17, Gold, true);
            continueLabel.name = "TutorialContinuePrompt";
            percentLabel = AddLabel(expanded, "0%", 280, 505, 47, 25, 16, Gold);
            percentLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            var progressTrack = new VisualElement { pickingMode = PickingMode.Ignore };
            SetRect(progressTrack, 27, 534, 301, 2);
            progressTrack.style.backgroundColor = new Color(.5f, .5f, .5f, .3f);
            expanded.Add(progressTrack);
            progressFill = new VisualElement { pickingMode = PickingMode.Ignore };
            progressFill.style.height = 2;
            progressFill.style.backgroundColor = Gold;
            progressTrack.Add(progressFill);
            // This help has its own persistent row; lesson status/feedback never replaces it.
            toggleHintLabel = AddLabel(expanded, panelText.CollapseHint, 27, 538, 301, 22, 15, Muted);
            toggleHintLabel.name = "TutorialToggleHint";

            collapsedTab = new VisualElement { name = "ExpandTutorial", pickingMode = PickingMode.Position,
                tooltip = panelText.ExpandTooltip };
            SetRect(collapsedTab, 0, 0, 58, 218);
            collapsedTab.style.transformOrigin = new TransformOrigin(0, 0, 0);
            collapsedTab.RegisterCallback<ClickEvent>(evt => { SetCollapsed(false); evt.StopPropagation(); });
            collapsedTab.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            root.Add(collapsedTab);
            var tabFrame = new PanelFrame { IsTab = true };
            FillParent(tabFrame);
            collapsedTab.Add(tabFrame);
            tabArrowsLabel = AddLabel(collapsedTab, panelText.ExpandArrows, 7, 10, 44, 26, 16, Gold);
            tabArrowsLabel.name = "TutorialTabToggleArrows";
            tabArrowsLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            tabHintLabel = AddLabel(collapsedTab, panelText.ExpandHint, 9, 36, 40, 20, 12, Muted);
            tabHintLabel.name = "TutorialTabToggleHint";
            tabHintLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            guideLabel = AddLabel(collapsedTab, panelText.GuideLabel, 17, 58, 24, 93, 13, Gold, true);
            guideLabel.name = "TutorialGuideLabel";
            guideLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            tabProgressLabel = AddLabel(collapsedTab, "01\n/08", 10, 162, 38, 45, 16, Color.white);
            tabProgressLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            RefreshPanelText();
            RefreshLessonHint();
            RefreshContent();
            RefreshContinuePrompt();
            SetProgress(progress);
            SetSuccess(successful);
            RefreshVisibility();
        }

        private void RefreshContent()
        {
            if (titleLabel == null) return;
            titleLabel.text = title;
            messageLabel.text = message;
            bool star = controlGlyph == "✧" || controlGlyph == "✦";
            controlLabel.text = star ? string.Empty : controlGlyph;
            if (controlStar != null)
            {
                controlStar.style.display = star ? DisplayStyle.Flex : DisplayStyle.None;
                controlStar.Filled = controlGlyph == "✦";
                controlStar.MarkDirtyRepaint();
            }
            controlLabel.style.fontSize = controlGlyph.Length > 3 ? 17 : 22;
            actionLabel.text = action;
            detailLabel.text = detail;
            statusLabel.text = status;
            RefreshChapterLabels();
            RefreshContinuePrompt();
        }

        private void RefreshChapterLabels()
        {
            if (chapterLabel == null || tabProgressLabel == null) return;
            currentLessonLabel.style.width = string.IsNullOrEmpty(contextLabel) ? 180f : 301f;
            currentLessonLabel.text = string.IsNullOrEmpty(contextLabel) ? panelText.CurrentLesson : contextLabel;
            chapterLabel.style.display = string.IsNullOrEmpty(contextLabel) ? DisplayStyle.Flex : DisplayStyle.None;
            chapterLabel.text = string.IsNullOrEmpty(contextLabel)
                ? chapter.ToString("00") + " / " + chapters.ToString("00") : string.Empty;
            tabProgressLabel.text = string.IsNullOrEmpty(contextLabel)
                ? chapter.ToString("00") + "\n/" + chapters.ToString("00")
                : Mathf.RoundToInt(progress * 100f) + "\n%";
        }

        private void RefreshContinuePrompt()
        {
            if (continueLabel == null || statusLabel == null) return;
            continueLabel.text = continuePrompt;
            continueLabel.style.display = continueReady ? DisplayStyle.Flex : DisplayStyle.None;
            statusLabel.style.display = continueReady ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RefreshPanelText()
        {
            if (headerLabel == null) return;
            headerLabel.text = panelText.Header ?? string.Empty;
            currentLessonLabel.text = panelText.CurrentLesson ?? string.Empty;
            currentActionLabel.text = panelText.CurrentAction ?? string.Empty;
            toggleHintLabel.text = panelText.CollapseHint ?? string.Empty;
            tabHintLabel.text = panelText.ExpandHint ?? string.Empty;
            tabArrowsLabel.text = panelText.ExpandArrows ?? string.Empty;
            guideLabel.text = panelText.GuideLabel ?? string.Empty;
            collapseControl.tooltip = panelText.CollapseTooltip ?? string.Empty;
            collapsedTab.tooltip = panelText.ExpandTooltip ?? string.Empty;
            RefreshChapterLabels();
        }

        private void RefreshLessonHint()
        {
            if (lessonHintLabel == null) return;
            lessonHintLabel.text = lessonHint;
            lessonHintLabel.style.display = string.IsNullOrWhiteSpace(lessonHint) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static void FitText(Label label, int preferredSize, int minimumSize)
        {
            if (label == null || label.panel == null || label.resolvedStyle.display == DisplayStyle.None) return;
            Vector2 box = label.contentRect.size;
            float measuredFontSize = label.resolvedStyle.fontSize;
            if (box.x <= 0f || box.y <= 0f || measuredFontSize <= 0f) return;
            bool wrap = label.resolvedStyle.whiteSpace == WhiteSpace.Normal;
            int fittedSize = minimumSize;
            for (int candidate = preferredSize; candidate >= minimumSize; candidate--)
            {
                // Measure against the currently resolved font without resetting it first. Scaling
                // the wrap width predicts the candidate size even before UI Toolkit resolves styles.
                float scale = measuredFontSize / candidate;
                Vector2 measured = label.MeasureTextSize(label.text, wrap ? (box.x - 1f) * scale : 0f,
                    wrap ? VisualElement.MeasureMode.AtMost : VisualElement.MeasureMode.Undefined,
                    0f, VisualElement.MeasureMode.Undefined);
                if (measured.y <= (box.y - 1f) * scale && (wrap || measured.x <= (box.x - 1f) * scale))
                { fittedSize = candidate; break; }
            }
            if (Mathf.Abs(label.style.fontSize.value.value - fittedSize) > .01f) label.style.fontSize = fittedSize;
        }

        private void LateUpdate()
        {
            TutorialContinueInput.ObserveRelease();
            if (document == null) return;
            // UIDocument rebuilds its visual tree after re-enabling its GameObject.
            if (root != document.rootVisualElement || expanded == null || expanded.parent == null) BuildTree();
            RefreshVisibility();
            if (root == null) return;
            Vector2 size = root.layout.size;
            if (size.x <= 0 || size.y <= 0 || float.IsNaN(size.x) || float.IsNaN(size.y)) return;
            // Keep the authored card bounds and copy when a font has wider glyphs.
            // Only overflowing labels shrink; short replacement text restores its preferred size.
            FitText(titleLabel, 32, 24);
            FitText(messageLabel, 24, 18);
            FitText(actionLabel, 23, 16);
            FitText(detailLabel, 16, 13);
            FitText(statusLabel, 17, 12);
            FitText(continueLabel, 17, 12);
            float fit = Mathf.Min(size.x / DesignWidth, size.y / DesignHeight);
            // Same fit as MainUI: on ultrawide displays the card stays close to the right edge;
            // on narrow displays it remains outside the central 960-unit ring.
            expanded.style.scale = new Scale(new Vector3(fit, fit, 1));
            expanded.style.left = size.x - (PanelWidth + 78f) * fit;
            expanded.style.top = (size.y - DesignHeight * fit) * .5f + 264f * fit;
            collapsedTab.style.scale = new Scale(new Vector3(fit, fit, 1));
            collapsedTab.style.left = size.x - 88f * fit;
            collapsedTab.style.top = size.y * .5f - 109f * fit;
        }

        private void RefreshVisibility()
        {
            if (root == null) return;
            var recovery = showDuringTutorialPause ? GetComponentInParent<TutorialTipOverLesson>() : null;
            bool tutorialPause = recovery != null && recovery.gameObject.scene == gameObject.scene
                && gameObject.scene.path == TutorialDrivingLesson.ScenePath && recovery.OwnsInertiaPause;
            bool obscured = (Time.timeScale <= 0f && !tutorialPause) || AnimalGame.MainUI.MainUI.BlocksGameplay
                || (tutorialPause && AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels);
            HeightMapPlayerSceneBootstrap bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap != null)
                obscured |= ((!showDuringPhotoMode || tutorialPause) && bootstrap.photoMode != null && bootstrap.photoMode.IsActive)
                    || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing);
            root.style.display = visible && !obscured ? DisplayStyle.Flex : DisplayStyle.None;
            expanded.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            collapsedTab.style.display = collapsed ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnDestroy()
        {
            TutorialContinueInput.Unregister(this);
            // PreviewScene checks also dispose this component outside Play mode. These three
            // objects are exclusively created by this component; template/font assets are not owned.
            DestroyOwned(documentObject);
            DestroyOwned(runtimeSettings);
            DestroyOwned(runtimeFont);
            documentObject = null;
            runtimeSettings = null;
            runtimeFont = null;
        }

        private static void DestroyOwned(UnityEngine.Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(owned);
            else UnityEngine.Object.DestroyImmediate(owned);
        }

        private static Label AddLabel(VisualElement parent, string text, float x, float y,
            float width, float height, int fontSize, Color color, bool bold = false)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            SetRect(label, x, y, width, height);
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            parent.Add(label);
            return label;
        }

        private static void SetRect(VisualElement element, float x, float y, float width, float height)
        {
            element.style.position = Position.Absolute;
            element.style.left = x; element.style.top = y;
            element.style.width = width; element.style.height = height;
        }

        private static void FillParent(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.top = element.style.right = element.style.bottom = 0;
        }

        private static void AddLine(VisualElement parent, float x, float y, float width, Color color)
        {
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            SetRect(line, x, y, width, 1);
            line.style.backgroundColor = color;
            parent.Add(line);
        }

        private static void Stroke(Painter2D painter, params Vector2[] points)
        {
            if (points.Length < 2) return;
            painter.BeginPath(); painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            painter.Stroke();
        }

        private sealed class PanelFrame : VisualElement
        {
            public bool IsTab;
            public bool Successful;

            public PanelFrame()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                float w = contentRect.width, h = contentRect.height;
                if (w <= 0 || h <= 0) return;
                float cut = IsTab ? 10 : 22;
                Painter2D p = context.painter2D;
                p.fillColor = new Color(.015f, .021f, .021f, .93f);
                p.BeginPath(); p.MoveTo(new Vector2(cut, 1));
                p.LineTo(new Vector2(w - cut, 1)); p.LineTo(new Vector2(w - 1, cut));
                p.LineTo(new Vector2(w - 1, h - cut)); p.LineTo(new Vector2(w - cut, h - 1));
                p.LineTo(new Vector2(cut, h - 1)); p.LineTo(new Vector2(1, h - cut));
                p.LineTo(new Vector2(1, cut)); p.ClosePath(); p.Fill();
                p.strokeColor = new Color(.91f, .93f, .92f, .95f); p.lineWidth = 1.6f;
                p.Stroke();
                p.strokeColor = Gold; p.lineWidth = 2.3f;
                Stroke(p, new Vector2(1, 42), new Vector2(1, cut), new Vector2(cut, 1), new Vector2(IsTab ? w - cut : 77, 1));
                Stroke(p, new Vector2(w - (IsTab ? 22 : 72), h - 1), new Vector2(w - cut, h - 1),
                    new Vector2(w - 1, h - cut), new Vector2(w - 1, h - (IsTab ? 33 : 46)));
                if (Successful && !IsTab)
                {
                    p.lineWidth = 2;
                    Stroke(p, new Vector2(w - 36, 92), new Vector2(w - 30, 98), new Vector2(w - 19, 85));
                }
            }
        }

        // Native four-point stars match the scan legend without relying on OS dingbat fonts.
        private sealed class ScanStarGlyph : VisualElement
        {
            public bool Filled;
            public ScanStarGlyph()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += context =>
                {
                    Painter2D p = context.painter2D;
                    p.strokeColor = p.fillColor = Gold;
                    p.lineWidth = 1.6f;
                    p.BeginPath(); p.MoveTo(new Vector2(19, 2));
                    p.BezierCurveTo(new Vector2(17, 14), new Vector2(14, 17), new Vector2(2, 19));
                    p.BezierCurveTo(new Vector2(14, 21), new Vector2(17, 24), new Vector2(19, 36));
                    p.BezierCurveTo(new Vector2(21, 24), new Vector2(24, 21), new Vector2(36, 19));
                    p.BezierCurveTo(new Vector2(24, 17), new Vector2(21, 14), new Vector2(19, 2));
                    p.ClosePath();
                    if (Filled) p.Fill(); else p.Stroke();
                };
            }
        }

        private sealed class ControlBadge : VisualElement
        {
            public ControlBadge()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += context =>
                {
                    Painter2D p = context.painter2D;
                    p.strokeColor = new Color(.76f, .8f, .8f); p.lineWidth = 1.5f;
                    p.BeginPath(); p.Arc(new Vector2(37, 35), 32, 0, 360); p.Stroke();
                    p.strokeColor = Color.white; p.lineWidth = 1.8f;
                    p.BeginPath(); p.Arc(new Vector2(37, 35), 23, 0, 360); p.Stroke();
                    p.lineWidth = 1.5f;
                    Stroke(p, new Vector2(37, 0), new Vector2(37, 6));
                    Stroke(p, new Vector2(37, 64), new Vector2(37, 70));
                    Stroke(p, new Vector2(1, 35), new Vector2(8, 35));
                    Stroke(p, new Vector2(66, 35), new Vector2(73, 35));
                };
            }
        }
    }
}

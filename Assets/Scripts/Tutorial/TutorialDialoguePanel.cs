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
        private Label titleLabel, messageLabel, controlLabel, actionLabel, detailLabel;
        private Label statusLabel, chapterLabel, tabProgressLabel, percentLabel;
        private bool visible = true;
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
        public event Action<bool> CollapsedChanged;

        private void Awake()
        {
            collapsed = startCollapsed;
            BuildDocument();
        }

        private void OnEnable()
        {
            if (documentObject != null) documentObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (documentObject != null) documentObject.SetActive(false);
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
            RefreshContent();
        }

        public void SetProgress(float normalized)
        {
            progress = Mathf.Clamp01(normalized);
            if (progressFill != null) progressFill.style.width = Length.Percent(progress * 100f);
            if (percentLabel != null) percentLabel.text = Mathf.RoundToInt(progress * 100f) + "%";
        }

        public void SetSuccess(bool value)
        {
            successful = value;
            if (statusLabel != null) statusLabel.style.color = successful ? Gold : Muted;
            if (frame != null) { frame.Successful = value; frame.MarkDirtyRepaint(); }
        }

        public void SetVisible(bool value)
        {
            visible = value;
            RefreshVisibility();
        }

        public void ToggleCollapsed() => SetCollapsed(!collapsed);

        public void SetCollapsed(bool value)
        {
            if (collapsed == value) return;
            collapsed = value;
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

            Font font = dialogueFont;
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
                tooltip = "Tap D-pad Left / Right to hide" };
            SetRect(header, 16, 16, PanelWidth - 32, 86);
            header.RegisterCallback<ClickEvent>(evt => { SetCollapsed(true); evt.StopPropagation(); });
            expanded.Add(header);
            Label headerLabel = AddLabel(header, "TUTORIAL", 11, 27, 244, 41, 27, Gold, true);
            headerLabel.name = "TutorialHeader";
            AddLabel(header, "›", 287, 16, 24, 46, 30, Gold);

            AddLine(expanded, 27, 112, PanelWidth - 54, new Color(.8f, .82f, .81f, .65f));
            AddLabel(expanded, "CURRENT LESSON", 27, 128, 170, 26, 16, Muted);
            chapterLabel = AddLabel(expanded, "01 / 08", 221, 128, 107, 27, 18, Color.white);
            chapterLabel.name = "TutorialChapter";
            chapterLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            titleLabel = AddLabel(expanded, title, 27, 163, 301, 48, 32, Gold, true);
            titleLabel.name = "TutorialTitle";
            messageLabel = AddLabel(expanded, message, 27, 231, 301, 137, 24, Color.white);
            messageLabel.name = "TutorialMessage";
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            AddLine(expanded, 27, 376, PanelWidth - 54, new Color(.8f, .82f, .81f, .65f));
            AddLabel(expanded, "CURRENT ACTION", 27, 390, 220, 26, 16, Muted);

            var key = new ControlBadge();
            SetRect(key, 29, 420, 74, 70);
            expanded.Add(key);
            controlLabel = AddLabel(expanded, controlGlyph, 33, 422, 66, 65, 22, Color.white, true);
            controlLabel.name = "TutorialControl";
            controlLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
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
            Label toggleHint = AddLabel(expanded, "Tap D-pad ← / →  Hide", 27, 538, 301, 22, 15, Muted);
            toggleHint.name = "TutorialToggleHint";

            collapsedTab = new VisualElement { name = "ExpandTutorial", pickingMode = PickingMode.Position,
                tooltip = "Tap D-pad Left / Right to show" };
            SetRect(collapsedTab, 0, 0, 58, 218);
            collapsedTab.style.transformOrigin = new TransformOrigin(0, 0, 0);
            collapsedTab.RegisterCallback<ClickEvent>(evt => { SetCollapsed(false); evt.StopPropagation(); });
            collapsedTab.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            root.Add(collapsedTab);
            var tabFrame = new PanelFrame { IsTab = true };
            FillParent(tabFrame);
            collapsedTab.Add(tabFrame);
            Label tabArrows = AddLabel(collapsedTab, "← →", 7, 10, 44, 26, 16, Gold);
            tabArrows.name = "TutorialTabToggleArrows";
            tabArrows.style.unityTextAlign = TextAnchor.UpperCenter;
            Label tabHint = AddLabel(collapsedTab, "Show", 9, 36, 40, 20, 12, Muted);
            tabHint.name = "TutorialTabToggleHint";
            tabHint.style.unityTextAlign = TextAnchor.UpperCenter;
            Label tabTitle = AddLabel(collapsedTab, "G\nU\nI\nD\nE", 17, 58, 24, 93, 13, Gold, true);
            tabTitle.style.unityTextAlign = TextAnchor.UpperCenter;
            tabProgressLabel = AddLabel(collapsedTab, "01\n/08", 10, 162, 38, 45, 16, Color.white);
            tabProgressLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            RefreshContent();
            SetProgress(progress);
            SetSuccess(successful);
            RefreshVisibility();
        }

        private void RefreshContent()
        {
            if (titleLabel == null) return;
            titleLabel.text = title;
            messageLabel.text = message;
            controlLabel.text = controlGlyph;
            controlLabel.style.fontSize = controlGlyph.Length > 3 ? 17 : 22;
            actionLabel.text = action;
            detailLabel.text = detail;
            statusLabel.text = status;
            chapterLabel.text = chapter.ToString("00") + " / " + chapters.ToString("00");
            tabProgressLabel.text = chapter.ToString("00") + "\n/" + chapters.ToString("00");
        }

        private void LateUpdate()
        {
            if (document == null) return;
            // UIDocument rebuilds its visual tree after re-enabling its GameObject.
            if (root != document.rootVisualElement || expanded == null || expanded.parent == null) BuildTree();
            RefreshVisibility();
            if (root == null) return;
            Vector2 size = root.layout.size;
            if (size.x <= 0 || size.y <= 0 || float.IsNaN(size.x) || float.IsNaN(size.y)) return;
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
            bool obscured = Time.timeScale <= 0f || AnimalGame.MainUI.MainUI.BlocksGameplay;
            HeightMapPlayerSceneBootstrap bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap != null)
                obscured |= (bootstrap.photoMode != null && bootstrap.photoMode.IsActive)
                    || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing);
            root.style.display = visible && !obscured ? DisplayStyle.Flex : DisplayStyle.None;
            expanded.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            collapsedTab.style.display = collapsed ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnDestroy()
        {
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

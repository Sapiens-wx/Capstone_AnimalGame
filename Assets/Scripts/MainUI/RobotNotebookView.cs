using UnityEngine;
using UnityEngine.UIElements;
using static AnimalGame.MainUI.SideHudCard;

namespace AnimalGame.MainUI
{
    internal sealed class RobotNotebookView
    {
        private const float SlideDuration = .2f;
        private const float FaultBlinkPeriod = 1.15f;
        private const float FaultBlinkDimTime = .20f;
        private static readonly Color FrameCut = new(.015f, .021f, .021f, 1f);
        private static readonly Color FaultTrace = new(Gold.r, Gold.g, Gold.b, .42f);
        private static readonly Color CorruptText = new(Gold.r, Gold.g, Gold.b, .6f);
        private readonly VisualElement panel, tab, faultTraces;
        private readonly Label faultLog;
        private readonly FontDefinition font;
        private float slide;

        internal VisualElement Root { get; }

        internal RobotNotebookView(VisualElement root)
        {
            Root = root;
            font = FontDefinition.FromFont(Resources.Load<Font>("UI/Font/Orbitron-Regular"));
            tab = AddCard("RobotNotebookTab", TabWidth, TabHeight, isTab: true);
            AddChevron(tab, 25, 16, pointsRight: false);
            AddNotebookLabel(tab, "D-PAD", 3, 42, 52, 17, 10, Muted).style.unityTextAlign = TextAnchor.UpperCenter;
            AddNotebookLabel(tab, "G\nO\nA\nL", 12, 72, 34, 86, 15, Gold, true)
                .style.unityTextAlign = TextAnchor.UpperCenter;
            AddNotebookLabel(tab, "FAULT", 3, 173, 52, 20, 10, Gold)
                .style.unityTextAlign = TextAnchor.UpperCenter;

            panel = AddCard("RobotNotebookPanel", PanelWidth, PanelHeight, isTab: false);
            AddFaultBlock(panel, 0, 211, 8, 28, FrameCut);
            AddFaultBlock(panel, 5, 219, 2, 12, FaultTrace);
            AddFaultBlock(panel, PanelWidth - 8, 350, 8, 37, FrameCut);
            AddFaultBlock(panel, PanelWidth - 8, 351, 2, 12, FaultTrace);
            AddFaultBlock(panel, PanelWidth - 5, 374, 2, 9, FaultTrace);
            AddNotebookLabel(panel, "GOAL MODULE", 27, 31, 274, 38, 27, Gold, true)
                .name = "RobotNotebookHeader";
            AddChevron(panel, 303, 40, pointsRight: true);
            AddLine(panel, 27, 97, 176, Divider);
            AddLine(panel, 213, 99, 36, FaultTrace);
            AddLine(panel, 262, 97, 66, Divider);

            var directive = AddNotebookLabel(panel, "HISTORY DIRECTIVE —<b>WRU-04-017</b>:", 27, 130, 301, 32, 14, Gold);
            directive.name = "RobotGoalModuleDirective";
            directive.enableRichText = true;
            var instruction = AddNotebookLabel(panel, "Locate, collect, and recycle scattered waste (SIZE: SMALL-MEDIEUM).",
                27, 174, 301, 116, 20, Color.white);
            instruction.name = "RobotGoalModuleInstruction";
            instruction.style.whiteSpace = WhiteSpace.Normal;

            AddNotebookLabel(panel, "......", 27, 326, 301, 30, 20, Muted);
            AddNotebookLabel(panel, ".........", 27, 359, 301, 30, 20, Muted);
            faultLog = AddNotebookLabel(panel, "SYSTEM FAULT...", 27, 420, 301, 39, 23, Gold, true);
            faultLog.name = "RobotGoalModuleFaultLog";

            var corruption = new VisualElement { name = "RobotGoalModuleCorruption", pickingMode = PickingMode.Ignore };
            FillParent(corruption);
            panel.Add(corruption);
            AddNotebookLabel(corruption, "#?//", 282, 17, 51, 18, 11, CorruptText);
            AddNotebookLabel(corruption, "[%]", 313, 62, 30, 17, 10, Muted);
            AddNotebookLabel(corruption, "## / 0x?F    [%_]", 27, 291, 214, 20, 16, CorruptText);
            AddNotebookLabel(corruption, "?/_+  #09", 192, 309, 130, 20, 14, Muted);
            AddNotebookLabel(corruption, "%?//    [##_]", 107, 337, 211, 18, 14, CorruptText);
            AddNotebookLabel(corruption, "[_?] // 00%%  +/", 143, 371, 184, 20, 14, Muted);
            AddNotebookLabel(corruption, "/#?=   [__//]", 27, 388, 157, 19, 14, CorruptText);

            faultTraces = new VisualElement { name = "RobotGoalModuleFaultTraces", pickingMode = PickingMode.Ignore };
            FillParent(faultTraces);
            faultTraces.style.opacity = 0f;
            panel.Add(faultTraces);
            AddFaultBlock(faultTraces, 266, 70, 54, 2, FaultTrace);
            AddFaultBlock(faultTraces, 278, 74, 47, 1, Divider);
            AddFaultBlock(faultTraces, 228, 455, 100, 2, FaultTrace);
            AddFaultBlock(faultTraces, 249, 460, 63, 1, Divider);
        }

        internal void Tick(bool open, bool visible, float unscaledDeltaTime)
        {
            slide = visible ? Mathf.MoveTowards(slide, open ? 1f : 0f, unscaledDeltaTime / SlideDuration) : 0f;
            SetPresentation(Mathf.SmoothStep(0f, 1f, slide), visible ? 1f - slide : 0f);
        }

        internal void SetPresentation(float openness, float tabOpacity)
        {
            tab.style.display = tabOpacity > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            Vector2 size = Root.layout.size;
            float fit = ScreenFit(size);
            ShowPanel(openness, size, fit);
            if (tabOpacity <= 0f || !(fit > 0f)) return;
            PositionTab(tab, size, fit);
            tab.style.opacity = tabOpacity;
        }

        private void ShowPanel(float openness, Vector2 size, float fit)
        {
            panel.style.display = openness > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            if (openness <= 0f || !(fit > 0f)) return;

            PositionPanel(panel, size, fit, openness);
            float now = Time.unscaledTime;
            float phase = Mathf.Repeat(now, 4f);
            bool flicker = (phase >= .76f && phase < .85f) || (phase >= .94f && phase < 1.04f);
            faultTraces.style.opacity = flicker ? 1f : 0f;
            faultLog.style.opacity = Mathf.Repeat(now, FaultBlinkPeriod) < FaultBlinkDimTime ? .16f : 1f;
        }

        private VisualElement AddCard(string name, float width, float height, bool isTab)
        {
            var card = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            SetRect(card, 0, 0, width, height);
            card.style.transformOrigin = new TransformOrigin(0, 0, 0);
            card.style.display = DisplayStyle.None;
            var frame = new PanelFrame { IsTab = isTab };
            FillParent(frame);
            card.Add(frame);
            Root.Add(card);
            return card;
        }

        private Label AddNotebookLabel(VisualElement parent, string text, float x, float y, float width,
            float height, int fontSize, Color color, bool bold = false)
        {
            Label label = AddLabel(parent, text, x, y, width, height, fontSize, color, bold);
            label.style.unityFontDefinition = font;
            return label;
        }

        private static void AddFaultBlock(VisualElement parent, float x, float y, float width, float height, Color color)
        {
            var block = new VisualElement { pickingMode = PickingMode.Ignore };
            SetRect(block, x, y, width, height);
            block.style.backgroundColor = color;
            parent.Add(block);
        }

        private static void AddChevron(VisualElement parent, float x, float y, bool pointsRight)
        {
            var chevron = new VisualElement { pickingMode = PickingMode.Ignore };
            SetRect(chevron, x, y, 9f, 16f);
            chevron.generateVisualContent += context =>
            {
                Painter2D painter = context.painter2D;
                painter.strokeColor = Gold;
                painter.lineWidth = 1.8f;
                float tip = pointsRight ? 7f : 1f;
                float tail = pointsRight ? 1f : 7f;
                Stroke(painter, new Vector2(tail, 1f), new Vector2(tip, 8f), new Vector2(tail, 15f));
            };
            parent.Add(chevron);
        }
    }
}

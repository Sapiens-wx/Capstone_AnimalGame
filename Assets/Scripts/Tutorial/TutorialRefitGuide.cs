using System.Collections.Generic;
using AnimalGame.Demos.CheckpointRefit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Tutorial
{
    /// <summary>A scene-owned guide in the refit screen's existing header gap. Never owns refit selection or equipment.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1800)]
    public sealed class TutorialRefitGuide : MonoBehaviour
    {
        private sealed class TextOverride
        {
            public TextElement Element;
            public string Original, Written;
        }

        private static readonly Color Gold = new Color32(212, 184, 84, 255);
        private static readonly Color White = new Color32(237, 237, 237, 255);
        private readonly List<TextOverride> textOverrides = new();
        private CheckpointRefitTerminal terminal;
        private VisualElement boundRoot, stage, guide;
        private Label titleLabel, messageLabel, controlLabel;
        private string title = string.Empty, message = string.Empty, control = string.Empty;
        private bool requestedVisible, ready, presented;
        private int readyFrame = -1, visibleFrame = -1;

        public VisualElement Root => guide;
        public Label MessageLabel => messageLabel;
        public Label ControlLabel => controlLabel;
        public bool IsVisible => presented && isActiveAndEnabled && terminal != null && terminal.IsOpen
            && terminal.View != null && terminal.View.IsVisible && !terminal.View.IsInspectionOpen;
        public bool ContinueWasPresented => IsVisible && ready
            && Time.frameCount > readyFrame && Time.frameCount > visibleFrame;

        public bool Configure(CheckpointRefitTerminal target)
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath || target == null
                || target.gameObject.scene != gameObject.scene) return false;
            if (terminal != target)
            {
                ReleasePresentation();
                terminal = target;
            }
            Refresh();
            return true;
        }

        public void SetContent(string heading, string body, string controlHint, bool readyToContinue)
        {
            heading ??= string.Empty;
            body ??= string.Empty;
            controlHint ??= string.Empty;
            if (readyToContinue && (!ready || title != heading || message != body || control != controlHint))
                readyFrame = Time.frameCount;
            title = heading; message = body; control = controlHint; ready = readyToContinue;
            Refresh();
        }

        public void SetVisible(bool value)
        {
            if (value && !requestedVisible) visibleFrame = Time.frameCount;
            requestedVisible = value;
            Refresh();
        }

        public void Clear()
        {
            requestedVisible = ready = false;
            ReleasePresentation();
            terminal = null;
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (!isActiveAndEnabled || gameObject.scene.path != TutorialDrivingLesson.ScenePath || terminal == null
                || terminal.gameObject.scene != gameObject.scene)
            { ReleasePresentation(); return; }

            VisualElement nextRoot = terminal.View != null && terminal.View.Document != null
                ? terminal.View.Document.rootVisualElement : null;
            VisualElement nextStage = nextRoot?.Q<VisualElement>("refit-stage");
            if (nextRoot != boundRoot || nextStage != stage)
            {
                ReleasePresentation();
                boundRoot = nextRoot; stage = nextStage;
            }
            if (stage == null) return;

            EnsureGuide();
            RefreshGamepadText();
            bool show = requestedVisible && terminal.IsOpen && terminal.View.IsVisible && !terminal.View.IsInspectionOpen;
            if (show && !presented) visibleFrame = Time.frameCount;
            presented = show;
            guide.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            titleLabel.text = title;
            messageLabel.text = message;
            controlLabel.text = control;
            controlLabel.style.color = ready ? Gold : White;
            controlLabel.style.unityFontStyleAndWeight = ready ? FontStyle.Bold : FontStyle.Normal;
        }

        private void EnsureGuide()
        {
            if (guide != null) return;
            guide = new VisualElement { name = "TutorialRefitGuide", pickingMode = PickingMode.Ignore };
            guide.style.position = Position.Absolute;
            guide.style.left = 86; guide.style.right = 86; guide.style.top = 198; guide.style.height = 58;
            guide.style.backgroundColor = (Color)new Color32(12, 12, 12, 255);
            guide.style.borderLeftWidth = 3; guide.style.borderLeftColor = Gold;
            guide.style.borderBottomWidth = 1; guide.style.borderBottomColor = (Color)new Color32(61, 61, 61, 255);
            guide.style.overflow = Overflow.Hidden;
            var eyebrow = AddLabel("TutorialRefitEyebrow", 16, 4, 280, 20, 16, new Color32(217, 217, 210, 255));
            eyebrow.text = "TUTORIAL";
            titleLabel = AddLabel("TutorialRefitTitle", 16, 25, 280, 27, 20, Gold);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            messageLabel = AddLabel("TutorialRefitMessage", 315, 4, 1030, 50, 20, White);
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            messageLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            controlLabel = AddLabel("TutorialRefitControl", 1365, 4, 365, 50, 19, White);
            controlLabel.style.whiteSpace = WhiteSpace.Normal;
            controlLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            stage.Add(guide);
        }

        private Label AddLabel(string name, float left, float top, float width, float height, float size, Color color)
        {
            var label = new Label { name = name, pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = left; label.style.top = top; label.style.width = width; label.style.height = height;
            label.style.fontSize = size; label.style.color = color;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            guide.Add(label);
            return label;
        }

        private void RefreshGamepadText()
        {
            // These overrides belong to this active tutorial's UI clone. Native selection refreshes
            // can replace the equip caption, so patch only the known keyboard-bearing forms.
            Override(boundRoot.Q<Button>("refit-equip"), "A / ENTER   EQUIP", "A   EQUIP");
            Override(boundRoot.Q<Button>("refit-equip-footer"), "A / ENTER   EQUIP", "A   EQUIP");
            Override(boundRoot.Q<Button>("refit-inspect-footer"), "X / I   INSPECT", "X   INSPECT");
            Override(boundRoot.Q<Button>("refit-close"), "B / ESC   BACK", "B   BACK");
            Override(boundRoot.Q<Button>("refit-inspection-close"), "B / ESC   CLOSE", "B   CLOSE");
            Override(boundRoot.Q<Label>("refit-interaction-prompt"), "[Enter]  Maintenance", "[A]  Maintenance");
        }

        private void Override(TextElement element, string expected, string replacement)
        {
            if (element == null || element.text != expected) return;
            TextOverride saved = textOverrides.Find(value => value.Element == element);
            if (saved == null)
            {
                saved = new TextOverride { Element = element };
                textOverrides.Add(saved);
            }
            saved.Original = expected; saved.Written = replacement;
            element.text = replacement;
        }

        private void ReleasePresentation()
        {
            foreach (TextOverride saved in textOverrides)
                if (saved.Element != null && saved.Element.text == saved.Written) saved.Element.text = saved.Original;
            textOverrides.Clear();
            guide?.RemoveFromHierarchy();
            guide = boundRoot = stage = null;
            titleLabel = messageLabel = controlLabel = null;
            presented = false;
        }

        private void OnDisable() => ReleasePresentation();
        private void OnDestroy() => Clear();
    }
}

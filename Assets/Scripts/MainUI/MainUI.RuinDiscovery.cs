using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    public sealed partial class MainUI
    {
        private const float RuinNameDesignY = 228f;

        internal bool CanPresentRuinName => isActiveAndEnabled && !BlocksGameplay && Time.timeScale > 0f
            && stage != null && stage.panel != null && stage.resolvedStyle.visibility == Visibility.Visible
            && root.resolvedStyle.display != DisplayStyle.None && photoAlpha > 0f;

        internal Label CreateRuinName(string displayName)
        {
            var label = new Label(displayName) { name = "RuinDiscoveryName", pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = Length.Percent(50f);
            label.style.width = DesignWidth;
            label.style.height = 54f;
            label.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = 36f;
            label.style.color = SideHudCard.Gold;
            label.style.unityTextOutlineColor = Color.black;
            label.style.unityTextOutlineWidth = 1f;
            label.style.opacity = 0f;
            return label;
        }

        internal void PlaceRuinName(Label label)
        {
            if (label.parent != stage) stage.Add(label);
            label.style.top = viewportOffset.y + RuinNameDesignY * fit;
            label.style.scale = new Scale(new Vector3(fit, fit, 1f));
            label.style.display = DisplayStyle.Flex;
        }
    }
}

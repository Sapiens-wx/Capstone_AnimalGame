using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    public sealed partial class MainUI
    {
        internal bool TryPlaceInteractionHint(VisualElement prompt, Vector3 screenPoint)
        {
            if (BlocksGameplay || stage == null || stage.panel == null || screenPoint.z <= 0f
                || !TryGetRingScreenGeometry(out Vector2 center, out float radius)
                || ((Vector2)screenPoint - center).sqrMagnitude > radius * radius)
                return false;

            if (prompt.parent != stage) stage.Add(prompt);
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(stage.panel,
                new Vector2(screenPoint.x, Screen.height - screenPoint.y));
            Vector2 local = stage.WorldToLocal(panelPoint);
            prompt.style.left = local.x;
            prompt.style.top = local.y - 12f * fit;
            prompt.style.scale = new Scale(new Vector3(fit, fit, 1f));
            prompt.style.display = DisplayStyle.Flex;
            return true;
        }
    }
}

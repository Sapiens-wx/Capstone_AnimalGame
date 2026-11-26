using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.UIElements;
using GameMainUI = AnimalGame.MainUI.MainUI;

namespace AnimalGame.Interaction
{
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class WorldInteractionHint : MonoBehaviour
    {
        [SerializeField] private string keyLabel = "A";
        [SerializeField] private string actionLabel = "INTERACT";
        private VisualElement prompt;

        internal void SetActionLabel(string label) => actionLabel = label;

        public static void ReplaceLegacyPrompt(GameObject anchor)
        {
            if (anchor.TryGetComponent(out TextMesh text))
                text.GetComponent<MeshRenderer>().enabled = false;
            anchor.AddComponent<WorldInteractionHint>();
        }

        private void LateUpdate()
        {
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            GameMainUI hud = GameMainUI.Active;
            if (bootstrap != null && bootstrap.gameObject.scene == gameObject.scene && bootstrap.mapCamera != null
                && hud != null && hud.gameObject.scene == gameObject.scene)
            {
                prompt ??= CreatePrompt();
                Vector3 screenPoint = bootstrap.mapCamera.WorldToScreenPoint(transform.position);
                if (hud.TryPlaceInteractionHint(prompt, screenPoint)) return;
            }

            if (prompt != null) prompt.style.display = DisplayStyle.None;
        }

        private VisualElement CreatePrompt()
        {
            var row = new VisualElement { name = "WorldInteractionHint", pickingMode = PickingMode.Ignore };
            row.AddToClassList("driving-control");
            row.style.position = Position.Absolute;
            row.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(100f));
            row.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-100f));
            var input = new Label(keyLabel) { pickingMode = PickingMode.Ignore };
            input.AddToClassList("driving-control-key");
            input.AddToClassList("driving-control-key-wide");
            row.Add(input);
            var action = new Label(actionLabel) { pickingMode = PickingMode.Ignore };
            action.AddToClassList("driving-control-action");
            row.Add(action);
            return row;
        }

        private void OnDisable() => prompt?.RemoveFromHierarchy();
    }
}

using UnityEngine;
using GameMainUI = AnimalGame.MainUI.MainUI;

namespace AnimalGame.World
{
    [DefaultExecutionOrder(115)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class WarehouseConsole : MonoBehaviour
    {
        [SerializeField] private GameObject interactionPrompt;

        private WeatherTower tower;
        private BoxCollider2D interactionTrigger;
        private bool interactWasHeld;

        private void Awake()
        {
            tower = GetComponentInParent<WeatherTower>();
            interactionTrigger = GetComponent<BoxCollider2D>();
        }

        private void OnEnable() => interactWasHeld = WorldStationInteraction.IsInteractHeld();

        private void Update()
        {
            bool held = WorldStationInteraction.IsInteractHeld();
            bool pressed = held && !interactWasHeld;
            interactWasHeld = held;

            var hud = GameMainUI.Active;
            bool available = hud != null && hud.isActiveAndEnabled && hud.gameObject.scene == gameObject.scene
                && !hud.IsCargoLocked && WorldStationInteraction.CanUseGroundConsole(tower, interactionTrigger);
            if (available && pressed && WarehouseUI.Open(this, hud)) available = false;
            if (interactionPrompt.activeSelf != available) interactionPrompt.SetActive(available);
        }

        private void OnDisable()
        {
            interactionPrompt.SetActive(false);
            WarehouseUI.CloseFor(this);
        }
    }
}

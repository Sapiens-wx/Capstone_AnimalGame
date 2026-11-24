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

            bool available = CanInteract && (!tower.UsesStationPlugs || tower.CanPlug(WeatherTowerSocket.Cargo, tower.Player));
            if (available && pressed)
                available = !(tower.UsesStationPlugs
                    ? tower.TryPlug(WeatherTowerSocket.Cargo, tower.Player, this, TryOpen, () => WarehouseUI.IsOpenFor(this))
                    : TryOpen());
            if (interactionPrompt.activeSelf != available) interactionPrompt.SetActive(available);
        }

        private bool CanInteract
        {
            get
            {
                var hud = GameMainUI.Active;
                return isActiveAndEnabled && hud != null && hud.isActiveAndEnabled && hud.gameObject.scene == gameObject.scene
                    && WorldStationInteraction.CanUseGroundConsole(tower, interactionTrigger);
            }
        }

        private bool TryOpen() => CanInteract && WarehouseUI.Open(this, GameMainUI.Active);

        private void OnDisable()
        {
            interactionPrompt.SetActive(false);
            WarehouseUI.CloseFor(this);
        }
    }
}

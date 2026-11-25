using System;
using System.Collections.Generic;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using UnityEngine;
using GameMainUI = AnimalGame.MainUI.MainUI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.World
{
    [Serializable]
    public sealed class StationLogEntry
    {
        public string id;
        public string title;
        public string timestamp;
        [TextArea(3, 12)] public string body;
    }

    [DefaultExecutionOrder(115)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class StationLogConsole : MonoBehaviour
    {
        [SerializeField] private GameObject interactionPrompt;
        [SerializeField] private string stationId;
        [SerializeField] private string stationName;
        [SerializeField] private StationLogEntry[] entries;

        private BoxCollider2D interactionTrigger;
        private WeatherTower tower;
        private bool interactWasHeld;

        public string StationId => stationId;
        public string StationName => stationName;
        public IReadOnlyList<StationLogEntry> Entries => entries;
        internal bool HasPendingInteraction => !SaveSystem.IsStationSynced(stationId);

        public bool CanInteract => isActiveAndEnabled && WorldStationInteraction.CanUseGroundConsole(tower, interactionTrigger);

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
            bool available = CanInteract && (!tower.UsesStationPlugs || tower.CanPlug(WeatherTowerSocket.Log, tower.Player));
            if (available && pressed)
                available = !(tower.UsesStationPlugs
                    ? tower.TryPlug(WeatherTowerSocket.Log, tower.Player, this, Open, () => StationLogUI.IsOpenFor(this))
                    : TryOpen());
            SetPrompt(available);
        }

        public bool TryOpen() => Open(null);

        private bool Open(Vector3? socketWorld)
        {
            if (!CanInteract || !StationLogUI.Open(this, socketWorld)) return false;
            SetPrompt(false);
            return true;
        }

        private void OnDisable()
        {
            SetPrompt(false);
            StationLogUI.CloseFor(this);
        }

        private void SetPrompt(bool visible)
        {
            if (interactionPrompt.activeSelf != visible) interactionPrompt.SetActive(visible);
        }
    }

    internal static class WorldStationInteraction
    {
        internal static bool CanUseGroundConsole(WeatherTower tower, BoxCollider2D trigger)
        {
            RobotMover player = tower.Player;
            return tower.EquipmentInteractionReady && tower.Phase == WeatherTowerPhase.Ground && trigger.enabled
                && player != null && player.isActiveAndEnabled && player.TowerCollisionRoot != null
                && trigger.transform.IsChildOf(player.TowerCollisionRoot)
                && IsInRange(trigger, player) && CanUseControls(player, tower.UsesStationPlugs);
        }

        internal static bool IsInRange(BoxCollider2D trigger, RobotMover player)
        {
            // RobotMover uses the custom world query, so read the authored trigger directly.
            Vector2 local = (Vector2)trigger.transform.InverseTransformPoint(player.transform.position) - trigger.offset;
            Vector2 halfSize = trigger.size * .5f;
            return Mathf.Abs(local.x) <= halfSize.x && Mathf.Abs(local.y) <= halfSize.y;
        }

        internal static bool CanUseControls(RobotMover player, bool allowStationPlug = false)
        {
            if (Time.timeScale <= 0f || GameMainUI.Active == null || GameMainUI.BlocksGameplay
                || GameMainUI.Active.InventoryProgress > .001f || GameMainUI.Active.IsInventoryOpen
                || AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels
                || AnimalGame.Tutorial.TutorialTipOverLesson.BlocksGameplayForReading(player.gameObject)
                || player.IgnoreCollisionAndTerrain || player.IsMovementLocked
                || player.IsPhotoModeInputLocked)
                return false;

            RobotArmController arm = player.GetComponent<RobotArmController>();
            bool usePlug = allowStationPlug && arm != null && (arm.CanStartStationPlug || arm.StationPlugSeated);
            PhotoModeController photo = player.GetComponent<PhotoModeController>();
            RobotHeightMotionDetector height = player.GetComponent<RobotHeightMotionDetector>();
            PlayerCombatReceiver combat = player.GetComponent<PlayerCombatReceiver>();
            return (!player.IsArmInputCaptured || usePlug)
                && (arm == null || arm.State == RobotArmState.Retracted || usePlug)
                && (photo == null || (!photo.IsActive && !photo.IsInputLocked))
                && (height == null || !height.IsAirborne)
                && (combat == null || !combat.IsDisplaced);
        }

        internal static bool IsInteractHeld()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed)) return true;
#else
            if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)) return true;
#endif
            return AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld();
        }
    }
}

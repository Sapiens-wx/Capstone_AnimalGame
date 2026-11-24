using System;
using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.Interaction;
using AnimalGame.MainUI.Inventory;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Demos.CheckpointRefit
{
    /// <summary>Opt-in maintenance interaction. Owns only its UI instance and a temporary player-input lease.</summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class CheckpointRefitTerminal : MonoBehaviour
    {
        private const int RepairCost = 3;
        private const float RepairAmount = .30f;
        private const float RepairHoldSeconds = .5f;

        [SerializeField] private GameObject uiPrefab;
        [SerializeField] private string checkpointName = "Weather Tower";
        [SerializeField] private string checkpointId = "WX-03";
        [SerializeField, Min(.1f)] private float interactionRadiusMeters = 2.75f;
        [Tooltip("Optional explicit same-scene player. Otherwise binds the map player or the scene's only active RobotMover.")]
        [SerializeField] private RobotMover playerOverride;
        [SerializeField] private InteractionMarker interactionMarker;
        [SerializeField] private bool useTutorialInteractionHint;
        [SerializeField] private PhotoCameraDefinition zoomLens300;
        [SerializeField] private PhotoCameraDefinition damagedPrime50;
        [SerializeField] private InventoryItem metalScrap;

        private RobotMover player;
        private WeatherTower tower;
        private PlayerCombatReceiver repairReceiver;
        private MapTestSceneController map;
        private CheckpointRefitView view;
        private WorldInteractionHint tutorialHint;
        private GameObject uiInstance;
        private RefitGameplayLease lease;
        private RefitCameraEquipment cameraEquipment;
        private readonly RefitPlayerPreview playerPreview = new RefitPlayerPreview();
        private bool inputArmed;
        private bool releasing;
        private bool previousInteract, previousCancel, previousInspection;
        private bool previousShoulderLeft, previousShoulderRight;
        private bool repairRequiresRelease;
        private float repairHoldElapsed;
        private int previousNavigationX, previousNavigationY;
        private float repeatAt, nextSearch, nextPromptCheck;
        private bool promptAllowed;
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<RobotMover> robots = new List<RobotMover>();
        private readonly List<HeightMapPlayerSceneBootstrap> bootstraps = new List<HeightMapPlayerSceneBootstrap>();

        public RobotMover Player => player;
        public CheckpointRefitView View => view;
        public bool IsOpen { get; private set; }
        public bool IsNearby => PlayerPresent && DistanceToPlayer() <= interactionRadiusMeters;
        public float InteractionRadiusMeters => interactionRadiusMeters;
        public InteractionMarker Marker => interactionMarker;
        public RefitCameraEquipment CameraEquipment => cameraEquipment;
        /// <summary>Optional owner-local submit interception. Returning true handles this press without equipping.</summary>
        public Func<bool> TutorialSubmitHandler { get; set; }
        /// <summary>Optional owner-local gate while a nearby tutorial card owns the same interaction press.</summary>
        public Func<bool> TutorialOpenBlocked { get; set; }
        private bool PlayerPresent => player != null && player.gameObject.activeInHierarchy
            && player.gameObject.scene == gameObject.scene;

        public void Configure(GameObject prefab, string siteName = "Weather Tower", string siteId = "WX-03")
        {
            ForceClose();
            DestroyUi();
            uiPrefab = prefab;
            checkpointName = siteName;
            checkpointId = siteId;
        }

        public void Bind(RobotMover target)
        {
            ForceClose();
            playerOverride = target;
            SetPlayer(target != null && target.gameObject.scene == gameObject.scene ? target : null);
        }

        public void ConfigureMarker(InteractionMarker marker)
        {
            interactionMarker = marker;
            RefreshMarker();
        }

        public void ConfigureCameras(PhotoCameraDefinition zoom, PhotoCameraDefinition damaged)
        {
            zoomLens300 = zoom;
            damagedPrime50 = damaged;
        }

        public void ConfigureRepairMaterial(InventoryItem item) => metalScrap = item;

        private void BindCameraEquipment()
        {
            PhotoModeController controller = player.GetComponentInChildren<PhotoModeController>(true);
            cameraEquipment = null;
            if (controller != null)
            {
                cameraEquipment = player.GetComponent<RefitCameraEquipment>();
                if (cameraEquipment == null) cameraEquipment = player.gameObject.AddComponent<RefitCameraEquipment>();
                cameraEquipment.Configure(controller, zoomLens300);
                cameraEquipment.Unlock(damagedPrime50);
            }
            view.BindCameraEquipment(cameraEquipment, zoomLens300, damagedPrime50);
        }

        private void EquipSelectedCamera()
        {
            if (!IsOpen || releasing || lease == null || !PlayerPresent || view == null
                || view.IsInspectionOpen || view.SelectedSlotIndex != 1 || cameraEquipment == null) return;
            PhotoCameraDefinition selected = view.SelectedCamera;
            if (cameraEquipment.TryEquip(selected)) view.RefreshEquipment();
        }

        private void LateUpdate() => RefreshMarker();

        private void RefreshMarker()
        {
            // Closing input retains the gameplay lease after IsOpen becomes false.
            // Discovery rings yield to the nearby prompt. Query the live range rather than the cached prompt,
            // so closing-input release and crossing the boundary cannot produce a one-frame flash.
            interactionMarker?.SetSuppressed(!isActiveAndEnabled || IsOpen || lease != null || releasing || IsNearby);
        }

        private void Awake()
        {
            tower = GetComponentInParent<WeatherTower>();
            if (!useTutorialInteractionHint) return;
            tutorialHint = gameObject.AddComponent<WorldInteractionHint>();
            tutorialHint.enabled = false;
        }

        private void Start()
        {
            ResolvePlayer();
            EnsureUi();
        }

        private void OnEnable()
        {
            inputArmed = false;
            nextSearch = nextPromptCheck = 0f;
            if (uiInstance != null) uiInstance.SetActive(true);
            RefreshMarker();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            if (lease != null && !PlayerPresent) ForceClose();
            if (lease == null && (!PlayerPresent || !player.isActiveAndEnabled))
            {
                if (Time.unscaledTime >= nextSearch)
                {
                    nextSearch = Time.unscaledTime + .25f;
                    ResolvePlayer();
                }
            }

            bool interact = InteractHeld(), cancel = CancelHeld(), inspection = InspectionHeld();
            bool interactPressed = interact && !previousInteract;
            bool cancelPressed = cancel && !previousCancel;
            bool inspectionPressed = inspection && !previousInspection;
            previousInteract = interact;
            previousCancel = cancel;
            previousInspection = inspection;

            bool repairPagePressed = false;
            bool refitPagePressed = false;
            bool pageInputHeld = false;
            if (IsOpen || releasing)
            {
                ReadPageInput(out bool left, out bool right);
                repairPagePressed = left && !previousShoulderLeft;
                refitPagePressed = right && !previousShoulderRight;
                pageInputHeld = left || right;
                previousShoulderLeft = left;
                previousShoulderRight = right;
            }

            if (releasing)
            {
                if (!interact && !cancel && !inspection && !pageInputHeld)
                {
                    ReleaseLease();
                    inputArmed = true;
                }
                UpdatePrompt(false);
                return;
            }

            if (!inputArmed)
            {
                inputArmed = !interact && !cancel && !inspection;
                if (!inputArmed) { UpdatePrompt(false); return; }
            }

            if (IsOpen)
            {
                if (view == null || !view.isActiveAndEnabled) { ForceClose(); return; }
                if (cancelPressed)
                {
                    if (!view.CloseInspection()) Close();
                    return;
                }
                bool pageRequested = repairPagePressed != refitPagePressed && view.RequestPage(repairPagePressed);
                if (pageRequested || view.IsPageSwitchPending)
                {
                    repairRequiresRelease = interact;
                    ResetRepairHold();
                    return;
                }
                if (view.IsRepairPage)
                {
                    UpdateRepair(interact);
                    return;
                }
                if (inspectionPressed) view.OpenInspection();
                if (!view.IsInspectionOpen)
                {
                    Navigate();
                    if (interactPressed && !(TutorialSubmitHandler?.Invoke() ?? false)) view.RequestEquipSelected();
                }
                return;
            }

            UpdatePrompt(true);
            if (!interactPressed) return;
            if (tower != null && tower.UsesStationPlugs)
            {
                if (CanOpen() && tower.TryPlug(WeatherTowerSocket.Refit, player, this, () => OpenRefit(true), () => lease != null))
                    SetPrompt(false);
            }
            else TryOpen();
        }

        private bool CanOpen() => Application.isPlaying && isActiveAndEnabled && !IsOpen && lease == null && IsNearby
            && !(TutorialOpenBlocked?.Invoke() ?? false) && EnsureUi() && RefitGameplayLease.CanAcquire(player, out _);

        public bool TryOpen() => OpenRefit(false);

        private bool OpenRefit(bool keepStationPlug)
        {
            if (!CanOpen()) return false;
            try
            {
                BindCameraEquipment();
                // The lease disables arm behaviours, so snapshot initialized artwork first.
                playerPreview.Capture(player);
                if (!RefitGameplayLease.TryAcquire(player, out lease, out _, keepStationPlug))
                {
                    ClearPlayerPreview();
                    return false;
                }
                IsOpen = true;
                inputArmed = false;
                SetPrompt(false);
                view.SetPlayerPreview(playerPreview.Texture);
                view.Show(checkpointName, checkpointId);
                RefreshRepairStatus();
                previousNavigationX = previousNavigationY = 0;
                ReadPageInput(out previousShoulderLeft, out previousShoulderRight);
                repairRequiresRelease = false;
                repairHoldElapsed = 0f;
                repeatAt = 0f;
                return true;
            }
            catch (Exception error)
            {
                ForceClose();
                Debug.LogException(error, this);
                return false;
            }
        }

        private (float integrity, float power, int cargo, string message) ReadRepairStatus()
        {
            var hud = repairReceiver != null ? repairReceiver.Hud : null;
            float integrity = repairReceiver != null ? repairReceiver.Integrity : float.NaN;
            int cargo = hud != null && metalScrap != null ? hud.Inventory.Count(metalScrap) : 0;
            string message = hud == null || metalScrap == null ? "Repair unavailable."
                : integrity >= 1f ? "Integrity is full."
                : cargo < RepairCost ? "Not enough Metal Scrap."
                : null;
            return (integrity, hud != null ? hud.Power : float.NaN, cargo, message);
        }

        private void RefreshRepairStatus()
        {
            var (integrity, power, cargo, message) = ReadRepairStatus();
            float after = float.IsNaN(integrity) ? float.NaN : Mathf.Min(1f, integrity + RepairAmount);
            view.SetRepairStatus(integrity, power, after, cargo, null, RepairCost,
                metalScrap != null ? metalScrap.Image : null, message == null, message);
        }

        private void UpdateRepair(bool interact)
        {
            if (!interact && !view.IsRepairPointerHeld)
            {
                repairRequiresRelease = false;
                ResetRepairHold();
                return;
            }
            if (repairRequiresRelease) return;

            if (ReadRepairStatus().message != null)
            {
                ResetRepairHold();
                RefreshRepairStatus();
                repairRequiresRelease = true;
                return;
            }

            repairHoldElapsed = Mathf.Min(RepairHoldSeconds, repairHoldElapsed + Time.unscaledDeltaTime);
            view.SetRepairHoldProgress(repairHoldElapsed / RepairHoldSeconds);
            if (repairHoldElapsed < RepairHoldSeconds) return;

            repairRequiresRelease = true;
            repairReceiver.TryRepairIntegrity(metalScrap, RepairCost, RepairAmount);
            RefreshRepairStatus();
        }

        private void ResetRepairHold()
        {
            if (repairHoldElapsed == 0f) return;
            repairHoldElapsed = 0f;
            view.SetRepairHoldProgress(0f);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            view?.Hide();
            SetPrompt(false);
            ClearPlayerPreview();
            // Keep gameplay isolated until maintenance inputs are released.
            releasing = true;
            inputArmed = false;
            nextPromptCheck = 0f;
        }

        private void ForceClose()
        {
            IsOpen = false;
            view?.Hide();
            SetPrompt(false);
            ClearPlayerPreview();
            ReleaseLease();
            inputArmed = false;
        }

        private void ReleaseLease()
        {
            lease?.Dispose();
            lease = null;
            releasing = false;
        }

        private void ClearPlayerPreview()
        {
            view?.SetPlayerPreview(null);
            playerPreview.Clear();
        }

        private bool EnsureUi()
        {
            if (view != null) return true;
            if (uiPrefab == null) return false;
            uiInstance = Instantiate(uiPrefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(uiInstance, gameObject.scene);
            uiInstance.name = "Checkpoint Refit UI (runtime only)";
            uiInstance.hideFlags = HideFlags.DontSave;
            view = uiInstance.GetComponent<CheckpointRefitView>();
            if (view == null)
            {
                Debug.LogError("Checkpoint refit UI prefab must contain CheckpointRefitView.", this);
                DestroyUi();
                return false;
            }
            view.CloseRequested += Close;
            view.EquipRequested += EquipSelectedCamera;
            view.Hide();
            return true;
        }

        private void DestroyUi()
        {
            if (view != null)
            {
                view.CloseRequested -= Close;
                view.EquipRequested -= EquipSelectedCamera;
            }
            view = null;
            if (uiInstance == null) return;
            uiInstance.SetActive(false);
            if (Application.isPlaying) Destroy(uiInstance);
            else DestroyImmediate(uiInstance);
            uiInstance = null;
        }

        private void UpdatePrompt(bool allow)
        {
            if (!EnsureUi()) return;
            bool nearby = allow && IsNearby && (tower == null || !tower.UsesStationPlugs || tower.CanPlug(WeatherTowerSocket.Refit, player));
            if (nearby && Time.unscaledTime >= nextPromptCheck)
            {
                nextPromptCheck = Time.unscaledTime + .15f;
                promptAllowed = RefitGameplayLease.CanAcquire(player, out _);
            }
            if (!nearby) { nextPromptCheck = 0f; promptAllowed = false; }
            SetPrompt(nearby && promptAllowed);
        }

        private void SetPrompt(bool visible)
        {
            if (tutorialHint != null)
            {
                tutorialHint.enabled = visible;
                return;
            }

            string label = visible ? (HasGamepad() ? "[A]  Maintenance" : "[Enter]  Maintenance") : string.Empty;
            view?.SetPrompt(label, visible);
        }

        private float DistanceToPlayer()
        {
            if (map != null && map.HasGeneratedMap
                && map.TrySampleWorldPosition(player.transform.position, out Vector2 p, out _)
                && map.TrySampleWorldPosition(transform.position, out Vector2 terminal, out _))
                return Vector2.Distance(p, terminal);
            return Vector2.Distance(player.transform.position, transform.position);
        }

        private void SetPlayer(RobotMover target)
        {
            player = target;
            repairReceiver = target != null ? target.GetComponent<PlayerCombatReceiver>() : null;
            map = null;
            promptAllowed = false;
            nextPromptCheck = 0f;
            if (target == null) return;
            gameObject.scene.GetRootGameObjects(roots);
            foreach (GameObject root in roots)
            {
                root.GetComponentsInChildren(false, bootstraps);
                foreach (HeightMapPlayerSceneBootstrap bootstrap in bootstraps)
                    if (bootstrap.robot == target) { map = bootstrap.map; return; }
            }
        }

        private void ResolvePlayer()
        {
            if (playerOverride != null)
            {
                SetPlayer(playerOverride.gameObject.scene == gameObject.scene ? playerOverride : null);
                return;
            }
            gameObject.scene.GetRootGameObjects(roots);
            foreach (GameObject root in roots)
            {
                root.GetComponentsInChildren(false, bootstraps);
                foreach (HeightMapPlayerSceneBootstrap bootstrap in bootstraps)
                    if (bootstrap.isActiveAndEnabled && bootstrap.robot != null && bootstrap.robot.isActiveAndEnabled
                        && bootstrap.robot.gameObject.scene == gameObject.scene)
                    { SetPlayer(bootstrap.robot); return; }
            }
            RobotMover candidate = null;
            foreach (GameObject root in roots)
            {
                root.GetComponentsInChildren(false, robots);
                foreach (RobotMover robot in robots)
                {
                    if (!robot.isActiveAndEnabled) continue;
                    if (candidate != null && candidate != robot) { SetPlayer(null); return; }
                    candidate = robot;
                }
            }
            SetPlayer(candidate);
        }

        private void Navigate()
        {
            Vector2 axis = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                axis.x = (Keyboard.current.rightArrowKey.isPressed ? 1 : 0) - (Keyboard.current.leftArrowKey.isPressed ? 1 : 0);
                axis.y = (Keyboard.current.upArrowKey.isPressed ? 1 : 0) - (Keyboard.current.downArrowKey.isPressed ? 1 : 0);
            }
            foreach (Gamepad pad in Gamepad.all)
            {
                if (!pad.added) continue;
                Vector2 gamepadAxis = pad.dpad.ReadValue();
                if (gamepadAxis.sqrMagnitude < .1f) gamepadAxis = pad.leftStick.ReadValue();
                if (gamepadAxis.sqrMagnitude > axis.sqrMagnitude) axis = gamepadAxis;
            }
#else
            axis.x = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            axis.y = (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
#endif
            int x = Mathf.Abs(axis.x) >= .55f ? (axis.x > 0 ? 1 : -1) : 0;
            int y = Mathf.Abs(axis.y) >= .55f ? (axis.y > 0 ? 1 : -1) : 0;
            bool changed = x != previousNavigationX || y != previousNavigationY;
            if ((x != 0 || y != 0) && (changed || Time.unscaledTime >= repeatAt))
            {
                if (y != 0) view.NavigateSlot(-y);
                else view.NavigateModule(x);
                repeatAt = Time.unscaledTime + (changed ? .35f : .12f);
            }
            previousNavigationX = x;
            previousNavigationY = y;
        }

        private static void ReadPageInput(out bool left, out bool right)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            left = keyboard != null && keyboard.qKey.isPressed;
            right = keyboard != null && keyboard.eKey.isPressed;
            if (Gamepad.all.Count > 0)
            {
                foreach (Gamepad pad in Gamepad.all)
                {
                    if (!pad.added) continue;
                    left |= pad.leftShoulder.isPressed;
                    right |= pad.rightShoulder.isPressed;
                }
                return;
            }
#else
            left = Input.GetKey(KeyCode.Q);
            right = Input.GetKey(KeyCode.E);
#endif
            left |= Input.GetKey(KeyCode.JoystickButton4);
            right |= AdaptiveLegacyGamepadInput.IsRightShoulderHeld();
        }

        private static bool HasGamepad()
        {
#if ENABLE_INPUT_SYSTEM
            return Gamepad.all.Count > 0;
#else
            foreach (string joystick in Input.GetJoystickNames()) if (!string.IsNullOrEmpty(joystick)) return true;
            return false;
#endif
        }

        private static bool InteractHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && (Keyboard.current.enterKey.isPressed || Keyboard.current.numpadEnterKey.isPressed)) return true;
#else
            if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)) return true;
#endif
            return AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld();
        }

        private static bool CancelHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.isPressed) return true;
            foreach (Gamepad pad in Gamepad.all) if (pad.added && pad.buttonEast.isPressed) return true;
            if (Gamepad.all.Count > 0) return false;
#else
            if (Input.GetKey(KeyCode.Escape)) return true;
#endif
            return Input.GetKey(AdaptiveLegacyGamepadInput.ActiveFamily == LegacyGamepadFamily.Sony
                ? KeyCode.JoystickButton2 : KeyCode.JoystickButton1);
        }

        private static bool InspectionHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.iKey.isPressed) return true;
            foreach (Gamepad pad in Gamepad.all) if (pad.added && pad.buttonWest.isPressed) return true;
            if (Gamepad.all.Count > 0) return false;
#else
            if (Input.GetKey(KeyCode.I)) return true;
#endif
            return Input.GetKey(AdaptiveLegacyGamepadInput.ActiveFamily == LegacyGamepadFamily.Sony
                ? KeyCode.JoystickButton0 : KeyCode.JoystickButton2);
        }

        private void OnDisable()
        {
            interactionMarker?.SetSuppressed(true);
            ForceClose();
            if (uiInstance != null) uiInstance.SetActive(false);
        }

        private void OnDestroy() { ForceClose(); DestroyUi(); }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.83f, .72f, .33f, .65f);
            Gizmos.DrawWireSphere(transform.position, interactionRadiusMeters);
        }
    }
}

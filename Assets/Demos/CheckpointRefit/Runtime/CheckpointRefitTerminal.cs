using System;
using System.Collections.Generic;
using AnimalGame.Interaction;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
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
        [SerializeField] private GameObject uiPrefab;
        [SerializeField] private string checkpointName = "Weather Tower";
        [SerializeField] private string checkpointId = "WX-03";
        [SerializeField, Min(.1f)] private float interactionRadiusMeters = 2.75f;
        [Tooltip("Optional explicit same-scene player. Otherwise binds the map player or the scene's only active RobotMover.")]
        [SerializeField] private RobotMover playerOverride;
        [SerializeField] private InteractionMarker interactionMarker;

        private RobotMover player;
        private MapTestSceneController map;
        private CheckpointRefitView view;
        private GameObject uiInstance;
        private RefitGameplayLease lease;
        private readonly RefitPlayerPreview playerPreview = new RefitPlayerPreview();
        private bool inputArmed;
        private bool releasing;
        private bool previousInteract, previousCancel, previousInspection;
        private bool previousShoulderLeft, previousShoulderRight;
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

        private void LateUpdate() => RefreshMarker();

        private void RefreshMarker()
        {
            // Closing input retains the gameplay lease after IsOpen becomes false.
            interactionMarker?.SetSuppressed(!isActiveAndEnabled || IsOpen || lease != null || releasing);
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

            if (releasing)
            {
                if (!interact && !cancel && !inspection)
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
                if (inspectionPressed) view.OpenInspection();
                if (!view.IsInspectionOpen) Navigate();
                // The demo displays the initial configuration; A/Enter performs no equip action.
                return;
            }

            UpdatePrompt(true);
            if (interactPressed) TryOpen();
        }

        public bool TryOpen()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || IsOpen || lease != null || !IsNearby)
                return false;
            if (!EnsureUi() || !RefitGameplayLease.CanAcquire(player, out _)) return false;
            try
            {
                // The lease disables arm behaviours, so snapshot initialized artwork first.
                playerPreview.Capture(player);
                if (!RefitGameplayLease.TryAcquire(player, out lease, out _))
                {
                    ClearPlayerPreview();
                    return false;
                }
                IsOpen = true;
                inputArmed = false;
                view.SetPrompt(string.Empty, false);
                view.SetPlayerPreview(playerPreview.Texture);
                view.Show(checkpointName, checkpointId);
                previousNavigationX = previousNavigationY = 0;
                previousShoulderLeft = previousShoulderRight = false;
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

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            view?.Hide();
            view?.SetPrompt(string.Empty, false);
            ClearPlayerPreview();
            // Keep gameplay isolated until the closing A/B/Enter/Escape press is released.
            releasing = true;
            inputArmed = false;
            nextPromptCheck = 0f;
        }

        private void ForceClose()
        {
            IsOpen = false;
            if (view != null) { view.Hide(); view.SetPrompt(string.Empty, false); }
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
            view.Hide();
            return true;
        }

        private void DestroyUi()
        {
            if (view != null) view.CloseRequested -= Close;
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
            bool nearby = allow && IsNearby;
            if (nearby && Time.unscaledTime >= nextPromptCheck)
            {
                nextPromptCheck = Time.unscaledTime + .15f;
                promptAllowed = RefitGameplayLease.CanAcquire(player, out _);
            }
            if (!nearby) { nextPromptCheck = 0f; promptAllowed = false; }
            view.SetPrompt(HasGamepad() ? "[A]  Maintenance" : "[Enter]  Maintenance", nearby && promptAllowed);
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
            bool shoulderLeft = false, shoulderRight = false;
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
                shoulderLeft |= pad.leftShoulder.isPressed;
                shoulderRight |= pad.rightShoulder.isPressed;
            }
#else
            axis.x = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            axis.y = (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
            shoulderLeft = Input.GetKey(KeyCode.JoystickButton4);
            shoulderRight = Input.GetKey(KeyCode.JoystickButton5);
#endif
            if (shoulderLeft && !previousShoulderLeft) view.NavigateSlot(-1);
            else if (shoulderRight && !previousShoulderRight) view.NavigateSlot(1);
            previousShoulderLeft = shoulderLeft;
            previousShoulderRight = shoulderRight;
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

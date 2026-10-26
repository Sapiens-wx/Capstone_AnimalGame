using AnimalGame.MapTest;
using AnimalGame.MainUI.Inventory;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GameMainUI = AnimalGame.MainUI.MainUI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Tutorial
{
    /// <summary>Scene-owned small-waste recycling followed by native large-pile pulling and fragment recycling.</summary>
    [DefaultExecutionOrder(1550)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TutorialPhotographyLesson), typeof(TutorialLargeWasteTarget))]
    public sealed class TutorialRecyclingLesson : MonoBehaviour
    {
        [SerializeField] private HeightMapPlayerSceneBootstrap bootstrap;
        [SerializeField] private TutorialTextContent tutorialText;
        [SerializeField] private Sprite targetSprite;
        [SerializeField, Min(.5f)] private float approachRadiusMeters = 4f;
        [SerializeField, Min(0f)] private float discoveryCompletionDelay = .5f;
        [SerializeField, Min(5f)] private float recoveryDistanceMeters = 15f;

        private readonly RecyclingLessonText defaultText = new RecyclingLessonText();
        private readonly TutorialPanelText defaultPanelText = new TutorialPanelText();
        private TutorialDiscoveryLesson discovery;
        private TutorialPhotographyLesson photography;
        private TutorialDiscoveryTargets targets;
        private TutorialLargeWasteTarget largeTargets;
        private RecyclingTutorialProgress progress;
        private TutorialWaypointIndicator waypoint;
        private WorldInteraction targetWaste;
        private WorldInteraction targetFragment;
        private bool hasStarted, chapterConfirmed, acceptedRecycle, previousToggleHeld;
        private bool acceptedFragmentRecycle;
        private bool ownsPresentation = true;
        private bool interrupted;
        private float completionDelay;
        private RecyclingTutorialStep displayedStep = (RecyclingTutorialStep)(-1);
        private string practiceStatus;
        private InletOutline inletOutline;
        private GameMainUI inventoryUi;
        private readonly Dictionary<InventoryItem, int> inventorySnapshot = new Dictionary<InventoryItem, int>();
        private readonly HashSet<InventoryItem> recoveredMaterials = new HashSet<InventoryItem>();
        private readonly List<InventoryItem> visibleMaterials = new List<InventoryItem>();

        public TutorialTextContent TextContent => tutorialText;
        public TutorialDiscoveryTargets Targets => targets;
        public TutorialDiscoveryLesson Discovery => discovery;
        public TutorialDialoguePanel Panel => discovery != null ? discovery.Panel : null;
        public RecyclingTutorialProgress Progress => progress;
        public TutorialWaypointIndicator Waypoint => waypoint;
        public WorldInteraction TargetWaste => targetWaste;
        public TutorialLargeWasteTarget LargeTargets => largeTargets;
        public WorldInteraction TargetLargeWaste => largeTargets != null ? largeTargets.Target : null;
        public WorldInteraction TargetFragment => targetFragment;
        public float ApproachRadiusMeters => approachRadiusMeters;
        public bool HasStarted => hasStarted;
        public bool IsComplete => hasStarted && progress != null && progress.IsComplete;
        public bool IsInventoryTeaching => isActiveAndEnabled && hasStarted && ownsPresentation
            && progress != null && progress.IsInventoryStep;
        public string CurrentPracticeStatus => practiceStatus;
        public bool IsInletCueVisible => inletOutline != null && inletOutline.style.display.value != DisplayStyle.None;
        public void SetPresentationOwned(bool value)
        {
            ownsPresentation = value;
            if (!value) ClearInventoryPresentation();
            if (!value && inletOutline != null) inletOutline.style.display = DisplayStyle.None;
        }
        private RecyclingLessonText Text => tutorialText != null && tutorialText.Recycling != null ? tutorialText.Recycling : defaultText;
        private TutorialPanelText PanelText => tutorialText != null ? tutorialText.Panel : defaultPanelText;
        private bool IsLargePractice => progress != null && (progress.Step == RecyclingTutorialStep.ReachLargeWaste
            || progress.Step == RecyclingTutorialStep.RecycleLargeWaste);

        private void Start()
        {
            if (gameObject.scene.path != TutorialDrivingLesson.ScenePath) { enabled = false; return; }
            discovery = GetComponent<TutorialDiscoveryLesson>();
            photography = GetComponent<TutorialPhotographyLesson>();
            targets = GetComponent<TutorialDiscoveryTargets>();
            largeTargets = GetComponent<TutorialLargeWasteTarget>();
            if (bootstrap == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                { bootstrap = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>(); if (bootstrap != null) break; }
            if (largeTargets != null)
            {
                largeTargets.Configure(bootstrap);
                largeTargets.Broken += OnLargeWasteBroken;
            }
            previousToggleHeld = AdaptiveLegacyGamepadInput.IsLeftStickButtonHeld();
        }

        private void LateUpdate()
        {
            if (TutorialTipOverLesson.BlocksMainline(gameObject))
            {
                ClearInventoryPresentation();
                if (!interrupted) progress?.Suspend();
                interrupted = true;
                previousToggleHeld = AdaptiveLegacyGamepadInput.IsLeftStickButtonHeld();
                if (waypoint != null) waypoint.SetVisible(false);
                if (inletOutline != null) inletOutline.style.display = DisplayStyle.None;
                return;
            }
            if (interrupted)
            {
                interrupted = false;
                progress?.Suspend();
                previousToggleHeld = AdaptiveLegacyGamepadInput.IsLeftStickButtonHeld();
            }
            bool toggleHeld = AdaptiveLegacyGamepadInput.IsLeftStickButtonHeld();
            bool togglePressed = toggleHeld && !previousToggleHeld;
            previousToggleHeld = toggleHeld;
            if (discovery == null || photography == null || targets == null || Panel == null || bootstrap == null || bootstrap.robot == null) return;
            if (!photography.IsComplete)
            {
                if (hasStarted) ReleasePresentation();
                completionDelay = 0f; return;
            }
            if (!ownsPresentation) return;
            if (IsComplete) { HideCompletedPresentation(); return; }
            EnsureInventoryContext();
            if (IsInventoryTeaching) { UpdateInventoryTeaching(); return; }
            bool modal = IsModal();
            if (!modal && !acceptedRecycle) EnsureWaste();
            if (!hasStarted)
            {
                if (modal || !CanPractice() || (targetWaste == null && !acceptedRecycle)) return;
                BeginLesson();
            }
            if (!modal && IsLargePractice && !acceptedFragmentRecycle) EnsureLargeWaste();
            Panel.SetPhotoModeVisible(false); Panel.SetVisible(!modal);
            if (waypoint != null) waypoint.SetVisible(!modal);
            if (modal || !CanPractice())
            {
                progress.Suspend(); Panel.SetContinuePrompt(false);
                if (inletOutline != null) inletOutline.style.display = DisplayStyle.None;
                if (!modal) RefreshPanel(false);
                return;
            }
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null && (Gamepad.current.dpad.left.wasPressedThisFrame
                || Gamepad.current.dpad.right.wasPressedThisFrame)) Panel.ToggleCollapsed();
#endif
            RobotArmController arm = bootstrap.armController;
            bool holding = (targetWaste != null && arm.HeldObject == targetWaste)
                || (targetFragment != null && arm.HeldObject == targetFragment);
            bool holdingLarge = TargetLargeWaste != null && arm.HeldObject == TargetLargeWaste;
            bool processing = arm.State == RobotArmState.Recycling;
            bool deployed = arm.IsArmModeActive && arm.VisibleDeployment01 >= .999f && !processing;
            bool retracted = !arm.IsArmModeActive && arm.State == RobotArmState.Retracted && arm.VisibleDeployment01 <= .001f;
            bool atWaste = targetWaste != null && DistanceTo(targetWaste) <= approachRadiusMeters;
            if (acceptedRecycle) progress.RegisterRecycled(true);
            if (largeTargets != null && largeTargets.HasBroken) progress.RegisterLargeWasteBroken(true);
            if (acceptedFragmentRecycle) progress.RegisterFragmentRecycled(true);
            progress.Sample(new RecyclingTutorialSample(Time.deltaTime, true, atWaste, IsStopped(),
                deployed, retracted, holding, arm.IsRecycleReady && holding, processing,
                AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld(),
                (togglePressed && !arm.IsArmModeActive) || arm.ManualRetractRequestedThisFrame,
                largeTargets != null && (largeTargets.HasBroken || (TargetLargeWaste != null
                    && largeTargets.DistanceMeters <= approachRadiusMeters)),
                holdingLarge, largeTargets != null && largeTargets.Pull != null ? largeTargets.Pull.Progress : 0f));
            Panel.SetContinuePrompt(progress.CanContinue, PanelText.ContinuePrompt);
            if (TutorialContinueInput.TryConsume(Panel, bootstrap))
            {
                progress.Continue();
                Panel.SetContinuePrompt(false);
            }
            if (IsInventoryTeaching) { UpdateInventoryTeaching(); return; }
            if (IsComplete) { HideCompletedPresentation(); return; }
            if (progress.Step != displayedStep)
            { Panel.SetCollapsed(false); displayedStep = progress.Step; }
            if (IsLargePractice && !acceptedFragmentRecycle) EnsureLargeWaste();
            RefreshPanel(true);
            UpdateWaypoint();
            UpdateInletOutline();
        }

        private void BeginLesson()
        {
            progress = new RecyclingTutorialProgress();
            if (acceptedRecycle) progress.RegisterRecycled(true);
            chapterConfirmed = false; hasStarted = true;
            photography.SetPresentationOwned(false);
            Panel.SetCollapsed(false); Panel.SetContinuePrompt(false);
            var owner = new GameObject("Recycling Tutorial Destination"); owner.transform.SetParent(transform, false);
            waypoint = owner.AddComponent<TutorialWaypointIndicator>(); waypoint.Configure(bootstrap, targetSprite);
        }

        private bool IsModal() => Time.timeScale <= 0f || Time.deltaTime <= 0f
            || AnimalGame.MainUI.MainUI.BlocksGameplay || AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels
            || (bootstrap.photoMode != null && bootstrap.photoMode.IsActive)
            || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing);

        private void EnsureInventoryContext()
        {
            var active = GameMainUI.Active;
            if (active == null || !active.isActiveAndEnabled || active.gameObject.scene != gameObject.scene) return;
            if (inventoryUi == active) return;
            inventoryUi?.InventoryController.SetTutorialHighlightedItems(null);
            inventoryUi = active;
            CaptureInventorySnapshot();
        }

        private void CaptureInventorySnapshot()
        {
            inventorySnapshot.Clear();
            if (inventoryUi == null) return;
            InventoryStore store = inventoryUi.Inventory;
            for (int i = 0; i < store.Capacity; i++)
            {
                InventoryItem item = store.Items[i];
                if (item == null) continue;
                inventorySnapshot.TryGetValue(item, out int quantity);
                inventorySnapshot[item] = quantity + store.Quantity(i);
            }
        }

        private void RecordRecycledMaterials()
        {
            EnsureInventoryContext();
            if (inventoryUi == null) return;
            InventoryStore store = inventoryUi.Inventory;
            for (int i = 0; i < store.Capacity; i++)
            {
                InventoryItem item = store.Items[i];
                if (item == null || !inventoryUi.InventoryController.IsRecycleRewardItem(item)) continue;
                int total = 0;
                for (int j = 0; j < store.Capacity; j++)
                    if (store.Items[j] == item) total += store.Quantity(j);
                inventorySnapshot.TryGetValue(item, out int before);
                if (total > before) recoveredMaterials.Add(item);
            }
            CaptureInventorySnapshot();
        }

        private void UpdateInventoryTeaching()
        {
            bool inventoryPause = inventoryUi != null && GameMainUI.BlocksGameplay
                && (inventoryUi.IsInventoryOpen || inventoryUi.InventoryProgress > 0f);
            bool valid = inventoryUi != null && inventoryUi.isActiveAndEnabled && CanPractice()
                && !AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels
                && (bootstrap.photoMode == null || !bootstrap.photoMode.IsActive)
                && (bootstrap.photoResultView == null || !bootstrap.photoResultView.IsShowing)
                && (Time.timeScale > 0f || inventoryPause);
            Panel.SetPhotoModeVisible(false);
            Panel.SetContinuePrompt(false);
            Panel.SetCollapseEnabled(false);
            Panel.SetInventoryTutorialVisible(valid);
            Panel.SetVisible(valid);
            waypoint?.ClearTarget();
            if (inletOutline != null) inletOutline.style.display = DisplayStyle.None;
            if (!valid)
            {
                progress.Suspend();
                inventoryUi?.InventoryController.SetTutorialHighlightedItems(null);
                return;
            }

            RecyclingTutorialStep before = progress.Step;
            progress.SampleInventory(true, inventoryUi.IsInventoryOpen, inventoryUi.InventoryProgress);
            if (IsComplete) { HideCompletedPresentation(); return; }
            RefreshInventoryMaterials(before != progress.Step && progress.Step == RecyclingTutorialStep.ViewMaterials);
            displayedStep = progress.Step;
            RefreshPanel();
        }

        private void RefreshInventoryMaterials(bool focus)
        {
            visibleMaterials.Clear();
            InventoryStore store = inventoryUi.Inventory;
            for (int i = 0; i < store.Capacity; i++)
            {
                InventoryItem item = store.Items[i];
                if (item == null || store.Quantity(i) <= 0 || visibleMaterials.Contains(item)) continue;
                if (recoveredMaterials.Contains(item) || (recoveredMaterials.Count == 0
                    && inventoryUi.InventoryController.IsRecycleRewardItem(item))) visibleMaterials.Add(item);
            }
            bool showingMaterials = progress.Step == RecyclingTutorialStep.ViewMaterials && inventoryUi.IsInventoryOpen;
            inventoryUi.InventoryController.SetTutorialHighlightedItems(showingMaterials ? visibleMaterials : null);
            if (!focus || visibleMaterials.Count == 0) return;
            for (int i = 0; i < store.Capacity; i++)
                if (store.Items[i] == visibleMaterials[0]) { inventoryUi.SelectItem(i); break; }
        }

        private string InventoryMaterialsSummary()
        {
            if (inventoryUi == null || visibleMaterials.Count == 0) return Text.NoMaterialsDetail;
            InventoryStore store = inventoryUi.Inventory;
            InventoryItem selected = store.Items[inventoryUi.SelectedIndex];
            InventoryItem item = visibleMaterials.Contains(selected) ? selected : visibleMaterials[0];
            int quantity = 0;
            for (int i = 0; i < store.Capacity; i++) if (store.Items[i] == item) quantity += store.Quantity(i);
            return item.ItemName + " × " + quantity;
        }

        private void ClearInventoryPresentation()
        {
            inventoryUi?.InventoryController.SetTutorialHighlightedItems(null);
            if (Panel == null) return;
            Panel.SetInventoryTutorialVisible(false);
            Panel.SetCollapseEnabled(true);
        }

        private bool CanPractice() => bootstrap.map != null && bootstrap.map.HasGeneratedMap
            && bootstrap.robot.isActiveAndEnabled && !bootstrap.robot.IsMovementLocked
            && !bootstrap.robot.IsPhotoModeInputLocked && !bootstrap.robot.IsLevelThreeUnstable
            && !bootstrap.robot.IsAutoAligningDownhill && bootstrap.armController != null && bootstrap.armController.isActiveAndEnabled
            && (bootstrap.tumble == null || bootstrap.tumble.State == RobotTumbleState.Upright);

        private bool IsStopped()
        {
            float scale = Mathf.Max(.0001f, bootstrap.map.MapMetersToWorldDistance(bootstrap.robot.Forward, 1f));
            return Mathf.Abs(bootstrap.robot.CurrentSpeed) / scale <= .25f
                && bootstrap.robot.CurrentTerrainVelocity.magnitude / scale <= .25f
                && Mathf.Abs(bootstrap.robot.CurrentTurnSpeed) <= 2f;
        }

        private float WasteDistance => IsLargePractice
            ? largeTargets != null && !largeTargets.HasBroken ? largeTargets.DistanceMeters : DistanceTo(targetFragment)
            : DistanceTo(targetWaste);

        private float DistanceTo(WorldInteraction item) => item != null && bootstrap.map != null
            && bootstrap.map.TrySampleWorldPosition(bootstrap.robot.transform.position, out Vector2 player, out _)
            && bootstrap.map.TrySampleWorldPosition(item.transform.position, out Vector2 waste, out _)
                ? Vector2.Distance(player, waste) : 0f;

        private void EnsureLargeWaste()
        {
            if (largeTargets == null) return;
            largeTargets.EnsureTarget();
            if (largeTargets.HasBroken && largeTargets.Fragment != null && targetFragment != largeTargets.Fragment)
                OnLargeWasteBroken(largeTargets.Fragment);
        }

        private void OnLargeWasteBroken(WorldInteraction fragment)
        {
            if (largeTargets == null || !largeTargets.HasBroken || fragment == null || fragment != largeTargets.Fragment
                || fragment.gameObject.scene != gameObject.scene || !fragment.Recyclable || fragment.Size != RecyclableSize.Medium) return;
            if (targetFragment != fragment)
            {
                UnbindFragment(); targetFragment = fragment;
                targetFragment.Recycled += OnFragmentRecycled;
            }
            progress?.RegisterLargeWasteBroken(true);
        }

        private void OnFragmentRecycled()
        {
            if (!hasStarted || largeTargets == null || !largeTargets.HasBroken || targetFragment == null
                || targetFragment != largeTargets.Fragment || targetFragment.gameObject.scene != gameObject.scene
                || !targetFragment.Recyclable || targetFragment.Size != RecyclableSize.Medium) return;
            acceptedFragmentRecycle = true;
            RecordRecycledMaterials();
            progress?.RegisterFragmentRecycled(true);
        }

        private void UnbindFragment()
        { if (targetFragment != null) targetFragment.Recycled -= OnFragmentRecycled; targetFragment = null; }

        private void EnsureWaste()
        {
            if (targetWaste != null && targetWaste.Available && targetWaste.Owner == null && WasteDistance > recoveryDistanceMeters)
            { UnbindWaste(); targets.ResetWasteTarget(); }
            if (targetWaste == null || !targetWaste.Available)
            {
                if (!targets.EnsureWasteTarget()) return;
            }
            WorldInteraction next = targets.Item != null ? targets.Item.GetComponent<WorldInteraction>() : null;
            if (next == targetWaste) return;
            UnbindWaste(); targetWaste = next;
            if (targetWaste != null) targetWaste.Recycled += OnWasteRecycled;
        }

        private void OnWasteRecycled()
        {
            // Only this exact clone's successful native callback is accepted. Disabling, dropping,
            // pushing or merely entering the inlet must not masquerade as receipt of materials.
            // The arm clears its hand state before WorldInteraction performs this successful handoff.
            if (targetWaste == null || photography == null || !photography.IsComplete || targetWaste.gameObject.scene != gameObject.scene
                || !targetWaste.Recyclable || targetWaste.Size != RecyclableSize.Small) return;
            acceptedRecycle = true;
            RecordRecycledMaterials();
            targets.MarkWasteRecycled(targetWaste);
            progress?.RegisterRecycled(true);
        }

        private void RefreshPanel(bool valid = true)
        {
            if (progress == null || progress.IsComplete) { HideCompletedPresentation(); return; }
            TutorialStepText item = Text.GetStep(progress.Step) ?? defaultText.GetStep(progress.Step);
            var tokens = new TutorialTextTokens().Add("confirm", "A").Add("rt", "RT").Add("lt", "LT")
                .Add("distance", WasteDistance).Add("pull", progress.LargePullProgress * 100f)
                .Add("inventoryRight", "→").Add("inventoryLeft", "←")
                .Add("cancel", AdaptiveLegacyGamepadInput.ActiveFamily == LegacyGamepadFamily.Sony ? "○" : "B")
                .Add("materials", InventoryMaterialsSummary());
            string message = item.Message, action = item.Action, glyph = item.ControlGlyph, detail = item.Detail;
            string status = !valid ? Text.BlockedStatus : Text.WaitingStatus;
            if (valid && !progress.IsComplete)
            {
                if (progress.IsInventoryStep)
                    status = progress.Step == RecyclingTutorialStep.ViewMaterials ? Text.MaterialsDetail : Text.WaitingStatus;
                else if (IsLargePractice) RefreshLargePractice(ref message, ref action, ref glyph, ref detail, ref status);
                else if (progress.IsProcessing)
                { message = Text.ProcessingMessage; status = action = detail = Text.ProcessingStatus; }
                else if (progress.IsHoldingWaste)
                {
                    bool ready = progress.IsRecycleReady;
                    message = ready ? Text.ReadyMessage : Text.HoldingMessage;
                    status = action = ready ? Text.ReadyStatus : Text.HoldingStatus;
                    glyph = "A"; detail = ready ? Text.ReadyStatus : Text.PullStatus;
                }
                else if (progress.Step == RecyclingTutorialStep.RecycleWaste && progress.HasRecycledWaste)
                {
                    message = Text.RecycledMessage; status = action = detail = Text.RecycledStatus;
                    glyph = "✓";
                }
                else if (progress.Step == RecyclingTutorialStep.StowArms && bootstrap.armController != null
                    && bootstrap.armController.State == RobotArmState.Retracted && progress.NormalizedProgress < 1f)
                    message = Text.StowRecoveryMessage;
                else if (targetWaste == null || !targetWaste.Available && !acceptedRecycle)
                { message = Text.TargetMissingMessage; status = Text.TargetMissingStatus; }
                else if (progress.Step == RecyclingTutorialStep.RecycleWaste && progress.HasDroppedWaste)
                { message = Text.DroppedMessage; status = Text.DroppedStatus; }
                else if (progress.Step == RecyclingTutorialStep.RecycleWaste && bootstrap.armController != null
                    && !bootstrap.armController.IsArmModeActive) message = Text.ReopenMessage;
            }
            practiceStatus = tokens.Format(status);
            Panel.SetPanelText(PanelText);
            Panel.SetLessonHint(tokens.Format(progress.Step == RecyclingTutorialStep.OpenInventory ? Text.OpenInventoryHint
                : progress.Step == RecyclingTutorialStep.ViewMaterials ? Text.InventoryToggleHint
                : progress.Step == RecyclingTutorialStep.RecycleLargeWaste && !progress.HasBrokenLargeWaste
                    ? Text.LargeToggleHint : Text.ToggleHint));
            Panel.SetContent(tokens.Format(Text.Title), tokens.Format(message), tokens.Format(glyph),
                tokens.Format(action), tokens.Format(detail), practiceStatus, 7, 9);
            Panel.SetProgress(progress.NormalizedProgress); Panel.SetSuccess(progress.IsComplete);
            Panel.SetContinuePrompt(valid && progress.CanContinue, PanelText.ContinuePrompt);
        }

        private void RefreshLargePractice(ref string message, ref string action, ref string glyph, ref string detail, ref string status)
        {
            if (progress.HasRecycledFragment)
            { message = Text.LargeRecycledMessage; action = status = detail = Text.RecycledStatus; glyph = "✓"; }
            else if (progress.IsProcessing)
            { message = Text.ProcessingMessage; action = status = detail = Text.ProcessingStatus; }
            else if (progress.IsHoldingLargeWaste)
            {
                message = Text.LargePullMessage; action = Text.LargePullAction; glyph = "{lt}";
                detail = Text.LargePullDetail; status = Text.LargePullStatus;
            }
            else if (progress.HasBrokenLargeWaste && progress.IsHoldingWaste)
            {
                bool ready = progress.IsRecycleReady;
                message = ready ? Text.ReadyMessage : Text.FragmentHoldingMessage;
                action = status = ready ? Text.ReadyStatus : Text.HoldingStatus;
                glyph = "A"; detail = ready ? Text.ReadyStatus : Text.PullStatus;
            }
            else if (progress.HasBrokenLargeWaste)
            { message = Text.FragmentDroppedMessage; action = Text.FragmentAction; glyph = "A"; status = Text.DroppedStatus; }
            else if (TargetLargeWaste == null || !TargetLargeWaste.Available)
            { message = Text.TargetMissingMessage; status = Text.PreparingStatus; }
            else if (progress.Step == RecyclingTutorialStep.RecycleLargeWaste && bootstrap.armController != null
                && !bootstrap.armController.IsArmModeActive)
            { message = Text.LargeReopenMessage; glyph = "L3"; status = Text.WaitingStatus; }
        }

        private void HideCompletedPresentation()
        {
            ClearInventoryPresentation();
            if (Panel != null) { Panel.SetContinuePrompt(false); Panel.SetVisible(false); Panel.SetLessonHint(string.Empty); }
            if (waypoint != null) waypoint.ClearTarget();
            if (inletOutline != null) inletOutline.style.display = DisplayStyle.None;
        }

        private void UpdateWaypoint()
        {
            if (waypoint == null) return;
            if (IsLargePractice)
            {
                if (acceptedFragmentRecycle || progress.IsHoldingWaste || progress.IsHoldingLargeWaste
                    || largeTargets == null || WasteDistance <= approachRadiusMeters)
                { waypoint.ClearTarget(); return; }
                if (!largeTargets.HasBroken && TargetLargeWaste != null) waypoint.SetTarget(largeTargets.ApproachMapPosition);
                else if (targetFragment != null && targetFragment.Available && bootstrap.map.TrySampleWorldPosition(
                    targetFragment.transform.position, out Vector2 fragmentPosition, out _)) waypoint.SetTarget(fragmentPosition);
                else waypoint.ClearTarget();
                return;
            }
            if (acceptedRecycle || targetWaste == null || !targetWaste.Available || progress.IsHoldingWaste
                || WasteDistance <= approachRadiusMeters || progress.Step == RecyclingTutorialStep.Completed)
            { waypoint.ClearTarget(); return; }
            if (bootstrap.map.TrySampleWorldPosition(targetWaste.transform.position, out Vector2 position, out _)) waypoint.SetTarget(position);
        }

        private void UnbindWaste()
        { if (targetWaste != null) targetWaste.Recycled -= OnWasteRecycled; targetWaste = null; }

        private void UpdateInletOutline()
        {
            RobotArmController arm = bootstrap.armController;
            bool received = IsLargePractice ? acceptedFragmentRecycle : acceptedRecycle;
            bool show = !received && progress.IsHoldingWaste && !progress.IsHoldingLargeWaste && !progress.IsProcessing
                && !Panel.IsCollapsed && Panel.Root?.panel != null && bootstrap.mapCamera != null
                && arm.TryGetRecyclingInlet(out _, out _);
            if (!show) { if (inletOutline != null) inletOutline.style.display = DisplayStyle.None; return; }
            if (inletOutline == null || inletOutline.parent != Panel.Root)
            {
                inletOutline?.RemoveFromHierarchy(); inletOutline = new InletOutline(); Panel.Root.Add(inletOutline);
            }
            var marker = bootstrap.robot.GetComponent<RobotMarkerView>();
            if (marker == null || marker.MarkerVisualRoot == null) return;
            arm.TryGetRecyclingInlet(out Vector2 dock, out Vector2 half);
            var points = inletOutline.Points; points.Clear();
            // Observe the native receiving geometry for the held small waste or medium fragment.
            AddOutlinePoint(new Vector2(dock.x + half.x, .01f), marker.MarkerVisualRoot, points);
            for (int i = 0; i <= 20; i++)
            {
                float angle = i * Mathf.PI / 20f;
                AddOutlinePoint(dock + new Vector2(Mathf.Cos(angle) * half.x, Mathf.Sin(angle) * half.y), marker.MarkerVisualRoot, points);
            }
            AddOutlinePoint(new Vector2(dock.x - half.x, .01f), marker.MarkerVisualRoot, points);
            inletOutline.style.display = DisplayStyle.Flex; inletOutline.MarkDirtyRepaint();
        }

        private void AddOutlinePoint(Vector2 local, Transform frame, List<Vector2> points)
        {
            Vector3 screen = bootstrap.mapCamera.WorldToScreenPoint(frame.TransformPoint(local));
            points.Add(RuntimePanelUtils.ScreenToPanel(Panel.Root.panel, new Vector2(screen.x, Screen.height - screen.y)));
        }

        private sealed class InletOutline : VisualElement
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            public InletOutline()
            {
                name = "TutorialRecyclingInlet"; pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute; style.left = style.top = style.right = style.bottom = 0f;
                generateVisualContent += context =>
                {
                    if (Points.Count < 2) return;
                    var painter = context.painter2D; painter.lineWidth = 1.5f;
                    painter.strokeColor = new Color(212f / 255f, 184f / 255f, 84f / 255f); painter.BeginPath(); painter.MoveTo(Points[0]);
                    for (int i = 1; i < Points.Count; i++) painter.LineTo(Points[i]);
                    painter.Stroke();
                };
            }
        }

        [ContextMenu("Restart Recycling Lesson")]
        public void RestartLesson()
        {
            if (!hasStarted || progress == null) return;
            ClearInventoryPresentation(); recoveredMaterials.Clear(); visibleMaterials.Clear(); CaptureInventorySnapshot();
            UnbindWaste(); targets.ResetWasteTarget(); acceptedRecycle = false;
            UnbindFragment(); acceptedFragmentRecycle = false; largeTargets?.ResetTarget();
            progress.Reset(); chapterConfirmed = false; ownsPresentation = true;
            displayedStep = (RecyclingTutorialStep)(-1); EnsureWaste();
            Panel.SetCollapsed(false); Panel.SetContinuePrompt(false);
        }

        private void ReleasePresentation()
        {
            ClearInventoryPresentation(); recoveredMaterials.Clear(); visibleMaterials.Clear(); inventorySnapshot.Clear(); inventoryUi = null;
            UnbindWaste();
            UnbindFragment(); acceptedFragmentRecycle = false; largeTargets?.ResetTarget();
            inletOutline?.RemoveFromHierarchy(); inletOutline = null;
            if (waypoint != null) { waypoint.ClearTarget(); Destroy(waypoint.gameObject); waypoint = null; }
            if (Panel != null) { Panel.SetContinuePrompt(false); Panel.SetVisible(false); Panel.SetLessonHint(string.Empty); }
            if (photography != null) photography.SetPresentationOwned(true);
            hasStarted = chapterConfirmed = acceptedRecycle = false; progress = null; ownsPresentation = true;
        }

        private void OnDisable() { if (hasStarted) ReleasePresentation(); else { UnbindWaste(); UnbindFragment(); } }
        private void OnDestroy() { if (largeTargets != null) largeTargets.Broken -= OnLargeWasteBroken; }
    }
}

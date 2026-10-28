using System;
using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.Demos.CheckpointRefit
{
    /// <summary>
    /// Temporarily hands this player's input to the refit demo without changing
    /// shared player prefabs, input mappings, equipment, or persistent data.
    /// The terminal must keep this lease until its closing inputs are released.
    /// </summary>
    public sealed class RefitGameplayLease : IDisposable
    {
        private readonly List<EnabledState> states = new List<EnabledState>();
        private readonly HashSet<int> captured = new HashSet<int>();
        private readonly float previousTimeScale;
        private readonly CursorLockMode previousCursorLock;
        private bool timeScaleOwned;
        private bool cursorOwned;
        private bool disposed;

        private readonly struct EnabledState
        {
            public readonly Behaviour Target;
            public readonly bool Enabled;

            public EnabledState(Behaviour target)
            {
                Target = target;
                Enabled = target.enabled;
            }
        }

        private sealed class PlayerContext
        {
            public readonly RobotMover Player;
            public readonly HeightMapPlayerSceneBootstrap Bootstrap;
            public readonly GameObject Hud;

            public PlayerContext(RobotMover player)
            {
                Player = player;
                Bootstrap = FindBootstrap(player);
                GameObject candidate = Bootstrap != null ? Bootstrap.mainUiObject : null;
                Hud = candidate != null && candidate.scene == player.gameObject.scene
                    ? candidate
                    : null;
            }
        }

        private RefitGameplayLease(PlayerContext context)
        {
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;

            // Collect every reference and original enabled flag before invoking
            // OnDisable on anything. Some legacy components reset other systems.
            Capture(context.Player);
            CapturePlayer<RobotBalanceController>(context);
            CapturePlayer<RobotTumbleController>(context);
            CapturePlayer<RobotHeightMotionDetector>(context);
            CapturePlayer<RobotSelfRightingController>(context);
            CapturePlayer<RobotArmController>(context);
            CapturePlayer<PhotoModeController>(context);
            CaptureHud<PhotoResultView>(context);
            CaptureHud<PhotoResultUI>(context);
            CaptureHud<ScanChargeUI>(context);
            CaptureHud<AnimalGame.MainUI.MainUI>(context);

            if (context.Bootstrap != null)
            {
                CaptureSceneBound(context, context.Bootstrap.cameraFollow);
                CaptureSceneBound(context, context.Bootstrap.cameraShake);
                CaptureSceneBound(context, context.Bootstrap.traversalOverlay);
            }

            // BioScanController is deliberately left enabled. Its idle visual
            // phase reads no input, and its already emitted scan wave uses
            // scaled time. Disabling it would destroy a paused wave.
        }

        public static bool CanAcquire(RobotMover player, out string reason)
        {
            if (player == null || !player.isActiveAndEnabled)
            {
                reason = "Player unavailable.";
                return false;
            }

            return CanAcquire(new PlayerContext(player), out reason);
        }

        public static bool TryAcquire(
            RobotMover player,
            out RefitGameplayLease lease,
            out string reason)
        {
            lease = null;
            if (player == null || !player.isActiveAndEnabled)
            {
                reason = "Player unavailable.";
                return false;
            }

            var context = new PlayerContext(player);
            if (!CanAcquire(context, out reason))
                return false;

            RefitGameplayLease candidate = null;
            try
            {
                candidate = new RefitGameplayLease(context);
                candidate.Acquire();
                lease = candidate;
                reason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                candidate?.Dispose();
                reason = "Maintenance could not acquire player input.";
                Debug.LogException(exception, player);
                return false;
            }
        }

        private static bool CanAcquire(PlayerContext context, out string reason)
        {
            if (AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels)
            { reason = "Close the debug UI before maintenance."; return false; }
            RobotMover player = context.Player;
            if (Time.timeScale <= 0f)
            {
                reason = "Close the current menu before maintenance.";
                return false;
            }

            if (player.IsPhotoModeInputLocked
                || AnyPlayer<PhotoModeController>(context, value => value.IsActive)
                || AnyHud<PhotoResultUI>(context, value => value.IsVisible)
                || AnyHud<PhotoResultView>(context, value => value.IsShowing || value.IsWarmingUp))
            {
                reason = "Finish using the camera before maintenance.";
                return false;
            }

            if (AnyHud<AnimalGame.MainUI.MainUI>(context,
                    value => value.IsInventoryOpen || value.InventoryProgress > 0.001f))
            {
                reason = "Close the inventory before maintenance.";
                return false;
            }

            if (player.IsArmInputCaptured
                || AnyPlayer<RobotArmController>(context, value =>
                    value.HeldObject != null
                    || value.IsArmModeActive
                    || value.State != RobotArmState.Retracted
                    || value.VisibleDeployment01 > 0.001f))
            {
                reason = "Stow the arms and release held objects first.";
                return false;
            }

            if (AnyPlayer<BioScanController>(context, value => value.IsScanning)
                || AnyHud<ScanChargeUI>(context, value => value.IsCharging || value.Charge01 > 0.001f))
            {
                reason = "Finish scanning before maintenance.";
                return false;
            }

            if (player.MovementMode != RobotMovementMode.Driven
                || player.IsLevelThreeUnstable
                || player.IsAutoAligningDownhill
                || player.CurrentLevelThreeClimbPhase != LevelThreeClimbFailurePhase.None
                || AnyPlayer<RobotBalanceController>(context, value => value.IsTippedOver)
                || AnyPlayer<RobotHeightMotionDetector>(context, value => value.IsAirborne)
                || AnyPlayer<RobotTumbleController>(context, value =>
                    value.State != RobotTumbleState.Upright
                    || value.IsBalanceRecoveryActive
                    || value.IsSelfRightingVisualActive)
                || AnyPlayer<RobotSelfRightingController>(context,
                    value => value.State != RobotSelfRightingState.Inactive))
            {
                reason = "Return to stable ground before maintenance.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void Acquire()
        {
            Time.timeScale = 0f;
            timeScaleOwned = true;

            foreach (EnabledState state in states)
                if (state.Target != null && state.Enabled)
                    state.Target.enabled = false;

            cursorOwned = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            // Restore dependencies before their input consumers. Each component
            // is independent so a destroyed object cannot prevent the cleanup.
            for (int index = states.Count - 1; index >= 0; index--)
            {
                EnabledState state = states[index];
                try
                {
                    if (state.Target != null && state.Target.enabled != state.Enabled)
                        state.Target.enabled = state.Enabled;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            if (timeScaleOwned && Time.timeScale == 0f)
                Time.timeScale = previousTimeScale;

            if (cursorOwned)
            {
                // Respect an explicit cursor change made by another owner.
                if (Cursor.lockState == CursorLockMode.None)
                    Cursor.lockState = previousCursorLock;
            }

            states.Clear();
            captured.Clear();
        }

        private void Capture(Behaviour target)
        {
            if (target != null && captured.Add(target.GetInstanceID()))
                states.Add(new EnabledState(target));
        }

        private void CapturePlayer<T>(PlayerContext context) where T : Behaviour
        {
            foreach (T target in context.Player.GetComponentsInChildren<T>(true))
                Capture(target);
        }

        private void CaptureHud<T>(PlayerContext context) where T : Behaviour
        {
            CapturePlayer<T>(context);
            if (context.Hud == null)
                return;
            foreach (T target in context.Hud.GetComponentsInChildren<T>(true))
                Capture(target);
        }

        private void CaptureSceneBound(PlayerContext context, Behaviour target)
        {
            if (target != null && target.gameObject.scene == context.Player.gameObject.scene)
                Capture(target);
        }

        private static bool AnyPlayer<T>(PlayerContext context, Predicate<T> predicate)
            where T : Component
        {
            foreach (T target in context.Player.GetComponentsInChildren<T>(true))
                if (predicate(target))
                    return true;
            return false;
        }

        private static bool AnyHud<T>(PlayerContext context, Predicate<T> predicate)
            where T : Component
        {
            if (AnyPlayer(context, predicate))
                return true;
            if (context.Hud != null)
                foreach (T target in context.Hud.GetComponentsInChildren<T>(true))
                    if (predicate(target))
                        return true;
            return false;
        }

        private static HeightMapPlayerSceneBootstrap FindBootstrap(RobotMover player)
        {
            var scene = player.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (HeightMapPlayerSceneBootstrap candidate in
                         root.GetComponentsInChildren<HeightMapPlayerSceneBootstrap>(true))
                    if (candidate.robot == player || candidate.robotObject == player.gameObject)
                        return candidate;
            return null;
        }
    }
}

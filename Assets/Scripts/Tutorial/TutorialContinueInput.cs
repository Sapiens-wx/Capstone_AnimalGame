using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Tutorial
{
    /// <summary>Only an expanded, ready tutorial card owns South/A. Menus keep their own input.</summary>
    public static class TutorialContinueInput
    {
        private static readonly HashSet<TutorialDialoguePanel> panels = new();
        private static int consumedFrame = -1;
        private static bool blockUntilRelease;
        private static int consumedSceneHandle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        { panels.Clear(); consumedFrame = -1; blockUntilRelease = false; consumedSceneHandle = 0; }

        internal static void Register(TutorialDialoguePanel panel) { if (panel != null) panels.Add(panel); }
        internal static void Unregister(TutorialDialoguePanel panel) { if (panel != null) panels.Remove(panel); }
        // Interaction consumers may only ask while A is held. Observe neutral frames independently
        // so an earlier confirmation cannot block a later, separate equipment interaction forever.
        internal static void ObserveRelease()
        { if (blockUntilRelease && !AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld()) blockUntilRelease = false; }

        /// <summary>Discards the press crossing an interruption boundary without altering gameplay controls.</summary>
        public static void DisarmUntilRelease(Scene scene)
        {
            consumedFrame = Time.frameCount;
            consumedSceneHandle = scene.handle;
            blockUntilRelease = true;
        }

        public static bool TryConsume(TutorialDialoguePanel panel, HeightMapPlayerSceneBootstrap bootstrap,
            bool allowDuringInterruption = false, bool allowDuringTutorialPause = false)
        {
            ObserveRelease();
            if (panel == null || !panel.ContinueWasPresented || !CanOwn(panel, bootstrap, allowDuringTutorialPause)
                || (!allowDuringInterruption && TutorialTipOverLesson.BlocksMainline(panel.gameObject))
                || (blockUntilRelease && panel.gameObject.scene.handle == consumedSceneHandle)
                || consumedFrame == Time.frameCount || !PressedThisFrame()) return false;
            consumedFrame = Time.frameCount;
            consumedSceneHandle = panel.gameObject.scene.handle;
            blockUntilRelease = true;
            return true;
        }

        public static bool BlocksWorldSouth(Scene scene)
        {
            if (scene.path != TutorialDrivingLesson.ScenePath) return false;
            ObserveRelease();
            if (IsModal()) return false;
            HeightMapPlayerSceneBootstrap bootstrap = FindBootstrap(scene);
            // A held grip remains native input even when an unrelated tutorial card becomes ready.
            // Returning true here would synthesize a release in RobotArmController.Update.
            if (NativeArmOwnsSouth(bootstrap)) return false;
            if (bootstrap != null && ((bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing)
                || (bootstrap.photoMode != null && (bootstrap.photoMode.IsCapturing || bootstrap.photoMode.IsReviewing))))
                return false;
            if (bootstrap != null && TutorialTipOverLesson.BlocksGameplayForReading(bootstrap.gameObject)) return true;
            if (blockUntilRelease && scene.handle == consumedSceneHandle) return true;
            foreach (TutorialDialoguePanel panel in panels)
                if (panel != null && panel.gameObject.scene == scene && panel.CanContinue
                    && CanOwn(panel, bootstrap)) return true;
            return false;
        }

        private static bool CanOwn(TutorialDialoguePanel panel, HeightMapPlayerSceneBootstrap bootstrap,
            bool allowDuringTutorialPause = false)
        {
            if (panel == null || panel.gameObject.scene.path != TutorialDrivingLesson.ScenePath
                || AnimalGame.MainUI.MainUI.BlocksGameplay || AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels
                || NativeArmOwnsSouth(bootstrap)
                || (bootstrap != null && bootstrap.gameObject.scene != panel.gameObject.scene)) return false;
            var recovery = allowDuringTutorialPause && panel.ShowDuringTutorialPause
                ? panel.GetComponentInParent<TutorialTipOverLesson>() : null;
            bool tutorialPause = recovery != null && recovery.gameObject.scene == panel.gameObject.scene && recovery.OwnsInertiaPause;
            if (!tutorialPause && (Time.deltaTime <= 0f || Time.timeScale <= 0f)) return false;
            return (bootstrap == null || bootstrap.photoResultView == null || !bootstrap.photoResultView.IsShowing)
                && (bootstrap == null || bootstrap.photoMode == null
                    || ((!bootstrap.photoMode.IsActive || (panel.ShowDuringPhotoMode && !tutorialPause))
                        && !bootstrap.photoMode.IsCapturing && !bootstrap.photoMode.IsReviewing
                        && !bootstrap.photoMode.IsEntering && !bootstrap.photoMode.IsExiting));
        }

        private static bool NativeArmOwnsSouth(HeightMapPlayerSceneBootstrap bootstrap) => bootstrap != null
            && bootstrap.armController != null && (bootstrap.armController.HeldObject != null
                || bootstrap.armController.State == RobotArmState.Recycling);

        private static bool IsModal() => Time.deltaTime <= 0f || Time.timeScale <= 0f
            || AnimalGame.MainUI.MainUI.BlocksGameplay || AnimalGame.AnimalDebug.AnimalDebugUI.BlocksOtherPanels;

        private static HeightMapPlayerSceneBootstrap FindBootstrap(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            { var value = root.GetComponentInChildren<HeightMapPlayerSceneBootstrap>(); if (value != null) return value; }
            return null;
        }

        private static bool PressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null) return Gamepad.current.buttonSouth.wasPressedThisFrame;
#endif
            return Input.GetKeyDown(AdaptiveLegacyGamepadInput.ActiveFamily == LegacyGamepadFamily.Sony
                ? KeyCode.JoystickButton1 : KeyCode.JoystickButton0);
        }
    }
}

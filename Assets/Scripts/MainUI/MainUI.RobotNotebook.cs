using AnimalGame.AnimalDebug;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.MainUI
{
    public sealed partial class MainUI
    {
        private RobotNotebookView robotNotebookView;
        private bool robotNotebookOpen;

        internal bool HasRobotNotebook { get; private set; }

        internal void EnableRobotGoalModule()
        {
            HasRobotNotebook = true;
            BindRobotNotebook();
            RefreshRobotNotebook(0f);
        }

        internal void SetOpeningGoalModule(float openness, bool showTab)
        {
            if (openness > 0f || showTab) EnsureRobotNotebookView();
            robotNotebookView?.SetPresentation(openness, showTab ? 1f - openness : 0f);
        }

        private void BindRobotNotebook()
        {
            if (HasRobotNotebook) EnsureRobotNotebookView();
        }

        private void EnsureRobotNotebookView()
        {
            if (stage != null && robotNotebookView?.Root != root) robotNotebookView = new RobotNotebookView(root);
        }

        private bool CanShowRobotNotebook()
        {
            if (BlocksGameplay || Time.timeScale <= 0f || DebugUI.BlocksOtherPanels) return false;
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            return bootstrap == null || !(bootstrap.photoMode.IsActive || bootstrap.photoMode.IsInputLocked
                || (bootstrap.photoResultView != null && bootstrap.photoResultView.IsShowing));
        }

        // A right press that puts the notebook away belongs to it until the button is released.
        private bool UpdateRobotNotebookInput()
        {
            if (!HasRobotNotebook || !CanShowRobotNotebook()) return false;
            if (robotNotebookOpen && AdaptiveLegacyGamepadInput.WasDpadRightPressedThisFrame())
            {
                robotNotebookOpen = false;
                dpadHintVisibleUntil = 0f;
                return true;
            }

            if (AdaptiveLegacyGamepadInput.WasDpadLeftPressedThisFrame()) robotNotebookOpen = true;
            return false;
        }

        private void RefreshRobotNotebook(float deltaTime)
        {
            if (HasRobotNotebook)
                robotNotebookView?.Tick(robotNotebookOpen, CanShowRobotNotebook(), deltaTime);
        }
    }
}

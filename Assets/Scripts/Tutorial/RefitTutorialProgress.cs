using UnityEngine;

namespace AnimalGame.Tutorial
{
    public enum RefitTutorialStep
    {
        ReachMachine, EnterMachine, ReachWorkbench, OpenWorkbench, RefitIntroduction,
        EquipLens, CloseRefit, NewLensControls, Completed
    }

    /// <summary>Actual doorway, terminal and equipped-camera observations, including paused menu UI.</summary>
    public readonly struct RefitTutorialSample
    {
        public readonly float DeltaTime;
        public readonly bool Valid, PanelExpanded, AtEntrance, InsideMachine, AtWorkbench;
        public readonly bool Stopped, RefitOpen, TargetLensInstalled;
        public readonly bool PhotoActive;
        public readonly float FrameMovement;

        public RefitTutorialSample(float deltaTime, bool valid, bool panelExpanded, bool atEntrance,
            bool insideMachine, bool atWorkbench, bool stopped, bool refitOpen, bool targetLensInstalled,
            bool photoActive = false, float frameMovement = 0f)
        {
            DeltaTime = deltaTime; Valid = valid; PanelExpanded = panelExpanded; AtEntrance = atEntrance;
            InsideMachine = insideMachine; AtWorkbench = atWorkbench; Stopped = stopped;
            RefitOpen = refitOpen; TargetLensInstalled = targetLensInstalled;
            PhotoActive = photoActive; FrameMovement = frameMovement;
        }
    }

    /// <summary>
    /// Navigation follows real interior/range state without extra confirmations at the doorway or
    /// workbench. Native equipment selection is never intercepted by an in-menu tutorial.
    /// </summary>
    public sealed class RefitTutorialProgress
    {
        private readonly float requiredFrameMovement;
        private bool entranceReached, lastValid, lastRefitOpen, targetInstalled;

        public RefitTutorialStep Step { get; private set; }
        public float FrameMovement { get; private set; }
        public bool HasOpenedCamera { get; private set; }
        public bool HasAdjustedFrame => FrameMovement >= requiredFrameMovement * .99999f;
        public bool HasInstalledLens { get; private set; }
        public bool IsComplete => Step == RefitTutorialStep.Completed;
        public bool CanContinue => !IsComplete && lastValid && (Step switch
        {
            RefitTutorialStep.ReachMachine => entranceReached,
            RefitTutorialStep.NewLensControls => targetInstalled && !lastRefitOpen && HasOpenedCamera && HasAdjustedFrame,
            RefitTutorialStep.Completed => true,
            _ => false
        });
        public float Target => Step == RefitTutorialStep.NewLensControls ? requiredFrameMovement : 1f;
        public float Value => CanContinue ? Target : Step == RefitTutorialStep.NewLensControls ? FrameMovement : 0f;
        public float NormalizedProgress => IsComplete ? 1f : Mathf.Clamp01(Value / Target);
        public RefitTutorialProgress(float requiredFrameMovement = .12f)
        {
            this.requiredFrameMovement = float.IsFinite(requiredFrameMovement)
                ? Mathf.Max(.01f, requiredFrameMovement) : .12f;
            Reset();
        }

        public void Reset()
        {
            Step = RefitTutorialStep.ReachMachine;
            FrameMovement = 0f;
            HasOpenedCamera = false;
            entranceReached = lastValid = lastRefitOpen = targetInstalled = false;
            HasInstalledLens = false;
        }

        public void Suspend() => lastValid = false;

        public void Sample(in RefitTutorialSample sample)
        {
            if (!sample.Valid || !float.IsFinite(sample.DeltaTime) || sample.DeltaTime <= 0f || sample.DeltaTime > .25f)
            { Suspend(); return; }
            lastValid = true; lastRefitOpen = sample.RefitOpen; targetInstalled = sample.TargetLensInstalled;
            HasInstalledLens |= targetInstalled;
            if (IsComplete) return;
            entranceReached |= sample.AtEntrance;

            // Opening the actual terminal proves arrival. An early open must not strand a player
            // behind navigation cards whose targets are hidden by that same terminal.
            if (sample.RefitOpen && (int)Step <= (int)RefitTutorialStep.OpenWorkbench)
                MoveTo(RefitTutorialStep.EquipLens);

            for (int transition = 0; transition < 5; transition++)
            {
                RefitTutorialStep before = Step;
                switch (Step)
                {
                    case RefitTutorialStep.ReachMachine:
                    case RefitTutorialStep.EnterMachine:
                        if (sample.InsideMachine) MoveTo(RefitTutorialStep.ReachWorkbench);
                        break;
                    case RefitTutorialStep.ReachWorkbench:
                        if (!sample.InsideMachine) MoveTo(RefitTutorialStep.EnterMachine);
                        else if (sample.AtWorkbench) MoveTo(RefitTutorialStep.OpenWorkbench);
                        break;
                    case RefitTutorialStep.OpenWorkbench:
                        if (sample.RefitOpen)
                            MoveTo(RefitTutorialStep.EquipLens);
                        else if (!sample.InsideMachine) MoveTo(RefitTutorialStep.EnterMachine);
                        else if (!sample.AtWorkbench) MoveTo(RefitTutorialStep.ReachWorkbench);
                        break;
                    case RefitTutorialStep.RefitIntroduction:
                    case RefitTutorialStep.EquipLens:
                    case RefitTutorialStep.CloseRefit:
                        // Native menu state and the equipped lens unlock the final camera practice.
                        // Equipment selection itself needs no extra tutorial confirmation.
                        if (sample.RefitOpen) MoveTo(RefitTutorialStep.EquipLens);
                        else if (targetInstalled) MoveTo(RefitTutorialStep.NewLensControls);
                        else RecoverWorkbench(sample);
                        break;
                    case RefitTutorialStep.NewLensControls:
                        if (!targetInstalled)
                        {
                            if (sample.RefitOpen) MoveTo(RefitTutorialStep.EquipLens);
                            else RecoverWorkbench(sample);
                        }
                        break;
                }
                if (Step == before) break;
            }

            if (Step == RefitTutorialStep.NewLensControls && targetInstalled && sample.PhotoActive && !sample.RefitOpen)
            {
                HasOpenedCamera = true;
                if (float.IsFinite(sample.FrameMovement) && sample.FrameMovement > 0f)
                    FrameMovement = Mathf.Min(requiredFrameMovement, FrameMovement + sample.FrameMovement);
            }
        }

        public bool Continue()
        {
            if (!CanContinue || IsComplete) return false;
            switch (Step)
            {
                case RefitTutorialStep.ReachMachine: MoveTo(RefitTutorialStep.EnterMachine); break;
                case RefitTutorialStep.NewLensControls: MoveTo(RefitTutorialStep.Completed); break;
                default: return false;
            }
            return true;
        }

        private void RecoverWorkbench(in RefitTutorialSample sample) => MoveTo(!sample.InsideMachine
            ? RefitTutorialStep.EnterMachine : sample.AtWorkbench
                ? RefitTutorialStep.OpenWorkbench : RefitTutorialStep.ReachWorkbench);
        private void MoveTo(RefitTutorialStep step)
        {
            if (Step == step) return;
            // Equipment replacement invalidates practice performed with the earlier lens.
            if (Step == RefitTutorialStep.NewLensControls && step != RefitTutorialStep.Completed)
            { FrameMovement = 0f; HasOpenedCamera = false; }
            Step = step;
        }
    }
}

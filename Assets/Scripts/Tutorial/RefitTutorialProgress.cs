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

        public RefitTutorialSample(float deltaTime, bool valid, bool panelExpanded, bool atEntrance,
            bool insideMachine, bool atWorkbench, bool stopped, bool refitOpen, bool targetLensInstalled)
        {
            DeltaTime = deltaTime; Valid = valid; PanelExpanded = panelExpanded; AtEntrance = atEntrance;
            InsideMachine = insideMachine; AtWorkbench = atWorkbench; Stopped = stopped;
            RefitOpen = refitOpen; TargetLensInstalled = targetLensInstalled;
        }
    }

    /// <summary>
    /// Navigation follows real interior/range state without extra confirmations at the doorway or
    /// workbench. Native equipment selection is never intercepted by an in-menu tutorial.
    /// </summary>
    public sealed class RefitTutorialProgress
    {
        private readonly float controlsReadingSeconds;
        private bool entranceReached, lastValid, lastRefitOpen, targetInstalled;

        public RefitTutorialStep Step { get; private set; }
        public float ReadingSeconds { get; private set; }
        public bool HasInstalledLens { get; private set; }
        public bool IsComplete => Step == RefitTutorialStep.Completed;
        public bool CanContinue => !IsComplete && lastValid && (Step switch
        {
            RefitTutorialStep.ReachMachine => entranceReached,
            RefitTutorialStep.NewLensControls => targetInstalled && !lastRefitOpen && IsRead,
            RefitTutorialStep.Completed => true,
            _ => false
        });
        public float Target => Step == RefitTutorialStep.NewLensControls ? controlsReadingSeconds : 1f;
        public float Value => CanContinue ? Target : Step == RefitTutorialStep.NewLensControls ? ReadingSeconds : 0f;
        public float NormalizedProgress => IsComplete ? 1f : Mathf.Clamp01(Value / Target);
        private bool IsRead => controlsReadingSeconds - ReadingSeconds <= controlsReadingSeconds * .00001f;

        public RefitTutorialProgress(float controlsReadingSeconds = .8f)
        {
            this.controlsReadingSeconds = float.IsFinite(controlsReadingSeconds)
                ? Mathf.Max(.1f, controlsReadingSeconds) : .8f;
            Reset();
        }

        public void Reset()
        {
            Step = RefitTutorialStep.ReachMachine;
            ReadingSeconds = 0f;
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
                        // Only native menu state and the actual equipped lens determine completion.
                        // No tutorial A is needed before, during or after installation.
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

            if (Step == RefitTutorialStep.NewLensControls && sample.PanelExpanded && !sample.RefitOpen)
                ReadingSeconds = Mathf.Min(controlsReadingSeconds, ReadingSeconds + sample.DeltaTime);
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
            Step = step; ReadingSeconds = 0f;
        }
    }
}

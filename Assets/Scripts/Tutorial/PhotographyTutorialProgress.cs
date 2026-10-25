using UnityEngine;

namespace AnimalGame.Tutorial
{
    public enum PhotographyTutorialStep
    {
        ObserveMovement, EnterPhoto, FrameSubject, Capture, SavePhoto, ExitPhoto, Completed
    }

    /// <summary>Native camera and target observations. Result UI visibility is separate from photo mode.</summary>
    public readonly struct PhotographyTutorialSample
    {
        public readonly float DeltaTime, MovementMeters;
        public readonly bool Valid, PanelExpanded, PhotoModeActive, ResultVisible;
        public readonly bool TargetVisible, TargetHasBeenSeen, TargetFramed;

        public PhotographyTutorialSample(float deltaTime, bool valid, bool panelExpanded,
            bool photoModeActive, bool resultVisible, bool targetVisible, bool targetHasBeenSeen,
            bool targetFramed, float movementMeters)
        {
            DeltaTime = deltaTime; Valid = valid; PanelExpanded = panelExpanded;
            PhotoModeActive = photoModeActive; ResultVisible = resultVisible;
            TargetVisible = targetVisible; TargetHasBeenSeen = targetHasBeenSeen;
            TargetFramed = targetFramed; MovementMeters = movementMeters;
        }
    }

    /// <summary>
    /// Observes real movement, framing, capture and album-save outcomes. The lesson controller verifies
    /// that results belong to its teaching animal before forwarding their monotonic identifiers.
    /// </summary>
    public sealed class PhotographyTutorialProgress
    {
        private readonly float observeSeconds, frameSeconds, requiredMovementMeters;
        private long resultFloor, newestResultId;
        private bool lastPhotoModeActive, lastTargetFramed, observedResultVisible;

        public PhotographyTutorialStep Step { get; private set; }
        public float ReadingSeconds { get; private set; }
        public float MovementMeters { get; private set; }
        public long ActiveResultId { get; private set; }
        public bool HasSavedPhoto { get; private set; }
        public bool IsComplete => Step == PhotographyTutorialStep.Completed;
        public float Value => IsComplete || HasSavedPhoto ? Target : ReadingSeconds;
        public float Target => Step switch
        {
            PhotographyTutorialStep.ObserveMovement => observeSeconds,
            PhotographyTutorialStep.FrameSubject => frameSeconds,
            _ => 1f
        };
        public float NormalizedProgress => IsComplete ? 1f : Mathf.Clamp01(Value / Target);

        public PhotographyTutorialProgress(float observeSeconds = .8f, float frameSeconds = .5f,
            float requiredMovementMeters = 2f, long existingResultId = 0)
        {
            this.observeSeconds = FiniteDuration(observeSeconds, .8f);
            this.frameSeconds = FiniteDuration(frameSeconds, .5f);
            this.requiredMovementMeters = float.IsFinite(requiredMovementMeters)
                ? Mathf.Max(0f, requiredMovementMeters) : 2f;
            Reset(existingResultId);
        }

        public void Reset(long existingResultId = 0)
        {
            // Old result callbacks remain old even when this lesson is restarted in the same run.
            resultFloor = System.Math.Max(resultFloor, System.Math.Max(newestResultId, existingResultId));
            newestResultId = resultFloor;
            Step = PhotographyTutorialStep.ObserveMovement; ReadingSeconds = MovementMeters = 0f;
            ActiveResultId = 0; HasSavedPhoto = false;
            lastPhotoModeActive = lastTargetFramed = observedResultVisible = false;
        }

        public void Suspend() => ReadingSeconds = 0f;

        /// <summary>
        /// Called for an actual new result containing the teaching animal, never for a shutter press.
        /// An early successful shot also proves that entering and framing the subject were achieved.
        /// </summary>
        public bool RegisterValidPhoto(long resultId, bool valid)
        {
            if (!valid || IsComplete || HasSavedPhoto || resultId <= 0 || resultId <= newestResultId)
                return false;
            newestResultId = resultId; ActiveResultId = resultId; observedResultVisible = false;
            MoveTo(PhotographyTutorialStep.SavePhoto);
            return true;
        }

        /// <summary>Only the album save for the current accepted result satisfies this action.</summary>
        public bool RegisterPhotoSaved(long resultId, bool valid)
        {
            if (!valid || IsComplete || ActiveResultId <= 0 || resultId != ActiveResultId || HasSavedPhoto)
                return false;
            HasSavedPhoto = true; MoveTo(PhotographyTutorialStep.ExitPhoto);
            return true;
        }

        /// <summary>Discarding an accepted but unsaved result leaves the player free to shoot again.</summary>
        public bool RegisterResultClosed(long resultId, bool valid)
        {
            if (!valid || IsComplete || ActiveResultId <= 0 || resultId != ActiveResultId) return false;
            observedResultVisible = false;
            if (!HasSavedPhoto)
            {
                ActiveResultId = 0;
                MoveTo(!lastPhotoModeActive ? PhotographyTutorialStep.EnterPhoto
                    : lastTargetFramed ? PhotographyTutorialStep.Capture : PhotographyTutorialStep.FrameSubject);
            }
            return true;
        }

        public void Sample(in PhotographyTutorialSample sample)
        {
            if (IsComplete) return;
            if (!sample.Valid || !float.IsFinite(sample.DeltaTime) || sample.DeltaTime <= 0f || sample.DeltaTime > .25f)
            { Suspend(); return; }
            lastPhotoModeActive = sample.PhotoModeActive;
            lastTargetFramed = sample.TargetFramed;
            if (float.IsFinite(sample.MovementMeters)) MovementMeters = Mathf.Max(MovementMeters, sample.MovementMeters);

            if (sample.ResultVisible)
            {
                if (ActiveResultId > 0) observedResultVisible = true;
                ReadingSeconds = 0f;
                return;
            }
            // This also handles a native result close if its UI has no explicit close event.
            if (Step == PhotographyTutorialStep.SavePhoto && observedResultVisible)
                RegisterResultClosed(ActiveResultId, true);

            switch (Step)
            {
                case PhotographyTutorialStep.ObserveMovement:
                    Read(sample.PanelExpanded && sample.TargetVisible && sample.TargetHasBeenSeen,
                        sample.DeltaTime, observeSeconds);
                    if (MovementMeters >= requiredMovementMeters && IsRead(observeSeconds))
                        MoveTo(sample.PhotoModeActive ? PhotographyTutorialStep.FrameSubject : PhotographyTutorialStep.EnterPhoto);
                    break;
                case PhotographyTutorialStep.EnterPhoto:
                    if (sample.PhotoModeActive) MoveTo(PhotographyTutorialStep.FrameSubject);
                    break;
                case PhotographyTutorialStep.FrameSubject:
                    if (!sample.PhotoModeActive) { MoveTo(PhotographyTutorialStep.EnterPhoto); break; }
                    Read(sample.TargetFramed, sample.DeltaTime, frameSeconds);
                    if (IsRead(frameSeconds)) MoveTo(PhotographyTutorialStep.Capture);
                    break;
                case PhotographyTutorialStep.Capture:
                    if (!sample.PhotoModeActive) MoveTo(PhotographyTutorialStep.EnterPhoto);
                    else if (!sample.TargetFramed) MoveTo(PhotographyTutorialStep.FrameSubject);
                    break;
                case PhotographyTutorialStep.ExitPhoto:
                    if (HasSavedPhoto && !sample.PhotoModeActive) MoveTo(PhotographyTutorialStep.Completed);
                    break;
            }
        }

        private void Read(bool eligible, float deltaTime, float duration) =>
            ReadingSeconds = eligible ? Mathf.Min(duration, ReadingSeconds + deltaTime) : 0f;
        private bool IsRead(float duration) => duration - ReadingSeconds <= duration * .00001f;
        private void MoveTo(PhotographyTutorialStep step) { Step = step; ReadingSeconds = 0f; }
        private static float FiniteDuration(float value, float fallback) =>
            float.IsFinite(value) ? Mathf.Max(.1f, value) : fallback;
    }
}

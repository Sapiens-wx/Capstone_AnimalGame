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
        private bool lastPhotoModeActive, enteredPhoto, lastResultVisible;
        private float observationReading, framingReading;

        public PhotographyTutorialStep Step { get; private set; }
        public float ReadingSeconds { get; private set; }
        public float MovementMeters { get; private set; }
        public long ActiveResultId { get; private set; }
        public bool HasSavedPhoto { get; private set; }
        public bool HasCapturedPhoto => ActiveResultId > 0;
        public bool IsComplete => Step == PhotographyTutorialStep.Completed;
        public bool CanContinue => !lastResultVisible && (Step switch
        {
            PhotographyTutorialStep.ObserveMovement => HasCapturedPhoto || (IsRead(observationReading, observeSeconds) && MovementMeters >= requiredMovementMeters),
            PhotographyTutorialStep.EnterPhoto => enteredPhoto || HasCapturedPhoto,
            PhotographyTutorialStep.FrameSubject => IsRead(framingReading, frameSeconds) || HasCapturedPhoto,
            PhotographyTutorialStep.Capture => HasCapturedPhoto,
            PhotographyTutorialStep.SavePhoto => HasSavedPhoto,
            PhotographyTutorialStep.ExitPhoto => HasSavedPhoto && !lastPhotoModeActive,
            PhotographyTutorialStep.Completed => true,
            _ => false
        });
        public float Value => CanContinue ? Target : ReadingSeconds;
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
            lastPhotoModeActive = enteredPhoto = lastResultVisible = false;
            observationReading = framingReading = 0f;
        }

        public void Suspend() { }

        /// <summary>
        /// Called for an actual new result containing the teaching animal, never for a shutter press.
        /// An early successful shot also proves that entering and framing the subject were achieved.
        /// </summary>
        public bool RegisterValidPhoto(long resultId, bool valid)
        {
            if (!valid || IsComplete || HasSavedPhoto || resultId <= 0 || resultId <= newestResultId)
                return false;
            newestResultId = resultId; ActiveResultId = resultId;
            // Keep the current instruction until A. A real subject result also proves the preceding
            // camera actions, including an early shot taken before their individual confirmations.
            enteredPhoto = true;
            observationReading = observeSeconds; framingReading = frameSeconds;
            return true;
        }

        /// <summary>Only the album save for the current accepted result satisfies this action.</summary>
        public bool RegisterPhotoSaved(long resultId, bool valid)
        {
            if (!valid || IsComplete || ActiveResultId <= 0 || resultId != ActiveResultId || HasSavedPhoto)
                return false;
            HasSavedPhoto = true;
            return true;
        }

        /// <summary>Keep the captured result available when the bird has already left the scene.</summary>
        public bool RegisterResultClosed(long resultId, bool valid)
        {
            if (!valid || IsComplete || ActiveResultId <= 0 || resultId != ActiveResultId) return false;
            return true;
        }

        public void Sample(in PhotographyTutorialSample sample)
        {
            if (IsComplete) return;
            if (!sample.Valid || !float.IsFinite(sample.DeltaTime) || sample.DeltaTime <= 0f || sample.DeltaTime > .25f)
            { Suspend(); return; }
            lastPhotoModeActive = sample.PhotoModeActive;
            lastResultVisible = sample.ResultVisible;
            enteredPhoto |= sample.PhotoModeActive;
            if (float.IsFinite(sample.MovementMeters)) MovementMeters = Mathf.Max(MovementMeters, sample.MovementMeters);

            if (sample.ResultVisible) return;
            if (sample.PanelExpanded)
            {
                if (sample.TargetVisible && sample.TargetHasBeenSeen)
                    observationReading = Mathf.Min(observeSeconds, observationReading + sample.DeltaTime);
                else if (!IsRead(observationReading, observeSeconds)) observationReading = 0f;
                if (sample.PhotoModeActive && sample.TargetFramed)
                    framingReading = Mathf.Min(frameSeconds, framingReading + sample.DeltaTime);
                else if (!IsRead(framingReading, frameSeconds)) framingReading = 0f;
            }

            switch (Step)
            {
                case PhotographyTutorialStep.ObserveMovement:
                    ReadingSeconds = observationReading;
                    break;
                case PhotographyTutorialStep.FrameSubject:
                    ReadingSeconds = framingReading;
                    break;
            }
        }

        public bool Continue()
        {
            if (!CanContinue || IsComplete) return false;
            MoveTo(Step switch
            {
                PhotographyTutorialStep.ObserveMovement => PhotographyTutorialStep.EnterPhoto,
                PhotographyTutorialStep.EnterPhoto => PhotographyTutorialStep.FrameSubject,
                PhotographyTutorialStep.FrameSubject => PhotographyTutorialStep.Capture,
                PhotographyTutorialStep.Capture => PhotographyTutorialStep.SavePhoto,
                PhotographyTutorialStep.SavePhoto => PhotographyTutorialStep.ExitPhoto,
                _ => PhotographyTutorialStep.Completed
            });
            return true;
        }
        private void MoveTo(PhotographyTutorialStep step) { Step = step; ReadingSeconds = 0f; }
        private static bool IsRead(float value, float duration) => duration - value <= duration * .00001f;
        private static float FiniteDuration(float value, float fallback) =>
            float.IsFinite(value) ? Mathf.Max(.1f, value) : fallback;
    }
}

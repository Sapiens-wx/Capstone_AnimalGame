using AnimalGame.RobotMap;
using UnityEngine;

namespace Capstone.Audio
{
    internal sealed class Milestone1CameraAudio
    {
        private readonly Milestone1AudioSettings settings;
        private readonly Milestone1AudioVoices voices;
        private PhotoModeController source;
        private Milestone1AudioVoice movement;
        private Milestone1AudioVoice focus;
        private Vector2 previousAim;
        private float stationaryElapsed;
        private float movementParameterValue;
        private float focusParameterValue;
        private float movementPlaybackRate;
        private bool wasFocusing;
        private bool wasActive;
        private bool closeHandled;

        internal PhotoModeController Source => source;

        internal Milestone1CameraAudio(Milestone1AudioSettings settings)
        {
            this.settings = settings;
            voices = new Milestone1AudioVoices(settings);
        }

        internal void Bind(PhotoModeController nextSource)
        {
            if (ReferenceEquals(source, nextSource))
                return;
            if (!ReferenceEquals(source, null))
            {
                source.ModeChanged -= OnModeChanged;
                source.PhotoCaptured -= OnPhotoCaptured;
            }
            StopAllImmediately();
            source = nextSource;
            if (source == null)
                return;

            source.ModeChanged += OnModeChanged;
            source.PhotoCaptured += OnPhotoCaptured;
            previousAim = source.AimLocalPosition;
            wasActive = source.IsActive;
        }

        internal void Tick(float deltaTime)
        {
            if (source == null || !source.isActiveAndEnabled)
            {
                StopAllImmediately();
                return;
            }

            Vector2 aim = source.AimLocalPosition;
            float actualAimSpeed = deltaTime > 0f
                ? Vector2.Distance(previousAim, aim) / deltaTime
                : 0f;
            previousAim = aim;

            if (source.IsExiting && !closeHandled)
                BeginClose();
            else if (wasActive && !source.IsActive && !closeHandled)
                BeginClose();

            bool focusing = source.IsActive
                            && !source.IsEntering
                            && !source.IsExiting
                            && source.IsFocusing;
            if (focusing && !wasFocusing)
            {
                if (settings.CameraDynamicsEnabled)
                {
                    focusParameterValue = Mathf.Clamp01(source.FocusProgress01);
                    focus = voices.Play(Milestone1Cue.CameraFocus, settings.CameraFocusParameterName, focusParameterValue);
                }
                else
                    focus = voices.Play(Milestone1Cue.CameraFocus);
            }
            else if (!focusing && wasFocusing)
                StopFocus();
            wasFocusing = focusing;
            if (settings.CameraDynamicsEnabled && focusing && focus != null && focus.IsPlaying)
            {
                focusParameterValue = SmoothParameter(focusParameterValue, source.FocusProgress01, deltaTime);
                focus.SetNormalizedParameter(settings.CameraFocusParameterName, focusParameterValue, settings.CameraParameterEpsilon);
            }

            bool canMove = source.IsActive
                           && !source.IsEntering
                           && !source.IsExiting
                           && !source.IsInputLocked;
            if (canMove && actualAimSpeed > settings.CameraMoveThreshold)
            {
                stationaryElapsed = 0f;
                if (movement == null || !movement.IsPlaying)
                {
                    movementPlaybackRate = settings.CameraMovePlaybackRate(actualAimSpeed);
                    if (settings.CameraDynamicsEnabled)
                    {
                        movementParameterValue = Mathf.Clamp01(actualAimSpeed / settings.CameraMoveReferenceSpeed);
                        movement = voices.Play(Milestone1Cue.CameraMove, settings.CameraMoveParameterName, movementParameterValue, movementPlaybackRate);
                    }
                    else
                        movement = voices.Play(Milestone1Cue.CameraMove, null, 0f, movementPlaybackRate);
                }
            }
            else
            {
                stationaryElapsed += deltaTime;
                if (!canMove || stationaryElapsed >= settings.CameraMoveStopDelay)
                    StopMovement();
            }
            if (canMove && movement != null && movement.IsPlaying)
            {
                float targetRate = settings.CameraMovePlaybackRate(actualAimSpeed);
                float seconds = settings.CameraParameterSmoothingSeconds;
                float amount = seconds > 0f ? 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / seconds) : 1f;
                movementPlaybackRate = Mathf.Lerp(movementPlaybackRate, targetRate, amount);
                movement.SetPlaybackRate(movementPlaybackRate);
            }
            if (settings.CameraDynamicsEnabled && canMove && movement != null && movement.IsPlaying)
            {
                float target = actualAimSpeed > settings.CameraMoveThreshold
                    ? Mathf.Clamp01(actualAimSpeed / settings.CameraMoveReferenceSpeed)
                    : 0f;
                movementParameterValue = SmoothParameter(movementParameterValue, target, deltaTime);
                movement.SetNormalizedParameter(settings.CameraMoveParameterName, movementParameterValue, settings.CameraParameterEpsilon);
            }

            wasActive = source.IsActive;
            voices.Tick(deltaTime);
        }

        internal void StopAllImmediately()
        {
            voices.StopAll(true);
            movement = null;
            focus = null;
            wasFocusing = false;
            wasActive = false;
            closeHandled = false;
            stationaryElapsed = 0f;
            movementParameterValue = 0f;
            focusParameterValue = 0f;
            movementPlaybackRate = 1f;
        }

        private float SmoothParameter(float current, float target, float deltaTime)
        {
            float seconds = settings.CameraParameterSmoothingSeconds;
            float amount = seconds > 0f ? 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / seconds) : 1f;
            return Mathf.Lerp(current, Mathf.Clamp01(target), amount);
        }

        private void OnModeChanged(bool active)
        {
            if (source == null || !source.isActiveAndEnabled)
            {
                StopAllImmediately();
                return;
            }

            previousAim = source.AimLocalPosition;
            stationaryElapsed = 0f;
            if (active)
            {
                voices.StopAll(false);
                movement = null;
                focus = null;
                wasFocusing = false;
                wasActive = true;
                closeHandled = false;
                voices.Play(Milestone1Cue.CameraOpen);
            }
            else
            {
                // A normal exit already played Close at its start. An immediate
                // gameplay exit (for example tipping over) reaches only this event.
                if (!closeHandled && wasActive)
                    BeginClose();
                StopFocus();
                StopMovement();
                wasFocusing = false;
                wasActive = false;
            }
        }

        private void BeginClose()
        {
            voices.StopAll(false);
            movement = null;
            focus = null;
            wasFocusing = false;
            closeHandled = true;
            voices.Play(Milestone1Cue.CameraClose);
        }

        private void OnPhotoCaptured()
        {
            if (source == null || !source.isActiveAndEnabled || !source.IsActive)
                return;
            StopFocus();
            StopMovement();
            voices.Play(Milestone1Cue.CameraShutter);
        }

        private void StopMovement()
        {
            voices.Stop(movement);
            movement = null;
        }

        private void StopFocus()
        {
            voices.Stop(focus);
            focus = null;
        }
    }
}

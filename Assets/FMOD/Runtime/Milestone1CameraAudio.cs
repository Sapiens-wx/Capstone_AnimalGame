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
                focus = voices.Play(Milestone1Cue.CameraFocus);
            else if (!focusing && wasFocusing)
                StopFocus();
            wasFocusing = focusing;

            bool canMove = source.IsActive
                           && !source.IsEntering
                           && !source.IsExiting
                           && !source.IsInputLocked;
            if (canMove && actualAimSpeed > settings.CameraMoveThreshold)
            {
                stationaryElapsed = 0f;
                if (movement == null || !movement.IsPlaying)
                    movement = voices.Play(Milestone1Cue.CameraMove);
            }
            else
            {
                stationaryElapsed += deltaTime;
                if (!canMove || stationaryElapsed >= settings.CameraMoveStopDelay)
                    StopMovement();
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

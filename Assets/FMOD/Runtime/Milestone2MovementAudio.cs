using AnimalGame.RobotMap;
using UnityEngine;

namespace Capstone.Audio
{
    internal sealed class Milestone2MovementAudio
    {
        private readonly Milestone2ActionSettings settings;
        private readonly Milestone2ActionVoices voices;
        private RobotMover source;
        private RobotHeightMotionDetector height;
        private Vector2 previousPosition;
        private float previousAngle, previousLongitudinalSpeed, stationaryElapsed;
        private bool hasBaseline, wasMoving, gripLossHandled, wasAirborne;

        internal RobotMover Source => source;
        internal RobotHeightMotionDetector HeightSource => height;

        internal Milestone2MovementAudio(Milestone2ActionSettings settings, Milestone2ActionVoices voices)
        {
            this.settings = settings;
            this.voices = voices;
        }

        internal void Bind(RobotMover next, RobotHeightMotionDetector nextHeight)
        {
            if (ReferenceEquals(source, next) && ReferenceEquals(height, nextHeight)) return;
            if (!ReferenceEquals(height, null)) height.Landed -= OnLanded;
            StopAllImmediately();
            source = next;
            height = nextHeight;
            if (height != null) height.Landed += OnLanded;
            if (source != null) EstablishBaseline();
        }

        internal void Tick(float deltaTime)
        {
            if (source == null || !source.isActiveAndEnabled || deltaTime <= 0f)
            {
                StopAllImmediately();
                return;
            }
            if (!hasBaseline) { EstablishBaseline(); return; }

            Vector2 position = source.MapPosition;
            float angle = source.transform.eulerAngles.z;
            Vector2 velocity = (position - previousPosition) / deltaTime;
            float longitudinalSpeed = Vector2.Dot(velocity, source.Forward);
            float speed = Mathf.Abs(longitudinalSpeed);
            float turnSpeed = Mathf.Abs(Mathf.DeltaAngle(previousAngle, angle)) / deltaTime;
            bool airborne = height != null && height.isActiveAndEnabled && height.HasData && height.IsAirborne;
            bool driven = source.MovementMode == RobotMovementMode.Driven;
            bool groundMotion = velocity.sqrMagnitude > settings.MovementThreshold * settings.MovementThreshold
                                || turnSpeed > settings.TurnThreshold;
            bool slipState = source.CurrentLevelThreeClimbPhase == LevelThreeClimbFailurePhase.Slip
                || source.IsAutoAligningDownhill
                || (source.IsLevelThreeUnstable
                    && (source.CurrentTerrainVelocity.sqrMagnitude > settings.MovementThreshold * settings.MovementThreshold
                        || Mathf.Abs(source.CurrentTerrainTurnSpeed) > settings.TurnThreshold));
            bool sliding = driven && !airborne && slipState && groundMotion;
            if (airborne && !wasAirborne && driven) voices.Play(Milestone2ActionCue.Airborne);
            if (!slipState || !driven || airborne) gripLossHandled = false;
            if (sliding && !gripLossHandled)
            {
                voices.Play(Milestone2ActionCue.GripLoss);
                gripLossHandled = true;
            }

            float throttle = source.CurrentThrottleIntent;
            float deceleration = Mathf.Max(0f, Mathf.Abs(previousLongitudinalSpeed) - speed) / deltaTime;
            bool translating = driven && !airborne && !slipState && speed > settings.MovementThreshold;
            bool braking = translating && Mathf.Abs(throttle) > 0.01f
                && longitudinalSpeed * throttle < 0f && deceleration > 0.01f;
            bool coasting = translating && Mathf.Abs(throttle) <= 0.01f;
            bool powering = translating && !braking && !coasting;
            bool turning = driven && !airborne && !slipState && turnSpeed > settings.TurnThreshold;

            // Prime parameters before starting an instance so Studio trigger
            // conditions never select an instrument using stale/default values.
            float normalizedSpeed = Mathf.Clamp01(speed / settings.DriveReferenceSpeed);
            float direction = speed > settings.MovementThreshold ? Mathf.Sign(longitudinalSpeed) : 0f;
            float load = Mathf.Clamp01(1f - source.GrabMovementMultiplier);
            UpdateDriveParameters(Milestone2ActionCue.Drive, normalizedSpeed, direction, throttle, load);
            UpdateDriveParameters(Milestone2ActionCue.Coast, normalizedSpeed, direction, throttle, load);
            UpdateDriveParameters(Milestone2ActionCue.Brake, normalizedSpeed, direction, throttle, load);
            voices.SetParameter(Milestone2ActionCue.Brake, ActionAudioParameter.Effort,
                deceleration / settings.BrakeReferenceDeceleration);
            voices.SetParameter(Milestone2ActionCue.Turn, ActionAudioParameter.Speed,
                turnSpeed / settings.TurnReferenceSpeed);
            voices.SetParameter(Milestone2ActionCue.Slide, ActionAudioParameter.Speed,
                Mathf.Max(velocity.magnitude / settings.DriveReferenceSpeed, turnSpeed / settings.TurnReferenceSpeed));

            if (powering && !wasMoving) voices.Play(Milestone2ActionCue.MoveStart);
            voices.SetLoop(Milestone2ActionCue.Drive, powering);
            voices.SetLoop(Milestone2ActionCue.Coast, coasting);
            voices.SetLoop(Milestone2ActionCue.Brake, braking);
            voices.SetLoop(Milestone2ActionCue.Turn, turning);
            voices.SetLoop(Milestone2ActionCue.Slide, sliding);

            if (translating)
            {
                wasMoving = true;
                stationaryElapsed = 0f;
            }
            else if (wasMoving)
            {
                stationaryElapsed += deltaTime;
                if (!driven || airborne || slipState) wasMoving = false;
                else if (stationaryElapsed >= settings.MovementStopDelay)
                {
                    voices.Play(Milestone2ActionCue.Stop);
                    wasMoving = false;
                }
            }

            previousPosition = position;
            previousAngle = angle;
            previousLongitudinalSpeed = longitudinalSpeed;
            wasAirborne = airborne;
        }

        internal void StopAllImmediately()
        {
            for (int cue = (int)Milestone2ActionCue.MoveStart; cue <= (int)Milestone2ActionCue.Land; cue++)
                voices.Stop((Milestone2ActionCue)cue, true);
            hasBaseline = false;
            wasMoving = gripLossHandled = wasAirborne = false;
            stationaryElapsed = 0f;
        }

        private void EstablishBaseline()
        {
            previousPosition = source.MapPosition;
            previousAngle = source.transform.eulerAngles.z;
            previousLongitudinalSpeed = 0f;
            wasAirborne = height != null && height.HasData && height.IsAirborne;
            hasBaseline = true;
        }

        private void OnLanded(RobotLandingImpact impact)
        {
            // Only the height detector's actual landing event is used. Tumble and
            // self-righting impacts are separate requirements and are not inferred.
            if (source == null || !source.isActiveAndEnabled || Time.timeScale <= 0f
                || source.MovementMode != RobotMovementMode.Driven) return;
            voices.Play(Milestone2ActionCue.Land, ActionAudioParameter.Impact, impact.Strength01);
        }

        private void UpdateDriveParameters(Milestone2ActionCue cue, float speed,
            float direction, float throttle, float load)
        {
            voices.SetParameter(cue, ActionAudioParameter.Speed, speed);
            voices.SetParameter(cue, ActionAudioParameter.Direction, direction);
            voices.SetParameter(cue, ActionAudioParameter.Throttle, Mathf.Abs(throttle));
            voices.SetParameter(cue, ActionAudioParameter.Load, load);
        }
    }
}

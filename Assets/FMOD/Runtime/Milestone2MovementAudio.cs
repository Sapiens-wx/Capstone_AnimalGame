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
        private float previousAngle, engineRpm, engineDirection, forwardReference, linearStillElapsed, turnStillElapsed;
        private bool linearMotion, turnMotion, originalAcceleration;
        private bool hasBaseline, wasMoving, slipEpisode, wasAirborne;

        internal RobotMover Source => source;
        internal RobotHeightMotionDetector HeightSource => height;

        internal Milestone2MovementAudio(Milestone2ActionSettings settings, Milestone2ActionVoices voices)
        {
            this.settings = settings;
            this.voices = voices;
            forwardReference = settings.DriveReferenceSpeed;
        }

        internal void Bind(RobotMover next, RobotHeightMotionDetector nextHeight)
        {
            if (ReferenceEquals(source, next) && ReferenceEquals(height, nextHeight)) return;
            if (!ReferenceEquals(height, null)) height.Landed -= OnLanded;
            StopAllImmediately();
            source = next;
            height = nextHeight;
            forwardReference = settings.DriveReferenceSpeed;
            if (height != null) height.Landed += OnLanded;
            if (source != null) EstablishBaseline();
        }

        internal void Tick(float deltaTime)
        {
            if (source == null || !source.isActiveAndEnabled)
            {
                StopAllImmediately();
                return;
            }
            if (deltaTime <= 0f) return;
            if (!hasBaseline) { EstablishBaseline(); return; }

            Vector2 position = source.MapPosition;
            float angle = source.transform.eulerAngles.z;
            Vector2 velocity = (position - previousPosition) / deltaTime;
            float longitudinalSpeed = Vector2.Dot(velocity, source.Forward);
            float speed = velocity.magnitude;
            float turnSpeed = Mathf.Abs(Mathf.DeltaAngle(previousAngle, angle)) / deltaTime;
            bool airborne = height != null && height.isActiveAndEnabled && height.HasData && height.IsAirborne;
            bool driven = source.MovementMode == RobotMovementMode.Driven;
            bool translating = false, turning = false;
            if (driven && !airborne)
            {
                translating = ObserveMotion(speed, settings.MovementThreshold,
                    ref linearMotion, ref linearStillElapsed, deltaTime);
                turning = ObserveMotion(turnSpeed, settings.TurnThreshold,
                    ref turnMotion, ref turnStillElapsed, deltaTime);
            }
            else
            {
                linearMotion = turnMotion = false;
                linearStillElapsed = turnStillElapsed = 0f;
            }
            bool groundMotion = driven && !airborne && (translating || turning);
            bool slip = driven && !airborne && (source.CurrentLevelThreeClimbPhase == LevelThreeClimbFailurePhase.Slip
                || source.IsAutoAligningDownhill
                || (source.IsLevelThreeUnstable
                    && (source.CurrentTerrainVelocity.sqrMagnitude > settings.MovementThreshold * settings.MovementThreshold
                        || Mathf.Abs(source.CurrentTerrainTurnSpeed) > settings.TurnThreshold)));

            if (airborne && !wasAirborne)
            {
                StopGroundImmediately();
                if (driven) voices.Play(Milestone2ActionCue.Airborne);
            }

            float throttle = source.CurrentThrottleIntent;
            if (throttle > 0.01f && source.UnresistedMovementIntentWorld.sqrMagnitude > 0.0001f)
                forwardReference = source.UnresistedMovementIntentWorld.magnitude / throttle;
            float reference = longitudinalSpeed < -settings.MovementThreshold
                ? Mathf.Max(0.01f, source.UnloadedReverseSpeed) : forwardReference;
            float normalizedSpeed = Mathf.Clamp01(speed / reference);
            bool hasThrottle = Mathf.Abs(throttle) > 0.01f;
            bool stationaryTurn = driven && !airborne && !slip && !hasThrottle
                && speed <= settings.MovementThreshold * 0.5f && turnSpeed > settings.TurnThreshold;
            if (stationaryTurn)
            {
                // Pure turning belongs to the ground layer. Retire coast RPM so
                // ending the turn cannot bring the fading engine back to life.
                engineRpm = engineDirection = 0f;
                originalAcceleration = false;
            }
            bool pendingRamp = originalAcceleration && voices.IsAlive(Milestone2ActionCue.DriveAcceleration);
            if (hasThrottle && (engineDirection == 0f || (engineRpm <= 0.001f && !pendingRamp)))
                engineDirection = Mathf.Sign(throttle);
            // Opposite throttle brakes the audible RPM until it reaches idle.
            // Physical speed crossing zero cannot prematurely cancel the brake.
            bool braking = hasThrottle && engineDirection * throttle < 0f && (engineRpm > 0.001f || pendingRamp);
            bool powering = hasThrottle && !braking;
            bool motorMotion = driven && !stationaryTurn && (hasThrottle
                || (engineRpm > 0.001f && (voices.IsAlive(Milestone2ActionCue.Drive)
                    || voices.IsAlive(Milestone2ActionCue.DriveAcceleration))));

            // Motor command and audio RPM are independent of ground contact or
            // displacement. Ground friction still follows actual ground motion.
            if (motorMotion)
            {
                bool freshMotorStart = !voices.IsAlive(Milestone2ActionCue.Drive)
                    && !voices.IsAlive(Milestone2ActionCue.DriveAcceleration);
                float target = powering ? Mathf.Clamp01(Mathf.Abs(throttle)) : 0f;
                UpdateMotor(target, powering, braking, hasThrottle, throttle, deltaTime);
                voices.SetParameter(Milestone2ActionCue.Drive, ActionAudioParameter.Speed, normalizedSpeed);
                voices.SetParameter(Milestone2ActionCue.Drive, ActionAudioParameter.Throttle, Mathf.Abs(throttle));
                voices.SetParameter(Milestone2ActionCue.Drive, ActionAudioParameter.Load,
                    Mathf.Clamp01(1f - source.GrabMovementMultiplier));
                if (freshMotorStart && !wasMoving && powering) voices.Play(Milestone2ActionCue.MoveStart);
                wasMoving = true;
                if (!powering) voices.Stop(Milestone2ActionCue.MoveStart);
                if (!hasThrottle && engineRpm <= 0.001f)
                {
                    voices.Stop(Milestone2ActionCue.Drive);
                    voices.Stop(Milestone2ActionCue.DriveAcceleration);
                }
            }
            else
            {
                voices.Stop(Milestone2ActionCue.Drive);
                voices.Stop(Milestone2ActionCue.DriveAcceleration);
                voices.Stop(Milestone2ActionCue.MoveStart);
                if (wasMoving && driven && !airborne && !slip && !stationaryTurn) voices.Play(Milestone2ActionCue.Stop);
                wasMoving = false;
                if (!voices.IsAlive(Milestone2ActionCue.Drive) && !voices.IsAlive(Milestone2ActionCue.DriveAcceleration))
                { engineRpm = 0f; originalAcceleration = false; }
            }

            voices.SetLoop(Milestone2ActionCue.GroundMove, driven && !airborne && !slip && translating);
            voices.SetLoopFrom(Milestone2ActionCue.GroundTurn,
                settings.StationaryTurnGroundAudioEnabled && driven && !airborne && !slip && !translating && turning, 810);
            if (slip)
            {
                if (!slipEpisode) voices.Play(Milestone2ActionCue.Slide);
                voices.SetPaused(Milestone2ActionCue.Slide, !groundMotion);
                slipEpisode = true;
            }
            else if (slipEpisode)
            {
                if (driven && !airborne) voices.KeyOff(Milestone2ActionCue.Slide);
                else voices.Stop(Milestone2ActionCue.Slide, true);
                slipEpisode = false;
            }
            if (!driven) StopGroundImmediately();

            previousPosition = position;
            previousAngle = angle;
            wasAirborne = airborne;
        }

        internal void StopAllImmediately()
        {
            for (int cue = (int)Milestone2ActionCue.MoveStart; cue <= (int)Milestone2ActionCue.Land; cue++)
                voices.Stop((Milestone2ActionCue)cue, true);
            StopGroundImmediately();
            voices.Stop(Milestone2ActionCue.LandParts, true);
            voices.Stop(Milestone2ActionCue.DriveAcceleration, true);
            hasBaseline = wasMoving = wasAirborne = false;
            linearMotion = turnMotion = originalAcceleration = false;
            engineRpm = engineDirection = linearStillElapsed = turnStillElapsed = 0f;
        }

        private bool ObserveMotion(float speed, float enterThreshold, ref bool moving, ref float stillElapsed, float deltaTime)
        {
            float threshold = moving ? enterThreshold * 0.5f : enterThreshold;
            if (speed > threshold) { moving = true; stillElapsed = 0f; }
            else if (moving)
            {
                stillElapsed += deltaTime;
                if (stillElapsed >= settings.MovementStopDelay) moving = false;
            }
            return moving;
        }

        private void UpdateMotor(float target, bool powering, bool braking, bool hasThrottle, float throttle, float deltaTime)
        {
            bool hasSustain = voices.IsAlive(Milestone2ActionCue.Drive);
            bool hasRamp = voices.IsAlive(Milestone2ActionCue.DriveAcceleration);
            if (!hasSustain && !hasRamp)
            {
                engineRpm = 0f;
                originalAcceleration = powering && hasThrottle && Mathf.Abs(throttle) >= 0.9f;
                if (originalAcceleration)
                {
                    voices.SetPitch(Milestone2ActionCue.DriveAcceleration, 1f);
                    voices.SetLoopFrom(Milestone2ActionCue.DriveAcceleration, true, 0, settings.MovementFadeInSeconds);
                    hasRamp = voices.IsAlive(Milestone2ActionCue.DriveAcceleration);
                    if (!hasRamp) originalAcceleration = false;
                }
            }

            if (originalAcceleration && hasRamp)
            {
                // Query actual audio progress only while this short one-shot is
                // playing. Its original acceleration is always played at 1x.
                voices.SetLoopFrom(Milestone2ActionCue.DriveAcceleration, true, 0);
                if (voices.TryGetTimelinePosition(Milestone2ActionCue.DriveAcceleration, out int position))
                {
                    engineRpm = Mathf.Clamp01(position / 3500f);
                    if (powering && Mathf.Abs(throttle) >= 0.9f && position < 3420) return;
                }
                // Overlap at most two motor events for 80 ms; interrupting the
                // ramp never hard-seeks its live source or replays its intro.
                voices.SetPitch(Milestone2ActionCue.Drive, Mathf.Lerp(settings.EngineMinimumPitch, 1f, engineRpm));
                voices.SetLoopFrom(Milestone2ActionCue.Drive, true, 3500, settings.EngineCrossfadeSeconds);
                voices.FadeStop(Milestone2ActionCue.DriveAcceleration, settings.EngineCrossfadeSeconds);
                originalAcceleration = false;
                return;
            }
            originalAcceleration = false;
            float rate = powering || target > engineRpm ? settings.EngineAccelerationPerSecond
                : settings.EngineCoastDeceleration * (braking ? 3f : 1f);
            engineRpm = Mathf.MoveTowards(engineRpm, target, rate * deltaTime);
            voices.SetPitch(Milestone2ActionCue.Drive, Mathf.Lerp(settings.EngineMinimumPitch, 1f, engineRpm));
            voices.SetLoopFrom(Milestone2ActionCue.Drive, true, 3500);
        }

        private void StopGroundImmediately()
        {
            voices.Stop(Milestone2ActionCue.GroundMove, true);
            voices.Stop(Milestone2ActionCue.GroundTurn, true);
            voices.Stop(Milestone2ActionCue.Slide, true);
            slipEpisode = false;
        }

        private void EstablishBaseline()
        {
            previousPosition = source.MapPosition;
            previousAngle = source.transform.eulerAngles.z;
            wasAirborne = height != null && height.HasData && height.IsAirborne;
            hasBaseline = true;
        }

        private void OnLanded(RobotLandingImpact impact)
        {
            if (source == null || !source.isActiveAndEnabled || Time.timeScale <= 0f
                || source.MovementMode != RobotMovementMode.Driven) return;
            voices.PlayWithGain(Milestone2ActionCue.Land, Mathf.Clamp01(impact.Strength01));
            voices.Play(Milestone2ActionCue.LandParts);
        }
    }
}

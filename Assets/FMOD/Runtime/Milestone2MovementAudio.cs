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
        private RobotTumbleController tumble;
        private Vector2 previousPosition;
        private float previousAngle, engineRpm, engineDirection, forwardReference, linearStillElapsed, turnStillElapsed;
        private bool linearMotion, turnMotion, engineReleasing;
        private bool hasBaseline, wasMoving, slipEpisode, wasAirborne, groundTurnLooping;
        private bool tumbleSlideActive;
        private float tumbleSlideRemaining;
        private int lastTumbleStartFrame = -1;

        internal RobotMover Source => source;
        internal RobotHeightMotionDetector HeightSource => height;
        internal RobotTumbleController TumbleSource => tumble;

        internal Milestone2MovementAudio(Milestone2ActionSettings settings, Milestone2ActionVoices voices)
        {
            this.settings = settings;
            this.voices = voices;
            forwardReference = settings.DriveReferenceSpeed;
        }

        internal void Bind(RobotMover next, RobotHeightMotionDetector nextHeight,
            RobotTumbleController nextTumble = null)
        {
            if (ReferenceEquals(source, next) && ReferenceEquals(height, nextHeight)
                && ReferenceEquals(tumble, nextTumble)) return;
            if (!ReferenceEquals(height, null)) height.Landed -= OnLanded;
            if (!ReferenceEquals(tumble, null)) tumble.Started -= OnTumbleStarted;
            StopAllImmediately();
            source = next;
            height = nextHeight;
            tumble = nextTumble;
            forwardReference = settings.DriveReferenceSpeed;
            if (height != null) height.Landed += OnLanded;
            if (tumble != null) tumble.Started += OnTumbleStarted;
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

            UpdateTumbleSlide(deltaTime, airborne, slip);

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
            if (engineReleasing)
            {
                if (voices.IsAlive(Milestone2ActionCue.Drive))
                    engineRpm = RpmFromPitch(voices.GetEnginePitch(Milestone2ActionCue.Drive));
                else
                {
                    engineRpm = 0f;
                    engineReleasing = false;
                }
            }
            bool stationaryTurn = driven && !airborne && !slip && !hasThrottle
                && speed <= settings.MovementThreshold * 0.5f && turnSpeed > settings.TurnThreshold;
            if (stationaryTurn)
            {
                // Pure turning belongs to the ground layer. Retire coast RPM so
                // ending the turn cannot bring the fading engine back to life.
                engineRpm = engineDirection = 0f;
                engineReleasing = false;
            }
            if (hasThrottle && (engineDirection == 0f || (engineRpm <= 0.001f && !engineReleasing)))
                engineDirection = Mathf.Sign(throttle);
            // Opposite throttle brakes the audible RPM until it reaches idle.
            // Physical speed crossing zero cannot prematurely cancel the brake.
            bool braking = hasThrottle && engineDirection * throttle < 0f && (engineRpm > 0.001f || engineReleasing);
            bool powering = hasThrottle && !braking;
            bool motorMotion = driven && !stationaryTurn && (hasThrottle
                || voices.IsAlive(Milestone2ActionCue.Drive));

            // Motor command and audio RPM are independent of ground contact or
            // displacement. Ground friction still follows actual ground motion.
            if (motorMotion)
            {
                float target = powering ? Mathf.Clamp01(Mathf.Abs(throttle)) : 0f;
                UpdateMotor(target, powering, braking, deltaTime);
                voices.SetParameter(Milestone2ActionCue.Drive, ActionAudioParameter.Speed, normalizedSpeed);
                voices.SetParameter(Milestone2ActionCue.Drive, ActionAudioParameter.Throttle, Mathf.Abs(throttle));
                voices.SetParameter(Milestone2ActionCue.Drive, ActionAudioParameter.Load,
                    Mathf.Clamp01(1f - source.GrabMovementMultiplier));
                wasMoving = true;
            }
            else
            {
                voices.FadeEngineToIdle(Milestone2ActionCue.Drive, false);
                voices.Stop(Milestone2ActionCue.DriveAcceleration);
                voices.Stop(Milestone2ActionCue.MoveStart);
                if (wasMoving && driven && !airborne && !slip && !stationaryTurn) voices.Play(Milestone2ActionCue.Stop);
                wasMoving = false;
                if (!voices.IsAlive(Milestone2ActionCue.Drive) && !voices.IsAlive(Milestone2ActionCue.DriveAcceleration))
                { engineRpm = 0f; engineReleasing = false; }
            }

            // Slipping does not remove terrain contact. Keep the delivered ground
            // layer for actual sliding translation or uncontrolled ground rotation.
            voices.SetLoop(Milestone2ActionCue.GroundMove, driven && !airborne
                && (translating || (slip && turning)));
            UpdateGroundTurn(settings.StationaryTurnGroundAudioEnabled && driven && !airborne
                && !slip && !translating && turning,
                settings.StationaryTurnGroundAudioEnabled && driven && !airborne && !slip);
            if (slip)
            {
                if (!slipEpisode)
                {
                    voices.SetPitch(Milestone2ActionCue.Slide, 1f);
                    voices.Play(Milestone2ActionCue.Slide);
                }
                voices.SetPaused(Milestone2ActionCue.Slide, !groundMotion);
                slipEpisode = true;
            }
            else if (slipEpisode)
            {
                if (driven && !airborne) voices.KeyOff(Milestone2ActionCue.Slide);
                else voices.Stop(Milestone2ActionCue.Slide, true);
                slipEpisode = false;
            }
            if (!driven) StopGroundImmediately(tumbleSlideActive);

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
            hasBaseline = wasMoving = wasAirborne = groundTurnLooping = false;
            linearMotion = turnMotion = engineReleasing = false;
            engineRpm = engineDirection = linearStillElapsed = turnStillElapsed = 0f;
            lastTumbleStartFrame = -1;
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

        private float RpmFromPitch(float pitch)
        {
            float range = settings.EngineMaximumPitch - settings.EngineMinimumPitch;
            return range > 0f ? Mathf.Clamp01((pitch - settings.EngineMinimumPitch) / range) : 0f;
        }

        private void UpdateMotor(float target, bool powering, bool braking, float deltaTime)
        {
            if (!voices.IsAlive(Milestone2ActionCue.Drive))
            {
                engineRpm = 0f;
                engineReleasing = false;
            }
            if (!powering)
            {
                // Pitch and volume descend together, starting at the release
                // edge. Braking changes pitch speed, not the volume fade time.
                voices.FadeEngineToIdle(Milestone2ActionCue.Drive, braking);
                engineReleasing = voices.IsAlive(Milestone2ActionCue.Drive);
                return;
            }
            if (engineReleasing)
            {
                engineRpm = RpmFromPitch(voices.GetEnginePitch(Milestone2ActionCue.Drive));
                engineReleasing = false;
            }
            // Use one stable part of M02 for every RPM. Its original intro has
            // an independent rising sweep, so a 1x intro cannot obey this cap.
            engineRpm = Mathf.MoveTowards(engineRpm, target, settings.EngineAccelerationPerSecond * deltaTime);
            voices.SetPitch(Milestone2ActionCue.Drive, Mathf.Lerp(settings.EngineMinimumPitch, settings.EngineMaximumPitch, engineRpm));
            voices.SetLoopFrom(Milestone2ActionCue.Drive, true, 3500);
        }

        private void StopGroundImmediately(bool preserveTumbleSlide = false)
        {
            voices.Stop(Milestone2ActionCue.GroundMove, true);
            voices.Stop(Milestone2ActionCue.GroundTurn, true);
            if (!preserveTumbleSlide)
            {
                voices.Stop(Milestone2ActionCue.Slide, true);
                voices.SetPitch(Milestone2ActionCue.Slide, 1f);
                tumbleSlideActive = false;
                tumbleSlideRemaining = 0f;
            }
            slipEpisode = false;
            groundTurnLooping = false;
        }

        private void OnTumbleStarted(RobotTumbleStartedInfo info)
        {
            if (source == null || !source.isActiveAndEnabled || tumble == null || !tumble.isActiveAndEnabled
                || !source.gameObject.scene.isLoaded || Time.timeScale <= 0f
                || lastTumbleStartFrame == Time.frameCount
                || (height != null && height.isActiveAndEnabled && height.HasData && height.IsAirborne)) return;
            lastTumbleStartFrame = Time.frameCount;
            bool continuingSlide = slipEpisode && voices.IsAlive(Milestone2ActionCue.Slide);
            voices.SetPitch(Milestone2ActionCue.Slide, settings.TumbleSlidePitch);
            if (continuingSlide)
                // Preserve the preceding slip's handle and sample position, even
                // if it was paused at a brief standstill. Never play its recovery tail.
                voices.SetPaused(Milestone2ActionCue.Slide, false);
            else
                // A direct tip has no preceding slip to continue. Reuse the
                // delivered M06 steady segment, without replaying its loss-of-grip intro.
                voices.PlayFrom(Milestone2ActionCue.Slide, 1230);
            tumbleSlideActive = voices.IsAlive(Milestone2ActionCue.Slide);
            tumbleSlideRemaining = settings.TumbleSlideAccentSeconds;
            slipEpisode = false;
            StopGroundImmediately(tumbleSlideActive);
        }

        private void UpdateTumbleSlide(float deltaTime, bool airborne, bool slippingAgain)
        {
            if (!tumbleSlideActive) return;
            if (airborne || slippingAgain || tumble == null || !tumble.isActiveAndEnabled)
            {
                voices.Stop(Milestone2ActionCue.Slide, true);
                tumbleSlideActive = false;
                voices.SetPitch(Milestone2ActionCue.Slide, 1f);
                return;
            }
            if (!voices.IsAlive(Milestone2ActionCue.Slide))
            {
                tumbleSlideActive = false;
                voices.SetPitch(Milestone2ActionCue.Slide, 1f);
                return;
            }
            // Started occurs during Update: this frame's delta predates the
            // accent and must not consume its whole lifetime on a slow frame.
            if (Time.frameCount == lastTumbleStartFrame) return;
            tumbleSlideRemaining -= deltaTime;
            if (tumbleSlideRemaining <= 0f)
                // Keep ownership through the fade so non-Driven cleanup does not
                // cut it short. Lifecycle/airborne cleanup still releases immediately.
                voices.FadeStop(Milestone2ActionCue.Slide, settings.FadeOutSeconds, true);
        }

        private void UpdateGroundTurn(bool turning, bool canPlayTail)
        {
            if (turning)
                // A keyed-off tail is replaced in the same fixed slot, from
                // the loop start; it never resumes with TurnHold still zero.
                voices.SetLoopFrom(Milestone2ActionCue.GroundTurn, true, 0);
            else if (!canPlayTail)
                voices.Stop(Milestone2ActionCue.GroundTurn, true);
            else if (groundTurnLooping)
                // Let the source's natural tail finish without a gain fade or
                // repeated stop/key-off requests on subsequent idle frames.
                voices.KeyOff(Milestone2ActionCue.GroundTurn);
            groundTurnLooping = turning;
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
                || source.MovementMode != RobotMovementMode.Driven
                || Milestone3AudioRuntime.SuppressesOrdinaryLanding(source)) return;
            voices.PlayWithGain(Milestone2ActionCue.Land, Mathf.Clamp01(impact.Strength01));
            voices.Play(Milestone2ActionCue.LandParts);
        }
    }
}

using AnimalGame.RobotMap;
using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace Capstone.Audio
{
    internal sealed class Milestone2MovementAudio
    {
        private const int GrassAccelerationIntroMs = 2400;
        private const float SurfaceFullSpeedFraction = .98f;
        private const float SurfaceReaccelerationFraction = .20f;
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
        private readonly bool hasSurfaceAudio;
        private Milestone2ActionCue surfaceCue = Milestone2ActionCue.Count;
        private float surfaceSampleRemaining;
        private float grassIntroRemaining;
        private float grassPreviousStrength, grassLowStrength = 1f;
        private bool surfaceIntro;
        private int surfaceSeekMs = -1;
        private float surfaceSeekGain = 1f;
        private float surfacePeakStrength, surfaceLowStrength;
        private float tumbleSlideRemaining;
        private int lastTumbleStartFrame = -1;

        internal RobotMover Source => source;
        internal RobotHeightMotionDetector HeightSource => height;
        internal RobotTumbleController TumbleSource => tumble;

        internal Milestone2MovementAudio(Milestone2ActionSettings settings, Milestone2ActionVoices voices)
        {
            this.settings = settings;
            this.voices = voices;
            hasSurfaceAudio = settings.HasCue(Milestone2ActionCue.GrassRoll) || settings.HasCue(Milestone2ActionCue.SandRoll)
                || settings.HasCue(Milestone2ActionCue.RockRoll) || settings.HasCue(Milestone2ActionCue.GravelRoll);
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
            bool wasTranslating = linearMotion;
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
            bool rolling = driven && !airborne && (translating || (slip && turning));
            if (hasSurfaceAudio)
            {
                var previousSurface = surfaceCue;
                UpdateSurface(rolling && !source.IgnoreCollisionAndTerrain && !source.HasTowerSupport
                    && !source.CurrentClimbableSurface.IsActive && source.CurrentWaterSpeedMultiplier >= 1f, deltaTime);
                if (surfaceCue == Milestone2ActionCue.GrassRoll)
                    UpdateGrassRoll(!wasTranslating && translating, speed, longitudinalSpeed, deltaTime);
                else if (surfaceCue != Milestone2ActionCue.Count)
                    UpdateSurfaceRoll(previousSurface != surfaceCue, !wasTranslating && !wasAirborne, speed, longitudinalSpeed);
            }
            // Each assigned material supplies the complete translational ground sound.
            voices.SetLoop(Milestone2ActionCue.GroundMove, rolling
                && (surfaceCue == Milestone2ActionCue.Count || !translating));
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
            if (hasSurfaceAudio)
            {
                // A previous surface can still be in its exit fade at takeoff.
                for (var cue = Milestone2ActionCue.GrassRoll; cue <= Milestone2ActionCue.GrassRollAccel; cue++)
                    voices.Stop(cue, true);
                surfaceCue = Milestone2ActionCue.Count;
                surfaceSampleRemaining = 0f;
                ResetGrassMotion();
            }
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

        private void UpdateSurface(bool rollingOnTerrain, float deltaTime)
        {
            var next = Milestone2ActionCue.Count;
            if (rollingOnTerrain)
            {
                surfaceSampleRemaining -= deltaTime;
                if (surfaceSampleRemaining > 0f) return;
                surfaceSampleRemaining = 0.08f;
                var bootstrap = HeightMapPlayerSceneBootstrap.inst;
                var map = bootstrap != null && bootstrap.robot == source ? bootstrap.map : null;
                if (WorldTerrainQuery.TrySampleWorldPosition(map, source.MapPosition, out var terrain))
                {
                    switch (terrain)
                    {
                        case TerrainKind.Grass:
                        case TerrainKind.Grassland: next = Milestone2ActionCue.GrassRoll; break;
                        case TerrainKind.Sand: next = Milestone2ActionCue.SandRoll; break;
                        case TerrainKind.Rock: next = Milestone2ActionCue.RockRoll; break;
                        case TerrainKind.Gravel: next = Milestone2ActionCue.GravelRoll; break;
                    }
                    if (next != Milestone2ActionCue.Count && !settings.HasCue(next)) next = Milestone2ActionCue.Count;
                }
            }
            else surfaceSampleRemaining = 0f;
            if (next == surfaceCue) return;
            if (surfaceCue != Milestone2ActionCue.Count) voices.Stop(surfaceCue);
            if (surfaceCue == Milestone2ActionCue.GrassRoll)
            {
                voices.Stop(Milestone2ActionCue.GrassRollAccel);
                ResetGrassMotion();
            }
            surfaceCue = next;
        }

        private float SurfaceSpeedFraction(float speed, float longitudinalSpeed)
        {
            float ratedSpeed = longitudinalSpeed < -settings.MovementThreshold ? source.UnloadedReverseSpeed
                : (source.DebugMoveSpeed ?? source.DefaultMoveSpeed);
            float strength = Mathf.Clamp01(speed / Mathf.Max(.01f, ratedSpeed));
            return strength >= SurfaceFullSpeedFraction ? 1f : strength;
        }

        private void UpdateSurfaceRoll(bool entered, bool starting, float speed, float longitudinalSpeed)
        {
            if (!linearMotion)
            {
                voices.Stop(surfaceCue);
                return;
            }
            int sustainMs = surfaceCue == Milestone2ActionCue.SandRoll ? 2500 : 2000;
            float strength = SurfaceSpeedFraction(speed, longitudinalSpeed);
            bool cruiseImmediately = strength == 1f && surfaceCue != Milestone2ActionCue.RockRoll;
            int entryMs = 0;
            if (entered || starting)
            {
                // Sand contains the gravel recording on its own synchronized track.
                // Retire a previous copy even if a narrow tile left it fading.
                if (surfaceCue == Milestone2ActionCue.SandRoll)
                {
                    voices.Stop(Milestone2ActionCue.GravelRoll, true);
                    voices.Stop(Milestone2ActionCue.GroundMove, true);
                }
                else if (surfaceCue == Milestone2ActionCue.GravelRoll)
                    voices.Stop(Milestone2ActionCue.SandRoll, true);

                // The sand mix and its standalone gravel layer both skip their intro
                // when the first motion sample already measures full speed.
                bool startFromRest = starting && !cruiseImmediately;
                entryMs = startFromRest ? 0 : Mathf.RoundToInt(sustainMs * strength);
                surfaceIntro = entryMs < sustainMs;
                surfaceSeekGain = 1f;
                surfaceSeekMs = -1;
                surfacePeakStrength = strength;
                surfaceLowStrength = -1f;
                if (voices.TryGetTimelinePosition(surfaceCue, out int currentMs))
                {
                    // Brief material changes may return to a still-fading voice.
                    // Only genuine rest can rewind it to the onset.
                    surfaceIntro &= startFromRest || currentMs < sustainMs;
                    int toleranceMs = entryMs == sustainMs ? 0 : 60;
                    if (startFromRest || currentMs < entryMs - toleranceMs)
                        surfaceSeekMs = entryMs;
                }
            }
            else
            {
                ObserveSurfaceReacceleration(strength, sustainMs);
                if (surfaceIntro && strength == 1f)
                {
                    surfaceIntro = false;
                    // A natural handoff (or a rise straight to full speed) needs no rewind.
                    int toleranceMs = cruiseImmediately ? 0 : 60;
                    surfaceSeekMs = voices.TryGetTimelinePosition(surfaceCue, out int currentMs)
                        && currentMs < sustainMs - toleranceMs ? sustainMs : -1;
                }
            }

            // Attenuate around a live seek, using the same voice and source. A low
            // floor masks the splice without inserting a silent frame at the boundary.
            if (surfaceSeekMs == sustainMs && (entered || cruiseImmediately)) surfaceSeekGain = .03f;
            float dt = Time.unscaledDeltaTime;
            surfaceSeekGain = surfaceSeekMs >= 0
                ? Mathf.MoveTowards(surfaceSeekGain, .03f, dt / .025f)
                : Mathf.MoveTowards(surfaceSeekGain, 1f, dt / .04f);
            voices.SetLoopFrom(surfaceCue, true, entryMs, gain: Mathf.Lerp(.7f, 1f, strength) * surfaceSeekGain);
            if (surfaceSeekMs >= 0 && surfaceSeekGain <= .03f)
            {
                voices.SetTimelinePosition(surfaceCue, surfaceSeekMs);
                surfaceSeekMs = -1;
            }
        }

        private void ObserveSurfaceReacceleration(float strength, int sustainMs)
        {
            if (surfaceLowStrength < 0f)
            {
                surfacePeakStrength = Mathf.Max(surfacePeakStrength, strength);
                if (surfacePeakStrength - strength < SurfaceReaccelerationFraction) return;
                surfacePeakStrength = strength;
                if (surfaceSeekMs >= 0) return;
                if (surfaceIntro)
                {
                    // Sample on a significant drop only. The intro may already
                    // have crossed into the native loop at less than full speed.
                    if (!voices.TryGetTimelinePosition(surfaceCue, out int ms) || ms < sustainMs) return;
                    surfaceIntro = false;
                }
                surfaceLowStrength = strength;
                return;
            }

            surfaceLowStrength = Mathf.Min(surfaceLowStrength, strength);
            if (strength - surfaceLowStrength < SurfaceReaccelerationFraction) return;
            surfaceLowStrength = -1f;
            surfacePeakStrength = strength;
            if (strength < 1f)
            {
                surfaceSeekMs = Mathf.RoundToInt(sustainMs * strength);
                surfaceIntro = true;
            }
        }

        private void UpdateGrassRoll(bool starting, float speed, float longitudinalSpeed, float deltaTime)
        {
            if (!linearMotion)
            {
                // Uncontrolled ground rotation can keep a material selected at rest.
                voices.Stop(Milestone2ActionCue.GrassRollAccel);
                voices.Stop(Milestone2ActionCue.GrassRoll);
                ResetGrassMotion();
                return;
            }
            float strength = SurfaceSpeedFraction(speed, longitudinalSpeed);
            if (settings.HasCue(Milestone2ActionCue.GrassRollAccel))
            {
                if (starting && strength < 1f && voices.Play(Milestone2ActionCue.GrassRollAccel))
                    grassIntroRemaining = GrassAccelerationIntroMs / 1000f;
                else if (!starting && !voices.IsAlive(Milestone2ActionCue.GrassRollAccel)
                    && strength - grassLowStrength >= SurfaceReaccelerationFraction)
                {
                    grassLowStrength = 1f;
                    voices.PlayFrom(Milestone2ActionCue.GrassRollAccel, Mathf.RoundToInt(GrassAccelerationIntroMs * strength));
                }

                // Track a settled or falling speed. An ongoing accent absorbs rises
                // while it plays, so a single ramp cannot queue another onset.
                if (starting || voices.IsAlive(Milestone2ActionCue.GrassRollAccel)) grassLowStrength = 1f;
                if (strength <= grassPreviousStrength + .01f * deltaTime)
                    grassLowStrength = Mathf.Min(grassLowStrength, strength);
                grassPreviousStrength = strength;
            }
            if (!starting && grassIntroRemaining > 0f)
            {
                grassIntroRemaining -= Time.unscaledDeltaTime;
                // Only sample near the transition. Mixer startup can lag a frame.
                if (strength < 1f && grassIntroRemaining <= 0f && voices.TryGetTimelinePosition(
                    Milestone2ActionCue.GrassRollAccel, out int ms))
                    grassIntroRemaining = (GrassAccelerationIntroMs - ms) / 1000f;
            }
            if (strength == 1f) grassIntroRemaining = 0f;
            if (grassIntroRemaining > 0f) return;

            // Once joined, the loop tracks actual speed. The one-shot keeps its
            // original timeline and authored ending through both fast and slow travel.
            voices.SetLoopFrom(Milestone2ActionCue.GrassRoll, true, 0, gain: Mathf.Lerp(.7f, 1f, strength));
        }

        private void ResetGrassMotion()
        {
            grassIntroRemaining = grassPreviousStrength = 0f;
            grassLowStrength = 1f;
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

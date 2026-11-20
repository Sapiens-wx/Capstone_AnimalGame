using System;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.Discovery;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace AnimalGame.Editor
{
    /// <summary>Exercises released radar particles and their camera observation lifetime together.</summary>
    public static class DiscoveryScanObservationRegressionChecks
    {
        private const string RobotPrefabPath = "Assets/Prefabs/Resources/Robot/RobotMarker.prefab";
        private const string UiPrefabPath = "Assets/Prefabs/Resources/UI/MainUI.prefab";
        private static int passedChecks;

        [MenuItem("Animal Game/Validation/Run Discovery Scan Observation Checks")]
        public static void Run()
        {
            float originalTimeScale = Time.timeScale;
            Random.State originalRandom = Random.state;
            try
            {
                passedChecks = 0;
                Check("Authored slower scan preserves the user's 13.5 observation profile and angular density", CheckAuthoredRange);
                Check("Formation is excluded while outgoing particles remain authoritative", CheckWaveOwnership);
                Check("Resized outer fringe is detected and launch tuning is snapshotted", CheckDetectionRange);
                Check("Released particles retain their world-space anchors", CheckWorldSpace);
                Check("Discovery pullback accelerates faster and brakes throughout its latter half", CheckPullbackShape);
                Check("Arrival rebound is a single continuous outward pulse with controlled settling", CheckReboundShape);
                Check("Camera reaches its target before rebounding and consumes cross-phase frame time", CheckReboundLifecycle);
                Check("A disabled rebound or an already wider view does not pulse", CheckReboundGuards);
                Check("Slower waves retain pullback timing and the camera returns thirty percent slower", CheckObservationLifetime);
                Check("Camera expansion duration is snapshotted and the next release uses new tuning", CheckArrivalSnapshot);
                Check("Short waves return before completing pullback and camera timing retains a finite minimum", CheckShortWaveArrival);
                Check("Recharging and cancellation preserve the current wide observation", CheckRechargeAndCancel);
                Check("Repeated releases wait for the last outgoing particle", CheckRepeatedRelease);
                Check("Pause preserves the released wave and observation", CheckPause);
                Check("Disabling the source or the view cleans up observation", CheckDisabledCleanup);
                Check("Observation respects larger camera bases and independent camera effects", CheckComposition);
                Debug.Log("Discovery scan observation checks PASS: " + passedChecks
                    + " checks; authored coverage, real particles, fringe detection, launch snapshots, world anchors,"
                    + " pullback velocity, arrival rebound, independent camera duration, arrival snapshots, short waves, observation lifetime, repeated release,"
                    + " cancellation, pause, cleanup and camera composition.");
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                Random.state = originalRandom;
            }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static BioScanController LoadRobotProfile()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
            Require(prefab != null, "RobotMarker prefab is missing.");
            BioScanController profile = prefab.GetComponentInChildren<BioScanController>(true);
            Require(profile != null, "RobotMarker has no BioScanController.");
            return profile;
        }

        private static ScanChargeUI LoadViewProfile()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiPrefabPath);
            Require(prefab != null, "MainUI prefab is missing.");
            ScanChargeUI profile = prefab.GetComponentInChildren<ScanChargeUI>(true);
            Require(profile != null, "MainUI has no ScanChargeUI.");
            return profile;
        }

        private static void CheckAuthoredRange()
        {
            BioScanController profile = LoadRobotProfile();
            const float previousObservationSize = 15.5f;
            const float observationSize = 13.5f;
            float expectedRadius = 12.2f * 1.2f * observationSize / previousObservationSize;
            Require(Near(profile.MaximumScanRadiusWorld, expectedRadius),
                "Effective radius must follow the 15.5-to-13.5 observation ratio, including the ready ring.");
            Require(Near((float)Get(profile, "signalSpeed"), 17.4f * .8f),
                "The real authored wave must propagate 20 percent slower than the prior fast wave.");
            Require(Near((float)Get(profile, "signalFadeOutDuration"), .12f)
                && Near((float)Get(profile, "signalPointDiameter"), .12f)
                && Near((float)Get(profile, "signalHeadCoreRatio"), .5f),
                "The slower wave must retain its short fade and firmer, thicker front.");
            Require((int)Get(profile, "maximumPointCount") == 360,
                "The enlarged circumference requires the agreed 360-point density cap.");
            int points = (int)Call(profile, "CalculateEffectivePointCount");
            Require(points / profile.MaximumScanRadiusWorld >= 299f / 12.2f - .001f,
                "The expanded wave reduced the original angular signal density.");
            ScanChargeUI view = LoadViewProfile();
            float reboundRatio = (float)Get(view, "discoveryObservationReboundRatio");
            float reboundDuration = (float)Get(view, "discoveryObservationReboundDuration");
            Require(Near((float)Get(view, "discoveryObservationOrthographicSize"), observationSize)
                && Near((float)Get(view, "releaseZoomOutDuration"), .45f)
                && Near((float)Get(view, "discoveryObservationZoomOutDuration"), .541f)
                && reboundRatio >= 0f && reboundRatio <= .1f
                && reboundDuration >= .01f && !float.IsInfinity(reboundDuration)
                && Near((float)Get(view, "releaseZoomReturnDuration"), .55f / .7f),
                "The authored observation size or transition timing does not match the agreed profile.");
            using (var f = new Fixture())
            {
                f.Launch();
                var particles = new ParticleSystem.Particle[f.Emitted.particleCount];
                Require(f.Emitted.GetParticles(particles) > 0 && Near(particles[0].startSize, .12f),
                    "The authored thicker wave front is not applied to actual emitted particles.");
                float lifetime = f.Source.LastReleasedWaveTravelDuration;
                Material material = (Material)Get(f.Source, "signalPointMaterial");
                Require(material != null && Near(material.GetFloat("_PointCoreRatio"), .5f),
                    "The firmer wave core was not transferred to the real particle material.");
                Gradient fade = f.Emitted.colorOverLifetime.color.gradient;
                Require(f.Emitted.colorOverLifetime.enabled && Near(fade.Evaluate(1f - .12f / lifetime).a, 1f)
                    && Near(fade.Evaluate(1f).a, 0f),
                    "The emitted front does not keep full strength until its short final fade.");
            }
        }

        private static void CheckWaveOwnership()
        {
            using (var f = new Fixture())
            {
                Call(f.Source, "BeginSignalFormation");
                Call(f.Source, "UpdateSignalFormation", 1f, 1f);
                Require(f.Forming.particleCount > 0 && !f.Source.HasActiveReleasedScanWaves,
                    "An unreleased ready ring must not keep the camera in observation.");
                f.Launch();
                Require(f.Forming.particleCount == 0 && f.Emitted.particleCount >= 360
                    && f.Source.HasActiveReleasedScanWaves,
                    "A real release must transfer ready points into an authoritative outgoing wave.");
                // Older particles can outlive a newer, shorter detector wave.
                Set(f.Source, "maximumSignalDistance", .5f);
                Set(f.Source, "signalSpeed", 30f);
                f.Launch();
                f.AdvanceWave(.03f);
                Require(!f.Source.HasActiveScanWave && f.Emitted.particleCount > 0
                    && f.Source.HasActiveReleasedScanWaves,
                    "Ending the newest detector must not ignore older outgoing particles.");
                f.AdvanceWave(3f);
                Require(!f.Source.HasActiveReleasedScanWaves, "A drained outgoing system retained a stale active flag.");
            }
        }

        private static void CheckDetectionRange()
        {
            using (var f = new Fixture())
            {
                float maximumRadius = f.Source.MaximumScanRadiusWorld;
                float edgeTolerance = (float)Get(f.Source, "scanWaveHalfThickness") + .01f;
                DiscoverableEntity expandedFringe = f.Plant(maximumRadius);
                DiscoverableEntity outside = f.Plant(maximumRadius + edgeTolerance + .1f);
                f.Launch();
                float travel = f.Source.LastReleasedWaveTravelDuration;
                // Inspector/runtime changes affect the next release, not an airborne wave.
                Set(f.Source, "maximumSignalDistance", 50f);
                Set(f.Source, "signalSpeed", .1f);
                f.AdvanceWave(travel * .8f);
                Require(!expandedFringe.IsTemporarilyRevealed && f.Source.HasActiveScanWave,
                    "The old scan duration or newly changed speed was applied to an airborne wave.");
                f.AdvanceWave(travel * .2f + .01f);
                Require(expandedFringe.IsTemporarilyRevealed && !outside.IsDiscovered && !outside.IsTemporarilyRevealed
                    && !f.Source.HasActiveScanWave && !f.Source.HasActiveReleasedScanWaves,
                    "Detection must reach the resized outer fringe and ignore targets beyond the launch radius and wave thickness.");
            }
        }

        private static void CheckWorldSpace()
        {
            using (var f = new Fixture())
            {
                f.Launch();
                var particles = new ParticleSystem.Particle[f.Emitted.particleCount];
                int count = f.Emitted.GetParticles(particles);
                Require(count > 0 && f.Emitted.main.simulationSpace == ParticleSystemSimulationSpace.World,
                    "Outgoing points must use world simulation space.");
                Vector3 original = particles[0].position;
                float speed = ((Vector2)particles[0].velocity).magnitude;
                Require(Near(speed, (float)Get(f.Source, "signalSpeed")),
                    "The actual outgoing particle velocity does not use the authored slower scan speed.");
                f.Source.transform.position += new Vector3(4f, -3f, 0f);
                f.Emitted.GetParticles(particles);
                Require(Vector3.Distance(particles[0].position, original) < .001f,
                    "Moving the robot moved an already released wave.");
                f.AdvanceWave(.1f);
                f.Emitted.GetParticles(particles);
                Require(Mathf.Abs(Vector3.Distance(particles[0].position, original) - speed * .1f) < .005f,
                    "The particle did not propagate from its emission point at the authored speed.");
            }
        }

        private static void CheckPullbackShape()
        {
            MethodInfo evaluator = typeof(ScanChargeUI).GetMethod("EvaluateDiscoveryPullback",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(evaluator != null, "Discovery pullback has no dedicated motion evaluator.");
            Func<float, float> sample = progress => (float)evaluator.Invoke(null, new object[] { progress });
            Require(Near(sample(-1f), 0f) && Near(sample(0f), 0f)
                && Near(sample(1f), 1f) && Near(sample(2f), 1f),
                "Pullback must begin and end exactly at its clamped targets.");
            float previous = 0f;
            for (int i = 1; i <= 1024; i++)
            {
                float value = sample(i / 1024f);
                Require(value >= previous && value >= 0f && value <= 1f,
                    "Pullback reverses, overshoots or produces a non-finite sample.");
                previous = value;
            }
            const float earlyProgress = .04f;
            Require(sample(earlyProgress) > Mathf.SmoothStep(0f, 1f, earlyProgress) * 2f,
                "The short starting push remains as soft as the old full-duration SmoothStep.");

            const float delta = .001f;
            Func<float, float> velocity = progress => (sample(progress + delta)
                - sample(progress - delta)) / (2f * delta);
            float cruiseSpeed = velocity(.25f);
            Require(cruiseSpeed > 1f, "The main push never establishes a sustained higher speed.");
            const float priorAccelerationDuration = .08f / 1.16f;
            const float priorBrakeDuration = .18f / 1.16f;
            float priorInitialAcceleration = 1f / (1f - (priorAccelerationDuration
                + priorBrakeDuration) * .5f) / priorAccelerationDuration;
            float initialAcceleration = (velocity(.025f) - velocity(.015f)) / .01f;
            Require(initialAcceleration > priorInitialAcceleration * 2f,
                "The initial camera acceleration did not become substantially stronger than the prior pullback.");
            foreach (float progress in new[] { .12f, .25f, .4f })
                Require(Mathf.Abs(velocity(progress) - cruiseSpeed) < cruiseSpeed * .02f,
                    "The first-half camera push lost its sustained driving speed.");
            Require(velocity(.5f) < cruiseSpeed * .95f
                && velocity(.65f) < velocity(.5f) * .8f
                && velocity(.8f) < velocity(.65f) * .8f
                && velocity(.95f) < velocity(.8f) * .3f,
                "The latter half must begin braking early and keep slowing toward its final target.");
            foreach (float join in new[] { .04f, .45f })
            {
                float left = (sample(join) - sample(join - delta)) / delta;
                float right = (sample(join + delta) - sample(join)) / delta;
                Require(Mathf.Abs(left - right) < cruiseSpeed * .03f,
                    "The camera changes speed abruptly between acceleration, drive and braking.");
            }
            Require((sample(delta) - sample(0f)) / delta < cruiseSpeed * .02f
                && (sample(1f) - sample(1f - delta)) / delta < cruiseSpeed * .02f
                && velocity(.99f) < velocity(.9f) * .2f,
                "The camera starts or stops with residual velocity instead of a controlled arrival.");
        }

        private static void CheckReboundShape()
        {
            MethodInfo evaluator = typeof(ScanChargeUI).GetMethod("EvaluateDiscoveryRebound",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(evaluator != null, "Discovery arrival has no dedicated rebound evaluator.");
            Func<float, float> sample = progress => (float)evaluator.Invoke(null, new object[] { progress });
            Require(Near(sample(-1f), 0f) && Near(sample(0f), 0f)
                && Near(sample(.32f), 1f) && Near(sample(1f), 0f) && Near(sample(2f), 0f),
                "The rebound must leave and return to its exact target with one controlled peak.");
            float previous = 0f;
            for (int i = 1; i <= 1024; i++)
            {
                float progress = i / 1024f;
                float value = sample(progress);
                Require(!float.IsNaN(value) && value >= 0f && value <= 1f,
                    "The rebound generates an invalid size or exceeds its authored amplitude.");
                if (progress <= .32f)
                    Require(value >= previous, "The rebound reverses before its outward peak.");
                else if ((i - 1f) / 1024f >= .32f)
                    Require(value <= previous, "The rebound adds a second oscillation while settling.");
                previous = value;
            }
            const float delta = .0001f;
            Require(sample(delta) / delta < .01f && sample(1f - delta) / delta < .01f
                && (1f - sample(.32f - delta)) / delta < .01f
                && (1f - sample(.32f + delta)) / delta < .01f,
                "The rebound has residual velocity at arrival, its peak or its final hold.");
        }

        private static void CheckReboundLifecycle()
        {
            using (var f = new Fixture())
            using (var skippedBoundary = new Fixture())
            using (var skippedBothBoundaries = new Fixture())
            {
                f.Charge(1f); f.Release(); f.TickCamera(0f);
                skippedBoundary.Charge(1f); skippedBoundary.Release(); skippedBoundary.TickCamera(0f);
                skippedBothBoundaries.Charge(1f); skippedBothBoundaries.Release(); skippedBothBoundaries.TickCamera(0f);
                float arrival = f.ArrivalDuration;
                float rebound = f.ReboundDuration;
                float amplitude = f.TargetSize * .03f;
                f.AdvanceWave(arrival); f.TickCamera(arrival);
                Require(f.ObservationPhase == "Rebounding" && Near(f.Size, f.TargetSize),
                    "The original pullback must reach its exact target before the extra rebound begins.");
                float toPeak = rebound * .32f;
                f.AdvanceWave(toPeak); f.TickCamera(toPeak);
                Require(f.ObservationPhase == "Rebounding" && Near(f.Size, f.TargetSize + amplitude),
                    "Arrival did not produce the authored three-percent outward rebound.");
                skippedBoundary.AdvanceWave(arrival + toPeak);
                skippedBoundary.TickCamera(arrival + toPeak);
                Require(skippedBoundary.ObservationPhase == "Rebounding" && Near(skippedBoundary.Size, f.Size),
                    "A frame crossing arrival discarded its remaining time or postponed the rebound.");
                f.AdvanceWave(rebound - toPeak + .001f); f.TickCamera(rebound - toPeak + .001f);
                Require(f.ObservationPhase == "Holding" && Near(f.Size, f.TargetSize)
                    && f.Source.HasActiveReleasedScanWaves,
                    "The rebound failed to settle back to the original observation target while the wave remained.");
                skippedBoundary.AdvanceWave(rebound - toPeak + .01f);
                skippedBoundary.TickCamera(rebound - toPeak + .01f);
                Require(skippedBoundary.ObservationPhase == "Holding" && Near(skippedBoundary.Size, f.TargetSize),
                    "Crossing both arrival boundaries extended the rebound or left a residual offset.");
                float singleFrame = arrival + rebound + .01f;
                skippedBothBoundaries.AdvanceWave(singleFrame);
                skippedBothBoundaries.TickCamera(singleFrame);
                Require(skippedBothBoundaries.ObservationPhase == "Holding"
                    && Near(skippedBothBoundaries.Size, f.TargetSize),
                    "One frame crossing expansion and rebound failed to consume the full elapsed time.");
            }
        }

        private static void CheckReboundGuards()
        {
            using (var f = new Fixture(useAuthoredRebound: true))
            {
                Require(Near((float)Get(f.View, "discoveryObservationReboundRatio"),
                    (float)Get(LoadViewProfile(), "discoveryObservationReboundRatio"))
                    && Near(f.ReboundDuration, Mathf.Max(.01f,
                        (float)Get(LoadViewProfile(), "discoveryObservationReboundDuration"))),
                    "The authored rebound configuration was not retained by the real-profile fixture.");
                // The current authored zero takes this path without changing it.
                // Keep the disabled-pulse check valid if a designer enables it later.
                if ((float)Get(f.View, "discoveryObservationReboundRatio") > 0f)
                    Set(f.View, "discoveryObservationReboundRatio", 0f);
                f.Charge(1f); f.Release(); f.TickCamera(0f);
                f.Advance(f.ArrivalDuration + .01f);
                Require(f.ObservationPhase == "Holding" && Near(f.Size, f.TargetSize)
                    && Near((float)Get(f.View, "discoveryObservationReboundAmplitude"), 0f),
                    "A zero rebound ratio must skip the pulse and retain the original holding behavior.");
            }
            using (var f = new Fixture(18f))
            {
                // An already wide observation must not bounce simply because a new wave begins.
                Call(f.View, "CancelCharge");
                f.Launch(); f.TickCamera(0f);
                f.Advance(f.ArrivalDuration + .01f);
                Require(f.ObservationPhase == "Holding" && Near(f.Size, 18f)
                    && Near((float)Get(f.View, "discoveryObservationReboundAmplitude"), 0f),
                    "A release with no outward camera expansion must not acquire an arrival rebound.");
            }
            using (var f = new Fixture())
            {
                Set(f.View, "currentScanOrthographicSize", f.TargetSize);
                f.Apply();
                f.Launch(); f.TickCamera(0f);
                f.Advance(f.ArrivalDuration + .01f);
                Require(f.ObservationPhase == "Holding" && Near(f.Size, f.TargetSize)
                    && Near((float)Get(f.View, "discoveryObservationReboundAmplitude"), 0f),
                    "A wave beginning at its observation target must not pulse without a new expansion.");
            }
        }

        private static void CheckObservationLifetime()
        {
            using (var f = new Fixture())
            using (var previous = new Fixture())
            {
                Set(previous.Source, "signalSpeed", 17.4f);
                f.Charge(1f);
                previous.Charge(1f);
                float chargedSize = f.Size;
                f.Release();
                previous.Release();
                f.TickCamera(0f);
                previous.TickCamera(0f);
                Require(Near(f.Size, chargedSize), "Release snapped instead of starting from the charged view.");
                float travelDuration = f.Source.LastReleasedWaveTravelDuration;
                float arrival = f.ArrivalDuration;
                float holdingDuration = travelDuration - arrival;
                float previousHoldingDuration = previous.Source.LastReleasedWaveTravelDuration - previous.ArrivalDuration;
                float speed = (float)Get(f.Source, "signalSpeed");
                float distance = (float)Get(f.Source, "maximumSignalDistance");
                Require(Near(travelDuration, distance / speed)
                    && Near(travelDuration, .90165f)
                    && Near(arrival, .541f)
                    && Near((float)Get(f.View, "discoveryObservationExpansionDuration"), arrival)
                    && Near((float)Get(previous.View, "discoveryObservationExpansionDuration"), arrival)
                    && holdingDuration - previousHoldingDuration > .17f,
                    "Slowing the real wave must retain the prior camera duration and extend only observation holding.");
                const float earlyProgress = .04f;
                f.Advance(arrival * earlyProgress);
                previous.Advance(arrival * earlyProgress);
                float earlyDisplacement = Mathf.InverseLerp(chargedSize, f.TargetSize, f.Size);
                Require(earlyDisplacement > Mathf.SmoothStep(0f, 1f, earlyProgress) * 2f
                    && earlyDisplacement < .15f && Near(f.Size, previous.Size),
                    "The real released camera did not use the stronger starting push, or jumped on release.");
                f.Advance(arrival * (.5f - earlyProgress));
                previous.Advance(arrival * (.5f - earlyProgress));
                Require(f.ObservationPhase == "Expanding" && f.Size < f.TargetSize - .05f
                    && Near(f.Size, previous.Size),
                    "Changing the wave speed retimed the camera's main driving segment.");
                f.Advance(arrival * .4f);
                previous.Advance(arrival * .4f);
                Require(f.ObservationPhase == "Expanding" && f.Size < f.TargetSize - .001f,
                    "The camera reached its final size before the scheduled arrival boundary.");
                f.Advance(arrival * .1f + .001f);
                previous.Advance(arrival * .1f + .001f);
                Require(f.Source.HasActiveReleasedScanWaves && Near(f.Size, f.TargetSize)
                    && f.ObservationPhase == "Rebounding" && previous.ObservationPhase == "Rebounding"
                    && Near(previous.Size, f.Size),
                    "The camera failed to reach its target at the prior arrival time before the new rebound.");
                float waveRemaining = ((float)Get(f.Source, "scanWaveMaximumRadius")
                    - (float)Get(f.Source, "scanWaveRadius")) / (float)Get(f.Source, "scanWaveSpeed");
                Require(Mathf.Abs(waveRemaining - holdingDuration) < .003f,
                    "The remaining real wave lifetime does not cover the rebound and subsequent holding.");
                float pastPreviousWave = previousHoldingDuration + .02f;
                f.Advance(pastPreviousWave);
                previous.Advance(pastPreviousWave);
                Require(f.Source.HasActiveReleasedScanWaves && f.Size > f.TargetSize
                    && f.ObservationPhase == "Rebounding" && !previous.Source.HasActiveReleasedScanWaves
                    && previous.ObservationPhase == "Returning",
                    "The slower wave failed to retain observation during the longer rebound after the prior faster wave expired.");
                float settledFromArrival = f.ReboundDuration + .005f;
                f.Advance(settledFromArrival - pastPreviousWave);
                Require(f.Source.HasActiveReleasedScanWaves && Near(f.Size, f.TargetSize)
                    && f.ObservationPhase == "Holding",
                    "The longer rebound did not settle into holding while the slower wave was still visible.");
                f.Advance(holdingDuration - settledFromArrival + .02f);
                Require(!f.Source.HasActiveReleasedScanWaves && f.Size > 11f,
                    "Wave completion should begin a smooth return, not snap to the normal view.");
                f.TickCamera(f.ReturnDuration + .01f);
                Require(Near(f.Size, 11f), "The completed scan failed to return to the current base camera Size.");
            }
            using (var current = new Fixture())
            using (var prior = new Fixture())
            {
                const float priorReturnDuration = .55f;
                Set(prior.View, "releaseZoomReturnDuration", priorReturnDuration);
                foreach (Fixture f in new[] { current, prior })
                {
                    f.Charge(1f); f.Release(); f.TickCamera(0f);
                    f.Advance(f.ArrivalDuration + f.ReboundDuration + .01f);
                    f.AdvanceWave(f.Source.LastReleasedWaveTravelDuration);
                    f.TickCamera(0f);
                    Require(f.ObservationPhase == "Returning" && Near(f.Size, f.TargetSize),
                        "The return-speed comparison did not start from the same settled observation view.");
                }
                Require(Near(priorReturnDuration / current.ReturnDuration, .7f),
                    "The authored return duration must reduce camera speed to seventy percent of its prior value.");
                const float normalizedProgress = .4f;
                current.TickCamera(current.ReturnDuration * normalizedProgress);
                prior.TickCamera(priorReturnDuration * normalizedProgress);
                Require(Near(current.Size, prior.Size),
                    "Slowing the return changed its normalized motion rather than stretching its duration.");
                float currentBefore = current.Size;
                float priorBefore = prior.Size;
                const float normalizedStep = .002f;
                current.TickCamera(current.ReturnDuration * normalizedStep);
                prior.TickCamera(priorReturnDuration * normalizedStep);
                float currentSpeed = (currentBefore - current.Size) / (current.ReturnDuration * normalizedStep);
                float priorSpeed = (priorBefore - prior.Size) / (priorReturnDuration * normalizedStep);
                Require(priorSpeed > 0f && Mathf.Abs(currentSpeed / priorSpeed - .7f) < .002f,
                    "The actual camera return is not thirty percent slower at the same normalized point.");
                current.TickCamera(priorReturnDuration - current.ReturnDuration
                    * (normalizedProgress + normalizedStep));
                prior.TickCamera(priorReturnDuration * (1f - normalizedProgress - normalizedStep) + .001f);
                Require(prior.ObservationPhase == "None" && Near(prior.Size, 11f)
                    && current.ObservationPhase == "Returning" && current.Size > 11.1f,
                    "The slower camera must remain in return after the old duration has already completed.");
                current.TickCamera(current.ReturnDuration - priorReturnDuration + .01f);
                Require(current.ObservationPhase == "None" && Near(current.Size, 11f),
                    "The extended return did not reach its exact base size at the new duration.");
            }
        }

        private static void CheckArrivalSnapshot()
        {
            using (var f = new Fixture())
            {
                f.Charge(1f); f.Release(); f.TickCamera(0f);
                float travel = f.Source.LastReleasedWaveTravelDuration;
                float arrival = f.ArrivalDuration;
                float rebound = f.ReboundDuration;
                float amplitude = f.TargetSize * .03f;
                Set(f.Source, "maximumSignalDistance", 8.4f);
                Set(f.Source, "signalSpeed", 7f);
                Set(f.View, "discoveryObservationZoomOutDuration", .7f);
                Set(f.View, "releaseZoomOutDuration", .9f);
                Set(f.View, "discoveryObservationReboundRatio", .04f);
                Set(f.View, "discoveryObservationReboundDuration", .3f);
                f.Advance(arrival * .6f);
                Require(Near(f.Source.LastReleasedWaveTravelDuration, travel)
                    && Near((float)Get(f.View, "discoveryObservationExpansionDuration"), arrival)
                    && f.ObservationPhase == "Expanding",
                    "Changing scan distance, speed, expansion duration or the legacy timer retimed an airborne release.");
                f.Advance(arrival * .4f + .001f);
                Require(f.ObservationPhase == "Rebounding" && Near(f.Size, f.TargetSize)
                    && Near((float)Get(f.View, "discoveryObservationReboundAmplitude"), amplitude)
                    && Near((float)Get(f.View, "discoveryObservationReboundTime"), rebound)
                    && f.Source.HasActiveReleasedScanWaves,
                    "An airborne release lost its captured expansion, rebound amplitude or rebound duration.");
                f.Advance(rebound * .32f - .001f);
                Require(Near(f.Size, f.TargetSize + amplitude),
                    "Changing the next release's rebound settings changed the current pulse peak.");
                f.Advance(travel - arrival - rebound * .32f + .01f); f.TickCamera(f.ReturnDuration + .01f);
                Require(!f.Source.HasActiveReleasedScanWaves && Near(f.Size, 11f),
                    "The original release did not finish at its captured wave lifetime.");
                f.Charge(1f); f.Release(); f.TickCamera(0f);
                Require(Near(f.Source.LastReleasedWaveTravelDuration, 1.2f)
                    && Near((float)Get(f.View, "discoveryObservationExpansionDuration"), f.ArrivalDuration)
                    && Near(f.ArrivalDuration, .7f)
                    && Near((float)Get(f.View, "discoveryObservationReboundAmplitude"), f.TargetSize * .04f)
                    && Near((float)Get(f.View, "discoveryObservationReboundTime"), .3f),
                    "The next release did not use newly authored wave, camera and rebound settings.");
                float nextArrival = f.ArrivalDuration;
                f.Advance(nextArrival * .9f);
                Require(f.ObservationPhase == "Expanding", "The new release completed ahead of its captured duration.");
                f.Advance(nextArrival * .1f + .001f);
                Require(f.ObservationPhase == "Rebounding" && Near(f.Size, f.TargetSize)
                    && f.Source.HasActiveReleasedScanWaves,
                    "The next release did not reach its observation target with its new timing.");
                f.Advance(.3f * .32f - .001f);
                Require(Near(f.Size, f.TargetSize * 1.04f),
                    "The next release did not use its newly authored rebound amplitude and duration.");
            }
        }

        private static void CheckShortWaveArrival()
        {
            foreach (float distance in new[] { .6f, .24f })
            using (var f = new Fixture())
            {
                Set(f.Source, "maximumSignalDistance", distance);
                Set(f.Source, "signalSpeed", 4f);
                f.Charge(1f); f.Release(); f.TickCamera(0f);
                float travel = f.Source.LastReleasedWaveTravelDuration;
                float arrival = f.ArrivalDuration;
                Require(Near(travel, distance / 4f) && Near(arrival, .541f)
                    && Near((float)Get(f.View, "discoveryObservationExpansionDuration"), arrival),
                    "A short wave unexpectedly changed the independent camera transition duration.");
                f.Advance(travel * .8f);
                Require(f.ObservationPhase == "Expanding" && f.Size < f.TargetSize,
                    "The short-wave camera snapped directly to its observation target.");
                f.Advance(travel * .2f + .02f);
                Require(!f.Source.HasActiveReleasedScanWaves && f.ObservationPhase == "Returning"
                    && f.Size < f.TargetSize,
                    "An expired short wave must return immediately instead of forcing the rest of its camera expansion.");
                f.TickCamera(f.ReturnDuration + .01f);
                Require(!f.Source.HasActiveReleasedScanWaves && Near(f.Size, 11f),
                    "A short wave left the observation camera stuck after its particles expired.");
            }
            using (var f = new Fixture())
            {
                Set(f.View, "discoveryObservationZoomOutDuration", 0f);
                f.Charge(1f); f.Release(); f.TickCamera(0f);
                Require(Near((float)Get(f.View, "discoveryObservationExpansionDuration"), .05f),
                    "An invalid camera expansion duration lost its finite minimum transition.");
                f.Advance(.04f);
                Require(f.ObservationPhase == "Expanding" && f.Size < f.TargetSize,
                    "The minimum-duration camera snapped directly to observation.");
                f.Advance(.011f);
                Require(f.ObservationPhase == "Rebounding" && Near(f.Size, f.TargetSize)
                    && f.Source.HasActiveReleasedScanWaves,
                    "The minimum-duration camera did not reach its observation target before rebounding.");
            }
            using (var f = new Fixture())
            {
                Set(f.Source, "maximumSignalDistance", (f.ArrivalDuration + f.ReboundDuration * .6f) * 4f);
                Set(f.Source, "signalSpeed", 4f);
                f.Charge(1f); f.Release(); f.TickCamera(0f);
                f.Advance(f.ArrivalDuration + f.ReboundDuration * .32f);
                Require(f.ObservationPhase == "Rebounding" && f.Size > f.TargetSize + .2f,
                    "The shortened wave did not reach the expected rebound before expiring.");
                float visibleSize = f.Size;
                f.AdvanceWave(f.Source.LastReleasedWaveTravelDuration);
                f.TickCamera(0f);
                Require(f.ObservationPhase == "Returning" && Near(f.Size, visibleSize),
                    "A wave ending during rebound must return from its current visible size without a snap.");
                f.TickCamera(f.ReturnDuration + .01f);
                Require(Near(f.Size, 11f), "An interrupted rebound left the camera stuck after its wave expired.");
            }
        }

        private static void CheckRechargeAndCancel()
        {
            using (var f = new Fixture())
            {
                f.Charge(1f);
                f.Release();
                f.Advance(f.ArrivalDuration + f.ReboundDuration * .32f);
                float reboundPeak = f.Size;
                float elapsed = (float)Get(f.View, "discoveryObservationElapsed");
                f.Charge(0f);
                Require(Near(f.Size, reboundPeak) && f.ObservationPhase == "Rebounding",
                    "Recharging snapped or restarted an active arrival rebound.");
                f.Charge(.7f);
                Require(Near(f.Size, reboundPeak)
                    && Near((float)Get(f.View, "discoveryObservationElapsed"), elapsed),
                    "A follow-up charge restarted the elapsed observation motion.");
                Call(f.View, "CancelCharge");
                f.Advance(.03f);
                Require(f.Source.HasActiveReleasedScanWaves && f.ObservationPhase == "Rebounding"
                    && f.Size < reboundPeak && f.Size > f.TargetSize
                    && (float)Get(f.View, "discoveryObservationElapsed") > elapsed,
                    "Cancelling the new charge discarded or restarted the prior scan's settling rebound.");
                f.Charge(.4f);
                f.Advance(f.Source.LastReleasedWaveTravelDuration);
                Require(!f.Source.HasActiveReleasedScanWaves && Near(f.Size, f.TargetSize),
                    "An unfinished follow-up charge pumped the camera after its older wave expired.");
                Call(f.View, "CancelCharge");
                f.TickCamera(0f);
                Require(Near(f.Size, f.TargetSize), "Cancelling the final charge snapped the held observation view.");
                f.TickCamera(f.ReturnDuration + .01f);
                Require(Near(f.Size, 11f), "Cancelled recharging left observation stuck after all waves drained.");
            }
        }

        private static void CheckRepeatedRelease()
        {
            using (var f = new Fixture())
            {
                f.Charge(1f);
                f.Release();
                float travel = f.Source.LastReleasedWaveTravelDuration;
                f.Advance(f.ArrivalDuration + f.ReboundDuration * .25f);
                int oldCount = f.Emitted.particleCount;
                float previousSize = f.Size;
                float previousElapsed = (float)Get(f.View, "discoveryObservationElapsed");
                Require(f.ObservationPhase == "Rebounding", "Repeat-release fixture missed the active rebound.");
                f.Charge(1f);
                f.Release();
                f.TickCamera(0f);
                Require(f.Emitted.particleCount > oldCount && Near(f.Size, previousSize)
                    && f.ObservationPhase == "Rebounding"
                    && Near((float)Get(f.View, "discoveryObservationElapsed"), previousElapsed),
                    "A repeated release cleared older particles, snapped the view or restarted its arrival rebound.");
                f.Advance(travel * .8f);
                Require(f.Source.HasActiveReleasedScanWaves && Near(f.Size, f.TargetSize),
                    "Finishing the first wave ended observation while the second wave remained.");
                f.Advance(travel * .2f + .02f);
                f.TickCamera(f.ReturnDuration + .01f);
                Require(!f.Source.HasActiveReleasedScanWaves && Near(f.Size, 11f),
                    "The camera did not recover after the final overlapping wave expired.");
            }
        }

        private static void CheckPause()
        {
            using (var f = new Fixture())
            {
                f.Charge(1f);
                f.Release();
                f.Advance(f.ArrivalDuration + f.ReboundDuration * .32f);
                Require(f.ObservationPhase == "Rebounding" && f.Size > f.TargetSize,
                    "Pause fixture did not begin inside its arrival rebound.");
                float radius = (float)Get(f.Source, "scanWaveRadius");
                int count = f.Emitted.particleCount;
                Time.timeScale = 0f;
                for (int frame = 0; frame < 20; frame++)
                {
                    f.AdvanceWave(0f);
                    f.TickCamera(.1f);
                }
                Require(f.Emitted.particleCount == count && Near((float)Get(f.Source, "scanWaveRadius"), radius)
                    && f.Source.HasActiveReleasedScanWaves && Near(f.Size, f.TargetSize),
                    "Paused world time drained the wave or prematurely ended observation.");
                Time.timeScale = 1f;
                f.Advance(f.Source.LastReleasedWaveTravelDuration);
                f.TickCamera(f.ReturnDuration + .01f);
                Require(Near(f.Size, 11f), "Resuming did not finish the pending observation lifecycle.");
            }
        }

        private static void CheckDisabledCleanup()
        {
            using (var f = new Fixture())
            {
                f.Charge(1f);
                f.Release();
                f.Advance(f.ArrivalDuration + f.ReboundDuration * .32f);
                Require(f.ObservationPhase == "Rebounding", "Source-disable fixture missed its active rebound.");
                f.Source.enabled = false;
                Call(f.Source, "OnDisable");
                Require(!f.Source.HasActiveReleasedScanWaves && f.Emitted.particleCount == 0,
                    "Disabling the scan source retained its released wave.");
                f.TickCamera(0f);
                f.TickCamera(f.ReturnDuration + .01f);
                Require(Near(f.Size, 11f), "A disabled source left the observation camera stuck wide.");
                f.Source.enabled = true;
                f.Charge(1f);
                f.Release();
                f.Advance(f.ArrivalDuration + f.ReboundDuration * .32f);
                Require(f.ObservationPhase == "Rebounding", "View-disable fixture missed its active rebound.");
                Call(f.View, "OnDisable");
                f.Apply();
                Require(Near(f.Size, 11f), "Disabling the view retained a scan camera multiplier.");
            }
        }

        private static void CheckComposition()
        {
            using (var f = new Fixture(18f))
            {
                f.Charge(1f);
                f.Release();
                f.Advance(f.ArrivalDuration + f.ReboundDuration + .01f);
                Require(Near(f.Size, 18f), "An existing larger base view was narrowed to the observation target.");
            }
            using (var f = new Fixture())
            {
                f.Shake.SetPhotoFocusMagnification(2f);
                Set(f.Shake, "springZoom", .02f);
                Set(f.Shake, "globalIntensity", 1f);
                f.Charge(1f);
                f.Release();
                f.Advance(f.ArrivalDuration + f.ReboundDuration * .32f);
                Require(Near(f.Size, f.TargetSize * 1.03f / 2f * 1.02f)
                    && Near(f.Shake.BaseOrthographicSize, 11f),
                    "The arrival rebound overwrote photo focus, impact zoom or the immutable camera base.");
                f.Advance(f.ReboundDuration * .68f + .01f);
                Require(Near(f.Size, f.TargetSize / 2f * 1.02f)
                    && Near(f.Shake.BaseOrthographicSize, 11f),
                    "Observation overwrote photo focus, impact zoom or the camera's immutable base size.");
            }
        }

        private static void Check(string name, Action action)
        {
            try { action(); passedChecks++; }
            catch (Exception error) { throw new InvalidOperationException(name + ": " + error.Message, error); }
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= .001f;
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static object Call(object target, string name, params object[] args)
        {
            try
            {
                return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(target, args);
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { throw error.InnerException; }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            private readonly List<DiscoverableEntity> plants = new List<DiscoverableEntity>();
            public readonly Camera Camera;
            public readonly RobotCameraShake Shake;
            public readonly ScanChargeUI View;
            public readonly BioScanController Source;
            public ParticleSystem Forming => (ParticleSystem)Get(Source, "formingPointSystem");
            public ParticleSystem Emitted => (ParticleSystem)Get(Source, "emittedPointSystem");
            public float Size => Camera.orthographicSize;
            public string ObservationPhase => Get(View, "discoveryObservationPhase").ToString();
            public float TargetSize => (float)Get(View, "discoveryObservationOrthographicSize");
            public float ReturnDuration => (float)Get(View, "releaseZoomReturnDuration");
            public float ArrivalDuration => Mathf.Max(.05f, (float)Get(View, "discoveryObservationZoomOutDuration"));
            public float ReboundDuration => Mathf.Max(.01f, (float)Get(View, "discoveryObservationReboundDuration"));

            public Fixture(float baseSize = 11f, bool useAuthoredRebound = false)
            {
                scene = EditorSceneManager.NewPreviewScene();
                Camera = Create("Discovery observation validation camera").AddComponent<Camera>();
                Camera.orthographic = true;
                Camera.orthographicSize = baseSize;
                Camera.transform.position = new Vector3(10000f, 10000f, -10f);
                Shake = Camera.gameObject.AddComponent<RobotCameraShake>();
                Set(Shake, "enableGamepadRumble", false);
                Call(Shake, "Awake");
                var viewObject = new GameObject("Discovery observation validation view", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(viewObject, scene);
                View = viewObject.AddComponent<ScanChargeUI>();
                ScanChargeUI viewProfile = LoadViewProfile();
                foreach (string field in new[] { "scanZoomReferenceOrthographicSize", "holdTargetOrthographicSize",
                    "releasePeakOrthographicSize", "maximumChargeDuration", "releaseZoomOutDuration",
                    "releaseZoomReturnDuration", "cancelledChargeZoomReturnDuration", "discoveryObservationOrthographicSize",
                    "discoveryObservationZoomOutDuration", "discoveryObservationReboundRatio",
                    "discoveryObservationReboundDuration" })
                    Set(View, field, Get(viewProfile, field));
                // Exercise the optional pulse independently of the user's current
                // authored zero-amplitude setting, which is covered by its guard.
                if (!useAuthoredRebound)
                {
                    Set(View, "discoveryObservationReboundRatio", .03f);
                    Set(View, "discoveryObservationReboundDuration", .32f);
                }
                Set(View, "showScanRing", false);
                Set(View, "mapCamera", Camera);
                Set(View, "cameraZoomInitialized", false);
                Call(View, "ResolveTrackingReferences");

                Source = Create("Discovery observation validation source").AddComponent<BioScanController>();
                Source.transform.position = new Vector3(10000f, 10000f, 0f);
                BioScanController sourceProfile = LoadRobotProfile();
                foreach (string field in new[] { "biologicalSignalClipShader", "signalPointCount", "maximumPointCount",
                    "automaticAngularCoverage", "pointCoverageOverlap", "formedRingRadius", "signalSpeed",
                    "maximumSignalDistance", "signalFadeOutDuration", "signalPointDiameter", "signalHeadCoreRatio", "scanWaveHalfThickness" })
                    Set(Source, field, Get(sourceProfile, field));
                Call(Source, "CreateSignalResources");
                Transform particleRoot = (Transform)Get(Source, "signalParticleRoot");
                SceneManager.MoveGameObjectToScene(particleRoot.gameObject, scene);
                View.BindDiscoveryScanController(Source);
                View.FullyChargedBiologicalScanReleased += Launch;
                TickCamera(0f);
            }

            private GameObject Create(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                return go;
            }

            public DiscoverableEntity Plant(float distance)
            {
                GameObject go = Create("Discovery scan boundary plant");
                go.transform.position = Source.transform.position + Vector3.right * distance;
                var entity = go.AddComponent<DiscoverableEntity>();
                entity.ConfigureEditorDefaults(DiscoverableKind.Plant, "observation-regression", false);
                Set(entity, "biologicalScanCollisionRadiusMeters", .01f);
                Call(entity, "OnEnable");
                plants.Add(entity);
                return entity;
            }

            public void Charge(float progress)
            {
                if (!View.IsCharging)
                    Call(View, "BeginCharge");
                Set(View, "chargeElapsed", (float)Get(View, "maximumChargeDuration") * progress);
                TickCamera(0f);
            }

            public void Release() => Call(View, "BeginRelease");
            public void Launch() => Call(Source, "LaunchFormedSignals");

            public void AdvanceWave(float seconds)
            {
                Call(Source, "TickScanWave", seconds);
                Emitted.Simulate(seconds, true, false, false);
            }

            public void Advance(float seconds)
            {
                // Preserve real update ordering instead of handing a whole wave lifetime to one camera frame.
                int frames = Mathf.Max(1, Mathf.CeilToInt(seconds / .01f));
                float step = seconds / frames;
                for (int frame = 0; frame < frames; frame++)
                {
                    AdvanceWave(step);
                    TickCamera(step);
                }
            }

            public void TickCamera(float seconds)
            {
                Call(View, "UpdateScanCameraZoom", seconds);
                Apply();
            }

            public void Apply()
            {
                Call(View, "ApplyScanCameraZoom");
                Call(Shake, "ApplyShakeToCamera");
            }

            public void Dispose()
            {
                View.FullyChargedBiologicalScanReleased -= Launch;
                foreach (DiscoverableEntity plant in plants)
                    if (plant != null) Call(plant, "OnDisable");
                Call(Source, "OnDisable");
                Transform particleRoot = (Transform)Get(Source, "signalParticleRoot");
                Material material = (Material)Get(Source, "signalPointMaterial");
                Set(Source, "signalParticleRoot", null);
                Set(Source, "signalPointMaterial", null);
                if (particleRoot != null) Object.DestroyImmediate(particleRoot.gameObject);
                if (material != null) Object.DestroyImmediate(material);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}

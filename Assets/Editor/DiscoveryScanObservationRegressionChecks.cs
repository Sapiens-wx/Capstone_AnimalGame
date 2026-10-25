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
                Check("Authored radius grows twenty percent without losing angular density", CheckAuthoredRange);
                Check("Formation is excluded while outgoing particles remain authoritative", CheckWaveOwnership);
                Check("Expanded fringe is detected and launch tuning is snapshotted", CheckDetectionRange);
                Check("Released particles retain their world-space anchors", CheckWorldSpace);
                Check("Discovery pullback builds speed promptly, sustains its drive and brakes continuously", CheckPullbackShape);
                Check("Observation arrives 0.5 seconds before the real wave and holds until it ends", CheckObservationLifetime);
                Check("Camera arrival uses the current release snapshot and the next release uses new tuning", CheckArrivalSnapshot);
                Check("Very short waves retain a finite smooth camera transition", CheckShortWaveArrival);
                Check("Recharging and cancellation preserve the current wide observation", CheckRechargeAndCancel);
                Check("Repeated releases wait for the last outgoing particle", CheckRepeatedRelease);
                Check("Pause preserves the released wave and observation", CheckPause);
                Check("Disabling the source or the view cleans up observation", CheckDisabledCleanup);
                Check("Observation respects larger camera bases and independent camera effects", CheckComposition);
                Debug.Log("Discovery scan observation checks PASS: " + passedChecks
                    + " checks; authored coverage, real particles, fringe detection, launch snapshots, world anchors,"
                    + " pullback velocity, camera arrival lead, arrival snapshots, short waves, observation lifetime, repeated release,"
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
            Require(Near(profile.MaximumScanRadiusWorld, 12.2f * 1.2f),
                "Effective radius, including the 0.2-unit ready ring, must be 14.64.");
            Require(Near((float)Get(profile, "signalSpeed"), 8.7f), "Scan speed changed unexpectedly.");
            Require((int)Get(profile, "maximumPointCount") == 360,
                "The enlarged circumference requires the agreed 360-point density cap.");
            int points = (int)Call(profile, "CalculateEffectivePointCount");
            Require(points / profile.MaximumScanRadiusWorld >= 299f / 12.2f - .001f,
                "The expanded wave reduced the original angular signal density.");
            ScanChargeUI view = LoadViewProfile();
            Require(Near((float)Get(view, "discoveryObservationOrthographicSize"), 15.5f)
                && Near((float)Get(view, "releaseZoomOutDuration"), .45f)
                && Near((float)Get(view, "discoveryCameraArrivalLeadTime"), .5f)
                && Near((float)Get(view, "releaseZoomReturnDuration"), .55f),
                "The authored observation size or transition timing does not match the agreed profile.");
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
                DiscoverableEntity expandedFringe = f.Plant(14.6f);
                DiscoverableEntity outside = f.Plant(14.95f);
                f.Launch();
                // Inspector/runtime changes affect the next release, not an airborne wave.
                Set(f.Source, "maximumSignalDistance", 50f);
                Set(f.Source, "signalSpeed", .1f);
                f.AdvanceWave(1.4f);
                Require(!expandedFringe.IsTemporarilyRevealed && f.Source.HasActiveScanWave,
                    "The old scan duration or newly changed speed was applied to an airborne wave.");
                f.AdvanceWave(.3f);
                Require(expandedFringe.IsTemporarilyRevealed && !outside.IsDiscovered
                    && !f.Source.HasActiveScanWave && !f.Source.HasActiveReleasedScanWaves,
                    "Detection must reach the added fringe, stop at the launch radius and ignore out-of-range targets.");
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
                f.Source.transform.position += new Vector3(4f, -3f, 0f);
                f.Emitted.GetParticles(particles);
                Require(Vector3.Distance(particles[0].position, original) < .001f,
                    "Moving the robot moved an already released wave.");
                f.AdvanceWave(.1f);
                f.Emitted.GetParticles(particles);
                Require(Mathf.Abs(Vector3.Distance(particles[0].position, original) - .87f) < .005f,
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
            const float earlyProgress = .08f / 1.16f;
            Require(sample(earlyProgress) > Mathf.SmoothStep(0f, 1f, earlyProgress) * 2f,
                "The short starting push remains as soft as the old full-duration SmoothStep.");

            const float delta = .001f;
            Func<float, float> velocity = progress => (sample(progress + delta)
                - sample(progress - delta)) / (2f * delta);
            float cruiseSpeed = velocity(.5f);
            Require(cruiseSpeed > 1f, "The main push never establishes a sustained higher speed.");
            foreach (float progress in new[] { .2f, .4f, .6f, .8f })
                Require(Mathf.Abs(velocity(progress) - cruiseSpeed) < cruiseSpeed * .02f,
                    "The central camera push eases continuously instead of holding a clear driving speed.");
            foreach (float join in new[] { .08f / 1.16f, .98f / 1.16f })
            {
                float left = (sample(join) - sample(join - delta)) / delta;
                float right = (sample(join + delta) - sample(join)) / delta;
                Require(Mathf.Abs(left - right) < cruiseSpeed * .03f,
                    "The camera changes speed abruptly between acceleration, drive and braking.");
            }
            Require((sample(delta) - sample(0f)) / delta < cruiseSpeed * .02f
                && (sample(1f) - sample(1f - delta)) / delta < cruiseSpeed * .02f
                && velocity(.99f) < velocity(.9f) * .2f,
                "The camera starts or stops with residual velocity instead of a short controlled brake.");
        }

        private static void CheckObservationLifetime()
        {
            using (var f = new Fixture())
            {
                f.Charge(1f);
                float chargedSize = f.Size;
                f.Release();
                f.TickCamera(0f);
                Require(Near(f.Size, chargedSize), "Release snapped instead of starting from the charged view.");
                float travelDuration = f.Source.LastReleasedWaveTravelDuration;
                float arrival = travelDuration - .5f;
                Require(Near(travelDuration, 14.44f / 8.7f)
                    && Near((float)Get(f.View, "discoveryObservationExpansionDuration"), arrival),
                    "The real released particle lifetime did not schedule camera arrival 0.5 seconds earlier.");
                f.Advance(.1f);
                float earlyDisplacement = Mathf.InverseLerp(chargedSize, 15.5f, f.Size);
                Require(earlyDisplacement > Mathf.SmoothStep(0f, 1f, .1f / arrival) * 2f
                    && earlyDisplacement < .15f,
                    "The real released camera did not use the stronger starting push, or jumped on release.");
                f.Advance(.35f);
                Require(f.ObservationPhase == "Expanding" && f.Size < 15.45f,
                    "The old 0.45-second timer still completes the camera pullback too early.");
                f.Advance(arrival - .45f - .05f);
                Require(f.ObservationPhase == "Expanding" && f.Size < 15.499f,
                    "The camera reached its final size before the scheduled arrival boundary.");
                f.Advance(.051f);
                Require(f.Source.HasActiveReleasedScanWaves && Near(f.Size, 15.5f),
                    "The camera failed to reach observation at the agreed arrival time.");
                float waveRemaining = ((float)Get(f.Source, "scanWaveMaximumRadius")
                    - (float)Get(f.Source, "scanWaveRadius")) / (float)Get(f.Source, "scanWaveSpeed");
                Require(f.ObservationPhase == "Holding" && Mathf.Abs(waveRemaining - .5f) < .003f,
                    "Camera completion does not lead the actual moving wave by 0.5 seconds.");
                f.Advance(.45f);
                Require(f.Source.HasActiveReleasedScanWaves && Near(f.Size, 15.5f),
                    "Observation did not hold while the final part of the wave was travelling.");
                f.Advance(.06f);
                Require(!f.Source.HasActiveReleasedScanWaves && f.Size > 11f,
                    "Wave completion should begin a smooth return, not snap to the normal view.");
                f.TickCamera(.56f);
                Require(Near(f.Size, 11f), "The completed scan failed to return to the current base camera Size.");
            }
        }

        private static void CheckArrivalSnapshot()
        {
            using (var f = new Fixture())
            {
                f.Charge(1f); f.Release(); f.TickCamera(0f);
                float travel = f.Source.LastReleasedWaveTravelDuration;
                float arrival = travel - .5f;
                Set(f.Source, "maximumSignalDistance", 8.4f);
                Set(f.Source, "signalSpeed", 7f);
                Set(f.View, "discoveryCameraArrivalLeadTime", .3f);
                Set(f.View, "releaseZoomOutDuration", .9f);
                f.Advance(.6f);
                Require(Near(f.Source.LastReleasedWaveTravelDuration, travel)
                    && Near((float)Get(f.View, "discoveryObservationExpansionDuration"), arrival)
                    && f.ObservationPhase == "Expanding",
                    "Changing scan distance, speed, lead or the legacy timer retimed an airborne release.");
                f.Advance(arrival - .6f + .001f);
                Require(f.ObservationPhase == "Holding" && Near(f.Size, 15.5f)
                    && f.Source.HasActiveReleasedScanWaves,
                    "An airborne release failed to finish camera expansion at its captured duration.");
                f.Advance(.51f); f.TickCamera(.56f);
                Require(!f.Source.HasActiveReleasedScanWaves && Near(f.Size, 11f),
                    "The original release did not finish at its captured wave lifetime.");
                f.Charge(1f); f.Release(); f.TickCamera(0f);
                Require(Near(f.Source.LastReleasedWaveTravelDuration, 1.2f)
                    && Near((float)Get(f.View, "discoveryObservationExpansionDuration"), .9f),
                    "The next release did not use the newly authored distance, speed and arrival lead.");
                f.Advance(.85f);
                Require(f.ObservationPhase == "Expanding", "The new release completed ahead of its captured duration.");
                f.Advance(.051f);
                Require(f.ObservationPhase == "Holding" && Near(f.Size, 15.5f)
                    && f.Source.HasActiveReleasedScanWaves,
                    "The next release did not reach its observation target with its new timing.");
            }
        }

        private static void CheckShortWaveArrival()
        {
            using (var f = new Fixture())
            {
                Set(f.Source, "maximumSignalDistance", .6f);
                Set(f.Source, "signalSpeed", 4f);
                f.Charge(1f); f.Release(); f.TickCamera(0f);
                Require(Near(f.Source.LastReleasedWaveTravelDuration, .15f)
                    && Near((float)Get(f.View, "discoveryObservationExpansionDuration"), .05f),
                    "An arrival lead longer than the wave lifetime did not keep a positive transition duration.");
                f.Advance(.04f);
                Require(f.ObservationPhase == "Expanding" && f.Size < 15.5f,
                    "The short-wave camera snapped directly to its observation target.");
                f.Advance(.011f);
                Require(f.ObservationPhase == "Holding" && Near(f.Size, 15.5f)
                    && f.Source.HasActiveReleasedScanWaves,
                    "The short-wave minimum transition failed to reach a finite observation target.");
                f.Advance(.11f); f.TickCamera(.56f);
                Require(!f.Source.HasActiveReleasedScanWaves && Near(f.Size, 11f),
                    "A short wave left the observation camera stuck after its particles expired.");
            }
        }

        private static void CheckRechargeAndCancel()
        {
            using (var f = new Fixture())
            {
                f.Charge(1f);
                f.Release();
                f.Advance(f.ArrivalDuration + .01f);
                f.Charge(0f);
                Require(Near(f.Size, 15.5f), "Recharging snapped an active observation view.");
                f.Charge(.7f);
                Require(Near(f.Size, 15.5f), "An old outgoing wave must retain the wide view during a new charge.");
                Call(f.View, "CancelCharge");
                f.Advance(.03f);
                Require(f.Source.HasActiveReleasedScanWaves && Near(f.Size, 15.5f),
                    "Cancelling the new charge discarded the prior scan's observation.");
                f.Charge(.4f);
                f.Advance(.6f);
                Require(!f.Source.HasActiveReleasedScanWaves && Near(f.Size, 15.5f),
                    "An unfinished follow-up charge pumped the camera after its older wave expired.");
                Call(f.View, "CancelCharge");
                f.TickCamera(0f);
                Require(Near(f.Size, 15.5f), "Cancelling the final charge snapped the held observation view.");
                f.TickCamera(.56f);
                Require(Near(f.Size, 11f), "Cancelled recharging left observation stuck after all waves drained.");
            }
        }

        private static void CheckRepeatedRelease()
        {
            using (var f = new Fixture())
            {
                f.Charge(1f);
                f.Release();
                f.Advance(1.2f);
                int oldCount = f.Emitted.particleCount;
                float previousSize = f.Size;
                f.Charge(1f);
                f.Release();
                f.TickCamera(0f);
                Require(f.Emitted.particleCount > oldCount && Near(f.Size, previousSize),
                    "A repeated release cleared older particles or snapped the current observation size.");
                f.Advance(.6f);
                Require(f.Source.HasActiveReleasedScanWaves && Near(f.Size, 15.5f),
                    "Finishing the first wave ended observation while the second wave remained.");
                f.Advance(1.1f);
                f.TickCamera(.56f);
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
                f.Advance(.6f);
                float radius = (float)Get(f.Source, "scanWaveRadius");
                int count = f.Emitted.particleCount;
                Time.timeScale = 0f;
                for (int frame = 0; frame < 20; frame++)
                {
                    f.AdvanceWave(0f);
                    f.TickCamera(.1f);
                }
                Require(f.Emitted.particleCount == count && Near((float)Get(f.Source, "scanWaveRadius"), radius)
                    && f.Source.HasActiveReleasedScanWaves && Near(f.Size, 15.5f),
                    "Paused world time drained the wave or prematurely ended observation.");
                Time.timeScale = 1f;
                f.Advance(1.2f);
                f.TickCamera(.56f);
                Require(Near(f.Size, 11f), "Resuming did not finish the pending observation lifecycle.");
            }
        }

        private static void CheckDisabledCleanup()
        {
            using (var f = new Fixture())
            {
                f.Charge(1f);
                f.Release();
                f.Advance(.5f);
                f.Source.enabled = false;
                Call(f.Source, "OnDisable");
                Require(!f.Source.HasActiveReleasedScanWaves && f.Emitted.particleCount == 0,
                    "Disabling the scan source retained its released wave.");
                f.TickCamera(.7f);
                f.TickCamera(.7f);
                Require(Near(f.Size, 11f), "A disabled source left the observation camera stuck wide.");
                f.Source.enabled = true;
                f.Charge(1f);
                f.Release();
                f.Advance(.5f);
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
                f.Advance(f.ArrivalDuration + .01f);
                Require(Near(f.Size, 18f), "An existing larger base view was narrowed to the observation target.");
            }
            using (var f = new Fixture())
            {
                f.Shake.SetPhotoFocusMagnification(2f);
                Set(f.Shake, "springZoom", .02f);
                Set(f.Shake, "globalIntensity", 1f);
                f.Charge(1f);
                f.Release();
                f.Advance(f.ArrivalDuration + .01f);
                Require(Near(f.Size, 15.5f / 2f * 1.02f)
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
            public float ArrivalDuration => Mathf.Max(.05f, Source.LastReleasedWaveTravelDuration
                - (float)Get(View, "discoveryCameraArrivalLeadTime"));

            public Fixture(float baseSize = 11f)
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
                    "discoveryCameraArrivalLeadTime" })
                    Set(View, field, Get(viewProfile, field));
                Set(View, "showScanRing", false);
                Set(View, "mapCamera", Camera);
                Set(View, "cameraZoomInitialized", false);
                Call(View, "ResolveTrackingReferences");

                Source = Create("Discovery observation validation source").AddComponent<BioScanController>();
                Source.transform.position = new Vector3(10000f, 10000f, 0f);
                BioScanController sourceProfile = LoadRobotProfile();
                foreach (string field in new[] { "biologicalSignalClipShader", "signalPointCount", "maximumPointCount",
                    "automaticAngularCoverage", "pointCoverageOverlap", "formedRingRadius", "signalSpeed",
                    "maximumSignalDistance", "signalFadeOutDuration", "signalPointDiameter", "scanWaveHalfThickness" })
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

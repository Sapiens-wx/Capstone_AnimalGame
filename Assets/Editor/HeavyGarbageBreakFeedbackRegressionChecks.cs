using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AnimalGame.Garbage;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    // Exercise the real break transaction and inspect pure camera/motor outputs.
    // These checks never send motor commands or modify the user's open scene.
    public static class HeavyGarbageBreakFeedbackRegressionChecks
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly List<string> passed = new();

        [MenuItem("Animal Game/Validation/Run Heavy Garbage Break Feedback Checks")]
        public static void Run()
        {
            passed.Clear();
            Check("Production big-garbage breaks trigger once on the success frame at 30/60/120 FPS", CheckProductionBreak);
            Check("Releasing A and destroying the source retain the independent break tail without another grab", CheckSourceLifetime);
            Check("Blocked pulls and cancelled grabs do not create a break impact", CheckBlockedAndCancelled);
            Check("Missing fragment previews and invalid completion layouts do not trigger feedback", CheckFailedCompletion);
            Check("Break camera impulses follow the pull direction and reverse with it", CheckDirectionalCamera);
            Check("The sharp high-frequency impact expires before the low-frequency recoil", CheckEnvelope);
            Check("Overlapping legitimate break pulses take the strongest source per motor", CheckOverlappingPulses);
            Check("Regular, grab and break channels compose by maximum after independent Sony calibration", CheckMotorComposition);
            Check("Camera and haptic switches stay independent; break camera motion does not create ordinary rumble", CheckIndependentChannels);
            Check("Global intensity scales the screen and hand channels, including complete silence", CheckGlobalIntensity);
            Check("Focus loss, application pause and component disable discard feedback without replay", CheckLifecycle);
            Check("Time-scale pause and runtime channel disabling discard pending haptics", CheckPausedAndDisabledChannels);
            Debug.Log("Heavy garbage break feedback checks PASS: " + passed.Count + " groups\n" + string.Join("\n", passed));
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Check(string name, Action check) { check(); passed.Add(name); }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < .0002f;
        private static void RequireNear(Vector2 actual, Vector2 expected, string message)
        { Require(Vector2.Distance(actual, expected) < .0003f, message + ": " + actual + " versus " + expected); }

        private static void CheckProductionBreak()
        {
            foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
            using (var f = new Fixture())
            {
                WorldInteraction source = f.PrepareProductionSource(out HeavyGarbagePull pull, out _);
                Vector2 away = ((Vector2)f.Root.transform.position - (Vector2)source.WorldPosition).normalized;
                Require(BreakCount(f.Shake) == 0 && GrabCount(f.Shake) == 1,
                    "The initial clamp did not remain separate from the break impact");
                Call(f.Shake, "ClearGarbageGrabFeedback"); Call(f.Shake, "ResetShakeState");
                float duration = (float)Get(pull, "requiredPullDuration");
                int limit = Mathf.CeilToInt((duration + .2f) / dt);
                bool broke = false;
                for (int frame = 0; frame < limit && source != null; frame++)
                {
                    Require(BreakCount(f.Shake) == 0, "Intact garbage produced a break impact before success");
                    f.PullStep(pull, dt, away);
                    if (source == null || !source.gameObject.activeSelf)
                    {
                        broke = true;
                        Require(BreakCount(f.Shake) == 1, "Successful split did not emit one impact in its completion frame at dt " + dt);
                        Vector2 cameraImpulse = (Vector2)Get(f.Shake, "heavyGarbageBreakCameraPositionVelocity");
                        Require(cameraImpulse.sqrMagnitude > 0f && Vector2.Angle(cameraImpulse, away) < .05f,
                            "Break camera recoil did not follow the release direction");
                        Vector2 released = (Vector2)Get(f.Mover, "heavyReleaseVelocity");
                        Require(released.sqrMagnitude > 0f && Vector2.Angle(released, away) < .05f,
                            "The feedback transaction altered or lost the existing release inertia");
                    }
                }
                Require(broke && f.Arms.HeldObject != null && f.Arms.HeldObject.Size == RecyclableSize.Medium,
                    "Production pull did not finish and hand off its medium fragment at dt " + dt);
                Require(GrabCount(f.Shake) == 0, "A successful fragment handoff fabricated an additional manual grab pulse");
                RequireNear(Motors(f.Shake, PeakTime(f.Shake)), new Vector2(.8f, .55f), "Production break used the wrong motor strengths");
                f.TickArms(.05f, true);
                Require(BreakCount(f.Shake) == 1 && GrabCount(f.Shake) == 0, "Holding A after the split repeated the feedback");
            }
        }

        private static void CheckSourceLifetime()
        {
            using (var f = new Fixture())
            {
                WorldInteraction source = f.PrepareProductionSource(out HeavyGarbagePull pull, out _);
                Vector2 away = ((Vector2)f.Root.transform.position - (Vector2)source.WorldPosition).normalized;
                Call(f.Shake, "ClearGarbageGrabFeedback"); Call(f.Shake, "ResetShakeState");
                f.Complete(pull, away);
                Require(source == null || !source.gameObject.activeSelf, "Lifetime check did not destroy/deactivate the large source");
                float start = Time.time;
                f.TickArms(.01f, false);
                Require(f.Arms.HeldObject == null && BreakCount(f.Shake) == 1 && GrabCount(f.Shake) == 0,
                    "Releasing A after success cleared or retriggered the independent break pulse");
                Require(Motors(f.Shake, start + .2f).x > 0f && Near(Motors(f.Shake, start + .2f).y, 0f),
                    "The low-frequency recoil did not survive the vanished source and released controls");
                Call(f.Shake, "IntegrateSprings", .08f); Call(f.Shake, "ApplyShakeToCamera");
                Require(f.Shake.CurrentLocalPositionOffset.sqrMagnitude > 0f,
                    "The independent screen recoil disappeared with the source or released controls");
                for (int i = 0; i < 180; i++) Call(f.Shake, "IntegrateSprings", 1f / 60f);
                Call(f.Shake, "ApplyShakeToCamera");
                Require(f.Shake.CurrentLocalPositionOffset.magnitude < .0005f
                    && Mathf.Abs(f.Shake.CurrentRotationOffsetDegrees) < .005f,
                    "The screen recoil failed to settle naturally");
                RequireNear(Motors(f.Shake, start + .36f), Vector2.zero, "The recoil motors outlived the configured pulse");
            }
        }

        private static void CheckBlockedAndCancelled()
        {
            using (var f = new Fixture())
            {
                WorldInteraction source = f.PrepareProductionSource(out HeavyGarbagePull pull, out _);
                Vector2 away = ((Vector2)f.Root.transform.position - (Vector2)source.WorldPosition).normalized;
                f.PullStep(pull, .1f, away);
                float before = pull.Progress;
                SetProperty(f.Mover, "HeavyPullMovementBlocked", true);
                Call(pull, "Step", 4f);
                Require(Near(pull.Progress, before) && source.gameObject.activeSelf && BreakCount(f.Shake) == 0,
                    "A blocked pull completed or produced a break impact");
                SetProperty(f.Mover, "HeavyPullMovementBlocked", false);
                f.TickArms(.01f, false); Call(pull, "Step", .3f);
                Require(pull.Progress == 0f && source.gameObject.activeSelf && BreakCount(f.Shake) == 0
                    && (Vector2)Get(f.Shake, "heavyGarbageBreakCameraPositionVelocity") == Vector2.zero,
                    "Releasing A without completion produced break feedback");
            }
            using (var f = new Fixture())
            {
                WorldInteraction source = f.PrepareProductionSource(out HeavyGarbagePull pull, out _);
                Vector2 away = ((Vector2)f.Root.transform.position - (Vector2)source.WorldPosition).normalized;
                f.PullStep(pull, .1f, away);
                SetProperty(f.Mover, "CurrentThrottleIntent", 0f);
                SetProperty(f.Mover, "UnresistedMovementIntentWorld", Vector2.zero);
                Call(pull, "Step", .3f);
                Require(pull.Progress == 0f && source.gameObject.activeSelf && BreakCount(f.Shake) == 0,
                    "Stopping backward movement triggered a break impact");
            }
        }

        private static void CheckFailedCompletion()
        {
            using (var f = new Fixture())
            {
                WorldInteraction source = f.PrepareProductionSource(out HeavyGarbagePull pull, out GarbageFragmentSpawner spawner);
                Set(spawner, "heldFragments", Array.Empty<GameObject>());
                Vector2 away = ((Vector2)f.Root.transform.position - (Vector2)source.WorldPosition).normalized;
                f.PullStep(pull, 4f, away);
                Require(source.gameObject.activeSelf && spawner.PullPreviewCount == 0 && BreakCount(f.Shake) == 0,
                    "A pull without fragment previews emitted a successful-break impact");
            }
            using (var f = new Fixture())
            {
                WorldInteraction source = f.PrepareProductionSource(out HeavyGarbagePull pull, out GarbageFragmentSpawner spawner);
                Vector2 away = ((Vector2)f.Root.transform.position - (Vector2)source.WorldPosition).normalized;
                float duration = (float)Get(pull, "requiredPullDuration");
                f.PullStep(pull, duration - .05f, away);
                Require(spawner.PullPreviewRoot != null && spawner.PullPreviewCount >= 3 && BreakCount(f.Shake) == 0,
                    "Completion-failure fixture did not prepare a real preview near completion");
                // A newly arrived obstacle invalidates a scattered piece at the exact
                // saved preview position, after a valid plan has already been prepared.
                Vector2 fragmentPosition = spawner.PullPreviewRoot.GetChild(1).position;
                f.Solid(fragmentPosition, Vector2.one * .3f);
                f.PullStep(pull, .1f, away);
                Require(source != null && source.gameObject.activeSelf && !((bool)Get(pull, "completed"))
                    && BreakCount(f.Shake) == 0 && f.Arms.HeldObject == source,
                    "A failed completion layout emitted feedback or transferred the held source");
                Require((Vector2)Get(f.Mover, "heavyReleaseVelocity") == Vector2.zero,
                    "A failed completion layout released successful-break inertia");
            }
        }

        private static void CheckDirectionalCamera()
        {
            using (var f = new Fixture())
            {
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Big, Vector2.right);
                Vector2 grip = (Vector2)Get(f.Shake, "garbageGrabCameraPositionVelocity");
                Reset(f.Shake);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                Vector2 right = (Vector2)Get(f.Shake, "heavyGarbageBreakCameraPositionVelocity");
                float rightRotation = (float)Get(f.Shake, "heavyGarbageBreakCameraRotationVelocity");
                Require(right.x > 0f && Mathf.Abs(right.y) < .0001f && right.magnitude > grip.magnitude
                    && Mathf.Abs(rightRotation) > 0f, "Break screen impact is not stronger and directional");
                Require((Vector2)Get(f.Shake, "springPositionVelocity") == Vector2.zero
                    && (Vector2)Get(f.Shake, "garbageGrabCameraPositionVelocity") == Vector2.zero,
                    "Break impact leaked into an ordinary or grab spring");
                Reset(f.Shake); f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.left);
                RequireNear((Vector2)Get(f.Shake, "heavyGarbageBreakCameraPositionVelocity"), -right, "Reverse pulling did not reverse screen displacement");
                Require(Near((float)Get(f.Shake, "heavyGarbageBreakCameraRotationVelocity"), -rightRotation),
                    "Reverse pulling did not reverse screen roll");
                Reset(f.Shake); f.Shake.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                Vector2 rotated = (Vector2)Get(f.Shake, "heavyGarbageBreakCameraPositionVelocity");
                Require(Mathf.Abs(rotated.x) < .0001f && rotated.y < 0f
                    && Near(rotated.magnitude, right.magnitude), "Camera recoil was not transformed into the camera's local axes");
                Reset(f.Shake); f.Shake.transform.rotation = Quaternion.identity;
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.zero);
                Vector2 fallback = (Vector2)Get(f.Shake, "heavyGarbageBreakCameraPositionVelocity");
                Require(!float.IsNaN(fallback.x) && !float.IsNaN(fallback.y) && fallback.sqrMagnitude > 0f,
                    "A missing direction produced an invalid screen recoil");
            }
        }

        private static void CheckEnvelope()
        {
            using (var f = new Fixture())
            {
                Require(Near((float)Get(f.Shake, "heavyBreakLowFrequency"), .8f)
                    && Near((float)Get(f.Shake, "heavyBreakHighFrequency"), .55f)
                    && Near((float)Get(f.Shake, "heavyBreakDuration"), .35f)
                    && Near((float)Get(f.Shake, "heavyBreakHighFrequencyDuration"), .14f)
                    && Near((float)Get(f.Shake, "heavyBreakAttackDuration"), .02f)
                    && Near((float)Get(f.Shake, "heavyBreakImmediateRumbleFraction"), .35f)
                    && Near((float)Get(f.Shake, "heavyBreakPeakHoldDuration"), .05f)
                    && Near((float)Get(f.Shake, "heavyBreakFalloffExponent"), 1.4f),
                    "The production break feedback defaults differ from the authored two-stage impact");
                float start = Time.time;
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.up);
                RequireNear(Motors(f.Shake, start), new Vector2(.28f, .1925f), "Break motors had no immediate structural snap in the success frame");
                RequireNear(Motors(f.Shake, start + .01f), new Vector2(.54f, .37125f), "The break attack did not ramp from its immediate snap to peak");
                RequireNear(Motors(f.Shake, start + .03f), new Vector2(.8f, .55f), "The break peak was incorrect");
                RequireNear(Motors(f.Shake, start + .065f), new Vector2(.8f, .55f), "The break peak did not hold for the initial structural snap");
                Vector2 firstDecay = Motors(f.Shake, start + .10f), secondDecay = Motors(f.Shake, start + .13f);
                Require(firstDecay.x > secondDecay.x && firstDecay.y > secondDecay.y && secondDecay.y > 0f,
                    "The break channels did not decay smoothly after the peak");
                Vector2 recoil = Motors(f.Shake, start + .141f);
                Require(recoil.x > 0f && Near(recoil.y, 0f), "High frequency did not stop before the low-frequency recoil");
                Require(Motors(f.Shake, start + .349f).x > 0f, "Low-frequency recoil expired before its duration");
                RequireNear(Motors(f.Shake, start + .351f), Vector2.zero, "Break feedback did not expire to silence");
                Require(BreakCount(f.Shake) == 0, "Expired break pulse remained queued");
            }
        }

        private static void CheckOverlappingPulses()
        {
            using (var f = new Fixture())
            {
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.up);
                Set(f.Shake, "heavyBreakLowFrequency", .2f); Set(f.Shake, "heavyBreakHighFrequency", .7f);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.up);
                Require(BreakCount(f.Shake) == 2, "A second legitimate break replaced the first pulse");
                RequireNear(Motors(f.Shake, PeakTime(f.Shake)), new Vector2(.8f, .7f),
                    "Overlapping break pulses were summed or lost the strongest motor source");
            }
        }

        private static void CheckMotorComposition()
        {
            using (var f = new Fixture())
            {
                Type adaptive = typeof(RobotCameraShake).Assembly.GetType("AnimalGame.RobotMap.AdaptiveGamepadRumble", true);
                Type calibrationType = typeof(RobotCameraShake).Assembly.GetType("AnimalGame.RobotMap.SonyRumbleCalibration", true);
                object ordinary = Activator.CreateInstance(calibrationType, Instance, null,
                    new object[] { true, .1f, .2f, 2f, 1f, 1f, 0f }, null);
                object grip = Call(f.Shake, "CreateSonyGrabRumbleCalibration");
                object fracture = Call(f.Shake, "CreateSonyHeavyBreakRumbleCalibration");
                Vector2 Compose(Vector2 regular, Vector2 grab, Vector2 heavyBreak, bool sony, object breakCalibration) =>
                    (Vector2)CallStatic(adaptive, "ComposeFeedbackMotorSpeeds", regular, grab, heavyBreak, sony, ordinary, grip, breakCalibration);
                RequireNear(Compose(new Vector2(.9f, .1f), new Vector2(.45f, .3f), new Vector2(.8f, .55f), false, fracture),
                    new Vector2(.9f, .55f), "Non-Sony channels did not compose by raw per-motor maximum");
                RequireNear(Compose(new Vector2(.4f, .6f), new Vector2(.45f, .3f), new Vector2(.8f, .55f), true, fracture),
                    new Vector2(.8f, .55f), "Ordinary Sony calibration suppressed the dedicated break pulse");
                Set(f.Shake, "sonyHeavyBreakLowFrequencyMultiplier", .5f); Set(f.Shake, "sonyHeavyBreakHighFrequencyMultiplier", .25f);
                object tunedFracture = Call(f.Shake, "CreateSonyHeavyBreakRumbleCalibration");
                RequireNear(Compose(Vector2.zero, Vector2.zero, new Vector2(.8f, .55f), true, tunedFracture),
                    new Vector2(.4f, .1375f), "Dedicated Sony break tuning was not applied independently");
                RequireNear(Compose(new Vector2(.4f, .6f), Vector2.zero, Vector2.zero, true, tunedFracture),
                    new Vector2(.016f, .072f), "Break tuning altered the existing ordinary Sony channel");
                RequireNear(Compose(Vector2.zero, new Vector2(.45f, .3f), Vector2.zero, true, tunedFracture),
                    new Vector2(.45f, .3f), "Break tuning altered the existing grab Sony channel");
            }
        }

        private static void CheckIndependentChannels()
        {
            using (var f = new Fixture())
            {
                Set(f.Shake, "enableCameraShake", false);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                RequireNear(Motors(f.Shake, PeakTime(f.Shake)), new Vector2(.8f, .55f), "Global camera switch silenced the break motors");
                Require(CameraIsClear(f.Shake), "Disabled global camera shake retained a break impulse");
                Reset(f.Shake); Set(f.Shake, "enableCameraShake", true); Set(f.Shake, "enableHeavyGarbageBreakCameraShake", false);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                Require(Motors(f.Shake, PeakTime(f.Shake)).sqrMagnitude > 0f && CameraIsClear(f.Shake),
                    "Dedicated camera switch silenced haptics or retained camera feedback");
                Reset(f.Shake); Set(f.Shake, "enableHeavyGarbageBreakCameraShake", true); Set(f.Shake, "enableHeavyGarbageBreakRumble", false);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                Require(BreakCount(f.Shake) == 0 && !CameraIsClear(f.Shake), "Dedicated rumble switch disabled screen feedback or emitted motors");
                Call(f.Shake, "IntegrateSprings", .04f); Call(f.Shake, "ApplyShakeToCamera");
                Require(f.Shake.CurrentLocalPositionOffset.sqrMagnitude > 0f
                    && (Vector2)Get(f.Shake, "regularRumblePositionOffset") == Vector2.zero
                    && Near((float)Get(f.Shake, "regularRumbleRotationOffsetDegrees"), 0f),
                    "Break camera recoil created a second ordinary rumble tail");
                Reset(f.Shake); Set(f.Shake, "enableHeavyGarbageBreakRumble", true); Set(f.Shake, "enableGamepadRumble", false);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                Require(BreakCount(f.Shake) == 0 && !CameraIsClear(f.Shake), "Global rumble switch silenced screen feedback or queued motors");
                Reset(f.Shake); Set(f.Shake, "enableGamepadRumble", true); Set(f.Shake, "enableHeavyGarbageBreakFeedback", false);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                Require(BreakCount(f.Shake) == 0 && CameraIsClear(f.Shake), "Disabled break feedback still generated an impact");
            }
        }

        private static void CheckGlobalIntensity()
        {
            using (var f = new Fixture())
            {
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                Call(f.Shake, "IntegrateSprings", .04f); Call(f.Shake, "ApplyShakeToCamera");
                Vector2 full = f.Shake.CurrentLocalPositionOffset;
                float fullRoll = f.Shake.CurrentRotationOffsetDegrees;
                Require(full.sqrMagnitude > 0f && Mathf.Abs(fullRoll) > 0f, "Global-intensity fixture had no screen output");
                Reset(f.Shake); Set(f.Shake, "globalIntensity", .5f);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                RequireNear(Motors(f.Shake, PeakTime(f.Shake)), new Vector2(.4f, .275f), "Global intensity did not scale the break motors once");
                Call(f.Shake, "IntegrateSprings", .04f); Call(f.Shake, "ApplyShakeToCamera");
                RequireNear(f.Shake.CurrentLocalPositionOffset, full * .5f, "Global intensity did not scale screen displacement once");
                Require(Near(f.Shake.CurrentRotationOffsetDegrees, fullRoll * .5f), "Global intensity did not scale screen roll once");
                Set(f.Shake, "globalIntensity", 0f); Call(f.Shake, "ApplyShakeToCamera");
                RequireNear(Motors(f.Shake, PeakTime(f.Shake)), Vector2.zero, "Zero global intensity retained motor output");
                Require(f.Shake.CurrentLocalPositionOffset == Vector2.zero && Near(f.Shake.CurrentRotationOffsetDegrees, 0f),
                    "Zero global intensity retained screen output");
            }
        }

        private static void CheckLifecycle()
        {
            foreach (string callback in new[] { "OnApplicationFocus", "OnApplicationPause", "OnDisable" })
            using (var f = new Fixture())
            {
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                Require(BreakCount(f.Shake) == 1 && !CameraIsClear(f.Shake), "Lifecycle fixture had no pending break feedback");
                if (callback == "OnApplicationFocus") Call(f.Shake, callback, false);
                else if (callback == "OnApplicationPause") Call(f.Shake, callback, true);
                else { f.Shake.enabled = false; Call(f.Shake, callback); }
                Require(BreakCount(f.Shake) == 0 && CameraIsClear(f.Shake), "Lifecycle callback retained pending break feedback: " + callback);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                Require(BreakCount(f.Shake) == 0 && CameraIsClear(f.Shake), "Feedback accepted a new impact while suspended: " + callback);
                if (callback == "OnApplicationFocus") Call(f.Shake, callback, true);
                else if (callback == "OnApplicationPause") Call(f.Shake, callback, false);
                else { f.Shake.enabled = true; Call(f.Shake, "OnEnable"); }
                RequireNear(Motors(f.Shake, PeakTime(f.Shake)), Vector2.zero, "Resuming replayed discarded break feedback: " + callback);
                Require(CameraIsClear(f.Shake), "Resuming replayed discarded screen recoil: " + callback);
                f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                Require(BreakCount(f.Shake) == 1 && !CameraIsClear(f.Shake), "A fresh break after resuming did not trigger: " + callback);
            }
        }

        private static void CheckPausedAndDisabledChannels()
        {
            using (var f = new Fixture())
            {
                float previousScale = Time.timeScale;
                try
                {
                    f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                    Time.timeScale = 0f; Call(f.Shake, "LateUpdate");
                    Require(BreakCount(f.Shake) == 0 && CameraIsClear(f.Shake), "Paused zero-delta update retained break feedback");
                    f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                    Require(BreakCount(f.Shake) == 0 && CameraIsClear(f.Shake), "Time-scale pause accepted new break feedback");
                }
                finally { Time.timeScale = previousScale; }
                foreach (string channel in new[] { "enableHeavyGarbageBreakRumble", "enableGamepadRumble", "enableHeavyGarbageBreakFeedback" })
                {
                    Reset(f.Shake); f.Shake.PlayHeavyGarbageBreakFeedback(Vector2.right);
                    Require(BreakCount(f.Shake) == 1, "Runtime-switch fixture has no pending pulse");
                    Set(f.Shake, channel, false);
                    RequireNear(Motors(f.Shake, PeakTime(f.Shake)), Vector2.zero, "Runtime switch retained haptics: " + channel);
                    Require(BreakCount(f.Shake) == 0, "Runtime switch did not discard its pending haptics: " + channel);
                    Set(f.Shake, channel, true);
                    RequireNear(Motors(f.Shake, PeakTime(f.Shake)), Vector2.zero, "Re-enabling a switch replayed old haptics: " + channel);
                }
            }
        }

        private static int BreakCount(RobotCameraShake shake) => ((IList)Get(shake, "heavyGarbageBreakPulses")).Count;
        private static int GrabCount(RobotCameraShake shake) => ((IList)Get(shake, "garbageGrabPulses")).Count;
        private static float PeakTime(RobotCameraShake shake) => Time.time + (float)Get(shake, "heavyBreakAttackDuration") + .005f;
        private static Vector2 Motors(RobotCameraShake shake, float now) => (Vector2)Call(shake, "GetHeavyGarbageBreakMotorSpeeds", now);
        private static bool CameraIsClear(RobotCameraShake shake) =>
            (Vector2)Get(shake, "heavyGarbageBreakCameraPosition") == Vector2.zero
            && (Vector2)Get(shake, "heavyGarbageBreakCameraPositionVelocity") == Vector2.zero
            && Near((float)Get(shake, "heavyGarbageBreakCameraRotation"), 0f)
            && Near((float)Get(shake, "heavyGarbageBreakCameraRotationVelocity"), 0f);
        private static void Reset(RobotCameraShake shake)
        {
            Call(shake, "ClearGarbageGrabFeedback"); Call(shake, "ClearHeavyGarbageBreakFeedback");
            Call(shake, "ResetShakeState"); shake.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
        private static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
        private static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name, Instance).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);
        private static object CallStatic(Type type, string name, params object[] args) => type.GetMethod(name, Static).Invoke(null, args);

        private sealed class Fixture : IDisposable
        {
            private readonly object heavyFixture;
            public GameObject Root { get; }
            public RobotArmController Arms { get; }
            public RobotMover Mover => Root.GetComponent<RobotMover>();
            public RobotCameraShake Shake { get; }
            public Fixture()
            {
                Type type = typeof(HeavyGarbageRegressionChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
                heavyFixture = Activator.CreateInstance(type, Instance, null, Array.Empty<object>(), null);
                Root = (GameObject)type.GetProperty("Robot", Instance).GetValue(heavyFixture);
                Arms = (RobotArmController)type.GetProperty("Arms", Instance).GetValue(heavyFixture);
                var camera = new GameObject("Heavy break feedback regression camera");
                SceneManager.MoveGameObjectToScene(camera, Root.scene);
                Shake = camera.AddComponent<RobotCameraShake>(); Call(Shake, "Awake");
                Shake.Initialize(Mover, null, null); Set(Arms, "grabFeedback", Shake);
            }
            public void TickArms(float dt, bool grab) => Call(heavyFixture, "TickArms", dt, Vector2.up, grab, true);
            public WorldInteraction PrepareProductionSource(out HeavyGarbagePull pull, out GarbageFragmentSpawner spawner)
            {
                for (int i = 0; i < 90; i++) TickArms(1f / 60f, false);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Garbage/Big_Garbage.prefab");
                Require(prefab != null, "Missing production big-garbage prefab");
                GameObject root = Object.Instantiate(prefab);
                WorldInteraction item = root.GetComponent<WorldInteraction>();
                item.MoveToScene(Root.scene); item.WorldRotation = Quaternion.identity;
                Vector2 anchor = (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f;
                SpriteRenderer sprite = item.SpriteSource;
                Require(sprite != null && sprite.sprite != null, "Production big garbage lacks its visual bounds");
                Vector2 currentCenterOffset = (Vector2)sprite.bounds.center - (Vector2)item.WorldPosition;
                // Put both hands .1 world units inside the near edge. All prefab
                // scales, fragment prefabs, break settings and art stay intact.
                item.WorldPosition = anchor + Vector2.up * (sprite.bounds.extents.y - .1f) - currentCenterOffset;
                Call(root.GetComponent<GarbageItem>(), "Awake");
                spawner = root.GetComponent<GarbageFragmentSpawner>(); Call(spawner, "Awake");
                pull = root.GetComponent<HeavyGarbagePull>(); Call(pull, "Awake");
                Set(pull, "cameraShake", Shake);
                TickArms(1f / 60f, true);
                Require(Arms.HeldObject == item && item.Owner == Arms && item.Size == RecyclableSize.Big
                    && !item.Recyclable && spawner.HasPullFragmentPrefabs, "Fixture could not clamp the production big garbage with both hands");
                return item;
            }
            public void PullStep(HeavyGarbagePull pull, float dt, Vector2 away)
            {
                SetProperty(Mover, "CurrentThrottleIntent", -1f);
                SetProperty(Mover, "UnresistedMovementIntentWorld", away * 3f);
                Call(Mover, "StepHeavyPullMovement", dt);
                TickArms(dt, true);
                Call(pull, "Step", dt);
            }
            public void Complete(HeavyGarbagePull pull, Vector2 away)
            {
                WorldInteraction source = Arms.HeldObject;
                int limit = Mathf.CeilToInt(((float)Get(pull, "requiredPullDuration") + .2f) * 60f);
                for (int i = 0; i < limit && source != null && source.gameObject.activeSelf; i++)
                    PullStep(pull, 1f / 60f, away);
                Require(BreakCount(Shake) == 1 && Arms.HeldObject != source, "The lifetime fixture failed to complete its production pull");
            }
            public WorldInteraction Solid(Vector2 position, Vector2 size)
            {
                var obstacle = new GameObject("Arriving completion obstruction");
                SceneManager.MoveGameObjectToScene(obstacle, Root.scene);
                WorldInteraction item = obstacle.AddComponent<WorldInteraction>();
                item.WorldPosition = position; item.LocalSize = size;
                item.SetKind(WorldInteractionKind.BodyCollision);
                return item;
            }
            public void Dispose()
            {
                Call(Shake, "ClearGarbageGrabFeedback"); Call(Shake, "ClearHeavyGarbageBreakFeedback");
                // No check touches sent/current motor state or calls the transport.
                ((IDisposable)heavyFixture).Dispose();
            }
        }
    }
}

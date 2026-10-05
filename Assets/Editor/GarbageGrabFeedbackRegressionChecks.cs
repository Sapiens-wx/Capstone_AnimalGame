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
    // These checks inspect pending feedback and pure output composition. They never send
    // a motor command, and all objects live in an isolated editor preview scene.
    public static class GarbageGrabFeedbackRegressionChecks
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly List<string> passed = new();

        [MenuItem("Animal Game/Validation/Run Garbage Grab Feedback Checks")]
        public static void Run()
        {
            passed.Clear();
            Check("Production small, medium and big garbage trigger their own successful-grab feedback", CheckSuccessfulGrabs);
            Check("Empty, obstructed, occupied and insufficient-hand grabs create no feedback", CheckFailedGrabs);
            Check("Holding A does not retrigger; dropping and grabbing again does", CheckRepeatedGrabs);
            Check("Ordinary grabbable props do not trigger garbage feedback", CheckOrdinaryProp);
            Check("Big-to-medium and medium-to-small held fragment handoffs do not retrigger", CheckHeldHandoffs);
            Check("Small grab is haptic only; medium and big apply matching directional camera impacts", CheckCameraImpacts);
            Check("Grab motor attack, hold, decay and expiry use the configured duration", CheckEnvelope);
            Check("Overlapping grab sources take each motor's maximum", CheckOverlappingPulses);
            Check("Regular and grab channels calibrate Sony independently before maximum composition", CheckMotorComposition);
            Check("Grab camera and haptic switches stay independent without camera-to-rumble leakage", CheckIndependentHaptics);
            Check("Pause, focus loss and component disable clear pending grab haptics", CheckLifecycle);
            Debug.Log("Garbage grab feedback checks PASS: " + passed.Count + " groups\n" + string.Join("\n", passed));
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Check(string name, Action check) { check(); passed.Add(name); }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < .0001f;
        private static void RequireNear(Vector2 actual, Vector2 expected, string message)
        { Require(Vector2.Distance(actual, expected) < .0002f, message + ": " + actual + " versus " + expected); }

        private static void CheckSuccessfulGrabs()
        {
            foreach (RecyclableSize size in Enum.GetValues(typeof(RecyclableSize)))
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                Require(PulseCount(f.Shake) == 0, "Empty deployment emitted a grab pulse");
                WorldInteraction item = f.Garbage(size);
                Require(size != RecyclableSize.Big || !item.Recyclable,
                    "Big-garbage fixture lost the production non-recyclable setting");
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == item && item.Owner == f.Arms, "Production garbage could not be grabbed: " + size);
                Require(PulseCount(f.Shake) == 1, "Successful grab did not emit exactly one pulse: " + size);
                RequireNear(Motors(f.Shake, PeakTime(f.Shake)), size == RecyclableSize.Small
                    ? new Vector2(.18f, .12f) : new Vector2(.45f, .30f), "Wrong default grab strengths for " + size);
                Require(f.Shake.FollowsRobot(f.Root.GetComponent<RobotMover>()), "Feedback camera does not follow its initialized robot");
                Require(!f.Shake.FollowsRobot(null), "Unbound robot matched an initialized feedback camera");
            }
        }

        private static void CheckFailedGrabs()
        {
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                Require(PulseCount(f.Shake) == 0, "Empty grab triggered feedback");
                WorldInteraction item = f.Garbage(RecyclableSize.Medium, oneHandOnly: true);
                f.Tick(4, Vector2.up, true);
                Require(f.Arms.HeldObject == null && PulseCount(f.Shake) == 0,
                    "One hand triggered feedback for two-hand medium garbage");
                item.gameObject.SetActive(false);
                item = f.Garbage(RecyclableSize.Small);
                Object otherOwner = f.Shake;
                Require(item.TryGrab(otherOwner), "Occupied-grab fixture could not claim the item");
                f.Tick(3, Vector2.up, true);
                Require(f.Arms.HeldObject == null && PulseCount(f.Shake) == 0, "Already owned garbage triggered feedback");
            }
            using (var f = new Fixture())
            {
                WorldInteraction item = f.Garbage(RecyclableSize.Small);
                item.WorldPosition = Vector3.zero; item.LocalSize = Vector2.one * 3f;
                WorldInteraction obstacle = f.Solid(new Vector2(-.3f, .1f), new Vector2(.5f, .12f));
                f.Tick(120, Vector2.up, true);
                Require(f.Arms.State == RobotArmState.Extending && f.Arms.IsBlocked && f.Arms.HeldObject == null,
                    "Fixture did not exercise blocked deployment");
                Require(item.Owner == null && PulseCount(f.Shake) == 0, "Blocked or undeployed grab triggered feedback");
                obstacle.enabled = false;
                f.Tick(120, Vector2.up, true);
                Require(f.Arms.HeldObject == item && PulseCount(f.Shake) == 1,
                    "Removing the deployment obstruction did not trigger the first successful grab");
            }
        }

        private static void CheckRepeatedGrabs()
        {
            foreach (RecyclableSize size in Enum.GetValues(typeof(RecyclableSize)))
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                WorldInteraction item = f.Garbage(size);
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == item && PulseCount(f.Shake) == 1, "Initial grab fixture failed");
                Clear(f.Shake);
                f.Tick(20, Vector2.up, true);
                Require(f.Arms.HeldObject == item && PulseCount(f.Shake) == 0, "Sustained A repeated a grab pulse");
                f.Tick(1, Vector2.up, false);
                Require(item.Owner == null && f.Arms.HeldObject == null, "Release did not drop outer-held garbage");
                Clear(f.Shake);
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == item && PulseCount(f.Shake) == 1, "A new grab after a drop did not emit feedback");
            }
        }

        private static void CheckOrdinaryProp()
        {
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                WorldInteraction prop = f.Item(f.Arms.LeftHandWorld, Vector2.one * .05f, 1);
                // WorldInteraction defaults to recyclable: being recyclable alone must not
                // classify an arbitrary prop as garbage.
                Require(prop.Recyclable, "Ordinary-prop fixture no longer covers recyclable world props");
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == prop && PulseCount(f.Shake) == 0, "Non-garbage prop played garbage grab feedback");
            }
        }

        private static void CheckHeldHandoffs()
        {
            foreach (RecyclableSize sourceSize in new[] { RecyclableSize.Big, RecyclableSize.Medium })
            using (var f = new Fixture())
            {
                f.Tick(90, Vector2.up, true);
                WorldInteraction source = f.Garbage(sourceSize);
                f.Tick(1, Vector2.up, true);
                Require(f.Arms.HeldObject == source, "Fragment handoff did not start with a held source");
                Clear(f.Shake);
                RecyclableSize fragmentSize = sourceSize == RecyclableSize.Big ? RecyclableSize.Medium : RecyclableSize.Small;
                WorldInteraction fragment = f.Garbage(fragmentSize);
                Require(f.Arms.TryReplaceHeldObject(source, fragment), "Held fragment handoff failed");
                Require(f.Arms.HeldObject == fragment && fragment.Owner == f.Arms && source.Owner == null,
                    "Held fragment handoff lost ownership");
                f.Tick(4, Vector2.up, true);
                Require(PulseCount(f.Shake) == 0, "Fragment handoff fabricated another manual grab pulse");
            }
        }

        private static void CheckCameraImpacts()
        {
            using (var f = new Fixture())
            {
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Small, Vector2.right);
                Require((Vector2)Get(f.Shake, "garbageGrabCameraPositionVelocity") == Vector2.zero
                    && Near((float)Get(f.Shake, "garbageGrabCameraRotationVelocity"), 0f)
                    && (Vector2)Get(f.Shake, "springPositionVelocity") == Vector2.zero
                    && Near((float)Get(f.Shake, "springRotationVelocity"), 0f), "Small grab added a screen impact");
                Require(PulseCount(f.Shake) == 1, "Small grab did not add its haptic pulse");
                Clear(f.Shake); Call(f.Shake, "ResetShakeState");
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Medium, Vector2.right);
                Vector2 medium = (Vector2)Get(f.Shake, "garbageGrabCameraPositionVelocity");
                float mediumRotation = (float)Get(f.Shake, "garbageGrabCameraRotationVelocity");
                Require(medium.sqrMagnitude > 0f && Mathf.Abs(mediumRotation) > 0f, "Medium grab has no screen impulse");
                Require(Mathf.Abs(medium.x) > Mathf.Abs(medium.y), "Grab impact ignored the requested horizontal direction");
                Require((Vector2)Get(f.Shake, "springPositionVelocity") == Vector2.zero
                    && Near((float)Get(f.Shake, "springRotationVelocity"), 0f), "Medium grab leaked into the regular camera springs");
                Clear(f.Shake); Call(f.Shake, "ResetShakeState");
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Big, Vector2.right);
                RequireNear((Vector2)Get(f.Shake, "garbageGrabCameraPositionVelocity"), medium, "Big screen strength differs from medium");
                Require(Near((float)Get(f.Shake, "garbageGrabCameraRotationVelocity"), mediumRotation), "Big rotation strength differs from medium");
                Require((Vector2)Get(f.Shake, "springPositionVelocity") == Vector2.zero
                    && Near((float)Get(f.Shake, "springRotationVelocity"), 0f), "Big grab leaked into the regular camera springs");
                Clear(f.Shake); Call(f.Shake, "ResetShakeState");
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Medium, Vector2.left);
                RequireNear((Vector2)Get(f.Shake, "garbageGrabCameraPositionVelocity"), -medium, "Opposite grab force did not reverse screen displacement");
                Require(Near((float)Get(f.Shake, "garbageGrabCameraRotationVelocity"), -mediumRotation),
                    "Opposite grab force did not reverse screen rotation");
            }
        }

        private static void CheckEnvelope()
        {
            float duration = .22f, attack = .02f, hold = .03f, falloff = 1.15f;
            float Evaluate(float elapsed) => (float)CallStatic(typeof(RobotCameraShake), "EvaluateGarbageGrabEnvelope", elapsed, duration, attack, hold, falloff);
            Require(Near(Evaluate(0f), 0f) && Near(Evaluate(attack), 1f) && Near(Evaluate(attack + hold), 1f),
                "Grab pulse did not attack quickly and retain its peak");
            Require(Evaluate(.01f) > 0f && Evaluate(.01f) < 1f && Evaluate(.13f) > 0f && Evaluate(.13f) < 1f,
                "Grab attack or decay has no intermediate levels");
            Require(Evaluate(.13f) > Evaluate(.18f) && Near(Evaluate(duration), 0f) && Near(Evaluate(duration + 1f), 0f),
                "Grab pulse does not decay to silence at its own duration");
            using (var f = new Fixture())
            {
                foreach (RecyclableSize size in Enum.GetValues(typeof(RecyclableSize)))
                {
                    Clear(f.Shake);
                    float start = Time.time;
                    f.Shake.PlayGarbageGrabFeedback(size, Vector2.up);
                    float end = size == RecyclableSize.Small ? .12f : .22f;
                    Require(Motors(f.Shake, start + end - .001f).sqrMagnitude > 0f, "Grab pulse expired early: " + size);
                    RequireNear(Motors(f.Shake, start + end + .001f), Vector2.zero, "Grab pulse outlived its configured duration: " + size);
                }
            }
        }

        private static void CheckOverlappingPulses()
        {
            using (var f = new Fixture())
            {
                Set(f.Shake, "smallGrabLowFrequency", .6f);
                Set(f.Shake, "smallGrabHighFrequency", .1f);
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Medium, Vector2.up);
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Small, Vector2.up);
                Require(PulseCount(f.Shake) == 2, "Second legitimate pulse replaced the first source");
                RequireNear(Motors(f.Shake, PeakTime(f.Shake)), new Vector2(.6f, .3f),
                    "Overlapping grab pulses were added together or lost the stronger per-motor source");
                RequireNear(Motors(f.Shake, Time.time + .5f), Vector2.zero, "Expired pulse collection kept motors active");
            }
        }

        private static void CheckMotorComposition()
        {
            using (var f = new Fixture())
            {
                Type adaptive = typeof(RobotCameraShake).Assembly.GetType("AnimalGame.RobotMap.AdaptiveGamepadRumble", true);
                Type calibrationType = typeof(RobotCameraShake).Assembly.GetType("AnimalGame.RobotMap.SonyRumbleCalibration", true);
                object regular = Activator.CreateInstance(calibrationType, Instance, null,
                    new object[] { true, .1f, .2f, 2f, 1f, 1f, 0f }, null);
                object grab = Call(f.Shake, "CreateSonyGrabRumbleCalibration");
                Vector2 Compose(Vector2 ordinary, Vector2 pulse, bool sony, object pulseCalibration) =>
                    (Vector2)CallStatic(adaptive, "ComposeMotorSpeeds", ordinary, pulse, sony, regular, pulseCalibration);
                RequireNear(Compose(new Vector2(.4f, .6f), new Vector2(.18f, .12f), false, grab), new Vector2(.4f, .6f),
                    "Non-Sony motors did not use raw per-motor maximum composition");
                RequireNear(Compose(new Vector2(.4f, .6f), new Vector2(.18f, .12f), true, grab), new Vector2(.18f, .12f),
                    "Sony ordinary calibration suppressed the dedicated grab pulse");
                RequireNear(Compose(Vector2.zero, new Vector2(.45f, .3f), true, grab), new Vector2(.45f, .3f),
                    "Dedicated Sony default calibration changed the planned medium grab strength");
                RequireNear(Compose(new Vector2(.4f, .6f), Vector2.zero, true, grab), new Vector2(.016f, .072f),
                    "Adding the grab channel changed ordinary Sony calibration");
                Set(f.Shake, "sonyGrabLowFrequencyMultiplier", .5f);
                Set(f.Shake, "sonyGrabHighFrequencyMultiplier", .25f);
                object tunedGrab = Call(f.Shake, "CreateSonyGrabRumbleCalibration");
                RequireNear(Compose(Vector2.zero, new Vector2(.45f, .3f), true, tunedGrab), new Vector2(.225f, .075f),
                    "Dedicated Sony grab tuning was not applied independently");
                RequireNear(Compose(new Vector2(.4f, .6f), Vector2.zero, true, tunedGrab), new Vector2(.016f, .072f),
                    "Dedicated Sony tuning leaked into ordinary feedback");
            }
        }

        private static void CheckIndependentHaptics()
        {
            using (var f = new Fixture())
            {
                Set(f.Shake, "enableCameraShake", false);
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Medium, Vector2.up);
                RequireNear(Motors(f.Shake, PeakTime(f.Shake)), new Vector2(.45f, .3f), "Camera-shake switch disabled grab haptics");
                Require((Vector2)Get(f.Shake, "garbageGrabCameraPositionVelocity") == Vector2.zero
                    && (Vector2)Get(f.Shake, "springPositionVelocity") == Vector2.zero, "Disabled screen shake still injected an impact");
                Clear(f.Shake);
                Set(f.Shake, "enableCameraShake", true); Set(f.Shake, "enableGarbageGrabCameraShake", false);
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Big, Vector2.up);
                Require(Motors(f.Shake, PeakTime(f.Shake)).sqrMagnitude > 0f
                    && (Vector2)Get(f.Shake, "garbageGrabCameraPositionVelocity") == Vector2.zero
                    && (Vector2)Get(f.Shake, "springPositionVelocity") == Vector2.zero, "Dedicated camera switch disabled haptics or retained screen impact");
                Clear(f.Shake); Set(f.Shake, "enableGarbageGrabRumble", false);
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Small, Vector2.up);
                Require(Motors(f.Shake, PeakTime(f.Shake)) == Vector2.zero, "Disabled dedicated grab rumble still generated a motor pulse");
                Set(f.Shake, "enableGarbageGrabCameraShake", true);
                foreach (RecyclableSize size in new[] { RecyclableSize.Medium, RecyclableSize.Big })
                {
                    Clear(f.Shake); Call(f.Shake, "ResetShakeState");
                    f.Shake.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    f.Shake.PlayGarbageGrabFeedback(size, Vector2.right);
                    Require((Vector2)Get(f.Shake, "garbageGrabCameraPositionVelocity") != Vector2.zero
                        && PulseCount(f.Shake) == 0 && Motors(f.Shake, PeakTime(f.Shake)) == Vector2.zero,
                        "Disabling grab haptics also disabled the requested screen impact: " + size);
                    Require((Vector2)Get(f.Shake, "springPositionVelocity") == Vector2.zero
                        && Near((float)Get(f.Shake, "springRotationVelocity"), 0f),
                        "Grab screen impact leaked into springs used by regular rumble: " + size);
                    Call(f.Shake, "IntegrateSprings", 1f / 60f);
                    Call(f.Shake, "ApplyShakeToCamera");
                    Require(f.Shake.CurrentLocalPositionOffset.sqrMagnitude > 0f
                        && Mathf.Abs(f.Shake.CurrentRotationOffsetDegrees) > 0f,
                        "Independent grab camera springs were omitted from final screen shake: " + size);
                    Require((Vector2)Get(f.Shake, "regularRumblePositionOffset") == Vector2.zero
                        && Near((float)Get(f.Shake, "regularRumbleRotationOffsetDegrees"), 0f),
                        "Grab camera shake was mapped back into ordinary hand rumble: " + size);
                }
            }
        }

        private static void CheckLifecycle()
        {
            using (var f = new Fixture())
            {
                foreach (string callback in new[] { "OnApplicationFocus", "OnApplicationPause", "OnDisable" })
                {
                    f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Medium, Vector2.up);
                    Require(PulseCount(f.Shake) > 0, "Lifecycle fixture has no pending pulse");
                    if (callback == "OnApplicationFocus") Call(f.Shake, callback, false);
                    else if (callback == "OnApplicationPause") Call(f.Shake, callback, true);
                    else Call(f.Shake, callback);
                    Require(PulseCount(f.Shake) == 0 && Motors(f.Shake, PeakTime(f.Shake)) == Vector2.zero,
                        "Lifecycle stop retained pending grab rumble: " + callback);
                }
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Small, Vector2.up);
                float previousScale = Time.timeScale;
                try
                {
                    Time.timeScale = 0f;
                    // Pending pulses must be cleared even if zero deltaTime would have
                    // returned before the ordinary camera update.
                    Call(f.Shake, "LateUpdate");
                    Require(PulseCount(f.Shake) == 0, "Paused zero-delta update retained grab rumble");
                    f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Small, Vector2.up);
                    Require(PulseCount(f.Shake) == 0, "Paused grab accepted a new pulse");
                }
                finally { Time.timeScale = previousScale; }
                f.Shake.enabled = false;
                f.Shake.PlayGarbageGrabFeedback(RecyclableSize.Small, Vector2.up);
                Require(PulseCount(f.Shake) == 0, "Disabled feedback component accepted a new pulse");
            }
        }

        private static int PulseCount(RobotCameraShake shake) => ((IList)Get(shake, "garbageGrabPulses")).Count;
        private static void Clear(RobotCameraShake shake) => Call(shake, "ClearGarbageGrabFeedback");
        private static float PeakTime(RobotCameraShake shake) => Time.time + (float)Get(shake, "grabRumbleAttackDuration");
        private static Vector2 Motors(RobotCameraShake shake, float time) => (Vector2)Call(shake, "GetGarbageGrabMotorSpeeds", time);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
        private static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);
        private static object CallStatic(Type type, string name, params object[] args) => type.GetMethod(name, Static).Invoke(null, args);

        private sealed class Fixture : IDisposable
        {
            private readonly object armsFixture;
            public GameObject Root { get; }
            public RobotArmController Arms { get; }
            public RobotCameraShake Shake { get; }
            public Fixture()
            {
                Type type = typeof(RobotArmRegressionChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
                armsFixture = Activator.CreateInstance(type, Instance, null, new object[] { false }, null);
                Root = (GameObject)type.GetProperty("Root", Instance).GetValue(armsFixture);
                Arms = (RobotArmController)type.GetProperty("Arms", Instance).GetValue(armsFixture);
                GameObject camera = new GameObject("Garbage feedback regression camera");
                SceneManager.MoveGameObjectToScene(camera, Root.scene);
                Shake = camera.AddComponent<RobotCameraShake>();
                Call(Shake, "Awake");
                Shake.Initialize(Root.GetComponent<RobotMover>(), null, null);
                Set(Arms, "grabFeedback", Shake);
            }
            public void Tick(int frames, Vector2 stick, bool grab) => Call(armsFixture, "Tick", frames, stick, grab, true);
            public WorldInteraction Item(Vector2 position, Vector2 size, int hands) => (WorldInteraction)Call(armsFixture, "Item", position, size, hands);
            public WorldInteraction Solid(Vector2 position, Vector2 size) => (WorldInteraction)Call(armsFixture, "Solid", position, size);
            public WorldInteraction Garbage(RecyclableSize size, bool oneHandOnly = false)
            {
                string name = size == RecyclableSize.Big ? "Big" : size.ToString();
                GameObject settings = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Garbage/" + name + "_Garbage.prefab");
                Require(settings != null && settings.GetComponent<GarbageItem>() != null, "Missing production garbage classification: " + name);
                bool twoHands = size != RecyclableSize.Small;
                Vector2 position = twoHands && !oneHandOnly ? (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f : Arms.LeftHandWorld;
                WorldInteraction item = Item(position, twoHands && !oneHandOnly ? new Vector2(.5f, .1f) : Vector2.one * .05f, twoHands ? 2 : 1);
                EditorUtility.CopySerialized(settings.GetComponent<WorldInteraction>(), item);
                item.BoxSource = null; item.SpriteSource = null; item.LocalCenter = Vector2.zero;
                item.LocalSize = twoHands && !oneHandOnly ? new Vector2(.5f, .1f) : Vector2.one * .05f;
                item.SetKind(WorldInteractionKind.Grabbable);
                GarbageItem garbage = item.gameObject.AddComponent<GarbageItem>();
                EditorUtility.CopySerialized(settings.GetComponent<GarbageItem>(), garbage);
                Call(garbage, "Awake");
                return item;
            }
            public void Dispose()
            {
                Clear(Shake);
                // No check enters UpdateGamepadRumble or mutates the sent/current motor
                // fields, so destroying the component cannot issue a stop command.
                ((IDisposable)armsFixture).Dispose();
            }
        }
    }
}

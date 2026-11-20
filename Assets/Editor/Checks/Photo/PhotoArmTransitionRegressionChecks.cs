using System;
using System.Collections;
using System.Reflection;
using AnimalGame.Garbage;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    // Request the actual photo transition while the production arm state machine
    // owns input, a grip or a recycling presentation. Do not fake photo state.
    public static class PhotoArmTransitionRegressionChecks
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly int[] FrameRates = { 30, 60, 120 };

        [MenuItem("Animal Game/Validation/Run Photo Arm Transition Checks")]
        public static void Run()
        {
            foreach (int fps in FrameRates)
            {
                CheckOpenAndPartialDeployment(fps);
                CheckHeldGarbage(fps);
                CheckRecycleCancellation(fps);
                CheckHeavyPullCancellation(fps);
                CheckOldInputAndFreshReopen(fps);
            }
            CheckFailedEntryPreservesArms();
            CheckRepeatedRequestsAndClosedArms();
            CheckReleaseCallbacksAbortEntry();
            CheckReentrantPhotoRequests();
            CheckIndependentCaptureIsPreserved();
            CheckSharedKeyboardGrabKey();
            Debug.Log("Photo arm transition checks PASS: 11 groups; actual photo requests from open/partially "
                + "extending/retracting production arms, small/medium/big/ordinary grips, small/medium feeding "
                + "and processing cancellation/material restoration, real heavy pull cancellation, entry-event "
                + "cleanup ordering and old-input rearming at 30/60/120 FPS; failed-entry isolation, repeat "
                + "and recursive requests, callback disable/control-change cancellation, independent capture "
                + "and shared keyboard grab/shutter isolation.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckOpenAndPartialDeployment(int fps)
        {
            foreach (string phase in new[] { "open", "extending", "retracting" })
            using (var f = new Fixture(fps))
            {
                if (phase == "extending")
                {
                    f.Step(Vector2.zero, true, false);
                    f.Tick(.1f, Vector2.zero, false, false);
                }
                else
                {
                    f.Deploy();
                    if (phase == "retracting")
                    {
                        f.Step(Vector2.zero, true, false);
                        f.Tick(.1f, Vector2.zero, false, false);
                    }
                }
                Require(f.Arms.VisibleDeployment01 > 0f, "Photo transition fixture has no visible arms: " + phase);
                if (phase != "open") Require(f.Arms.VisibleDeployment01 < 1f,
                    "Photo transition fixture was not partway through its animation: " + phase);
                f.EnterPhoto(() => f.RequireClean("Photo entry event saw arms during " + phase));
                f.RequireClean("Photo request did not synchronously cancel " + phase);
                Require(f.Arms.enabled, "Photo entry disabled the arm controller instead of cancelling its operation");
            }
        }

        private static void CheckHeldGarbage(int fps)
        {
            foreach (string kind in new[] { "Small", "Medium", "Big", "ordinary" })
            using (var f = new Fixture(fps))
            {
                f.Deploy();
                WorldInteraction item = f.Grab(kind);
                Vector3 position = item.WorldPosition, scale = item.MotionRoot.localScale;
                Quaternion rotation = item.WorldRotation;
                int released = 0, recycled = 0;
                Set(item, "onReleased", (Action)(() => released++));
                Set(item, "onRecycled", (Action)(() => recycled++));
                f.EnterPhoto(() =>
                {
                    f.RequireClean("Photo event fired before held-item cleanup: " + kind);
                    Require(item.Owner == null && released == 1 && recycled == 0 && item.gameObject.activeSelf,
                        "Photo entry event still owned, consumed or repeatedly released garbage: " + kind);
                });
                Require(Vector3.Distance(position, item.WorldPosition) < .0001f
                    && Vector3.Distance(scale, item.MotionRoot.localScale) < .0001f
                    && Quaternion.Angle(rotation, item.WorldRotation) < .001f,
                    "Photo cancellation displaced or rescaled held garbage: " + kind);
                f.Photo.SetPhotoModeActive(true);
                Call(f.Arms, "CancelArmControl");
                Require(released == 1 && recycled == 0 && item.Owner == null,
                    "Repeated active/cancel requests repeated the garbage release: " + kind);
            }
        }

        private static void CheckRecycleCancellation(int fps)
        {
            foreach (string kind in new[] { "Small", "Medium" })
            foreach (bool processing in new[] { false, true })
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.Grab(kind);
                SpriteRenderer[] sprites = item.GetComponentsInChildren<SpriteRenderer>(true);
                Material[] originals = new Material[sprites.Length];
                for (int i = 0; i < sprites.Length; i++) originals[i] = sprites[i].sharedMaterial;
                Vector3 scale = item.MotionRoot.localScale;
                int released = 0, recycled = 0;
                Set(item, "onReleased", (Action)(() => released++));
                Set(item, "onRecycled", (Action)(() => recycled++));
                f.BeginRecycle(item);
                Vector3 safePosition = (Vector3)Get(f.Arms, "recycleSafePosition");
                Quaternion safeRotation = (Quaternion)Get(f.Arms, "recycleSafeRotation");
                if (processing)
                {
                    for (int i = 0; i < fps * 8
                        && f.Arms.CurrentMediumRecycleFrame.Phase != MediumRecyclePhase.Processing; i++)
                        f.Step(Vector2.zero, false, false);
                    Require(f.Arms.CurrentMediumRecycleFrame.Phase == MediumRecyclePhase.Processing,
                        "Photo recycle fixture did not reach processing: " + kind);
                    f.Tick(.137f, Vector2.zero, false, false);
                }
                else f.Tick(.1f, Vector2.zero, false, false);
                Require(f.Arms.State == RobotArmState.Recycling && Get(f.Arms, "mediumRecycleClip") != null
                    && item.Owner == f.Arms && recycled == 0,
                    "Photo recycle fixture was not actively feeding/processing: " + kind);
                Material[] temporary = new Material[sprites.Length];
                for (int i = 0; i < sprites.Length; i++) temporary[i] = sprites[i].sharedMaterial;
                f.EnterPhoto(() =>
                {
                    f.RequireClean("Photo entry event retained a recycle presentation: " + kind);
                    Require(item.Owner == null && released == 1 && recycled == 0 && item.gameObject.activeSelf,
                        "Photo entry completed rather than cancelled recycling: " + kind);
                    Require(Vector3.Distance(item.WorldPosition, safePosition) < .0001f
                        && Quaternion.Angle(item.WorldRotation, safeRotation) < .001f
                        && Vector3.Distance(item.MotionRoot.localScale, scale) < .0001f,
                        "Photo entry did not restore interrupted garbage to its safe physical pose: " + kind);
                    for (int i = 0; i < sprites.Length; i++)
                        Require(sprites[i].sharedMaterial == originals[i] && temporary[i] == null,
                            "Photo entry retained an inlet material or changed a shared asset: " + kind);
                });
                f.Tick(8f, Vector2.down, true, true);
                Require(released == 1 && recycled == 0 && item.Owner == null && item.gameObject.activeSelf,
                    "Cancelled recycling resumed in photo mode: " + kind);
            }
        }

        private static void CheckHeavyPullCancellation(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy(); WorldInteraction item = f.Grab("Big");
                HeavyGarbagePull pull = item.GetComponent<HeavyGarbagePull>();
                GarbageFragmentSpawner spawner = item.GetComponent<GarbageFragmentSpawner>();
                Require(pull != null && spawner != null && spawner.HasPullFragmentPrefabs,
                    "Heavy photo fixture does not use the production pull and fragment prefabs");
                Set(pull, "cameraShake", f.Shake);
                Vector2 away = ((Vector2)f.Root.transform.position - (Vector2)item.WorldPosition).normalized;
                SetProperty(f.Mover, "CurrentThrottleIntent", -1f);
                SetProperty(f.Mover, "UnresistedMovementIntentWorld", away * 3f);
                for (int i = 0; i < Mathf.CeilToInt(.3f * fps); i++)
                {
                    Call(f.Mover, "StepHeavyPullMovement", 1f / fps);
                    f.Step(Vector2.zero, false, true);
                    Call(pull, "Step", 1f / fps);
                }
                Require(pull.HasGripAnchor && pull.Progress > 0f && spawner.PullPreviewRoot != null
                    && Get(f.Mover, "heavyPullOwner") != null && (float)Get(f.Shake, "garbagePullRumble") > 0f,
                    "Photo heavy fixture did not create actual tension, preview, constraint and rumble");
                int released = 0; Set(item, "onReleased", (Action)(() => released++));
                f.EnterPhoto(() =>
                {
                    f.RequireClean("Photo event retained heavy-pull arms");
                    Require(item.Owner == null && item.gameObject.activeSelf && released == 1
                        && !pull.HasGripAnchor && !pull.IsReturningGrip && pull.Progress == 0f
                        && pull.StoredPullSpeed == 0f && spawner.PullPreviewRoot == null
                        && Get(f.Mover, "heavyPullOwner") == null
                        && (float)Get(f.Shake, "garbagePullRumble") == 0f,
                        "Photo event retained heavy-pull anchors, preview, motion or rumble");
                });
                Require(pull.enabled, "Photo cancellation disabled the reusable heavy-pull component");
            }
        }

        private static void CheckOldInputAndFreshReopen(int fps)
        {
            using (var f = new Fixture(fps))
            {
                f.Deploy(true); WorldInteraction item = f.Grab("Small", true);
                f.EnterPhoto();
                f.Tick(.5f, Vector2.up, true, true);
                f.RequireClean("Old held L3/A revived arms during photo mode");
                f.Photo.SetPhotoModeActive(false);
                Call(f.Photo, "CompleteExitPresentation");
                Require(!f.Photo.IsActive && !f.Mover.IsPhotoModeInputLocked,
                    "Photo exit did not restore ordinary input ownership");
                f.Tick(.5f, Vector2.up, true, true);
                f.RequireClean("Photo exit automatically restored an old deployment or held input");
                Require(item.Owner == null, "Photo exit re-grabbed dropped garbage without new deployment");
                f.Step(Vector2.zero, false, false);
                f.Step(Vector2.zero, true, false);
                f.Tick(3f, Vector2.zero, false, false);
                Require(f.Arms.IsArmModeActive && f.Mover.IsArmInputCaptured
                    && f.Arms.VisibleDeployment01 > .999f,
                    "A fresh L3 press could not reopen arms after leaving photo mode");
            }
        }

        private static void CheckFailedEntryPreservesArms()
        {
            foreach (string reason in new[] { "external", "fallen", "tipped", "disabled" })
            using (var f = new Fixture(60))
            {
                f.Deploy(); WorldInteraction item = f.Grab("Small");
                Vector3 position = item.WorldPosition;
                int released = 0, events = 0;
                Set(item, "onReleased", (Action)(() => released++));
                f.Photo.ModeChanged += active => { if (active) events++; };
                if (reason == "external") SetProperty(f.Mover, "MovementMode", RobotMovementMode.ExternalTumble);
                else if (reason == "fallen") SetProperty(f.Mover, "MovementMode", RobotMovementMode.Fallen);
                else if (reason == "tipped")
                {
                    var balance = f.Root.AddComponent<RobotBalanceController>();
                    SetProperty(balance, "IsTippedOver", true); Set(f.Photo, "balance", balance);
                }
                else f.Photo.enabled = false;
                f.Photo.SetPhotoModeActive(true);
                Require(!f.Photo.IsActive && events == 0 && released == 0
                    && f.Arms.IsArmModeActive && f.Arms.HeldObject == item && item.Owner == f.Arms
                    && f.Arms.VisibleDeployment01 > .999f
                    && Vector3.Distance(position, item.WorldPosition) < .0001f,
                    "A rejected photo request cancelled the existing arm session: " + reason);
                SetProperty(f.Mover, "MovementMode", RobotMovementMode.Driven);
            }
        }

        private static void CheckRepeatedRequestsAndClosedArms()
        {
            using (var f = new Fixture(60))
            {
                f.RequireClean("New production fixture did not start closed");
                int events = 0;
                f.Photo.ModeChanged += active => { if (active) { events++; f.RequireClean("Closed-arm entry revived arms"); } };
                f.Photo.SetPhotoModeActive(true); f.Photo.SetPhotoModeActive(true);
                Require(f.Photo.IsEntering && events == 1, "Repeated active requests restarted photo entry");
                Call(f.Arms, "CancelArmControl"); Call(f.Arms, "CancelArmControl");
                Require(f.Photo.IsEntering && events == 1, "Idempotent arm cancellation changed photo state");
            }
        }

        private static void CheckReleaseCallbacksAbortEntry()
        {
            foreach (string callback in new[] { "photo disable", "disable/re-enable", "inactive robot", "external", "false request",
                "destroy photo", "destroy robot" })
            using (var f = new Fixture(60))
            {
                f.Deploy(); WorldInteraction item = f.Grab("Small");
                int released = 0, events = 0;
                f.Photo.ModeChanged += active => { if (active) events++; };
                Set(item, "onReleased", (Action)(() =>
                {
                    released++;
                    if (callback.StartsWith("photo disable", StringComparison.Ordinal)) f.Photo.enabled = false;
                    else if (callback == "disable/re-enable")
                    {
                        f.Photo.enabled = false; Call(f.Photo, "OnDisable"); f.Photo.enabled = true;
                    }
                    else if (callback == "inactive robot") f.Root.SetActive(false);
                    else if (callback == "external") SetProperty(f.Mover, "MovementMode", RobotMovementMode.ExternalTumble);
                    else if (callback == "destroy photo") Object.DestroyImmediate(f.Photo);
                    else if (callback == "destroy robot") Object.DestroyImmediate(f.Root);
                    else f.Photo.SetPhotoModeActive(false);
                }));
                f.Photo.SetPhotoModeActive(true);
                Require((f.Photo == null || !f.Photo.IsActive) && events == 0 && released == 1 && item.Owner == null,
                    "Photo entry overrode a release callback that invalidated/cancelled it: " + callback);
                if (f.Root != null)
                {
                    f.RequireClean("Aborted photo transition restored arms: " + callback);
                    SetProperty(f.Mover, "MovementMode", RobotMovementMode.Driven);
                }
            }
        }

        private static void CheckReentrantPhotoRequests()
        {
            using (var f = new Fixture(60))
            {
                f.Deploy(); WorldInteraction item = f.Grab("Small");
                int released = 0, events = 0;
                Set(item, "onReleased", (Action)(() => { released++; f.Photo.SetPhotoModeActive(true); }));
                f.Photo.ModeChanged += active => { if (active) { events++; f.RequireClean("Recursive entry observed dirty arms"); } };
                f.Photo.SetPhotoModeActive(true);
                Require(f.Photo.IsEntering && released == 1 && events == 1,
                    "Recursive active request duplicated release or mode events");
            }
        }

        private static void CheckIndependentCaptureIsPreserved()
        {
            // Without a functioning arm controller, an unrelated input capture must
            // still reject photography rather than being cleared as if it were arms.
            var root = new GameObject("Independent photo capture regression");
            try
            {
                var mover = root.AddComponent<RobotMover>(); Call(mover, "Awake");
                var photo = root.AddComponent<PhotoModeController>(); Call(photo, "Awake");
                mover.SetArmInputCaptured(true);
                photo.SetPhotoModeActive(true);
                Require(!photo.IsActive && mover.IsArmInputCaptured,
                    "Photography cleared an independent capture without an arm controller");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void CheckSharedKeyboardGrabKey()
        {
            using (var f = new Fixture(60))
            {
                Require((bool)Call(f.Photo, "CanUseKeyboardShutterForEntry"),
                    "The default keyboard shutter was blocked while arms were closed");
                f.Deploy();
                Require(!(bool)Call(f.Photo, "CanUseKeyboardShutterForEntry"),
                    "The shared Space grab key could accidentally enter photography from open arms");
                // Dedicated photo requests (P/RB and callers) remain valid even though
                // the shared keyboard grab/shutter entry sample is filtered out.
                f.EnterPhoto();
                Require((bool)Call(f.Photo, "CanUseKeyboardShutterForEntry"),
                    "The shared key could not operate the camera after entering photography");
            }
            using (var f = new Fixture(60))
            {
                f.Deploy();
                Set(f.Arms, "keyboardGrabKey", KeyCode.E);
                Require((bool)Call(f.Photo, "CanUseKeyboardShutterForEntry"),
                    "A rebound grab key incorrectly suppressed the unrelated default shutter");
                Set(f.Arms, "keyboardGrabKey", KeyCode.Space);
                Set(f.Photo, "keyboardFocusAndShutterKey", KeyCode.K);
                Require((bool)Call(f.Photo, "CanUseKeyboardShutterForEntry"),
                    "An unrelated rebound shutter key was suppressed by arm deployment");
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
        private static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name, Instance).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);

        private sealed class Fixture : IDisposable
        {
            private readonly object artworkFixture;
            private readonly int fps;
            private readonly Scene scene;
            public GameObject Root { get; }
            public RobotArmController Arms { get; }
            public RobotMover Mover { get; }
            public PhotoModeController Photo { get; }
            public RobotCameraShake Shake { get; }
            public Transform Frame { get; }
            public Fixture(int frameRate)
            {
                fps = frameRate;
                Type type = typeof(RobotArmRegressionChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
                artworkFixture = Activator.CreateInstance(type, Instance, null, new object[] { true }, null);
                Root = (GameObject)type.GetProperty("Root", Instance).GetValue(artworkFixture);
                scene = Root.scene;
                Arms = (RobotArmController)type.GetProperty("Arms", Instance).GetValue(artworkFixture);
                Mover = Root.GetComponent<RobotMover>(); Call(Mover, "Awake");
                Photo = Root.AddComponent<PhotoModeController>(); Call(Photo, "Awake");
                Call(Arms, "Awake"); Frame = Root.GetComponent<RobotMarkerView>().MarkerVisualRoot;
                var camera = new GameObject("Photo arm regression feedback"); SceneManager.MoveGameObjectToScene(camera, Root.scene);
                Shake = camera.AddComponent<RobotCameraShake>();
                EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Resources/Camera/RobotCamera.prefab").GetComponent<RobotCameraShake>(), Shake);
                Call(Shake, "Awake"); Shake.Initialize(Mover, null, null); Set(Arms, "grabFeedback", Shake);
            }
            public void Step(Vector2 stick, bool rawL3, bool grab)
                => Call(Arms, "StepInput", 1f / fps, stick, rawL3, grab, false);
            public void Tick(float seconds, Vector2 stick, bool rawL3, bool grab)
            { for (int i = 0; i < Mathf.CeilToInt(seconds * fps); i++) Step(stick, rawL3, grab); }
            public void Deploy(bool keepL3Held = false)
            {
                Step(Vector2.zero, true, false); Tick(3f, Vector2.zero, keepL3Held, false);
                Require(Arms.IsArmModeActive && Mover.IsArmInputCaptured && Arms.VisibleDeployment01 > .999f,
                    "Production photo-arm fixture failed to deploy");
            }
            public WorldInteraction Grab(string kind, bool rawL3 = false)
            {
                Tick(1f, Vector2.up, rawL3, false);
                WorldInteraction item;
                if (kind == "ordinary")
                {
                    var go = new GameObject("Ordinary photo-transition item"); SceneManager.MoveGameObjectToScene(go, Root.scene);
                    item = go.AddComponent<WorldInteraction>(); item.SetKind(WorldInteractionKind.Grabbable);
                    item.LocalSize = Vector2.one * .05f; Set(item, "requiredHands", 1);
                    Set(item, "recyclable", false); Set(item, "grabResistance", .35f);
                }
                else
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefabs/Environment/Garbage/" + kind + "_Garbage.prefab");
                    GameObject go = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(go, Root.scene);
                    item = go.GetComponent<WorldInteraction>(); Call(go.GetComponent<GarbageItem>(), "Awake");
                    if (kind == "Big")
                    {
                        Call(go.GetComponent<GarbageFragmentSpawner>(), "Awake");
                        Call(go.GetComponent<HeavyGarbagePull>(), "Awake");
                    }
                }
                item.WorldPosition = item.RequiredHands == 2
                    ? (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f : Arms.LeftHandWorld;
                // The robot reaches the near edge of fixed heavy garbage; its body
                // and fragment previews begin farther forward than the hand anchor.
                if (kind == "Big") item.WorldPosition += Root.transform.up * .5f;
                item.WorldRotation = Root.transform.rotation;
                Step(Vector2.zero, rawL3, true);
                Require(Arms.HeldObject == item && item.Owner == Arms && !Arms.IsRecycleReady,
                    "Production photo-arm fixture failed to grab outside inlet: " + kind);
                return item;
            }
            public void BeginRecycle(WorldInteraction item)
            {
                Vector2 inlet = (Vector2)typeof(RobotArmController).GetProperty("DockLocal", Instance).GetValue(Arms);
                item.WorldPosition = Frame.TransformPoint(inlet);
                int hands = (int)Get(Arms, "heldHands");
                Vector2 anchor = hands == 3 ? (Arms.LeftHandWorld + Arms.RightHandWorld) * .5f
                    : hands == 1 ? Arms.LeftHandWorld : Arms.RightHandWorld;
                Set(Arms, "heldOffset", (Vector2)Frame.InverseTransformVector(item.WorldPosition - (Vector3)anchor));
                Call(Arms, "StepInput", 0f, Vector2.zero, false, false, false);
                Require(Arms.State == RobotArmState.Recycling, "Photo fixture failed to start recycling");
            }
            public void EnterPhoto(Action onEntry = null)
            {
                int events = 0;
                Photo.ModeChanged += active => { if (active) { events++; onEntry?.Invoke(); } };
                Photo.SetPhotoModeActive(true);
                Require(Photo.IsEntering && Mover.IsPhotoModeInputLocked && events == 1,
                    "Actual photo request did not enter after cancelling mechanical arms; FPS=" + fps);
            }
            public void RequireClean(string message)
            {
                Require(Arms.State == RobotArmState.Retracted && Arms.VisibleDeployment01 < .00001f
                    && !Arms.IsArmModeActive && !Mover.IsArmInputCaptured && Arms.HeldObject == null
                    && !Arms.IsRecycleReady && Arms.CurrentTargetLocal == Vector2.zero && Arms.CurrentInputMagnitude == 0f
                    && !(bool)Get(Arms, "armToggleActive") && !(bool)Get(Arms, "grabArmSession")
                    && (int)Get(Arms, "heldHands") == 0 && Mathf.Abs(Mover.GrabMovementMultiplier - 1f) < .00001f,
                    message + "; ownership/input/state; FPS=" + fps);
                Require(!Frame.Find("Left Mechanical Arm").gameObject.activeSelf
                    && !Frame.Find("Right Mechanical Arm").gameObject.activeSelf,
                    message + "; visible arm roots; FPS=" + fps);
                Require(Get(Arms, "mediumRecycleClip") == null && !(bool)Get(Arms, "recyclePresentationActive")
                    && Arms.MediumRecycleHandVisualOffset == Vector2.zero
                    && Arms.MediumRecycleHandVisualRollDegrees == 0f
                    && Arms.MediumRecycleGarbageVisualOffset == Vector2.zero
                    && Frame.Find("Body Visual").localPosition == Vector3.zero
                    && Frame.Find("Body Visual").localRotation == Quaternion.identity
                    && ((IList)Get(Shake, "mediumRecyclePulses")).Count == 0
                    && (Vector2)Call(Shake, "GetMediumRecycleMotorSpeeds", Time.time) == Vector2.zero
                    && (Vector3)Call(Shake, "GetMediumRecycleCameraSample") == Vector3.zero,
                    message + "; recycle presentation/feedback; FPS=" + fps);
            }
            public void Dispose()
            {
                if (Shake != null)
                { Shake.CancelMediumRecycleFeedback(Arms); Call(Shake, "ClearGarbageGrabFeedback"); }
                if (Root != null) ((IDisposable)artworkFixture).Dispose();
                else if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}

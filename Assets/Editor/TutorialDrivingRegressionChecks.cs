using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Exercises measured driving and the real Toolkit panel in a disposable preview scene.</summary>
    public static class TutorialDrivingRegressionChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static int checks;
        private static readonly List<string> screenshots = new List<string>();

        [Serializable] private sealed class Report
        {
            public bool passed;
            public int checks;
            public string error;
            public string[] screenshots;
        }

        [MenuItem("Animal Game/Validation/Run Tutorial Driving Checks")]
        public static void Run()
        {
            checks = 0;
            screenshots.Clear();
            try
            {
                CheckMeasuredDriving();
                CheckTurning();
                CheckMovingTurns();
                CheckInterruptions();
                CheckContinuousStop();
                foreach (int fps in new[] { 30, 60, 120 }) CheckCourseAtFrameRate(fps);
                CheckPanel();
                WriteReport(true, null);
                Debug.Log("TUTORIAL DRIVING CHECKS PASSED: " + checks + " assertions; measured motion, "
                    + "interruptions, 30/60/120 FPS, private UI resources, font coverage and responsive panel.");
            }
            catch (Exception error)
            {
                WriteReport(false, error.ToString());
                throw;
            }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckMeasuredDriving()
        {
            var p = new DrivingTutorialProgress();
            Require(p.Step == DrivingTutorialStep.Forward && !p.Advance(), "A lesson cannot advance before driving.");
            Feed(p, Vector2.zero, 0, 2, 1, 0, .5f);
            Feed(p, Vector2.up, 0, 2, 1, 0, .5f);
            Near(p.Value, 1, "One actual driven metre counts once.");
            Feed(p, Vector2.up * 2, 0, 2, -1, 0, .5f);
            Near(p.Value, 1, "Pressing reverse to brake while still moving forward is not forward practice.");
            Feed(p, Vector2.up * 3, 0, 2, 0, 0, .5f);
            Near(p.Value, 1, "Coasting with no throttle does not complete forward practice.");
            Feed(p, new Vector2(1, 3), 0, 2, 1, 0, .5f);
            Near(p.Value, 1, "Lateral displacement does not count as forward travel.");
            Feed(p, new Vector2(1, 2), 0, -2, 1, 0, .5f);
            Near(p.Value, 1, "Forward throttle while still reversing is braking, not forward travel.");
            Require(!p.Advance(), "Partial distance cannot advance the lesson.");
            p.Reset();
            Near(p.Value, 0, "Reset clears measured progress.");
            ReachStep(p, DrivingTutorialStep.Reverse);
            Feed(p, Vector2.zero, 0, -2, -1, 0, .5f);
            Feed(p, Vector2.down, 0, -2, -1, 0, .5f);
            Near(p.Value, 1, "Reverse requires negative throttle and real backwards travel.");
            Feed(p, Vector2.zero, 0, 2, -1, 0, .5f);
            Near(p.Value, 1, "Reverse throttle while still rolling forward does not count as reversing.");
            Feed(p, Vector2.down, 0, -2, 0, 0, .5f);
            Near(p.Value, 1, "Backwards coasting does not add reverse distance.");
        }

        private static void CheckTurning()
        {
            var p = new DrivingTutorialProgress();
            ReachStep(p, DrivingTutorialStep.TurnLeft);
            Feed(p, Vector2.zero, 350, 0, 0, -30, 1);
            Feed(p, Vector2.zero, 10, 0, 0, -30, 1);
            Near(p.Value, 20, "Left turning crosses 360 degrees without a large-angle jump.");
            Feed(p, Vector2.zero, 2, 0, 0, 30, 1);
            Near(p.Value, 12, "Turning back the wrong way removes accumulated turn progress.");
            Feed(p, Vector2.zero, 347, 0, 0, 30, 1);
            Near(p.Value, 0, "Wrong-way turning cannot create negative progress.");
            Feed(p, Vector2.zero, 17, 0, 0, -30, 1);
            Near(p.Value, 30, "A real left turn adds positive progress after recovery.");
            Feed(p, Vector2.zero, 37, 1, 1, -30, 1);
            Near(p.Value, 30, "Steering under forward drive does not substitute for the stationary turn lesson.");
            Feed(p, Vector2.zero, 57, 0, 0, 0, 1);
            Near(p.Value, 30, "External rotation with no commanded turn does not count.");
            Satisfy(p);
            Require(p.Advance() && p.Step == DrivingTutorialStep.TurnRight, "Satisfied left turn advances to right turn.");
            Feed(p, Vector2.zero, 10, 0, 0, 30, 1);
            Feed(p, Vector2.zero, 350, 0, 0, 30, 1);
            Near(p.Value, 20, "Right turning also crosses zero correctly.");
            Feed(p, Vector2.zero, 355, 0, 0, -30, 1);
            Near(p.Value, 15, "Left motion removes progress during the right-turn lesson.");
        }

        private static void CheckInterruptions()
        {
            var p = new DrivingTutorialProgress();
            Feed(p, Vector2.zero, 0, 2, 1, 0, .5f);
            Feed(p, Vector2.up, 0, 2, 1, 0, .5f);
            Near(p.Value, 1, "Interruption fixture has real progress.");
            Feed(p, Vector2.up * 20, 0, 2, 1, 0, .5f, false);
            Feed(p, Vector2.up * 21, 0, 2, 1, 0, .5f);
            Near(p.Value, 1, "Blocked/photo/arm/fallen frames and their recovery baseline cannot add travel.");
            Feed(p, Vector2.up * 22, 0, 2, 1, 0, .5f);
            Near(p.Value, 2, "Normal driving resumes after a blocked frame.");
            Feed(p, Vector2.up * 23, 0, 2, 1, 0, 0);
            Feed(p, Vector2.up * 24, 0, 2, 1, 0, .5f);
            Near(p.Value, 2, "Zero-delta pause and the first resumed sample do not add distance.");
            p.Suspend();
            Feed(p, Vector2.up * 25, 0, 2, 1, 0, .5f);
            Near(p.Value, 2, "Explicit suspension preserves completed travel and discards its baseline.");
            Feed(p, Vector2.up * 100, 0, 2, 1, 0, .5f);
            Near(p.Value, 2, "Teleport displacement cannot complete a driving lesson.");
            Feed(p, Vector2.up * 101, 0, 2, 1, 0, .5f);
            Near(p.Value, 3, "Rejected teleport becomes a fresh baseline for subsequent real driving.");
            ReachStep(p, DrivingTutorialStep.TurnLeft);
            Feed(p, Vector2.zero, 0, 0, 0, -30, .1f);
            Feed(p, Vector2.zero, 90, 0, 0, -30, .1f);
            Near(p.Value, 0, "An externally snapped orientation cannot satisfy the turn lesson.");
        }

        private static void CheckMovingTurns()
        {
            var p = MovingTurnFixture();
            Feed(p, Vector2.zero, 0, 0, 0, -30, 1);
            Feed(p, Vector2.zero, 30, 0, 0, -30, 1);
            Vector2 straightEnd = Direction(30) * 2;
            Feed(p, straightEnd, 30, 2, 1, 0, 1);
            RequireNoMovingTurn(p, "An in-place turn followed by straight driving cannot be joined into a moving turn.");

            p = MovingTurnFixture();
            Feed(p, Vector2.zero, 0, 1, 1, -60, .5f);
            Vector2 position = Direction(15) * .5f;
            Feed(p, position, 30, 1, 1, -60, .5f);
            Near(p.Value, 30, "A tight moving arc reaches the angular target.");
            Near(p.MovingTurnDistanceMeters, .5f, "Only distance actually driven while turning counts.");
            Require(!p.IsStepSatisfied && !p.Advance() && p.NormalizedProgress < 1,
                "Reaching the angle alone cannot finish the moving-turn lesson.");
            position += Direction(30) * 2;
            Feed(p, position, 30, 2, 1, 0, 1);
            Near(p.MovingTurnDistanceMeters, .5f, "Straight travel cannot fill a moving arc's missing distance.");
            position += Direction(37.5f) * 1.51f;
            Feed(p, position, 45, 1, 1, -10, 1.5f);
            Require(p.IsStepSatisfied && p.NormalizedProgress == 1 && p.Advance()
                && p.Step == DrivingTutorialStep.Reverse, "Both arc targets unlock the following reverse lesson.");
            Near(p.MovingTurnDistanceMeters, 0, "Advancing resets arc distance for subsequent lesson stages.");

            p = MovingTurnFixture();
            Feed(p, Vector2.zero, 0, 2, 1, -10, 1);
            position = Direction(5) * 2;
            Feed(p, position, 10, 2, 1, -10, 1);
            Near(p.MovingTurnDistanceMeters, 2, "A broad arc reaches its distance target.");
            Near(p.Value, 10, "The broad arc retains its actual angular change.");
            Require(!p.IsStepSatisfied && !p.Advance() && p.NormalizedProgress < 1,
                "Reaching distance alone cannot finish the moving-turn lesson.");
            position += Direction(20) * .5f;
            Feed(p, position, 30, 1, 1, -40, .5f);
            Require(p.IsStepSatisfied, "Continuing the actual arc can satisfy the missing angular target.");

            p = MovingTurnFixture();
            Feed(p, Vector2.zero, 0, 2, 1, 30, 1);
            Feed(p, Direction(-15) * 2.01f, 330, 2, 1, 30, 1);
            Require(p.IsStepSatisfied, "A genuine right-hand moving arc is accepted across the zero-degree heading boundary.");

            foreach (string mode in new[] { "braking", "coasting", "reverse", "no steering", "wrong command", "blocked", "paused", "teleport" })
            {
                p = MovingTurnFixture();
                Feed(p, Vector2.zero, 0, 2, 1, -30, 1);
                float speed = mode == "reverse" ? -2 : 2;
                float throttle = mode == "braking" || mode == "reverse" ? -1 : mode == "coasting" ? 0 : 1;
                float turn = mode == "no steering" ? 0 : mode == "wrong command" ? 30 : -30;
                float dt = mode == "paused" ? 0 : 1;
                Vector2 end = Direction(15) * (mode == "teleport" ? 100 : mode == "reverse" ? -2 : 2);
                Feed(p, end, 30, speed, throttle, turn, dt, mode != "blocked");
                RequireNoMovingTurn(p, "Moving-turn rejection: " + mode + ".");
            }

            p = MovingTurnFixture();
            Feed(p, Vector2.zero, 0, 1, 1, -20, 1);
            position = Direction(10);
            Feed(p, position, 20, 1, 1, -20, 1);
            Near(p.Value, 20, "A simultaneous moving turn earns partial angular progress.");
            Near(p.MovingTurnDistanceMeters, 1, "A simultaneous moving turn earns partial distance progress.");
            p.Suspend();
            Feed(p, Vector2.one * 100, 160, 1, 1, -20, 1);
            Near(p.Value, 20, "Resuming an interrupted arc cannot import an external heading change.");
            Near(p.MovingTurnDistanceMeters, 1, "Resuming an interrupted arc cannot import external travel.");
            p.Reset();
            Near(p.Value, 0, "Restart resets angular progress.");
            Near(p.MovingTurnDistanceMeters, 0, "Restart resets moving-turn distance.");
        }

        private static DrivingTutorialProgress MovingTurnFixture()
        {
            var progress = new DrivingTutorialProgress(movingTurnDegrees: 30, movingTurnMeters: 2);
            ReachStep(progress, DrivingTutorialStep.SteerWhileMoving);
            return progress;
        }

        private static void RequireNoMovingTurn(DrivingTutorialProgress progress, string message)
        {
            Near(progress.Value, 0, message + " Angle stays zero.");
            Near(progress.MovingTurnDistanceMeters, 0, message + " Arc distance stays zero.");
            Require(!progress.IsStepSatisfied && !progress.Advance(), message + " The lesson stays pending.");
        }

        private static void CheckContinuousStop()
        {
            var p = new DrivingTutorialProgress(stopSeconds: .8f);
            ReachStep(p, DrivingTutorialStep.Stop);
            Feed(p, Vector2.zero, 0, 0, 0, 0, .2f);
            Feed(p, Vector2.zero, 0, 0, 0, 0, .2f);
            Feed(p, Vector2.zero, 0, 0, 0, 0, .2f);
            Near(p.Value, .4f, "Stationary samples accumulate elapsed simulation time.");
            Feed(p, Vector2.up * .05f, 0, .25f, 0, 0, .2f);
            Near(p.Value, 0, "Physical movement interrupts the consecutive stop interval.");
            Feed(p, Vector2.up * .05f, 0, 0, 0, 0, .2f);
            Feed(p, Vector2.up * .05f, 0, 0, .2f, 0, .2f);
            Near(p.Value, 0, "Holding throttle resets the stop interval even against an obstruction.");
            Feed(p, Vector2.up * .05f, 0, 0, 0, 0, .2f);
            p.Suspend();
            Near(p.Value, 0, "Opening a modal/pause resets an unfinished consecutive stop interval.");
            Feed(p, Vector2.up * .05f, 0, 0, 0, 0, .2f);
            for (int i = 0; i < 4; i++) Feed(p, Vector2.up * .05f, 0, 0, 0, 0, .2f);
            Require(p.IsStepSatisfied && p.Advance() && p.IsComplete, "Only a complete uninterrupted stop finishes the lesson.");
            Require(!p.Advance(), "Completed lessons cannot advance past the terminal state.");
            Feed(p, Vector2.one * 1000, 90, 20, 1, 30, 1);
            Require(p.IsComplete && p.NormalizedProgress == 1, "Completion remains stable after further world movement.");
        }

        private static void CheckCourseAtFrameRate(int fps)
        {
            var p = new DrivingTutorialProgress(3, 2, 30, .6f);
            float dt = 1f / fps;
            Vector2 position = Vector2.zero;
            float heading = 350;
            foreach (DrivingTutorialStep expected in new[] { DrivingTutorialStep.Forward, DrivingTutorialStep.TurnLeft,
                DrivingTutorialStep.TurnRight, DrivingTutorialStep.SteerWhileMoving, DrivingTutorialStep.Reverse, DrivingTutorialStep.Stop })
            {
                Require(p.Step == expected && !p.Advance(), "An unsatisfied step stays pending at " + fps + " FPS: " + expected);
                bool movingTurn = expected == DrivingTutorialStep.SteerWhileMoving;
                float speed = expected == DrivingTutorialStep.Forward || movingTurn ? 2 : expected == DrivingTutorialStep.Reverse ? -2 : 0;
                float throttle = Mathf.Sign(speed) * (speed == 0 ? 0 : 1);
                float angular = expected == DrivingTutorialStep.TurnLeft || movingTurn ? 30 : expected == DrivingTutorialStep.TurnRight ? -30 : 0;
                Feed(p, position, heading, speed, throttle, -angular, dt);
                int frames = 0;
                while (!p.IsStepSatisfied && frames < fps * 5)
                {
                    position += Direction(heading + angular * dt * .5f) * speed * dt;
                    heading = Mathf.Repeat(heading + angular * dt, 360);
                    Feed(p, position, heading, speed, throttle, -angular, dt);
                    frames++;
                }
                float expectedSeconds = angular != 0 ? p.Target / Mathf.Abs(angular)
                    : speed != 0 ? p.Target / Mathf.Abs(speed) : p.Target;
                if (movingTurn) expectedSeconds = Mathf.Max(expectedSeconds, p.MovingTurnTargetMeters / speed);
                Require(p.IsStepSatisfied, "Real course reaches its target at " + fps + " FPS: " + expected);
                Require(Mathf.Abs(frames * dt - expectedSeconds) <= dt * 2 + .001f,
                    "Course timing remains consistent at " + fps + " FPS: " + expected);
                Require(p.Advance(), "Satisfied course step advances at " + fps + " FPS: " + expected);
            }
            Require(p.IsComplete, "All six measured steps complete at " + fps + " FPS.");
        }

        private static void CheckPanel()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run regression checks in Edit Mode.");
            var shared = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Prefabs/Resources/UI/Main/MainUISetting.asset");
            Require(shared != null, "The existing HUD settings are available.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            string sharedBefore = EditorJsonUtility.ToJson(shared);
            float timeBefore = Time.timeScale;
            TutorialDialoguePanel panel = null;
            RenderTexture texture = null;
            PanelSettings privateSettings = null;
            Font privateFont = null;
            GameObject documentObject = null;
            try
            {
                Time.timeScale = 1;
                var obj = new GameObject("Tutorial panel regression (disposable)");
                SceneManager.MoveGameObjectToScene(obj, scene);
                panel = obj.AddComponent<TutorialDialoguePanel>();
                Set(panel, "panelSettingsTemplate", shared);
                if (panel.Document == null) Invoke(panel, "Awake");
                Require(panel.Document != null && panel.Root != null, "The real tutorial document is created.");
                privateSettings = panel.Document.panelSettings;
                privateFont = Get<Font>(panel, "runtimeFont");
                documentObject = Get<GameObject>(panel, "documentObject");
                Require(privateSettings != shared && privateSettings.targetTexture == null,
                    "Tutorial settings are private and do not inherit the HUD render target.");
                texture = new RenderTexture(1920, 1080, 24);
                texture.Create();
                privateSettings.targetTexture = texture;
                VisualElement root = panel.Root;
                Require(root.pickingMode == PickingMode.Ignore, "The full-screen tutorial root does not intercept world input.");
                Require(root.Q("TutorialPanel") != null && root.Q("CollapseTutorial") != null && root.Q("ExpandTutorial") != null,
                    "The card and both actual fold controls are present.");
                VisualElement header = root.Q("CollapseTutorial");
                Require(header.Q<Label>("TutorialHeader")?.text == "TUTORIAL", "The header identifies the tutorial directly.");
                Require(header.Query<Label>().ToList().Count == 2
                    && header.Query<VisualElement>().ToList().TrueForAll(element => element.GetType().Name != "Waveform"),
                    "Only the tutorial title and folding arrow remain in the header.");
                Require(header.Query<Label>().ToList().TrueForAll(label =>
                    label.text.IndexOf("Patrol", StringComparison.OrdinalIgnoreCase) < 0
                    && label.text.IndexOf("Link", StringComparison.OrdinalIgnoreCase) < 0),
                    "The header has no former support identity or connection subtitle.");
                Require(root.Query<Label>().ToList().TrueForAll(label => label.pickingMode == PickingMode.Ignore),
                    "Text and readouts do not steal pointer picking.");
                Settle(panel, new Vector2(1920, 1080));
                CheckVisibleEnglishText(root, "Initial expanded panel");
                foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720),
                    new Vector2(1440, 1080), new Vector2(2560, 1080) })
                {
                    Settle(panel, size);
                    Rect card = root.Q("TutorialPanel").worldBound;
                    float fit = Mathf.Min(size.x / 1920, size.y / 1080);
                    Require(card.width > 250 * fit && card.width < 400 * fit,
                        "Card remains compact at " + size);
                    Require(card.xMin > size.x * .7f && card.xMax < size.x - 25 * fit,
                        "Card has right-edge breathing room and stays on the right at " + size);
                    Require(card.yMin > 180 * fit && card.yMax < size.y - 100 * fit,
                        "Card avoids top instruments and bottom coordinates at " + size);
                    Require(!Within(root, root.panel.Pick(size * .5f)),
                        "The central game view is not intercepted at " + size);
                    CheckVisibleEnglishText(root, "Expanded panel at " + size);
                    panel.SetCollapsed(true);
                    Settle(panel, size);
                    Rect tab = root.Q("ExpandTutorial").worldBound;
                    Require(panel.IsCollapsed && root.Q("TutorialPanel").resolvedStyle.display == DisplayStyle.None,
                        "Collapsing removes the full card at " + size);
                    Require(tab.width < card.width * .25f && tab.xMin > card.xMin && tab.xMax < size.x,
                        "Collapsed tab is narrow and stays within the right edge at " + size);
                    CheckVisibleEnglishText(root, "Collapsed panel at " + size);
                    panel.SetCollapsed(false);
                    Settle(panel, size);
                    Require(root.Q("TutorialPanel").resolvedStyle.display == DisplayStyle.Flex,
                        "Expanding restores the card at " + size);
                }

                Settle(panel, new Vector2(1920, 1080));
                var lesson = obj.AddComponent<TutorialDrivingLesson>();
                var progress = new DrivingTutorialProgress();
                Set(lesson, "panel", panel);
                Set(lesson, "progress", progress);
                for (int step = 0; step <= (int)DrivingTutorialStep.Completed; step++)
                {
                    Invoke(lesson, "RefreshPanel", true);
                    Settle(panel, new Vector2(1920, 1080));
                    CheckVisibleEnglishText(root, "Lesson " + progress.Step);
                    Invoke(lesson, "RefreshPanel", false);
                    Settle(panel, new Vector2(1920, 1080));
                    CheckVisibleEnglishText(root, "Blocked lesson " + progress.Step);
                    Set(lesson, "feedbackRemaining", .5f);
                    string feedbackName = (string)typeof(TutorialDrivingLesson)
                        .GetMethod("StepName", Flags | BindingFlags.Static).Invoke(null, new object[] { progress.Step });
                    Set(lesson, "lastFeedback", feedbackName + " complete");
                    Invoke(lesson, "RefreshPanel", true);
                    Settle(panel, new Vector2(1920, 1080));
                    CheckVisibleEnglishText(root, "Completion feedback " + progress.Step);
                    Set(lesson, "feedbackRemaining", 0f);
                    Invoke(lesson, "RefreshPanel", true);
                    Settle(panel, new Vector2(1920, 1080));
                    if (step == 0 || progress.IsComplete)
                        Capture(root, texture, progress.IsComplete ? "driving-completed.png" : "driving-expanded.png");
                    if (!progress.IsComplete) { Satisfy(progress); Require(progress.Advance(), "UI fixture advances through actual lesson steps."); }
                }
                Click(root.Q("CollapseTutorial"));
                Require(panel.IsCollapsed, "Clicking the real header collapses the tutorial.");
                Settle(panel, new Vector2(1920, 1080));
                CheckVisibleEnglishText(root, "Clicked collapsed tab");
                Capture(root, texture, "driving-collapsed.png");
                panel.SetContent("Driving Practice", "New instruction", "RT", "Hold RT", "0 / 5 m", "Waiting", 1, 8);
                Require(panel.IsCollapsed, "Replacing content preserves the user's folded preference.");
                Click(root.Q("ExpandTutorial"));
                Require(!panel.IsCollapsed, "Clicking the actual tab expands the tutorial.");
                Settle(panel, new Vector2(1920, 1080));
                CheckVisibleEnglishText(root, "Updated expanded content");
                panel.SetProgress(2);
                Require(Get<Label>(panel, "percentLabel").text == "100%", "Progress accepts a clamped upper bound.");
                panel.SetProgress(-1);
                Require(Get<Label>(panel, "percentLabel").text == "0%", "Progress accepts a clamped lower bound.");
                panel.SetVisible(false); Layout(root);
                Require(root.resolvedStyle.display == DisplayStyle.None, "Explicit hiding removes the tutorial from display.");
                panel.SetVisible(true); Time.timeScale = 0; Invoke(panel, "LateUpdate"); Layout(root);
                Require(root.resolvedStyle.display == DisplayStyle.None, "Pausing hides the overlay without changing gameplay time.");
                Time.timeScale = 1; Invoke(panel, "LateUpdate"); Layout(root);
                Require(root.resolvedStyle.display == DisplayStyle.Flex, "The overlay returns when simulation resumes.");
                Require(EditorJsonUtility.ToJson(shared) == sharedBefore,
                    "Screen sizing, folding and capture never modify shared HUD PanelSettings.");
            }
            finally
            {
                // Runtime teardown uses Destroy; explicitly own the disposable resources in Edit Mode.
                if (panel != null)
                {
                    Set(panel, "documentObject", null); Set(panel, "runtimeSettings", null); Set(panel, "runtimeFont", null);
                }
                if (documentObject != null) Object.DestroyImmediate(documentObject);
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                if (privateSettings != null) Object.DestroyImmediate(privateSettings);
                if (privateFont != null) Object.DestroyImmediate(privateFont);
                Time.timeScale = timeBefore;
            }
        }

        private static void CheckVisibleEnglishText(VisualElement root, string context)
        {
            CheckToggleHelp(root, context);
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                if (!Visible(element, root)) continue;
                if (element is Label label)
                {
                    Require(!ContainsHan(label.text), context + " uses English labels: " + label.text);
                    CheckTextFits(label, context + " / " + (string.IsNullOrEmpty(label.name) ? label.text : label.name));
                }
                if (string.IsNullOrWhiteSpace(element.tooltip)) continue;
                Require(!ContainsHan(element.tooltip), context + " uses an English tooltip: " + element.tooltip);
                // Toolkit tooltips size themselves rather than using an authored rectangle. Keep
                // their measured content compact enough to fit beside the tutorial card.
                Label metrics = root.Q<Label>("TutorialDetail");
                Vector2 tooltipSize = metrics.MeasureTextSize(element.tooltip, 0, VisualElement.MeasureMode.Undefined,
                    0, VisualElement.MeasureMode.Undefined);
                Require(tooltipSize.x <= 360 && tooltipSize.y <= 96,
                    context + " has a compact tooltip: " + element.tooltip + " / " + tooltipSize);
            }
            CheckFont(root);
        }

        private static void CheckToggleHelp(VisualElement root, string context)
        {
            VisualElement card = root.Q("TutorialPanel");
            if (Visible(card, root))
            {
                Label hint = root.Q<Label>("TutorialToggleHint");
                Require(hint != null && Visible(hint, root) && hint.text == "Tap D-pad ← / →  Hide",
                    context + " always displays both D-pad directions in its dedicated hide-help row.");
                Label status = root.Q<Label>("TutorialStatus");
                Require(hint.parent == card && hint != status && hint.worldBound.yMin >= status.worldBound.yMax,
                    context + " keeps input help separate from changing status and completion feedback.");
            }
            VisualElement tab = root.Q("ExpandTutorial");
            if (Visible(tab, root))
            {
                Label arrows = root.Q<Label>("TutorialTabToggleArrows");
                Label hint = root.Q<Label>("TutorialTabToggleHint");
                Require(arrows != null && Visible(arrows, root) && arrows.text == "← →",
                    context + " preserves both directions while the card is hidden.");
                Require(hint != null && Visible(hint, root) && hint.text == "Show",
                    context + " explains that the hidden card can be shown again.");
            }
        }

        private static bool Visible(VisualElement element, VisualElement root)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility != Visibility.Visible)
                    return false;
                if (current == root) return true;
            }
            return false;
        }

        private static bool ContainsHan(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char character in text)
                if ((character >= '\u3400' && character <= '\u9fff')
                    || (character >= '\uf900' && character <= '\ufaff')) return true;
            return false;
        }

        private static void CheckFont(VisualElement root)
        {
            Font font = root.resolvedStyle.unityFontDefinition.font;
            if (font == null) font = root.resolvedStyle.unityFont;
            Require(font != null, "The tutorial resolves an actual font.");
            var characters = new HashSet<char>();
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                if (!Visible(element, root)) continue;
                string shown = (element is Label label ? label.text : string.Empty) + element.tooltip;
                foreach (char character in shown)
                    if (!char.IsWhiteSpace(character) && !char.IsControl(character)) characters.Add(character);
            }
            var text = new StringBuilder(); foreach (char character in characters) text.Append(character);
            font.RequestCharactersInTexture(text.ToString(), 28, FontStyle.Normal);
            foreach (char character in characters)
                Require(font.HasCharacter(character), "Tutorial font contains visible glyph '" + character + "' (U+"
                    + ((int)character).ToString("X4") + ").");
        }

        private static void CheckTextFits(Label label, string name)
        {
            Require(label != null && !string.IsNullOrWhiteSpace(label.text), name + " is populated.");
            Vector2 measured = label.MeasureTextSize(label.text, label.layout.width, VisualElement.MeasureMode.AtMost,
                0, VisualElement.MeasureMode.Undefined);
            Require(measured.y <= label.layout.height + 2, name + " fits its text height: " + measured + " in " + label.layout.size);
            if (label.resolvedStyle.whiteSpace == WhiteSpace.NoWrap)
            {
                Vector2 natural = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined,
                    0, VisualElement.MeasureMode.Undefined);
                Require(natural.x <= label.layout.width + 2,
                    name + " fits without horizontal overflow: " + natural + " in " + label.layout.size);
            }
        }

        private static void Settle(TutorialDialoguePanel panel, Vector2 size)
        {
            panel.Root.style.width = size.x; panel.Root.style.height = size.y;
            for (int i = 0; i < 3; i++) { Layout(panel.Root); Invoke(panel, "LateUpdate"); }
            Layout(panel.Root);
        }

        private static void Capture(VisualElement root, RenderTexture target, string name)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = target; GL.Clear(true, true, new Color(.035f, .055f, .055f));
                Type panelType = root.panel.GetType();
                // RuntimePanel.Update applies the changed private PanelSettings and pumps bindings/
                // scheduled work. ValidateLayout alone never synchronizes its render target.
                panelType.GetMethod("Update", Flags, null, Type.EmptyTypes, null).Invoke(root.panel, null);
                root.MarkDirtyRepaint();
                for (int pass = 0; pass < 2; pass++)
                {
                    panelType.GetMethod("Repaint", Flags, null, new[] { typeof(Event) }, null)
                        .Invoke(root.panel, new object[] { new Event { type = EventType.Repaint } });
                    panelType.GetMethod("Render", Flags, null, Type.EmptyTypes, null).Invoke(root.panel, null);
                }
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                int foreground = 0;
                foreach (Color32 pixel in image.GetPixels32())
                    if (pixel.r > 120 || pixel.g > 120 || pixel.b > 120) foreground++;
                Require(foreground > 200, "Capture contains actual tutorial linework/text, not an empty background: "
                    + name + " (foreground pixels " + foreground + ").");
                string path = Path.Combine(OutputDirectory(), name);
                File.WriteAllBytes(path, image.EncodeToPNG()); screenshots.Add(path);
            }
            finally { if (image != null) Object.DestroyImmediate(image); RenderTexture.active = previous; }
        }

        private static bool Within(VisualElement root, VisualElement element)
        {
            for (VisualElement item = element; item != null; item = item.parent) if (item == root) return true;
            return false;
        }

        private static void Click(VisualElement element)
        {
            using (var click = ClickEvent.GetPooled())
            {
                // A directly pooled pointer event has no hit target or position. SendEvent does not
                // imply that its receiver was hit; target it explicitly as the runtime picker would.
                click.target = element;
                element.SendEvent(click);
            }
        }

        private static void ReachStep(DrivingTutorialProgress progress, DrivingTutorialStep step)
        {
            while (progress.Step < step) { Satisfy(progress); Require(progress.Advance(), "Fixture reaches " + step); }
            Require(progress.Step == step, "Fixture is at " + step);
        }

        private static void Satisfy(DrivingTutorialProgress progress)
        {
            progress.Suspend();
            switch (progress.Step)
            {
                case DrivingTutorialStep.Forward:
                    Feed(progress, Vector2.zero, 0, 5, 1, 0, 1);
                    Feed(progress, Vector2.up * progress.Target, 0, 5, 1, 0, 1); break;
                case DrivingTutorialStep.TurnLeft:
                case DrivingTutorialStep.TurnRight:
                    float sign = progress.Step == DrivingTutorialStep.TurnLeft ? 1 : -1;
                    Feed(progress, Vector2.zero, 0, 0, 0, -90 * sign, 1);
                    Feed(progress, Vector2.zero, progress.Target * sign, 0, 0, -90 * sign, 1); break;
                case DrivingTutorialStep.SteerWhileMoving:
                    Feed(progress, Vector2.zero, 0, progress.MovingTurnTargetMeters, 1, -progress.Target, 1);
                    Feed(progress, Direction(progress.Target * .5f) * (progress.MovingTurnTargetMeters + .01f),
                        progress.Target, progress.MovingTurnTargetMeters, 1, -progress.Target, 1); break;
                case DrivingTutorialStep.Reverse:
                    Feed(progress, Vector2.zero, 0, -5, -1, 0, 1);
                    Feed(progress, Vector2.down * progress.Target, 0, -5, -1, 0, 1); break;
                case DrivingTutorialStep.Stop:
                    Feed(progress, Vector2.zero, 0, 0, 0, 0, 1);
                    Feed(progress, Vector2.zero, 0, 0, 0, 0, 1); break;
            }
            Require(progress.IsStepSatisfied, "Fixture satisfies " + progress.Step);
        }

        private static Vector2 Direction(float heading)
        {
            float radians = heading * Mathf.Deg2Rad;
            return new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
        }

        private static void Feed(DrivingTutorialProgress progress, Vector2 position, float heading,
            float speed, float throttle, float turn, float dt, bool canDrive = true) =>
            progress.Sample(new DrivingTutorialSample(position, Direction(heading), heading, speed, throttle, turn, dt, canDrive));

        private static void Layout(VisualElement root) => root.panel.GetType().GetMethod("ValidateLayout", Flags).Invoke(root.panel, null);
        private static void Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
        private static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Flags).GetValue(obj);
        private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
        private static void Near(float actual, float expected, string message) => Require(Mathf.Abs(actual - expected) < .002f,
            message + " (actual " + actual + ", expected " + expected + ")");
        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static string OutputDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("TUTORIAL_DRIVING_VALIDATION_OUTPUT");
            string directory = string.IsNullOrEmpty(configured) ? Path.GetFullPath("output/validation/tutorial-driving") : configured;
            Directory.CreateDirectory(directory); return directory;
        }

        private static void WriteReport(bool passed, string error) => File.WriteAllText(Path.Combine(OutputDirectory(), "regression-report.json"),
            JsonUtility.ToJson(new Report { passed = passed, checks = checks, error = error, screenshots = screenshots.ToArray() }, true));
    }
}

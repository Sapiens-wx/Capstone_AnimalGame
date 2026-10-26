using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Edit-mode state, geometry and actual Toolkit text checks; does not enter Play Mode.</summary>
    public static class TutorialTerrainRegressionChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Vector2 Survey = new Vector2(20f, 10f);
        private static readonly Vector2 Destination = new Vector2(20f, 30f);
        private static readonly List<string> screenshots = new List<string>();
        private static int checks, geometryChecks, textChecks;

        [Serializable] private sealed class Report
        {
            public bool passed;
            public int checks;
            public int geometryChecks;
            public int textChecks;
            public string error;
            public string[] screenshots;
        }

        [MenuItem("Animal Game/Validation/Run Tutorial Terrain Checks")]
        public static void Run()
        {
            checks = geometryChecks = textChecks = 0;
            screenshots.Clear();
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run terrain checks in Edit Mode.");
                CheckManualContinueGates();
                CheckForgivingArrivalDefaults();
                CheckOrderAndApproach();
                CheckPositionAndScanGates();
                CheckReadingGates();
                CheckAcknowledgedScanNeverReturns();
                CheckWaypointGeometry();
                CheckAssetWiring();
                CheckTerrainPanel();
                WriteReport(true, null);
                Debug.Log("TUTORIAL TERRAIN CHECKS PASSED: " + checks + " assertions; " + geometryChecks
                    + " waypoint geometry and " + textChecks + " actual Toolkit text assertions.");
            }
            catch (Exception error) { WriteReport(false, error.ToString()); throw; }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static TerrainTutorialProgress Fixture() =>
            new TerrainTutorialProgress(new[] { new Vector2(10, 10), Survey }, Destination, 2, 6, 4);

        private static void Feed(TerrainTutorialProgress progress, Vector2 position, float dt = 1,
            bool valid = true, bool expanded = true, bool markers = true, bool stopped = true)
        {
            progress.Sample(position, dt, valid, expanded, markers, stopped);
            if (progress.CanContinue && !progress.IsComplete) progress.Continue();
        }

        // Outcome fixtures explicitly confirm each goal; the dedicated check below never does.
        private static bool Scan(TerrainTutorialProgress progress, Vector2 position, bool valid)
        {
            bool accepted = progress.RegisterTerrainScan(position, valid);
            if (accepted && progress.CanContinue && !progress.IsComplete) progress.Continue();
            return accepted;
        }

        private static void CheckManualContinueGates()
        {
            var progress = new TerrainTutorialProgress(new[] { Survey }, Destination, 2, 6, 4);
            Require(!progress.CanContinue && !progress.Continue(), "A cannot skip an unvisited destination.");
            progress.Sample(Survey, 1f, true, true, true, true);
            Require(progress.Step == TerrainTutorialStep.ReachSurvey && progress.CanContinue && !progress.HasTarget,
                "Arrival hides the reached marker but waits on the same instruction for A.");
            progress.Sample(Survey, 20f, true, true, true, true);
            Require(progress.Step == TerrainTutorialStep.ReachSurvey && progress.Continue(),
                "Waiting cannot advance arrival; a confirmation advances exactly one instruction.");
            Require(progress.Step == TerrainTutorialStep.ScanTerrain && !progress.Continue(), "A cannot skip scanning.");
            Require(progress.RegisterTerrainScan(Survey, true) && progress.Step == TerrainTutorialStep.ScanTerrain
                && progress.CanContinue && !progress.HasAcknowledgedScan, "A native terrain scan is latched without replacing its instruction or inventing confirmation.");
            Require(progress.Continue() && progress.Step == TerrainTutorialStep.ReadSigns && progress.HasAcknowledgedScan,
                "Only confirming the accepted scan reveals its legend.");
            progress.Sample(Survey, 4f, true, true, true, true);
            Require(progress.Step == TerrainTutorialStep.ReadSigns && progress.CanContinue,
                "Finished legend reading waits for A instead of automatically showing the route.");
            progress.RequireFreshScan();
            Require(progress.Step == TerrainTutorialStep.ReadSigns && progress.CanContinue,
                "A completed reading stays latched while waiting for confirmation.");
            Require(progress.Continue() && progress.Step == TerrainTutorialStep.FollowRoute,
                "Confirming reading starts the route instruction.");
            progress.Sample(Destination, 1f, true, true, true, true);
            Require(progress.Step == TerrainTutorialStep.FollowRoute && progress.CanContinue && !progress.IsComplete,
                "The final arrival also waits for A.");
            Require(progress.Continue() && progress.IsComplete && progress.CanContinue && !progress.Continue(),
                "The internal completion state is terminal and requires no separate confirmation.");
        }

        private static void CheckForgivingArrivalDefaults()
        {
            var progress = new TerrainTutorialProgress(new[] { Survey }, Destination);
            Near(progress.ArrivalRadiusMeters, 4f, "Default navigation accepts a four-metre arrival area.");
            Near(progress.SurveyRadiusMeters, 8f, "Sign reading and scan practice use the wider eight-metre survey area.");
            progress.Sample(Survey + Vector2.right * 4.01f, 1f, true, true, false, true);
            Require(!progress.CanContinue && progress.HasTarget, "A point outside the larger arrival boundary remains pending.");
            progress.Sample(Survey + Vector2.right * 3.9f, 1f, true, true, false, true);
            Require(progress.Step == TerrainTutorialStep.ReachSurvey && progress.CanContinue && !progress.HasTarget,
                "A near miss that used to require precise parking now clears navigation but still waits for A.");
            Require(!progress.RegisterTerrainScan(Survey, true), "The wider arrival cannot skip the navigation confirmation.");
            Require(progress.Continue() && progress.Step == TerrainTutorialStep.ScanTerrain,
                "The wider approach still confirms into the separate scan task.");
            Require(!progress.RegisterTerrainScan(Survey + Vector2.right * 8.01f, true),
                "The wider survey still rejects scans beyond its real boundary.");
            Require(progress.RegisterTerrainScan(Survey + Vector2.right * 8f, true),
                "A genuine scan at the new survey boundary is accepted.");
            Require(progress.Continue() && progress.Step == TerrainTutorialStep.ReadSigns,
                "Scan completion still waits for confirmation before sign reading.");
            progress.Sample(Survey + Vector2.right * 7.9f, 4f, true, true, true, true);
            Require(progress.CanContinue && progress.Step == TerrainTutorialStep.ReadSigns,
                "Real reading remains eligible near the wider survey edge and still waits for A.");
            Require(progress.Continue() && progress.Step == TerrainTutorialStep.FollowRoute,
                "Reading confirmation starts the route without changing its destination.");
            progress.Sample(Destination + Vector2.right * 3.9f, 1f, true, true, true, true);
            Require(progress.CanContinue && !progress.IsComplete && !progress.HasTarget,
                "The final route accepts the same generous arrival radius without automatically completing the chapter.");
        }

        private static TerrainTutorialProgress At(TerrainTutorialStep step)
        {
            var progress = Fixture();
            if (step == TerrainTutorialStep.ReachSurvey) return progress;
            Feed(progress, new Vector2(10, 10)); Feed(progress, Survey);
            if (step == TerrainTutorialStep.ScanTerrain) return progress;
            Require(Scan(progress, Survey, true), "Fixture requests a real eligible terrain scan.");
            if (step == TerrainTutorialStep.ReadSigns) return progress;
            Feed(progress, Survey, 4);
            if (step == TerrainTutorialStep.FollowRoute) return progress;
            Feed(progress, Destination);
            Require(progress.IsComplete, "Fixture reaches the completed terrain state in order.");
            return progress;
        }

        private static void CheckOrderAndApproach()
        {
            var progress = Fixture();
            Require(progress.Step == TerrainTutorialStep.ReachSurvey && progress.HasTarget,
                "Terrain begins with an approach destination.");
            Require(progress.ApproachIndex == 0 && progress.CurrentTarget == new Vector2(10, 10),
                "The first approach point is the first target.");
            Feed(progress, Destination, 20);
            Require(progress.ApproachIndex == 0 && progress.Step == TerrainTutorialStep.ReachSurvey,
                "Driving directly to the final destination cannot skip the approach or scan lessons.");
            Feed(progress, Survey);
            Require(progress.ApproachIndex == 0, "The survey point cannot skip an earlier approach point.");
            Require(!Scan(progress, Survey, true), "Scanning early cannot skip the approach sequence.");
            Feed(progress, new Vector2(10, 10));
            Require(progress.ApproachIndex == 1 && progress.CurrentTarget == Survey && progress.HasTarget,
                "Reaching one approach point selects the next one.");
            Feed(progress, new Vector2(10, 10));
            Require(progress.ApproachIndex == 1 && progress.Step == TerrainTutorialStep.ReachSurvey,
                "Remaining at the previous point cannot complete the next point.");
            Feed(progress, Survey);
            Require(progress.Step == TerrainTutorialStep.ScanTerrain && !progress.HasTarget,
                "Arriving at the survey removes its destination marker and requests scanning.");
            Feed(progress, Survey, 100);
            Require(progress.Step == TerrainTutorialStep.ScanTerrain && progress.ReadingSeconds == 0,
                "Waiting without an eligible scan cannot start reading.");
            Require(Scan(progress, Survey, true), "A scan at the survey starts sign reading.");
            Require(!progress.HasTarget && progress.Step == TerrainTutorialStep.ReadSigns,
                "No destination triangle obscures the sign-reading stage.");
            Feed(progress, Destination, 1, false);
            Require(progress.Step == TerrainTutorialStep.ReadSigns, "Invalid movement cannot jump to the finish.");
            Feed(progress, Survey, 4);
            Require(progress.Step == TerrainTutorialStep.FollowRoute && progress.HasTarget
                && progress.CurrentTarget == Destination, "Only completed sign reading enables the gentle-route destination.");
            Feed(progress, Survey);
            Require(!progress.IsComplete, "The final route needs an actual arrival.");
            Feed(progress, Destination);
            Require(progress.IsComplete && !progress.HasTarget && progress.NormalizedProgress == 1,
                "Reaching the final destination completes the chapter and removes the marker.");
            Feed(progress, Vector2.one * 1000, 100);
            Require(progress.IsComplete && !Scan(progress, Survey, true),
                "Completion is stable and cannot be reopened by later scans.");

            Vector2[] points = { new Vector2(5, 5), Survey };
            var cloned = new TerrainTutorialProgress(points, Destination);
            points[0] = Vector2.one * 999;
            Require(cloned.CurrentTarget == new Vector2(5, 5), "Approach configuration is copied rather than shared.");
            Require(new TerrainTutorialProgress(null, Destination).CurrentTarget == Vector2.zero,
                "An empty approach has a safe zero-point fallback.");
        }

        private static void CheckPositionAndScanGates()
        {
            foreach (float dt in new[] { 0f, -1f, float.NaN })
            {
                var progress = Fixture();
                Feed(progress, new Vector2(10, 10), dt);
                Require(progress.ApproachIndex == 0 && progress.Step == TerrainTutorialStep.ReachSurvey,
                    "Paused, negative or invalid simulation time does not accept arrival: " + dt);
            }
            foreach (Vector2 position in new[] { new Vector2(float.NaN, 10), new Vector2(10, float.PositiveInfinity) })
            {
                var progress = Fixture();
                Feed(progress, position);
                Require(progress.ApproachIndex == 0 && progress.Step == TerrainTutorialStep.ReachSurvey,
                    "Non-finite positions cannot accept an approach point.");
            }
            var p = Fixture();
            Feed(p, new Vector2(10, 10), 1, false);
            Require(p.ApproachIndex == 0, "Invalid driving frames cannot accept a location.");
            p = At(TerrainTutorialStep.ScanTerrain);
            Require(!Scan(p, Survey, false), "Paused/modal/invalid driving scans are rejected.");
            Require(!Scan(p, Survey + Vector2.right * 6.1f, true), "Scans outside the survey radius are rejected.");
            Require(!Scan(p, new Vector2(float.NaN, 0), true), "Invalid scan positions are rejected.");
            Require(p.Step == TerrainTutorialStep.ScanTerrain, "Rejected scans leave the lesson pending.");
            Require(Scan(p, Survey + Vector2.right * 6f, true), "A valid scan on the survey boundary is accepted.");
        }

        private static void CheckReadingGates()
        {
            foreach (string blocked in new[] { "collapsed", "invalid", "paused" })
            {
                var p = At(TerrainTutorialStep.ReadSigns);
                Feed(p, Survey, blocked == "paused" ? 0 : 10, blocked != "invalid", blocked != "collapsed",
                    false, false);
                Require(p.Step == TerrainTutorialStep.ReadSigns && p.ReadingSeconds == 0,
                    "Acknowledged sign reading still requires an expanded panel and valid running gameplay: " + blocked);
            }
            var progress = At(TerrainTutorialStep.ReadSigns);
            Feed(progress, Survey, 1);
            Near(progress.ReadingSeconds, 1, "Visible reading adds actual simulation time.");
            Feed(progress, Survey, 20, true, false);
            Feed(progress, Survey, 20, false, true, false);
            Near(progress.ReadingSeconds, 1, "Interrupted reading preserves credit without adding blocked time.");
            Require(progress.Step == TerrainTutorialStep.ReadSigns, "Partial reading does not unlock the route.");
            Feed(progress, Survey + Vector2.right * 30f, 3, true, true, false, false);
            Require(progress.Step == TerrainTutorialStep.FollowRoute,
                "Reading a confirmed scan while moving outside the survey still unlocks the route even after its markers disappear.");
            Near(progress.ReadingSeconds, 4, "Reading credit is clamped to its configured duration.");
            Require(!Scan(progress, Survey, true), "Later scans cannot force route progress back into reading.");
        }

        private static void CheckAcknowledgedScanNeverReturns()
        {
            var progress = At(TerrainTutorialStep.ReadSigns);
            Feed(progress, Survey, 1.5f);
            progress.RequireFreshScan();
            Require(progress.Step == TerrainTutorialStep.ReadSigns && progress.HasAcknowledgedScan && !progress.HasTarget,
                "The former snapshot-expiry request cannot reopen a scan that the player confirmed with A.");
            Near(progress.ReadingSeconds, 1.5f, "Snapshot expiry preserves time already spent reading.");
            Feed(progress, Survey, 0f, markers: false);
            Feed(progress, Survey, 100f, valid: false, markers: false);
            Feed(progress, Survey, 100f, expanded: false, markers: false);
            Near(progress.ReadingSeconds, 1.5f, "Pausing, other modes and a collapsed card add no reading time and preserve the scan acknowledgement.");
            for (int frame = 0; frame < 60; frame++)
            {
                progress.RequireFreshScan();
                progress.Sample(Survey + Vector2.right * 30f, 1f / 60f, true, true, false, false);
                Require(progress.Step == TerrainTutorialStep.ReadSigns && progress.HasAcknowledgedScan,
                    "Expired/replaced markers never require a repeated scan during ordinary exploration.");
            }
            Require(progress.Step == TerrainTutorialStep.ReadSigns && !progress.HasTarget,
                "Leaving the survey preserves the acknowledged scan without a forced return task.");
            Near(progress.ReadingSeconds, 2.5f, "Reading continues after acknowledgement without requiring live scan markers.");
            Require(progress.RegisterTerrainScan(Survey, true), "The player may voluntarily scan again while reading.");
            Near(progress.ReadingSeconds, 2.5f, "A voluntary rescan neither repeats a card nor resets its reading credit.");
            progress.Sample(Survey, 2f, true, true, false, false);
            Require(progress.Step == TerrainTutorialStep.ReadSigns && progress.CanContinue,
                "The completed reading still waits for its own A confirmation.");
            Require(progress.Continue() && progress.Step == TerrainTutorialStep.FollowRoute,
                "Only the reading confirmation starts the route, without another scan.");
            progress.RequireFreshScan();
            Require(progress.Step == TerrainTutorialStep.FollowRoute, "Expiry cannot reset a route that has already unlocked.");

            var fresh = At(TerrainTutorialStep.ScanTerrain);
            Require(!fresh.HasAcknowledgedScan && !fresh.Continue(), "An unfinished initial scan cannot be skipped with A.");
            fresh.RequireFreshScan();
            Require(fresh.Step == TerrainTutorialStep.ScanTerrain && !fresh.HasAcknowledgedScan,
                "Display refreshes cannot supply a scan or acknowledge it on the player's behalf.");
            Require(fresh.RegisterTerrainScan(Survey, true) && !fresh.HasAcknowledgedScan,
                "The actual scan is recorded once but still needs the player's A.");
            fresh.Sample(Survey, 100f, true, true, false, false);
            Require(fresh.Step == TerrainTutorialStep.ScanTerrain && fresh.CanContinue && !fresh.HasAcknowledgedScan,
                "Losing markers before A cannot discard the performed scan or auto-confirm it.");
            Require(fresh.Continue() && fresh.HasAcknowledgedScan && fresh.Step == TerrainTutorialStep.ReadSigns,
                "A confirms that same performed scan even when its transient markers have already gone.");
        }

        private static void CheckWaypointGeometry()
        {
            int before = checks;
            Vector2 center = new Vector2(800, 520);
            var hover = Waypoint(center, center);
            Require(hover.Mode == TutorialWaypointIndicator.WaypointMode.InView, "Center waypoint is in view.");
            Require(hover.ScreenPosition == center + new Vector2(0, 24), "A downward triangle hovers seven pixels above its destination.");
            var high = Waypoint(center, center, 400, 7);
            Require(high.ScreenPosition == center + new Vector2(0, 31) && high.RotationDegrees == 0,
                "Bouncing keeps the triangle above its destination and pointing vertically down.");
            for (int degrees = 0; degrees < 360; degrees++)
            {
                float radians = degrees * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                var edge = Waypoint(center + direction * 1000, center);
                Require(edge.Mode == TutorialWaypointIndicator.WaypointMode.Edge, "Far targets remain on the edge at " + degrees);
                float angle = edge.RotationDegrees * Mathf.Deg2Rad;
                Vector2 pointed = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
                Require(Vector2.Dot(pointed, direction) > .9999f, "Edge triangle points at its target at " + degrees);
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    {
                        Vector2 corner = new Vector2(x * 12, y * 17);
                        Vector2 rotated = new Vector2(corner.x * Mathf.Cos(angle) - corner.y * Mathf.Sin(angle),
                            corner.x * Mathf.Sin(angle) + corner.y * Mathf.Cos(angle));
                        Require((edge.ScreenPosition + rotated - center).magnitude <= 394.001f,
                            "The complete rotated icon is within the padded ring at " + degrees);
                    }
            }
            Vector2 nearTop = center + new Vector2(0, 370);
            for (int phase = 0; phase <= 7; phase++)
                Require(Waypoint(nearTop, center, 400, phase).Mode == TutorialWaypointIndicator.WaypointMode.Edge,
                    "The full bounce envelope determines the boundary mode, not the current phase.");
            var normal = Waypoint(center + new Vector2(1000, 200), center);
            Vector2 shifted = new Vector2(650, 330);
            var scaled = TutorialWaypointIndicator.CalculateLayout(shifted + new Vector2(500, 100), shifted, 200,
                new Vector2(12, 17), 3.5f, 0, 3.5f, 3);
            Require((scaled.ScreenPosition - shifted - (normal.ScreenPosition - center) * .5f).magnitude < .001f,
                "A shifted, scaled HUD ring produces a correctly shifted, scaled waypoint.");
            Require(Waypoint(center + Vector2.right * 100, center, 400, 0, false).Mode == TutorialWaypointIndicator.WaypointMode.Edge,
                "A behind-camera target cannot appear as an in-view waypoint.");
            Require(Waypoint(new Vector2(float.NaN, 0), center).Mode == TutorialWaypointIndicator.WaypointMode.Hidden,
                "Invalid waypoint positions are hidden.");
            Require(Waypoint(center, center, 15).Mode == TutorialWaypointIndicator.WaypointMode.Hidden,
                "A ring smaller than the complete waypoint icon cannot render it safely.");
            geometryChecks = checks - before;
            Require(geometryChecks == 2175, "The full 2175-assertion geometry suite is included.");
        }

        private static TutorialWaypointIndicator.IndicatorLayout Waypoint(Vector2 point, Vector2 center,
            float radius = 400, float bounce = 0, bool front = true) =>
            TutorialWaypointIndicator.CalculateLayout(point, center, radius, new Vector2(24, 34), 7, bounce, 7, 6, front);

        private static void CheckAssetWiring()
        {
            const string spritePath = "Assets/Arts/UI/Target.png";
            Sprite target = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(spritePath))
                if (asset is Sprite sprite && sprite.name == "Target_0") target = sprite;
            Require(target != null && target.rect.width > 0 && target.rect.height > 0,
                "Target_0 is an available non-empty cropped sprite.");
            Require(Mathf.Abs(target.rect.width - target.rect.height * (70f / 98f)) <= 1f,
                "The imported target keeps the 70:98 source aspect ratio after texture downsampling.");
            var importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            Require(importer != null, "The target texture importer is available.");
#pragma warning disable 0618
            bool hasSourceCrop = false;
            foreach (SpriteMetaData sourceSprite in importer.spritesheet)
                if (sourceSprite.name == "Target_0")
                    hasSourceCrop = sourceSprite.rect.width == 70 && sourceSprite.rect.height == 98;
#pragma warning restore 0618
            Require(hasSourceCrop, "The source importer retains the original 70 by 98 Target_0 crop.");
            Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string spriteGuid, out long spriteId)
                && spriteId == -4119334708683220384L, "The supplied destination sub-sprite identity is preserved.");
            string terrainGuid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Tutorial/TutorialTerrainLesson.cs");
            string drivingGuid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Tutorial/TutorialDrivingLesson.cs");
            string sceneText = File.ReadAllText(TutorialDrivingLesson.ScenePath);
            Require(Count(sceneText, "guid: " + terrainGuid) == 1, "The temporary tutorial scene opts into terrain teaching exactly once.");
            Require(Count(sceneText, "guid: " + drivingGuid) == 1, "The existing driving chapter remains present exactly once.");
            Require(sceneText.Contains("fileID: " + spriteId + ", guid: " + spriteGuid),
                "The tutorial scene references the cropped target sprite instead of the complete source image.");
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == TutorialDrivingLesson.ScenePath) continue;
                Require(!File.ReadAllText(path).Contains("guid: " + terrainGuid),
                    "Terrain teaching is not attached to an unrelated scene: " + path);
            }
        }

        private static void CheckTerrainPanel()
        {
            var shared = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Prefabs/Resources/UI/Main/MainUISetting.asset");
            Require(shared != null, "The existing HUD settings are available.");
            string sharedBefore = EditorJsonUtility.ToJson(shared);
            Scene scene = EditorSceneManager.NewPreviewScene();
            float previousTimeScale = Time.timeScale;
            TutorialDialoguePanel panel = null;
            RenderTexture texture = null;
            PanelSettings privateSettings = null;
            Font privateFont = null;
            GameObject documentObject = null;
            try
            {
                Time.timeScale = 1;
                var owner = new GameObject("Terrain UI regression (disposable)");
                SceneManager.MoveGameObjectToScene(owner, scene);
                panel = owner.AddComponent<TutorialDialoguePanel>();
                Set(panel, "panelSettingsTemplate", shared);
                if (panel.Document == null) Invoke(panel, "Awake");
                Require(panel.Document != null && panel.Root != null, "The actual tutorial document exists.");
                privateSettings = panel.Document.panelSettings;
                privateFont = Get<Font>(panel, "runtimeFont");
                documentObject = Get<GameObject>(panel, "documentObject");
                texture = new RenderTexture(1920, 1080, 24); texture.Create();
                privateSettings.targetTexture = texture;

                // An inactive bootstrap supplies fields without running its prefab-spawning Awake.
                var fixtureObject = new GameObject("Inactive terrain lesson fixture");
                fixtureObject.SetActive(false);
                SceneManager.MoveGameObjectToScene(fixtureObject, scene);
                var bootstrap = fixtureObject.AddComponent<HeightMapPlayerSceneBootstrap>();
                bootstrap.robot = fixtureObject.AddComponent<RobotMover>();
                var driving = fixtureObject.AddComponent<TutorialDrivingLesson>();
                Set(driving, "panel", panel);
                var lesson = fixtureObject.AddComponent<TutorialTerrainLesson>();
                Set(lesson, "driving", driving); Set(lesson, "bootstrap", bootstrap);
                VisualElement root = panel.Root;

                // Active framing deliberately permits driving, so an input-lock-only modal check
                // must not leave terrain teaching visible while the photo view owns presentation.
                var photoMode = fixtureObject.AddComponent<PhotoModeController>();
                bootstrap.photoMode = photoMode;
                FieldInfo photoState = typeof(PhotoModeController).GetField("state", Flags);
                Require(photoState != null && photoState.FieldType.IsEnum, "The photo state fixture resolves its actual enum.");
                photoState.SetValue(photoMode, Enum.Parse(photoState.FieldType, "Active"));
                Require(photoMode.IsActive && !photoMode.IsInputLocked,
                    "The modal regression fixture is active photo framing with driving input still unlocked.");
                Require((bool)typeof(TutorialTerrainLesson).GetMethod("IsModal", Flags).Invoke(lesson, null),
                    "Terrain teaching treats active photo framing as modal even without an input lock.");
                photoState.SetValue(photoMode, Enum.Parse(photoState.FieldType, "Inactive"));
                Require(!photoMode.IsActive, "The photo fixture returns to inactive before text layout checks.");

                // Dependency loss while a route is displayed must produce the fallback readout,
                // including when an evaluator still exists but the robot has already disappeared.
                RobotMover fixtureRobot = bootstrap.robot;
                bootstrap.robot = null;
                bootstrap.traversalEvaluator = fixtureObject.AddComponent<HeightMapTraversalEvaluator>();
                Set(lesson, "progress", At(TerrainTutorialStep.FollowRoute));
                Invoke(lesson, "RefreshPanel", false, false, true);
                Require(root.Q<Label>("TutorialDetail").text.StartsWith("Ground slope 0", StringComparison.Ordinal),
                    "A missing robot with a surviving evaluator uses a safe slope fallback without throwing.");
                bootstrap.traversalEvaluator = null;
                Invoke(lesson, "RefreshPanel", false, false, true);
                Require(root.Q<Label>("TutorialDetail").text.StartsWith("Ground slope 0", StringComparison.Ordinal),
                    "A missing robot and evaluator also leave the route panel usable.");
                bootstrap.robot = fixtureRobot;

                int before = checks;
                foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720),
                    new Vector2(1440, 1080), new Vector2(2560, 1080) })
                    foreach (TerrainTutorialStep step in Enum.GetValues(typeof(TerrainTutorialStep)))
                    {
                        if (step == TerrainTutorialStep.Completed) continue;
                        Set(lesson, "progress", At(step));
                        foreach (bool[] state in new[] { new[] { true, true, true }, new[] { false, false, true },
                            new[] { true, false, false }, new[] { true, true, false } })
                        {
                            Invoke(lesson, "RefreshPanel", state[0], state[1], state[2]);
                            panel.SetCollapsed(false); Settle(panel, size);
                            Require(root.Q<Label>("TutorialChapter").text == "02 / 09", "Terrain is chapter two of nine.");
                            Require(root.Q<Label>("TutorialHeader").text == "TUTORIAL", "The header remains the simplified tutorial title.");
                            CheckVisibleText(root, step + " at " + size + " / " + string.Join(",", state));
                            if (size == new Vector2(1920, 1080) && state[0] && state[1] && state[2])
                                Capture(root, texture, "terrain-" + step.ToString().ToLowerInvariant() + ".png");
                            panel.SetCollapsed(true); Settle(panel, size);
                            CheckVisibleText(root, "Collapsed " + step + " at " + size);
                        }
                    }
                Set(lesson, "progress", At(TerrainTutorialStep.Completed));
                Invoke(lesson, "RefreshPanel", true, true, true);
                Require(!panel.IsVisible && !panel.CanContinue,
                    "Terminal terrain state hides its card without asking for an extra chapter confirmation.");
                textChecks = checks - before;
                Require(EditorJsonUtility.ToJson(shared) == sharedBefore, "Terrain rendering does not modify shared HUD settings.");
            }
            finally
            {
                if (panel != null)
                {
                    Set(panel, "documentObject", null); Set(panel, "runtimeSettings", null); Set(panel, "runtimeFont", null);
                }
                if (documentObject != null) Object.DestroyImmediate(documentObject);
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                if (privateSettings != null) Object.DestroyImmediate(privateSettings);
                if (privateFont != null) Object.DestroyImmediate(privateFont);
                Time.timeScale = previousTimeScale;
            }
        }

        private static void CheckVisibleText(VisualElement root, string context)
        {
            CheckToggleHelp(root, context);
            var characters = new HashSet<char>();
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
            {
                if (!Visible(element, root)) continue;
                string text = element is Label label ? label.text : string.Empty;
                Require(!ContainsHan(text + element.tooltip), context + " uses English labels and tooltips.");
                foreach (char character in text + element.tooltip)
                    if (!char.IsWhiteSpace(character) && !char.IsControl(character)) characters.Add(character);
                if (element is Label shown)
                {
                    Vector2 measured = shown.MeasureTextSize(shown.text, shown.layout.width, VisualElement.MeasureMode.AtMost,
                        0, VisualElement.MeasureMode.Undefined);
                    Require(measured.y <= shown.layout.height + 2,
                        context + " / " + shown.name + " fits height: " + measured + " in " + shown.layout.size + " / " + shown.text);
                    if (shown.resolvedStyle.whiteSpace == WhiteSpace.NoWrap)
                    {
                        Vector2 natural = shown.MeasureTextSize(shown.text, 0, VisualElement.MeasureMode.Undefined,
                            0, VisualElement.MeasureMode.Undefined);
                        Require(natural.x <= shown.layout.width + 2,
                            context + " / " + shown.name + " fits width: " + natural + " in " + shown.layout.size + " / " + shown.text);
                    }
                }
            }
            Font font = root.resolvedStyle.unityFontDefinition.font;
            if (font == null) font = root.resolvedStyle.unityFont;
            Require(font != null, context + " resolves a font.");
            var glyphs = new StringBuilder(); foreach (char character in characters) glyphs.Append(character);
            font.RequestCharactersInTexture(glyphs.ToString(), 28, FontStyle.Normal);
            foreach (char character in characters)
                Require(font.HasCharacter(character), context + " contains glyph U+" + ((int)character).ToString("X4"));
        }

        private static void CheckToggleHelp(VisualElement root, string context)
        {
            VisualElement card = root.Q("TutorialPanel");
            if (Visible(card, root))
            {
                Label hint = root.Q<Label>("TutorialToggleHint");
                Require(hint != null && Visible(hint, root) && hint.text == "Tap D-pad ← / →  Hide",
                    context + " keeps both D-pad directions visible through every terrain stage and scan status.");
                Label status = root.Q<Label>("TutorialStatus");
                Require(hint.parent == card && hint != status && hint.worldBound.yMin >= status.worldBound.yMax,
                    context + " places persistent input help below, independently of lesson feedback.");
            }
            VisualElement tab = root.Q("ExpandTutorial");
            if (Visible(tab, root))
            {
                Label arrows = root.Q<Label>("TutorialTabToggleArrows");
                Label hint = root.Q<Label>("TutorialTabToggleHint");
                Require(arrows != null && Visible(arrows, root) && arrows.text == "← →",
                    context + " shows both D-pad directions on the narrow tab.");
                Require(hint != null && Visible(hint, root) && hint.text == "Show",
                    context + " retains its show-help label while hidden.");
            }
        }

        private static bool Visible(VisualElement element, VisualElement root)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility != Visibility.Visible) return false;
                if (current == root) return true;
            }
            return false;
        }

        private static bool ContainsHan(string text)
        {
            foreach (char character in text ?? string.Empty)
                if ((character >= '\u3400' && character <= '\u9fff') || (character >= '\uf900' && character <= '\ufaff')) return true;
            return false;
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
                RenderTexture.active = target;
                GL.Clear(true, true, new Color(.035f, .055f, .055f));
                Type panelType = root.panel.GetType();
                // Apply the private PanelSettings target and pump the real runtime panel before
                // repainting. ValidateLayout alone does not synchronize the target texture.
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
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                int foreground = 0;
                foreach (Color32 pixel in image.GetPixels32())
                    if (pixel.r > 120 || pixel.g > 120 || pixel.b > 120) foreground++;
                Require(foreground > 200, "Capture contains actual terrain teaching linework/text: "
                    + name + " (foreground pixels " + foreground + ").");
                string path = Path.Combine(OutputDirectory(), name);
                File.WriteAllBytes(path, image.EncodeToPNG());
                screenshots.Add(path);
            }
            finally
            {
                if (image != null) Object.DestroyImmediate(image);
                RenderTexture.active = previous;
            }
        }

        private static int Count(string text, string value)
        {
            int result = 0, offset = 0;
            while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0) { result++; offset += value.Length; }
            return result;
        }

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
            string configured = Environment.GetEnvironmentVariable("TUTORIAL_TERRAIN_VALIDATION_OUTPUT");
            string directory = string.IsNullOrEmpty(configured) ? Path.GetFullPath("output/validation/tutorial-terrain") : configured;
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteReport(bool passed, string error) =>
            File.WriteAllText(Path.Combine(OutputDirectory(), "regression-report.json"), JsonUtility.ToJson(new Report
                { passed = passed, checks = checks, geometryChecks = geometryChecks, textChecks = textChecks,
                    error = error, screenshots = screenshots.ToArray() }, true));
    }
}

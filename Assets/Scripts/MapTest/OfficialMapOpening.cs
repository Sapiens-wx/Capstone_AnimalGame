using System.Collections.Generic;
using AnimalGame.Demos.CheckpointRefit;
using AnimalGame.MainUI;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using AnimalGame.World;
using AnimalGame.World.Map;
using Capstone.Audio;
using UnityEngine;
using GameHud = AnimalGame.MainUI.MainUI;

namespace AnimalGame.MapTest
{
    [DefaultExecutionOrder(320)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HeightMapPlayerSceneBootstrap))]
    public sealed class OfficialMapOpening : MonoBehaviour
    {
        [Header("Driving tutorial line (map meters)")]
        [SerializeField] private Vector2 drivingHintLineSouth;
        [SerializeField] private Vector2 drivingHintLineNorth;

        [Header("Map tutorial signal source")]
        [SerializeField] private Transform signalTower;

        [Header("Traversal tutorial line (map meters)")]
        [SerializeField] private Vector2 traversalHintLineSouth;
        [SerializeField] private Vector2 traversalHintLineNorth;

        [Header("Arm tutorial line (map meters)")]
        [SerializeField] private Vector2 armHintLineSouth;
        [SerializeField] private Vector2 armHintLineNorth;

        [Header("Interest scan tutorial line (map meters)")]
        [SerializeField] private Vector2 interestScanHintLineSouth;
        [SerializeField] private Vector2 interestScanHintLineNorth;

        [Header("Drive fault activation line (map meters)")]
        [SerializeField] private Vector2 damageLineSouth;
        [SerializeField] private Vector2 damageLineNorth;

        [Header("Opening woodpecker")]
        [SerializeField] private SpriteRenderer woodpeckerAppearance;

        private const float SkipHoldDuration = 1f;
        private const float RestartPeckInterval = 3f;
        private const float TestRumbleDuration = .18f;
        private const float StartingIntegrity = .32f;
        private const float ArmMotionStart = .08f;
        private const float FirstJam = .42f;
        private const float SecondJam = .75f;
        private const float RetractionJam = OpeningHudTimeline.ArmRetractionStart + .28f;
        private const float HandoffRightingDuration = .46f;
        private const float MapScanShakeSettleDuration = .28f;
        private const float ArmTutorialApproachDepthMeters = 30f;
        private static readonly Vector2 mediumRumble = new(.42f, .48f);
        private static readonly Impact peckImpact = new(0f, .22f, 12f, -16f, .48f, 18f);

        private static readonly Vector2[] leftArm =
        {
            new(0f, 0f), new(ArmMotionStart, 0f), new(.25f, .24f), new(.37f, .19f), new(.60f, .60f), new(.71f, .60f),
            new(1.04f, .98f), new(OpeningHudTimeline.ArmFullyExtended, 1f), new(OpeningHudTimeline.ArmRetractionStart, 1f),
            new(OpeningHudTimeline.ArmRetractionStart + .22f, .49f), new(OpeningHudTimeline.ArmRetractionStart + .34f, .26f),
            new(OpeningHudTimeline.ArmRetractionStart + .44f, .26f), new(OpeningHudTimeline.ArmRetractionStart + .58f, 0f)
        };

        private static readonly Vector2[] rightArm =
        {
            new(0f, 0f), new(.19f, 0f), new(.38f, .12f), new(.44f, .22f), new(.55f, .22f), new(.63f, .12f),
            new(.74f, .44f), new(.83f, .32f), new(1f, .79f), new(1.12f, .86f), new(OpeningHudTimeline.ArmFullyExtended, 1f),
            new(OpeningHudTimeline.ArmRetractionStart + .05f, 1f), new(RetractionJam, .42f),
            new(OpeningHudTimeline.ArmRetractionStart + .43f, .42f), new(OpeningHudTimeline.ArmRetractionStart + .56f, .19f),
            new(OpeningHudTimeline.ArmDuration, 0f)
        };

        private static readonly Impact[] impacts =
        {
            new(OpeningHudTimeline.ArmStart + ArmMotionStart, .24f, -18f, 13f, .50f, 16f),
            new(OpeningHudTimeline.ArmStart + .28f, .25f, 20f, -15f, -.64f, 19f),
            new(OpeningHudTimeline.ArmStart + .53f, .26f, -25f, 15f, .76f, 18f),
            new(OpeningHudTimeline.ArmStart + .83f, .27f, 25f, -14f, -.70f, 17f),
            new(OpeningHudTimeline.ArmStart + 1.06f, .17f, -21f, -9f, .46f, 15f),
            new(OpeningHudTimeline.TerrainStart, .22f, 13f, 21f, .43f, 15f),
            new(OpeningHudTimeline.TerrainDrop, .19f, -18f, -13f, -.45f, 17f),
            new(OpeningHudTimeline.TerrainLock, .14f, 12f, 19f, .45f, 16f)
        };

        private static readonly TimedSound[] sounds =
        {
            new(0f, OpeningAudioCue.RestartConfirmed),
            new(0f, OpeningAudioCue.StatusScan),
            new(.26f, OpeningAudioCue.StatusGlitch),
            new(OpeningHudTimeline.RestartStart, OpeningAudioCue.Title),
            new(OpeningHudTimeline.PowerReady, OpeningAudioCue.TestResult),
            new(OpeningHudTimeline.LastRunFound, OpeningAudioCue.InstrumentOnline),
            new(OpeningHudTimeline.LastRunFound + .25f, OpeningAudioCue.InstrumentGlitch),
            new(OpeningHudTimeline.ClockResult, OpeningAudioCue.TestResult),
            new(OpeningHudTimeline.Positioning.Result, OpeningAudioCue.InstrumentOnline),
            new(OpeningHudTimeline.Positioning.Result + .16f, OpeningAudioCue.InstrumentGlitch),
            new(OpeningHudTimeline.Altitude.Result, OpeningAudioCue.InstrumentOnline),
            new(OpeningHudTimeline.Altitude.Result + .23f, OpeningAudioCue.InstrumentGlitch),
            new(OpeningHudTimeline.Frame, OpeningAudioCue.InstrumentOnline),
            new(OpeningHudTimeline.Frame + .23f, OpeningAudioCue.InstrumentGlitch),
            new(OpeningHudTimeline.CompassTest.Result, OpeningAudioCue.InstrumentOnline),
            new(OpeningHudTimeline.CompassTest.Result + .23f, OpeningAudioCue.InstrumentGlitch),
            new(OpeningHudTimeline.Level.Result, OpeningAudioCue.InstrumentOnline),
            new(OpeningHudTimeline.Level.Result + .23f, OpeningAudioCue.InstrumentGlitch),
            new(OpeningHudTimeline.AudioResult, OpeningAudioCue.InstrumentOnline),
            new(OpeningHudTimeline.AudioResult + .23f, OpeningAudioCue.InstrumentGlitch),
            new(OpeningHudTimeline.ArmStart + ArmMotionStart, OpeningAudioCue.ArmDeploy),
            new(OpeningHudTimeline.ArmStart + FirstJam, OpeningAudioCue.ArmJam),
            new(OpeningHudTimeline.ArmStart + .71f, OpeningAudioCue.ArmServo),
            new(OpeningHudTimeline.ArmStart + SecondJam, OpeningAudioCue.ArmJam),
            new(OpeningHudTimeline.ArmStart + OpeningHudTimeline.ArmFullyExtended, OpeningAudioCue.ArmDeploy, stop: true),
            new(OpeningHudTimeline.ArmStart + OpeningHudTimeline.ArmFullyExtended, OpeningAudioCue.ArmServo, stop: true),
            new(OpeningHudTimeline.ArmStart + OpeningHudTimeline.ArmRetractionStart, OpeningAudioCue.ArmRetract),
            new(OpeningHudTimeline.ArmStart + RetractionJam, OpeningAudioCue.ArmJam),
            new(OpeningHudTimeline.ArmEnd, OpeningAudioCue.ArmRetract, stop: true),
            new(OpeningHudTimeline.ArmEnd, OpeningAudioCue.ArmSettled),
            new(OpeningHudTimeline.SensorLogStart, OpeningAudioCue.SensorsRestart),
            new(OpeningHudTimeline.SensorLogEnd, OpeningAudioCue.SensorsRestart, stop: true),
            new(OpeningHudTimeline.ContourStart, OpeningAudioCue.ContourReveal),
            new(OpeningHudTimeline.ContourStart + .39f, OpeningAudioCue.ContourDrop),
            new(OpeningHudTimeline.TerrainStart, OpeningAudioCue.TerrainReveal),
            new(OpeningHudTimeline.TerrainDrop, OpeningAudioCue.TerrainDrop),
            new(OpeningHudTimeline.TerrainLock, OpeningAudioCue.MapReady)
        };

        private readonly List<Renderer> hiddenWorldRenderers = new();
        private static OfficialMapOpening active;

        private GameHud hud;
        private MapTestSceneController map;
        private RobotMover robot;
        private RobotMarkerView marker;
        private RobotArmController arms;
        private Camera mapCamera;
        private RobotCameraFollow cameraFollow;
        private RobotCameraShake cameraShake;
        private OpeningAudioPlayer openingAudio;
        private OpeningWoodpecker openingWoodpecker;
        private CameraClearFlags originalClearFlags;
        private Color originalBackgroundColor;
        private float originalTimeScale;
        private double previousFrameTime;
        private double nextTypingSoundTime;
        private double lastPeckTime = double.NegativeInfinity;
        private double? restartStartedAt;
        private float standbyPromptTime;
        private float skipHoldTime = -1f;
        private float elapsed;
        private float testPromptTime = -1f;
        private float testRumbleRemaining;
        private int testPauseIndex;
        private int wakePecks;

        public static bool IsPlaying => active != null;

        private readonly struct TimedSound
        {
            public readonly float Time;
            public readonly OpeningAudioCue Cue;
            public readonly bool Stop;

            public TimedSound(float time, OpeningAudioCue cue, bool stop = false)
            {
                Time = time;
                Cue = cue;
                Stop = stop;
            }
        }

        private readonly struct Impact
        {
            public readonly float Start, Duration, X, Y, Turn, Rate;

            public Impact(float start, float duration, float x, float y, float turn, float rate)
            {
                Start = start;
                Duration = duration;
                X = x;
                Y = y;
                Turn = turn;
                Rate = rate;
            }
        }

        private void Awake()
        {
            var bootstrap = GetComponent<HeightMapPlayerSceneBootstrap>();
            if (!bootstrap.isActiveAndEnabled)
            {
                enabled = false;
                return;
            }

            hud = bootstrap.mainUiObject.GetComponent<GameHud>();
            map = bootstrap.map;
            robot = bootstrap.robot;
            marker = bootstrap.robotObject.GetComponent<RobotMarkerView>();
            arms = bootstrap.armController;
            mapCamera = bootstrap.mapCamera;
            cameraFollow = bootstrap.cameraFollow;
            cameraShake = bootstrap.cameraShake;
            openingAudio = new OpeningAudioPlayer(Resources.Load<OpeningAudioSettings>(OpeningAudioSettings.ResourceName));
            openingWoodpecker = new OpeningWoodpecker(woodpeckerAppearance, robot.transform, mapCamera);

            originalTimeScale = Time.timeScale;
            originalClearFlags = mapCamera.clearFlags;
            originalBackgroundColor = mapCamera.backgroundColor;
            active = this;
            Time.timeScale = 0f;
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = Color.black;

            map.SetOpeningScanTime(0f);
            marker.SetOpeningPresentation(0f, 0f, 0f, 0f);
            arms.SetOpeningPresentation(0f, 0f, 0f);
            robot.GetComponent<PlayerCombatReceiver>().InitializeIntegrity(hud, StartingIntegrity);
            hud.BeginOpeningPresentation(PlayTypingSound);
        }

        private void Start()
        {
            // Tiles have their own reveal shader. Props join the view at the terrain lock.
            var tiles = new HashSet<Renderer>(map.PhotoContourRenderers);
            Transform robotTransform = robot.transform;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.forceRenderingOff || tiles.Contains(renderer) || renderer.transform.IsChildOf(robotTransform))
                        continue;
                    renderer.forceRenderingOff = true;
                    hiddenWorldRenderers.Add(renderer);
                }
            }

            openingAudio.PlayWoodpeckerLanding();
            previousFrameTime = Time.unscaledTimeAsDouble;
        }

        private void LateUpdate()
        {
            if (active != this) return;
            openingAudio.Tick();
            double now = Time.unscaledTimeAsDouble;
            float deltaTime = (float)(now - previousFrameTime);
            previousFrameTime = now;
            if (AdvanceSkipHold(deltaTime, Application.isFocused && Input.GetKey(KeyCode.Escape)))
            {
                CompleteOpening();
                return;
            }
            openingWoodpecker.Tick(deltaTime);
            if (!restartStartedAt.HasValue)
            {
                UpdateStandby(deltaTime, now, Application.isFocused && WasContinuePressed());
                return;
            }

            bool nextTestPressed = testPromptTime >= 0f && WasContinuePressed();
            if (nextTestPressed) openingAudio.Play(OpeningAudioCue.Confirm);
            float previousElapsed = elapsed;
            AdvanceTimeline(deltaTime, nextTestPressed);
            PlayTimedSounds(previousElapsed);
            float takeoffTime = OpeningHudTimeline.ArmStart + ArmMotionStart;
            if (previousElapsed < takeoffTime && elapsed >= takeoffTime)
                openingAudio.PlayWoodpeckerTakeoff(openingWoodpecker.BeginDeparture(), WoodpeckerWaveBodyRadius);
            else if (elapsed < takeoffTime && now >= System.Math.Max(restartStartedAt.Value, lastPeckTime) + RestartPeckInterval)
                PeckWoodpecker(now);
            if (elapsed >= OpeningHudTimeline.Duration)
            {
                openingAudio.Play(OpeningAudioCue.Handoff);
                openingAudio.Finish();
                CompleteOpening();
                return;
            }
            ApplyFrame(now);
        }

        private static bool WasContinuePressed() =>
            AdaptiveLegacyGamepadInput.WasSouthFaceButtonPressedThisFrame() || Input.GetKeyDown(KeyCode.Return);

        private float WoodpeckerWaveBodyRadius =>
            marker.VisualBodyDiameter * Mathf.Abs(marker.MarkerVisualRoot.lossyScale.x) * .5f;

        private void UpdateStandby(float deltaTime, double now, bool continuePressed)
        {
            standbyPromptTime += deltaTime;
            if (continuePressed && standbyPromptTime >= OpeningHudTimeline.WakePromptDelay)
            {
                if (wakePecks == OpeningHudTimeline.WakePecksRequired)
                {
                    restartStartedAt = now;
                    hud.EndOpeningStandby();
                    PlayTimedSounds(-1f);
                    ApplyFrame(now);
                    return;
                }
                if (wakePecks == 0)
                {
                    marker.SetOpeningPresentation(1f, 0f, 0f, 0f);
                    openingWoodpecker.Show();
                }
                wakePecks++;
                PeckWoodpecker(now);
            }
            hud.UpdateOpeningStandby(standbyPromptTime, wakePecks);
            ApplyShake(now, Vector2.zero, 0f, Vector2.zero);
        }

        private void PeckWoodpecker(double now)
        {
            lastPeckTime = now;
            openingAudio.PlayWoodpeckerPeck(openingWoodpecker.Peck(), WoodpeckerWaveBodyRadius);
        }

        private void CompleteOpening()
        {
            enabled = false;
            robot.SetOpeningDriveFaultTrigger(map.MapPositionToWorld(damageLineSouth),
                map.MapPositionToWorld(damageLineNorth));
            hud.EnableRobotGoalModule();
            hud.ShowOpeningControlHints(robot, map.MapPositionToWorld(drivingHintLineSouth),
                map.MapPositionToWorld(drivingHintLineNorth), map.MapPositionToWorld(traversalHintLineSouth),
                map.MapPositionToWorld(traversalHintLineNorth));
            hud.ShowOpeningArmHints(arms, marker, map.MapPositionToWorld(armHintLineSouth),
                map.MapPositionToWorld(armHintLineNorth),
                map.MapMetersToWorldDistance(Vector2.right, ArmTutorialApproachDepthMeters));
            hud.ShowOpeningRepairHint(signalTower.GetComponentInChildren<CheckpointRefitTerminal>(true));
            CommunicationsStation.SignalReceived += OnCommunicationsSignalReceived;
            var firstTower = signalTower.GetComponent<WeatherTower>();
            var scanInput = GetComponent<HeightMapPlayerSceneBootstrap>().scanChargeUi;
            gameObject.AddComponent<OpeningInterestScanLesson>().Bind(firstTower, robot, hud, scanInput,
                map.MapPositionToWorld(interestScanHintLineSouth), map.MapPositionToWorld(interestScanHintLineNorth));
            if (!SaveSystem.HasPlayedFirstTowerRavenFlyover)
                gameObject.AddComponent<CommunicationsRavenFlyover>().Bind(
                    firstTower, robot, mapCamera, hud, woodpeckerAppearance);
        }

        private bool AdvanceSkipHold(float deltaTime, bool held)
        {
            if (!held)
            {
                skipHoldTime = -1f;
                return false;
            }
            skipHoldTime = skipHoldTime < 0f ? 0f : skipHoldTime + deltaTime;
            return skipHoldTime >= SkipHoldDuration;
        }

        private void OnCommunicationsSignalReceived(CommunicationsStation station, RobotMover player)
        {
            if (player != robot || !station.transform.IsChildOf(signalTower)) return;
            CommunicationsStation.SignalReceived -= OnCommunicationsSignalReceived;
            WorldMapUI worldMap = WorldMapUI.Instance;
            string stationId = signalTower.GetComponentInChildren<StationLogConsole>(true).StationId;
            if (worldMap.RevealUnknownSignal(stationId, station.SignalOrigin.position))
                hud.ShowOpeningSignalPrompt(worldMap);
        }

        private void OnDestroy() => CommunicationsStation.SignalReceived -= OnCommunicationsSignalReceived;

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) skipHoldTime = -1f;
        }

        private void PlayTypingSound()
        {
            double now = Time.unscaledTimeAsDouble;
            if (now < nextTypingSoundTime) return;
            nextTypingSoundTime = now + .10;
            openingAudio.Play(OpeningAudioCue.Typing);
        }

        private void PlayTimedSounds(float previousElapsed)
        {
            if (elapsed <= previousElapsed) return;
            foreach (TimedSound sound in sounds)
            {
                if (sound.Time > elapsed) break;
                if (sound.Time <= previousElapsed) continue;
                if (sound.Stop) openingAudio.Stop(sound.Cue);
                else openingAudio.Play(sound.Cue);
            }
        }

        private void AdvanceTimeline(float deltaTime, bool nextTestPressed)
        {
            testRumbleRemaining = Mathf.Max(0f, testRumbleRemaining - deltaTime);
            if (testPromptTime >= 0f)
            {
                if (nextTestPressed)
                {
                    testPauseIndex++;
                    testPromptTime = -1f;
                    testRumbleRemaining = TestRumbleDuration;
                }
                else testPromptTime += deltaTime;
                return;
            }

            elapsed += deltaTime;
            var pauses = OpeningHudTimeline.TestPauses;
            if (testPauseIndex >= pauses.Length || elapsed < pauses[testPauseIndex]) return;
            elapsed = pauses[testPauseIndex];
            testPromptTime = 0f;
        }

        private void ApplyFrame(double now)
        {
            float rightingStart = OpeningHudTimeline.Duration - HandoffRightingDuration;
            if (elapsed >= rightingStart)
                Milestone3AudioRuntime.SetOpeningRightingProgress((elapsed - rightingStart) / HandoffRightingDuration);

            float armTime = elapsed - OpeningHudTimeline.ArmStart;
            float hatch = OpeningHudTimeline.Ease(armTime, 0f, .22f)
                * (1f - OpeningHudTimeline.Ease(armTime, OpeningHudTimeline.ArmRetractionStart + .52f, OpeningHudTimeline.ArmDuration));
            float jam = FaultPulse(armTime, FirstJam, .50f, .56f, .67f)
                + FaultPulse(armTime, SecondJam, .82f, .86f, .96f)
                + .72f * FaultPulse(armTime, RetractionJam, RetractionJam + .07f, RetractionJam + .12f, RetractionJam + .22f);
            marker.SetOpeningPresentation(
                1f,
                OpeningHudTimeline.Ease(elapsed, OpeningHudTimeline.HeadingStart, OpeningHudTimeline.HeadingStart + .20f),
                OpeningHudTimeline.Ease(elapsed, OpeningHudTimeline.PlayerStateStart, OpeningHudTimeline.PlayerStateStart + .23f),
                hatch);
            arms.SetOpeningPresentation(SampleArm(armTime, leftArm), SampleArm(armTime, rightArm), jam);
            hud.UpdateOpeningPresentation(elapsed, testPromptTime);
            map.SetOpeningScanTime(Mathf.Max(0f, elapsed - OpeningHudTimeline.ContourStart + MapTestSceneController.OpeningContourStart));
            if (elapsed >= OpeningHudTimeline.TerrainLock)
                RestoreWorldRenderers();
            SampleShake(elapsed, testRumbleRemaining, (float)now, out Vector2 offset, out float roll, out Vector2 motors);
            ApplyShake(now, offset, roll, motors);
        }

        private static float SampleArm(float time, Vector2[] keyframes)
        {
            for (int i = 1; i < keyframes.Length; i++)
            {
                if (time > keyframes[i].x) continue;
                Vector2 from = keyframes[i - 1], to = keyframes[i];
                return Mathf.Lerp(from.y, to.y, OpeningHudTimeline.Ease(time, from.x, to.x));
            }
            return 0f;
        }

        private static float FaultPulse(float time, float start, float peak, float release, float end) =>
            OpeningHudTimeline.Ease(time, start, peak) * (1f - OpeningHudTimeline.Ease(time, release, end));

        private static void SampleShake(float elapsed, float testRumbleRemaining, float unscaledTime,
            out Vector2 offset, out float roll, out Vector2 motors)
        {
            offset = Vector2.zero;
            roll = 0f;
            motors = new Vector2(.46f, .49f) * Mathf.Clamp01(testRumbleRemaining / .11f);
            if (elapsed >= OpeningHudTimeline.SensorLogStart && elapsed < OpeningHudTimeline.Duration)
            {
                // Keep the motor load pulsing while the timeline waits for the last confirmation.
                float effort = .5f + .5f * Mathf.Sin(unscaledTime * (2.35f * 2f * Mathf.PI));
                var strain = Vector2.Lerp(new Vector2(.20f, .12f), new Vector2(.34f, .24f), effort * effort);
                motors = Vector2.Max(motors, strain);
            }
            if (elapsed >= 0f && elapsed < OpeningHudTimeline.StatusScanDuration)
            {
                float decay = 1f - elapsed / OpeningHudTimeline.StatusScanDuration;
                float stutter = .72f + .28f * Mathf.Cos(elapsed * 13f * 2f * Mathf.PI);
                motors = Vector2.Max(motors, new Vector2(.45f, .53f) * (decay * stutter));
            }
            float scanAge = elapsed - OpeningHudTimeline.ContourStart;
            float scanSettleEnd = OpeningHudTimeline.TerrainLock + MapScanShakeSettleDuration;
            if (scanAge >= 0f && elapsed < scanSettleEnd)
            {
                float terrainStrength = OpeningHudTimeline.Ease(elapsed, OpeningHudTimeline.TerrainStart,
                    OpeningHudTimeline.TerrainStart + .16f);
                float settle = 1f - OpeningHudTimeline.Ease(elapsed, OpeningHudTimeline.TerrainLock, scanSettleEnd);
                float strength = Mathf.Lerp(1f, 1.4f, terrainStrength) * settle;
                float phase = scanAge * (8.5f * 2f * Mathf.PI);
                offset = new Vector2(7f * Mathf.Cos(phase) + 2f * Mathf.Sin(1.73f * phase),
                    5f * Mathf.Sin(.77f * phase) + 1.5f * Mathf.Cos(1.29f * phase)) * strength;
                roll = .26f * Mathf.Sin(.61f * phase) * strength;
            }
            foreach (Impact impact in impacts)
                AddImpact(impact, elapsed, ref offset, ref roll, ref motors);
        }

        private static void AddImpact(Impact impact, float elapsed, ref Vector2 offset, ref float roll, ref Vector2 motors)
        {
            float age = elapsed - impact.Start;
            if (age < 0f || age >= impact.Duration) return;
            float damping = Mathf.Pow(1f - age / impact.Duration, 1.65f);
            float phase = age * impact.Rate * 2f * Mathf.PI;
            offset.x += impact.X * Mathf.Cos(phase) * damping;
            offset.y += impact.Y * Mathf.Cos(.72f * phase) * damping;
            roll += impact.Turn * Mathf.Cos(.58f * phase) * damping;

            float positionStrength = Mathf.Clamp01(new Vector2(impact.X, impact.Y).magnitude / 30f);
            float turnStrength = Mathf.Clamp01(Mathf.Abs(impact.Turn) / .76f);
            var impactMotors = Vector2.Max(mediumRumble, new Vector2(.82f * positionStrength, .62f * turnStrength));
            motors = Vector2.Max(motors, impactMotors * damping);
        }

        private void ApplyShake(double now, Vector2 offset, float roll, Vector2 motors)
        {
            AddImpact(peckImpact, (float)(now - lastPeckTime), ref offset, ref roll, ref motors);
            cameraShake.SetUnscaledRumble(cameraShake.isActiveAndEnabled ? motors : Vector2.zero, outputGain: 2f);
            Vector2 screenShift = new Vector2(offset.x / 1578f, offset.y / 886f);
            hud.SetOpeningScreenShake(screenShift, roll);
            if (screenShift == Vector2.zero && roll == 0f) return;

            // The normal camera follow has already supplied this frame's unshaken pose.
            Transform view = mapCamera.transform;
            float viewHeight = 2f * mapCamera.orthographicSize;
            Vector3 translation = viewHeight * (view.right * (screenShift.x * mapCamera.aspect) - view.up * screenShift.y);
            view.SetPositionAndRotation(view.position - translation, view.rotation * Quaternion.Euler(0f, 0f, roll));
        }

        private void RestoreWorldRenderers()
        {
            foreach (Renderer renderer in hiddenWorldRenderers)
                if (renderer != null) renderer.forceRenderingOff = false;
            hiddenWorldRenderers.Clear();
        }

        private void OnDisable()
        {
            if (active != this) return;
            openingAudio.StopAll();
            openingWoodpecker.Dispose();
            Milestone3AudioRuntime.EndOpeningRighting(elapsed >= OpeningHudTimeline.Duration ? robot : null);
            if (cameraShake != null) cameraShake.StopUnscaledRumble();
            if (hud != null) hud.EndOpeningPresentation();
            if (arms != null) arms.EndOpeningPresentation();
            if (marker != null) marker.EndOpeningPresentation();
            if (map != null) map.ClearOpeningScan();
            RestoreWorldRenderers();
            if (mapCamera != null)
            {
                mapCamera.clearFlags = originalClearFlags;
                mapCamera.backgroundColor = originalBackgroundColor;
            }
            if (cameraFollow != null) cameraFollow.SnapToTarget();
            Time.timeScale = originalTimeScale;
            active = null;
        }
    }
}

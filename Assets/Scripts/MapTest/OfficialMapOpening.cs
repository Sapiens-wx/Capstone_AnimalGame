using System.Collections.Generic;
using AnimalGame.MainUI;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
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

        private const float RestartHoldDuration = .65f;
        private const float RestartTransitionTime = RestartHoldDuration + .10f;
        private const float TestRumbleDuration = .18f;
        private const float StartingIntegrity = .32f;
        private const float ArmMotionStart = .08f;
        private const float FirstJam = .42f;
        private const float SecondJam = .75f;
        private const float RetractionStart = 1.23f;
        private const float RetractionJam = 1.51f;
        private static readonly Vector2 mediumRumble = new(.42f, .48f);

        private static readonly Vector2[] leftArm =
        {
            new(0f, 0f), new(ArmMotionStart, 0f), new(.25f, .24f), new(.37f, .19f), new(.60f, .60f), new(.71f, .60f),
            new(1.04f, .98f), new(RetractionStart, .98f), new(1.45f, .49f), new(1.57f, .26f), new(1.67f, .26f), new(1.81f, 0f)
        };

        private static readonly Vector2[] rightArm =
        {
            new(0f, 0f), new(.19f, 0f), new(.38f, .12f), new(.44f, .22f), new(.55f, .22f), new(.63f, .12f),
            new(.74f, .44f), new(.83f, .32f), new(1f, .79f), new(1.12f, .86f), new(1.28f, .86f),
            new(1.51f, .42f), new(1.66f, .42f), new(1.79f, .19f), new(OpeningHudTimeline.ArmDuration, 0f)
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
            new(OpeningHudTimeline.ArmStart + RetractionStart, OpeningAudioCue.ArmDeploy, stop: true),
            new(OpeningHudTimeline.ArmStart + RetractionStart, OpeningAudioCue.ArmServo, stop: true),
            new(OpeningHudTimeline.ArmStart + RetractionStart, OpeningAudioCue.ArmRetract),
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
        private CameraClearFlags originalClearFlags;
        private Color originalBackgroundColor;
        private float originalTimeScale;
        private double previousFrameTime;
        private double nextTypingSoundTime;
        private float standbyPromptTime;
        private float restartHoldTime = -1f;
        private float elapsed;
        private float testPromptTime = -1f;
        private float testRumbleRemaining;
        private int testPauseIndex;

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

            previousFrameTime = Time.unscaledTimeAsDouble;
        }

        private void LateUpdate()
        {
            if (active != this) return;
            openingAudio.Tick();
            double now = Time.unscaledTimeAsDouble;
            float deltaTime = (float)(now - previousFrameTime);
            previousFrameTime = now;
            if (restartHoldTime < RestartTransitionTime)
            {
                bool held = Application.isFocused
                    && (AdaptiveLegacyGamepadInput.IsSouthFaceButtonHeld() || Input.GetKey(KeyCode.Return));
                if (held && restartHoldTime < 0f) openingAudio.Play(OpeningAudioCue.RestartHold);
                if (!held && restartHoldTime >= 0f && restartHoldTime < RestartHoldDuration)
                    openingAudio.Stop(OpeningAudioCue.RestartHold);
                if (AdvanceRestartHold(deltaTime, held))
                {
                    openingAudio.Stop(OpeningAudioCue.RestartHold);
                    hud.EndOpeningStandby();
                    PlayTimedSounds(-1f);
                    ApplyFrame();
                }
                else hud.UpdateOpeningStandby(standbyPromptTime, held ? restartHoldTime / RestartHoldDuration : -1f);
                return;
            }

            bool nextTestPressed = testPromptTime >= 0f
                && (AdaptiveLegacyGamepadInput.WasSouthFaceButtonPressedThisFrame() || Input.GetKeyDown(KeyCode.Return));
            if (nextTestPressed) openingAudio.Play(OpeningAudioCue.Confirm);
            float previousElapsed = elapsed;
            AdvanceTimeline(deltaTime, nextTestPressed);
            PlayTimedSounds(previousElapsed);
            if (elapsed >= OpeningHudTimeline.Duration)
            {
                openingAudio.Play(OpeningAudioCue.Handoff);
                openingAudio.Finish();
                enabled = false;
                hud.ShowOpeningDrivingHint(robot, map.MapPositionToWorld(drivingHintLineSouth),
                    map.MapPositionToWorld(drivingHintLineNorth));
                return;
            }
            ApplyFrame();
        }

        private bool AdvanceRestartHold(float deltaTime, bool held)
        {
            standbyPromptTime += deltaTime;
            // Keep the full ring visible briefly before starting the status scan.
            if (restartHoldTime >= RestartHoldDuration)
                restartHoldTime += deltaTime;
            else if (!held)
                restartHoldTime = -1f;
            else if (restartHoldTime < 0f)
                restartHoldTime = 0f;
            else
                restartHoldTime = Mathf.Min(RestartHoldDuration, restartHoldTime + deltaTime);
            return restartHoldTime >= RestartTransitionTime;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && restartHoldTime < RestartHoldDuration)
            {
                restartHoldTime = -1f;
                openingAudio?.Stop(OpeningAudioCue.RestartHold);
            }
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

        private void ApplyFrame()
        {
            float armTime = elapsed - OpeningHudTimeline.ArmStart;
            float hatch = OpeningHudTimeline.Ease(armTime, 0f, .22f)
                * (1f - OpeningHudTimeline.Ease(armTime, 1.75f, OpeningHudTimeline.ArmDuration));
            float jam = FaultPulse(armTime, FirstJam, .50f, .56f, .67f)
                + FaultPulse(armTime, SecondJam, .82f, .86f, .96f)
                + .72f * FaultPulse(armTime, RetractionJam, 1.58f, 1.63f, 1.73f);
            marker.SetOpeningPresentation(
                OpeningHudTimeline.Ease(elapsed, OpeningHudTimeline.BodyStart, OpeningHudTimeline.BodyReady),
                OpeningHudTimeline.Ease(elapsed, OpeningHudTimeline.HeadingStart, OpeningHudTimeline.HeadingStart + .20f),
                OpeningHudTimeline.Ease(elapsed, OpeningHudTimeline.PlayerStateStart, OpeningHudTimeline.PlayerStateStart + .23f),
                hatch);
            arms.SetOpeningPresentation(SampleArm(armTime, leftArm), SampleArm(armTime, rightArm), jam);
            hud.UpdateOpeningPresentation(elapsed, testPromptTime);
            map.SetOpeningScanTime(Mathf.Max(0f, elapsed - OpeningHudTimeline.ContourStart + MapTestSceneController.OpeningContourStart));
            if (elapsed >= OpeningHudTimeline.TerrainLock)
                RestoreWorldRenderers();
            ApplyShake();
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

        private static void SampleShake(float elapsed, float testRumbleRemaining,
            out Vector2 offset, out float roll, out Vector2 motors)
        {
            offset = Vector2.zero;
            roll = 0f;
            motors = new Vector2(.46f, .49f) * Mathf.Clamp01(testRumbleRemaining / .11f);
            if (elapsed >= 0f && elapsed < OpeningHudTimeline.StatusScanDuration)
            {
                float decay = 1f - elapsed / OpeningHudTimeline.StatusScanDuration;
                float stutter = .72f + .28f * Mathf.Cos(elapsed * 13f * 2f * Mathf.PI);
                motors = Vector2.Max(motors, new Vector2(.45f, .53f) * (decay * stutter));
            }
            foreach (Impact impact in impacts)
            {
                float age = elapsed - impact.Start;
                if (age < 0f || age >= impact.Duration) continue;
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
        }

        private void ApplyShake()
        {
            SampleShake(elapsed, testRumbleRemaining, out Vector2 offset, out float roll, out Vector2 motors);
            cameraShake.SetOpeningRumble(motors);
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
            if (cameraShake != null) cameraShake.StopOpeningRumble();
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

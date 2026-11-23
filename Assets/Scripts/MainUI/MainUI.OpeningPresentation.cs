using System;
using System.Collections.Generic;
using System.Text;
using AnimalGame.Animals.UI;
using AnimalGame.RobotMap;
using AnimalGame.World.Map;
using UnityEngine;
using UnityEngine.UIElements;
using static AnimalGame.MainUI.OpeningHudTimeline;

namespace AnimalGame.MainUI
{
    public sealed partial class MainUI
    {
        private const float OpeningDrivingHintFadeSeconds = .6f;
        private OpeningPresentation openingPresentation;
        private bool navigationTelemetryUnavailable;
        private Vector2 openingScreenShift;
        private float openingScreenRoll;
        private RobotMover openingDrivingHintRobot;
        private Vector2 openingDrivingHintLinePoint, openingDrivingHintEastNormal;
        private float openingDrivingHintOpacity;
        private bool openingDrivingHintDismissed;
        private VisualElement openingSignalPrompt;
        private Label openingSignalMapKey;
        private WorldMapUI openingSignalMap;
        private float openingSignalPromptOpacity;

        internal void BeginOpeningPresentation(Action onTextTyped)
        {
            if (stage == null) Init();
            navigationTelemetryUnavailable = true;
            IsCargoLocked = true;
            UpdateCargoVisibility();
            ApplyUnknownCoordinateLayout();
            TryApplyUnknownCoordinates();
            openingPresentation = new OpeningPresentation(this, onTextTyped);
            powerRing.style.opacity = 1f;
            UpdateStatus();
            openingPresentation.Apply(-1f, -1f);
            openingPresentation.UpdateStandby(0f, 0);
            openingPresentation.UpdateSkipHold(-1f);
        }

        internal void UpdateOpeningStandby(float elapsed, int pecks) =>
            openingPresentation.UpdateStandby(elapsed, pecks);

        internal void EndOpeningStandby() => openingPresentation.EndStandby();

        internal void UpdateOpeningSkipHold(float progress) => openingPresentation.UpdateSkipHold(progress);

        internal void ShowOpeningDrivingHint(RobotMover robot, Vector2 lineSouth, Vector2 lineNorth)
        {
            Vector2 line = lineNorth - lineSouth;
            openingDrivingHintRobot = robot;
            openingDrivingHintLinePoint = lineSouth;
            openingDrivingHintEastNormal = new Vector2(line.y, -line.x);
            openingDrivingHintOpacity = 1f;
            openingDrivingHintDismissed = false;
            UpdateOpeningDrivingHint();
        }

        private void AdvanceOpeningDrivingHint(float deltaTime)
        {
            if (openingDrivingHintOpacity <= 0f || BlocksGameplay) return;
            if (!openingDrivingHintDismissed && Vector2.Dot(openingDrivingHintEastNormal,
                    openingDrivingHintRobot.MapPosition - openingDrivingHintLinePoint) <= 0f) return;

            openingDrivingHintDismissed = true;
            openingDrivingHintOpacity = Mathf.MoveTowards(openingDrivingHintOpacity, 0f,
                deltaTime / OpeningDrivingHintFadeSeconds);
            UpdateOpeningDrivingHint();
        }

        private void UpdateOpeningDrivingHint()
        {
            if (openingDrivingHint == null) return;
            bool visible = openingDrivingHintOpacity > 0f && !BlocksGameplay;
            openingDrivingHint.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            openingDrivingHint.style.opacity = openingDrivingHintOpacity;
            openingDrivingHint.style.marginLeft = 150f * fit;
            openingDrivingHint.style.marginTop = -132f * fit;
            openingDrivingHint.style.scale = new Scale(new Vector3(fit, fit, 1f));
        }

        internal void ShowOpeningSignalPrompt(WorldMapUI worldMap)
        {
            openingSignalMap = worldMap;
            openingSignalPromptOpacity = 0f;
        }

        private void AdvanceOpeningSignalPrompt(float deltaTime)
        {
            if (openingSignalMap == null) return;
            if (openingSignalMap.IsOpen)
            {
                openingSignalMap = null;
                openingSignalPrompt.style.display = DisplayStyle.None;
                return;
            }

            bool visible = !BlocksGameplay;
            openingSignalPrompt.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            openingSignalPromptOpacity = Mathf.MoveTowards(openingSignalPromptOpacity, 1f, deltaTime / .24f);
            openingSignalPrompt.style.opacity = openingSignalPromptOpacity;
            openingSignalPrompt.style.marginLeft = 270f * fit;
            openingSignalPrompt.style.marginTop = -60f * fit;
            openingSignalPrompt.style.scale = new Scale(new Vector3(fit, fit, 1f));
            openingSignalMapKey.text = WorldMapUI.OpenControlLabel;
        }

        public void UpdateOpeningPresentation(float elapsed, float testPromptTime) =>
            openingPresentation.Apply(elapsed, testPromptTime);

        public void EndOpeningPresentation()
        {
            if (openingPresentation == null) return;
            SetOpeningGoalModule(0f, showTab: false);
            openingPresentation.Release();
            openingPresentation = null;
            SetOpeningScreenShake(Vector2.zero, 0f);
            SetDate(gameTime);
            RefreshTelemetry(System.DateTime.Now, true);
            UpdateStatus();
        }

        /// <summary>Normalized viewport translation with down-positive Y and clockwise roll.</summary>
        public void SetOpeningScreenShake(Vector2 normalizedScreenShift, float rollDegrees)
        {
            openingScreenShift = normalizedScreenShift;
            openingScreenRoll = rollDegrees;
            ApplyPhotoTransform();
        }

        private Vector2 OpeningShakeOffset => openingScreenShift == Vector2.zero
            ? Vector2.zero : Vector2.Scale(openingScreenShift, stage.layout.size);

        private void ApplyUnknownCoordinateLayout()
        {
            VisualElement row = latitudeLabel.parent;
            row.style.width = 340f;
            row.style.alignSelf = Align.FlexEnd;
        }

        private bool TryApplyUnknownCoordinates()
        {
            if (!navigationTelemetryUnavailable) return false;
            latitudeHemisphereLabel.text = "N";
            longitudeHemisphereLabel.text = "E";
            latitudeLabel.text = longitudeLabel.text = UnknownReading;
            return true;
        }

        private sealed class OpeningPresentation
        {
            private const float TerminalLeft = 272f;
            private const float StandbyPromptTop = 148f;
            private const string Amber = "<color=#D4B854>";
            private const string PromptButton = "A";
            private const string HighlightedPromptButton = Amber + PromptButton + "</color>";
            private const string ContinueText = "PRESS " + HighlightedPromptButton + " TO CONTINUE";
            private const string RestartText = "PRESS " + HighlightedPromptButton + " TO RESTART";
            private const string NextTestPrefix = "PRESS ";
            private const string NextTestText = NextTestPrefix + PromptButton + " TO PERFORM NEXT TEST";
            private const float NextTestTypingInterval = .027f;
            private static readonly float NextTestTextReady = TypingEnd(NextTestText, 0f, NextTestTypingInterval);
            private const string HiddenKeyword = "<color=#00000000>";
            private readonly MainUI ui;
            private readonly Action onTextTyped;
            private readonly VisualElement position, altitude;
            private readonly VisualElement altitudeDivider, altitudeTick;
            private readonly VisualElement terminal, standby, nextTestPrompt, standbyPrompt, skipPrompt;
            private readonly Label titleLabel, percentage, nextTestLabel, nextTestEllipsis, standbyLabel, standbyEllipsis;
            private readonly ArchiveHoldCircleElement skipCircle;
            private readonly RestartMeter meter;
            private readonly Label[] logLabels;
            private readonly StringBuilder typed = new StringBuilder(128);
            private readonly Dictionary<float, float> keywordStartedAt = new();
            private int typedLogCharacters, nextTestTypedCharacters;
            private readonly ScanMask statusMask, clockMask, positionMask, altitudeMask, compassMask, levelMask, receiverMask;

            private static readonly OpeningScanBand[] FrameBands =
            {
                new OpeningScanBand(.14f, .27f, .26f),
                new OpeningScanBand(.39f, .57f, FrameScanDuration, .11f, .23f),
                new OpeningScanBand(.72f, .87f, .34f)
            };

            private readonly struct LogPart
            {
                public readonly string Text;
                public readonly float Start, Interval, Ready;
                public readonly int KeywordLength;
                public readonly bool Accent;

                public LogPart(string text, float start, float interval = 0f, bool accent = false)
                {
                    Text = text;
                    Start = start;
                    Interval = interval;
                    Ready = TypingEnd(text, start, interval);
                    KeywordLength = accent ? text.TrimEnd('.').Length : 0;
                    Accent = accent;
                }
            }

            private static readonly LogPart[][] Logs =
            {
                new[] { new LogPart(PowerMessage, PowerStart, PowerTypingInterval, true) },
                new[] { new LogPart("QUERYING SYSTEM LOG...", SystemLogStart, .031f),
                    new LogPart(" LAST RUN: ", SystemLogStart + .80f, .031f),
                    new LogPart("5_04_2650", LastRunFound, accent: true), new LogPart("...", LastRunFound) },
                new[] { new LogPart("QUERYING CURRENT TIME...", ClockQuery, .028f),
                    new LogPart(" " + UnknownReading, ClockResult, accent: true) },
                new[] { new LogPart("TESTING GLOBAL POSITIONING SYSTEM......", Positioning.Start, .028f),
                    new LogPart(" ....", Positioning.Start + 1.16f, .030f), new LogPart(" DATA INVALID", Positioning.Result, accent: true) },
                new[] { new LogPart("TESTING ALTIMETER......", Altitude.Start, .030f), new LogPart(" FUNCTIONING!", Altitude.Result, accent: true) },
                new[] { new LogPart("TESTING ELECTRONIC COMPASS..", CompassTest.Start, .030f), new LogPart(" FUNCTIONING!", CompassTest.Result, accent: true) },
                new[] { new LogPart("TESTING LASER LEVEL......", Level.Start, .030f), new LogPart(" FUNCTIONING!", Level.Result, accent: true) },
                new[] { new LogPart("TESTING AUDIO RECEIVER.......", AudioTest, .030f), new LogPart(" FUNCTIONING", AudioResult, accent: true) },
                new[] { new LogPart("ARM TESTING...", ArmLog, .031f), new LogPart(" ...", ArmLog + .57f),
                    new LogPart(" ...", ArmLog + .79f), new LogPart("BASICALLY FUNCTIONING", ArmEnd, accent: true) },
                new[] { new LogPart(GoalModuleMessage, GoalModuleLogStart, GoalModuleTypingInterval) },
                new[] { new LogPart("MODULE SYSTEM FAULT.", GoalModuleFault, accent: true) },
                new[] { new LogPart(SensorMessage, SensorLogStart, SensorTypingInterval, true) }
            };

            public OpeningPresentation(MainUI ui, Action onTextTyped)
            {
                this.ui = ui;
                this.onTextTyped = onTextTyped;
                position = ui.latitudeLabel.parent;
                altitude = ui.altitudeLabel.parent;
                altitudeDivider = ui.coordinates[1];
                altitudeTick = ui.coordinates[3];
                statusMask = new ScanMask(ui.status, ui.status, true, 0f, 0f,
                    new OpeningScanBand(.045f, .29f, .31f),
                    new OpeningScanBand(.32f, .52f, .44f, .13f, .26f),
                    new OpeningScanBand(.57f, .71f, .21f),
                    new OpeningScanBand(.74f, .90f, StatusScanDuration, .20f, .33f));
                clockMask = new ScanMask(ui.stage, ui.time, true, 0f, 0f,
                    new OpeningScanBand(.0705f, .0983f, .39f), new OpeningScanBand(.106f, .1465f, ClockScanDuration, .15f, .25f),
                    new OpeningScanBand(.156f, .171f, .31f));
                positionMask = new ScanMask(ui.stage, position, false, 0f, 0f,
                    new OpeningScanBand(.859f, .875f, .22f), new OpeningScanBand(.90f, .924f, .36f, .09f, .16f));
                altitudeMask = new ScanMask(ui.stage, altitude, false, 0f, 0f,
                    new OpeningScanBand(.8625f, .8985f, .27f), new OpeningScanBand(.9115f, .948f, .46f, .12f, .23f));
                compassMask = new ScanMask(ui.stage, ui.compassHost, false, 0f, 9f,
                    new OpeningScanBand(.348f, .40f, .27f), new OpeningScanBand(.413f, .496f, .50f, .12f, .23f),
                    new OpeningScanBand(.546f, .617f, .37f));
                levelMask = new ScanMask(ui.stage, ui.levelIndicator, false, 12f, 0f,
                    new OpeningScanBand(.47f, .488f, .26f), new OpeningScanBand(.493f, .5218f, .47f, .12f, .23f),
                    new OpeningScanBand(.5315f, .535f, .34f));
                receiverMask = new ScanMask(ui.stage, ui.sound, false, 0f, 0f,
                    new OpeningScanBand(.782f, .794f, .33f), new OpeningScanBand(.8015f, .8385f, ReceiverScanDuration, .13f, .23f));

                terminal = new VisualElement { name = "OpeningRestart", pickingMode = PickingMode.Ignore };
                terminal.style.position = Position.Absolute;
                terminal.style.transformOrigin = new TransformOrigin(new Length(0f), new Length(0f));
                terminal.style.width = 692f;
                ui.stage.Add(terminal);

                var title = new VisualElement { pickingMode = PickingMode.Ignore };
                title.style.flexDirection = FlexDirection.Row;
                title.style.justifyContent = Justify.SpaceBetween;
                title.style.alignItems = Align.Center;
                titleLabel = MakeLabel(string.Empty, 32f, Color.white);
                title.Add(titleLabel);
                percentage = MakeLabel("0%", 25f, HudDrawing.Yellow);
                title.Add(percentage);
                terminal.Add(title);

                meter = new RestartMeter();
                meter.style.height = 11f;
                meter.style.marginTop = 10f;
                meter.style.marginBottom = 10f;
                terminal.Add(meter);

                logLabels = new Label[Logs.Length];
                for (int i = 0; i < Logs.Length; i++)
                {
                    Label label = MakeLabel(string.Empty, 20f, new Color32(201, 205, 208, 255));
                    label.enableRichText = true;
                    label.style.marginBottom = 4f;
                    terminal.Add(label);
                    logLabels[i] = label;
                }
                nextTestLabel = MakeLabel(string.Empty, 20f, Color.white);
                nextTestLabel.enableRichText = true;
                nextTestLabel.name = "OpeningNextTest";
                nextTestPrompt = MakePrompt(nextTestLabel, 20f, out nextTestEllipsis);
                nextTestPrompt.style.marginTop = 10f;
                terminal.Add(nextTestPrompt);

                standby = new VisualElement { name = "OpeningStandby", pickingMode = PickingMode.Ignore };
                standby.style.position = Position.Absolute;
                standby.style.left = standby.style.top = standby.style.right = standby.style.bottom = 0f;
                standby.style.backgroundColor = Color.black;
                standbyLabel = MakeLabel(ContinueText, 20f, Color.white);
                standbyLabel.name = "OpeningStandbyPrompt";
                standbyLabel.enableRichText = true;
                standbyPrompt = MakePrompt(standbyLabel, 20f, out standbyEllipsis);
                standbyPrompt.style.position = Position.Absolute;
                standbyPrompt.style.transformOrigin = new TransformOrigin(0f, 0f);
                standby.Add(standbyPrompt);
                ui.root.Add(standby);

                skipPrompt = new VisualElement { name = "OpeningSkipPrompt", pickingMode = PickingMode.Ignore };
                skipPrompt.style.position = Position.Absolute;
                skipPrompt.style.flexDirection = FlexDirection.Row;
                skipPrompt.style.alignItems = Align.Center;
                skipPrompt.style.transformOrigin = new TransformOrigin(0f, new Length(100f, LengthUnit.Percent));
                skipPrompt.style.paddingLeft = skipPrompt.style.paddingRight = 8f;
                skipPrompt.style.paddingTop = skipPrompt.style.paddingBottom = 4f;
                skipPrompt.style.backgroundColor = new Color(0f, 0f, 0f, .84f);
                skipCircle = MakeHoldCircle("OpeningSkipProgress", 24f);
                skipCircle.style.marginRight = 8f;
                skipPrompt.Add(skipCircle);
                Label skipLabel = MakeLabel("HOLD " + Amber + "ESC</color> TO SKIP", 16f, Color.white);
                skipLabel.enableRichText = true;
                skipPrompt.Add(skipLabel);
                ui.root.Add(skipPrompt);
            }

            public void UpdateStandby(float elapsed, int pecks)
            {
                standby.style.backgroundColor = pecks > 0 ? Color.clear : Color.black;
                standbyLabel.text = pecks >= WakePecksRequired ? RestartText : ContinueText;
                standbyPrompt.style.left = TerminalLeft * ui.fit;
                standbyPrompt.style.top = StandbyPromptTop * ui.fit;
                standbyPrompt.style.scale = new Scale(new Vector3(ui.fit, ui.fit, 1f));
                float promptAge = elapsed - WakePromptDelay;
                standbyPrompt.style.opacity = skipPrompt.style.opacity = promptAge >= 0f ? 1f : 0f;
                standbyEllipsis.style.opacity = BlinkEllipsis(promptAge);
            }

            public void EndStandby() => standby.RemoveFromHierarchy();

            public void UpdateSkipHold(float progress)
            {
                skipPrompt.style.left = ui.viewportOffset.x + 44f * ui.fit;
                skipPrompt.style.bottom = ui.viewportOffset.y + 44f * ui.fit;
                skipPrompt.style.scale = new Scale(new Vector3(ui.fit, ui.fit, 1f));
                UpdateHoldCircle(skipCircle, progress);
            }

            private static ArchiveHoldCircleElement MakeHoldCircle(string name, float size)
            {
                var circle = new ArchiveHoldCircleElement { name = name, pickingMode = PickingMode.Ignore };
                circle.style.width = circle.style.height = size;
                var radius = new Length(50f, LengthUnit.Percent);
                circle.style.borderTopLeftRadius = circle.style.borderTopRightRadius = radius;
                circle.style.borderBottomLeftRadius = circle.style.borderBottomRightRadius = radius;
                circle.style.borderLeftWidth = circle.style.borderRightWidth = 1f;
                circle.style.borderTopWidth = circle.style.borderBottomWidth = 1f;
                Color trackColor = HudDrawing.Yellow;
                trackColor.a = .30f;
                circle.style.borderLeftColor = circle.style.borderRightColor = trackColor;
                circle.style.borderTopColor = circle.style.borderBottomColor = trackColor;
                return circle;
            }

            private static void UpdateHoldCircle(ArchiveHoldCircleElement circle, float holdProgress)
            {
                circle.style.visibility = holdProgress >= 0f ? Visibility.Visible : Visibility.Hidden;
                float progress = Mathf.Clamp01(holdProgress);
                if (circle.Progress != progress) circle.Progress = progress;
            }

            private static Label MakeLabel(string value, float size, Color color)
            {
                var label = new Label(value) { pickingMode = PickingMode.Ignore };
                label.style.fontSize = size;
                label.style.color = color;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.flexShrink = 0f;
                return label;
            }

            private static VisualElement MakePrompt(Label text, float size, out Label ellipsis)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                ellipsis = MakeLabel("...", size, text.style.color.value);
                row.Add(text);
                row.Add(ellipsis);
                return row;
            }

            private static float BlinkEllipsis(float age) => age % .80f < .48f ? 1f : 0f;

            public void Apply(float elapsed, float testPromptTime)
            {
                float now = Time.unscaledTime;
                ui.SetOpeningGoalModule(GoalModuleOpenness(elapsed), showTab: elapsed >= GoalModuleReady);
                ui.status.style.opacity = elapsed >= 0f ? 1f : 0f;
                statusMask.Apply(elapsed);
                ui.ring.style.opacity = Ease(elapsed, ContourStart, ContourLock);

                bool unknownVisible = VisibleKeyword(ClockResult, elapsed, now);
                bool clockDisplayReady = elapsed >= ClockDisplayReady;
                ui.time.style.opacity = elapsed >= LastRunFound ? 1f : 0f;
                clockMask.Apply(clockDisplayReady ? ClockScanDuration : elapsed - LastRunFound);
                ui.dateLabel.style.opacity = clockDisplayReady ? 1f : 0f;
                ui.dateLabel.text = elapsed < ClockResult ? "--_--\n----" : unknownVisible ? UnknownReading : string.Empty;
                ApplyInstrument(position, positionMask, elapsed, Positioning.Result, Positioning.Ready);
                float altitudeOpacity = ApplyInstrument(altitude, altitudeMask, elapsed, Altitude.Result, Altitude.Ready);
                altitudeDivider.style.opacity = altitudeTick.style.opacity = altitudeOpacity;
                ui.frame.style.opacity = elapsed >= Frame ? 1f : 0f;
                int frameMissing = elapsed < FrameReady ? OpeningScanBand.Missing(FrameBands, elapsed - Frame) : 0;
                ui.frame.SetOpeningScan(FrameBands, frameMissing);
                ApplyInstrument(ui.compassHost, compassMask, elapsed, CompassTest.Result, CompassTest.Ready);
                ApplyInstrument(ui.levelIndicator, levelMask, elapsed, Level.Result, Level.Ready);
                ApplyInstrument(ui.sound, receiverMask, elapsed, AudioResult, ReceiverReady);

                float alpha = elapsed < RestartStart ? 0f : 1f - Ease(elapsed, SensorLogEnd + .14f, SensorLogEnd + .58f);
                terminal.style.opacity = alpha;
                if (alpha <= 0f) return;
                terminal.style.left = TerminalLeft * ui.fit;
                terminal.style.top = 62.6f * ui.fit;
                terminal.style.scale = new Scale(new Vector3(ui.fit, ui.fit, 1f));
                int checks = 0;
                foreach (float check in InstrumentChecks)
                    if (elapsed >= check) checks++;
                int progress = elapsed >= SensorLogEnd ? 100 : elapsed >= ArmEnd ? 96 : 11 * checks;
                if (meter.SetProgress(progress))
                    percentage.text = $"{progress}%";
                bool typedCharacter = false;
                if (testPromptTime <= 0f)
                {
                    int titleCount = TypedCharacterCount(RestartTitle, RestartStart, RestartTypingInterval, elapsed);
                    typedCharacter = SetTypedLabel(titleLabel, RestartTitle, titleCount);
                }
                titleLabel.style.opacity = VisibleKeyword(RestartTitleReady, elapsed, now) ? 1f : 0f;
                typedCharacter |= TypeLog(elapsed, now);
                bool showNextTest = testPromptTime >= 0f;
                nextTestPrompt.style.display = showNextTest ? DisplayStyle.Flex : DisplayStyle.None;
                if (showNextTest)
                {
                    int count = TypedCharacterCount(NextTestText, 0f, NextTestTypingInterval, testPromptTime);
                    if (count != nextTestTypedCharacters)
                    {
                        int buttonEnd = NextTestPrefix.Length + PromptButton.Length;
                        nextTestLabel.text = count < buttonEnd ? NextTestText.Substring(0, count)
                            : NextTestPrefix + HighlightedPromptButton
                                + NextTestText.Substring(buttonEnd, count - buttonEnd);
                        typedCharacter |= count > nextTestTypedCharacters;
                        nextTestTypedCharacters = count;
                    }
                    nextTestEllipsis.style.opacity = testPromptTime >= NextTestTextReady
                        ? BlinkEllipsis(testPromptTime - NextTestTextReady) : 0f;
                }
                else if (nextTestTypedCharacters > 0)
                {
                    nextTestTypedCharacters = 0;
                    nextTestLabel.text = string.Empty;
                }
                if (typedCharacter) onTextTyped();
            }

            private bool VisibleKeyword(float ready, float elapsed, float now)
            {
                if (elapsed < ready) return true;
                // Results keep blinking while the test clock is paused for confirmation.
                if (!keywordStartedAt.TryGetValue(ready, out float started))
                    keywordStartedAt.Add(ready, started = now);
                return BlinkKeyword(now - started);
            }

            private static bool BlinkKeyword(float age) =>
                age < 0f || age >= ClockBlinkDuration || age % ClockBlinkPeriod < .13f;

            private static float ApplyInstrument(VisualElement element, ScanMask mask, float elapsed, float start, float ready)
            {
                float blinkAge = elapsed - (ready + BlinkDelay);
                float opacity = elapsed < start ? 0f : blinkAge >= 0f && elapsed < ready + InstrumentFeedbackDuration
                    && blinkAge % BlinkInterval < BlinkDimTime ? .18f : 1f;
                element.style.opacity = opacity;
                mask.Apply(elapsed - start);
                return opacity;
            }

            private bool TypeLog(float elapsed, float now)
            {
                int revealedCharacters = 0;
                for (int i = 0; i < Logs.Length; i++)
                {
                    typed.Clear();
                    foreach (LogPart part in Logs[i])
                    {
                        if (elapsed < part.Start) continue;
                        int count = TypedCharacterCount(part.Text, part.Start, part.Interval, elapsed);
                        if (part.Interval > 0f) revealedCharacters += count;
                        if (typed.Length == 0) typed.Append("–   ");
                        if (!part.Accent)
                            typed.Append(part.Text, 0, count);
                        else if (VisibleKeyword(part.Ready, elapsed, now))
                            typed.Append(Amber).Append(part.Text, 0, count).Append("</color>");
                        else
                        {
                            typed.Append(HiddenKeyword).Append(part.Text, 0, part.KeywordLength).Append("</color>");
                            if (count > part.KeywordLength)
                                typed.Append(Amber).Append(part.Text, part.KeywordLength, count - part.KeywordLength).Append("</color>");
                        }
                    }
                    Label label = logLabels[i];
                    label.style.display = typed.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                    if (!typed.Equals(label.text.AsSpan())) label.text = typed.ToString();
                }
                bool advanced = revealedCharacters > typedLogCharacters;
                typedLogCharacters = revealedCharacters;
                return advanced;
            }

            private static bool SetTypedLabel(Label label, string text, int count)
            {
                int previousCount = label.text.Length;
                if (count != previousCount) label.text = text.Substring(0, count);
                return count > previousCount;
            }

            private static int TypedCharacterCount(string text, float start, float interval, float elapsed) =>
                interval > 0f && elapsed < TypingEnd(text, start, interval)
                    ? 1 + Mathf.FloorToInt((elapsed - start) / interval)
                    : text.Length;

            public void Release()
            {
                foreach (VisualElement element in new[] { ui.status, ui.ring, ui.time, ui.dateLabel,
                    position, altitude, altitudeDivider, altitudeTick, ui.frame, ui.compassHost, ui.levelIndicator, ui.sound })
                    element.style.opacity = StyleKeyword.Null;
                foreach (ScanMask mask in new[] { statusMask, clockMask, positionMask, altitudeMask, compassMask, levelMask, receiverMask })
                    mask.RemoveFromHierarchy();
                ui.frame.SetOpeningScan(null, 0);
                terminal.RemoveFromHierarchy();
                skipPrompt.RemoveFromHierarchy();
                EndStandby();
            }

            // The pre-map black background makes these clipped local bands hard cuts in the native HUD.
            private sealed class ScanMask : VisualElement
            {
                private readonly VisualElement reference;
                private readonly OpeningScanBand[] bands;
                private readonly bool horizontal;
                private readonly float topInset, bottomInset;
                private int missing;

                public ScanMask(VisualElement reference, VisualElement element, bool horizontal, float topInset, float bottomInset,
                    params OpeningScanBand[] bands)
                {
                    this.reference = reference;
                    this.bands = bands;
                    this.horizontal = horizontal;
                    this.topInset = topInset;
                    this.bottomInset = bottomInset;
                    pickingMode = PickingMode.Ignore;
                    style.position = Position.Absolute;
                    style.left = style.right = style.top = style.bottom = 0f;
                    generateVisualContent += Draw;
                    element.Add(this);
                }

                public void Apply(float age)
                {
                    int next = OpeningScanBand.Missing(bands, age);
                    if (missing == next) return;
                    missing = next;
                    MarkDirtyRepaint();
                }

                private void Draw(MeshGenerationContext context)
                {
                    if (missing == 0 || contentRect.width <= 0f || contentRect.height <= topInset + bottomInset) return;
                    Painter2D p = context.painter2D;
                    p.fillColor = Color.black;
                    for (int i = 0; i < bands.Length; i++)
                    {
                        if ((missing & (1 << i)) == 0) continue;
                        OpeningScanBand band = bands[i];
                        if (horizontal)
                        {
                            float a = Mathf.Max(0f, reference.ChangeCoordinatesTo(this, new Vector2(0f, reference.layout.height * band.From)).y);
                            float b = Mathf.Min(contentRect.height, reference.ChangeCoordinatesTo(this, new Vector2(0f, reference.layout.height * band.To)).y);
                            if (b > a) HudDrawing.Rect(p, 0f, a, contentRect.width, b - a);
                        }
                        else
                        {
                            float a = Mathf.Max(0f, reference.ChangeCoordinatesTo(this, new Vector2(reference.layout.width * band.From, 0f)).x);
                            float b = Mathf.Min(contentRect.width, reference.ChangeCoordinatesTo(this, new Vector2(reference.layout.width * band.To, 0f)).x);
                            if (b > a) HudDrawing.Rect(p, a, topInset, b - a, contentRect.height - topInset - bottomInset);
                        }
                    }
                }
            }

            private sealed class RestartMeter : VisualElement
            {
                private int progress;

                public RestartMeter()
                {
                    pickingMode = PickingMode.Ignore;
                    generateVisualContent += Draw;
                }

                public bool SetProgress(int value)
                {
                    if (value == progress) return false;
                    progress = value;
                    MarkDirtyRepaint();
                    return true;
                }

                private void Draw(MeshGenerationContext context)
                {
                    const int segments = 8;
                    const float gap = 5f, skew = 4f;
                    float step = contentRect.width / segments;
                    if (step <= gap + skew) return;
                    Painter2D p = context.painter2D;
                    for (int i = 0; i < segments; i++)
                    {
                        float x = i * step;
                        DrawSegment(p, x, step - gap, contentRect.height, skew, new Color32(40, 40, 36, 255));
                        float portion = Mathf.Clamp01(progress / 100f * segments - i);
                        if (portion > 0f) DrawSegment(p, x, (step - gap) * portion, contentRect.height, skew, HudDrawing.Yellow);
                    }
                }

                private static void DrawSegment(Painter2D p, float x, float width, float height, float skew, Color color)
                {
                    p.fillColor = color;
                    HudDrawing.Polygon(p, new[] { new Vector2(x + skew, 0f), new Vector2(x + skew + width, 0f),
                        new Vector2(x + width, height), new Vector2(x, height) }, true);
                }
            }
        }
    }
}

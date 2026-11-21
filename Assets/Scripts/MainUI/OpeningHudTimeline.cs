namespace AnimalGame.MainUI
{
    /// <summary>The clock shared by the opening's native HUD, arm test and map reveal.</summary>
    public static class OpeningHudTimeline
    {
        internal const float StatusScanDuration = .50f;
        public const float BodyStart = StatusScanDuration + .12f;
        public const float BodyReady = StatusScanDuration + .48f;

        internal const float RestartStart = StatusScanDuration + .54f;
        internal const string RestartTitle = "SYSTEM RESTART";
        internal const float RestartTypingInterval = .045f;
        internal static readonly float RestartTitleReady = TypingEnd(RestartTitle, RestartStart, RestartTypingInterval);
        internal static readonly float PowerStart = RestartTitleReady + .10f;
        internal const string PowerMessage = "POWER RESTORED";
        internal const float PowerTypingInterval = .043f;
        internal static readonly float PowerReady = TypingEnd(PowerMessage, PowerStart, PowerTypingInterval);
        internal static readonly float SystemLogStart = PowerReady + .12f;
        internal static readonly float LastRunFound = SystemLogStart + 1.24f;
        internal const float ClockScanDuration = .49f;
        internal static readonly float ClockDisplayReady = LastRunFound + ClockScanDuration;
        internal static readonly float ClockQuery = ClockDisplayReady + .21f;
        internal static readonly float ClockResult = ClockQuery + .83f;
        internal const float ClockBlinkPeriod = .23f;
        internal const float ClockBlinkDuration = 3 * ClockBlinkPeriod;
        internal static readonly float ClockReady = ClockResult + ClockBlinkDuration;
        internal const string UnknownReading = "UNKNOWN";

        internal const float BlinkDelay = .14f;
        internal const float BlinkInterval = .17f;
        internal const float BlinkDimTime = .07f;
        internal const float BlinkDuration = BlinkInterval + BlinkDimTime;
        internal const float InstrumentFeedbackDuration = BlinkDelay + BlinkDuration;

        internal readonly struct InstrumentTest
        {
            public readonly float Start, Result, Ready, FeedbackReady;

            public InstrumentTest(float previousReady, float typingTime, float scanTime)
            {
                Start = previousReady + .12f;
                Result = Start + typingTime;
                Ready = Result + scanTime;
                FeedbackReady = Ready + InstrumentFeedbackDuration;
            }
        }

        internal static readonly InstrumentTest Positioning = new InstrumentTest(ClockReady, 1.40f, .36f);
        internal static readonly InstrumentTest Altitude = new InstrumentTest(Positioning.FeedbackReady, .79f, .46f);
        internal static readonly float Frame = Altitude.FeedbackReady;
        internal const float FrameScanDuration = .46f;
        internal static readonly float FrameReady = Frame + FrameScanDuration;
        internal static readonly InstrumentTest CompassTest = new InstrumentTest(FrameReady, .95f, .50f);
        internal static readonly InstrumentTest Level = new InstrumentTest(CompassTest.FeedbackReady, .85f, .47f);
        internal static readonly float AudioTest = Level.FeedbackReady + .12f;
        internal static readonly float AudioResult = AudioTest + 1f;
        internal const float ReceiverScanDuration = .49f;
        internal static readonly float ReceiverReady = AudioResult + ReceiverScanDuration;
        internal static readonly float ArmLog = ReceiverReady + InstrumentFeedbackDuration + .12f;
        public static readonly float ArmStart = ArmLog + 1.24f;
        public const float ArmDuration = 1.94f;
        public static readonly float ArmEnd = ArmStart + ArmDuration;
        public static readonly float HeadingStart = ArmStart;
        public static readonly float PlayerStateStart = HeadingStart + .13f;
        internal static readonly float SensorLogStart = ArmEnd + .14f;
        internal const string SensorMessage = "RESTARTING SENSORS......";
        internal const float SensorTypingInterval = .030f;
        public static readonly float SensorLogEnd = TypingEnd(SensorMessage, SensorLogStart, SensorTypingInterval);
        public static readonly float ContourStart = SensorLogEnd + .18f;
        public static readonly float ContourLock = ContourStart + .79f;
        public static readonly float TerrainStart = ContourStart + 1.07f;
        public static readonly float TerrainDrop = ContourStart + 1.29f;
        public static readonly float TerrainLock = ContourStart + 1.49f;
        public static readonly float Duration = ContourStart + 3.30f;

        // Confirm the start, then wait after each line and its associated visual feedback.
        internal static readonly float[] TestPauses =
        {
            RestartTitleReady, PowerReady, ClockDisplayReady, ClockReady,
            Positioning.FeedbackReady, FrameReady, CompassTest.FeedbackReady, Level.FeedbackReady,
            ReceiverReady + InstrumentFeedbackDuration,
            ArmEnd, SensorLogEnd
        };

        internal static readonly float[] InstrumentChecks =
        {
            PowerReady, LastRunFound, ClockResult, Positioning.Ready,
            Altitude.Ready, CompassTest.Ready, Level.Ready, ReceiverReady
        };

        internal static float TypingEnd(string message, float start, float interval) => start + (message.Length - 1) * interval;

        internal static float Ease(float t, float start, float end)
        {
            float x = UnityEngine.Mathf.Clamp01((t - start) / (end - start));
            return x * x * (3f - 2f * x);
        }
    }

    internal readonly struct OpeningScanBand
    {
        public readonly float From, To;
        private readonly float lockAt, retryAt, dropAt;

        public OpeningScanBand(float from, float to, float lockAt, float retryAt = 0, float dropAt = 0)
        {
            From = from;
            To = to;
            this.lockAt = lockAt;
            this.retryAt = retryAt;
            this.dropAt = dropAt;
        }

        public static int Missing(OpeningScanBand[] bands, float age)
        {
            int mask = 0;
            for (int i = 0; i < bands.Length; i++)
            {
                OpeningScanBand band = bands[i];
                if (age < band.lockAt && (age < band.retryAt || age >= band.dropAt)) mask |= 1 << i;
            }
            return mask;
        }
    }
}

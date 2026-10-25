using UnityEngine;

namespace AnimalGame.Tutorial
{
    public enum BalanceTutorialStep { ReadIndicator, AdjustWeight, ReadRisk, Completed }

    /// <summary>Actual local counterweight and displayed balance point, sampled after native simulation.</summary>
    public readonly struct BalanceTutorialSample
    {
        public readonly float DeltaTime;
        public readonly bool Valid, PanelExpanded;
        public readonly Vector2 RawRightStick, PlayerCounterbalance, BalanceOffset;

        public BalanceTutorialSample(float dt, bool valid, bool panelExpanded, Vector2 rawRightStick,
            Vector2 playerCounterbalance, Vector2 balanceOffset)
        {
            DeltaTime = dt; Valid = valid; PanelExpanded = panelExpanded;
            RawRightStick = rawRightStick; PlayerCounterbalance = playerCounterbalance; BalanceOffset = balanceOffset;
        }
    }

    /// <summary>Observes one effective manual weight adjustment without changing balance physics.</summary>
    public sealed class BalanceTutorialProgress
    {
        private readonly float indicatorReadingSeconds, riskReadingSeconds, requiredPointMovement;
        private Vector2 baselineCounterbalance, baselineOffset;
        private bool armed;
        private float movement;
        private bool satisfied;

        public BalanceTutorialStep Step { get; private set; }
        public bool IsComplete => Step == BalanceTutorialStep.Completed;
        public bool CanContinue => IsComplete || satisfied;
        public float ReadingSeconds { get; private set; }
        public bool NeedsNeutral => Step == BalanceTutorialStep.AdjustWeight && !armed;
        public float Value => Step switch
        {
            BalanceTutorialStep.AdjustWeight => movement,
            BalanceTutorialStep.Completed => 1f,
            _ => ReadingSeconds
        };
        public float Target => Step switch
        {
            BalanceTutorialStep.ReadIndicator => indicatorReadingSeconds,
            BalanceTutorialStep.AdjustWeight => requiredPointMovement,
            BalanceTutorialStep.ReadRisk => riskReadingSeconds,
            _ => 1f
        };
        public float NormalizedProgress => IsComplete ? 1f : Mathf.Clamp01(Value / Target);

        public BalanceTutorialProgress(float indicatorReadingSeconds = 3f, float riskReadingSeconds = 3f,
            float requiredPointMovement = .05f)
        {
            this.indicatorReadingSeconds = FiniteMinimum(indicatorReadingSeconds, .1f, 3f);
            this.riskReadingSeconds = FiniteMinimum(riskReadingSeconds, .1f, 3f);
            this.requiredPointMovement = FiniteMinimum(requiredPointMovement, .001f, .05f);
            Reset();
        }

        public void Reset()
        {
            Step = BalanceTutorialStep.ReadIndicator; ReadingSeconds = 0f;
            satisfied = false;
            Disarm();
        }

        /// <summary>Reading time is retained; fresh neutral is required before a resumed manual action.</summary>
        public void Suspend() { if (!satisfied) Disarm(); }

        public bool Continue()
        {
            if (!satisfied || IsComplete) return false;
            MoveTo(Step + 1);
            return true;
        }

        public void Sample(in BalanceTutorialSample sample)
        {
            if (IsComplete || satisfied) return;
            if (!sample.Valid || !sample.PanelExpanded || !float.IsFinite(sample.DeltaTime)
                || sample.DeltaTime <= 0f || sample.DeltaTime > .25f
                || !Finite(sample.RawRightStick) || !Finite(sample.PlayerCounterbalance) || !Finite(sample.BalanceOffset))
            { Suspend(); return; }

            switch (Step)
            {
                case BalanceTutorialStep.ReadIndicator:
                    Read(sample.DeltaTime, indicatorReadingSeconds);
                    break;
                case BalanceTutorialStep.AdjustWeight:
                    Adjust(sample);
                    break;
                case BalanceTutorialStep.ReadRisk:
                    Read(sample.DeltaTime, riskReadingSeconds);
                    break;
            }
        }

        private void Adjust(in BalanceTutorialSample sample)
        {
            float rawMagnitude = sample.RawRightStick.magnitude;
            if (rawMagnitude <= .2f)
            {
                armed = true;
                baselineCounterbalance = sample.PlayerCounterbalance;
                baselineOffset = sample.BalanceOffset;
                movement = 0f;
                return;
            }
            if (!armed || rawMagnitude < .25f) return;
            Vector2 manualChange = sample.PlayerCounterbalance - baselineCounterbalance;
            Vector2 pointChange = sample.BalanceOffset - baselineOffset;
            bool manualMatchesInput = Vector2.Dot(sample.RawRightStick, manualChange) > 0f;
            bool pointMatchesManual = Vector2.Dot(manualChange, pointChange) > 0f;
            if (manualChange.magnitude < .04f || !manualMatchesInput || !pointMatchesManual)
            { movement = 0f; return; }
            movement = Mathf.Min(requiredPointMovement, pointChange.magnitude);
            if (pointChange.magnitude + .000001f >= requiredPointMovement) satisfied = true;
        }

        private void Read(float deltaTime, float duration)
        {
            ReadingSeconds = Mathf.Min(duration, ReadingSeconds + deltaTime);
            if (duration - ReadingSeconds <= duration * .00001f) satisfied = true;
        }
        private void MoveTo(BalanceTutorialStep step)
        { Step = step; ReadingSeconds = 0f; satisfied = false; Disarm(); }
        private void Disarm()
        { armed = false; movement = 0f; baselineCounterbalance = baselineOffset = Vector2.zero; }
        private static bool Finite(Vector2 vector) => float.IsFinite(vector.x) && float.IsFinite(vector.y);
        private static float FiniteMinimum(float value, float minimum, float fallback) =>
            float.IsFinite(value) ? Mathf.Max(minimum, value) : fallback;
    }
}

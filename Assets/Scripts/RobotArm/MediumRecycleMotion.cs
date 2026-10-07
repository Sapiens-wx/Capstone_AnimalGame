using UnityEngine;

namespace AnimalGame.RobotArm
{
    public enum MediumRecyclePhase { Clamp, Load, Push, Lock, Finish, Complete, Processing }

    public readonly struct MediumRecycleFrame
    {
        public readonly float Progress, Effort01, Recoil01, HandRelease01, ProcessingEnvelope01;
        public readonly int Stage;
        public readonly MediumRecyclePhase Phase;
        public MediumRecycleFrame(float progress, int stage, MediumRecyclePhase phase,
            float effort = 0f, float recoil = 0f, float release = 0f, float processing = 0f)
        { Progress = progress; Stage = stage; Phase = phase; Effort01 = effort; Recoil01 = recoil; HandRelease01 = release; ProcessingEnvelope01 = processing; }
    }

    /// <summary>One clock drives rigid feeding, visual effort and the stop-feedback markers.</summary>
    public static class MediumRecycleMotion
    {
        private const float LoadFraction = .22f, PushFraction = .50f;
        private static float Stroke(Vector3 durations, int stage) => Mathf.Max(.01f, durations[stage]);
        public static float ProcessingDuration(float finish, float processing)
            => Mathf.Max(Mathf.Max(.01f, finish), processing);
        public static float ProcessingStartTime(float clamp, Vector3 strokes) => StartTime(clamp, strokes, 3);
        public static float Duration(float clamp, Vector3 strokes, float finish, float processing = .95f)
            => ProcessingStartTime(clamp, strokes) + ProcessingDuration(finish, processing);
        private static float StartTime(float clamp, Vector3 strokes, int stage)
        {
            float time = Mathf.Max(0f, clamp);
            for (int i = 0; i < stage; i++) time += Stroke(strokes, i);
            return time;
        }
        public static float PushTime(float clamp, Vector3 strokes, int stage)
            => StartTime(clamp, strokes, stage) + Stroke(strokes, stage) * LoadFraction;
        public static float StopTime(float clamp, Vector3 strokes, int stage)
            => StartTime(clamp, strokes, stage) + Stroke(strokes, stage) * (LoadFraction + PushFraction);

        public static MediumRecycleFrame Sample(float elapsed, float clamp, Vector3 strokes, float finish, float lockRecoilFraction,
            float processing = .95f, float processingFade = .10f)
        {
            elapsed = Mathf.Max(0f, elapsed); clamp = Mathf.Max(0f, clamp);
            if (elapsed >= Duration(clamp, strokes, finish, processing))
                return new MediumRecycleFrame(1f, 3, MediumRecyclePhase.Complete, release: 1f);
            if (elapsed < clamp)
                return new MediumRecycleFrame(0f, -1, MediumRecyclePhase.Clamp,
                    .2f * Mathf.SmoothStep(0f, 1f, elapsed / Mathf.Max(.01f, clamp)));
            float start = clamp;
            for (int stage = 0; stage < 3; stage++)
            {
                float duration = Stroke(strokes, stage);
                if (elapsed < start + duration)
                {
                    float phase = (elapsed - start) / duration;
                    float from = stage == 0 ? 0f : stage == 1 ? .30f : .65f;
                    float to = stage == 0 ? .30f : stage == 1 ? .65f : 1f;
                    if (phase < LoadFraction)
                        return new MediumRecycleFrame(from, stage, MediumRecyclePhase.Load,
                            Mathf.Lerp(.25f, .55f, phase / LoadFraction));
                    if (phase < LoadFraction + PushFraction)
                    {
                        float push = (phase - LoadFraction) / PushFraction;
                        float advance = Mathf.SmoothStep(0f, 1f, push);
                        return new MediumRecycleFrame(Mathf.Lerp(from, to, advance), stage, MediumRecyclePhase.Push, 1f);
                    }
                    float lockTime = (phase - LoadFraction - PushFraction) / (1f - LoadFraction - PushFraction);
                    // Recoil settles within the early lock; the rest is a genuinely motionless hold.
                    float recoilTime = Mathf.Clamp01(lockTime / .45f);
                    float recoil = Mathf.Sin(recoilTime * Mathf.PI) * (1f - recoilTime);
                    return new MediumRecycleFrame(to - (to - from) * Mathf.Clamp(lockRecoilFraction, 0f, .05f) * recoil,
                        stage, MediumRecyclePhase.Lock, .35f * (1f - recoilTime), recoil);
                }
                start += duration;
            }
            return SampleProcessing(elapsed - start, finish, processing, processingFade);
        }

        /// <summary>Shared post-ingestion clock for small and medium waste.</summary>
        public static MediumRecycleFrame SampleProcessing(float elapsed, float finish, float processing, float processingFade)
        {
            float processingTime = Mathf.Max(0f, elapsed);
            float processingDuration = ProcessingDuration(finish, processing);
            if (processingTime >= processingDuration)
                return new MediumRecycleFrame(1f, 3, MediumRecyclePhase.Complete, release: 1f);
            float fadeDuration = Mathf.Clamp(processingFade, 0f, processingDuration);
            float envelope = fadeDuration > 0f
                ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((processingTime - processingDuration + fadeDuration) / fadeDuration))
                : 1f;
            float release = Mathf.Clamp01(processingTime / Mathf.Max(.01f, finish));
            return new MediumRecycleFrame(1f, 3, MediumRecyclePhase.Processing,
                release: Mathf.SmoothStep(0f, 1f, release), processing: envelope);
        }
    }
}

using System;
using UnityEngine;

namespace AnimalGame.MapTest
{
    // Constant-space ordered peak/valley detector. Equality (including 1e-5m
    // numerical noise) does not block: both sides must exceed interval + epsilon.
    public struct TerrainScanPeakState
    {
        private double minimum, maximum, peak, valley, threshold;
        public bool Blocked { get; private set; }

        public TerrainScanPeakState(double originHeight, double interval)
        {
            minimum = maximum = originHeight;
            peak = double.NegativeInfinity;
            valley = double.PositiveInfinity;
            threshold = interval + 0.00001;
            Blocked = false;
        }

        public void Add(double height)
        {
            Blocked |= peak - height > threshold || height - valley > threshold;
            if (height - minimum > threshold) peak = Math.Max(peak, height);
            if (maximum - height > threshold) valley = Math.Min(valley, height);
            minimum = Math.Min(minimum, height);
            maximum = Math.Max(maximum, height);
        }
    }

    /// <summary>Resumable uniformly sampled surface profile. Sampling density is
    /// chosen by the overlay from distance / captured view radius. O(S) time,
    /// O(1) memory; no DDA or per-height-field-pixel traversal.</summary>
    public struct TerrainScanProfile
    {
        private BakedHeightField field;
        private Vector2 originUv, targetUv;
        private TerrainScanPeakState peaks;
        public int SampleCount { get; private set; }
        public int SamplesRead { get; private set; }
        public bool Complete { get; private set; }
        public bool InvalidMap { get; private set; }
        public bool Blocked => peaks.Blocked;
        public bool Clear => Complete && !InvalidMap && !Blocked;

        // SampleCount counts positions after O, including P. O is always read
        // separately, so count=1 still checks both endpoints.
        public TerrainScanProfile(BakedHeightField heightField, Vector2 origin,
            Vector2 target, float interval, int sampleCount) : this()
        {
            field = heightField;
            SampleCount = Math.Max(1, sampleCount);
            originUv = new Vector2(origin.x / field.MapSizeMeters.x, origin.y / field.MapSizeMeters.y);
            targetUv = new Vector2(target.x / field.MapSizeMeters.x, target.y / field.MapSizeMeters.y);
            InvalidMap = !InBounds(originUv) || !InBounds(targetUv)
                || !field.IsPlayable(originUv) || !field.IsPlayable(targetUv);
            if (InvalidMap) { Complete = true; return; }
            peaks = new TerrainScanPeakState(field.SampleSurfaceHeight(originUv), interval);
            Complete = origin == target;
        }

        private static bool InBounds(Vector2 uv) => uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;

        public void Step()
        {
            if (Complete) return;
            SamplesRead++;
            Vector2 uv = Vector2.Lerp(originUv, targetUv, (float)SamplesRead / SampleCount);
            if (!field.IsPlayable(uv)) InvalidMap = true;
            else peaks.Add(field.SampleSurfaceHeight(uv));
            Complete = InvalidMap || Blocked || SamplesRead >= SampleCount;
        }
    }
}

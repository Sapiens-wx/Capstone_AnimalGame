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
        private IMapHeightSource field;
        private Vector2 originPosition, targetPosition;
        private TerrainScanPeakState peaks;
        public int SampleCount { get; private set; }
        public int SamplesRead { get; private set; }
        public bool Complete { get; private set; }
        public bool InvalidMap { get; private set; }
        public bool Blocked => peaks.Blocked;
        public bool Clear => Complete && !InvalidMap && !Blocked;

        public TerrainScanProfile(IMapHeightSource source, Vector2 origin,
            Vector2 target, float interval, int sampleCount) : this()
        {
            field = source;
            originPosition = origin;
            targetPosition = target;
            SampleCount = Math.Max(1, sampleCount);
            float originHeight = 0;
            InvalidMap = source == null || !source.TrySample(origin, out originHeight) || !source.TrySample(target, out _);
            if (InvalidMap) { Complete = true; return; }
            peaks = new TerrainScanPeakState(originHeight, interval);
            Complete = origin == target;
        }

        public void Step()
        {
            if (Complete) return;
            SamplesRead++;
            Vector2 position = Vector2.Lerp(originPosition, targetPosition, (float)SamplesRead / SampleCount);
            if (!field.TrySample(position, out float height)) InvalidMap = true;
            else peaks.Add(height);
            Complete = InvalidMap || Blocked || SamplesRead >= SampleCount;
        }
    }
}

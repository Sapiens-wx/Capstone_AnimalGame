using System;
using System.Collections.Generic;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Production traversal display. A fully charged scan replaces the previous
    /// snapshot, reveals absolute map markers with the release wave, keeps them for
    /// a configured duration, and periodically re-evaluates robot passability.
    /// This component is deliberately independent from the Q debug overlay.
    /// </summary>
    [DefaultExecutionOrder(325)]
    [DisallowMultipleComponent]
    public sealed class TraversalScanOverlayUI : MonoBehaviour
    {
        private enum PeriodicRefreshVisualPhase
        {
            None,
            FadingOut,
            WaitingForEvaluation,
            FadingIn
        }

        [Header("Sign Assets")]
        [SerializeField] private Sprite passableSign;
        [SerializeField] private Sprite unpassableSign;

        [Tooltip("Fixed screen-space Z angle for every traversal sign. Zero displays the sprite exactly as imported and never inherits robot or camera rotation.")]
        [SerializeField] private float fixedIconScreenAngleDegrees = 0f;

        [Header("Scan Sampling")]
        [Tooltip("Row/column spacing of scan candidates in reference-canvas pixels. This is a regular grid, not a radial pattern.")]
        [SerializeField, Min(4f)] private float sampleGridSpacingPixels = 32f;

        [Tooltip("Candidates this close to the visible contour crossing are included on both sides of the line, measured in logical map meters.")]
        [SerializeField, Min(0f)] private float contourBoundaryHalfWidthMeters = 3f;

        [Tooltip("Map distance used to estimate the local height gradient and contour normal.")]
        [SerializeField, Min(0.1f)] private float terrainGradientProbeMeters = 1.5f;

        [Tooltip("No production signs are calculated or rendered inside this screen-space radius around the fixed player UI centre.")]
        [SerializeField, Min(0f)] private float centerExclusionRadiusPixels = 40f;

        [Tooltip("Maximum number of signs produced by one scan snapshot.")]
        [SerializeField, Range(32, 4096)] private int maximumScannedSigns = 700;

        [Header("Local Danger Expansion")]
        [Tooltip("Local danger seeds expose candidates within this radius if their scan-origin profile is clear.")]
        [SerializeField, Min(0f)] private float unpassableNeighborhoodRadiusMeters = 8f;

        [Header("Persistence and Refresh")]
        [Tooltip("Seconds the completed scan snapshot remains visible. A new scan always replaces it immediately.")]
        [SerializeField, Min(0.1f)] private float markerLifetimeSeconds = 8f;

        [Tooltip("Seconds between passability rechecks for the absolute marker positions.")]
        [SerializeField, Min(0.05f)] private float stateRefreshIntervalSeconds = 0.75f;

        [Tooltip("Hard safety cap for marker states re-evaluated in one frame when a refresh is due.")]
        [SerializeField, Range(1, 512)] private int refreshCalculationsPerFrame = 96;

        [Tooltip("CPU time budget in milliseconds used by scheduled traversal refreshes each frame.")]
        [SerializeField, Min(0.1f)] private float refreshCalculationBudgetMilliseconds = 1.25f;

        [Header("Refresh Breathing Visual")]
        [Tooltip("Enables the full-snapshot breathing animation during scheduled refreshes. Disabling this does not disable the scheduled passability calculation.")]
        [SerializeField] private bool enablePeriodicRefreshBreathing = true;

        [Tooltip("Seconds used to breathe all signs down toward the map background before a scheduled refresh is committed.")]
        [SerializeField, Min(0.02f)] private float periodicFadeOutSeconds = 0.28f;

        [Tooltip("Seconds used to breathe all signs back into view after a scheduled refresh.")]
        [SerializeField, Min(0.02f)] private float periodicFadeInSeconds = 0.38f;

        [Tooltip("Opacity retained at the weakest point of a refresh breath.")]
        [SerializeField, Range(0f, 1f)] private float refreshMinimumAlpha = 0.04f;

        [Tooltip("Fade-out time for one sign whose state changes because the robot moved.")]
        [SerializeField, Min(0.02f)] private float changedStateFadeOutSeconds = 0.14f;

        [Tooltip("Fade-in time for one sign after its passable/unpassable sprite changes.")]
        [SerializeField, Min(0.02f)] private float changedStateFadeInSeconds = 0.24f;

        [Tooltip("Enables the individual breathing refresh when real-time robot-relative passability changes. Disabling it swaps the sign immediately.")]
        [SerializeField] private bool enableChangedStateBreathing = true;

        [Header("Real-time Robot-relative Recheck")]
        [Tooltip("Robot map distance that requests an immediate passability recheck. Unpassable signs are processed first.")]
        [SerializeField, Min(0.01f)] private float realtimeMoveThresholdMeters = 0.35f;

        [Tooltip("Minimum time between movement-triggered recheck passes.")]
        [SerializeField, Min(0.02f)] private float realtimeRecheckMinimumInterval = 0.08f;

        [Tooltip("Hard safety cap for robot-relative path checks performed per frame.")]
        [SerializeField, Range(1, 512)] private int realtimeRechecksPerFrame = 64;

        [Tooltip("CPU time budget in milliseconds used by movement-triggered traversal rechecks each frame.")]
        [SerializeField, Min(0.1f)] private float realtimeCalculationBudgetMilliseconds = 1f;

        [Header("Scan Work Scheduling")]
        [Tooltip("Height samples along a centre-to-edge profile at the captured UI view radius. Shorter profiles use proportionally fewer samples. Includes the target; the centre is checked separately. Lower values can miss narrow peaks, valleys, or mask holes.")]
        [SerializeField, Min(1)] private int profileSamplesPerViewRadius = 32;

        [Tooltip("Complete scan calculations within this many Update frames after the scan request. Remaining work is divided over remaining frames; 1 completes all calculations in one frame. The release wave remains a separate visual animation.")]
        [SerializeField, Min(1)] private int scanCompletionFrames = 5;

        [Header("Presentation")]
        [SerializeField, Min(1f)] private float iconSizePixels = 8f;
        [SerializeField, Range(-100, 100)] private int canvasSortingOrder = 21;

        private struct PendingScreenSample
        {
            public Vector2 MapPosition;
            public int ProfileSampleCount;
        }

        private struct SampledCandidate
        {
            public Vector2 MapPosition;
            public Vector2 EvaluationDirection;
            public bool NearContour, LocalDanger, NearAnyDanger, ProfileClear, ProfileChecked;
            public int NextInBucket;
            public int ProfileSampleCount;
            public bool IsPassable;
            public bool IsSelected;
        }

        private sealed class PersistentMarker
        {
            public Vector2 MapPosition;
            public Vector2 EvaluationDirection;
            public bool IsPassable;
            public bool PendingIsPassable;
            public bool IndividualRefreshActive;
            public bool IndividualSpriteCommitted;
            public float IndividualRefreshStartedAt;
        }

        private readonly List<PendingScreenSample> pendingSamples =
            new List<PendingScreenSample>(512);
        private readonly List<SampledCandidate> sampledCandidates =
            new List<SampledCandidate>(512);
        private readonly List<int> unpassableSeeds = new List<int>(128);
        private readonly Dictionary<Vector2Int, int> candidateBuckets = new Dictionary<Vector2Int, int>();
        private readonly List<int> selectedCandidates = new List<int>(512);
        private enum ScanStage { GridGeneration, LocalEvaluation, DangerExpansion, Profiles, Markers, Finished }
        private ScanStage scanStage;
        private int nextSeed, nextNeighbor, nextBucketCandidate = -1, nextProfile, previousBucketCandidate = -1;
        private Vector2Int activeBucket;
        private bool bucketActive, profileActive;
        private TerrainScanProfile profile;
        private Vector2 scanOrigin;
        private Vector2 gridCentre;
        private float gridX, gridY, gridFirstX, gridMaximumX, gridMaximumY, gridSpacing;
        private float gridRadiusSquared, gridExclusionSquared;
        private Vector3 nearOrigin, nearAxisX, nearAxisY, farOrigin, farAxisX, farAxisY;
        private BakedHeightField scanHeightField;
        private float scanContourInterval;
        private int capturedProfileSamples, capturedCompletionFrames, generatedGridCount, totalGridCount;
        public int ScanFramesUsed { get; private set; }
        public int ProfileCheckCount { get; private set; }
        public int ProfileSampleVisitCount { get; private set; }
        public int ScanCandidateCount => sampledCandidates.Count;
        public float WorstScanFrameMilliseconds { get; private set; }
        private readonly List<PersistentMarker> markers =
            new List<PersistentMarker>(256);
        private readonly Stack<PersistentMarker> recycledMarkers =
            new Stack<PersistentMarker>(256);
        private readonly List<TraversalSignRenderData> passableRenderData =
            new List<TraversalSignRenderData>(256);
        private readonly List<TraversalSignRenderData> unpassableRenderData =
            new List<TraversalSignRenderData>(256);
        private readonly List<int> realtimeRecheckOrder = new List<int>(256);

        private MapTestSceneController map;
        private HeightMapTraversalEvaluator evaluator;
        private Camera mapCamera;
        private RobotMover robot;
        private ScanChargeUI scanChargeUi;
        private GameObject overlayRoot;
        private TraversalSignsGraphic passableSignsGraphic;
        private TraversalSignsGraphic unpassableSignsGraphic;
        private int nextPendingSample;
        private int nextRefreshMarker;
        private float scanStartedAt;
        private float scanWaveDuration;
        private float scannedUiRadiusPixels;
        private float snapshotExpiresAt;
        private float nextStateRefreshAt;
        private bool scanIsRevealing;
        private bool refreshInProgress;
        private PeriodicRefreshVisualPhase periodicRefreshPhase;
        private float periodicRefreshPhaseStartedAt;
        private int nextRealtimeRecheck;
        private float nextRealtimeRecheckAllowedAt;
        private Vector2 lastRealtimeRobotMapPosition;
        private bool hasRealtimeRobotPosition;
        private bool realtimeRecheckInProgress;
        private bool realtimeRecheckRequested;

        public int VisibleMarkerCount => markers.Count;
        public bool HasActiveSnapshot => scanIsRevealing || markers.Count > 0;

        public Canvas OverlayCanvas { get; private set; }

        public void Initialize(
            MapTestSceneController mapController,
            HeightMapTraversalEvaluator traversalEvaluator,
            Camera cameraToSample,
            RobotMover playerRobot,
            ScanChargeUI scanUi)
        {
            UnsubscribeFromScan();
            map = mapController;
            evaluator = traversalEvaluator;
            mapCamera = cameraToSample;
            robot = playerRobot;
            scanChargeUi = scanUi;

            if (map == null || evaluator == null || mapCamera == null
                || robot == null || scanChargeUi == null
                || map.HeightField == null)
            {
                Debug.LogError(
                    "TraversalScanOverlayUI requires map, evaluator, camera, robot, scan UI, and a baked height field.",
                    this);
                enabled = false;
                return;
            }

            CreateOverlayIfNeeded();
            PrewarmMarkerPool();
            scanChargeUi.TerrainScanRequested += BeginScannedSnapshot;
            ClearSnapshot();
        }

        private void Update()
        {
            if (map == null || evaluator == null || mapCamera == null)
                return;

            if (scanIsRevealing)
            {
                RevealScanWave();
                return;
            }

            if (scanHeightField != null && (scanHeightField != map.HeightField
                || scanContourInterval != map.ContourIntervalMeters)) ClearSnapshot();
            UpdatePersistentSnapshot();

            if (refreshInProgress)
                ProcessRefreshBatch();

            UpdatePeriodicRefreshVisual();
            UpdateRealtimeRechecks();
        }

        private void LateUpdate()
        {
            RenderMarkers();
        }

        private void BeginScannedSnapshot()
        {
            ClearSnapshot();
            if (!map.TrySampleWorldPosition(
                    robot.transform.position,
                    out Vector2 robotMapPosition,
                    out _))
            {
                return;
            }

            scanOrigin = robotMapPosition;
            scanHeightField = map.HeightField;
            scanContourInterval = map.ContourIntervalMeters;
            capturedProfileSamples = Mathf.Max(1, profileSamplesPerViewRadius);
            capturedCompletionFrames = Mathf.Max(1, scanCompletionFrames);
            scanStage = ScanStage.GridGeneration;
            scannedUiRadiusPixels = Mathf.Max(
                1f,
                scanChargeUi.GetUiRingScreenRadiusPixels());
            scanWaveDuration = Mathf.Max(
                0.05f,
                scanChargeUi.ReleaseRingExpansionDuration);
            BuildPendingScreenGrid(scannedUiRadiusPixels);
            scanStartedAt = Time.unscaledTime;
            scanIsRevealing = true;
        }

        private void BuildPendingScreenGrid(float radiusPixels)
        {
            pendingSamples.Clear();
            gridCentre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float canvasScale = radiusPixels / Mathf.Max(1f, scanChargeUi.UiRingRadiusPixels);
            gridSpacing = Mathf.Max(4f, sampleGridSpacingPixels * canvasScale);
            gridMaximumX = Mathf.Min(Screen.width, gridCentre.x + radiusPixels);
            gridMaximumY = Mathf.Min(Screen.height, gridCentre.y + radiusPixels);
            gridFirstX = Mathf.Ceil(Mathf.Max(0f, gridCentre.x - radiusPixels) / gridSpacing) * gridSpacing;
            gridX = gridFirstX;
            gridY = Mathf.Ceil(Mathf.Max(0f, gridCentre.y - radiusPixels) / gridSpacing) * gridSpacing;
            int columns = Mathf.Max(0, Mathf.FloorToInt((gridMaximumX + 0.01f - gridFirstX) / gridSpacing) + 1);
            int rows = Mathf.Max(0, Mathf.FloorToInt((gridMaximumY + 0.01f - gridY) / gridSpacing) + 1);
            totalGridCount = columns * rows;
            gridRadiusSquared = radiusPixels * radiusPixels;
            float exclusion = (centerExclusionRadiusPixels + iconSizePixels * 0.70710678f) * canvasScale;
            gridExclusionSquared = exclusion * exclusion;
            // Both clipping planes are affine in screen coordinates, including for
            // perspective cameras. Capture once so generation can yield while the
            // player/camera moves, retaining the original screen candidate layout.
            nearOrigin = mapCamera.ScreenToWorldPoint(new Vector3(0, 0, mapCamera.nearClipPlane));
            nearAxisX = (mapCamera.ScreenToWorldPoint(new Vector3(Screen.width, 0, mapCamera.nearClipPlane)) - nearOrigin) / Mathf.Max(1, Screen.width);
            nearAxisY = (mapCamera.ScreenToWorldPoint(new Vector3(0, Screen.height, mapCamera.nearClipPlane)) - nearOrigin) / Mathf.Max(1, Screen.height);
            farOrigin = mapCamera.ScreenToWorldPoint(new Vector3(0, 0, mapCamera.farClipPlane));
            farAxisX = (mapCamera.ScreenToWorldPoint(new Vector3(Screen.width, 0, mapCamera.farClipPlane)) - farOrigin) / Mathf.Max(1, Screen.width);
            farAxisY = (mapCamera.ScreenToWorldPoint(new Vector3(0, Screen.height, mapCamera.farClipPlane)) - farOrigin) / Mathf.Max(1, Screen.height);
        }

        private void GenerateScreenCandidate()
        {
            if (gridY > gridMaximumY + 0.01f)
            {
                scanStage = ScanStage.LocalEvaluation;
                return;
            }
            generatedGridCount++;
            Vector2 screen = new Vector2(gridX, gridY);
            gridX += gridSpacing;
            if (gridX > gridMaximumX + 0.01f) { gridX = gridFirstX; gridY += gridSpacing; }
            float squared = (screen - gridCentre).sqrMagnitude;
            if (squared > gridRadiusSquared || squared < gridExclusionSquared) return;
            Vector3 start = nearOrigin + nearAxisX * screen.x + nearAxisY * screen.y;
            Vector3 direction = farOrigin + farAxisX * screen.x + farAxisY * screen.y - start;
            if (Mathf.Abs(direction.z) < 0.000001f) return;
            float t = (map.WorldBounds.center.z - start.z) / direction.z;
            if (t < 0f) return;
            if (map.TrySampleWorldPosition(start + direction * t, out Vector2 position, out _))
                pendingSamples.Add(new PendingScreenSample
                {
                    MapPosition = position,
                    ProfileSampleCount = SamplesForRadius(Mathf.Sqrt(squared / gridRadiusSquared), capturedProfileSamples)
                });
        }

        private void RevealScanWave()
        {
            if (scanHeightField != map.HeightField || scanContourInterval != map.ContourIntervalMeters)
            {
                ClearSnapshot();
                return;
            }
            if (scanStage != ScanStage.Finished) ProcessScanFrame();
            if (scanStage != ScanStage.Finished || Time.unscaledTime < scanStartedAt + scanWaveDuration) return;
            scanIsRevealing = false;
            snapshotExpiresAt = Time.unscaledTime + Mathf.Max(0.1f, markerLifetimeSeconds);
            nextStateRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, stateRefreshIntervalSeconds);
            CaptureRealtimeRobotPosition();
        }

        internal static int SamplesForRadius(float radius01, int samplesAtRadius)
        {
            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Clamp01(radius01) * Mathf.Max(1, samplesAtRadius)));
        }

        // Stage costs differ: surface evaluation and full-path checks get greater
        // weights than grid/bucket bookkeeping and one profile height sample.
        // Counts become exact as preceding stages discover seeds and eligibility.
        private long EstimateRemainingScanWork()
        {
            const int localCost = 16, markerCost = 128;
            long futureCandidates = sampledCandidates.Count;
            long gridWork = 0, localWork = 0, dangerWork = 0, profileWork = 0;
            if (scanStage == ScanStage.GridGeneration)
            {
                long remainingGrid = Mathf.Max(0, totalGridCount - generatedGridCount);
                gridWork = remainingGrid + 1;
                futureCandidates = pendingSamples.Count + remainingGrid;
                localWork = futureCandidates * localCost + 1;
            }
            else if (scanStage == ScanStage.LocalEvaluation)
            {
                futureCandidates += pendingSamples.Count - nextPendingSample;
                localWork = (pendingSamples.Count - nextPendingSample) * localCost + 1;
            }
            if (scanStage == ScanStage.GridGeneration || scanStage == ScanStage.LocalEvaluation)
            {
                // Seed membership is not known until local evaluation finishes.
                dangerWork = futureCandidates * 10 + 1;
                profileWork = futureCandidates * (capturedProfileSamples + 1L);
            }
            else if (scanStage == ScanStage.DangerExpansion)
            {
                dangerWork = Math.Max(0, unpassableSeeds.Count - nextSeed) * 9L
                    + sampledCandidates.Count + 1;
                foreach (SampledCandidate candidate in sampledCandidates)
                    profileWork += !candidate.LocalDanger && !candidate.NearContour
                        ? candidate.ProfileSampleCount + 1L : 1L;
            }
            else if (scanStage == ScanStage.Profiles)
            {
                for (int i = nextProfile; i < sampledCandidates.Count; i++)
                {
                    SampledCandidate candidate = sampledCandidates[i];
                    long samples = !candidate.LocalDanger && !candidate.NearContour && candidate.NearAnyDanger
                        ? candidate.ProfileSampleCount : 0;
                    if (i == nextProfile && profileActive) samples = Math.Max(0, samples - profile.SamplesRead);
                    profileWork += samples + 1;
                }
            }
            long markerCount = scanStage == ScanStage.Markers
                ? Math.Min(selectedCandidates.Count, Math.Max(0, maximumScannedSigns - markers.Count))
                : Math.Min(futureCandidates, maximumScannedSigns);
            return gridWork + localWork + dangerWork + profileWork + markerCount * markerCost + 1;
        }

        private void ProcessScanFrame()
        {
            float started = Time.realtimeSinceStartup;
            int framesLeft = Mathf.Max(1, capturedCompletionFrames - ScanFramesUsed);
            long remaining = EstimateRemainingScanWork();
            long allowance = Math.Max(1, (remaining + framesLeft - 1) / framesLeft);
            long work = 0;
            while (scanStage != ScanStage.Finished && (framesLeft == 1 || work < allowance))
            {
                ScanStage previousStage = scanStage;
                switch (scanStage)
                {
                    case ScanStage.GridGeneration: GenerateScreenCandidate(); work++; break;
                    case ScanStage.LocalEvaluation:
                        if (nextPendingSample < pendingSamples.Count)
                            SampleScreenCandidate(pendingSamples[nextPendingSample++]);
                        else scanStage = ScanStage.DangerExpansion;
                        work += 16;
                        break;
                    case ScanStage.DangerExpansion: ExpandDangerStep(); work++; break;
                    case ScanStage.Profiles: CheckProfileStep(); work++; break;
                    case ScanStage.Markers:
                        if (selectedCandidates.Count > 0 && markers.Count < maximumScannedSigns)
                            SelectCandidate(PopSelectedCandidate());
                        else scanStage = ScanStage.Finished;
                        work += 128;
                        break;
                }
                // Revise estimates at phase boundaries without scanning lists per
                // sample. This prevents an obsolete upper bound front-loading work.
                if (previousStage != scanStage && framesLeft > 1)
                {
                    remaining = EstimateRemainingScanWork();
                    allowance = Math.Max(1, (work + remaining + framesLeft - 1) / framesLeft);
                }
            }
            ScanFramesUsed++;
            WorstScanFrameMilliseconds = Mathf.Max(WorstScanFrameMilliseconds,
                (Time.realtimeSinceStartup - started) * 1000f);
        }

        private Vector2Int Bucket(Vector2 position) => new Vector2Int(
            Mathf.FloorToInt(position.x / unpassableNeighborhoodRadiusMeters),
            Mathf.FloorToInt(position.y / unpassableNeighborhoodRadiusMeters));

        // One bucket member per work unit, so dense buckets obey the budget.
        private void ExpandDangerStep()
        {
            if (unpassableNeighborhoodRadiusMeters <= 0f || nextSeed >= unpassableSeeds.Count)
            {
                scanStage = ScanStage.Profiles;
                return;
            }
            Vector2 seed = sampledCandidates[unpassableSeeds[nextSeed]].MapPosition;
            if (!bucketActive)
            {
                Vector2Int key = Bucket(seed) + new Vector2Int(nextNeighbor % 3 - 1, nextNeighbor / 3 - 1);
                activeBucket = key;
                previousBucketCandidate = -1;
                nextBucketCandidate = candidateBuckets.TryGetValue(key, out int head) ? head : -1;
                bucketActive = true;
            }
            if (nextBucketCandidate >= 0)
            {
                int index = nextBucketCandidate;
                SampledCandidate candidate = sampledCandidates[index];
                nextBucketCandidate = candidate.NextInBucket;
                if (!candidate.NearAnyDanger && (candidate.MapPosition - seed).sqrMagnitude
                    <= unpassableNeighborhoodRadiusMeters * unpassableNeighborhoodRadiusMeters)
                {
                    candidate.NearAnyDanger = true;
                    sampledCandidates[index] = candidate;
                    // Unlink marked candidates: subsequent seeds never visit them.
                    if (previousBucketCandidate < 0) candidateBuckets[activeBucket] = nextBucketCandidate;
                    else
                    {
                        SampledCandidate previous = sampledCandidates[previousBucketCandidate];
                        previous.NextInBucket = nextBucketCandidate;
                        sampledCandidates[previousBucketCandidate] = previous;
                    }
                }
                else previousBucketCandidate = index;
                return;
            }
            bucketActive = false;
            if (++nextNeighbor == 9) { nextNeighbor = 0; nextSeed++; }
        }

        private void CheckProfileStep()
        {
            if (nextProfile >= sampledCandidates.Count)
            {
                scanStage = ScanStage.Markers;
                return;
            }
            SampledCandidate candidate = sampledCandidates[nextProfile];
            if (!candidate.LocalDanger && !candidate.NearContour && candidate.NearAnyDanger)
            {
                if (!profileActive)
                {
                    profile = new TerrainScanProfile(scanHeightField, scanOrigin,
                        candidate.MapPosition, Mathf.Max(0.01f, scanContourInterval), candidate.ProfileSampleCount);
                    profileActive = true;
                    ProfileCheckCount++;
                }
                if (!profile.Complete) { profile.Step(); ProfileSampleVisitCount++; }
                if (!profile.Complete) return;
                candidate.ProfileChecked = true;
                candidate.ProfileClear = profile.Clear;
                profileActive = false;
            }
            candidate.IsSelected = candidate.LocalDanger || candidate.NearContour
                || (candidate.NearAnyDanger && candidate.ProfileClear);
            sampledCandidates[nextProfile] = candidate;
            if (candidate.IsSelected) PushSelectedCandidate(nextProfile);
            nextProfile++;
        }

        // A heap applies the explicit cap policy without one unbudgeted N log N
        // sort: each insertion/removal costs O(log N) and allocates no delegate.
        private int CompareSelected(int left, int right)
        {
            SampledCandidate a = sampledCandidates[left], b = sampledCandidates[right];
            int order = (a.LocalDanger ? 0 : a.NearContour ? 1 : 2)
                .CompareTo(b.LocalDanger ? 0 : b.NearContour ? 1 : 2);
            if (order != 0) return order;
            order = (a.MapPosition - scanOrigin).sqrMagnitude.CompareTo((b.MapPosition - scanOrigin).sqrMagnitude);
            if (order != 0) return order;
            order = a.MapPosition.x.CompareTo(b.MapPosition.x);
            return order != 0 ? order : a.MapPosition.y.CompareTo(b.MapPosition.y);
        }

        private void PushSelectedCandidate(int index)
        {
            int child = selectedCandidates.Count;
            selectedCandidates.Add(index);
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (CompareSelected(selectedCandidates[parent], index) <= 0) break;
                selectedCandidates[child] = selectedCandidates[parent];
                child = parent;
            }
            selectedCandidates[child] = index;
        }

        private int PopSelectedCandidate()
        {
            int result = selectedCandidates[0];
            int last = selectedCandidates[selectedCandidates.Count - 1];
            selectedCandidates.RemoveAt(selectedCandidates.Count - 1);
            if (selectedCandidates.Count == 0) return result;
            int parent = 0;
            while (parent * 2 + 1 < selectedCandidates.Count)
            {
                int child = parent * 2 + 1;
                if (child + 1 < selectedCandidates.Count
                    && CompareSelected(selectedCandidates[child + 1], selectedCandidates[child]) < 0) child++;
                if (CompareSelected(last, selectedCandidates[child]) <= 0) break;
                selectedCandidates[parent] = selectedCandidates[child];
                parent = child;
            }
            selectedCandidates[parent] = last;
            return result;
        }

        private void SampleScreenCandidate(PendingScreenSample pending)
        {
            Vector2 mapPosition = pending.MapPosition;
            if (!TryAnalyzeLocalTerrain(mapPosition, out Vector2 gradientDirection, out bool isNearContour)) return;
            SlopeTraversalResult result = EvaluateAt(mapPosition, gradientDirection);
            if (!result.HasData) return;
            int index = sampledCandidates.Count;
            var candidate = new SampledCandidate
            {
                MapPosition = mapPosition,
                EvaluationDirection = gradientDirection,
                IsPassable = result.IsPassable,
                LocalDanger = !result.IsPassable,
                NearContour = isNearContour,
                ProfileSampleCount = pending.ProfileSampleCount,
                NextInBucket = -1
            };
            if (unpassableNeighborhoodRadiusMeters > 0f)
            {
                Vector2Int key = Bucket(mapPosition);
                if (candidateBuckets.TryGetValue(key, out int head)) candidate.NextInBucket = head;
                candidateBuckets[key] = index;
            }
            sampledCandidates.Add(candidate);
            if (candidate.LocalDanger) unpassableSeeds.Add(index);
        }

        private void SelectCandidate(int candidateIndex)
        {
            if (candidateIndex < 0
                || candidateIndex >= sampledCandidates.Count
                || markers.Count >= maximumScannedSigns)
            {
                return;
            }

            SampledCandidate candidate = sampledCandidates[candidateIndex];
            SlopeTraversalResult robotRelativeResult =
                EvaluateFromRobot(candidate.MapPosition);
            bool displayedPassability = robotRelativeResult.HasData
                ? robotRelativeResult.IsPassable
                : candidate.IsPassable;
            PersistentMarker marker = recycledMarkers.Count > 0
                ? recycledMarkers.Pop()
                : new PersistentMarker();
            marker.MapPosition = candidate.MapPosition;
            marker.EvaluationDirection = candidate.EvaluationDirection;
            marker.IsPassable = displayedPassability;
            marker.PendingIsPassable = displayedPassability;
            marker.IndividualRefreshActive = false;
            marker.IndividualSpriteCommitted = true;
            marker.IndividualRefreshStartedAt = 0f;
            markers.Add(marker);
        }

        private bool TryAnalyzeLocalTerrain(
            Vector2 mapPosition,
            out Vector2 gradientDirection,
            out bool isNearContour)
        {
            gradientDirection = Vector2.up;
            isNearContour = false;
            float probeX = Mathf.Max(
                terrainGradientProbeMeters,
                map.HeightField.TexelSizeXMeters);
            float probeY = Mathf.Max(
                terrainGradientProbeMeters,
                map.HeightField.TexelSizeYMeters);
            if (!TrySampleMapHeight(mapPosition, out float centreHeight))
                return false;

            float left = SampleOrCentre(mapPosition + Vector2.left * probeX, centreHeight);
            float right = SampleOrCentre(mapPosition + Vector2.right * probeX, centreHeight);
            float down = SampleOrCentre(mapPosition + Vector2.down * probeY, centreHeight);
            float up = SampleOrCentre(mapPosition + Vector2.up * probeY, centreHeight);
            Vector2 gradient = new Vector2(
                (right - left) / (2f * probeX),
                (up - down) / (2f * probeY));
            float gradientMagnitude = gradient.magnitude;
            if (gradientMagnitude > 0.0001f)
                gradientDirection = gradient / gradientMagnitude;

            float interval = Mathf.Max(0.01f, map.ContourIntervalMeters);
            float relativeHeight = centreHeight
                                   - map.HeightField.MinimumHeightMeters;
            float nearestContourHeight = Mathf.Round(relativeHeight / interval)
                                         * interval
                                         + map.HeightField.MinimumHeightMeters;
            float estimatedDistance = gradientMagnitude > 0.0001f
                ? Mathf.Abs(centreHeight - nearestContourHeight)
                  / gradientMagnitude
                : float.PositiveInfinity;
            isNearContour = estimatedDistance
                            <= contourBoundaryHalfWidthMeters;
            return true;
        }

        private float SampleOrCentre(Vector2 mapPosition, float centreHeight)
        {
            return TrySampleMapHeight(mapPosition, out float height)
                ? height
                : centreHeight;
        }

        private bool TrySampleMapHeight(Vector2 mapPosition, out float height)
        {
            return map.TrySampleMapPosition(mapPosition, out height);
        }

        private SlopeTraversalResult EvaluateAt(
            Vector2 mapPosition,
            Vector2 mapDirection)
        {
            Vector2 direction = mapDirection.sqrMagnitude > 0.0001f
                ? mapDirection.normalized
                : Vector2.up;
            Vector3 worldPosition = map.MapPositionToWorld(mapPosition);
            Vector2 worldDirection = map.MapDirectionToWorldDirection(direction);
            return evaluator.EvaluateCurrentSurface(worldPosition, worldDirection);
        }

        private void UpdatePersistentSnapshot()
        {
            if (markers.Count == 0)
                return;

            if (Time.unscaledTime >= snapshotExpiresAt)
            {
                ClearSnapshot();
                return;
            }

            if (periodicRefreshPhase == PeriodicRefreshVisualPhase.None
                && Time.unscaledTime >= nextStateRefreshAt)
                BeginPeriodicRefresh();
        }

        private void BeginPeriodicRefresh()
        {
            realtimeRecheckInProgress = false;
            realtimeRecheckRequested = false;
            realtimeRecheckOrder.Clear();
            refreshInProgress = true;
            nextRefreshMarker = 0;
            periodicRefreshPhase = enablePeriodicRefreshBreathing
                ? PeriodicRefreshVisualPhase.FadingOut
                : PeriodicRefreshVisualPhase.WaitingForEvaluation;
            periodicRefreshPhaseStartedAt = Time.unscaledTime;
            for (int index = 0; index < markers.Count; index++)
            {
                markers[index].PendingIsPassable = markers[index].IsPassable;
                markers[index].IndividualRefreshActive = false;
            }
        }

        private void ProcessRefreshBatch()
        {
            int processed = 0;
            float calculationStartedAt = Time.realtimeSinceStartup;
            while (nextRefreshMarker < markers.Count
                   && processed < refreshCalculationsPerFrame)
            {
                if (processed > 0
                    && HasExceededCalculationBudget(
                        calculationStartedAt,
                        refreshCalculationBudgetMilliseconds))
                {
                    break;
                }

                PersistentMarker marker = markers[nextRefreshMarker++];
                SlopeTraversalResult result = EvaluateFromRobot(marker.MapPosition);
                if (result.HasData)
                    marker.PendingIsPassable = result.IsPassable;
                processed++;
            }

            if (nextRefreshMarker < markers.Count)
                return;

            refreshInProgress = false;
            if (periodicRefreshPhase
                == PeriodicRefreshVisualPhase.WaitingForEvaluation)
            {
                CommitPeriodicRefreshAtTrough();
            }
        }

        private void UpdatePeriodicRefreshVisual()
        {
            switch (periodicRefreshPhase)
            {
                case PeriodicRefreshVisualPhase.FadingOut:
                    if (Time.unscaledTime - periodicRefreshPhaseStartedAt
                        < periodicFadeOutSeconds)
                    {
                        return;
                    }

                    if (refreshInProgress)
                    {
                        periodicRefreshPhase =
                            PeriodicRefreshVisualPhase.WaitingForEvaluation;
                    }
                    else
                    {
                        CommitPeriodicRefreshAtTrough();
                    }
                    break;

                case PeriodicRefreshVisualPhase.WaitingForEvaluation:
                    if (!refreshInProgress)
                        CommitPeriodicRefreshAtTrough();
                    break;

                case PeriodicRefreshVisualPhase.FadingIn:
                    if (Time.unscaledTime - periodicRefreshPhaseStartedAt
                        >= periodicFadeInSeconds)
                    {
                        periodicRefreshPhase = PeriodicRefreshVisualPhase.None;
                        nextStateRefreshAt = Time.unscaledTime
                                             + Mathf.Max(
                                                 0.05f,
                                                 stateRefreshIntervalSeconds);
                        CaptureRealtimeRobotPosition();
                    }
                    break;
            }
        }

        private void CommitPeriodicRefreshAtTrough()
        {
            for (int index = 0; index < markers.Count; index++)
            {
                PersistentMarker marker = markers[index];
                marker.IsPassable = marker.PendingIsPassable;
                marker.IndividualRefreshActive = false;
            }

            if (enablePeriodicRefreshBreathing)
            {
                periodicRefreshPhase = PeriodicRefreshVisualPhase.FadingIn;
                periodicRefreshPhaseStartedAt = Time.unscaledTime;
                return;
            }

            periodicRefreshPhase = PeriodicRefreshVisualPhase.None;
            nextStateRefreshAt = Time.unscaledTime
                                 + Mathf.Max(0.05f, stateRefreshIntervalSeconds);
            CaptureRealtimeRobotPosition();
        }

        private SlopeTraversalResult EvaluateFromRobot(Vector2 targetMapPosition)
        {
            return TryGetRobotMapPosition(out Vector2 robotMapPosition)
                ? evaluator.EvaluateMapPath(robotMapPosition, targetMapPosition)
                : SlopeTraversalResult.NoData;
        }

        private bool TryGetRobotMapPosition(out Vector2 robotMapPosition)
        {
            robotMapPosition = default;
            return robot != null
                   && map.TrySampleWorldPosition(
                       robot.transform.position,
                       out robotMapPosition,
                       out _);
        }

        private void CaptureRealtimeRobotPosition()
        {
            if (!TryGetRobotMapPosition(out lastRealtimeRobotMapPosition))
            {
                hasRealtimeRobotPosition = false;
                return;
            }

            hasRealtimeRobotPosition = true;
        }

        private void UpdateRealtimeRechecks()
        {
            if (markers.Count == 0
                || periodicRefreshPhase != PeriodicRefreshVisualPhase.None
                || refreshInProgress
                || !TryGetRobotMapPosition(out Vector2 currentRobotMapPosition))
            {
                return;
            }

            if (!hasRealtimeRobotPosition)
            {
                lastRealtimeRobotMapPosition = currentRobotMapPosition;
                hasRealtimeRobotPosition = true;
            }

            float movementThresholdSquared = realtimeMoveThresholdMeters
                                             * realtimeMoveThresholdMeters;
            if ((currentRobotMapPosition - lastRealtimeRobotMapPosition).sqrMagnitude
                >= movementThresholdSquared)
            {
                if (realtimeRecheckInProgress
                    || Time.unscaledTime < nextRealtimeRecheckAllowedAt)
                {
                    realtimeRecheckRequested = true;
                }
                else
                {
                    BeginRealtimeRecheck(currentRobotMapPosition);
                }
            }

            if (!realtimeRecheckInProgress
                && realtimeRecheckRequested
                && Time.unscaledTime >= nextRealtimeRecheckAllowedAt)
            {
                BeginRealtimeRecheck(currentRobotMapPosition);
            }

            if (!realtimeRecheckInProgress)
                return;

            int processed = 0;
            float calculationStartedAt = Time.realtimeSinceStartup;
            while (nextRealtimeRecheck < realtimeRecheckOrder.Count
                   && processed < realtimeRechecksPerFrame)
            {
                if (processed > 0
                    && HasExceededCalculationBudget(
                        calculationStartedAt,
                        realtimeCalculationBudgetMilliseconds))
                {
                    break;
                }

                PersistentMarker marker =
                    markers[realtimeRecheckOrder[nextRealtimeRecheck++]];
                SlopeTraversalResult result = evaluator.EvaluateMapPath(
                    currentRobotMapPosition,
                    marker.MapPosition);
                if (result.HasData)
                    ApplyRealtimeResult(marker, result.IsPassable);
                processed++;
            }

            if (nextRealtimeRecheck < realtimeRecheckOrder.Count)
                return;

            realtimeRecheckInProgress = false;
            realtimeRecheckOrder.Clear();
        }

        private void BeginRealtimeRecheck(Vector2 currentRobotMapPosition)
        {
            realtimeRecheckOrder.Clear();
            for (int index = 0; index < markers.Count; index++)
                realtimeRecheckOrder.Add(index);

            // Previously unpassable signs are checked first so a route that became
            // possible reacts with the shortest latency. Distance breaks ties.
            realtimeRecheckOrder.Sort((leftIndex, rightIndex) =>
            {
                PersistentMarker left = markers[leftIndex];
                PersistentMarker right = markers[rightIndex];
                if (left.IsPassable != right.IsPassable)
                    return left.IsPassable ? 1 : -1;

                float leftDistance =
                    (left.MapPosition - currentRobotMapPosition).sqrMagnitude;
                float rightDistance =
                    (right.MapPosition - currentRobotMapPosition).sqrMagnitude;
                return leftDistance.CompareTo(rightDistance);
            });

            nextRealtimeRecheck = 0;
            realtimeRecheckInProgress = realtimeRecheckOrder.Count > 0;
            realtimeRecheckRequested = false;
            lastRealtimeRobotMapPosition = currentRobotMapPosition;
            hasRealtimeRobotPosition = true;
            nextRealtimeRecheckAllowedAt = Time.unscaledTime
                                           + Mathf.Max(
                                               0.02f,
                                               realtimeRecheckMinimumInterval);
        }

        private void BeginIndividualStateRefresh(
            PersistentMarker marker,
            bool newPassability)
        {
            if (!enableChangedStateBreathing)
            {
                marker.IsPassable = newPassability;
                marker.PendingIsPassable = newPassability;
                marker.IndividualRefreshActive = false;
                marker.IndividualSpriteCommitted = true;
                return;
            }

            if (marker.IndividualRefreshActive
                && marker.PendingIsPassable == newPassability)
            {
                return;
            }

            marker.PendingIsPassable = newPassability;
            marker.IndividualRefreshActive = true;
            marker.IndividualSpriteCommitted = false;
            marker.IndividualRefreshStartedAt = Time.unscaledTime;
        }
        private void ApplyRealtimeResult(
            PersistentMarker marker,
            bool newPassability)
        {
            if (!marker.IndividualRefreshActive)
            {
                if (newPassability != marker.IsPassable)
                    BeginIndividualStateRefresh(marker, newPassability);
                return;
            }

            if (!marker.IndividualSpriteCommitted
                && newPassability == marker.IsPassable)
            {
                marker.PendingIsPassable = marker.IsPassable;
                marker.IndividualRefreshActive = false;
                return;
            }

            if (newPassability != marker.PendingIsPassable)
                BeginIndividualStateRefresh(marker, newPassability);
        }
        private static bool HasExceededCalculationBudget(
            float calculationStartedAt,
            float budgetMilliseconds)
        {
            return (Time.realtimeSinceStartup - calculationStartedAt) * 1000f
                   >= Mathf.Max(0.1f, budgetMilliseconds);
        }

        private float GetPeriodicRefreshVisibility()
        {
            if (!enablePeriodicRefreshBreathing)
                return 1f;

            float elapsed = Time.unscaledTime - periodicRefreshPhaseStartedAt;
            switch (periodicRefreshPhase)
            {
                case PeriodicRefreshVisualPhase.FadingOut:
                    return 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(elapsed / periodicFadeOutSeconds));
                case PeriodicRefreshVisualPhase.WaitingForEvaluation:
                    return 0f;
                case PeriodicRefreshVisualPhase.FadingIn:
                    return Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(elapsed / periodicFadeInSeconds));
                default:
                    return 1f;
            }
        }

        private float GetIndividualRefreshVisibility(PersistentMarker marker)
        {
            if (!marker.IndividualRefreshActive)
                return 1f;

            float elapsed = Time.unscaledTime - marker.IndividualRefreshStartedAt;
            if (elapsed < changedStateFadeOutSeconds)
            {
                return 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(elapsed / changedStateFadeOutSeconds));
            }

            if (!marker.IndividualSpriteCommitted)
            {
                marker.IsPassable = marker.PendingIsPassable;
                marker.IndividualSpriteCommitted = true;
            }

            float fadeInElapsed = elapsed - changedStateFadeOutSeconds;
            if (fadeInElapsed < changedStateFadeInSeconds)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(fadeInElapsed / changedStateFadeInSeconds));
            }

            marker.IndividualRefreshActive = false;
            return 1f;
        }

        private Color GetRefreshColor(float visibility)
        {
            float amount = Mathf.Clamp01(visibility);
            Color color = Color.Lerp(map.BackgroundColor, Color.white, amount);
            color.a = Mathf.Lerp(refreshMinimumAlpha, 1f, amount);
            return color;
        }
        private void RenderMarkers()
        {
            if (overlayRoot == null || mapCamera == null)
                return;

            overlayRoot.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            passableRenderData.Clear();
            unpassableRenderData.Clear();
            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float currentRadius = scanChargeUi != null
                ? scanChargeUi.GetUiRingScreenRadiusPixels()
                : scannedUiRadiusPixels;
            float wave = scanIsRevealing ? Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((Time.unscaledTime - scanStartedAt) / scanWaveDuration)) : 1f;
            float radiusSquared = currentRadius * currentRadius * wave * wave;
            float exclusionScale = currentRadius
                                   / Mathf.Max(1f, scanChargeUi.UiRingRadiusPixels);
            float exclusion = (centerExclusionRadiusPixels
                               + iconSizePixels * 0.70710678f) * exclusionScale;
            float exclusionSquared = exclusion * exclusion;
            float periodicVisibility = GetPeriodicRefreshVisibility();

            for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
            {
                PersistentMarker marker = markers[markerIndex];
                Vector3 viewport = mapCamera.WorldToViewportPoint(
                    map.MapPositionToWorld(marker.MapPosition));
                if (viewport.z <= 0f
                    || viewport.x < 0f || viewport.x > 1f
                    || viewport.y < 0f || viewport.y > 1f)
                {
                    continue;
                }

                Vector2 screenPosition = new Vector2(
                    Mathf.Round(viewport.x * Screen.width),
                    Mathf.Round(viewport.y * Screen.height));
                float distanceSquared = (screenPosition - centre).sqrMagnitude;
                if (distanceSquared > radiusSquared
                    || distanceSquared < exclusionSquared)
                {
                    continue;
                }

                float individualVisibility =
                    GetIndividualRefreshVisibility(marker);
                float visibility = Mathf.Min(
                    periodicVisibility,
                    individualVisibility);
                var renderData = new TraversalSignRenderData(
                    screenPosition,
                    iconSizePixels,
                    fixedIconScreenAngleDegrees,
                    GetRefreshColor(visibility));
                if (marker.IsPassable)
                    passableRenderData.Add(renderData);
                else
                    unpassableRenderData.Add(renderData);
            }

            if (passableSignsGraphic != null)
                passableSignsGraphic.SetSigns(passableRenderData);
            if (unpassableSignsGraphic != null)
                unpassableSignsGraphic.SetSigns(unpassableRenderData);
        }

        private void CreateOverlayIfNeeded()
        {
            if (overlayRoot != null)
                return;

            overlayRoot = new GameObject(
                "Scanned Traversal Snapshot Overlay",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            overlayRoot.layer = LayerMask.NameToLayer("UI");
            overlayRoot.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            Canvas canvas = overlayRoot.GetComponent<Canvas>();
            OverlayCanvas = canvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.overrideSorting = true;
            canvas.sortingOrder = canvasSortingOrder;
            CanvasScaler scaler = overlayRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            overlayRoot.GetComponent<GraphicRaycaster>().enabled = false;

            passableSignsGraphic = CreateSignsGraphic(
                "Passable Traversal Signs",
                passableSign);
            unpassableSignsGraphic = CreateSignsGraphic(
                "Unpassable Traversal Signs",
                unpassableSign);
        }

        private TraversalSignsGraphic CreateSignsGraphic(
            string objectName,
            Sprite sprite)
        {
            var graphicObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TraversalSignsGraphic));
            graphicObject.layer = LayerMask.NameToLayer("UI");
            graphicObject.transform.SetParent(overlayRoot.transform, false);
            RectTransform rect = graphicObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            TraversalSignsGraphic graphic =
                graphicObject.GetComponent<TraversalSignsGraphic>();
            graphic.Initialize(sprite);
            return graphic;
        }

        private void PrewarmMarkerPool()
        {
            while (recycledMarkers.Count < maximumScannedSigns)
                recycledMarkers.Push(new PersistentMarker());
        }

        private void ClearSnapshot()
        {
            pendingSamples.Clear();
            sampledCandidates.Clear();
            unpassableSeeds.Clear();
            for (int index = 0; index < markers.Count; index++)
                recycledMarkers.Push(markers[index]);
            markers.Clear();
            passableRenderData.Clear();
            unpassableRenderData.Clear();
            if (passableSignsGraphic != null)
                passableSignsGraphic.ClearSigns();
            if (unpassableSignsGraphic != null)
                unpassableSignsGraphic.ClearSigns();
            candidateBuckets.Clear();
            selectedCandidates.Clear();
            nextSeed = nextNeighbor = nextProfile = 0;
            previousBucketCandidate = -1;
            nextBucketCandidate = -1;
            bucketActive = profileActive = false;
            scanHeightField = null;
            profile = default;
            ProfileCheckCount = ProfileSampleVisitCount = 0;
            WorstScanFrameMilliseconds = 0f;
            ScanFramesUsed = generatedGridCount = totalGridCount = 0;
            capturedCompletionFrames = 1;
            capturedProfileSamples = Mathf.Max(1, profileSamplesPerViewRadius);
            nextPendingSample = 0;
            nextRefreshMarker = 0;
            scanIsRevealing = false;
            refreshInProgress = false;
            periodicRefreshPhase = PeriodicRefreshVisualPhase.None;
            periodicRefreshPhaseStartedAt = 0f;
            realtimeRecheckOrder.Clear();
            nextRealtimeRecheck = 0;
            nextRealtimeRecheckAllowedAt = 0f;
            hasRealtimeRobotPosition = false;
            realtimeRecheckInProgress = false;
            realtimeRecheckRequested = false;
            snapshotExpiresAt = 0f;
            nextStateRefreshAt = 0f;
        }

        private void UnsubscribeFromScan()
        {
            if (scanChargeUi != null)
                scanChargeUi.TerrainScanRequested -= BeginScannedSnapshot;
        }

        private void OnDisable()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(true);
        }

        private void OnDestroy()
        {
            UnsubscribeFromScan();
            if (overlayRoot != null)
                Destroy(overlayRoot);
        }

        private void OnValidate()
        {
            sampleGridSpacingPixels = Mathf.Max(4f, sampleGridSpacingPixels);
            contourBoundaryHalfWidthMeters = Mathf.Max(
                0f,
                contourBoundaryHalfWidthMeters);
            terrainGradientProbeMeters = Mathf.Max(0.1f, terrainGradientProbeMeters);
            centerExclusionRadiusPixels = Mathf.Max(0f, centerExclusionRadiusPixels);
            maximumScannedSigns = Mathf.Clamp(maximumScannedSigns, 32, 4096);
            unpassableNeighborhoodRadiusMeters = Mathf.Max(
                0f,
                unpassableNeighborhoodRadiusMeters);
            markerLifetimeSeconds = Mathf.Max(0.1f, markerLifetimeSeconds);
            stateRefreshIntervalSeconds = Mathf.Max(
                0.05f,
                stateRefreshIntervalSeconds);
            refreshCalculationsPerFrame = Mathf.Clamp(
                refreshCalculationsPerFrame,
                1,
                512);
            refreshCalculationBudgetMilliseconds = Mathf.Max(
                0.1f,
                refreshCalculationBudgetMilliseconds);
            periodicFadeOutSeconds = Mathf.Max(0.02f, periodicFadeOutSeconds);
            periodicFadeInSeconds = Mathf.Max(0.02f, periodicFadeInSeconds);
            refreshMinimumAlpha = Mathf.Clamp01(refreshMinimumAlpha);
            changedStateFadeOutSeconds = Mathf.Max(
                0.02f,
                changedStateFadeOutSeconds);
            changedStateFadeInSeconds = Mathf.Max(
                0.02f,
                changedStateFadeInSeconds);
            realtimeMoveThresholdMeters = Mathf.Max(
                0.01f,
                realtimeMoveThresholdMeters);
            realtimeRecheckMinimumInterval = Mathf.Max(
                0.02f,
                realtimeRecheckMinimumInterval);
            realtimeRechecksPerFrame = Mathf.Clamp(
                realtimeRechecksPerFrame,
                1,
                512);
            realtimeCalculationBudgetMilliseconds = Mathf.Max(
                0.1f,
                realtimeCalculationBudgetMilliseconds);
            profileSamplesPerViewRadius = Mathf.Max(1, profileSamplesPerViewRadius);
            scanCompletionFrames = Mathf.Max(1, scanCompletionFrames);
            iconSizePixels = Mathf.Max(1f, iconSizePixels);
        }
    }
}

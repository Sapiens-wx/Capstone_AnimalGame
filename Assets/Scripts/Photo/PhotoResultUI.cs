using System;
using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    [DefaultExecutionOrder(360)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/UI/Photo Result UI")]
    public sealed class PhotoResultUI : MonoBehaviour
    {
        [Header("Result Prefab")]
        [Tooltip("Optional override. Defaults to Resources/UI/AnimalPhotoResultUI.")]
        [SerializeField] private PhotoResultView resultPrefab;

        [Header("Subject Detection")]
        [Tooltip("Shrinks the visible camera frame slightly before evaluating subjects, preventing a barely touching animal from counting as photographed.")]
        [SerializeField, Range(0f, 0.2f)]
        private float frameInsetNormalized = 0.025f;
        [Tooltip("Minimum fraction of the animal's authored body bounds that must lie inside the camera frame.")]
        [SerializeField, Range(0.01f, 1f)]
        private float minimumSubjectCoverage = 0.18f;
        [SerializeField, Min(1f)]
        private float minimumSubjectLongestSidePixels = 24f;
        [SerializeField, Min(1f)]
        private float minimumSubjectAreaPixels = 400f;
        [SerializeField, Min(0f)] private float coverageScoreWeight = 0.55f;
        [SerializeField, Min(0f)] private float centerednessScoreWeight = 0.3f;
        [SerializeField, Min(0f)] private float sizeScoreWeight = 0.15f;

        [Header("Keyboard Result Controls")]
        [SerializeField] private KeyCode closeKey = KeyCode.B;
        [SerializeField] private KeyCode alternateCloseKey = KeyCode.Escape;
        [SerializeField] private KeyCode saveKey = KeyCode.Y;

        private readonly Vector2[] captureFrameCorners = new Vector2[4];
        private readonly Vector3[] subjectWorldCorners = new Vector3[4];
        private readonly List<Vector2> clipInput = new List<Vector2>(12);
        private readonly List<Vector2> clipOutput = new List<Vector2>(12);

        private PhotoModeController controller;
        private PhotoModeUI photoModeUi;
        private Camera mapCamera;
        private MapTestSceneController map;

        private PhotoResultView resultView;
        private readonly PhotoContourCapture contourCapture = new PhotoContourCapture();
        private PhotoResultSnapshot pendingResult;
        private PhotoResultSnapshot displayedResult;
        private bool visible;
        private bool saved;

        public bool IsVisible => visible;

        private void Awake()
        {
            EnsureVisuals();
        }

        public void Initialize(
            PhotoModeController photoModeController,
            PhotoModeUI cameraUi,
            Camera camera,
            MapTestSceneController mapController)
        {
            if (controller != null)
                controller.PhotoCaptured -= HandlePhotoCaptured;

            controller = photoModeController;
            photoModeUi = cameraUi;
            mapCamera = camera;
            map = mapController;
            EnsureVisuals();

            if (controller != null)
                controller.PhotoCaptured += HandlePhotoCaptured;

            HideResult(false);
        }

        private void Update()
        {
            if (pendingResult != null
                && controller != null
                && controller.IsReviewing
                && !visible)
            {
                ShowPendingResult();
            }

            if (!visible)
            {
                if (pendingResult != null && (controller == null || !controller.IsActive))
                    HideResult(false);
                return;
            }

            if (controller == null
                || !controller.IsActive
                || !controller.IsReviewing)
            {
                HideResult(false);
                return;
            }

            if (resultView == null || resultView.IsClosing) return;
            bool closePressed = Input.GetKeyDown(closeKey)
                || Input.GetKeyDown(alternateCloseKey)
                || AdaptiveLegacyGamepadInput.WasEastFaceButtonPressedThisFrame();
            if (closePressed) { resultView.Close(); return; }
            bool savePressed = Input.GetKeyDown(saveKey)
                || AdaptiveLegacyGamepadInput.WasNorthFaceButtonPressedThisFrame();
            if (savePressed) SaveDisplayedResult();
        }

        private void HandlePhotoCaptured()
        {
            if (!isActiveAndEnabled) return;
            contourCapture.Dispose();
            pendingResult = null;
            if (!TrySelectMainSubject(
                    out AnimalPhotoSubject subject,
                    out float frameCoverage))
            {
                return;
            }

            AnimalResultPhoto selectedPhoto = null;
            bool hasLibraryPhoto = subject.TryChooseResultPhoto(
                out selectedPhoto);
            if (!hasLibraryPhoto)
            {
                Debug.LogWarning(
                    $"Photo result skipped because '{subject.name}' has no valid authored result photo.",
                    subject);
                return;
            }

            pendingResult = CreateSnapshot(
                subject,
                selectedPhoto,
                frameCoverage);
            EnsureVisuals();
            if (resultView == null || controller == null || !controller.RequestPhotoReview())
            {
                pendingResult = null;
                return;
            }
            // Freeze at the shutter event, before the review transition.
            contourCapture.Capture(mapCamera, map, resultView.SnapshotResolution);
        }

        private bool TrySelectMainSubject(
            out AnimalPhotoSubject selectedSubject,
            out float selectedCoverage)
        {
            selectedSubject = null;
            selectedCoverage = 0f;
            if (mapCamera == null
                || photoModeUi == null
                || !photoModeUi.TryGetCaptureFrameScreenCorners(
                    captureFrameCorners,
                    frameInsetNormalized))
            {
                return false;
            }

            Vector2 frameCenter = Vector2.zero;
            for (int index = 0; index < captureFrameCorners.Length; index++)
                frameCenter += captureFrameCorners[index];
            frameCenter *= 0.25f;

            float frameWidth = Mathf.Max(
                Vector2.Distance(
                    captureFrameCorners[0],
                    captureFrameCorners[3]),
                Vector2.Distance(
                    captureFrameCorners[1],
                    captureFrameCorners[2]));
            float frameHeight = Mathf.Max(
                Vector2.Distance(
                    captureFrameCorners[0],
                    captureFrameCorners[1]),
                Vector2.Distance(
                    captureFrameCorners[3],
                    captureFrameCorners[2]));
            float frameHalfDiagonal = Mathf.Max(
                1f,
                0.5f * Mathf.Sqrt(
                    frameWidth * frameWidth
                    + frameHeight * frameHeight));
            float frameLongestSide = Mathf.Max(1f, frameWidth, frameHeight);

            float bestScore = float.NegativeInfinity;
            foreach (AnimalPhotoSubject subject in AnimalPhotoSubject.Active)
            {
                if (subject == null
                    || !subject.IsPhotographable()
                    || !subject.TryGetWorldBounds(out Bounds worldBounds)
                    || !TryProjectBoundsToScreen(
                        worldBounds,
                        out Rect subjectScreenRect))
                {
                    continue;
                }

                float subjectArea = subjectScreenRect.width
                                    * subjectScreenRect.height;
                float subjectLongestSide = Mathf.Max(
                    subjectScreenRect.width,
                    subjectScreenRect.height);
                if (subjectArea < minimumSubjectAreaPixels
                    || subjectLongestSide
                    < minimumSubjectLongestSidePixels)
                {
                    continue;
                }

                float intersectionArea = CalculateRectFrameIntersectionArea(
                    subjectScreenRect,
                    captureFrameCorners);
                float coverage = subjectArea > 0.0001f
                    ? intersectionArea / subjectArea
                    : 0f;
                if (coverage < minimumSubjectCoverage)
                    continue;

                float centeredness = 1f - Mathf.Clamp01(
                    Vector2.Distance(
                        subjectScreenRect.center,
                        frameCenter)
                    / frameHalfDiagonal);
                float relativeSize = Mathf.Clamp01(
                    subjectLongestSide / frameLongestSide);
                float score = coverage * coverageScoreWeight
                              + centeredness * centerednessScoreWeight
                              + relativeSize * sizeScoreWeight;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                selectedSubject = subject;
                selectedCoverage = coverage;
            }

            return selectedSubject != null;
        }

        private bool TryProjectBoundsToScreen(
            Bounds bounds,
            out Rect screenRect)
        {
            screenRect = default;
            subjectWorldCorners[0] = new Vector3(
                bounds.min.x,
                bounds.min.y,
                bounds.center.z);
            subjectWorldCorners[1] = new Vector3(
                bounds.min.x,
                bounds.max.y,
                bounds.center.z);
            subjectWorldCorners[2] = new Vector3(
                bounds.max.x,
                bounds.max.y,
                bounds.center.z);
            subjectWorldCorners[3] = new Vector3(
                bounds.max.x,
                bounds.min.y,
                bounds.center.z);

            float minimumX = float.PositiveInfinity;
            float minimumY = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float maximumY = float.NegativeInfinity;
            for (int index = 0; index < subjectWorldCorners.Length; index++)
            {
                Vector3 screenPoint = mapCamera.WorldToScreenPoint(
                    subjectWorldCorners[index]);
                if (screenPoint.z <= 0f)
                    return false;

                minimumX = Mathf.Min(minimumX, screenPoint.x);
                minimumY = Mathf.Min(minimumY, screenPoint.y);
                maximumX = Mathf.Max(maximumX, screenPoint.x);
                maximumY = Mathf.Max(maximumY, screenPoint.y);
            }

            screenRect = Rect.MinMaxRect(
                minimumX,
                minimumY,
                maximumX,
                maximumY);
            return screenRect.width > 0.0001f
                   && screenRect.height > 0.0001f;
        }

        private float CalculateRectFrameIntersectionArea(
            Rect subjectRect,
            Vector2[] frameCorners)
        {
            clipInput.Clear();
            clipInput.Add(new Vector2(subjectRect.xMin, subjectRect.yMin));
            clipInput.Add(new Vector2(subjectRect.xMax, subjectRect.yMin));
            clipInput.Add(new Vector2(subjectRect.xMax, subjectRect.yMax));
            clipInput.Add(new Vector2(subjectRect.xMin, subjectRect.yMax));

            float frameOrientation = Mathf.Sign(
                CalculateSignedArea(frameCorners));
            if (Mathf.Approximately(frameOrientation, 0f))
                return 0f;

            List<Vector2> input = clipInput;
            List<Vector2> output = clipOutput;
            for (int edgeIndex = 0;
                 edgeIndex < frameCorners.Length;
                 edgeIndex++)
            {
                output.Clear();
                if (input.Count == 0)
                    return 0f;

                Vector2 edgeStart = frameCorners[edgeIndex];
                Vector2 edgeEnd = frameCorners[
                    (edgeIndex + 1) % frameCorners.Length];
                Vector2 previous = input[input.Count - 1];
                bool previousInside = IsInsideClipEdge(
                    previous,
                    edgeStart,
                    edgeEnd,
                    frameOrientation);

                for (int pointIndex = 0;
                     pointIndex < input.Count;
                     pointIndex++)
                {
                    Vector2 current = input[pointIndex];
                    bool currentInside = IsInsideClipEdge(
                        current,
                        edgeStart,
                        edgeEnd,
                        frameOrientation);
                    if (currentInside)
                    {
                        if (!previousInside)
                        {
                            output.Add(IntersectLines(
                                previous,
                                current,
                                edgeStart,
                                edgeEnd));
                        }

                        output.Add(current);
                    }
                    else if (previousInside)
                    {
                        output.Add(IntersectLines(
                            previous,
                            current,
                            edgeStart,
                            edgeEnd));
                    }

                    previous = current;
                    previousInside = currentInside;
                }

                List<Vector2> swap = input;
                input = output;
                output = swap;
            }

            return Mathf.Abs(CalculateSignedArea(input));
        }

        private static bool IsInsideClipEdge(
            Vector2 point,
            Vector2 edgeStart,
            Vector2 edgeEnd,
            float orientation)
        {
            float cross = Cross(edgeEnd - edgeStart, point - edgeStart);
            return cross * orientation >= -0.001f;
        }

        private static Vector2 IntersectLines(
            Vector2 segmentStart,
            Vector2 segmentEnd,
            Vector2 lineStart,
            Vector2 lineEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            Vector2 line = lineEnd - lineStart;
            float denominator = Cross(segment, line);
            if (Mathf.Abs(denominator) <= 0.00001f)
                return segmentEnd;

            float time = Cross(lineStart - segmentStart, line)
                         / denominator;
            return segmentStart + segment * time;
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static float CalculateSignedArea(IList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return 0f;

            float twiceArea = 0f;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 current = polygon[index];
                Vector2 next = polygon[(index + 1) % polygon.Count];
                twiceArea += current.x * next.y - next.x * current.y;
            }

            return twiceArea * 0.5f;
        }

        private PhotoResultSnapshot CreateSnapshot(
            AnimalPhotoSubject subject,
            AnimalResultPhoto selectedPhoto,
            float frameCoverage)
        {
            Vector2 mapPosition = Vector2.zero;
            float heightMeters = 0f;
            if (map != null)
            {
                map.TrySampleWorldPosition(
                    subject.transform.position,
                    out mapPosition,
                    out heightMeters);
            }

            string levelName = map != null && map.LevelAsset != null
                ? map.LevelAsset.name.Replace('_', ' ')
                : subject.RegionName;
            return new PhotoResultSnapshot(
                subject.SpeciesId,
                subject.DisplayName,
                subject.EnglishName,
                subject.ScientificName,
                string.IsNullOrWhiteSpace(subject.RegionName)
                    ? levelName
                    : subject.RegionName,
                subject.AccentColor,
                subject.CognitionDegrees,
                subject.BaseReward,
                subject.CognitionReward,
                selectedPhoto,
                mapPosition,
                heightMeters,
                Mathf.Clamp01(frameCoverage),
                DateTime.Now);
        }

        private void ShowPendingResult()
        {
            if (pendingResult == null) return;
            EnsureVisuals();
            if (resultView == null) { HideResult(true); return; }
            displayedResult = pendingResult;
            pendingResult = null;
            visible = true;
            saved = false;
            resultView.Show(displayedResult, contourCapture);
        }

        private void SaveDisplayedResult()
        {
            if (saved || displayedResult == null) return;
            PhotoAlbumService.Save(displayedResult);
            saved = true;
            resultView.SetSaved();
        }

        private void HandleViewClosed() => HideResult(true);

        private void HideResult(bool returnToCamera)
        {
            visible = false;
            pendingResult = null;
            displayedResult = null;
            saved = false;
            if (resultView != null) resultView.HideImmediately();
            contourCapture.Dispose();
            if (returnToCamera) controller?.EndPhotoReview();
        }

        private void EnsureVisuals()
        {
            if (resultView != null) return;
            PhotoResultView prefab = resultPrefab != null
                ? resultPrefab : Resources.Load<PhotoResultView>("UI/AnimalPhotoResultUI");
            if (prefab == null)
            {
                Debug.LogError("Missing Resources/UI/AnimalPhotoResultUI prefab.", this);
                return;
            }
            resultView = Instantiate(prefab, transform);
            resultView.name = "Animal Photo Result UI";
            if (!resultView.IsReady)
            {
                Destroy(resultView.gameObject);
                resultView = null;
                return;
            }
            resultView.Closed += HandleViewClosed;
        }

        private void OnDisable() => HideResult(true);

        private void OnDestroy()
        {
            if (controller != null) controller.PhotoCaptured -= HandlePhotoCaptured;
            if (resultView != null)
            {
                resultView.Closed -= HandleViewClosed;
                Destroy(resultView.gameObject);
            }
            contourCapture.Dispose();
        }

        private void OnValidate()
        {
            frameInsetNormalized = Mathf.Clamp(
                frameInsetNormalized,
                0f,
                0.2f);
            minimumSubjectCoverage = Mathf.Clamp01(
                minimumSubjectCoverage);
            minimumSubjectLongestSidePixels = Mathf.Max(
                1f,
                minimumSubjectLongestSidePixels);
            minimumSubjectAreaPixels = Mathf.Max(
                1f,
                minimumSubjectAreaPixels);
            coverageScoreWeight = Mathf.Max(0f, coverageScoreWeight);
            centerednessScoreWeight = Mathf.Max(
                0f,
                centerednessScoreWeight);
            sizeScoreWeight = Mathf.Max(0f, sizeScoreWeight);
        }
    }

    public sealed class PhotoResultSnapshot
    {
        public PhotoResultSnapshot(
            string speciesId,
            string displayName,
            string englishName,
            string scientificName,
            string regionName,
            Color accentColor,
            int cognitionDegrees,
            int baseReward,
            int cognitionReward,
            AnimalResultPhoto photo,
            Vector2 mapPositionMeters,
            float heightMeters,
            float frameCoverage,
            DateTime capturedAt)
        {
            SpeciesId = speciesId;
            DisplayName = displayName;
            EnglishName = englishName;
            ScientificName = scientificName;
            RegionName = regionName;
            AccentColor = accentColor;
            CognitionDegrees = cognitionDegrees;
            BaseReward = baseReward;
            CognitionReward = cognitionReward;
            Photo = photo;
            MapPositionMeters = mapPositionMeters;
            HeightMeters = heightMeters;
            FrameCoverage = frameCoverage;
            CapturedAt = capturedAt;
        }

        public string SpeciesId { get; }
        public string DisplayName { get; }
        public string EnglishName { get; }
        public string ScientificName { get; }
        public string RegionName { get; }
        public Color AccentColor { get; }
        public int CognitionDegrees { get; }
        public int BaseReward { get; }
        public int CognitionReward { get; }
        public int TotalReward => BaseReward + CognitionReward;
        public AnimalResultPhoto Photo { get; }
        public Vector2 MapPositionMeters { get; }
        public float HeightMeters { get; }
        public float FrameCoverage { get; }
        public DateTime CapturedAt { get; }
    }

    public sealed class SavedAnimalPhotoRecord
    {
        internal SavedAnimalPhotoRecord(PhotoResultSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public PhotoResultSnapshot Snapshot { get; }
    }

    public static class PhotoAlbumService
    {
        private static readonly List<SavedAnimalPhotoRecord> SavedPhotos =
            new List<SavedAnimalPhotoRecord>();

        public static IReadOnlyList<SavedAnimalPhotoRecord> Photos =>
            SavedPhotos;

        public static void Save(PhotoResultSnapshot snapshot)
        {
            if (snapshot != null)
                SavedPhotos.Add(new SavedAnimalPhotoRecord(snapshot));
        }
    }

}

using UnityEngine;

namespace AnimalGame.Tutorial
{
    public enum TerrainTutorialStep { ReachSurvey, ScanTerrain, ReadSigns, FollowRoute, Completed }

    /// <summary>Logical-map progress only. Scan requests come from the existing scan system.</summary>
    public sealed class TerrainTutorialProgress
    {
        private readonly Vector2[] approach;
        private readonly Vector2 destination;
        private readonly float arrivalRadius, surveyRadius, readingDuration;
        private float startingDistance;
        private bool satisfied;

        public TerrainTutorialStep Step { get; private set; }
        public int ApproachIndex { get; private set; }
        public float ReadingSeconds { get; private set; }
        public float DistanceMeters { get; private set; }
        public float ReadingDuration => readingDuration;
        public float ArrivalRadiusMeters => arrivalRadius;
        public float SurveyRadiusMeters => surveyRadius;
        public bool IsComplete => Step == TerrainTutorialStep.Completed;
        public bool CanContinue => IsComplete || satisfied;
        public bool HasTarget => !satisfied && (Step == TerrainTutorialStep.ReachSurvey || Step == TerrainTutorialStep.FollowRoute);
        public Vector2 SurveyPosition => approach[approach.Length - 1];
        public Vector2 CurrentTarget => Step == TerrainTutorialStep.FollowRoute ? destination : approach[ApproachIndex];
        public float NormalizedProgress => CanContinue ? 1f : Step == TerrainTutorialStep.ReadSigns
            ? Mathf.Clamp01(ReadingSeconds / readingDuration) : HasTarget && startingDistance > .001f
                ? Mathf.Clamp01(1f - Mathf.Max(0f, DistanceMeters - arrivalRadius) / startingDistance) : 0f;

        public TerrainTutorialProgress(Vector2[] approachPointsMeters, Vector2 destinationMeters,
            float arrivalRadiusMeters = 4f, float surveyRadiusMeters = 8f, float readingSeconds = 4f)
        {
            approach = approachPointsMeters != null && approachPointsMeters.Length > 0
                ? (Vector2[])approachPointsMeters.Clone() : new[] { Vector2.zero };
            destination = destinationMeters;
            arrivalRadius = Mathf.Max(.5f, arrivalRadiusMeters);
            surveyRadius = Mathf.Max(arrivalRadius, surveyRadiusMeters);
            readingDuration = Mathf.Max(1f, readingSeconds);
        }

        public void Sample(Vector2 positionMeters, float deltaTime, bool canDrive,
            bool panelExpanded, bool scanHasMarkers, bool stopped)
        {
            if (!canDrive || deltaTime <= 0f || !float.IsFinite(deltaTime)
                || !float.IsFinite(positionMeters.x) || !float.IsFinite(positionMeters.y) || CanContinue) return;
            DistanceMeters = Vector2.Distance(positionMeters, CurrentTarget);
            if (HasTarget)
            {
                if (startingDistance <= 0f) startingDistance = Mathf.Max(.001f, DistanceMeters - arrivalRadius);
                if (DistanceMeters > arrivalRadius) return;
                startingDistance = 0f;
                if (Step == TerrainTutorialStep.FollowRoute) satisfied = true;
                else if (ApproachIndex < approach.Length - 1) ApproachIndex++;
                else satisfied = true;
                return;
            }

            // Reading an acquired scan can continue while driving or exploring elsewhere.
            if (Step == TerrainTutorialStep.ReadSigns && panelExpanded && scanHasMarkers)
            {
                ReadingSeconds = Mathf.Min(readingDuration, ReadingSeconds + deltaTime);
                if (ReadingSeconds >= readingDuration)
                {
                    satisfied = true;
                }
            }
        }

        public bool IsInSurveyArea(Vector2 positionMeters) =>
            Vector2.Distance(positionMeters, SurveyPosition) <= surveyRadius;

        public bool RegisterTerrainScan(Vector2 positionMeters, bool canDrive)
        {
            if (!canDrive || !IsInSurveyArea(positionMeters)
                || (Step != TerrainTutorialStep.ScanTerrain && Step != TerrainTutorialStep.ReadSigns)) return false;
            if (Step == TerrainTutorialStep.ScanTerrain) satisfied = true;
            return true;
        }

        public bool Continue()
        {
            if (!satisfied || IsComplete) return false;
            Step++; satisfied = false; startingDistance = 0f;
            return true;
        }

        /// <summary>Expired snapshots can be scanned again without losing time already spent reading.</summary>
        public void RequireFreshScan()
        {
            if (Step == TerrainTutorialStep.ReadSigns && !satisfied)
            { Step = TerrainTutorialStep.ScanTerrain; satisfied = false; }
        }
    }
}

using UnityEngine;

namespace AnimalGame.RobotMap
{
    public sealed partial class RobotMarkerView
    {
        private bool openingMarkerPresentation;
        private float openingBodyOpacity = 1f;
        private float openingHeadingOpacity = 1f;
        internal float OpeningStatusOpacity { get; private set; } = 1f;

        public void SetOpeningPresentation(float bodyOpacity, float headingOpacity, float statusOpacity, float hatchOpen01)
        {
            openingMarkerPresentation = true;
            openingBodyOpacity = Mathf.Clamp01(bodyOpacity);
            openingHeadingOpacity = Mathf.Clamp01(headingOpacity);
            OpeningStatusOpacity = Mathf.Clamp01(statusOpacity);

            Color outline = bodyOutlineColor;
            outline.a *= openingBodyOpacity;
            bodyArtwork.color = outline;
            leftBodyHatchArtwork.color = outline;
            rightBodyHatchArtwork.color = outline;
            SynchronizeBodyFillColor();
            outline.a = bodyOutlineColor.a * OpeningStatusOpacity;
            innerRingArtwork.color = outline;
            StepDirectionIndicatorSurfaceProjection(0f);

            float opening = Mathf.Clamp01(hatchOpen01);
            if (opening == bodyHatchOpen01) return;
            bodyHatchOpen01 = opening;
            ApplyBodyHatchPose();
        }

        public void EndOpeningPresentation()
        {
            if (!openingMarkerPresentation) return;
            SetOpeningPresentation(1f, 1f, 1f, 0f);
            openingMarkerPresentation = false;
        }
    }
}

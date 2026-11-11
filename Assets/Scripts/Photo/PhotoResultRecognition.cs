using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.RobotMap
{
    /// <summary>Fills the gold arc by recognition percentage, with an inward radial marker.</summary>
    public sealed class PhotoResultRecognition
    {
        private const float Duration = 1f;
        private const float MarkerLength = 30.5f;
        private PhotoResultArcElement arc;
        private PhotoResultLineElement marker;
        private float startArcDegrees, fullSweepDegrees;
        private float before, after, current, elapsed;
        private bool active;
        public int DisplayedRecognition => Mathf.RoundToInt(current);

        public void Bind(VisualElement root)
        {
            arc = root.Q<PhotoResultArcElement>("gold-orbit");
            marker = root.Q<PhotoResultLineElement>("gold-diagonal-cont");
            var upper = root.Q<PhotoResultLineElement>("white-upper-diagonal");
            var lower = root.Q<PhotoResultLineElement>("white-diagonal");
            // These are authored design coordinates; shared zoom/scale must not affect the angles.
            Vector2 start = CircleIntersection(upper.startPoint, (upper.endPoint - upper.startPoint).normalized) - arc.center;
            Vector2 end = CircleIntersection(lower.startPoint, (lower.endPoint - lower.startPoint).normalized) - arc.center;
            startArcDegrees = Mathf.Atan2(start.y, start.x) * Mathf.Rad2Deg;
            fullSweepDegrees = Mathf.Repeat(Mathf.Atan2(end.y, end.x) * Mathf.Rad2Deg - startArcDegrees, 360);
            arc.startDegrees = startArcDegrees;
            ApplyGeometry();
        }

        public void Begin(int recognitionBefore, int recognitionAfter)
        {
            before = Mathf.Clamp(recognitionBefore, 0, 100);
            after = Mathf.Clamp(recognitionAfter, 0, 100);
            current = before;
            elapsed = 0;
            active = before != after;
            ApplyGeometry();
        }

        public void Tick(float unscaledDeltaTime, bool fullyOpen, bool covered, bool closing)
        {
            if (closing) { Cancel(); return; }
            if (!active || !fullyOpen || covered) return;
            elapsed = Mathf.Min(Duration, elapsed + unscaledDeltaTime);
            float next = Mathf.Lerp(before, after, Mathf.SmoothStep(0, 1, elapsed / Duration));
            if (current != next) { current = next; ApplyGeometry(); }
            if (elapsed >= Duration) active = false;
        }

        public void Cancel() => active = false;

        private Vector2 CircleIntersection(Vector2 vertex, Vector2 direction)
        {
            Vector2 offset = vertex - arc.center;
            float projection = Vector2.Dot(offset, direction);
            float distance = -projection + Mathf.Sqrt(projection * projection
                + arc.radius * arc.radius - offset.sqrMagnitude);
            return vertex + direction * distance;
        }

        private void ApplyGeometry()
        {
            arc.sweepDegrees = fullSweepDegrees * current / 100f;
            float radians = (startArcDegrees + arc.sweepDegrees) * Mathf.Deg2Rad;
            Vector2 radial = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 point = arc.center + radial * arc.radius;
            marker.startPoint = point;
            marker.endPoint = point - radial * MarkerLength;
        }
    }
}

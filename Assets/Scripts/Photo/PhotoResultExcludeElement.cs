using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.RobotMap
{
    /// <summary>An invisible layout rectangle that masks photo-result vector strokes.</summary>
    [UxmlElement]
    public partial class PhotoResultExcludeElement : VisualElement
    {
    }

    /// <summary>Rebuilds clipped points only when source geometry or relative mask layout changes.</summary>
    internal sealed class PhotoResultExclusionCache
    {
        private readonly VisualElement owner;
        private readonly Action changed;
        private readonly List<PhotoResultExcludeElement> elements = new List<PhotoResultExcludeElement>();
        private readonly List<Rect> relativeRects = new List<Rect>();
        private readonly List<Rect> rects = new List<Rect>();
        private readonly List<string> names = new List<string>();
        private readonly HashSet<VisualElement> layoutSources = new HashSet<VisualElement>();
        private Vector2[] sourcePoints = Array.Empty<Vector2>();
        private readonly List<Vector2> intervals = new List<Vector2>();
        private readonly List<Vector2> clippedPoints = new List<Vector2>();
        private readonly List<bool> clippedExcluded = new List<bool>();
        private const float Epsilon = 0.000001f;
        private bool dirty = true;
        private IVisualElementScheduledItem watcher;
        public bool[] Excluded { get; private set; } = Array.Empty<bool>();
        public Vector2[] Points { get; private set; } = Array.Empty<Vector2>();
        public bool IsReady => names.Count == 0 || !dirty;

        public PhotoResultExclusionCache(VisualElement owner, Action changed)
        {
            this.owner = owner;
            this.changed = changed;
            owner.RegisterCallback<AttachToPanelEvent>(_ => { elements.Clear(); dirty = true; });
            owner.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ClearLayoutSources();
                elements.Clear();
                dirty = true;
            });
            owner.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
        }

        public void Configure(List<string> excludedNames, Vector2[] geometry)
        {
            names.Clear();
            foreach (string name in excludedNames)
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            sourcePoints = geometry;
            Points = geometry;
            ClearLayoutSources();
            elements.Clear();
            relativeRects.Clear();
            rects.Clear();
            Excluded = new bool[geometry.Length];
            dirty = true;
            if (names.Count == 0) { watcher?.Pause(); return; }
            if (watcher == null) watcher = owner.schedule.Execute(Refresh).Every(1);
            else watcher.Resume();
            Refresh();
        }

        private static bool Near(Rect a, Rect b)
        {
            // Ignore floating-point noise from cancelling a shared ancestor transform.
            return (a.position - b.position).sqrMagnitude < 0.000001f
                && (a.size - b.size).sqrMagnitude < 0.000001f;
        }

        private void ClearLayoutSources()
        {
            foreach (var element in layoutSources)
                element.UnregisterCallback<GeometryChangedEvent>(OnRelatedLayout);
            layoutSources.Clear();
        }

        private void WatchAncestors(VisualElement element)
        {
            while (element != null)
            {
                if (layoutSources.Add(element))
                    element.RegisterCallback<GeometryChangedEvent>(OnRelatedLayout);
                element = element.parent;
            }
        }

        private void OnRelatedLayout(GeometryChangedEvent evt) => Refresh();

        private void Refresh()
        {
            if (names.Count == 0 || owner.panel == null) return;
            bool resolve = elements.Count != names.Count;
            for (int i = 0; !resolve && i < elements.Count; i++)
                resolve = elements[i] == null || elements[i].panel != owner.panel || elements[i].name != names[i];
            if (resolve)
            {
                ClearLayoutSources();
                WatchAncestors(owner.parent);
                VisualElement root = owner;
                while (root.parent != null) root = root.parent;
                for (int i = 0; i < names.Count; i++)
                {
                    var element = root.Q<PhotoResultExcludeElement>(names[i]);
                    if (i >= elements.Count) { elements.Add(element); dirty = true; }
                    else if (elements[i] != element) { elements[i] = element; dirty = true; }
                    WatchAncestors(element);
                }
            }

            // Scheduled after attachment: defer until the initial layout is available.
            foreach (var element in elements)
                if (element != null && (float.IsNaN(element.layout.width) || float.IsNaN(element.layout.height))) return;

            // Relative bounds include changes on either ancestor branch. A common
            // translation/scale cancels, so zoom-content does not resample the masks.
            for (int i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                Rect relative = element == null ? default
                    : element.ChangeCoordinatesTo(owner, new Rect(Vector2.zero, element.layout.size));
                if (i >= relativeRects.Count) { relativeRects.Add(relative); dirty = true; }
                else if (!Near(relativeRects[i], relative)) { relativeRects[i] = relative; dirty = true; }
            }
            if (!dirty) return;
            rects.Clear();
            for (int i = 0; i < elements.Count; i++)
                if (elements[i] != null && relativeRects[i].width > 0 && relativeRects[i].height > 0)
                    rects.Add(relativeRects[i]);
            RebuildClippedGeometry();
            dirty = false;
            changed();
        }

        private void RebuildClippedGeometry()
        {
            clippedPoints.Clear();
            clippedExcluded.Clear();
            if (sourcePoints.Length != 0) Append(sourcePoints[0], Contains(sourcePoints[0]));
            for (int i = 1; i < sourcePoints.Length; i++)
            {
                Vector2 a = sourcePoints[i - 1], b = sourcePoints[i];
                intervals.Clear();
                foreach (Rect rect in rects)
                    if (Clip(a, b, rect, out Vector2 interval)) intervals.Add(interval);
                intervals.Sort((aInterval, bInterval) => aInterval.x.CompareTo(bInterval.x));
                for (int j = 0; j < intervals.Count; j++)
                {
                    Vector2 interval = intervals[j];
                    while (j + 1 < intervals.Count && intervals[j + 1].x <= interval.y + Epsilon)
                        interval.y = Mathf.Max(interval.y, intervals[++j].y);
                    if (interval.x > 0f) Append(Vector2.Lerp(a, b, interval.x), false);
                    // A hidden interior point breaks the path even when both original endpoints are outside.
                    Append(Vector2.Lerp(a, b, (interval.x + interval.y) * 0.5f), true);
                    if (interval.y < 1f) Append(Vector2.Lerp(a, b, interval.y), false);
                }
                Append(b, Contains(b));
            }
            Points = clippedPoints.ToArray();
            Excluded = clippedExcluded.ToArray();
        }

        private void Append(Vector2 point, bool excluded)
        {
            int last = clippedPoints.Count - 1;
            // Never merge a hidden break with a visible boundary endpoint.
            if (last >= 0 && clippedExcluded[last] == excluded
                && (clippedPoints[last] - point).sqrMagnitude <= Epsilon * Epsilon) return;
            clippedPoints.Add(point);
            clippedExcluded.Add(excluded);
        }

        private static bool Clip(Vector2 a, Vector2 b, Rect rect, out Vector2 interval)
        {
            float enter = 0f, exit = 1f;
            interval = default;
            if ((b - a).sqrMagnitude <= Epsilon * Epsilon) return false;
            if (!ClipAxis(a.x, b.x - a.x, rect.xMin, rect.xMax, ref enter, ref exit)
                || !ClipAxis(a.y, b.y - a.y, rect.yMin, rect.yMax, ref enter, ref exit)
                || exit - enter <= Epsilon) return false;
            interval = new Vector2(enter, exit);
            return true;
        }

        private static bool ClipAxis(float origin, float delta, float min, float max,
            ref float enter, ref float exit)
        {
            // The rectangle interior is hidden; tangencies and strokes along its boundary remain visible.
            if (Mathf.Abs(delta) <= Epsilon) return origin > min && origin < max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }

        public bool Intersects(Vector2 a, Vector2 b)
        {
            foreach (Rect rect in rects)
                if (Clip(a, b, rect, out _)) return true;
            return false;
        }

        public bool Contains(Vector2 point)
        {
            if (rects.Count == 0) return false;
            foreach (Rect rect in rects)
                if (point.x > rect.xMin && point.x < rect.xMax
                    && point.y > rect.yMin && point.y < rect.yMax) return true;
            return false;
        }
    }
}

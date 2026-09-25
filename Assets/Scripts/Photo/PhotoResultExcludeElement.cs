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

    /// <summary>Samples masks only when geometry or relative layout changes, never in Draw.</summary>
    internal sealed class PhotoResultExclusionCache
    {
        private readonly VisualElement owner;
        private readonly bool localSpace;
        private readonly Action changed;
        private readonly List<PhotoResultExcludeElement> elements = new List<PhotoResultExcludeElement>();
        private readonly List<Rect> relativeRects = new List<Rect>();
        private readonly List<Rect> rects = new List<Rect>();
        private readonly List<string> names = new List<string>();
        private readonly HashSet<VisualElement> layoutSources = new HashSet<VisualElement>();
        private Vector2[] points = Array.Empty<Vector2>();
        private Matrix4x4 pointTransform;
        private bool dirty = true;
        private IVisualElementScheduledItem watcher;
        public bool[] Excluded { get; private set; } = Array.Empty<bool>();
        public bool IsReady => names.Count == 0 || !dirty;

        public PhotoResultExclusionCache(VisualElement owner, bool localSpace, Action changed)
        {
            this.owner = owner;
            this.localSpace = localSpace;
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
            points = geometry;
            ClearLayoutSources();
            elements.Clear();
            relativeRects.Clear();
            rects.Clear();
            Excluded = names.Count == 0 ? Array.Empty<bool>() : new bool[points.Length];
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
            if (names.Count == 0 || owner.panel == null || !(owner.layout.width > 0)
                || !(owner.layout.height > 0)) return;
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
            dirty = false;
            rects.Clear();
            for (int i = 0; i < elements.Count; i++)
                if (elements[i] != null) rects.Add(localSpace ? relativeRects[i] : elements[i].worldBound);
            // Lines retain world-space masks and the matching transform snapshot.
            // This also lets a moving tip reuse the masks after shared movement.
            pointTransform = owner.worldTransform;
            if (Excluded.Length != points.Length) Excluded = new bool[points.Length];
            for (int i = 0; i < points.Length; i++) Excluded[i] = Contains(points[i]);
            changed();
        }

        public bool Contains(Vector2 point)
        {
            if (rects.Count == 0) return false;
            if (!localSpace) point = pointTransform.MultiplyPoint3x4(point);
            foreach (Rect rect in rects)
                if (rect.Contains(point)) return true;
            return false;
        }
    }
}

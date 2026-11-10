using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    public sealed class CatalogSidebarView
    {
        public const int VisibleSlots = 6;
        private const float ScrollDuration = .22f;
        private readonly ScrollView scroll;
        private readonly List<AnimalSpecies> animals = new();
        private readonly List<CatalogSidebarItem> items = new();
        private readonly List<bool> selectable = new();
        private readonly IVisualElementScheduledItem animation;
        private Action<int> select;
        private int selected = -1;
        private float slotHeight, from, target, started;
        private bool animating;

        public CatalogSidebarView(ScrollView scroll)
        {
            this.scroll = scroll;
            animation = scroll.schedule.Execute(Animate).Every(16);
            animation.Pause();
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => ResizeSlots());
            scroll.RegisterCallback<DetachFromPanelEvent>(_ => StopAnimation());
        }

        public void Bind(IReadOnlyList<AnimalSpecies> species, Func<AnimalSpecies, bool> identified,
            Func<AnimalSpecies, string> name, int selectedIndex, Action<int> onSelect)
        {
            select = onSelect;
            bool rebuild = animals.Count != species.Count;
            for (int i = 0; !rebuild && i < animals.Count; i++) rebuild = animals[i] != species[i];
            if (rebuild)
            {
                StopAnimation(); scroll.Clear(); items.Clear(); animals.Clear(); animals.AddRange(species);
                for (int i = 0; i < animals.Count; i++)
                {
                    int index = i;
                    var item = new CatalogSidebarItem { focusable = false };
                    if (slotHeight > 0) item.style.height = slotHeight;
                    item.clicked += () => select(index);
                    items.Add(item); scroll.Add(item);
                }
            }
            selectable.Clear();
            foreach (var animal in animals) selectable.Add(identified(animal));
            for (int i = 0; i < items.Count; i++)
            {
                bool enabled = i < animals.Count && selectable[i];
                items[i].text = enabled ? name(animals[i]) : "";
                items[i].SetEnabled(enabled);
                items[i].Selected = enabled && i == selectedIndex;
            }
            int direction = selectedIndex >= selected ? 1 : -1;
            selected = selectedIndex;
            if (selected < 0) StopAnimation();
            ResizeSlots();
            if (selected >= 0) scroll.schedule.Execute(() => RevealSelection(direction));
        }

        public int FindNextSelectable(int current, int direction)
        {
            int start = current < 0 ? (direction > 0 ? -1 : 0) : current;
            for (int step = 1; step <= animals.Count; step++)
            {
                int i = ((start + step * direction) % animals.Count + animals.Count) % animals.Count;
                if (i != current && selectable[i]) return i;
            }
            return -1;
        }

        private void ResizeSlots()
        {
            float height = scroll.contentViewport.layout.height / VisibleSlots;
            if (height <= 0 || float.IsNaN(height) || Mathf.Approximately(height, slotHeight)) return;
            StopAnimation(); slotHeight = height;
            foreach (var item in items) item.style.height = slotHeight;
            if (selected >= 0) scroll.schedule.Execute(() => RevealSelection(1));
        }

        private void RevealSelection(int direction)
        {
            if (selected < 0 || slotHeight <= 0) return;
            float offset = animating ? target : scroll.scrollOffset.y;
            float top = selected * slotHeight, bottom = top + slotHeight;
            float viewport = slotHeight * VisibleSlots;
            if (top >= offset - 1f && bottom <= offset + viewport + 1f) return;
            target = Mathf.Clamp(direction > 0 ? top : bottom - viewport,
                0, Mathf.Max(0, (items.Count - VisibleSlots) * slotHeight));
            from = scroll.scrollOffset.y;
            started = Time.realtimeSinceStartup;
            animating = true; animation.Resume();
        }

        private void Animate()
        {
            float t = Mathf.Clamp01((Time.realtimeSinceStartup - started) / ScrollDuration);
            scroll.scrollOffset = new Vector2(0, Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, t)));
            if (t >= 1) StopAnimation();
        }

        public void StopAnimation() { animating = false; animation.Pause(); }
        public void Reset() { StopAnimation(); selected = -1; scroll.scrollOffset = Vector2.zero; }
    }
}

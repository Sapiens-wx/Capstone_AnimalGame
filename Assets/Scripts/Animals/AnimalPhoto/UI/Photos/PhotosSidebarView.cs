using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    public sealed class PhotosSidebarView
    {
        private readonly ScrollView scroll;
        private readonly List<AnimalSpecies?> animals = new();
        private readonly List<Button> items = new();
        private readonly IVisualElementScheduledItem animation;
        private Action<int> select;
        private int selected = -1;
        private float from, target, started;
        private bool animating;

        public PhotosSidebarView(ScrollView scroll)
        {
            this.scroll = scroll;
            animation = scroll.schedule.Execute(Animate).Every(16);
            animation.Pause();
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => RevealSelection(1));
            scroll.RegisterCallback<DetachFromPanelEvent>(_ => StopAnimation());
        }

        public void Bind(IReadOnlyList<AnimalSpecies?> species, Func<AnimalSpecies?, string> name,
            Func<AnimalSpecies?, int> count, int selectedIndex, Action<int> onSelect)
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
                    var item = new Button(() => select(index)) { focusable = false };
                    item.AddToClassList("collection"); item.AddToClassList("photo-collection");
                    var title = new Label { pickingMode = PickingMode.Ignore }; title.AddToClassList("collection-name");
                    var records = new Label { pickingMode = PickingMode.Ignore }; records.AddToClassList("collection-records");
                    item.Add(title); item.Add(records); items.Add(item); scroll.Add(item);
                }
            }
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Q<Label>(className: "collection-name").text = name(animals[i]);
                items[i].Q<Label>(className: "collection-records").text = $"{count(animals[i]):00} RECORDS";
                items[i].EnableInClassList("selected", i == selectedIndex);
            }
            int direction = selectedIndex >= selected ? 1 : -1;
            selected = selectedIndex;
            scroll.schedule.Execute(() => RevealSelection(direction));
        }

        private void RevealSelection(int direction)
        {
            if (selected < 0 || scroll.contentViewport.layout.height <= 0) return;
            var item = items[selected];
            float top = item.layout.y, bottom = item.layout.yMax;
            float viewport = scroll.contentViewport.layout.height;
            float offset = animating ? target : scroll.scrollOffset.y;
            if (top >= offset - 1f && bottom <= offset + viewport + 1f) return;
            target = Mathf.Clamp(direction > 0 ? top : bottom - viewport,
                0, Mathf.Max(0, scroll.contentContainer.layout.height - viewport));
            from = scroll.scrollOffset.y; started = Time.realtimeSinceStartup;
            animating = true; animation.Resume();
        }

        private void Animate()
        {
            float t = Mathf.Clamp01((Time.realtimeSinceStartup - started) / .22f);
            scroll.scrollOffset = new Vector2(0, Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, t)));
            if (t >= 1) StopAnimation();
        }

        public void StopAnimation() { animating = false; animation.Pause(); }
        public void Reset() { StopAnimation(); selected = -1; scroll.scrollOffset = Vector2.zero; }
    }
}

using System;
using System.Collections.Generic;
using AnimalGame.Save;

namespace AnimalGame.Animals.UI
{
    // Selection is independent of rendering, so scrolling and restoration use the same rules.
    public sealed class PhotoCollection
    {
        public readonly List<AnimalPhotoRecord> Photos = new();
        public int Selected, TopRow;
        public bool SortFocused, OldestFirst;
        public AnimalPhotoRecord Current => Photos.Count == 0 ? null : Photos[Selected];

        public void Bind(IEnumerable<AnimalPhotoRecord> photos, AnimalSpecies? species = null)
        {
            string selectedId = Current?.id;
            Photos.Clear();
            foreach (var photo in photos) if (!species.HasValue || photo.species == species.Value) Photos.Add(photo);
            Sort(selectedId);
        }

        public void ToggleSort() { OldestFirst = !OldestFirst; Sort(Current?.id); }
        private void Sort(string id)
        {
            Photos.Sort((a, b) => {
                int order = a.capturedTicks.CompareTo(b.capturedTicks);
                if (order == 0) order = string.CompareOrdinal(a.id, b.id);
                return OldestFirst ? order : -order;
            });
            Selected = Math.Max(0, Photos.FindIndex(p => p.id == id));
            TopRow = Math.Max(0, Selected / 3 - 1);
        }

        public void Move(int x, int y, bool inspection)
        {
            if (Photos.Count == 0) return;
            if (SortFocused) { if (y < 0) { SortFocused = false; Selected = 0; TopRow = 0; } return; }
            if (!inspection && y > 0 && Selected / 3 == 0) { SortFocused = true; return; }
            int next = Selected;
            if (y != 0) next += y > 0 ? -3 : 3;
            else if (x != 0) next += x;
            if (next < 0 || next >= Photos.Count) return;
            Selected = next;
            TopRow = Math.Max(Math.Min(TopRow, Selected / 3), Selected / 3 - 1);
        }
    }
}

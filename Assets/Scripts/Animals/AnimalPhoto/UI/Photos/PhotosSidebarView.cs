using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    public sealed class PhotosSidebarView
    {
        private readonly CatalogSidebarView sidebar;

        public PhotosSidebarView(ScrollView scroll) => sidebar = new CatalogSidebarView(scroll);

        public void Bind(IReadOnlyList<AnimalSpecies?> species, Func<AnimalSpecies?, string> name,
            int selectedIndex, Action<int> onSelect)
        {
            // Unknown is the shared sidebar's identity for the unfiltered All Collection.
            var identities = species.Select(animal => animal ?? AnimalSpecies.Unknown).ToArray();
            sidebar.Bind(identities, _ => true,
                animal => name(animal == AnimalSpecies.Unknown ? (AnimalSpecies?)null : animal),
                selectedIndex, onSelect);
        }

        public int FindNextSelectable(int current, int direction) => sidebar.FindNextSelectable(current, direction);
        public void StopAnimation() => sidebar.StopAnimation();
        public void Reset() => sidebar.Reset();
    }
}

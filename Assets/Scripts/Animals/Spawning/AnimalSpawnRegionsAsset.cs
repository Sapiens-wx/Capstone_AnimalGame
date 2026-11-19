using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    public sealed class AnimalSpawnRegionsAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Ring
        {
            public Vector2[] vertices = Array.Empty<Vector2>();
        }

        [Serializable]
        public sealed class Polygon
        {
            // Map XY in meters from the bottom-left corner. The closing edge is implicit.
            public Vector2[] vertices = Array.Empty<Vector2>();
            public Ring[] holes = Array.Empty<Ring>();

            public ref Vector2[] GetRing(int index)
            {
                if (index == 0)
                    return ref vertices;

                return ref holes[index - 1].vertices;
            }
        }

        [Serializable]
        public sealed class SpeciesRegions
        {
            public AnimalSpecies species;
            public Polygon[] polygons = Array.Empty<Polygon>();
        }

        [SerializeField] private SpeciesRegions[] speciesRegions = Array.Empty<SpeciesRegions>();

        public IReadOnlyList<SpeciesRegions> Regions => speciesRegions;

        public SpeciesRegions GetRegions(AnimalSpecies species)
        {
            foreach (SpeciesRegions regions in speciesRegions)
            {
                if (regions.species == species)
                    return regions;
            }

            return null;
        }
    }
}

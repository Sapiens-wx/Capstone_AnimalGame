using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    public sealed class AnimalSpawnRegionsAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Polygon
        {
            // Map XY in meters from the bottom-left corner. The closing edge is implicit.
            public Vector2[] vertices = Array.Empty<Vector2>();
        }

        [Serializable]
        public sealed class SpeciesRegions
        {
            public Polygon[] polygons = Array.Empty<Polygon>();
        }

        [SerializeField] private SpeciesRegions muskrat = new();
        [SerializeField] private SpeciesRegions pileatedWoodpecker = new();
        [SerializeField] private SpeciesRegions mountainGoat = new();
        [SerializeField] private SpeciesRegions wolf = new();
        [SerializeField] private SpeciesRegions americanBison = new();
        [SerializeField] private SpeciesRegions pronghorn = new();
        [SerializeField] private SpeciesRegions greaterPrairieChicken = new();
        [SerializeField] private SpeciesRegions groundhog = new();
        [SerializeField] private SpeciesRegions northAmericanPorcupine = new();

        public SpeciesRegions GetRegions(AnimalSpecies species) => species switch
        {
            AnimalSpecies.Muskrat => muskrat,
            AnimalSpecies.PileatedWoodpecker => pileatedWoodpecker,
            AnimalSpecies.MountainGoat => mountainGoat,
            AnimalSpecies.Wolf => wolf,
            AnimalSpecies.AmericanBison => americanBison,
            AnimalSpecies.Pronghorn => pronghorn,
            AnimalSpecies.GreaterPrairieChicken => greaterPrairieChicken,
            AnimalSpecies.Groundhog => groundhog,
            AnimalSpecies.NorthAmericanPorcupine => northAmericanPorcupine,
            _ => throw new ArgumentOutOfRangeException(nameof(species), species, null)
        };
    }
}

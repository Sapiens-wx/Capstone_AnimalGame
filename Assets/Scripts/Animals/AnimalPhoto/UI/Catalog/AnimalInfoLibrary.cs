using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    [Serializable]
    public sealed class AnimalInfo
    {
        public AnimalSpecies Species;
        public string Region;
        [TextArea] public string PhysicalCharacteristics, Classification, Distribution, Habitat, ConservationStatus;
        [TextArea] public string Lifespan, Description, ActivityPattern, Diet, SocialBehavior;
        public Texture2D Illustration, DistributionMap;
    }

    [CreateAssetMenu(menuName = "Animal Game/Animals/Animal Info Library")]
    public sealed class AnimalInfoLibrary : ScriptableObject
    {
        [SerializeField] private List<AnimalInfo> animals = new();
        private Dictionary<AnimalSpecies, AnimalInfo> bySpecies;
        public IReadOnlyDictionary<AnimalSpecies, AnimalInfo> BySpecies { get { BuildIndex(); return bySpecies; } }
        public void BuildIndex()
        {
            if (bySpecies != null) return;
            bySpecies = new();
            foreach (var animal in animals) bySpecies.Add(animal.Species, animal);
        }
        private void OnEnable() => bySpecies = null;
        private void OnValidate() => bySpecies = null;
    }
}

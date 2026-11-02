using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    [Serializable]
    public sealed class AnimalInfoEntry
    {
        public AnimalSpecies Species;
        public AnimalPhotoLibrary photo;
        public AnimalSpeciesConfig behaviour;
        public AnimalInfo info;
    }

    [CreateAssetMenu(menuName = "Animal Game/Animals/Animal Info Library")]
    public sealed class AnimalInfoLibrary : ScriptableObject
    {
        [SerializeField] private List<AnimalInfoEntry> animals = new();
        private Dictionary<AnimalSpecies, AnimalInfoEntry> bySpecies;
        private Dictionary<AnimalSpecies, AnimalPhotoLibrary> photos;
        private Dictionary<AnimalSpecies, AnimalSpeciesConfig> behaviours;
        private Dictionary<AnimalSpecies, AnimalInfo> information;
        private Dictionary<UnityEngine.Object, AnimalSpecies> speciesByAsset;
        private static AnimalInfoLibrary active;
        public IReadOnlyList<AnimalInfoEntry> Animals => animals;
        public IReadOnlyDictionary<AnimalSpecies, AnimalInfoEntry> BySpecies { get { BuildIndex(); return bySpecies; } }
        public IReadOnlyDictionary<AnimalSpecies, AnimalPhotoLibrary> Photos { get { BuildIndex(); return photos; } }
        public IReadOnlyDictionary<AnimalSpecies, AnimalSpeciesConfig> Behaviours { get { BuildIndex(); return behaviours; } }
        public IReadOnlyDictionary<AnimalSpecies, AnimalInfo> Info { get { BuildIndex(); return information; } }
        public static AnimalInfoLibrary Active => active != null ? active : active = Resources.Load<AnimalArchiveAssets>("UI/AnimalCatalog/AnimalArchiveAssets").animalInfo;

        public AnimalPhotoLibrary GetPhotoLibrary(AnimalSpecies species) => BySpecies[species].photo;
        public bool TryChoosePhoto(AnimalSpecies species, AnimalPhotoEnvironment environment, out AnimalPhoto photo)
            => AnimalPhotoSelector.TryChoosePhoto(this, species, environment, out photo);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize() { Active.bySpecies = null; Active.BuildIndex(); }

        public void BuildIndex()
        {
            if (bySpecies != null) return;
            var entries = new Dictionary<AnimalSpecies, AnimalInfoEntry>();
            photos = new(); behaviours = new(); information = new(); speciesByAsset = new();
            foreach (var animal in animals)
            {
                entries.Add(animal.Species, animal);
                photos.Add(animal.Species, animal.photo);
                behaviours.Add(animal.Species, animal.behaviour);
                information.Add(animal.Species, animal.info);
                speciesByAsset.Add(animal.photo, animal.Species);
                speciesByAsset.Add(animal.behaviour, animal.Species);
                speciesByAsset.Add(animal.info, animal.Species);
            }
            bySpecies = entries;
        }
        public AnimalSpecies GetSpecies(UnityEngine.Object asset)
        {
            BuildIndex();
            return speciesByAsset[asset];
        }
        public void SetBehaviour(AnimalSpecies species, AnimalSpeciesConfig config)
        {
            var entry = animals.Find(a => a.Species == species);
            if (entry == null) throw new InvalidOperationException($"Register {species} in {name} first.");
            entry.behaviour = config;
            bySpecies = null;
        }
        private void OnEnable() => bySpecies = null;
        private void OnValidate() => bySpecies = null;
    }
}

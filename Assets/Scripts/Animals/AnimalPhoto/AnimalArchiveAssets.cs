using System;
using System.Collections.Generic;
using AnimalGame.Save;
using UnityEngine;

namespace AnimalGame.Animals
{
    // Build-time references keep assets outside Resources available in a player build.
    public sealed class AnimalArchiveAssets : ScriptableObject
    {
        [Serializable] public sealed class ImageReference { public string id; public Texture2D image; }
        public GameSave defaultSave;
        public PhotoLibrary photoLibrary;
        public AnimalInfoLibrary animalInfo;
        public List<AnimalSpeciesConfig> speciesConfigs = new();
        public List<ImageReference> images = new();
        public static AnimalArchiveAssets Active { get; private set; }
        private readonly Dictionary<string, Texture2D> imageById = new();
        private readonly Dictionary<Texture2D, string> idByImage = new();
        private readonly Dictionary<AnimalSpecies, AnimalSpeciesConfig> configBySpecies = new();
        public IReadOnlyDictionary<AnimalSpecies, AnimalSpeciesConfig> SpeciesConfigs => configBySpecies;
        public IReadOnlyDictionary<AnimalSpecies, AnimalPhotoLibrary> Photos => photoLibrary.BySpecies;
        public IReadOnlyDictionary<AnimalSpecies, AnimalInfo> Info => animalInfo.BySpecies;
        public string SourceId(Texture2D source) => idByImage[source];
        public Texture2D Source(string id) => imageById[id];

        public static void Initialize()
        {
            Active = Resources.Load<AnimalArchiveAssets>("UI/AnimalCatalog/AnimalArchiveAssets");
            if (Active == null) throw new InvalidOperationException("AnimalArchiveAssets resource is missing.");
            Active.imageById.Clear(); Active.idByImage.Clear(); Active.configBySpecies.Clear();
            foreach (var image in Active.images) { Active.imageById.Add(image.id, image.image); Active.idByImage.Add(image.image, image.id); }
            foreach (var config in Active.speciesConfigs) Active.configBySpecies.Add(config.Species, config);
            Active.photoLibrary.BuildIndex(); Active.animalInfo.BuildIndex();
        }
    }
}

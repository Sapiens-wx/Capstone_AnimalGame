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
        public AnimalInfoLibrary animalInfo;
        public List<ImageReference> images = new();
        public static AnimalArchiveAssets Active { get; private set; }
        private readonly Dictionary<string, Texture2D> imageById = new();
        private readonly Dictionary<Texture2D, string> idByImage = new();
        public IReadOnlyDictionary<AnimalSpecies, AnimalSpeciesConfig> SpeciesConfigs => animalInfo.Behaviours;
        public IReadOnlyDictionary<AnimalSpecies, AnimalPhotoLibrary> Photos => animalInfo.Photos;
        public IReadOnlyDictionary<AnimalSpecies, AnimalInfo> Info => animalInfo.Info;
        public string SourceId(Texture2D source) => idByImage[source];
        public Texture2D Source(string id) => imageById[id];

        public static void Initialize()
        {
            Active = Resources.Load<AnimalArchiveAssets>("UI/AnimalCatalog/AnimalArchiveAssets");
            if (Active == null) throw new InvalidOperationException("AnimalArchiveAssets resource is missing.");
            Active.imageById.Clear(); Active.idByImage.Clear();
            foreach (var image in Active.images) { Active.imageById.Add(image.id, image.image); Active.idByImage.Add(image.image, image.id); }
            Active.animalInfo.BuildIndex();
        }
    }
}

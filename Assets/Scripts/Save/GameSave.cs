using System;
using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.Animals.Communication;

namespace AnimalGame.Save
{
    [Serializable]
    public sealed class GameSave
    {
        public static readonly DateTime InitialGameTime = new DateTime(3164, 8, 4);
        public DateTime gameTime = InitialGameTime;
        public List<AnimalPhotoRecord> photos = new();
        public List<SpeciesProgress> speciesProgress = new();
        public List<ContactConversation> conversations = new();
        public List<string> syncedStationIds = new();
        public List<string> discoveredRuinIds = new();
        public List<string> completedRepairTutorialIds = new();
        public bool playedFirstTowerRavenFlyover;
        public List<string> viewedStationCommunicationsIds = new();
        public SpeciesProgress Progress(AnimalSpecies species) => speciesProgress.Find(p => p.species == species);
    }

    [Serializable]
    public sealed class SpeciesProgress
    {
        public AnimalSpecies species;
        public int recognition;
        public int sentPhotos;
        public int Tier => recognition <= 0 ? 0 : recognition >= 100 ? 3 : recognition >= 30 ? 2 : 1;
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(fileName = "PhotoLibrary", menuName = "Animal Game/Photos/Photo Library")]
    public sealed class PhotoLibrary : ScriptableObject
    {
        [SerializeField] private List<AnimalPhotoLibrary> animals = new List<AnimalPhotoLibrary>();
        public IReadOnlyList<AnimalPhotoLibrary> Animals => animals;

        public bool TryChoosePhoto(AnimalSpecies species, AnimalState state, out AnimalPhoto photo)
        {
            photo = null;
            AnimalPhotoEntry selected = null;
            int count = 0;
            if (animals == null) return false;
            // Reservoir sampling also handles multiple groups for the same state uniformly.
            foreach (AnimalPhotoLibrary animal in animals)
            {
                if (animal == null || animal.Species != species || animal.States == null) continue;
                foreach (AnimalStatePhotos group in animal.States)
                {
                    if (group == null || group.State != state || group.Photos == null) continue;
                    foreach (AnimalPhotoEntry entry in group.Photos)
                    {
                        if (entry == null || entry.Image == null) continue;
                        if (Random.Range(0, ++count) == 0) selected = entry;
                    }
                }
            }
            if (selected == null) return false;
            photo = new AnimalPhoto(selected.Image,
                AnimalPhotoProcessing.RandomCrop(selected.SubjectRect), selected.Saturation);
            return true;
        }
    }
}

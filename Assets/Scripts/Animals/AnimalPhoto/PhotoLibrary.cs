using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(fileName = "PhotoLibrary", menuName = "Animal Game/Photos/Photo Library")]
    public sealed class PhotoLibrary : ScriptableObject
    {
        // The effective window is capped at the number of distinct images minus one.
        public const int RecentPhotoCount = 3;

        // Shared by users of this asset for one play session, including scene changes.
        private static readonly Dictionary<(PhotoLibrary, AnimalSpecies, AnimalPhotoEnvironment), ShuffleBag>
            SessionBags = new Dictionary<(PhotoLibrary, AnimalSpecies, AnimalPhotoEnvironment), ShuffleBag>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => SessionBags.Clear();

        private sealed class ShuffleBag
        {
            public readonly List<Texture2D> Pool = new List<Texture2D>();
            public readonly List<Texture2D> Remaining = new List<Texture2D>();
            public readonly List<Texture2D> Recent = new List<Texture2D>();
        }

        [SerializeField] private List<AnimalPhotoLibrary> animals = new List<AnimalPhotoLibrary>();
        public IReadOnlyList<AnimalPhotoLibrary> Animals => animals;

        /// <summary>Select and consume a photo once its random crop has been created successfully.</summary>
        public bool TryChoosePhoto(AnimalSpecies species, AnimalPhotoEnvironment environment, out AnimalPhoto photo)
        {
            photo = null;
            var candidates = new Dictionary<Texture2D, AnimalPhotoEntry>();
            var images = new List<Texture2D>();
            if (animals == null) return false;
            // A source image is one photo, even if authored in several matching groups.
            foreach (AnimalPhotoLibrary animal in animals)
            {
                if (animal == null || animal.Species != species || animal.States == null) continue;
                foreach (AnimalStatePhotos group in animal.States)
                {
                    if (group == null || group.State != environment || group.Photos == null) continue;
                    foreach (AnimalPhotoEntry entry in group.Photos)
                    {
                        if (entry == null || entry.Image == null) continue;
                        if (!AnimalPhotoProcessing.CanSquareCrop(entry.SubjectRect, entry.Image.width, entry.Image.height)) continue;
                        if (candidates.ContainsKey(entry.Image)) continue;
                        candidates.Add(entry.Image, entry);
                        images.Add(entry.Image);
                    }
                }
            }
            if (images.Count == 0) return false;
            var key = (this, species, environment);
            if (!SessionBags.TryGetValue(key, out ShuffleBag bag))
            {
                bag = new ShuffleBag();
                SessionBags.Add(key, bag);
            }
            bool changed = bag.Pool.Count != images.Count;
            for (int i = 0; !changed && i < images.Count; i++)
                changed = bag.Pool[i] != images[i];
            if (changed)
            {
                bag.Pool.Clear();
                bag.Pool.AddRange(images);
                bag.Remaining.Clear();
                bag.Recent.RemoveAll(image => !candidates.ContainsKey(image));
            }
            int recentLimit = Mathf.Min(RecentPhotoCount, images.Count - 1);
            while (bag.Recent.Count > recentLimit) bag.Recent.RemoveAt(0);
            if (bag.Remaining.Count == 0)
            {
                bag.Remaining.AddRange(bag.Pool);
                for (int i = bag.Remaining.Count - 1; i > 0; i--)
                {
                    int other = Random.Range(0, i + 1);
                    (bag.Remaining[i], bag.Remaining[other]) = (bag.Remaining[other], bag.Remaining[i]);
                }
            }
            // Skip recent images without discarding them from this round.
            Texture2D source = bag.Remaining.Find(image => !bag.Recent.Contains(image));
            if (source == null) return false;
            AnimalPhotoEntry selected = candidates[source];
            if (!AnimalPhotoProcessing.TryRandomSquareCrop(selected.SubjectRect,
                selected.Image.width, selected.Image.height, out Rect crop)) return false;
            photo = new AnimalPhoto(selected.Image, crop, selected.Saturation);
            bag.Remaining.Remove(source);
            bag.Recent.Remove(source);
            bag.Recent.Add(source);
            while (bag.Recent.Count > recentLimit) bag.Recent.RemoveAt(0);
            return true;
        }
    }
}

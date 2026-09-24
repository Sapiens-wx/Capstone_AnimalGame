using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    [Serializable]
    public sealed class AnimalPhotoEntry
    {
        [SerializeField] private Texture2D image;
        [Tooltip("Required subject area. Normalized coordinates, origin at the bottom left.")]
        [SerializeField] private Rect subjectRect = new Rect(0, 0, 1, 1);
        [SerializeField, Range(0, 2)] private float saturation = 1;

        public Texture2D Image => image;
        public Rect SubjectRect => AnimalPhotoProcessing.ClampSubjectRect(subjectRect);
        public float Saturation => Mathf.Clamp(saturation, 0, 2);
    }

    [Serializable]
    public sealed class AnimalStatePhotos
    {
        [SerializeField] private AnimalState state;
        [SerializeField] private List<AnimalPhotoEntry> photos = new List<AnimalPhotoEntry>();
        public AnimalState State => state;
        public IReadOnlyList<AnimalPhotoEntry> Photos => photos;
    }

    [CreateAssetMenu(fileName = "AnimalPhotoLibrary", menuName = "Animal Game/Photos/Animal Photo Library")]
    public sealed class AnimalPhotoLibrary : ScriptableObject
    {
        [SerializeField] private AnimalSpecies species;
        [SerializeField] private List<AnimalStatePhotos> states = new List<AnimalStatePhotos>();
        public AnimalSpecies Species => species;
        public IReadOnlyList<AnimalStatePhotos> States => states;
    }
}

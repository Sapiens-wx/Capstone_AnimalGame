using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    [Serializable]
    public sealed class AnimalResultPhoto
    {
        [SerializeField] private Sprite photo;
        [Tooltip("Normalized crop inside the source sprite. This lets a library image keep transparent layout padding outside the actual photograph.")]
        [SerializeField] private Rect normalizedCrop = new Rect(0f, 0f, 1f, 1f);

        public Sprite Photo => photo;
        public bool IsValid => photo != null && photo.texture != null;

        public Rect GetTextureUvRect()
        {
            if (!IsValid)
                return new Rect(0f, 0f, 1f, 1f);

            Rect spritePixels = photo.textureRect;
            float textureWidth = Mathf.Max(1f, photo.texture.width);
            float textureHeight = Mathf.Max(1f, photo.texture.height);
            Rect crop = ClampNormalizedRect(normalizedCrop);
            return new Rect(
                (spritePixels.x + spritePixels.width * crop.x)
                / textureWidth,
                (spritePixels.y + spritePixels.height * crop.y)
                / textureHeight,
                spritePixels.width * crop.width / textureWidth,
                spritePixels.height * crop.height / textureHeight);
        }

        private static Rect ClampNormalizedRect(Rect value)
        {
            float xMin = Mathf.Clamp(value.xMin, 0f, 0.9999f);
            float yMin = Mathf.Clamp(value.yMin, 0f, 0.9999f);
            float xMax = Mathf.Clamp(value.xMax, xMin + 0.0001f, 1f);
            float yMax = Mathf.Clamp(value.yMax, yMin + 0.0001f, 1f);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }

    /// <summary>
    /// Defines the photographable body and the authored result-photo library
    /// for one animal species prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Photo Subject")]
    public sealed class AnimalPhotoSubject : MonoBehaviour
    {
        private static readonly HashSet<AnimalPhotoSubject> ActiveSubjects =
            new HashSet<AnimalPhotoSubject>();

        [Header("Species Result Identity")]
        [SerializeField] private AnimalSpecies species = AnimalSpecies.Unknown;
        [SerializeField] private Color accentColor =
            new Color(212f / 255f, 184f / 255f, 84f / 255f, 1f);

        [Header("Recognition And Rewards")]
        [SerializeField, Min(0)] private int cognitionDegrees = 100;
        [SerializeField, Min(0)] private int baseReward = 10;
        [SerializeField, Min(0)] private int cognitionReward;

        [Header("Photo Detection")]
        [Tooltip("Only these authored body renderers define how much of the animal is inside the camera frame. Do not include the unknown-static field or direction indicator.")]
        [SerializeField] private SpriteRenderer[] photoBoundsRenderers =
            Array.Empty<SpriteRenderer>();

        private AnimalAgent agent;
        private SpriteRenderer[] visibilityRenderers =
            Array.Empty<SpriteRenderer>();

        public static IReadOnlyCollection<AnimalPhotoSubject> Active =>
            ActiveSubjects;
        public AnimalSpecies Species => species;
        public AnimalPhotoEnvironment PhotoEnvironment
        {
            get
            {
                if (agent == null) agent = GetComponent<AnimalAgent>();
                if (agent == null || agent.Motor == null)
                    return AnimalPhotoEnvironment.Ground;

                AnimalMotor motor = agent.Motor;
                // Tree crawling also uses aerial paths to bypass ground obstacles.
                // Perching/crawling belongs to Ground, even above a river.
                if (motor.IsPerchedMovement)
                    return AnimalPhotoEnvironment.Ground;
                if (motor.IsAerialMovement)
                    return AnimalPhotoEnvironment.Air;
                if (agent.Map != null
                    && agent.Map.TrySampleStaticWaterMapPosition(
                        motor.CurrentMapPosition, out float depthMeters)
                    && depthMeters > 0.01f)
                    return AnimalPhotoEnvironment.Water;

                return AnimalPhotoEnvironment.Ground;
            }
        }

        public AnimalState PhotoState
        {
            get
            {
                if (agent == null) agent = GetComponent<AnimalAgent>();
                return agent != null ? agent.Snapshot.PhotoState : AnimalState.Daily;
            }
        }
        public Color AccentColor => accentColor;
        public int CognitionDegrees => Mathf.Max(0, cognitionDegrees);
        public int BaseReward => Mathf.Max(0, baseReward);
        public int CognitionReward => Mathf.Max(0, cognitionReward);
        // Shared by scan markers so neither the uncertainty field nor the
        // direction indicator can count as seeing the animal's actual body.
        public IReadOnlyList<SpriteRenderer> BodyRenderers => photoBoundsRenderers;

        private void Awake()
        {
            CacheRuntimeReferences();
        }

        private void OnEnable()
        {
            CacheRuntimeReferences();
            ActiveSubjects.Add(this);
        }

        private void OnDisable()
        {
            ActiveSubjects.Remove(this);
        }

        private void OnDestroy()
        {
            ActiveSubjects.Remove(this);
        }

        public bool IsPhotographable()
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
                return false;

            if (agent == null)
                agent = GetComponent<AnimalAgent>();
            if (agent != null && !agent.IsPresent)
                return false;

            for (int index = 0; index < visibilityRenderers.Length; index++)
            {
                SpriteRenderer renderer = visibilityRenderers[index];
                if (renderer != null
                    && renderer.enabled
                    && renderer.gameObject.activeInHierarchy
                    && renderer.color.a > 0.001f)
                {
                    return true;
                }
            }

            return visibilityRenderers.Length == 0;
        }

        public bool TryGetWorldBounds(out Bounds bounds)
        {
            GetComponent<AnimalTurnDeformation>()?.SynchronizeForQuery();
            bounds = default;
            bool hasBounds = false;
            if (photoBoundsRenderers == null)
                return false;

            for (int index = 0; index < photoBoundsRenderers.Length; index++)
            {
                SpriteRenderer renderer = photoBoundsRenderers[index];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds && bounds.size.sqrMagnitude > 0.000001f;
        }

        private void CacheRuntimeReferences()
        {
            if (agent == null)
                agent = GetComponent<AnimalAgent>();
            visibilityRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        private void OnValidate()
        {
            cognitionDegrees = Mathf.Max(0, cognitionDegrees);
            baseReward = Mathf.Max(0, baseReward);
            cognitionReward = Mathf.Max(0, cognitionReward);
            if (photoBoundsRenderers == null)
                photoBoundsRenderers = Array.Empty<SpriteRenderer>();
        }
    }
}

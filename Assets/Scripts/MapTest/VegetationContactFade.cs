using AnimalGame.Rendering;
using AnimalGame.World;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Reduces every sprite on a vegetation prefab as the player or an animal
    /// approaches its authored map footprint, reaching the strongest fade at
    /// contact. Collider components are not required, so this also works for
    /// fully traversable aquatic plants.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HeightMapPlacedObject))]
    [AddComponentMenu("Animal Game/Level/Vegetation Contact Fade")]
    public sealed class VegetationContactFade : MonoBehaviour
    {
        private static readonly HashSet<VegetationContactFade> activeVegetation =
            new HashSet<VegetationContactFade>();

        [Tooltip("Edge-to-edge distance at which vegetation starts fading, measured in logical map meters.")]
        [SerializeField, Min(0.01f)] private float fadeStartDistanceMeters = 3f;

        [Tooltip("Fraction removed when an actor first reaches Fade Start Distance. A value of 0.5 leaves 50 percent alpha.")]
        [SerializeField, Range(0f, 1f)] private float fadeStartAlphaReduction = 0.5f;

        [Tooltip("Fraction removed when an actor touches the vegetation footprint. A value of 0.9 leaves 10 percent alpha.")]
        [SerializeField, Range(0f, 1f)] private float contactAlphaReduction = 0.9f;

        [Tooltip("Small extra map-space margin used so solid plants fade at the same moment traversal reaches their contact edge.")]
        [SerializeField, Min(0f)] private float contactPaddingMeters = 0.05f;

        private readonly List<WorldInteraction> nearbyActors = new();

        private HeightMapPlacedObject placedObject;
        private HeightMapObstacleFootprint solidFootprint;
        private WorldInteraction[] plantInteractions;
        private MapTestSceneController map;
        private SpriteRenderer[] spriteRenderers;
        private Color[] authoredColours;
        private float appliedAlphaReduction = -1f;

        public static IReadOnlyCollection<VegetationContactFade> Active =>
            activeVegetation;

        private void Awake()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            activeVegetation.Add(this);
            CacheReferences();
            ApplyAlphaReduction(0f, true);
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            if (map == null || !map.HasGeneratedMap)
            {
                ResolveMap();
                ApplyAlphaReduction(0f);
                return;
            }

            ApplyAlphaReduction(CalculateAlphaReduction());
        }

        private void OnDisable()
        {
            activeVegetation.Remove(this);
            if (Application.isPlaying)
                ApplyAlphaReduction(0f, true);
        }

        private void OnDestroy()
        {
            activeVegetation.Remove(this);
            PlayerUiOrganicVisibility.UnregisterRenderers(spriteRenderers);
        }

        public float GetBiologicalScanCollisionRadiusWorld()
        {
            if (placedObject == null)
                CacheReferences();

            float radiusMeters = Mathf.Max(0.05f, GetPlantContactRadiusMeters());
            return map != null && map.HasGeneratedMap
                ? map.MapMetersToWorldDistance(Vector2.right, radiusMeters)
                : radiusMeters;
        }

        public void ConfigureEditorDefaults(
            float startDistanceMeters,
            float startAlphaReduction,
            float maximumAlphaReduction,
            float paddingMeters)
        {
            fadeStartDistanceMeters = Mathf.Max(0.01f, startDistanceMeters);
            fadeStartAlphaReduction = Mathf.Clamp01(startAlphaReduction);
            contactAlphaReduction = Mathf.Clamp(
                maximumAlphaReduction,
                fadeStartAlphaReduction,
                1f);
            contactPaddingMeters = Mathf.Max(0f, paddingMeters);
        }

        private void CacheReferences()
        {
            placedObject = GetComponent<HeightMapPlacedObject>();
            solidFootprint = GetComponentInChildren<HeightMapObstacleFootprint>(
                true);
            plantInteractions = GetComponentsInChildren<WorldInteraction>(true);
            ResolveMap();

            spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            PlayerUiOrganicVisibility.RegisterRenderers(spriteRenderers);
            authoredColours = new Color[spriteRenderers.Length];
            for (int index = 0; index < spriteRenderers.Length; index++)
            {
                SpriteRenderer renderer = spriteRenderers[index];
                authoredColours[index] = renderer != null
                    ? renderer.color
                    : Color.white;
            }
        }

        private void ResolveMap()
        {
            if (placedObject == null)
                placedObject = GetComponent<HeightMapPlacedObject>();
            map = placedObject != null ? placedObject.Map : null;
            if (map == null)
                map = FindObjectOfType<MapTestSceneController>();
        }

        private float CalculateAlphaReduction()
        {
            Vector2 plantPosition = InteractionShape.ToQuery(transform.position, map);
            float contactRadius = GetPlantContactRadiusMeters() + contactPaddingMeters;
            InteractionShape contactShape = InteractionShape.Circle(plantPosition, contactRadius);

            // Actor body radii are already included by the shape overlap query.
            WorldInteractionQuery.Query(
                InteractionShape.Circle(plantPosition, contactRadius + fadeStartDistanceMeters),
                WorldInteractionKind.Actor,
                map,
                gameObject.scene,
                nearbyActors);

            float greatestReduction = 0f;
            foreach (WorldInteraction actor in nearbyActors)
            {
                greatestReduction = Mathf.Max(
                    greatestReduction,
                    CalculateActorAlphaReduction(contactShape, actor.GetShape(map)));
            }

            return greatestReduction;
        }

        private float GetPlantContactRadiusMeters()
        {
            float radius = placedObject != null
                ? placedObject.FootprintRadiusMeters
                : 0f;
            if (solidFootprint != null
                && solidFootprint.isActiveAndEnabled
                && solidFootprint.BlocksTraversal)
            {
                radius = Mathf.Max(radius, solidFootprint.RadiusMeters);
            }

            // Traversable bushes keep fading around their actual virtual surface,
            // rather than falling back to the smaller placement-only footprint.
            if (plantInteractions != null)
                foreach (WorldInteraction interaction in plantInteractions)
                {
                    if (!ClimbableSurface.IsUsable(interaction)) continue;
                    if (map != null && map.HasGeneratedMap)
                    {
                        InteractionShape shape = interaction.GetShape(map);
                        if (!shape.IsBox)
                            radius = Mathf.Max(radius, shape.Radius
                                + Vector2.Distance(shape.Center, InteractionShape.ToQuery(transform.position, map)));
                    }
                    else if (solidFootprint != null && solidFootprint.isActiveAndEnabled)
                    {
                        // With no generated map, shape radii are world units; keep the authored map-metre fallback.
                        radius = Mathf.Max(radius, solidFootprint.RadiusMeters);
                    }
                }

            return Mathf.Max(0f, radius);
        }

        private float CalculateActorAlphaReduction(
            InteractionShape plantShape,
            InteractionShape actorShape)
        {
            float edgeDistance = Mathf.Max(
                0f,
                -WorldInteractionQuery.Penetration(plantShape, actorShape));
            if (edgeDistance > fadeStartDistanceMeters)
                return 0f;

            float approachProgress = 1f - Mathf.Clamp01(
                edgeDistance / Mathf.Max(0.01f, fadeStartDistanceMeters));
            return Mathf.Lerp(
                fadeStartAlphaReduction,
                contactAlphaReduction,
                approachProgress);
        }

        private void ApplyAlphaReduction(
            float alphaReduction,
            bool force = false)
        {
            alphaReduction = Mathf.Clamp01(alphaReduction);
            if (!force
                && Mathf.Abs(alphaReduction - appliedAlphaReduction)
                <= 0.0001f)
            {
                return;
            }
            if (spriteRenderers == null
                || authoredColours == null
                || spriteRenderers.Length != authoredColours.Length)
            {
                CacheReferences();
            }

            float alphaMultiplier = 1f - alphaReduction;
            for (int index = 0; index < spriteRenderers.Length; index++)
            {
                SpriteRenderer renderer = spriteRenderers[index];
                if (renderer == null)
                    continue;

                Color colour = authoredColours[index];
                colour.a *= alphaMultiplier;
                renderer.color = colour;
            }

            appliedAlphaReduction = alphaReduction;
        }

        private void OnValidate()
        {
            fadeStartDistanceMeters = Mathf.Max(0.01f, fadeStartDistanceMeters);
            fadeStartAlphaReduction = Mathf.Clamp01(
                fadeStartAlphaReduction);
            contactAlphaReduction = Mathf.Clamp(
                contactAlphaReduction,
                fadeStartAlphaReduction,
                1f);
            contactPaddingMeters = Mathf.Max(0f, contactPaddingMeters);
        }
    }
}

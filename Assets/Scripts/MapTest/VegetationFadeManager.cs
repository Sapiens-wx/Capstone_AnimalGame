using System.Collections;
using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.MapTest
{
    [DisallowMultipleComponent]
    public sealed class VegetationFadeManager : MonoBehaviour
    {
        private const float RefreshInterval = 2f;
        private readonly List<WorldInteraction> nearbyVegetation = new();
        private MapTestSceneController map;
        private Transform player;
        private float viewRadiusMeters;
        private float nextRefreshTime;

        private IEnumerator Start()
        {
            HeightMapPlayerSceneBootstrap bootstrap = HeightMapPlayerSceneBootstrap.inst;
            map = bootstrap.map;
            player = bootstrap.robot.transform;
            Camera camera = bootstrap.mapCamera;
            // Freeze the initial camera scale while waiting for the UI's first layout.
            float mapMetersPerPixel = camera.orthographicSize * 2f / camera.pixelHeight
                / map.MapMetersToWorldDistance(Vector2.right, 1f);
            float radiusPixels;
            while ((radiusPixels = bootstrap.scanChargeUi.GetUiRingScreenRadiusPixels()) <= 0f)
                yield return null;

            viewRadiusMeters = radiusPixels * mapMetersPerPixel * 1.5f;
            RefreshVegetation();
        }

        private void Update()
        {
            if (viewRadiusMeters <= 0f || Time.deltaTime <= 0f)
                return;

            if (Time.time >= nextRefreshTime)
                RefreshVegetation();

            foreach (WorldInteraction interaction in nearbyVegetation)
            {
                if (interaction != null && interaction.isActiveAndEnabled && interaction is Vegetation vegetation)
                {
                    VegetationContactFade fade = vegetation.ContactFade;
                    if (fade != null && fade.isActiveAndEnabled)
                        fade.UpdateFade();
                }
            }
        }

        private void RefreshVegetation()
        {
            WorldInteractionQuery.Query(
                InteractionShape.Circle(InteractionShape.ToQuery(player.position, map), viewRadiusMeters),
                WorldInteractionKind.Vegetation,
                map,
                player.gameObject.scene,
                nearbyVegetation);
            nextRefreshTime = Time.time + RefreshInterval;
        }
    }
}

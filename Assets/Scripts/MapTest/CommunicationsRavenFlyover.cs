using System;
using System.IO;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using AnimalGame.World;
using Capstone.Audio;
using UnityEngine;
using GameHud = AnimalGame.MainUI.MainUI;

namespace AnimalGame.MapTest
{
    [DefaultExecutionOrder(330)]
    [DisallowMultipleComponent]
    internal sealed class CommunicationsRavenFlyover : MonoBehaviour
    {
        private const float FlightSeconds = 3.8f;
        private const float FirstCallAt = 1.25f;
        private const float SecondCallAt = 2.6f;
        private const float SpanInViewRadii = .13f;
        // The reference route starts below the right side of the disc and bends toward the upper right.
        private static readonly Vector2 startOffset = new(.40f, -1.12f);
        private static readonly Vector2 entryHandle = new(.42f, -.49f);
        private static readonly Vector2 exitHandle = new(.58f, .25f);
        private static readonly Vector2 endOffset = new(.70f, 1.10f);

        private readonly RavenFlyoverAudio ravenAudio = new();
        private CommunicationsStation station;
        private RobotMover player;
        private Camera mapCamera;
        private GameHud hud;
        private SpriteRenderer unknownAppearance;
        private CommunicationsRavenVisual visual;
        private Vector3 start, entry, exit, end;
        private float wingspan;
        private float elapsed;

        internal void Bind(WeatherTower tower, RobotMover robot, Camera camera, GameHud gameHud, SpriteRenderer appearance)
        {
            station = tower.GetComponentInChildren<CommunicationsStation>(true);
            unknownAppearance = appearance;
            player = robot;
            mapCamera = camera;
            hud = gameHud;
            enabled = false;
            CommunicationsStation.InteractionCompleted += OnInteractionCompleted;
            ravenAudio.Preload();
        }

        private void OnInteractionCompleted(CommunicationsStation source, RobotMover requestor)
        {
            if (source != station || requestor != player) return;
            CommunicationsStation.InteractionCompleted -= OnInteractionCompleted;
            enabled = true;
        }

        private void LateUpdate()
        {
            if (visual != null)
            {
                if (Time.timeScale > 0f) AdvanceFlight(Time.deltaTime);
                return;
            }

            if (!hud.CanPresentRuinName || HeightMapPlayerSceneBootstrap.StationPlugOwnsInput
                || !hud.TryGetRingScreenGeometry(out Vector2 center, out float radius))
                return;

            try
            {
                if (!SaveSystem.TryMarkFirstTowerRavenFlyoverPlayed())
                {
                    ravenAudio.Dispose();
                    enabled = false;
                    return;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogException(error, this);
            }

            // Project once with the settled roof camera. From here the bird follows a world-space path.
            float depth = mapCamera.WorldToScreenPoint(player.transform.position).z;
            start = OnMapPlane(startOffset);
            entry = OnMapPlane(entryHandle);
            exit = OnMapPlane(exitHandle);
            end = OnMapPlane(endOffset);
            wingspan = Vector3.Distance(OnMapPlane(Vector2.zero), OnMapPlane(Vector2.right)) * SpanInViewRadii;
            visual = new CommunicationsRavenVisual(transform, unknownAppearance);
            AdvanceFlight(0f);

            Vector3 OnMapPlane(Vector2 offset)
            {
                Vector2 screen = center + offset * radius;
                return mapCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
            }
        }

        private void AdvanceFlight(float deltaTime)
        {
            float previous = elapsed;
            elapsed = Mathf.Min(elapsed + deltaTime, FlightSeconds);
            float progress = elapsed / FlightSeconds;
            Vector3 middle = Vector3.Lerp(entry, exit, progress);
            Vector3 approach = Vector3.Lerp(Vector3.Lerp(start, entry, progress), middle, progress);
            Vector3 departure = Vector3.Lerp(middle, Vector3.Lerp(exit, end, progress), progress);
            Vector3 position = Vector3.Lerp(approach, departure, progress);
            visual.Pose(position, (Vector2)(departure - approach), wingspan, elapsed);

            if (previous < FirstCallAt && elapsed >= FirstCallAt)
                ravenAudio.PlayCall(position, secondCall: false);
            if (previous < SecondCallAt && elapsed >= SecondCallAt)
                ravenAudio.PlayCall(position, secondCall: true);
            ravenAudio.Tick(position);
            if (elapsed < FlightSeconds) return;

            visual.Dispose();
            visual = null;
            ravenAudio.Dispose();
            enabled = false;
        }

        private void OnDestroy()
        {
            CommunicationsStation.InteractionCompleted -= OnInteractionCompleted;
            visual?.Dispose();
            ravenAudio.Dispose();
        }
    }
}

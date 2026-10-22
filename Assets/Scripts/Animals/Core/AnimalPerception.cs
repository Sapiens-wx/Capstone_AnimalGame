using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Perception")]
    public sealed class AnimalPerception : MonoBehaviour
    {
        public RobotMover Player { get; private set; }

        private MapTestSceneController map;
        private AnimalSpeciesConfig config;
        private AnimalMotor motor;
        private float detectionCountdown;
        private bool playerWasInsideAlertRange;
        private AnimalAgent agent;
        private readonly List<WorldInteraction> neighbours = new();
        private readonly List<float> angles = new();
        private float predatorCountdown;
        public Transform PrimaryThreat { get; private set; }
        public bool HasThreatInRange => TryGetThreatProximity(out _);

        public void Initialize(
            MapTestSceneController mapController,
            AnimalSpeciesConfig speciesConfig)
        {
            map = mapController;
            config = speciesConfig;
            motor = GetComponent<AnimalMotor>();
            agent = GetComponent<AnimalAgent>();
            Player = agent != null ? agent.Simulation.World.Player : null;
            predatorCountdown = Random.value * config.DetectionIntervalSeconds;
            detectionCountdown = config != null
                ? config.DetectionIntervalSeconds
                : 0.5f;
            playerWasInsideAlertRange = false;
        }

        public void ResetDetection()
        {
            detectionCountdown = config != null
                ? config.DetectionIntervalSeconds
                : 0.5f;
            playerWasInsideAlertRange = false;
        }

        public bool TickDetection(float deltaTime)
        {
            if (Player == null && agent != null) Player = agent.Simulation.World.Player;
            bool found = TickPlayerDetection(deltaTime);
            if (found && Player != null) Remember(Player.transform);
            predatorCountdown -= deltaTime;
            if (predatorCountdown <= 0f && agent != null && agent.IsInitialized)
            {
                predatorCountdown = config.DetectionIntervalSeconds;
                WorldInteractionQuery.Query(InteractionShape.Circle(motor.CurrentMapPosition, config.AlertRadiusMeters),
                    WorldInteractionKind.Collision, map, gameObject.scene, neighbours, ignore: transform);
                foreach (WorldInteraction item in neighbours)
                {
                    AnimalAgent predator = item.GetComponent<AnimalAgent>();
                    if (predator == null || !predator.IsPresent || !predator.Config.Eats(config)) continue;
                    float proximity = 1f - Mathf.Clamp01(Vector2.Distance(motor.CurrentMapPosition, predator.Motor.CurrentMapPosition) / config.AlertRadiusMeters);
                    float speed = Mathf.Clamp01(predator.Motor.CurrentSpeedMetersPerSecond / config.PlayerSpeedForMaximumBonus);
                    float chance = config.BaseDetectionChancePerCheck * Mathf.Lerp(1f, config.NearestDetectionMultiplier, proximity)
                        * Mathf.Lerp(1f, config.MaximumPlayerSpeedDetectionMultiplier, speed)
                        * (HasDirectSight(predator.transform) ? config.DirectLineOfSightDetectionMultiplier : 1f);
                    if (Random.value < chance) { Remember(predator.transform); found = true; }
                }
            }
            TryGetThreatProximity(out _);
            return found;
        }

        private bool TickPlayerDetection(float deltaTime)
        {
            if (!TryGetPlayerProximity(out float proximity, out _))
            {
                playerWasInsideAlertRange = false;
                return false;
            }

            if (!playerWasInsideAlertRange)
            {
                playerWasInsideAlertRange = true;
                detectionCountdown = config.DetectionIntervalSeconds;
                return false;
            }

            detectionCountdown -= deltaTime;
            if (detectionCountdown > 0f)
                return false;

            detectionCountdown += config.DetectionIntervalSeconds;
            float playerSpeedProgress = Mathf.Clamp01(
                Mathf.Abs(Player.CurrentSpeed)
                / config.PlayerSpeedForMaximumBonus);
            float distanceMultiplier = Mathf.Lerp(
                1f,
                config.NearestDetectionMultiplier,
                proximity);
            float speedMultiplier = Mathf.Lerp(
                1f,
                config.MaximumPlayerSpeedDetectionMultiplier,
                playerSpeedProgress);
            float directSightMultiplier = HasDirectSightOfPlayer()
                ? config.DirectLineOfSightDetectionMultiplier
                : 1f;
            float detectionChance = Mathf.Clamp01(
                config.BaseDetectionChancePerCheck
                * distanceMultiplier
                * speedMultiplier
                * directSightMultiplier);

            // Hearing is part of detection, so this deliberately has no
            // forward-view-cone requirement. Direct sight is only a bonus;
            // cover never removes the hearing-based chance. A successful roll
            // means the animal noticed the player and immediately gets Curious.
            return Random.value < detectionChance;
        }

        private bool HasDirectSightOfPlayer() => Player != null && HasDirectSight(Player.transform);

        private bool HasDirectSight(Transform target)
        {
            if (map == null || target == null
                || !map.TrySampleWorldPosition(
                    transform.position,
                    out Vector2 animalMapPosition,
                    out _)
                || !map.TrySampleWorldPosition(
                    target.position,
                    out Vector2 playerMapPosition,
                    out _))
            {
                return false;
            }

            Vector2 line = playerMapPosition - animalMapPosition;
            float lineLengthSquared = line.sqrMagnitude;
            if (lineLengthSquared <= 0.0001f)
                return true;

            float fullVisionAngle = config.DirectVisionAngleDegrees;
            if (fullVisionAngle < 359.999f)
            {
                Vector2 facingDirection = motor != null
                    ? motor.FacingMapDirection
                    : map.WorldDirectionToMapDirection(transform.up);
                if (facingDirection.sqrMagnitude <= 0.000001f
                    || Vector2.Angle(facingDirection, line)
                    > fullVisionAngle * 0.5f)
                {
                    return false;
                }
            }

            foreach (HeightMapObstacleFootprint obstacle
                     in HeightMapObstacleFootprint.ActiveFootprints)
            {
                if (obstacle == null
                    || obstacle.gameObject.scene != gameObject.scene
                    || !obstacle.BlocksSight
                    || obstacle.RadiusMeters <= 0f
                    || !map.TrySampleWorldPosition(
                        obstacle.transform.position,
                        out Vector2 obstacleMapPosition,
                        out _))
                {
                    continue;
                }

                float progress = Mathf.Clamp01(
                    Vector2.Dot(
                        obstacleMapPosition - animalMapPosition,
                        line)
                    / lineLengthSquared);
                Vector2 closestPoint = animalMapPosition + line * progress;
                float radius = obstacle.RadiusMeters;
                if ((obstacleMapPosition - closestPoint).sqrMagnitude
                    < radius * radius)
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryGetPlayerProximity(
            out float proximity,
            out float distanceMeters)
        {
            proximity = 0f;
            distanceMeters = float.PositiveInfinity;
            if (config == null || map == null)
                return false;

            if (Player == null && agent != null)
                Player = agent.Simulation.World.Player;
            if (Player == null
                || !map.TrySampleWorldPosition(
                    transform.position,
                    out Vector2 animalMapPosition,
                    out _)
                || !map.TrySampleWorldPosition(
                    Player.transform.position,
                    out Vector2 playerMapPosition,
                    out _))
            {
                return false;
            }

            distanceMeters = Vector2.Distance(
                animalMapPosition,
                playerMapPosition);
            if (distanceMeters > config.AlertRadiusMeters)
                return false;

            proximity = 1f - Mathf.Clamp01(
                distanceMeters / config.AlertRadiusMeters);
            return true;
        }

        public bool TryGetPlayerMapPosition(out Vector2 playerMapPosition)
        {
            playerMapPosition = Vector2.zero;
            if (map == null)
                return false;

            if (Player == null && agent != null)
                Player = agent.Simulation.World.Player;
            return Player != null
                   && map.TrySampleWorldPosition(
                       Player.transform.position,
                       out playerMapPosition,
                       out _);
        }

        public void Remember(Transform threat)
        {
            if (threat == null || agent?.Runtime == null || threat == transform) return;
            if (!agent.Runtime.Threats.Contains(threat)) agent.Runtime.Threats.Add(threat);
            PrimaryThreat = threat;
        }

        public bool TryGetThreatProximity(out float proximity)
        {
            proximity = 0f;
            if (agent?.Runtime == null || map == null) return false;
            float nearest = float.PositiveInfinity;
            PrimaryThreat = null;
            List<Transform> threats = agent.Runtime.Threats;
            for (int i = threats.Count - 1; i >= 0; i--)
            {
                Transform threat = threats[i];
                if (threat == null || !threat.gameObject.activeInHierarchy || threat.gameObject.scene != gameObject.scene
                    || !map.TrySampleWorldPosition(threat.position, out Vector2 position, out _))
                { threats.RemoveAt(i); continue; }
                AnimalAgent animal = threat.GetComponent<AnimalAgent>();
                float distance = Vector2.Distance(motor.CurrentMapPosition, position);
                if ((animal != null && animal.Health != null && animal.Health.IsDead) || distance > config.AlertRadiusMeters)
                { threats.RemoveAt(i); continue; }
                if (distance < nearest) { nearest = distance; PrimaryThreat = threat; }
            }
            if (PrimaryThreat == null) return false;
            proximity = 1f - Mathf.Clamp01(nearest / config.AlertRadiusMeters);
            return true;
        }

        public Vector2 EscapeDirection()
        {
            angles.Clear();
            TryGetThreatProximity(out _);
            if (agent?.Runtime != null)
                foreach (Transform threat in agent.Runtime.Threats)
                {
                    Vector2 direction = InteractionShape.ToQuery(threat.position, map) - motor.CurrentMapPosition;
                    if (direction.sqrMagnitude > .000001f) angles.Add(Mathf.Repeat(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, 360f));
                }
            if (angles.Count == 0) return motor.FacingMapDirection;
            angles.Sort();
            float largest = -1f, chosen = 0f;
            int ties = 0;
            for (int i = 0; i < angles.Count; i++)
            {
                float gap = (i + 1 < angles.Count ? angles[i + 1] : angles[0] + 360f) - angles[i];
                if (gap > largest + .001f) { largest = gap; chosen = angles[i] + gap * .5f; ties = 1; }
                else if (Mathf.Abs(gap - largest) <= .001f && Random.Range(0, ++ties) == 0) chosen = angles[i] + gap * .5f;
            }
            return new Vector2(Mathf.Cos(chosen * Mathf.Deg2Rad), Mathf.Sin(chosen * Mathf.Deg2Rad));
        }
    }
}

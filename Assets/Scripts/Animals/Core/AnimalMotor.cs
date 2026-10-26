using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Motor")]
    public sealed class AnimalMotor : MonoBehaviour
    {
        private static readonly float[] avoidanceAngles =
        {
            0f,
            30f,
            -30f,
            60f,
            -60f,
            90f,
            -90f,
            120f,
            -120f
        };

        public bool HasTarget { get; private set; }
        public bool HasArrived { get; private set; }
        public float CurrentSpeedMetersPerSecond { get; private set; }
        public Vector2 TargetMapPosition { get; private set; }
        public Vector2 FacingMapDirection { get; private set; } = Vector2.up;
        public bool IsAerialMovement { get; private set; }
        public bool IsPerchedMovement { get; private set; }
        private List<Vector2> aerialPath;
        private int pathIndex;
        public void SetPerchedMode(bool perched) { IsPerchedMovement = perched; }
        public void SetMovementSpeed(float speed) { targetSpeedMetersPerSecond = Mathf.Max(0f, speed); }
        public Vector2 CurrentMapPosition
        {
            get
            {
                if (map != null
                    && map.TrySampleWorldPosition(
                        transform.position,
                        out Vector2 mapPosition,
                        out _))
                {
                    return mapPosition;
                }

                return Vector2.zero;
            }
        }

        private MapTestSceneController map;
        private AnimalSpeciesConfig config;
        private float targetSpeedMetersPerSecond;
        private Vector2 desiredFacingMapDirection = Vector2.up;
        private Vector2 impactVelocity;
        private float blockedTime;
        private bool wasDisplaced;
        private bool facingLocked;
        // Optional constraints apply to the actual avoidance step as well as planned segments.
        public System.Func<Vector2, Vector2, bool> SegmentFilter { get; set; }
        public bool IsFacingLocked => facingLocked;
        public float? FacingTurnSpeedDegreesPerSecond { get; set; }
        public void LockFacing(Vector2 direction) { facingLocked = true; FaceMapDirection(direction); }
        public void ClearMovementOverrides() { facingLocked = false; SegmentFilter = null; FacingTurnSpeedDegreesPerSecond = null; IsPerchedMovement = false; }
        public AnimalMovementResult Result { get; private set; }
        public bool IsDisplaced => impactVelocity.sqrMagnitude > .001f || wasDisplaced;
        public void Knockback(Vector2 direction, float distance)
        {
            if (direction.sqrMagnitude < .000001f || distance <= 0f) return;
            impactVelocity += direction.normalized * distance * 5f;
            wasDisplaced = true;
        }

        public void Initialize(
            MapTestSceneController mapController,
            AnimalSpeciesConfig speciesConfig)
        {
            map = mapController;
            config = speciesConfig;
            FacingMapDirection = map != null
                ? map.WorldDirectionToMapDirection(transform.up)
                : Vector2.up;
            if (FacingMapDirection.sqrMagnitude < 0.000001f)
                FacingMapDirection = Vector2.up;
            desiredFacingMapDirection = FacingMapDirection;
            Stop();
        }

        public bool SetTarget(
            Vector2 mapPositionMeters,
            float speedMetersPerSecond)
        {
            return SetTargetInternal(
                mapPositionMeters,
                speedMetersPerSecond,
                false);
        }

        /// <summary>
        /// Starts an unobstructed straight-line flight to a playable map
        /// position. Unlike normal ground movement, this does not steer around
        /// or collide with placed-object footprints. Map bounds are still
        /// sampled on every step.
        /// </summary>
        public bool SetAerialTarget(
            Vector2 mapPositionMeters,
            float speedMetersPerSecond)
        {
            return SetTargetInternal(
                mapPositionMeters,
                speedMetersPerSecond,
                true);
        }

        // A distance budget may cross several samples in one frame. Never skip a
        // collision check, or consume path distance while the motor is blocked.
        public bool SetAerialPath(IReadOnlyList<Vector2> points, float speed)
        {
            if (points == null || points.Count == 0) return false;
            Vector2 previous = CurrentMapPosition;
            foreach (Vector2 point in points)
            {
                if (!CanFlySegment(previous, point)) return false;
                previous = point;
            }
            if (!SetAerialTarget(points[points.Count - 1], speed)) return false;
            aerialPath = new List<Vector2>(points);
            pathIndex = 0;
            return true;
        }

        public bool CanFlySegment(Vector2 start, Vector2 end)
        {
            if (map == null || config == null || !map.TrySampleMapPosition(start, out _)
                || !map.TrySampleMapPosition(end, out _)) return false;
            float radius = config.BodyRadiusMeters;
            if (!map.TrySampleMapPosition(end + Vector2.right * radius, out _)
                || !map.TrySampleMapPosition(end - Vector2.right * radius, out _)
                || !map.TrySampleMapPosition(end + Vector2.up * radius, out _)
                || !map.TrySampleMapPosition(end - Vector2.up * radius, out _)) return false;
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / .1f));
            Vector2 previous = start;
            for (int i = 1; i <= count; i++)
            {
                Vector2 next = Vector2.Lerp(start, end, i / (float)count);
                if (!map.TrySampleMapPosition(next, out _)
                    || (SegmentFilter != null && !SegmentFilter(previous, next))) return false;
                previous = next;
            }
            return !WorldInteractionQuery.Query(InteractionShape.Capsule(start, end, config.BodyRadiusMeters),
                WorldInteractionKind.Actor, map, gameObject.scene, ignore: transform,
                previous: InteractionShape.Circle(start, config.BodyRadiusMeters));
        }

        private bool SetTargetInternal(
            Vector2 mapPositionMeters,
            float speedMetersPerSecond,
            bool aerialMovement)
        {
            if (map == null || !map.TrySampleMapPosition(mapPositionMeters, out _))
                return false;

            TargetMapPosition = mapPositionMeters;
            aerialPath = null;
            targetSpeedMetersPerSecond = Mathf.Max(0f, speedMetersPerSecond);
            IsAerialMovement = aerialMovement;
            if (!HasTarget || HasArrived) { Result = AnimalMovementResult.Moving; blockedTime = 0f; }
            HasTarget = true;
            HasArrived = false;
            return true;
        }

        public void Stop()
        {
            aerialPath = null;
            HasTarget = false;
            HasArrived = false;
            CurrentSpeedMetersPerSecond = 0f;
            IsAerialMovement = false;
            Result = AnimalMovementResult.Stopped;
            blockedTime = 0f;
        }

        public void FaceMapPosition(Vector2 mapPositionMeters)
        {
            Vector2 direction = mapPositionMeters - CurrentMapPosition;
            FaceMapDirection(direction);
        }

        public void FaceMapDirection(Vector2 mapDirection)
        {
            if (mapDirection.sqrMagnitude <= 0.000001f)
                return;

            desiredFacingMapDirection = mapDirection.normalized;
        }

        public void Tick(float deltaTime)
        {
            if (map == null || config == null || deltaTime <= 0f)
                return;

            Vector2 currentMapPosition = CurrentMapPosition;
            if (TickDisplacement(deltaTime)) return;
            if (HasTarget && aerialPath != null)
            {
                TickAerialPath(deltaTime);
                RotateTowardsDesiredFacing(deltaTime);
                return;
            }
            if (HasTarget)
            {
                Vector2 toTarget = TargetMapPosition - currentMapPosition;
                float distance = toTarget.magnitude;
                if (distance <= config.ArrivalDistanceMeters)
                {
                    HasTarget = false;
                    HasArrived = true;
                    CurrentSpeedMetersPerSecond = 0f;
                    IsAerialMovement = false;
                    Result = AnimalMovementResult.Reached;
                }
                else
                {
                    float step = Mathf.Min(
                        distance,
                        targetSpeedMetersPerSecond * deltaTime);
                    if (step > 0f
                        && TryChooseMovementDirectionForCurrentMode(
                            currentMapPosition,
                            toTarget / distance,
                            step,
                            out Vector2 movementDirection))
                    {
                        if (!facingLocked) desiredFacingMapDirection = movementDirection;
                        Vector2 nextMapPosition = currentMapPosition
                                                  + movementDirection * step;
                        ApplyMapPosition(nextMapPosition);
                        CurrentSpeedMetersPerSecond = step / deltaTime;
                        blockedTime = 0f;
                        Result = AnimalMovementResult.Moving;
                    }
                    else
                    {
                        CurrentSpeedMetersPerSecond = 0f;
                        blockedTime += deltaTime;
                        Result = blockedTime > 2f ? AnimalMovementResult.NoProgress : AnimalMovementResult.Blocked;
                    }
                }
            }
            else
            {
                CurrentSpeedMetersPerSecond = 0f;
            }

            RotateTowardsDesiredFacing(deltaTime);
        }

        public bool TeleportToMapPosition(Vector2 mapPositionMeters)
        {
            if (map == null || !map.TrySampleMapPosition(mapPositionMeters, out _))
                return false;

            ApplyMapPosition(mapPositionMeters);
            aerialPath = null;
            HasTarget = false;
            HasArrived = true;
            CurrentSpeedMetersPerSecond = 0f;
            IsAerialMovement = false;
            GetComponent<AnimalTurnDeformation>()?.ResetDeformation();
            return true;
        }

        public bool CanOccupyMapPosition(Vector2 mapPositionMeters, bool ignoreGround = false)
        {
            if (map == null || config == null
                || !map.TrySampleMapPosition(mapPositionMeters, out _))
            {
                return false;
            }

            if (!ignoreGround && !CanWalkAt(mapPositionMeters)) return false;
            return !WorldInteractionQuery.Query(
                InteractionShape.Capsule(mapPositionMeters, mapPositionMeters, config.BodyRadiusMeters),
                ignoreGround ? WorldInteractionKind.Actor : WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision,
                map, gameObject.scene, ignore: transform);
        }

        private bool TryChooseMovementDirection(
            Vector2 currentMapPosition,
            Vector2 directDirection,
            float step,
            out Vector2 movementDirection)
        {
            for (int index = 0; index < avoidanceAngles.Length; index++)
            {
                Vector2 candidateDirection = Rotate(
                    directDirection,
                    avoidanceAngles[index]);
                Vector2 candidatePosition = currentMapPosition
                                            + candidateDirection * step;
                if (!CanTraverseSegment(currentMapPosition, candidatePosition))
                {
                    continue;
                }

                movementDirection = candidateDirection;
                return true;
            }

            movementDirection = Vector2.zero;
            return false;
        }

        private bool TryChooseMovementDirectionForCurrentMode(
            Vector2 currentMapPosition,
            Vector2 directDirection,
            float step,
            out Vector2 movementDirection)
        {
            if (!IsAerialMovement)
            {
                return TryChooseMovementDirection(
                    currentMapPosition,
                    directDirection,
                    step,
                    out movementDirection);
            }

            movementDirection = directDirection;
            return CanFlySegment(currentMapPosition, currentMapPosition + directDirection * step);
        }

        private void TickAerialPath(float dt)
        {
            float remaining = targetSpeedMetersPerSecond * dt, moved = 0f;
            while (aerialPath != null && pathIndex < aerialPath.Count)
            {
                Vector2 current = CurrentMapPosition;
                Vector2 delta = aerialPath[pathIndex] - current;
                float distance = delta.magnitude;
                if (distance < .00001f) { pathIndex++; continue; }
                if (remaining <= 0f) break;
                float step = Mathf.Min(distance, remaining);
                Vector2 next = current + delta / distance * step;
                if (!CanFlySegment(current, next))
                {
                    blockedTime += dt;
                    Result = blockedTime > 2f ? AnimalMovementResult.NoProgress : AnimalMovementResult.Blocked;
                    CurrentSpeedMetersPerSecond = moved / dt;
                    return;
                }
                if (!facingLocked) desiredFacingMapDirection = delta / distance;
                ApplyMapPosition(next);
                remaining -= step;
                moved += step;
                if (step >= distance - .00001f) pathIndex++;
            }
            blockedTime = 0f;
            CurrentSpeedMetersPerSecond = moved / dt;
            Result = AnimalMovementResult.Moving;
            if (aerialPath != null && pathIndex >= aerialPath.Count)
            {
                aerialPath = null;
                HasTarget = false;
                HasArrived = true;
                IsAerialMovement = false;
                CurrentSpeedMetersPerSecond = 0f;
                Result = AnimalMovementResult.Reached;
            }
        }

        private bool IsObstacleSweepBlocked(
            Vector2 startMapPosition,
            Vector2 endMapPosition)
        {
            return WorldInteractionQuery.Query(
                InteractionShape.Capsule(startMapPosition, endMapPosition, config.BodyRadiusMeters),
                WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision, map, gameObject.scene,
                ignore: transform, previous: InteractionShape.Capsule(startMapPosition, startMapPosition, config.BodyRadiusMeters));
        }

        private bool CanWalkAt(Vector2 position)
        {
            if (config.MaximumWalkSlopeDegrees >= 90f) return map.TrySampleMapPosition(position, out _);
            return AnimalTerrainQuery.TrySample(map, position, out var sample)
                && sample.SlopeDegrees <= config.MaximumWalkSlopeDegrees + .001f;
        }

        public bool CanTraverseSegment(Vector2 start, Vector2 end)
        {
            if (map == null || config == null || IsObstacleSweepBlocked(start, end)) return false;
            if (SegmentFilter != null && !SegmentFilter(start, end)) return false;
            Vector2 spacing = map.SampleSpacing(start);
            float stride = Mathf.Clamp(Mathf.Min(spacing.x, spacing.y) * .5f, .05f, .25f);
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / stride));
            Vector2 previous = start;
            for (int i = 0; i <= count; i++)
            {
                Vector2 position = Vector2.Lerp(start, end, i / (float)count);
                if (!CanWalkAt(position) || (i > 0 && SegmentFilter != null && !SegmentFilter(previous, position))) return false;
                previous = position;
            }
            return true;
        }

        private void ApplyMapPosition(Vector2 mapPositionMeters)
        {
            Vector3 worldPosition = map.MapPositionToWorld(mapPositionMeters);
            worldPosition.z = transform.position.z;
            transform.position = worldPosition;
            WorldInteraction.MarkHierarchySpatialDirty(transform);
        }

        private void RotateTowardsDesiredFacing(float deltaTime)
        {
            if (desiredFacingMapDirection.sqrMagnitude <= 0.000001f)
                return;

            Vector2 worldDirection = map.MapDirectionToWorldDirection(
                desiredFacingMapDirection);
            if (worldDirection.sqrMagnitude <= 0.000001f)
                return;

            float targetAngle = Vector2.SignedAngle(Vector2.up, worldDirection);
            float currentAngle = transform.eulerAngles.z;
            float nextAngle = Mathf.MoveTowardsAngle(
                currentAngle,
                targetAngle,
                Mathf.Max(0f, FacingTurnSpeedDegreesPerSecond ?? config.TurnSpeedDegreesPerSecond) * deltaTime);
            transform.rotation = Quaternion.Euler(0f, 0f, nextAngle);
            WorldInteraction.MarkHierarchySpatialDirty(transform);
            FacingMapDirection = map.WorldDirectionToMapDirection(transform.up);
        }

        private bool TickDisplacement(float deltaTime)
        {
            Vector2 position = CurrentMapPosition;
            bool steep = !IsAerialMovement && !IsPerchedMovement && config.MaximumWalkSlopeDegrees < 90f
                && AnimalTerrainQuery.TrySample(map, position, out var ground)
                && ground.SlopeDegrees > config.SlideSlopeDegrees + .001f;
            if (!steep && !wasDisplaced && impactVelocity.sqrMagnitude <= .001f) return false;
            wasDisplaced = true;
            // Passive movement uses solid sweeps, but may enter dangerous slopes/water.
            if (!IsAerialMovement && !IsPerchedMovement && AnimalTerrainQuery.TrySample(map, position, out var sampledGround))
            {
                if (sampledGround.SlopeDegrees > config.SlideSlopeDegrees + .001f)
                    impactVelocity += -sampledGround.Gradient.normalized * 6f * deltaTime;
            }
            float damping = Mathf.Exp(-5f * deltaTime);
            Vector2 delta = impactVelocity * ((1f - damping) / 5f);
            impactVelocity *= damping;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / .1f));
            for (int i = 0; i < steps; i++)
            {
                Vector2 next = position + delta / steps;
                if (!map.TrySampleMapPosition(next, out _) || IsObstacleSweepBlocked(position, next))
                { impactVelocity = Vector2.zero; wasDisplaced = false; CurrentSpeedMetersPerSecond = 0f; return true; }
                ApplyMapPosition(next);
                position = next;
            }
            CurrentSpeedMetersPerSecond = impactVelocity.magnitude;
            if (impactVelocity.sqrMagnitude < .001f) { impactVelocity = Vector2.zero; wasDisplaced = false; }
            return true;
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                value.x * cosine - value.y * sine,
                value.x * sine + value.y * cosine).normalized;
        }

        private static float DistanceSquaredToSegment(
            Vector2 point,
            Vector2 segmentStart,
            Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.000001f)
                return (point - segmentStart).sqrMagnitude;

            float t = Mathf.Clamp01(
                Vector2.Dot(point - segmentStart, segment) / lengthSquared);
            Vector2 closest = segmentStart + segment * t;
            return (point - closest).sqrMagnitude;
        }
    }
}

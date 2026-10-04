using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Garbage
{
    [DefaultExecutionOrder(40)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldInteraction))]
    public sealed class GarbageMotion : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float stopSpeed = .04f;
        [SerializeField, Min(.05f)] private float maximumSweepStep = .3f;
        private WorldInteraction item;
        private MapTestSceneController map;
        private RobotMover player;
        private float playerRadius;
        private Vector2 velocity;
        private Vector2 slideAcceleration;
        private Vector2 pendingPush;
        private float drag = 4f;
        private float maximumSpeed = 8f;
        private readonly List<GarbageMotion> separationSiblings = new();
        private readonly List<WorldInteraction> collisionHits = new();
        private float separationTimeRemaining;
        public Vector2 Velocity => velocity;
        public bool WasBlocked { get; private set; }

        private void Awake() => item = GetComponent<WorldInteraction>();
        private void OnEnable() { ClearFragmentSeparation(); velocity = Vector2.zero; slideAcceleration = Vector2.zero; pendingPush = Vector2.zero; WasBlocked = false; }
        private void OnDisable() { Stop(); pendingPush = Vector2.zero; }

        public void Initialize(MapTestSceneController sceneMap, RobotMover scenePlayer)
        {
            ClearFragmentSeparation();
            map = sceneMap;
            player = scenePlayer;
            if (player != null && player.TryGetComponent(out RobotMarkerView marker))
                playerRadius = marker.BodyDiameter * .5f;
        }

        public void Launch(Vector2 initialVelocity, float deceleration)
        {
            ClearFragmentSeparation();
            velocity = initialVelocity;
            slideAcceleration = Vector2.zero;
            drag = Mathf.Max(0f, deceleration);
            maximumSpeed = Mathf.Max(.1f, initialVelocity.magnitude);
        }

        public void SetSlide(Vector2 direction, float acceleration, float friction, float topSpeed)
        {
            slideAcceleration = direction.sqrMagnitude > .000001f
                ? direction.normalized * Mathf.Max(0f, acceleration) : Vector2.zero;
            drag = Mathf.Max(0f, friction);
            maximumSpeed = Mathf.Max(.1f, topSpeed);
        }

        public void Stop()
        {
            ClearFragmentSeparation();
            velocity = Vector2.zero;
            slideAcceleration = Vector2.zero;
        }

        // Call on every scattered fragment after the complete batch has been created.
        // Only pairs overlapping at this instant may pass through one another while
        // separating. The exception never applies to other garbage or solid props.
        public void BeginFragmentSeparation(IReadOnlyList<GarbageMotion> siblings,
            float maximumDuration = 1f)
        {
            ClearFragmentSeparation();
            if (item == null) item = GetComponent<WorldInteraction>();
            if (siblings == null || item == null || item.Owner != null
                || !isActiveAndEnabled || float.IsNaN(maximumDuration)
                || float.IsInfinity(maximumDuration) || maximumDuration <= 0f)
                return;
            foreach (GarbageMotion sibling in siblings)
            {
                if (sibling == null || sibling == this || !sibling.isActiveAndEnabled
                    || sibling.gameObject.scene != gameObject.scene
                    || separationSiblings.Contains(sibling)) continue;
                if (sibling.item == null) sibling.item = sibling.GetComponent<WorldInteraction>();
                if (OverlapsSibling(sibling)) separationSiblings.Add(sibling);
            }
            if (separationSiblings.Count > 0) separationTimeRemaining = maximumDuration;
        }

        private bool OverlapsSibling(GarbageMotion sibling)
        {
            return sibling != null && sibling.isActiveAndEnabled && sibling.item != null
                   && sibling.gameObject.scene == gameObject.scene
                   && sibling.item.Available && sibling.item.Owner == null && item != null
                   && item.Available && item.Owner == null
                   && WorldInteractionQuery.Penetration(item.GetShape(map), sibling.item.GetShape(map)) >= 0f;
        }

        private void RefreshFragmentSeparation(float deltaTime)
        {
            if (separationSiblings.Count == 0) return;
            separationTimeRemaining -= Mathf.Max(0f, deltaTime);
            if (separationTimeRemaining <= 0f)
            {
                ClearFragmentSeparation();
                return;
            }
            for (int i = separationSiblings.Count - 1; i >= 0; i--)
            {
                GarbageMotion sibling = separationSiblings[i];
                if (OverlapsSibling(sibling) && sibling.separationSiblings.Contains(this)) continue;
                separationSiblings.RemoveAt(i);
                RemoveFromSibling(sibling);
            }
            if (separationSiblings.Count == 0) separationTimeRemaining = 0f;
        }

        private void ClearFragmentSeparation()
        {
            foreach (GarbageMotion sibling in separationSiblings)
                RemoveFromSibling(sibling);
            separationSiblings.Clear();
            separationTimeRemaining = 0f;
        }

        private void RemoveFromSibling(GarbageMotion sibling)
        {
            if (sibling == null) return;
            sibling.separationSiblings.Remove(this);
            if (sibling.separationSiblings.Count == 0) sibling.separationTimeRemaining = 0f;
        }

        public void ApplyExternalPush(Vector2 displacement)
        {
            if (item == null || item.Owner != null) return;
            Stop();
            pendingPush += displacement;
        }

        private void Update() => Step(Time.deltaTime);

        private void Step(float deltaTime)
        {
            if (item == null || item.Owner != null)
            {
                Stop();
                pendingPush = Vector2.zero;
                return;
            }
            if (pendingPush.sqrMagnitude > 0f)
            {
                item.WorldPosition += (Vector3)pendingPush;
                pendingPush = Vector2.zero;
                return;
            }
            float dt = Mathf.Clamp(deltaTime, 0f, .05f);
            if (dt <= 0f) return;
            RefreshFragmentSeparation(deltaTime);
            velocity += slideAcceleration * dt;
            velocity = Vector2.MoveTowards(velocity, Vector2.zero, drag * dt);
            velocity = Vector2.ClampMagnitude(velocity, maximumSpeed);
            if (velocity.magnitude <= stopSpeed)
            {
                ClearFragmentSeparation();
                velocity = Vector2.zero;
                return;
            }
            Vector2 start = transform.position;
            Vector2 travel = velocity * dt;
            int steps = Mathf.Max(1, Mathf.CeilToInt(travel.magnitude / maximumSweepStep));
            WasBlocked = false;
            for (int i = 1; i <= steps; i++)
            {
                Vector2 next = start + travel * (i / (float)steps);
                if (!CanMoveTo(next))
                {
                    Stop();
                    WasBlocked = true;
                    return;
                }
                Vector3 position = item.WorldPosition;
                item.WorldPosition = new Vector3(next.x, next.y, position.z);
                // Restore each pair immediately after it separates, including between
                // substeps. Once removed, a pair cannot regain its initial exception.
                RefreshFragmentSeparation(0f);
            }
        }

        private bool CanMoveTo(Vector2 position)
        {
            if (map != null && map.HasGeneratedMap && !map.TrySampleWorldPosition(position, out _, out _))
                return false;
            InteractionShape shape = item.GetShape(map);
            Vector2 currentQuery = InteractionShape.ToQuery(transform.position, map);
            Vector2 nextQuery = InteractionShape.ToQuery(position, map);
            InteractionShape nextShape = shape.Translated(nextQuery - currentQuery);
            float sweepRadius = (shape.A - shape.Center).magnitude;
            if (shape.IsBox) sweepRadius = Mathf.Max(sweepRadius, (shape.C - shape.Center).magnitude);
            else sweepRadius += shape.Radius;
            InteractionShape priorCenter = InteractionShape.Capsule(shape.Center, shape.Center, sweepRadius);
            if (CollisionQueryBlocked(
                    InteractionShape.Capsule(shape.Center, nextShape.Center, sweepRadius),
                    priorCenter)) return false;
            if (CollisionQueryBlocked(nextShape, shape)) return false;
            if (player == null) return true;
            float shapeRadius = (shape.A - shape.Center).magnitude;
            if (shape.IsBox)
                shapeRadius = Mathf.Max(shapeRadius, (shape.C - shape.Center).magnitude);
            else shapeRadius += shape.Radius;
            shapeRadius += (shape.Center - currentQuery).magnitude;
            float worldRadius = playerRadius + (map != null && map.HasGeneratedMap
                ? map.MapMetersToWorldDistance(Vector2.right, shapeRadius) : shapeRadius);
            return ((Vector2)player.transform.position - position).sqrMagnitude > worldRadius * worldRadius;
        }

        private bool CollisionQueryBlocked(InteractionShape shape, InteractionShape previous)
        {
            if (separationSiblings.Count == 0)
                return WorldInteractionQuery.Query(shape,
                    WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision,
                    map, gameObject.scene, ignore: transform, previous: previous);
            WorldInteractionQuery.Query(shape,
                WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision,
                map, gameObject.scene, collisionHits, ignore: transform, previous: previous);
            foreach (WorldInteraction hit in collisionHits)
            {
                bool siblingHit = false;
                foreach (GarbageMotion sibling in separationSiblings)
                    if (sibling != null && (hit.transform == sibling.transform
                        || hit.transform.IsChildOf(sibling.transform)))
                    {
                        siblingHit = true;
                        break;
                    }
                if (!siblingHit) return true;
            }
            return false;
        }
    }
}

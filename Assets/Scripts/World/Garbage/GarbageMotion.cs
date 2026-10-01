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
        public Vector2 Velocity => velocity;
        public bool WasBlocked { get; private set; }

        private void Awake() => item = GetComponent<WorldInteraction>();
        private void OnEnable() { velocity = Vector2.zero; slideAcceleration = Vector2.zero; pendingPush = Vector2.zero; WasBlocked = false; }
        private void OnDisable() { Stop(); pendingPush = Vector2.zero; }

        public void Initialize(MapTestSceneController sceneMap, RobotMover scenePlayer)
        {
            map = sceneMap;
            player = scenePlayer;
            if (player != null && player.TryGetComponent(out RobotMarkerView marker))
                playerRadius = marker.BodyDiameter * .5f;
        }

        public void Launch(Vector2 initialVelocity, float deceleration)
        {
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
            velocity = Vector2.zero;
            slideAcceleration = Vector2.zero;
        }

        public void ApplyExternalPush(Vector2 displacement)
        {
            if (item == null || item.Owner != null) return;
            Stop();
            pendingPush += displacement;
        }

        private void Update()
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
            float dt = Mathf.Min(Time.deltaTime, .05f);
            if (dt <= 0f) return;
            velocity += slideAcceleration * dt;
            velocity = Vector2.MoveTowards(velocity, Vector2.zero, drag * dt);
            velocity = Vector2.ClampMagnitude(velocity, maximumSpeed);
            if (velocity.magnitude <= stopSpeed)
            {
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
            if (WorldInteractionQuery.Query(
                    InteractionShape.Capsule(shape.Center, nextShape.Center, sweepRadius),
                    WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision,
                    map, gameObject.scene, ignore: transform, previous: priorCenter)) return false;
            if (WorldInteractionQuery.Query(nextShape,
                    WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision,
                    map, gameObject.scene, ignore: transform, previous: shape)) return false;
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
    }
}

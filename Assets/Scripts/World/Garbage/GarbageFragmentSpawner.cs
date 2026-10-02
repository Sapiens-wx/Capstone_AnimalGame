using System.Collections;
using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Garbage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldInteraction))]
    public sealed class GarbageFragmentSpawner : MonoBehaviour
    {
        [Header("Small garbage prefabs")]
        [SerializeField] private GameObject[] heldFragments;
        [SerializeField] private GameObject[] scatteredFragments;
        [Header("Disassembly")]
        [SerializeField, Min(0f)] private float fadeDuration = .45f;
        [SerializeField, Range(2, 4)] private int minimumFragments = 2;
        [SerializeField, Range(2, 4)] private int maximumFragments = 4;
        [SerializeField, Range(0f, 180f)] private float scatterHalfAngle = 105f;
        [SerializeField, Min(0f)] private float minimumLaunchSpeed = 1.5f;
        [SerializeField, Min(0f)] private float maximumLaunchSpeed = 3.5f;
        [SerializeField, Min(0f)] private float launchDeceleration = 4f;
        [SerializeField, Min(1)] private int placementAttempts = 12;
        [SerializeField, Min(0f)] private float spawnClearance = .08f;
        private WorldInteraction source;
        private WorldInteractionKind originalKind;
        private SpriteRenderer[] renderers;
        private Color[] originalColors;
        private MapTestSceneController map;
        private RobotMover player;
        private RobotArmController arm;
        private readonly List<Vector3> placed = new();
        private readonly List<float> placedRadii = new();
        private bool started;

        private void Awake()
        {
            source = GetComponent<WorldInteraction>();
            originalKind = source.Kind;
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) originalColors[i] = renderers[i].color;
        }

        private void ResolveContext()
        {
            if (map == null)
                foreach (MapTestSceneController candidate in FindObjectsByType<MapTestSceneController>(FindObjectsSortMode.None))
                    if (candidate.gameObject.scene == gameObject.scene) { map = candidate; break; }
            if (player == null)
                foreach (RobotMover candidate in FindObjectsByType<RobotMover>(FindObjectsSortMode.None))
                    if (candidate.gameObject.scene == gameObject.scene)
                    { player = candidate; arm = candidate.GetComponent<RobotArmController>(); break; }
        }

        private void OnEnable()
        {
            started = false;
            if (source != null && source.Kind != originalKind) source.SetKind(originalKind);
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].color = originalColors[i];
        }

        public bool BeginBreak(bool transferToHand)
        {
            if (started || source == null) return false;
            started = true;
            StartCoroutine(BreakRoutine(transferToHand));
            return true;
        }

        private IEnumerator BreakRoutine(bool transferToHand)
        {
            ResolveContext();
            // Stop new grabs while the old held object is still allowed to be released.
            source.SetKind(WorldInteractionKind.None);
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float opacity = 1f - Mathf.Clamp01(elapsed / Mathf.Max(.001f, fadeDuration));
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null) continue;
                    Color color = originalColors[i];
                    color.a *= opacity;
                    renderers[i].color = color;
                }
                yield return null;
            }

            Vector3 center = transform.position;
            Vector2 outward = player != null ? (Vector2)(center - player.transform.position) : Vector2.up;
            if (outward.sqrMagnitude < .000001f) outward = Vector2.up;
            outward.Normalize();
            placed.Clear(); placedRadii.Clear();
            if (transferToHand && TrySpawnHeldFragment(out WorldInteraction carried))
            {
                if (arm == null || !arm.TryReplaceHeldObject(source, carried))
                    carried.Release(arm);
            }
            int count = Random.Range(Mathf.Min(minimumFragments, maximumFragments),
                Mathf.Max(minimumFragments, maximumFragments) + 1);
            for (int i = 0; i < count; i++)
            {
                Vector2 direction = Quaternion.Euler(0f, 0f, Random.Range(-scatterHalfAngle, scatterHalfAngle)) * outward;
                float speed = Random.Range(minimumLaunchSpeed, Mathf.Max(minimumLaunchSpeed, maximumLaunchSpeed));
                TrySpawnScatteredFragment(center, direction, speed, out _);
            }
            if (arm != null) arm.ReleaseHeldObject(source);
            Destroy(gameObject);
        }

        private bool TrySpawnHeldFragment(out WorldInteraction result)
        {
            result = null;
            if (arm == null || heldFragments == null || heldFragments.Length == 0) return false;

            // Pick a valid prefab without depending on placement attempts or free space.
            GameObject prefab = null;
            int validCount = 0;
            foreach (GameObject candidate in heldFragments)
            {
                if (candidate == null || candidate.GetComponent<WorldInteraction>() == null) continue;
                if (Random.Range(0, ++validCount) == 0) prefab = candidate;
            }
            if (prefab == null) return false;

            Vector2 midpoint = (arm.LeftHandWorld + arm.RightHandWorld) * .5f;
            Vector3 position = new Vector3(midpoint.x, midpoint.y, transform.position.z);
            GameObject clone = Instantiate(prefab, position, prefab.transform.rotation);
            result = clone.GetComponent<WorldInteraction>();
            if (!clone.TryGetComponent(out GarbageMotion motion)) motion = clone.AddComponent<GarbageMotion>();
            motion.Initialize(map, player);
            // Scattered fragments still avoid this fragment, but its own placement is unconditional.
            placed.Add(position); placedRadii.Add(FragmentRadius(prefab));
            return true;
        }

        private bool TrySpawnScatteredFragment(Vector3 center, Vector2 direction, float speed,
            out WorldInteraction result)
        {
            result = null;
            GameObject[] prefabs = scatteredFragments;
            if (prefabs == null || prefabs.Length == 0) return false;
            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
                if (prefab == null || prefab.GetComponent<WorldInteraction>() == null) continue;
                float radius = FragmentRadius(prefab);
                float distance = radius + spawnClearance + .15f + attempt * .12f;
                Vector2 bearing = attempt == 0 ? direction :
                    (Vector2)(Quaternion.Euler(0f, 0f, Random.Range(-35f, 35f)) * direction);
                Vector3 position = center + (Vector3)(bearing.normalized * distance);
                if (!SafePath(position, bearing, speed, radius)) continue;
                GameObject clone = Instantiate(prefab, position, prefab.transform.rotation);
                result = clone.GetComponent<WorldInteraction>();
                if (result == null) { Destroy(clone); continue; }
                placed.Add(position); placedRadii.Add(radius);
                if (!clone.TryGetComponent(out GarbageMotion motion)) motion = clone.AddComponent<GarbageMotion>();
                motion.Initialize(map, player);
                if (speed > 0f) motion.Launch(bearing.normalized * speed, launchDeceleration);
                return true;
            }
            return false;
        }

        private static float FragmentRadius(GameObject prefab)
        {
            SpriteRenderer sprite = prefab.GetComponentInChildren<SpriteRenderer>();
            float radius = .35f;
            if (sprite != null && sprite.sprite != null)
            {
                Vector3 scale = sprite.transform.lossyScale;
                Vector3 extent = sprite.sprite.bounds.extents;
                float visualRadius = new Vector2(extent.x * Mathf.Abs(scale.x),
                    extent.y * Mathf.Abs(scale.y)).magnitude;
                Vector3 visualCenter = sprite.transform.TransformPoint(sprite.sprite.bounds.center);
                radius = Mathf.Max(.15f, visualRadius
                    + Vector2.Distance(visualCenter, prefab.transform.position));
            }
            else if (prefab.TryGetComponent(out BoxCollider2D box))
            {
                Vector3 scale = box.transform.lossyScale;
                radius = new Vector2(box.size.x * Mathf.Abs(scale.x),
                    box.size.y * Mathf.Abs(scale.y)).magnitude * .5f
                    + Vector2.Distance(box.transform.TransformPoint(box.offset), prefab.transform.position);
            }
            return radius;
        }

        private bool SafePath(Vector3 origin, Vector2 direction, float speed, float radius)
        {
            Vector2 end = (Vector2)origin + direction.normalized * speed * .18f;
            if (map != null && map.HasGeneratedMap)
            {
                if (!map.TrySampleWorldPosition(origin, out _, out _)
                    || !map.TrySampleWorldPosition(end, out _, out _)
                    || !map.TrySampleWorldPosition(((Vector2)origin + end) * .5f, out _, out _)) return false;
            }
            float clearance = radius + spawnClearance;
            if (player != null)
            {
                float bodyRadius = player.TryGetComponent(out RobotMarkerView marker) ? marker.BodyDiameter * .5f : .4f;
                if (SegmentDistanceSquared(player.transform.position, origin, end)
                    < (bodyRadius + clearance) * (bodyRadius + clearance)) return false;
            }
            for (int i = 0; i < placed.Count; i++)
                if (SegmentDistanceSquared(placed[i], origin, end)
                    < Mathf.Pow(placedRadii[i] + clearance, 2f)) return false;
            Vector2 a = InteractionShape.ToQuery(origin, map);
            Vector2 b = InteractionShape.ToQuery(end, map);
            float queryRadius = map != null && map.HasGeneratedMap
                ? Mathf.Max(
                    (InteractionShape.ToQuery((Vector2)origin + Vector2.right * clearance, map) - a).magnitude,
                    (InteractionShape.ToQuery((Vector2)origin + Vector2.up * clearance, map) - a).magnitude)
                : clearance;
            return !WorldInteractionQuery.Query(InteractionShape.Capsule(a, b, queryRadius),
                WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision,
                map, gameObject.scene, ignore: transform);
        }

        private static float SegmentDistanceSquared(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 segment = b - a;
            float t = segment.sqrMagnitude > .000001f
                ? Mathf.Clamp01(Vector2.Dot(point - a, segment) / segment.sqrMagnitude) : 0f;
            return (point - (a + segment * t)).sqrMagnitude;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.RobotArm;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Garbage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldInteraction))]
    public sealed class GarbageFragmentSpawner : MonoBehaviour
    {
        [Header("Fragment prefabs")]
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
        [Header("Heavy pull: interior regions")]
        [Tooltip("Distance of each region from the large sprite center, relative to its shorter dimension.")]
        [SerializeField, Range(.15f, .28f)] private float interiorSpreadRatio = .21f;
        [SerializeField, Range(0f, .025f)] private float interiorPositionJitterRatio = .015f;
        [SerializeField, Range(0f, 18f)] private float interiorRotationJitter = 12f;
        [Tooltip("Inset from each edge of the filled outline, relative to the sprite dimensions.")]
        [SerializeField, Range(0f, .05f)] private float interiorBoundaryInsetRatio = .01f;
        [Tooltip("Convex filled outer envelope of the pile. Coordinates relative to the sprite bounds center, from -0.5 to 0.5. Empty uses the sprite rectangle.")]
        [SerializeField] private Vector2[] interiorOutline;
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
        private Transform pullPreviewRoot;
        private readonly List<PullFragment> pullFragments = new();
        private float pullBurstDeceleration;
        private float pullSeparationDuration;
        private Vector3 pullInitialHand;

        private sealed class PullFragment
        {
            public GameObject Prefab;
            public Transform Preview;
            public Vector3 Position;
            public Vector3 InitialPosition, ExtractionOffset;
            public Quaternion Rotation;
            public Vector2 Direction;
            public float Speed, Radius;
            public bool Held;
            public SpriteRenderer[] Sprites;
            public Color[] Colors;
        }

        public Transform PullPreviewRoot => pullPreviewRoot;
        public int PullPreviewCount => pullFragments.Count;
        public bool HasPullFragmentPrefabs => HasValidPrefab(heldFragments) && HasValidPrefab(scatteredFragments);

        private static bool HasValidPrefab(GameObject[] candidates)
        {
            if (candidates == null) return false;
            foreach (GameObject candidate in candidates)
                if (candidate != null && candidate.GetComponent<WorldInteraction>() != null) return true;
            return false;
        }

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

        public bool BeginPullPreview(RobotArmController owner, Vector2 pullDirection)
        {
            if (started || source == null || owner == null || source.Owner != owner) return false;
            if (pullPreviewRoot != null) return true;
            ResolveContext();
            arm = owner;
            player = owner.GetComponent<RobotMover>();
            GameObject heldPrefab = PickValidPrefab(heldFragments);
            if (heldPrefab == null) return false;
            var root = new GameObject("Pull Fragment Preview (visual only)");
            root.transform.SetParent(transform, false);
            pullPreviewRoot = root.transform;
            placed.Clear(); placedRadii.Clear();
            Vector2 hand = (arm.LeftHandWorld + arm.RightHandWorld) * .5f;
            Vector3 center = source.SpriteSource != null ? SpriteVisualCenter(source.SpriteSource) : transform.position;
            Vector2 outward = pullDirection.sqrMagnitude > .000001f ? -pullDirection.normalized : Vector2.up;
            Vector2 right = new Vector2(outward.y, -outward.x);
            float sourceSize = SourceShortDimension();
            pullInitialHand = new Vector3(hand.x, hand.y, transform.position.z);
            if (!TryAddInteriorFragment(heldPrefab, new Vector2(0f, -interiorSpreadRatio),
                    center, right, outward, sourceSize, Vector2.zero, 0f, true))
            {
                CancelPullPreview();
                return false;
            }
            PullFragment held = pullFragments[0];
            Vector3 gripCenter = (Vector3)hand + (Vector3)(outward * FragmentGripDepth(heldPrefab, held.Rotation, outward));
            gripCenter.z = center.z;
            Vector3 target = RootForVisualCenter(heldPrefab, held.Rotation, gripCenter);
            Vector3 extraction = target - held.InitialPosition;
            // A deeply embedded grip already reaches this region. Only extract the
            // piece toward the player; never push it farther into the pile.
            if (Vector2.Dot(extraction, -outward) > 0f) held.ExtractionOffset = extraction;
            int minimumCount = Mathf.Clamp(Mathf.Min(minimumFragments, maximumFragments), 2, 3);
            int maximumCount = Mathf.Clamp(Mathf.Max(minimumFragments, maximumFragments), minimumCount, 3);
            int count = Random.Range(minimumCount, maximumCount + 1);
            for (int i = 0; i < count; i++)
            {
                GameObject prefab = PickValidPrefab(scatteredFragments);
                if (prefab == null) continue;
                float spread = Mathf.Clamp(scatterHalfAngle, 60f, 90f);
                if (count == 2) spread *= .65f;
                float angle = Mathf.Lerp(spread, -spread, i / (float)(count - 1));
                angle = Mathf.Clamp(angle + Random.Range(-7f, 7f), -90f, 90f);
                Vector2 direction = Quaternion.Euler(0f, 0f, angle) * outward;
                float speed = Random.Range(minimumLaunchSpeed, Mathf.Max(minimumLaunchSpeed, maximumLaunchSpeed));
                Vector2 region = count == 2
                    ? new Vector2((i == 0 ? -1f : 1f) * interiorSpreadRatio * .86f, interiorSpreadRatio * .38f)
                    : i == 1 ? Vector2.up * interiorSpreadRatio
                        : Vector2.right * (i == 0 ? -interiorSpreadRatio : interiorSpreadRatio);
                TryAddInteriorFragment(prefab, region, center, right, outward, sourceSize, direction, speed, false);
            }
            if (pullFragments.Count != count + 1)
            {
                CancelPullPreview();
                return false;
            }
            ConfigurePullBurst();
            UpdatePullPreview(0f, hand);
            return true;
        }

        public void UpdatePullPreview(float progress, Vector2 heldAnchor)
        {
            float opacity = Mathf.Clamp01(progress);
            foreach (PullFragment fragment in pullFragments)
            {
                if (fragment.Held)
                {
                    Vector3 hand = new Vector3(heldAnchor.x, heldAnchor.y, pullInitialHand.z);
                    fragment.Position = fragment.InitialPosition + hand - pullInitialHand
                        + fragment.ExtractionOffset * Mathf.SmoothStep(0f, 1f, opacity);
                    fragment.Preview.position = fragment.Position;
                }
                for (int i = 0; i < fragment.Sprites.Length; i++)
                {
                    Color color = fragment.Colors[i];
                    color.a *= opacity;
                    fragment.Sprites[i].color = color;
                }
            }
        }

        public void CancelPullPreview()
        {
            pullFragments.Clear();
            pullInitialHand = Vector3.zero;
            if (pullPreviewRoot != null)
            {
                pullPreviewRoot.gameObject.SetActive(false);
                DestroyRuntimeObject(pullPreviewRoot.gameObject);
            }
            pullPreviewRoot = null;
            placed.Clear(); placedRadii.Clear();
        }

        public bool CompletePullBreak()
        {
            if (started || pullFragments.Count == 0 || arm == null || arm.HeldObject != source
                || source.Owner != arm) return false;
            // Validate the exact preview positions again; moving obstacles cannot cause a
            // different arrangement to appear on the break frame.
            placed.Clear(); placedRadii.Clear();
            foreach (PullFragment fragment in pullFragments)
            {
                if (!fragment.Held && (!FitsSourceInterior(fragment.Prefab, fragment.Position, fragment.Rotation)
                    || !SafeExternalPath(fragment.Position, fragment.Direction, fragment.Speed, fragment.Radius)))
                    return false;
                placed.Add(fragment.Position); placedRadii.Add(fragment.Radius);
            }
            PullFragment held = pullFragments[0];
            WorldInteraction carried = SpawnPullFragment(held);
            if (carried == null) return false;
            if (!arm.TryReplaceHeldObject(source, carried))
            {
                DestroyRuntimeObject(carried.gameObject);
                return false;
            }
            started = true;
            source.SetKind(WorldInteractionKind.None);
            var scatteredMotion = new List<GarbageMotion>();
            for (int i = 1; i < pullFragments.Count; i++)
            {
                WorldInteraction fragment = SpawnPullFragment(pullFragments[i]);
                if (fragment != null && fragment.TryGetComponent(out GarbageMotion motion)) scatteredMotion.Add(motion);
            }
            foreach (GarbageMotion motion in scatteredMotion)
                motion.BeginFragmentSeparation(scatteredMotion, pullSeparationDuration);
            CancelPullPreview();
            gameObject.SetActive(false);
            DestroyRuntimeObject(gameObject);
            return true;
        }

        private void AddPullFragment(GameObject prefab, Vector3 position, Quaternion rotation,
            Vector2 direction, float speed, bool held)
        {
            var visual = new GameObject(held ? "Held Medium Preview" : "Scattered Medium Preview");
            visual.transform.SetParent(pullPreviewRoot, false);
            visual.transform.SetPositionAndRotation(position, rotation);
            Vector3 parentScale = pullPreviewRoot.lossyScale;
            Vector3 prefabScale = prefab.transform.localScale;
            visual.transform.localScale = new Vector3(prefabScale.x / Mathf.Max(.0001f, Mathf.Abs(parentScale.x)),
                prefabScale.y / Mathf.Max(.0001f, Mathf.Abs(parentScale.y)),
                prefabScale.z / Mathf.Max(.0001f, Mathf.Abs(parentScale.z)));
            CopySpriteVisuals(prefab.transform, visual.transform);
            SpriteRenderer[] sprites = visual.GetComponentsInChildren<SpriteRenderer>(true);
            var colors = new Color[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                colors[i] = sprites[i].color;
                Color transparent = colors[i]; transparent.a = 0f;
                sprites[i].color = transparent;
            }
            var fragment = new PullFragment { Prefab = prefab, Preview = visual.transform,
                Position = position, InitialPosition = position, Rotation = rotation, Direction = direction,
                Speed = speed, Radius = FragmentRadius(prefab), Held = held, Sprites = sprites, Colors = colors };
            pullFragments.Add(fragment);
            placed.Add(position); placedRadii.Add(fragment.Radius);
        }

        private void CopySpriteVisuals(Transform template, Transform destination)
        {
            if (template.TryGetComponent(out SpriteRenderer original))
            {
                SpriteRenderer copy = destination.gameObject.AddComponent<SpriteRenderer>();
                copy.sprite = original.sprite;
                copy.sharedMaterial = original.sharedMaterial;
                copy.color = original.color;
                copy.flipX = original.flipX; copy.flipY = original.flipY;
                copy.drawMode = original.drawMode; copy.size = original.size;
                copy.sortingLayerID = source.SpriteSource != null ? source.SpriteSource.sortingLayerID : original.sortingLayerID;
                copy.sortingOrder = (source.SpriteSource != null ? source.SpriteSource.sortingOrder : 0) + original.sortingOrder + 1;
                copy.enabled = original.enabled;
            }
            foreach (Transform child in template)
            {
                if (child.GetComponentInChildren<SpriteRenderer>(true) == null) continue;
                var copy = new GameObject(child.name);
                copy.transform.SetParent(destination, false);
                copy.transform.localPosition = child.localPosition;
                copy.transform.localRotation = child.localRotation;
                copy.transform.localScale = child.localScale;
                CopySpriteVisuals(child, copy.transform);
                copy.SetActive(child.gameObject.activeSelf);
            }
        }

        private WorldInteraction SpawnPullFragment(PullFragment fragment)
        {
            GameObject clone = Instantiate(fragment.Prefab, fragment.Position, fragment.Rotation);
            WorldInteraction interaction = clone.GetComponent<WorldInteraction>();
            if (interaction == null) { DestroyRuntimeObject(clone); return null; }
            if (clone.scene != gameObject.scene) interaction.MoveToScene(gameObject.scene);
            SpriteRenderer[] sprites = clone.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer sprite in sprites)
            {
                sprite.sortingLayerID = source.SpriteSource != null ? source.SpriteSource.sortingLayerID : sprite.sortingLayerID;
                sprite.sortingOrder += (source.SpriteSource != null ? source.SpriteSource.sortingOrder : 0) + 1;
            }
            if (!clone.TryGetComponent(out GarbageMotion motion)) motion = clone.AddComponent<GarbageMotion>();
            motion.Initialize(map, player);
            if (!fragment.Held && fragment.Speed > 0f)
                motion.Launch(fragment.Direction * fragment.Speed, pullBurstDeceleration);
            return interaction;
        }

        private float SourceShortDimension()
        {
            SpriteRenderer renderer = source.SpriteSource;
            if (renderer == null || renderer.sprite == null) return Mathf.Min(source.LocalSize.x, source.LocalSize.y);
            Vector3 size = renderer.sprite.bounds.size;
            return Mathf.Min(renderer.transform.TransformVector(Vector3.right * size.x).magnitude,
                renderer.transform.TransformVector(Vector3.up * size.y).magnitude);
        }

        private bool TryAddInteriorFragment(GameObject prefab, Vector2 region, Vector3 center,
            Vector2 right, Vector2 outward, float sourceSize, Vector2 direction, float speed, bool held)
        {
            Vector2 jitter = new Vector2(Random.Range(-interiorPositionJitterRatio, interiorPositionJitterRatio),
                Random.Range(-interiorPositionJitterRatio, interiorPositionJitterRatio));
            float rotationJitter = Random.Range(-interiorRotationJitter, interiorRotationJitter);
            Quaternion baseRotation = source.SpriteSource != null
                ? source.SpriteSource.transform.rotation : transform.rotation;
            float radius = FragmentRadius(prefab);
            for (int attempt = 0; attempt < Mathf.Max(1, placementAttempts); attempt++)
            {
                // Stay in the assigned region. First reduce rotation and jitter, then
                // move at most 20% inward; a crowded region never collapses to the center.
                int variant = attempt % 3;
                float spread = Mathf.Max(.8f, 1f - attempt / 3 * .05f);
                float variation = 1f - variant * .5f;
                Vector2 local = region * spread + jitter * variation;
                Vector3 visualCenter = center + (Vector3)((right * local.x + outward * local.y) * sourceSize);
                Quaternion rotation = baseRotation * Quaternion.Euler(0f, 0f, rotationJitter * variation);
                Vector3 position = RootForVisualCenter(prefab, rotation, visualCenter);
                if (!FitsSourceInterior(prefab, position, rotation)
                    || !SafeExternalPath(position, direction, speed, radius)) continue;
                bool distinct = true;
                foreach (PullFragment existing in pullFragments)
                    if (Vector2.Distance(visualCenter,
                            SpriteVisualCenter(existing.Preview.GetComponentInChildren<SpriteRenderer>())) < sourceSize * .15f)
                    {
                        distinct = false;
                        break;
                    }
                if (!distinct) continue;
                AddPullFragment(prefab, position, rotation, direction, speed, held);
                return true;
            }
            return false;
        }

        private static float FragmentGripDepth(GameObject prefab, Quaternion rotation, Vector2 outward)
        {
            SpriteRenderer body = prefab.GetComponentInChildren<SpriteRenderer>();
            if (body == null || body.sprite == null) return .15f;
            Quaternion rotate = rotation * Quaternion.Inverse(prefab.transform.rotation);
            Vector3 halfX = rotate * body.transform.TransformVector(Vector3.right * body.sprite.bounds.extents.x);
            Vector3 halfY = rotate * body.transform.TransformVector(Vector3.up * body.sprite.bounds.extents.y);
            // Put the hands inside the player-facing half of the released piece.
            return (Mathf.Abs(Vector2.Dot(halfX, outward)) + Mathf.Abs(Vector2.Dot(halfY, outward))) * .55f;
        }

        private static Vector3 RootForVisualCenter(GameObject prefab, Quaternion rotation, Vector3 center)
        {
            SpriteRenderer body = prefab.GetComponentInChildren<SpriteRenderer>();
            if (body == null) return center;
            Vector3 offset = Quaternion.Inverse(prefab.transform.rotation)
                * (SpriteVisualCenter(body) - prefab.transform.position);
            return center - rotation * offset;
        }

        private static Vector3 SpriteVisualCenter(SpriteRenderer renderer)
        {
            if (renderer.sprite == null) return renderer.transform.position;
            // The original renderer is disabled during the pull. Derive its center
            // from sprite geometry so an unrendered or moved object cannot use stale bounds.
            Vector3 localCenter = renderer.sprite.bounds.center;
            if (renderer.flipX) localCenter.x = -localCenter.x;
            if (renderer.flipY) localCenter.y = -localCenter.y;
            return renderer.transform.TransformPoint(localCenter);
        }

        private bool FitsSourceInterior(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            SpriteRenderer original = source.SpriteSource;
            if (original == null || original.sprite == null) return true;
            Bounds bounds = original.sprite.bounds;
            Matrix4x4 placement = Matrix4x4.TRS(position, rotation, prefab.transform.localScale)
                * prefab.transform.worldToLocalMatrix;
            foreach (SpriteRenderer sprite in prefab.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sprite.sprite == null || !sprite.enabled) continue;
                Bounds fragmentBounds = sprite.sprite.bounds;
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 point = new Vector3((corner & 1) == 0 ? fragmentBounds.min.x : fragmentBounds.max.x,
                        (corner & 2) == 0 ? fragmentBounds.min.y : fragmentBounds.max.y, 0f);
                    if (sprite.flipX) point.x = -point.x;
                    if (sprite.flipY) point.y = -point.y;
                    point = original.transform.InverseTransformPoint(
                        placement.MultiplyPoint3x4(sprite.transform.TransformPoint(point)));
                    if (original.flipX) point.x = -point.x;
                    if (original.flipY) point.y = -point.y;
                    Vector2 normalized = new Vector2((point.x - bounds.center.x) / Mathf.Max(.0001f, bounds.size.x),
                        (point.y - bounds.center.y) / Mathf.Max(.0001f, bounds.size.y));
                    if (!InsideInteriorOutline(normalized)) return false;
                }
            }
            return true;
        }

        private bool InsideInteriorOutline(Vector2 point)
        {
            if (interiorOutline == null || interiorOutline.Length < 3)
            {
                float extent = .5f - interiorBoundaryInsetRatio;
                return Mathf.Abs(point.x) <= extent && Mathf.Abs(point.y) <= extent;
            }
            float area = 0f;
            for (int i = 0; i < interiorOutline.Length; i++)
            {
                Vector2 a = interiorOutline[i], b = interiorOutline[(i + 1) % interiorOutline.Length];
                area += a.x * b.y - a.y * b.x;
            }
            float winding = area >= 0f ? 1f : -1f;
            for (int i = 0; i < interiorOutline.Length; i++)
            {
                Vector2 a = interiorOutline[i], edge = interiorOutline[(i + 1) % interiorOutline.Length] - a;
                float length = edge.magnitude;
                if (length < .000001f) continue;
                Vector2 offset = point - a;
                float distance = winding * (edge.x * offset.y - edge.y * offset.x) / length;
                if (distance < interiorBoundaryInsetRatio) return false;
            }
            return true;
        }

        private void ConfigurePullBurst()
        {
            // Give overlapping interior fragments enough travel to become separate objects at rest.
            // Initial speeds keep their configured range; adjust this burst's drag instead.
            float minimumSpeed = float.PositiveInfinity, maximumSpeed = 0f, requiredTravel = 0f;
            for (int i = 1; i < pullFragments.Count; i++)
            {
                PullFragment a = pullFragments[i];
                minimumSpeed = Mathf.Min(minimumSpeed, a.Speed);
                maximumSpeed = Mathf.Max(maximumSpeed, a.Speed);
                for (int j = i + 1; j < pullFragments.Count; j++)
                {
                    PullFragment b = pullFragments[j];
                    float halfAngleSine = Mathf.Sqrt(Mathf.Max(.0001f, (1f - Vector2.Dot(a.Direction, b.Direction)) * .5f));
                    requiredTravel = Mathf.Max(requiredTravel,
                        (a.Radius + b.Radius + spawnClearance) / (2f * halfAngleSine));
                }
            }
            requiredTravel += maximumSpeed * .05f;
            pullBurstDeceleration = launchDeceleration;
            if (minimumSpeed > 0f && requiredTravel > 0f && pullBurstDeceleration > 0f)
                pullBurstDeceleration = Mathf.Min(pullBurstDeceleration,
                    minimumSpeed * minimumSpeed / (2f * requiredTravel));
            pullSeparationDuration = pullBurstDeceleration > 0f
                ? maximumSpeed / pullBurstDeceleration + .1f
                : requiredTravel / Mathf.Max(.01f, minimumSpeed) * 2f + .1f;
        }

        private static GameObject PickValidPrefab(GameObject[] candidates)
        {
            if (candidates == null) return null;
            GameObject result = null;
            int count = 0;
            foreach (GameObject candidate in candidates)
                if (candidate != null && candidate.GetComponent<WorldInteraction>() != null
                    && Random.Range(0, ++count) == 0) result = candidate;
            return result;
        }

        private static void DestroyRuntimeObject(Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private void OnDisable() => CancelPullPreview();
        private void OnDestroy() => CancelPullPreview();

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

        private bool SafePath(Vector3 origin, Vector2 direction, float speed, float radius) =>
            SafeFragmentPath(origin, direction, speed, radius, true);

        private bool SafeExternalPath(Vector3 origin, Vector2 direction, float speed, float radius) =>
            SafeFragmentPath(origin, direction, speed, radius, false);

        private bool SafeFragmentPath(Vector3 origin, Vector2 direction, float speed, float radius, bool checkFragments)
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
            if (checkFragments)
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

using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    /// <summary>
    /// Presentation-only head-to-tail turn lag. The motor remains the authority
    /// for facing, movement and collisions; only the sprite vertices are bent.
    /// </summary>
    [DefaultExecutionOrder(140)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Turn Deformation")]
    public sealed class AnimalTurnDeformation : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [Tooltip("Body fill and outline only. Exclude arrows and the undiscovered field.")]
        [SerializeField] private SpriteRenderer[] bodyRenderers = Array.Empty<SpriteRenderer>();
        [SerializeField] private Material turnMaterial;
        [SerializeField, Min(0.001f)] private float followTimeSeconds = 0.12f;
        [SerializeField, Range(0f, 45f)] private float maximumBendDegrees = 22f;
        [Tooltip("Head anchor in Visual Root local units, shared by all body layers.")]
        [SerializeField] private Vector2 headAnchor = new Vector2(0f, 0.4f);
        [Tooltip("Tail anchor in Visual Root local units, shared by all body layers.")]
        [SerializeField] private Vector2 tailAnchor = new Vector2(0f, -0.4f);

        private const int HorizontalCells = 4;
        private const int VerticalCells = 24;
        private static readonly int RendererToVisualId = Shader.PropertyToID("_TurnRendererToVisual");
        private static readonly int VisualToRendererId = Shader.PropertyToID("_TurnVisualToRenderer");
        private static readonly int HeadTailId = Shader.PropertyToID("_TurnHeadTail");
        private static readonly int BendId = Shader.PropertyToID("_TurnBendRadians");

        private sealed class SpriteMesh
        {
            public Sprite Source;
            public Sprite Runtime;
            public Vector2[] Vertices;
            public int Users;
        }

        private sealed class BodyBinding
        {
            public SpriteRenderer Renderer;
            public Material OriginalMaterial;
            public Material AppliedMaterial;
            public SpriteMesh Mesh;
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        }

        private static readonly Dictionary<Sprite, SpriteMesh> Meshes = new Dictionary<Sprite, SpriteMesh>();
        private BodyBinding[] bindings;
        private AnimalAgent agent;
        private float previousWrappedAngle;
        private double continuousHeading;
        private double followingHeading;
        private bool headingInitialized;
        private float bendDegrees;
        private int synchronizedFrame = -1;
        private HeadingState frameStartHeading;

        private struct HeadingState
        {
            public float Wrapped, Bend;
            public double Continuous, Following;
            public bool Initialized;
        }

        public float FollowTimeSeconds
        {
            get => followTimeSeconds;
            set => followTimeSeconds = Mathf.Max(0.001f, value);
        }
        public float MaximumBendDegrees
        {
            get => maximumBendDegrees;
            set => maximumBendDegrees = Mathf.Clamp(value, 0f, 45f);
        }
        public Vector2 HeadAnchor { get => headAnchor; set => headAnchor = value; }
        public Vector2 TailAnchor { get => tailAnchor; set => tailAnchor = value; }
        public float CurrentBendDegrees => bendDegrees;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMeshCache()
        {
            foreach (SpriteMesh mesh in Meshes.Values)
                DestroyRuntimeSprite(mesh.Runtime);
            Meshes.Clear();
        }

        private void OnEnable()
        {
            agent = GetComponent<AnimalAgent>();
            ResetDeformation();
        }

        private void LateUpdate() => SynchronizeForQuery();

        // Queries can run before OR after AnimalSimulation.Update. Re-evaluate
        // from the frame's starting state, so an early camera/debug query cannot
        // suppress the final pose or integrate time twice in the same frame.
        internal void SynchronizeForQuery()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
                return;
            SynchronizeFrame(Time.deltaTime, Time.frameCount);
        }

        private void SynchronizeFrame(float deltaTime, int frame)
        {
            if (!isActiveAndEnabled)
                return;
            if (synchronizedFrame == frame)
            {
                previousWrappedAngle = frameStartHeading.Wrapped;
                continuousHeading = frameStartHeading.Continuous;
                followingHeading = frameStartHeading.Following;
                bendDegrees = frameStartHeading.Bend;
                headingInitialized = frameStartHeading.Initialized;
            }
            else
            {
                frameStartHeading = new HeadingState
                {
                    Wrapped = previousWrappedAngle,
                    Continuous = continuousHeading,
                    Following = followingHeading,
                    Bend = bendDegrees,
                    Initialized = headingInitialized
                };
            }
            synchronizedFrame = frame;
            TickVisuals(deltaTime);
        }

        public void ResetDeformation()
        {
            InitializeHeading();
            synchronizedFrame = -1;
            ApplyToBodies();
        }

        private void InitializeHeading()
        {
            previousWrappedAngle = transform.eulerAngles.z;
            continuousHeading = previousWrappedAngle;
            followingHeading = continuousHeading;
            headingInitialized = true;
            bendDegrees = 0f;
        }

        private void TickVisuals(float deltaTime)
        {
            if (!isActiveAndEnabled)
                return;
            if (agent == null)
                agent = GetComponent<AnimalAgent>();
            if (visualRoot == null || turnMaterial == null
                || (agent != null && agent.Health != null && agent.Health.IsDead))
            {
                RestoreBodies();
                headingInitialized = false;
                bendDegrees = 0f;
                return;
            }

            EnsureBindings();
            if (!headingInitialized)
                InitializeHeading();
            if (deltaTime > 0f)
            {
                float wrapped = transform.eulerAngles.z;
                double step = Mathf.DeltaAngle(previousWrappedAngle, wrapped);
                double previousHeading = continuousHeading;
                continuousHeading += step;
                previousWrappedAngle = wrapped;

                // Exact first-order response to a heading ramp over this frame.
                // Unlike Lerp(currentHeading), this has the same phase lag at
                // 30, 60 and 120 FPS for a constant angular velocity.
                double time = Math.Max(0.001, followTimeSeconds);
                double decay = Math.Exp(-deltaTime / time);
                double rampLag = step * time / deltaTime;
                followingHeading = continuousHeading - rampLag
                    + (followingHeading - previousHeading + rampLag) * decay;
                bendDegrees = Mathf.Clamp((float)(followingHeading - continuousHeading),
                    -maximumBendDegrees, maximumBendDegrees);
            }
            ApplyToBodies();
        }

        private void EnsureBindings()
        {
            int count = bodyRenderers != null ? bodyRenderers.Length : 0;
            bool changed = bindings == null || bindings.Length != count;
            if (!changed)
                for (int index = 0; index < count; index++)
                    if (bindings[index].Renderer != bodyRenderers[index]) { changed = true; break; }
            if (changed)
            {
                RestoreBodies();
                bindings = new BodyBinding[count];
                for (int index = 0; index < count; index++)
                {
                    SpriteRenderer renderer = bodyRenderers[index];
                    bindings[index] = new BodyBinding
                    {
                        Renderer = renderer,
                        OriginalMaterial = renderer != null ? renderer.sharedMaterial : null
                    };
                }
            }

            foreach (BodyBinding binding in bindings)
            {
                SpriteRenderer renderer = binding.Renderer;
                if (renderer == null)
                    continue;
                if (binding.Mesh != null && renderer.sprite != binding.Mesh.Runtime)
                {
                    Release(binding.Mesh);
                    binding.Mesh = null;
                    renderer.ResetLocalBounds();
                }
                if (binding.Mesh == null && renderer.sprite != null)
                {
                    binding.Mesh = Acquire(renderer.sprite);
                    renderer.sprite = binding.Mesh.Runtime;
                }
                if (binding.AppliedMaterial != turnMaterial)
                {
                    if (renderer.sharedMaterial == binding.AppliedMaterial)
                        renderer.sharedMaterial = binding.OriginalMaterial;
                    binding.AppliedMaterial = turnMaterial;
                }
                if (renderer.sharedMaterial != turnMaterial)
                    renderer.sharedMaterial = turnMaterial;
            }
        }

        private void ApplyToBodies()
        {
            if (bindings == null || visualRoot == null)
                return;
            Vector4 anchors = new Vector4(headAnchor.x, headAnchor.y, tailAnchor.x, tailAnchor.y);
            float radians = bendDegrees * Mathf.Deg2Rad;
            foreach (BodyBinding binding in bindings)
            {
                SpriteRenderer renderer = binding.Renderer;
                if (renderer == null || binding.Mesh == null || renderer.sprite != binding.Mesh.Runtime)
                    continue;
                Matrix4x4 toVisual = visualRoot.worldToLocalMatrix * renderer.localToWorldMatrix;
                Matrix4x4 toRenderer = renderer.worldToLocalMatrix * visualRoot.localToWorldMatrix;
                renderer.GetPropertyBlock(binding.Properties);
                binding.Properties.SetMatrix(RendererToVisualId, toVisual);
                binding.Properties.SetMatrix(VisualToRendererId, toRenderer);
                binding.Properties.SetVector(HeadTailId, anchors);
                binding.Properties.SetFloat(BendId, radians);
                renderer.SetPropertyBlock(binding.Properties);

                // Straight movement is the common case. Match the shader's
                // bypass without evaluating hundreds of trigonometric bends.
                if (Mathf.Abs(radians) <= 0.000001f)
                {
                    Bounds straight = binding.Mesh.Runtime.bounds;
                    Vector3 center = straight.center;
                    if (renderer.flipX) center.x = -center.x;
                    if (renderer.flipY) center.y = -center.y;
                    straight.center = center;
                    straight.Expand(new Vector3(0.0002f, 0.0002f, 0.01f));
                    renderer.localBounds = straight;
                    continue;
                }

                // Bound the actual deformed triangles, including flipped sprites.
                // This also feeds PhotoSubject and scan queries, without changing
                // AnimalBody's independent circular collision shape.
                Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0f);
                Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0f);
                foreach (Vector2 vertex in binding.Mesh.Vertices)
                {
                    Vector3 local = new Vector3(renderer.flipX ? -vertex.x : vertex.x,
                        renderer.flipY ? -vertex.y : vertex.y, 0f);
                    Vector3 point = toVisual.MultiplyPoint3x4(local);
                    Vector2 bent = BendPoint(point, headAnchor, tailAnchor, radians);
                    point.x = bent.x;
                    point.y = bent.y;
                    point = toRenderer.MultiplyPoint3x4(point);
                    point.z = local.z;
                    minimum = Vector3.Min(minimum, point);
                    maximum = Vector3.Max(maximum, point);
                }
                Bounds bounds = new Bounds();
                bounds.SetMinMax(minimum, maximum);
                // A small depth extent avoids zero-volume camera culling.
                bounds.Expand(new Vector3(0.0002f, 0.0002f, 0.01f));
                renderer.localBounds = bounds;
            }
        }

        private static Vector2 BendPoint(Vector2 point, Vector2 head, Vector2 tail, float radians)
        {
            Vector2 headToTail = head - tail;
            float length = Mathf.Max(headToTail.magnitude, 0.0001f);
            float t = Mathf.Clamp01(Vector2.Dot(head - point, headToTail / length) / length);
            float angle = radians * t * t * (3f - 2f * t);
            Vector2 offset = point - head;
            float sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle);
            return head + new Vector2(cosine * offset.x - sine * offset.y,
                sine * offset.x + cosine * offset.y);
        }

        private static SpriteMesh Acquire(Sprite source)
        {
            if (Meshes.TryGetValue(source, out SpriteMesh mesh) && mesh.Runtime != null)
            {
                mesh.Users++;
                return mesh;
            }
            // Give each cached mesh independent geometry while retaining the
            // source texture, rect, pivot, pixel density and UV mapping. Unity
            // requires OverrideGeometry to run inside the player loop.
            Sprite runtime = Sprite.Create(source.texture, source.rect,
                new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border, false);
            runtime.name = source.name + " (Turn Grid)";
            Vector2 size = source.rect.size;
            var pixels = new Vector2[(HorizontalCells + 1) * (VerticalCells + 1)];
            var triangles = new ushort[HorizontalCells * VerticalCells * 6];
            for (int y = 0; y <= VerticalCells; y++)
                for (int x = 0; x <= HorizontalCells; x++)
                    pixels[y * (HorizontalCells + 1) + x] = new Vector2(
                        size.x * x / HorizontalCells, size.y * y / VerticalCells);
            int triangle = 0;
            for (int y = 0; y < VerticalCells; y++)
                for (int x = 0; x < HorizontalCells; x++)
                {
                    ushort bottomLeft = (ushort)(y * (HorizontalCells + 1) + x);
                    ushort topLeft = (ushort)(bottomLeft + HorizontalCells + 1);
                    triangles[triangle++] = bottomLeft;
                    triangles[triangle++] = topLeft;
                    triangles[triangle++] = (ushort)(bottomLeft + 1);
                    triangles[triangle++] = (ushort)(bottomLeft + 1);
                    triangles[triangle++] = topLeft;
                    triangles[triangle++] = (ushort)(topLeft + 1);
                }
            runtime.OverrideGeometry(pixels, triangles);
            runtime.hideFlags = HideFlags.HideAndDontSave;
            mesh = new SpriteMesh { Source = source, Runtime = runtime, Vertices = runtime.vertices, Users = 1 };
            Meshes[source] = mesh;
            return mesh;
        }

        private static void Release(SpriteMesh mesh)
        {
            if (--mesh.Users > 0)
                return;
            if (Meshes.TryGetValue(mesh.Source, out SpriteMesh current) && ReferenceEquals(current, mesh))
                Meshes.Remove(mesh.Source);
            DestroyRuntimeSprite(mesh.Runtime);
        }

        private static void DestroyRuntimeSprite(Sprite sprite)
        {
            if (sprite == null) return;
            if (Application.isPlaying) Destroy(sprite);
            else DestroyImmediate(sprite);
        }

        private void RestoreBodies()
        {
            if (bindings == null)
                return;
            foreach (BodyBinding binding in bindings)
            {
                SpriteRenderer renderer = binding.Renderer;
                if (renderer != null)
                {
                    if (binding.Mesh != null && renderer.sprite == binding.Mesh.Runtime)
                        renderer.sprite = binding.Mesh.Source;
                    if (renderer.sharedMaterial == binding.AppliedMaterial)
                        renderer.sharedMaterial = binding.OriginalMaterial;
                    renderer.GetPropertyBlock(binding.Properties);
                    binding.Properties.SetFloat(BendId, 0f);
                    renderer.SetPropertyBlock(binding.Properties);
                    renderer.ResetLocalBounds();
                }
                if (binding.Mesh != null)
                    Release(binding.Mesh);
            }
            bindings = null;
        }

        private void OnDisable()
        {
            RestoreBodies();
            headingInitialized = false;
            bendDegrees = 0f;
            synchronizedFrame = -1;
        }
        private void OnDestroy() => RestoreBodies();

        private void OnValidate()
        {
            followTimeSeconds = Mathf.Max(0.001f, followTimeSeconds);
            maximumBendDegrees = Mathf.Clamp(maximumBendDegrees, 0f, 45f);
        }

        private void OnDrawGizmosSelected()
        {
            if (visualRoot == null) return;
            Gizmos.color = Color.cyan;
            Vector3 head = visualRoot.TransformPoint(headAnchor);
            Vector3 tail = visualRoot.TransformPoint(tailAnchor);
            Gizmos.DrawLine(head, tail);
            Gizmos.DrawWireSphere(head, 0.015f);
            Gizmos.DrawWireSphere(tail, 0.01f);
        }
    }
}

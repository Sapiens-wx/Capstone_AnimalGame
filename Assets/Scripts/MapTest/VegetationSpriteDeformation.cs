using System.Collections.Generic;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.MapTest
{
    // Subdivided visual-only geometry, following HeavyGarbageVisual's pull/neck deformation.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Vegetation))]
    public sealed class VegetationSpriteDeformation : WorldInteraction
    {
        [SerializeField] private SpriteRenderer[] deformedSprites;
        [SerializeField] private bool triggerOnImpact;
        [SerializeField, Min(.05f)] private float impactSeconds = .28f;
        [SerializeField, Range(0f, .2f)] private float impactPositionStrength = .035f;
        [SerializeField, Range(0f, 1f)] private float bendStrength = .275f;
        [SerializeField, Min(.05f)] private float recoverySeconds = .35f;
        [SerializeField, Range(4, 24)] private int subdivisions = 15;
        [SerializeField, Range(0f, .3f)] private float neckPinch = .08f;
        private readonly List<Layer> layers = new(3);
        private RenderResources rendering;
        private Vector2 pull, grip;
        private bool wasContact;
        private float impactElapsed;
        private Vector2 impactDirection;
        private Vegetation vegetation;

        public override WorldInteractionKind Kind => WorldInteractionKind.VegetationDeformation;

        protected override void OnEnable()
        {
            if (!Application.isPlaying) return;
            vegetation = GetComponent<Vegetation>();
            if (triggerOnImpact) vegetation.Deformation = this;
            impactElapsed = impactSeconds;
            base.OnEnable();
        }

        public override InteractionShape GetShape(MapTestSceneController map)
        {
            if (triggerOnImpact)
                return vegetation.GetShape(map);
            Vector2 units = InteractionShape.WorldUnitsPerQueryUnit(map);
            return InteractionShape.Circle(InteractionShape.ToQuery(transform.position, map),
                VisualRadius / Mathf.Min(units.x, units.y));
        }

        internal sealed class Driver
        {
            private const float SearchPaddingMeters = 1f;
            private readonly HeightMapPlayerSceneBootstrap world;
            private readonly RobotMarkerView marker;
            private readonly List<WorldInteraction> candidates = new(16);
            private readonly List<VegetationSpriteDeformation> active = new(8);
            private readonly RenderResources rendering;
            private Vector2 previousPosition;
            private bool hasPosition;
            private Vector2 searchCenter;
            private float cachedSearchRadius;
            private int searchVersion = -1;

            internal Driver(HeightMapPlayerSceneBootstrap world, RobotMarkerView marker)
            {
                this.world = world;
                this.marker = marker;
                rendering = new RenderResources(world.transform);
            }

            internal void PrepareRendering()
            {
                VegetationSpriteDeformation bush = null, tree = null;
                foreach (WorldInteraction interaction in WorldInteraction.Active)
                {
                    if (interaction is not VegetationSpriteDeformation plant || plant.gameObject.scene != world.gameObject.scene)
                        continue;
                    foreach (var source in plant.deformedSprites)
                        rendering.GetAsset(source, plant.subdivisions);
                    if (plant.triggerOnImpact) tree ??= plant;
                    else bush ??= plant;
                }

                // Exercise the two render paths during loading so the first player contact
                // can use the same renderer bindings and compiled code as later contacts.
                if (bush != null)
                {
                    StepContacts(bush.transform.position, Vector2.right, Vector2.zero, 1f / 60f);
                    StepContacts(bush.transform.position, Vector2.right, Vector2.zero, 1f / 60f);
                    StepContacts(bush.transform.position, Vector2.zero, Vector2.zero, 1f);
                    foreach (var plant in active)
                        plant.ResetPose();
                    active.Clear();
                    candidates.Clear();
                    searchVersion = -1;
                }
                if (tree != null)
                {
                    tree.rendering = rendering;
                    Vector2 position = (Vector2)tree.transform.position - Vector2.right;
                    tree.StepImpact(true, Vector2.right, position, 1f / 60f);
                    tree.StepImpact(true, Vector2.right, position, 1f / 60f);
                    tree.ResetPose();
                }
            }

            internal void Dispose()
            {
                foreach (var plant in active)
                    if (plant != null) plant.ReleaseLayers();
                rendering.Dispose();
            }

            internal void LateUpdate()
            {
                float dt = Time.deltaTime;
                if (dt <= 0f) return;

                var robot = world.robot;
                Vector2 position = robot.transform.position;
                Vector2 travel = hasPosition ? Vector2.ClampMagnitude((position - previousPosition) / (dt * .4f), 1f) : Vector2.zero;
                previousPosition = position;
                hasPosition = true;
                bool grounded = !robot.HasTowerSupport && (robot.CurrentClimbableSurface.IsActive
                    || world.heightMotion == null || !world.heightMotion.IsAirborne);
                if (!grounded) travel = Vector2.zero;
                Vector2 intent = grounded && !robot.IgnoreCollisionAndTerrain ? robot.UnresistedMovementIntentWorld : Vector2.zero;
                StepContacts(position, travel, intent, dt);
            }

            private void StepContacts(Vector2 position, Vector2 travel, Vector2 intent, float dt)
            {
                var robot = world.robot;
                bool moving = travel.sqrMagnitude > 0f;
                bool driving = intent.sqrMagnitude > 0f;
                if (!moving && !driving && active.Count == 0) return;

                float playerRadius = 0f, impactPadding = 0f;
                InteractionShape body = default;
                var map = world.map;
                bool checkContacts = (moving || driving) && map.HasGeneratedMap;
                if (checkContacts)
                {
                    Vector2 units = InteractionShape.WorldUnitsPerQueryUnit(map);
                    Vector2 point = (position - (Vector2)map.WorldBounds.min) / units;
                    float searchRadius = 0f;
                    if (moving)
                    {
                        playerRadius = marker.VisualBodyDiameter * Mathf.Abs(marker.MarkerVisualRoot.lossyScale.x) * .5f;
                        searchRadius = playerRadius / Mathf.Min(units.x, units.y);
                    }
                    if (driving)
                    {
                        body = robot.GetCollisionShape(map);
                        impactPadding = robot.CollisionSkin + .04f;
                        Rect bounds = WorldInteractionQuadTree.BoundsOf(body);
                        Vector2 extent = Vector2.Max(bounds.max - point, point - bounds.min);
                        searchRadius = Mathf.Max(searchRadius, extent.magnitude + impactPadding);
                    }
                    RefreshCandidates(map, point, searchRadius);
                }

                // Leaving the query still lets the last pose recover and resets impact latches.
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    var plant = active[i];
                    if (plant != null && plant.isActiveAndEnabled &&
                        ((checkContacts && candidates.Contains(plant)) || Step(plant, false))) continue;
                    active.RemoveAt(i);
                }
                if (!checkContacts) return;
                foreach (VegetationSpriteDeformation plant in candidates)
                {
                    bool keep = Step(plant, true);
                    int index = active.IndexOf(plant);
                    if (keep && index < 0) active.Add(plant);
                    if (!keep && index >= 0) active.RemoveAt(index);
                }

                bool Step(VegetationSpriteDeformation plant, bool nearby)
                {
                    plant.rendering = rendering;
                    if (!plant.triggerOnImpact)
                        return plant.StepPlayer(position, playerRadius, nearby ? travel : Vector2.zero, dt);
                    bool pressing = nearby && driving &&
                        Vector2.Dot(intent, (Vector2)plant.transform.position - position) > .001f &&
                        WorldInteractionQuery.Penetration(plant.vegetation.GetShape(map), body) >= -impactPadding;
                    return plant.StepImpact(pressing, intent, position, dt);
                }
            }

            private void RefreshCandidates(MapTestSceneController map, Vector2 point, float radius)
            {
                int version = WorldInteractionQuery.VegetationDeformationVersion;
                float remainingPadding = cachedSearchRadius - radius;
                if (version == searchVersion && remainingPadding >= 0f &&
                    (point - searchCenter).sqrMagnitude <= remainingPadding * remainingPadding) return;
                searchVersion = version;
                searchCenter = point;
                cachedSearchRadius = radius + SearchPaddingMeters;
                WorldInteractionQuery.Query(InteractionShape.Circle(point, cachedSearchRadius),
                    WorldInteractionKind.VegetationDeformation, map, world.gameObject.scene, candidates);
            }
        }

        private bool StepPlayer(Vector2 position, float playerRadius, Vector2 travel, float dt)
        {
            if (travel == Vector2.zero && pull == Vector2.zero) return false;
            float radius = VisualRadius;
            float contact = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(radius * .65f,
                radius + playerRadius, Vector2.Distance(position, transform.position)));
            Vector2 target = travel * (radius * bendStrength * contact);
            if (target == Vector2.zero && pull == Vector2.zero) return false;
            bool gripping = target.sqrMagnitude > .000001f;
            if (gripping) grip = position;
            float response = gripping ? .06f : recoverySeconds / 3f;
            pull = Vector2.Lerp(pull, target, 1f - Mathf.Exp(-dt / response));
            if (pull.sqrMagnitude < .0000001f) pull = Vector2.zero;
            UpdateLayers(radius, Vector2.zero);
            return pull != Vector2.zero;
        }

        private float VisualRadius
        {
            get
            {
                float radius = .01f;
                foreach (var source in deformedSprites)
                    radius = Mathf.Max(radius, source.bounds.extents.x, source.bounds.extents.y);
                return radius;
            }
        }

        private bool StepImpact(bool pressing, Vector2 intent, Vector2 playerPosition, float dt)
        {
            if (pressing && !wasContact)
            {
                impactDirection = intent.normalized;
                impactElapsed = 0;
                grip = playerPosition;
            }
            wasContact = pressing;
            if (impactElapsed >= impactSeconds && pull == Vector2.zero && layers.Count == 0) return pressing;
            float radius = VisualRadius;
            float t = Mathf.Clamp01(impactElapsed / impactSeconds);
            pull = impactDirection * (radius * bendStrength * Mathf.Sin(t * Mathf.PI) * Mathf.Exp(-2f * t));
            Vector2 offset = impactDirection * (radius * impactPositionStrength
                * Mathf.Sin(t * Mathf.PI * 5f) * Mathf.Exp(-4f * t));
            if (t >= 1f) { pull = Vector2.zero; offset = Vector2.zero; }
            UpdateLayers(radius, offset);
            impactElapsed = Mathf.Min(impactSeconds, impactElapsed + dt);
            return pressing || impactElapsed < impactSeconds || pull != Vector2.zero || layers.Count != 0;
        }

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int LocalToWorldId = Shader.PropertyToID("_VegetationLocalToWorld");
        private static readonly int CenterRadiusId = Shader.PropertyToID("_VegetationCenterRadius");
        private static readonly int DirectionPullId = Shader.PropertyToID("_VegetationDirectionPull");
        private static readonly int GripPinchOffsetId = Shader.PropertyToID("_VegetationGripPinchOffset");

        private void UpdateLayers(float radius, Vector2 offset)
        {
            if (pull == Vector2.zero && offset == Vector2.zero)
            {
                ReleaseLayers();
                return;
            }
            if (layers.Count == 0)
            {
                foreach (var source in deformedSprites)
                    if (source.enabled)
                        layers.Add(rendering.Rent(source, subdivisions));
            }

            Vector2 center = transform.position;
            Vector2 direction = pull.normalized;
            Vector2 across = new(-direction.y, direction.x);
            float gripAcross = Vector2.Dot(grip - center, across);
            float strength = pull.magnitude / radius;
            float paddingFactor = bendStrength * (1f + neckPinch) + (triggerOnImpact ? impactPositionStrength : 0f);
            foreach (var layer in layers)
            {
                Bounds bounds = layer.Source.bounds;
                float layerRadius = Mathf.Max(.001f, Mathf.Max(bounds.extents.x, bounds.extents.y));
                bounds.Expand(2f * layerRadius * paddingFactor);
                var properties = layer.Properties;
                properties.SetColor(ColorId, layer.Source.color);
                Matrix4x4 matrix = layer.Source.localToWorldMatrix;
                if (!layer.SourceMatrix.Equals(matrix))
                {
                    layer.SourceMatrix = matrix;
                    properties.SetMatrix(LocalToWorldId, matrix);
                }
                properties.SetVector(CenterRadiusId, new Vector4(center.x, center.y, layerRadius, 0f));
                properties.SetVector(DirectionPullId, new Vector4(direction.x, direction.y, layerRadius * strength, 0f));
                float side = Mathf.Clamp(gripAcross, -layerRadius * .65f, layerRadius * .65f);
                properties.SetVector(GripPinchOffsetId, new Vector4(side, 4f * neckPinch * strength, offset.x, offset.y));
                if (!layer.RenderBounds.Equals(bounds))
                {
                    layer.RenderBounds = bounds;
                    layer.Renderer.bounds = bounds;
                }
                int sortingLayer = layer.Source.sortingLayerID;
                int sortingOrder = layer.Source.sortingOrder;
                if (layer.SortingLayer != sortingLayer)
                {
                    layer.SortingLayer = sortingLayer;
                    layer.Renderer.sortingLayerID = sortingLayer;
                }
                if (layer.SortingOrder != sortingOrder)
                {
                    layer.SortingOrder = sortingOrder;
                    layer.Renderer.sortingOrder = sortingOrder;
                }
                layer.Renderer.SetPropertyBlock(properties);
            }
        }

        private sealed class Layer
        {
            internal readonly MeshFilter Filter;
            internal readonly MeshRenderer Renderer;
            internal readonly MaterialPropertyBlock Properties = new();
            internal SpriteRenderer Source;
            internal Mesh BoundMesh;
            internal Matrix4x4 SourceMatrix;
            internal Bounds RenderBounds;
            internal int SortingLayer, SortingOrder;

            internal Layer(Transform root)
            {
                var view = new GameObject("Vegetation deformation") { hideFlags = HideFlags.DontSave };
                view.transform.SetParent(root, false);
                Filter = view.AddComponent<MeshFilter>();
                Renderer = view.AddComponent<MeshRenderer>();
                Renderer.shadowCastingMode = ShadowCastingMode.Off;
                Renderer.receiveShadows = false;
                Renderer.lightProbeUsage = LightProbeUsage.Off;
                Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Renderer.enabled = false;
            }
        }

        private sealed class RenderResources
        {
            private readonly Transform root;
            private readonly Shader shader = Shader.Find("AnimalGame/Player UI Clipped Sprite");
            private readonly Dictionary<(Sprite, int, bool, bool), (Mesh mesh, Material material)> assets = new();
            private readonly Stack<Layer> inactive = new();

            internal RenderResources(Transform owner)
            {
                root = new GameObject("Vegetation deformation renderers") { hideFlags = HideFlags.DontSave }.transform;
                root.SetParent(owner, false);
                // A bush uses three sorted renderers, so this covers four simultaneous bushes.
                for (int i = 0; i < 12; i++)
                    inactive.Push(new Layer(root));
            }

            internal (Mesh mesh, Material material) GetAsset(SpriteRenderer source, int subdivisions)
            {
                Sprite sprite = source.sprite;
                var key = (sprite, subdivisions, source.flipX, source.flipY);
                if (assets.TryGetValue(key, out var asset)) return asset;

                var material = new Material(shader) { name = "Vegetation deformation", hideFlags = HideFlags.DontSave };
                material.EnableKeyword("VEGETATION_DEFORMATION");
                material.mainTexture = sprite.texture;
                if (sprite.associatedAlphaSplitTexture != null)
                {
                    material.SetTexture("_AlphaTex", sprite.associatedAlphaSplitTexture);
                    material.SetFloat("_EnableExternalAlpha", 1f);
                    material.EnableKeyword("ETC1_EXTERNAL_ALPHA");
                }
                asset = (BuildMesh(source, subdivisions), material);
                assets.Add(key, asset);
                return asset;
            }

            internal Layer Rent(SpriteRenderer source, int subdivisions)
            {
                var asset = GetAsset(source, subdivisions);
                var layer = inactive.Count > 0 ? inactive.Pop() : new Layer(root);
                layer.Source = source;
                layer.Renderer.gameObject.layer = source.gameObject.layer;
                if (layer.BoundMesh != asset.mesh)
                {
                    layer.BoundMesh = asset.mesh;
                    layer.Filter.sharedMesh = asset.mesh;
                    layer.Renderer.sharedMaterial = asset.material;
                }
                layer.Renderer.enabled = true;
                source.enabled = false;
                return layer;
            }

            internal void Return(Layer layer)
            {
                if (layer.Renderer != null) layer.Renderer.enabled = false;
                if (layer.Source != null) layer.Source.enabled = true;
                layer.Source = null;
                inactive.Push(layer);
            }

            internal void Dispose()
            {
                Release(root.gameObject);
                foreach (var asset in assets.Values)
                {
                    Release(asset.mesh);
                    Release(asset.material);
                }
            }
        }

        private static Mesh BuildMesh(SpriteRenderer source, int subdivisions)
        {
            var sprite = source.sprite;
            var points = sprite.vertices; var uv = sprite.uv; var indices = sprite.triangles;
            var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            var localPositions = new List<Vector2>();
            float extent = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            for (int t = 0; t < indices.Length; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                float edge = Mathf.Max((points[a] - points[b]).magnitude, (points[b] - points[c]).magnitude, (points[c] - points[a]).magnitude);
                int steps = Mathf.Clamp(Mathf.CeilToInt(edge / extent * subdivisions), 1, subdivisions);
                int[] rows = new int[steps + 1];
                for (int row = 0; row <= steps; row++)
                {
                    rows[row] = vertices.Count;
                    for (int col = 0; col <= steps - row; col++)
                    {
                        float wb = row / (float)steps, wc = col / (float)steps, wa = 1 - wb - wc;
                        Vector2 p = points[a] * wa + points[b] * wb + points[c] * wc;
                        if (source.flipX) p.x = -p.x;
                        if (source.flipY) p.y = -p.y;
                        vertices.Add(p); uvs.Add(uv[a] * wa + uv[b] * wb + uv[c] * wc);
                        localPositions.Add(p);
                    }
                }
                for (int row = 0; row < steps; row++)
                for (int col = 0; col < steps - row; col++)
                {
                    triangles.Add(rows[row] + col); triangles.Add(rows[row + 1] + col); triangles.Add(rows[row] + col + 1);
                    if (col == steps - row - 1) continue;
                    triangles.Add(rows[row] + col + 1); triangles.Add(rows[row + 1] + col); triangles.Add(rows[row + 1] + col + 1);
                }
            }
            var colors = new Color[vertices.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            var mesh = new Mesh { name = "Vegetation subdivided mesh", hideFlags = HideFlags.DontSave,
                vertices = vertices.ToArray(), uv = uvs.ToArray(), triangles = triangles.ToArray(), colors = colors };
            // The pooled renderer supplies each source's matrix. Keep sprite-local positions
            // in a UV channel so dynamic batching cannot transform them before the shader.
            mesh.SetUVs(1, localPositions);
            mesh.UploadMeshData(true);
            return mesh;
        }

        private void ReleaseLayers()
        {
            foreach (var layer in layers)
                rendering.Return(layer);
            layers.Clear();
        }

        protected override void OnDisable()
        {
            if (vegetation != null) vegetation.Deformation = null;
            base.OnDisable();
            ResetPose();
        }

        private void ResetPose()
        {
            ReleaseLayers();
            pull = Vector2.zero;
            wasContact = false;
            impactDirection = Vector2.zero;
            impactElapsed = impactSeconds;
        }

        private static void Release(Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}

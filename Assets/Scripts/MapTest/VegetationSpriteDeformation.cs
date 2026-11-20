using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.MapTest
{
    // Subdivided visual-only geometry, following HeavyGarbageVisual's pull/neck deformation.
    [DisallowMultipleComponent]
    public sealed class VegetationSpriteDeformation : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer[] deformedSprites;
        [SerializeField] private bool triggerOnImpact;
        [SerializeField, Min(.05f)] private float impactSeconds = .28f;
        [SerializeField, Range(0f, .2f)] private float impactPositionStrength = .035f;
        [SerializeField, Range(0f, 1f)] private float bendStrength = .275f;
        [SerializeField, Min(.05f)] private float recoverySeconds = .35f;
        [SerializeField, Range(4, 24)] private int subdivisions = 15;
        [SerializeField, Range(0f, .3f)] private float neckPinch = .08f;
        private sealed class Layer
        {
            public SpriteRenderer Source;
            public bool WasEnabled;
            public GameObject Object;
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Material Material;
            public Vector3[] Original, Deformed;
        }
        private readonly List<Layer> layers = new();
        private Vector2 previousPlayerPosition, pull, grip;
        private bool hasPlayerPosition;
        private bool wasContact;
        private float impactElapsed;
        private Vector2 impactDirection;

        private void LateUpdate()
        {
            var world = HeightMapPlayerSceneBootstrap.inst;
            if (world == null || world.robot == null || world.gameObject.scene != gameObject.scene) return;
            bool grounded = !world.robot.HasTowerSupport && (world.robot.CurrentClimbableSurface.IsActive
                || world.heightMotion == null || !world.heightMotion.IsAirborne);
            if (triggerOnImpact)
            {
                var map = GetComponent<HeightMapPlacedObject>().Map;
                if (map == null || !map.HasGeneratedMap) return;
                bool contact = grounded && !world.robot.IgnoreCollisionAndTerrain
                    && WorldInteractionQuery.Penetration(GetComponent<Vegetation>().GetShape(map),
                        world.robot.GetCollisionShape(map)) >= -(world.robot.CollisionSkin + .04f);
                StepImpact(contact, world.robot.UnresistedMovementIntentWorld,
                    world.robot.transform.position, Time.deltaTime);
                return;
            }
            var marker = world.robot.GetComponent<AnimalGame.RobotMap.RobotMarkerView>();
            float radius = marker.VisualBodyDiameter * Mathf.Abs(marker.MarkerVisualRoot.lossyScale.x) * .5f;
            StepPlayer(world.robot.transform.position, radius, grounded, Time.deltaTime);
        }

        private void StepPlayer(Vector2 position, float playerRadius, bool grounded, float dt)
        {
            if (!hasPlayerPosition) { previousPlayerPosition = position; hasPlayerPosition = true; }
            if (dt <= 0) return;
            Vector2 velocity = (position - previousPlayerPosition) / dt;
            previousPlayerPosition = position;
            float radius = VisualRadius;
            float contact = grounded ? 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(radius * .65f,
                radius + playerRadius, Vector2.Distance(position, transform.position))) : 0;
            Vector2 target = Vector2.ClampMagnitude(velocity / .4f, 1f) * (radius * bendStrength * contact);
            if (target.sqrMagnitude > .000001f) grip = position;
            float response = target.sqrMagnitude > .000001f ? .06f : recoverySeconds / 3f;
            Vector2 previousPull = pull;
            pull = Vector2.Lerp(pull, target, 1f - Mathf.Exp(-dt / response));
            if (pull.sqrMagnitude < .0000001f) pull = Vector2.zero;
            UpdateLayers(radius, Vector2.zero, pull != Vector2.zero || previousPull != Vector2.zero);
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

        private void StepImpact(bool contact, Vector2 intent, Vector2 playerPosition, float dt)
        {
            if (dt <= 0) return;
            // Use driving intent: an actual solid collision may already have stopped the body.
            bool pressing = contact && Vector2.Dot(intent, (Vector2)transform.position - playerPosition) > .001f;
            if (pressing && !wasContact)
            {
                impactDirection = intent.normalized;
                impactElapsed = 0;
                grip = playerPosition;
            }
            wasContact = pressing;
            bool wasDeformed = pull != Vector2.zero;
            float radius = VisualRadius;
            float t = Mathf.Clamp01(impactElapsed / impactSeconds);
            pull = impactDirection * (radius * bendStrength * Mathf.Sin(t * Mathf.PI) * Mathf.Exp(-2f * t));
            Vector2 offset = impactDirection * (radius * impactPositionStrength
                * Mathf.Sin(t * Mathf.PI * 5f) * Mathf.Exp(-4f * t));
            if (t >= 1f) { pull = Vector2.zero; offset = Vector2.zero; }
            UpdateLayers(radius, offset, pull != Vector2.zero || wasDeformed);
            impactElapsed = Mathf.Min(impactSeconds, impactElapsed + dt);
        }

        private void UpdateLayers(float radius, Vector2 offset, bool updateGeometry)
        {
            if (layers.Count == 0)
            {
                if (pull == Vector2.zero) return;
                CreateLayers();
            }
            foreach (var layer in layers)
            {
                bool showMesh = !triggerOnImpact || pull != Vector2.zero || offset != Vector2.zero;
                layer.Source.enabled = layer.WasEnabled && !showMesh;
                layer.Renderer.enabled = layer.WasEnabled && showMesh;
                layer.Material.SetColor("_Color", layer.Source.color);
                layer.Renderer.sortingLayerID = layer.Source.sortingLayerID;
                layer.Renderer.sortingOrder = layer.Source.sortingOrder;
                layer.Object.transform.localPosition = layer.Source.transform.InverseTransformVector(offset);
                if (updateGeometry) Deform(layer, radius);
            }
        }

        private void CreateLayers()
        {
            foreach (var source in deformedSprites)
            {
                var layer = new Layer { Source = source, WasEnabled = source.enabled };
                layer.Mesh = BuildMesh(source, out layer.Original);
                layer.Deformed = new Vector3[layer.Original.Length];
                layer.Material = new Material(Shader.Find("AnimalGame/Player UI Clipped Sprite"))
                    { name = "Vegetation deformation (runtime)", hideFlags = HideFlags.DontSave };
                layer.Material.mainTexture = source.sprite.texture;
                if (source.sprite.associatedAlphaSplitTexture != null)
                {
                    layer.Material.SetTexture("_AlphaTex", source.sprite.associatedAlphaSplitTexture);
                    layer.Material.SetFloat("_EnableExternalAlpha", 1);
                    layer.Material.EnableKeyword("ETC1_EXTERNAL_ALPHA");
                }
                layer.Object = new GameObject("Vegetation deformation") { layer = source.gameObject.layer, hideFlags = HideFlags.DontSave };
                layer.Object.transform.SetParent(source.transform, false);
                layer.Object.AddComponent<MeshFilter>().sharedMesh = layer.Mesh;
                layer.Renderer = layer.Object.AddComponent<MeshRenderer>();
                layer.Renderer.sharedMaterial = layer.Material;
                layer.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                layer.Renderer.receiveShadows = false;
                layer.Renderer.lightProbeUsage = LightProbeUsage.Off;
                layer.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                layer.Renderer.enabled = layer.WasEnabled;
                source.enabled = false;
                layers.Add(layer);
            }
        }

        private Mesh BuildMesh(SpriteRenderer source, out Vector3[] original)
        {
            var sprite = source.sprite;
            var points = sprite.vertices; var uv = sprite.uv; var indices = sprite.triangles;
            var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
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
            original = vertices.ToArray();
            var colors = new Color[original.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            var mesh = new Mesh { name = "Vegetation subdivided mesh", hideFlags = HideFlags.DontSave,
                vertices = original, uv = uvs.ToArray(), triangles = triangles.ToArray(), colors = colors };
            mesh.MarkDynamic(); mesh.RecalculateBounds();
            return mesh;
        }

        private void Deform(Layer layer, float radius)
        {
            // Normalize each layer separately so the small center artwork is not entirely
            // swallowed by the canopy-sized fixed-root region.
            float layerRadius = Mathf.Max(.001f, Mathf.Max(layer.Source.bounds.extents.x, layer.Source.bounds.extents.y));
            Vector2 layerPull = pull * (layerRadius / radius);
            Vector2 direction = pull.sqrMagnitude > 0 ? pull.normalized : Vector2.right;
            Vector2 across = new Vector2(-direction.y, direction.x);
            Vector2 center = transform.position;
            float gripAcross = Mathf.Clamp(Vector2.Dot(grip - center, across), -layerRadius * .65f, layerRadius * .65f);
            for (int i = 0; i < layer.Original.Length; i++)
            {
                Vector2 relative = (Vector2)layer.Source.transform.TransformPoint(layer.Original[i]) - center;
                float along = Vector2.Dot(relative, direction), side = Vector2.Dot(relative, across) - gripAcross;
                float front = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(layerRadius * .05f, layerRadius * .85f, along));
                float width = 1f - Mathf.SmoothStep(0, 1, Mathf.Abs(side) / (layerRadius * .9f));
                Vector2 displacement = layerPull * (front * width)
                    - across * (side * front * (1 - front) * 4 * width * neckPinch * layerPull.magnitude / layerRadius);
                layer.Deformed[i] = layer.Original[i] + layer.Source.transform.InverseTransformVector(displacement);
            }
            layer.Mesh.vertices = layer.Deformed;
            layer.Mesh.RecalculateBounds();
        }

        private void OnDisable()
        {
            foreach (var layer in layers)
            {
                if (layer.Source != null) layer.Source.enabled = layer.WasEnabled;
                layer.Object.SetActive(false);
                Release(layer.Object); Release(layer.Mesh); Release(layer.Material);
            }
            layers.Clear(); pull = Vector2.zero; hasPlayerPosition = false;
            wasContact = false; impactDirection = Vector2.zero; impactElapsed = impactSeconds;
        }
        private static void Release(Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}

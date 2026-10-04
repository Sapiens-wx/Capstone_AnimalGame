using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.Garbage
{
    // The replacement mesh is visual only. WorldInteraction keeps using the unchanged
    // source SpriteRenderer and its original bounds throughout the pull.
    [DisallowMultipleComponent]
    public sealed class HeavyGarbageVisual : MonoBehaviour
    {
        [SerializeField, Range(4, 32)] private int subdivisions = 15;
        [SerializeField, Range(.1f, 1f)] private float affectedWidthRatio = .72f;
        [SerializeField, Range(0f, .3f)] private float neckPinch = .08f;

        private SpriteRenderer sourceRenderer;
        private SpriteRenderer[] originalRenderers;
        private Color[] originalColors;
        private bool sourceWasEnabled;
        private GameObject visualObject;
        private MeshRenderer visualRenderer;
        private Mesh deformationMesh;
        private Material visualMaterial;
        private Vector3[] baseVertices;
        private Vector3[] deformedVertices;
        private Vector3 localCenter;
        private bool initialized;

        public Mesh DeformationMesh => deformationMesh;
        public SpriteRenderer SourceRenderer => sourceRenderer;
        public float CurrentOpacity { get; private set; } = 1f;
        public float CurrentStretchWorld { get; private set; }

        public void SetPull(Vector2 gripWorld, Vector2 pullDirectionWorld,
            float stretchWorld, float opacity)
        {
            if (!isActiveAndEnabled) return;
            EnsureInitialized();
            CurrentOpacity = Mathf.Clamp01(opacity);
            CurrentStretchWorld = Mathf.Max(0f, stretchWorld);
            if (originalRenderers != null)
            {
                for (int i = 0; i < originalRenderers.Length; i++)
                {
                    if (originalRenderers[i] == null) continue;
                    Color color = originalColors[i];
                    color.a *= CurrentOpacity;
                    originalRenderers[i].color = color;
                }
            }
            if (visualRenderer == null || sourceRenderer == null) return;

            sourceRenderer.enabled = false;
            visualRenderer.enabled = sourceWasEnabled;
            visualRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            visualRenderer.sortingOrder = sourceRenderer.sortingOrder;
            Color bodyColor = sourceRenderer.color;
            visualMaterial.SetColor("_Color", bodyColor);
            Deform(gripWorld, pullDirectionWorld, CurrentStretchWorld);
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            WorldInteraction interaction = GetComponent<WorldInteraction>();
            sourceRenderer = interaction != null ? interaction.SpriteSource : null;
            if (sourceRenderer == null) sourceRenderer = GetComponent<SpriteRenderer>();

            // Snapshot once, before fragment previews are created. Nested garbage
            // sprites belong to their own item and must never become source icons.
            List<SpriteRenderer> originals = new();
            foreach (SpriteRenderer candidate in GetComponentsInChildren<SpriteRenderer>(true))
            {
                WorldInteraction owner = candidate.GetComponentInParent<WorldInteraction>();
                if (owner != null && owner != interaction) continue;
                originals.Add(candidate);
            }
            if (sourceRenderer != null && !originals.Contains(sourceRenderer))
                originals.Add(sourceRenderer);
            originalRenderers = originals.ToArray();
            originalColors = new Color[originalRenderers.Length];
            for (int i = 0; i < originalRenderers.Length; i++)
                originalColors[i] = originalRenderers[i].color;

            if (sourceRenderer != null) sourceWasEnabled = sourceRenderer.enabled;
            if (sourceRenderer == null || sourceRenderer.sprite == null) return;

            // Sprites/Default is already included by this project's GraphicsSettings.
            // It also renders MeshRenderer through URP's unlit pass, without relying
            // on SpriteRenderer-only per-draw flip/color state in the URP 2D shader.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported) return;
            visualMaterial = new Material(shader)
            {
                name = "Heavy garbage deformation (runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            Sprite sprite = sourceRenderer.sprite;
            visualMaterial.mainTexture = sprite.texture;
            visualMaterial.SetColor("_Color", Color.white);
            if (visualMaterial.HasProperty("_RendererColor"))
                visualMaterial.SetColor("_RendererColor", Color.white);
            if (visualMaterial.HasProperty("_Flip"))
                visualMaterial.SetVector("_Flip", Vector4.one);
            if (sprite.associatedAlphaSplitTexture != null)
            {
                visualMaterial.SetTexture("_AlphaTex", sprite.associatedAlphaSplitTexture);
                visualMaterial.SetFloat("_EnableExternalAlpha", 1f);
                visualMaterial.EnableKeyword("ETC1_EXTERNAL_ALPHA");
            }

            BuildMesh(sprite);
            visualObject = new GameObject("Heavy garbage deformation")
            {
                hideFlags = HideFlags.DontSave,
                layer = sourceRenderer.gameObject.layer
            };
            visualObject.transform.SetParent(sourceRenderer.transform, false);
            visualObject.AddComponent<MeshFilter>().sharedMesh = deformationMesh;
            visualRenderer = visualObject.AddComponent<MeshRenderer>();
            visualRenderer.sharedMaterial = visualMaterial;
            visualRenderer.shadowCastingMode = ShadowCastingMode.Off;
            visualRenderer.receiveShadows = false;
            visualRenderer.lightProbeUsage = LightProbeUsage.Off;
            visualRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            visualRenderer.enabled = false;
        }

        private void BuildMesh(Sprite sprite)
        {
            Vector2[] spriteVertices = sprite.vertices;
            Vector2[] spriteUvs = sprite.uv;
            ushort[] spriteTriangles = sprite.triangles;
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            float longestExtent = Mathf.Max(.001f,
                Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y));
            int resolution = Mathf.Clamp(subdivisions, 4, 32);

            // Subdivide the sprite's existing triangles instead of sampling a full
            // rect: this preserves tight atlas boundaries, rotated packing, rect,
            // and pivot without reading texture pixels or changing the sprite asset.
            for (int triangle = 0; triangle < spriteTriangles.Length; triangle += 3)
            {
                int a = spriteTriangles[triangle];
                int b = spriteTriangles[triangle + 1];
                int c = spriteTriangles[triangle + 2];
                float longestEdge = Mathf.Max((spriteVertices[b] - spriteVertices[a]).magnitude,
                    Mathf.Max((spriteVertices[c] - spriteVertices[a]).magnitude,
                        (spriteVertices[c] - spriteVertices[b]).magnitude));
                int steps = Mathf.Clamp(Mathf.CeilToInt(longestEdge / longestExtent * resolution),
                    1, resolution);
                int[] rows = new int[steps + 1];
                for (int row = 0; row <= steps; row++)
                {
                    rows[row] = vertices.Count;
                    for (int column = 0; column <= steps - row; column++)
                    {
                        float bWeight = row / (float)steps;
                        float cWeight = column / (float)steps;
                        float aWeight = 1f - bWeight - cWeight;
                        Vector2 position = spriteVertices[a] * aWeight
                            + spriteVertices[b] * bWeight + spriteVertices[c] * cWeight;
                        if (sourceRenderer.flipX) position.x = -position.x;
                        if (sourceRenderer.flipY) position.y = -position.y;
                        vertices.Add(position);
                        uvs.Add(spriteUvs[a] * aWeight + spriteUvs[b] * bWeight
                            + spriteUvs[c] * cWeight);
                    }
                }
                for (int row = 0; row < steps; row++)
                {
                    for (int column = 0; column < steps - row; column++)
                    {
                        triangles.Add(rows[row] + column);
                        triangles.Add(rows[row + 1] + column);
                        triangles.Add(rows[row] + column + 1);
                        if (column >= steps - row - 1) continue;
                        triangles.Add(rows[row] + column + 1);
                        triangles.Add(rows[row + 1] + column);
                        triangles.Add(rows[row + 1] + column + 1);
                    }
                }
            }

            baseVertices = vertices.ToArray();
            deformedVertices = new Vector3[baseVertices.Length];
            Color[] colors = new Color[baseVertices.Length];
            Vector3[] normals = new Vector3[baseVertices.Length];
            for (int i = 0; i < baseVertices.Length; i++)
            {
                colors[i] = Color.white;
                normals[i] = Vector3.back;
            }
            localCenter = sprite.bounds.center;
            if (sourceRenderer.flipX) localCenter.x = -localCenter.x;
            if (sourceRenderer.flipY) localCenter.y = -localCenter.y;
            deformationMesh = new Mesh
            {
                name = "Heavy garbage subdivided sprite (runtime)",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = baseVertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = baseVertices,
                uv = uvs.ToArray(),
                colors = colors,
                normals = normals,
                triangles = triangles.ToArray()
            };
            deformationMesh.MarkDynamic();
            deformationMesh.RecalculateBounds();
        }

        private void Deform(Vector2 gripWorld, Vector2 pullDirectionWorld, float stretch)
        {
            Transform visualTransform = sourceRenderer.transform;
            Vector2 center = visualTransform.TransformPoint(localCenter);
            Vector2 direction = pullDirectionWorld;
            if (direction.sqrMagnitude < .000001f) direction = gripWorld - center;
            if (direction.sqrMagnitude < .000001f) direction = Vector2.up;
            direction.Normalize();
            Vector2 across = new Vector2(-direction.y, direction.x);
            float halfLength = .001f;
            float halfWidth = .001f;
            for (int i = 0; i < baseVertices.Length; i++)
            {
                Vector2 relative = (Vector2)visualTransform.TransformPoint(baseVertices[i]) - center;
                halfLength = Mathf.Max(halfLength, Mathf.Abs(Vector2.Dot(relative, direction)));
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector2.Dot(relative, across)));
            }
            Vector2 gripRelative = gripWorld - center;
            float gripAcross = Mathf.Clamp(Vector2.Dot(gripRelative, across),
                -halfWidth * .85f, halfWidth * .85f);
            float tipDistance = Mathf.Clamp(Vector2.Dot(gripRelative, direction),
                halfLength * .35f, halfLength);
            float stretchStart = halfLength * .05f;
            float width = Mathf.Max(.001f, halfWidth * affectedWidthRatio);
            float strain = Mathf.Clamp01(stretch / halfLength);
            for (int i = 0; i < baseVertices.Length; i++)
            {
                Vector2 relative = (Vector2)visualTransform.TransformPoint(baseVertices[i]) - center;
                float along = Vector2.Dot(relative, direction);
                float sideDistance = Vector2.Dot(relative, across) - gripAcross;
                float frontWeight = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(stretchStart, tipDistance, along));
                float widthWeight = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(Mathf.Abs(sideDistance) / width));
                float influence = frontWeight * widthWeight;
                float neckWeight = frontWeight * (1f - frontWeight) * 4f * widthWeight;
                Vector2 offset = direction * (stretch * influence)
                    - across * (sideDistance * neckWeight * neckPinch * strain);
                deformedVertices[i] = baseVertices[i]
                    + visualTransform.InverseTransformVector((Vector3)offset);
            }
            deformationMesh.vertices = deformedVertices;
            deformationMesh.RecalculateBounds();
        }

        public void ResetVisual()
        {
            if (originalRenderers != null)
                for (int i = 0; i < originalRenderers.Length; i++)
                    if (originalRenderers[i] != null) originalRenderers[i].color = originalColors[i];
            if (sourceRenderer != null && initialized) sourceRenderer.enabled = sourceWasEnabled;
            if (visualObject != null) visualObject.SetActive(false);
            DestroyRuntime(visualObject);
            DestroyRuntime(deformationMesh);
            DestroyRuntime(visualMaterial);
            visualObject = null;
            visualRenderer = null;
            deformationMesh = null;
            visualMaterial = null;
            baseVertices = null;
            deformedVertices = null;
            originalRenderers = null;
            originalColors = null;
            sourceRenderer = null;
            initialized = false;
            CurrentOpacity = 1f;
            CurrentStretchWorld = 0f;
        }

        private static void DestroyRuntime(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private void OnDisable() => ResetVisual();
        private void OnDestroy() => ResetVisual();
    }
}

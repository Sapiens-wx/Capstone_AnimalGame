using System;
using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.RobotArm
{
    /// <summary>
    /// Hides only the portion of a held item that has crossed the recycling inlet.
    /// Each sprite receives its own temporary material; the asset, sprite tint,
    /// transforms, sorting and existing property blocks remain untouched.
    /// </summary>
    public sealed class MediumRecycleInletClip : IDisposable
    {
        public const string ShaderName = "AnimalGame/Medium Recycle Inlet";
        // Draw after normal world sprites (3000), but before the robot's body
        // and arms (3500), so the working claws remain visible during feeding.
        public const int RecycleRenderQueue = 3499;

        private static readonly int InletPlaneId = Shader.PropertyToID("_RecycleInletPlane");
        private static readonly int ClipEnabledId = Shader.PropertyToID("_RecycleInletClipEnabled");
        private static readonly int VisualOffsetId = Shader.PropertyToID("_RecycleVisualOffset");
        private readonly WorldInteraction item;
        private readonly Shader inletShader;
        private readonly List<SpriteRenderer> sprites = new();
        private readonly List<RendererState> states = new();
        private bool disposed;

        public Vector3 VisualOffsetWorld { get; private set; }

        private sealed class RendererState
        {
            public SpriteRenderer Renderer;
            public Material[] Originals;
            public Material[] Runtime;
        }

        public MediumRecycleInletClip(WorldInteraction item, Shader shader = null)
        {
            this.item = item;
            inletShader = shader;
            if (item == null) return;
            foreach (SpriteRenderer candidate in item.GetComponentsInChildren<SpriteRenderer>(true))
            {
                // Fragment previews or nested props belong to their own item.
                WorldInteraction owner = candidate.GetComponentInParent<WorldInteraction>();
                if (owner != null && owner != item) continue;
                sprites.Add(candidate);
            }
            if (item.SpriteSource != null && !sprites.Contains(item.SpriteSource))
                sprites.Add(item.SpriteSource);
        }

        public bool Begin(Transform robotFrame, float inletLocalY)
        {
            if (disposed || item == null || robotFrame == null) return false;
            SetVisualOffset(robotFrame, Vector2.zero);
            if (states.Count > 0)
            {
                Update(robotFrame, inletLocalY);
                return true;
            }

            bool hasSprite = false;
            foreach (SpriteRenderer sprite in sprites)
                if (sprite != null && sprite.sprite != null) { hasSprite = true; break; }
            // Logical items used by tests or nonvisual worlds need no clipping.
            if (!hasSprite) return true;
            Shader shader = inletShader != null ? inletShader : Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported) return false;
            foreach (SpriteRenderer sprite in sprites)
            {
                if (sprite == null || sprite.sprite == null) continue;
                Material[] originals = sprite.sharedMaterials;
                Material[] runtime = new Material[Mathf.Max(1, originals.Length)];
                for (int i = 0; i < runtime.Length; i++)
                {
                    Material original = i < originals.Length ? originals[i] : null;
                    Material material = new Material(shader)
                    {
                        name = "Medium recycle inlet (runtime)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    if (original != null) material.CopyPropertiesFromMaterial(original);
                    material.renderQueue = RecycleRenderQueue;
                    material.enableInstancing = false;
                    material.SetVector(VisualOffsetId, Vector4.zero);
                    // SpriteRenderer normally supplies the atlas texture per draw.
                    // These fallbacks also preserve external-alpha sprite atlases.
                    material.SetTexture("_MainTex", sprite.sprite.texture);
                    Texture2D splitAlpha = sprite.sprite.associatedAlphaSplitTexture;
                    if (splitAlpha != null)
                    {
                        material.SetTexture("_AlphaTex", splitAlpha);
                        material.SetFloat("_EnableExternalAlpha", 1f);
                        material.EnableKeyword("ETC1_EXTERNAL_ALPHA");
                    }
                    runtime[i] = material;
                }
                states.Add(new RendererState { Renderer = sprite, Originals = originals, Runtime = runtime });
                sprite.sharedMaterials = runtime;
            }
            Update(robotFrame, inletLocalY);
            return states.Count > 0;
        }

        /// <summary>
        /// Applies one rigid visual-only offset to the body and its icon. Logical
        /// geometry and sprite transforms stay stable while the inlet clips the
        /// displaced pixels, including during a rotated or scaled robot pose.
        /// </summary>
        public void SetVisualOffset(Transform robotFrame, Vector2 localOffset)
        {
            if (disposed) return;
            VisualOffsetWorld = robotFrame != null
                ? robotFrame.TransformVector((Vector3)localOffset) : Vector3.zero;
            foreach (RendererState state in states)
                foreach (Material material in state.Runtime)
                    if (material != null) material.SetVector(VisualOffsetId, (Vector4)VisualOffsetWorld);
        }

        public void Update(Transform robotFrame, float inletLocalY)
        {
            if (disposed || robotFrame == null) return;
            // Transform the local plane with the inverse transpose. This also
            // handles scaled and rotated robot parents without changing depth.
            Vector4 plane = robotFrame.worldToLocalMatrix.transpose
                * new Vector4(0f, 1f, 0f, -inletLocalY);
            float normalLength = new Vector3(plane.x, plane.y, plane.z).magnitude;
            if (normalLength > .000001f) plane /= normalLength;
            foreach (RendererState state in states)
                foreach (Material material in state.Runtime)
                {
                    if (material == null) continue;
                    material.SetVector(InletPlaneId, plane);
                    material.SetFloat(ClipEnabledId, normalLength > .000001f ? 1f : 0f);
                }
        }

        public float MinimumLocalY(Transform robotFrame) => LocalYBounds(robotFrame, true);
        public float MaximumLocalY(Transform robotFrame) => LocalYBounds(robotFrame, false);

        private float LocalYBounds(Transform robotFrame, bool minimum)
        {
            if (robotFrame == null || item == null) return 0f;
            float result = minimum ? float.PositiveInfinity : float.NegativeInfinity;
            foreach (SpriteRenderer sprite in sprites)
            {
                if (sprite == null || sprite.sprite == null) continue;
                if (sprite.drawMode == SpriteDrawMode.Simple)
                {
                    foreach (Vector2 vertex in sprite.sprite.vertices)
                    {
                        Vector3 local = new Vector3(sprite.flipX ? -vertex.x : vertex.x,
                            sprite.flipY ? -vertex.y : vertex.y, 0f);
                        Accumulate(sprite.transform.TransformPoint(local));
                    }
                }
                else
                {
                    // Sliced/tiled sprites use generated geometry, whose public
                    // world bounds provide a conservative completion distance.
                    Bounds bounds = sprite.bounds;
                    Accumulate(new Vector3(bounds.min.x, bounds.min.y, bounds.center.z));
                    Accumulate(new Vector3(bounds.max.x, bounds.min.y, bounds.center.z));
                    Accumulate(new Vector3(bounds.min.x, bounds.max.y, bounds.center.z));
                    Accumulate(new Vector3(bounds.max.x, bounds.max.y, bounds.center.z));
                }
            }
            return float.IsInfinity(result)
                ? robotFrame.InverseTransformPoint(item.transform.position).y : result;

            void Accumulate(Vector3 world)
            {
                float y = robotFrame.InverseTransformPoint(world).y;
                result = minimum ? Mathf.Min(result, y) : Mathf.Max(result, y);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            SetVisualOffset(null, Vector2.zero);
            disposed = true;
            foreach (RendererState state in states)
            {
                if (state.Renderer != null) state.Renderer.sharedMaterials = state.Originals;
                // No property block was replaced or modified, so overrides made
                // by gameplay while recycling survive cancellation and pooling.
                foreach (Material material in state.Runtime)
                {
                    if (material == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                    else UnityEngine.Object.DestroyImmediate(material);
                }
            }
            states.Clear();
        }
    }
}

using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Placeholder View")]
    public sealed class AnimalPlaceholderView : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer bodyFill;
        [SerializeField] private SpriteRenderer bodyOutline;
        [SerializeField] private AnimalDiscoveryVisual discoveryVisual;

        private Vector3 baseVisualScale = Vector3.one;
        private Color baseBodyFillColor = Color.black;
        private Color baseBodyOutlineColor = Color.white;

        private void Awake()
        {
            CacheBaseAppearance();
        }

        public void ConfigureEditorReferences(
            Transform root,
            SpriteRenderer fill,
            SpriteRenderer outline)
        {
            visualRoot = root;
            bodyFill = fill;
            bodyOutline = outline;
            CacheBaseAppearance();
        }

        public void SetSubmergeProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            float visibility = 1f - progress;
            if (visualRoot != null)
            {
                visualRoot.localScale = Vector3.Lerp(
                    baseVisualScale,
                    baseVisualScale * 0.58f,
                    progress);
            }

            if (discoveryVisual != null)
            {
                discoveryVisual.SetExternalVisibility(visibility);
            }
            else
            {
                ApplyAlpha(bodyFill, baseBodyFillColor, visibility);
                ApplyAlpha(bodyOutline, baseBodyOutlineColor, visibility);
            }
        }

        public void RestoreVisibleAppearance()
        {
            SetSubmergeProgress(0f);
        }

#if UNITY_EDITOR
        public void ConfigureDiscoveryVisual(AnimalDiscoveryVisual visual)
        {
            discoveryVisual = visual;
        }
#endif

        private void CacheBaseAppearance()
        {
            if (visualRoot != null)
                baseVisualScale = visualRoot.localScale;
            if (bodyFill != null)
                baseBodyFillColor = bodyFill.color;
            if (bodyOutline != null)
                baseBodyOutlineColor = bodyOutline.color;
        }

        public void ScaleVisuals(float factor)
        {
            baseVisualScale *= Mathf.Max(.01f, factor);
            if (visualRoot != null) visualRoot.localScale = baseVisualScale;
        }

        private static void ApplyAlpha(
            SpriteRenderer renderer,
            Color baseColour,
            float visibility)
        {
            if (renderer == null)
                return;

            Color colour = baseColour;
            colour.a *= visibility;
            renderer.color = colour;
            renderer.enabled = colour.a > 0.001f;
        }
    }
}

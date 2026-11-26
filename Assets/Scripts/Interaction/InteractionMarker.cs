using UnityEngine;

namespace AnimalGame.Interaction
{
    /// <summary>Reusable presentation only. The owner controls suppression; the shader owns view-circle clipping.</summary>
    [DisallowMultipleComponent]
    public sealed class InteractionMarker : MonoBehaviour
    {
        [SerializeField] private MeshRenderer outerRing;
        [SerializeField] private MeshRenderer innerRing;
        [SerializeField] private Color tint = new Color(212f / 255f, 184f / 255f, 84f / 255f, 1f);
        [SerializeField, Min(.01f)] private float diameter = .75f;
        [SerializeField] private float outerDegreesPerSecond = -30f;
        [SerializeField] private float innerDegreesPerSecond = 45f;
        [SerializeField] private int sortingOrder = 25;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock properties;
        private bool suppressed;

        public bool IsSuppressed => suppressed;
        // This describes the owner's permission, not whether any pixels are inside the current view circle.
        public bool PresentationEnabled => isActiveAndEnabled && !suppressed;
        public MeshRenderer OuterRing => outerRing;
        public MeshRenderer InnerRing => innerRing;

        public void Configure(MeshRenderer outer, MeshRenderer inner)
        {
            outerRing = outer;
            innerRing = inner;
            ApplyAppearance();
            if (Application.isPlaying) SetVisible(PresentationEnabled);
        }

        public void SetSuppressed(bool value)
        {
            suppressed = value;
            SetVisible(PresentationEnabled);
        }

        private void OnEnable()
        {
            ApplyAppearance();
            SetVisible(PresentationEnabled);
        }

        private void LateUpdate()
        {
            if (!PresentationEnabled) return;
            float deltaTime = Time.deltaTime;
            if (outerRing != null)
                outerRing.transform.Rotate(0f, 0f, outerDegreesPerSecond * deltaTime, Space.Self);
            if (innerRing != null)
                innerRing.transform.Rotate(0f, 0f, innerDegreesPerSecond * deltaTime, Space.Self);
        }

        private void ApplyAppearance()
        {
            // Meshes and materials are shared assets. Only per-instance properties are changed here.
            if (properties == null) properties = new MaterialPropertyBlock();
            ApplyAppearance(outerRing);
            ApplyAppearance(innerRing);
        }

        private void ApplyAppearance(MeshRenderer ring)
        {
            if (ring == null) return;
            ring.transform.localScale = Vector3.one * diameter;
            ring.sortingOrder = sortingOrder;
            ring.GetPropertyBlock(properties);
            properties.SetColor(ColorProperty, tint);
            ring.SetPropertyBlock(properties);
        }

        private void SetVisible(bool visible)
        {
            if (outerRing != null) outerRing.enabled = visible;
            if (innerRing != null) innerRing.enabled = visible;
        }

        private void OnDisable() => SetVisible(false);

        private void OnValidate()
        {
            diameter = Mathf.Max(.01f, diameter);
            ApplyAppearance();
        }
    }
}

using UnityEngine;

namespace AnimalGame.RobotMap
{
    public sealed partial class RobotMarkerView
    {
        [Header("Body hatches")]
        [SerializeField] private Shader bodyHatchShader;
        [SerializeField, Range(10f, 60f)] private float bodyHatchArcDegrees = 50f;
        [SerializeField, Range(0f, 40f)] private float bodyHatchHingeAngleDegrees = 30f;
        [SerializeField, Range(0f, 120f)] private float bodyHatchOpenDegrees = 50f;
        [SerializeField, Min(.01f)] private float bodyHatchOpenDuration = .4f;
        [SerializeField, Min(.01f)] private float bodyHatchCloseDuration = .2f;
        [SerializeField, Min(1f)] private float bodyHatchOutlineWidthPixels = 12f;
        [SerializeField, Min(1f)] private float bodyHatchOutlineGapPixels = 32f;

        private Transform leftBodyHatch, rightBodyHatch;
        private Material[] bodyHatchMaterials;
        private Sprite bodyHatchSprite;
        private float bodyHatchOpen01;
        public bool AreBodyHatchesClosed => bodyHatchOpen01 <= .001f;

        private void BindBodyHatches()
        {
            Sprite source = bodyArtwork.sprite;
            Rect rect = source.rect;
            float padding = bodyHatchOutlineWidthPixels * 2f + bodyHatchOutlineGapPixels;
            Rect doorRect = Rect.MinMaxRect(Mathf.Max(0f, rect.xMin - padding), Mathf.Max(0f, rect.yMin - padding),
                Mathf.Min(source.texture.width, rect.xMax + padding), Mathf.Min(source.texture.height, rect.yMax + padding));
            Vector2 pivot = rect.position + source.pivot - doorRect.position;
            bodyHatchSprite = Sprite.Create(source.texture, doorRect,
                new Vector2(pivot.x / doorRect.width, pivot.y / doorRect.height),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            bodyHatchSprite.name = "Runtime Body Hatch Artwork";
            bodyHatchSprite.hideFlags = HideFlags.DontSave;
            bodyHatchMaterials = new Material[3];
            for (int i = 0; i < bodyHatchMaterials.Length; i++)
            {
                var material = new Material(bodyHatchShader)
                {
                    name = "Runtime Body Hatch " + (i - 1), hideFlags = HideFlags.DontSave,
                    renderQueue = bodyArtwork.sharedMaterial.renderQueue
                };
                material.SetVector("_BodyCenter", new Vector4(rect.center.x / source.texture.width,
                    rect.center.y / source.texture.height, source.texture.width, source.texture.height));
                material.SetFloat("_HingeAngle", bodyHatchHingeAngleDegrees);
                material.SetFloat("_ArcDegrees", bodyHatchArcDegrees);
                material.SetFloat("_Part", i - 1);
                material.SetVector("_DoorShape", new Vector4(bodyArtworkVisibleDiameterPixels * .47f,
                    bodyHatchOutlineWidthPixels, (bodyHatchOutlineWidthPixels + bodyHatchOutlineGapPixels) * .5f, 0f));
                bodyHatchMaterials[i] = material;
            }
            bodyArtwork.sharedMaterial = bodyHatchMaterials[0];
            leftBodyHatch = CreateBodyHatch("Left Body Hatch", -1f, bodyHatchMaterials[1]);
            rightBodyHatch = CreateBodyHatch("Right Body Hatch", 1f, bodyHatchMaterials[2]);
        }

        private Transform CreateBodyHatch(string name, float side, Material material)
        {
            var hinge = new GameObject(name).transform;
            hinge.SetParent(bodyVisualRoot, false);
            float angle = (side < 0f ? 180f - bodyHatchHingeAngleDegrees : bodyHatchHingeAngleDegrees) * Mathf.Deg2Rad;
            Vector3 pivot = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * (VisualBodyDiameter * .47f);
            hinge.localPosition = bodyArtwork.transform.localPosition + pivot;
            var artwork = new GameObject("Hatch Artwork", typeof(SpriteRenderer));
            artwork.transform.SetParent(hinge, false);
            artwork.transform.localPosition = -pivot;
            artwork.transform.localScale = bodyArtwork.transform.localScale;
            var renderer = artwork.GetComponent<SpriteRenderer>();
            renderer.sprite = bodyHatchSprite;
            renderer.color = bodyOutlineColor;
            renderer.sharedMaterial = material;
            renderer.sortingLayerID = bodyArtwork.sortingLayerID;
            renderer.sortingOrder = bodyArtwork.sortingOrder;
            return hinge;
        }

        private void UpdateBodyHatches() => StepBodyHatches(
            armController != null && armController.IsInventoryPresentationActive
                ? Time.unscaledDeltaTime : Time.deltaTime);

        private void StepBodyHatches(float dt)
        {
            float target = armController == null ? 0f : armController.IsArmModeActive ? 1f
                : Mathf.Clamp01(armController.VisibleDeployment01 / .35f);
            float previous = bodyHatchOpen01;
            float duration = target > bodyHatchOpen01 ? bodyHatchOpenDuration : bodyHatchCloseDuration;
            bodyHatchOpen01 = Mathf.MoveTowards(bodyHatchOpen01, target, dt / duration);
            if (previous == bodyHatchOpen01) return;
            ApplyBodyHatchPose();
        }

        private void ApplyBodyHatchPose()
        {
            float angle = bodyHatchOpenDegrees * Mathf.SmoothStep(0f, 1f, bodyHatchOpen01);
            leftBodyHatch.localRotation = Quaternion.Euler(0f, 0f, angle);
            rightBodyHatch.localRotation = Quaternion.Euler(0f, 0f, -angle);
            float open = Mathf.SmoothStep(0f, 1f, bodyHatchOpen01);
            foreach (Material material in bodyHatchMaterials) material.SetFloat("_Open01", open);
        }

        private void DisposeBodyHatches()
        {
            if (bodyHatchMaterials != null)
                foreach (Material material in bodyHatchMaterials) Destroy(material);
            if (bodyHatchSprite != null) Destroy(bodyHatchSprite);
        }

        public void CloseBodyHatchesImmediately()
        {
            bodyHatchOpen01 = 0f;
            if (leftBodyHatch == null) return;
            ApplyBodyHatchPose();
        }
    }
}

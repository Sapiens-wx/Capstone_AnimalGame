using System;
using System.Collections.Generic;
using AnimalGame.Animals;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.RobotMap
{
    /// <summary>Prefab presentation only: no subject detection, review state or album ownership.</summary>
    [DisallowMultipleComponent]
    public sealed class PhotoResultView : MonoBehaviour
    {
        [Header("Assets")]
        [SerializeField] private UIDocument document;
        [SerializeField] private Shader compositeShader;
        [Header("Design layout")]
        [SerializeField] private Vector2 designSize = new Vector2(1920, 1080);
        [SerializeField] private Vector2 snapshotCircleCenter = new Vector2(1150, 815);
        [Header("Animation tracks (normalized start/end)")]
        [SerializeField] private PhotoResultAnimation animationSettings = new PhotoResultAnimation();
        [Header("Photo perspective")]
        [SerializeField] private Vector2 restingTiltDegrees = new Vector2(3, -8);
        [SerializeField, Range(0, 20)] private float interactiveTiltDegrees = 9;
        [SerializeField, Min(0.01f)] private float tiltResponse = 9;
        [SerializeField, Range(0, 0.9f)] private float stickDeadZone = 0.16f;
        [Header("Rendering")]
        [SerializeField, Range(256, 2048)] private int photoResolution = 1024;
        [SerializeField, Range(128, 2048)] private int snapshotResolution = 768;

        public int SnapshotResolution => snapshotResolution;
        public bool IsReady => root != null;
        public bool IsClosing => animationSettings.IsClosing;
        public event Action Closed;

        private VisualElement root, stage, zoomContent, backdrop, vectors, circle, photo, textContent;
        private Label saveLabel;
        private Image photoImage, snapshotImage;
        private readonly List<PhotoResultArcElement> arcs = new List<PhotoResultArcElement>();
        private readonly List<PhotoResultLineElement> lines = new List<PhotoResultLineElement>();
        private Material cardMaterial, circleMaterial;
        private RenderTexture cardOutput, circleOutput;
        private RenderTexture processedPhoto;
        private Texture photoSource, contourSource;
        private Vector2 tilt;
        private float lastCircleReveal = -1;
        private bool showing;
        private void Awake()
        {
            BuildDocumentTree();
        }

        private void BuildDocumentTree()
        {
            root = document.rootVisualElement;
            arcs.Clear();
            lines.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.overflow = Overflow.Hidden;
            stage = root.Q("stage");
            zoomContent = root.Q("zoom-content");
            backdrop = root.Q("backdrop");
            vectors = root.Q("vectors");
            circle = root.Q("snapshot-circle");
            photo = root.Q("photo");
            textContent = root.Q("text-content");
            saveLabel = root.Q<Label>("save-label");
            photoImage = root.Q<Image>("photo-image");
            snapshotImage = root.Q<Image>("snapshot-image");
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
                element.pickingMode = PickingMode.Ignore;
            CollectVectorElements();
            ConfigureDesignLayout();
            root.style.display = DisplayStyle.None;
        }

        private void ConfigureDesignLayout()
        {
            if (stage == null || zoomContent == null || circle == null) return;

            float designWidth = Mathf.Max(1f, designSize.x);
            float designHeight = Mathf.Max(1f, designSize.y);
            stage.style.width = designWidth;
            stage.style.height = designHeight;
            zoomContent.style.width = designWidth;
            zoomContent.style.height = designHeight;
            ConfigureCircleLayout();
            Vector2 initialOffset = new Vector2(designWidth, designHeight) * 0.5f
                - snapshotCircleCenter;
            zoomContent.style.left = initialOffset.x;
            zoomContent.style.top = initialOffset.y;
            zoomContent.style.scale = new Scale(Vector3.one * animationSettings.zoomedScale);
        }

        private void ConfigureCircleLayout()
        {
            zoomContent.style.transformOrigin = new TransformOrigin(
                new Length(snapshotCircleCenter.x, LengthUnit.Pixel),
                new Length(snapshotCircleCenter.y, LengthUnit.Pixel));
        }

        private void CollectVectorElements()
        {
            VisualElement arcElements = root.Q("arc-vectors");
            VisualElement lineElements = root.Q("line-vectors");
            if (arcElements != null) arcs.AddRange(arcElements.Query<PhotoResultArcElement>().ToList());
            if (lineElements != null) lines.AddRange(lineElements.Query<PhotoResultLineElement>().ToList());
        }

        internal void Show(PhotoResultSnapshot result, PhotoContourCapture capture)
        {
            // UIDocument may rebuild its root after its parent GameObject is re-enabled.
            if (document.rootVisualElement != root || document.rootVisualElement.Q("stage") == null)
                BuildDocumentTree();
            ReleaseTextures();
            processedPhoto = result.Photo.Render(photoResolution);
            photoSource = processedPhoto;
            contourSource = capture.Texture;
            root.Q<Label>("animal-name").text = result.EnglishName;
            root.Q<Label>("scientific-name").text = result.ScientificName;
            root.Q<Label>("region").text = result.RegionName;
            root.Q<Label>("altitude").text = result.HasHeight ? $"{result.HeightMeters:0}m" : "— m";
            root.Q<Label>("coordinates").text = $"Coordinates ({result.MapPositionMeters.x:0.0}, {result.MapPositionMeters.y:0.0})";
            string altitude = result.HasHeight ? $"{result.HeightMeters:0}m" : "unknown-altitude";
            root.Q<Label>("metadata").text = $"{result.ScientificName}_{altitude}_{result.CapturedAt:yyyyMMdd_HHmmss}";
            root.Q<Label>("reward").text = $"Recognition {result.CognitionDegrees}°   +{result.TotalReward}";
            saveLabel.text = "Save Photo";
            cardMaterial = new Material(compositeShader) { hideFlags = HideFlags.HideAndDontSave };
            circleMaterial = new Material(compositeShader) { hideFlags = HideFlags.HideAndDontSave };
            circleMaterial.SetFloat("_Mode", 1);
            cardMaterial.SetVector("_Crop", new Vector4(0, 0, 1, 1));
            // Fit the entire processed image into the square card without another crop.
            cardMaterial.SetFloat("_ImageAspect", (float)photoSource.width / photoSource.height);
            cardOutput = CreateOutput("Perspective Animal Photo", photoResolution);
            photoImage.image = cardOutput;
            circleOutput = CreateOutput("Circular Frozen Contours", snapshotResolution);
            Blit(contourSource != null ? contourSource : Texture2D.blackTexture, circleOutput, circleMaterial);
            snapshotImage.image = circleOutput;
            tilt = restingTiltDegrees;
            lastCircleReveal = -1;
            animationSettings.Open();
            showing = true;
            root.style.display = DisplayStyle.Flex;
            RenderCard();
            ApplyAnimation();
        }

        public void SetSaved() => saveLabel.text = "Photo Saved";
        public void Close() { if (showing) animationSettings.Close(); }

        public void HideImmediately()
        {
            showing = false;
            if (root != null) root.style.display = DisplayStyle.None;
            ReleaseTextures();
        }

        private void Update()
        {
            if (!showing) return;
            animationSettings.Tick(Time.unscaledDeltaTime);
            ApplyAnimation();
            UpdateTilt();
            if (!animationSettings.IsClosed) return;
            HideImmediately();
            Closed?.Invoke();
        }

        private void ApplyAnimation()
        {
            float width = root.resolvedStyle.width;
            float height = root.resolvedStyle.height;
            if (float.IsNaN(width) || width <= 0 || float.IsNaN(height) || height <= 0)
                return;

            float designWidth = Mathf.Max(1f, designSize.x);
            float designHeight = Mathf.Max(1f, designSize.y);
            float scale = Mathf.Max(0.01f, Mathf.Min(width / designWidth, height / designHeight));
            Vector2 offset = new Vector2(
                (width - designWidth * scale) * 0.5f,
                (height - designHeight * scale) * 0.5f);
            stage.style.left = offset.x;
            stage.style.top = offset.y;
            stage.style.scale = new Scale(new Vector3(scale, scale, 1));
            backdrop.style.left = -offset.x / scale;
            backdrop.style.top = -offset.y / scale;
            backdrop.style.width = width / scale;
            backdrop.style.height = height / scale;

            float zoomProgress = animationSettings.Evaluate(animationSettings.zoomWindow);
            float zoom = Mathf.Lerp(animationSettings.zoomedScale,
                animationSettings.restingScale, zoomProgress);
            zoomContent.style.scale = new Scale(new Vector3(zoom, zoom, 1));

            float positionProgress = animationSettings.Evaluate(animationSettings.contentPositionWindow);
            Vector2 screenCenter = new Vector2(designWidth, designHeight) * 0.5f;
            Vector2 contentOffset = Vector2.Lerp(screenCenter - snapshotCircleCenter,
                Vector2.zero, positionProgress);
            zoomContent.style.left = contentOffset.x;
            zoomContent.style.top = contentOffset.y;
            float circleProgress = animationSettings.Evaluate(animationSettings.circleWindow);
            float arcProgress = animationSettings.Evaluate(animationSettings.arcWindow);
            float lineProgress = animationSettings.Evaluate(animationSettings.lineWindow);
            float photoProgress = animationSettings.Evaluate(animationSettings.photoWindow);
            float textProgress = animationSettings.Evaluate(animationSettings.textWindow);
            backdrop.style.opacity = circleProgress;
            vectors.style.opacity = circleProgress;
            foreach (var element in arcs) element.Reveal(arcProgress);
            foreach (var element in lines) element.Reveal(lineProgress);
            photo.style.opacity = photoProgress;
            textContent.style.opacity = textProgress;
            circle.style.opacity=textProgress;
        }

        private void UpdateTilt()
        {
            Vector2 input = Vector2.zero;
            bool gamepadConnected = false;
#if ENABLE_INPUT_SYSTEM
            foreach (Gamepad pad in Gamepad.all)
            {
                if (!pad.added) continue;
                gamepadConnected = true;
                Vector2 value = pad.leftStick.ReadValue();
                if (value.sqrMagnitude > input.sqrMagnitude) input = value;
            }
#endif
            if (!gamepadConnected)
            {
                input = AdaptiveLegacyGamepadInput.ReadLeftStick();
                gamepadConnected = AdaptiveLegacyGamepadInput.HasConnectedGamepad;
            }
            if (gamepadConnected)
            {
                float magnitude = input.magnitude;
                input = magnitude <= stickDeadZone ? Vector2.zero
                    : input.normalized * Mathf.InverseLerp(stickDeadZone, 1, magnitude);
            }
            else
            {
                Vector2 mouse = Vector2.zero;
#if ENABLE_LEGACY_INPUT_MANAGER
                mouse = Input.mousePosition;
#endif
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null) mouse = Mouse.current.position.ReadValue();
#endif
                Vector2 point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mouse.x, Screen.height - mouse.y));
                Rect bounds = photo.worldBound;
                input = new Vector2(Mathf.Clamp((point.x - bounds.center.x) / (bounds.width * 0.5f), -1, 1),
                    Mathf.Clamp((bounds.center.y - point.y) / (bounds.height * 0.5f), -1, 1));
            }
            Vector2 target = restingTiltDegrees + new Vector2(-input.y, input.x) * interactiveTiltDegrees;
            Vector2 next = Vector2.Lerp(tilt, target, 1 - Mathf.Exp(-tiltResponse * Time.unscaledDeltaTime));
            if ((next - tilt).sqrMagnitude < 0.000001f) return;
            tilt = next;
            RenderCard();
        }

        private void RenderCard()
        {
            cardMaterial.SetVector("_Tilt", new Vector4(tilt.x * Mathf.Deg2Rad, tilt.y * Mathf.Deg2Rad, 0, 0));
            Blit(photoSource, cardOutput, cardMaterial);
        }

        private static void Blit(Texture source, RenderTexture target, Material material)
        {
            RenderTexture previous = RenderTexture.active;
            try { Graphics.Blit(source, target, material); }
            finally { RenderTexture.active = previous; }
        }

        private static RenderTexture CreateOutput(string label, int size)
        {
            var texture = new RenderTexture(Mathf.Clamp(size, 128, 2048), Mathf.Clamp(size, 128, 2048), 0, RenderTextureFormat.ARGB32)
            { name = label, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            texture.Create();
            return texture;
        }

        private void ReleaseTextures()
        {
            if (photoImage != null) photoImage.image = null;
            if (snapshotImage != null) snapshotImage.image = null;
            if (cardOutput != null) { cardOutput.Release(); Destroy(cardOutput); }
            if (circleOutput != null) { circleOutput.Release(); Destroy(circleOutput); }
            if (cardMaterial != null) Destroy(cardMaterial);
            if (circleMaterial != null) Destroy(circleMaterial);
            AnimalPhotoProcessing.Release(processedPhoto);
            processedPhoto = null;
            cardOutput = circleOutput = null;
            cardMaterial = circleMaterial = null;
            photoSource = contourSource = null;
        }

        private void OnDisable() => HideImmediately();
        private void OnDestroy()
        {
            ReleaseTextures();
        }
    }
}

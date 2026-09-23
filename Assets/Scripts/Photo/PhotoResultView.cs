using System;
using System.Collections.Generic;
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
        [SerializeField] private VisualTreeAsset layout;
        [SerializeField] private Shader compositeShader;
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

        private VisualElement root, stage, circle, photo, textContent;
        private Label saveLabel;
        private Image photoImage, snapshotImage;
        private readonly List<PhotoResultArcElement> arcs = new List<PhotoResultArcElement>();
        private readonly List<PhotoResultLineElement> lines = new List<PhotoResultLineElement>();
        private PhotoResultGridElement grid;
        private PhotoResultArcElement snapshotRing;
        private Material cardMaterial, circleMaterial;
        private RenderTexture cardOutput, circleOutput;
        private Texture photoSource, contourSource;
        private Vector2 sourceCenterViewport, tilt;
        private float sourceRadiusViewportHeight;
        private float lastCircleReveal = -1;
        private bool showing;
        private static readonly Vector2 FinalCircleCenter = new Vector2(1150, 815);
        private const float FinalCircleRadius = 175;

        private void Awake()
        {
            BuildDocumentTree();
        }

        private void BuildDocumentTree()
        {
            root = document.rootVisualElement;
            arcs.Clear();
            lines.Clear();
            grid = null;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            stage = root.Q("stage");
            circle = root.Q("snapshot-circle");
            photo = root.Q("photo");
            textContent = root.Q("text-content");
            saveLabel = root.Q<Label>("save-label");
            photoImage = root.Q<Image>("photo-image");
            snapshotImage = root.Q<Image>("snapshot-image");
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
                element.pickingMode = PickingMode.Ignore;
            CollectVectorElements();
            root.style.display = DisplayStyle.None;
        }

        private void CollectVectorElements()
        {
            VisualElement arcElements = root.Q("arc-vectors");
            VisualElement lineElements = root.Q("line-vectors");
            grid = root.Q<PhotoResultGridElement>("grid");
            if (arcElements != null) arcs.AddRange(arcElements.Query<PhotoResultArcElement>().ToList());
            if (lineElements != null) lines.AddRange(lineElements.Query<PhotoResultLineElement>().ToList());
            snapshotRing = root.Q<PhotoResultArcElement>("snapshot-outline");
        }

        internal void Show(PhotoResultSnapshot result, PhotoContourCapture capture)
        {
            // UIDocument may rebuild its root after its parent GameObject is re-enabled.
            if (document.rootVisualElement != root || document.rootVisualElement.Q("stage") == null)
                BuildDocumentTree();
            ReleaseTextures();
            photoSource = result.Photo.Photo.texture;
            contourSource = capture.Texture;
            sourceCenterViewport = capture.CenterViewport;
            sourceRadiusViewportHeight = capture.RadiusViewportHeight;
            root.Q<Label>("animal-name").text = result.EnglishName;
            root.Q<Label>("scientific-name").text = result.ScientificName;
            root.Q<Label>("region").text = result.RegionName;
            root.Q<Label>("altitude").text = $"{result.HeightMeters:0}m";
            root.Q<Label>("metadata").text = $"{result.ScientificName}_{result.HeightMeters:0}m_{result.CapturedAt:yyyyMMdd_HHmmss}";
            root.Q<Label>("reward").text = $"Recognition {result.CognitionDegrees}°   +{result.TotalReward}";
            saveLabel.text = "Save Photo";
            cardMaterial = new Material(compositeShader) { hideFlags = HideFlags.HideAndDontSave };
            circleMaterial = new Material(compositeShader) { hideFlags = HideFlags.HideAndDontSave };
            circleMaterial.SetFloat("_Mode", 1);
            Rect crop = SquareCrop(result.Photo.GetTextureUvRect(), photoSource);
            cardMaterial.SetVector("_Crop", new Vector4(crop.x, crop.y, crop.width, crop.height));
            cardOutput = CreateOutput("Perspective Animal Photo", photoResolution);
            circleOutput = CreateOutput("Circular Frozen Contours", snapshotResolution);
            photoImage.image = cardOutput;
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
            if (float.IsNaN(width) || width <= 0) width = Screen.width;
            if (float.IsNaN(height) || height <= 0) height = Screen.height;
            float scale = Mathf.Max(0.01f, Mathf.Min(width / 1920, height / 1080));
            Vector2 offset = new Vector2((width - 1920 * scale) / 2, (height - 1080 * scale) / 2);
            stage.style.left = offset.x;
            stage.style.top = offset.y;
            stage.style.scale = new Scale(new Vector3(scale, scale, 1));
            float circleProgress = animationSettings.Evaluate(animationSettings.circleWindow);
            float arcProgress = animationSettings.Evaluate(animationSettings.arcWindow);
            float lineProgress = animationSettings.Evaluate(animationSettings.lineWindow);
            float photoProgress = animationSettings.Evaluate(animationSettings.photoWindow);
            float textProgress = animationSettings.Evaluate(animationSettings.textWindow);
            root.style.backgroundColor = new Color(0, 0, 0, circleProgress);
            if (grid != null) grid.Reveal(circleProgress);
            foreach (var element in arcs) element.Reveal(arcProgress);
            foreach (var element in lines) element.Reveal(lineProgress);
            if (snapshotRing != null) snapshotRing.Reveal(arcProgress);
            photo.style.opacity = photoProgress;
            photo.style.translate = new Translate(-70 * (1 - photoProgress), 15 * (1 - photoProgress));
            textContent.style.opacity = textProgress;
            circle.Q("close-badge").style.opacity = textProgress;
            Vector2 startCenter = (new Vector2(sourceCenterViewport.x * width,
                (1 - sourceCenterViewport.y) * height) - offset) / scale;
            float startRadius = sourceRadiusViewportHeight * height / scale;
            Vector2 center = Vector2.Lerp(startCenter, FinalCircleCenter, circleProgress);
            float radius = Mathf.Lerp(Mathf.Max(1, startRadius), FinalCircleRadius, circleProgress);
            circle.style.left = center.x - FinalCircleRadius;
            circle.style.top = center.y - FinalCircleRadius;
            circle.style.scale = new Scale(Vector3.one * (radius / FinalCircleRadius));
            if (circleMaterial != null && !Mathf.Approximately(lastCircleReveal, arcProgress))
            {
                circleMaterial.SetFloat("_Reveal", arcProgress);
                Blit(contourSource != null ? contourSource : Texture2D.blackTexture, circleOutput, circleMaterial);
                lastCircleReveal = arcProgress;
            }
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

        private static Rect SquareCrop(Rect crop, Texture texture)
        {
            float width = crop.width * texture.width, height = crop.height * texture.height;
            if (width > height) { float newWidth = height / texture.width; crop.x += (crop.width - newWidth) * 0.5f; crop.width = newWidth; }
            else { float newHeight = width / texture.height; crop.y += (crop.height - newHeight) * 0.5f; crop.height = newHeight; }
            return crop;
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

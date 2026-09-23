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
        [SerializeField] private VisualTreeAsset layout;
        [SerializeField] private ThemeStyleSheet theme;
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
        [SerializeField] private Color accentColor = new Color(0.79f, 0.68f, 0.24f, 1);

        public int SnapshotResolution => snapshotResolution;
        public bool IsReady => root != null;
        public bool IsClosing => animationSettings.IsClosing;
        public event Action Closed;

        private UIDocument document;
        private PanelSettings panel;
        private VisualElement root, stage, circle, photo, textContent;
        private Label saveLabel;
        private Image photoImage, snapshotImage;
        private readonly List<PhotoResultVectorElement> arcs = new List<PhotoResultVectorElement>();
        private readonly List<PhotoResultVectorElement> lines = new List<PhotoResultVectorElement>();
        private readonly List<PhotoResultVectorElement> grid = new List<PhotoResultVectorElement>();
        private PhotoResultVectorElement snapshotRing;
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
            if (layout == null) layout = Resources.Load<VisualTreeAsset>("UI/PhotoResult");
            if (theme == null) theme = Resources.Load<ThemeStyleSheet>("UI/PhotoResultTheme");
            if (layout == null || theme == null || compositeShader == null)
            {
                Debug.LogError("Photo result prefab is missing its layout, theme or composite shader.", this);
                enabled = false;
                return;
            }
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "Photo Result Runtime Panel";
            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.sortingOrder = 200;
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.sortingOrder = 200;
            BuildDocumentTree();
        }

        private void BuildDocumentTree()
        {
            root = document.rootVisualElement;
            root.Clear();
            arcs.Clear();
            lines.Clear();
            grid.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            layout.CloneTree(root);
            stage = root.Q("stage");
            circle = root.Q("snapshot-circle");
            photo = root.Q("photo");
            textContent = root.Q("text-content");
            saveLabel = root.Q<Label>("save-label");
            photoImage = root.Q<Image>("photo-image");
            snapshotImage = root.Q<Image>("snapshot-image");
            foreach (VisualElement element in root.Query<VisualElement>().ToList())
                element.pickingMode = PickingMode.Ignore;
            BuildVectors();
            root.style.display = DisplayStyle.None;
        }

        private void BuildVectors()
        {
            VisualElement vectors = root.Q("vectors");
            var exclusions = new List<Rect>
            {
                new Rect(1005, 410, 275, 72), new Rect(995, 533, 350, 77),
                new Rect(1250, 303, 340, 80), new Rect(1380, 408, 340, 85),
                new Rect(1385, 510, 285, 120), new Rect(1310, 900, 460, 66)
            };
            Color white = new Color(0.94f, 0.96f, 0.94f, 1);
            Color gridColor = new Color(0.55f, 0.59f, 0.57f, 0.23f);
            for (int x = 0; x <= 1920; x += 56)
                AddLine(grid, vectors, "grid-v-" + x, new Vector2(x, 0), new Vector2(x, 1080), gridColor, 1);
            for (int y = 12; y <= 1080; y += 56)
                AddLine(grid, vectors, "grid-h-" + y, new Vector2(0, y), new Vector2(1920, y), gridColor, 1);

            AddArc(vectors, "outer-orbit", new Vector2(1400, 550), 370, 180, 235, white, exclusions);
            AddArc(vectors, "gold-orbit", new Vector2(1400, 550), 342, 180, 230, accentColor, exclusions);
            AddArc(vectors, "upper-orbit", new Vector2(1147, 0), 704, 180, 390, accentColor, exclusions);
            AddLine(lines, vectors, "baseline", new Vector2(25, 958), new Vector2(1745, 1011), white, 2);
            AddLine(lines, vectors, "metadata-rule", new Vector2(62, 180), new Vector2(62, 1015), white);
            AddLine(lines, vectors, "top-tangent", new Vector2(1175, 95), new Vector2(1166, 322), white);
            AddLine(lines, vectors, "cross-tangent", new Vector2(1120, 305), new Vector2(1280, 293), white);
            AddLine(lines, vectors, "photo-save-rule", new Vector2(1018, 598), new Vector2(1330, 606), white);
            AddLine(lines, vectors, "catalog-rule", new Vector2(1030, 477), new Vector2(1265, 482), white);
            AddLine(lines, vectors, "altitude-rule", new Vector2(1390, 568), new Vector2(1605, 576), white);
            AddLine(lines, vectors, "gold-leader", new Vector2(1137, 698), new Vector2(1440, 630), accentColor);
            AddLine(lines, vectors, "gold-diagonal", new Vector2(1390, 569), new Vector2(1504, 700), accentColor);
            AddLine(lines, vectors, "location-chevron-a", new Vector2(1496, 390), new Vector2(1650, 380), white);
            AddLine(lines, vectors, "location-chevron-b", new Vector2(1496, 390), new Vector2(1650, 397), white);
            for (int i = 0; i < 21; i++)
            {
                float x = 640 + i * 52;
                float y = 958 + (x - 25) / 1720 * 53;
                AddLine(lines, vectors, "ruler-" + i, new Vector2(x, y), new Vector2(x, y - (i % 2 == 0 ? 22 : 11)), white);
            }
            for (int i = 0; i < 24; i++)
            {
                float x = 30 + i * 82;
                AddLine(lines, vectors, "dash-" + i, new Vector2(x, 300 - x * 0.07f),
                    new Vector2(x + 38, 300 - (x + 38) * 0.07f), white);
            }
            snapshotRing = new PhotoResultVectorElement("snapshot-outline",
                PhotoResultVectorElement.Arc(Vector2.one * FinalCircleRadius, FinalCircleRadius - 2, 180, 360), white, 2.5f);
            snapshotRing.style.width = snapshotRing.style.height = FinalCircleRadius * 2;
            circle.Add(snapshotRing);
            // Badge belongs above the snapshot and remains exactly at the circle's center.
            circle.Q("close-badge").BringToFront();
        }

        private void AddArc(VisualElement parent, string id, Vector2 center, float radius,
            float start, float sweep, Color color, List<Rect> exclusions)
        {
            var element = new PhotoResultVectorElement(id,
                PhotoResultVectorElement.Arc(center, radius, start, sweep), color, 1.5f, exclusions);
            parent.Add(element);
            arcs.Add(element);
        }

        private static void AddLine(List<PhotoResultVectorElement> list, VisualElement parent,
            string id, Vector2 start, Vector2 end, Color color, float width = 1.5f)
        {
            var element = new PhotoResultVectorElement(id, PhotoResultVectorElement.Line(start, end), color, width);
            parent.Add(element);
            list.Add(element);
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
            foreach (var element in grid) element.Reveal(circleProgress);
            foreach (var element in arcs) element.Reveal(arcProgress);
            foreach (var element in lines) element.Reveal(lineProgress);
            snapshotRing.Reveal(arcProgress);
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
            if (panel != null) Destroy(panel);
        }
    }
}

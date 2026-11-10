using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class CatalogRulerElement : VisualElement
    {
        private const int VerticalTicks = 20;
        // Shared frame proportions match the American bison reference (1445 x 965).
        private const float FrameAspectRatio = 1445f / 965f;
        private readonly Image illustration;
        private readonly Label shoulder, length, mass;
        private CatalogHeadingElement heading;
        private VisualElement viewport;
        private float heightRatio = .38f;

        [UxmlAttribute]
        public float HeightRatio
        {
            get => heightRatio;
            set { heightRatio = Mathf.Clamp01(value); Resize(); }
        }

        public CatalogRulerElement()
        {
            AddToClassList("catalog-ruler");
            illustration = new Image { name = "catalog-illustration", scaleMode = ScaleMode.ScaleToFit };
            illustration.AddToClassList("ruler-illustration"); Add(illustration);
            shoulder = Caption("ruler-shoulder");
            length = Caption("ruler-length");
            mass = Caption("ruler-mass");
            generateVisualContent += DrawRuler;
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                viewport = panel.visualTree;
                viewport.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                Resize();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                viewport.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                viewport = null;
            });
        }

        public void Bind(AnimalInfo info, CatalogHeadingElement physicalHeading)
        {
            if (heading != physicalHeading)
            {
                if (heading != null) heading.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                heading = physicalHeading;
                heading.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }
            illustration.image = info.Illustration;
            shoulder.text = $"Shoulder Height: {info.ShoulderHeight}";
            length.text = $"Total Length: {info.TotalLength}";
            mass.text = $"Body Mass: {info.BodyMass}";
            Resize();
        }

        private Label Caption(string className)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className); Add(label); return label;
        }

        private void OnGeometryChanged(GeometryChangedEvent _) => Resize();

        private void Resize()
        {
            if (viewport == null || heading == null) return;
            float height = viewport.layout.height * heightRatio;
            if (float.IsNaN(height) || height <= 0) return;
            // Follow the heading layout, excluding its visual-only translation.
            Vector2 anchor = heading.parent.ChangeCoordinatesTo(parent, heading.layout.position +
                new Vector2(heading.contentRect.xMin - 6f, heading.contentRect.yMax + 3f));
            style.left = anchor.x;
            style.top = anchor.y;
            style.height = height;
            float width = height * FrameAspectRatio;
            style.width = width;
            // Center the title on the horizontal ruler without moving the illustration or rulers.
            heading.style.translate = new Translate(anchor.x + width * .5f
                - heading.layout.x - heading.layout.width * .5f, 0);
            // The description shares the row below the ruler with its length caption.
            parent.style.height = anchor.y + height + 16f;
            MarkDirtyRepaint();
        }

        private void DrawRuler(MeshGenerationContext context)
        {
            float height = contentRect.height, width = contentRect.width;
            if (height <= 0 || width <= 0) return;
            float step = height / VerticalTicks;
            var painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = 1f;
            painter.BeginPath();
            Line(painter, 0, 0, 0, height);
            Line(painter, 0, height, width, height);
            for (int i = 1; i <= VerticalTicks; i++)
            {
                float y = height - i * step;
                Line(painter, 0, y, -TickLength(i), y);
            }
            for (int i = 1; i <= Mathf.FloorToInt(width / step); i++)
            {
                float x = i * step;
                Line(painter, x, height, x, height + TickLength(i));
            }
            painter.Stroke();
        }

        private static float TickLength(int index) => index % 5 == 0 ? 14f : 10f;
        private static void Line(Painter2D painter, float x1, float y1, float x2, float y2)
        {
            painter.MoveTo(new Vector2(x1, y1));
            painter.LineTo(new Vector2(x2, y2));
        }
    }
}

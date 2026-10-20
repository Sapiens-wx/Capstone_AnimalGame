using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Demos.CheckpointRefit
{
    /// <summary>Open mechanical structure shared by the isolated refit interface.</summary>
    [UxmlElement]
    public partial class RefitMechanicalFrameElement : VisualElement
    {
        private static readonly Color Structure = new Color32(231, 231, 221, 255);
        private static readonly Color UnitGuides = new Color32(130, 130, 124, 255);
        private string kind = "outer";

        [UxmlAttribute]
        public string frameKind
        {
            get => kind;
            set
            {
                kind = value ?? "outer";
                MarkDirtyRepaint();
            }
        }

        public RefitMechanicalFrameElement()
        {
            pickingMode = PickingMode.Ignore;
            focusable = false;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width < 8 || rect.height < 8) return;

            Painter2D painter = context.painter2D;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;
            switch (kind)
            {
                case "unit":
                    DrawUnit(painter, rect);
                    break;
                case "inspection":
                    DrawInspection(painter, rect);
                    break;
                default:
                    DrawOuter(painter, rect);
                    break;
            }
        }

        private static void DrawOuter(Painter2D painter, Rect rect)
        {
            SetStroke(painter, Structure, 2);
            rect = Inset(rect, 1);
            float chamfer = Mathf.Min(30, Mathf.Min(rect.width, rect.height) * .1f);

            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin + chamfer, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax - chamfer, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin + chamfer));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax - chamfer));
            painter.LineTo(new Vector2(rect.xMax - chamfer, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin + chamfer, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax - chamfer));
            painter.LineTo(new Vector2(rect.xMin, rect.yMin + chamfer));
            painter.ClosePath();
            painter.Stroke();
        }

        private static void DrawUnit(Painter2D painter, Rect rect)
        {
            rect = Inset(rect, 2);
            SetStroke(painter, UnitGuides, 2);
            DrawCorners(painter, rect, 18);

            float halfBeam = Mathf.Min(86, rect.width * .18f);
            float bottom = rect.yMax;
            float middle = rect.center.x;
            SetStroke(painter, Structure, 3);
            Path(painter, new Vector2(middle - halfBeam, bottom), new Vector2(middle + halfBeam, bottom));
            SetStroke(painter, Structure, 2);
            Path(painter, new Vector2(middle - halfBeam, bottom - 7), new Vector2(middle - halfBeam, bottom + 1));
            Path(painter, new Vector2(middle + halfBeam, bottom - 7), new Vector2(middle + halfBeam, bottom + 1));
        }

        private static void DrawInspection(Painter2D painter, Rect rect)
        {
            rect = Inset(rect, 1.5f);
            SetStroke(painter, Structure, 3);
            DrawCorners(painter, rect, 18);
            float halfBeam = Mathf.Min(38, rect.width * .15f);
            Path(painter,
                new Vector2(rect.center.x - halfBeam, rect.yMin),
                new Vector2(rect.center.x + halfBeam, rect.yMin));
        }

        private static void DrawCorners(Painter2D painter, Rect rect, float length)
        {
            length = Mathf.Min(length, Mathf.Min(rect.width, rect.height) * .2f);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                {
                    float px = x == 0 ? rect.xMin : rect.xMax;
                    float py = y == 0 ? rect.yMin : rect.yMax;
                    float dx = x == 0 ? length : -length;
                    float dy = y == 0 ? length : -length;
                    Path(painter, new Vector2(px + dx, py), new Vector2(px, py), new Vector2(px, py + dy));
                }
        }

        private static Rect Inset(Rect rect, float amount)
        {
            return new Rect(rect.x + amount, rect.y + amount, rect.width - amount * 2, rect.height - amount * 2);
        }

        private static void SetStroke(Painter2D painter, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
        }

        private static void Path(Painter2D painter, params Vector2[] points)
        {
            if (points.Length < 2) return;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            painter.Stroke();
        }
    }
}

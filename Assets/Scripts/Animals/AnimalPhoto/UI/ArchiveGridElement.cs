using UnityEngine;
using UnityEngine.UIElements;
namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class ArchiveGridElement : VisualElement
    {
        public ArchiveGridElement() => generateVisualContent += Draw;
        private void Draw(MeshGenerationContext context)
        {
            // Match 12% white over black directly, without alpha blending changing its brightness.
            var p = context.painter2D; p.strokeColor = new Color32(31, 31, 31, 255); p.lineWidth = 1;
            p.BeginPath();
            for (float x = 0; x < contentRect.width; x += 32) { p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, contentRect.height)); }
            for (float y = 0; y < contentRect.height; y += 32) { p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(contentRect.width, y)); }
            p.Stroke();
        }
    }
}

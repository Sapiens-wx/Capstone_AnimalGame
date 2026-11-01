using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    [UxmlElement]
    public partial class ArchivePhotoImageElement : Image
    {
        private bool square = true;

        [UxmlAttribute]
        public bool Square
        {
            get => square;
            set
            {
                square = value;
                if (square) UpdateSquareSize();
                else style.height = StyleKeyword.Null;
            }
        }

        public ArchivePhotoImageElement()
        {
            RegisterCallback<GeometryChangedEvent>(_ => UpdateSquareSize());
        }

        private void UpdateSquareSize()
        {
            if (square && !Mathf.Approximately(layout.height, layout.width))
                style.height = layout.width;
        }
    }
}

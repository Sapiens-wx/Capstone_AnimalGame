using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.World.Map
{
    // All positions are map metres, with north along positive Y.
    public sealed class MapViewState
    {
        public const int PinLimit = 3;
        private readonly List<Vector2> pins = new List<Vector2>(PinLimit);
        private Vector2 projectionOffset;
        public IReadOnlyList<Vector2> Pins => pins;
        private readonly List<int> pinShapes = new List<int>(PinLimit);
        public int GetPinShape(int index) => pinShapes[index];
        public int SelectedPinShape { get; private set; }
        public bool HasPinShape(int shape) => pinShapes.Contains(shape);
        public void CyclePinShape(int direction) => SelectedPinShape = (SelectedPinShape + direction + PinLimit) % PinLimit;
        public bool TryGetPinPosition(int shape, out Vector2 position)
        {
            int index = pinShapes.IndexOf(shape);
            position = index >= 0 ? pins[index] : Vector2.zero;
            return index >= 0;
        }
        public Vector2 Center { get; private set; }
        public Vector2 Cursor { get; private set; }
        public Vector2 DisplayCursor => SelectedPin >= 0 ? pins[SelectedPin] : Cursor;
        public Vector2 ViewSize { get; private set; }
        public int SelectedPin { get; private set; } = -1;
        internal float Bearing { get; set; }
        internal Vector2? TransitionViewSize { get; set; }
        internal Vector2 ProjectedCenter => Center + projectionOffset;
        internal Vector2 ProjectedViewSize => TransitionViewSize ?? ViewSize;
        internal Matrix4x4 MapFromView => Matrix4x4.TRS(ProjectedCenter,
            Quaternion.AngleAxis(-Bearing, Vector3.forward), Vector3.one);

        public void Open(Vector2 player, Vector2 viewSize, Vector2 mapSize)
        {
            Cursor = ClampToMap(player, mapSize);
            Center = Cursor;
            ViewSize = viewSize;
            SelectedPin = -1;
            SelectedPinShape = 0;
            Bearing = 0f;
            TransitionViewSize = null;
            projectionOffset = Vector2.zero;
        }

        internal void AnchorProjection(Vector2 focus, Vector2 viewportOffset)
        {
            Vector2 viewShift = Vector2.Scale(viewportOffset, ProjectedViewSize);
            projectionOffset = focus - Center - (Vector2)(Quaternion.AngleAxis(-Bearing, Vector3.forward) * viewShift);
        }

        public void SetFocus(Vector2 center, Vector2 cursor, Vector2 mapSize)
        {
            Center = center;
            Cursor = ClampToMap(cursor, mapSize);
            SelectedPin = -1;
        }

        public void Resize(Vector2 size)
        {
            // Keep the unsnapped cursor at the same screen position while zooming.
            Vector2 fraction = Divide(Cursor - Center, ViewSize);
            ViewSize = size;
            Center = Cursor - Vector2.Scale(fraction, size);
        }

        public void FitMap(Vector2 mapSize)
        {
            Center = mapSize * .5f;
            Vector2 extent = ViewSize * .25f;
            Cursor = ClampToMap(new Vector2(Mathf.Clamp(Cursor.x, Center.x - extent.x, Center.x + extent.x),
                Mathf.Clamp(Cursor.y, Center.y - extent.y, Center.y + extent.y)), mapSize);
            SelectedPin = -1;
        }

        public void Move(Vector2 screenDelta, Vector2 screenSize, Vector2 mapSize)
        {
            Cursor = ClampToMap(Cursor + Vector2.Scale(Divide(screenDelta, screenSize), ViewSize), mapSize);
            KeepInCursorRect(Cursor);
        }

        public void Snap(Vector2 screenSize, float radiusPixels)
        {
            SelectedPin = -1;
            float nearest = radiusPixels * radiusPixels;
            for (int i = 0; i < pins.Count; i++)
            {
                float distance = Vector2.Scale(Divide(pins[i] - Cursor, ViewSize), screenSize).sqrMagnitude;
                if (distance > nearest) continue;
                nearest = distance;
                SelectedPin = i;
            }
            KeepInCursorRect(DisplayCursor);
        }

        public void Submit()
        {
            if (SelectedPin >= 0)
            {
                Cursor = pins[SelectedPin];
                pins.RemoveAt(SelectedPin);
                pinShapes.RemoveAt(SelectedPin);
                SelectedPin = -1;
                return;
            }
            int existing = pinShapes.IndexOf(SelectedPinShape);
            if (existing >= 0)
            {
                pins[existing] = Cursor;
                SelectedPin = existing;
            }
            else
            {
                pinShapes.Add(SelectedPinShape);
                pins.Add(Cursor);
                SelectedPin = pins.Count - 1;
            }
            CyclePinShape(1);
        }

        public Vector2 ToScreen(Vector2 position, Vector2 screenSize)
        {
            Vector2 offset = position - ProjectedCenter;
            if (Bearing != 0f) offset = Quaternion.AngleAxis(Bearing, Vector3.forward) * offset;
            Vector2 uv = Divide(offset, ProjectedViewSize) + Vector2.one * .5f;
            return new Vector2(uv.x * screenSize.x, (1 - uv.y) * screenSize.y);
        }

        private void KeepInCursorRect(Vector2 point)
        {
            Vector2 offset = point - Center;
            Vector2 extent = ViewSize * .25f;
            Center += new Vector2(offset.x - Mathf.Clamp(offset.x, -extent.x, extent.x),
                offset.y - Mathf.Clamp(offset.y, -extent.y, extent.y));
        }

        private static Vector2 ClampToMap(Vector2 value, Vector2 size) =>
            new Vector2(Mathf.Clamp(value.x, 0, size.x), Mathf.Clamp(value.y, 0, size.y));
        private static Vector2 Divide(Vector2 a, Vector2 b) => new Vector2(a.x / b.x, a.y / b.y);
    }
}

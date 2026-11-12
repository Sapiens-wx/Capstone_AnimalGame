using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.World.Map
{
    // All positions are map metres, with north along positive Y.
    public sealed class MapViewState
    {
        public const int PinLimit = 3;
        private readonly List<Vector2> pins = new List<Vector2>(PinLimit);
        public IReadOnlyList<Vector2> Pins => pins;
        public Vector2 Center { get; private set; }
        public Vector2 Cursor { get; private set; }
        public Vector2 DisplayCursor => SelectedPin >= 0 ? pins[SelectedPin]
            : SelectedCheckpoint >= 0 ? checkpointCursor : Cursor;
        public Vector2 ViewSize { get; private set; }
        public int SelectedPin { get; private set; } = -1;
        public int SelectedCheckpoint { get; private set; } = -1;
        private Vector2 checkpointCursor;

        public void Open(Vector2 player, Vector2 viewSize, Vector2 mapSize)
        {
            Cursor = ClampToMap(player, mapSize);
            Center = Cursor;
            ViewSize = viewSize;
            SelectedPin = -1;
            SelectedCheckpoint = -1;
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
            SelectedCheckpoint = -1;
        }

        public void Move(Vector2 screenDelta, Vector2 screenSize, Vector2 mapSize)
        {
            Cursor = ClampToMap(Cursor + Vector2.Scale(Divide(screenDelta, screenSize), ViewSize), mapSize);
            KeepInCursorRect(Cursor);
        }

        public void Snap(Vector2 screenSize, float radiusPixels, IReadOnlyList<Vector2> checkpoints = null,
            float checkpointRadiusPixels = 0f)
        {
            SelectedPin = -1;
            SelectedCheckpoint = -1;
            float nearest = radiusPixels * radiusPixels;
            for (int i = 0; i < pins.Count; i++)
            {
                float distance = Vector2.Scale(Divide(pins[i] - Cursor, ViewSize), screenSize).sqrMagnitude;
                if (distance > nearest) continue;
                nearest = distance;
                SelectedPin = i;
            }
            if (checkpoints != null)
                for (int i = 0; i < checkpoints.Count; i++)
                {
                    float distance = Vector2.Scale(Divide(checkpoints[i] - Cursor, ViewSize), screenSize).sqrMagnitude;
                    // A pin at exactly the same point wins so A still removes that pin.
                    float radius = checkpointRadiusPixels > 0 ? checkpointRadiusPixels : radiusPixels;
                    if (distance > radius * radius || ((SelectedPin >= 0 || SelectedCheckpoint >= 0) && distance >= nearest)) continue;
                    nearest = distance;
                    SelectedPin = -1;
                    SelectedCheckpoint = i;
                    checkpointCursor = checkpoints[i];
                }
            KeepInCursorRect(DisplayCursor);
        }

        public void Submit()
        {
            if (SelectedPin >= 0)
            {
                Cursor = pins[SelectedPin];
                pins.RemoveAt(SelectedPin);
                SelectedPin = -1;
                return;
            }
            Cursor = DisplayCursor;
            if (pins.Count == PinLimit) pins.RemoveAt(0);
            pins.Add(Cursor);
            SelectedPin = pins.Count - 1;
            SelectedCheckpoint = -1;
        }

        public Vector2 ToScreen(Vector2 position, Vector2 screenSize)
        {
            Vector2 uv = Divide(position - Center, ViewSize) + Vector2.one * .5f;
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

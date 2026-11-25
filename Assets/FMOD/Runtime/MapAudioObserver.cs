using AnimalGame.World.Map;
using UnityEngine;

namespace Capstone.Audio
{
    internal sealed class MapAudioObserver
    {
        private readonly Milestone1AudioSettings settings;
        private readonly Milestone1AudioVoices voices;
        private readonly Vector2?[] pins = new Vector2?[MapViewState.PinLimit];
        private readonly Milestone1AudioVoice[] pinVoices = new Milestone1AudioVoice[3];
        private Milestone1AudioVoice movement, focus;
        private Vector2 lastCursor, lastDisplayCursor, lastCenter, lastSize;
        private float lastZoom, movementIdle, zoomIdle;
        private int lastShape;
        private bool moving, zooming, recentering;

        internal WorldMapUI Source { get; private set; }

        internal MapAudioObserver(Milestone1AudioSettings settings)
        {
            this.settings = settings;
            voices = new Milestone1AudioVoices(settings);
        }

        internal void Tick(WorldMapUI map, float deltaTime)
        {
            if (map == null || !map.isActiveAndEnabled || !map.IsOpen)
            {
                if (!ReferenceEquals(Source, null)) Reset();
                return;
            }

            MapViewState view = map.View;
            bool first = !ReferenceEquals(Source, map);
            if (first)
            {
                Reset();
                Source = map;
            }
            bool placed = false, removed = false;
            for (int shape = 0; shape < pins.Length; shape++)
            {
                Vector2? current = view.TryGetPinPosition(shape, out Vector2 position) ? position : (Vector2?)null;
                if (current != pins[shape])
                {
                    placed |= current.HasValue;  // A previously placed shape can be moved.
                    removed |= !current.HasValue;
                    pins[shape] = current;
                }
            }
            bool cursorMoved = false, zoomMoved = false;
            if (!first && !map.IsClosing)
            {
                bool zoomChanged = !Mathf.Approximately(map.CurrentZoom, lastZoom);
                bool resized = !Mathf.Approximately(view.ViewSize.x / view.ViewSize.y, lastSize.x / lastSize.y);
                bool shapeChanged = view.SelectedPinShape != lastShape && !placed;
                // Manual scrolling only moves the viewport to keep the cursor or its
                // snapped pin/checkpoint inside the central rectangle. A separate
                // center translation with unchanged zoom is observable recentering.
                bool centerMoved = !zoomChanged && !resized && !placed && !removed
                    && view.Center != lastCenter
                    && view.Center != KeepCursorVisible(KeepCursorVisible(lastCenter, view.Cursor, view.ViewSize),
                        view.DisplayCursor, view.ViewSize);

                if (placed) PlayPin(Milestone1Cue.MapPinPlace);
                if (removed) PlayPin(Milestone1Cue.MapPinRemove);
                if (shapeChanged || (centerMoved && !recentering)) PlayPin(Milestone1Cue.MapSelect);
                recentering = centerMoved || (shapeChanged && view.HasPinShape(view.SelectedPinShape));

                cursorMoved = !centerMoved && !resized && !placed && !removed
                    && view.Cursor != lastCursor && view.DisplayCursor != lastDisplayCursor
                    && !(zoomChanged && Mathf.Approximately(map.CurrentZoom, map.MaximumZoom));
                zoomMoved = zoomChanged && !resized;
            }
            FollowMotion(Milestone1Cue.CameraMove, cursorMoved, deltaTime, ref moving, ref movementIdle, ref movement);
            FollowMotion(Milestone1Cue.CameraFocus, zoomMoved, deltaTime, ref zooming, ref zoomIdle, ref focus);
            lastCursor = view.Cursor;
            lastDisplayCursor = view.DisplayCursor;
            lastCenter = view.Center;
            lastSize = view.ViewSize;
            lastZoom = map.CurrentZoom;
            lastShape = view.SelectedPinShape;
            voices.Tick(deltaTime);
        }

        private static Vector2 KeepCursorVisible(Vector2 center, Vector2 cursor, Vector2 viewSize)
        {
            Vector2 offset = cursor - center;
            Vector2 extent = viewSize * .25f;
            return center + new Vector2(offset.x - Mathf.Clamp(offset.x, -extent.x, extent.x),
                offset.y - Mathf.Clamp(offset.y, -extent.y, extent.y));
        }

        private void FollowMotion(Milestone1Cue cue, bool changed, float deltaTime,
            ref bool active, ref float idle, ref Milestone1AudioVoice voice)
        {
            if (changed)
            {
                idle = 0f;
                if (active) return;
                active = true;
                voices.Stop(voice, true);
                voice = voices.Play(cue);
            }
            else if (active)
            {
                idle += deltaTime;
                if (idle < settings.CameraMoveStopDelay) return;
                active = false;
                voices.Stop(voice);
            }
        }

        private void PlayPin(Milestone1Cue cue)
        {
            if (settings.GetCue(cue).EventReference.Guid.IsNull) return;
            int index = (int)cue - (int)Milestone1Cue.MapPinPlace;
            voices.Stop(pinVoices[index], true);
            pinVoices[index] = voices.Play(cue);
        }

        internal void Reset()
        {
            voices.StopAll(true);
            System.Array.Clear(pinVoices, 0, pinVoices.Length);
            Source = null;
            movement = focus = null;
            moving = zooming = recentering = false;
        }
    }
}

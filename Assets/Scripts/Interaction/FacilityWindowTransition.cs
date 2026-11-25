using System;
using AnimalGame.Animals.UI;
using AnimalGame.MainUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Interaction
{
    // The owning facility keeps its ordinary modal input/pause lease until finishClose runs.
    internal sealed class FacilityWindowTransition : IDisposable
    {
        private const float FillDuration = .26f;
        private const float ReadDuration = .30f;
        private const float ExpandDuration = .54f;
        private const float CollapseDuration = .18f;
        private const float SmallScale = .10f;
        private const float LoadingSocketGap = 52f;
        private const float LoadingEdgeInset = 12f;

        private enum Phase { Reading, Expanding, Open, Closing, Closed }
        private Phase phase = Phase.Reading;
        private float elapsed;
        private Action finishClose;
        private readonly VisualElement window, overlay, row, frame;
        private readonly CatalogRecognitionElement meter;
        private readonly Camera worldCamera;
        private readonly Vector3 socketWorld;

        public bool BlocksInput => phase != Phase.Open;

        public FacilityWindowTransition(VisualElement documentRoot, VisualElement window, Vector3 socketWorld)
        {
            this.window = window;
            this.socketWorld = socketWorld;
            worldCamera = Camera.main;
            overlay = new VisualElement { name = "facility-transition-overlay" };
            overlay.styleSheets.Add(Resources.Load<StyleSheet>("UI/FacilityWindowTransition"));
            row = new VisualElement { name = "facility-loading-row", pickingMode = PickingMode.Ignore };
            row.Add(new Label("LOADING DATA") { name = "facility-loading-label", pickingMode = PickingMode.Ignore });
            meter = new CatalogRecognitionElement { name = "facility-loading-meter" };
            row.Add(meter);
            overlay.Add(row);
            frame = new VisualElement { name = "facility-window-frame", pickingMode = PickingMode.Ignore };
            overlay.Add(frame);
            row.RegisterCallback<GeometryChangedEvent>(_ => PositionLoadingRow());
            documentRoot.Add(overlay);
            window.SetEnabled(false);
            window.style.opacity = 0f;
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (phase == Phase.Open || phase == Phase.Closed) return;
            elapsed += unscaledDeltaTime;
            if (phase == Phase.Closing)
            {
                float t = Mathf.Clamp01(elapsed / CollapseDuration);
                float openness = 1f - t * t * t;
                ApplyWindow(openness, .4f);
                if (t >= 1f)
                {
                    phase = Phase.Closed;
                    finishClose();
                }
                return;
            }

            if (phase == Phase.Reading)
            {
                PositionLoadingRow();
                // A slow frame can cross the entire 100% hold. Show it once before starting the zoom.
                if (elapsed < ReadDuration || meter.Value < 100)
                {
                    meter.Value = Mathf.FloorToInt(100f * Mathf.Clamp01(elapsed / FillDuration));
                    return;
                }
                phase = Phase.Expanding;
                elapsed = 0f;
                row.style.display = DisplayStyle.None;
                frame.style.display = DisplayStyle.Flex;
                ApplyWindow(0f, .65f);
                return;
            }

            float progress = Mathf.Clamp01(elapsed / ExpandDuration);
            ApplyWindow(Mathf.SmoothStep(0f, 1f, progress), .65f);
            if (progress >= 1f)
            {
                phase = Phase.Open;
                overlay.style.display = DisplayStyle.None;
                RestoreWindow();
            }
        }

        private Vector2 SocketOnPanel()
        {
            Vector3 screen = worldCamera.WorldToScreenPoint(socketWorld);
            return RuntimePanelUtils.ScreenToPanel(overlay.panel, new Vector2(screen.x, Screen.height - screen.y));
        }

        private void PositionLoadingRow()
        {
            Vector2 rowSize = row.layout.size;
            if (!(rowSize.x > 0f && rowSize.y > 0f)) return;
            Vector2 panelSize = overlay.contentRect.size;
            float fit = SideHudCard.ScreenFit(panelSize);
            Vector2 size = rowSize * fit;
            Vector2 socket = overlay.WorldToLocal(SocketOnPanel());
            float inset = LoadingEdgeInset * fit;
            float x = Mathf.Clamp(socket.x - size.x * .5f, inset, panelSize.x - size.x - inset);
            float y = Mathf.Clamp(socket.y + LoadingSocketGap * fit, inset, panelSize.y - size.y - inset);
            row.style.translate = new Translate(x, y);
            row.style.scale = new Scale(new Vector3(fit, fit, 1f));
        }

        private void ApplyWindow(float openness, float minimumOpacity)
        {
            // Measure in the unscaled parent so the window's current transform never feeds back into its path.
            Rect layout = window.layout;
            Vector2 offset = (window.parent.WorldToLocal(SocketOnPanel()) - layout.center) * (1f - openness);
            float scale = Mathf.Lerp(SmallScale, 1f, openness);
            window.style.translate = new Translate(offset.x, offset.y);
            window.style.scale = new Scale(new Vector3(scale, scale, 1f));
            window.style.opacity = Mathf.Lerp(minimumOpacity, 1f, openness);

            // Draw the outline above the window so its stroke stays crisp even at the smallest scale.
            Vector2 size = layout.size * scale;
            Rect bounds = window.parent.ChangeCoordinatesTo(overlay, new Rect(layout.center + offset - size * .5f, size));
            frame.style.left = bounds.x;
            frame.style.top = bounds.y;
            frame.style.width = bounds.width;
            frame.style.height = bounds.height;
        }

        public void Close(Action onClosed)
        {
            if (phase != Phase.Open) return;
            phase = Phase.Closing;
            elapsed = 0f;
            finishClose = onClosed;
            overlay.style.display = DisplayStyle.Flex;
            window.SetEnabled(false);
            ApplyWindow(1f, .4f);
        }

        public void Dispose()
        {
            phase = Phase.Closed;
            finishClose = null;
            overlay.RemoveFromHierarchy();
            RestoreWindow();
        }

        private void RestoreWindow()
        {
            window.style.translate = StyleKeyword.Null;
            window.style.scale = StyleKeyword.Null;
            window.style.opacity = StyleKeyword.Null;
            window.SetEnabled(true);
        }
    }
}

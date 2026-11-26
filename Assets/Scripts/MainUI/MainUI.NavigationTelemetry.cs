using System;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.MainUI
{
    public sealed partial class MainUI
    {
        private const float NavigationRevealDuration = 1.35f;
        private const float InitialNavigationRevealDuration = 4.6f;
        private const float NavigationNoticeY = 120f;
        private float navigationRevealAge = -1f;
        private bool initialNavigationReveal;
        private VisualElement clockSyncAccent, coordinateSyncAccent;
        private Label navigationSyncNotice, clockSyncNotice, coordinateSyncNotice;

        internal void QueueNavigationTelemetryReveal()
        {
            if (navigationRevealAge >= 0f) HideNavigationReveal();
            navigationRevealAge = 0f;
        }

        private void UnlockNavigationTelemetry()
        {
            if (!navigationTelemetryUnavailable) return;
            navigationTelemetryUnavailable = false;
            VisualElement row = latitudeLabel.parent;
            row.style.width = StyleKeyword.Null;
            row.style.alignSelf = StyleKeyword.Null;
        }

        private void AdvanceNavigationTelemetryReveal(float deltaTime)
        {
            // The map and log console release the HUD before the plug finishes retracting.
            if (navigationRevealAge < 0f) return;
            if (!CanPresentRuinName || HeightMapPlayerSceneBootstrap.StationPlugOwnsInput)
            {
                HideNavigationReveal();
                return;
            }
            if (navigationRevealAge == 0f)
            {
                initialNavigationReveal = navigationTelemetryUnavailable;
                UnlockNavigationTelemetry();
                RefreshGameClock(CurrentGameTime, true);
                RefreshTelemetry(DateTime.Now, true);
            }

            navigationRevealAge += deltaTime;
            float duration = initialNavigationReveal ? InitialNavigationRevealDuration : NavigationRevealDuration;
            if (navigationRevealAge >= duration)
            {
                navigationRevealAge = -1f;
                HideNavigationReveal();
                return;
            }

            VisualElement row = latitudeLabel.parent;
            float accentOpacity = NavigationAccentOpacity;
            if (accentOpacity > 0f)
            {
                clockSyncAccent ??= CreateSyncAccent(DrawClockSyncAccent);
                coordinateSyncAccent ??= CreateSyncAccent(DrawCoordinateSyncAccent);
                if (clockSyncAccent.parent != time) time.Add(clockSyncAccent);
                if (coordinateSyncAccent.parent != row) row.Add(coordinateSyncAccent);
                clockSyncAccent.MarkDirtyRepaint();
                coordinateSyncAccent.MarkDirtyRepaint();
            }
            else ClearNavigationAccents();

            float noticeOpacity = FadeNavigationCue(NavigationRevealDuration);
            if (noticeOpacity > 0f)
            {
                navigationSyncNotice ??= CreateNavigationSyncNotice();
                if (navigationSyncNotice.parent != stage) stage.Add(navigationSyncNotice);
                navigationSyncNotice.style.top = viewportOffset.y + NavigationNoticeY * fit;
                navigationSyncNotice.style.scale = new Scale(new Vector3(fit, fit, 1f));
                navigationSyncNotice.style.opacity = noticeOpacity;
            }
            else navigationSyncNotice?.RemoveFromHierarchy();

            if (initialNavigationReveal)
            {
                clockSyncNotice ??= CreateTelemetrySyncBadge("ClockSyncNotice", "TIME DATA SYNCED", aboveReadout: false);
                coordinateSyncNotice ??= CreateTelemetrySyncBadge("CoordinateSyncNotice", "COORDINATE DATA SYNCED", aboveReadout: true);
                UpdateTelemetrySyncBadge(clockSyncNotice, time, .08f);
                UpdateTelemetrySyncBadge(coordinateSyncNotice, row, .24f);
            }

            if (accentOpacity > 0f)
            {
                Color highlight = Color.Lerp(HudDrawing.Yellow, Color.white, accentOpacity * .85f);
                dateLabel.style.color = latitudeLabel.style.color = longitudeLabel.style.color = highlight;
            }
        }

        private void HideNavigationReveal()
        {
            ClearNavigationAccents();
            navigationSyncNotice?.RemoveFromHierarchy();
            clockSyncNotice?.RemoveFromHierarchy();
            coordinateSyncNotice?.RemoveFromHierarchy();
        }

        private void ClearNavigationAccents()
        {
            clockSyncAccent?.RemoveFromHierarchy();
            coordinateSyncAccent?.RemoveFromHierarchy();
            dateLabel.style.color = latitudeLabel.style.color = longitudeLabel.style.color = StyleKeyword.Null;
        }

        private float NavigationAccentOpacity => FadeNavigationCue(initialNavigationReveal ? 2.25f : NavigationRevealDuration);

        private float FadeNavigationCue(float end) => Mathf.SmoothStep(0f, 1f, navigationRevealAge / .08f)
            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.60f, end, navigationRevealAge)));

        private static Label CreateNavigationSyncNotice()
        {
            var notice = new Label("DATA ACQUIRED") { name = "NavigationSyncNotice", pickingMode = PickingMode.Ignore };
            notice.AddToClassList("driving-control");
            notice.style.position = Position.Absolute;
            notice.style.left = Length.Percent(50f);
            notice.style.translate = new Translate(Length.Percent(-50f), 0f);
            notice.style.paddingLeft = notice.style.paddingRight = 12f;
            notice.style.color = HudDrawing.Yellow;
            return notice;
        }

        private static Label CreateTelemetrySyncBadge(string name, string text, bool aboveReadout)
        {
            var badge = new Label(text) { name = name, pickingMode = PickingMode.Ignore };
            badge.AddToClassList("driving-control");
            badge.style.position = Position.Absolute;
            badge.style.right = aboveReadout ? 0f : 25f;
            if (aboveReadout)
            {
                badge.style.bottom = Length.Percent(100f);
                badge.style.marginBottom = 8f;
            }
            else badge.style.top = 204f;
            badge.style.color = HudDrawing.Yellow;
            badge.style.fontSize = 20f;
            badge.style.paddingLeft = badge.style.paddingRight = 10f;
            badge.style.borderBottomWidth = 1f;
            badge.style.borderBottomColor = HudDrawing.Yellow;
            badge.style.whiteSpace = WhiteSpace.NoWrap;
            return badge;
        }

        private void UpdateTelemetrySyncBadge(Label badge, VisualElement parent, float delay)
        {
            if (badge.parent != parent) parent.Add(badge);
            float appear = Mathf.SmoothStep(0f, 1f, (navigationRevealAge - delay) / .28f);
            float fade = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(3.6f, InitialNavigationRevealDuration, navigationRevealAge));
            badge.style.translate = new Translate(14f * (1f - appear), 0f);
            badge.style.opacity = appear * fade;
        }

        private static VisualElement CreateSyncAccent(Action<MeshGenerationContext> draw)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute;
            element.style.left = element.style.right = element.style.top = element.style.bottom = 0f;
            element.generateVisualContent += draw;
            return element;
        }

        private void DrawClockSyncAccent(MeshGenerationContext context)
        {
            float sweep = Mathf.Clamp01(navigationRevealAge / (initialNavigationReveal ? 1.1f : .74f));
            float radius = Mathf.Lerp(timeOrbit.radius + 10f, timeOrbit.radius + (initialNavigationReveal ? 38f : 29f), sweep);
            Color gold = HudDrawing.Yellow;
            gold.a = NavigationAccentOpacity;
            Painter2D painter = context.painter2D;
            painter.lineWidth = initialNavigationReveal ? 3.5f : 2f;
            painter.strokeColor = gold;
            Vector2 center = clockSyncAccent.contentRect.center;
            float angle = -90f + sweep * 360f;
            HudDrawing.Arc(painter, center, radius, radius, angle - 105f, 105f);
            gold.a *= .35f;
            painter.strokeColor = gold;
            HudDrawing.Arc(painter, center, radius, radius, angle, 255f);
            if (initialNavigationReveal)
            {
                float pulse = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.24f, 1.5f, navigationRevealAge));
                float rippleRadius = timeOrbit.radius + Mathf.Lerp(4f, 31f, pulse);
                painter.lineWidth = 2f;
                HudDrawing.Arc(painter, center, rippleRadius, rippleRadius, -90f, 360f);
            }
        }

        private void DrawCoordinateSyncAccent(MeshGenerationContext context)
        {
            Rect rect = coordinateSyncAccent.contentRect;
            float sweep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.08f, initialNavigationReveal ? 1.16f : .76f, navigationRevealAge));
            float scan = Mathf.Lerp(rect.xMin, rect.xMax, sweep);
            Color gold = HudDrawing.Yellow;
            gold.a = NavigationAccentOpacity;
            Painter2D painter = context.painter2D;
            painter.lineWidth = initialNavigationReveal ? 3.5f : 2f;
            if (initialNavigationReveal)
            {
                float tail = Mathf.Max(rect.xMin, scan - 56f);
                painter.fillColor = new Color(gold.r, gold.g, gold.b, gold.a * .17f);
                HudDrawing.Rect(painter, tail, rect.yMin - 4f, scan - tail, rect.height + 5f);
            }
            painter.strokeColor = gold;
            HudDrawing.Line(painter, new Vector2(scan, rect.yMin - 4f), new Vector2(scan, rect.yMax + 1f));
            gold.a *= .45f;
            painter.strokeColor = gold;
            HudDrawing.Line(painter, new Vector2(rect.xMin, rect.yMin - 4f), new Vector2(scan, rect.yMin - 4f));
            HudDrawing.Line(painter, new Vector2(rect.xMin, rect.yMax + 1f), new Vector2(scan, rect.yMax + 1f));
        }
    }
}

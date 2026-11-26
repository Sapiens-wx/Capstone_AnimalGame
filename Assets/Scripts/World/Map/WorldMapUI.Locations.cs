using System.Collections.Generic;
using AnimalGame.MainUI;
using AnimalGame.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.World.Map
{
    public sealed partial class WorldMapUI
    {
        private const float LocationRadius = 14f;
        private const float TowerLocationRadius = 20f;
        private const float LocationHoverPadding = 4f;
        private const float LocationNameInset = 4f;
        private static readonly Vector2[] locationFrame =
        {
            new(-11f, -6f), new(-6f, -11f), new(6f, -11f), new(11f, -6f),
            new(11f, 6f), new(6f, 11f), new(-6f, 11f), new(-11f, 6f)
        };

        private readonly List<SyncedLocation> syncedLocations = new();
        private Label locationName;

        private sealed class SyncedLocation
        {
            public readonly string Name;
            public readonly VisualElement Icon;
            private readonly Vector2 mapPosition;
            private readonly Vector2[] outline;
            private readonly Vector2[] screenOutline;
            private Vector2 drawOrigin, drawCenter;

            public SyncedLocation(Vector2 position, string name, string stationId, Vector2[] towerOutline)
            {
                outline = towerOutline;
                screenOutline = outline == null ? null : new Vector2[outline.Length];
                mapPosition = outline == null ? position : FootprintCenter(position, outline);
                Name = name;
                Icon = new VisualElement { name = "map-location-" + stationId, pickingMode = PickingMode.Ignore };
                Icon.style.position = Position.Absolute;
                Icon.generateVisualContent += DrawLocation;
            }

            public float Radius => outline == null ? LocationRadius : TowerLocationRadius;

            public bool Place(MapViewState view, Vector2 size, out Vector2 center)
            {
                center = view.ToScreen(mapPosition, size);
                Vector2 min = center - Vector2.one * Radius;
                Vector2 max = center + Vector2.one * Radius;
                if (outline != null)
                {
                    Vector2 padding = Vector2.one * 2f;
                    for (int i = 0; i < outline.Length; i++)
                    {
                        Vector2 point = view.ToScreen(outline[i], size);
                        screenOutline[i] = point;
                        min = Vector2.Min(min, point - padding);
                        max = Vector2.Max(max, point + padding);
                    }
                }

                bool visible = max.x >= 0f && max.y >= 0f && min.x <= size.x && min.y <= size.y;
                Icon.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) return false;

                drawOrigin = min;
                drawCenter = center - min;
                Icon.style.left = min.x;
                Icon.style.top = min.y;
                Icon.style.width = max.x - min.x;
                Icon.style.height = max.y - min.y;
                if (outline != null) Icon.MarkDirtyRepaint();
                return true;
            }

            public bool ContainsFootprint(Vector2 pointer)
            {
                if (screenOutline == null) return false;
                Vector2 from = screenOutline[^1];
                bool inside = false;
                foreach (Vector2 to in screenOutline)
                {
                    Vector2 edge = to - from;
                    float along = Mathf.Clamp01(Vector2.Dot(pointer - from, edge) / edge.sqrMagnitude);
                    if ((from + edge * along - pointer).sqrMagnitude <= LocationHoverPadding * LocationHoverPadding)
                        return true;
                    if ((from.y > pointer.y) != (to.y > pointer.y) && pointer.x < from.x + (pointer.y - from.y) * edge.x / edge.y)
                        inside = !inside;
                    from = to;
                }
                return inside;
            }

            private static Vector2 FootprintCenter(Vector2 origin, Vector2[] points)
            {
                // Keep the signed-area sums local to the building to avoid loss of precision on large maps.
                Vector2 from = points[^1] - origin;
                Vector2 center = Vector2.zero;
                float twiceArea = 0f;
                foreach (Vector2 point in points)
                {
                    Vector2 to = point - origin;
                    float cross = from.x * to.y - from.y * to.x;
                    center += (from + to) * cross;
                    twiceArea += cross;
                    from = to;
                }
                return origin + center / (3f * twiceArea);
            }

            private void DrawLocation(MeshGenerationContext context)
            {
                Painter2D painter = context.painter2D;
                if (screenOutline != null)
                {
                    Color fill = HudDrawing.Yellow;
                    fill.a = .14f;
                    painter.fillColor = fill;
                    TraceOutline(painter, screenOutline, -drawOrigin, 1f);
                    painter.Fill();
                    painter.strokeColor = HudDrawing.Yellow;
                    painter.lineWidth = 1.8f;
                    TraceOutline(painter, screenOutline, -drawOrigin, 1f);
                    painter.Stroke();
                }

                float scale = Radius / LocationRadius;
                painter.fillColor = Color.black;
                TraceOutline(painter, locationFrame, drawCenter, scale);
                painter.Fill();
                painter.strokeColor = HudDrawing.Yellow;
                painter.lineWidth = 1.4f * scale;
                TraceOutline(painter, locationFrame, drawCenter, scale);
                painter.Stroke();

                painter.strokeColor = Color.white;
                painter.lineWidth = 1.25f * scale;
                painter.BeginPath();
                painter.MoveTo(drawCenter + new Vector2(-6f, 1f) * scale);
                painter.LineTo(drawCenter + new Vector2(0f, -5f) * scale);
                painter.LineTo(drawCenter + new Vector2(6f, 1f) * scale);
                painter.MoveTo(drawCenter + new Vector2(-4f, -1f) * scale);
                painter.LineTo(drawCenter + new Vector2(-4f, 5f) * scale);
                painter.LineTo(drawCenter + new Vector2(4f, 5f) * scale);
                painter.LineTo(drawCenter + new Vector2(4f, -1f) * scale);
                painter.Stroke();
            }

            private static void TraceOutline(Painter2D painter, Vector2[] points, Vector2 origin, float scale)
            {
                painter.BeginPath();
                painter.MoveTo(origin + points[0] * scale);
                for (int i = 1; i < points.Length; i++) painter.LineTo(origin + points[i] * scale);
                painter.ClosePath();
            }
        }

        private void RebuildSyncedLocations(string surveyingStationId)
        {
            ClearSyncedLocations();
            var displayedIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var console in FindObjectsByType<StationLogConsole>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string id = console.StationId;
                if (console.gameObject.scene != gameObject.scene || string.IsNullOrWhiteSpace(id)
                    || id == surveyingStationId || !SaveSystem.IsStationSynced(id) || !displayedIds.Add(id)) continue;

                var ruin = console.GetComponentInParent<RuinDiscovery>(true);
                var tower = console.GetComponentInParent<WeatherTower>(true);
                Transform anchor = ruin != null ? ruin.transform : tower != null ? tower.transform : console.transform;
                string title = ruin != null && !string.IsNullOrWhiteSpace(ruin.DisplayName)
                    ? ruin.DisplayName : $"{console.StationName} {id}".Trim();
                Vector2[] towerOutline = null;
                if (tower != null && ruin != null && ruin.VisibleOutline is { Count: >= 3 } outline)
                {
                    towerOutline = new Vector2[outline.Count];
                    for (int i = 0; i < outline.Count; i++)
                        towerOutline[i] = WorldToMap(ruin.transform.TransformPoint(outline[i]));
                }
                var location = new SyncedLocation(WorldToMap(anchor.position), title, id, towerOutline);
                root.Insert(root.IndexOf(locationName), location.Icon);
                syncedLocations.Add(location);
            }
        }

        private void ClearSyncedLocations()
        {
            foreach (SyncedLocation location in syncedLocations) location.Icon.RemoveFromHierarchy();
            syncedLocations.Clear();
            locationName.style.display = DisplayStyle.None;
        }

        private void RefreshSyncedLocations(Vector2 size)
        {
            Vector2 pointer = View.ToScreen(View.DisplayCursor, size);
            SyncedLocation hovered = null;
            Vector2 hoveredPosition = default;
            float gap = 0f;
            float nearest = float.PositiveInfinity;
            bool canHover = !centeringMapView && previousSurveyFog == null;
            foreach (SyncedLocation location in syncedLocations)
            {
                if (!location.Place(View, size, out Vector2 position) || !canHover) continue;
                float distance = (position - pointer).sqrMagnitude;
                float hoverRadius = location.Radius + LocationHoverPadding;
                bool onBadge = distance <= hoverRadius * hoverRadius;
                if (distance > nearest || (!onBadge && !location.ContainsFootprint(pointer))) continue;
                nearest = distance;
                hovered = location;
                hoveredPosition = onBadge ? position : pointer;
                gap = onBadge ? location.Radius + 8f : 10f;
            }

            locationName.style.display = hovered != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (hovered == null) return;
            locationName.text = hovered.Name;
            var style = locationName.resolvedStyle;
            float horizontalInsets = style.paddingLeft + style.paddingRight + style.borderLeftWidth + style.borderRightWidth;
            float verticalInsets = style.paddingTop + style.paddingBottom + style.borderTopWidth + style.borderBottomWidth;
            Vector2 textSize = locationName.MeasureTextSize(hovered.Name, 0f, VisualElement.MeasureMode.Undefined,
                0f, VisualElement.MeasureMode.Undefined);
            // Leave one extra unit for pixel rounding after layout.
            float preferredWidth = Mathf.Ceil(textSize.x) + horizontalInsets + 1f;
            float width = Mathf.Min(preferredWidth, Mathf.Max(1f, size.x - 2f * LocationNameInset));
            if (width < preferredWidth)
                textSize = locationName.MeasureTextSize(hovered.Name, Mathf.Max(1f, width - horizontalInsets),
                    VisualElement.MeasureMode.Exactly, 0f, VisualElement.MeasureMode.Undefined);
            float height = Mathf.Ceil(textSize.y) + verticalInsets + 1f;
            float x = hoveredPosition.x + gap;
            if (x + width > size.x - LocationNameInset) x = hoveredPosition.x - gap - width;
            locationName.style.left = Mathf.Clamp(x, LocationNameInset, Mathf.Max(LocationNameInset, size.x - width - LocationNameInset));
            locationName.style.top = Mathf.Clamp(hoveredPosition.y - height * .5f,
                LocationNameInset, Mathf.Max(LocationNameInset, size.y - height - LocationNameInset));
            locationName.style.width = width;
            locationName.style.height = height;
        }
    }
}

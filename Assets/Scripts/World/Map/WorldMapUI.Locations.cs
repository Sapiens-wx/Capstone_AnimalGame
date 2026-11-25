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
        private const float LocationHoverRadius = 18f;
        private const float LocationNameInset = 4f;

        private readonly List<SyncedLocation> syncedLocations = new();
        private Label locationName;

        private sealed class SyncedLocation
        {
            public readonly Vector2 MapPosition;
            public readonly string Name;
            public readonly VisualElement Icon;

            public SyncedLocation(Vector2 position, string name, string stationId)
            {
                MapPosition = position;
                Name = name;
                Icon = new VisualElement { name = "map-location-" + stationId, pickingMode = PickingMode.Ignore };
                Icon.style.position = Position.Absolute;
                Icon.generateVisualContent += DrawLocation;
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
                var location = new SyncedLocation(WorldToMap(anchor.position), title, id);
                root.Insert(root.IndexOf(playerIcon), location.Icon);
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
            float nearest = LocationHoverRadius * LocationHoverRadius;
            bool canHover = !centeringMapView && previousSurveyFog == null;
            foreach (SyncedLocation location in syncedLocations)
            {
                Vector2 position = View.ToScreen(location.MapPosition, size);
                bool visible = position.x >= -LocationRadius && position.x <= size.x + LocationRadius
                    && position.y >= -LocationRadius && position.y <= size.y + LocationRadius;
                location.Icon.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;

                PositionCircle(location.Icon, position, LocationRadius);
                float distance = (position - pointer).sqrMagnitude;
                if (!canHover || distance > nearest) continue;
                nearest = distance;
                hovered = location;
                hoveredPosition = position;
            }

            locationName.style.display = hovered != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (hovered == null) return;
            locationName.text = hovered.Name;
            Vector2 labelSize = locationName.MeasureTextSize(hovered.Name, 0f, VisualElement.MeasureMode.Undefined,
                0f, VisualElement.MeasureMode.Undefined) + new Vector2(20f, 10f);
            labelSize.x = Mathf.Min(labelSize.x, Mathf.Max(1f, size.x - 2f * LocationNameInset));
            float gap = LocationRadius + 8f;
            float x = hoveredPosition.x + gap;
            if (x + labelSize.x > size.x - LocationNameInset) x = hoveredPosition.x - gap - labelSize.x;
            locationName.style.left = Mathf.Clamp(x, LocationNameInset, Mathf.Max(LocationNameInset, size.x - labelSize.x - LocationNameInset));
            locationName.style.top = Mathf.Clamp(hoveredPosition.y - labelSize.y * .5f,
                LocationNameInset, Mathf.Max(LocationNameInset, size.y - labelSize.y - LocationNameInset));
            locationName.style.width = labelSize.x;
            locationName.style.height = labelSize.y;
        }

        private static void DrawLocation(MeshGenerationContext context)
        {
            Vector2 center = context.visualElement.contentRect.center;
            Vector2[] frame =
            {
                center + new Vector2(-11f, -6f), center + new Vector2(-6f, -11f),
                center + new Vector2(6f, -11f), center + new Vector2(11f, -6f),
                center + new Vector2(11f, 6f), center + new Vector2(6f, 11f),
                center + new Vector2(-6f, 11f), center + new Vector2(-11f, 6f)
            };
            Painter2D painter = context.painter2D;
            painter.fillColor = Color.black;
            HudDrawing.Polygon(painter, frame, true);
            painter.strokeColor = HudDrawing.Yellow;
            painter.lineWidth = 1.4f;
            HudDrawing.Polygon(painter, frame);

            painter.strokeColor = Color.white;
            painter.lineWidth = 1.25f;
            painter.BeginPath();
            painter.MoveTo(center + new Vector2(-6f, 1f));
            painter.LineTo(center + new Vector2(0f, -5f));
            painter.LineTo(center + new Vector2(6f, 1f));
            painter.MoveTo(center + new Vector2(-4f, -1f));
            painter.LineTo(center + new Vector2(-4f, 5f));
            painter.LineTo(center + new Vector2(4f, 5f));
            painter.LineTo(center + new Vector2(4f, -1f));
            painter.Stroke();
        }
    }
}

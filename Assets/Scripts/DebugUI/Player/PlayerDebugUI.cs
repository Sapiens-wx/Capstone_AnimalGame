using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.AnimalDebug
{
    [DefaultExecutionOrder(400)]
    [DisallowMultipleComponent, RequireComponent(typeof(DebugUI))]
    public sealed class PlayerDebugUI : DebugUITab
    {
        public override string Title => "Player";
        public override VisualElement Content => content;
        private VisualElement root, content, marker;
        private Toggle collision, detection, overrideSpeed;
        private Vector2Field coordinates;
        private FloatField speed;
        private Label message;
        private Button teleport;
        private RobotMover player;
        private MapTestSceneController map;
        private Vector2 coordinateValue;
        private Vector3 targetWorld;
        private bool shown, hasCoordinates, validTarget;

        public override void Bind(VisualElement nextRoot)
        {
            root = nextRoot;
            content = new ScrollView { name = "PlayerPanel" };
            content.style.flexGrow = 1;
            content.style.minHeight = 0;
            collision = new Toggle("Ignore collision + height map") { name = "IgnoreCollisionAndTerrain" };
            detection = new Toggle("Disable player detection") { name = "DisablePlayerDetection" };
            coordinates = new Vector2Field("Position (longitude, latitude)") { name = "PlayerCoordinates", value = coordinateValue };
            teleport = new Button(Teleport) { name = "TeleportPlayer", text = "Teleport" };
            overrideSpeed = new Toggle("Override player speed") { name = "OverridePlayerSpeed" };
            speed = new FloatField("Speed") { name = "PlayerSpeed" };
            speed.style.display = DisplayStyle.None;
            message = new Label { name = "PlayerDebugMessage" };
            message.AddToClassList("message");
            foreach (VisualElement control in new VisualElement[] { collision, detection, coordinates, teleport, overrideSpeed, speed, message })
            {
                control.AddToClassList("detail");
                content.Add(control);
            }
            collision.RegisterValueChangedCallback(evt => player.SetIgnoreCollisionAndTerrain(evt.newValue));
            detection.RegisterValueChangedCallback(evt =>
            {
                player.IgnoreAnimalDetection = evt.newValue;
                if (!evt.newValue) return;
                foreach (var animal in AnimalGame.Animals.AnimalAgent.ActiveAgents)
                    if (animal != null && animal.gameObject.scene == player.gameObject.scene && animal.IsInitialized)
                    {
                        bool noticedPlayer = animal.Runtime.Threats.Contains(player.transform);
                        bool hasThreat = animal.Perception.TryGetThreatProximity(out _);
                        if (noticedPlayer && !hasThreat && (animal.CurrentState == AnimalGame.Animals.AnimalState.Curious
                            || animal.CurrentState == AnimalGame.Animals.AnimalState.Fleeing
                            || animal.CurrentState == AnimalGame.Animals.AnimalState.Aggressive)) animal.ReturnToDaily();
                    }
            });
            coordinates.RegisterValueChangedCallback(evt =>
            {
                coordinateValue = evt.newValue;
                hasCoordinates = true;
                UpdateTarget();
            });
            overrideSpeed.RegisterValueChangedCallback(evt =>
            {
                speed.SetValueWithoutNotify(player.DefaultMoveSpeed);
                player.DebugMoveSpeed = evt.newValue ? speed.value : (float?)null;
                speed.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
            });
            speed.RegisterValueChangedCallback(evt =>
            {
                float value = Mathf.Max(0, evt.newValue);
                speed.SetValueWithoutNotify(value);
                player.DebugMoveSpeed = value;
            });
            marker = new VisualElement { name = "PlayerPositionMarker", pickingMode = PickingMode.Ignore };
            marker.style.position = Position.Absolute;
            marker.style.width = marker.style.height = 28;
            marker.style.display = DisplayStyle.None;
            marker.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                painter.strokeColor = new Color32(0xD4, 0xB8, 0x54, 0xFF);
                painter.lineWidth = 3;
                painter.BeginPath();
                painter.MoveTo(new Vector2(2, 14)); painter.LineTo(new Vector2(26, 14));
                painter.MoveTo(new Vector2(14, 2)); painter.LineTo(new Vector2(14, 26));
                painter.Stroke();
            };
            root.Add(content);
            root.Add(marker);
        }

        public override void Show()
        {
            shown = true;
            foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
            {
                if (player == null) player = sceneRoot.GetComponentInChildren<RobotMover>();
                if (map == null) map = sceneRoot.GetComponentInChildren<MapTestSceneController>();
            }
            content.style.display = DisplayStyle.Flex;
            content.SetEnabled(player != null && map != null);
            if (player == null || map == null) { message.text = "No player or map in this scene."; return; }
            collision.SetValueWithoutNotify(player.IgnoreCollisionAndTerrain);
            detection.SetValueWithoutNotify(player.IgnoreAnimalDetection);
            overrideSpeed.SetValueWithoutNotify(player.DebugMoveSpeed.HasValue);
            speed.SetValueWithoutNotify(player.DebugMoveSpeed ?? player.DefaultMoveSpeed);
            speed.style.display = player.DebugMoveSpeed.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasCoordinates && map.TrySampleGeographicWorldPosition(player.transform.position, out float latitude, out float longitude, out _))
            {
                coordinateValue = new Vector2(longitude, latitude);
                coordinates.SetValueWithoutNotify(coordinateValue);
                hasCoordinates = true;
            }
            UpdateTarget();
        }

        private void UpdateTarget()
        {
            validTarget = map.TryGeographicToWorldPosition(coordinateValue, out targetWorld);
            teleport.SetEnabled(validTarget);
            message.text = validTarget ? "" : "Coordinates are outside this map.";
        }

        private void Teleport() => player.Teleport(targetWorld);

        private void LateUpdate()
        {
            if (marker == null) return;
            marker.style.display = DisplayStyle.None;
            if (!shown || !validTarget || root.panel == null) return;
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            Camera camera = bootstrap != null && bootstrap.gameObject.scene == gameObject.scene ? bootstrap.mapCamera : Camera.main;
            if (camera == null) return;
            Vector3 screen = camera.WorldToScreenPoint(targetWorld);
            if (screen.z <= 0) return;
            Vector2 point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            marker.style.left = point.x - 14; marker.style.top = point.y - 14;
            marker.style.display = DisplayStyle.Flex;
        }

        public override void Hide()
        {
            shown = false;
            content.style.display = DisplayStyle.None;
            marker.style.display = DisplayStyle.None;
        }
        public override bool Back() => false;
        public override void HandleInput(int move, bool submit, bool click, Vector2 mouse) { }
    }
}

using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Demos.WasteRobot
{
    // Lives only on the independent Demo scene, never on a production bootstrap.
    [DefaultExecutionOrder(-100)]
    public sealed class WasteRobotDemoScene : MonoBehaviour
    {
        [SerializeField] private WasteRobotBase machine;
        [SerializeField] private RobotMover player;
        [SerializeField] private Camera demoCamera;
        [SerializeField] private bool showCollisionOverlay;

        public WasteRobotBase Machine => machine;
        public RobotMover Player => player;
        public Camera DemoCamera => demoCamera;

        public void Configure(WasteRobotBase value, RobotMover robot, Camera camera)
        {
            machine = value;
            player = robot;
            demoCamera = camera;
        }

        private void Start()
        {
            if (machine == null || player == null || demoCamera == null)
            {
                Debug.LogError("Waste Robot Demo needs explicit machine/player/camera references.", this);
                enabled = false;
                return;
            }
            player.SetTraversalEvaluator(null);
            RobotHeightMotionDetector height = player.GetComponent<RobotHeightMotionDetector>();
            height?.Initialize(null);
            RobotBalanceController balance = player.GetComponent<RobotBalanceController>();
            RobotCameraFollow follow = demoCamera.GetComponent<RobotCameraFollow>();
            if (balance != null) follow?.FollowBalanceTarget(balance);
            else if (follow != null) follow.Target = player.transform;
            follow?.SnapToTarget();
            demoCamera.GetComponent<RobotCameraShake>()?.Initialize(player, balance, height);
            machine.Bind(player.transform);
        }

        private void OnGUI()
        {
            if (machine == null || demoCamera == null) return;
            GUI.Box(new Rect(12, 12, 330, 132), "Waste Robot traversal demo");
            GUI.Label(new Rect(24, 40, 308, 22),
                (machine.IsInside ? "INTERIOR" : "EXTERIOR") + " | transitions: " + machine.TransitionCount);
            GUI.Label(new Rect(24, 63, 308, 22), "W/S drive, A/D steer; cross the TOP dashed entry.");
            showCollisionOverlay = GUI.Toggle(new Rect(24, 91, 300, 24),
                showCollisionOverlay, "Show active collision outlines");
            if (showCollisionOverlay) DrawCollisionOverlay();
        }

        private void DrawCollisionOverlay()
        {
            GameObject root = machine.IsInside ? machine.InteriorRoot : machine.ExteriorRoot;
            if (root == null) return;
            Color savedColor = GUI.color;
            GUI.color = new Color(.3f, 1f, .7f, .8f);
            foreach (WorldInteraction wall in root.GetComponentsInChildren<WorldInteraction>())
            {
                InteractionShape shape = wall.GetShape(null);
                if (shape.IsBox)
                {
                    for (int i = 0; i < 4; i++) ScreenLine(shape.Vertex(i), shape.Vertex((i + 1) % 4));
                }
                else
                {
                    Vector2 previous = shape.Center + Vector2.right * shape.Radius;
                    for (int i = 1; i <= 24; i++)
                    {
                        float angle = i * Mathf.PI * 2f / 24f;
                        Vector2 next = shape.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * shape.Radius;
                        ScreenLine(previous, next);
                        previous = next;
                    }
                }
            }
            GUI.color = Color.yellow;
            ScreenLine(machine.PortalToWorld(new Vector2(-machine.EntranceWidth * .5f, 0f)),
                machine.PortalToWorld(new Vector2(machine.EntranceWidth * .5f, 0f)));
            GUI.color = savedColor;
        }

        private void ScreenLine(Vector2 a, Vector2 b)
        {
            Vector3 ap = demoCamera.WorldToScreenPoint(a);
            Vector3 bp = demoCamera.WorldToScreenPoint(b);
            if (ap.z <= 0f || bp.z <= 0f) return;
            Vector2 from = new Vector2(ap.x, Screen.height - ap.y);
            Vector2 to = new Vector2(bp.x, Screen.height - bp.y);
            Vector2 delta = to - from;
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y - .75f, delta.magnitude, 1.5f), Texture2D.whiteTexture);
            GUI.matrix = saved;
        }
    }
}


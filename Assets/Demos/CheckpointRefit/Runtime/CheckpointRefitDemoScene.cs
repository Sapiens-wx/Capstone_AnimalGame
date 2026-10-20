using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.Demos.CheckpointRefit
{
    /// <summary>Explicit bindings for the independent flat-ground demo only.</summary>
    [DefaultExecutionOrder(-1100)]
    public sealed class CheckpointRefitDemoScene : MonoBehaviour
    {
        [SerializeField] private CheckpointRefitTerminal terminal;
        [SerializeField] private RobotMover player;
        [SerializeField] private Camera demoCamera;

        public void Configure(CheckpointRefitTerminal station, RobotMover robot, Camera camera)
        {
            terminal = station;
            player = robot;
            demoCamera = camera;
        }

        private void Start()
        {
            if (terminal == null || player == null || demoCamera == null)
            {
                Debug.LogError("Checkpoint refit demo requires its terminal, player and camera bindings.", this);
                enabled = false;
                return;
            }
            player.SetTraversalEvaluator(null);
            var height = player.GetComponent<RobotHeightMotionDetector>();
            height?.Initialize(null);
            var balance = player.GetComponent<RobotBalanceController>();
            var follow = demoCamera.GetComponent<RobotCameraFollow>();
            if (balance != null) follow?.FollowBalanceTarget(balance);
            else if (follow != null) follow.Target = player.transform;
            follow?.SnapToTarget();
            demoCamera.GetComponent<RobotCameraShake>()?.Initialize(player, balance, height);
            terminal.Bind(player);
        }
    }
}

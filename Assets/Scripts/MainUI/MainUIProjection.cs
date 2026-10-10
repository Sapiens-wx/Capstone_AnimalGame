using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.MainUI
{
    /// <summary>Moves the rendered optical center without changing the camera's position or follow target.</summary>
    [DefaultExecutionOrder(275)]
    [DisallowMultipleComponent]
    public sealed class MainUIProjection : MonoBehaviour
    {
        private MainUI hud;
        private Camera shiftedCamera;
        private Camera targetCamera;
        private bool applied;

        private void Awake() => hud = GetComponent<MainUI>();
        public void Initialize(Camera camera) => targetCamera = camera;
        private void LateUpdate()
        {
            if (hud == null) return;
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            Camera camera = targetCamera != null ? targetCamera : bootstrap != null ? bootstrap.mapCamera : null;
            if (camera == null || !camera.orthographic) { Restore(); return; }
            if (shiftedCamera != camera) { Restore(); shiftedCamera = camera; }
            if (!hud.IsInventoryOpen && hud.InventoryProgress == 0) { Restore(); return; }
            if (!hud.TryGetRingScreenGeometry(out Vector2 center, out _)) return;
            camera.ResetProjectionMatrix();
            Rect rect = camera.pixelRect;
            Matrix4x4 projection = camera.projectionMatrix;
            projection.m03 += (center.x - rect.center.x) * 2 / rect.width;
            projection.m13 += (center.y - rect.center.y) * 2 / rect.height;
            camera.projectionMatrix = projection;
            applied = true;
        }
        private void Restore()
        {
            if (applied && shiftedCamera != null) shiftedCamera.ResetProjectionMatrix();
            applied = false;
        }
        private void OnDisable() => Restore();
    }
}

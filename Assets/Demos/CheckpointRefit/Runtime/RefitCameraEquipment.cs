using System.Collections.Generic;
using AnimalGame.Animals;
using AnimalGame.RobotMap;
using UnityEngine;

namespace AnimalGame.Demos.CheckpointRefit
{
    /// <summary>Player-owned camera availability shared by the refit terminals used during this session.</summary>
    [DisallowMultipleComponent]
    public sealed class RefitCameraEquipment : MonoBehaviour
    {
        [SerializeField] private List<PhotoCameraDefinition> unlockedCameras = new List<PhotoCameraDefinition>();

        private PhotoModeController controller;

        // A refit gameplay lease temporarily disables the controller without relinquishing its camera.
        public bool IsAvailable => controller != null;
        public PhotoCameraDefinition CurrentCamera => controller != null ? controller.CurrentCamera : null;

        public void Configure(PhotoModeController photographicController, PhotoCameraDefinition initiallyUnlocked)
        {
            controller = photographicController;
            Unlock(initiallyUnlocked);
            Unlock(CurrentCamera);
        }

        public bool IsUnlocked(PhotoCameraDefinition camera)
            => camera != null && unlockedCameras != null && unlockedCameras.Contains(camera);

        public bool IsInstalled(PhotoCameraDefinition camera)
            => camera != null && controller != null && controller.CurrentCamera == camera;

        public void Unlock(PhotoCameraDefinition camera)
        {
            if (camera == null) return;
            if (unlockedCameras == null) unlockedCameras = new List<PhotoCameraDefinition>();
            if (!unlockedCameras.Contains(camera)) unlockedCameras.Add(camera);
        }

        public bool TryEquip(PhotoCameraDefinition camera)
        {
            if (controller == null || !IsUnlocked(camera)) return false;
            controller.SetCamera(camera);
            return true;
        }
    }
}

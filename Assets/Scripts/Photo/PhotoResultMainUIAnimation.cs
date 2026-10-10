using UnityEngine;

namespace AnimalGame.RobotMap
{
    /// <summary>Applies the existing photo result animation tracks to the retained Toolkit HUD.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AnimalGame.MainUI.MainUI))]
    public sealed class PhotoResultMainUIAnimation : MonoBehaviour
    {
        private AnimalGame.MainUI.MainUI hud;
        private float relativeScale = 1;
        private Vector2 designSize = new Vector2(1920, 1080);
        private Vector2 displacement;

        private void Awake() => hud = GetComponent<AnimalGame.MainUI.MainUI>();
        public void SetZoomPose(float scale, Vector2 design, Vector2 offset)
        {
            relativeScale = scale; designSize = design; displacement = offset;
        }
        public void RevealAnimation(float value)
        {
            if (hud == null) hud = GetComponent<AnimalGame.MainUI.MainUI>();
            if (hud != null) hud.SetPhotoPose(relativeScale, designSize, displacement, 1 - Mathf.Clamp01(value));
        }
        public void Restore()
        {
            relativeScale = 1; displacement = Vector2.zero;
            if (hud != null) hud.SetPhotoPose(1, designSize, Vector2.zero, 1);
        }
        private void OnDisable() => Restore();
    }
}

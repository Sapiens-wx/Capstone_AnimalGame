using UnityEngine;

namespace AnimalGame.RobotArm
{
    public sealed partial class RobotArmController
    {
        [SerializeField] private Material stationPlugLipMaterial;
        private static readonly int SocketPlaneId = Shader.PropertyToID("_SocketPlane");
        private Material originalPlugHandMaterial;
        private MaterialPropertyBlock plugProperties;

        private void ShowStationPlugArt(Arm arm)
        {
            if (arm.HandSprite.sprite == stationPlugSprite) return;
            arm.HandSprite.sprite = stationPlugSprite;
            originalPlugHandMaterial = arm.HandSprite.sharedMaterial;
            plugProperties ??= new MaterialPropertyBlock();
            arm.HandSprite.GetPropertyBlock(plugProperties);
            plugProperties.SetVector(SocketPlaneId, new Vector4(
                stationSocketWorld.x, stationSocketWorld.y, stationInsertionWorld.x, stationInsertionWorld.y));
            arm.HandSprite.SetPropertyBlock(plugProperties);
        }

        private void SetStationPlugLip(bool active)
        {
            Material material = active ? stationPlugLipMaterial : originalPlugHandMaterial;
            if (stationPlugArm.HandSprite.sharedMaterial != material)
                stationPlugArm.HandSprite.sharedMaterial = material;
        }

        private void RestoreStationClawArt(Arm arm)
        {
            if (arm.HandSprite.sprite != stationPlugSprite) return;
            arm.HandSprite.sprite = arm.OpenHandSprite;
            arm.HandSprite.sharedMaterial = originalPlugHandMaterial;
            arm.HandSprite.SetPropertyBlock(null);
            originalPlugHandMaterial = null;
        }
    }
}

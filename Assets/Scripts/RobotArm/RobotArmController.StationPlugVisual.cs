using UnityEngine;

namespace AnimalGame.RobotArm
{
    public sealed partial class RobotArmController
    {
        [SerializeField] private Material stationPlugLipMaterial;
        private static readonly int SocketPlaneId = Shader.PropertyToID("_SocketPlane");
        private Material originalPlugHandMaterial;
        private MaterialPropertyBlock plugProperties;

        private void BeginStationPlugVisual(Arm arm, Vector2 worldSocket, Vector2 worldAxis)
        {
            originalPlugHandMaterial = arm.HandSprite.sharedMaterial;
            plugProperties ??= new MaterialPropertyBlock();
            arm.HandSprite.GetPropertyBlock(plugProperties);
            plugProperties.SetVector(SocketPlaneId, new Vector4(
                worldSocket.x, worldSocket.y, worldAxis.x, worldAxis.y));
            arm.HandSprite.SetPropertyBlock(plugProperties);
        }

        private void SetStationPlugLip(bool active)
        {
            Material material = active ? stationPlugLipMaterial : originalPlugHandMaterial;
            if (stationPlugArm.HandSprite.sharedMaterial != material)
                stationPlugArm.HandSprite.sharedMaterial = material;
        }

        private void EndStationPlugVisual(Arm arm)
        {
            arm.HandSprite.sharedMaterial = originalPlugHandMaterial;
            arm.HandSprite.SetPropertyBlock(null);
            originalPlugHandMaterial = null;
        }
    }
}

using UnityEngine;

namespace AnimalGame.RobotArm
{
    public sealed partial class RobotArmController
    {
        private static readonly Color OpeningJamColor = new Color32(0xD4, 0xB8, 0x54, 0xFF);
        private bool openingArmPresentation;

        // Play the real arm artwork directly from the intro's authored poses.
        // Keep deployment state, the collision plan and held objects out of the presentation.
        public void SetOpeningPresentation(float leftExtension, float rightExtension, float jam)
        {
            EnsureVisuals();
            openingArmPresentation = true;

            DrawOpeningArm(left, Mathf.Clamp01(leftExtension), 0f);
            DrawOpeningArm(right, Mathf.Clamp01(rightExtension), Mathf.Clamp01(jam));
        }

        public void EndOpeningPresentation()
        {
            if (!openingArmPresentation) return;
            openingArmPresentation = false;
            ApplyOpeningArmTint(left, 1f, 0f);
            ApplyOpeningArmTint(right, 1f, 0f);
            ApplyVisuals(left);
            ApplyVisuals(right);
        }

        private void DrawOpeningArm(Arm arm, float extension, float jam)
        {
            if (extension <= 0f)
            {
                ApplyVisuals(arm, default, false);
                return;
            }

            // The prototype's two angles are in screen coordinates, measured from
            // the outward horizontal. Convert to the rig's up-relative angles.
            float shoulder = (1.29f - 1.58f * extension) * Mathf.Rad2Deg;
            float forearm = (-1.70f + .26f * extension) * Mathf.Rad2Deg;
            var pose = new Pose
            {
                LowerAngle = arm.Side * (-90f - shoulder),
                UpperAngle = arm.Side * (-90f - forearm),
                LowerLength = arm.LowerLength,
                UpperLength = arm.UpperLength,
                Hand = Mathf.Lerp(.68f, 1f, extension)
            };
            ApplyVisuals(arm, pose, true);
            ApplyOpeningArmTint(arm, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.01f, .17f, extension)), jam);
        }

        private void ApplyOpeningArmTint(Arm arm, float opacity, float jam)
        {
            Color color = armColor;
            color.a *= opacity;
            Color jamColor = OpeningJamColor;
            jamColor.a = color.a;
            arm.LowerSprite.color = Color.Lerp(color, jamColor, jam * .6f);
            arm.UpperSprite.color = color;
            arm.LoopSprite.color = Color.Lerp(color, jamColor, jam);
            arm.HandSprite.color = color;
        }
    }
}

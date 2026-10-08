using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(fileName = "BasicPhotoRenderSet", menuName = "Animal Game/Photos/Render Sets/Basic")]
    public sealed class BasicPhotoRenderSet : PhotoRenderSet
    {
        [Tooltip("Multiplies the existing per-photo saturation. A value of one preserves the original processing.")]
        [SerializeField, Range(0, 2)] private float saturation = 1;

        public float Saturation => Mathf.Clamp(saturation, 0, 2);

        public override PhotoRenderSnapshot Capture(int seed)
        {
            return new BasicPhotoRenderSnapshot(SetId, Version, seed, Saturation);
        }
    }
}

using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(fileName = "InstantFilmPhotoRenderSet", menuName = "Animal Game/Photos/Render Sets/Instant Film")]
    public sealed class InstantFilmPhotoRenderSet : PhotoRenderSet
    {
        [SerializeField] private InstantFilmSettings settings = InstantFilmSettings.Default;
        public InstantFilmSettings Settings => settings.Sanitized();

        public override PhotoRenderSnapshot Capture(int seed)
        {
            return new InstantFilmPhotoRenderSnapshot(SetId, Version, seed, Settings);
        }
    }
}

using UnityEngine;
namespace AnimalGame.Animals
{
    [CreateAssetMenu(menuName = "Animal Game/Animals/Animal Info")]
    public sealed class AnimalInfo : ScriptableObject
    {
        public string Region;
        public string ShoulderHeight, TotalLength, BodyMass;
        [TextArea] public string Classification, Distribution, Habitat, ConservationStatus;
        [TextArea] public string Lifespan, Description, ActivityPattern, Diet, SocialBehavior;
        public Texture2D Illustration, DistributionMap;
        public Texture2D CatalogPreview;
    }
}

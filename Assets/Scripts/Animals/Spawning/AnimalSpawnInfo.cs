using UnityEngine;

namespace AnimalGame.Animals
{
    [CreateAssetMenu(fileName = "AnimalSpawnInfo", menuName = "Animal Game/Animals/Animal Spawn Info")]
    public sealed class AnimalSpawnInfo : ScriptableObject
    {
        [Range(0f, 1f)] public float BaseSpawnProbability;
        [Range(0f, 1f)] public float ProbabilityReduction;
        public int TerrainValue;
    }
}

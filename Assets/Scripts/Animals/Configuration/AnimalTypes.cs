using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    public enum AnimalDailyBehaviourKind
    {
        EatAtNearbyPlant,
        RoamAndLook,
        TravelToPlantAndEat,
        DiveAndResurface,
        PerchAtTree,
        FlyToTree,
        PeckAtTree,
        Idle,
        Hunt,
        TreeActivity
    }

    public enum AnimalFoodType
    {
        Bush,
        Lotus,
        Australis
    }

    [Serializable]
    public sealed class AnimalDailyBehaviourSettings
    {
        [SerializeField] private AnimalDailyBehaviourKind behaviour;
        [SerializeField, Min(0f)] private float selectionWeight = 1f;
        [Tooltip("Used by actions that read the daily entry duration. Tree bird actions use the duration in their Action asset.")]
        [SerializeField] private Vector2 durationSeconds = new Vector2(3f, 6f);
        [SerializeField] private AnimalActionDefinition action;
        [SerializeField, Min(0f)] private float seasonMultiplier = 1f;
        [SerializeField, Min(0f)] private float timeOfDayMultiplier = 1f;
        [SerializeField, Min(0f)] private float cooldownSeconds;
        [Tooltip("Spring, summer, autumn, winter weight multipliers.")]
        [SerializeField] private Vector4 seasonalWeights = Vector4.one;
        [Tooltip("00-06, 06-12, 12-18, 18-24 weight multipliers.")]
        [SerializeField] private Vector4 timeOfDayWeights = Vector4.one;

        public AnimalActionDefinition Action => action;
        public float SeasonMultiplier => Mathf.Max(0f, seasonMultiplier);
        public float TimeOfDayMultiplier => Mathf.Max(0f, timeOfDayMultiplier);
        public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
        public float CalendarWeight(DateTime date)
        {
            int season = date.Month >= 3 && date.Month <= 5 ? 0 : date.Month <= 8 && date.Month >= 6 ? 1
                : date.Month >= 9 && date.Month <= 11 ? 2 : 3;
            return Mathf.Max(0f, seasonalWeights[season]) * Mathf.Max(0f, timeOfDayWeights[date.Hour / 6]);
        }

        public AnimalDailyBehaviourKind Behaviour => behaviour;
        public float SelectionWeight => Mathf.Max(0f, selectionWeight);
        public Vector2 DurationSeconds => durationSeconds;

        public AnimalDailyBehaviourSettings(
            AnimalDailyBehaviourKind behaviour,
            float selectionWeight,
            float minimumDurationSeconds,
            float maximumDurationSeconds)
        {
            this.behaviour = behaviour;
            this.selectionWeight = Mathf.Max(0f, selectionWeight);
            durationSeconds = new Vector2(
                Mathf.Max(0.05f, minimumDurationSeconds),
                Mathf.Max(minimumDurationSeconds, maximumDurationSeconds));
        }

        public float ChooseDuration()
        {
            float minimum = Mathf.Max(0.05f, durationSeconds.x);
            float maximum = Mathf.Max(minimum, durationSeconds.y);
            return UnityEngine.Random.Range(minimum, maximum);
        }

        public void ClampValues()
        {
            selectionWeight = Mathf.Max(0f, selectionWeight);
            durationSeconds.x = Mathf.Max(0.05f, durationSeconds.x);
            durationSeconds.y = Mathf.Max(
                durationSeconds.x,
                durationSeconds.y);
        }
    }

    [Serializable]
    public sealed class AnimalFoodPreference
    {
        [SerializeField] private AnimalFoodType foodType;
        [SerializeField, Min(0f)] private float selectionWeight = 1f;

        public AnimalFoodType FoodType => foodType;
        public float SelectionWeight => Mathf.Max(0f, selectionWeight);

        public AnimalFoodPreference(
            AnimalFoodType foodType,
            float selectionWeight)
        {
            this.foodType = foodType;
            this.selectionWeight = Mathf.Max(0f, selectionWeight);
        }

        public void ClampValues()
        {
            selectionWeight = Mathf.Max(0f, selectionWeight);
        }
    }
}

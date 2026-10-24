using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals
{
    // Shared selection logic; all progress and cooldowns belong to one animal.
    public sealed class AnimalDailyScheduler
    {
        private readonly AnimalContext context;
        private readonly Dictionary<AnimalDailyBehaviourSettings, float> cooldowns = new();
        public AnimalDailyScheduler(AnimalContext context) { this.context = context; }

        public float Weight(AnimalDailyBehaviourSettings settings)
        {
            if (settings == null || (cooldowns.TryGetValue(settings, out float until)
                && context.Runtime.Time < until)) return 0f;
            return Mathf.Max(0f, settings.SelectionWeight * settings.SeasonMultiplier
                * settings.TimeOfDayMultiplier * context.Runtime.SeasonMultiplier
                * context.Runtime.TimeOfDayMultiplier * settings.CalendarWeight(context.World.CalendarDate));
        }

        public AnimalDailyBehaviourSettings Choose(IReadOnlyList<AnimalDailyBehaviourSettings> candidates)
        {
            float total = 0f;
            for (int i = 0; i < candidates.Count; i++) total += Weight(candidates[i]);
            if (total <= 0f) return null;
            float roll = Random.value * total;
            AnimalDailyBehaviourSettings last = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                float weight = Weight(candidates[i]);
                if (weight <= 0f) continue;
                last = candidates[i];
                roll -= weight;
                if (roll <= 0f) return last;
            }
            return last;
        }

        public void Complete(AnimalDailyBehaviourSettings settings)
        {
            if (settings != null) cooldowns[settings] = context.Runtime.Time + settings.CooldownSeconds;
        }
        public void Failed(AnimalDailyBehaviourSettings settings)
        {
            if (settings != null) cooldowns[settings] = context.Runtime.Time + Mathf.Max(1f, settings.CooldownSeconds);
        }
    }
}

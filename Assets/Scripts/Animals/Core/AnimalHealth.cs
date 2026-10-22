using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    public sealed class AnimalHealth
    {
        public float CurrentHealth { get; private set; }
        public float MaximumHealth { get; }
        public float CorpseRemaining { get; private set; }
        public bool IsDead { get; private set; }
        public event Action Died;
        private readonly float naturalDecayPerSecond;
        private readonly System.Collections.Generic.HashSet<AnimalAgent> eaters = new();
        public bool CanFeed(AnimalAgent animal)
        {
            eaters.RemoveWhere(a => a == null || !a.isActiveAndEnabled || !a.IsPresent);
            return IsDead && CorpseRemaining > 0f && (eaters.Contains(animal) || eaters.Count < 5);
        }
        public bool Reserve(AnimalAgent animal)
        {
            return CanFeed(animal) && (eaters.Contains(animal) || eaters.Add(animal));
        }
        public void Release(AnimalAgent animal) { eaters.Remove(animal); }

        public AnimalHealth(AnimalSpeciesConfig config)
        {
            MaximumHealth = config.MaximumHealth;
            float minimum = config.MinimumInitialHealth, maximum = config.MaximumInitialHealth;
            CurrentHealth = minimum >= maximum ? minimum
                : Mathf.Clamp(UnityEngine.Random.Range(minimum, maximum), minimum, maximum);
            CorpseRemaining = config.CorpseNutrition;
            naturalDecayPerSecond = CorpseRemaining / 120f;
        }

        public bool Damage(float amount)
        {
            if (IsDead || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return false;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                Died?.Invoke();
            }
            return true;
        }

        public float Consume(float amount)
        {
            if (!IsDead || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return 0f;
            float consumed = Mathf.Min(CorpseRemaining, amount);
            CorpseRemaining -= consumed;
            return consumed;
        }

        public void Tick(float deltaTime)
        {
            if (IsDead && deltaTime > 0f) Consume(naturalDecayPerSecond * deltaTime);
        }
    }
}

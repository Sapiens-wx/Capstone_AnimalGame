using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    public static class AnimalHuntTargets
    {
        public static bool Eligible(AnimalContext c, AnimalAgent prey, bool corpseOnly = false, bool aliveOnly = false, bool ignoreCooldown = false)
        {
            return prey != null && prey != c.Agent && prey.IsInitialized && !prey.Runtime.Removed
                && prey.gameObject.scene == c.Agent.gameObject.scene && prey.isActiveAndEnabled && c.Config.Eats(prey.Config)
                && (!corpseOnly || prey.Health.IsDead)
                && (!aliveOnly || !prey.Health.IsDead)
                && !(c.Runtime.FailedTargets.TryGetValue(prey, out float retryAt) && c.Runtime.Time < retryAt)
                && (c.Runtime.Group == null || prey.Runtime.Group != c.Runtime.Group)
                && (!prey.Health.IsDead || ((ignoreCooldown || c.Runtime.Time >= c.Runtime.FeedCooldownUntil) && prey.Health.CanFeed(c.Agent)));
        }
        public static AnimalAgent Select(AnimalContext c, AnimalAgent current = null, bool corpseOnly = false, bool aliveOnly = false, bool ignoreCooldown = false)
        {
            AnimalAgent selected = Eligible(c, current, corpseOnly, aliveOnly, ignoreCooldown) ? current : null;
            float nearest = selected != null ? Vector2.Distance(c.Motor.CurrentMapPosition, selected.Motor.CurrentMapPosition) * .8f : float.PositiveInfinity;
            var candidates = new List<WorldInteraction>();
            WorldInteractionQuery.Query(InteractionShape.Circle(c.Motor.CurrentMapPosition, c.Config.HuntingRadiusMeters),
                WorldInteractionKind.Collision, c.Agent.Map, c.Agent.gameObject.scene, candidates, ignore: c.Agent.transform);
            foreach (WorldInteraction item in candidates)
            {
                AnimalAgent prey = item.GetComponent<AnimalAgent>();
                if (!Eligible(c, prey, corpseOnly, aliveOnly, ignoreCooldown)) continue;
                float distance = Vector2.Distance(c.Motor.CurrentMapPosition, prey.Motor.CurrentMapPosition);
                if (distance < nearest) { nearest = distance; selected = prey; }
            }
            return selected;
        }
    }
}

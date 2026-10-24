using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    public static class AnimalImpact
    {
        // Shared by angry charges and adult sparring: same radius, pair cooldown, delayed damage and knockback.
        public static void Apply(AnimalContext c, Vector2 direction, bool sparring = false)
        {
            if (direction.sqrMagnitude < .001f) direction = c.Motor.FacingMapDirection;
            Vector2 center = c.Motor.CurrentMapPosition + direction.normalized * c.Config.BodyRadiusMeters;
            var hits = new List<WorldInteraction>();
            WorldInteractionQuery.Query(InteractionShape.Circle(center, c.Config.ImpactRadius), WorldInteractionKind.Collision,
                c.Agent.Map, c.Agent.gameObject.scene, hits, ignore: c.Agent.transform);
            foreach (WorldInteraction hit in hits)
                if (hit is AnimalBody || hit is AnimalGame.RobotMap.PlayerWorldBody)
                    c.Agent.Simulation.Hit(c.Agent, hit.transform, true, sparring);
        }
    }
}

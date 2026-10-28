using System.Collections.Generic;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Animals
{
    public static class AnimalImpact
    {
        // Social sparring never produces combat impacts.
        public static void Apply(AnimalContext c, Vector2 direction, bool sparring = false)
        {
            if (sparring) return;
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

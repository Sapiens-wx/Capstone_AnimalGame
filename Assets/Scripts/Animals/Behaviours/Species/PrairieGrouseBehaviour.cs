using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    public sealed class PrairieGrouseBehaviour : GenericAnimalBehaviour
    {
        public override bool SupportsAggression => false;
        public override AnimalAction CreateEscapeAction() => new GroundBirdFlightAction(true);
    }
}

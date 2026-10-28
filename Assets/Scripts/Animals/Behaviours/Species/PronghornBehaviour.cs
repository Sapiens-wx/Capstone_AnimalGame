using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    public sealed class PronghornBehaviour : GenericAnimalBehaviour
    {
        public override bool SupportsAggression => false;
    }
}

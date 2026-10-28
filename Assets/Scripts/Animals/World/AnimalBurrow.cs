using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.Animals
{
    // Invisible scene anchor, deliberately has no renderer or collision body.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class AnimalBurrow : MonoBehaviour
    {
        private static readonly HashSet<AnimalBurrow> active = new();
        public static IEnumerable<AnimalBurrow> ActiveBurrows => active;
        private void OnEnable() { if (gameObject.scene.IsValid()) active.Add(this); }
        private void OnDisable() { active.Remove(this); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() { active.Clear(); }
        public bool TryGetPosition(MapTestSceneController map, out Vector2 position)
            => map.TrySampleWorldPosition(transform.position, out position, out _);
    }
}

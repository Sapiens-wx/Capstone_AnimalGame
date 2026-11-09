using AnimalGame.Animals;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace Capstone.Audio
{
    // Aquatic reeds have no WorldInteraction collider. Observe the overlap of
    // their public map footprint with the actual robot body, not visual alpha
    // (which animals also affect) or a predicted gameplay collision.
    internal sealed class Milestone3ReedContactObserver
    {
        private const int MaximumTrackedReeds = 32;
        private const float DiscoveryInterval = .25f;
        private const float NearbyDistanceMeters = 8f;

        private struct Reed
        {
            internal AnimalFoodSource Food;
            internal HeightMapPlacedObject Placement;
            internal float Distance;
        }

        private readonly Reed[] reeds = new Reed[MaximumTrackedReeds];
        private RobotMover mover;
        private MapTestSceneController map;
        private int count;
        private float discoveryRemaining;
        private Vector2 discoveryPosition;

        internal void Bind(RobotMover source)
        {
            Clear();
            mover = source;
            map = null;
            if (!Usable(mover)) return;
            foreach (var candidate in Object.FindObjectsByType<MapTestSceneController>(FindObjectsSortMode.None))
                if (Usable(candidate) && candidate.gameObject.scene == mover.gameObject.scene)
                { map = candidate; break; }
        }

        internal bool IsTouching(float deltaTime)
        {
            if (deltaTime <= 0f || !Usable(mover) || !Usable(map) || !map.HasGeneratedMap)
                return false;

            Vector2 position = InteractionShape.ToQuery(mover.transform.position, map);
            float bodyRadius = mover.BodyRadiusInQuery(map);
            discoveryRemaining -= deltaTime;
            if (discoveryRemaining <= 0f
                || (position - discoveryPosition).sqrMagnitude > NearbyDistanceMeters * NearbyDistanceMeters)
            {
                Discover(position, bodyRadius);
                discoveryPosition = position;
                discoveryRemaining = DiscoveryInterval;
            }

            for (int i = 0; i < count; i++)
            {
                Reed reed = reeds[i];
                if (!Usable(reed.Food) || !Usable(reed.Placement)
                    || reed.Food.FoodType != AnimalFoodType.Australis
                    || reed.Food.gameObject.scene != mover.gameObject.scene
                    || reed.Placement.Map != map) continue;
                Vector2 plantPosition = InteractionShape.ToQuery(reed.Food.transform.position, map);
                float radius = bodyRadius + reed.Placement.FootprintRadiusMeters;
                if ((plantPosition - position).sqrMagnitude <= radius * radius) return true;
            }
            return false;
        }

        private void Discover(Vector2 position, float bodyRadius)
        {
            for (int i = 0; i < count; i++) reeds[i] = default;
            count = 0;
            // The public registry is IEnumerable: its enumerator is acquired
            // only on discovery, never on the intervening observation frames.
            foreach (AnimalFoodSource food in AnimalFoodSource.ActiveSources)
            {
                if (!Usable(food) || food.FoodType != AnimalFoodType.Australis
                    || food.gameObject.scene != mover.gameObject.scene
                    || !food.TryGetComponent(out HeightMapPlacedObject placement)
                    || !Usable(placement) || placement.Map != map) continue;

                Vector2 plantPosition = InteractionShape.ToQuery(food.transform.position, map);
                float distance = Vector2.Distance(position, plantPosition)
                    - placement.FootprintRadiusMeters - bodyRadius;
                if (distance > NearbyDistanceMeters) continue;
                int slot = count;
                if (slot == reeds.Length)
                {
                    slot = 0;
                    for (int i = 1; i < count; i++)
                        if (reeds[i].Distance > reeds[slot].Distance) slot = i;
                    if (distance >= reeds[slot].Distance) continue;
                }
                else count++;
                reeds[slot] = new Reed { Food = food, Placement = placement, Distance = distance };
            }
        }

        internal void Clear()
        {
            for (int i = 0; i < count; i++) reeds[i] = default;
            count = 0;
            discoveryRemaining = 0f;
            discoveryPosition = default;
        }

        private static bool Usable(Behaviour source) => source != null && source.isActiveAndEnabled
            && source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded;
    }
}

using UnityEngine;

namespace AnimalGame.Rendering
{
    internal static class EnclosedRoomTerrain
    {
        private const float TransitionSeconds = .46f;
        private static readonly int RoomProperty = Shader.PropertyToID("_EnclosedRoomSurfaceFade");
        private static Object transitionRoom;
        private static bool targetInside;
        private static float progress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            transitionRoom = null;
            targetInside = false;
            progress = 0f;
            Shader.SetGlobalFloat(RoomProperty, 0f);
        }

        public static void SetOccupied(Object room, bool occupied)
        {
            // The room keeps ownership during its fade-out. Other towers cannot clear it.
            if (!occupied && transitionRoom != room) return;
            transitionRoom = room;
            targetInside = occupied;
        }

        public static void Advance(Object room, float deltaTime)
        {
            if (transitionRoom != room) return;

            float next = Mathf.MoveTowards(progress, targetInside ? 1f : 0f, deltaTime / TransitionSeconds);
            if (next != progress)
            {
                progress = next;
                Shader.SetGlobalFloat(RoomProperty, Mathf.SmoothStep(0f, 1f, next));
            }
            if (!targetInside && progress == 0f) transitionRoom = null;
        }

        public static void Release(Object room)
        {
            if (transitionRoom == room) Reset();
        }
    }
}

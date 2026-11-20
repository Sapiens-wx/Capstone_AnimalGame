using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Animals
{
    public sealed class AnimalWorldQuery
    {
        public RobotMover Player { get; private set; }
        public Camera Camera { get; private set; }
        public Scene Scene { get; }
        private float nextResolve;
        private AnimalGame.MainUI.MainUI clock;
        public System.DateTime CalendarDate => clock != null && clock.CurrentGameTime != default
            ? clock.CurrentGameTime : new System.DateTime(2000, 1, 1, 12, 0, 0);
        public AnimalWorldQuery(Scene scene) { Scene = scene; }
        public void Resolve(float time)
        {
            if (time < nextResolve) return;
            nextResolve = time + 1f;
            if (Player == null)
                foreach (GameObject root in Scene.GetRootGameObjects())
                {
                    Player = root.GetComponentInChildren<RobotMover>();
                    if (Player != null) break;
                }
            if (Camera == null || !Camera.isActiveAndEnabled) Camera = Camera.main;
            if (clock == null)
                foreach (GameObject root in Scene.GetRootGameObjects())
                { clock = root.GetComponentInChildren<AnimalGame.MainUI.MainUI>(); if (clock != null) break; }
        }

        private MapTestSceneController viewMap;
        public float InitialViewRadiusMeters { get; private set; }

        public void ConfigureView(MapTestSceneController map, float radius)
        {
            viewMap = map;
            InitialViewRadiusMeters = radius;
        }

        public bool IsBeyondView(AnimalAgent animal)
        {
            if (Player == null || InitialViewRadiusMeters <= 0f) return false;
            var map = viewMap;
            Vector2 delta = InteractionShape.ToQuery(Player.transform.position, map)
                - InteractionShape.ToQuery(animal.transform.position, map);
            return delta.magnitude > AnimalSpawner.OuterRadiusMultiplier * InitialViewRadiusMeters;
        }

        public bool Sweep(AnimalAgent animal, Vector2 start, Vector2 end)
        {
            return WorldInteractionQuery.Query(InteractionShape.Capsule(start, end, animal.Config.BodyRadiusMeters),
                WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision,
                animal.Map, Scene, ignore: animal.transform,
                previous: InteractionShape.Circle(start, animal.Config.BodyRadiusMeters));
        }
    }
}

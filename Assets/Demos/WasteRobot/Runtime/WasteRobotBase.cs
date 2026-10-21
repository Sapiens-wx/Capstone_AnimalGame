using System;
using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.Demos.WasteRobot
{
    // Opt-in component. Player discovery is limited to this prefab's own scene.
    [DefaultExecutionOrder(50)]
    public sealed class WasteRobotBase : MonoBehaviour
    {
        [SerializeField] private GameObject exteriorRoot;
        [SerializeField] private GameObject interiorRoot;
        [SerializeField] private Transform target;
        [Tooltip("When Target is empty, wait for the current scene's map player, or its only active RobotMover. Explicit targets always take priority.")]
        [SerializeField] private bool autoBindPlayer = true;
        [SerializeField, Min(.8f)] private float entranceWidth = 2.4f;
        [SerializeField, Min(.01f)] private float crossingHysteresis = .10f;
        [Tooltip("Fallback world-space radius when the target has no RobotMarkerView. Map players use their actual collision radius and map scale.")]
        [SerializeField, Min(0f)] private float bodyClearance = .38f;

        private Vector2 previousLocal;
        private bool hasSample;
        private bool pendingCrossing;
        private bool targetWasAutoBound;
        private RobotMover trackedRobot;
        private RobotMarkerView trackedMarker;
        private HeightMapPlayerSceneBootstrap playerBootstrap;
        private float nextPlayerSearch;
        private readonly List<GameObject> sceneRoots = new List<GameObject>();
        private readonly List<RobotMover> sceneRobots = new List<RobotMover>();
        private readonly List<HeightMapPlayerSceneBootstrap> sceneBootstraps = new List<HeightMapPlayerSceneBootstrap>();

        public GameObject ExteriorRoot => exteriorRoot;
        public GameObject InteriorRoot => interiorRoot;
        public Transform Target => target;
        public bool AutoBindPlayer
        {
            get => autoBindPlayer;
            set { autoBindPlayer = value; nextPlayerSearch = 0f; }
        }
        public float BodyClearanceWorld => GetBodyClearanceWorld();
        public float EntranceWidth => entranceWidth;
        public float CrossingHysteresis => crossingHysteresis;
        public bool IsInside { get; private set; }
        public int TransitionCount { get; private set; }

        // Presentation observers never own the portal/collision state. Capture
        // the outgoing visual before the authoritative roots switch this frame.
        public event Action<bool, bool> PresentationChanging;
        public event Action PresentationReset;
        public event Action PresentationUnavailable;

        public void Configure(GameObject exterior, GameObject interior, float width)
        {
            exteriorRoot = exterior;
            interiorRoot = interior;
            entranceWidth = Mathf.Max(.8f, width);
            ApplyPresentation();
        }

        public void Bind(Transform player, bool inside = false)
        {
            target = player;
            targetWasAutoBound = false;
            trackedRobot = player != null ? player.GetComponent<RobotMover>() : null;
            trackedMarker = player != null ? player.GetComponent<RobotMarkerView>() : null;
            playerBootstrap = FindPlayerBootstrap(trackedRobot);
            IsInside = inside;
            TransitionCount = 0;
            pendingCrossing = false;
            hasSample = target != null;
            if (hasSample) previousLocal = WorldToPortal(target.position);
            ApplyPresentation();
            Notify(PresentationReset);
        }

        private void Awake()
        {
            ApplyPresentation();
            if (target != null) Bind(target);
        }

        private void LateUpdate()
        {
            if (targetWasAutoBound && !CanRetainScenePlayer(trackedRobot)) Bind(null);
            if (target == null && autoBindPlayer && Time.unscaledTime >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.unscaledTime + .25f;
                TryBindScenePlayer();
            }
            if (target == null || target.gameObject.scene != gameObject.scene
                || !target.gameObject.activeInHierarchy)
            {
                hasSample = false;
                pendingCrossing = false;
                return;
            }
            // A modal menu can pause the world and temporarily disable locomotion.
            // Preserve the interior without counting a paused reposition as a
            // doorway crossing. Keep a stationary pending crossing intact and
            // retain a sample for the first movement frame after resume.
            if (Time.timeScale <= 0f)
            {
                Vector2 pausedLocal = WorldToPortal(target.position);
                if (!hasSample || pausedLocal != previousLocal) pendingCrossing = false;
                previousLocal = pausedLocal;
                hasSample = true;
                return;
            }
            ObserveWorldPosition(target.position);
        }

        private void OnDisable()
        {
            hasSample = false;
            pendingCrossing = false;
            Notify(PresentationUnavailable);
        }

        // Also callable by explicit validation tools. No global searches, player
        // creation, terrain changes or dependency on the separate Demo bootstrap.
        public bool TryBindScenePlayer()
        {
            if (targetWasAutoBound && !CanRetainScenePlayer(trackedRobot)) Bind(null);
            if (target != null) return target.gameObject.scene == gameObject.scene;
            if (!autoBindPlayer || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
                return false;

            gameObject.scene.GetRootGameObjects(sceneRoots);
            RobotMover candidate = null;
            foreach (GameObject root in sceneRoots)
            {
                root.GetComponentsInChildren(false, sceneBootstraps);
                foreach (HeightMapPlayerSceneBootstrap bootstrap in sceneBootstraps)
                {
                    if (!bootstrap.isActiveAndEnabled || !IsScenePlayer(bootstrap.robot)) continue;
                    if (candidate != null && candidate != bootstrap.robot) return false;
                    candidate = bootstrap.robot;
                }
            }
            // A map bootstrap identifies its controlled player even when another
            // RobotMover is present. Without one, require exactly one candidate.
            if (candidate == null)
            {
                foreach (GameObject root in sceneRoots)
                {
                    root.GetComponentsInChildren(false, sceneRobots);
                    foreach (RobotMover robot in sceneRobots)
                    {
                        if (!IsScenePlayer(robot)) continue;
                        if (candidate != null && candidate != robot) return false;
                        candidate = robot;
                    }
                }
            }
            if (candidate == null) return false;
            Bind(candidate.transform);
            targetWasAutoBound = true;
            return true;
        }

        private bool IsScenePlayer(RobotMover robot) => robot != null && robot.isActiveAndEnabled
            && robot.gameObject.scene == gameObject.scene;

        // Input leases disable RobotMover while Time.timeScale is zero. The
        // player still occupies this interior. New candidates must remain enabled,
        // and disabled players still expire normally once the world is running.
        private bool CanRetainScenePlayer(RobotMover robot) => IsScenePlayer(robot)
            || (Time.timeScale <= 0f && robot != null && robot.gameObject.activeInHierarchy
                && robot.gameObject.scene == gameObject.scene);

        private HeightMapPlayerSceneBootstrap FindPlayerBootstrap(RobotMover robot)
        {
            if (robot == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return null;
            gameObject.scene.GetRootGameObjects(sceneRoots);
            foreach (GameObject root in sceneRoots)
            {
                root.GetComponentsInChildren(false, sceneBootstraps);
                foreach (HeightMapPlayerSceneBootstrap bootstrap in sceneBootstraps)
                    if (bootstrap.robot == robot) return bootstrap;
            }
            return null;
        }

        private float GetBodyClearanceWorld()
        {
            // A player can exist before its map bootstrap is initialized. This
            // runs only when portal clearance is queried, not every frame.
            if (trackedRobot != null && (playerBootstrap == null || playerBootstrap.robot != trackedRobot))
                playerBootstrap = FindPlayerBootstrap(trackedRobot);
            if (trackedRobot != null && playerBootstrap != null && playerBootstrap.robot == trackedRobot)
            {
                HeightMapTraversalEvaluator evaluator = playerBootstrap.traversalEvaluator;
                MapTestSceneController map = evaluator != null ? evaluator.Map : null;
                if (map != null && map.HasGeneratedMap)
                {
                    float x = Mathf.Abs(map.MapMetersToWorldDistance(Vector2.right, 1f));
                    float y = Mathf.Abs(map.MapMetersToWorldDistance(Vector2.up, 1f));
                    Vector2 right = transform.right;
                    // Map collision circles are measured in logical metres. Use
                    // their projected world-space extent across the portal.
                    return (evaluator.RobotObstacleCollisionRadiusMeters + .02f)
                        * Mathf.Sqrt(right.x * right.x * x * x + right.y * right.y * y * y);
                }
            }
            if (trackedMarker != null)
                return trackedMarker.BodyDiameter * .5f * Mathf.Max(Mathf.Abs(target.lossyScale.x),
                    Mathf.Abs(target.lossyScale.y)) + .02f;
            return bodyClearance;
        }

        // Public for deterministic tests; the player transform is never moved here.
        public bool ObserveWorldPosition(Vector2 worldPosition)
        {
            Vector2 current = WorldToPortal(worldPosition);
            if (!hasSample)
            {
                previousLocal = current;
                hasSample = true;
                return false;
            }

            float deltaY = current.y - previousLocal.y;
            if (Mathf.Abs(deltaY) > .000001f)
            {
                float t = -previousLocal.y / deltaY;
                if (t >= 0f && t <= 1f)
                {
                    float x = Mathf.Lerp(previousLocal.x, current.x, t);
                    float scale = Mathf.Max(.0001f, Mathf.Abs(transform.lossyScale.x));
                    float halfClear = entranceWidth * .5f - GetBodyClearanceWorld() / scale;
                    bool expectedDirection = IsInside ? deltaY > 0f : deltaY < 0f;
                    if (halfClear >= 0f && Mathf.Abs(x) <= halfClear)
                    {
                        if (expectedDirection) pendingCrossing = true;
                    }
                    else pendingCrossing = false;
                }
            }

            // Keep the valid pending crossing while the player dwells/jitters on
            // the threshold; only a stable return to the original side cancels it.
            bool arrived = IsInside ? current.y >= crossingHysteresis : current.y <= -crossingHysteresis;
            bool returned = IsInside ? current.y <= -crossingHysteresis : current.y >= crossingHysteresis;
            bool changed = pendingCrossing && arrived;
            previousLocal = current;
            if (changed) SetInside(!IsInside);
            else if (returned) pendingCrossing = false;
            return changed;
        }

        public void SetInside(bool value)
        {
            if (IsInside != value)
            {
                if (PresentationChanging != null)
                    foreach (Action<bool, bool> observer in PresentationChanging.GetInvocationList())
                        try { observer(IsInside, value); }
                        catch (Exception exception) { Debug.LogException(exception, this); }
                IsInside = value;
                TransitionCount++;
            }
            pendingCrossing = false;
            ApplyPresentation();
        }

        private void Notify(Action observers)
        {
            if (observers == null) return;
            foreach (Action observer in observers.GetInvocationList())
                try { observer(); }
                catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private void ApplyPresentation()
        {
            if (exteriorRoot != null && exteriorRoot != gameObject)
                exteriorRoot.SetActive(!IsInside);
            if (interiorRoot != null && interiorRoot != gameObject)
                interiorRoot.SetActive(IsInside);
            // OnEnable/OnDisable register/unregister each active WorldInteraction.
            WorldInteraction.MarkHierarchySpatialDirty(transform);
        }

        public Vector2 WorldToPortal(Vector2 world) => transform.InverseTransformPoint(world);
        public Vector2 PortalToWorld(Vector2 local) => transform.TransformPoint(local);

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 saved = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(new Vector3(-entranceWidth * .5f, 0f),
                new Vector3(entranceWidth * .5f, 0f));
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(entranceWidth, crossingHysteresis * 2f, .01f));
            Gizmos.matrix = saved;
        }
    }
}


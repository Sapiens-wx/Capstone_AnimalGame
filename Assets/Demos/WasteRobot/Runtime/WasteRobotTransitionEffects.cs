using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.Demos.WasteRobot
{
    /// <summary>Local presentation only. Portal state, movement and collision remain owned by WasteRobotBase.</summary>
    [DefaultExecutionOrder(60)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WasteRobotBase))]
    public sealed class WasteRobotTransitionEffects : MonoBehaviour
    {
        [Header("World fade (existing overlay HUD stays visible)")]
        [SerializeField] private bool fadeEnabled = true;
        [SerializeField, Min(0f)] private float fadeOutDuration = .18f;
        [SerializeField, Min(0f)] private float blackHoldDuration = .04f;
        [SerializeField, Min(0f)] private float fadeInDuration = .22f;
        [Header("Interior camera")]
        [SerializeField] private bool zoomEnabled = true;
        [SerializeField, Min(.01f)] private float interiorCameraSize = 6.5f;
        [SerializeField, Min(0f)] private float zoomDuration = .3f;
        [Tooltip("Optional same-scene player camera. Otherwise use this player's map bootstrap or follow camera.")]
        [SerializeField] private Camera cameraOverride;

        private enum FadePhase { Idle, FadingOut, Black, Revealing }
        private WasteRobotBase machine;
        private Camera activeCamera;
        private HeightMapPlayerSceneBootstrap cameraBootstrap;
        private RobotCameraFollow cameraFollow;
        private WasteRobotInteriorCameraZoom activeZoom;
        private Transform trackedTarget;
        private FadePhase phase;
        private float elapsed;
        private float fadeStart;
        private float fadeAlpha;
        private int blackStartedFrame;
        private bool desiredInside;
        private bool zoomRequested;
        private bool zoomRequestedInside;
        private float requestedSize;
        private float nextCameraSearch;
        private GameObject overlayObject;
        private CanvasGroup overlayGroup;
        private GameObject outgoingObject;
        private readonly Dictionary<SpriteRenderer, bool> hiddenRenderers = new Dictionary<SpriteRenderer, bool>();
        private readonly List<GameObject> sceneRoots = new List<GameObject>();
        private readonly List<HeightMapPlayerSceneBootstrap> bootstraps = new List<HeightMapPlayerSceneBootstrap>();
        private readonly List<RobotCameraFollow> follows = new List<RobotCameraFollow>();

        public bool FadeEnabled
        {
            get => fadeEnabled;
            set { fadeEnabled = value; if (!value) CancelFade(); }
        }
        public bool ZoomEnabled
        {
            get => zoomEnabled;
            set
            {
                if (zoomEnabled == value) return;
                zoomEnabled = value;
                zoomRequested = false;
                if (!value && activeZoom != null)
                {
                    ReleaseZoom();
                }
            }
        }
        public float InteriorCameraSize => interiorCameraSize;
        public float FadeOutDuration => fadeOutDuration;
        public float BlackHoldDuration => blackHoldDuration;
        public float FadeInDuration => fadeInDuration;
        public float ZoomDuration => zoomDuration;
        public bool IsTransitioning => phase != FadePhase.Idle;
        public float FadeAlpha => fadeAlpha;
        public Camera ActiveCamera => activeCamera;
        public WasteRobotInteriorCameraZoom ActiveZoom => activeZoom;
        public int BlackFrameCount { get; private set; }
        public GameObject FadeOverlayObject => overlayObject;
        public GameObject OutgoingVisualObject => outgoingObject;

        private void OnEnable()
        {
            machine = GetComponent<WasteRobotBase>();
            machine.PresentationChanging += BeforeSwitch;
            machine.PresentationReset += ResetPresentation;
            machine.PresentationUnavailable += Cleanup;
            ResetPresentation();
        }

        private bool HasPlayer => machine != null && machine.isActiveAndEnabled && machine.Target != null
            && machine.Target.gameObject.activeInHierarchy && machine.Target.gameObject.scene == gameObject.scene;

        private void ResetPresentation()
        {
            Cleanup();
            desiredInside = machine != null && machine.IsInside;
            trackedTarget = machine != null ? machine.Target : null;
            nextCameraSearch = 0f;
            if (Application.isPlaying && HasPlayer && EnsureCamera(true)) RequestZoom(0f);
        }

        private void BeforeSwitch(bool wasInside, bool willBeInside)
        {
            desiredInside = willBeInside;
            if (!Application.isPlaying || !isActiveAndEnabled || !HasPlayer) return;
            if (!EnsureCamera(true)) return;
            RequestZoom(zoomDuration);
            if (!fadeEnabled) { CancelFade(); return; }
            // During fade-out/black the original outgoing image is still the one
            // on screen. Reversals only update the eventual authoritative view.
            if (phase == FadePhase.FadingOut || phase == FadePhase.Black) return;
            CaptureOutgoing(wasInside ? machine.InteriorRoot : machine.ExteriorRoot);
            EnsureOverlay();
            fadeStart = fadeAlpha;
            elapsed = 0f;
            phase = FadePhase.FadingOut;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (!HasPlayer) { Cleanup(); return; }
            if (trackedTarget != machine.Target) { ResetPresentation(); return; }
            if (!EnsureCamera()) { CancelFade(); return; }
            desiredInside = machine.IsInside;
            RequestZoom(zoomDuration);
            if (!fadeEnabled) CancelFade();
            AdvanceFade(Mathf.Max(0f, Time.unscaledDeltaTime));
            if (!IsTransitioning && (!zoomEnabled || !desiredInside) && activeZoom != null && !activeZoom.IsBlending)
                ReleaseZoom();
        }

        private bool EnsureCamera(bool force = false)
        {
            bool usable = IsUsableCamera(activeCamera);
            if (!usable && (!ReferenceEquals(activeCamera, null) || activeZoom != null || IsTransitioning))
            {
                CancelFade();
                ReleaseZoom();
                activeCamera = null;
            }
            // A cached source lets normal outdoor frames avoid scanning the map
            // hierarchy. Its camera reference is checked even if the old camera
            // is still alive, so runtime camera replacement cleans up promptly.
            if (usable && CameraSourceStillCurrent()) return true;
            if (!force && !usable && activeCamera == null && Time.unscaledTime < nextCameraSearch) return false;
            nextCameraSearch = Time.unscaledTime + .25f;
            Camera resolved = ResolveCamera();
            if (usable && resolved == activeCamera) return true;
            CancelFade();
            ReleaseZoom();
            activeCamera = null;
            if (resolved == null) return false;
            activeCamera = resolved;
            zoomRequested = false;
            return true;
        }

        private bool IsUsableCamera(Camera camera) => camera != null && camera.isActiveAndEnabled
            && camera.orthographic && camera.gameObject.scene == gameObject.scene;

        private bool CameraSourceStillCurrent()
        {
            if (IsUsableCamera(cameraOverride)) return cameraOverride == activeCamera;
            if (cameraBootstrap != null) return cameraBootstrap.isActiveAndEnabled
                && cameraBootstrap.robot != null && cameraBootstrap.robot.transform == machine.Target
                && cameraBootstrap.mapCamera == activeCamera;
            return cameraFollow != null && cameraFollow.isActiveAndEnabled
                && cameraFollow.GetComponent<Camera>() == activeCamera && cameraFollow.Target != null
                && (cameraFollow.Target == machine.Target || cameraFollow.Target.IsChildOf(machine.Target));
        }

        private Camera ResolveCamera()
        {
            if (!HasPlayer) return null;
            cameraBootstrap = null;
            cameraFollow = null;
            if (IsUsableCamera(cameraOverride)) return cameraOverride;
            gameObject.scene.GetRootGameObjects(sceneRoots);
            foreach (GameObject root in sceneRoots)
            {
                root.GetComponentsInChildren(false, bootstraps);
                foreach (HeightMapPlayerSceneBootstrap bootstrap in bootstraps)
                    if (bootstrap.isActiveAndEnabled && bootstrap.robot != null
                        && bootstrap.robot.transform == machine.Target && IsUsableCamera(bootstrap.mapCamera))
                    { cameraBootstrap = bootstrap; return bootstrap.mapCamera; }
            }
            Camera candidate = null;
            foreach (GameObject root in sceneRoots)
            {
                root.GetComponentsInChildren(false, follows);
                foreach (RobotCameraFollow follow in follows)
                {
                    if (!follow.isActiveAndEnabled || follow.Target == null
                        || (follow.Target != machine.Target && !follow.Target.IsChildOf(machine.Target))) continue;
                    Camera camera = follow.GetComponent<Camera>();
                    if (!IsUsableCamera(camera)) continue;
                    if (candidate != null && candidate != camera) return null;
                    candidate = camera;
                    cameraFollow = follow;
                }
            }
            return candidate;
        }

        private void RequestZoom(float duration)
        {
            if (!zoomEnabled) { ReleaseZoom(); return; }
            bool inside = zoomEnabled && desiredInside;
            if (activeZoom == null || activeZoom.Owner != this)
            {
                // Outside and idle, no adapter or camera write is needed.
                if (!inside) return;
                activeZoom = WasteRobotInteriorCameraZoom.GetOrCreate(activeCamera);
                if (activeZoom == null || !activeZoom.Acquire(this, activeCamera))
                { activeZoom = null; return; }
                zoomRequested = false;
            }
            if (zoomRequested && zoomRequestedInside == inside && Mathf.Approximately(requestedSize, interiorCameraSize)) return;
            activeZoom.SetInterior(this, inside, interiorCameraSize, duration);
            zoomRequested = true;
            zoomRequestedInside = inside;
            requestedSize = interiorCameraSize;
        }

        private void AdvanceFade(float delta)
        {
            elapsed += delta;
            // One phase per frame guarantees an actual opaque frame, even at low
            // frame rates or with zero duration, before the incoming view reveals.
            switch (phase)
            {
                case FadePhase.FadingOut:
                    SetAlpha(Mathf.Lerp(fadeStart, 1f, Progress(elapsed, fadeOutDuration)));
                    if (fadeAlpha >= .99999f)
                    {
                        SetAlpha(1f);
                        phase = FadePhase.Black;
                        elapsed = 0f;
                        blackStartedFrame = Time.frameCount;
                    }
                    break;
                case FadePhase.Black:
                    if (Time.frameCount > blackStartedFrame && elapsed >= Mathf.Max(0f, blackHoldDuration))
                    {
                        BlackFrameCount++;
                        RestoreOriginals();
                        DisposeRuntimeObject(ref outgoingObject);
                        phase = FadePhase.Revealing;
                        elapsed = 0f;
                    }
                    break;
                case FadePhase.Revealing:
                    SetAlpha(1f - Progress(elapsed, fadeInDuration));
                    if (fadeAlpha <= .00001f) CancelFade();
                    break;
            }
        }

        private static float Progress(float time, float duration) => duration <= 0f ? 1f : Mathf.Clamp01(time / duration);

        private void CaptureOutgoing(GameObject sourceRoot)
        {
            RestoreOriginals();
            DisposeRuntimeObject(ref outgoingObject);
            outgoingObject = new GameObject("Waste Robot Outgoing Visual (runtime only)");
            outgoingObject.hideFlags = HideFlags.DontSave;
            outgoingObject.transform.SetParent(transform, false);
            if (sourceRoot != null)
                foreach (SpriteRenderer source in sourceRoot.GetComponentsInChildren<SpriteRenderer>(false))
                {
                    if (!source.enabled || source.sprite == null) continue;
                    var clone = new GameObject(source.name + " (visual only)");
                    clone.hideFlags = HideFlags.DontSave;
                    clone.layer = source.gameObject.layer;
                    clone.transform.SetParent(outgoingObject.transform, false);
                    clone.transform.position = source.transform.position;
                    clone.transform.rotation = source.transform.rotation;
                    Vector3 parentScale = outgoingObject.transform.lossyScale;
                    Vector3 sourceScale = source.transform.lossyScale;
                    clone.transform.localScale = new Vector3(sourceScale.x / SafeScale(parentScale.x),
                        sourceScale.y / SafeScale(parentScale.y), sourceScale.z / SafeScale(parentScale.z));
                    SpriteRenderer renderer = clone.AddComponent<SpriteRenderer>();
                    renderer.sprite = source.sprite;
                    renderer.sharedMaterials = source.sharedMaterials;
                    renderer.color = source.color;
                    renderer.flipX = source.flipX;
                    renderer.flipY = source.flipY;
                    renderer.sortingLayerID = source.sortingLayerID;
                    renderer.sortingOrder = source.sortingOrder + 1;
                    renderer.maskInteraction = source.maskInteraction;
                    renderer.drawMode = source.drawMode;
                    renderer.size = source.size;
                    renderer.tileMode = source.tileMode;
                    renderer.spriteSortPoint = source.spriteSortPoint;
                    var block = new MaterialPropertyBlock();
                    source.GetPropertyBlock(block);
                    renderer.SetPropertyBlock(block);
                }
            // The snapshot can outlive the source view being deactivated. Give
            // it its own clip registration, released with the runtime object.
            outgoingObject.AddComponent<WasteRobotVisibility>();
            HideOriginals(machine.ExteriorRoot);
            HideOriginals(machine.InteriorRoot);
        }

        private static float SafeScale(float value) => Mathf.Abs(value) < .0001f ? .0001f : value;
        private void HideOriginals(GameObject root)
        {
            if (root == null) return;
            foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                hiddenRenderers[renderer] = renderer.enabled;
                renderer.enabled = false;
            }
        }
        private void RestoreOriginals()
        {
            foreach (KeyValuePair<SpriteRenderer, bool> pair in hiddenRenderers)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            hiddenRenderers.Clear();
        }

        private void EnsureOverlay()
        {
            if (overlayObject != null) return;
            overlayObject = new GameObject("Waste Robot World Fade (runtime only)", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            overlayObject.hideFlags = HideFlags.DontSave;
            overlayObject.layer = activeCamera.gameObject.layer;
            overlayObject.transform.SetParent(activeCamera.transform, false);
            Canvas canvas = overlayObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = activeCamera;
            canvas.planeDistance = Mathf.Clamp(1f, activeCamera.nearClipPlane + .01f, activeCamera.farClipPlane - .01f);
            canvas.sortingOrder = short.MaxValue;
            overlayGroup = overlayObject.GetComponent<CanvasGroup>();
            overlayGroup.interactable = false;
            overlayGroup.blocksRaycasts = false;
            var panel = new GameObject("Black", typeof(RectTransform), typeof(Image));
            panel.hideFlags = HideFlags.DontSave;
            panel.layer = overlayObject.layer;
            panel.transform.SetParent(overlayObject.transform, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = panel.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            SetAlpha(fadeAlpha);
        }
        private void SetAlpha(float alpha)
        {
            fadeAlpha = Mathf.Clamp01(alpha);
            if (overlayGroup != null) overlayGroup.alpha = fadeAlpha;
        }
        private void CancelFade()
        {
            phase = FadePhase.Idle;
            elapsed = 0f;
            SetAlpha(0f);
            RestoreOriginals();
            DisposeRuntimeObject(ref outgoingObject);
            DisposeRuntimeObject(ref overlayObject);
            overlayGroup = null;
        }
        private void ReleaseZoom()
        {
            if (activeZoom != null) activeZoom.Release(this, true);
            activeZoom = null;
            zoomRequested = false;
        }
        private void Cleanup()
        {
            CancelFade();
            ReleaseZoom();
            activeCamera = null;
            cameraBootstrap = null;
            cameraFollow = null;
            nextCameraSearch = 0f;
        }
        private static void DisposeRuntimeObject(ref GameObject runtimeObject)
        {
            if (runtimeObject == null) return;
            runtimeObject.SetActive(false);
            if (Application.isPlaying) Destroy(runtimeObject);
            else DestroyImmediate(runtimeObject);
            runtimeObject = null;
        }
        private void OnDisable()
        {
            if (machine != null)
            {
                machine.PresentationChanging -= BeforeSwitch;
                machine.PresentationReset -= ResetPresentation;
                machine.PresentationUnavailable -= Cleanup;
            }
            Cleanup();
        }
        private void OnDestroy() => Cleanup();
    }
}

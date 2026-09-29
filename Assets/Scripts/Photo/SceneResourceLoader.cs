using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    /// <summary>Warms the retained result document behind a separately rendered black canvas.</summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class SceneResourceLoader : MonoBehaviour
    {
        public enum LoadingState { Covering, WaitingForView, Warming, Hiding, Fading, Completed, Failed }
        [SerializeField] private Canvas coverCanvas;
        [SerializeField] private CanvasGroup coverGroup;
        [SerializeField] private Image coverImage;
        [SerializeField, Min(0)] private float fadeDuration = 0.35f;
        [SerializeField, Min(0)] private float minimumBlackTime;
        [SerializeField, Min(0.1f)] private float waitTimeout = 15;
        [SerializeField, Min(2)] private int finalPoseFrames = 2;
        [SerializeField] private bool diagnostics;
        public LoadingState State { get; private set; } = LoadingState.Covering;
        public bool WarmupSucceeded { get; private set; }

        private PhotoResultView resultView;
        private bool started, ownsWarmup, failed, reported;
        private int renderedFrame = -1, stateFrame, lastObservedFrame = -1, stableFrames;
        private int pose;
        private double startedAt;
        private float fadeElapsed, fadeStart;
        private Vector2 observedSize;
        private Coroutine renderObserver;

        private void Awake()
        {
            if (coverCanvas != null)
            {
                coverCanvas.gameObject.SetActive(true);
                coverCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                coverCanvas.overrideSorting = true;
                coverCanvas.sortingOrder = 1000;
                coverCanvas.enabled = true;
            }
            if (coverGroup != null)
            {
                coverGroup.alpha = 1;
                coverGroup.blocksRaycasts = false;
                coverGroup.interactable = false;
            }
            if (coverImage != null)
            {
                coverImage.enabled = true;
                coverImage.color = Color.black;
                coverImage.raycastTarget = false;
                var rect = coverImage.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
        }

        private void Start()
        {
            if (started) return;
            started = true;
            startedAt = Time.realtimeSinceStartupAsDouble;
            stateFrame = Time.frameCount;
            resultView = MapTest.HeightMapPlayerSceneBootstrap.inst.photoResultView;
            if (coverCanvas == null || coverGroup == null || coverImage == null || resultView == null)
            {
                Fail("Missing result view or cover references.");
                return;
            }
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Fail("No rendered warmup available in batch/headless mode.");
                return;
            }
            renderObserver = StartCoroutine(ObserveRendering());
        }

        private IEnumerator ObserveRendering()
        {
            var boundary = new WaitForEndOfFrame();
            while (true)
            {
                yield return boundary;
                renderedFrame = Time.frameCount;
                yield return null;
            }
        }

        private bool RenderedSinceTransition => renderedFrame >= stateFrame && Time.frameCount > renderedFrame;
        private void Transition(LoadingState next)
        {
            State = next;
            stateFrame = Time.frameCount;
            if (diagnostics) Debug.Log($"SceneResourceLoader: {next}", this);
        }

        private void Update()
        {
            if (!started || State == LoadingState.Completed || State == LoadingState.Failed) return;
            try { Advance(); }
            catch (Exception exception) { Fail(exception.ToString()); }
        }

        private void Advance()
        {
            if (State == LoadingState.Fading)
            {
                // Start the clock after initialization; never include a stalled initialization frame.
                fadeElapsed += Mathf.Max(0, Time.unscaledDeltaTime);
                if (coverGroup != null)
                    coverGroup.alpha = fadeStart * (1 - Mathf.SmoothStep(0, 1,
                        fadeDuration <= 0 ? 1 : Mathf.Clamp01(fadeElapsed / fadeDuration)));
                if (fadeElapsed >= fadeDuration) Finish();
                return;
            }
            if (Time.realtimeSinceStartupAsDouble - startedAt > Mathf.Max(0.1f, waitTimeout))
            {
                Fail("Timed out waiting for the result document/layout/render frames.");
                return;
            }
            if (resultView == null) { Fail("Result view was destroyed."); return; }
            if (ownsWarmup && !resultView.IsWarmingUp) { Fail("Result view warmup was interrupted."); return; }
            switch (State)
            {
                case LoadingState.Covering:
                    if (RenderedSinceTransition) Transition(LoadingState.WaitingForView);
                    break;
                case LoadingState.WaitingForView:
                    if (resultView.IsShowing || resultView.IsWarmingUp)
                    {
                        BeginFade(); // Another owner has the view. Never hide it.
                        break;
                    }
                    if (!resultView.IsWarmupReady) break;
                    ownsWarmup = resultView.TryBeginWarmup();
                    if (!ownsWarmup) { BeginFade(); break; }
                    Transition(LoadingState.Warming);
                    break;
                case LoadingState.Warming:
                    if (!resultView.HasWarmupLayout) { stableFrames = 0; break; }
                    resultView.SetWarmupProgress(pose < 2 ? 0.6f : 1);
                    if (pose == 0)
                    {
                        pose = 1;
                        stateFrame = Time.frameCount;
                        break;
                    }
                    if (!RenderedSinceTransition) break;
                    if (pose == 1)
                    {
                        pose = 2;
                        resultView.SetWarmupProgress(1);
                        stateFrame = Time.frameCount;
                        lastObservedFrame = -1;
                        observedSize = resultView.WarmupLayoutSize;
                        break;
                    }
                    if (lastObservedFrame == renderedFrame) break;
                    lastObservedFrame = renderedFrame;
                    Vector2 size = resultView.WarmupLayoutSize;
                    if ((size - observedSize).sqrMagnitude > 0.01f) stableFrames = 0;
                    else stableFrames++;
                    observedSize = size;
                    if (stableFrames < Mathf.Max(2, finalPoseFrames)) break;
                    ReleaseWarmup();
                    WarmupSucceeded = true;
                    Transition(LoadingState.Hiding);
                    break;
                case LoadingState.Hiding:
                    if (RenderedSinceTransition && Time.realtimeSinceStartupAsDouble - startedAt >= minimumBlackTime)
                        BeginFade();
                    break;
            }
        }

        private void ReleaseWarmup()
        {
            if (!ownsWarmup) return;
            ownsWarmup = false;
            if (resultView != null) resultView.EndWarmup();
        }

        private void Fail(string reason)
        {
            failed = true;
            if (!reported) { reported = true; Debug.LogError($"SceneResourceLoader ({name}): {reason}", this); }
            try { ReleaseWarmup(); }
            finally { BeginFade(); }
        }

        private void BeginFade()
        {
            fadeElapsed = 0;
            fadeStart = coverGroup != null ? coverGroup.alpha : 1;
            Transition(LoadingState.Fading);
        }

        private void Finish()
        {
            RemoveCover();
            Transition(failed ? LoadingState.Failed : LoadingState.Completed);
            StopObserver();
            Destroy(gameObject);
        }

        private void RemoveCover()
        {
            if (coverGroup != null) coverGroup.alpha = 0;
            if (coverImage != null) coverImage.enabled = false;
            if (coverCanvas != null) coverCanvas.enabled = false;
        }
        private void StopObserver()
        {
            if (renderObserver != null) StopCoroutine(renderObserver);
            renderObserver = null;
        }
        private void Cancel()
        {
            StopObserver();
            try { ReleaseWarmup(); }
            finally { RemoveCover(); }
            if (State != LoadingState.Completed) State = LoadingState.Failed;
        }
        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();
    }
}

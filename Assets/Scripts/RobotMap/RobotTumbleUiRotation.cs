using AnimalGame.MapTest;
using UnityEngine;
using System.Collections.Generic;

namespace AnimalGame.RobotMap{
[DefaultExecutionOrder(350)]
[DisallowMultipleComponent]
public sealed class RobotTumbleUiRotation : MonoBehaviour
{
    public static float ActiveRotationDegrees { get; private set; }

    private readonly Dictionary<Canvas, RectTransform> canvasPivots = new();
    private RobotTumbleController tumble;
    private Camera mapCamera;
    private Canvas mainUiCanvas;
    private Vector2 mainUiInitialPivotPosition;
    private bool mainUiInitialPositionCaptured;
    private float currentRotationDegrees;
    private int screenQuarterTurnSign = 1;
    private bool tumbleDirectionLocked;

    public void Initialize(RobotTumbleController tumbleController, Camera camera)
    {
        tumble = tumbleController;
        mapCamera = camera;
        mainUiCanvas = GetComponent<Canvas>();
    }

    private void LateUpdate()
    {
        currentRotationDegrees = CalculateTargetRotation();
        ActiveRotationDegrees = currentRotationDegrees;
        ApplyRotationToCanvasPivots();
    }

    public static Matrix4x4 BeginImmediateModeGuiRotation()
    {
        Matrix4x4 previousMatrix = GUI.matrix;
        if (Mathf.Abs(ActiveRotationDegrees) > 0.0001f)
        {
            GUIUtility.RotateAroundPivot(
                ActiveRotationDegrees,
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        }

        return previousMatrix;
    }

    private float CalculateTargetRotation()
    {
        if (tumble == null || tumble.State == RobotTumbleState.Upright)
        {
            tumbleDirectionLocked = false;
            return 0f;
        }

        if (!tumbleDirectionLocked)
            LockScreenQuarterTurnSign();

        float quarterTurnProgress = tumble.ContinuousQuarterTurnProgress;
        return screenQuarterTurnSign * quarterTurnProgress * 90f;
    }

    private void LockScreenQuarterTurnSign()
    {
        screenQuarterTurnSign = tumble != null
            ? tumble.QuarterTurnSign
            : 1;
        if (tumble != null
            && mapCamera != null
            && tumble.DirectionWorld.sqrMagnitude > 0.000001f)
        {
            Vector3 originScreen = mapCamera.WorldToScreenPoint(
                tumble.transform.position);
            Vector3 directionScreen = mapCamera.WorldToScreenPoint(
                tumble.transform.position + (Vector3)tumble.DirectionWorld);
            float screenDirectionX = directionScreen.x - originScreen.x;
            if (Mathf.Abs(screenDirectionX) > 0.001f)
                screenQuarterTurnSign = screenDirectionX > 0f ? -1 : 1;
        }

        tumbleDirectionLocked = true;
    }

    private void Start()
    {
        RegisterCanvas(mainUiCanvas);
        HeightMapPlayerSceneBootstrap bootstrap = HeightMapPlayerSceneBootstrap.inst;
        if (bootstrap == null)
            return;

        RegisterCanvas(bootstrap.scanOverlayCanvas);
        RegisterCanvas(bootstrap.traversalOverlayCanvas);
    }

    private void RegisterCanvas(Canvas canvas)
    {
        if (canvas == null || canvasPivots.ContainsKey(canvas))
            return;

        RectTransform pivot = FindOrCreateRotationPivot(canvas);
        canvasPivots.Add(canvas, pivot);
        if (canvas == mainUiCanvas)
        {
            mainUiInitialPivotPosition = pivot.anchoredPosition;
            mainUiInitialPositionCaptured = true;
        }
    }

    private static RectTransform FindOrCreateRotationPivot(Canvas canvas)
    {
        const string PivotName = "Tumble UI Rotation Pivot";
        Transform canvasTransform = canvas.transform;
        for (int i = 0; i < canvasTransform.childCount; i++)
        {
            Transform child = canvasTransform.GetChild(i);
            if (child.name == PivotName && child is RectTransform existingPivot)
                return existingPivot;
        }

        var pivotObject = new GameObject(PivotName, typeof(RectTransform));
        pivotObject.layer = canvas.gameObject.layer;
        RectTransform pivot = pivotObject.GetComponent<RectTransform>();
        pivot.SetParent(canvasTransform, false);
        pivot.anchorMin = Vector2.zero;
        pivot.anchorMax = Vector2.one;
        pivot.offsetMin = Vector2.zero;
        pivot.offsetMax = Vector2.zero;
        pivot.pivot = Vector2.one * 0.5f;
        MoveDirectCanvasChildrenUnderPivot(canvas, pivot);
        return pivot;
    }

    private static void MoveDirectCanvasChildrenUnderPivot(
        Canvas canvas,
        RectTransform pivot)
    {
        Transform canvasTransform = canvas.transform;
        for (int i = canvasTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = canvasTransform.GetChild(i);
            if (child == pivot)
                continue;

            child.SetParent(pivot, false);
        }
    }

    private void ApplyRotationToCanvasPivots()
    {
        var missingCanvases = new List<Canvas>();
        foreach (KeyValuePair<Canvas, RectTransform> canvasPivot in canvasPivots)
        {
            if (canvasPivot.Key == null || canvasPivot.Value == null)
            {
                missingCanvases.Add(canvasPivot.Key);
                continue;
            }

            canvasPivot.Value.localRotation = Quaternion.Euler(
                0f,
                0f,
                currentRotationDegrees);
            if (canvasPivot.Key == mainUiCanvas
                && mainUiInitialPositionCaptured)
            {
                // Only MainUI is position-locked. Player/world canvases keep
                // their own positioning rules and are never forced to zero.
                canvasPivot.Value.anchoredPosition =
                    mainUiInitialPivotPosition;
            }
        }

        foreach (Canvas missingCanvas in missingCanvases)
            canvasPivots.Remove(missingCanvas);
    }

    private void RestoreCanvasRotations()
    {
        foreach (KeyValuePair<Canvas, RectTransform> canvasPivot in canvasPivots)
        {
            if (canvasPivot.Value != null)
            {
                canvasPivot.Value.localRotation = Quaternion.identity;
                if (canvasPivot.Key == mainUiCanvas
                    && mainUiInitialPositionCaptured)
                {
                    canvasPivot.Value.anchoredPosition =
                        mainUiInitialPivotPosition;
                }
            }
        }

        currentRotationDegrees = 0f;
        ActiveRotationDegrees = 0f;
    }

    private void OnDisable()
    {
        RestoreCanvasRotations();
    }

    private void OnDestroy()
    {
        RestoreCanvasRotations();
    }
}
}

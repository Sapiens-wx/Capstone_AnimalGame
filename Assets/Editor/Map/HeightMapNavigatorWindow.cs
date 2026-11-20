using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class HeightMapNavigatorWindow : EditorWindow
{
    private readonly List<MapTestSceneController> maps = new();
    private readonly Dictionary<GameObject, bool> originalStates = new();
    private RenderTexture overview;
    private Rect overviewWorldRect;
    private Rect visibleWorldRect;
    private SceneView targetView;
    private bool hasViewRect;
    private bool mapsDirty = true;
    [SerializeField] private bool dynamicTileActivation = true;
    private bool dragging;
    private Vector2 dragStart;
    private Vector3 dragPivot;

    [MenuItem("Animal Game/Level/Navigator")]
    public static void Open()
    {
        var window = GetWindow<HeightMapNavigatorWindow>();
        window.titleContent = new GUIContent("Navigator");
        window.minSize = new Vector2(280f, 300f);
        window.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("Navigator");
        minSize = new Vector2(280f, 300f);
        SceneView.duringSceneGui += HandleSceneGui;
        EditorApplication.hierarchyChanged += HandleHierarchyChanged;
        MapTestSceneController.EditorTilesChanged += HandleHierarchyChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        EditorApplication.delayCall += Initialize;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= HandleSceneGui;
        EditorApplication.hierarchyChanged -= HandleHierarchyChanged;
        MapTestSceneController.EditorTilesChanged -= HandleHierarchyChanged;
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.delayCall -= Initialize;
        RestoreTiles();
        ReleaseOverview();
    }

    private void Initialize()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        SynchronizeTiles();
        if (!dynamicTileActivation)
            ActivateAllTiles();
        CaptureOverview();
        hasViewRect = false;
        SceneView.RepaintAll();
        Repaint();
    }

    private void HandleHierarchyChanged() => mapsDirty = true;

    private void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            RestoreTiles();
            hasViewRect = false;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            mapsDirty = true;
            SceneView.RepaintAll();
        }
        Repaint();
    }

    private bool SynchronizeTiles()
    {
        mapsDirty = false;
        maps.Clear();
        var live = new HashSet<GameObject>();
        bool changed = false;
        foreach (var map in FindObjectsByType<MapTestSceneController>(FindObjectsSortMode.None))
        {
            if (EditorUtility.IsPersistent(map) || !map.HasGeneratedMap)
                continue;
            maps.Add(map);
            foreach (SpriteRenderer tile in map.EditorTileRenderers)
            {
                GameObject go = tile.gameObject;
                live.Add(go);
                if (originalStates.ContainsKey(go))
                    continue;
                originalStates.Add(go, go.activeSelf);
                changed = true;
            }
        }
        var removed = new List<GameObject>();
        foreach (var pair in originalStates)
        {
            if (live.Contains(pair.Key))
                continue;
            if (pair.Key != null && pair.Key.activeSelf != pair.Value)
                pair.Key.SetActive(pair.Value);
            removed.Add(pair.Key);
        }
        foreach (GameObject go in removed)
            originalStates.Remove(go);
        return changed || removed.Count != 0;
    }

    private void HandleSceneGui(SceneView view)
    {
        if (Event.current.type != EventType.Repaint || view != SceneView.lastActiveSceneView
            || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        targetView = view;
        UpdateVisibility(view.camera);
    }

    internal void UpdateVisibility(Camera camera)
    {
        bool refreshVisibility = mapsDirty;
        if (refreshVisibility)
            SynchronizeTiles();
        if (refreshVisibility && !dynamicTileActivation)
            ActivateAllTiles();
        if (!TryGetViewRect(camera, out Rect rect))
        {
            if (hasViewRect)
            {
                if (dynamicTileActivation)
                    RestoreTileStates();
                hasViewRect = false;
                if (targetView != null) targetView.Repaint();
                Repaint();
            }
            return;
        }
        if (!refreshVisibility && hasViewRect && rect == visibleWorldRect)
            return;
        visibleWorldRect = rect;
        hasViewRect = true;
        if (dynamicTileActivation)
            ApplyVisibility(rect);
        if (targetView != null) targetView.Repaint();
        Repaint();
    }

    internal static bool TryGetViewRect(Camera camera, out Rect rect)
    {
        rect = default;
        if (camera == null || !camera.orthographic
            || Mathf.Abs(Vector3.Dot(camera.transform.forward, Vector3.forward)) < 0.9999f)
            return false;
        Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity, float.NegativeInfinity);
        var plane = new Plane(Vector3.forward, Vector3.zero);
        for (int y = 0; y <= 1; y++)
        for (int x = 0; x <= 1; x++)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(x, y, 0));
            if (!plane.Raycast(ray, out float distance))
                return false;
            Vector2 point = ray.GetPoint(distance);
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }
        rect = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        return true;
    }

    internal static bool TileIsVisible(Rect viewRect, Vector2 center, Vector2 tileSize)
    {
        return center.x >= viewRect.xMin - tileSize.x && center.x <= viewRect.xMax + tileSize.x
            && center.y >= viewRect.yMin - tileSize.y && center.y <= viewRect.yMax + tileSize.y;
    }

    private void ApplyVisibility(Rect rect)
    {
        foreach (var pair in originalStates)
        {
            Transform tile = pair.Key.transform;
            var renderer = pair.Key.GetComponent<SpriteRenderer>();
            Vector2 size = Vector3.Scale(renderer.sprite.bounds.size, tile.lossyScale);
            bool active = TileIsVisible(rect, tile.position, size);
            if (pair.Key.activeSelf != active)
                pair.Key.SetActive(active);
        }
    }

    private void SetDynamicTileActivation(bool enabled)
    {
        dynamicTileActivation = enabled;
        if (mapsDirty)
            SynchronizeTiles();
        if (!enabled)
            ActivateAllTiles();
        hasViewRect = false;
        SceneView.RepaintAll();
        Repaint();
    }

    private void ActivateAllTiles()
    {
        foreach (GameObject tile in originalStates.Keys)
            if (!tile.activeSelf)
                tile.SetActive(true);
    }

    private void RestoreTileStates()
    {
        foreach (var pair in originalStates)
            if (pair.Key != null && pair.Key.activeSelf != pair.Value)
                pair.Key.SetActive(pair.Value);
    }

    private void RestoreTiles()
    {
        RestoreTileStates();
        originalStates.Clear();
        maps.Clear();
    }

    private void CaptureOverview()
    {
        ReleaseOverview();
        if (maps.Count == 0)
            return;

        Bounds bounds = maps[0].WorldBounds;
        foreach (var map in maps)
            bounds.Encapsulate(map.WorldBounds);
        float longestSide = Mathf.Max(bounds.size.x, bounds.size.y);
        int width = Mathf.Max(1, Mathf.RoundToInt(1024 * bounds.size.x / longestSide));
        int height = Mathf.Max(1, Mathf.RoundToInt(1024 * bounds.size.y / longestSide));
        var preview = new PreviewRenderUtility();
        var materials = new List<Material>();
        RenderTexture previous = RenderTexture.active;
        try
        {
            var properties = new MaterialPropertyBlock();
            foreach (var map in maps)
            {
                var tiles = map.EditorTileRenderers;
                if (tiles.Count == 0) continue;
                var material = new Material(tiles[0].sharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
                materials.Add(material);
                material.SetFloat("_SurfaceRevealEnabled", 0f);
                material.SetFloat("_VisibleMinimumHeight", map.Presentation.MinimumHeightMeters);
                material.SetFloat("_VisibleMaximumHeight", map.Presentation.MaximumHeightMeters);
                foreach (var source in tiles)
                {
                    var go = new GameObject("Navigator Tile") { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    go.transform.localScale = source.transform.lossyScale;
                    var renderer = go.AddComponent<SpriteRenderer>();
                    renderer.sprite = source.sprite;
                    renderer.sortingOrder = source.sortingOrder;
                    renderer.sharedMaterial = material;
                    source.GetPropertyBlock(properties);
                    renderer.SetPropertyBlock(properties);
                    preview.AddSingleGO(go);
                }
            }
            Camera camera = preview.camera;
            camera.orthographic = true;
            camera.aspect = width / (float)height;
            camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / camera.aspect);
            camera.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 10f), Quaternion.identity);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = bounds.size.z + 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = maps[0].BackgroundColor;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            overview = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            { name = "Navigator Overview", hideFlags = HideFlags.HideAndDontSave };
            overview.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = overview };
            RenderPipeline.SubmitRenderRequest(camera, request);
            float worldHeight = camera.orthographicSize * 2f;
            Vector2 size = new(worldHeight * camera.aspect, worldHeight);
            overviewWorldRect = new Rect((Vector2)bounds.center - size * 0.5f, size);
        }
        finally
        {
            RenderTexture.active = previous;
            preview.Cleanup();
            foreach (Material material in materials)
                DestroyImmediate(material);
        }
    }

    private void ReleaseOverview()
    {
        if (overview == null)
            return;
        overview.Release();
        DestroyImmediate(overview);
        overview = null;
    }

    private void OnGUI()
    {
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            bool enabled = EditorGUILayout.Toggle("Dynamic Tile Activation", dynamicTileActivation);
            if (enabled != dynamicTileActivation)
                SetDynamicTileActivation(enabled);
            if (GUILayout.Button("Refresh Overview"))
                Initialize();
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("Navigator is paused during Play Mode.", MessageType.Info);
            return;
        }
        if (overview == null)
        {
            EditorGUILayout.HelpBox("No generated height-map tiles in the open scene.", MessageType.Info);
            return;
        }
        EditorGUILayout.LabelField($"{originalStates.Count} tiles · drag the cyan view rectangle to navigate");
        if (!hasViewRect)
            EditorGUILayout.HelpBox("Use a front-facing orthographic Scene view (2D) to manage tile visibility.", MessageType.Info);
        Rect area = GUILayoutUtility.GetRect(100f, 100f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        Rect image = FitImage(area, overview.width / (float)overview.height);
        GUI.DrawTexture(image, overview, ScaleMode.StretchToFill, false);
        if (!hasViewRect)
            return;
        Rect viewRect = WorldToImageRect(visibleWorldRect, overviewWorldRect, image);
        GUI.BeginClip(image);
        Rect clipped = new(viewRect.position - image.position, viewRect.size);
        EditorGUI.DrawRect(clipped, new Color(0f, 1f, 1f, 0.08f));
        EditorGUI.DrawRect(new Rect(clipped.xMin, clipped.yMin, clipped.width, 2f), Color.cyan);
        EditorGUI.DrawRect(new Rect(clipped.xMin, clipped.yMax - 2f, clipped.width, 2f), Color.cyan);
        EditorGUI.DrawRect(new Rect(clipped.xMin, clipped.yMin, 2f, clipped.height), Color.cyan);
        EditorGUI.DrawRect(new Rect(clipped.xMax - 2f, clipped.yMin, 2f, clipped.height), Color.cyan);
        GUI.EndClip();
        HandleDrag(image, viewRect);
    }

    internal static Rect FitImage(Rect area, float aspect)
    {
        float width = Mathf.Min(area.width, area.height * aspect);
        Vector2 size = new(width, width / aspect);
        return new Rect(area.center - size * 0.5f, size);
    }

    internal static Rect WorldToImageRect(Rect world, Rect overviewRect, Rect image)
    {
        return new Rect(image.x + (world.xMin - overviewRect.xMin) / overviewRect.width * image.width,
            image.y + (overviewRect.yMax - world.yMax) / overviewRect.height * image.height,
            world.width / overviewRect.width * image.width, world.height / overviewRect.height * image.height);
    }

    internal static Vector3 DragOffset(Vector2 pixels, Rect image, Rect world)
        => new(pixels.x / image.width * world.width, -pixels.y / image.height * world.height, 0f);

    internal static Vector3 DragStartPivot(Vector2 point, Rect image, Rect world, Rect viewRect, Vector3 pivot)
    {
        if (viewRect.Contains(point))
            return pivot;
        return new Vector3(world.xMin + (point.x - image.xMin) / image.width * world.width,
            world.yMax - (point.y - image.yMin) / image.height * world.height, pivot.z);
    }

    private void HandleDrag(Rect image, Rect viewRect)
    {
        Event current = Event.current;
        int control = GUIUtility.GetControlID("NavigatorDrag".GetHashCode(), FocusType.Passive);
        if (current.type == EventType.MouseDown && current.button == 0
            && image.Contains(current.mousePosition) && targetView != null)
        {
            dragging = true;
            dragStart = current.mousePosition;
            dragPivot = DragStartPivot(current.mousePosition, image, overviewWorldRect, viewRect, targetView.pivot);
            if (dragPivot != targetView.pivot)
            {
                targetView.LookAtDirect(dragPivot, targetView.rotation, targetView.size);
                targetView.Repaint();
            }
            GUIUtility.hotControl = control;
            current.Use();
        }
        else if (dragging && GUIUtility.hotControl == control && current.type == EventType.MouseDrag)
        {
            Vector3 position = dragPivot + DragOffset(current.mousePosition - dragStart, image, overviewWorldRect);
            targetView.LookAtDirect(position, targetView.rotation, targetView.size);
            targetView.Repaint();
            current.Use();
        }
        else if (dragging && current.rawType == EventType.MouseUp)
        {
            dragging = false;
            GUIUtility.hotControl = 0;
            current.Use();
        }
    }
}

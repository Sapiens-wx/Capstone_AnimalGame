using System.IO;
using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

public sealed class HeightMapStaticWaterPainterWindow : EditorWindow
{
    private const float MinimumBrushRadiusMeters = 0.25f;
    private const float DefaultBrushRadiusMeters = 8f;
    private const int BrushOutlineSegments = 64;
    private const string DefaultWaterTexturePath =
        "Assets/Maps/MainHeightMapLevel/data/Texture_Water.png";
    private const string PreviewShaderName =
        "Hidden/AnimalGame/StaticWaterEditorPreview";

    private static readonly string[] WaterAuthoringProperties =
    {
        "staticWaterDepthMap", "staticWaterFlowAngleMap", "staticWaterFlowSpeedMap", "staticWaterResolution",
        "staticWaterPixelBounds", "staticWaterDataResolution",
        "maximumStaticWaterDepthMeters", "maximumPassableStaticWaterDepthMeters",
        "staticWaterEditorTexture", "staticWaterEditorTint",
        "staticWaterEditorTileSizeMeters",
        "staticWaterLayerOneSpeedMetersPerSecond",
        "staticWaterLayerTwoSpeedMetersPerSecond", "staticWaterLayerTwoScale",
        "staticWaterWaveDistortion", "staticWaterWaveSpeed",
        "staticWaterWaveLengthMeters", "staticWaterDeepSpeedMultiplier"
    };

    private HeightMapLevelAsset sourceLevelAsset;
    // All editing and Undo operate on a temporary copy until Save is clicked.
    private HeightMapLevelAsset levelAsset;
    private MapTestSceneController mapController;
    private SerializedObject serializedLevel;
    private Vector2 scrollPosition;
    private int brushPage;
    private int strokeBrushPage;
    private bool strokeErase;
    private float flowAngleDegrees;
    private float flowSpeed = 0.5f;
    private StaticWaterFlowPaintMode flowPaintMode;
    private StaticWaterFlowPaintMode strokeFlowPaintMode;
    private bool hasStrokeDirection;
    private float strokeFlowAngleDegrees;
    private float directionPointSpacingMeters = 4.5f;
    private StaticWaterDepthPaintMode paintMode =
        StaticWaterDepthPaintMode.Set;
    private float brushRadiusMeters = DefaultBrushRadiusMeters;
    private float targetDepthMeters = 1f;
    private float depthStepMeters = 0.25f;
    private float brushStrength = 1f;
    private float brushHardness = 0.75f;
    private bool paintInScene = true;
    private bool strokeActive;
    private StaticWaterDepthPaintMode strokePaintMode;
    private bool strokeChanged;
    private bool hasLastStrokePosition;
    private Vector2 lastStrokeMapPosition;
    private int strokeUndoGroup = -1;
    private int lastObservedAuthoringHash;
    private bool previewDirty = true;
    private double nextPreviewRefreshTime;
    private int wetPixelCount;
    private float minimumWetDepth;
    private float maximumWetDepth;
    private float averageWetDepth;

    private GameObject previewObject;
    private Mesh previewMesh;
    private MeshRenderer previewRenderer;
    private Material previewMaterial;
    private Texture2D previewDepthTexture;

    [MenuItem("Animal Game/Level/Static Water Painter")]
    public static void Open()
    {
        var window = GetWindow<HeightMapStaticWaterPainterWindow>();
        window.titleContent = new GUIContent("Static Water");
        window.minSize = new Vector2(360f, 520f);
        window.TryAdoptCurrentSelection();
        window.Show();
    }

    public static void OpenForAsset(HeightMapLevelAsset asset)
    {
        var window = GetWindow<HeightMapStaticWaterPainterWindow>();
        window.titleContent = new GUIContent("Static Water");
        window.minSize = new Vector2(360f, 520f);
        window.SetLevelAsset(asset);
        window.Show();
        window.Focus();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("Static Water");
        minSize = new Vector2(360f, 520f);
        SceneView.duringSceneGui += HandleSceneGui;
        Undo.undoRedoPerformed += HandleUndoRedo;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        if (levelAsset == null)
            TryAdoptCurrentSelection();
    }

    private void OnDisable()
    {
        CancelUvInteraction();
        ClearUvSelection();
        if (strokeActive)
            EndStroke();

        SceneView.duringSceneGui -= HandleSceneGui;
        Undo.undoRedoPerformed -= HandleUndoRedo;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        DestroyPreviewResources();
        DestroyWorkingCopy();
        serializedLevel = null;
        SceneView.RepaintAll();
    }

    private void OnFocus()
    {
        ResolveSceneMap();
        RefreshPreview(true);
        Repaint();
    }

    private void OnHierarchyChange()
    {
        ResolveSceneMap();
        previewDirty = true;
        Repaint();
    }

    private void OnGUI()
    {
        using (new EditorGUI.DisabledScope(sourceLevelAsset == null))
        {
            if (GUILayout.Button("Save", GUILayout.Height(28f)))
                SaveWater();
        }
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.LabelField("Static Water Authoring", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Edits appear only in the Scene preview until you click Save. " +
            "Save writes the water data, runtime Water Mask and generated UV texture. " +
            "Closing this window or switching maps discards unsaved edits.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        HeightMapLevelAsset selectedAsset = (HeightMapLevelAsset)
            EditorGUILayout.ObjectField(
                "Fixed Map Asset",
                sourceLevelAsset,
                typeof(HeightMapLevelAsset),
                false);
        if (EditorGUI.EndChangeCheck())
            SetLevelAsset(selectedAsset);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Scene Map",
                mapController,
                typeof(MapTestSceneController),
                true);
        }

        if (levelAsset == null)
        {
            EditorGUILayout.HelpBox(
                "Select a HeightMapLevelAsset or a scene MapTestController.",
                MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawWaterMapSettings();
        DrawBrushSettings();
        DrawWaterStatistics();
        if (brushPage == 0)
            DrawBulkActions();

        if (mapController == null || !mapController.HasGeneratedMap)
        {
            EditorGUILayout.HelpBox(
                "Open the scene that uses this level asset to paint and preview " +
                "water in the Scene view.",
                MessageType.Warning);
        }

        EditorGUILayout.EndScrollView();

        if (previewDirty
            && EditorApplication.timeSinceStartup >= nextPreviewRefreshTime)
        {
            RefreshPreview(false);
        }
    }

    private void DrawWaterMapSettings()
    {
        if (serializedLevel == null)
            serializedLevel = new SerializedObject(levelAsset);

        serializedLevel.Update();
        SerializedProperty resolution = serializedLevel.FindProperty(
            "staticWaterResolution");
        SerializedProperty maximumDepth = serializedLevel.FindProperty(
            "maximumStaticWaterDepthMeters");
        SerializedProperty passableDepth = serializedLevel.FindProperty(
            "maximumPassableStaticWaterDepthMeters");
        SerializedProperty previewTexture = serializedLevel.FindProperty(
            "staticWaterEditorTexture");
        SerializedProperty previewTint = serializedLevel.FindProperty(
            "staticWaterEditorTint");
        SerializedProperty previewTileSize = serializedLevel.FindProperty(
            "staticWaterEditorTileSizeMeters");
        SerializedProperty layerOneSpeed = serializedLevel.FindProperty(
            "staticWaterLayerOneSpeedMetersPerSecond");
        SerializedProperty layerTwoSpeed = serializedLevel.FindProperty(
            "staticWaterLayerTwoSpeedMetersPerSecond");
        SerializedProperty layerTwoScale = serializedLevel.FindProperty(
            "staticWaterLayerTwoScale");
        SerializedProperty waveDistortion = serializedLevel.FindProperty(
            "staticWaterWaveDistortion");
        SerializedProperty waveSpeed = serializedLevel.FindProperty(
            "staticWaterWaveSpeed");
        SerializedProperty waveLength = serializedLevel.FindProperty(
            "staticWaterWaveLengthMeters");
        SerializedProperty deepSpeedMultiplier = serializedLevel.FindProperty(
            "staticWaterDeepSpeedMultiplier");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Water Map", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(resolution, new GUIContent("Full Map Resolution"));
        EditorGUILayout.PropertyField(
            maximumDepth,
            new GUIContent("Maximum Depth (m)"));
        EditorGUILayout.PropertyField(
            passableDepth,
            new GUIContent("Maximum Passable Depth (m)"));
        EditorGUILayout.PropertyField(
            previewTexture,
            new GUIContent("Preview Texture"));
        EditorGUILayout.PropertyField(
            previewTint,
            new GUIContent("Preview Tint"));
        EditorGUILayout.PropertyField(
            previewTileSize,
            new GUIContent("Water Tile Size (m)"));
        EditorGUILayout.PropertyField(
            layerOneSpeed,
            new GUIContent("Primary Speed (m/s)"));
        EditorGUILayout.PropertyField(
            layerTwoSpeed,
            new GUIContent("Secondary Speed (m/s)"));
        EditorGUILayout.PropertyField(
            layerTwoScale,
            new GUIContent("Secondary Scale"));
        EditorGUILayout.PropertyField(
            waveDistortion,
            new GUIContent("Wave Distortion"));
        EditorGUILayout.PropertyField(
            waveSpeed,
            new GUIContent("Wave Speed"));
        EditorGUILayout.PropertyField(
            waveLength,
            new GUIContent("Wave Length (m)"));
        EditorGUILayout.PropertyField(
            deepSpeedMultiplier,
            new GUIContent("Deep Speed Multiplier"));
        bool settingsChanged = EditorGUI.EndChangeCheck();
        serializedLevel.ApplyModifiedProperties();

        if (!settingsChanged)
            return;

        levelAsset.EnsureStaticWaterAuthoringData();
        targetDepthMeters = Mathf.Clamp(
            targetDepthMeters,
            0f,
            levelAsset.MaximumStaticWaterDepthMeters);
        RefreshPreview(true);
    }

    private void DrawBrushSettings()
    {
        EditorGUILayout.Space();
        int selectedPage = GUILayout.Toolbar(brushPage, new[] { "Depth Brush", "Flow Brush", "UV Lines" });
        if (selectedPage != brushPage)
        {
            EndStroke();
            CancelUvInteraction();
            brushPage = selectedPage;
            RefreshPreview(true);
        }
        if (brushPage == 2)
        {
            DrawUvSettings();
            return;
        }
        paintInScene = EditorGUILayout.Toggle("Paint In Scene", paintInScene);
        if (brushPage == 1)
        {
            EditorGUI.BeginChangeCheck();
            var selectedMode = (StaticWaterFlowPaintMode)EditorGUILayout.EnumPopup("Mode", flowPaintMode);
            if (EditorGUI.EndChangeCheck())
            {
                EndStroke();
                flowPaintMode = selectedMode;
            }
            if (flowPaintMode == StaticWaterFlowPaintMode.Direction)
                flowAngleDegrees = EditorGUILayout.Slider("Direction (degrees)", flowAngleDegrees, 0f, 360f);
            if (flowPaintMode == StaticWaterFlowPaintMode.Speed)
                flowSpeed = EditorGUILayout.Slider("Flow Speed", flowSpeed, 0f, 1f);
            EditorGUI.BeginChangeCheck();
            directionPointSpacingMeters = Mathf.Max(0.25f, EditorGUILayout.FloatField(
                new GUIContent("Direction Point Spacing (m)", "Smaller spacing displays denser direction lines. Maximum density is one point per authoring pixel."),
                directionPointSpacingMeters));
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
            DrawCommonBrushSettings();
            EditorGUILayout.HelpBox(
                (flowPaintMode == StaticWaterFlowPaintMode.Speed
                    ? "Paint speed only. Shift paints zero speed. Direction is preserved. "
                    : flowPaintMode == StaticWaterFlowPaintMode.StrokeDirection
                        ? "Drag to paint direction along the stroke. A click alone does not paint. Speed is preserved. "
                        : "Paint direction only. 0 degrees points right (+X); 90 degrees points up (+Y). Speed is preserved. ") +
                "Strength blends toward the target; Hardness controls edge falloff. " +
                "Darker blue means faster flow. White arrows show direction.",
                MessageType.None);
            return;
        }
        paintMode = (StaticWaterDepthPaintMode)EditorGUILayout.EnumPopup(
            "Mode",
            paintMode);
        using (new EditorGUI.DisabledScope(
                   paintMode != StaticWaterDepthPaintMode.Set))
        {
            targetDepthMeters = EditorGUILayout.Slider(
                "Target Depth (m)",
                targetDepthMeters,
                0f,
                levelAsset.MaximumStaticWaterDepthMeters);
        }

        using (new EditorGUI.DisabledScope(
                   paintMode != StaticWaterDepthPaintMode.Add
                   && paintMode != StaticWaterDepthPaintMode.Subtract))
        {
            depthStepMeters = Mathf.Max(
                0.01f,
                EditorGUILayout.FloatField(
                    "Depth Change Per Dab (m)",
                    depthStepMeters));
        }

        DrawCommonBrushSettings();
        EditorGUILayout.HelpBox(
            "Left-drag applies the selected brush. Hold Shift while left-dragging " +
            "to erase, regardless of the selected mode. Alt remains available " +
            "for normal Scene-view navigation.",
            MessageType.None);
    }

    // UV line previews and working UV pixels remain temporary until SaveWater.
    private enum UvInteraction { None, SelectWater, DrawSection }
    private UvInteraction uvInteraction;
    private int uvHotControl;
    private bool uvDragging;
    private Vector2 uvSectionA, uvSectionB;
    private bool uvHasSection;
    private Vector2Int uvWaterSeed;
    private bool[] uvIsland;
    private RectInt uvBounds;
    private int uvResolution;
    private byte[] uvAngles, uvSpeeds;
    private RectInt uvDataBounds;
    private int uvIslandPixelCount;
    private float uvVSpacing = 3f;
    private float uvUSpacing = 6f;
    private float uvStep = 1f;
    private float uvCurveSpacing = 1f;
    private string uvStatus;
    private readonly List<Vector2[]> uvVLines = new List<Vector2[]>();
    private readonly List<Vector2[]> uvULines = new List<Vector2[]>();
    private readonly List<UvGridVertex[]> uvGridRows = new List<UvGridVertex[]>();
    private readonly List<float> uvRowDistances = new List<float>();
    private Vector2[] workingWaterUv;
    private int workingWaterUvResolution;

    private struct UvGridVertex
    {
        public Vector2 position, uv;
        public UvGridVertex(Vector2 position, Vector2 uv) { this.position = position; this.uv = uv; }
    }

    private void DrawUvSettings()
    {
        EditorGUILayout.HelpBox(
            "Select connected water, then drag A to B " +
            "to draw a cross-section. The green arrow points 90 degrees counterclockwise from A to B. " +
            "Update advances along one river, re-centers at each cross-section, then connects matching samples " +
            "to form V lines and bake pixel UVs. Save writes the RGHalf UV texture. " +
            "All distances below are in water-data pixels.",
            MessageType.Info);
        if (GUILayout.Button(uvInteraction == UvInteraction.SelectWater
                ? "Click Water In Scene..." : "1. Select Water Island"))
        {
            CancelUvInteraction();
            uvInteraction = UvInteraction.SelectWater;
            SceneView.RepaintAll();
        }
        if (uvIsland != null)
            EditorGUILayout.LabelField("Selected Island",
                $"{uvIslandPixelCount} pixels; bounds ({uvBounds.x}, {uvBounds.y}), {uvBounds.width} x {uvBounds.height}");
        using (new EditorGUI.DisabledScope(uvIsland == null))
        {
            if (GUILayout.Button(uvInteraction == UvInteraction.DrawSection
                    ? "Drag A To B In Scene..." : "2. Draw Cross-Section"))
            {
                CancelUvInteraction();
                uvInteraction = UvInteraction.DrawSection;
                SceneView.RepaintAll();
            }
        }
        uvVSpacing = Mathf.Max(1f, EditorGUILayout.FloatField("V Seed Spacing (px)", uvVSpacing));
        uvUSpacing = Mathf.Max(1f, EditorGUILayout.FloatField("U Seed Spacing (px)", uvUSpacing));
        uvStep = Mathf.Max(0.1f, EditorGUILayout.FloatField("Step Length (px)", uvStep));
        uvCurveSpacing = Mathf.Max(0.25f, EditorGUILayout.FloatField(
            new GUIContent("V Curve Spacing (px)", "Approximate arc-length spacing along Catmull-Rom V curves. Preview and UV baking share these samples."),
            uvCurveSpacing));
        EditorGUILayout.LabelField("Center Trace Budget", "3 x (bounds width + height)");
        EditorGUILayout.LabelField("U Max Length", "bounds width + height (both sides combined)");
        using (new EditorGUI.DisabledScope(uvIsland == null || !uvHasSection || uvDragging))
        {
            if (GUILayout.Button("3. Update UV Lines", GUILayout.Height(28f)))
            {
                CancelUvInteraction();
                UpdateUvLines();
            }
        }
        using (new EditorGUI.DisabledScope(uvIsland == null))
        {
            if (GUILayout.Button("Clear Selected Water UV"))
            {
                EnsureWorkingWaterUv();
                for (int i = 0; i < uvIsland.Length; i++)
                    if (uvIsland[i]) workingWaterUv[i] = Vector2.zero;
                uvStatus = "Selected water UV cleared. Click Save to apply.";
            }
        }
        if (GUILayout.Button("Clear UV Lines"))
        {
            CancelUvInteraction();
            ClearUvSelection();
            SceneView.RepaintAll();
        }
        EditorGUILayout.HelpBox(
            "V: cyan; U: orange. U Spacing advances along the river; V Spacing samples " +
            "each cross-section outward from its arc-length midpoint. V lines connect matching sample indices " +
            "without sampling flow. V lines use centripetal Catmull-Rom interpolation through their original intersections. " +
            "Curve spacing controls pixel arc-length subdivision for preview and baking. Flood fill uses four neighbors. " +
            "Outside water, seeds travel straight until entry or the length limit. " +
            "After entry, leaving the selected water or its bounds stops the line. " +
            "Zero speed stops tracing. Only step endpoints are checked. Escape cancels Scene input. " +
            "Changes to brushes or spacing require another Update.", MessageType.None);
        if (!string.IsNullOrEmpty(uvStatus))
            EditorGUILayout.HelpBox(uvStatus, MessageType.Info);
    }

    private void CancelUvInteraction()
    {
        if (uvHotControl != 0 && GUIUtility.hotControl == uvHotControl)
            GUIUtility.hotControl = 0;
        uvHotControl = 0;
        uvDragging = false;
        uvInteraction = UvInteraction.None;
    }

    private void ClearUvSelection()
    {
        uvIsland = null;
        uvAngles = uvSpeeds = null;
        uvHasSection = false;
        uvVLines.Clear();
        uvULines.Clear();
        uvGridRows.Clear();
        uvRowDistances.Clear();
        uvStatus = null;
    }

    private Vector2 UvMapToPixel(Vector2 map)
    {
        Vector2 size = levelAsset.MapSizeMeters;
        return new Vector2(map.x / size.x, map.y / size.y) * levelAsset.StaticWaterResolution;
    }

    private Vector3 UvPixelToWorld(Vector2 pixel)
    {
        Vector2 size = levelAsset.MapSizeMeters;
        return TilePositionToWorld(new Vector2(pixel.x * size.x, pixel.y * size.y) / uvResolution);
    }

    private void HandleUvSceneGui(SceneView sceneView)
    {
        Event current = Event.current;
        if (current.type == EventType.Repaint)
            DrawUvPreview();
        if (uvInteraction == UvInteraction.None)
            return;
        int control = GUIUtility.GetControlID("StaticWaterUvPreview".GetHashCode(), FocusType.Passive);
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
        {
            CancelUvInteraction();
            current.Use();
            Repaint();
            sceneView.Repaint();
            return;
        }
        if (current.type == EventType.Layout && !current.alt)
            HandleUtility.AddDefaultControl(control);
        if (current.alt || current.button != 0)
            return;
        bool hit = TryGetMapPosition(current.mousePosition, out Vector2 map);
        if (current.type == EventType.MouseDown && hit)
        {
            Vector2 pixel = UvMapToPixel(map);
            if (uvInteraction == UvInteraction.SelectWater)
            {
                ClearUvSelection();
                uvWaterSeed = Vector2Int.FloorToInt(pixel);
                if (SelectUvIsland())
                    uvInteraction = UvInteraction.None;
            }
            else
            {
                uvSectionA = uvSectionB = pixel;
                uvHasSection = false;
                uvDragging = true;
                uvHotControl = control;
                GUIUtility.hotControl = control;
            }
            current.Use();
        }
        else if (uvDragging && current.type == EventType.MouseDrag)
        {
            if (hit) uvSectionB = UvMapToPixel(map);
            current.Use();
        }
        else if (uvDragging && current.type == EventType.MouseUp)
        {
            if (hit) uvSectionB = UvMapToPixel(map);
            uvHasSection = (uvSectionB - uvSectionA).sqrMagnitude >= 1f;
            CancelUvInteraction();
            current.Use();
        }
        Repaint();
        if (current.type != EventType.Repaint && current.type != EventType.Layout)
            sceneView.Repaint();
    }

    private bool SelectUvIsland()
    {
        levelAsset.EnsureStaticWaterAuthoringData();
        uvResolution = levelAsset.StaticWaterResolution;
        uvDataBounds = levelAsset.StaticWaterPixelBounds;
        byte[] depths = levelAsset.GetOrCreateStaticWaterDepthMap();
        var selected = new bool[uvResolution * uvResolution];
        var queue = new Queue<Vector2Int>();
        bool IsWet(Vector2Int p) => uvDataBounds.Contains(p)
            && depths[(p.y - uvDataBounds.y) * uvDataBounds.width + p.x - uvDataBounds.x] > 0;
        if (!IsWet(uvWaterSeed))
        {
            ClearUvSelection();
            uvStatus = "Click a water pixel to select an island.";
            return false;
        }
        queue.Enqueue(uvWaterSeed);
        selected[uvWaterSeed.y * uvResolution + uvWaterSeed.x] = true;
        int minX = uvWaterSeed.x, maxX = minX, minY = uvWaterSeed.y, maxY = minY;
        uvIslandPixelCount = 0;
        void Visit(Vector2Int p)
        {
            if (!IsWet(p)) return;
            int index = p.y * uvResolution + p.x;
            if (selected[index]) return;
            selected[index] = true;
            queue.Enqueue(p);
        }
        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            uvIslandPixelCount++;
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            Visit(p + Vector2Int.left); Visit(p + Vector2Int.right);
            Visit(p + Vector2Int.up); Visit(p + Vector2Int.down);
        }
        uvIsland = selected;
        uvBounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        // Keep immutable flow data for this Update, separate from subsequent brush changes.
        uvAngles = (byte[])levelAsset.GetOrCreateStaticWaterFlowAngleMap().Clone();
        uvSpeeds = (byte[])levelAsset.GetOrCreateStaticWaterFlowSpeedMap().Clone();
        uvStatus = "Island selected. Draw a cross-section, then press Update UV Lines.";
        return true;
    }

    private bool UvContainsWater(Vector2 p)
    {
        Vector2Int pixel = Vector2Int.FloorToInt(p);
        return uvBounds.Contains(pixel) && uvIsland[pixel.y * uvResolution + pixel.x];
    }

    private Vector2 SampleUvDirection(Vector2 p, bool transverse, float sign)
    {
        // Decode before interpolation so the angle wrap at 0/360 degrees stays continuous.
        Vector2 sample = p - Vector2.one * 0.5f;
        Vector2Int lo = Vector2Int.FloorToInt(sample);
        Vector2 fraction = sample - (Vector2)lo;
        Vector2 flow = Vector2.zero;
        float totalWeight = 0f;
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
        {
            Vector2Int pixel = lo + new Vector2Int(x, y);
            if (!uvBounds.Contains(pixel) || !uvIsland[pixel.y * uvResolution + pixel.x]) continue;
            int index = (pixel.y - uvDataBounds.y) * uvDataBounds.width + pixel.x - uvDataBounds.x;
            float weight = (x == 0 ? 1f - fraction.x : fraction.x)
                * (y == 0 ? 1f - fraction.y : fraction.y);
            float angle = uvAngles[index] * (Mathf.PI * 2f / 256f);
            flow += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (uvSpeeds[index] / 255f * weight);
            totalWeight += weight;
        }
        if (totalWeight == 0f || flow.sqrMagnitude < 1e-12f) return Vector2.zero;
        if (transverse) flow = new Vector2(-flow.y, flow.x);
        // Angles are authored in map meters; integration distances are water-data pixels.
        Vector2 size = levelAsset.MapSizeMeters;
        Vector2 pixelFlow = new Vector2(flow.x / size.x, flow.y / size.y);
        return pixelFlow / pixelFlow.magnitude * sign;
    }

    private List<Vector2> TraceUvLine(Vector2 seed, Vector2 initialDirection, bool transverse,
        float sign, float maxLength, bool startAlongNormal = false)
    {
        var points = new List<Vector2> { seed };
        Vector2 p = seed;
        bool enteredWater = UvContainsWater(p);
        bool enteredBounds = uvBounds.Contains(Vector2Int.FloorToInt(p));
        for (float travelled = 0f; travelled < maxLength;)
        {
            Vector2 direction = enteredWater ? SampleUvDirection(p, transverse, sign) : initialDirection;
            if (direction == Vector2.zero) break;
            if (startAlongNormal && travelled == 0f) direction = initialDirection;
            float distance = Mathf.Min(uvStep, maxLength - travelled);
            Vector2 next = p + direction * distance;
            travelled += distance;
            bool inBounds = uvBounds.Contains(Vector2Int.FloorToInt(next));
            bool inWater = UvContainsWater(next);
            // Before entering bounds, a seed is allowed to approach from outside.
            if ((enteredBounds && !inBounds) || (enteredWater && !inWater)) break;
            points.Add(next);
            enteredBounds |= inBounds;
            enteredWater |= inWater;
            p = next;
        }
        return points;
    }

    private Vector2 UvSectionNormal()
    {
        return UvNormal(uvSectionB - uvSectionA);
    }

    private Vector2 UvNormal(Vector2 delta)
    {
        Vector2 size = levelAsset.MapSizeMeters;
        Vector2 mapDelta = new Vector2(delta.x * size.x, delta.y * size.y);
        Vector2 normal = new Vector2(-mapDelta.y, mapDelta.x);
        return new Vector2(normal.x / size.x, normal.y / size.y).normalized;
    }

    private static float UvLineLength(IReadOnlyList<Vector2> line)
    {
        float length = 0f;
        for (int i = 1; i < line.Count; i++) length += Vector2.Distance(line[i - 1], line[i]);
        return length;
    }

    private static Vector2 UvPointAtDistance(IReadOnlyList<Vector2> line, float distance,
        out Vector2 tangent)
    {
        for (int i = 1; i < line.Count; i++)
        {
            Vector2 delta = line[i] - line[i - 1];
            float length = delta.magnitude;
            if (length == 0f) continue;
            if (distance <= length)
            {
                tangent = delta / length;
                return line[i - 1] + tangent * distance;
            }
            distance -= length;
        }
        tangent = line.Count > 1 ? (line[line.Count - 1] - line[line.Count - 2]).normalized : Vector2.zero;
        return line[line.Count - 1];
    }

    private void UpdateUvLines()
    {
        uvVLines.Clear();
        uvULines.Clear();
        uvGridRows.Clear();
        uvRowDistances.Clear();
        if (!SelectUvIsland()) return;
        float lengthLimit = uvBounds.width + uvBounds.height;
        Vector2 normal = UvSectionNormal();
        Vector2 centre = (uvSectionA + uvSectionB) * 0.5f;
        var sections = new List<Vector2[]>();
        float travelled = 0f;
        bool wetStarted = false;
        bool oppositeEntry = false;
        while (travelled < 3f * lengthLimit)
        {
            float advance = Mathf.Min(uvUSpacing, 3f * lengthLimit - travelled);
            // Start each segment along the preceding section's normal, then sample flow at every step.
            List<Vector2> segment = TraceUvLine(centre, normal, false, 1f, advance, true);
            for (int i = 0; i < segment.Count; i++)
            {
                if (wetStarted || !UvContainsWater(segment[i])) continue;
                wetStarted = true;
                oppositeEntry = Vector2.Dot(SampleUvDirection(segment[i], false, 1f), normal) < 0f;
            }
            float segmentLength = UvLineLength(segment);
            travelled += segmentLength;
            if (segmentLength + 0.001f < advance || segment.Count < 2) break;
            centre = segment[segment.Count - 1];
            // Do not emit a shorter final interval just because the overall budget is exhausted.
            if (advance + 0.001f < uvUSpacing) break;
            if (!UvContainsWater(centre)) continue;
            List<Vector2> cross = TraceUvCrossLine(centre, lengthLimit);
            if (cross.Count < 2) break;
            sections.Add(cross.ToArray());
            uvRowDistances.Add(travelled);
            Vector2 forward = segment[segment.Count - 1] - segment[segment.Count - 2];
            centre = UvPointAtDistance(cross, UvLineLength(cross) * 0.5f, out Vector2 tangent);
            if (!UvContainsWater(centre)) break;
            normal = UvNormal(tangent);
            if (Vector2.Dot(normal, forward) < 0f) normal = -normal;
        }
        BuildUvGrid(sections);
        int uvPixelCount = BakeSelectedWaterUv();
        uvStatus = $"Generated {uvVLines.Count} V lines and {uvULines.Count} U cross-sections."
            + $" Baked {uvPixelCount} UV pixels; click Save to apply."
            + (!wetStarted ? " The center trace did not reach the selected water." : "")
            + (oppositeEntry ? " The entry flow opposes the green arrow. Draw A to B in reverse to point downstream." : "");
        SceneView.RepaintAll();
        Repaint();
    }

    private void BuildUvGrid(IReadOnlyList<Vector2[]> sections)
    {
        // A signed index denotes distance from the section midpoint: negative on one side, positive on the other.
        // End a V line when its index disappears, instead of bridging across missing rows in a narrowing river.
        var vCoordinates = new HashSet<float>();
        int rowIndex = 0;
        float metersPerPixel = (levelAsset.MapSizeMeters.x + levelAsset.MapSizeMeters.y) * 0.5f / uvResolution;
        foreach (Vector2[] section in sections)
        {
            float length = UvLineLength(section);
            float midpoint = length * 0.5f;
            int sideCount = Mathf.FloorToInt(midpoint / uvVSpacing);
            var gridRow = new List<UvGridVertex>();
            // Positive U reserves exactly (0,0) for uncomputed pixels, including on the middle V line.
            float u = (uvRowDistances[rowIndex++] + 1f) * metersPerPixel;
            gridRow.Add(new UvGridVertex(section[0], new Vector2(u, -midpoint * metersPerPixel)));
            for (int index = -sideCount; index <= sideCount; index++)
            {
                Vector2 point = UvPointAtDistance(section, midpoint + index * uvVSpacing, out _);
                if (gridRow[gridRow.Count - 1].position != point)
                    gridRow.Add(new UvGridVertex(point, new Vector2(u, index * uvVSpacing * metersPerPixel)));
                vCoordinates.Add(index * uvVSpacing * metersPerPixel);
            }
            if (gridRow[gridRow.Count - 1].position != section[section.Length - 1])
                gridRow.Add(new UvGridVertex(section[section.Length - 1], new Vector2(u, midpoint * metersPerPixel)));
            uvGridRows.Add(gridRow.ToArray());
            var positions = new Vector2[gridRow.Count];
            for (int i = 0; i < positions.Length; i++) positions[i] = gridRow[i].position;
            uvULines.Add(positions);
        }
        SmoothUvColumns(vCoordinates);
    }

    private sealed class UvSplineSegment
    {
        public UvGridVertex start, end;
        public Vector2[] points;
        public float[] distances;
        public float Length => distances[distances.Length - 1];

        public Vector2 PointAtDistance(float distance)
        {
            int next = System.Array.BinarySearch(distances, distance);
            if (next >= 0) return points[next];
            next = ~next;
            if (next == points.Length) return points[points.Length - 1];
            float t = (distance - distances[next - 1]) / (distances[next] - distances[next - 1]);
            return Vector2.Lerp(points[next - 1], points[next], t);
        }
    }

    private static Vector2 CatmullRomPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        // Centripetal knots: alpha=0.5. Controls follow the longitudinal V line.
        float k1 = Mathf.Sqrt(Vector2.Distance(p0, p1));
        float k2 = k1 + Mathf.Sqrt(Vector2.Distance(p1, p2));
        float k3 = k2 + Mathf.Sqrt(Vector2.Distance(p2, p3));
        float k = Mathf.Lerp(k1, k2, t);
        Vector2 a = Vector2.LerpUnclamped(p0, p1, k / k1);
        Vector2 b = Vector2.LerpUnclamped(p1, p2, (k - k1) / (k2 - k1));
        Vector2 c = Vector2.LerpUnclamped(p2, p3, (k - k2) / (k3 - k2));
        Vector2 d = Vector2.LerpUnclamped(a, b, k / k2);
        Vector2 e = Vector2.LerpUnclamped(b, c, (k - k1) / (k3 - k1));
        return Vector2.LerpUnclamped(d, e, (k - k1) / (k2 - k1));
    }

    private UvSplineSegment MakeUvSplineSegment(UvGridVertex start, UvGridVertex end, Vector2 p0, Vector2 p3)
    {
        Vector2 p1 = start.position, p2 = end.position;
        float controlLength = Vector2.Distance(p0, p1) + Vector2.Distance(p1, p2) + Vector2.Distance(p2, p3);
        int count = Mathf.Max(8, Mathf.CeilToInt(controlLength / (uvCurveSpacing * 0.25f)));
        var segment = new UvSplineSegment
        {
            start = start, end = end,
            points = new Vector2[count + 1], distances = new float[count + 1]
        };
        segment.points[0] = p1;
        for (int i = 1; i <= count; i++)
        {
            segment.points[i] = i == count ? p2 : CatmullRomPoint(p0, p1, p2, p3, i / (float)count);
            segment.distances[i] = segment.distances[i - 1] + Vector2.Distance(segment.points[i - 1], segment.points[i]);
        }
        return segment;
    }

    private void SmoothUvColumns(HashSet<float> vCoordinates)
    {
        if (uvGridRows.Count < 2) return;
        var lookup = new List<Dictionary<float, UvGridVertex>>();
        foreach (UvGridVertex[] row in uvGridRows)
        {
            var vertices = new Dictionary<float, UvGridVertex>();
            foreach (UvGridVertex vertex in row) vertices.Add(vertex.uv.y, vertex);
            lookup.Add(vertices);
        }
        var expanded = new List<UvGridVertex[]> { uvGridRows[0] };
        for (int row = 0; row + 1 < uvGridRows.Count; row++)
        {
            UvGridVertex[] a = uvGridRows[row], b = uvGridRows[row + 1];
            var curves = new Dictionary<float, UvSplineSegment>();
            int count = 1;
            foreach (UvGridVertex start in a)
            {
                float v = start.uv.y;
                if (!lookup[row + 1].TryGetValue(v, out UvGridVertex end)) continue;
                Vector2 before = row > 0 && lookup[row - 1].TryGetValue(v, out UvGridVertex previous)
                    ? previous.position : 2f * start.position - end.position;
                Vector2 after = row + 2 < lookup.Count && lookup[row + 2].TryGetValue(v, out UvGridVertex next)
                    ? next.position : 2f * end.position - start.position;
                UvSplineSegment segment = MakeUvSplineSegment(start, end, before, after);
                curves.Add(v, segment);
                count = Mathf.Max(count, Mathf.CeilToInt(segment.Length / uvCurveSpacing));
            }
            count = Mathf.Max(count, Mathf.CeilToInt(Mathf.Max(Vector2.Distance(a[0].position, b[0].position),
                Vector2.Distance(a[a.Length - 1].position, b[b.Length - 1].position)) / uvCurveSpacing));
            for (int i = 1; i < count; i++)
            {
                float t = i / (float)count;
                UvGridVertex Interpolate(UvGridVertex left, UvGridVertex right)
                {
                    Vector2 uv = Vector2.Lerp(left.uv, right.uv, t);
                    Vector2 position = left.uv.y == right.uv.y && curves.TryGetValue(left.uv.y, out UvSplineSegment curve)
                        ? curve.PointAtDistance(curve.Length * t) : Vector2.Lerp(left.position, right.position, t);
                    return new UvGridVertex(position, uv);
                }
                UvGridVertex leftEdge = Interpolate(a[0], b[0]);
                UvGridVertex rightEdge = Interpolate(a[a.Length - 1], b[b.Length - 1]);
                var vertices = new List<UvGridVertex> { leftEdge };
                // One shared subdivision count per U interval keeps adjacent V curves connected.
                foreach (UvGridVertex start in a)
                    if (start.uv.y > leftEdge.uv.y && start.uv.y < rightEdge.uv.y
                        && curves.TryGetValue(start.uv.y, out UvSplineSegment curve))
                        vertices.Add(new UvGridVertex(curve.PointAtDistance(curve.Length * t),
                            Vector2.Lerp(curve.start.uv, curve.end.uv, t)));
                vertices.Add(rightEdge);
                expanded.Add(vertices.ToArray());
            }
            expanded.Add(b); // Keep every original U cross-section and its intersections unchanged.
        }
        uvGridRows.Clear();
        uvGridRows.AddRange(expanded);
        uvVLines.Clear();
        var active = new Dictionary<float, List<Vector2>>();
        foreach (UvGridVertex[] row in uvGridRows)
        {
            var next = new Dictionary<float, List<Vector2>>();
            foreach (UvGridVertex vertex in row)
            {
                float v = vertex.uv.y;
                if (!vCoordinates.Contains(v)) continue;
                if (!active.TryGetValue(v, out List<Vector2> line)) line = new List<Vector2>();
                line.Add(vertex.position);
                next.Add(v, line);
            }
            foreach (var pair in active)
                if (!next.ContainsKey(pair.Key) && pair.Value.Count > 1) uvVLines.Add(pair.Value.ToArray());
            active = next;
        }
        foreach (List<Vector2> line in active.Values)
            if (line.Count > 1) uvVLines.Add(line.ToArray());
    }

    private List<Vector2> TraceUvCrossLine(Vector2 seed, float maxLength)
    {
        Vector2 direction = SampleUvDirection(seed, true, 1f);
        if (direction == Vector2.zero) return new List<Vector2> { seed };
        // Split the total budget equally between the two perpendicular directions.
        List<Vector2> left = TraceUvLine(seed, direction, true, 1f, maxLength * 0.5f);
        List<Vector2> right = TraceUvLine(seed, -direction, true, -1f, maxLength * 0.5f);
        left.Reverse();
        for (int i = 1; i < right.Count; i++) left.Add(right[i]);
        return left;
    }

    private void EnsureWorkingWaterUv()
    {
        int resolution = levelAsset.StaticWaterResolution;
        if (workingWaterUv == null)
            workingWaterUv = HeightMapStaticWaterUvBaker.ReadFullMap(levelAsset);
        else if (workingWaterUvResolution != resolution)
        {
            var resized = new Vector2[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
                resized[y * resolution + x] = workingWaterUv[
                    Mathf.Min(workingWaterUvResolution - 1, (int)((y + 0.5f) * workingWaterUvResolution / resolution))
                    * workingWaterUvResolution
                    + Mathf.Min(workingWaterUvResolution - 1, (int)((x + 0.5f) * workingWaterUvResolution / resolution))];
            workingWaterUv = resized;
        }
        workingWaterUvResolution = resolution;
    }

    private int BakeSelectedWaterUv()
    {
        EnsureWorkingWaterUv();
        for (int i = 0; i < uvIsland.Length; i++)
            if (uvIsland[i]) workingWaterUv[i] = Vector2.zero;
        if (uvGridRows.Count < 2) return 0;
        var covered = new bool[uvIsland.Length];
        var gradients = new Vector4[uvIsland.Length];
        for (int row = 1; row < uvGridRows.Count; row++)
        {
            UvGridVertex[] a = uvGridRows[row - 1], b = uvGridRows[row];
            int i = 0, j = 0;
            // Zipper the two ordered rows, including fractional-V shore endpoints.
            while (i + 1 < a.Length || j + 1 < b.Length)
            {
                if (j + 1 == b.Length || (i + 1 < a.Length && a[i + 1].uv.y <= b[j + 1].uv.y))
                {
                    RasterizeUvTriangle(a[i], b[j], a[i + 1], covered, gradients);
                    i++;
                }
                else
                {
                    RasterizeUvTriangle(a[i], b[j], b[j + 1], covered, gradients);
                    j++;
                }
            }
        }
        // Extend boundary cells' affine UV fields to all remaining shore/end pixels of this island.
        // Restrict propagation to selected water, so unrelated water and dry pixels stay untouched.
        var queue = new Queue<int>();
        for (int i = 0; i < covered.Length; i++) if (covered[i]) queue.Enqueue(i);
        int count = queue.Count;
        void Extend(int from, int x, int y, Vector2 delta)
        {
            if (x < 0 || y < 0 || x >= uvResolution || y >= uvResolution) return;
            int to = y * uvResolution + x;
            if (!uvIsland[to] || covered[to]) return;
            Vector4 g = gradients[from];
            workingWaterUv[to] = workingWaterUv[from]
                + new Vector2(g.x, g.y) * delta.x + new Vector2(g.z, g.w) * delta.y;
            gradients[to] = g;
            covered[to] = true;
            queue.Enqueue(to);
            count++;
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % uvResolution, y = i / uvResolution;
            Extend(i, x - 1, y, Vector2.left); Extend(i, x + 1, y, Vector2.right);
            Extend(i, x, y - 1, Vector2.down); Extend(i, x, y + 1, Vector2.up);
        }
        for (int i = 0; i < covered.Length; i++)
            if (covered[i] && workingWaterUv[i] == Vector2.zero)
                workingWaterUv[i] = new Vector2(0.001f, 0f);
        return count;
    }

    private void RasterizeUvTriangle(UvGridVertex a, UvGridVertex b, UvGridVertex c,
        bool[] covered, Vector4[] gradients)
    {
        float Cross(Vector2 x, Vector2 y) => x.x * y.y - x.y * y.x;
        Vector2 ab = b.position - a.position, ac = c.position - a.position;
        float determinant = Cross(ab, ac);
        if (Mathf.Abs(determinant) < 0.000001f) return;
        Vector2 uvAb = b.uv - a.uv, uvAc = c.uv - a.uv;
        Vector2 dx = (uvAb * ac.y - uvAc * ab.y) / determinant;
        Vector2 dy = (uvAc * ab.x - uvAb * ac.x) / determinant;
        var gradient = new Vector4(dx.x, dx.y, dy.x, dy.y);
        int minX = Mathf.Max(uvBounds.xMin, Mathf.FloorToInt(Mathf.Min(a.position.x, Mathf.Min(b.position.x, c.position.x))));
        int minY = Mathf.Max(uvBounds.yMin, Mathf.FloorToInt(Mathf.Min(a.position.y, Mathf.Min(b.position.y, c.position.y))));
        int maxX = Mathf.Min(uvBounds.xMax - 1, Mathf.FloorToInt(Mathf.Max(a.position.x, Mathf.Max(b.position.x, c.position.x))));
        int maxY = Mathf.Min(uvBounds.yMax - 1, Mathf.FloorToInt(Mathf.Max(a.position.y, Mathf.Max(b.position.y, c.position.y))));
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            int index = y * uvResolution + x;
            if (!uvIsland[index]) continue;
            Vector2 delta = new Vector2(x + 0.5f, y + 0.5f) - a.position;
            float wb = Cross(delta, ac) / determinant, wc = Cross(ab, delta) / determinant;
            if (wb < -0.00001f || wc < -0.00001f || wb + wc > 1.00001f) continue;
            workingWaterUv[index] = a.uv + wb * uvAb + wc * uvAc;
            gradients[index] = gradient;
            covered[index] = true;
        }
    }

    private void DrawUvPreview()
    {
        Color previousColor = Handles.color;
        var previousZTest = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        void Draw(Vector2[] pixels, Color color, float thickness)
        {
            if (pixels.Length < 2) return;
            var points = new Vector3[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) points[i] = UvPixelToWorld(pixels[i]);
            Handles.color = color;
            Handles.DrawAAPolyLine(thickness, points);
        }
        if (uvIsland != null)
        {
            Vector2 min = uvBounds.min, max = uvBounds.max;
            Draw(new[] { min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y), min },
                new Color(0.8313726f, 0.7215686f, 0.3294118f), 2f); // #D4B854
        }
        foreach (Vector2[] line in uvVLines) Draw(line, Color.cyan, 1.5f);
        foreach (Vector2[] line in uvULines) Draw(line, new Color(1f, 0.5f, 0.2f), 1.5f);
        if (uvHasSection || uvDragging)
        {
            Draw(new[] { uvSectionA, uvSectionB }, Color.green, 3f);
            Handles.color = Color.green;
            Vector3 a = UvPixelToWorld(uvSectionA), b = UvPixelToWorld(uvSectionB);
            Vector3 centre = (a + b) * 0.5f;
            Vector3 normal = new Vector3(-(b - a).y, (b - a).x, 0f).normalized;
            float length = HandleUtility.GetHandleSize(centre) * 0.6f;
            Vector3 tip = centre + normal * length;
            Vector3 side = (b - a).normalized * length * 0.2f;
            Handles.DrawAAPolyLine(3f, centre, tip);
            Handles.DrawAAPolyLine(3f, tip - normal * length * 0.3f + side, tip,
                tip - normal * length * 0.3f - side);
        }
        Handles.color = previousColor;
        Handles.zTest = previousZTest;
    }

    private void DrawCommonBrushSettings()
    {
        brushRadiusMeters = Mathf.Max(MinimumBrushRadiusMeters,
            EditorGUILayout.FloatField("Radius (m)", brushRadiusMeters));
        brushStrength = EditorGUILayout.Slider(
            "Strength",
            brushStrength,
            0.01f,
            1f);
        brushHardness = EditorGUILayout.Slider(
            "Hardness",
            brushHardness,
            0f,
            1f);
    }

    private void DrawWaterStatistics()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Current Water", EditorStyles.boldLabel);
        RectInt bounds = levelAsset.StaticWaterPixelBounds;
        EditorGUILayout.LabelField("Water Data Size", $"{bounds.width} x {bounds.height}");
        EditorGUILayout.LabelField("Wet authoring pixels", wetPixelCount.ToString());
        if (wetPixelCount <= 0)
        {
            EditorGUILayout.LabelField("Depth range", "Dry map");
            return;
        }

        EditorGUILayout.LabelField(
            "Depth range",
            $"{minimumWetDepth:0.00} m - {maximumWetDepth:0.00} m");
        EditorGUILayout.LabelField(
            "Average depth",
            $"{averageWetDepth:0.00} m");
    }

    private void DrawBulkActions()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Whole Map", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Fill At Target Depth"))
            {
                if (EditorUtility.DisplayDialog(
                        "Fill Static Water",
                        $"Fill the complete map with {targetDepthMeters:0.00} m " +
                        "static water?",
                        "Fill",
                        "Cancel"))
                {
                    FillWater(targetDepthMeters, "Fill Static Water");
                }
            }

            if (GUILayout.Button("Clear All Water"))
            {
                if (EditorUtility.DisplayDialog(
                        "Clear Static Water",
                        "Erase all authored static water from this map?",
                        "Clear",
                        "Cancel"))
                {
                    FillWater(0f, "Clear Static Water");
                }
            }
        }
    }

    private void HandleSceneGui(SceneView sceneView)
    {
        if (levelAsset == null
            || mapController == null
            || !mapController.HasGeneratedMap)
        {
            return;
        }

        EnsurePreviewResources();
        if (previewRenderer != null)
            previewRenderer.enabled = (paintInScene || brushPage == 2) && !EditorApplication.isPlaying;
        if (EditorApplication.isPlaying)
            return;

        if (brushPage == 2)
        {
            HandleUvSceneGui(sceneView);
            return;
        }
        if (!paintInScene)
            return;

        Event current = Event.current;
        if (brushPage == 1 && current.type == EventType.Repaint)
            DrawFlowDirections();
        int controlId = GUIUtility.GetControlID(
            "HeightMapStaticWaterPainter".GetHashCode(),
            FocusType.Passive);
        if (!TryGetMapPosition(current.mousePosition, out Vector2 mapPosition))
        {
            if (current.type == EventType.MouseUp
                && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                EndStroke();
                current.Use();
            }

            return;
        }

        bool erase = current.shift && (brushPage == 0 || flowPaintMode == StaticWaterFlowPaintMode.Speed);
        DrawBrushOutline(mapPosition, erase);
        if (current.type == EventType.Layout)
            HandleUtility.AddDefaultControl(controlId);

        if (current.alt || current.button != 0)
            return;

        if (current.type == EventType.MouseDown)
        {
            GUIUtility.hotControl = controlId;
            BeginStroke(mapPosition, erase);
            current.Use();
        }
        else if (current.type == EventType.MouseDrag
                 && GUIUtility.hotControl == controlId)
        {
            PaintStrokeTo(mapPosition);
            SchedulePreviewRefresh();
            current.Use();
            sceneView.Repaint();
        }
        else if (current.type == EventType.MouseUp
                 && GUIUtility.hotControl == controlId)
        {
            PaintStrokeTo(mapPosition);
            GUIUtility.hotControl = 0;
            EndStroke();
            current.Use();
            sceneView.Repaint();
        }

        if (previewDirty
            && EditorApplication.timeSinceStartup >= nextPreviewRefreshTime)
        {
            RefreshPreview(false);
        }
    }

    private bool TryGetMapPosition(
        Vector2 sceneGuiPosition,
        out Vector2 mapPosition)
    {
        mapPosition = default;
        Bounds bounds = mapController.WorldBounds;
        Ray ray = HandleUtility.GUIPointToWorldRay(sceneGuiPosition);
        var mapPlane = new Plane(Vector3.forward, bounds.center);
        if (!mapPlane.Raycast(ray, out float distance))
            return false;

        Vector3 world = ray.GetPoint(distance);
        return TrySampleTilePosition(
            (Vector2)world,
            out mapPosition,
            out _);
    }

    private void DrawFlowDirections()
    {
        Vector2 size = levelAsset.MapSizeMeters;
        int resolution = levelAsset.StaticWaterResolution;
        float spacing = Mathf.Max(directionPointSpacingMeters,
            Mathf.Max(size.x, size.y) / resolution);
        byte[] depths = levelAsset.GetOrCreateStaticWaterDepthMap();
        byte[] angles = levelAsset.GetOrCreateStaticWaterFlowAngleMap();
        RectInt bounds = levelAsset.StaticWaterPixelBounds;
        var lines = new System.Collections.Generic.List<Vector3>();
        float startX = (Mathf.Max(0, Mathf.CeilToInt(bounds.xMin / (float)resolution * size.x / spacing - 0.5f)) + 0.5f) * spacing;
        float startY = (Mathf.Max(0, Mathf.CeilToInt(bounds.yMin / (float)resolution * size.y / spacing - 0.5f)) + 0.5f) * spacing;
        for (float y = startY; y < bounds.yMax / (float)resolution * size.y; y += spacing)
        for (float x = startX; x < bounds.xMax / (float)resolution * size.x; x += spacing)
        {
            int ix = Mathf.Min(resolution - 1, (int)(x / size.x * resolution));
            int iy = Mathf.Min(resolution - 1, (int)(y / size.y * resolution));
            int index = (iy - bounds.y) * bounds.width + ix - bounds.x;
            if (depths[index] == 0) continue;
            float angle = angles[index] * (Mathf.PI * 2f / 256f);
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 side = new Vector2(-direction.y, direction.x);
            Vector2 centre = new Vector2(x, y);
            Vector2 tip = centre + direction * spacing * 0.3f;
            lines.Add(TilePositionToWorld(centre - direction * spacing * 0.3f));
            lines.Add(TilePositionToWorld(tip));
            lines.Add(TilePositionToWorld(tip));
            lines.Add(TilePositionToWorld(tip - direction * spacing * 0.14f + side * spacing * 0.1f));
            lines.Add(TilePositionToWorld(tip));
            lines.Add(TilePositionToWorld(tip - direction * spacing * 0.14f - side * spacing * 0.1f));
        }
        Color previousColor = Handles.color;
        var previousZTest = Handles.zTest;
        Handles.color = Color.white;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.DrawLines(lines.ToArray());
        Handles.color = previousColor;
        Handles.zTest = previousZTest;
    }

    private void DrawBrushOutline(Vector2 centreMapPosition, bool erase)
    {
        var points = new Vector3[BrushOutlineSegments + 1];
        for (int index = 0; index <= BrushOutlineSegments; index++)
        {
            float angle = index / (float)BrushOutlineSegments
                          * Mathf.PI * 2f;
            Vector2 mapPoint = centreMapPosition + new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) * brushRadiusMeters;
            points[index] = TilePositionToWorld(mapPoint);
            points[index].z -= 0.08f;
        }

        Color outlineColor = erase
            ? new Color(1f, 0.28f, 0.2f, 0.98f)
            : levelAsset.StaticWaterEditorTint;
        outlineColor.a = 0.98f;
        Handles.color = outlineColor;
        Handles.DrawAAPolyLine(3f, points);

        string label = brushPage == 1
            ? (erase ? "Stop flow"
                : flowPaintMode == StaticWaterFlowPaintMode.Speed ? $"Speed {flowSpeed:0.00}"
                : flowPaintMode == StaticWaterFlowPaintMode.StrokeDirection ? "Stroke direction"
                : $"Direction {flowAngleDegrees:0.#} degrees")
            : erase
            ? "Erase water"
            : paintMode == StaticWaterDepthPaintMode.Set
                ? $"Set {targetDepthMeters:0.00} m"
                : paintMode.ToString();
        Vector3 labelPosition = TilePositionToWorld(
            centreMapPosition + Vector2.up * brushRadiusMeters);
        labelPosition.z -= 0.09f;
        Handles.Label(labelPosition, label, EditorStyles.miniBoldLabel);
    }

    private void BeginStroke(Vector2 mapPosition, bool erase)
    {
        strokeBrushPage = brushPage;
        strokeFlowPaintMode = flowPaintMode;
        hasStrokeDirection = false;
        strokeFlowAngleDegrees = flowAngleDegrees;
        strokeErase = erase;
        strokeActive = true;
        strokeChanged = false;
        hasLastStrokePosition = false;
        strokePaintMode = erase
            ? StaticWaterDepthPaintMode.Erase
            : paintMode;

        Undo.IncrementCurrentGroup();
        strokeUndoGroup = Undo.GetCurrentGroup();
        string operationName = strokeBrushPage == 1
            ? "Paint Static Water Flow"
            : strokePaintMode
                               == StaticWaterDepthPaintMode.Erase
            ? "Erase Static Water"
            : $"{strokePaintMode} Static Water Depth";
        Undo.SetCurrentGroupName(operationName);
        Undo.RegisterCompleteObjectUndo(levelAsset, operationName);
        PaintStrokeTo(mapPosition);
    }

    private void PaintStrokeTo(Vector2 mapPosition)
    {
        if (!strokeActive)
            return;

        if (!hasLastStrokePosition)
        {
            if (strokeBrushPage != 1 || strokeFlowPaintMode != StaticWaterFlowPaintMode.StrokeDirection)
                PaintDab(mapPosition);
            lastStrokeMapPosition = mapPosition;
            hasLastStrokePosition = true;
            return;
        }

        Vector2 delta = mapPosition - lastStrokeMapPosition;
        float distance = delta.magnitude;
        if (distance <= 0f)
            return;

        int firstStep = 1;
        if (strokeBrushPage == 1 && strokeFlowPaintMode == StaticWaterFlowPaintMode.StrokeDirection)
        {
            Vector2 pixelSize = levelAsset.MapSizeMeters / levelAsset.StaticWaterResolution;
            float minimumDistance = Mathf.Min(pixelSize.x, pixelSize.y) * 0.1f;
            // Keep the previous sample until movement is large enough to give a stable direction.
            if (distance < minimumDistance)
                return;
            strokeFlowAngleDegrees = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, 360f);
            if (!hasStrokeDirection)
                firstStep = 0;
            hasStrokeDirection = true;
        }
        float spacing = Mathf.Max(0.05f, brushRadiusMeters * 0.25f);
        int stepCount = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));
        for (int step = firstStep; step <= stepCount; step++)
        {
            PaintDab(Vector2.Lerp(
                lastStrokeMapPosition,
                mapPosition,
                step / (float)stepCount));
        }

        lastStrokeMapPosition = mapPosition;
    }

    private void PaintDab(Vector2 mapPosition)
    {
        bool changed = strokeBrushPage == 1
            ? levelAsset.PaintStaticWaterFlow(
                mapPosition, brushRadiusMeters, strokeFlowPaintMode,
                strokeFlowPaintMode == StaticWaterFlowPaintMode.StrokeDirection ? strokeFlowAngleDegrees : flowAngleDegrees,
                strokeErase ? 0f : flowSpeed, brushStrength, brushHardness)
            : levelAsset.PaintStaticWaterDepth(
                mapPosition,
                brushRadiusMeters,
                strokePaintMode,
                targetDepthMeters,
                depthStepMeters,
                brushStrength,
                brushHardness);
        if (!changed)
        {
            return;
        }

        strokeChanged = true;
        previewDirty = true;
        EditorUtility.SetDirty(levelAsset);
    }

    private void EndStroke()
    {
        if (!strokeActive)
            return;

        strokeActive = false;
        hasLastStrokePosition = false;
        if (strokeUndoGroup >= 0)
            Undo.CollapseUndoOperations(strokeUndoGroup);
        strokeUndoGroup = -1;

        if (strokeChanged)
        {
            RefreshPreview(true);
        }

        strokeChanged = false;
        Repaint();
    }

    private void FillWater(float depthMeters, string operationName)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(operationName);
        Undo.RegisterCompleteObjectUndo(levelAsset, operationName);
        if (levelAsset.FillStaticWaterDepth(depthMeters))
        {
            EditorUtility.SetDirty(levelAsset);
            RefreshPreview(true);
        }

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
    }

    private void SchedulePreviewRefresh()
    {
        previewDirty = true;
        nextPreviewRefreshTime = EditorApplication.timeSinceStartup + 0.05d;
    }

    private void RefreshPreview(bool force)
    {
        if (levelAsset == null)
            return;
        if (!force
            && EditorApplication.timeSinceStartup < nextPreviewRefreshTime)
        {
            return;
        }

        levelAsset.EnsureStaticWaterAuthoringData();
        if (uvIsland != null && uvResolution != levelAsset.StaticWaterResolution)
            ClearUvSelection();
        levelAsset.CompactStaticWaterData();
        EnsurePreviewResources();
        byte[] depthMap = levelAsset.GetOrCreateStaticWaterDepthMap();
        RectInt bounds = levelAsset.StaticWaterPixelBounds;
        if (previewDepthTexture == null
            || previewDepthTexture.width != bounds.width
            || previewDepthTexture.height != bounds.height)
        {
            if (previewDepthTexture != null)
                DestroyImmediate(previewDepthTexture);
            previewDepthTexture = new Texture2D(
                bounds.width,
                bounds.height,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Static Water Depth Preview",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        byte[] flowAngles = levelAsset.GetOrCreateStaticWaterFlowAngleMap();
        byte[] flowSpeeds = levelAsset.GetOrCreateStaticWaterFlowSpeedMap();
        var pixels = new Color32[depthMap.Length];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(depthMap[i], flowAngles[i], flowSpeeds[i], 255);
        previewDepthTexture.SetPixels32(pixels);
        previewDepthTexture.Apply(false, false);
        UpdateStatistics(depthMap);
        UpdatePreviewMaterial();
        lastObservedAuthoringHash =
            levelAsset.CalculateStaticWaterAuthoringHash();
        previewDirty = false;
        nextPreviewRefreshTime = EditorApplication.timeSinceStartup;
        SceneView.RepaintAll();
        Repaint();
    }

    private void UpdateStatistics(byte[] depthMap)
    {
        wetPixelCount = 0;
        minimumWetDepth = float.PositiveInfinity;
        maximumWetDepth = 0f;
        double depthSum = 0d;
        float maximumDepth = levelAsset.MaximumStaticWaterDepthMeters;
        foreach (byte encodedDepth in depthMap)
        {
            if (encodedDepth == 0)
                continue;

            float depth = encodedDepth / (float)byte.MaxValue * maximumDepth;
            wetPixelCount++;
            minimumWetDepth = Mathf.Min(minimumWetDepth, depth);
            maximumWetDepth = Mathf.Max(maximumWetDepth, depth);
            depthSum += depth;
        }

        if (wetPixelCount <= 0)
        {
            minimumWetDepth = 0f;
            averageWetDepth = 0f;
            return;
        }

        averageWetDepth = (float)(depthSum / wetPixelCount);
    }

    private void EnsurePreviewResources()
    {
        if (mapController == null || !mapController.HasGeneratedMap)
            return;

        Shader previewShader = Shader.Find(PreviewShaderName);
        if (previewShader == null)
            return;

        if (previewMaterial == null)
        {
            previewMaterial = new Material(previewShader)
            {
                name = "Static Water Editor Preview Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (previewObject == null)
        {
            previewObject = new GameObject("Static Water Editor Preview")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var meshFilter = previewObject.AddComponent<MeshFilter>();
            previewRenderer = previewObject.AddComponent<MeshRenderer>();
            previewRenderer.sharedMaterial = previewMaterial;
            previewMesh = new Mesh
            {
                name = "Static Water Editor Preview Mesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            meshFilter.sharedMesh = previewMesh;
        }

        UpdatePreviewMesh();
        if (previewRenderer != null)
            previewRenderer.enabled = paintInScene && !EditorApplication.isPlaying;
    }

    private void UpdatePreviewMesh()
    {
        if (previewMesh == null || mapController == null)
            return;

        Vector3 bottomLeft = TilePositionToWorld(Vector2.zero);
        Vector3 bottomRight = TilePositionToWorld(
            new Vector2(levelAsset.MapSizeMeters.x, 0f));
        Vector3 topLeft = TilePositionToWorld(
            new Vector2(0f, levelAsset.MapSizeMeters.y));
        Vector3 topRight = TilePositionToWorld(
            levelAsset.MapSizeMeters);
        bottomLeft.z -= 0.04f;
        bottomRight.z -= 0.04f;
        topLeft.z -= 0.04f;
        topRight.z -= 0.04f;

        previewMesh.Clear();
        previewMesh.vertices = new[]
        {
            bottomLeft,
            bottomRight,
            topLeft,
            topRight
        };
        previewMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        previewMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        previewMesh.RecalculateBounds();
    }

    private void UpdatePreviewMaterial()
    {
        if (previewMaterial == null || levelAsset == null)
            return;

        Texture2D pattern = levelAsset.StaticWaterTexture;
        if (pattern == null)
        {
            pattern = AssetDatabase.LoadAssetAtPath<Texture2D>(
                DefaultWaterTexturePath);
        }

        previewMaterial.SetTexture("_DepthTex", previewDepthTexture);
        RectInt bounds = levelAsset.StaticWaterPixelBounds;
        float resolution = levelAsset.StaticWaterResolution;
        previewMaterial.SetVector("_DepthBounds", new Vector4(
            bounds.x / resolution, bounds.y / resolution, bounds.width / resolution, bounds.height / resolution));
        previewMaterial.SetFloat("_FlowPreview", brushPage == 1 ? 1f : 0f);
        previewMaterial.SetTexture("_MainTex", pattern != null
            ? pattern
            : Texture2D.whiteTexture);
        previewMaterial.SetColor("_Tint", levelAsset.StaticWaterEditorTint);
        float tileSize = Mathf.Max(
            0.1f,
            levelAsset.StaticWaterTileSizeMeters);
        previewMaterial.SetVector(
            "_TileScale",
            new Vector4(
                levelAsset.MapSizeMeters.x / tileSize,
                levelAsset.MapSizeMeters.y / tileSize,
                0f,
                0f));
        previewMaterial.SetFloat(
            "_PassableDepthNormalized",
            levelAsset.MaximumPassableStaticWaterDepthMeters
            / Mathf.Max(0.1f, levelAsset.MaximumStaticWaterDepthMeters));
    }

    private void DestroyPreviewResources()
    {
        if (previewObject != null)
            DestroyImmediate(previewObject);
        if (previewMesh != null)
            DestroyImmediate(previewMesh);
        if (previewMaterial != null)
            DestroyImmediate(previewMaterial);
        if (previewDepthTexture != null)
            DestroyImmediate(previewDepthTexture);

        previewObject = null;
        previewMesh = null;
        previewRenderer = null;
        previewMaterial = null;
        previewDepthTexture = null;
    }

    private void HandleUndoRedo()
    {
        if (levelAsset == null)
            return;

        levelAsset.EnsureStaticWaterAuthoringData();
        int currentHash = levelAsset.CalculateStaticWaterAuthoringHash();
        if (currentHash == lastObservedAuthoringHash)
            return;

        RefreshPreview(true);
    }

    private void SaveWater()
    {
        if (levelAsset == null)
            return;

        EndStroke();
        EnsureWorkingWaterUv();
        levelAsset.CompactStaticWaterData();
        // Copy only water authoring fields so other map edits remain intact.
        serializedLevel.Update();
        var destination = new SerializedObject(sourceLevelAsset);
        foreach (string property in WaterAuthoringProperties)
            destination.CopyFromSerializedProperty(serializedLevel.FindProperty(property));
        destination.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(sourceLevelAsset);
        HeightMapStaticWaterMaskBaker.Bake(sourceLevelAsset, false, workingWaterUv);
        RefreshPreview(true);
    }

    private void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        CancelUvInteraction();
        if (previewRenderer == null)
            return;

        previewRenderer.enabled = (paintInScene || brushPage == 2)
                                  && state == PlayModeStateChange.EnteredEditMode;
    }

    private void TryAdoptCurrentSelection()
    {
        if (Selection.activeObject is HeightMapLevelAsset selectedLevel)
        {
            SetLevelAsset(selectedLevel);
            return;
        }

        GameObject selectedObject = Selection.activeGameObject;
        MapTestSceneController selectedMap = selectedObject != null
            ? selectedObject.GetComponentInParent<MapTestSceneController>()
            : null;
        if (selectedMap != null)
        {
            SetLevelAsset(selectedMap.LevelAsset != null && selectedMap.LevelAsset.Tiles.Count == 1 ? selectedMap.LevelAsset.Tiles[0].level : null);
            return;
        }

        MapTestSceneController[] openMaps =
            Object.FindObjectsByType<MapTestSceneController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (openMaps.Length > 0)
            SetLevelAsset(openMaps[0].LevelAsset != null && openMaps[0].LevelAsset.Tiles.Count == 1 ? openMaps[0].LevelAsset.Tiles[0].level : null);
    }

    private void SetLevelAsset(HeightMapLevelAsset asset)
    {
        if (sourceLevelAsset == asset && levelAsset != null)
            return;

        if (strokeActive)
            EndStroke();
        CancelUvInteraction();
        DestroyPreviewResources();
        DestroyWorkingCopy();
        ClearUvSelection();
        workingWaterUv = null;
        workingWaterUvResolution = 0;
        sourceLevelAsset = asset;
        levelAsset = asset != null ? Instantiate(asset) : null;
        if (levelAsset != null)
        {
            levelAsset.name = asset.name + " (Water Preview)";
            levelAsset.hideFlags = HideFlags.HideAndDontSave;
            levelAsset.EnsureStaticWaterAuthoringData();
        }
        serializedLevel = levelAsset != null
            ? new SerializedObject(levelAsset)
            : null;

        ResolveSceneMap();
        lastObservedAuthoringHash = levelAsset != null
            ? levelAsset.CalculateStaticWaterAuthoringHash()
            : 0;
        RefreshPreview(true);
        Repaint();
        SceneView.RepaintAll();
    }

    private Vector3 TilePositionToWorld(Vector2 position)
    {
        mapController.TryGetTileOrigin(sourceLevelAsset, out var origin);
        return mapController.MapPositionToWorld(origin + position);
    }

    private bool TrySampleTilePosition(Vector2 world, out Vector2 local, out float height)
    {
        local = default;
        height = 0;
        if (!mapController.TrySampleWorldPosition(world, out var global, out height)
            || !mapController.TryGetTileOrigin(sourceLevelAsset, out var origin)) return false;
        local = global - origin;
        return local.x >= 0 && local.y >= 0
            && local.x <= levelAsset.MapSizeMeters.x && local.y <= levelAsset.MapSizeMeters.y;
    }

    private void ResolveSceneMap()
    {
        MapTestSceneController previousMap = mapController;
        mapController = null;
        if (levelAsset != null)
        {
            MapTestSceneController[] openMaps =
                FindObjectsByType<MapTestSceneController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (MapTestSceneController candidate in openMaps)
            {
                if (candidate != null
                    && !EditorUtility.IsPersistent(candidate)
                    && candidate.ContainsLevel(sourceLevelAsset))
                {
                    mapController = candidate;
                    break;
                }
            }
        }

        if (previousMap != mapController)
        {
            DestroyPreviewResources();
            previewDirty = true;
        }
    }

    private void DestroyWorkingCopy()
    {
        if (levelAsset == null)
            return;
        Undo.ClearUndo(levelAsset);
        DestroyImmediate(levelAsset);
        levelAsset = null;
        sourceLevelAsset = null;
    }

}

internal static class HeightMapStaticWaterMaskBaker
{
    public static bool Bake(HeightMapLevelAsset level, bool registerUndo, Vector2[] waterUv = null)
    {
        if (level == null)
        {
            Debug.LogError(
                "Static-water mask baking requires a fixed map asset.");
            return false;
        }

        if (level.EnsureStaticWaterAuthoringData())
            EditorUtility.SetDirty(level);
        level.CompactStaticWaterData();
        // Capture the old UV rectangle before assigning the new mask bounds.
        if (waterUv == null && level.BakedStaticWaterUv != null)
            waterUv = HeightMapStaticWaterUvBaker.ReadFullMap(level);

        string levelPath = AssetDatabase.GetAssetPath(level);
        if (string.IsNullOrEmpty(levelPath))
        {
            Debug.LogError(
                "Save the HeightMapLevelAsset before baking static water.");
            return false;
        }

        byte[] depthMap = level.GetOrCreateStaticWaterDepthMap();
        string directory = HeightMapAssetPaths.DataDirectory(levelPath);
        string fileName = Path.GetFileNameWithoutExtension(levelPath) + "_WaterMask.png";
        string outputPath = Path.Combine(directory, fileName).Replace('\\', '/');
        // Compaction represents a completely dry map with one zero texel.
        if (depthMap.Length == 1 && depthMap[0] == 0)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath) != null
                && !AssetDatabase.DeleteAsset(outputPath))
                throw new IOException($"Could not delete the dry map's Water Mask: {outputPath}");
            if (registerUndo)
                Undo.RecordObject(level, "Clear Static Water Mask");
            float emptySize = 1f / level.StaticWaterResolution;
            level.SetBakedStaticWaterMask(null, new Rect(0f, 0f, emptySize, emptySize));
            HeightMapStaticWaterUvBaker.Save(level, waterUv);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssetIfDirty(level);
            return true;
        }
        byte[] flowAngles = level.GetOrCreateStaticWaterFlowAngleMap();
        byte[] flowSpeeds = level.GetOrCreateStaticWaterFlowSpeedMap();
        int resolution = level.StaticWaterResolution;
        RectInt pixelBounds = level.StaticWaterPixelBounds;
        var bounds = new Rect(
            pixelBounds.x / (float)resolution,
            pixelBounds.y / (float)resolution,
            pixelBounds.width / (float)resolution,
            pixelBounds.height / (float)resolution);
        var pixels = new Color32[pixelBounds.width * pixelBounds.height];
        for (int y = 0; y < pixelBounds.height; y++)
        {
            for (int x = 0; x < pixelBounds.width; x++)
            {
                int sourceIndex = y * pixelBounds.width + x;
                byte encodedDepth = depthMap[sourceIndex];
                pixels[y * pixelBounds.width + x] = new Color32(
                    encodedDepth > 0 ? byte.MaxValue : (byte)0,
                    encodedDepth,
                    encodedDepth > 0 ? flowAngles[sourceIndex] : (byte)0,
                    encodedDepth > 0 ? flowSpeeds[sourceIndex] : (byte)0);
            }
        }

        Texture2D output = null;
        try
        {
            output = new Texture2D(
                pixelBounds.width,
                pixelBounds.height,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Baked Static Water Data",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            output.SetPixels32(pixels);
            output.Apply(false, false);

            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            File.WriteAllBytes(outputPath, output.EncodeToPNG());
            AssetDatabase.ImportAsset(
                outputPath,
                ImportAssetOptions.ForceSynchronousImport
                | ImportAssetOptions.ForceUpdate);
            ConfigureImporter(outputPath, resolution);

            Texture2D bakedMask = AssetDatabase.LoadAssetAtPath<Texture2D>(
                outputPath);
            if (bakedMask == null)
            {
                Debug.LogError(
                    $"Unity could not load the static-water mask at {outputPath}.");
                return false;
            }

            if (registerUndo)
                Undo.RecordObject(level, "Bake Static Water Mask");
            level.SetBakedStaticWaterMask(bakedMask, bounds);
            HeightMapStaticWaterUvBaker.Save(level, waterUv);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssetIfDirty(level);
            Debug.Log($"Baked static-water range and depth: {outputPath}", level);
            return true;
        }
        finally
        {
            if (output != null)
                Object.DestroyImmediate(output);
        }
    }

    private static void ConfigureImporter(string assetPath, int resolution)
    {
        TextureImporter importer =
            AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = Mathf.Clamp(resolution, 64, 8192);
        importer.SaveAndReimport();
    }
}

internal static class HeightMapStaticWaterUvBaker
{
    public static Vector2[] ReadFullMap(HeightMapLevelAsset level)
    {
        int resolution = level.StaticWaterResolution;
        var result = new Vector2[resolution * resolution];
        Texture2D texture = level.BakedStaticWaterUv;
        if (texture == null) return result;
        Color[] pixels = texture.GetPixels();
        Rect bounds = level.BakedStaticWaterBounds;
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            Vector2 uv = new Vector2(x + 0.5f, y + 0.5f) / resolution;
            if (!bounds.Contains(uv)) continue;
            int sx = Mathf.Clamp((int)((uv.x - bounds.x) / bounds.width * texture.width), 0, texture.width - 1);
            int sy = Mathf.Clamp((int)((uv.y - bounds.y) / bounds.height * texture.height), 0, texture.height - 1);
            Color value = pixels[sy * texture.width + sx];
            result[y * resolution + x] = new Vector2(value.r, value.g);
        }
        return result;
    }

    public static void Save(HeightMapLevelAsset level, Vector2[] fullMap)
    {
        string levelPath = AssetDatabase.GetAssetPath(level);
        string directory = HeightMapAssetPaths.DataDirectory(levelPath);
        string path = Path.Combine(directory, Path.GetFileNameWithoutExtension(levelPath) + "_WaterUV.asset").Replace('\\', '/');
        RectInt bounds = level.StaticWaterPixelBounds;
        int resolution = level.StaticWaterResolution;
        byte[] depths = level.GetOrCreateStaticWaterDepthMap();
        var data = new ushort[bounds.width * bounds.height * 2];
        bool anyUv = false;
        if (fullMap != null)
        {
            for (int y = 0; y < bounds.height; y++)
            for (int x = 0; x < bounds.width; x++)
            {
                int i = y * bounds.width + x;
                if (depths[i] == 0) continue;
                Vector2 value = fullMap[(y + bounds.y) * resolution + x + bounds.x];
                data[i * 2] = Mathf.FloatToHalf(value.x);
                data[i * 2 + 1] = Mathf.FloatToHalf(value.y);
                anyUv |= (data[i * 2] & 0x7fff) != 0 || (data[i * 2 + 1] & 0x7fff) != 0;
            }
        }
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (!anyUv)
        {
            level.SetBakedStaticWaterUv(null);
            if (texture != null && !AssetDatabase.DeleteAsset(path))
                throw new IOException($"Could not delete empty water UV texture: {path}");
        }
        else
        {
            if (texture == null)
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
                texture = new Texture2D(bounds.width, bounds.height, TextureFormat.RGHalf, false, true);
                AssetDatabase.CreateAsset(texture, path);
            }
            else if (!texture.Reinitialize(bounds.width, bounds.height, TextureFormat.RGHalf, false))
                throw new IOException($"Could not resize water UV texture: {path}");
            texture.name = Path.GetFileNameWithoutExtension(path);
            // Shader performs validity-aware bilinear sampling so zero texels stay invalid at borders.
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixelData(data, 0);
            texture.Apply(false, false);
            EditorUtility.SetDirty(texture);
            AssetDatabase.SaveAssetIfDirty(texture);
            level.SetBakedStaticWaterUv(texture);
        }
        EditorUtility.SetDirty(level);
        AssetDatabase.SaveAssetIfDirty(level);
    }
}

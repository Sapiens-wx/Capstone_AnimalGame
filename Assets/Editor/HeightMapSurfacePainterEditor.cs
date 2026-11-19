using System;
using System.Collections.Generic;
using System.IO;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HeightMapLevelAsset))]
public sealed class HeightMapLevelAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();
        var tile = (HeightMapLevelAsset)target;
        using var settings = tile.MapParams != null ? new SerializedObject(tile.MapParams) : null;
        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (settings != null && property.propertyPath != "m_Script" &&
                settings.FindProperty(property.propertyPath) != null)
                continue;
            using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script" ||
                (settings != null && property.propertyPath == "mapParams")))
                EditorGUILayout.PropertyField(property, true);
        }

        if (settings != null)
            serializedObject.ApplyModifiedProperties();
        else
            HeightMapSurfaceSettings.ApplyEdits(serializedObject);
        if (!((HeightMapLevelAsset)target).TryValidateSurfacePalette(out string paletteError))
            EditorGUILayout.HelpBox(paletteError, MessageType.Error);

        EditorGUILayout.Space();
        if (GUILayout.Button("Open Terrain Surface Painter"))
        {
            HeightMapSurfacePainterWindow.OpenForAsset(
                (HeightMapLevelAsset)target);
        }

        if (GUILayout.Button("Open Static Water Painter"))
        {
            HeightMapStaticWaterPainterWindow.OpenForAsset(
                (HeightMapLevelAsset)target);
        }
    }
}

public sealed class HeightMapSurfacePainterWindow : EditorWindow
{
    private const float MinimumBrushRadiusMeters = 0.25f;
    private const float DefaultBrushRadiusMeters = 10f;
    private const int BrushOutlineSegments = 64;

    private HeightMapLevelAsset sourceLevelAsset;
    // Painting, shared settings and Undo affect temporary copies until Save.
    private HeightMapLevelAsset levelAsset;
    private TiledLevelAsset workingMap;
    private readonly Dictionary<HeightMapLevelAsset, HeightMapLevelAsset> workingTiles = new();
    private MapTestSceneController mapController;
    private Vector2 scrollPosition;
    private float brushRadiusMeters = DefaultBrushRadiusMeters;
    private TerrainKind selectedTerrain = TerrainKind.Grass;
    private bool paintInScene = true;
    private bool strokeActive;
    private TerrainSurfaceDefinition strokeDefinition;
    private string strokeUndoName;
    private readonly HashSet<HeightMapLevelAsset> changedStrokeTiles = new();
    private bool hasLastStrokePosition;
    private Vector2 lastStrokeMapPosition;
    private int strokeUndoGroup = -1;
    private SerializedObject serializedLevel;
    private HeightMapPainterEditState editState;
    private Texture2D terrainPreviewTexture;
    private Material terrainPreviewMaterial;
    private Mesh terrainPreviewMesh;
    private bool terrainPreviewDirty = true;
    [SerializeField] private TiledLevelAsset terrainRegionLevel;
    [SerializeField] private bool showTerrainRegions;
    [SerializeField] private float terrainRegionCellSize = 1f;
    [SerializeField] private float terrainRegionTolerance = 2f;
    [SerializeField] private float terrainRegionMinimumArea = 36f;
    private TiledLevelAsset previewedTerrainRegionLevel;
    private (TerrainKind Terrain, Vector3[] Lines)[] terrainRegionLines;
    private string terrainRegionError;

    private TiledLevelAsset TerrainRegionLevel => mapController != null
        ? mapController.LevelAsset : terrainRegionLevel;

    [MenuItem("Animal Game/Level/Terrain Surface Painter")]
    public static void Open()
    {
        var window = GetWindow<HeightMapSurfacePainterWindow>();
        window.titleContent = new GUIContent("Surface Painter");
        window.minSize = new Vector2(390f, 520f);
        window.TryAdoptCurrentSelection();
        window.Show();
    }

    public static void OpenForAsset(HeightMapLevelAsset asset)
    {
        Open();
        var window = GetWindow<HeightMapSurfacePainterWindow>();
        window.SetLevelAsset(asset);
        window.Focus();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("Surface Painter");
        minSize = new Vector2(390f, 520f);
        SceneView.duringSceneGui += HandleSceneGui;
        Undo.undoRedoPerformed += HandleUndoRedo;
        if (levelAsset == null)
            TryAdoptCurrentSelection();
        ResolveSceneMap();
    }

    private void OnDisable()
    {
        if (strokeActive)
            EndStroke();

        SceneView.duringSceneGui -= HandleSceneGui;
        Undo.undoRedoPerformed -= HandleUndoRedo;
        DestroyWorkingCopies();
        serializedLevel = null;
    }

    private void OnFocus()
    {
        ResolveSceneMap();
        Repaint();
    }

    private void OnHierarchyChange()
    {
        ResolveSceneMap();
        Repaint();
    }

    private void OnProjectChange()
    {
        if (showTerrainRegions) ReloadTerrainRegionPreview();
    }

    private void OnGUI()
    {
        using (new EditorGUI.DisabledScope(levelAsset == null
                   || !levelAsset.HasUsableSurfaceDefinitions
                   || !levelAsset.TryValidateSurfacePalette(out _)))
        {
            if (GUILayout.Button("Save", GUILayout.Height(28f)))
                SaveSurface();
        }
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.LabelField(
            "Permanent Multi-Terrain Surface",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Edits remain temporary until you click Save to bake and save the changed tiles. " +
            "Scene colors show the selected tile's terrain IDs; baked textures update after Save. Settings are shared by the scene map tiles. " +
            "Fill and Clear apply to the selected tile. Click another tile to save and select it without painting. Closing the window or assigning another asset discards unsaved edits.",
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

        DrawTerrainRegionSettings();
        if (levelAsset == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a map tile (HeightMapLevelAsset) to choose its brush palette.",
                MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawSurfaceSettings();
        DrawPaletteSelector();
        bool validPalette = levelAsset.TryValidateSurfacePalette(out string paletteError);
        if (!validPalette)
            EditorGUILayout.HelpBox(paletteError, MessageType.Error);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
        brushRadiusMeters = EditorGUILayout.Slider(
            "Radius (map metres)",
            brushRadiusMeters,
            MinimumBrushRadiusMeters,
            Mathf.Max(25f, levelAsset.MapSizeMeters.magnitude * 0.25f));
        paintInScene = GUILayout.Toggle(
            paintInScene,
            paintInScene ? "Scene Painting Enabled" : "Scene Painting Disabled",
            "Button");

        TerrainSurfaceDefinition selectedDefinition =
            levelAsset.GetSurfaceDefinition(selectedTerrain);
        bool canPaintSelected = selectedDefinition != null
                                && selectedDefinition.IsUsable;
        using (new EditorGUI.DisabledScope(
                   !validPalette || !levelAsset.HasUsableSurfaceDefinitions))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canPaintSelected))
                {
                    if (GUILayout.Button("Fill With Selected"))
                        FillType(selectedTerrain);
                }

                if (GUILayout.Button("Clear All Terrain"))
                    FillType(TerrainKind.Unknown);
            }
        }

        if (!levelAsset.HasUsableSurfaceDefinitions)
        {
            EditorGUILayout.HelpBox(
                "Add at least one Palette entry with a Pattern Texture before " +
                "painting or baking.",
                MessageType.Error);
        }
        else if (!canPaintSelected)
        {
            EditorGUILayout.HelpBox(
                "The selected terrain has no Pattern Texture. Assign one or " +
                "select another Palette entry.",
                MessageType.Warning);
        }
        else if (mapController == null)
        {
            EditorGUILayout.HelpBox(
                "The selected level is not present in the open scene. Baking " +
                "still works, but Scene painting needs its MapTestController.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Scene controls\n" +
            "• Choose a terrain kind above, then left-drag to paint it\n" +
            "• Shift + left-drag: erase to Unknown\n" +
            "• Alt + mouse: normal Scene camera navigation\n" +
            "• Ctrl+Z / Ctrl+Y: undo or redo temporary edits",
            MessageType.None);

        EditorGUILayout.EndScrollView();
        if (GUI.changed)
            SceneView.RepaintAll();
    }

    private void DrawSurfaceSettings()
    {
        if (levelAsset.MapParams != null)
        {
            EditorGUILayout.ObjectField("Map Params", levelAsset.MapParams, typeof(MapParams), false);
            if (GUILayout.Button("Edit Map Params"))
                Selection.activeObject = levelAsset.MapParams;
            return;
        }
        if (serializedLevel == null
            || serializedLevel.targetObject != levelAsset)
        {
            serializedLevel = new SerializedObject(levelAsset);
        }

        SerializedProperty palette = serializedLevel.FindProperty(
            "surfacePalette");
        SerializedProperty transitionWidth = serializedLevel.FindProperty(
            "surfaceTransitionWidthMeters");
        SerializedProperty alphaCoreWidth = serializedLevel.FindProperty(
            "surfaceAlphaCoreWidthMeters");
        SerializedProperty alphaBlendShare = serializedLevel.FindProperty(
            "surfaceAlphaBlendShare");
        SerializedProperty boundaryNoiseScale = serializedLevel.FindProperty(
            "surfaceBoundaryNoiseScaleMeters");
        SerializedProperty boundaryNoiseAmplitude = serializedLevel.FindProperty(
            "surfaceBoundaryNoiseAmplitudeMeters");
        SerializedProperty scatterCellSize = serializedLevel.FindProperty(
            "surfaceScatterCellSizeMeters");
        SerializedProperty scatterStrength = serializedLevel.FindProperty(
            "surfaceScatterStrength");
        SerializedProperty noiseSeed = serializedLevel.FindProperty(
            "surfaceNoiseSeed");
        SerializedProperty bakeResolution = serializedLevel.FindProperty(
            "surfaceBakeResolution");
        SerializedProperty maskResolution = serializedLevel.FindProperty(
            "surfaceMaskResolution");
        SerializedProperty patternAlphaNormalization =
            serializedLevel.FindProperty(
                "surfacePatternAlphaNormalizationStrength");
        SerializedProperty closedContourAlphaEnabled =
            serializedLevel.FindProperty(
                "surfaceClosedContourAlphaEnabled");
        SerializedProperty closedContourAlphaResolution =
            serializedLevel.FindProperty(
                "surfaceClosedContourAlphaResolution");
        SerializedProperty closedContourEdgeAlpha =
            serializedLevel.FindProperty(
                "surfaceClosedContourEdgeAlphaMultiplier");
        SerializedProperty closedContourEdgeHoldDistance =
            serializedLevel.FindProperty(
                "surfaceClosedContourEdgeHoldDistanceMeters");
        SerializedProperty closedContourFadeDistance =
            serializedLevel.FindProperty(
                "surfaceClosedContourFadeDistanceMeters");
        SerializedProperty closedContourCenterAlpha =
            serializedLevel.FindProperty(
                "surfaceClosedContourCenterAlphaMultiplier");
        SerializedProperty closedContourDistanceCurve =
            serializedLevel.FindProperty(
                "surfaceClosedContourDistanceCurve");
        SerializedProperty closedContourMinimumArea =
            serializedLevel.FindProperty(
                "surfaceClosedContourMinimumAreaSquareMeters");
        SerializedProperty outsideClosedContourAlpha =
            serializedLevel.FindProperty(
                "surfaceOutsideClosedContourAlphaMultiplier");
        SerializedProperty revealEdge = serializedLevel.FindProperty(
            "surfaceRevealEdgePixels");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Terrain Palette", EditorStyles.boldLabel);
        // Keep the same SerializedObject alive while a text field owns keyboard
        // focus. Recreating it every IMGUI repaint discarded an unfinished
        // numeric edit and made text-only values such as Tile Size Meters snap
        // back to their previous serialized value (usually the default 8).
        serializedLevel.UpdateIfRequiredOrScript();
        EditorGUILayout.PropertyField(palette, true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Baked Transition Settings",
            EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(transitionWidth);
        EditorGUILayout.PropertyField(alphaCoreWidth);
        EditorGUILayout.PropertyField(alphaBlendShare);
        EditorGUILayout.PropertyField(boundaryNoiseScale);
        EditorGUILayout.PropertyField(boundaryNoiseAmplitude);
        EditorGUILayout.PropertyField(
            scatterCellSize,
            new GUIContent("Boundary Detail Scale (metres)"));
        EditorGUILayout.PropertyField(
            scatterStrength,
            new GUIContent("Boundary Detail Strength"));
        EditorGUILayout.PropertyField(noiseSeed);
        EditorGUILayout.PropertyField(
            bakeResolution,
            new GUIContent("Baked Texture Resolution"));
        EditorGUILayout.PropertyField(
            maskResolution,
            new GUIContent("Terrain Map Resolution"));
        EditorGUILayout.PropertyField(
            patternAlphaNormalization,
            new GUIContent("Pattern Alpha Normalization"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Static Closed-Contour Alpha",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "This gradient is calculated only when the fixed map is baked. " +
            "It is stored permanently in the runtime texture and never reads " +
            "the player position or changes during gameplay. The map's four " +
            "straight sides close contour lines that reach an edge.",
            MessageType.Info);
        EditorGUILayout.PropertyField(
            closedContourAlphaEnabled,
            new GUIContent("Enable Static Contour Alpha"));
        using (new EditorGUI.DisabledScope(
                   !closedContourAlphaEnabled.boolValue))
        {
            EditorGUILayout.PropertyField(
                closedContourAlphaResolution,
                new GUIContent("Contour Analysis Resolution"));
            EditorGUILayout.PropertyField(
                closedContourEdgeAlpha,
                new GUIContent("Boundary Alpha Multiplier"));
            EditorGUILayout.PropertyField(
                closedContourEdgeHoldDistance,
                new GUIContent("Full Edge Width (metres)"));
            EditorGUILayout.PropertyField(
                closedContourFadeDistance,
                new GUIContent("Transparent Distance (metres)"));
            EditorGUILayout.PropertyField(
                closedContourCenterAlpha,
                new GUIContent("Centre Alpha Multiplier"));
            EditorGUILayout.PropertyField(
                closedContourDistanceCurve,
                new GUIContent("Interior Fade Strength"));
            EditorGUILayout.PropertyField(
                closedContourMinimumArea,
                new GUIContent("Minimum Region Area (m²)"));
            EditorGUILayout.PropertyField(
                outsideClosedContourAlpha,
                new GUIContent("Outside Region Alpha Multiplier"));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Reveal", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            revealEdge,
            new GUIContent("UI Reveal Edge (pixels)"));

        foreach (string path in HeightMapSurfaceSettings.GrasslandPaths)
            EditorGUILayout.PropertyField(serializedLevel.FindProperty(path));

        if (serializedLevel.hasModifiedProperties)
        {
            terrainPreviewDirty = true;
            editState.RecordChange("Change Terrain Surface Settings");
        }
        HeightMapSurfaceSettings.ApplyEdits(serializedLevel, mapController, workingMap);

        bool normalized = levelAsset.EnsureSurfaceAuthoringData();
        if (normalized)
        {
            terrainPreviewDirty = true;
            EditorUtility.SetDirty(levelAsset);
        }
        EnsureSelectedTerrain();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Baked Runtime Texture",
                levelAsset.BakedSurfaceVisual,
                typeof(Texture2D),
                false);
        }
    }

    private void DrawPaletteSelector()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Paint Terrain", EditorStyles.boldLabel);
        IReadOnlyList<TerrainSurfaceDefinition> palette =
            levelAsset.SurfacePalette;
        if (palette == null || palette.Count == 0)
            return;

        foreach (TerrainSurfaceDefinition definition in palette)
        {
            if (definition == null)
                continue;

            Color buttonColor = TerrainPreviewColor(definition.Terrain);
            var buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.normal.textColor = buttonColor;
            buttonStyle.hover.textColor = buttonColor;
            buttonStyle.active.textColor = buttonColor;
            buttonStyle.focused.textColor = buttonColor;
            buttonStyle.onNormal.textColor = buttonColor;
            buttonStyle.onHover.textColor = buttonColor;
            buttonStyle.onActive.textColor = buttonColor;
            buttonStyle.onFocused.textColor = buttonColor;
            bool selected = definition.Terrain == selectedTerrain;
            string textureStatus = definition.PatternTexture != null
                ? string.Empty
                : " (texture missing)";
            if (GUILayout.Toggle(
                    selected,
                    $"{definition.Terrain}: {definition.DisplayName}" +
                    textureStatus,
                    buttonStyle))
            {
                selectedTerrain = definition.Terrain;
            }
        }
    }

    private void EnsureSelectedTerrain()
    {
        if (levelAsset == null)
            return;
        if (levelAsset.GetSurfaceDefinition(selectedTerrain) != null)
            return;

        IReadOnlyList<TerrainSurfaceDefinition> palette =
            levelAsset.SurfacePalette;
        if (palette == null)
            return;

        foreach (TerrainSurfaceDefinition definition in palette)
        {
            if (definition == null)
                continue;

            selectedTerrain = definition.Terrain;
            return;
        }
    }

    private void HandleSceneGui(SceneView sceneView)
    {
        Event current = Event.current;
        DrawTerrainPreview();
        DrawTerrainRegionPreview();
        HeightMapPainterSceneInput.DrawSelectedTileBounds(mapController, sourceLevelAsset);
        if (!paintInScene || EditorApplication.isPlaying || mapController == null || !mapController.HasGeneratedMap)
            return;

        int controlId = GUIUtility.GetControlID("HeightMapSurfacePainter".GetHashCode(), FocusType.Passive);
        bool ownsStroke = strokeActive && GUIUtility.hotControl == controlId;
        bool hasPosition = HeightMapPainterSceneInput.TryGetTileAtCursor(mapController, current.mousePosition,
            out HeightMapLevelAsset hitLevel, out Vector2 mapPosition);

        if (ownsStroke && current.rawType == EventType.MouseUp && current.button == 0)
        {
            if (!current.alt && hasPosition && hitLevel == sourceLevelAsset)
                PaintStrokeTo(mapPosition);
            GUIUtility.hotControl = 0;
            EndStroke();
            current.Use();
            sceneView.Repaint();
            return;
        }

        if (!hasPosition || hitLevel != sourceLevelAsset)
        {
            if (ownsStroke && current.type == EventType.MouseDrag)
                hasLastStrokePosition = false;
        }
        if (!hasPosition)
            return;
        if (current.type == EventType.Layout)
            HandleUtility.AddDefaultControl(controlId);
        if (current.alt || current.button != 0)
        {
            if (ownsStroke && current.type == EventType.MouseDrag)
                hasLastStrokePosition = false;
            return;
        }
        if (current.type == EventType.MouseDown && !strokeActive && SelectTileForStroke(hitLevel))
        {
            current.Use();
            return;
        }
        if (hitLevel != sourceLevelAsset || levelAsset == null)
            return;

        TerrainSurfaceDefinition definition = levelAsset.GetSurfaceDefinition(selectedTerrain);
        if (definition == null || !definition.IsUsable || !levelAsset.TryValidateSurfacePalette(out _))
            return;
        TerrainSurfaceDefinition outlineDefinition = current.shift ? null : definition;
        DrawBrushOutline(mapPosition, ownsStroke ? strokeDefinition : outlineDefinition);
        if (current.type == EventType.MouseDown && !strokeActive)
        {
            GUIUtility.hotControl = controlId;
            BeginStroke(mapPosition, outlineDefinition);
            current.Use();
        }
        else if (current.type == EventType.MouseDrag && ownsStroke)
        {
            PaintStrokeTo(mapPosition);
            current.Use();
            sceneView.Repaint();
        }
    }

    // A click on a different tile only saves and selects; it never starts a stroke.
    private bool SelectTileForStroke(HeightMapLevelAsset target)
    {
        if (target == sourceLevelAsset)
            return false;
        if (sourceLevelAsset == null || !editState.HasChanges || SaveSurface())
            SetLevelAsset(target);
        return true;
    }

    private void DrawBrushOutline(Vector2 centreMapPosition, TerrainSurfaceDefinition definition)
    {
        var points = new Vector3[BrushOutlineSegments + 1];
        for (int index = 0; index <= BrushOutlineSegments; index++)
        {
            float angle = index / (float)BrushOutlineSegments * Mathf.PI * 2f;
            Vector2 mapPoint = centreMapPosition + new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) * brushRadiusMeters;
            points[index] = mapController.MapPositionToWorld(mapPoint);
        }

        Color outlineColor = TerrainPreviewColor(definition?.Terrain ?? TerrainKind.Unknown);
        outlineColor.a = 0.95f;
        Handles.color = outlineColor;
        Handles.DrawAAPolyLine(3f, points);
    }

    private void BeginStroke(Vector2 mapPosition, TerrainSurfaceDefinition definition)
    {
        strokeActive = true;
        strokeDefinition = definition;
        hasLastStrokePosition = false;
        Undo.IncrementCurrentGroup();
        strokeUndoGroup = Undo.GetCurrentGroup();
        strokeUndoName = definition == null
            ? "Erase Terrain Surface"
            : $"Paint {definition.DisplayName}";
        Undo.SetCurrentGroupName(strokeUndoName);
        PaintStrokeTo(mapPosition);
    }

    private void PaintStrokeTo(Vector2 mapPosition)
    {
        if (!strokeActive)
            return;
        if (!HeightMapPainterSceneInput.IsInSelectedTile(mapController, sourceLevelAsset, mapPosition))
        {
            hasLastStrokePosition = false;
            return;
        }

        if (!hasLastStrokePosition)
        {
            PaintDab(mapPosition);
            lastStrokeMapPosition = mapPosition;
            hasLastStrokePosition = true;
            return;
        }

        float distance = Vector2.Distance(lastStrokeMapPosition, mapPosition);
        float spacing = Mathf.Max(0.05f, brushRadiusMeters * 0.3f);
        int stepCount = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));
        for (int step = 1; step <= stepCount; step++)
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
        if (!HeightMapPainterSceneInput.IsInSelectedTile(mapController, sourceLevelAsset, mapPosition))
            return;
        mapController.TryGetTileOrigin(sourceLevelAsset, out Vector2 origin);
        TerrainKind terrain = strokeDefinition?.Terrain ?? TerrainKind.Unknown;
        if (levelAsset.PaintSurfaceType(mapPosition - origin, brushRadiusMeters, terrain, strokeDefinition,
                () => RegisterStrokeUndo(levelAsset)))
        {
            terrainPreviewDirty = true;
            changedStrokeTiles.Add(levelAsset);
            EditorUtility.SetDirty(levelAsset);
        }
    }

    private void RegisterStrokeUndo(HeightMapLevelAsset tile)
    {
        if (!changedStrokeTiles.Contains(tile))
        {
            Undo.RegisterCompleteObjectUndo(tile, strokeUndoName);
            editState.RecordChange(strokeUndoName);
        }
    }

    private void EndStroke()
    {
        if (!strokeActive)
            return;

        strokeActive = false;
        hasLastStrokePosition = false;
        Undo.CollapseUndoOperations(strokeUndoGroup);
        changedStrokeTiles.Clear();
        strokeDefinition = null;
        SceneView.RepaintAll();
        Repaint();
    }

    private void FillType(TerrainKind terrain)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        string operationName = terrain == TerrainKind.Unknown
            ? "Clear Terrain Surface"
            : "Fill Terrain Surface";
        Undo.SetCurrentGroupName(operationName);
        Undo.RegisterCompleteObjectUndo(levelAsset, operationName);
        if (levelAsset.FillSurfaceType(terrain))
        {
            terrainPreviewDirty = true;
            editState.RecordChange(operationName);
            EditorUtility.SetDirty(levelAsset);
        }

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
    }

    private void HandleUndoRedo()
    {
        terrainPreviewDirty = true;
        SceneView.RepaintAll();
        Repaint();
    }

    private bool SaveSurface()
    {
        EndStroke();
        if (workingMap != null)
            HeightMapSurfaceSettings.CopyAll(levelAsset, workingMap);

        foreach (var pair in workingTiles)
        {
            HeightMapLevelAsset source = pair.Key;
            using var edited = new SerializedObject(pair.Value);
            using var destination = new SerializedObject(source);
            HeightMapSurfaceSettings.CopyAuthoring(edited, destination);
            bool changed = destination.hasModifiedProperties;
            destination.ApplyModifiedPropertiesWithoutUndo();
            if (changed || source.SurfaceBakeNeedsUpgrade || source == sourceLevelAsset)
            {
                if (!HeightMapSurfaceBaker.Bake(source, false))
                    return false;
            }
        }
        editState.MarkSaved();
        SceneView.RepaintAll();
        Repaint();
        return true;
    }

    private HeightMapLevelAsset GetWorkingTile(HeightMapLevelAsset source)
    {
        if (!workingTiles.TryGetValue(source, out HeightMapLevelAsset working))
        {
            working = Instantiate(source);
            working.name = source.name + " (Surface Edit)";
            working.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.NotEditable;
            working.EnsureSurfaceAuthoringData();
            workingTiles.Add(source, working);
        }
        return working;
    }

    private void DestroyWorkingCopies()
    {
        DestroyImmediate(terrainPreviewTexture);
        DestroyImmediate(terrainPreviewMaterial);
        DestroyImmediate(terrainPreviewMesh);
        terrainPreviewTexture = null;
        terrainPreviewMaterial = null;
        terrainPreviewMesh = null;
        terrainPreviewDirty = true;
        editState?.Dispose();
        editState = null;
        foreach (HeightMapLevelAsset working in workingTiles.Values)
        {
            Undo.ClearUndo(working);
            DestroyImmediate(working);
        }
        workingTiles.Clear();
        if (workingMap != null)
            DestroyImmediate(workingMap);
        workingMap = null;
        levelAsset = null;
    }

    private static Color32 TerrainPreviewColor(TerrainKind terrain) => terrain switch
    {
        TerrainKind.Unknown => new Color32(150, 150, 150, 255),
        TerrainKind.GridTest => new Color32(245, 245, 245, 255),
        TerrainKind.GrassTest => new Color32(245, 135, 190, 255),
        TerrainKind.Grass => new Color32(75, 205, 95, 255),
        TerrainKind.Grassland => new Color32(75, 220, 205, 255),
        TerrainKind.Gravel => new Color32(235, 140, 65, 255),
        TerrainKind.Rock => new Color32(155, 140, 240, 255),
        TerrainKind.Water => new Color32(65, 155, 245, 255),
        TerrainKind.Web => new Color32(225, 75, 80, 255),
        TerrainKind.Sand => new Color32(212, 184, 84, 255), // #D4B854
        _ => throw new ArgumentOutOfRangeException(nameof(terrain), terrain, null)
    };

    private void DrawTerrainRegionSettings()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Terrain Regions", EditorStyles.boldLabel);
        if (mapController == null)
        {
            terrainRegionLevel = (TiledLevelAsset)EditorGUILayout.ObjectField(
                "Tiled Level", terrainRegionLevel, typeof(TiledLevelAsset), false);
        }

        bool show = EditorGUILayout.Toggle("Show Region Polygons", showTerrainRegions);
        if (show != showTerrainRegions || (show && previewedTerrainRegionLevel != TerrainRegionLevel))
        {
            showTerrainRegions = show;
            ReloadTerrainRegionPreview();
        }
        terrainRegionCellSize = Mathf.Max(0, EditorGUILayout.FloatField(new GUIContent("Input Cell Size (m)",
            "0 uses the native terrain ID grid. Larger cells sample the terrain ID at their center."),
            terrainRegionCellSize));
        terrainRegionTolerance = Mathf.Max(0, EditorGUILayout.FloatField(new GUIContent("Boundary Tolerance (m)",
            "0 preserves the grid outline. Positive values simplify shared boundaries together."),
            terrainRegionTolerance));
        terrainRegionMinimumArea = Mathf.Max(0, EditorGUILayout.FloatField(new GUIContent("Minimum Region Area (m²)",
            "Omit disconnected polygons below this area, after subtracting their holes."),
            terrainRegionMinimumArea));
        if (editState != null && editState.HasChanges)
            EditorGUILayout.HelpBox("Update uses saved terrain IDs. Current brush edits are still unsaved.", MessageType.Info);
        using (new EditorGUI.DisabledScope(TerrainRegionLevel == null || EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Update Terrain Regions"))
                UpdateTerrainRegions();
        if (!string.IsNullOrEmpty(terrainRegionError))
            EditorGUILayout.HelpBox(terrainRegionError, MessageType.Error);
        else if (showTerrainRegions && TerrainRegionLevel != null && terrainRegionLines == null)
            EditorGUILayout.HelpBox("No terrain regions have been generated for this level.", MessageType.Info);
    }

    private void UpdateTerrainRegions()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Terrain Regions", "Baking the saved terrain IDs…", 0.5f);
            TerrainRegionsPrebake data = AnimalGame.Editor.TerrainRegionsBaker.Update(TerrainRegionLevel,
                terrainRegionCellSize, terrainRegionTolerance, terrainRegionMinimumArea);
            terrainRegionError = null;
            if (showTerrainRegions) SetTerrainRegionPreview(data);
        }
        catch (Exception error)
        {
            terrainRegionError = error.Message;
            Debug.LogException(error);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void ReloadTerrainRegionPreview()
    {
        terrainRegionError = null;
        SetTerrainRegionPreview(null);
        if (!showTerrainRegions || previewedTerrainRegionLevel == null) return;
        try
        {
            SetTerrainRegionPreview(previewedTerrainRegionLevel.LoadPrebakedTerrainRegions());
        }
        catch (Exception error) when (error is IOException || error is InvalidDataException)
        {
            terrainRegionError = error.Message;
        }
    }

    private void SetTerrainRegionPreview(TerrainRegionsPrebake data)
    {
        previewedTerrainRegionLevel = TerrainRegionLevel;
        terrainRegionLines = null;
        if (data != null)
        {
            terrainRegionLines = new (TerrainKind, Vector3[])[data.Regions.Length];
            for (int i = 0; i < data.Regions.Length; i++)
            {
                var region = data.Regions[i];
                var lines = new List<Vector3>();
                foreach (var polygon in region.Polygons)
                {
                    AddRegionRing(lines, polygon.Outer);
                    foreach (Vector2[] hole in polygon.Holes) AddRegionRing(lines, hole);
                }
                terrainRegionLines[i] = (region.Terrain, lines.ToArray());
            }
        }
        SceneView.RepaintAll();
        Repaint();
    }

    private static void AddRegionRing(List<Vector3> lines, Vector2[] ring)
    {
        for (int i = 0; i < ring.Length; i++)
        {
            lines.Add(ring[i]);
            lines.Add(ring[(i + 1) % ring.Length]);
        }
    }

    private void DrawTerrainRegionPreview()
    {
        if (Event.current.type != EventType.Repaint || !showTerrainRegions || terrainRegionLines == null
            || mapController == null || !mapController.HasGeneratedMap || EditorApplication.isPlaying
            || mapController.LevelAsset != previewedTerrainRegionLevel)
            return;

        Bounds bounds = mapController.WorldBounds;
        Vector2 mapSize = mapController.MapSizeMeters;
        var matrix = Matrix4x4.TRS(new Vector3(bounds.min.x, bounds.min.y, bounds.center.z),
            Quaternion.identity, new Vector3(bounds.size.x / mapSize.x, bounds.size.y / mapSize.y, 1));
        var previousZTest = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        foreach (var (terrain, lines) in terrainRegionLines)
        {
            using var scope = new Handles.DrawingScope(TerrainPreviewColor(terrain), matrix);
            Handles.DrawLines(lines);
        }
        Handles.zTest = previousZTest;
    }

    private void DrawTerrainPreview()
    {
        if (Event.current.type != EventType.Repaint || !paintInScene || EditorApplication.isPlaying
            || levelAsset == null || mapController == null || !mapController.HasGeneratedMap
            || !mapController.TryGetTileOrigin(sourceLevelAsset, out Vector2 origin))
            return;

        if (terrainPreviewMaterial == null)
        {
            terrainPreviewMaterial = new Material(Shader.Find("Hidden/AnimalGame/SurfaceTerrainEditorPreview"))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            terrainPreviewMesh = new Mesh
            {
                name = "Surface Terrain Scene Preview",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (terrainPreviewDirty)
        {
            byte[] types = levelAsset.GetOrCreateSurfaceTypeMap();
            int resolution = levelAsset.SurfaceMaskResolution;
            if (terrainPreviewTexture == null || terrainPreviewTexture.width != resolution)
            {
                DestroyImmediate(terrainPreviewTexture);
                terrainPreviewTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
                {
                    name = "Surface Terrain ID Colors",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                terrainPreviewMaterial.mainTexture = terrainPreviewTexture;
            }
            var colors = new Color32[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                Color32 color = TerrainPreviewColor((TerrainKind)types[i]);
                color.a = types[i] == (byte)TerrainKind.Unknown ? (byte)45 : (byte)145;
                colors[i] = color;
            }
            terrainPreviewTexture.SetPixels32(colors);
            terrainPreviewTexture.Apply(false, false);
            terrainPreviewDirty = false;
        }

        Vector2 size = levelAsset.MapSizeMeters;
        terrainPreviewMesh.vertices = new[]
        {
            mapController.MapPositionToWorld(origin),
            mapController.MapPositionToWorld(origin + Vector2.right * size.x),
            mapController.MapPositionToWorld(origin + Vector2.up * size.y),
            mapController.MapPositionToWorld(origin + size)
        };
        terrainPreviewMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        terrainPreviewMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        terrainPreviewMesh.RecalculateBounds();
        terrainPreviewMaterial.SetPass(0);
        Graphics.DrawMeshNow(terrainPreviewMesh, Matrix4x4.identity);
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
            UnityEngine.Object.FindObjectsByType<MapTestSceneController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (openMaps.Length > 0)
            SetLevelAsset(openMaps[0].LevelAsset != null && openMaps[0].LevelAsset.Tiles.Count == 1 ? openMaps[0].LevelAsset.Tiles[0].level : null);
    }

    private void SetLevelAsset(HeightMapLevelAsset asset)
    {
        if (sourceLevelAsset == asset && levelAsset != null)
            return;

        EndStroke();
        DestroyWorkingCopies();
        sourceLevelAsset = asset;
        if (asset != null)
        {
            editState = CreateInstance<HeightMapPainterEditState>();
            editState.hideFlags = HideFlags.HideAndDontSave;
        }
        levelAsset = asset != null ? GetWorkingTile(asset) : null;
        serializedLevel = levelAsset != null ? new SerializedObject(levelAsset) : null;
        EnsureSelectedTerrain();
        ResolveSceneMap();
        Repaint();
        SceneView.RepaintAll();
    }

    private void ResolveSceneMap()
    {
        mapController = HeightMapPainterSceneInput.FindSceneMap(sourceLevelAsset);
        if (mapController == null || workingMap != null)
            return;

        workingMap = Instantiate(mapController.LevelAsset);
        workingMap.hideFlags = HideFlags.HideAndDontSave;
        foreach (TiledLevelAsset.Tile entry in workingMap.Tiles)
            entry.level = GetWorkingTile(entry.level);
    }
}

// Only revision is serialized into Undo; the unique counter and save point stay current.
internal sealed class HeightMapPainterEditState : ScriptableObject, IDisposable
{
    [SerializeField] private int revision;
    private int nextRevision;
    private int savedRevision;

    internal bool HasChanges => revision != savedRevision;

    internal void RecordChange(string operation)
    {
        Undo.RegisterCompleteObjectUndo(this, operation);
        revision = ++nextRevision;
    }

    internal void MarkSaved() => savedRevision = revision;

    public void Dispose()
    {
        Undo.ClearUndo(this);
        DestroyImmediate(this);
    }
}

internal static class HeightMapPainterSceneInput
{
    internal static void DrawSelectedTileBounds(MapTestSceneController mapController, HeightMapLevelAsset sourceLevelAsset)
    {
        if (Event.current.type != EventType.Repaint || mapController == null || sourceLevelAsset == null
            || !mapController.TryGetTileOrigin(sourceLevelAsset, out Vector2 origin))
            return;
        Vector2 size = sourceLevelAsset.MapSizeMeters;
        Vector3[] points =
        {
            mapController.MapPositionToWorld(origin),
            mapController.MapPositionToWorld(origin + Vector2.right * size.x),
            mapController.MapPositionToWorld(origin + size),
            mapController.MapPositionToWorld(origin + Vector2.up * size.y),
            mapController.MapPositionToWorld(origin)
        };
        Color color = Handles.color;
        var zTest = Handles.zTest;
        Handles.color = Color.cyan;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.DrawAAPolyLine(3f, points);
        Handles.color = color;
        Handles.zTest = zTest;
    }

    internal static MapTestSceneController FindSceneMap(HeightMapLevelAsset selected)
    {
        if (selected != null)
            return HeightMapSurfaceSettings.FindSceneMap(selected);
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapTestSceneController>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!EditorUtility.IsPersistent(map) && map.HasGeneratedMap)
                return map;
        }
        return null;
    }

    internal static bool TryGetTileAtCursor(MapTestSceneController map, Vector2 mousePosition,
        out HeightMapLevelAsset level, out Vector2 mapPosition)
    {
        level = null;
        mapPosition = default;
        Bounds bounds = map.WorldBounds;
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        if (!new Plane(Vector3.forward, bounds.center).Raycast(ray, out float distance))
            return false;
        Vector3 offset = ray.GetPoint(distance) - bounds.min;
        Vector2 size = map.MapSizeMeters;
        mapPosition = new Vector2(offset.x / bounds.size.x * size.x, offset.y / bounds.size.y * size.y);
        if (!map.RuntimeMap.TryLocate(mapPosition, out var tile, out _))
            return false;
        level = tile.Level;
        return true;
    }

    internal static bool IsInSelectedTile(MapTestSceneController map, HeightMapLevelAsset selected, Vector2 position)
    {
        return map.RuntimeMap.TryLocate(position, out var tile, out _) && tile.Level == selected;
    }
}

internal static class HeightMapSurfaceSettings
{
    private const string MaskResolutionPath = "surfaceMaskResolution";
    private const string BakeVersionPath = "surfaceBakeGeneratorVersion";
    internal static readonly string[] BakedPaths =
    {
        "surfacePalette",
        "surfaceTransitionWidthMeters",
        "surfaceAlphaCoreWidthMeters",
        "surfaceAlphaBlendShare",
        "surfaceBoundaryNoiseScaleMeters",
        "surfaceBoundaryNoiseAmplitudeMeters",
        "surfaceScatterCellSizeMeters",
        "surfaceScatterStrength",
        "surfaceNoiseSeed",
        "surfaceBakeResolution",
        MaskResolutionPath,
        "surfacePatternAlphaNormalizationStrength",
        "surfaceClosedContourAlphaEnabled",
        "surfaceClosedContourAlphaResolution",
        "surfaceClosedContourEdgeAlphaMultiplier",
        "surfaceClosedContourEdgeHoldDistanceMeters",
        "surfaceClosedContourFadeDistanceMeters",
        "surfaceClosedContourCenterAlphaMultiplier",
        "surfaceClosedContourDistanceCurve",
        "surfaceClosedContourMinimumAreaSquareMeters",
        "surfaceOutsideClosedContourAlphaMultiplier"
    };

    internal static readonly string[] GrasslandPaths =
    {
        "grasslandLayoutSeed", "grasslandPositionJitter", "grasslandDirectionRegionScale",
        "grasslandBladeAngleVariation", "grasslandDensity", "grasslandBladeLengthScale",
        "grass1", "grass2", "grass3", "grassOverlayDensity", "grassOverlaySize"
    };

    private static readonly IReadOnlyList<string> RuntimePaths = new List<string>(GrasslandPaths)
    {
        "surfaceRevealEdgePixels"
    };

    internal static void ApplyEdits(SerializedObject edited, MapTestSceneController sceneMap = null,
        TiledLevelAsset workingMap = null)
    {
        if (!edited.hasModifiedProperties)
            return;

        var source = (HeightMapLevelAsset)edited.targetObject;
        using var previous = new SerializedObject(source);
        List<string> bakedChanges = FindChanges(edited, previous, BakedPaths);
        List<string> runtimeChanges = FindChanges(edited, previous, RuntimePaths);
        if (bakedChanges.Count == 0 && runtimeChanges.Count == 0)
        {
            edited.ApplyModifiedProperties();
            return;
        }

        sceneMap ??= FindSceneMap(source);
        TiledLevelAsset map = workingMap != null ? workingMap : sceneMap != null ? sceneMap.LevelAsset : null;
        bool tiledScene = map != null && map.Tiles.Count > 1;
        int undoGroup = Undo.GetCurrentGroup();
        bool resize = bakedChanges.Contains(MaskResolutionPath);
        if (resize)
            Undo.RegisterCompleteObjectUndo(source, "Change Terrain Map Resolution");
        // Reuse the saved bake version to keep pending work after a reload.
        if (tiledScene && bakedChanges.Count != 0)
            edited.FindProperty(BakeVersionPath).intValue = 0;

        edited.ApplyModifiedProperties();
        if (resize)
            source.EnsureSurfaceAuthoringData();

        if (tiledScene && source.HasUsableSurfaceDefinitions && source.TryValidateSurfacePalette(out _))
        {
            CopySettings(source, map, bakedChanges, runtimeChanges);
            Undo.CollapseUndoOperations(undoGroup);
        }
        SceneView.RepaintAll();
    }

    internal static void CopyAuthoring(SerializedObject source, SerializedObject destination)
    {
        if (((HeightMapLevelAsset)destination.targetObject).MapParams == null)
        {
            foreach (string path in BakedPaths)
                destination.CopyFromSerializedPropertyIfDifferent(source.FindProperty(path));
            foreach (string path in RuntimePaths)
                destination.CopyFromSerializedPropertyIfDifferent(source.FindProperty(path));
        }
        destination.CopyFromSerializedPropertyIfDifferent(source.FindProperty("surfaceMask"));
        destination.CopyFromSerializedPropertyIfDifferent(source.FindProperty("surfaceDataVersion"));
    }

    private static List<string> FindChanges(SerializedObject edited, SerializedObject previous, IReadOnlyList<string> paths)
    {
        var changed = new List<string>();
        foreach (string path in paths)
        {
            if (!SerializedProperty.DataEquals(edited.FindProperty(path), previous.FindProperty(path)))
                changed.Add(path);
        }
        return changed;
    }

    internal static void CopyAll(HeightMapLevelAsset source, TiledLevelAsset map)
    {
        if (source.MapParams != null || !source.HasUsableSurfaceDefinitions || !source.TryValidateSurfacePalette(out _))
            return;
        if (CopySettings(source, map, BakedPaths, RuntimePaths))
        {
            using var settings = new SerializedObject(source);
            settings.FindProperty(BakeVersionPath).intValue = 0;
            settings.ApplyModifiedProperties();
        }
    }

    private static bool CopySettings(HeightMapLevelAsset source, TiledLevelAsset map,
        IReadOnlyList<string> bakedPaths, IReadOnlyList<string> runtimePaths)
    {
        bool changedBakedSettings = false;
        using var settings = new SerializedObject(source);
        foreach (TiledLevelAsset.Tile entry in map.Tiles)
        {
            HeightMapLevelAsset target = entry.level;
            if (target == source || target.MapParams != null)
                continue;

            using var destination = new SerializedObject(target);
            bool bakedChanges = false;
            foreach (string path in bakedPaths)
                bakedChanges |= destination.CopyFromSerializedPropertyIfDifferent(settings.FindProperty(path));
            foreach (string path in runtimePaths)
                destination.CopyFromSerializedPropertyIfDifferent(settings.FindProperty(path));
            if (!destination.hasModifiedProperties)
                continue;

            bool resize = destination.FindProperty(MaskResolutionPath).intValue != target.SurfaceMaskResolution;
            if (resize)
                Undo.RegisterCompleteObjectUndo(target, "Change Terrain Map Resolution");
            if (bakedChanges)
            {
                destination.FindProperty(BakeVersionPath).intValue = 0;
                changedBakedSettings = true;
            }
            destination.ApplyModifiedProperties();
            if (resize)
                target.EnsureSurfaceAuthoringData();
        }
        return changedBakedSettings;
    }

    internal static MapTestSceneController FindSceneMap(HeightMapLevelAsset level)
    {
        if (level == null)
            return null;

        MapTestSceneController[] maps = UnityEngine.Object.FindObjectsByType<MapTestSceneController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (MapTestSceneController map in maps)
        {
            if (!EditorUtility.IsPersistent(map) && map.ContainsLevel(level))
                return map;
        }
        return null;
    }
}

internal static class HeightMapSurfaceBaker
{
    private const string BakeShaderName =
        "Hidden/AnimalGame/Bake Terrain Surface Palette";
    private const int PaletteSize = 256;
    private const int AtlasGridSize = 16;
    private const int AtlasCellSize = 128;
    private const int AtlasPadding = 2;
    private const int AtlasSize = AtlasGridSize * AtlasCellSize;

    private static readonly int[] NeighbourX =
        { -1, 0, 1, -1, 1, -1, 0, 1 };
    private static readonly int[] NeighbourY =
        { -1, -1, -1, 0, 0, 1, 1, 1 };

    public static bool Bake(HeightMapLevelAsset level, bool registerUndo)
    {
        if (level == null)
        {
            Debug.LogError(
                "Terrain surface baking requires a fixed map asset.");
            return false;
        }

        if (level.EnsureSurfaceAuthoringData())
            EditorUtility.SetDirty(level);
        if (!level.TryValidateSurfacePalette(out string paletteError))
        {
            Debug.LogError(paletteError, level);
            return false;
        }
        if (!level.HasUsableSurfaceDefinitions)
        {
            Debug.LogError(
                "Terrain surface baking requires at least one Palette entry " +
                "with a Pattern Texture.");
            return false;
        }

        string levelPath = AssetDatabase.GetAssetPath(level);
        if (string.IsNullOrEmpty(levelPath))
        {
            Debug.LogError("Save the HeightMapLevelAsset before baking its surface.");
            return false;
        }

        Shader bakeShader = Shader.Find(BakeShaderName);
        if (bakeShader == null)
        {
            Debug.LogError($"Missing Editor bake shader: {BakeShaderName}");
            return false;
        }

        byte[] typeMap = level.GetOrCreateSurfaceTypeMap();
        int bakeResolution = level.SurfaceBakeResolution;
        float maximumBoundaryDistance = CalculateMaximumBoundaryDistance(level);
        Texture2D patternAtlas = null;
        Texture2D pairTexture = null;
        Texture2D staticContourAlphaTexture = null;
        Texture2D paletteTintTexture = null;
        Texture2D paletteSettingsTexture = null;
        Texture2D readableOutput = null;
        Material bakeMaterial = null;
        RenderTexture renderTarget = null;
        RenderTexture previousTarget = RenderTexture.active;

        try
        {
            patternAtlas = BuildPatternAtlas(level);
            pairTexture = BuildPairTexture(
                level,
                typeMap,
                maximumBoundaryDistance);
            if (level.SurfaceClosedContourAlphaEnabled)
            {
                staticContourAlphaTexture =
                    StaticClosedContourAlphaBuilder.Build(level);
            }
            BuildPaletteTextures(
                level,
                out paletteTintTexture,
                out paletteSettingsTexture);

            bakeMaterial = new Material(bakeShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            bakeMaterial.SetTexture("_PatternAtlas", patternAtlas);
            foreach (TerrainSurfaceDefinition definition in level.SurfacePalette)
            {
                if (definition.Terrain == TerrainKind.Rock)
                    bakeMaterial.SetTexture("_RockPattern", definition.PatternTexture);
                if (definition.Terrain == TerrainKind.Sand)
                    bakeMaterial.SetTexture("_SandPattern", definition.PatternTexture);
            }
            bakeMaterial.SetTexture("_TerrainPairTex", pairTexture);
            bakeMaterial.SetTexture(
                "_StaticContourAlphaTex",
                staticContourAlphaTexture != null
                    ? staticContourAlphaTexture
                    : Texture2D.whiteTexture);
            bakeMaterial.SetFloat(
                "_StaticContourAlphaEnabled",
                staticContourAlphaTexture != null ? 1f : 0f);
            bakeMaterial.SetTexture("_PaletteTintTex", paletteTintTexture);
            bakeMaterial.SetTexture(
                "_PaletteSettingsTex",
                paletteSettingsTexture);
            bakeMaterial.SetVector(
                "_MapSizeMeters",
                new Vector4(
                    level.MapSizeMeters.x,
                    level.MapSizeMeters.y,
                    0f,
                    0f));
            bakeMaterial.SetFloat(
                "_MaximumBoundaryDistanceMeters",
                maximumBoundaryDistance);
            bakeMaterial.SetFloat(
                "_TransitionWidthMeters",
                level.SurfaceTransitionWidthMeters);
            bakeMaterial.SetFloat(
                "_AlphaCoreWidthMeters",
                level.SurfaceAlphaCoreWidthMeters);
            bakeMaterial.SetFloat(
                "_AlphaBlendShare",
                level.SurfaceAlphaBlendShare);
            bakeMaterial.SetFloat(
                "_BoundaryNoiseScaleMeters",
                level.SurfaceBoundaryNoiseScaleMeters);
            bakeMaterial.SetFloat(
                "_BoundaryNoiseAmplitudeMeters",
                level.SurfaceBoundaryNoiseAmplitudeMeters);
            bakeMaterial.SetFloat(
                "_ScatterCellSizeMeters",
                level.SurfaceScatterCellSizeMeters);
            bakeMaterial.SetFloat(
                "_ScatterStrength",
                level.SurfaceScatterStrength);
            bakeMaterial.SetFloat("_NoiseSeed", level.SurfaceNoiseSeed);

            renderTarget = RenderTexture.GetTemporary(
                bakeResolution,
                bakeResolution,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            renderTarget.name = "Temporary Baked Terrain Surface";
            renderTarget.filterMode = FilterMode.Bilinear;
            renderTarget.wrapMode = TextureWrapMode.Clamp;
            Graphics.Blit(Texture2D.whiteTexture, renderTarget, bakeMaterial);

            RenderTexture.active = renderTarget;
            readableOutput = new Texture2D(
                bakeResolution,
                bakeResolution,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = "Baked Terrain Surface"
            };
            readableOutput.ReadPixels(
                new Rect(0f, 0f, bakeResolution, bakeResolution),
                0,
                0,
                false);
            readableOutput.Apply(false, false);

            string directory = HeightMapAssetPaths.DataDirectory(levelPath);
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            string fileName = Path.GetFileNameWithoutExtension(levelPath)
                              + "_SurfaceVisual.png";
            string outputPath = Path.Combine(directory ?? "Assets", fileName)
                .Replace('\\', '/');
            File.WriteAllBytes(outputPath, readableOutput.EncodeToPNG());
            AssetDatabase.ImportAsset(
                outputPath,
                ImportAssetOptions.ForceSynchronousImport
                | ImportAssetOptions.ForceUpdate);
            ConfigureBakedTextureImporter(outputPath, bakeResolution);

            Texture2D bakedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                outputPath);
            if (bakedTexture == null)
            {
                Debug.LogError(
                    $"Unity could not load the baked terrain texture at {outputPath}.");
                return false;
            }

            if (registerUndo)
                Undo.RecordObject(level, "Bake Terrain Surface");
            BakeGrassland(level, bakeMaterial, renderTarget, readableOutput, directory, levelPath);
            level.SetBakedSurfaceVisual(bakedTexture);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Baked permanent multi-terrain surface: {outputPath}",
                level);
            return true;
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (renderTarget != null)
                RenderTexture.ReleaseTemporary(renderTarget);
            DestroyTemporary(bakeMaterial);
            DestroyTemporary(patternAtlas);
            DestroyTemporary(pairTexture);
            DestroyTemporary(staticContourAlphaTexture);
            DestroyTemporary(paletteTintTexture);
            DestroyTemporary(paletteSettingsTexture);
            DestroyTemporary(readableOutput);
        }
    }

    private static void BakeGrassland(HeightMapLevelAsset level, Material material,
        RenderTexture target, Texture2D readable, string directory, string levelPath)
    {
        TerrainSurfaceDefinition grass = null;
        foreach (var entry in level.SurfacePalette)
            if (entry.Terrain == TerrainKind.Grassland && entry.IsUsable) grass = entry;
        if (grass == null)
        {
            level.SetBakedGrassland(null, null, Array.Empty<Vector4>(), 1f, Vector2.one);
            return;
        }
        var blades = new List<Sprite>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(grass.PatternTexture)))
            if (asset is Sprite blade) blades.Add(blade);
        if (blades.Count != 8)
            throw new InvalidOperationException("Grassland requires exactly eight authored blade sprites.");
        blades.Sort((a, b) => a.rect.x.CompareTo(b.rect.x));
        var rects = new Vector4[8];
        for (int i = 0; i < 8; i++)
        {
            Rect rect = blades[i].rect;
            rects[i] = new Vector4(rect.x / grass.PatternTexture.width, rect.y / grass.PatternTexture.height,
                rect.width / grass.PatternTexture.width, rect.height / grass.PatternTexture.height);
        }
        float alphaReference;
        var source = CreateReadableCopy(grass.PatternTexture);
        try { alphaReference = CalculatePatternAlphaReference(source); }
        finally { DestroyTemporary(source); }
        material.SetFloat("_GrassBakeMode", 1f);
        Graphics.Blit(Texture2D.whiteTexture, target, material);
        RenderTexture.active = target;
        readable.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
        readable.Apply(false, false);
        string path = Path.Combine(directory, Path.GetFileNameWithoutExtension(levelPath) + "_GrasslandMask.png").Replace('\\', '/');
        File.WriteAllBytes(path, readable.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        ConfigureBakedTextureImporter(path, target.width);
        level.SetBakedGrassland(AssetDatabase.LoadAssetAtPath<Texture2D>(path), grass.PatternTexture,
            rects, grass.TileSizeMeters, new Vector2(alphaReference, level.SurfacePatternAlphaNormalizationStrength));
    }

    private static Texture2D BuildPatternAtlas(HeightMapLevelAsset level)
    {
        var atlasPixels = new Color32[AtlasSize * AtlasSize];
        IReadOnlyList<TerrainSurfaceDefinition> palette = level.SurfacePalette;
        if (palette != null)
        {
            foreach (TerrainSurfaceDefinition definition in palette)
            {
                if (definition == null || !definition.IsUsable)
                    continue;

                Texture2D readable = CreateReadableCopy(
                    definition.PatternTexture);
                try
                {
                    CopyRepeatedPatternIntoAtlas(
                        readable,
                        atlasPixels,
                        definition.Terrain,
                        level.SurfacePatternAlphaNormalizationStrength);
                }
                finally
                {
                    DestroyTemporary(readable);
                }
            }
        }

        var atlas = new Texture2D(
            AtlasSize,
            AtlasSize,
            TextureFormat.RGBA32,
            false,
            false)
        {
            name = "Temporary Terrain Pattern Atlas",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        atlas.SetPixels32(atlasPixels);
        atlas.Apply(false, true);
        return atlas;
    }

    private static void CopyRepeatedPatternIntoAtlas(
        Texture2D source,
        Color32[] atlasPixels,
        TerrainKind terrain,
        float alphaNormalizationStrength)
    {
        int cellX = (int)terrain % AtlasGridSize;
        int cellY = (int)terrain / AtlasGridSize;
        int cellOriginX = cellX * AtlasCellSize;
        int cellOriginY = cellY * AtlasCellSize;
        int interiorSize = AtlasCellSize - AtlasPadding * 2;
        float alphaReference = CalculatePatternAlphaReference(source);
        float normalizationStrength = Mathf.Clamp01(
            alphaNormalizationStrength);

        for (int y = 0; y < AtlasCellSize; y++)
        {
            float sourceV = Mathf.Repeat(
                (y - AtlasPadding + 0.5f) / interiorSize,
                1f);
            for (int x = 0; x < AtlasCellSize; x++)
            {
                float sourceU = Mathf.Repeat(
                    (x - AtlasPadding + 0.5f) / interiorSize,
                    1f);
                Color sampled = source.GetPixelBilinear(sourceU, sourceV);
                if (sampled.a > 0.0001f && normalizationStrength > 0f)
                {
                    float normalizedAlpha = Mathf.Clamp01(
                        sampled.a / alphaReference);
                    sampled.a = Mathf.Lerp(
                        sampled.a,
                        normalizedAlpha,
                        normalizationStrength);
                }
                int targetX = cellOriginX + x;
                int targetY = cellOriginY + y;
                atlasPixels[targetY * AtlasSize + targetX] = sampled;
            }
        }
    }

    private static float CalculatePatternAlphaReference(Texture2D source)
    {
        Color32[] pixels = source.GetPixels32();
        var histogram = new int[256];
        int nonTransparentCount = 0;
        foreach (Color32 pixel in pixels)
        {
            if (pixel.a == 0)
                continue;

            histogram[pixel.a]++;
            nonTransparentCount++;
        }

        if (nonTransparentCount == 0)
            return 1f;

        int medianRank = (nonTransparentCount - 1) / 2;
        int accumulated = 0;
        for (int alpha = 1; alpha < histogram.Length; alpha++)
        {
            accumulated += histogram[alpha];
            if (accumulated > medianRank)
                return alpha / 255f;
        }

        return 1f;
    }

    private static Texture2D CreateReadableCopy(Texture2D source)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D readable = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readable = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = $"Readable Copy of {source.name}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            readable.ReadPixels(
                new Rect(0f, 0f, source.width, source.height),
                0,
                0,
                false);
            readable.Apply(false, false);
            return readable;
        }
        catch
        {
            DestroyTemporary(readable);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    private static void BuildPaletteTextures(
        HeightMapLevelAsset level,
        out Texture2D tintTexture,
        out Texture2D settingsTexture)
    {
        var tints = new Color[PaletteSize];
        var settings = new Color[PaletteSize];
        settings[0] = new Color(1f, 0f, 1f, 1f);

        IReadOnlyList<TerrainSurfaceDefinition> palette = level.SurfacePalette;
        if (palette != null)
        {
            foreach (TerrainSurfaceDefinition definition in palette)
            {
                if (definition == null)
                    continue;

                int index = (byte)definition.Terrain;
                Color tint = definition.Tint;
                tint.a *= definition.Opacity;
                if (!definition.IsUsable)
                    tint.a = 0f;
                tints[index] = tint;
                settings[index] = new Color(
                    definition.TileSizeMeters,
                    (float)definition.TransitionMode,
                    definition.TransitionWidthMultiplier,
                    definition.NoiseStrengthMultiplier);
            }
        }

        tintTexture = new Texture2D(
            PaletteSize,
            1,
            TextureFormat.RGBAFloat,
            false,
            true)
        {
            name = "Temporary Terrain Palette Tint",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        tintTexture.SetPixels(tints);
        tintTexture.Apply(false, true);

        settingsTexture = new Texture2D(
            PaletteSize,
            1,
            TextureFormat.RGBAFloat,
            false,
            true)
        {
            name = "Temporary Terrain Palette Settings",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        settingsTexture.SetPixels(settings);
        settingsTexture.Apply(false, true);
    }

    private static Texture2D BuildPairTexture(
        HeightMapLevelAsset level,
        byte[] typeMap,
        float maximumDistanceMeters)
    {
        int resolution = level.SurfaceMaskResolution;
        int pixelCount = resolution * resolution;
        var distances = new float[pixelCount];
        var secondaryTypes = new byte[pixelCount];
        for (int index = 0; index < pixelCount; index++)
            distances[index] = float.PositiveInfinity;

        Vector2 mapSize = level.MapSizeMeters;
        float pixelWidth = mapSize.x / resolution;
        float pixelHeight = mapSize.y / resolution;
        var heap = new DistanceHeap(Mathf.Max(1024, pixelCount / 8));

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = y * resolution + x;
                byte primaryType = typeMap[index];
                for (int neighbour = 0;
                     neighbour < NeighbourX.Length;
                     neighbour++)
                {
                    int neighbourX = x + NeighbourX[neighbour];
                    int neighbourY = y + NeighbourY[neighbour];
                    if (neighbourX < 0
                        || neighbourX >= resolution
                        || neighbourY < 0
                        || neighbourY >= resolution)
                    {
                        continue;
                    }

                    int neighbourIndex = neighbourY * resolution + neighbourX;
                    byte otherType = typeMap[neighbourIndex];
                    if (otherType == primaryType)
                        continue;

                    float deltaX = NeighbourX[neighbour] * pixelWidth;
                    float deltaY = NeighbourY[neighbour] * pixelHeight;
                    float candidateDistance = 0.5f * Mathf.Sqrt(
                        deltaX * deltaX + deltaY * deltaY);
                    if (!IsBetterPair(
                            candidateDistance,
                            otherType,
                            distances[index],
                            secondaryTypes[index]))
                    {
                        continue;
                    }

                    distances[index] = candidateDistance;
                    secondaryTypes[index] = otherType;
                    heap.Push(index, candidateDistance, otherType);
                }
            }
        }

        while (heap.Count > 0)
        {
            DistanceNode node = heap.Pop();
            if (node.Distance > maximumDistanceMeters)
                continue;
            if (node.Distance > distances[node.Index] + 0.0001f
                || secondaryTypes[node.Index] != node.SecondaryType)
            {
                continue;
            }

            int x = node.Index % resolution;
            int y = node.Index / resolution;
            byte primaryType = typeMap[node.Index];
            for (int neighbour = 0;
                 neighbour < NeighbourX.Length;
                 neighbour++)
            {
                int neighbourX = x + NeighbourX[neighbour];
                int neighbourY = y + NeighbourY[neighbour];
                if (neighbourX < 0
                    || neighbourX >= resolution
                    || neighbourY < 0
                    || neighbourY >= resolution)
                {
                    continue;
                }

                int neighbourIndex = neighbourY * resolution + neighbourX;
                if (typeMap[neighbourIndex] != primaryType)
                    continue;

                float deltaX = NeighbourX[neighbour] * pixelWidth;
                float deltaY = NeighbourY[neighbour] * pixelHeight;
                float candidateDistance = node.Distance + Mathf.Sqrt(
                    deltaX * deltaX + deltaY * deltaY);
                if (candidateDistance > maximumDistanceMeters
                    || !IsBetterPair(
                        candidateDistance,
                        node.SecondaryType,
                        distances[neighbourIndex],
                        secondaryTypes[neighbourIndex]))
                {
                    continue;
                }

                distances[neighbourIndex] = candidateDistance;
                secondaryTypes[neighbourIndex] = node.SecondaryType;
                heap.Push(
                    neighbourIndex,
                    candidateDistance,
                    node.SecondaryType);
            }
        }

        var pairPixels = new Color32[pixelCount];
        float inverseMaximumDistance = 1f / Mathf.Max(
            0.0001f,
            maximumDistanceMeters);
        for (int index = 0; index < pixelCount; index++)
        {
            bool hasPair = !float.IsInfinity(distances[index]);
            byte encodedDistance = hasPair
                ? (byte)Mathf.RoundToInt(Mathf.Clamp01(
                    distances[index] * inverseMaximumDistance) * 255f)
                : (byte)255;
            pairPixels[index] = new Color32(
                typeMap[index],
                secondaryTypes[index],
                encodedDistance,
                hasPair ? (byte)255 : (byte)0);
        }

        var pairTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = "Temporary Terrain Pair Distance Map",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        pairTexture.SetPixels32(pairPixels);
        pairTexture.Apply(false, true);
        return pairTexture;
    }

    private static bool IsBetterPair(
        float candidateDistance,
        byte candidateType,
        float currentDistance,
        byte currentType)
    {
        if (candidateDistance < currentDistance - 0.0001f)
            return true;

        return Mathf.Abs(candidateDistance - currentDistance) <= 0.0001f
               && candidateType < currentType;
    }

    private static float CalculateMaximumBoundaryDistance(
        HeightMapLevelAsset level)
    {
        float maximumWidthMultiplier = 1f;
        float maximumNoiseMultiplier = 1f;
        IReadOnlyList<TerrainSurfaceDefinition> palette = level.SurfacePalette;
        if (palette != null)
        {
            foreach (TerrainSurfaceDefinition definition in palette)
            {
                if (definition == null)
                    continue;

                maximumWidthMultiplier = Mathf.Max(
                    maximumWidthMultiplier,
                    definition.TransitionWidthMultiplier);
                maximumNoiseMultiplier = Mathf.Max(
                    maximumNoiseMultiplier,
                    definition.NoiseStrengthMultiplier);
            }
        }

        float pixelWidth = level.MapSizeMeters.x
                           / level.SurfaceMaskResolution;
        float pixelHeight = level.MapSizeMeters.y
                            / level.SurfaceMaskResolution;
        float pixelDiagonal = Mathf.Sqrt(
            pixelWidth * pixelWidth + pixelHeight * pixelHeight);
        return Mathf.Max(
            pixelDiagonal,
            level.SurfaceTransitionWidthMeters
            * 0.5f
            * maximumWidthMultiplier
            + level.SurfaceBoundaryNoiseAmplitudeMeters
            * maximumNoiseMultiplier
            * (1f + level.SurfaceScatterStrength * 0.35f)
            + pixelDiagonal);
    }

    private static void ConfigureBakedTextureImporter(
        string assetPath,
        int resolution)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath)
            as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.isReadable = false;
        // The map surface is already baked at its final display resolution.
        // Mip generation softens thin terrain marks and can create broken,
        // dirty-looking lines in low-alpha patterns.
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = Mathf.Clamp(
            Mathf.NextPowerOfTwo(resolution),
            256,
            8192);
        importer.SaveAndReimport();
    }

    private static void DestroyTemporary(UnityEngine.Object temporary)
    {
        if (temporary != null)
            UnityEngine.Object.DestroyImmediate(temporary);
    }

    private readonly struct DistanceNode
    {
        public readonly int Index;
        public readonly float Distance;
        public readonly byte SecondaryType;

        public DistanceNode(int index, float distance, byte secondaryType)
        {
            Index = index;
            Distance = distance;
            SecondaryType = secondaryType;
        }
    }

    private sealed class DistanceHeap
    {
        private DistanceNode[] nodes;

        public DistanceHeap(int initialCapacity)
        {
            nodes = new DistanceNode[Mathf.Max(4, initialCapacity)];
        }

        public int Count { get; private set; }

        public void Push(int index, float distance, byte secondaryType)
        {
            if (Count == nodes.Length)
                Array.Resize(ref nodes, nodes.Length * 2);

            int insertionIndex = Count++;
            var node = new DistanceNode(index, distance, secondaryType);
            while (insertionIndex > 0)
            {
                int parentIndex = (insertionIndex - 1) / 2;
                if (!ComesBefore(node, nodes[parentIndex]))
                    break;

                nodes[insertionIndex] = nodes[parentIndex];
                insertionIndex = parentIndex;
            }

            nodes[insertionIndex] = node;
        }

        public DistanceNode Pop()
        {
            DistanceNode result = nodes[0];
            DistanceNode tail = nodes[--Count];
            if (Count == 0)
                return result;

            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= Count)
                    break;

                int right = left + 1;
                int child = right < Count
                            && ComesBefore(nodes[right], nodes[left])
                    ? right
                    : left;
                if (!ComesBefore(nodes[child], tail))
                    break;

                nodes[index] = nodes[child];
                index = child;
            }

            nodes[index] = tail;
            return result;
        }

        private static bool ComesBefore(DistanceNode left, DistanceNode right)
        {
            if (left.Distance < right.Distance)
                return true;
            if (left.Distance > right.Distance)
                return false;
            if (left.SecondaryType < right.SecondaryType)
                return true;
            if (left.SecondaryType > right.SecondaryType)
                return false;
            return left.Index < right.Index;
        }
    }
}

/// <summary>
/// Builds a temporary, Editor-only alpha multiplier field from the fixed map's
/// smoothed height data. The result is consumed by the surface bake shader and
/// written permanently into the baked PNG; none of this work exists at runtime.
/// </summary>
internal static class StaticClosedContourAlphaBuilder
{
    private static readonly int[] CardinalX = { -1, 1, 0, 0 };
    private static readonly int[] CardinalY = { 0, 0, -1, 1 };
    private static readonly int[] NeighbourX =
        { -1, 0, 1, -1, 1, -1, 0, 1 };
    private static readonly int[] NeighbourY =
        { -1, -1, -1, 0, 0, 1, 1, 1 };

    public static Texture2D Build(HeightMapLevelAsset level)
    {
        if (level == null || !level.IsValid)
        {
            throw new InvalidOperationException(
                "Static closed-contour alpha requires a valid fixed height map.");
        }

        int resolution = Mathf.Clamp(
            level.SurfaceClosedContourAlphaResolution,
            128,
            1024);
        int pixelCount = resolution * resolution;
        var heights = new float[pixelCount];

        using (BakedHeightField heightField = BakedHeightField.Bake(
                   level.HeightMap,
                   resolution,
                   level.MapSizeMeters,
                   level.MinimumHeightMeters,
                   level.MaximumHeightMeters,
                   level.NormalizeSourceRange,
                   level.SurfaceSmoothingSigmaMeters))
        {
            for (int y = 0; y < resolution; y++)
            {
                int row = y * resolution;
                for (int x = 0; x < resolution; x++)
                {
                    heights[row + x] =
                        heightField.GetSurfaceHeightSample(x, y);
                }
            }
        }

        var smallestContainingArea = new float[pixelCount];
        var distanceWithinSmallestRegionMeters = new float[pixelCount];
        var maximumDistanceWithinSmallestRegionMeters =
            new float[pixelCount];
        var labels = new int[pixelCount];
        var floodQueue = new int[pixelCount];
        var distances = new float[pixelCount];
        var distanceHeap = new ContourDistanceHeap(
            Mathf.Max(1024, pixelCount / 8));
        for (int index = 0; index < pixelCount; index++)
            smallestContainingArea[index] = float.PositiveInfinity;

        float interval = Mathf.Max(0.0001f, level.ContourIntervalMeters);
        float minimumHeight = level.MinimumHeightMeters;
        float maximumHeight = level.MaximumHeightMeters;
        int contourCount = Mathf.FloorToInt(
            (maximumHeight - minimumHeight) / interval);
        float maximumThreshold = maximumHeight - 0.0001f;
        for (int contourIndex = 1;
             contourIndex <= contourCount;
             contourIndex++)
        {
            float threshold = minimumHeight + contourIndex * interval;
            if (threshold >= maximumThreshold)
                break;

            // A contour region can surround either a hill (highland component)
            // or a depression (lowland component), so both sides are indexed.
            // Lines reaching the rectangular map border are closed by that border.
            AccumulateClosedComponents(
                heights,
                resolution,
                level.MapSizeMeters,
                threshold,
                true,
                level.SurfaceClosedContourMinimumAreaSquareMeters,
                labels,
                floodQueue,
                distances,
                distanceHeap,
                smallestContainingArea,
                distanceWithinSmallestRegionMeters,
                maximumDistanceWithinSmallestRegionMeters);
            AccumulateClosedComponents(
                heights,
                resolution,
                level.MapSizeMeters,
                threshold,
                false,
                level.SurfaceClosedContourMinimumAreaSquareMeters,
                labels,
                floodQueue,
                distances,
                distanceHeap,
                smallestContainingArea,
                distanceWithinSmallestRegionMeters,
                maximumDistanceWithinSmallestRegionMeters);
        }

        float edgeMultiplier =
            level.SurfaceClosedContourEdgeAlphaMultiplier;
        float centreMultiplier =
            level.SurfaceClosedContourCenterAlphaMultiplier;
        float outsideMultiplier =
            level.SurfaceOutsideClosedContourAlphaMultiplier;
        float edgeHoldDistance = Mathf.Max(
            0f,
            level.SurfaceClosedContourEdgeHoldDistanceMeters);
        float configuredFadeDistance = Mathf.Max(
            0.1f,
            level.SurfaceClosedContourFadeDistanceMeters);
        float fadeStrength = Mathf.Max(
            0.1f,
            level.SurfaceClosedContourFadeStrength);
        var multipliers = new float[pixelCount];
        for (int index = 0; index < pixelCount; index++)
        {
            if (float.IsPositiveInfinity(smallestContainingArea[index]))
            {
                multipliers[index] = outsideMultiplier;
                continue;
            }

            float regionMaximumDistance =
                maximumDistanceWithinSmallestRegionMeters[index];
            if (regionMaximumDistance <= 0.0001f)
            {
                multipliers[index] = edgeMultiplier;
                continue;
            }

            // Large regions reach the centre multiplier at a fixed physical
            // distance, producing a truly transparent interior plateau. Small
            // regions use their own depth so their deepest point still fades
            // fully instead of disappearing as one uniformly bright patch.
            float effectiveFadeDistance = Mathf.Min(
                configuredFadeDistance,
                regionMaximumDistance);
            float effectiveHoldDistance = Mathf.Min(
                edgeHoldDistance,
                effectiveFadeDistance * 0.25f);
            float fadeDistanceRange = Mathf.Max(
                0.0001f,
                effectiveFadeDistance - effectiveHoldDistance);
            float normalizedDistance = Mathf.Clamp01(
                (distanceWithinSmallestRegionMeters[index]
                 - effectiveHoldDistance)
                / fadeDistanceRange);
            float smoothDistance = Mathf.SmoothStep(
                0f,
                1f,
                normalizedDistance);
            float fadeAmount = 1f - Mathf.Pow(
                1f - smoothDistance,
                fadeStrength);
            multipliers[index] = Mathf.Lerp(
                edgeMultiplier,
                centreMultiplier,
                fadeAmount);
        }

        var texture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RFloat,
            false,
            true)
        {
            name = "Temporary Static Closed-Contour Alpha",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixelData(multipliers, 0);
        texture.Apply(false, true);
        return texture;
    }

    private static void AccumulateClosedComponents(
        float[] heights,
        int resolution,
        Vector2 mapSizeMeters,
        float threshold,
        bool highland,
        float minimumAreaSquareMeters,
        int[] labels,
        int[] floodQueue,
        float[] distances,
        ContourDistanceHeap distanceHeap,
        float[] smallestContainingArea,
        float[] distanceWithinSmallestRegionMeters,
        float[] maximumDistanceWithinSmallestRegionMeters)
    {
        int pixelCount = heights.Length;
        for (int index = 0; index < pixelCount; index++)
            labels[index] = -1;

        var componentPixelCounts = new List<int>();
        var componentHasContourBoundary = new List<bool>();
        for (int seed = 0; seed < pixelCount; seed++)
        {
            if (labels[seed] >= 0
                || !IsInsideThreshold(heights[seed], threshold, highland))
            {
                continue;
            }

            int component = componentPixelCounts.Count;
            int head = 0;
            int tail = 0;
            int count = 0;
            bool hasContourBoundary = false;
            labels[seed] = component;
            floodQueue[tail++] = seed;

            while (head < tail)
            {
                int index = floodQueue[head++];
                count++;
                int x = index % resolution;
                int y = index / resolution;
                for (int neighbour = 0;
                     neighbour < CardinalX.Length;
                     neighbour++)
                {
                    int neighbourX = x + CardinalX[neighbour];
                    int neighbourY = y + CardinalY[neighbour];
                    if (neighbourX < 0
                        || neighbourX >= resolution
                        || neighbourY < 0
                        || neighbourY >= resolution)
                    {
                        continue;
                    }

                    int neighbourIndex =
                        neighbourY * resolution + neighbourX;
                    if (!IsInsideThreshold(
                            heights[neighbourIndex],
                            threshold,
                            highland))
                    {
                        hasContourBoundary = true;
                        continue;
                    }

                    if (labels[neighbourIndex] >= 0)
                    {
                        continue;
                    }

                    labels[neighbourIndex] = component;
                    floodQueue[tail++] = neighbourIndex;
                }
            }

            componentPixelCounts.Add(count);
            componentHasContourBoundary.Add(hasContourBoundary);
        }

        int componentCount = componentPixelCounts.Count;
        if (componentCount == 0)
            return;

        float pixelWidth = mapSizeMeters.x / resolution;
        float pixelHeight = mapSizeMeters.y / resolution;
        float pixelArea = pixelWidth * pixelHeight;
        var validComponents = new bool[componentCount];
        bool hasValidComponent = false;
        for (int component = 0; component < componentCount; component++)
        {
            float area = componentPixelCounts[component] * pixelArea;
            // The rectangular map boundary closes edge-reaching contours. A
            // real threshold transition is still required so a uniform map is
            // not mistaken for one giant contour region.
            bool valid = componentHasContourBoundary[component]
                         && area >= minimumAreaSquareMeters;
            validComponents[component] = valid;
            hasValidComponent |= valid;
        }

        if (!hasValidComponent)
            return;

        distanceHeap.Clear();
        for (int index = 0; index < pixelCount; index++)
            distances[index] = float.PositiveInfinity;

        // Every valid component boundary is a simultaneous source. Distance is
        // propagated only through pixels with the same component label.
        for (int index = 0; index < pixelCount; index++)
        {
            int component = labels[index];
            if (component < 0 || !validComponents[component])
                continue;

            int x = index % resolution;
            int y = index / resolution;
            bool isBoundary = false;
            for (int neighbour = 0;
                 neighbour < NeighbourX.Length;
                 neighbour++)
            {
                int neighbourX = x + NeighbourX[neighbour];
                int neighbourY = y + NeighbourY[neighbour];
                if (neighbourX < 0
                    || neighbourX >= resolution
                    || neighbourY < 0
                    || neighbourY >= resolution
                    || labels[neighbourY * resolution + neighbourX]
                    != component)
                {
                    isBoundary = true;
                    break;
                }
            }

            if (!isBoundary)
                continue;

            distances[index] = 0f;
            distanceHeap.Push(index, 0f);
        }

        while (distanceHeap.Count > 0)
        {
            ContourDistanceNode node = distanceHeap.Pop();
            if (node.Distance > distances[node.Index] + 0.0001f)
                continue;

            int component = labels[node.Index];
            int x = node.Index % resolution;
            int y = node.Index / resolution;
            for (int neighbour = 0;
                 neighbour < NeighbourX.Length;
                 neighbour++)
            {
                int neighbourX = x + NeighbourX[neighbour];
                int neighbourY = y + NeighbourY[neighbour];
                if (neighbourX < 0
                    || neighbourX >= resolution
                    || neighbourY < 0
                    || neighbourY >= resolution)
                {
                    continue;
                }

                int neighbourIndex =
                    neighbourY * resolution + neighbourX;
                if (labels[neighbourIndex] != component)
                    continue;

                float deltaX = NeighbourX[neighbour] * pixelWidth;
                float deltaY = NeighbourY[neighbour] * pixelHeight;
                float candidate = node.Distance + Mathf.Sqrt(
                    deltaX * deltaX + deltaY * deltaY);
                if (candidate >= distances[neighbourIndex] - 0.0001f)
                    continue;

                distances[neighbourIndex] = candidate;
                distanceHeap.Push(neighbourIndex, candidate);
            }
        }

        var componentMaximumDistances = new float[componentCount];
        for (int index = 0; index < pixelCount; index++)
        {
            int component = labels[index];
            if (component < 0 || !validComponents[component])
                continue;

            componentMaximumDistances[component] = Mathf.Max(
                componentMaximumDistances[component],
                distances[index]);
        }

        for (int index = 0; index < pixelCount; index++)
        {
            int component = labels[index];
            if (component < 0 || !validComponents[component])
                continue;

            float area = componentPixelCounts[component] * pixelArea;
            if (area >= smallestContainingArea[index] - 0.0001f)
                continue;

            float maximumDistance =
                componentMaximumDistances[component];
            smallestContainingArea[index] = area;
            distanceWithinSmallestRegionMeters[index] = distances[index];
            maximumDistanceWithinSmallestRegionMeters[index] =
                maximumDistance;
        }
    }

    private static bool IsInsideThreshold(
        float height,
        float threshold,
        bool highland)
    {
        return highland ? height >= threshold : height <= threshold;
    }

    private readonly struct ContourDistanceNode
    {
        public readonly int Index;
        public readonly float Distance;

        public ContourDistanceNode(int index, float distance)
        {
            Index = index;
            Distance = distance;
        }
    }

    private sealed class ContourDistanceHeap
    {
        private ContourDistanceNode[] nodes;

        public ContourDistanceHeap(int initialCapacity)
        {
            nodes = new ContourDistanceNode[
                Mathf.Max(4, initialCapacity)];
        }

        public int Count { get; private set; }

        public void Clear()
        {
            Count = 0;
        }

        public void Push(int index, float distance)
        {
            if (Count == nodes.Length)
                Array.Resize(ref nodes, nodes.Length * 2);

            int insertionIndex = Count++;
            var node = new ContourDistanceNode(index, distance);
            while (insertionIndex > 0)
            {
                int parentIndex = (insertionIndex - 1) / 2;
                if (!ComesBefore(node, nodes[parentIndex]))
                    break;

                nodes[insertionIndex] = nodes[parentIndex];
                insertionIndex = parentIndex;
            }

            nodes[insertionIndex] = node;
        }

        public ContourDistanceNode Pop()
        {
            ContourDistanceNode result = nodes[0];
            ContourDistanceNode tail = nodes[--Count];
            if (Count == 0)
                return result;

            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= Count)
                    break;

                int right = left + 1;
                int child = right < Count
                            && ComesBefore(nodes[right], nodes[left])
                    ? right
                    : left;
                if (!ComesBefore(nodes[child], tail))
                    break;

                nodes[index] = nodes[child];
                index = child;
            }

            nodes[index] = tail;
            return result;
        }

        private static bool ComesBefore(
            ContourDistanceNode left,
            ContourDistanceNode right)
        {
            if (left.Distance < right.Distance)
                return true;
            if (left.Distance > right.Distance)
                return false;
            return left.Index < right.Index;
        }
    }
}

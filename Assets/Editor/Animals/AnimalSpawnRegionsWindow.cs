using System;
using System.Collections.Generic;
using System.IO;
using AnimalGame.Animals;
using AnimalGame.MapTest;
using Clipper2Lib;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    public sealed class AnimalSpawnRegionsWindow : EditorWindow
    {
        private const float PickRadius = 8f;
        private static readonly Color OutlineColor = new(0.15f, 0.78f, 0.24f);
        private static readonly Color SelectedColor = new(0.58f, 1f, 0.62f);

        private readonly HashSet<int> expanded = new();
        private readonly List<Vector2> draftVertices = new();
        private MapTestSceneController map;
        [SerializeField] private TiledLevelAsset level;
        [SerializeField] private AnimalSpawnRegionsAsset source;
        [SerializeField] private AnimalSpawnRegionsAsset draft;
        private int editingEntry = -1;
        private bool drawingPolygon;
        private int selectedPolygon = -1;
        private Vector2 scrollPosition;

        [MenuItem("Animal Game/Level/Animal Spawn Regions")]
        private static void Open() => GetWindow<AnimalSpawnRegionsWindow>("Animal Spawn Regions");

        private void OnEnable()
        {
            saveChangesMessage = "Save your changes to Animal Spawn Regions?";
            RefreshUnsavedState();
            EditorApplication.hierarchyChanged += FindMap;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            SceneView.duringSceneGui += DrawScene;
            Undo.undoRedoPerformed += OnUndoRedo;
            FindMap();
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= FindMap;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            SceneView.duringSceneGui -= DrawScene;
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.RepaintAll();
        }

        private void OnDestroy()
        {
            DestroyDraft();
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            ClearDraft();
            FindMap();
            SceneView.RepaintAll();
        }

        private void FindMap()
        {
            Scene scene = SceneManager.GetActiveScene();
            map = null;
            foreach (var controller in FindObjectsByType<MapTestSceneController>(FindObjectsSortMode.InstanceID))
            {
                if (controller.gameObject.scene != scene)
                    continue;

                map = controller;
                break;
            }

            UpdateAsset();
            Repaint();
        }

        private void UpdateAsset()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            TiledLevelAsset currentLevel = map != null ? map.LevelAsset : null;
            AnimalSpawnRegionsAsset currentAsset = currentLevel != null ? currentLevel.AnimalSpawnRegions : null;
            if (level == currentLevel && source == currentAsset && (source == null || draft != null))
                return;

            if (hasUnsavedChanges && EditorUtility.DisplayDialog(
                "Animal Spawn Regions", saveChangesMessage, "Save", "Discard"))
                SaveChanges();

            DestroyDraft();
            level = currentLevel;
            source = currentAsset;
            if (source != null)
            {
                draft = Instantiate(source);
                draft.hideFlags = HideFlags.HideAndDontSave;
            }

            hasUnsavedChanges = false;
            expanded.Clear();
            ClearDraft();
            editingEntry = -1;
            selectedPolygon = -1;
            SceneView.RepaintAll();
        }

        private void DestroyDraft()
        {
            if (draft == null)
                return;

            Undo.ClearUndo(draft);
            DestroyImmediate(draft);
        }

        public override void SaveChanges()
        {
            Undo.FlushUndoRecordObjects();
            source = AnimalSpawnRegionsImporter.Save(AssetDatabase.GetAssetPath(source), draft);
            base.SaveChanges();
        }

        private void OnUndoRedo()
        {
            UpdateAsset();
            RefreshUnsavedState();
            if (draft != null && editingEntry >= draft.Regions.Count)
                editingEntry = -1;
            selectedPolygon = -1;
            Repaint();
            SceneView.RepaintAll();
        }

        private void RefreshUnsavedState()
        {
            if (source == null || draft == null)
                return;

            using var draftObject = new SerializedObject(draft);
            using var sourceObject = new SerializedObject(source);
            hasUnsavedChanges = !SerializedProperty.DataEquals(
                draftObject.FindProperty("speciesRegions"), sourceObject.FindProperty("speciesRegions"));
        }

        private void OnGUI()
        {
            UpdateAsset();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Exit Play Mode to edit spawn regions.", MessageType.Info);
                return;
            }

            if (level == null)
            {
                EditorGUILayout.HelpBox("Assign a Tiled Level Asset to the Map Test Scene Controller in the active scene.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Level", level, typeof(TiledLevelAsset), false);

            if (draft == null)
            {
                if (GUILayout.Button("Create Spawn Regions Asset"))
                    CreateAsset();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasUnsavedChanges || drawingPolygon))
                    if (GUILayout.Button("Save", GUILayout.Width(65)))
                        SaveChanges();

                if (hasUnsavedChanges)
                    GUILayout.Label("Unsaved changes", EditorStyles.miniLabel);
            }

            if (!map.HasGeneratedMap)
                EditorGUILayout.HelpBox("Generate the map to edit polygon vertices in the Scene view.", MessageType.Info);
            else if (drawingPolygon)
                EditorGUILayout.HelpBox("Click in the Scene view to place vertices, then click Finish.", MessageType.None);
            else if (selectedPolygon >= 0)
                EditorGUILayout.HelpBox("Drag a vertex to move it. Click an edge to insert a vertex. Ctrl/Cmd-click a vertex to remove it.", MessageType.None);

            using var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition);
            scrollPosition = scroll.scrollPosition;
            for (int i = 0; i < draft.Regions.Count; i++)
                DrawSpecies(i);

            using (new EditorGUI.DisabledScope(drawingPolygon))
            {
                if (GUILayout.Button("Add"))
                {
                    RecordEdit("Add Animal");
                    expanded.Add(draft.Regions.Count);
                    ArrayUtility.Add(ref draft.EditableRegions, new AnimalSpawnRegionsAsset.SpeciesRegions());
                }
            }
        }

        private void CreateAsset()
        {
            string levelPath = AssetDatabase.GetAssetPath(level);
            string directory = Path.GetDirectoryName(levelPath).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{Path.GetFileNameWithoutExtension(levelPath)} Animal Spawn Regions.{AnimalSpawnRegionsImporter.Extension}");
            var empty = CreateInstance<AnimalSpawnRegionsAsset>();
            AnimalSpawnRegionsAsset regions;
            try
            {
                regions = AnimalSpawnRegionsImporter.Save(path, empty);
            }
            finally
            {
                DestroyImmediate(empty);
            }

            using var levelObject = new SerializedObject(level);
            levelObject.FindProperty("animalSpawnRegions").objectReferenceValue = regions;
            levelObject.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(level);
            UpdateAsset();
            Selection.activeObject = regions;
        }

        private void DrawSpecies(int entry)
        {
            var regions = draft.Regions[entry];
            AnimalSpecies species = regions.species;
            bool isExpanded = expanded.Contains(entry);
            bool isDrawingForSpecies = drawingPolygon && editingEntry == entry;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool show;
                using (new EditorGUI.DisabledScope(isDrawingForSpecies))
                    show = EditorGUILayout.Foldout(isExpanded, ObjectNames.NicifyVariableName(species.ToString()), true);
                if (show != isExpanded)
                {
                    if (show)
                        expanded.Add(entry);
                    else
                        expanded.Remove(entry);
                }

                using (new EditorGUI.DisabledScope(drawingPolygon || editingEntry == entry))
                {
                    if (GUILayout.Button(editingEntry == entry ? "Editing" : "Edit", GUILayout.Width(60)))
                        SelectPolygon(entry, -1);
                }

                using (new EditorGUI.DisabledScope(drawingPolygon))
                {
                    if (GUILayout.Button("-", GUILayout.Width(24)))
                    {
                        RecordEdit("Remove Animal");
                        ArrayUtility.RemoveAt(ref draft.EditableRegions, entry);
                        for (int i = entry; i < draft.Regions.Count; i++)
                        {
                            expanded.Remove(i);
                            if (expanded.Contains(i + 1))
                                expanded.Add(i);
                        }
                        expanded.Remove(draft.Regions.Count);

                        if (editingEntry == entry)
                            SelectPolygon(-1, -1);
                        else if (editingEntry > entry)
                            editingEntry--;

                        GUIUtility.ExitGUI();
                    }
                }

                if (!show)
                    return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(drawingPolygon))
                {
                    var nextSpecies = (AnimalSpecies)EditorGUILayout.EnumPopup("Species", species);
                    if (nextSpecies != species)
                    {
                        if (nextSpecies != AnimalSpecies.Unknown && draft.GetRegions(nextSpecies) != null)
                            EditorUtility.DisplayDialog("Animal Spawn Regions",
                                $"An entry for {ObjectNames.NicifyVariableName(nextSpecies.ToString())} already exists.", "OK");
                        else
                        {
                            RecordEdit("Change Animal Species");
                            regions.species = nextSpecies;
                        }
                    }
                }

                for (int index = 0; index < regions.polygons.Length; index++)
                {
                    using var row = new EditorGUILayout.HorizontalScope();
                    using var disabled = new EditorGUI.DisabledScope(drawingPolygon);
                    bool selected = editingEntry == entry && selectedPolygon == index;
                    if (GUILayout.Toggle(selected, $"Polygon {index + 1}", EditorStyles.miniButton) && !selected)
                        SelectPolygon(entry, index);

                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        RecordEdit("Remove Spawn Polygon");
                        ArrayUtility.RemoveAt(ref regions.polygons, index);
                        if (editingEntry == entry && selectedPolygon >= index)
                            SelectPolygon(entry, selectedPolygon == index ? -1 : selectedPolygon - 1);
                        return;
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!isDrawingForSpecies && (drawingPolygon || !map.HasGeneratedMap)))
                    {
                        if (GUILayout.Button(isDrawingForSpecies ? "Finish" : "Add Polygon"))
                        {
                            if (isDrawingForSpecies)
                                FinishPolygon(regions);
                            else
                            {
                                drawingPolygon = true;
                                SelectPolygon(entry, -1);
                            }
                        }
                    }

                    if (!isDrawingForSpecies)
                    {
                        using var disabled = new EditorGUI.DisabledScope(drawingPolygon);
                        if (GUILayout.Button(new GUIContent("⋯", "Polygon actions"), GUILayout.Width(26)))
                            ShowPolygonMenu(entry, regions);
                    }
                }
            }
        }

        private void ShowPolygonMenu(int entry, AnimalSpawnRegionsAsset.SpeciesRegions regions)
        {
            var menu = new GenericMenu();
            var intersect = new GUIContent("Calc Intersect");
            int index = editingEntry == entry ? selectedPolygon : -1;
            if (index >= 0 && index < regions.polygons.Length)
                menu.AddItem(intersect, false, () => IntersectPolygons(regions, index));
            else
                menu.AddDisabledItem(intersect);

            var intersectAll = new GUIContent("Calc Intersect All");
            if (regions.polygons.Length > 0)
                menu.AddItem(intersectAll, false, () => IntersectPolygons(regions, -1));
            else
                menu.AddDisabledItem(intersectAll);

            var altitude = new GUIContent("Calc Altitude Intersect");
            var altitudeAll = new GUIContent("Calc Altitude Intersect All");
            bool ready = map != null && map.HasGeneratedMap && !map.HasPendingHeightLoads;
            if (ready && index >= 0 && index < regions.polygons.Length)
                menu.AddItem(altitude, false, () => IntersectPolygons(regions, index, true));
            else menu.AddDisabledItem(altitude);
            if (ready && regions.polygons.Length > 0)
                menu.AddItem(altitudeAll, false, () => IntersectPolygons(regions, -1, true));
            else menu.AddDisabledItem(altitudeAll);
            menu.ShowAsContext();
        }

        private void IntersectPolygons(AnimalSpawnRegionsAsset.SpeciesRegions regions, int targetIndex, bool altitude = false)
        {
            AnimalInfoLibrary.Active.BySpecies.TryGetValue(regions.species, out var entry);
            AnimalSpawnInfo spawnInfo = entry?.spawnInfo;
            if (spawnInfo == null)
            {
                EditorUtility.DisplayDialog("Calc Intersect",
                    $"Assign Spawn Info for {ObjectNames.NicifyVariableName(regions.species.ToString())}.", "OK");
                return;
            }
            if (!altitude && !spawnInfo.IsAquatic && spawnInfo.AllowedTerrains.Count == 0)
            {
                EditorUtility.DisplayDialog("Calc Intersect",
                    $"Set Allowed Terrains in the Spawn Info for {ObjectNames.NicifyVariableName(regions.species.ToString())}.", "OK");
                return;
            }

            TerrainRegionsPrebake terrain = altitude ? null : level.LoadPrebakedTerrainRegions();
            if (!altitude && (terrain == null || !terrain.IncludesWaterMask))
            {
                EditorUtility.DisplayDialog("Calc Intersect",
                    "Run Update Terrain Regions for this Tiled Level in the Surface Painter to bake the current land and water regions.", "OK");
                return;
            }

            Paths64 clip = altitude ? null : BuildTerrainClip(terrain, spawnInfo);
            var result = new List<AnimalSpawnRegionsAsset.Polygon>(regions.polygons.Length);
            int nextSelection = -1;
            for (int i = 0; i < regions.polygons.Length; i++)
            {
                int firstResult = result.Count;
                if (targetIndex < 0 || i == targetIndex)
                {
                    var polygon = regions.polygons[i];
                    AppendIntersection(polygon, altitude ? BuildAltitudeClip(map, polygon, spawnInfo) : clip, result);
                }
                else
                    result.Add(regions.polygons[i]);

                if (editingEntry >= 0 && draft.Regions[editingEntry] == regions &&
                    i == selectedPolygon && result.Count > firstResult)
                    nextSelection = firstResult;
            }

            RecordEdit(targetIndex < 0 ? "Intersect All Spawn Polygons" : "Intersect Spawn Polygon");
            regions.polygons = result.ToArray();
            if (editingEntry >= 0 && draft.Regions[editingEntry] == regions)
                selectedPolygon = nextSelection;
            Repaint();
        }

        private const int AltitudeCellSize = 1;
        private const float AltitudeBoundaryTolerance = 2f;
        private const float MinimumAltitudeRegionArea = 16f;

        internal static Paths64 BuildAltitudeClip(MapTestSceneController map, AnimalSpawnRegionsAsset.Polygon polygon, AnimalSpawnInfo info)
        {
            var strips = new Paths64();
            if (polygon.vertices.Length < 3) return strips;
            Vector2 min = polygon.vertices[0], max = min;
            foreach (var vertex in polygon.vertices)
            {
                min = Vector2.Min(min, vertex);
                max = Vector2.Max(max, vertex);
            }
            int x0 = Mathf.Max(0, Mathf.FloorToInt(min.x / AltitudeCellSize));
            int x1 = Mathf.CeilToInt(Mathf.Min(map.MapSizeMeters.x, max.x) / AltitudeCellSize);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(min.y / AltitudeCellSize));
            int y1 = Mathf.CeilToInt(Mathf.Min(map.MapSizeMeters.y, max.y) / AltitudeCellSize);
            double units = TerrainRegionsBaker.UnitsPerMeter * AltitudeCellSize;
            for (int y = y0; y < y1; y++)
            {
                int run = x0;
                for (int x = x0; x <= x1; x++)
                {
                    if (x < x1 && map.TrySampleMapPosition(new Vector2(x + .5f, y + .5f) * AltitudeCellSize,
                        out float height) && height >= info.MinimumAltitude && height <= info.MaximumAltitude)
                        continue;
                    if (run < x)
                        strips.Add(new Path64 { new Point64(run * units, y * units), new Point64(x * units, y * units),
                            new Point64(x * units, (y + 1) * units), new Point64(run * units, (y + 1) * units) });
                    run = x + 1;
                }
            }
            var merged = Clipper.Union(strips, FillRule.NonZero);
            var simplified = Clipper.SimplifyPaths(merged, AltitudeBoundaryTolerance * TerrainRegionsBaker.UnitsPerMeter);
            var tree = new PolyTree64();
            var clipper = new Clipper64();
            clipper.AddSubject(simplified);
            clipper.Execute(ClipType.Union, FillRule.NonZero, tree);
            var result = new Paths64();
            CollectAltitudeRegions(tree, result);
            return result;
        }

        private static void CollectAltitudeRegions(PolyPath64 parent, Paths64 result)
        {
            for (int i = 0; i < parent.Count; i++)
            {
                var outer = parent[i];
                double area = Math.Abs(Clipper.Area(outer.Polygon));
                for (int j = 0; j < outer.Count; j++) area -= Math.Abs(Clipper.Area(outer[j].Polygon));
                if (area >= MinimumAltitudeRegionArea * TerrainRegionsBaker.UnitsPerMeter * TerrainRegionsBaker.UnitsPerMeter)
                {
                    result.Add(outer.Polygon);
                    for (int j = 0; j < outer.Count; j++) result.Add(outer[j].Polygon);
                }
                for (int j = 0; j < outer.Count; j++) CollectAltitudeRegions(outer[j], result);
            }
        }

        private static Paths64 BuildTerrainClip(TerrainRegionsPrebake terrain, AnimalSpawnInfo spawnInfo)
        {
            var clip = new Paths64();
            foreach (var region in terrain.Regions)
            {
                bool allowed = spawnInfo.IsAquatic ? region.Terrain == TerrainKind.Water
                    : spawnInfo.AllowedTerrains.Contains(region.Terrain);
                if (!allowed)
                    continue;

                foreach (var polygon in region.Polygons)
                {
                    clip.Add(ToClipperPath(polygon.Outer, isHole: false));
                    foreach (Vector2[] hole in polygon.Holes)
                        clip.Add(ToClipperPath(hole, isHole: true));
                }
            }
            return clip;
        }

        private static void AppendIntersection(AnimalSpawnRegionsAsset.Polygon polygon, Paths64 clip,
            List<AnimalSpawnRegionsAsset.Polygon> result)
        {
            if (polygon.vertices.Length < 3 || clip.Count == 0)
                return;

            var clipper = new Clipper64 { PreserveCollinear = false };
            clipper.AddSubject(ToClipperPath(polygon.vertices, isHole: false));
            foreach (var hole in polygon.holes)
                clipper.AddSubject(ToClipperPath(hole.vertices, isHole: true));

            // NonZero treats all allowed terrain rings as one filled area, including shared borders.
            clipper.AddClip(clip);
            var tree = new PolyTree64();
            if (!clipper.Execute(ClipType.Intersection, FillRule.NonZero, tree))
                throw new InvalidOperationException("Could not intersect the spawn polygon with terrain regions.");

            AppendPolygons(tree, result);
        }

        private static Path64 ToClipperPath(Vector2[] ring, bool isHole)
        {
            var path = new Path64(ring.Length);
            foreach (Vector2 point in ring)
                path.Add(new Point64(point.x * TerrainRegionsBaker.UnitsPerMeter,
                    point.y * TerrainRegionsBaker.UnitsPerMeter));

            if (Clipper.IsPositive(path) == isHole)
                path.Reverse();
            return path;
        }

        private static void AppendPolygons(PolyPath64 parent, List<AnimalSpawnRegionsAsset.Polygon> result)
        {
            for (int i = 0; i < parent.Count; i++)
            {
                PolyPath64 outer = parent[i];
                var polygon = new AnimalSpawnRegionsAsset.Polygon
                {
                    vertices = TerrainRegionsBaker.InMeters(outer.Polygon),
                    holes = new AnimalSpawnRegionsAsset.Ring[outer.Count]
                };
                result.Add(polygon);
                for (int j = 0; j < outer.Count; j++)
                {
                    PolyPath64 hole = outer[j];
                    polygon.holes[j] = new AnimalSpawnRegionsAsset.Ring
                    {
                        vertices = TerrainRegionsBaker.InMeters(hole.Polygon)
                    };
                    AppendPolygons(hole, result);
                }
            }
        }

        private void FinishPolygon(AnimalSpawnRegionsAsset.SpeciesRegions regions)
        {
            if (draftVertices.Count > 0)
            {
                RecordEdit("Add Spawn Polygon");
                ArrayUtility.Add(ref regions.polygons, new AnimalSpawnRegionsAsset.Polygon
                {
                    vertices = draftVertices.ToArray()
                });
                selectedPolygon = regions.polygons.Length - 1;
            }

            ClearDraft();
            Repaint();
            SceneView.RepaintAll();
        }

        private void ClearDraft()
        {
            drawingPolygon = false;
            draftVertices.Clear();
        }

        private void SelectPolygon(int entry, int index)
        {
            editingEntry = entry;
            selectedPolygon = index;
            Repaint();
            SceneView.RepaintAll();
        }

        private void RecordEdit(string action)
        {
            Undo.RecordObject(draft, action);
            hasUnsavedChanges = true;
            Repaint();
            SceneView.RepaintAll();
        }

        private void DrawScene(SceneView view)
        {
            UpdateAsset();
            if (editingEntry < 0 || map == null || !map.HasGeneratedMap ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var regions = draft.Regions[editingEntry];
            var polygons = regions.polygons;
            if (selectedPolygon >= polygons.Length)
                selectedPolygon = -1;

            Event current = Event.current;
            int pickControl = GUIUtility.GetControlID(FocusType.Passive);
            bool removeVertex = (current.control || current.command) && GUIUtility.hotControl == 0;
            float closestDistance = PickRadius;
            int pickedPolygon = -1;
            int pickedRing = -1;
            int pickedPoint = -1;
            Vector3 edgeStart = default;
            Vector3 edgeEnd = default;

            for (int index = 0; index < polygons.Length; index++)
            {
                bool selected = index == selectedPolygon;
                var polygon = polygons[index];
                using var outline = new Handles.DrawingScope(selected ? SelectedColor : OutlineColor);
                for (int ring = 0; ring <= polygon.holes.Length; ring++)
                {
                    Vector2[] vertices = polygon.GetRing(ring);
                    for (int point = 0; point < vertices.Length; point++)
                    {
                        Vector3 start = map.MapPositionToWorld(vertices[point]);
                        Vector3 end = map.MapPositionToWorld(vertices[(point + 1) % vertices.Length]);
                        if (current.type == EventType.Repaint)
                        {
                            if (vertices.Length == 1)
                                DrawPoint(start);
                            else
                                Handles.DrawLine(start, end, selected ? 3f : 2f);
                        }

                        if (drawingPolygon || current.alt || (removeVertex && (!selected || vertices.Length <= 3)))
                            continue;

                        float distance = removeVertex || vertices.Length == 1
                            ? Vector2.Distance(current.mousePosition, HandleUtility.WorldToGUIPoint(start))
                            : HandleUtility.DistanceToLine(start, end);
                        if (distance > closestDistance || (distance == closestDistance && !selected))
                            continue;

                        closestDistance = distance;
                        pickedPolygon = index;
                        pickedRing = ring;
                        pickedPoint = point;
                        edgeStart = start;
                        edgeEnd = end;
                    }
                }
            }

            if (drawingPolygon)
            {
                DrawDraft(view, current, pickControl);
                return;
            }

            if (pickedPolygon >= 0 && current.type == EventType.Layout)
            {
                // Keep the outline within Unity's pick threshold while preferring vertex handles.
                HandleUtility.AddControl(pickControl, Mathf.Lerp(3f, 4.9f, closestDistance / PickRadius));
            }

            if (selectedPolygon >= 0 && !removeVertex)
            {
                var polygon = polygons[selectedPolygon];
                using var vertices = new Handles.DrawingScope(SelectedColor);
                for (int ring = 0; ring <= polygon.holes.Length; ring++)
                    DrawVertexHandles(polygon.GetRing(ring));
            }

            if (pickedPolygon < 0 || GUIUtility.hotControl != 0 ||
                HandleUtility.nearestControl != pickControl)
                return;

            Vector3 edgePoint = edgeStart;
            if (!removeVertex && pickedPolygon == selectedPolygon)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
                var plane = new Plane(Vector3.forward, edgeStart);
                if (!plane.Raycast(ray, out float distance))
                    return;

                Vector3 edge = edgeEnd - edgeStart;
                if (edge.sqrMagnitude == 0f)
                    return;

                float fraction = Vector3.Dot(ray.GetPoint(distance) - edgeStart, edge) / edge.sqrMagnitude;
                if (fraction <= 0f || fraction >= 1f)
                    return;

                edgePoint += fraction * edge;
            }

            if (current.type == EventType.Repaint && pickedPolygon == selectedPolygon)
            {
                using var highlight = new Handles.DrawingScope(removeVertex ? Color.red : SelectedColor);
                Handles.DotHandleCap(pickControl, edgePoint, Quaternion.identity,
                    HandleUtility.GetHandleSize(edgePoint) * 0.06f, EventType.Repaint);
            }

            if (current.type == EventType.MouseMove)
                view.Repaint();

            if (current.type != EventType.MouseDown || current.button != 0)
                return;

            if (pickedPolygon != selectedPolygon)
                SelectPolygon(editingEntry, pickedPolygon);
            else if (removeVertex)
            {
                RecordEdit("Remove Spawn Polygon Vertex");
                ArrayUtility.RemoveAt(ref polygons[pickedPolygon].GetRing(pickedRing), pickedPoint);
            }
            else
            {
                RecordEdit("Insert Spawn Polygon Vertex");
                ArrayUtility.Insert(ref polygons[pickedPolygon].GetRing(pickedRing), pickedPoint + 1, WorldToMap(edgePoint));
            }
            current.Use();
        }

        private void DrawDraft(SceneView view, Event current, int control)
        {
            if (current.type == EventType.Layout && !current.alt)
                HandleUtility.AddDefaultControl(control);
            if (current.type == EventType.MouseMove)
                view.Repaint();

            bool placeVertex = current.type == EventType.MouseDown && current.button == 0 &&
                !current.alt && GUIUtility.hotControl == 0 && HandleUtility.nearestControl == control;
            if (!placeVertex && current.type != EventType.Repaint)
                return;
            if (!placeVertex && draftVertices.Count == 0)
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            var plane = new Plane(Vector3.forward, map.WorldBounds.center);
            bool hitMapPlane = plane.Raycast(ray, out float distance);
            if (placeVertex)
            {
                if (hitMapPlane)
                {
                    draftVertices.Add(WorldToMap(ray.GetPoint(distance)));
                    current.Use();
                    view.Repaint();
                }
                return;
            }

            using var preview = new Handles.DrawingScope(SelectedColor);
            Vector3 first = map.MapPositionToWorld(draftVertices[0]);
            Vector3 previous = first;
            DrawPoint(first);
            for (int i = 1; i < draftVertices.Count; i++)
            {
                Vector3 point = map.MapPositionToWorld(draftVertices[i]);
                Handles.DrawLine(previous, point, 3f);
                DrawPoint(point);
                previous = point;
            }

            if (hitMapPlane)
            {
                Vector3 cursor = map.MapPositionToWorld(WorldToMap(ray.GetPoint(distance)));
                Handles.DrawLine(previous, cursor, 2f);
                DrawPoint(cursor);
                previous = cursor;
            }

            int previewVertexCount = draftVertices.Count + (hitMapPlane ? 1 : 0);
            if (previewVertexCount >= 3)
                Handles.DrawLine(previous, first, 2f);
        }

        private static void DrawPoint(Vector3 point)
        {
            Handles.DotHandleCap(0, point, Quaternion.identity,
                HandleUtility.GetHandleSize(point) * 0.055f, EventType.Repaint);
        }

        private void DrawVertexHandles(Vector2[] vertices)
        {
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 position = map.MapPositionToWorld(vertices[index]);
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.Slider2D(position, Vector3.forward, Vector3.right, Vector3.up,
                    HandleUtility.GetHandleSize(position) * 0.055f, Handles.DotHandleCap, 0f);
                if (!EditorGUI.EndChangeCheck())
                    continue;

                Vector2 mapPosition = WorldToMap(moved);
                if (mapPosition == vertices[index])
                    continue;

                RecordEdit("Move Spawn Polygon Vertex");
                vertices[index] = mapPosition;
            }
        }

        private Vector2 WorldToMap(Vector3 worldPosition)
        {
            Bounds bounds = map.WorldBounds;
            var normalized = new Vector2(
                Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPosition.x),
                Mathf.InverseLerp(bounds.min.y, bounds.max.y, worldPosition.y));
            return Vector2.Scale(normalized, map.MapSizeMeters);
        }
    }
}

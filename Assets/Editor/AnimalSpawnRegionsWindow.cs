using System.Collections.Generic;
using System.IO;
using AnimalGame.Animals;
using AnimalGame.MapTest;
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

        private readonly HashSet<AnimalSpecies> expanded = new();
        private readonly List<Vector2> draftVertices = new();
        private MapTestSceneController map;
        private TiledLevelAsset level;
        private AnimalSpawnRegionsAsset asset;
        private AnimalSpecies editingSpecies;
        private bool drawingPolygon;
        private int selectedPolygon = -1;
        private Vector2 scrollPosition;

        [MenuItem("Animal Game/Level/Animal Spawn Regions")]
        private static void Open() => GetWindow<AnimalSpawnRegionsWindow>("Animal Spawn Regions");

        private void OnEnable()
        {
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
            TiledLevelAsset currentLevel = map != null ? map.LevelAsset : null;
            AnimalSpawnRegionsAsset currentAsset = currentLevel != null ? currentLevel.AnimalSpawnRegions : null;
            if (level == currentLevel && asset == currentAsset)
                return;

            level = currentLevel;
            asset = currentAsset;
            ClearDraft();
            editingSpecies = AnimalSpecies.Unknown;
            selectedPolygon = -1;
            SceneView.RepaintAll();
        }

        private void OnUndoRedo()
        {
            UpdateAsset();
            selectedPolygon = -1;
            Repaint();
            SceneView.RepaintAll();
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

            if (asset == null)
            {
                if (GUILayout.Button("Create Spawn Regions Asset"))
                    CreateAsset();
                return;
            }

            if (asset.Regions.Count == 0)
            {
                EditorGUILayout.HelpBox("Add a Species Regions entry and assign its Species in the asset Inspector.", MessageType.Info);
                return;
            }

            if (!map.HasGeneratedMap)
                EditorGUILayout.HelpBox("Generate the map to edit polygon vertices in the Scene view.", MessageType.Info);
            else if (drawingPolygon)
                EditorGUILayout.HelpBox("Click in the Scene view to place vertices, then click Finish.", MessageType.None);
            else if (selectedPolygon >= 0)
                EditorGUILayout.HelpBox("Drag a vertex to move it. Click an edge to insert a vertex. Ctrl/Cmd-click a vertex to remove it.", MessageType.None);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (var regions in asset.Regions)
            {
                if (regions.species == AnimalSpecies.Unknown)
                    EditorGUILayout.HelpBox("Assign a species to each entry in the asset Inspector.", MessageType.Warning);
                else if (asset.GetRegions(regions.species) != regions)
                    EditorGUILayout.HelpBox($"Keep one entry for {ObjectNames.NicifyVariableName(regions.species.ToString())} in the asset Inspector.", MessageType.Warning);
                else
                    DrawSpecies(regions);
            }
            EditorGUILayout.EndScrollView();
        }

        private void CreateAsset()
        {
            string levelPath = AssetDatabase.GetAssetPath(level);
            string directory = Path.GetDirectoryName(levelPath).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{Path.GetFileNameWithoutExtension(levelPath)} Animal Spawn Regions.asset");
            var regions = CreateInstance<AnimalSpawnRegionsAsset>();
            AssetDatabase.CreateAsset(regions, path);

            using var levelObject = new SerializedObject(level);
            levelObject.FindProperty("animalSpawnRegions").objectReferenceValue = regions;
            levelObject.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(regions);
            AssetDatabase.SaveAssetIfDirty(level);
            UpdateAsset();
            Selection.activeObject = regions;
        }

        private void DrawSpecies(AnimalSpawnRegionsAsset.SpeciesRegions regions)
        {
            AnimalSpecies species = regions.species;
            bool isExpanded = expanded.Contains(species);
            bool isDrawingForSpecies = drawingPolygon && editingSpecies == species;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool show;
                using (new EditorGUI.DisabledScope(isDrawingForSpecies))
                    show = EditorGUILayout.Foldout(isExpanded, ObjectNames.NicifyVariableName(species.ToString()), true);
                if (show != isExpanded)
                {
                    if (show)
                        expanded.Add(species);
                    else
                        expanded.Remove(species);
                }

                using (new EditorGUI.DisabledScope(drawingPolygon || editingSpecies == species))
                {
                    if (GUILayout.Button(editingSpecies == species ? "Editing" : "Edit", GUILayout.Width(60)))
                        SelectPolygon(species, -1);
                }

                if (!show)
                    return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int index = 0; index < regions.polygons.Length; index++)
                {
                    using var row = new EditorGUILayout.HorizontalScope();
                    using var disabled = new EditorGUI.DisabledScope(drawingPolygon);
                    bool selected = editingSpecies == species && selectedPolygon == index;
                    if (GUILayout.Toggle(selected, $"Polygon {index + 1}", EditorStyles.miniButton) && !selected)
                        SelectPolygon(species, index);

                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        RecordEdit("Remove Spawn Polygon");
                        ArrayUtility.RemoveAt(ref regions.polygons, index);
                        if (editingSpecies == species && selectedPolygon >= index)
                            SelectPolygon(species, selectedPolygon == index ? -1 : selectedPolygon - 1);
                        return;
                    }
                }

                using (new EditorGUI.DisabledScope(!isDrawingForSpecies && (drawingPolygon || !map.HasGeneratedMap)))
                {
                    if (GUILayout.Button(isDrawingForSpecies ? "Finish" : "Add Polygon"))
                    {
                        if (isDrawingForSpecies)
                            FinishPolygon(regions);
                        else
                        {
                            drawingPolygon = true;
                            SelectPolygon(species, -1);
                        }
                    }
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

        private void SelectPolygon(AnimalSpecies species, int index)
        {
            editingSpecies = species;
            selectedPolygon = index;
            Repaint();
            SceneView.RepaintAll();
        }

        private void RecordEdit(string action)
        {
            Undo.RecordObject(asset, action);
            EditorUtility.SetDirty(asset);
            SceneView.RepaintAll();
        }

        private void DrawScene(SceneView view)
        {
            UpdateAsset();
            if (editingSpecies == AnimalSpecies.Unknown || map == null || !map.HasGeneratedMap ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var regions = asset.GetRegions(editingSpecies);
            if (regions == null)
            {
                ClearDraft();
                SelectPolygon(AnimalSpecies.Unknown, -1);
                return;
            }

            var polygons = regions.polygons;
            if (selectedPolygon >= polygons.Length)
                selectedPolygon = -1;

            Event current = Event.current;
            int pickControl = GUIUtility.GetControlID(FocusType.Passive);
            bool removeVertex = (current.control || current.command) && GUIUtility.hotControl == 0;
            float closestDistance = PickRadius;
            int pickedPolygon = -1;
            int pickedPoint = -1;
            Vector3 edgeStart = default;
            Vector3 edgeEnd = default;

            for (int index = 0; index < polygons.Length; index++)
            {
                bool selected = index == selectedPolygon;
                Vector2[] vertices = polygons[index].vertices;
                using var outline = new Handles.DrawingScope(selected ? SelectedColor : OutlineColor);
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
                    pickedPoint = point;
                    edgeStart = start;
                    edgeEnd = end;
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
                DrawVertexHandles(polygons[selectedPolygon]);

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
                SelectPolygon(editingSpecies, pickedPolygon);
            else if (removeVertex)
            {
                RecordEdit("Remove Spawn Polygon Vertex");
                ArrayUtility.RemoveAt(ref polygons[pickedPolygon].vertices, pickedPoint);
            }
            else
            {
                RecordEdit("Insert Spawn Polygon Vertex");
                ArrayUtility.Insert(ref polygons[pickedPolygon].vertices, pickedPoint + 1, WorldToMap(edgePoint));
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

        private void DrawVertexHandles(AnimalSpawnRegionsAsset.Polygon polygon)
        {
            using var vertices = new Handles.DrawingScope(SelectedColor);
            for (int index = 0; index < polygon.vertices.Length; index++)
            {
                Vector3 position = map.MapPositionToWorld(polygon.vertices[index]);
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.Slider2D(position, Vector3.forward, Vector3.right, Vector3.up,
                    HandleUtility.GetHandleSize(position) * 0.055f, Handles.DotHandleCap, 0f);
                if (!EditorGUI.EndChangeCheck())
                    continue;

                Vector2 mapPosition = WorldToMap(moved);
                if (mapPosition == polygon.vertices[index])
                    continue;

                RecordEdit("Move Spawn Polygon Vertex");
                polygon.vertices[index] = mapPosition;
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

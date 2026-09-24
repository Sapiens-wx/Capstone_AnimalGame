using AnimalGame.Animals;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.EditorTools
{
    public sealed class AnimalPhotoLibraryWindow : EditorWindow
    {
        private AnimalPhotoLibrary library;
        private SerializedObject serializedLibrary;
        private Vector2 scroll;
        private int selectedState = -1, selectedPhoto = -1;
        private RenderTexture preview, cropPreview;
        private Texture2D previewSource;
        private float previewSaturation = -1;
        private Rect previewSubject, randomCrop;
        private bool dragging;
        private Vector2 dragStart;
        private string previewError;

        [MenuItem("Animal Game/Animals/Animal Photo Library")]
        public static void Open()
        {
            GetWindow<AnimalPhotoLibraryWindow>("Animal Photos");
        }

        private void OnEnable()
        {
            minSize = new Vector2(480, 500);
            Undo.undoRedoPerformed += Refresh;
            OnSelectionChange();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Refresh;
            ClearPreview();
        }

        private void Refresh()
        {
            ClearPreview();
            Repaint();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is AnimalPhotoLibrary selected) SetLibrary(selected);
        }

        private void SetLibrary(AnimalPhotoLibrary value)
        {
            library = value;
            serializedLibrary = value != null ? new SerializedObject(value) : null;
            selectedState = selectedPhoto = -1;
            dragging = false;
            Refresh();
        }

        private void OnGUI()
        {
            var next = (AnimalPhotoLibrary)EditorGUILayout.ObjectField("Animal library", library,
                typeof(AnimalPhotoLibrary), false);
            if (next != library) SetLibrary(next);
            if (library == null)
            {
                EditorGUILayout.HelpBox("Select an AnimalPhotoLibrary asset, or create one via Assets > Create > Animal Game > Photos.", MessageType.Info);
                return;
            }

            serializedLibrary.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.PropertyField(serializedLibrary.FindProperty("species"));
            SerializedProperty states = serializedLibrary.FindProperty("states");
            for (int s = 0; s < states.arraySize; s++)
            {
                SerializedProperty group = states.GetArrayElementAtIndex(s);
                SerializedProperty state = group.FindPropertyRelative("state");
                SerializedProperty photos = group.FindPropertyRelative("photos");
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                group.isExpanded = EditorGUILayout.Foldout(group.isExpanded,
                    $"{state.enumDisplayNames[state.enumValueIndex]} ({photos.arraySize})", true);
                if (group.isExpanded)
                {
                    EditorGUILayout.PropertyField(state);
                    for (int p = 0; p < photos.arraySize; p++)
                    {
                        SerializedProperty entry = photos.GetArrayElementAtIndex(p);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("image"), GUIContent.none);
                        if (GUILayout.Button(selectedState == s && selectedPhoto == p ? "Editing" : "Edit", GUILayout.Width(65)))
                        {
                            selectedState = s;
                            selectedPhoto = p;
                            Refresh();
                        }
                        bool remove = GUILayout.Button("−", GUILayout.Width(25));
                        EditorGUILayout.EndHorizontal();
                        if (remove)
                        {
                            photos.DeleteArrayElementAtIndex(p);
                            selectedState = selectedPhoto = -1;
                            Refresh();
                            break;
                        }
                    }
                    if (GUILayout.Button("Add photo"))
                    {
                        int index = photos.arraySize++;
                        SerializedProperty added = photos.GetArrayElementAtIndex(index);
                        added.FindPropertyRelative("image").objectReferenceValue = null;
                        added.FindPropertyRelative("subjectRect").rectValue = new Rect(0, 0, 1, 1);
                        added.FindPropertyRelative("saturation").floatValue = 1;
                        selectedState = s;
                        selectedPhoto = index;
                        Refresh();
                    }
                    if (GUILayout.Button("Remove state"))
                    {
                        states.DeleteArrayElementAtIndex(s);
                        selectedState = selectedPhoto = -1;
                        Refresh();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                }
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("Add state"))
            {
                int index = states.arraySize++;
                SerializedProperty added = states.GetArrayElementAtIndex(index);
                added.FindPropertyRelative("state").enumValueIndex = 0;
                added.FindPropertyRelative("photos").ClearArray();
                added.isExpanded = true;
            }

            if (selectedState >= 0 && selectedState < states.arraySize)
            {
                SerializedProperty photos = states.GetArrayElementAtIndex(selectedState).FindPropertyRelative("photos");
                if (selectedPhoto >= 0 && selectedPhoto < photos.arraySize)
                    DrawPhoto(photos.GetArrayElementAtIndex(selectedPhoto));
            }
            EditorGUILayout.EndScrollView();
            serializedLibrary.ApplyModifiedProperties();
            if (GUILayout.Button("Save library")) AssetDatabase.SaveAssetIfDirty(library);
        }

        private void DrawPhoto(SerializedProperty entry)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Photo preprocessing", EditorStyles.boldLabel);
            SerializedProperty subject = entry.FindPropertyRelative("subjectRect");
            SerializedProperty saturation = entry.FindPropertyRelative("saturation");
            EditorGUILayout.PropertyField(subject, new GUIContent("Subject rect (normalized)"));
            subject.rectValue = AnimalPhotoProcessing.ClampSubjectRect(subject.rectValue);
            EditorGUILayout.Slider(saturation, 0, 2, new GUIContent("Saturation"));
            EditorGUILayout.HelpBox("Drag on the image to replace the required subject rectangle (green). Coordinates use a bottom-left origin, range 0–1. Saturation: 0 = grayscale, 1 = original, 2 = enhanced. Orange shows the random crop.", MessageType.Info);

            Texture2D source = entry.FindPropertyRelative("image").objectReferenceValue as Texture2D;
            if (source == null) return;
            bool regenerate = GUILayout.Button("Preview another random crop");
            if (source != previewSource || !Mathf.Approximately(previewSaturation, saturation.floatValue) ||
                previewSubject != subject.rectValue || regenerate)
            {
                ClearPreview();
                previewSource = source;
                previewSaturation = saturation.floatValue;
                previewSubject = subject.rectValue;
                // Editor preview must not consume the game's random sequence.
                Random.State previousRandom = Random.state;
                Random.InitState(System.Guid.NewGuid().GetHashCode());
                try { randomCrop = AnimalPhotoProcessing.RandomCrop(previewSubject); }
                finally { Random.state = previousRandom; }
                try
                {
                    preview = AnimalPhotoProcessing.Render(source, new Rect(0, 0, 1, 1), previewSaturation, 1024);
                    cropPreview = AnimalPhotoProcessing.Render(source, randomCrop, previewSaturation, 1024);
                }
                catch (System.Exception ex) { previewError = ex.Message; }
            }
            if (!string.IsNullOrEmpty(previewError)) EditorGUILayout.HelpBox(previewError, MessageType.Error);

            Rect area = GUILayoutUtility.GetRect(100, 350, GUILayout.ExpandWidth(true));
            Rect imageRect = Fit(area, (float)source.width / source.height);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(imageRect, preview != null ? (Texture)preview : source, ScaleMode.StretchToFill);
                DrawOutline(ToGuiRect(imageRect, randomCrop), new Color(1, 0.65f, 0));
                DrawOutline(ToGuiRect(imageRect, subject.rectValue), Color.green);
            }
            HandleRectangleInput(imageRect, subject);
            EditorGUILayout.LabelField("Processed crop (same processing as the result UI)");
            if (cropPreview != null)
            {
                Rect resultArea = GUILayoutUtility.GetRect(100, 240, GUILayout.ExpandWidth(true));
                GUI.DrawTexture(Fit(resultArea, (float)cropPreview.width / cropPreview.height), cropPreview, ScaleMode.StretchToFill);
            }
        }

        private void HandleRectangleInput(Rect imageRect, SerializedProperty subject)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && imageRect.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = id;
                dragging = true;
                dragStart = NormalizedPoint(imageRect, evt.mousePosition);
                evt.Use();
            }
            else if (dragging && GUIUtility.hotControl == id &&
                (evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp))
            {
                Vector2 end = NormalizedPoint(imageRect, evt.mousePosition);
                subject.rectValue = AnimalPhotoProcessing.ClampSubjectRect(Rect.MinMaxRect(
                    Mathf.Min(dragStart.x, end.x), Mathf.Min(dragStart.y, end.y),
                    Mathf.Max(dragStart.x, end.x), Mathf.Max(dragStart.y, end.y)));
                if (evt.type == EventType.MouseUp) { dragging = false; GUIUtility.hotControl = 0; }
                evt.Use();
                Repaint();
            }
            EditorGUIUtility.AddCursorRect(imageRect, MouseCursor.ArrowPlus);
        }

        private static Vector2 NormalizedPoint(Rect image, Vector2 mouse) => new Vector2(
            Mathf.Clamp01((mouse.x - image.x) / image.width), 1 - Mathf.Clamp01((mouse.y - image.y) / image.height));

        private static Rect ToGuiRect(Rect image, Rect normalized) => new Rect(
            image.x + normalized.x * image.width, image.y + (1 - normalized.yMax) * image.height,
            normalized.width * image.width, normalized.height * image.height);

        private static Rect Fit(Rect area, float aspect)
        {
            float width = Mathf.Min(area.width, area.height * aspect);
            float height = width / aspect;
            return new Rect(area.center.x - width / 2, area.center.y - height / 2, width, height);
        }

        private static void DrawOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2, rect.y, 2, rect.height), color);
        }

        private void ClearPreview()
        {
            AnimalPhotoProcessing.Release(preview);
            AnimalPhotoProcessing.Release(cropPreview);
            preview = cropPreview = null;
            previewSource = null;
            previewError = null;
        }
    }
}

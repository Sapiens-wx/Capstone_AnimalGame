using System;
using System.IO;
using AnimalGame.Animals;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalGame.EditorTools
{
    /// <summary>Photo source editing and camera render-Set authoring use the same runtime renderer.</summary>
    public sealed class AnimalPhotoLibraryWindow : EditorWindow
    {
        private AnimalPhotoLibrary library;
        private SerializedObject serializedLibrary;
        private PhotoCameraDefinition cameraModel;
        private PhotoRenderSet renderSet, comparisonSet;
        private UnityEditor.Editor setEditor;
        private Vector2 selectionScroll, previewScroll, settingsScroll;
        private int selectedState = -1, selectedPhoto = -1;
        private RenderTexture originalPreview, renderedPreview, comparisonPreview;
        private Rect randomCrop;
        private bool hasRandomCrop, dragging;
        private Texture2D cropSource;
        private int cropSourceWidth, cropSourceHeight;
        private string cropEntryPath;
        private Vector2 dragStart;
        private int grainSeed = 1979;
        private int cardPreviewSize = 160;
        private int comparisonMode, framingMode;
        private bool showCropEditor = true;
        private string previewKey, previewError, observedSetContents;
        private string assetName = "", assetError;
        private bool previewDirty = true;

        [MenuItem("Animal Game/Animals/Animal Photo Library")]
        public static void Open() => GetWindow<AnimalPhotoLibraryWindow>("Animal Photos");

        private void OnEnable()
        {
            minSize = new Vector2(1120, 650);
            Undo.undoRedoPerformed += Refresh;
            EditorApplication.projectChanged += Refresh;
            AnimalPhotoPreviewInvalidation.AssetsChanged += Refresh;
            OnSelectionChange();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Refresh;
            EditorApplication.projectChanged -= Refresh;
            AnimalPhotoPreviewInvalidation.AssetsChanged -= Refresh;
            ClearPreview();
            if (setEditor != null) DestroyImmediate(setEditor);
        }

        private void Refresh()
        {
            previewDirty = true;
            Repaint();
        }

        // Catches edits made in another Inspector before that asset has been saved.
        private void OnInspectorUpdate()
        {
            string contents = SetContents(renderSet) + SetContents(comparisonSet);
            if (contents == observedSetContents) return;
            observedSetContents = contents;
            Refresh();
        }

        private static string SetContents(PhotoRenderSet set) => set == null ? "basic" :
            set.GetInstanceID() + ":" + EditorJsonUtility.ToJson(set);

        private void OnSelectionChange()
        {
            if (Selection.activeObject is AnimalPhotoLibrary selected) SetLibrary(selected);
        }

        private void SetLibrary(AnimalPhotoLibrary value)
        {
            library = value;
            serializedLibrary = value != null ? new SerializedObject(value) : null;
            selectedState = selectedPhoto = -1;
            dragging = hasRandomCrop = false;
            cropEntryPath = null;
            ClearPreview();
            Refresh();
        }

        private void SelectSet(PhotoRenderSet value)
        {
            renderSet = value;
            if (setEditor != null) DestroyImmediate(setEditor);
            setEditor = value == null ? null : UnityEditor.Editor.CreateEditor(value);
            ExpandSetSettings();
            assetName = value != null ? value.name : "";
            assetError = null;
            Refresh();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Animal photos · camera rendering", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Crop and grain stay locked while you adjust a Set.");
            EditorGUILayout.EndHorizontal();
            if (serializedLibrary != null && serializedLibrary.targetObject == null) SetLibrary(null);
            if (library != null && serializedLibrary == null) serializedLibrary = new SerializedObject(library);
            if (serializedLibrary != null) serializedLibrary.Update();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox,
                GUILayout.Width(Mathf.Clamp(position.width * 0.22f, 240, 300)), GUILayout.ExpandHeight(true));
            DrawLibrary();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            previewScroll = EditorGUILayout.BeginScrollView(previewScroll);
            DrawCameraSelection();
            SerializedProperty entry = SelectedEntry();
            if (entry != null) DrawPhoto(entry);
            else EditorGUILayout.HelpBox("Choose an animal library and click Edit beside a photo. You can manage render Sets in the right column without a photo selected.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox,
                GUILayout.Width(Mathf.Clamp(position.width * 0.27f, 300, 390)), GUILayout.ExpandHeight(true));
            settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll);
            DrawSetWorkspace();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (serializedLibrary != null && serializedLibrary.ApplyModifiedProperties()) Repaint();
        }

        private void DrawLibrary()
        {
            EditorGUILayout.LabelField("Photo library", EditorStyles.boldLabel);
            var next = (AnimalPhotoLibrary)EditorGUILayout.ObjectField(library, typeof(AnimalPhotoLibrary), false);
            if (next != library) SetLibrary(next);
            if (library == null)
            {
                EditorGUILayout.HelpBox("Select an AnimalPhotoLibrary asset, or create one via Assets > Create > Animal Game > Photos.", MessageType.Info);
                return;
            }
            selectionScroll = EditorGUILayout.BeginScrollView(selectionScroll);
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
                        SerializedProperty photo = photos.GetArrayElementAtIndex(p);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PropertyField(photo.FindPropertyRelative("image"), GUIContent.none);
                        if (GUILayout.Button(selectedState == s && selectedPhoto == p ? "Editing" : "Edit", GUILayout.Width(52)))
                        {
                            selectedState = s;
                            selectedPhoto = p;
                            Refresh();
                        }
                        bool remove = GUILayout.Button("−", GUILayout.Width(23));
                        EditorGUILayout.EndHorizontal();
                        if (!remove) continue;
                        photos.DeleteArrayElementAtIndex(p);
                        selectedState = selectedPhoto = -1;
                        ClearPreview();
                        break;
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
                        ClearPreview();
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
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Save library"))
            {
                serializedLibrary.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(library);
            }
        }

        private SerializedProperty SelectedEntry()
        {
            if (serializedLibrary == null) return null;
            SerializedProperty states = serializedLibrary.FindProperty("states");
            if (selectedState < 0 || selectedState >= states.arraySize) return null;
            SerializedProperty photos = states.GetArrayElementAtIndex(selectedState).FindPropertyRelative("photos");
            return selectedPhoto >= 0 && selectedPhoto < photos.arraySize ? photos.GetArrayElementAtIndex(selectedPhoto) : null;
        }

        private void DrawCameraSelection()
        {
            EditorGUILayout.LabelField("Camera and render Set", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var nextCamera = (PhotoCameraDefinition)EditorGUILayout.ObjectField(
                new GUIContent("Camera model", "Choosing a model previews its assigned Set without changing the photo or crop."),
                cameraModel, typeof(PhotoCameraDefinition), false);
            if (EditorGUI.EndChangeCheck())
            {
                cameraModel = nextCamera;
                SelectSet(cameraModel != null ? cameraModel.RenderSet : null);
            }
            var nextSet = (PhotoRenderSet)EditorGUILayout.ObjectField(
                new GUIContent("Set A", "The shared render Set edited in the right column. An empty field previews Basic processing."),
                renderSet, typeof(PhotoRenderSet), false);
            if (nextSet != renderSet) SelectSet(nextSet);
            if (cameraModel != null && cameraModel.RenderSet != renderSet)
            {
                EditorGUILayout.HelpBox("Set A is a preview override. Bind it to the camera to use this Set when that model takes a photo.", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Use camera's Set")) SelectSet(cameraModel.RenderSet);
                if (GUILayout.Button("Bind Set A to camera"))
                {
                    var serializedCamera = new SerializedObject(cameraModel);
                    serializedCamera.FindProperty("renderSet").objectReferenceValue = renderSet;
                    serializedCamera.ApplyModifiedProperties();
                    AssetDatabase.SaveAssetIfDirty(cameraModel);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawSetWorkspace()
        {
            EditorGUILayout.LabelField("Shared render Set", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Set…"))
            {
                var menu = new GenericMenu();
                bool hasTypes = false;
                foreach (Type type in TypeCache.GetTypesDerivedFrom<PhotoRenderSet>())
                {
                    if (type.IsAbstract || type.ContainsGenericParameters) continue;
                    Type setType = type;
                    string label = RenderSetTypeLabel(setType);
                    menu.AddItem(new GUIContent(label), false, () => CreateSet(setType, label));
                    hasTypes = true;
                }
                if (!hasTypes) menu.AddDisabledItem(new GUIContent("No render Set implementations found"));
                menu.ShowAsContext();
            }
            using (new EditorGUI.DisabledScope(renderSet == null))
            {
                if (GUILayout.Button("Duplicate…")) DuplicateSet();
            }
            EditorGUILayout.EndHorizontal();
            if (renderSet == null)
            {
                EditorGUILayout.HelpBox("No Set selected: Basic processing uses the photo's legacy saturation. Create or select a Set to edit a reusable camera look.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("These settings are shared by every camera using this Set. Duplicate the Set before making an independent variation. Already captured photos keep their captured settings.", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                assetName = EditorGUILayout.TextField(assetName);
                if (GUILayout.Button("Rename", GUILayout.Width(65))) RenameSet();
                EditorGUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(assetError)) EditorGUILayout.HelpBox(assetError, MessageType.Error);
                EditorGUILayout.LabelField("Set ID", renderSet.SetId, EditorStyles.miniLabel);
                EditorGUILayout.Space();
                if (setEditor == null || setEditor.target != renderSet)
                {
                    if (setEditor != null) DestroyImmediate(setEditor);
                    setEditor = UnityEditor.Editor.CreateEditor(renderSet);
                    ExpandSetSettings();
                }
                EditorGUI.BeginChangeCheck();
                setEditor.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck()) Refresh();
                if (GUILayout.Button("Save Set")) AssetDatabase.SaveAssetIfDirty(renderSet);
            }

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("Camera model asset", EditorStyles.boldLabel);
            if (GUILayout.Button("Create camera using Set A…")) CreateCamera();
            if (cameraModel == null) return;
            var serializedCameraModel = new SerializedObject(cameraModel);
            serializedCameraModel.Update();
            EditorGUILayout.PropertyField(serializedCameraModel.FindProperty("displayName"));
            serializedCameraModel.ApplyModifiedProperties();
            EditorGUILayout.LabelField("Assigned Set", cameraModel.RenderSet != null ? cameraModel.RenderSet.name : "Basic fallback");
            if (GUILayout.Button("Save camera")) AssetDatabase.SaveAssetIfDirty(cameraModel);
        }

        private void DrawPhoto(SerializedProperty entry)
        {
            var source = entry.FindPropertyRelative("image").objectReferenceValue as Texture2D;
            SerializedProperty subject = entry.FindPropertyRelative("subjectRect");
            SerializedProperty saturation = entry.FindPropertyRelative("saturation");
            subject.rectValue = AnimalPhotoProcessing.ClampSubjectRect(subject.rectValue);
            if (cropSource != source || cropEntryPath != entry.propertyPath ||
                (source != null && (cropSourceWidth != source.width || cropSourceHeight != source.height)))
            {
                cropSource = source;
                cropSourceWidth = source != null ? source.width : 0;
                cropSourceHeight = source != null ? source.height : 0;
                cropEntryPath = entry.propertyPath;
                hasRandomCrop = false;
                if (source != null) GenerateCrop(source, subject.rectValue);
                Refresh();
            }
            if (source == null)
            {
                EditorGUILayout.HelpBox("Assign a source image to this entry.", MessageType.Info);
                return;
            }
            bool canCrop = AnimalPhotoProcessing.CanSquareCrop(subject.rectValue, source.width, source.height);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(source.name, EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            framingMode = EditorGUILayout.Popup("Preview framing", framingMode, new[] { "Final square crop", "Full source image" });
            comparisonMode = EditorGUILayout.Popup("Comparison", comparisonMode, new[] { "Original / Set A", "Set A / Set B" });
            if (comparisonMode == 1)
                comparisonSet = (PhotoRenderSet)EditorGUILayout.ObjectField("Set B", comparisonSet, typeof(PhotoRenderSet), false);
            if (EditorGUI.EndChangeCheck()) Refresh();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!canCrop))
            {
                if (GUILayout.Button("New crop")) GenerateCrop(source, subject.rectValue);
            }
            if (GUILayout.Button("New grain"))
            {
                grainSeed = Guid.NewGuid().GetHashCode() & int.MaxValue;
                Refresh();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            grainSeed = EditorGUILayout.IntField(new GUIContent("Grain seed", "The same fixed seed is used for both Sets and the card preview."), grainSeed);
            if (EditorGUI.EndChangeCheck()) Refresh();

            if (!canCrop)
                EditorGUILayout.HelpBox("No square can contain the entire subject rectangle. Reduce the green rectangle below, or inspect the full source image.", MessageType.Warning);
            if (hasRandomCrop && !Contains(randomCrop, subject.rectValue))
                EditorGUILayout.HelpBox("The locked crop no longer contains the edited subject. Click New crop when ready.", MessageType.Warning);

            if (framingMode == 1 || hasRandomCrop)
            {
                Rect crop = framingMode == 1 ? new Rect(0, 0, 1, 1) : randomCrop;
                EnsurePreview(source, crop, saturation.floatValue);
                if (!string.IsNullOrEmpty(previewError)) EditorGUILayout.HelpBox(previewError, MessageType.Error);
                DrawComparison(source, crop);
            }
            else EditorGUILayout.HelpBox("Define a subject rectangle that fits within a square, then click New crop to preview the in-game framing.", MessageType.Info);

            EditorGUILayout.Space();
            showCropEditor = EditorGUILayout.Foldout(showCropEditor, "Source photo and required subject rectangle", true);
            if (!showCropEditor) return;
            EditorGUILayout.PropertyField(subject, new GUIContent("Subject rect"));
            subject.rectValue = AnimalPhotoProcessing.ClampSubjectRect(subject.rectValue);
            EditorGUILayout.Slider(saturation, 0, 2, new GUIContent("Legacy saturation", "Only Basic processing uses this per-photo value. Instant Film owns its own color settings."));
            EditorGUILayout.HelpBox("Drag on the original image to define the required subject (green). Orange is the locked square crop. Coordinates use a bottom-left origin from 0 to 1.", MessageType.None);
            Rect area = GUILayoutUtility.GetRect(100, 245, GUILayout.ExpandWidth(true));
            Rect imageRect = Fit(area, (float)source.width / source.height);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(imageRect, source, ScaleMode.StretchToFill);
                if (hasRandomCrop) DrawOutline(ToGuiRect(imageRect, randomCrop), new Color(1, 0.65f, 0));
                DrawOutline(ToGuiRect(imageRect, subject.rectValue), Color.green);
            }
            HandleRectangleInput(imageRect, subject);
        }

        private void GenerateCrop(Texture2D source, Rect subject)
        {
            Random.State previousRandom = Random.state;
            Random.InitState(Guid.NewGuid().GetHashCode());
            try { hasRandomCrop = AnimalPhotoProcessing.TryRandomSquareCrop(subject, source.width, source.height, out randomCrop); }
            finally { Random.state = previousRandom; }
            Refresh();
        }

        private static bool Contains(Rect outer, Rect inner) => outer.xMin <= inner.xMin && outer.yMin <= inner.yMin &&
            outer.xMax >= inner.xMax && outer.yMax >= inner.yMax;

        private void EnsurePreview(Texture2D source, Rect crop, float legacySaturation)
        {
            string key = source.GetInstanceID() + ":" + crop.ToString("R") + ":" + legacySaturation.ToString("R") + ":" + grainSeed + ":" +
                comparisonMode + ":" + SetContents(renderSet) + ":" + SetContents(comparisonSet);
            if (!previewDirty && key == previewKey) return;
            // Resolve errors during layout too, so an error message never changes the
            // control count between the layout and repaint events.
            if (Event.current.type != EventType.Layout) return;
            ClearPreview();
            previewKey = key;
            previewDirty = false;
            try
            {
                originalPreview = PhotoRenderSnapshot.Basic().Render(source, crop, 1, 1024);
                renderedPreview = (renderSet != null ? renderSet.Capture(grainSeed) : PhotoRenderSnapshot.Basic(grainSeed))
                    .Render(source, crop, legacySaturation, 1024);
                if (comparisonMode == 1)
                    comparisonPreview = (comparisonSet != null ? comparisonSet.Capture(grainSeed) : PhotoRenderSnapshot.Basic(grainSeed))
                        .Render(source, crop, legacySaturation, 1024);
            }
            catch (Exception ex) { previewError = ex.Message; }
        }

        private void DrawComparison(Texture2D source, Rect crop)
        {
            float aspect = source.width * crop.width / (source.height * crop.height);
            EditorGUILayout.BeginHorizontal();
            DrawPreviewPanel(comparisonMode == 0 ? "Original" : SetLabel(renderSet, "A"),
                comparisonMode == 0 ? originalPreview : renderedPreview, aspect);
            DrawPreviewPanel(comparisonMode == 0 ? SetLabel(renderSet, "A") : SetLabel(comparisonSet, "B"),
                comparisonMode == 0 ? renderedPreview : comparisonPreview, aspect);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Card-size check · Set A", EditorStyles.miniBoldLabel);
            cardPreviewSize = EditorGUILayout.IntSlider(new GUIContent("Card size (UI points)",
                "Change the displayed size to check readability at the size of your game's photo card. This does not change render resolution."),
                cardPreviewSize, 80, 320);
            Rect cardArea = GUILayoutUtility.GetRect(100, cardPreviewSize, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && renderedPreview != null)
            {
                float size = Mathf.Min(cardPreviewSize, cardArea.width);
                GUI.DrawTexture(Fit(new Rect(cardArea.center.x - size / 2, cardArea.y, size, size), aspect), renderedPreview, ScaleMode.StretchToFill);
            }
            EditorGUILayout.LabelField("Preview uses the game's photo renderer at up to 1024 px. No border is baked into the image.", EditorStyles.wordWrappedMiniLabel);
        }

        private static string SetLabel(PhotoRenderSet set, string suffix) => "Set " + suffix + " · " + (set != null ? set.name : "Basic");

        private static void DrawPreviewPanel(string label, Texture texture, float aspect)
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            Rect area = GUILayoutUtility.GetRect(100, 230, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, new Color(0.10f, 0.10f, 0.10f));
                if (texture != null) GUI.DrawTexture(Fit(area, aspect), texture, ScaleMode.StretchToFill);
            }
            EditorGUILayout.EndVertical();
        }

        private static string RenderSetTypeLabel(Type type)
        {
            var menu = (CreateAssetMenuAttribute)Attribute.GetCustomAttribute(type, typeof(CreateAssetMenuAttribute));
            if (!string.IsNullOrWhiteSpace(menu?.menuName))
            {
                string[] segments = menu.menuName.Split('/');
                return segments[segments.Length - 1];
            }
            return ObjectNames.NicifyVariableName(type.Name);
        }

        private void CreateSet(Type type, string label)
        {
            string path = EditorUtility.SaveFilePanelInProject("Create render Set", label, "asset", "Choose where to save this shared render Set.");
            if (string.IsNullOrEmpty(path)) return;
            var asset = (PhotoRenderSet)CreateInstance(type);
            asset.RegenerateIdentity();
            CreateAsset(asset, path);
            AssetDatabase.SaveAssetIfDirty(asset);
            SelectSet(asset);
            EditorGUIUtility.PingObject(asset);
        }

        private void ExpandSetSettings()
        {
            if (setEditor == null) return;
            SerializedProperty settings = setEditor.serializedObject.FindProperty("settings");
            if (settings != null) settings.isExpanded = true;
        }

        private void DuplicateSet()
        {
            string path = EditorUtility.SaveFilePanelInProject("Duplicate render Set", renderSet.name + " Copy", "asset", "The copy receives a new Set ID and independent settings.");
            if (string.IsNullOrEmpty(path)) return;
            PhotoRenderSet copy = Instantiate(renderSet);
            copy.RegenerateIdentity();
            CreateAsset(copy, path);
            AssetDatabase.SaveAssetIfDirty(copy);
            SelectSet(copy);
            EditorGUIUtility.PingObject(copy);
        }

        private void RenameSet()
        {
            string name = assetName.Trim();
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                assetError = "Enter a valid, non-empty asset name.";
                return;
            }
            assetError = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(renderSet), name);
            if (!string.IsNullOrEmpty(assetError)) return;
            assetName = renderSet.name;
            AssetDatabase.SaveAssetIfDirty(renderSet);
        }

        private void CreateCamera()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create camera model", "Photo Camera", "asset", "The new camera will use the currently selected Set A.");
            if (string.IsNullOrEmpty(path)) return;
            var asset = CreateInstance<PhotoCameraDefinition>();
            asset.RegenerateIdentity();
            var serializedCamera = new SerializedObject(asset);
            serializedCamera.FindProperty("displayName").stringValue = Path.GetFileNameWithoutExtension(path);
            serializedCamera.FindProperty("renderSet").objectReferenceValue = renderSet;
            serializedCamera.ApplyModifiedPropertiesWithoutUndo();
            CreateAsset(asset, path);
            AssetDatabase.SaveAssetIfDirty(asset);
            cameraModel = asset;
            EditorGUIUtility.PingObject(asset);
            Refresh();
        }

        private static void CreateAsset(ScriptableObject asset, string requestedPath)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(requestedPath);
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
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
            else if (dragging && GUIUtility.hotControl == id && (evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp))
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
            AnimalPhotoProcessing.Release(originalPreview);
            AnimalPhotoProcessing.Release(renderedPreview);
            AnimalPhotoProcessing.Release(comparisonPreview);
            originalPreview = renderedPreview = comparisonPreview = null;
            previewKey = previewError = null;
            previewDirty = true;
        }
    }

    /// <summary>Reimport may keep the same Texture2D object, so object identity is not a cache key.</summary>
    internal sealed class AnimalPhotoPreviewInvalidation : AssetPostprocessor
    {
        internal static event Action AssetsChanged;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Length + deleted.Length + moved.Length + movedFrom.Length > 0) AssetsChanged?.Invoke();
        }
    }
}

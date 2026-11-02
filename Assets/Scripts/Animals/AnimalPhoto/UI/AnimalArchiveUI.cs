using System;
using System.Collections.Generic;
using System.Linq;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Animals.UI
{
    [DefaultExecutionOrder(410)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class AnimalArchiveUI : MonoBehaviour
    {
        public Shader compositeShader;
        public static bool BlocksGameplay { get; private set; }
        private enum Page { Photos, Inspection, Catalog, Species }
        private sealed class Location
        {
            public Page page;
            public int collection, selected, topRow, region, catalogIndex;
            public bool oldest, sortFocused;
            public AnimalSpecies species;
        }
        private readonly Stack<Location> history = new();
        private readonly PhotoCollection photos = new();
        private readonly List<AnimalSpecies?> collections = new();
        private readonly List<AnimalSpecies> catalogSpecies = new();
        private readonly List<string> regions = new();
        private Page page;
        private AnimalSpecies species;
        private int collection, region, catalogIndex;
        private VisualElement root;
        private PhotosView photosView;
        private CatalogView catalogView;
        private float savedTimeScale, holdTime, nextMove;
        private CursorLockMode cursorLock;
        private bool holdLatched, triggerLeft, triggerRight, leftShoulder;
        private Vector2 previousDirection;

        private void Start()
        {
            SaveSystem.Initialize();
            root = GetComponent<UIDocument>().rootVisualElement;
            root.style.display = DisplayStyle.None;
            // Navigation belongs to this controller; native submit must not click a second time.
            root.Query<Button>().ForEach(button => button.focusable = false);
            photosView = new PhotosView(root, compositeShader);
            catalogView = new CatalogView(root);
            root.Q<Button>("photos-tab").clicked += () => Navigate(Page.Photos);
            root.Q<Button>("catalog-tab").clicked += () => Navigate(Page.Catalog);
            root.Q("photos-tab").RegisterCallback<GeometryChangedEvent>(_ => UpdateTabSelection());
            root.Q("catalog-tab").RegisterCallback<GeometryChangedEvent>(_ => UpdateTabSelection());
            root.Q<Button>("back").clicked += Back;
            root.Q<Button>("main-catalog").clicked += () => Navigate(Page.Catalog);
            root.Q<Button>("species-photos").clicked += ViewSpeciesPhotos;
            root.Q<Button>("previous").clicked += () => StepCollection(-1);
            root.Q<Button>("next").clicked += () => StepCollection(1);
            SaveSystem.Changed += Refresh;
        }

        public void Open()
        {
            if (AnimalGame.MainUI.MainUI.BlocksGameplay || AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels) return;
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap == null || bootstrap.photoMode.IsActive
                || AnimalGame.Tutorial.TutorialTipOverLesson.BlocksGameplayForReading(bootstrap.robotObject)) return;
            collections.Clear();
            regions.Clear(); regions.AddRange(AnimalArchiveAssets.Active.Info.Values.Select(i => i.Region).Distinct());
            history.Clear(); page = Page.Photos; collection = region = catalogIndex = 0;
            photos.Selected = photos.TopRow = 0; photos.SortFocused = false;
            savedTimeScale = Time.timeScale; cursorLock = Cursor.lockState;
            BlocksGameplay = true; Time.timeScale = 0; Cursor.lockState = CursorLockMode.None;
            root.style.display = DisplayStyle.Flex; nextMove = Time.unscaledTime + .2f;
            Refresh();
        }

        public void Close()
        {
            if (!BlocksGameplay) return;
            BlocksGameplay = false;
            if (Time.timeScale == 0) Time.timeScale = savedTimeScale;
            Cursor.lockState = cursorLock;
            root.style.display = DisplayStyle.None; photosView.Clear(); history.Clear();
        }

        private Location CaptureLocation() => new Location { page = page, species = species,
            collection = collection, selected = photos.Selected, topRow = photos.TopRow,
            oldest = photos.OldestFirst, sortFocused = photos.SortFocused, region = region, catalogIndex = catalogIndex };

        private void Navigate(Page target)
        {
            if (target == page) return;
            history.Push(CaptureLocation()); page = target;
            Refresh();
        }

        private void Back()
        {
            if (history.Count == 0) { Close(); return; }
            var location = history.Pop(); page = location.page; species = location.species;
            collection = location.collection; region = location.region; catalogIndex = location.catalogIndex;
            photos.OldestFirst = location.oldest; photos.SortFocused = location.sortFocused;
            Refresh();
            photos.Selected = location.selected; photos.TopRow = location.topRow;
            if (page == Page.Photos || page == Page.Inspection) RenderPhotos();
        }

        private void OpenPhotoCatalog()
        {
            if (photos.Current == null) return;
            var selectedSpecies = photos.Current.species;
            history.Push(CaptureLocation()); species = selectedSpecies; page = Page.Species; Refresh();
        }

        private void ViewSpeciesPhotos()
        {
            int index = collections.IndexOf(species);
            if (index < 0) return;
            history.Push(CaptureLocation()); collection = index; page = Page.Photos; Refresh();
        }

        private void Refresh()
        {
            if (!BlocksGameplay || root == null) return;
            var selectedCollection = collections.Count == 0 ? null : collections[collection];
            collections.Clear();
            collections.Add(null); // All Collection has no species filter.
            collections.AddRange(SaveSystem.Current.photos.Select(p => p.species).Distinct()
                .OrderBy(s => (int)s).Select(s => (AnimalSpecies?)s));
            collection = Math.Max(0, collections.IndexOf(selectedCollection));
            bool isPhotos = page == Page.Photos || page == Page.Inspection;
            if (page == Page.Species) region = regions.IndexOf(AnimalArchiveAssets.Active.Info[species].Region);
            root.Q("photos-page").style.display = isPhotos ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("catalog-page").style.display = page == Page.Catalog ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("species-page").style.display = page == Page.Species ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Button>("photos-tab").EnableInClassList("active-tab", isPhotos);
            root.Q<Button>("catalog-tab").EnableInClassList("active-tab", !isPhotos);
            UpdateTabSelection();
            root.Q<Label>("sidebar-title").text = isPhotos ? "COLLECTION" : "CATALOG";
            root.Q<Label>("local-count").text = $"LOCAL COLLECTION\n{SaveSystem.Current.photos.Count} photographs\n{SaveSystem.Current.photos.Select(p => p.species).Distinct().Count()} observed species";
            var sidebar = root.Q<ScrollView>("collections"); sidebar.Clear();
            if (isPhotos)
            {
                for (int i = 0; i < collections.Count; i++)
                {
                    int chosen = i; var animal = collections[i];
                    var button = new Button(() => { collection = chosen; photos.SortFocused = false; Refresh(); });
                    string name = animal.HasValue ? AnimalArchiveAssets.Active.Photos[animal.Value].DisplayName : "All Collection";
                    int count = animal.HasValue ? SaveSystem.Current.photos.Count(p => p.species == animal.Value) : SaveSystem.Current.photos.Count;
                    button.AddToClassList("photo-collection");
                    var nameLabel = new Label(name) { pickingMode = PickingMode.Ignore };
                    nameLabel.AddToClassList("collection-name");
                    var recordsLabel = new Label($"{count:00} RECORDS") { pickingMode = PickingMode.Ignore };
                    recordsLabel.AddToClassList("collection-records");
                    button.Add(nameLabel);
                    button.Add(recordsLabel);
                    button.focusable = false; button.AddToClassList("collection"); button.EnableInClassList("selected", i == collection); sidebar.Add(button);
                }
                photos.Bind(SaveSystem.Current.photos, collections[collection]);
                RenderPhotos();
            }
            else
            {
                photosView.Clear();
                if (page == Page.Species)
                {
                    foreach (var info in AnimalArchiveAssets.Active.Info.Where(i => i.Value.Region == regions[region]))
                    {
                        var chosen = info.Key;
                        var progress = SaveSystem.Current.Progress(chosen);
                        var button = new Button(() => { species = chosen; Refresh(); }) {
                            text = progress != null && progress.Tier > 0 ? AnimalArchiveAssets.Active.Photos[chosen].DisplayName : "Unidentified species" };
                        button.focusable = false; button.AddToClassList("collection"); button.EnableInClassList("selected", chosen == species); sidebar.Add(button);
                    }
                }
                else for (int i = 0; i < regions.Count; i++)
                {
                    int chosen = i; var button = new Button(() => { region = chosen; catalogIndex = 0; Refresh(); }) { text = regions[i] };
                    button.focusable = false; button.AddToClassList("collection"); button.EnableInClassList("selected", i == region); sidebar.Add(button);
                }
                RenderCatalog();
            }
            sidebar.schedule.Execute(() => {
                var selected = sidebar.Q<Button>(className: "selected");
                if (selected != null) sidebar.ScrollTo(selected);
            });
        }

        private void UpdateTabSelection()
        {
            var tab = root.Q<Button>(className: "active-tab");
            var indicator = root.Q("tab-selection");
            Vector2 bottomLeft = tab.ChangeCoordinatesTo(indicator.parent, new Vector2(0, tab.layout.height));
            indicator.style.left = bottomLeft.x;
            indicator.style.top = bottomLeft.y - 5f;
            indicator.style.width = tab.layout.width;
        }

        private void RenderPhotos() => photosView.Render(photos, page == Page.Inspection,
            index => { photos.Selected = index; photos.SortFocused = false; RenderPhotos(); },
            () => { if (photos.Current != null) Navigate(Page.Inspection); }, OpenPhotoCatalog,
            () => { photos.ToggleSort(); RenderPhotos(); });

        private void RenderCatalog()
        {
            if (regions.Count == 0) return;
            if (page == Page.Species) region = regions.IndexOf(AnimalArchiveAssets.Active.Info[species].Region);
            catalogSpecies.Clear(); catalogSpecies.AddRange(AnimalArchiveAssets.Active.Info
                .Where(i => i.Value.Region == regions[region]).Select(i => i.Key));
            var list = root.Q<ScrollView>("catalog-list"); list.Clear();
            root.Q<Label>("region-title").text = regions[region];
            for (int i = 0; i < catalogSpecies.Count; i++)
            {
                int chosen = i; var animal = catalogSpecies[i]; var progress = SaveSystem.Current.Progress(animal);
                var button = new Button(() => { catalogIndex = chosen; species = animal; Navigate(Page.Species); }) {
                    text = $"{(progress != null && progress.Tier > 0 ? AnimalArchiveAssets.Active.Photos[animal].DisplayName : "Unidentified species")}\n{progress?.recognition ?? 0} recognition" };
                button.focusable = false; button.AddToClassList("catalog-entry"); button.EnableInClassList("selected", catalogIndex == i); list.Add(button);
            }
            if (page == Page.Species)
            {
                catalogIndex = catalogSpecies.IndexOf(species); catalogView.Render(species);
                root.Q<Button>("species-photos").SetEnabled(SaveSystem.Current.photos.Exists(p => p.species == species));
                root.Q<Label>("record-count").text = $"{catalogIndex + 1:00} / {catalogSpecies.Count:00} SPECIES";
            }
            else root.Q<Label>("record-count").text = $"{catalogSpecies.Count:00} SPECIES   A  VIEW SPECIES";
        }

        private void StepCollection(int direction)
        {
            if (page == Page.Photos || page == Page.Inspection)
            {
                collection = (collection + direction + collections.Count) % collections.Count; photos.SortFocused = false;
            }
            else if (page == Page.Species)
            {
                catalogIndex = (catalogIndex + direction + catalogSpecies.Count) % catalogSpecies.Count;
                species = catalogSpecies[catalogIndex];
            }
            else { if (regions.Count == 0) return; region = (region + direction + regions.Count) % regions.Count; catalogIndex = 0; }
            Refresh();
        }

        private void Confirm()
        {
            if (page == Page.Photos)
            {
                if (photos.SortFocused) { photos.ToggleSort(); RenderPhotos(); }
                else if (photos.Current != null) Navigate(Page.Inspection);
            }
            else if (page == Page.Catalog && catalogSpecies.Count > 0) { species = catalogSpecies[catalogIndex]; Navigate(Page.Species); }
        }

        private void Update()
        {
            if (root == null) return;
            Vector2 direction = AdaptiveLegacyGamepadInput.ReadLeftStick();
            Vector2 right = AdaptiveLegacyGamepadInput.ReadRightStick();
            bool upHeld = Input.GetKey(KeyCode.G), confirm = Input.GetKeyDown(KeyCode.Return),
                back = Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.Escape),
                y = Input.GetKeyDown(KeyCode.Y), x = Input.GetKeyDown(KeyCode.X),
                lb = Input.GetKeyDown(KeyCode.Q), rb = Input.GetKeyDown(KeyCode.E),
                lt = Input.GetKeyDown(KeyCode.LeftBracket), rt = Input.GetKeyDown(KeyCode.RightBracket);
            if (Input.GetKey(KeyCode.LeftArrow)) direction.x = -1;
            if (Input.GetKey(KeyCode.RightArrow)) direction.x = 1;
            if (Input.GetKey(KeyCode.UpArrow)) direction.y = 1;
            if (Input.GetKey(KeyCode.DownArrow)) direction.y = -1;
#if ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad != null)
            {
                upHeld |= pad.dpad.up.isPressed;
                if (pad.dpad.ReadValue().sqrMagnitude > .1f) direction = pad.dpad.ReadValue();
                confirm |= pad.buttonSouth.wasPressedThisFrame; back |= pad.buttonEast.wasPressedThisFrame;
                y |= pad.buttonNorth.wasPressedThisFrame; x |= pad.buttonWest.wasPressedThisFrame;
                lb |= pad.leftShoulder.wasPressedThisFrame; rb |= pad.rightShoulder.wasPressedThisFrame;
                bool l = pad.leftTrigger.ReadValue() > .5f, r = pad.rightTrigger.ReadValue() > .5f;
                lt |= l && !triggerLeft; rt |= r && !triggerRight; triggerLeft = l; triggerRight = r;
            }
            else
#endif
            {
                confirm |= Input.GetKeyDown(KeyCode.JoystickButton0);
                back |= AdaptiveLegacyGamepadInput.WasEastFaceButtonPressedThisFrame();
                y |= AdaptiveLegacyGamepadInput.WasNorthFaceButtonPressedThisFrame();
                x |= AdaptiveLegacyGamepadInput.WasWestFaceButtonPressedThisFrame();
                rb |= AdaptiveLegacyGamepadInput.WasRightShoulderPressedThisFrame();
                bool shoulder = Input.GetKey(KeyCode.JoystickButton4); lb |= shoulder && !leftShoulder; leftShoulder = shoulder;
            }
            if (!upHeld) { holdTime = 0; holdLatched = false; }
            if (!BlocksGameplay)
            {
                if (Input.GetKeyDown(KeyCode.V)) { Open(); return; }
                if (upHeld && !holdLatched) { holdTime += Time.unscaledDeltaTime; if (holdTime >= .5f) { holdLatched = true; Open(); } }
                return;
            }
            if (holdLatched && upHeld) return;
            if (back) { Back(); return; }
            if (rb) { Navigate(Page.Catalog); return; }
            if (lb && page != Page.Photos) { Navigate(Page.Photos); return; }
            if (y) { if (page == Page.Photos || page == Page.Inspection) OpenPhotoCatalog(); else Navigate(Page.Catalog); return; }
            if (x && page == Page.Species) { ViewSpeciesPhotos(); return; }
            if (lt || rt) { StepCollection(lt ? -1 : 1); return; }
            if (confirm) { Confirm(); return; }
            if (page == Page.Inspection) photosView.Tilt(right);
            else if (page == Page.Photos && right.sqrMagnitude > direction.sqrMagnitude) direction = right;
            if (direction.magnitude < .5f) { previousDirection = Vector2.zero; nextMove = 0; return; }
            direction = Mathf.Abs(direction.x) > Mathf.Abs(direction.y) ? new Vector2(Mathf.Sign(direction.x), 0) : new Vector2(0, Mathf.Sign(direction.y));
            if (direction == previousDirection && Time.unscaledTime < nextMove) return;
            nextMove = Time.unscaledTime + (direction == previousDirection ? .15f : .35f); previousDirection = direction;
            if (page == Page.Photos || page == Page.Inspection) { photos.Move((int)direction.x, (int)direction.y, page == Page.Inspection); RenderPhotos(); }
            else if (page == Page.Catalog)
            {
                catalogIndex = Mathf.Clamp(catalogIndex + (direction.x != 0 ? (int)direction.x : -(int)direction.y), 0, Math.Max(0, catalogSpecies.Count - 1));
                RenderCatalog(); var list = root.Q<ScrollView>("catalog-list");
                if (list.childCount > 0) list.ScrollTo(list[catalogIndex]);
            }
            else root.Q<ScrollView>("catalog-details").scrollOffset += new Vector2(0, -direction.y * 100);
        }

        private void OnDisable() { if (root != null) Close(); }
        private void OnDestroy() { SaveSystem.Changed -= Refresh; photosView?.Dispose(); }
    }
}

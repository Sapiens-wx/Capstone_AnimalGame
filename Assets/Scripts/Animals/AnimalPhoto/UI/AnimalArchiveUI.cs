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
            public int collection, selected, topRow, catalogIndex;
            public bool oldest, sortFocused;
            public AnimalSpecies species;
        }
        private Location previousLocation;
        private readonly PhotoCollection photos = new();
        private readonly PhotoDeleteHold deleteHold = new();
        private readonly List<AnimalSpecies?> collections = new();
        private readonly List<AnimalSpecies> catalogSpecies = new();
        private Page page;
        private Page? lastRenderedPage;
        private AnimalSpecies species;
        private int collection, catalogIndex;
        private VisualElement root;
        private PhotosView photosView;
        private PhotosSidebarView photosSidebar;
        private CatalogView catalogView;
        private MainCatalogView mainCatalogView;
        private CatalogSidebarView catalogSidebar;
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
            photosSidebar = new PhotosSidebarView(root.Q<ScrollView>("collections"));
            catalogView = new CatalogView(root);
            mainCatalogView = new MainCatalogView(root);
            catalogSidebar = new CatalogSidebarView(root.Q<ScrollView>("catalog-sidebar"));
            root.Q<Button>("photos-tab").clicked += () => Navigate(Page.Photos);
            root.Q<Button>("catalog-tab").clicked += () => Navigate(Page.Catalog);
            root.Q("photos-tab").RegisterCallback<GeometryChangedEvent>(_ => UpdateTabSelection());
            root.Q("catalog-tab").RegisterCallback<GeometryChangedEvent>(_ => UpdateTabSelection());
            root.Q<Button>("back").clicked += Back;
            root.Q<Button>("species-previous").clicked += () => StepCollection(-1);
            root.Q<Button>("species-next").clicked += () => StepCollection(1);
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
            previousLocation = null; page = Page.Photos; collection = 0; catalogIndex = -1;
            lastRenderedPage = null;
            catalogSidebar.Reset(); photosSidebar.Reset();
            mainCatalogView.Reset();
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
            deleteHold.Cancel(); photosView.SetDeleteProgress(0);
            if (Time.timeScale == 0) Time.timeScale = savedTimeScale;
            Cursor.lockState = cursorLock;
            catalogSidebar.StopAnimation(); photosSidebar.StopAnimation();
            root.style.display = DisplayStyle.None; photosView.Clear(); previousLocation = null;
        }

        private Location CaptureLocation() => new Location { page = page, species = species,
            collection = collection, selected = photos.Selected, topRow = photos.TopRow,
            oldest = photos.OldestFirst, sortFocused = photos.SortFocused, catalogIndex = catalogIndex };

        private void Navigate(Page target)
        {
            if (target == page) return;
            previousLocation = CaptureLocation(); page = target;
            if (target == Page.Catalog) { catalogIndex = -1; species = AnimalSpecies.Unknown; }
            Refresh();
        }

        private void Back()
        {
            if (previousLocation == null) { Close(); return; }
            var location = previousLocation; previousLocation = null;
            page = location.page; species = location.species;
            collection = Mathf.Clamp(location.collection, 0, collections.Count - 1); catalogIndex = location.catalogIndex;
            photos.OldestFirst = location.oldest; photos.SortFocused = location.sortFocused;
            Refresh();
            photos.Selected = Mathf.Clamp(location.selected, 0, Math.Max(0, photos.Photos.Count - 1));
            photos.TopRow = Math.Min(location.topRow, photos.Selected / 3);
            if (page == Page.Photos || page == Page.Inspection) RenderPhotos();
        }

        private void OpenPhotoCatalog()
        {
            if (photos.Current == null) return;
            var selectedSpecies = photos.Current.species;
            if (!IsIdentified(selectedSpecies)) { Navigate(Page.Catalog); return; }
            previousLocation = CaptureLocation(); species = selectedSpecies; page = Page.Species; Refresh();
        }

        private void ViewSpeciesPhotos()
        {
            int index = collections.IndexOf(species);
            if (index < 0) return;
            previousLocation = CaptureLocation(); collection = index; page = Page.Photos; Refresh();
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
            root.Q("photos-page").style.display = isPhotos ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("catalog-page").style.display = page == Page.Catalog ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("species-page").style.display = page == Page.Species ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Button>("photos-tab").EnableInClassList("active-tab", isPhotos);
            root.Q<Button>("catalog-tab").EnableInClassList("active-tab", !isPhotos);
            UpdateTabSelection();
            root.Q("archive-shell").EnableInClassList("catalog-mode", !isPhotos);
            foreach (string name in new[] { "sidebar-title", "previous", "next", "collections", "local-count" })
                root.Q(name).style.display = isPhotos ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("main-catalog").style.display = page == Page.Species ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("catalog-sidebar").style.display = page == Page.Species ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("main-catalog-summary").style.display = page == Page.Catalog ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("species-navigation").style.display = page == Page.Species ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("species-recognition").style.display = page == Page.Species ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("record-count").style.display = isPhotos ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Label>("local-count").text = $"LOCAL COLLECTION\n{SaveSystem.Current.photos.Count} photographs\n{SaveSystem.Current.photos.Select(p => p.species).Distinct().Count()} observed species";
            if (isPhotos)
            {
                photosSidebar.Bind(collections,
                    animal => animal.HasValue ? AnimalArchiveAssets.Active.Photos[animal.Value].DisplayName : "All Collection",
                    animal => animal.HasValue ? SaveSystem.Current.photos.Count(p => p.species == animal.Value) : SaveSystem.Current.photos.Count,
                    collection, index => { collection = index; photos.SortFocused = false; Refresh(); });
                photos.Bind(SaveSystem.Current.photos, collections[collection]);
                RenderPhotos();
            }
            else
            {
                photosSidebar.StopAnimation();
                photosView.Clear();
                RenderCatalog();
            }
            lastRenderedPage = page;
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

        private bool IsIdentified(AnimalSpecies animal) => SaveSystem.Current.Progress(animal)?.Tier > 0;

        private void RenderCatalog()
        {
            catalogSpecies.Clear();
            catalogSpecies.AddRange(AnimalArchiveAssets.Active.Info.Keys.OrderBy(s => (int)s));
            if (page == Page.Species) catalogIndex = catalogSpecies.IndexOf(species);
            catalogSidebar.Bind(catalogSpecies, IsIdentified,
                animal => AnimalArchiveAssets.Active.Photos[animal].DisplayName,
                catalogIndex, SelectCatalogSpecies);
            if (page == Page.Catalog)
            {
                catalogSidebar.StopAnimation();
                if (lastRenderedPage != Page.Catalog)
                    mainCatalogView.RenderProgress(catalogSpecies.Count(IsIdentified));
                mainCatalogView.Render(catalogSpecies.Where(IsIdentified), animal => SelectCatalogSpecies(catalogSpecies.IndexOf(animal)));
            }
            if (page == Page.Species)
            {
                catalogView.Render(species);
                root.Q<Button>("species-photos").SetEnabled(SaveSystem.Current.photos.Exists(p => p.species == species));
                UpdateSpeciesNeighbor(-1, "species-previous", "previous-species-name");
                UpdateSpeciesNeighbor(1, "species-next", "next-species-name");
            }
        }

        private void SelectCatalogSpecies(int index)
        {
            if (!IsIdentified(catalogSpecies[index])) return;
            if (page == Page.Catalog)
            {
                previousLocation = CaptureLocation();
                page = Page.Species;
            }
            catalogIndex = index; species = catalogSpecies[index];
            Refresh();
        }

        private void UpdateSpeciesNeighbor(int direction, string buttonName, string labelName)
        {
            int next = catalogSidebar.FindNextSelectable(catalogIndex, direction);
            bool available = next >= 0;
            var button = root.Q<Button>(buttonName);
            button.SetEnabled(available);
            button.EnableInClassList("unavailable", !available);
            root.Q<Label>(labelName).text = available ? AnimalArchiveAssets.Active.Photos[catalogSpecies[next]].DisplayName : "";
        }

        private void StepCollection(int direction)
        {
            if (page == Page.Photos || page == Page.Inspection)
            {
                int next = Mathf.Clamp(collection + direction, 0, collections.Count - 1);
                if (next == collection) return;
                collection = next; photos.SortFocused = false;
            }
            else
            {
                int next = catalogSidebar.FindNextSelectable(catalogIndex, direction);
                if (next >= 0) SelectCatalogSpecies(next);
                return;
            }
            Refresh();
        }

        private void Confirm()
        {
            if (page == Page.Photos)
            {
                if (photos.SortFocused) { photos.ToggleSort(); RenderPhotos(); }
                else if (photos.Current != null) Navigate(Page.Inspection);
            }
            else if (page == Page.Catalog) mainCatalogView.Confirm();
        }

        private void Update()
        {
            if (root == null || AnimalGame.Animals.Communication.EarthlineUI.BlocksGameplay) return;
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
            bool deleteHeld = Input.GetKey(KeyCode.X) || AdaptiveLegacyGamepadInput.IsWestFaceButtonHeld();
            string deleteTarget = page == Page.Photos || page == Page.Inspection ? photos.Current?.id : null;
            bool delete = deleteHold.Update(deleteHeld, deleteTarget, Time.unscaledDeltaTime);
            photosView.SetDeleteProgress(deleteHold.Progress);
            if (delete)
            {
                deleteHold.Cancel(); photosView.SetDeleteProgress(0);
                SaveSystem.DeletePhoto(deleteTarget);
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
            else if (page == Page.Catalog) mainCatalogView.Move((int)direction.x, (int)direction.y);
            else if (direction.y != 0) StepCollection(-(int)direction.y);
        }

        private void OnDisable() { if (root != null) Close(); }
        private void OnDestroy() { SaveSystem.Changed -= Refresh; photosView?.Dispose(); }
    }
}

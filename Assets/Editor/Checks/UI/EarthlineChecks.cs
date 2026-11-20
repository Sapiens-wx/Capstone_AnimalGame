using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.Animals.Communication;
using AnimalGame.Animals.UI;
using AnimalGame.Save;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using GameMainUI = AnimalGame.MainUI.MainUI;

namespace AnimalGame.Editor
{
    public static class EarthlineChecks
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Output = "Assets/output/validation/earthline";
        private static int checks;
        private static void Require(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }

        [MenuItem("Animal Game/Validation/Run Earthline Checks")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Earthline checks run in Edit Mode.");
            checks = 0;
            AnimalArchiveAssets.Initialize();
            var original = SaveSystem.Current;
            byte[] disk = File.Exists(SaveSystem.SavePath) ? File.ReadAllBytes(SaveSystem.SavePath) : null;
            var originalMain = GameMainUI.Active;
            float timeScale = Time.timeScale;
            var cursorLock = UnityEngine.Cursor.lockState;
            Scene scene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null, clockObject = null;
            RenderTexture target = null;
            PanelSettings panel = null;
            try
            {
                Directory.CreateDirectory(Output);
                SetCurrent(Fixture(true));
                CheckMessages();
                SetCurrent(Fixture());
                clockObject = new GameObject("Earthline clock fixture"); clockObject.SetActive(false);
                SceneManager.MoveGameObjectToScene(clockObject, scene);
                var clock = clockObject.AddComponent<GameMainUI>();
                typeof(GameMainUI).GetField("gameTime", Flags).SetValue(clock, new DateTime(2026, 11, 4, 19, 12, 0));
                typeof(GameMainUI).GetProperty("Active", Flags).SetValue(null, clock);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Resources/UI/Communication/EarthlineUI.prefab"), scene);
                var document = instance.GetComponent<UIDocument>();
                panel = Object.Instantiate(document.panelSettings);
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                target = new RenderTexture(1920, 1080, 24); target.Create();
                panel.targetTexture = target; document.panelSettings = panel;
                var ui = instance.GetComponent<EarthlineUI>(); ui.InitializeView();
                var root = document.rootVisualElement;
                ui.Open(); Settle(root, new Vector2(1920, 1080));
                Require(EarthlineUI.BlocksGameplay && GameMainUI.BlocksGameplay && Time.timeScale == 0,
                    "Opening Earthline pauses and blocks gameplay.");
                Require(ui.contacts.contacts.Count == 3 && ui.contacts.contacts.All(c => c.portrait != null), "All three contacts have editable sprite portraits.");
                Require(root.Q<CatalogHeadingElement>("contacts-heading").Flip, "Contacts heading is flipped.");
                Require(ui.Focus == EarthlineFocus.Contacts, "Opening focuses contacts.");
                ui.Navigate(0, -1); Require(ui.ContactIndex == 1, "Down selects Mara.");
                ui.Navigate(1, 0); Require(ui.Focus == EarthlineFocus.Chat, "Right focuses the chat.");
                Require(!root.Q("contacts-sidebar").ClassListContains("contacts-focused"), "Contact retains an unfocused selection style.");
                root.Q<ScrollView>("chat-scroll").scrollOffset = new Vector2(0, Math.Max(0, root.Q<ScrollView>("chat-scroll").verticalScroller.highValue));
                ui.Navigate(0, -1); Require(ui.Focus == EarthlineFocus.Replies && ui.ReplyIndex == 0, "Down at chat bottom focuses the first reply.");
                ui.Navigate(0, -1); Require(ui.ReplyIndex == 1, "Down selects another reply.");
                ui.Navigate(0, 1); ui.Navigate(0, 1); Require(ui.Focus == EarthlineFocus.Chat, "Up from the first reply returns to chat.");
                Require(!root.Q("reply-0").ClassListContains("reply-selected"), "Replies have no selection without focus.");
                ui.Navigate(0, -1); ui.Confirm();
                Require(SaveSystem.Current.conversations.Single().messages.Single().kind == MessageKind.Text, "A reply creates a real text message.");
                Require(root.Q<Label>("conversation-date").text == "2026.11.04", "Header uses the in-game date.");
                ui.OpenPhotoPicker(); Settle(root, new Vector2(1920, 1080));
                var picker = (EarthlinePhotoPicker)typeof(EarthlineUI).GetField("picker", Flags).GetValue(ui);
                Require(ui.Focus == EarthlineFocus.Photos && picker.AvailableCount == 26, "Picker exclusively focuses all unsent photos.");
                Require(root.Q("conversation-panel").resolvedStyle.display == DisplayStyle.None, "Picker hides chat and replies.");
                ui.SelectContact(2); Require(ui.ContactIndex == 1, "Photo picker prevents background contact changes.");
                ui.Navigate(0, -1); ui.Navigate(0, -1); ui.Navigate(0, -1);
                Require(picker.Highlighted == 12 && picker.TopRow == 1, "Moving below twelve photos advances exactly one row.");
                Require(root.Q("picker-tiles").childCount == 12, "Only three rows of four photos are rendered.");
                Require(root.Q<Label>(className: "picker-number").text == "005", "Photo numbering belongs to the filtered picker.");
                ui.Confirm(); Require(picker.SelectedCount == 1, "A toggles the highlighted photo.");
                picker.ToggleAll(); Require(picker.SelectedCount == 26, "Y selects every unsent photo, including offscreen rows.");
                picker.ToggleAll(); Require(picker.SelectedCount == 0, "Y deselects all photos.");
                ui.Confirm(); ui.SendSelectedPhotos(); Settle(root, new Vector2(1920, 1080));
                Require(ui.Focus == EarthlineFocus.Chat && SaveSystem.Current.photos[12].sent, "Sending closes the picker and marks the photo sent.");
                Require(SaveSystem.LoadOrCreate(SaveSystem.SavePath).conversations.Single().messages.Last().photoIndices.Single() == 12,
                    "Shared photos survive the binary reload.");
                var conversation = SaveSystem.Current.conversations.Single();
                conversation.messages.Add(new ChatMessage { fromPlayer = false, kind = MessageKind.Text, hour = 19, minute = 13,
                    text = "Thanks for sharing. These photographs will help us understand how the habitat is changing." });
                SaveSystem.SharePhotos(ContactId.Mara, new[] { 0, 1, 2, 3, 4, 5, 6 }, new DateTime(2026, 11, 4, 19, 14, 0));
                Settle(root, new Vector2(1920, 1080));
                var scroller = root.Q<ScrollView>("chat-scroll");
                Require(scroller.verticalScroller.worldBound.x < scroller.contentViewport.worldBound.x, "Chat scrollbar sits on the left.");
                Require(root.Query(className: "message-photo").ToList().Count == 8, "Photo batches render every shared image.");
                scroller.scrollOffset = new Vector2(0, Mathf.Max(0, scroller.verticalScroller.highValue));
                Settle(root, new Vector2(1920, 1080));
                Capture(root, target, "earthline-chat.png");
                ui.OpenPhotoPicker(); Settle(root, new Vector2(1920, 1080));
                Require(picker.AvailableCount == 18, "Every previously sent photo is excluded across contacts.");
                foreach (var tile in root.Q("picker-tiles").Children())
                {
                    var picture = tile.Q(className: "picker-picture");
                    Require(Mathf.Abs(picture.layout.width - picture.layout.height) <= 1.1f, "Picker image frames stay square: " + picture.layout.size);
                    Require(tile.worldBound.yMax <= root.Q<ScrollView>("picker-scroll").contentViewport.worldBound.yMax + 1,
                        "All twelve photo cards fit the visible picker.");
                }
                Capture(root, target, "earthline-photos.png");
                ui.ClosePhotoPicker(); Require(ui.Focus == EarthlineFocus.Chat, "B returns from picker to chat.");
                ui.Close(); Require(!EarthlineUI.BlocksGameplay && Time.timeScale == timeScale, "Closing restores gameplay and the original time scale.");
                File.WriteAllText(Path.Combine(Output, "checks.txt"), $"PASS: {checks} Earthline assertions.\n");
                Debug.Log($"EARTHLINE CHECKS PASSED: {checks} assertions. Screenshots: {Output}");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (clockObject != null) Object.DestroyImmediate(clockObject);
                if (panel != null) Object.DestroyImmediate(panel);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                EditorSceneManager.ClosePreviewScene(scene);
                typeof(GameMainUI).GetProperty("Active", Flags).SetValue(null, originalMain);
                SetCurrent(original);
                if (disk == null) File.Delete(SaveSystem.SavePath); else File.WriteAllBytes(SaveSystem.SavePath, disk);
                Time.timeScale = timeScale; UnityEngine.Cursor.lockState = cursorLock;
            }
        }

        private static GameSave Fixture(bool singleSpecies = false)
        {
            var save = new GameSave();

            for (int i = 0; i < 26; i++)
            {
                var species = i % 2 == 0 ? AnimalSpecies.AmericanBison : AnimalSpecies.PileatedWoodpecker;
                var entry = AnimalArchiveAssets.Active.Photos[species];
                var source = entry.States[0].Photos[0].Image;
                float side = Math.Min(source.width, source.height);
                var photo = new AnimalPhoto(source, new Rect(0, 0, side / source.width, side / source.height), 1,
                    new PhotoCaptureSettings("camera", "Field camera", new BasicPhotoRenderSnapshot("basic", 1, i, i == 0 ? 0 : 1)));
                var record = AnimalPhotoRecord.Capture(AnimalArchiveChecks.Snapshot(photo, new DateTime(2026, 11, 4, 18, i, 0)));
                record.species = singleSpecies ? AnimalSpecies.AmericanBison : species; record.displayName = entry.DisplayName; record.scientificName = entry.ScientificName; record.regionName = AnimalArchiveAssets.Active.Info[species].Region; save.photos.Add(record);
            }
            save.speciesProgress = save.photos.GroupBy(p => p.species).Select(group => new SpeciesProgress {
                species = group.Key, recognition = group.Sum(p => p.recognitionValue) }).ToList();
            return save;
        }

        private static void CheckMessages()
        {
            var save = SaveSystem.Current;
            var catalogRoot = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Prefabs/Resources/UI/AnimalCatalog/AnimalArchive.uxml").CloneTree();
            var catalog = new CatalogView(catalogRoot);
            catalog.Render(AnimalSpecies.AmericanBison);
            Require(catalogRoot.Q<Label>("catalog-name").text == AnimalArchiveAssets.Active.Photos[AnimalSpecies.AmericanBison].DisplayName,
                "Captured photographs identify the species before uploading.");
            var progress = save.Progress(AnimalSpecies.AmericanBison);
            int recognitionBeforeUpload = progress.recognition;
            Require(progress.Tier == 3 && progress.sentPhotos == 0, "Capture recognition unlocks all tiers without uploading.");
            var time = new DateTime(2026, 11, 4, 19, 11, 0);
            SaveSystem.SendText(ContactId.Lin, "Hello from Earth.", time);
            SaveSystem.SharePhotos(ContactId.Lin, new[] { 0 }, time);
            Require(progress.recognition == recognitionBeforeUpload && progress.sentPhotos == 1, "Uploading marks a photo sent without granting recognition again.");
            catalog.Render(AnimalSpecies.AmericanBison);
            Require(catalogRoot.Q<Label>("catalog-name").text == AnimalArchiveAssets.Active.Photos[AnimalSpecies.AmericanBison].DisplayName
                && !catalogRoot.Q<Label>("classification").text.Contains("More Photos Required"), "Uploading preserves captured species unlocks.");
            bool rejected = false;
            try { SaveSystem.SharePhotos(ContactId.Mara, new[] { 0 }, time); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && save.conversations.Count == 1, "A photograph cannot be uploaded to a second contact.");
            SaveSystem.SharePhotos(ContactId.Mara, Enumerable.Range(1, 6).ToArray(), time);
            Require(progress.Tier == 3 && progress.recognition == recognitionBeforeUpload, "Sharing color photos does not grant recognition again.");
            catalog.Render(AnimalSpecies.AmericanBison);
            Require(!catalogRoot.Q<Label>("classification").text.Contains("More Photos Required") && !catalogRoot.Q<Label>("lifespan").text.Contains("More Photos Required"),
                "All fields remain unlocked after sharing.");
            SaveSystem.SharePhotos(ContactId.CrewLounge, Enumerable.Range(7, 14).ToArray(), time);
            Require(progress.Tier == 3 && progress.recognition == recognitionBeforeUpload, "Further uploads preserve recognition.");
            catalog.Render(AnimalSpecies.AmericanBison);
            Require(!catalogRoot.Q<Label>("lifespan").text.Contains("Requires"), "Level three unlocks lifespan and the final fields.");
            var loaded = SaveSystem.LoadOrCreate(SaveSystem.SavePath);
            Require(loaded.conversations.Count == 3 && loaded.conversations[0].messages[0].text == "Hello from Earth.", "All contact threads and text round-trip.");
            var message = loaded.conversations[2].messages[0];
            Require(message.fromPlayer && message.hour == 19 && message.minute == 11 && message.photoIndices.SequenceEqual(Enumerable.Range(7, 14)),
                "Photo message ownership, hour, minute and indices round-trip.");
            string lastId = save.photos[20].id;
            SaveSystem.DeletePhoto(save.photos[1].id);
            Require(save.conversations[1].messages[0].photoIndices.SequenceEqual(Enumerable.Range(1, 5)), "Deleting a shared photo removes it and remaps subsequent indices.");
            Require(save.photos[save.conversations[2].messages[0].photoIndices.Last()].id == lastId, "Photo deletion preserves the identity of remaining historical attachments.");
            Require(SaveSystem.LoadOrCreate(SaveSystem.SavePath).photos.Count == 25, "Remapped photo messages remain readable from disk.");
            string legacy = Path.Combine(Output, "legacy-test.bin");
            try
            {
                using (var writer = new BinaryWriter(File.Create(legacy)))
                { writer.Write(0x41475356); writer.Write(1); writer.Write(0); writer.Write(0); }
                Require(SaveSystem.LoadOrCreate(legacy).conversations.Count == 0, "Version-one binary saves still load with empty conversations.");
            }
            finally { File.Delete(legacy); }
        }

        private static void SetCurrent(GameSave save) => typeof(SaveSystem).GetProperty("Current", Flags).SetValue(null, save);
        private static void Settle(VisualElement root, Vector2 size)
        {
            root.panel.GetType().GetMethod("Update", Flags, null, Type.EmptyTypes, null).Invoke(root.panel, null);
            root.style.width = size.x; root.style.height = size.y;
            for (int i = 0; i < 5; i++) root.panel.GetType().GetMethod("ValidateLayout", Flags).Invoke(root.panel, null);
        }
        private static void Capture(VisualElement root, RenderTexture target, string filename)
        {
            var previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = target; GL.Clear(true, true, Color.black);
                Type type = root.panel.GetType();
                root.MarkDirtyRepaint();
                for (int i = 0; i < 2; i++)
                {
                    type.GetMethod("Repaint", Flags, null, new[] { typeof(Event) }, null).Invoke(root.panel, new object[] { new Event { type = EventType.Repaint } });
                    type.GetMethod("Render", Flags, null, Type.EmptyTypes, null).Invoke(root.panel, null);
                }
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                Require(image.GetPixels32().Count(p => p.r > 120 || p.g > 120 || p.b > 120) > 1000, "Preview contains rendered Earthline UI: " + filename);
                File.WriteAllBytes(Path.Combine(Output, filename), image.EncodeToPNG());
            }
            finally { if (image != null) Object.DestroyImmediate(image); RenderTexture.active = previous; }
        }
    }
}

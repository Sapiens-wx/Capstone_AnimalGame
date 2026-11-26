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
        public static void Run() => Run(true);

        public static void RunAssertions() => Run(false);

        private static void Run(bool captureScreenshots)
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
            Contacts testContacts = null;
            ContactOptions dialogue = null;
            try
            {
                if (captureScreenshots) Directory.CreateDirectory(Output);
                SetCurrent(Fixture(true));
                CheckMessages();
                SetCurrent(new GameSave());
                CheckOpeningDialogue();
                SetCurrent(Fixture());
                clockObject = new GameObject("Earthline clock fixture"); clockObject.SetActive(false);
                SceneManager.MoveGameObjectToScene(clockObject, scene);
                var clock = clockObject.AddComponent<GameMainUI>();
                SaveSystem.Current.gameTime = new DateTime(2026, 11, 4, 19, 12, 0);
                typeof(GameMainUI).GetProperty("Active", Flags).SetValue(null, clock);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Resources/UI/Communication/EarthlineUI.prefab"), scene);
                var document = instance.GetComponent<UIDocument>();
                panel = Object.Instantiate(document.panelSettings);
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                target = new RenderTexture(1920, 1080, 24); target.Create();
                panel.targetTexture = target; document.panelSettings = panel;
                var ui = instance.GetComponent<EarthlineUI>();
                testContacts = Object.Instantiate(ui.contacts);
                dialogue = CreateMaraDialogue();
                foreach (var contact in testContacts.contacts) contact.options = null;
                testContacts.contacts[1].options = dialogue;
                ui.contacts = testContacts;
                ui.InitializeView();
                var root = document.rootVisualElement;
                ui.Open(); Settle(root, new Vector2(1920, 1080));
                Require(EarthlineUI.BlocksGameplay && GameMainUI.BlocksGameplay && Time.timeScale == 0,
                    "Opening Earthline pauses and blocks gameplay.");
                Require(ui.contacts.contacts.Count == 3 && ui.contacts.contacts.All(c => c.portrait != null), "All three contacts have editable sprite portraits.");
                Require(root.Q<CatalogHeadingElement>("contacts-heading").text == "CONTACTS & CHANNELS", "Contacts heading is present.");
                Require(ui.Focus == EarthlineFocus.Contacts, "Opening focuses contacts.");
                ui.Navigate(0, -1); Require(ui.ContactIndex == 1, "Down selects Mara.");
                var historyLabels = root.Q("chat-messages").Query<Label>(className: "message-body").ToList();
                Require(historyLabels.Select(label => label.text).SequenceEqual(new[] {
                    "Earlier transmission", "Second transmission", "Opening transmission" }),
                    "History follows the first child through the ordinary entry.");
                var historyTimes = root.Q("chat-messages").Children().Select(item => item.Q(className: "message-header").Children().Last())
                    .Cast<Label>().Select(label => label.text);
                Require(historyTimes.SequenceEqual(new[] { "2026.10.01 05:30", "2026.10.03 06:45", "2026.01.01 00:00" }),
                    "History uses positional timestamps and falls back to the initial game time.");
                Require(SaveSystem.Current.conversations.Count == 0, "The authored history is displayed without writing it to the save.");
                ui.Navigate(1, 0); Require(ui.Focus == EarthlineFocus.Chat, "Right focuses the chat.");
                Require(!root.Q("contacts-sidebar").ClassListContains("contacts-focused"), "Contact retains an unfocused selection style.");
                root.Q<ScrollView>("chat-scroll").scrollOffset = new Vector2(0, Math.Max(0, root.Q<ScrollView>("chat-scroll").verticalScroller.highValue));
                ui.Navigate(0, -1); Require(ui.Focus == EarthlineFocus.Replies && ui.ReplyIndex == 0, "Down at chat bottom focuses the first reply.");
                ui.Navigate(0, -1); Require(ui.ReplyIndex == 1, "Down selects another reply.");
                ui.Navigate(0, 1); ui.Navigate(0, 1); Require(ui.Focus == EarthlineFocus.Chat, "Up from the first reply returns to chat.");
                Require(!root.Q("reply-0").ClassListContains("reply-selected"), "Replies have no selection without focus.");
                Require(root.Q<Button>("reply-2").text.Contains("Third reply") && !root.Q<Button>("reply-2").text.Contains("Fourth reply"),
                    "The reply UI shows at most the first three children.");
                ui.Navigate(0, -1); ui.Confirm();
                var conversation = SaveSystem.Current.conversations.Single();
                Require(conversation.messages.Single().nodeId == 4 && root.Q<Button>("reply-0").text.Contains("Follow-up"),
                    "Sending a player message records its node and presents consecutive player replies.");
                ui.Confirm();
                Require(conversation.messages.Select(message => message.nodeId).SequenceEqual(new[] { 4, 8, 9, 10 })
                    && conversation.messages.All(message => message.sentAt == new DateTime(2026, 11, 4, 19, 12, 0)),
                    "A reply records consecutive NPC messages at the current game time.");
                Require(root.Q<Button>("reply-0").text.Contains("Continue later") && root.Q<Button>("reply-1").style.display.value == DisplayStyle.None,
                    "Unused reply buttons are hidden after the NPC chain.");
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
                Require(dialogue.GetCurrentNodeId(conversation) == 10, "Sharing photos preserves the current dialogue node.");
                SaveSystem.SharePhotos(ContactId.Mara, new[] { 0, 1, 2, 3, 4, 5, 6 }, new DateTime(2026, 11, 4, 19, 14, 0));
                Settle(root, new Vector2(1920, 1080));
                var scroller = root.Q<ScrollView>("chat-scroll");
                Require(scroller.verticalScroller.worldBound.x < scroller.contentViewport.worldBound.x, "Chat scrollbar sits on the left.");
                Require(root.Query(className: "message-photo").ToList().Count == 8, "Photo batches render every shared image.");
                scroller.scrollOffset = new Vector2(0, Mathf.Max(0, scroller.verticalScroller.highValue));
                Settle(root, new Vector2(1920, 1080));
                if (captureScreenshots) Capture(root, target, "earthline-chat.png");
                ui.OpenPhotoPicker(); Settle(root, new Vector2(1920, 1080));
                Require(picker.AvailableCount == 18, "Every previously sent photo is excluded across contacts.");
                foreach (var tile in root.Q("picker-tiles").Children())
                {
                    var picture = tile.Q(className: "picker-picture");
                    Require(Mathf.Abs(picture.layout.width - picture.layout.height) <= 1.1f, "Picker image frames stay square: " + picture.layout.size);
                    Require(tile.worldBound.yMax <= root.Q<ScrollView>("picker-scroll").contentViewport.worldBound.yMax + 1,
                        "All twelve photo cards fit the visible picker.");
                }
                if (captureScreenshots) Capture(root, target, "earthline-photos.png");
                ui.ClosePhotoPicker(); Require(ui.Focus == EarthlineFocus.Chat, "B returns from picker to chat.");
                ui.Close(); Require(!EarthlineUI.BlocksGameplay && Time.timeScale == timeScale, "Closing restores gameplay and the original time scale.");
                if (captureScreenshots) File.WriteAllText(Path.Combine(Output, "checks.txt"), $"PASS: {checks} Earthline assertions.\n");
                Debug.Log($"EARTHLINE CHECKS PASSED: {checks} assertions.");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (clockObject != null) Object.DestroyImmediate(clockObject);
                if (testContacts != null) Object.DestroyImmediate(testContacts);
                if (dialogue != null) Object.DestroyImmediate(dialogue);
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
            var options = ScriptableObject.CreateInstance<ContactOptions>();
            try
            {
                int entry = AddMessage(options, Sender.Npc, "Opening");
                int reply = AddMessage(options, Sender.Player, "Hello from Earth.");
                options.SetEntryNode(entry);
                options.TryConnect(entry, reply);
                SaveSystem.AdvanceConversation(new ContactDefinition { contact = ContactId.Lin, options = options }, time, reply);
            }
            finally { Object.DestroyImmediate(options); }
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
            var savedText = loaded.conversations[0].messages[0];
            Require(loaded.conversations.Count == 3 && savedText.kind == MessageKind.Text && savedText.nodeId == 2 && savedText.sentAt == time,
                "Contact threads, node IDs and full text timestamps round-trip.");
            var message = loaded.conversations[2].messages[0];
            Require(message.fromPlayer && message.hour == 19 && message.minute == 11 && message.photoIndices.SequenceEqual(Enumerable.Range(7, 14)),
                "Photo message ownership, hour, minute and indices round-trip.");
            string lastId = save.photos[20].id;
            SaveSystem.DeletePhoto(save.photos[1].id);
            Require(save.conversations[1].messages[0].photoIndices.SequenceEqual(Enumerable.Range(1, 5)), "Deleting a shared photo removes it and remaps subsequent indices.");
            Require(save.photos[save.conversations[2].messages[0].photoIndices.Last()].id == lastId, "Photo deletion preserves the identity of remaining historical attachments.");
            Require(SaveSystem.LoadOrCreate(SaveSystem.SavePath).photos.Count == 25, "Remapped photo messages remain readable from disk.");
            string legacy = Path.GetTempFileName();
            try
            {
                using (var writer = new BinaryWriter(File.Create(legacy)))
                { writer.Write(0x41475356); writer.Write(1); writer.Write(0); writer.Write(0); }
                Require(SaveSystem.LoadOrCreate(legacy).conversations.Count == 0, "Version-one binary saves still load with empty conversations.");
                using (var writer = new BinaryWriter(File.Create(legacy)))
                {
                    writer.Write(0x41475356); writer.Write(4); writer.Write(0); writer.Write(0);
                    writer.Write(1); writer.Write((int)ContactId.Lin); writer.Write(2);
                    writer.Write(true); writer.Write(19); writer.Write(11); writer.Write((int)MessageKind.Text);
                    writer.Write(true); writer.Write("A v4 text message without a node.");
                    writer.Write(true); writer.Write(19); writer.Write(12); writer.Write((int)MessageKind.Photos); writer.Write(0);
                    writer.Write(time.Ticks);
                }
                var migrated = SaveSystem.LoadOrCreate(legacy);
                Require(migrated.gameTime == GameSave.InitialGameTime + time.TimeOfDay
                    && migrated.conversations.Single().messages.Single().kind == MessageKind.Photos,
                    "V4 migration skips unbound text and preserves photo messages and time of day.");
            }
            finally { File.Delete(legacy); }
        }

        private static ContactOptions CreateMaraDialogue()
        {
            var options = ScriptableObject.CreateInstance<ContactOptions>();
            AddMessage(options, Sender.Npc, "Earlier transmission");
            AddMessage(options, Sender.Npc, "Second transmission");
            AddMessage(options, Sender.Npc, "Opening transmission");
            AddMessage(options, Sender.Player, "First reply");
            AddMessage(options, Sender.Player, "Second reply");
            AddMessage(options, Sender.Player, "Third reply");
            AddMessage(options, Sender.Player, "Fourth reply");
            AddMessage(options, Sender.Player, "Follow-up");
            AddMessage(options, Sender.Npc, "First response");
            AddMessage(options, Sender.Npc, "Second response");
            AddMessage(options, Sender.Player, "Continue later");
            options.SetEntryNode(3);
            options.SetHistoryEntryNode(1);
            options.TryConnect(1, 2);
            options.UpdateHistory();
            options.SetHistoryDate(0, new DateTime(2026, 10, 1, 5, 30, 0));
            options.SetHistoryDate(1, new DateTime(2026, 10, 3, 6, 45, 0));
            options.TryConnect(2, 3);
            for (int reply = 4; reply <= 7; reply++) options.TryConnect(3, reply);
            options.TryConnect(4, 8);
            options.TryConnect(8, 9);
            options.TryConnect(9, 10);
            options.TryConnect(10, 11);
            return options;
        }

        private static void CheckOpeningDialogue()
        {
            var options = ScriptableObject.CreateInstance<ContactOptions>();
            try
            {
                AddMessage(options, Sender.Npc, "Hidden entry");
                AddMessage(options, Sender.Npc, "First message");
                AddMessage(options, Sender.Npc, "Next message");
                AddMessage(options, Sender.Player, "Player choice");
                options.SetEntryNode(1);
                options.TryConnect(1, 2);
                options.TryConnect(1, 4);
                options.TryConnect(2, 3);
                options.TryConnect(3, 4);
                var contact = new ContactDefinition { contact = ContactId.Lin, options = options };
                var time = new DateTime(2026, 11, 4, 19, 12, 17);
                var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Prefabs/Resources/UI/Communication/Earthline.uxml").CloneTree();
                using var view = new EarthlineConversationView(root);
                view.Render(SaveSystem.Current, contact, time);
                Require(!root.Q("chat-messages").Query<Label>(className: "message-body").ToList().Any(),
                    "An ordinary entry alone is not displayed as authored history.");
                var hasPendingMessage = typeof(SaveSystem).GetMethod("HasPendingIncomingMessage", Flags);
                bool HasPendingMessage() => (bool)hasPendingMessage.Invoke(null, new object[] { contact });
                Require(HasPendingMessage(), "A new incoming NPC turn is pending before the contact is opened.");
                Require(SaveSystem.AdvanceConversation(contact, time), "Opening starts consecutive NPC messages.");
                Require(!HasPendingMessage(), "Delivered messages and a player-reply branch do not retain the new message reminder.");
                var thread = SaveSystem.Current.conversations.Single();
                Require(thread.messages.Select(message => message.nodeId).SequenceEqual(new[] { 2, 3 })
                    && thread.messages.All(message => message.sentAt.Second == 0),
                    "Opening ignores the secondary branch, omits the hidden entry, and records minute-precision timestamps.");
                Require(!SaveSystem.AdvanceConversation(contact, time) && options.GetCurrentNodeId(thread) == 3,
                    "Reopening stops at the same player decision.");
                view.Render(SaveSystem.Current, contact, time);
                Require(root.Q("chat-messages").Query<Label>(className: "message-body").ToList()
                    .Select(label => label.text).SequenceEqual(new[] { "First message", "Next message" }),
                    "Without authored history the view reads only saved messages.");
                options.TryConnect(3, 1);
                Require(options.GetReplyNodeIds(3).SequenceEqual(new[] { 4, 1 }),
                    "When the first child belongs to the player, the first three positions are offered as authored.");
                options.Disconnect(3, 4);
                Require(!HasPendingMessage() && !SaveSystem.AdvanceConversation(contact, time) && thread.messages.Count == 2,
                    "Reopening an NPC-only cycle does not replay the hidden anchor or earlier NPC messages.");
                options.Disconnect(3, 1);
                options.TryConnect(3, AddMessage(options, Sender.Npc, "A newly available transmission"));
                Require(HasPendingMessage(), "A new incoming message reactivates the reminder after the previous turn was delivered.");
            }
            finally { Object.DestroyImmediate(options); }
        }

        private static int AddMessage(ContactOptions options, Sender sender, string message)
        {
            int id = options.AddNode(sender, Vector2.zero);
            options.SetMessage(id, message);
            return id;
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

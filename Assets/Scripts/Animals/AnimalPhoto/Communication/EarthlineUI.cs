using System;
using System.Collections.Generic;
using AnimalGame.Animals.UI;
using AnimalGame.Interaction;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
using GameMainUI = AnimalGame.MainUI.MainUI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.Animals.Communication
{
    public enum EarthlineFocus { Contacts, Chat, Replies, Photos }

    [DefaultExecutionOrder(420)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class EarthlineUI : MonoBehaviour
    {
        public Contacts contacts;
        public static bool BlocksGameplay { get; private set; }
        internal bool IsOpen => isOpen;
        internal bool HasPendingIncomingMessages => contacts.contacts.Exists(SaveSystem.HasPendingIncomingMessage);
        public EarthlineFocus Focus { get; private set; }
        public int ContactIndex { get; private set; }
        public int ReplyIndex { get; private set; }
        private readonly Button[] replyButtons = new Button[ContactOptions.MaxReplies];
        private readonly List<int> replyNodeIds = new();
        private VisualElement root;
        private EarthlineConversationView conversation;
        private EarthlinePhotoPicker picker;
        private readonly List<Button> contactItems = new();
        private float savedTimeScale, nextMove;
        private CursorLockMode savedCursorLock;
        private Vector2 previousDirection;
        private bool isOpen;
        private CommunicationsStation connectedStation;
        private FacilityWindowTransition transition;
        private int openedFrame;
        private ContactDefinition SelectedContact => contacts.contacts[ContactIndex];
        private DateTime GameTime => GameMainUI.Active.CurrentGameTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => BlocksGameplay = false;
        private void Start() => InitializeView();

        public void InitializeView()
        {
            if (root != null) return;
            SaveSystem.Initialize();
            root = GetComponent<UIDocument>().rootVisualElement;
            root.style.display = DisplayStyle.None;
            root.Query<Button>().ForEach(button => button.focusable = false);
            root.RegisterCallback<NavigationMoveEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            conversation = new EarthlineConversationView(root); picker = new EarthlinePhotoPicker(root);
            var list = root.Q<ScrollView>("contacts-list");
            for (int i = 0; i < contacts.contacts.Count; i++)
            {
                int index = i; var contact = contacts.contacts[i];
                var item = new Button(() => SelectContact(index)) { focusable = false }; item.AddToClassList("contact-item");
                var portrait = new Image { sprite = contact.portrait, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                portrait.AddToClassList("contact-avatar"); item.Add(portrait);
                var details = new VisualElement { pickingMode = PickingMode.Ignore }; details.AddToClassList("contact-details");
                var name = new Label(contact.Name); name.AddToClassList("contact-item-name"); details.Add(name);
                var profession = new Label(contact.profession); profession.AddToClassList("contact-item-profession"); details.Add(profession);
                item.Add(details); list.Add(item); contactItems.Add(item);
            }
            for (int i = 0; i < replyButtons.Length; i++)
            {
                int index = i;
                var button = replyButtons[i] = root.Q<Button>("reply-" + i);
                button.enableRichText = false;
                button.clicked += () =>
                {
                    if (Focus == EarthlineFocus.Photos || index >= replyNodeIds.Count) return;
                    ReplyIndex = index;
                    SetFocus(EarthlineFocus.Replies);
                    Confirm();
                };
            }
            root.Q<Button>("share-photo").clicked += OpenPhotoPicker;
            root.Q<Button>("picker-close").clicked += ClosePhotoPicker;
            root.Q<Button>("picker-select-all").clicked += () => picker.ToggleAll();
            root.Q<Button>("picker-send").clicked += SendSelectedPhotos;
            root.Q("chat-scroll").RegisterCallback<PointerDownEvent>(_ => { if (Focus != EarthlineFocus.Photos) SetFocus(EarthlineFocus.Chat); });
            SaveSystem.Changed += Refresh;
        }

        public void Open() => OpenFromStation(null, null);

        internal bool OpenFromStation(CommunicationsStation station, Vector3? socketWorld)
        {
            if (isOpen || GameMainUI.Active == null || GameMainUI.BlocksGameplay || AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels) return false;
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap != null && (bootstrap.photoMode.IsActive || bootstrap.photoResultView.IsShowing
                || AnimalGame.Tutorial.TutorialTipOverLesson.BlocksGameplayForReading(bootstrap.robotObject))) return false;
            InitializeView();
            savedTimeScale = Time.timeScale; savedCursorLock = Cursor.lockState;
            isOpen = BlocksGameplay = true; Time.timeScale = 0; Cursor.lockState = CursorLockMode.None;
            openedFrame = Time.frameCount;
            root.style.display = DisplayStyle.Flex;
            ContactIndex = Mathf.Clamp(ContactIndex, 0, contacts.contacts.Count - 1);
            ReplyIndex = 0; previousDirection = Vector2.zero; nextMove = Time.unscaledTime + .2f;
            root.Q("conversation-panel").style.display = DisplayStyle.Flex;
            root.Q("photo-picker").style.display = DisplayStyle.None;
            SetFocus(EarthlineFocus.Contacts);
            OpenConversation();
            connectedStation = station;
            if (socketWorld.HasValue)
                transition = new FacilityWindowTransition(root, root.Q("earthline-viewport"), socketWorld.Value);
            return true;
        }

        public void Close()
        {
            if (transition != null) transition.Close(FinishInteraction);
            else FinishInteraction();
        }

        internal void CloseFor(CommunicationsStation station)
        {
            if (connectedStation == station) FinishClose();
        }

        private void FinishInteraction()
        {
            if (!isOpen) return;
            CommunicationsStation station = connectedStation;
            FinishClose();
            if (station != null) station.ReportInteractionCompleted();
        }

        private void FinishClose()
        {
            if (!isOpen) return;
            transition?.Dispose();
            transition = null;
            connectedStation = null;
            isOpen = BlocksGameplay = false;
            if (Time.timeScale == 0) Time.timeScale = savedTimeScale;
            Cursor.lockState = savedCursorLock;
            root.style.display = DisplayStyle.None;
            picker.Clear(); conversation.Clear();
        }

        public void SelectContact(int index)
        {
            if (Focus == EarthlineFocus.Photos) return;
            int selectedIndex = Mathf.Clamp(index, 0, contacts.contacts.Count - 1);
            bool changed = ContactIndex != selectedIndex;
            ContactIndex = selectedIndex;
            ReplyIndex = 0;
            SetFocus(EarthlineFocus.Contacts);
            if (isOpen && changed) OpenConversation();
            root.Q<ScrollView>("contacts-list").ScrollTo(contactItems[ContactIndex]);
        }

        private void OpenConversation()
        {
            if (!SaveSystem.AdvanceConversation(SelectedContact, GameTime)) Refresh();
        }

        private void Refresh()
        {
            if (!isOpen) return;
            var contact = SelectedContact;
            root.Q<Image>("contact-portrait").sprite = contact.portrait;
            root.Q<Label>("contact-name").text = contact.Name;
            root.Q<Label>("contact-profession").text = contact.profession;
            conversation.Render(SaveSystem.Current, contact, GameTime);
            UpdateReplies(contact);
            UpdateFocus();
        }

        private void UpdateReplies(ContactDefinition contact)
        {
            replyNodeIds.Clear();
            var options = contact.options;
            if (options != null)
            {
                var thread = SaveSystem.Current.conversations.Find(c => c.contact == contact.contact);
                replyNodeIds.AddRange(options.GetReplyNodeIds(options.GetCurrentNodeId(thread)));
            }
            ReplyIndex = Mathf.Clamp(ReplyIndex, 0, Mathf.Max(0, replyNodeIds.Count - 1));
            if (replyNodeIds.Count == 0 && Focus == EarthlineFocus.Replies) Focus = EarthlineFocus.Chat;
            for (int i = 0; i < replyButtons.Length; i++)
            {
                bool available = i < replyNodeIds.Count;
                var button = replyButtons[i];
                button.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
                button.text = available ? $"{i + 1:00}   {options.GetNode(replyNodeIds[i]).Message}" : "";
            }
        }

        private void SetFocus(EarthlineFocus focus) { Focus = focus; UpdateFocus(); }
        private void UpdateFocus()
        {
            root.Q("contacts-sidebar").EnableInClassList("contacts-focused", Focus == EarthlineFocus.Contacts);
            for (int i = 0; i < contactItems.Count; i++) contactItems[i].EnableInClassList("current", i == ContactIndex);
            for (int i = 0; i < replyButtons.Length; i++)
                replyButtons[i].EnableInClassList("reply-selected", Focus == EarthlineFocus.Replies && i == ReplyIndex);
            conversation.Focus(Focus == EarthlineFocus.Chat);
        }

        public void Navigate(int x, int y)
        {
            if (Focus == EarthlineFocus.Photos) { picker.Move(x, y); return; }
            if (Focus == EarthlineFocus.Contacts)
            {
                if (x > 0) SetFocus(EarthlineFocus.Chat);
                else if (y != 0) SelectContact(ContactIndex - y);
            }
            else if (Focus == EarthlineFocus.Chat)
            {
                if (x < 0) SetFocus(EarthlineFocus.Contacts);
                else if (y < 0 && conversation.AtBottom && replyNodeIds.Count > 0) { ReplyIndex = 0; SetFocus(EarthlineFocus.Replies); }
                else if (y != 0) conversation.Scroll(y);
            }
            else
            {
                if (x < 0) SetFocus(EarthlineFocus.Contacts);
                else if (y > 0 && ReplyIndex == 0) SetFocus(EarthlineFocus.Chat);
                else if (y != 0) { ReplyIndex = Mathf.Clamp(ReplyIndex - y, 0, replyNodeIds.Count - 1); UpdateFocus(); }
            }
        }

        public void Confirm()
        {
            if (!isOpen) return;
            if (Focus == EarthlineFocus.Photos) picker.ToggleHighlighted();
            else if (Focus == EarthlineFocus.Replies && ReplyIndex < replyNodeIds.Count)
            {
                int id = replyNodeIds[ReplyIndex];
                ReplyIndex = 0;
                if (!SaveSystem.AdvanceConversation(SelectedContact, GameTime, id)) Refresh();
            }
        }

        public void OpenPhotoPicker()
        {
            if (!isOpen || Focus == EarthlineFocus.Photos) return;
            root.Q("conversation-panel").style.display = DisplayStyle.None;
            root.Q("photo-picker").style.display = DisplayStyle.Flex;
            SetFocus(EarthlineFocus.Photos); picker.Bind(SaveSystem.Current, SelectedContact.Name);
        }
        public void ClosePhotoPicker()
        {
            if (Focus != EarthlineFocus.Photos) return;
            picker.Clear(); root.Q("photo-picker").style.display = DisplayStyle.None;
            root.Q("conversation-panel").style.display = DisplayStyle.Flex;
            SetFocus(EarthlineFocus.Chat); Refresh();
        }
        public void SendSelectedPhotos()
        {
            if (Focus != EarthlineFocus.Photos || picker.SelectedCount == 0) return;
            SaveSystem.SharePhotos(SelectedContact.contact, picker.SelectedIndices, GameTime);
            ClosePhotoPicker();
        }

        private void Update()
        {
            if (transition != null && transition.BlocksInput)
            {
                transition.Tick(Time.unscaledDeltaTime);
                return;
            }
            if (Input.GetKeyDown(KeyCode.L))
            {
                if (isOpen) Close();
                else if (!HeightMapPlayerSceneBootstrap.StationPlugOwnsInput) Open();
                return;
            }
            if (!isOpen || openedFrame == Time.frameCount) return;
            bool confirm = Input.GetKeyDown(KeyCode.Return), back = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.B);
            bool y = Input.GetKeyDown(KeyCode.Y), x = Input.GetKeyDown(KeyCode.X);
            Vector2 direction = AdaptiveLegacyGamepadInput.ReadLeftStick();
            if (Input.GetKey(KeyCode.LeftArrow)) direction.x = -1;
            if (Input.GetKey(KeyCode.RightArrow)) direction.x = 1;
            if (Input.GetKey(KeyCode.UpArrow)) direction.y = 1;
            if (Input.GetKey(KeyCode.DownArrow)) direction.y = -1;
#if ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.dpad.ReadValue().sqrMagnitude > .1f) direction = pad.dpad.ReadValue();
                confirm |= pad.buttonSouth.wasPressedThisFrame; back |= pad.buttonEast.wasPressedThisFrame;
                y |= pad.buttonNorth.wasPressedThisFrame; x |= pad.buttonWest.wasPressedThisFrame;
            }
            else
#endif
            {
                confirm |= Input.GetKeyDown(KeyCode.JoystickButton0);
                back |= AdaptiveLegacyGamepadInput.WasEastFaceButtonPressedThisFrame();
                y |= AdaptiveLegacyGamepadInput.WasNorthFaceButtonPressedThisFrame();
                x |= AdaptiveLegacyGamepadInput.WasWestFaceButtonPressedThisFrame();
            }
            if (back) { if (Focus == EarthlineFocus.Photos) ClosePhotoPicker(); else Close(); return; }
            if (y) { if (Focus == EarthlineFocus.Photos) picker.ToggleAll(); else OpenPhotoPicker(); return; }
            if (x && Focus == EarthlineFocus.Photos) { SendSelectedPhotos(); return; }
            if (confirm) { Confirm(); return; }
            // Chat scrolling uses analog input every frame, before menu repeat/sign conversion.
            if (Focus == EarthlineFocus.Chat && Mathf.Abs(direction.y) > .1f
                && Mathf.Abs(direction.y) > Mathf.Abs(direction.x)
                && (direction.y > 0 || !conversation.AtBottom))
            {
                conversation.Scroll(direction.y);
                previousDirection = new Vector2(0, Mathf.Sign(direction.y));
                nextMove = Time.unscaledTime + .15f;
                return;
            }
            if (direction.magnitude < .5f) { previousDirection = Vector2.zero; nextMove = 0; return; }
            direction = Mathf.Abs(direction.x) > Mathf.Abs(direction.y)
                ? new Vector2(Mathf.Sign(direction.x), 0) : new Vector2(0, Mathf.Sign(direction.y));
            if (direction == previousDirection && Time.unscaledTime < nextMove) return;
            nextMove = Time.unscaledTime + (direction == previousDirection ? .15f : .35f); previousDirection = direction;
            Navigate((int)direction.x, (int)direction.y);
        }

        private void OnDisable() => FinishClose();
        private void OnDestroy()
        {
            FinishClose(); SaveSystem.Changed -= Refresh; picker?.Dispose(); conversation?.Dispose();
        }
    }
}

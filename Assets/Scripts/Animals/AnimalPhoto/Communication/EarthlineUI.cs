using System;
using System.Collections.Generic;
using AnimalGame.Animals.UI;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Save;
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
        public EarthlineFocus Focus { get; private set; }
        public int ContactIndex { get; private set; }
        public int ReplyIndex { get; private set; }
        private static readonly string[] Replies = {
            "What are you all doing tonight?", "What would you like to see from Earth?", "I'll check in again after my walk." };
        private VisualElement root;
        private EarthlineConversationView conversation;
        private EarthlinePhotoPicker picker;
        private readonly List<Button> contactItems = new();
        private float savedTimeScale, nextMove;
        private CursorLockMode savedCursorLock;
        private Vector2 previousDirection;
        private bool isOpen;
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
            for (int i = 0; i < Replies.Length; i++)
            {
                int index = i;
                root.Q<Button>("reply-" + i).text = $"{i + 1:00}   {Replies[i]}";
                root.Q<Button>("reply-" + i).clicked += () => { ReplyIndex = index; SetFocus(EarthlineFocus.Replies); Confirm(); };
            }
            root.Q<Button>("share-photo").clicked += OpenPhotoPicker;
            root.Q<Button>("picker-close").clicked += ClosePhotoPicker;
            root.Q<Button>("picker-select-all").clicked += () => picker.ToggleAll();
            root.Q<Button>("picker-send").clicked += SendSelectedPhotos;
            root.Q("chat-scroll").RegisterCallback<PointerDownEvent>(_ => { if (Focus != EarthlineFocus.Photos) SetFocus(EarthlineFocus.Chat); });
            SaveSystem.Changed += Refresh;
        }

        public void Open()
        {
            if (isOpen || GameMainUI.Active == null || GameMainUI.BlocksGameplay || AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels) return;
            var bootstrap = HeightMapPlayerSceneBootstrap.inst;
            if (bootstrap != null && (bootstrap.photoMode.IsActive || bootstrap.photoResultView.IsShowing
                || AnimalGame.Tutorial.TutorialTipOverLesson.BlocksGameplayForReading(bootstrap.robotObject))) return;
            InitializeView();
            savedTimeScale = Time.timeScale; savedCursorLock = Cursor.lockState;
            isOpen = BlocksGameplay = true; Time.timeScale = 0; Cursor.lockState = CursorLockMode.None;
            root.style.display = DisplayStyle.Flex;
            ContactIndex = Mathf.Clamp(ContactIndex, 0, contacts.contacts.Count - 1);
            ReplyIndex = 0; previousDirection = Vector2.zero; nextMove = Time.unscaledTime + .2f;
            root.Q("conversation-panel").style.display = DisplayStyle.Flex;
            root.Q("photo-picker").style.display = DisplayStyle.None;
            SetFocus(EarthlineFocus.Contacts); Refresh();
        }

        public void Close()
        {
            if (!isOpen) return;
            isOpen = BlocksGameplay = false;
            if (Time.timeScale == 0) Time.timeScale = savedTimeScale;
            Cursor.lockState = savedCursorLock;
            root.style.display = DisplayStyle.None;
            picker.Clear(); conversation.Clear();
        }

        public void SelectContact(int index)
        {
            if (Focus == EarthlineFocus.Photos) return;
            ContactIndex = Mathf.Clamp(index, 0, contacts.contacts.Count - 1);
            SetFocus(EarthlineFocus.Contacts); Refresh();
            root.Q<ScrollView>("contacts-list").ScrollTo(contactItems[ContactIndex]);
        }

        private void Refresh()
        {
            if (!isOpen) return;
            var contact = SelectedContact;
            root.Q<Image>("contact-portrait").sprite = contact.portrait;
            root.Q<Label>("contact-name").text = contact.Name;
            root.Q<Label>("contact-profession").text = contact.profession;
            conversation.Render(SaveSystem.Current, contact, GameTime);
            UpdateFocus();
        }

        private void SetFocus(EarthlineFocus focus) { Focus = focus; UpdateFocus(); }
        private void UpdateFocus()
        {
            root.Q("contacts-sidebar").EnableInClassList("contacts-focused", Focus == EarthlineFocus.Contacts);
            for (int i = 0; i < contactItems.Count; i++) contactItems[i].EnableInClassList("current", i == ContactIndex);
            for (int i = 0; i < Replies.Length; i++)
                root.Q("reply-" + i).EnableInClassList("reply-selected", Focus == EarthlineFocus.Replies && i == ReplyIndex);
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
                else if (y < 0 && conversation.AtBottom) { ReplyIndex = 0; SetFocus(EarthlineFocus.Replies); }
                else if (y != 0) conversation.Scroll(y);
            }
            else
            {
                if (x < 0) SetFocus(EarthlineFocus.Contacts);
                else if (y > 0 && ReplyIndex == 0) SetFocus(EarthlineFocus.Chat);
                else if (y != 0) { ReplyIndex = Mathf.Clamp(ReplyIndex - y, 0, Replies.Length - 1); UpdateFocus(); }
            }
        }

        public void Confirm()
        {
            if (Focus == EarthlineFocus.Photos) picker.ToggleHighlighted();
            else if (Focus == EarthlineFocus.Replies) SaveSystem.SendText(SelectedContact.contact, Replies[ReplyIndex], GameTime);
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
            if (Input.GetKeyDown(KeyCode.L)) { if (isOpen) Close(); else Open(); return; }
            if (!isOpen) return;
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

        private void OnDisable() => Close();
        private void OnDestroy()
        {
            Close(); SaveSystem.Changed -= Refresh; picker?.Dispose(); conversation?.Dispose();
        }
    }
}

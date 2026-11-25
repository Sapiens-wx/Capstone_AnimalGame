using System;
using System.IO;
using AnimalGame.Interaction;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using AnimalGame.World.Map;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace AnimalGame.World
{
    [DefaultExecutionOrder(-90)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class StationLogUI : MonoBehaviour
    {
        private const float SyncDuration = .2f;
        private static StationLogUI active;
        private static int closedFrame = -1;
        public static bool BlocksGameplay => active != null || closedFrame == Time.frameCount;

        private StationLogConsole station;
        internal StationLogConsole Station => station;
        private VisualElement root, syncProgress;
        private FacilityWindowTransition transition;
        private ScrollView list, body;
        private Button sync;
        private Label syncStatus, syncLabel;
        private int selectedIndex, heldDirection;
        private float nextStep, syncElapsed, previousTimeScale;
        private bool syncing, previousCursorVisible;
        private CursorLockMode previousCursorLock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { active = null; closedFrame = -1; }

        // Each physical console retains its reading position between visits.
        internal static bool Open(StationLogConsole source, Vector3? socketWorld)
        {
            if (BlocksGameplay) return false;
            var view = source.GetComponentInChildren<StationLogUI>(true);
            if (view == null)
            {
                var host = new GameObject("Station Logs UI");
                host.SetActive(false);
                host.transform.SetParent(source.transform, false);
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("UI/AnimalCatalog/AnimalArchivePanel");
                document.sortingOrder = 1;
                view = host.AddComponent<StationLogUI>();
                view.station = source;
            }
            view.gameObject.SetActive(true);
            view.Show(socketWorld);
            return true;
        }

        internal static void CloseFor(StationLogConsole source)
        {
            if (IsOpenFor(source)) active.CloseImmediately();
        }

        internal static bool IsOpenFor(StationLogConsole source) => active != null && active.station == source;

        private void Show(Vector3? socketWorld)
        {
            var documentRoot = GetComponent<UIDocument>().rootVisualElement;
            documentRoot.Clear();
            Resources.Load<VisualTreeAsset>("UI/StationLogs/StationLogs").CloneTree(documentRoot);
            root = documentRoot.Q("station-logs");
            if (socketWorld.HasValue)
                transition = new FacilityWindowTransition(documentRoot, root, socketWorld.Value);
            root.Q<Label>("station-type").text = station.StationName;
            root.Q<Label>("station-code").text = station.StationId;
            root.Q<Label>("sync-station-name").text = $"{station.StationName} {station.StationId}";
            list = root.Q<ScrollView>("record-list");
            body = root.Q<ScrollView>("log-body");
            sync = root.Q<Button>("sync");
            syncProgress = root.Q("sync-progress");
            syncStatus = root.Q<Label>("sync-status");
            syncLabel = root.Q<Label>("sync-label");
            sync.clicked += SyncStation;
            root.Q<Button>("close").clicked += Close;
            BuildList();
            ShowSelection();
            ShowSyncState(SaveSystem.IsStationSynced(station.StationId));
            // Gamepad/keyboard navigation is handled below. Pointer clicks still use Toolkit.
            root.Query<Button>().ForEach(button => button.focusable = false);
            root.RegisterCallback<NavigationMoveEvent>(evt => evt.StopPropagation(), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopPropagation(), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(evt => evt.StopPropagation(), TrickleDown.TrickleDown);
            heldDirection = 0;
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            active = this;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void BuildList()
        {
            int count = station.Entries.Count;
            root.Q<Label>("record-count").text = count == 1 ? "1 record" : $"{count} records";
            for (int i = 0; i < count; i++)
            {
                int index = i;
                StationLogEntry entry = station.Entries[i];
                var row = new Button(() => SelectRecord(index));
                row.AddToClassList("record");
                row.Add(Text(entry.id, "record-number"));
                var copy = new VisualElement { pickingMode = PickingMode.Ignore };
                copy.AddToClassList("record-copy");
                copy.Add(Text(entry.title, "record-title"));
                copy.Add(Text(entry.timestamp, "record-date"));
                row.Add(copy);
                list.Add(row);
            }
        }

        private static Label Text(string text, string styleClass)
        {
            var label = new Label(text) { enableRichText = false, pickingMode = PickingMode.Ignore };
            label.AddToClassList(styleClass);
            return label;
        }

        private void SelectRecord(int index)
        {
            if (index < 0 || index >= station.Entries.Count || index == selectedIndex) return;
            selectedIndex = index;
            ShowSelection();
        }

        private void ShowSelection()
        {
            if (station.Entries.Count == 0)
            {
                root.Q<Label>("detail-title").text = "No stored logs";
                return;
            }
            var entry = station.Entries[selectedIndex];
            root.Q<Label>("record-id").text = $"RECORD {entry.id}";
            root.Q<Label>("detail-title").text = entry.title;
            root.Q<Label>("detail-date").text = entry.timestamp;
            for (int i = 0; i < list.childCount; i++)
                list[i].EnableInClassList("record-selected", i == selectedIndex);
            var row = list[selectedIndex];
            list.schedule.Execute(() => list.ScrollTo(row));
            body.Clear();
            foreach (string paragraph in entry.body.Split(new[] { "\n\n" }, StringSplitOptions.None))
                body.Add(Text(paragraph, "log-paragraph"));
            body.scrollOffset = Vector2.zero;
        }

        private void SyncStation()
        {
            if (!sync.enabledSelf) return;
            syncing = true;
            syncElapsed = 0f;
            sync.SetEnabled(false);
            sync.AddToClassList("sync-busy");
            syncLabel.text = "Syncing…";
            syncStatus.text = "Syncing station data…";
        }

        private void OpenSurveyMap()
        {
            var map = WorldMapUI.Instance;
            try
            {
                if (map != null && map.OpenMap(this))
                {
                    ShowSyncState(true);
                    return;
                }
                ShowSyncState(SaveSystem.IsStationSynced(station.StationId));
                if (sync.enabledSelf) syncStatus.text = "Station map unavailable. Select Sync to retry.";
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogException(error, this);
                ShowSyncState(false);
                syncStatus.text = "Could not save station data. Select Sync to retry.";
            }
        }

        internal void ReturnFromMap()
        {
            heldDirection = 0;
            root.style.display = DisplayStyle.Flex;
        }

        private void ShowSyncState(bool complete)
        {
            syncing = false;
            sync.RemoveFromClassList("sync-busy");
            sync.EnableInClassList("sync-complete", complete);
            sync.SetEnabled(!complete);
            syncProgress.style.width = Length.Percent(0);
            syncLabel.text = complete ? "Station Synced" : "Sync Station Data";
            syncStatus.text = complete ? "Station data synced" : "Ready to sync station data";
        }

        private void Update()
        {
            if (active != this || WorldMapUI.BlocksGameplay) return;
            if (transition != null && transition.BlocksInput)
            {
                transition.Tick(Time.unscaledDeltaTime);
                return;
            }
            if (syncing)
            {
                syncElapsed += Time.unscaledDeltaTime;
                syncProgress.style.width = Length.Percent(100f * Mathf.Clamp01(syncElapsed / SyncDuration));
                if (syncElapsed >= SyncDuration) OpenSurveyMap();
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || AdaptiveLegacyGamepadInput.WasEastFaceButtonPressedThisFrame())
            {
                Close();
                return;
            }
            if ((keyboard != null && keyboard.sKey.wasPressedThisFrame)
                || AdaptiveLegacyGamepadInput.WasNorthFaceButtonPressedThisFrame())
                SyncStation();

            float vertical = AdaptiveLegacyGamepadInput.ReadLeftStick().y;
            var pad = Gamepad.current;
            if (pad != null && pad.dpad.y.ReadValue() != 0) vertical = pad.dpad.y.ReadValue();
            if (keyboard != null && keyboard.upArrowKey.isPressed) vertical = 1f;
            if (keyboard != null && keyboard.downArrowKey.isPressed) vertical = -1f;
            int direction = Mathf.Abs(vertical) < .5f ? 0 : vertical > 0f ? -1 : 1;
            if (direction != 0 && (direction != heldDirection || Time.unscaledTime >= nextStep))
            {
                SelectRecord(selectedIndex + direction);
                nextStep = Time.unscaledTime + (direction == heldDirection ? .15f : .35f);
            }
            heldDirection = direction;
            float scroll = -AdaptiveLegacyGamepadInput.ReadRightStick().y;
            if (keyboard != null && keyboard.pageDownKey.isPressed) scroll = 1f;
            if (keyboard != null && keyboard.pageUpKey.isPressed) scroll = -1f;
            if (Mathf.Abs(scroll) > .15f)
                body.verticalScroller.value += scroll * body.contentViewport.layout.height * Time.unscaledDeltaTime;
        }

        private void Close()
        {
            if (transition != null) transition.Close(CloseImmediately);
            else CloseImmediately();
        }

        private void CloseImmediately() => gameObject.SetActive(false);

        private void OnDisable()
        {
            if (active != this) return;
            if (WorldMapUI.Instance != null) WorldMapUI.Instance.CloseFor(this);
            active = null;
            closedFrame = Time.frameCount;
            transition?.Dispose();
            transition = null;
            if (Time.timeScale == 0f) Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
    }
}

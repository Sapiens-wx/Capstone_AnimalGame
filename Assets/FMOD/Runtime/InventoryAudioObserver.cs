using AnimalGame.MainUI;
using AnimalGame.MainUI.Inventory;
using UnityEngine;
using InventoryUI = AnimalGame.MainUI.MainUI;

namespace Capstone.Audio
{
    // Read-only observation of the existing controller and committed store.
    // Fixed eight-slot snapshots; no input reads, UI callbacks or frame allocations.
    internal sealed class InventoryAudioObserver
    {
        private readonly InventoryAudioVoices voices;
        private readonly InventoryItem[] beforeItems = new InventoryItem[InventoryController.SlotCount];
        private readonly int[] beforeQuantities = new int[InventoryController.SlotCount];
        private readonly InventoryItem[] observedItems = new InventoryItem[InventoryController.SlotCount];
        private readonly int[] observedQuantities = new int[InventoryController.SlotCount];
        private InventoryUI source;
        private InventoryStore store;
        private InventoryInteractionState previousState;
        private bool previousOpen, previousReady, moving;
        private float previousProgress;
        private int previousSelection, moveSource, confirmedFrame = -1;

        internal InventoryAudioObserver(InventoryAudioVoices voices) { this.voices = voices; }
        internal void Bind(InventoryUI next)
        {
            if (ReferenceEquals(source, next)) return;
            if (store != null) store.Changed -= OnStoreChanged;
            voices.StopAll();
            source = next;
            store = null;
            moving = false;
            confirmedFrame = -1;
            System.Array.Clear(beforeItems, 0, beforeItems.Length);
            System.Array.Clear(observedItems, 0, observedItems.Length);
            if (source == null) return;
            store = source.Inventory;
            for (int i = 0; i < observedItems.Length; i++)
            {
                observedItems[i] = store.Items[i];
                observedQuantities[i] = store.Quantity(i);
            }
            store.Changed += OnStoreChanged;
            // Binding, source replacement and pause recovery never synthesize actions.
            previousOpen = source.IsInventoryOpen;
            previousProgress = source.InventoryProgress;
            previousReady = Ready;
            previousState = source.InteractionState;
            previousSelection = source.SelectedIndex;
        }
        private bool Ready => source.IsInventoryOpen && source.InventoryProgress >= .98f;

        internal void Tick()
        {
            if (source == null) return;
            bool open = source.IsInventoryOpen;
            float progress = source.InventoryProgress;
            bool ready = Ready;
            InventoryInteractionState state = source.InteractionState;
            int selected = source.SelectedIndex;
            if (open != previousOpen)
            {
                moving = false;
                voices.Stop(open ? InventoryCue.Close : InventoryCue.Open);
                voices.Play(open ? InventoryCue.Open : InventoryCue.Close);
            }
            // Follow the real animation endpoint. A reversal stops the old direction.
            if (open && progress >= 1f && previousProgress < 1f) voices.Stop(InventoryCue.Open);
            if (!open && progress <= 0f && previousProgress > 0f) voices.Stop(InventoryCue.Close);
            if (ready && state == InventoryInteractionState.Move && previousState != InventoryInteractionState.Move)
            {
                voices.Play(InventoryCue.MoveSelect);
                CaptureMove(selected);
            }
            if (ready && previousReady && state == previousState
                && (state == InventoryInteractionState.Browse || state == InventoryInteractionState.Move)
                && selected != previousSelection && confirmedFrame != Time.frameCount)
                voices.Play(InventoryCue.Navigate);
            if (!ready || state != InventoryInteractionState.Move) moving = false;
            previousOpen = open;
            previousProgress = progress;
            previousReady = ready;
            previousState = state;
            previousSelection = selected;
        }
        private void CaptureMove(int selected)
        {
            moveSource = selected;
            for (int i = 0; i < beforeItems.Length; i++)
            {
                beforeItems[i] = store.Items[i];
                beforeQuantities[i] = store.Quantity(i);
            }
            moving = beforeItems[moveSource] != null && beforeQuantities[moveSource] > 0;
        }
        private void OnStoreChanged(int changedIndex)
        {
            // Deletion now means an actual inventory quantity loss. Synchronize
            // the baseline even while feedback is inactive, so reopening cannot
            // replay a removal that occurred while the inventory was closed.
            bool deleted = ObserveDeletion(changedIndex);
            if (source == null || !source.isActiveAndEnabled || !Ready) return;
            if (deleted) voices.Play(InventoryCue.Discard);
            if (!moving) return;
            // Move commits both stacks before notifying. The controller has reset
            // Move to Browse. Source confirmation, failure and cancellation have
            // no transfer; MarkViewed changes no item identity or quantity.
            if (source.InteractionState == InventoryInteractionState.Browse && IsCommittedMove())
            {
                moving = false;
                confirmedFrame = Time.frameCount;
                voices.Play(InventoryCue.MoveConfirm);
                return;
            }
            for (int i = 0; i < beforeItems.Length; i++)
                if (store.Items[i] != beforeItems[i] || store.Quantity(i) != beforeQuantities[i])
                {
                    moving = false;
                    return;
                }
        }
        private bool ObserveDeletion(int changedIndex)
        {
            InventoryItem oldItem = observedItems[changedIndex];
            long previousTotal = 0, currentTotal = 0;
            for (int i = 0; i < observedItems.Length; i++)
            {
                if (observedItems[i] == oldItem) previousTotal += observedQuantities[i];
                InventoryItem currentItem = store.Items[i];
                int currentQuantity = store.Quantity(i);
                if (currentItem == oldItem) currentTotal += currentQuantity;
                observedItems[i] = currentItem;
                observedQuantities[i] = currentQuantity;
            }
            // Move commits source and destination before either Changed event.
            // Its item total is conserved; the first event refreshes both slots.
            return oldItem != null && currentTotal < previousTotal;
        }
        private bool IsCommittedMove()
        {
            if (store.Items[moveSource] != null || store.Quantity(moveSource) != 0) return false;
            int target = -1;
            for (int i = 0; i < beforeItems.Length; i++)
            {
                if (i == moveSource) continue;
                if (beforeItems[i] == null && store.Items[i] == beforeItems[moveSource]
                    && store.Quantity(i) == beforeQuantities[moveSource])
                {
                    if (target >= 0) return false;
                    target = i;
                }
                else if (store.Items[i] != beforeItems[i] || store.Quantity(i) != beforeQuantities[i]) return false;
            }
            return target >= 0;
        }
    }
}

using System;
using System.Collections.Generic;

namespace AnimalGame.MainUI.Inventory
{
    /// <summary>Runtime inventory. Definitions never contain mutable stack or discovery state.</summary>
    public sealed class InventoryStore
    {
        public readonly struct AddResult
        {
            public readonly int Added, Remaining;
            public AddResult(int added, int remaining) { Added = added; Remaining = remaining; }
        }

        // Kept for the existing MainUI.Items read API. Mutations must use this store.
        public InventoryItem[] Items { get; }
        private readonly int[] quantities;
        private readonly bool[] unread;
        private readonly HashSet<InventoryItem> discovered = new();
        public event Action<int> Changed;
        public int Capacity => Items.Length;
        public bool HasUnread
        {
            get { for (int i = 0; i < Capacity; i++) if (unread[i]) return true; return false; }
        }

        public InventoryStore(int capacity = 8)
        {
            Items = new InventoryItem[capacity];
            quantities = new int[capacity];
            unread = new bool[capacity];
        }
        public int Quantity(int index) => quantities[index];
        public bool IsUnread(int index) => unread[index];

        public int Count(InventoryItem item)
        {
            if (item == null) return 0;
            int total = 0;
            for (int i = 0; i < Capacity; i++)
                if (Items[i] == item) total += quantities[i];
            return total;
        }

        public bool TryConsume(InventoryItem item, int quantity)
        {
            if (quantity <= 0 || Count(item) < quantity) return false;

            var changedSlots = new List<int>();
            for (int i = 0; i < Capacity && quantity > 0; i++)
            {
                if (Items[i] != item) continue;
                int consumed = Math.Min(quantity, quantities[i]);
                quantities[i] -= consumed;
                quantity -= consumed;
                if (quantities[i] == 0)
                {
                    Items[i] = null;
                    unread[i] = false;
                }
                changedSlots.Add(i);
            }

            // A listener inspecting other slots must see the entire payment.
            foreach (int slot in changedSlots) Changed?.Invoke(slot);
            return true;
        }

        public void Set(int index, InventoryItem item, int quantity = 1)
        {
            if (index < 0 || index >= Capacity) throw new ArgumentOutOfRangeException(nameof(index));
            quantities[index] = item == null ? 0 : Math.Max(0, Math.Min(item.MaxStack, quantity));
            Items[index] = quantities[index] > 0 ? item : null;
            unread[index] = false;
            if (Items[index] != null) discovered.Add(item);
            Changed?.Invoke(index);
        }

        public AddResult Add(InventoryItem item, int quantity)
        {
            if (item == null || quantity <= 0) return new AddResult(0, Math.Max(0, quantity));
            var changedSlots = new List<int>();
            var result = Receive(item, quantity, !discovered.Contains(item), changedSlots);
            foreach (int slot in changedSlots) Changed?.Invoke(slot);
            return result;
        }

        /// <summary>Transfers as much of the selected stack as fits. Both stores settle before any slot notifies.</summary>
        public AddResult TransferTo(int source, InventoryStore destination)
        {
            InventoryItem item = Items[source];
            int quantity = quantities[source];
            if (item == null || destination == this) return new AddResult(0, quantity);
            var changedSlots = new List<int>();
            var result = destination.Receive(item, quantity, unread[source], changedSlots);
            if (result.Added == 0) return result;

            quantities[source] = result.Remaining;
            if (result.Remaining == 0)
            {
                Items[source] = null;
                unread[source] = false;
            }
            Changed?.Invoke(source);
            foreach (int slot in changedSlots) destination.Changed?.Invoke(slot);
            return result;
        }

        private AddResult Receive(InventoryItem item, int quantity, bool markNewSlotsUnread, List<int> changedSlots)
        {
            int remaining = quantity;
            for (int i = 0; i < Capacity && remaining > 0; i++)
            {
                if (Items[i] != item || quantities[i] >= item.MaxStack) continue;
                int add = Math.Min(remaining, item.MaxStack - quantities[i]);
                quantities[i] += add;
                remaining -= add;
                changedSlots.Add(i);
            }
            for (int i = 0; i < Capacity && remaining > 0; i++)
            {
                if (Items[i] != null) continue;
                Items[i] = item;
                quantities[i] = Math.Min(remaining, item.MaxStack);
                unread[i] = markNewSlotsUnread;
                remaining -= quantities[i];
                changedSlots.Add(i);
            }
            if (remaining < quantity) discovered.Add(item);
            return new AddResult(quantity - remaining, remaining);
        }

        public void MarkViewed(int index)
        {
            if (!unread[index]) return;
            unread[index] = false;
            Changed?.Invoke(index);
        }

        public bool Move(int source, int target)
        {
            if (source < 0 || target < 0 || source >= Capacity || target >= Capacity ||
                Items[source] == null || Items[target] != null) return false;
            Items[target] = Items[source]; quantities[target] = quantities[source]; unread[target] = unread[source];
            Items[source] = null; quantities[source] = 0; unread[source] = false;
            Changed?.Invoke(source); Changed?.Invoke(target);
            return true;
        }
    }
}

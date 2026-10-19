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
            int remaining = quantity;
            bool firstType = !discovered.Contains(item);
            for (int i = 0; i < Capacity && remaining > 0; i++)
            {
                if (Items[i] != item || quantities[i] >= item.MaxStack) continue;
                int add = Math.Min(remaining, item.MaxStack - quantities[i]);
                quantities[i] += add;
                remaining -= add;
                Changed?.Invoke(i);
            }
            for (int i = 0; i < Capacity && remaining > 0; i++)
            {
                if (Items[i] != null) continue;
                Items[i] = item;
                quantities[i] = Math.Min(remaining, item.MaxStack);
                unread[i] = firstType;
                remaining -= quantities[i];
                Changed?.Invoke(i);
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

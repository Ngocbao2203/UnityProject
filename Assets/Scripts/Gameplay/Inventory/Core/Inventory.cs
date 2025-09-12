using System.Collections.Generic;
using UnityEngine;
using CGP.Gameplay.Items;

namespace CGP.Gameplay.InventorySystem
{
    [System.Serializable]
    public class Inventory
    {
        [System.Serializable]
        public class Slot
        {
            // Canonical identity for stacking
            public string itemId;
            public string itemName;
            public int count;
            public int maxAllowed;
            public ItemData itemData;
            public Sprite icon;

            public Slot()
            {
                itemId = null;
                itemName = "";
                count = 0;
                maxAllowed = 99;
                itemData = null;
                icon = null;
            }

            // Safer: "empty" means nothing to consume
            public bool IsEmpty => count <= 0;

            public bool CanStack(string targetItemId)
            {
                if (IsEmpty) return false;
                if (string.IsNullOrEmpty(targetItemId)) return false;
                return itemId == targetItemId && count < maxAllowed;
            }

            public void SetFromItemData(ItemData data, int initialCount = 1)
            {
                if (data == null) return;

                itemId = data.id;
                itemName = data.itemName;
                icon = data.icon;
                itemData = data;
                if (maxAllowed <= 0) maxAllowed = 99;

                count = Mathf.Clamp(initialCount, 0, maxAllowed);
                if (count == 0) Clear();
            }

            public void SetFromLoose(string id, string name, Sprite sprite, ItemData data, int initialCount = 1, int? overrideMax = null)
            {
                itemId = id;
                itemName = name ?? "";
                icon = sprite;
                itemData = data;
                if (overrideMax.HasValue) maxAllowed = Mathf.Max(1, overrideMax.Value);
                if (maxAllowed <= 0) maxAllowed = 99;

                count = Mathf.Clamp(initialCount, 0, maxAllowed);
                if (count == 0) Clear();
            }

            public void Clear()
            {
                itemId = null;
                itemName = "";
                icon = null;
                itemData = null;
                count = 0;
            }

            public void AddOne()
            {
                if (IsEmpty)
                {
                    // Caller should initialize slot first
                    return;
                }
                count = Mathf.Min(count + 1, maxAllowed);
            }

            public void AddMany(int amount)
            {
                if (IsEmpty || amount <= 0) return;
                count = Mathf.Min(count + amount, maxAllowed);
            }

            public void RemoveOne()
            {
                if (count <= 0) return;
                count--;
                if (count <= 0) Clear();
            }

            public void RemoveMany(int amount)
            {
                if (amount <= 0 || IsEmpty) return;
                count -= amount;
                if (count <= 0) Clear();
            }
        }

        public List<Slot> slots = new List<Slot>();
        public Slot selectedSlot = null;

        public Inventory(int numSlots)
        {
            if (numSlots < 0) numSlots = 0;
            slots = new List<Slot>(numSlots);
            for (int i = 0; i < numSlots; i++) slots.Add(new Slot());
        }

        // ---------- Adds ----------
        // Keep MonoBehaviour path (legacy)
        public void Add(Item item)
        {
            if (item == null || item.Data == null) return;
            Add(item.Data, 1);
        }

        public void Add(ItemData data, int qty = 1)
        {
            if (data == null || qty <= 0) return;

            // 1) Try stack (by id)
            int stackIdx = FindStackableSlotIndexById(data.id);
            if (stackIdx >= 0)
            {
                int canAdd = Mathf.Min(qty, slots[stackIdx].maxAllowed - slots[stackIdx].count);
                slots[stackIdx].AddMany(canAdd);
                qty -= canAdd;
                if (qty <= 0) return;
            }

            // 2) Fill empty slots
            while (qty > 0)
            {
                int empty = FindFirstEmptySlotIndex();
                if (empty < 0) break; // full

                int cap = (slots[empty].maxAllowed > 0) ? slots[empty].maxAllowed : 99;
                int add = Mathf.Min(qty, cap);
                slots[empty].SetFromItemData(data, add);
                qty -= add;
            }
        }

        // Useful when only id/name/icon known
        public void AddById(string itemId, int qty = 1, string itemName = null, Sprite icon = null, ItemData data = null, int? overrideMax = null)
        {
            if (string.IsNullOrEmpty(itemId) || qty <= 0) return;

            // Try stack
            int stackIdx = FindStackableSlotIndexById(itemId);
            if (stackIdx >= 0)
            {
                int canAdd = Mathf.Min(qty, slots[stackIdx].maxAllowed - slots[stackIdx].count);
                slots[stackIdx].AddMany(canAdd);
                qty -= canAdd;
                if (qty <= 0) return;
            }

            // Fill empty slots
            while (qty > 0)
            {
                int empty = FindFirstEmptySlotIndex();
                if (empty < 0) break;

                int cap = overrideMax.HasValue
                    ? Mathf.Max(1, overrideMax.Value)
                    : (slots[empty].maxAllowed > 0 ? slots[empty].maxAllowed : 99);

                int add = Mathf.Min(qty, cap);
                slots[empty].SetFromLoose(itemId, itemName, icon, data, add, overrideMax);
                qty -= add;
            }
        }

        // ---------- Removes ----------
        public void Remove(int index)
        {
            if (!TryGetIndex(index)) return;
            slots[index].RemoveOne();
        }

        public void Remove(int index, int count)
        {
            if (!TryGetIndex(index) || count <= 0) return;
            slots[index].RemoveMany(count);
        }

        // ---------- Selection ----------
        public void SelectSlot(int index)
        {
            if (!TryGetIndex(index)) return;
            selectedSlot = slots[index];
        }

        // Game-rule “use” (kept for backward compatibility)
        public void UseItem(int index)
        {
            if (!TryGetIndex(index)) return;
            var s = slots[index];
            if (s.IsEmpty || s.itemData == null) return;

            if (s.itemData.itemType == ItemData.ItemType.Seed)
            {
                s.RemoveOne();
                Debug.Log($"Used seed at index {index}, New Count: {s.count}");
            }
            else
            {
                Debug.Log($"Cannot use item at index {index} as it is not a seed, Type: {s.itemData?.itemType}");
            }
        }

        // ---------- Queries ----------
        public bool HasFreeSlot()
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] == null || slots[i].IsEmpty) return true;
            return false;
        }

        public int FindFirstEmptySlotIndex()
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] == null || slots[i].IsEmpty) return i;
            return -1;
        }

        public int FindStackableSlotIndexById(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return -1;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s != null && s.CanStack(itemId)) return i;
            }
            return -1;
        }

        // Backward-compatible (by name). Prefer ById when possible.
        public int FindStackableSlotIndex(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return -1;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s != null && !s.IsEmpty && s.itemName == itemName && s.count < s.maxAllowed)
                    return i;
            }
            return -1;
        }

        public int FindBestSlotIndexForAdd(string itemName)
        {
            int stackIdx = FindStackableSlotIndex(itemName);
            if (stackIdx >= 0) return stackIdx;
            return FindFirstEmptySlotIndex();
        }

        // ---------- Disabled move to avoid conflict with server sync ----------
        public void MoveSlot(int fromIndex, int toIndex, Inventory toInventory, int numToMove = 1)
        {
            Debug.LogWarning("MoveSlot is disabled. Use InventoryManager.MoveItem instead.");
        }

        // ---------- Helpers ----------
        private bool TryGetIndex(int index)
        {
            return (index >= 0 && index < slots.Count && slots[index] != null);
        }
    }
}

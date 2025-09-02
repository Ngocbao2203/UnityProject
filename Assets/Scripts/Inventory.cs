using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq; // Thêm directive này

[System.Serializable]
public class Inventory
{
    [System.Serializable]
    public class Slot
    {
        public string itemName;
        public int count;
        public int maxAllowed;
        public ItemData itemData;
        public Sprite icon;

        public Slot()
        {
            itemName = "";
            count = 0;
            maxAllowed = 99;
        }

        public bool IsEmpty => itemName == "" && count == 0;

        public bool CanAddItem(string itemName)
        {
            return this.itemName == itemName && count < maxAllowed;
        }

        public void AddItem(Item item)
        {
            itemName = item.Data.itemName; // Sử dụng item.Data
            icon = item.Data.icon;
            itemData = item.Data;
            count++;
        }

        public void AddItem(string itemName, Sprite icon, int maxAllowed, ItemData itemData)
        {
            this.itemName = itemName;
            this.icon = icon;
            this.maxAllowed = maxAllowed;
            this.itemData = itemData;
            count++;
        }

        public void RemoveItem()
        {
            if (count > 0)
            {
                count--;
                if (count == 0)
                {
                    icon = null;
                    itemName = "";
                    itemData = null;
                }
            }
        }

        public void RemoveItems(int amount)
        {
            if (count >= amount)
            {
                count -= amount;
                if (count == 0)
                {
                    icon = null;
                    itemName = "";
                    itemData = null;
                }
            }
        }
    }

    public List<Slot> slots = new List<Slot>();
    public Slot selectedSlot = null;

    public Inventory(int numSlots)
    {
        for (int i = 0; i < numSlots; i++)
        {
            slots.Add(new Slot());
        }
    }

    public void Add(Item item)
    {
        foreach (Slot slot in slots)
        {
            if (slot.itemName == item.Data.itemName && slot.CanAddItem(item.Data.itemName))
            {
                slot.AddItem(item);
                return;
            }
        }
        foreach (Slot slot in slots)
        {
            if (slot.IsEmpty)
            {
                slot.AddItem(item);
                return;
            }
        }
    }

    public void Remove(int index)
    {
        slots[index].RemoveItem();
    }

    public void Remove(int index, int count)
    {
        if (slots[index].count >= count)
        {
            slots[index].RemoveItems(count);
        }
    }

    public void MoveSlot(int fromIndex, int toIndex, Inventory toInventory, int numToMove = 1)
    {
        // Tạm thời disable method này để tránh conflict với InventoryManager.MoveItem
        Debug.LogWarning("MoveSlot is disabled. Use InventoryManager.MoveItem instead.");
        return;

        /* 
        // Code cũ được comment out
        if (slots != null && slots.Count > 0 && toInventory?.slots != null)
        {
            Slot fromSlot = slots[fromIndex];
            Slot toSlot = toInventory.slots[toIndex];
            int moveCount = Mathf.Min(numToMove, fromSlot.count);
            if (toSlot.IsEmpty || toSlot.CanAddItem(fromSlot.itemName))
            {
                for (int i = 0; i < moveCount; i++)
                {
                    toSlot.AddItem(fromSlot.itemName, fromSlot.icon, fromSlot.maxAllowed, fromSlot.itemData);
                    fromSlot.RemoveItem();
                }
            }
        }
        */
    }

    public void SelectSlot(int index)
    {
        if (slots != null && slots.Count > index)
        {
            selectedSlot = slots[index];
        }
    }

    public void UseItem(int index)
    {
        if (slots != null && slots.Count > index && !slots[index].IsEmpty)
        {
            if (slots[index].itemData.itemType == ItemData.ItemType.Seed)
            {
                slots[index].RemoveItem();
                Debug.Log($"Used seed at index {index}, New Count: {slots[index].count}");
            }
            else
            {
                Debug.Log($"Cannot use item at index {index} as it is not a seed, Type: {slots[index].itemData?.itemType}");
            }
        }
    }

    public void IncreaseItemQuantity(string itemName)
    {
        foreach (Slot slot in slots)
        {
            if (slot.itemName == itemName && slot.count < slot.maxAllowed)
            {
                slot.count++;
                return;
            }
        }
    }

    public bool HasFreeSlot()
    {
        return slots.Any(slot => slot.IsEmpty); // Sử dụng Any với LINQ
    }

    public int FindFirstEmptySlotIndex()
    {
        for (int i = 0; i < slots.Count; i++)
            if (slots[i].IsEmpty) return i;
        return -1;
    }

    public int FindStackableSlotIndex(string itemName)
    {
        for (int i = 0; i < slots.Count; i++)
            if (!slots[i].IsEmpty && slots[i].itemName == itemName && slots[i].count < slots[i].maxAllowed)
                return i;
        return -1;
    }

    public int FindBestSlotIndexForAdd(string itemName)
    {
        int stackIdx = FindStackableSlotIndex(itemName);
        if (stackIdx >= 0) return stackIdx;
        return FindFirstEmptySlotIndex();
    }

}
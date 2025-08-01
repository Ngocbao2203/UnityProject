﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

        public bool IsEmpty
        {
            get
            {
                if (itemName == "" && count == 0)
                {
                    return true;
                }
                return false;
            }
        }

        public bool CanAddItem(string itemName)
        {
            if (this.itemName == itemName && count < maxAllowed)
            {
                return true;
            }
            return false;
        }

        public void AddItem(Item item)
        {
            this.itemName = item.data.itemName;
            this.icon = item.data.icon;
            this.itemData = item.data;
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
                    itemData = null; // Xóa itemData khi count = 0
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
            if (slot.itemName == item.data.itemName && slot.CanAddItem(item.data.itemName))
            {
                slot.AddItem(item);
                return;
            }
        }
        foreach (Slot slot in slots)
        {
            if (slot.itemName == "")
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
            for (int i = 0; i < count; i++)
            {
                Remove(index);
            }
        }
    }

    public void MoveSlot(int fromIndex, int toIndex, Inventory toInventory, int numToMove = 1)
    {
        if (slots != null && slots.Count > 0)
        {
            Slot fromSlot = slots[fromIndex];
            Slot toSlot = toInventory.slots[toIndex];

            for (int i = 0; i < numToMove; i++)
            {
                if (toSlot.IsEmpty || toSlot.CanAddItem(fromSlot.itemName))
                {
                    toSlot.AddItem(fromSlot.itemName, fromSlot.icon, fromSlot.maxAllowed, fromSlot.itemData);
                    fromSlot.RemoveItem();
                }
            }
        }
    }

    public void SelectSlot(int index)
    {
        if (slots != null && slots.Count > 0)
        {
            selectedSlot = slots[index];
        }
    }

    public void UseItem(int index)
    {
        if (slots != null && slots.Count > index && !slots[index].IsEmpty)
        {
            if (slots[index].itemData.itemType == ItemData.ItemType.Seed) // Chỉ giảm nếu là hạt giống
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
}
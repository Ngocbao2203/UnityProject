using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Inventory
{
    [System.Serializable]
    public class Slot
    {
        public CollectableType type;
        public int count; // Number of items in this slot
        public int maxAllowed; // Maximum number of items allowed in this slot

        public Sprite icon; // Icon representing the item in this slot
        public Slot()
        { 
            type = CollectableType.NONE; // Default type is NONE
            count = 0; // Start with zero items in the slot
            maxAllowed = 99; // Default maximum allowed items in the slot
        }
        public bool CanAddItem()
        {
            if (count < maxAllowed)
            {
                return true; // Can add item if count is less than max allowed
            }
            return false; // Cannot add item if count is equal to or greater than max allowed
        }
        public void AddItem(Collectable item)
        {
            this.type = item.type; // Set the type of the item being added
            this.icon = item.icon; // Set the icon of the item being added
            count++;
        }
        public void RemoveItem()
        {
            if (count > 0)
            {
                count--; // Decrease the count of items in the slot

                if (count == 0)
                {
                    icon = null;
                    type = CollectableType.NONE; // Reset the type to NONE if count reaches zero
                }
            }
        }
    }

    public List<Slot> slots = new List<Slot>(); // List of slots in the inventory
    public Inventory(int numSlots)
    {
        for (int i = 0; i < numSlots; i++)
        {
            Slot slot = new Slot(); // Create a new slot
            slots.Add(slot);
        }
    }

    public void Add(Collectable item)
    {
        foreach (Slot slot in slots)
        {
            if (slot.type == item.type && slot.CanAddItem())
            {
                    slot.AddItem(item); // Add item to the slot if it matches the type and can be added
                    return; // Exit after adding the item
            }
        }
        foreach (Slot slot in slots)
        {
            if (slot.type == CollectableType.NONE)
            {
                slot.AddItem(item); // Add item to an empty slot
                return; // Exit after adding the item
            }
        }
    }
    public void Remove(int index)
    {
        slots[index].RemoveItem(); // Pass the required 'amount' parameter to RemoveItem  
    }

}

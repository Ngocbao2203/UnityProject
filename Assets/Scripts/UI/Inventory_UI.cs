using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class Inventory_UI : MonoBehaviour
{
    public GameObject inventoryPanel; // Reference to the inventory panel GameObject

    public Player player; // Reference to the Player script

    public List<Slot_UI> slots = new List<Slot_UI>(); // List of Slot_UI components to manage inventory slots
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Tab)) // Check if the 'I' key is pressed
        {
            ToggleInventory(); // Call the method to toggle the inventory panel
        }
    }
    public void ToggleInventory()
    {
        if(!inventoryPanel.activeSelf) // Check if the inventory panel is assigned
        {
            inventoryPanel.SetActive(true); // Toggle the active state of the inventory panel
            Refresh(); // Call the Setup method to initialize the inventory panel
        }
        else
        {
            inventoryPanel.SetActive(false); // Hide the inventory panel if it is already active
        }
    }
    void Refresh()
    {
        if(slots.Count == player.inventory.slots.Count) // Check if the number of slots matches the player's inventory slots
        {
            for(int i = 0; i < slots.Count; i++) // Loop through each slot
            {
                if(player.inventory.slots[i].type != CollectableType.NONE) // Check if the slot in the player's inventory is not null
                {
                    slots[i].SetItem(player.inventory.slots[i]);
                }
                else
                {
                    slots[i].SetEmpty(); // Set the slot to empty if the player's inventory slot is null
                }
            }
        }
    }
    public void Remove(int slotID)
    {
        Collectable itemToDrop = GameManager.instance.itemManager.GetItemByType(
            player.inventory.slots[slotID].type); // Get the item from the ItemManager using the slot ID

        if (itemToDrop !=null)
        {
            player.DropItem(itemToDrop); // Call the DropItem method in the Player script to drop the item
            player.inventory.Remove(slotID); // Remove the item from the player's inventory
            Refresh(); // Refresh the inventory UI after removing the item
        }    
    }
}

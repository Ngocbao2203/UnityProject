using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    private readonly Dictionary<string, Inventory> inventoryByName = new();

    public const string BACKPACK = "Backpack";
    public const string TOOLBAR = "Toolbar";

    [Header("Backpack")]
    public Inventory backpack;
    public int backpackSlotsCount = 27;

    [Header("Toolbar")]
    public Inventory toolbar;
    public int toolbarSlotsCount = 9;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        backpack = new Inventory(backpackSlotsCount);
        toolbar = new Inventory(toolbarSlotsCount);

        inventoryByName.Add(BACKPACK, backpack);
        inventoryByName.Add(TOOLBAR, toolbar);
    }

    public Inventory GetInventoryByName(string name)
    {
        return inventoryByName.TryGetValue(name, out var inventory) ? inventory : null;
    }

    public void AddItem(string inventoryName, Item item)
    {
        if (inventoryByName.TryGetValue(inventoryName, out var inventory))
        {
            inventory.Add(item);
        }
    }

    public bool HasItem(string inventoryName, ItemData itemData)
    {
        if (!inventoryByName.ContainsKey(inventoryName)) return false;

        Inventory inventory = inventoryByName[inventoryName];

        foreach (Inventory.Slot slot in inventory.slots)
        {
            if (!slot.IsEmpty && slot.itemData != null && slot.itemData.itemName == itemData.itemName)
            {
                return true;
            }
        }

        return false;
    }

    // Thêm phương thức UseItem
    public void UseItem(string inventoryName, int slotIndex)
    {
        if (inventoryByName.TryGetValue(inventoryName, out var inventory))
        {
            if (slotIndex >= 0 && slotIndex < inventory.slots.Count)
            {
                Debug.Log($"Using item at {inventoryName}[{slotIndex}], Before: Count = {inventory.slots[slotIndex].count}, Type = {inventory.slots[slotIndex].itemData?.itemType}");
                inventory.UseItem(slotIndex);
                Debug.Log($"After: Count = {inventory.slots[slotIndex].count}");
            }
        }
    }
}
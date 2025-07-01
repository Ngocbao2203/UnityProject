using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    private readonly Dictionary<string, Inventory> inventoryByName = new();

    private const string BACKPACK = "Backpack";
    private const string TOOLBAR = "Toolbar";

    [Header("Backpack")]
    public Inventory backpack;
    public int backpackSlotsCount = 27;

    [Header("Toolbar")]
    public Inventory toolbar;
    public int toolbarSlotsCount = 9;

    private void Awake()
    {
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
}
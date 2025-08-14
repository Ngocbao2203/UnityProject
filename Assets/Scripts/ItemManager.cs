using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemManager : MonoBehaviour
{
    public Item[] items; // Mảng các item được gán trong Editor
    public Dictionary<string, Item> collectableItemsDict = new Dictionary<string, Item>();

    private void Awake()
    {
        // Kiểm tra null trước khi lặp
        if (items == null || items.Length == 0)
        {
            Debug.LogWarning("No items assigned to ItemManager!");
            return;
        }

        foreach (Item collectable in items)
        {
            AddItem(collectable);
        }
    }

    private void AddItem(Item item)
    {
        if (item != null && !string.IsNullOrEmpty(item.Data.itemName)) // Kiểm tra null và itemName
        {
            if (!collectableItemsDict.ContainsKey(item.Data.itemName)) // Sử dụng item.Data
            {
                collectableItemsDict.Add(item.Data.itemName, item);
            }
            else
            {
                Debug.LogWarning($"Item with name {item.Data.itemName} already exists in collectableItemsDict!");
            }
        }
    }

    public Item GetItemByName(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning("Key is null or empty!");
            return null;
        }
        if (collectableItemsDict.ContainsKey(key))
        {
            return collectableItemsDict[key];
        }
        Debug.LogWarning($"Item with name {key} not found!");
        return null;
    }
}
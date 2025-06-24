using UnityEngine;
using System.Collections.Generic;
public class ItemManager : MonoBehaviour
{
    public Collectable[] collectableItems; // Array of collectable items

    private Dictionary<CollectableType, Collectable> collectableItemsDict = 
        new Dictionary<CollectableType, Collectable>(); // Dictionary to hold items by name
    private void Awake()
    {
        foreach (Collectable item in collectableItems)
        {
            AddItem(item);
        }
    }
    private void AddItem(Collectable item)
    {
        if (!collectableItemsDict.ContainsKey(item.type))
        {
            collectableItemsDict.Add(item.type, item);
        }
    }
    public Collectable GetItemByType(CollectableType type)
    {
        if (collectableItemsDict.ContainsKey(type))
        {
            return collectableItemsDict[type];
        }
        return null; // Return null if the item is not found
    }
}

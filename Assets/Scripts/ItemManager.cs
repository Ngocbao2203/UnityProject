using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class ItemManager : MonoBehaviour
{
    [Header("Assign in Inspector (or leave empty to auto-load from Resources/Items)")]
    public Item[] items;

    // Also load ItemData to fallback when no Item wrapper exists
    [Header("Also load ItemData (fallback if Item wrapper missing)")]
    public ItemData[] itemDataAssets;

    // Lookups for Item
    public Dictionary<string, Item> collectableItemsDict = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Item> itemsById = new();

    // Lookups for ItemData (fallback)
    private readonly Dictionary<string, ItemData> itemDataById = new();
    private readonly Dictionary<string, ItemData> itemDataByName = new(StringComparer.OrdinalIgnoreCase);

    public const string RES_PATH = "Items";

    private void Awake() => BuildLookups();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) BuildLookups();
    }
#endif

    // ---------- Normalize helpers ----------
    private static string NormalizeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim();
    }

    private static string NormalizeId(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // accept GUID with/without {}, any case -> lower + strip {}
        s = s.Trim().Trim('{', '}').ToLowerInvariant();
        return s;
    }

    // ---------- Build lookups ----------
    private void BuildLookups()
    {
        collectableItemsDict.Clear();
        itemsById.Clear();
        itemDataById.Clear();
        itemDataByName.Clear();

        // Load Item (prefab/GO with Item component)
        if (items == null || items.Length == 0)
            items = Resources.LoadAll<Item>(RES_PATH);
        if (items == null) items = Array.Empty<Item>();

        int addedItemByName = 0, addedItemById = 0;

        foreach (var it in items)
        {
            if (it == null || it.Data == null)
            {
                Debug.LogWarning("[ItemManager] Skipping null Item or Item.Data");
                continue;
            }

            var name = NormalizeName(it.Data.itemName);
            var id = NormalizeId(it.Data.id);

            // by name
            if (!string.IsNullOrEmpty(name))
            {
                if (!collectableItemsDict.ContainsKey(name))
                {
                    collectableItemsDict.Add(name, it);
                    addedItemByName++;
                }
                else
                {
                    Debug.LogWarning($"[ItemManager] Duplicate itemName '{name}'! (case-insensitive)");
                }
            }
            else
            {
                Debug.LogWarning($"[ItemManager] Item '{it.name}' has empty Data.itemName.");
            }

            // by id
            if (!string.IsNullOrEmpty(id))
            {
                if (!itemsById.ContainsKey(id))
                {
                    itemsById.Add(id, it);
                    addedItemById++;
                }
                else
                {
                    Debug.LogWarning($"[ItemManager] Duplicate ItemData.id '{id}'!");
                }
            }
            else
            {
                Debug.LogWarning($"[ItemManager] Item '{it.name}' has empty Data.id.");
            }

            if (it.Data.icon == null)
                Debug.LogWarning($"[ItemManager] Item '{name ?? it.name}' is missing icon Sprite.");
        }

        // Load ItemData (fallback)
        if (itemDataAssets == null || itemDataAssets.Length == 0)
        {
            // If all ItemData live under Resources/Items:
            itemDataAssets = Resources.LoadAll<ItemData>(RES_PATH);

            // If you spread ItemData anywhere under Resources, use this instead:
            // itemDataAssets = Resources.LoadAll<ItemData>(""); // grabs all under Resources
        }
        if (itemDataAssets == null) itemDataAssets = Array.Empty<ItemData>();

        int addedDataByName = 0, addedDataById = 0;

        foreach (var d in itemDataAssets)
        {
            if (d == null) continue;

            var name = NormalizeName(d.itemName);
            var id = NormalizeId(d.id);

            if (!string.IsNullOrEmpty(name) && !itemDataByName.ContainsKey(name))
            {
                itemDataByName.Add(name, d);
                addedDataByName++;
            }

            if (!string.IsNullOrEmpty(id) && !itemDataById.ContainsKey(id))
            {
                itemDataById.Add(id, d);
                addedDataById++;
            }

            if (d.icon == null)
                Debug.LogWarning($"[ItemManager] ItemData '{name ?? d.name}' missing icon.");
        }

        Debug.Log($"[ItemManager] Built lookups → Item(byName={addedItemByName}, byId={addedItemById}) | ItemData(byName={addedDataByName}, byId={addedDataById})");

        if (addedItemById == 0 && addedDataById == 0)
            Debug.LogWarning($"[ItemManager] No Item or ItemData found under Resources/{RES_PATH}. Add assets or assign in Inspector.");
    }

    // ---------- Public lookups (Item) ----------
    public Item GetItemByName(string key)
    {
        key = NormalizeName(key);
        if (string.IsNullOrEmpty(key)) return null;
        return collectableItemsDict.TryGetValue(key, out var it) ? it : null;
    }

    public Item GetItemByServerId(string id)
    {
        id = NormalizeId(id);
        if (string.IsNullOrEmpty(id)) return null;
        return itemsById.TryGetValue(id, out var it) ? it : null;
    }

    /// <summary>Try resolve by id (priority) then by name for Item.</summary>
    public bool TryResolveByIdOrName(string itemId, string itemName, out Item item)
    {
        item = null;

        if (!string.IsNullOrEmpty(itemId))
        {
            var idNorm = NormalizeId(itemId);
            if (!string.IsNullOrEmpty(idNorm) && itemsById.TryGetValue(idNorm, out item) && item != null)
                return true;
        }

        if (!string.IsNullOrEmpty(itemName))
        {
            var nameNorm = NormalizeName(itemName);
            if (!string.IsNullOrEmpty(nameNorm) && collectableItemsDict.TryGetValue(nameNorm, out item) && item != null)
                return true;
        }

        return false;
    }

    // ---------- Public lookups (ItemData fallback) ----------
    public ItemData GetItemDataByServerId(string id)
    {
        id = NormalizeId(id);
        if (string.IsNullOrEmpty(id)) return null;
        return itemDataById.TryGetValue(id, out var d) ? d : null;
    }

    public ItemData GetItemDataByName(string name)
    {
        name = NormalizeName(name);
        if (string.IsNullOrEmpty(name)) return null;
        return itemDataByName.TryGetValue(name, out var d) ? d : null;
    }

    // ---------- Icons with fallback ----------
    public Sprite GetIconById(string id)
    {
        var it = GetItemByServerId(id);
        if (it != null && it.Data != null && it.Data.icon != null) return it.Data.icon;

        var d = GetItemDataByServerId(id);
        return d != null ? d.icon : null;
    }

    public Sprite GetIconByName(string name)
    {
        var it = GetItemByName(name);
        if (it != null && it.Data != null && it.Data.icon != null) return it.Data.icon;

        var d = GetItemDataByName(name);
        return d != null ? d.icon : null;
    }

    // ---------- Editor helpers ----------
#if UNITY_EDITOR
    [ContextMenu("Rebuild Lookups")]
    private void RebuildMenu()
    {
        BuildLookups();
        Debug.Log("[ItemManager] Lookups rebuilt via context menu.");
    }

    [ContextMenu("Print All ItemData IDs")]
    private void PrintAllItemDataIds()
    {
        foreach (var kv in itemDataById)
            Debug.Log($"[ItemManager] ItemData: id={kv.Key} name={kv.Value.itemName}");
    }
#endif
}

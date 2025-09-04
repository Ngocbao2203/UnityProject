using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class ItemManager : MonoBehaviour
{
    [Header("Assign in Inspector (or leave empty to auto-load from Resources/Items)")]
    public Item[] items;

    [Header("Also load ItemData (fallback if Item wrapper missing)")]
    public ItemData[] itemDataAssets;

    [Header("Crop Data (drag all CropData assets here)")]
    public CropData[] cropDatas;

    // Lookups (runtime Item)
    public Dictionary<string, Item> collectableItemsDict = new(StringComparer.OrdinalIgnoreCase); // by name
    private readonly Dictionary<string, Item> itemsById = new();                                   // by id (normalized)

    // Lookups (ItemData fallback)
    private readonly Dictionary<string, ItemData> itemDataById = new();                            // by id (normalized)
    private readonly Dictionary<string, ItemData> itemDataByName = new(StringComparer.OrdinalIgnoreCase);

    // Crop lookups
    private readonly Dictionary<string, CropData> cropByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Tile> sproutTileBySeedId = new(); // cache seedId→sprout tile

    public const string RES_PATH = "Items";

    private void Awake() => BuildLookups();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        try { BuildLookups(); }
        catch (Exception ex) { Debug.LogError("[ItemManager] OnValidate exception: " + ex); }
    }
#endif

    // =============== helpers ===============
    private static string NormalizeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim();
    }
    private static string NormalizeId(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim().Trim('{', '}').ToLowerInvariant();
    }

    // Hỗ trợ nhiều kiểu field/property chứa ItemData bên trong Item
    private static ItemData ResolveItemDataFromItem(Item it)
    {
        if (!it) return null;
        var t = it.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        string[] names = { "Data", "data", "itemData", "ItemData" };

        foreach (var n in names)
        {
            var f = t.GetField(n, flags);
            if (f != null && typeof(ItemData).IsAssignableFrom(f.FieldType))
                return f.GetValue(it) as ItemData;

            var p = t.GetProperty(n, flags);
            if (p != null && typeof(ItemData).IsAssignableFrom(p.PropertyType))
                return p.GetValue(it) as ItemData;
        }
        return null;
    }

    // =============== build lookups ===============
    private void BuildLookups()
    {
        collectableItemsDict.Clear();
        itemsById.Clear();
        itemDataById.Clear();
        itemDataByName.Clear();
        cropByName.Clear();
        sproutTileBySeedId.Clear();

        // ---- Load Item: ưu tiên mảng inspector; nếu trống sẽ auto-load từ Resources/Items ----
        var itemList = new List<Item>();

        if (items != null && items.Length > 0)
        {
            foreach (var it in items) if (it) itemList.Add(it);
        }
        else
        {
            // 1) Nếu Item là ScriptableObject
            var soItems = Resources.LoadAll<Item>(RES_PATH);
            if (soItems != null)
                foreach (var it in soItems) if (it) itemList.Add(it);

            // 2) Nếu Item là component trên prefab
            var prefabs = Resources.LoadAll<GameObject>(RES_PATH);
            if (prefabs != null)
            {
                // tránh trùng bằng instance id
                var seen = new HashSet<int>();
                foreach (var go in prefabs)
                {
                    if (!go) continue;
                    var its = go.GetComponentsInChildren<Item>(true);
                    if (its == null) continue;
                    foreach (var it in its)
                    {
                        if (!it) continue;
                        int id = it.GetInstanceID();
                        if (seen.Add(id)) itemList.Add(it);
                    }
                }
            }
        }

        // Lọc null lần cuối
        items = itemList.ToArray();

        // Xây dict cho Item
        int warnNullData = 0, addedByName = 0, addedById = 0;
        foreach (var it in items)
        {
            if (!it) continue;

            var data = ResolveItemDataFromItem(it);
            if (data == null) { warnNullData++; continue; }

            var name = NormalizeName(data.itemName);
            var id = NormalizeId(data.id);

            if (!string.IsNullOrEmpty(name))
            {
                if (!collectableItemsDict.ContainsKey(name))
                {
                    collectableItemsDict.Add(name, it);
                    addedByName++;
                }
                else
                {
                    Debug.LogWarning($"[ItemManager] Duplicate itemName '{name}' (case-insensitive).");
                }
            }
            else
            {
                Debug.LogWarning($"[ItemManager] Item '{it.name}' has empty Data.itemName.");
            }

            if (!string.IsNullOrEmpty(id))
            {
                if (!itemsById.ContainsKey(id))
                {
                    itemsById.Add(id, it);
                    addedById++;
                }
                else
                {
                    Debug.LogWarning($"[ItemManager] Duplicate ItemData.id '{id}'.");
                }
            }
            else
            {
                Debug.LogWarning($"[ItemManager] Item '{it.name}' has empty Data.id.");
            }

            if (data.icon == null)
                Debug.LogWarning($"[ItemManager] '{name ?? it.name}' missing icon Sprite.");
        }

        if (warnNullData > 0)
            Debug.LogWarning($"[ItemManager] {warnNullData} Item(s) skipped because ItemData not found on Item (fields: Data/data/itemData).");

        // ---- Load ItemData fallback ----
        var dataList = new List<ItemData>();
        if (itemDataAssets != null && itemDataAssets.Length > 0)
        {
            foreach (var d in itemDataAssets) if (d) dataList.Add(d);
        }
        else
        {
            var loaded = Resources.LoadAll<ItemData>(RES_PATH);
            if (loaded != null) dataList.AddRange(loaded);
        }
        itemDataAssets = dataList.ToArray();

        int addedDataByName = 0, addedDataById = 0;
        foreach (var d in itemDataAssets)
        {
            if (!d) continue;

            var name = NormalizeName(d.itemName);
            var id = NormalizeId(d.id);

            if (!string.IsNullOrEmpty(name) && !itemDataByName.ContainsKey(name))
            { itemDataByName.Add(name, d); addedDataByName++; }

            if (!string.IsNullOrEmpty(id) && !itemDataById.ContainsKey(id))
            { itemDataById.Add(id, d); addedDataById++; }

            if (d.icon == null)
                Debug.LogWarning($"[ItemManager] ItemData '{name ?? d.name}' missing icon.");
        }

        // ---- Build crop lookups ----
        if (cropDatas != null)
        {
            foreach (var cd in cropDatas)
            {
                if (!cd) continue;
                var nm = NormalizeName(cd.cropName);
                if (string.IsNullOrEmpty(nm)) continue;
                if (!cropByName.ContainsKey(nm)) cropByName.Add(nm, cd);
            }
        }

        Debug.Log($"[ItemManager] Built → Item(byName={addedByName}, byId={addedById}) | ItemData(byName={addedDataByName}, byId={addedDataById})");
        if (addedById == 0 && addedDataById == 0)
            Debug.LogWarning($"[ItemManager] No Item/ItemData found under Resources/{RES_PATH}. Assign in inspector or add assets.");
    }

    // =============== public lookups: Item (runtime) ===============
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

    // ShopManager đang gọi tên này
    public Item GetItemById(string id) => GetItemByServerId(id);

    public bool TryResolveByIdOrName(string itemId, string itemName, out Item item)
    {
        item = null;
        if (!string.IsNullOrEmpty(itemId))
        {
            var id = NormalizeId(itemId);
            if (!string.IsNullOrEmpty(id) && itemsById.TryGetValue(id, out item) && item) return true;
        }
        if (!string.IsNullOrEmpty(itemName))
        {
            var nm = NormalizeName(itemName);
            if (!string.IsNullOrEmpty(nm) && collectableItemsDict.TryGetValue(nm, out item) && item) return true;
        }
        return false;
    }

    // =============== public lookups: ItemData fallback ===============
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

    // =============== icons with fallback ===============
    public Sprite GetIconById(string id)
    {
        var it = GetItemByServerId(id);
        var data = ResolveItemDataFromItem(it);
        if (data != null && data.icon) return data.icon;

        var d = GetItemDataByServerId(id);
        return d ? d.icon : null;
    }
    public Sprite GetIconByName(string name)
    {
        var it = GetItemByName(name);
        var data = ResolveItemDataFromItem(it);
        if (data != null && data.icon) return data.icon;

        var d = GetItemDataByName(name);
        return d ? d.icon : null;
    }

    // =============== sprout tile from seed id/name ===============
    public Tile GetSproutTileBySeedIdOrName(string seedIdOrName)
    {
        if (string.IsNullOrWhiteSpace(seedIdOrName)) return null;

        var idNorm = NormalizeId(seedIdOrName);
        if (!string.IsNullOrEmpty(idNorm) && sproutTileBySeedId.TryGetValue(idNorm, out var cached))
            return cached;

        var d = GetItemDataByServerId(seedIdOrName);
        string seedName = d ? d.itemName : seedIdOrName;

        // cắt “ Seed”
        string baseName = seedName;
        const string suffix = " Seed";
        if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            baseName = baseName.Substring(0, baseName.Length - suffix.Length).Trim();

        if (!cropByName.TryGetValue(baseName, out var crop) || crop == null ||
            crop.growthStages == null || crop.growthStages.Length == 0)
            return null;

        var spr = crop.growthStages[0];
        if (!spr) return null;

        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = spr;

        if (!string.IsNullOrEmpty(idNorm))
            sproutTileBySeedId[idNorm] = tile;

        return tile;
    }

#if UNITY_EDITOR
    [ContextMenu("Rebuild Lookups")]
    private void RebuildMenu()
    {
        BuildLookups();
        Debug.Log("[ItemManager] Lookups rebuilt.");
    }
#endif
}

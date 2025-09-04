using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class ShopManager : MonoBehaviour
{
    [Header("UI Settings")]
    public GameObject productPrefab;   // Prefab có Product_UI
    public Transform contentPanel;     // Nơi add các ô
    public GameObject shopUI;          // Panel gốc

    [Header("Local fallback (optional)")]
    public List<ProductData> itemList; // Dùng khi BE chưa có danh mục

    // serverItemId -> ProductData
    private readonly Dictionary<string, ProductData> _productsByItemId =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _isLoading;
    private bool _isShopOpen;

    // ================= LIFECYCLE =================
    private void Start()
    {
        if (shopUI) shopUI.SetActive(false);
        StartCoroutine(LoadShopAndPopulate());
    }

    public void ToggleShop()
    {
        _isShopOpen = !_isShopOpen;
        if (shopUI) shopUI.SetActive(_isShopOpen);
    }

    public void Refresh() => StartCoroutine(LoadShopAndPopulate());

    // ================= LOAD & POPULATE =================
    private IEnumerator LoadShopAndPopulate()
    {
        if (_isLoading) yield break;
        _isLoading = true;

        // clear UI cũ
        if (contentPanel)
        {
            for (int i = contentPanel.childCount - 1; i >= 0; i--)
                Destroy(contentPanel.GetChild(i).gameObject);
        }
        _productsByItemId.Clear();

        bool done = false;
        yield return ShopApiClient.GetItemsSell(
            onOk: env =>
            {
                try
                {
                    if (env != null && env.error == 0 && env.data != null)
                    {
                        foreach (var dto in env.data)
                        {
                            var pd = BuildProductDataFromServer(dto);
                            if (pd == null) continue;

                            _productsByItemId[Safe(dto.itemId)] = pd;
                            CreateProductUI(pd);
                        }

                        if (env.data.Length == 0 && itemList != null)
                        {
                            foreach (var local in itemList)
                            {
                                var id = GetItemId(local?.itemData);
                                if (string.IsNullOrEmpty(id)) continue;

                                _productsByItemId[id] = local;
                                CreateProductUI(local);
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[Shop] GetItemsSell rỗng/lỗi → dùng fallback local.");
                        if (itemList != null)
                        {
                            foreach (var local in itemList)
                            {
                                var id = GetItemId(local?.itemData);
                                if (string.IsNullOrEmpty(id)) continue;

                                _productsByItemId[id] = local;
                                CreateProductUI(local);
                            }
                        }
                    }
                }
                finally { done = true; }
            },
            onErr: err =>
            {
                Debug.LogError("[Shop] GetItemsSell HTTP error: " + err);
                done = true;
            }
        );

        while (!done) yield return null;
        _isLoading = false;
    }

    private void CreateProductUI(ProductData pd)
    {
        if (!productPrefab || !contentPanel || pd == null) return;

        var go = Instantiate(productPrefab, contentPanel);
        var ui = go.GetComponent<Product_UI>();
        if (ui != null) ui.Setup(pd, this);
        else Debug.LogError("[Shop] productPrefab chưa gắn Product_UI.");
    }

    private ProductData BuildProductDataFromServer(ShopPriceDto dto)
    {
        if (dto == null || string.IsNullOrEmpty(dto.itemId)) return null;

        // 1) ghép với ProductData inspector nếu trùng itemId
        if (itemList != null)
        {
            foreach (var it in itemList)
            {
                if (it?.itemData == null) continue;
                if (string.Equals(GetItemId(it.itemData), dto.itemId, StringComparison.OrdinalIgnoreCase))
                {
                    it.price = dto.sellPrice;                           // lấy giá server
                    if (!string.IsNullOrEmpty(dto.itemName))
                        it.productName = dto.itemName;
                    return it;
                }
            }
        }

        // 2) tạo runtime ProductData nếu không có sẵn
        var pd = ScriptableObject.CreateInstance<ProductData>();
        pd.productName = string.IsNullOrEmpty(dto.itemName) ? "Item" : dto.itemName;
        pd.price = dto.sellPrice;

        // map icon từ ItemData nếu có
        var im = GameManager.instance ? GameManager.instance.itemManager : FindFirstObjectByType<ItemManager>();
        ItemData data = null;
        if (im != null)
        {
            data = im.GetItemDataByServerId(dto.itemId);
            if (data == null && !string.IsNullOrEmpty(dto.itemName))
                data = im.GetItemDataByName(dto.itemName);
        }
        pd.itemData = data;
        pd.icon = data ? data.icon : pd.icon;

        return pd;
    }

    // ================= SELL =================
    public void SellItem(ProductData product)
    {
        if (product == null)
        {
            Debug.LogWarning("[Shop] Product null.");
            return;
        }

        var auth = AuthManager.Instance;
        string userId = auth != null ? auth.GetCurrentUserId() : null;
        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogWarning("[Shop] Chưa có userId (AuthManager).");
            return;
        }

        var invMgr = InventoryManager.Instance;
        var backpack = invMgr?.GetInventoryByName(InventoryManager.BACKPACK);
        if (backpack == null)
        {
            Debug.LogError("[Shop] Backpack not found.");
            return;
        }

        // Lấy GUID từ ItemData của product
        string targetId = ExtractIdFromItemData(product.itemData);
        if (string.IsNullOrEmpty(targetId))
        {
            Debug.LogWarning("[Shop] ProductData.itemData không có id hợp lệ.");
            return;
        }
        targetId = NormalizeId(targetId);

        // Tìm 1 slot khớp ID (ưu tiên), nếu không có thì khớp theo tên
        int slotIndex = -1;
        for (int i = 0; i < backpack.slots.Count; i++)
        {
            var s = backpack.slots[i];
            if (s == null || s.count <= 0 || s.itemData == null) continue;

            string slotId = NormalizeId(ExtractIdFromAny(s.itemData));
            string slotName = Safe(GetNameFromAny(s.itemData));
            string prodName = Safe(product.productName);

            bool idMatch = !string.IsNullOrEmpty(slotId) && slotId == targetId;
            bool nameMatch = !string.IsNullOrEmpty(slotName) && !string.IsNullOrEmpty(prodName) &&
                             string.Equals(slotName, prodName, StringComparison.OrdinalIgnoreCase);

            if (idMatch || nameMatch)
            {
                slotIndex = i;
                break;
            }
        }

        if (slotIndex < 0)
        {
            Debug.LogWarning($"❌ Không có {product.productName} trong Backpack để bán! (id={targetId})");
            return;
        }

        int quantity = 1;
        StartCoroutine(ShopApiClient.SellItem(
            userId, targetId, quantity,
            onOk: env =>
            {
                if (env != null && env.error == 0)
                {
                    // ✅ Trừ local ngay
                    var s = backpack.slots[slotIndex];
                    if (s != null)
                    {
                        s.count -= quantity;
                        if (s.count <= 0) backpack.Remove(slotIndex);
                    }

                    // ✅ Cộng tiền local
                    CurrencyManager.Instance?.AddCoins(product.price * quantity);
                    Debug.Log($"✅ Đã bán {product.productName}, +{product.price * quantity}$");

                    // 🔄 Hard refresh từ server để dọn record quantity=0 và đồng bộ UI
                    if (invMgr != null)
                    {
                        _ = invMgr.SyncInventory(
                            InventoryManager.BACKPACK,
                            reloadAfterSync: true,
                            allowCreateIfMissing: false,
                            ignoreDebounce: true
                        );
                    }
                }
                else
                {
                    Debug.LogError("[Shop] SELL FAIL: " + (env?.message ?? "null response"));
                }
            },
            onErr: err => Debug.LogError("[Shop] SELL HTTP ERROR: " + err)
        ));
    }

    // ================= Helpers =================
    private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

    private static string GetItemId(ItemData data) => data ? Safe(data.id) : null;

    private static string NormalizeId(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim().Trim('{', '}').ToLowerInvariant(); // GUID compare
    }

    // lấy id từ ItemData
    private static string ExtractIdFromItemData(object itemDataObj)
    {
        if (itemDataObj == null) return null;
        var t = itemDataObj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        string[] names = { "id", "Id", "serverId", "ServerId", "itemId", "ItemId" };

        foreach (var n in names)
        {
            var f = t.GetField(n, flags);
            if (f != null && f.FieldType == typeof(string))
            {
                var v = f.GetValue(itemDataObj) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
            var p = t.GetProperty(n, flags);
            if (p != null && p.PropertyType == typeof(string))
            {
                var v = p.GetValue(itemDataObj) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        return null;
    }

    // lấy id từ Item runtime hoặc ItemData
    private static string ExtractIdFromAny(object obj)
    {
        if (obj == null) return null;

        if (obj is Item runtimeItem)
        {
            var data = ResolveItemDataFromItem(runtimeItem);
            return data != null ? ExtractIdFromItemData(data) : null;
        }
        return ExtractIdFromItemData(obj);
    }

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

    private static string GetNameFromAny(object obj)
    {
        if (obj == null) return null;
        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var n in new[] { "itemName", "ItemName", "productName", "ProductName", "name" })
        {
            var f = t.GetField(n, flags);
            if (f != null && f.FieldType == typeof(string))
                return f.GetValue(obj) as string;

            var p = t.GetProperty(n, flags);
            if (p != null && p.PropertyType == typeof(string))
                return p.GetValue(obj) as string;
        }
        return null;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using System.Linq;
using CGP.Networking.DTOs;
using CGP.Networking.Clients;
using CGP.Gameplay.Items;
using CGP.Gameplay.Auth;                  // AuthManager
using CGP.Gameplay.Systems;               // CurrencyManager
using CGP.Gameplay.Inventory.Presenter;   // InventoryManager

namespace CGP.Gameplay.Shop
{
    public class ShopManager : MonoBehaviour
    {
        [Header("UI Settings")]
        public GameObject productPrefab;     // Prefab có Product_UI
        public Transform contentPanel;       // Nơi add các ô
        public GameObject shopUI;            // Panel gốc   

        [Header("Sell Dialog")]
        [SerializeField] private SellDialogUI sellDialog;   // <-- KÉO VÀO INSPECTOR

        [Header("Local fallback (optional)")]
        public List<ProductData> itemList;   // Dùng khi BE chưa có danh mục

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

            // Tìm component tên "Product_UI" rồi gọi Setup(ProductData, ShopManager)
            var comp = go.GetComponent("Product_UI") as MonoBehaviour;
            if (comp != null)
            {
                var setup = comp.GetType().GetMethod("Setup",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (setup != null)
                {
                    try { setup.Invoke(comp, new object[] { pd, this }); }
                    catch (Exception ex) { Debug.LogError($"[Shop] Product_UI.Setup invoke fail: {ex}"); }
                }
                else Debug.LogError("[Shop] Product_UI không có method Setup(ProductData, ShopManager).");
            }
            else
            {
                go.SendMessage("Setup", pd, SendMessageOptions.DontRequireReceiver);
                Debug.LogWarning("[Shop] productPrefab không gắn Product_UI hoặc tên khác.");
            }
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
                        it.price = dto.sellPrice;
                        if (!string.IsNullOrEmpty(dto.itemName))
                            it.productName = dto.itemName;
                        return it;
                    }
                }
            }

            // 2) runtime ProductData
            var pd = ScriptableObject.CreateInstance<ProductData>();
            pd.productName = string.IsNullOrEmpty(dto.itemName) ? "Item" : dto.itemName;
            pd.price = dto.sellPrice;

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

        // ================= SELL (mở dialog) =================
        public void OpenSellDialog(ProductData product)
        {
            if (product == null || product.itemData == null || sellDialog == null)
            {
                // fallback: bán 1 nếu thiếu dialog
                SellItem(product);
                return;
            }

            // ===== Chuẩn bị =====
            string targetId = NormalizeId(ExtractIdFromItemData(product.itemData));
            int unitPrice = Mathf.Max(0, product.price);

            // Tìm slot thực tế trong Backpack/Toolbar để lấy đúng số lượng
            CGP.Gameplay.InventorySystem.Inventory.Slot foundSlot = null;
            var invMgr = InventoryManager.Instance;

            if (invMgr != null)
            {
                foreach (var invName in new[] { InventoryManager.BACKPACK, InventoryManager.TOOLBAR })
                {
                    var inv = invMgr.GetInventoryByName(invName);
                    if (inv == null || inv.slots == null) continue;

                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.IsEmpty || s.itemData == null) continue;

                        string sid = NormalizeId(ExtractIdFromAny(s.itemData));
                        if (!string.IsNullOrEmpty(sid) && sid == targetId)
                        {
                            foundSlot = s;
                            break;
                        }
                    }
                    if (foundSlot != null) break;
                }
            }

            // ===== Nếu tìm được slot: gọi overload nhận Slot (an toàn nhất) =====
            if (foundSlot != null && !foundSlot.IsEmpty)
            {
                sellDialog.Show(foundSlot, unitPrice, qty => SellQuantityAsync(product, qty));
                return;
            }

            // ===== Fallback: truyền sprite + owned tổng =====
            var spr = product.icon ? product.icon : product.itemData.icon;

            int owned = 0;
            if (invMgr != null && !string.IsNullOrEmpty(targetId))
            {
                // cộng dồn quantity từ snapshot server đang cache
                owned = invMgr.inventoryItems != null
                    ? invMgr.inventoryItems.FindAll(it => NormalizeId(it.itemId) == targetId)
                                           .Sum(it => it.quantity)
                    : 0;
            }

            sellDialog.Show(spr, owned, unitPrice, qty => SellQuantityAsync(product, qty));
        }

        /// <summary>
        /// Giữ lại hàm cũ: bán 1 món (fallback khi thiếu dialog).
        /// </summary>
        public void SellItem(ProductData product)
        {
            if (product == null) return;
            _ = SellQuantityAsync(product, 1);
        }

        /// <summary>
        /// Bán N món: gọi API, trừ kho local (gộp nhiều stack), cộng tiền, sync lại.
        /// </summary>
        private Task<bool> SellQuantityAsync(ProductData product, int quantity)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (product == null || quantity <= 0)
            {
                tcs.SetResult(false);
                return tcs.Task;
            }

            var auth = AuthManager.Instance;
            string userId = auth?.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                Debug.LogWarning("[Shop] Chưa có userId (AuthManager).");
                tcs.SetResult(false);
                return tcs.Task;
            }

            string targetId = NormalizeId(ExtractIdFromItemData(product.itemData));
            if (string.IsNullOrEmpty(targetId))
            {
                Debug.LogWarning("[Shop] ProductData.itemData không có id hợp lệ.");
                tcs.SetResult(false);
                return tcs.Task;
            }

            StartCoroutine(ShopApiClient.SellItem(
                userId, targetId, quantity,
                onOk: env =>
                {
                    bool ok = env != null && env.error == 0;
                    if (!ok) { tcs.TrySetResult(false); return; }

                    // --- Trừ local từ Backpack (có thể nhiều stack) ---
                    var invMgr = InventoryManager.Instance;
                    var backpack = invMgr?.GetInventoryByName(InventoryManager.BACKPACK);

                    int need = quantity;
                    if (backpack != null)
                    {
                        for (int i = 0; i < backpack.slots.Count && need > 0; i++)
                        {
                            var s = backpack.slots[i];
                            if (s == null || s.count <= 0 || s.itemData == null) continue;

                            string sid = NormalizeId(ExtractIdFromAny(s.itemData));
                            if (sid != targetId) continue;

                            int take = Mathf.Min(need, s.count);
                            s.count -= take;
                            need -= take;
                            if (s.count <= 0) backpack.Remove(i);
                        }

                        // đồng bộ từ server cho sạch record
                        _ = invMgr.SyncInventory(
                            InventoryManager.BACKPACK,
                            reloadAfterSync: true,
                            allowCreateIfMissing: false,
                            ignoreDebounce: true
                        );
                    }

                    // --- Cộng tiền ---
                    int gained = Mathf.Max(0, product.price) * quantity;
                    CurrencyManager.Instance?.AddCoins(gained);

                    Debug.Log($"✅ Đã bán {product.productName} x{quantity}, +{gained}$");
                    tcs.TrySetResult(true);
                },
                onErr: err =>
                {
                    Debug.LogError("[Shop] SELL HTTP ERROR: " + err);
                    tcs.TrySetResult(false);
                }
            ));

            return tcs.Task;
        }

        // ================= Helpers =================
        private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

        private static string GetItemId(ItemData data) => data ? Safe(data.id) : null;

        private static string NormalizeId(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Trim().Trim('{', '}').ToLowerInvariant(); // GUID compare
        }

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
}

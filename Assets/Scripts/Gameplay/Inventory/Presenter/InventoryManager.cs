using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

using CGP.Framework;
using CGP.Gameplay.Items;             // Item, ItemData, ItemManager
using CGP.Gameplay.InventorySystem;   // Inventory + nested Slot
using CGP.Gameplay.Config;            // StarterPackConfig
using CGP.Networking.DTOs;
using CGP.Networking.Clients;
using CGP.Gameplay.Auth;
using CGP.Gameplay.Inventory.Presenter;

// ===== Alias để phân biệt rõ ràng =====
using Inv = CGP.Gameplay.InventorySystem.Inventory;
using Slot = CGP.Gameplay.InventorySystem.Inventory.Slot;

namespace CGP.Gameplay.Inventory.Presenter
{
    [DefaultExecutionOrder(-50)]
    public class InventoryManager : MonoBehaviour
    {
        // ======================= DATA MODELS =======================
        [Serializable] public class InventoryResponse { public int error; public string message; public InventoryItem[] data; }
        [Serializable] public class InventorySingleResponse { public int error; public string message; public InventoryItem data; }

        [Serializable]
        public class InventoryItem
        {
            public string id;
            public string userId;
            public string itemId;
            public string itemType;
            public int quantity;
            public string inventoryType;
            public int slotIndex;
            public string acquiredAt;
            public string creationDate;
            public string modificationDate;

            public int quality;
        }

        [Serializable]
        private class UpdateDto
        {
            public string Id;
            public string UserId;
            public string ItemId;
            public int Quantity;
            public string InventoryType;
            public int SlotIndex;
        }

        private struct HttpResult
        {
            public bool ok;
            public long code;
            public string body;
            public string error;
        }

        // ======================= SINGLETON & FIELDS =======================
        public static InventoryManager Instance { get; private set; }

        public List<InventoryItem> inventoryItems = new();
        private readonly Dictionary<string, Inv> inventoryByName = new();

        public const string BACKPACK = "Backpack";
        public const string TOOLBAR = "Toolbar";

        [Header("Backpack")] public Inv backpack;
        public int backpackSlotsCount = 27;

        [Header("Toolbar")] public Inv toolbar;
        public int toolbarSlotsCount = 7; // đổi thành 9 nếu UI có 9 ô

        private bool isInitialized;
        private bool isLoadingInventory;
        private bool isDragging;
        private bool isSyncing;
        private readonly Queue<Action> pendingOperations = new();

        public event Action OnInventoryLoaded;

        // Map "<InventoryName>:<SlotIndex>" -> recordId
        private readonly Dictionary<string, string> recordIdBySlot = new();
        private static string SlotKey(string inv, int slot) => $"{inv}:{slot}";

        // Khi move: lưu oldRid để fallback POST+DELETE
        private readonly Dictionary<string, string> pendingMoveRidByDestKey = new();

        // Debounce sync THEO TỪNG INVENTORY
        private readonly Dictionary<string, float> _lastSyncAtByInv = new();

        // ==== DRAG SNAPSHOT ====
        private Slot _dragSnapshot;
        private string _dragFromInv;
        private int _dragFromSlot = -1;

        // ======================= LIFECYCLE =======================
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            DontDestroyOnLoad(gameObject);
            InitializeEmptyInventories();

            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnUserInfoReceived += OnUserDataReceived;
                if (AuthManager.Instance.IsUserDataReady)
                    _ = LoadInventory(AuthManager.Instance.GetCurrentUserId());
            }
            else
            {
                StartCoroutine(WaitForAuthManager());
            }
        }

        private void OnDestroy()
        {
            if (AuthManager.Instance != null)
                AuthManager.Instance.OnUserInfoReceived -= OnUserDataReceived;
        }

        // ======================= AUTH & HELPERS =======================
        private static void AttachJsonHeaders(UnityWebRequest req)
        {
            req.SetRequestHeader("Content-Type", "application/json");
            string token = LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", $"Bearer {token}");
        }

        private static void AttachAuthOnly(UnityWebRequest req)
        {
            string token = LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", $"Bearer {token}");
        }

        private bool EnsureAuthReady(out string userId)
        {
            userId = AuthManager.Instance?.GetCurrentUserId();
            if (AuthManager.Instance == null || !AuthManager.Instance.IsUserDataReady || string.IsNullOrEmpty(userId))
            {
                return false;
            }
            return true;
        }
        // Overload async: nhận StarterPackConfig + named param 'reloadAfter' (để GameManager await)
        public async Task EnsureStarterPackOnFirstLogin(StarterPackConfig cfg, bool reloadAfter = true)
        {
            const string KEY = "cgp_starter_pack_given";

            // Nếu là lần đầu đăng nhập, có thể tặng vật phẩm từ cfg
            if (PlayerPrefs.GetInt(KEY, 0) == 0)
            {
                // TODO: nếu bạn muốn cấp quà tân thủ theo cfg, làm ở đây.
                // Ví dụ (giả sử cfg có danh sách itemId/quantity/slot):
                // var uid = AuthManager.Instance?.GetCurrentUserId();
                // foreach (var e in cfg.items)
                //     await PostCreateInventory(uid, e.itemId, e.quantity, BACKPACK, e.slotIndex);

                PlayerPrefs.SetInt(KEY, 1);
                PlayerPrefs.Save();
            }

            if (reloadAfter)
            {
                // Đồng bộ cả Backpack & Toolbar để UI/logic cập nhật ngay
                var t1 = SyncInventory(BACKPACK, reloadAfterSync: true, allowCreateIfMissing: true, ignoreDebounce: true);
                var t2 = SyncInventory(TOOLBAR, reloadAfterSync: true, allowCreateIfMissing: true, ignoreDebounce: true);
                await Task.WhenAll(t1, t2);
            }
        }

        // Overload: 2 tham số (khớp call site trong GameManager)
        public void EnsureStarterPackOnFirstLogin(bool reloadAfterSync = true, bool allowCreateIfMissing = true)
        {
            StartCoroutine(_StarterPackSyncAfterFlags(reloadAfterSync, allowCreateIfMissing));
        }

        private IEnumerator _StarterPackSyncAfterFlags(bool reloadAfterSync, bool allowCreateIfMissing)
        {
            float t = 0f;
            while ((AuthManager.Instance == null || !AuthManager.Instance.IsUserDataReady) && t < 5f)
            {
                t += Time.deltaTime;
                yield return null;
            }

            if (!reloadAfterSync) yield break;

            var task1 = SyncInventory(BACKPACK, reloadAfterSync: true, allowCreateIfMissing: allowCreateIfMissing, ignoreDebounce: true);
            var task2 = SyncInventory(TOOLBAR, reloadAfterSync: true, allowCreateIfMissing: allowCreateIfMissing, ignoreDebounce: true);
            while (!task1.IsCompleted || !task2.IsCompleted) yield return null;
        }

        // ======================= INIT =======================
        private void InitializeEmptyInventories()
        {
            backpack = new Inv(backpackSlotsCount);
            toolbar = new Inv(toolbarSlotsCount);

            inventoryByName.Clear();
            inventoryByName.Add(BACKPACK, backpack);
            inventoryByName.Add(TOOLBAR, toolbar);

            backpack.slots = new List<Slot>();
            toolbar.slots = new List<Slot>();

            for (int i = 0; i < backpackSlotsCount; i++) backpack.slots.Add(new Slot());
            for (int i = 0; i < toolbarSlotsCount; i++) toolbar.slots.Add(new Slot());

            recordIdBySlot.Clear();
            pendingMoveRidByDestKey.Clear();
            isInitialized = true;
        }

        // ======================= DRAG STATE =======================
        public void SetDragState(bool dragging)
        {
            isDragging = dragging;
            if (!dragging) ProcessPendingOperations();
        }
        public bool IsDragging() => isDragging;
        public bool IsSyncing() => isSyncing;

        private void ProcessPendingOperations()
        {
            while (pendingOperations.Count > 0 && !isDragging && !isSyncing)
                pendingOperations.Dequeue()?.Invoke();
        }

        // ==== DRAG SNAPSHOT API ====
        public void BeginDrag(string fromInventory, int fromSlot)
        {
            _dragFromInv = fromInventory;
            _dragFromSlot = fromSlot;
            _dragSnapshot = null;

            var inv = GetInventoryByName(fromInventory);
            if (inv != null && fromSlot >= 0 && fromSlot < inv.slots.Count)
            {
                var s = inv.slots[fromSlot];
                if (!s.IsEmpty)
                {
                    _dragSnapshot = new Slot
                    {
                        itemName = s.itemName,
                        count = s.count,
                        icon = s.icon,
                        itemData = s.itemData
                    };
                }
            }
        }

        public void EndDrag() { }

        // ======================= AUTH WAIT =======================
        private IEnumerator WaitForAuthManager()
        {
            float timeout = 10f, t = 0f;
            while (AuthManager.Instance == null && t < timeout) { t += Time.deltaTime; yield return null; }

            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnUserInfoReceived += OnUserDataReceived;
                if (AuthManager.Instance.IsUserDataReady) _ = LoadInventory(AuthManager.Instance.GetCurrentUserId());
                else
                {
                    yield return AuthManager.Instance.StartCoroutine(AuthManager.Instance.GetCurrentUser());
                    if (AuthManager.Instance.IsUserDataReady) _ = LoadInventory(AuthManager.Instance.GetCurrentUserId());
                }
            }
        }

        private async void OnUserDataReceived(bool success, string message, UserData userData)
        {
            if (!success || userData == null) { return; }
            if (isLoadingInventory || isDragging) return;

            isLoadingInventory = true;
            await LoadInventory(userData.id);
            isLoadingInventory = false;
        }

        // ======================= HTTP CORE =======================
        private static async Task<HttpResult> SendAsync(UnityWebRequest req, string tag)
        {
            var t0 = Time.realtimeSinceStartup;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            var ms = (Time.realtimeSinceStartup - t0) * 1000f;
            var body = req.downloadHandler != null ? (req.downloadHandler.text ?? "") : "";
            var err = req.error ?? "";
            bool ok = req.result == UnityWebRequest.Result.Success;

            return new HttpResult { ok = ok, code = req.responseCode, body = body, error = err };
        }

        private static bool IsNotExistError(HttpResult r)
        {
            if (r.ok) return false;
            var text = (r.body ?? string.Empty).ToLowerInvariant();
            return (r.code == 400 || r.code == 404)
                && (text.Contains("không tồn tại") || text.Contains("not exist") || text.Contains("not found"));
        }

        private static bool IsSlotOccupied(HttpResult r)
        {
            if (r.ok) return false;
            var t = (r.body ?? string.Empty).ToLowerInvariant();
            return t.Contains("vị trí đã tồn tại") || t.Contains("đã tồn tại vật phẩm")
                   || t.Contains("slot already") || t.Contains("position already")
                   || t.Contains("kho đồ đã tồn tại");
        }

        // ======================= LOAD & APPLY =======================
        private async Task<List<InventoryItem>> FetchInventoryData(string userId)
        {
            var list = new List<InventoryItem>();
            try
            {
                string url = ApiRoutes.Inventory.GET_BY_USERID.Replace("{userId}", userId);
                using var req = UnityWebRequest.Get(url);
                AttachJsonHeaders(req);
                var res = await SendAsync(req, "FetchInventoryData");
                if (!res.ok) return list;

                var parsed = JsonUtility.FromJson<InventoryResponse>(res.body);
                if (parsed?.data != null) list = new List<InventoryItem>(parsed.data);
            }
            catch { }
            return list;
        }

        private async Task LoadInventory(string userId, bool applyToLocal = true)
        {
            try
            {
                var list = await FetchInventoryData(userId);
                inventoryItems = list ?? new List<InventoryItem>();
                if (applyToLocal)
                {
                    ApplyServerInventoryToLocal();
                    OnInventoryLoaded?.Invoke();
                }
            }
            catch { }
        }

        private Inv GetInventoryByTypeString(string type)
            => string.Equals(type, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? toolbar : backpack;

        private static int FindFirstEmpty(Inv inv)
        {
            for (int i = 0; i < inv.slots.Count; i++) if (inv.slots[i].IsEmpty) return i;
            return -1;
        }

        private void ClearAllSlots()
        {
            for (int i = 0; i < backpack.slots.Count; i++) backpack.slots[i] = new Slot();
            for (int i = 0; i < toolbar.slots.Count; i++) toolbar.slots[i] = new Slot();
            recordIdBySlot.Clear();
            pendingMoveRidByDestKey.Clear();
        }

        private void ApplyServerInventoryToLocal()
        {
            if (isDragging) return;

            ClearAllSlots();
            if (inventoryItems == null || inventoryItems.Count == 0) return;

            var im = GameManager.instance ? GameManager.instance.itemManager
                                          : FindFirstObjectByType<ItemManager>();

            foreach (var it in inventoryItems)
            {
                if (it == null || it.quantity <= 0) continue;

                string invName = string.Equals(it.inventoryType, TOOLBAR, StringComparison.OrdinalIgnoreCase)
                    ? TOOLBAR : BACKPACK;
                var inv = GetInventoryByTypeString(invName) ?? backpack;

                int target = it.slotIndex;
                if (target < 0 || target >= inv.slots.Count) continue;

                var slot = new Slot { count = it.quantity };

                ItemData data = null;
                if (im != null)
                {
                    if (!string.IsNullOrEmpty(it.itemId))
                        data = im.GetItemDataByServerId(it.itemId);

                    if (data == null && !string.IsNullOrEmpty(it.itemType))
                        data = im.GetItemDataByName(it.itemType);
                }

                if (data != null)
                {
                    slot.itemName = data.itemName;
                    slot.icon = data.icon;
                    slot.itemData = data;
                }
                else
                {
                    slot.itemName = !string.IsNullOrEmpty(it.itemType) ? it.itemType : (it.itemId ?? "(Unknown)");
                    slot.icon = null;
                    slot.itemData = null;
                }

                inv.slots[target] = slot;

                if (!string.IsNullOrEmpty(it.id))
                    recordIdBySlot[SlotKey(invName, target)] = it.id;
            }
        }

        public Inv GetInventoryByName(string name)
            => inventoryByName.TryGetValue(name, out var inv) ? inv : null;

        // ======================= CRUD (PUBLIC) =======================
        private int GetServerSlotIndexForItem(string inventoryName, string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return -1;
            var rec = inventoryItems.FirstOrDefault(i =>
                i.itemId == itemId &&
                string.Equals(i.inventoryType, inventoryName, StringComparison.OrdinalIgnoreCase));
            return rec != null ? rec.slotIndex : -1;
        }

        private int GetBestSlotIndexForAdd(Inv inv, Item item)
        {
            var id = item?.Data?.id;
            if (string.IsNullOrEmpty(id)) return -1;

            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                if (!s.IsEmpty && s.itemData != null && s.itemData.id == id && s.count < s.maxAllowed)
                    return i;
            }
            for (int i = 0; i < inv.slots.Count; i++)
                if (inv.slots[i].IsEmpty) return i;

            return -1;
        }

        public async void AddItem(string inventoryName, Item item)
        {
            if (!isInitialized) return;

            if (isDragging || isSyncing) { pendingOperations.Enqueue(() => AddItem(inventoryName, item)); return; }
            if (!EnsureAuthReady(out var userId)) return;
            if (!inventoryByName.TryGetValue(inventoryName, out var inventory)) return;

            var itemId = item?.Data?.id;
            if (string.IsNullOrEmpty(itemId)) return;

            int serverSlot = GetServerSlotIndexForItem(inventoryName, itemId);
            int idx = serverSlot >= 0 ? serverSlot : GetBestSlotIndexForAdd(inventory, item);

            if (idx >= 0)
            {
                var s = inventory.slots[idx];
                if (!s.IsEmpty && s.itemData != null && s.itemData.id == itemId)
                {
                    s.count += 1; // stack
                }
                else
                {
                    inventory.slots[idx] = new Slot
                    {
                        itemName = item.Data.itemName,
                        icon = item.Data.icon,
                        itemData = item.Data,
                        count = 1
                    };
                }
            }

            bool allowCreateIfMissing = serverSlot < 0;
            await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: allowCreateIfMissing);
        }

        public async Task<bool> NormalizeDuplicateRecords(string inventoryName)
        {
            if (!EnsureAuthReady(out var userId)) return false;

            var latest = await FetchInventoryData(userId);
            inventoryItems = latest ?? new List<InventoryItem>();

            var sameInv = inventoryItems
                .Where(x => string.Equals(x.inventoryType, inventoryName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var groups = sameInv
                .Where(x => !string.IsNullOrEmpty(x.itemId))
                .GroupBy(x => x.itemId);

            bool changed = false;

            foreach (var g in groups)
            {
                var list = g.ToList();
                if (list.Count <= 1) continue;

                var keep = list[0];
                int totalQty = list.Sum(x => x.quantity);

                var dto = new UpdateDto
                {
                    Id = keep.id,
                    UserId = keep.userId,
                    ItemId = keep.itemId,
                    Quantity = totalQty,
                    InventoryType = keep.inventoryType,
                    SlotIndex = keep.slotIndex
                };
                var put = await PutUpdateInventory(dto);
                changed |= put.ok;

                for (int i = 1; i < list.Count; i++)
                {
                    await DeleteRecordById(list[i].id);
                    changed = true;
                }
            }

            if (changed)
            {
                await LoadInventory(userId, applyToLocal: true);
            }
            return changed;
        }

        public async Task<bool> MoveItem(string fromInventory, int fromSlot, string toInventory, int toSlot)
        {
            if (!isInitialized || isDragging || isSyncing) { return false; }

            bool fromExists = inventoryByName.TryGetValue(fromInventory, out var fromInv);
            bool toExists = inventoryByName.TryGetValue(toInventory, out var toInv);
            if (!fromExists || !toExists) { return false; }

            if (fromSlot < 0 || fromSlot >= fromInv.slots.Count) { return false; }
            if (toSlot < 0 || toSlot >= toInv.slots.Count) { return false; }

            var fromSlotData = fromInv.slots[fromSlot];
            var toSlotData = toInv.slots[toSlot];

            var kFrom = SlotKey(fromInventory, fromSlot);
            var kTo = SlotKey(toInventory, toSlot);
            recordIdBySlot.TryGetValue(kFrom, out var ridFrom);
            recordIdBySlot.TryGetValue(kTo, out var ridTo);

            if (fromSlotData.IsEmpty && _dragSnapshot != null &&
                string.Equals(_dragFromInv, fromInventory, StringComparison.OrdinalIgnoreCase) &&
                _dragFromSlot == fromSlot)
            {
                fromSlotData = new Slot
                {
                    itemName = _dragSnapshot.itemName,
                    count = _dragSnapshot.count,
                    icon = _dragSnapshot.icon,
                    itemData = _dragSnapshot.itemData
                };
                fromInv.slots[fromSlot] = fromSlotData;
            }

            if (fromSlotData.IsEmpty)
            {
                if (!string.IsNullOrEmpty(ridFrom) && EnsureAuthReady(out var userId))
                {
                    var list = await FetchInventoryData(userId);
                    var srv = list.FirstOrDefault(x => x.id == ridFrom);
                    if (srv != null && !string.IsNullOrEmpty(srv.itemId))
                    {
                        var data = GameManager.instance.itemManager.GetItemDataByServerId(srv.itemId);
                        if (data != null)
                        {
                            fromSlotData = new Slot
                            {
                                itemName = data.itemName,
                                icon = data.icon,
                                itemData = data,
                                count = Math.Max(1, srv.quantity)
                            };
                            fromInv.slots[fromSlot] = fromSlotData;
                        }
                    }
                }
            }

            if (fromSlotData.IsEmpty) { return false; }

            isSyncing = true;
            try
            {
                // MOVE
                if (toSlotData.IsEmpty)
                {
                    toInv.slots[toSlot] = new Slot
                    {
                        itemName = fromSlotData.itemName,
                        count = fromSlotData.count,
                        icon = fromSlotData.icon,
                        itemData = fromSlotData.itemData
                    };
                    fromInv.slots[fromSlot] = new Slot();

                    if (!string.IsNullOrEmpty(ridFrom))
                        recordIdBySlot[SlotKey(toInventory, toSlot)] = ridFrom;
                    recordIdBySlot.Remove(kFrom);

                    if (!string.IsNullOrEmpty(ridFrom))
                        pendingMoveRidByDestKey[SlotKey(toInventory, toSlot)] = ridFrom;
                }
                // STACK
                else if (toSlotData.itemData != null && fromSlotData.itemData != null &&
                         toSlotData.itemData.id == fromSlotData.itemData.id)
                {
                    toInv.slots[toSlot].count += fromSlotData.count;
                    fromInv.slots[fromSlot] = new Slot();

                    if (!string.IsNullOrEmpty(ridFrom))
                    {
                        await DeleteRecordById(ridFrom);
                        recordIdBySlot.Remove(kFrom);
                    }
                }
                // SWAP
                else
                {
                    var temp = new Slot
                    {
                        itemName = toSlotData.itemName,
                        count = toSlotData.count,
                        icon = toSlotData.icon,
                        itemData = toSlotData.itemData
                    };

                    toInv.slots[toSlot] = new Slot
                    {
                        itemName = fromSlotData.itemName,
                        count = fromSlotData.count,
                        icon = fromSlotData.icon,
                        itemData = fromSlotData.itemData
                    };
                    fromInv.slots[fromSlot] = temp;

                    if (!string.IsNullOrEmpty(ridFrom))
                        recordIdBySlot[SlotKey(toInventory, toSlot)] = ridFrom;
                    else
                        recordIdBySlot.Remove(SlotKey(toInventory, toSlot));

                    if (!string.IsNullOrEmpty(ridTo))
                        recordIdBySlot[SlotKey(fromInventory, fromSlot)] = ridTo;
                    else
                        recordIdBySlot.Remove(SlotKey(fromInventory, fromSlot));
                }

                if (fromInventory != toInventory)
                {
                    await SyncInventory(fromInventory, reloadAfterSync: false, allowCreateIfMissing: false);
                    await SyncInventory(toInventory, reloadAfterSync: true, allowCreateIfMissing: false);
                }
                else
                {
                    await SyncInventory(fromInventory, reloadAfterSync: true, allowCreateIfMissing: false);
                }
                return true;
            }
            catch { return false; }
            finally
            {
                isSyncing = false;
                _dragSnapshot = null;
                _dragFromInv = null;
                _dragFromSlot = -1;
            }
        }

        // PATCH: HasItem so sánh theo itemId
        public bool HasItem(string inventoryName, Item item)
        {
            if (!inventoryByName.ContainsKey(inventoryName)) return false;
            var inv = inventoryByName[inventoryName];
            var id = item?.Data?.id;
            if (string.IsNullOrEmpty(id)) return false;

            foreach (var s in inv.slots)
                if (!s.IsEmpty && s.itemData != null && s.itemData.id == id) return true;
            return false;
        }

        public async void UseItem(string inventoryName, int slotIndex, int amount = 1)
        {
            if (!isInitialized) return;
            if (isDragging || isSyncing) { pendingOperations.Enqueue(() => UseItem(inventoryName, slotIndex, amount)); return; }

            if (!ValidateInventories(inventoryName, out var inv)) return;
            if (slotIndex < 0 || slotIndex >= inv.slots.Count) return;

            inv.Remove(slotIndex, amount);
            if (inv.slots[slotIndex] == null) inv.slots[slotIndex] = new Slot();

            if (inv.slots[slotIndex].count <= 0)
                await DeleteItem(inventoryName, slotIndex);
            else
                await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: true);
        }

        public async Task DeleteItem(string inventoryName, int slotIndex)
        {
            if (!inventoryByName.TryGetValue(inventoryName, out var inv)) return;
            if (slotIndex < 0 || slotIndex >= inv.slots.Count || inv.slots[slotIndex].IsEmpty) return;

            var key = SlotKey(inventoryName, slotIndex);
            if (recordIdBySlot.TryGetValue(key, out var recordId) && !string.IsNullOrEmpty(recordId))
            {
                if (await DeleteRecordById(recordId))
                {
                    inv.Remove(slotIndex);
                    recordIdBySlot.Remove(key);
                    await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: true);
                }
            }
            else inv.Remove(slotIndex);
        }

        public void TryConsume(string inventoryName, int slotIndex, int amount = 1)
        {
            if (!inventoryByName.TryGetValue(inventoryName, out var inv)) return;
            if (slotIndex < 0 || slotIndex >= inv.slots.Count) return;

            var s = inv.slots[slotIndex];
            if (s == null || s.IsEmpty) return;

            s.count = Mathf.Max(0, s.count - amount);
            if (s.count == 0) inv.slots[slotIndex] = new Slot();

            _ = SyncInventory(inventoryName,
                reloadAfterSync: true,
                allowCreateIfMissing: false);
        }

        // ======================= LOW-LEVEL SERVER OPS =======================
        private async Task<bool> DeleteRecordById(string recordId)
        {
            string url = ApiRoutes.Inventory.DELETE_ITEM.Replace("{inventoryId}", recordId);
            using var req = UnityWebRequest.Delete(url);
            AttachAuthOnly(req);
            var res = await SendAsync(req, "DeleteRecord");
            return res.ok;
        }

        private async Task<bool> ExistsRecord(string recordId)
        {
            if (string.IsNullOrEmpty(recordId)) return false;

            string url = ApiRoutes.Inventory.GET_BY_ID.Replace("{inventoryId}", recordId);
            using var req = UnityWebRequest.Get(url);
            AttachAuthOnly(req);

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result == UnityWebRequest.Result.Success) return true;
            if (req.responseCode == 404) return false;

            return false;
        }

        private async Task<string> PostCreateInventory(string userId, string itemId, int qty, string inventoryName, int slotIndex, int? quality = null)
        {
            if (slotIndex < 0) return null;

            string url = ApiRoutes.Inventory.ADD_ITEM_TO_INVENTORY;
            var form = new WWWForm();
            form.AddField("UserId", userId);
            form.AddField("ItemId", itemId);
            form.AddField("Quantity", qty);
            form.AddField("InventoryType", inventoryName);
            form.AddField("SlotIndex", slotIndex);
            if (quality.HasValue) form.AddField("Quality", quality.Value);

            using var req = UnityWebRequest.Post(url, form);
            req.downloadHandler = new DownloadHandlerBuffer();
            AttachAuthOnly(req);

            var res = await SendAsync(req, "CreateInventory");
            if (!res.ok)
            {
                if (IsSlotOccupied(res))
                {
                    if (EnsureAuthReady(out var userIdOcc))
                    {
                        var mapOccupied = await FetchServerRecordMap(userIdOcc);
                        if (mapOccupied != null && mapOccupied.TryGetValue(SlotKey(inventoryName, slotIndex), out var occupiedId)
                            && !string.IsNullOrEmpty(occupiedId))
                        {
                            recordIdBySlot[SlotKey(inventoryName, slotIndex)] = occupiedId;
                            return occupiedId;
                        }
                    }
                }
                return null;
            }

            string newId = null;
            try
            {
                var single = JsonUtility.FromJson<InventorySingleResponse>(res.body);
                newId = single?.data?.id;
                if (string.IsNullOrEmpty(newId))
                {
                    var m = Regex.Match(res.body ?? "", "\"id\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success) newId = m.Groups[1].Value;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(newId))
            {
                if (EnsureAuthReady(out var userIdFetch))
                {
                    var map = await FetchServerRecordMap(userIdFetch);
                    map?.TryGetValue(SlotKey(inventoryName, slotIndex), out newId);
                }
            }

            if (!string.IsNullOrEmpty(newId))
                recordIdBySlot[SlotKey(inventoryName, slotIndex)] = newId;

            return newId;
        }

        private async Task<HttpResult> PutUpdateInventory(UpdateDto dto, int? quality = null)
        {
            string url = ApiRoutes.Inventory.UPDATE_ITEM;

            var form = new WWWForm();
            form.AddField("Id", dto.Id ?? "");
            form.AddField("UserId", dto.UserId ?? "");
            form.AddField("ItemId", dto.ItemId ?? "");
            form.AddField("Quantity", dto.Quantity);
            form.AddField("InventoryType", dto.InventoryType ?? "");
            form.AddField("SlotIndex", dto.SlotIndex);
            if (quality.HasValue) form.AddField("Quality", quality.Value);

            var req = UnityWebRequest.Post(url, form);
            req.method = UnityWebRequest.kHttpVerbPUT;
            req.downloadHandler = new DownloadHandlerBuffer();
            AttachAuthOnly(req);

            return await SendAsync(req, "SyncInventory(PUT)");
        }

        private async Task<Dictionary<string, string>> FetchServerRecordMap(string userId)
        {
            var map = new Dictionary<string, string>();
            string url = ApiRoutes.Inventory.GET_BY_USERID.Replace("{userId}", userId);

            using var req = UnityWebRequest.Get(url);
            AttachJsonHeaders(req);

            var res = await SendAsync(req, "FetchServerRecordMap");
            if (!res.ok) return map;

            try
            {
                var resp = JsonUtility.FromJson<InventoryResponse>(res.body);
                if (resp?.data != null)
                    foreach (var it in resp.data)
                    {
                        var invName = string.Equals(it.inventoryType, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? TOOLBAR : BACKPACK;
                        map[$"{invName}:{it.slotIndex}"] = it.id;
                    }
            }
            catch { }
            return map;
        }

        private class ServerItemInfo
        {
            public string id;
            public int slotIndex;
            public int quantity;
        }

        private async Task<Dictionary<string, ServerItemInfo>> FetchServerItemMapByItemId(string userId, string inventoryName)
        {
            var map = new Dictionary<string, ServerItemInfo>();
            string url = ApiRoutes.Inventory.GET_BY_USERID.Replace("{userId}", userId);

            using var req = UnityWebRequest.Get(url);
            AttachJsonHeaders(req);

            var res = await SendAsync(req, "FetchServerItemMapByItemId");
            if (!res.ok) return map;

            try
            {
                var resp = JsonUtility.FromJson<InventoryResponse>(res.body);
                if (resp?.data != null)
                {
                    foreach (var it in resp.data)
                    {
                        var invName = string.Equals(it.inventoryType, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? TOOLBAR : BACKPACK;
                        if (!string.Equals(invName, inventoryName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.IsNullOrEmpty(it.itemId) || string.IsNullOrEmpty(it.id)) continue;

                        map[it.itemId] = new ServerItemInfo
                        {
                            id = it.id,
                            slotIndex = it.slotIndex,
                            quantity = it.quantity
                        };
                    }
                }
            }
            catch { }

            return map;
        }

        private async Task<Dictionary<string, ServerItemInfo>> FetchServerItemMapByItemId_All(string userId)
        {
            var map = new Dictionary<string, ServerItemInfo>();
            string url = ApiRoutes.Inventory.GET_BY_USERID.Replace("{userId}", userId);

            using var req = UnityWebRequest.Get(url);
            AttachJsonHeaders(req);

            var res = await SendAsync(req, "FetchServerItemMapByItemId_All");
            if (!res.ok) return map;

            try
            {
                var resp = JsonUtility.FromJson<InventoryResponse>(res.body);
                if (resp?.data != null)
                {
                    foreach (var it in resp.data)
                    {
                        if (string.IsNullOrEmpty(it.itemId) || string.IsNullOrEmpty(it.id)) continue;

                        if (!map.ContainsKey(it.itemId))
                        {
                            map[it.itemId] = new ServerItemInfo
                            {
                                id = it.id,
                                slotIndex = it.slotIndex,
                                quantity = it.quantity
                            };
                        }
                    }
                }
            }
            catch { }

            return map;
        }

        private async Task<bool> RecreateSameSlotRecord(
            string userId, string inventoryName, int slotIndex,
            string itemId, int quantity, string recordId, string key, int? quality = null)
        {
            var delOk = await DeleteRecordById(recordId);
            if (!delOk) { return false; }

            var newId = await PostCreateInventory(userId, itemId, quantity, inventoryName, slotIndex, quality);
            if (!string.IsNullOrEmpty(newId))
            {
                recordIdBySlot[key] = newId;
                return true;
            }
            return false;
        }

        // ======================= SYNC CORE =======================
        public async Task<bool> SyncInventory(
    string inventoryName,
    bool reloadAfterSync = true,
    bool allowCreateIfMissing = true,
    bool ignoreDebounce = false)
        {
            try
            {
                if (!EnsureAuthReady(out var userId))
                {
                    Debug.LogWarning("[Inventory] SyncInventory: Auth not ready");
                    return false;
                }

                if (!ValidateInventories(inventoryName, out var inv))
                {
                    Debug.LogError("[Inventory] SyncInventory: ValidateInventories FAILED");
                    return false;
                }

                if (!ignoreDebounce)
                {
                    if (!_lastSyncAtByInv.TryGetValue(inventoryName, out var last)) last = -999f;
                    if (Time.unscaledTime - last < 0.1f) return false;
                }
                _lastSyncAtByInv[inventoryName] = Time.unscaledTime;

                // --- Chuẩn hóa slots: không để phần tử null
                for (int i = 0; i < inv.slots.Count; i++)
                    if (inv.slots[i] == null) inv.slots[i] = new Slot();

                // --- Lấy snapshot server + ép non-null
                var serverList = await FetchInventoryData(userId) ?? new List<InventoryItem>();
                var serverSnapshot = BuildServerSnapshot(serverList) ?? new Dictionary<string, InventoryItem>();
                var serverMap = await FetchServerRecordMap(userId) ?? new Dictionary<string, string>();
                var serverItemMap = await FetchServerItemMapByItemId_All(userId) ?? new Dictionary<string, ServerItemInfo>();

                Debug.Log($"[Inventory] SyncInventory begin inv={inventoryName} slots={inv.slots.Count} " +
                          $"srvList={serverList.Count} srvMap={serverMap.Count} srvItemMap={serverItemMap.Count}");

                bool anyChanged = false;

                void RemovePendingByRidLocal(string rid)
                {
                    var toRemove = new List<string>();
                    foreach (var kv in pendingMoveRidByDestKey)
                        if (kv.Value == rid) toRemove.Add(kv.Key);
                    foreach (var k in toRemove) pendingMoveRidByDestKey.Remove(k);
                }

                // ===== PASS 1: PUT / MOVE / CREATE =====
                for (int i = 0; i < inv.slots.Count; i++)
                {
                    var s = inv.slots[i];
                    if (s == null || s.IsEmpty || s.itemData == null || string.IsNullOrEmpty(s.itemData.id)) continue;

                    string key = SlotKey(inventoryName, i);

                    // Đảm bảo serverMap không null
                    serverMap.TryGetValue(key, out var serverId);

                    if (!string.IsNullOrEmpty(serverId))
                    {
                        // Nếu snapshot đã có record này & thông tin giống hệt → bỏ qua
                        if (serverSnapshot.TryGetValue(serverId, out var snap))
                        {
                            bool sameItem = snap.itemId == s.itemData.id;
                            bool sameQty = snap.quantity == s.count;
                            bool sameInv = string.Equals(snap.inventoryType, inventoryName, StringComparison.OrdinalIgnoreCase);
                            bool sameSlot = snap.slotIndex == i;

                            if (sameItem && sameQty && sameInv && sameSlot) continue;
                        }

                        var dto = new UpdateDto
                        {
                            Id = serverId,
                            UserId = userId,
                            ItemId = s.itemData.id,
                            Quantity = s.count,
                            InventoryType = inventoryName,
                            SlotIndex = i
                        };

                        var put = await PutUpdateInventory(dto);
                        if (!put.ok)
                        {
                            if (IsSlotOccupied(put))
                            {
                                var map2 = await FetchServerRecordMap(userId) ?? new Dictionary<string, string>();
                                if (map2.TryGetValue(key, out var existedId))
                                {
                                    if (existedId == serverId)
                                    {
                                        var recreated = await RecreateSameSlotRecord(userId, inventoryName, i, s.itemData.id, s.count, serverId, key, null);
                                        anyChanged |= recreated;
                                        if (recreated) continue;
                                    }
                                    else
                                    {
                                        await DeleteRecordById(existedId);
                                        var put2 = await PutUpdateInventory(dto);
                                        anyChanged |= put2.ok;
                                        if (put2.ok) recordIdBySlot[key] = dto.Id;
                                        continue;
                                    }
                                }
                            }

                            if (IsNotExistError(put))
                            {
                                if (serverItemMap.TryGetValue(s.itemData.id, out var info))
                                {
                                    var dto2 = new UpdateDto
                                    {
                                        Id = info.id,
                                        UserId = userId,
                                        ItemId = s.itemData.id,
                                        Quantity = s.count,
                                        InventoryType = inventoryName,
                                        SlotIndex = i
                                    };
                                    var put2 = await PutUpdateInventory(dto2);
                                    if (put2.ok)
                                    {
                                        anyChanged = true;
                                        recordIdBySlot[key] = dto2.Id;
                                        continue;
                                    }
                                }

                                var createdId = await PostCreateInventory(userId, s.itemData.id, s.count, inventoryName, i);
                                if (!string.IsNullOrEmpty(createdId))
                                {
                                    anyChanged = true;
                                    recordIdBySlot[key] = createdId;
                                    continue;
                                }
                            }

                            var map3 = await FetchServerRecordMap(userId) ?? new Dictionary<string, string>();
                            if (map3.TryGetValue(key, out var latestId) && latestId != serverId)
                            {
                                var dto3 = new UpdateDto
                                {
                                    Id = latestId,
                                    UserId = userId,
                                    ItemId = s.itemData.id,
                                    Quantity = s.count,
                                    InventoryType = inventoryName,
                                    SlotIndex = i
                                };
                                var put3 = await PutUpdateInventory(dto3);
                                anyChanged |= put3.ok;
                                if (put3.ok) recordIdBySlot[key] = latestId;
                            }
                        }
                        else
                        {
                            anyChanged = true;
                            recordIdBySlot[key] = dto.Id;
                        }

                        continue;
                    }

                    // Chưa có record ở slot → thử MOVE theo itemId, nếu không thì CREATE
                    if (serverItemMap.TryGetValue(s.itemData.id, out var srvInfo))
                    {
                        var dtoMove = new UpdateDto
                        {
                            Id = srvInfo.id,
                            UserId = userId,
                            ItemId = s.itemData.id,
                            Quantity = s.count,
                            InventoryType = inventoryName,
                            SlotIndex = i
                        };
                        var putMove = await PutUpdateInventory(dtoMove);
                        if (putMove.ok)
                        {
                            anyChanged = true;
                            recordIdBySlot[key] = srvInfo.id;

                            if (pendingMoveRidByDestKey.TryGetValue(key, out var oldRid) && !string.IsNullOrEmpty(oldRid) && oldRid != srvInfo.id)
                            {
                                var fresh = await FetchServerRecordMap(userId) ?? new Dictionary<string, string>();
                                if (fresh.Values.Contains(oldRid)) await DeleteRecordById(oldRid);
                                RemovePendingByRidLocal(oldRid);
                            }
                            pendingMoveRidByDestKey.Remove(key);
                            continue;
                        }

                        if (IsSlotOccupied(putMove))
                        {
                            var map2 = await FetchServerRecordMap(userId) ?? new Dictionary<string, string>();
                            if (map2.TryGetValue(key, out var existedId))
                            {
                                await DeleteRecordById(existedId);
                                var put2 = await PutUpdateInventory(dtoMove);
                                if (put2.ok)
                                {
                                    anyChanged = true;
                                    recordIdBySlot[key] = dtoMove.Id;
                                    continue;
                                }
                            }
                        }

                        if (IsNotExistError(putMove))
                        {
                            var latest = await FetchServerItemMapByItemId_All(userId) ?? new Dictionary<string, ServerItemInfo>();
                            if (latest.TryGetValue(s.itemData.id, out var info2))
                            {
                                var dto2 = new UpdateDto
                                {
                                    Id = info2.id,
                                    UserId = userId,
                                    ItemId = s.itemData.id,
                                    Quantity = s.count,
                                    InventoryType = inventoryName,
                                    SlotIndex = i
                                };
                                var put2 = await PutUpdateInventory(dto2);
                                if (put2.ok)
                                {
                                    anyChanged = true;
                                    recordIdBySlot[key] = dto2.Id;
                                    continue;
                                }
                            }
                        }
                    }

                    if (!allowCreateIfMissing)
                    {
                        if (pendingMoveRidByDestKey.TryGetValue(key, out var oldRid) && !string.IsNullOrEmpty(oldRid))
                        {
                            var serverMap2 = await FetchServerRecordMap(userId) ?? new Dictionary<string, string>();
                            if (serverMap2.TryGetValue(key, out var existed) && existed != oldRid)
                                await DeleteRecordById(existed);

                            var newId = await PostCreateInventory(userId, s.itemData.id, s.count, inventoryName, i);
                            if (!string.IsNullOrEmpty(newId))
                            {
                                anyChanged = true;

                                var fresh2 = await FetchServerRecordMap(userId) ?? new Dictionary<string, string>();
                                if (fresh2.Values.Contains(oldRid)) await DeleteRecordById(oldRid);

                                recordIdBySlot[key] = newId;
                                RemovePendingByRidLocal(oldRid);
                            }
                            pendingMoveRidByDestKey.Remove(key);
                        }
                    }
                    else
                    {
                        if (!serverItemMap.ContainsKey(s.itemData.id))
                        {
                            var createdId = await PostCreateInventory(userId, s.itemData.id, s.count, inventoryName, i);
                            if (!string.IsNullOrEmpty(createdId))
                            {
                                anyChanged = true;
                                recordIdBySlot[key] = createdId;
                            }
                        }
                    }
                }

                // ===== PASS 2: DELETE sweep (for-loop an toàn) =====
                var freshMap2 = await FetchServerRecordMap(userId) ?? new Dictionary<string, string>();
                for (int i = 0; i < inv.slots.Count; i++)
                {
                    var s = inv.slots[i];
                    bool isEmpty = (s == null || s.count <= 0);
                    if (!isEmpty) continue;

                    string key = SlotKey(inventoryName, i);
                    if (freshMap2.TryGetValue(key, out var rid) && !string.IsNullOrEmpty(rid))
                    {
                        var okDel = await DeleteRecordById(rid);
                        anyChanged |= okDel;

                        recordIdBySlot.Remove(key);
                        pendingMoveRidByDestKey.Remove(key);
                        RemovePendingByRidLocal(rid);

                        if (serverSnapshot.TryGetValue(rid, out var snap) && !string.IsNullOrEmpty(snap.itemId))
                            serverItemMap.Remove(snap.itemId);
                    }
                }

                if (anyChanged && reloadAfterSync) await LoadInventory(userId, applyToLocal: true);
                if (reloadAfterSync) pendingMoveRidByDestKey.Clear();
                if (reloadAfterSync) await NormalizeDuplicateRecords(inventoryName);

                Debug.Log($"[Inventory] SyncInventory done inv={inventoryName} anyChanged={anyChanged}");
                return anyChanged;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Inventory] SyncInventory fatal: {ex}");
                return false;
            }
        }

        private async Task AddItemToServer(string userId, Item item, int quantity)
        {
            if (!EnsureAuthReady(out var ensuredUser)) return;
            userId = ensuredUser;

            var data = item?.Data;
            if (data == null || string.IsNullOrEmpty(data.id)) { return; }

            int slotIndex = GetBestSlotIndexForAdd(backpack, item);
            if (slotIndex < 0) { return; }

            var newId = await PostCreateInventory(userId, data.id, quantity, BACKPACK, slotIndex);
            if (!string.IsNullOrEmpty(newId)) await LoadInventory(userId);
        }

        private Dictionary<string, InventoryItem> BuildServerSnapshot(List<InventoryItem> list)
        {
            var dict = new Dictionary<string, InventoryItem>();
            if (list == null) return dict;

            foreach (var it in list)
            {
                if (it != null && !string.IsNullOrEmpty(it.id))
                    dict[it.id] = it;
            }
            return dict;
        }

        // ======================= Update Quality =======================
        public async Task<bool> UpdateItemQuality(string inventoryName, int slotIndex, int newQuality, bool reloadAfterSync = true)
        {
            if (!EnsureAuthReady(out var userId)) return false;
            if (!inventoryByName.TryGetValue(inventoryName, out var inv)) return false;
            if (slotIndex < 0 || slotIndex >= inv.slots.Count) return false;

            string key = SlotKey(inventoryName, slotIndex);
            if (!recordIdBySlot.TryGetValue(key, out var recordId) || string.IsNullOrEmpty(recordId))
            {
                var map = await FetchServerRecordMap(userId);
                map.TryGetValue(key, out recordId);
                if (string.IsNullOrEmpty(recordId)) return false;
                recordIdBySlot[key] = recordId;
            }

            var serverList = await FetchInventoryData(userId);
            var snap = serverList.FirstOrDefault(x => x.id == recordId);
            if (snap == null) return false;

            var dto = new UpdateDto
            {
                Id = snap.id,
                UserId = snap.userId,
                ItemId = snap.itemId,
                Quantity = snap.quantity,
                InventoryType = inventoryName,
                SlotIndex = slotIndex
            };

            var put = await PutUpdateInventory(dto, newQuality);
            if (!put.ok)
            {
                if (IsSlotOccupied(put))
                {
                    var map = await FetchServerRecordMap(userId);
                    if (map.TryGetValue(key, out var existedId))
                    {
                        if (existedId == dto.Id)
                        {
                            var recreated = await RecreateSameSlotRecord(userId, inventoryName, slotIndex, dto.ItemId, dto.Quantity, dto.Id, key, newQuality);
                            if (!recreated) return false;
                        }
                        else
                        {
                            await DeleteRecordById(existedId);
                            var put2 = await PutUpdateInventory(dto, newQuality);
                            if (!put2.ok) return false;
                        }
                    }
                }
                else if (IsNotExistError(put))
                {
                    var itemMap = await FetchServerItemMapByItemId_All(userId);
                    if (itemMap.TryGetValue(dto.ItemId, out var info))
                    {
                        dto.Id = info.id;
                        var put2 = await PutUpdateInventory(dto, newQuality);
                        if (!put2.ok) return false;
                    }
                    else
                    {
                        var createdId = await PostCreateInventory(userId, dto.ItemId, dto.Quantity, inventoryName, slotIndex, newQuality);
                        if (string.IsNullOrEmpty(createdId)) return false;
                        recordIdBySlot[key] = createdId;
                    }
                }
                else
                {
                    return false;
                }
            }

            if (reloadAfterSync) await LoadInventory(userId, applyToLocal: true);
            return true;
        }

        // ======================= PUBLIC WRAPPER =======================
        public async System.Threading.Tasks.Task LoadInventoryPublic(string userId, bool applyToLocal = true)
        {
            await LoadInventory(userId, applyToLocal);
        }

        // ======================= HELPERS: Query by itemId =======================
        public int GetQuantityByItemId(string itemId)
        {
            if (!isInitialized || string.IsNullOrEmpty(itemId)) return 0;

            int total = 0;
            if (inventoryItems != null)
            {
                foreach (var it in inventoryItems)
                {
                    if (it != null && string.Equals(it.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                        total += Mathf.Max(0, it.quantity);
                }
            }
            return total;
        }
        // === Sanity/diagnostics ===
        private bool ValidateInventories(string inventoryName, out Inv inv)
        {
            inv = null;

            if (string.IsNullOrEmpty(inventoryName))
            {
                Debug.LogError("[Inventory] inventoryName is null/empty");
                return false;
            }

            if (!inventoryByName.TryGetValue(inventoryName, out inv) || inv == null)
            {
                Debug.LogError($"[Inventory] '{inventoryName}' not found in inventoryByName");
                return false;
            }

            if (inv.slots == null)
            {
                Debug.LogError($"[Inventory] '{inventoryName}'.slots is NULL -> recreate list");
                inv.slots = new List<Slot>();
            }

            // đảm bảo list có đủ số lượng phần tử Slot non-null
            int need = (inventoryName.Equals(TOOLBAR, StringComparison.OrdinalIgnoreCase) ? toolbarSlotsCount : backpackSlotsCount);
            if (inv.slots.Count < need)
            {
                Debug.LogWarning($"[Inventory] '{inventoryName}'.slots.Count={inv.slots.Count} < {need} -> padding");
                while (inv.slots.Count < need) inv.slots.Add(new Slot());
            }

            for (int i = 0; i < inv.slots.Count; i++)
            {
                if (inv.slots[i] == null)
                {
                    Debug.LogWarning($"[Inventory] '{inventoryName}'.slots[{i}] is NULL -> new Slot()");
                    inv.slots[i] = new Slot();
                }
            }
            return true;
        }

        public void DebugDumpServerInventory()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Inventory] Server snapshot:");
            if (inventoryItems == null || inventoryItems.Count == 0) sb.AppendLine("  (empty)");
            else foreach (var it in inventoryItems)
                    sb.AppendLine($"  - id={it?.id} itemId={it?.itemId} type={it?.itemType} inv={it?.inventoryType} slot={it?.slotIndex} qty={it?.quantity}");
            Debug.Log(sb.ToString());
        }
        // ===== Rewards / External add by itemId =====
        public async System.Threading.Tasks.Task<bool> AddItemByServerId(string itemId, int qty, string inventoryName = BACKPACK, int preferredSlot = -1)
        {
            if (string.IsNullOrEmpty(itemId) || qty <= 0) return false;
            if (!EnsureAuthReady(out var userId)) return false;
            if (!ValidateInventories(inventoryName, out var inv)) return false;

            // 1) Thử stack vào slot đang có cùng itemId
            int sameSlot = -1;
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                if (!s.IsEmpty && s.itemData != null && s.itemData.id == itemId)
                {
                    sameSlot = i;
                    break;
                }
            }

            if (sameSlot >= 0)
            {
                // Cập nhật local + sync lên server (PUT)
                inv.slots[sameSlot].count += qty;
                await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: true);
                return true;
            }

            // 2) Không có slot cùng item → chọn slot ưu tiên hoặc slot trống đầu tiên
            int targetSlot = preferredSlot;
            if (targetSlot < 0 || targetSlot >= inv.slots.Count)
            {
                targetSlot = -1;
                for (int i = 0; i < inv.slots.Count; i++)
                    if (inv.slots[i] == null || inv.slots[i].IsEmpty) { targetSlot = i; break; }
                if (targetSlot < 0) { Debug.LogWarning("[Inventory] Hết chỗ trống để cộng thưởng"); return false; }
            }

            // 3) Gọi API tạo record mới tại slot target
            var newId = await PostCreateInventory(userId, itemId, qty, inventoryName, targetSlot);
            if (!string.IsNullOrEmpty(newId))
            {
                // Kéo lại dữ liệu để UI cập nhật
                await LoadInventory(userId, applyToLocal: true);
                return true;
            }

            // Fallback: nếu tạo thất bại, thử sync để đồng bộ trạng thái hiện tại
            await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: true);
            return false;
        }
    }
}

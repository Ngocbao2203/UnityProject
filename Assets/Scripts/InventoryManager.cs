using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

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

        // NEW: quality của item (ví dụ: 0..100, tuỳ BE)
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
    private readonly Dictionary<string, Inventory> inventoryByName = new();

    public const string BACKPACK = "Backpack";
    public const string TOOLBAR = "Toolbar";

    [Header("Backpack")] public Inventory backpack;
    public int backpackSlotsCount = 27;

    [Header("Toolbar")] public Inventory toolbar;
    // Đổi thành 9 nếu UI thực tế có 9 ô.
    public int toolbarSlotsCount = 7;

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
    private Inventory.Slot _dragSnapshot;
    private string _dragFromInv;
    private int _dragFromSlot = -1;

    // ======================= LIFECYCLE =======================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        InitializeEmptyInventories();

        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.OnUserInfoReceived += OnUserDataReceived;
            if (AuthManager.Instance.IsUserDataReady)
                _ = LoadInventory(AuthManager.Instance.GetCurrentUserId());
        }
        else { StartCoroutine(WaitForAuthManager()); }
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

    // ======================= INIT =======================
    private void InitializeEmptyInventories()
    {
        backpack = new Inventory(backpackSlotsCount);
        toolbar = new Inventory(toolbarSlotsCount);

        inventoryByName.Clear();
        inventoryByName.Add(BACKPACK, backpack);
        inventoryByName.Add(TOOLBAR, toolbar);

        backpack.slots = new List<Inventory.Slot>();
        toolbar.slots = new List<Inventory.Slot>();

        for (int i = 0; i < backpackSlotsCount; i++) backpack.slots.Add(new Inventory.Slot());
        for (int i = 0; i < toolbarSlotsCount; i++) toolbar.slots.Add(new Inventory.Slot());

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
                _dragSnapshot = new Inventory.Slot
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
    // PATCH: gentle-logging cho lỗi đã có nhánh xử lý (đã bỏ log)
    private static async Task<HttpResult> SendAsync(UnityWebRequest req, string tag)
    {
        var t0 = Time.realtimeSinceStartup;
        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        var ms = (Time.realtimeSinceStartup - t0) * 1000f;
        var body = req.downloadHandler != null ? (req.downloadHandler.text ?? "") : "";
        var err = req.error ?? "";
        bool ok = req.result == UnityWebRequest.Result.Success;

        // đánh dấu các lỗi đã có nhánh xử lý (không in log)
        string lb = (body ?? string.Empty).ToLowerInvariant();
        bool handledConflict =
            (req.responseCode == 400 && (
                lb.Contains("kho đồ đã tồn tại") ||
                lb.Contains("vị trí đã tồn tại") ||
                lb.Contains("đã tồn tại vật phẩm") ||
                lb.Contains("slot already") ||
                lb.Contains("position already")))
            || req.responseCode == 404;

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
    // NEW: Fetch BE data without applying to UI
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

    // Overload: choose to apply or not
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

    private Inventory GetInventoryByTypeString(string type)
        => string.Equals(type, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? toolbar : backpack;

    private static int FindFirstEmpty(Inventory inv)
    {
        for (int i = 0; i < inv.slots.Count; i++) if (inv.slots[i].IsEmpty) return i;
        return -1;
    }

    private void ClearAllSlots()
    {
        for (int i = 0; i < backpack.slots.Count; i++) backpack.slots[i] = new Inventory.Slot();
        for (int i = 0; i < toolbar.slots.Count; i++) toolbar.slots[i] = new Inventory.Slot();
        recordIdBySlot.Clear();
        pendingMoveRidByDestKey.Clear();
    }

    private void ApplyServerInventoryToLocal()
    {
        if (isDragging) return;

        ClearAllSlots();
        if (inventoryItems == null || inventoryItems.Count == 0) return;

        // dùng ItemManager an toàn
        var im = GameManager.instance ? GameManager.instance.itemManager
                                      : FindFirstObjectByType<ItemManager>();

        foreach (var it in inventoryItems)
        {
            // BỎ QTY 0: coi như slot trống, không render/không map recordId
            if (it == null || it.quantity <= 0) continue;

            // Chọn inventory
            string invName = string.Equals(it.inventoryType, TOOLBAR, StringComparison.OrdinalIgnoreCase)
                ? TOOLBAR : BACKPACK;
            var inv = GetInventoryByTypeString(invName) ?? backpack;

            int target = it.slotIndex;
            if (target < 0 || target >= inv.slots.Count) continue;

            // Tạo slot với đúng số lượng từ server
            var slot = new Inventory.Slot { count = it.quantity };

            // Tìm ItemData theo itemId trước, fallback theo tên
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
                // vẫn cho hiển thị placeholder nếu thiếu data cục bộ
                slot.itemName = !string.IsNullOrEmpty(it.itemType) ? it.itemType : "(Unknown)";
                slot.icon = null;
                slot.itemData = null;
            }

            inv.slots[target] = slot;

            // Lưu map recordId -> slot để sync về sau
            if (!string.IsNullOrEmpty(it.id))
                recordIdBySlot[SlotKey(invName, target)] = it.id;
        }
    }

    public Inventory GetInventoryByName(string name)
        => inventoryByName.TryGetValue(name, out var inv) ? inv : null;

    // ======================= CRUD (PUBLIC) =======================

    // PATCH: Ưu tiên slot server đang giữ + so sánh theo itemId
    private int GetServerSlotIndexForItem(string inventoryName, string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return -1;
        var rec = inventoryItems.FirstOrDefault(i =>
            i.itemId == itemId &&
            string.Equals(i.inventoryType, inventoryName, StringComparison.OrdinalIgnoreCase));
        return rec != null ? rec.slotIndex : -1;
    }

    // PATCH: so sánh theo itemId thay vì itemName
    private int GetBestSlotIndexForAdd(Inventory inv, Item item)
    {
        var id = item?.Data?.id;
        if (string.IsNullOrEmpty(id)) return -1;

        // 1) stack vào slot có cùng itemId trước
        for (int i = 0; i < inv.slots.Count; i++)
        {
            var s = inv.slots[i];
            if (!s.IsEmpty && s.itemData != null && s.itemData.id == id && s.count < s.maxAllowed)
                return i;
        }
        // 2) nếu không có, tìm ô trống
        for (int i = 0; i < inv.slots.Count; i++)
            if (inv.slots[i].IsEmpty) return i;

        return -1;
    }

    // PATCH: AddItem ưu tiên stack đúng slot server đang giữ; cho phép createIfMissing chỉ khi server chưa có record
    public async void AddItem(string inventoryName, Item item)
    {
        if (!isInitialized) return;

        if (isDragging || isSyncing) { pendingOperations.Enqueue(() => AddItem(inventoryName, item)); return; }
        if (!EnsureAuthReady(out var userId)) return;
        if (!inventoryByName.TryGetValue(inventoryName, out var inventory)) return;

        var itemId = item?.Data?.id;
        if (string.IsNullOrEmpty(itemId)) return;

        // Ưu tiên slot server (nếu đã có record)
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
                inventory.slots[idx] = new Inventory.Slot
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

        // Dùng FetchInventoryData để không reset UI
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
            // Sau khi gộp, load + apply
            await LoadInventory(userId, applyToLocal: true);
        }
        return changed;
    }

    public async Task<bool> MoveItem(string fromInventory, int fromSlot, string toInventory, int toSlot)
    {
        if (!isInitialized) { return false; }
        if (isDragging) { return false; }
        if (isSyncing) { return false; }

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

        // Nếu UI đã clear slot nguồn khi drag, dùng snapshot
        if (fromSlotData.IsEmpty && _dragSnapshot != null &&
            string.Equals(_dragFromInv, fromInventory, StringComparison.OrdinalIgnoreCase) &&
            _dragFromSlot == fromSlot)
        {
            fromSlotData = new Inventory.Slot
            {
                itemName = _dragSnapshot.itemName,
                count = _dragSnapshot.count,
                icon = _dragSnapshot.icon,
                itemData = _dragSnapshot.itemData
            };
            fromInv.slots[fromSlot] = fromSlotData;
        }

        // Fallback: dựng lại từ server theo ridFrom (KHÔNG reset UI)
        if (fromSlotData.IsEmpty)
        {
            if (!string.IsNullOrEmpty(ridFrom) && EnsureAuthReady(out var userId))
            {
                var list = await FetchInventoryData(userId); // <-- không apply
                var srv = list.FirstOrDefault(x => x.id == ridFrom);
                if (srv != null && !string.IsNullOrEmpty(srv.itemId))
                {
                    var data = GameManager.instance.itemManager.GetItemDataByServerId(srv.itemId);
                    if (data != null)
                    {
                        fromSlotData = new Inventory.Slot
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
            // --- MOVE ---
            if (toSlotData.IsEmpty)
            {
                toInv.slots[toSlot] = new Inventory.Slot
                {
                    itemName = fromSlotData.itemName,
                    count = fromSlotData.count,
                    icon = fromSlotData.icon,
                    itemData = fromSlotData.itemData
                };
                fromInv.slots[fromSlot] = new Inventory.Slot();

                if (!string.IsNullOrEmpty(ridFrom))
                    recordIdBySlot[SlotKey(toInventory, toSlot)] = ridFrom;
                recordIdBySlot.Remove(kFrom);

                if (!string.IsNullOrEmpty(ridFrom))
                    pendingMoveRidByDestKey[SlotKey(toInventory, toSlot)] = ridFrom;
            }
            // --- STACK (PATCH: so sánh theo itemId) ---
            else if (toSlotData.itemData != null && fromSlotData.itemData != null &&
                     toSlotData.itemData.id == fromSlotData.itemData.id)
            {
                toInv.slots[toSlot].count += fromSlotData.count;
                fromInv.slots[fromSlot] = new Inventory.Slot();

                if (!string.IsNullOrEmpty(ridFrom))
                {
                    await DeleteRecordById(ridFrom);
                    recordIdBySlot.Remove(kFrom);
                }
            }
            // --- SWAP ---
            else
            {
                var temp = new Inventory.Slot
                {
                    itemName = toSlotData.itemName,
                    count = toSlotData.count,
                    icon = toSlotData.icon,
                    itemData = toSlotData.itemData
                };

                toInv.slots[toSlot] = new Inventory.Slot
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
        catch
        {
            return false;
        }
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

        if (!inventoryByName.TryGetValue(inventoryName, out var inv)) return;
        if (slotIndex < 0 || slotIndex >= inv.slots.Count) return;

        inv.Remove(slotIndex, amount);
        if (inv.slots[slotIndex].count <= 0) await DeleteItem(inventoryName, slotIndex);
        else await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: true);
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

    // NEW: cho phép truyền kèm quality (optional)
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

    // PUT bằng FORM (BE expect FromForm) + optional quality
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
        // (tuỳ cần) có thể thêm quality nếu BE trả về
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

    // ======================= NEW: SELF-OCCUPIED RECREATE (dùng cho UpdateQuality khi PUT báo occupied) =======================
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

    // ======================= SYNC CORE (ƯU TIÊN itemId) =======================
    public async Task<bool> SyncInventory(string inventoryName, bool reloadAfterSync = true, bool allowCreateIfMissing = true, bool ignoreDebounce = false)
    {
        if (!EnsureAuthReady(out var userId))
        {
            return false;
        }
        if (!inventoryByName.TryGetValue(inventoryName, out var inv))
        {
            return false;
        }

        // Debounce
        if (!ignoreDebounce)
        {
            if (!_lastSyncAtByInv.TryGetValue(inventoryName, out var last))
                last = -999f;
            if (Time.unscaledTime - last < 0.1f)
            {
                return false;
            }
        }
        _lastSyncAtByInv[inventoryName] = Time.unscaledTime;

        var nonEmpty = inv.slots.Select((s, i) => new { s, i })
                                .Where(x => !x.s.IsEmpty && x.s.itemData != null && !string.IsNullOrEmpty(x.s.itemData.id))
                                .ToList();

        // *** Fetch, KHÔNG apply vào UI ***
        var serverList = await FetchInventoryData(userId);
        var serverSnapshot = BuildServerSnapshot(serverList);
        var serverMap = await FetchServerRecordMap(userId);
        var serverItemMap = await FetchServerItemMapByItemId_All(userId);

        bool anyChanged = false;

        // helper: gỡ mọi pending có value == rid (tránh delete lần 2)
        void RemovePendingByRidLocal(string rid)
        {
            var keys = pendingMoveRidByDestKey.Where(kv => kv.Value == rid).Select(kv => kv.Key).ToList();
            foreach (var k in keys) pendingMoveRidByDestKey.Remove(k);
        }

        // ===== PASS 1: PUT / MOVE / CREATE cho các slot còn item =====
        foreach (var x in nonEmpty)
        {
            string key = SlotKey(inventoryName, x.i);
            serverMap.TryGetValue(key, out var serverId);

            if (!string.IsNullOrEmpty(serverId))
            {
                if (serverSnapshot.TryGetValue(serverId, out var snap))
                {
                    bool sameItem = snap.itemId == x.s.itemData.id;
                    bool sameQty = snap.quantity == x.s.count;
                    bool sameInv = string.Equals(snap.inventoryType, inventoryName, StringComparison.OrdinalIgnoreCase);
                    bool sameSlot = snap.slotIndex == x.i;

                    if (sameItem && sameQty && sameInv && sameSlot)
                    {
                        continue;
                    }
                }

                var dto = new UpdateDto
                {
                    Id = serverId,
                    UserId = userId,
                    ItemId = x.s.itemData.id,
                    Quantity = x.s.count,
                    InventoryType = inventoryName,
                    SlotIndex = x.i
                };

                var put = await PutUpdateInventory(dto);
                if (!put.ok)
                {
                    if (IsSlotOccupied(put))
                    {
                        var map2 = await FetchServerRecordMap(userId);

                        if (map2.TryGetValue(key, out var existedId))
                        {
                            if (existedId == serverId)
                            {
                                var recreated = await RecreateSameSlotRecord(
                                    userId, inventoryName, x.i, x.s.itemData.id, x.s.count, serverId, key, null
                                );
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
                        if (serverItemMap.TryGetValue(x.s.itemData.id, out var info))
                        {
                            dto.Id = info.id;
                            var put2 = await PutUpdateInventory(dto);
                            if (put2.ok)
                            {
                                anyChanged = true;
                                recordIdBySlot[key] = dto.Id;
                                continue;
                            }
                        }

                        var createdId = await PostCreateInventory(userId, x.s.itemData.id, x.s.count, inventoryName, x.i);
                        if (!string.IsNullOrEmpty(createdId))
                        {
                            anyChanged = true;
                            recordIdBySlot[key] = createdId;
                            continue;
                        }
                    }

                    var map3 = await FetchServerRecordMap(userId);
                    if (map3.TryGetValue(key, out var latestId) && latestId != serverId)
                    {
                        dto.Id = latestId;
                        var put2 = await PutUpdateInventory(dto);
                        anyChanged |= put2.ok;
                        if (put2.ok) recordIdBySlot[key] = latestId;
                    }
                }
                else
                {
                    anyChanged = true;
                    recordIdBySlot[key] = dto.Id;
                }
                continue;
            }

            // ----- Chưa có record ở slot đích → move hoặc create -----
            if (serverItemMap.TryGetValue(x.s.itemData.id, out var srvInfo))
            {
                var dtoMove = new UpdateDto
                {
                    Id = srvInfo.id,
                    UserId = userId,
                    ItemId = x.s.itemData.id,
                    Quantity = x.s.count,
                    InventoryType = inventoryName,
                    SlotIndex = x.i
                };

                var putMove = await PutUpdateInventory(dtoMove);
                if (putMove.ok)
                {
                    anyChanged = true;
                    recordIdBySlot[key] = srvInfo.id;

                    // CLEANUP pending oldRid an toàn: chỉ xóa nếu còn tồn tại trên server
                    if (pendingMoveRidByDestKey.TryGetValue(key, out var oldRid) && !string.IsNullOrEmpty(oldRid) && oldRid != srvInfo.id)
                    {
                        var fresh = await FetchServerRecordMap(userId);
                        if (fresh.Values.Contains(oldRid))
                        {
                            await DeleteRecordById(oldRid);
                        }
                        RemovePendingByRidLocal(oldRid);
                    }
                    pendingMoveRidByDestKey.Remove(key);
                    continue;
                }

                if (IsSlotOccupied(putMove))
                {
                    var map2 = await FetchServerRecordMap(userId);
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
                    var latest = await FetchServerItemMapByItemId_All(userId);
                    if (latest.TryGetValue(x.s.itemData.id, out var info2))
                    {
                        dtoMove.Id = info2.id;
                        var put2 = await PutUpdateInventory(dtoMove);
                        if (put2.ok)
                        {
                            anyChanged = true;
                            recordIdBySlot[key] = dtoMove.Id;
                            continue;
                        }
                    }
                }
            }

            if (!allowCreateIfMissing)
            {
                if (pendingMoveRidByDestKey.TryGetValue(key, out var oldRid) && !string.IsNullOrEmpty(oldRid))
                {
                    var serverMap2 = await FetchServerRecordMap(userId);
                    if (serverMap2.TryGetValue(key, out var existed) && existed != oldRid)
                    {
                        await DeleteRecordById(existed);
                    }

                    var newId = await PostCreateInventory(userId, x.s.itemData.id, x.s.count, inventoryName, x.i);
                    if (!string.IsNullOrEmpty(newId))
                    {
                        anyChanged = true;

                        // Chỉ DELETE oldRid nếu nó còn tồn tại ở server
                        var fresh2 = await FetchServerRecordMap(userId);
                        if (fresh2.Values.Contains(oldRid))
                        {
                            await DeleteRecordById(oldRid);
                        }

                        recordIdBySlot[key] = newId;
                        RemovePendingByRidLocal(oldRid);
                    }
                    pendingMoveRidByDestKey.Remove(key);
                }
            }
            else
            {
                if (!serverItemMap.ContainsKey(x.s.itemData.id))
                {
                    var createdId = await PostCreateInventory(userId, x.s.itemData.id, x.s.count, inventoryName, x.i);
                    if (!string.IsNullOrEmpty(createdId))
                    {
                        anyChanged = true;
                        recordIdBySlot[key] = createdId;
                    }
                }
            }
        }

        // ===== PASS 2: DELETE sweep – xoá những slot rỗng ở client nhưng còn record trên server =====
        var emptySlots = inv.slots.Select((s, i) => new { s, i })
                                  .Where(e => e.s.IsEmpty || e.s.count <= 0 || e.s.itemData == null || string.IsNullOrEmpty(e.s.itemData?.id))
                                  .ToList();

        if (emptySlots.Count > 0)
        {
            var freshMap = await FetchServerRecordMap(userId);
            foreach (var e in emptySlots)
            {
                string key = SlotKey(inventoryName, e.i);
                if (freshMap.TryGetValue(key, out var rid) && !string.IsNullOrEmpty(rid))
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
        }

        if (anyChanged && reloadAfterSync)
        {
            await LoadInventory(userId, applyToLocal: true);
        }
        if (reloadAfterSync) pendingMoveRidByDestKey.Clear();

        if (reloadAfterSync)
        {
            await NormalizeDuplicateRecords(inventoryName);
        }

        return anyChanged;
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

    // ======================= NEW: PUBLIC API Update Quality =======================
    /// <summary>
    /// Cập nhật Quality cho item ở (inventoryName, slotIndex).
    /// </summary>
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
            if (string.IsNullOrEmpty(recordId))
            {
                return false;
            }
            recordIdBySlot[key] = recordId;
        }

        // Lấy snapshot server của record này
        var serverList = await FetchInventoryData(userId);
        var snap = serverList.FirstOrDefault(x => x.id == recordId);
        if (snap == null)
        {
            return false;
        }

        var dto = new UpdateDto
        {
            Id = snap.id,
            UserId = snap.userId,
            ItemId = snap.itemId,
            Quantity = snap.quantity,                // giữ nguyên
            InventoryType = inventoryName,           // giữ nguyên
            SlotIndex = slotIndex                    // giữ nguyên
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
                        // self-occupied → recreate
                        var recreated = await RecreateSameSlotRecord(userId, inventoryName, slotIndex, dto.ItemId, dto.Quantity, dto.Id, key, newQuality);
                        if (!recreated) return false;
                    }
                    else
                    {
                        // occupied bởi record khác → xóa rồi PUT lại
                        await DeleteRecordById(existedId);
                        var put2 = await PutUpdateInventory(dto, newQuality);
                        if (!put2.ok) return false;
                    }
                }
            }
            else if (IsNotExistError(put))
            {
                // record biến mất → thử remap theo itemId hoặc POST tạo lại (kèm Quality)
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

        if (reloadAfterSync)
        {
            await LoadInventory(userId, applyToLocal: true);
        }

        return true;
    }

    // ======================= STARTER PACK (ONE-TIME GRANT) =======================
    // Gọi từ GameManager sau khi có userId + tải inventory lần đầu.
    // StarterPackConfig là ScriptableObject của bạn (định nghĩa ở file riêng).
    public async System.Threading.Tasks.Task EnsureStarterPackOnFirstLogin(StarterPackConfig cfg, bool reloadAfter = true)
    {
        if (cfg == null)
        {
            //Debug.LogWarning("[StarterPack] no config");
            return;
        }

        if (!EnsureAuthReady(out var userId))
        {
            //Debug.LogWarning("[StarterPack] abort: auth not ready");
            return;
        }

        // Local fallback key để đánh dấu đã cấp trên thiết bị này
        string localKey = $"starterpack:{userId}";

        // 1) Đọc server, kiểm tra marker
        var serverList = await FetchInventoryData(userId);

        bool hasServerMarker = false;
        if (!string.IsNullOrWhiteSpace(cfg.markerItemId) && !string.IsNullOrWhiteSpace(cfg.markerInventoryType))
        {
            hasServerMarker = serverList.Any(r =>
                string.Equals(r.inventoryType, cfg.markerInventoryType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.itemId, cfg.markerItemId, StringComparison.OrdinalIgnoreCase));
        }

        bool hasLocalMarker = PlayerPrefs.GetInt(localKey, 0) == 1;

        if (hasServerMarker || hasLocalMarker)
        {
            //Debug.Log("[StarterPack] already granted → skip");
            return;
        }

        // 2) Tập slot đã dùng theo từng inventory để chọn slot trống
        var usedByInv = serverList
            .GroupBy(r => r.inventoryType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.slotIndex).ToHashSet(), StringComparer.OrdinalIgnoreCase);

        int NextFreeSlot(string invType)
        {
            if (!usedByInv.TryGetValue(invType, out var used))
            {
                used = new HashSet<int>();
                usedByInv[invType] = used;
            }
            int s = 0;
            while (used.Contains(s)) s++;
            used.Add(s);
            return s;
        }

        // 3) Cố gắng tạo marker trên server (idempotent) — KHÔNG abort nếu thất bại
        bool markerCreatedOnServer = false;
        if (!string.IsNullOrWhiteSpace(cfg.markerItemId) && !string.IsNullOrWhiteSpace(cfg.markerInventoryType))
        {
            int markerSlot = NextFreeSlot(cfg.markerInventoryType);
            var markerRid = await PostCreateInventory(userId, cfg.markerItemId, 1, cfg.markerInventoryType, markerSlot);

            if (!string.IsNullOrEmpty(markerRid))
            {
                markerCreatedOnServer = true;
            }
            else
            {
                // refetch 1 lần phòng race condition
                serverList = await FetchInventoryData(userId);
                hasServerMarker = serverList.Any(r =>
                    string.Equals(r.inventoryType, cfg.markerInventoryType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.itemId, cfg.markerItemId, StringComparison.OrdinalIgnoreCase));
                markerCreatedOnServer = hasServerMarker;

                if (!markerCreatedOnServer)
                {
                    Debug.LogWarning("[StarterPack] cannot create server marker — will grant and fallback to PlayerPrefs.");
                }
            }
        }

        // 4) Cấp quà theo config (skip món đã có trên server để tránh trùng)
        int granted = 0;
        foreach (var g in cfg.items)
        {
            if (g == null || g.item == null || string.IsNullOrEmpty(g.item.id))
            {
                //Debug.LogWarning("[StarterPack] skip invalid grant entry");
                continue;
            }

            // Nếu server đã có sẵn item này → bỏ qua
            bool alreadyHas = serverList.Exists(x => x.itemId == g.item.id);
            if (alreadyHas) continue;

            var inv = string.IsNullOrEmpty(g.inventoryType) ? "Backpack" : g.inventoryType;
            int slot;

            if (g.preferredSlot >= 0)
            {
                // nếu slot bận → chọn slot trống tiếp theo
                if (usedByInv.TryGetValue(inv, out var used) && used.Contains(g.preferredSlot))
                    slot = NextFreeSlot(inv);
                else
                {
                    slot = g.preferredSlot;
                    if (!usedByInv.TryGetValue(inv, out var set)) usedByInv[inv] = set = new HashSet<int>();
                    set.Add(slot);
                }
            }
            else
            {
                slot = NextFreeSlot(inv);
            }

            var rid = await PostCreateInventory(userId, g.item.id, Math.Max(1, g.quantity), inv, slot);
            if (!string.IsNullOrEmpty(rid)) granted++;
        }

        // 5) Đánh dấu đã cấp 1 lần
        if (granted > 0)
        {
            if (!markerCreatedOnServer)
            {
                PlayerPrefs.SetInt(localKey, 1);
                PlayerPrefs.Save();
                Debug.Log($"[StarterPack] granted {granted} items (marked by PlayerPrefs).");
            }
            else
            {
                Debug.Log($"[StarterPack] granted {granted} items (marked on server).");
            }
        }
        else
        {
            // Không có gì để cấp: nếu không có marker server thì vẫn đánh dấu local để tránh chạy lại
            if (!markerCreatedOnServer && PlayerPrefs.GetInt(localKey, 0) == 0)
            {
                PlayerPrefs.SetInt(localKey, 1);
                PlayerPrefs.Save();
            }
            Debug.Log("[StarterPack] nothing to grant.");
        }

        if (reloadAfter)
        {
            await LoadInventory(userId, applyToLocal: true);
        }
    }

    // ======================= PUBLIC WRAPPER (cho GameManager) =======================
    public async System.Threading.Tasks.Task LoadInventoryPublic(string userId, bool applyToLocal = true)
    {
        await LoadInventory(userId, applyToLocal);
    }
}

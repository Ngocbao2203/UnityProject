using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[DefaultExecutionOrder(-50)]
public class InventoryManager : MonoBehaviour
{
    public List<InventoryItem> inventoryItems = new List<InventoryItem>();
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

    private bool isInitialized = false;
    private bool isLoadingInventory = false;
    private bool isDragging = false;
    private bool isSyncing = false;
    private readonly Queue<Action> pendingOperations = new();

    public event Action OnInventoryLoaded;

    // map recordId: "<InventoryName>:<SlotIndex>" -> Inventory.Id trên BE
    private readonly Dictionary<string, string> recordIdBySlot = new();
    private static string SlotKey(string inv, int slot) => $"{inv}:{slot}";

    // -------------------- Lifecycle --------------------
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

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

    // -------------------- Helpers (Auth / JWT) --------------------
    private bool EnsureAuthReady(out string userId)
    {
        userId = AuthManager.Instance?.GetCurrentUserId();
        if (AuthManager.Instance == null || !AuthManager.Instance.IsUserDataReady || string.IsNullOrEmpty(userId))
        {
            Debug.LogError("[Inventory] Auth not ready or userId null → skip server call");
            return false;
        }
        return true;
    }

    private static string DumpJwtPayload()
    {
        try
        {
            var token = LocalStorageHelper.GetToken();
            if (string.IsNullOrEmpty(token)) return "(no token)";
            var parts = token.Split('.');
            if (parts.Length < 2) return "(invalid token)";
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
            return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        }
        catch { return "(dump failed)"; }
    }

    // -------------------- Init inventories --------------------
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

        isInitialized = true;
        Debug.Log("Empty inventories initialized");
    }

    // -------------------- Drag / pending ops --------------------
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
        {
            var op = pendingOperations.Dequeue();
            op?.Invoke();
        }
    }

    // -------------------- Auth wait --------------------
    private IEnumerator WaitForAuthManager()
    {
        float timeout = 10f, t = 0f;
        while (AuthManager.Instance == null && t < timeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.OnUserInfoReceived += OnUserDataReceived;
            if (AuthManager.Instance.IsUserDataReady)
            {
                _ = LoadInventory(AuthManager.Instance.GetCurrentUserId());
            }
            else
            {
                yield return AuthManager.Instance.StartCoroutine(AuthManager.Instance.GetCurrentUser());
                if (AuthManager.Instance.IsUserDataReady)
                    _ = LoadInventory(AuthManager.Instance.GetCurrentUserId());
            }
        }
        else
        {
            Debug.LogError("AuthManager not found after timeout! Using offline mode.");
        }
    }

    private async void OnUserDataReceived(bool success, string message, UserData userData)
    {
        if (success && userData != null)
        {
            if (!isLoadingInventory && !isDragging)
            {
                isLoadingInventory = true;
                await LoadInventory(userData.id);
                ApplyServerInventoryToLocal();
                isLoadingInventory = false;
            }
        }
        else
        {
            Debug.LogError($"Failed to receive user data: {message}");
        }
    }

    // -------------------- HTTP helpers --------------------
    private static UnityWebRequest BuildPost(string url, string json)
    {
        var req = new UnityWebRequest(url, "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
            downloadHandler = new DownloadHandlerBuffer()
        };
        req.SetRequestHeader("Content-Type", "application/json");
        string token = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(token))
            req.SetRequestHeader("Authorization", $"Bearer {token}");
        Debug.Log($"[HTTP][POST] {url}\nPayload: {json}\nJWT: {DumpJwtPayload()}");
        return req;
    }

    private static async Task<bool> SendRequestAsync(UnityWebRequest request, string tag = "HTTP")
    {
        var t0 = Time.realtimeSinceStartup;
        var op = request.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        var ms = (Time.realtimeSinceStartup - t0) * 1000f;
        long code = request.responseCode;
        string body = request.downloadHandler != null ? (request.downloadHandler.text ?? "") : "";
        string err = request.error ?? "";

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"[{tag}] OK {code} ({ms:0} ms)\nResponse: {body}");
            return true;
        }
        else
        {
            Debug.LogError($"[{tag}] FAIL {code} ({ms:0} ms)\nError: {err}\nBody: {body}");
            return false;
        }
    }

    // -------------------- Load & Apply inventory --------------------
    private async Task LoadInventory(string userId)
    {
        try
        {
            string url = ApiRoutes.Inventory.GET_BY_USERID.Replace("{userId}", userId);
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 15;
                request.SetRequestHeader("Content-Type", "application/json");
                string token = LocalStorageHelper.GetToken();
                if (!string.IsNullOrEmpty(token))
                    request.SetRequestHeader("Authorization", $"Bearer {token}");

                var op = request.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string jsonResponse = request.downloadHandler.text;
                    var response = JsonUtility.FromJson<InventoryResponse>(jsonResponse);

                    inventoryItems = response?.data != null
                        ? new List<InventoryItem>(response.data)
                        : new List<InventoryItem>();

                    if (inventoryItems.Count > 0)
                        Debug.Log($"[INV] First parsed item -> itemId='{inventoryItems[0].itemId}', qty={inventoryItems[0].quantity}");

                    ApplyServerInventoryToLocal();
                    OnInventoryLoaded?.Invoke();
                }
                else
                {
                    Debug.LogError($"Error loading inventory: {request.error} - {request.responseCode}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception in LoadInventory: {ex.Message}");
        }
    }

    private Inventory GetInventoryByTypeString(string type)
    {
        if (string.Equals(type, TOOLBAR, StringComparison.OrdinalIgnoreCase)) return toolbar;
        return backpack; // default
    }

    private static int FindFirstEmpty(Inventory inv)
    {
        for (int i = 0; i < inv.slots.Count; i++)
            if (inv.slots[i].IsEmpty) return i;
        return -1;
    }

    private void ClearAllSlots()
    {
        for (int i = 0; i < backpack.slots.Count; i++) backpack.slots[i] = new Inventory.Slot();
        for (int i = 0; i < toolbar.slots.Count; i++) toolbar.slots[i] = new Inventory.Slot();
        recordIdBySlot.Clear();
    }

    private void ApplyServerInventoryToLocal()
    {
        if (isDragging) return;
        ClearAllSlots();

        foreach (var it in inventoryItems)
        {
            var invName = string.Equals(it.inventoryType, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? TOOLBAR : BACKPACK;
            var inv = GetInventoryByTypeString(invName);

            int target = it.slotIndex;
            if (target < 0 || target >= inv.slots.Count || !inv.slots[target].IsEmpty)
            {
                int fallback = FindFirstEmpty(inv);
                target = (fallback >= 0) ? fallback : Mathf.Clamp(it.slotIndex, 0, inv.slots.Count - 1);
            }

            var slot = inv.slots[target];
            slot.count = it.quantity;

            // ưu tiên qua Item
            Item found = null;
            if (!string.IsNullOrEmpty(it.itemId))
                found = GameManager.instance.itemManager.GetItemByServerId(it.itemId);

            if (found != null && found.Data != null)
            {
                slot.itemName = found.Data.itemName;
                slot.icon = found.Data.icon;
                slot.itemData = found.Data;
            }
            else
            {
                // fallback ItemData
                ItemData data = null;
                if (!string.IsNullOrEmpty(it.itemId))
                    data = GameManager.instance.itemManager.GetItemDataByServerId(it.itemId);
                if (data == null && !string.IsNullOrEmpty(it.itemType))
                    data = GameManager.instance.itemManager.GetItemDataByName(it.itemType);

                if (data != null)
                {
                    slot.itemName = data.itemName;
                    slot.icon = data.icon;
                    slot.itemData = data;
                }
                else
                {
                    slot.itemName = !string.IsNullOrEmpty(it.itemType) ? it.itemType : "(Unknown)";
                    slot.icon = null;
                    slot.itemData = null;
                    Debug.LogWarning($"[Inventory] Cannot resolve item visuals (itemId='{it.itemId}', itemType='{it.itemType}').");
                }
            }

            inv.slots[target] = slot;

            // lưu recordId (Inventory.Id)
            if (!string.IsNullOrEmpty(it.id))
                recordIdBySlot[SlotKey(invName, target)] = it.id;
        }
    }

    public Inventory GetInventoryByName(string name)
        => inventoryByName.TryGetValue(name, out var inv) ? inv : null;

    // -------------------- Add / Move / Use / Delete --------------------
    public async void AddItem(string inventoryName, Item item)
    {
        if (!isInitialized) return;

        if (isDragging || isSyncing)
        {
            pendingOperations.Enqueue(() => AddItem(inventoryName, item));
            return;
        }

        if (!EnsureAuthReady(out var serverUserId)) return;
        if (!inventoryByName.TryGetValue(inventoryName, out var inventory)) return;

        bool localHad = HasItem(inventoryName, item);
        if (localHad) inventory.IncreaseItemQuantity(item.Data.itemName);
        else inventory.Add(item);

        bool existsOnServer = inventoryItems.Any(i => !string.IsNullOrEmpty(i.itemId) && i.itemId == item.Data.id);

        if (existsOnServer)
        {
            await SyncInventory(inventoryName);
        }
        else
        {
            await AddItemToServer(serverUserId, item, 1);
            await LoadInventory(serverUserId);
        }
    }

    public async Task<bool> MoveItem(string fromInventory, int fromSlot, string toInventory, int toSlot)
    {
        if (!isInitialized || isDragging || isSyncing) return false;

        bool fromExists = inventoryByName.TryGetValue(fromInventory, out var fromInv);
        bool toExists = inventoryByName.TryGetValue(toInventory, out var toInv);
        if (!fromExists || !toExists) return false;

        if (fromSlot < 0 || fromSlot >= fromInv.slots.Count ||
            toSlot < 0 || toSlot >= toInv.slots.Count) return false;

        var fromSlotData = fromInv.slots[fromSlot];
        var toSlotData = toInv.slots[toSlot];
        if (fromSlotData.IsEmpty) return false;

        // recordId hiện tại của 2 ô
        var kFrom = SlotKey(fromInventory, fromSlot);
        var kTo = SlotKey(toInventory, toSlot);
        recordIdBySlot.TryGetValue(kFrom, out var ridFrom);
        recordIdBySlot.TryGetValue(kTo, out var ridTo);

        isSyncing = true;
        try
        {
            if (toSlotData.IsEmpty)
            {
                // move
                toInv.slots[toSlot] = new Inventory.Slot
                {
                    itemName = fromSlotData.itemName,
                    count = fromSlotData.count,
                    icon = fromSlotData.icon,
                    itemData = fromSlotData.itemData
                };
                fromInv.slots[fromSlot] = new Inventory.Slot();

                // move recordId mapping
                if (!string.IsNullOrEmpty(ridFrom))
                    recordIdBySlot[SlotKey(toInventory, toSlot)] = ridFrom;
                recordIdBySlot.Remove(kFrom);
            }
            else if (toSlotData.itemName == fromSlotData.itemName)
            {
                // stack
                toInv.slots[toSlot].count += fromSlotData.count;
                fromInv.slots[fromSlot] = new Inventory.Slot();

                // Xoá record nguồn trên server nếu có
                if (!string.IsNullOrEmpty(ridFrom))
                {
                    await DeleteRecordById(ridFrom);
                    recordIdBySlot.Remove(kFrom);
                }
                // Ô đích giữ nguyên recordId (ridTo)
            }
            else
            {
                // swap
                var tempSlot = new Inventory.Slot
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
                fromInv.slots[fromSlot] = tempSlot;

                // swap recordId mapping
                if (!string.IsNullOrEmpty(ridFrom)) recordIdBySlot[SlotKey(toInventory, toSlot)] = ridFrom; else recordIdBySlot.Remove(SlotKey(toInventory, toSlot));
                if (!string.IsNullOrEmpty(ridTo)) recordIdBySlot[SlotKey(fromInventory, fromSlot)] = ridTo; else recordIdBySlot.Remove(SlotKey(fromInventory, fromSlot));
            }

            // Sync (reload một lần)
            if (fromInventory != toInventory)
            {
                await SyncInventory(fromInventory, false);
                await SyncInventory(toInventory, true);
            }
            else
            {
                await SyncInventory(fromInventory, true);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception in MoveItem: {ex.Message}");
            return false;
        }
        finally { isSyncing = false; }
    }

    public bool HasItem(string inventoryName, Item item)
    {
        if (!inventoryByName.ContainsKey(inventoryName)) return false;
        var inventory = inventoryByName[inventoryName];
        foreach (var slot in inventory.slots)
        {
            if (!slot.IsEmpty && slot.itemData != null &&
                slot.itemData.itemName == item.Data.itemName) return true;
        }
        return false;
    }

    public async void UseItem(string inventoryName, int slotIndex, int amount = 1)
    {
        if (!isInitialized) return;

        if (isDragging || isSyncing)
        {
            pendingOperations.Enqueue(() => UseItem(inventoryName, slotIndex, amount));
            return;
        }

        if (inventoryByName.TryGetValue(inventoryName, out var inventory))
        {
            if (slotIndex >= 0 && slotIndex < inventory.slots.Count)
            {
                inventory.Remove(slotIndex, amount);
                if (inventory.slots[slotIndex].count <= 0)
                    await DeleteItem(inventoryName, slotIndex);
                else
                    await SyncInventory(inventoryName);
            }
        }
    }

    public async Task DeleteItem(string inventoryName, int slotIndex)
    {
        if (!inventoryByName.TryGetValue(inventoryName, out var inventory)) return;
        if (slotIndex < 0 || slotIndex >= inventory.slots.Count || inventory.slots[slotIndex].IsEmpty) return;

        // Dùng recordId (không phải ItemId)
        var key = SlotKey(inventoryName, slotIndex);
        if (recordIdBySlot.TryGetValue(key, out var recordId) && !string.IsNullOrEmpty(recordId))
        {
            bool ok = await DeleteRecordById(recordId);
            if (ok)
            {
                inventory.Remove(slotIndex);
                recordIdBySlot.Remove(key);
                await SyncInventory(inventoryName);
            }
        }
        else
        {
            inventory.Remove(slotIndex);
        }
    }

    private async Task<bool> DeleteRecordById(string recordId)
    {
        string url = ApiRoutes.Inventory.DELETE_ITEM.Replace("{inventoryId}", recordId);
        using (UnityWebRequest request = UnityWebRequest.Delete(url))
        {
            string token = LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", $"Bearer {token}");
            return await SendRequestAsync(request, "DeleteRecord");
        }
    }

    // ---------- PUT/POST helpers & existence check ----------
    [Serializable]
    private class UpdateDto   // PascalCase khớp Swagger
    {
        public string Id;            // recordId (Inventory.Id)
        public string UserId;
        public string ItemId;
        public int Quantity;
        public string InventoryType;
        public int SlotIndex;
    }

    private async Task<bool> ExistsRecord(string recordId)
    {
        if (string.IsNullOrEmpty(recordId)) return false;
        string url = ApiRoutes.Inventory.GET_BY_ID.Replace("{inventoryId}", recordId);
        using var req = UnityWebRequest.Get(url);
        var token = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", $"Bearer {token}");
        return await SendRequestAsync(req, "ExistsRecord");
    }

    private async Task<bool> PutUpdateInventory(UpdateDto dto)
    {
        string url = ApiRoutes.Inventory.UPDATE_ITEM;
        string json = JsonUtility.ToJson(dto);
        using var req = UnityWebRequest.Put(url, json);
        req.SetRequestHeader("Content-Type", "application/json");
        var token = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", $"Bearer {token}");
        Debug.Log($"[PUT] {url}\nPayload: {json}");
        return await SendRequestAsync(req, "SyncInventory");
    }

    private async Task<bool> PostCreateInventory(string userId, string itemId, int qty, string inventoryName, int slotIndex)
    {
        string url = ApiRoutes.Inventory.ADD_ITEM_TO_INVENTORY;
        var f = new WWWForm();
        f.AddField("UserId", userId);
        f.AddField("ItemId", itemId);
        f.AddField("Quantity", qty);
        f.AddField("InventoryType", inventoryName);
        f.AddField("SlotIndex", slotIndex);
        using var req = UnityWebRequest.Post(url, f);
        req.downloadHandler = new DownloadHandlerBuffer();
        var token = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", $"Bearer {token}");
        Debug.Log($"[POST fallback] {url} -> create new record for {inventoryName}[{slotIndex}]");
        return await SendRequestAsync(req, "CreateInventory");
    }

    private async Task<Dictionary<string, string>> FetchServerRecordMap(string userId)
    {
        var map = new Dictionary<string, string>();
        string url = ApiRoutes.Inventory.GET_BY_USERID.Replace("{userId}", userId);

        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("Content-Type", "application/json");
            var token = LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", $"Bearer {token}");

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FetchServerRecordMap] FAIL {req.responseCode}: {req.error}");
                return map;
            }

            try
            {
                var resp = JsonUtility.FromJson<InventoryResponse>(req.downloadHandler.text);
                if (resp?.data != null)
                {
                    foreach (var it in resp.data)
                    {
                        var invName = string.Equals(it.inventoryType, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? TOOLBAR : BACKPACK;
                        map[$"{invName}:{it.slotIndex}"] = it.id; // it.id = Inventory recordId
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FetchServerRecordMap] parse error: {ex.Message}");
            }
        }
        return map;
    }
    // -------------------- Server sync --------------------
    public async Task SyncInventory(string inventoryName, bool reloadAfterSync = true)
    {
        if (!EnsureAuthReady(out var userId)) return;
        if (!inventoryByName.TryGetValue(inventoryName, out var inventory)) return;

        var nonEmpty = inventory.slots
            .Select((s, i) => new { s, i })
            .Where(x => !x.s.IsEmpty && x.s.itemData != null && !string.IsNullOrEmpty(x.s.itemData.id))
            .ToList();
        if (nonEmpty.Count == 0) return;

        // Nếu có slot chưa biết recordId → lấy snapshot từ server để tra id theo (Inventory,SlotIndex)
        bool needServerMap = nonEmpty.Any(x => !recordIdBySlot.ContainsKey(SlotKey(inventoryName, x.i))
                                            || string.IsNullOrEmpty(recordIdBySlot[SlotKey(inventoryName, x.i)]));
        Dictionary<string, string> serverMap = null;
        if (needServerMap)
            serverMap = await FetchServerRecordMap(userId);

        bool anyChanged = false;

        foreach (var x in nonEmpty)
        {
            string key = SlotKey(inventoryName, x.i);
            recordIdBySlot.TryGetValue(key, out var recId);

            // Nếu local chưa có recId thì thử lấy từ serverMap
            if (string.IsNullOrEmpty(recId) && serverMap != null && serverMap.TryGetValue(key, out var srvId))
            {
                recId = srvId;
                recordIdBySlot[key] = recId;
            }

            // Nếu vẫn chưa có recId → chắc chắn ô này chưa có record → POST tạo mới
            if (string.IsNullOrEmpty(recId))
            {
                bool created = await PostCreateInventory(userId, x.s.itemData.id, x.s.count, inventoryName, x.i);
                anyChanged |= created;
                continue;
            }

            // Có recId → PUT cập nhật
            var dto = new UpdateDto
            {
                Id = recId,
                UserId = userId,
                ItemId = x.s.itemData.id,
                Quantity = x.s.count,
                InventoryType = inventoryName,
                SlotIndex = x.i
            };

            bool ok = await PutUpdateInventory(dto);
            if (!ok)
            {
                // Có thể recId local đã cũ → thử dùng id từ serverMap (nếu khác)
                if (serverMap != null && serverMap.TryGetValue(key, out var srvId2) && !string.Equals(srvId2, recId))
                {
                    dto.Id = srvId2;
                    bool ok2 = await PutUpdateInventory(dto);
                    if (ok2)
                    {
                        recordIdBySlot[key] = srvId2;
                        anyChanged = true;
                        continue;
                    }
                }

                // PUT vẫn lỗi → fallback POST (trường hợp record đã bị xoá phía server)
                bool created = await PostCreateInventory(userId, x.s.itemData.id, x.s.count, inventoryName, x.i);
                anyChanged |= created;
            }
            else
            {
                anyChanged = true;
            }
        }

        if (anyChanged && reloadAfterSync)
            await LoadInventory(userId); // refresh lại mapping & UI
    }

    private async Task AddItemToServer(string userId, Item item, int quantity)
    {
        if (!EnsureAuthReady(out var ensuredUser)) return;
        userId = ensuredUser;

        var data = item?.Data;
        if (data == null || string.IsNullOrEmpty(data.id))
        {
            Debug.LogError("[AddItemToServer] Missing ItemData or ItemData.id");
            return;
        }

        int slotIndex = GetBestSlotIndexForAdd(backpack, item);
        if (slotIndex < 0)
        {
            Debug.LogWarning("[AddItemToServer] Backpack full, skip server call");
            return;
        }

        string url = ApiRoutes.Inventory.ADD_ITEM_TO_INVENTORY;

        WWWForm form = new WWWForm();
        form.AddField("UserId", userId);
        form.AddField("ItemId", data.id);
        form.AddField("Quantity", quantity.ToString());
        form.AddField("InventoryType", BACKPACK);
        form.AddField("SlotIndex", slotIndex.ToString());

        using var req = UnityWebRequest.Post(url, form);
        req.downloadHandler = new DownloadHandlerBuffer();

        string token = LocalStorageHelper.GetToken();
        if (!string.IsNullOrEmpty(token))
            req.SetRequestHeader("Authorization", $"Bearer {token}");

        bool ok = await SendRequestAsync(req, "AddItemToServer(form)");
        if (ok)
            await LoadInventory(userId);
    }

    private int GetBestSlotIndexForAdd(Inventory inv, Item item)
    {
        // 1) Tìm stack cùng loại còn chỗ
        for (int i = 0; i < inv.slots.Count; i++)
        {
            var s = inv.slots[i];
            if (!s.IsEmpty && s.itemName == item.Data.itemName && s.count < s.maxAllowed)
                return i;
        }
        // 2) Tìm ô trống
        for (int i = 0; i < inv.slots.Count; i++)
            if (inv.slots[i].IsEmpty) return i;

        return -1; // hết chỗ
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class InventoryData
{
    public List<Inventory.Slot> backpackSlots;
    public List<Inventory.Slot> toolbarSlots;
}

[Serializable]
public class InventoryUpdateRequest
{
    public string userId;
    public List<Inventory.Slot> slots;
}

[DefaultExecutionOrder(-50)]
public class InventoryManager : MonoBehaviour
{
    // Server-side list of items (loaded from GET /GetInventoryByUserId)
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

    public event System.Action OnInventoryLoaded; // Thêm sự kiện

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
            {
                _ = LoadInventory(AuthManager.Instance.GetCurrentUserId()); // Gọi nếu đã sẵn sàng
            }
        }
        else
        {
            StartCoroutine(WaitForAuthManager());
        }
    }

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

        isInitialized = true;
        Debug.Log("Empty inventories initialized");
    }

    private IEnumerator WaitForAuthManager()
    {
        float timeout = 10f;
        float timeElapsed = 0f;
        while (AuthManager.Instance == null && timeElapsed < timeout)
        {
            timeElapsed += Time.deltaTime;
            yield return null;
        }
        if (AuthManager.Instance != null)
        {
            Debug.Log("AuthManager found after waiting");
            AuthManager.Instance.OnUserInfoReceived += OnUserDataReceived;
            if (AuthManager.Instance.IsUserDataReady)
            {
                _ = LoadInventory(AuthManager.Instance.GetCurrentUserId()); // Gọi nếu đã sẵn sàng
            }
            else
            {
                yield return AuthManager.Instance.StartCoroutine(AuthManager.Instance.GetCurrentUser()); // Đợi tải user
                if (AuthManager.Instance.IsUserDataReady)
                {
                    _ = LoadInventory(AuthManager.Instance.GetCurrentUserId());
                }
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
            Debug.Log($"User data received in InventoryManager: {userData.id}");
            if (!isLoadingInventory)
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

    private async Task LoadInventory(string userId)
    {
        try
        {
            string url = ApiRoutes.GET_INVENTORY_BY_USERID.Replace("{userId}", userId);
            Debug.Log($"Loading inventory for userId: {userId} from URL: {url}");
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 15; // Tăng timeout
                request.SetRequestHeader("Content-Type", "application/json");
                string token = LocalStorageHelper.GetToken();
                if (!string.IsNullOrEmpty(token))
                {
                    Debug.Log($"Using token: {token.Substring(0, 10)}..."); // In 10 ký tự đầu
                    request.SetRequestHeader("Authorization", $"Bearer {token}");
                }
                else
                {
                    Debug.LogError("No token available!");
                }

                var op = request.SendWebRequest();
                while (!op.isDone) await Task.Yield(); // Đợi request hoàn tất

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string jsonResponse = request.downloadHandler.text;
                    Debug.Log($"Inventory API response for {userId}: {jsonResponse}");

                    InventoryResponse response = JsonUtility.FromJson<InventoryResponse>(jsonResponse);
                    if (response != null && response.data != null)
                    {
                        inventoryItems = new List<InventoryItem>(response.data);
                        Debug.Log($"Loaded {inventoryItems.Count} items from server. Items: {string.Join(", ", inventoryItems.Select(i => i.itemType))}");
                        ApplyServerInventoryToLocal(response.data);
                        OnInventoryLoaded?.Invoke();
                    }
                    else
                    {
                        inventoryItems = new List<InventoryItem>();
                        Debug.LogWarning($"No inventory data found in response for userId {userId}. Response: {jsonResponse}");
                    }
                }
                else
                {
                    Debug.LogError($"Error loading inventory for {userId}: {request.error} - HTTP {request.responseCode} - {request.downloadHandler.text}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception in LoadInventory for {userId}: {ex.Message} - StackTrace: {ex.StackTrace}");
        }
    }

    private void ApplyServerInventoryToLocal(InventoryItem[] serverData = null)
    {
        //Debug.Log($"Applying inventory to backpack. ServerData length: {(serverData != null ? serverData.Length : inventoryItems.Count)}");
        foreach (var s in backpack.slots)
        {
            s.itemName = "";
            s.count = 0;
            s.icon = null;
            s.itemData = null;
        }

        var dataToUse = serverData ?? inventoryItems.ToArray();
        //Debug.Log($"Applying {dataToUse.Length} items to backpack.");
        int idx = 0;
        foreach (var srv in dataToUse)
        {
            if (idx >= backpack.slots.Count) break;
            var slot = backpack.slots[idx];
            slot.itemName = srv.itemType;
            slot.count = srv.quantity;
            var item = GameManager.instance.itemManager.GetItemByName(srv.itemType);
            if (item != null && item.Data != null)
            {
                slot.icon = item.Data.icon; // Lấy icon từ ItemData
            }
            else
            {
                Debug.LogWarning($"Item or ItemData for {srv.itemType} is null!");
            }
            //Debug.Log($"Applied to Slot {idx}: {slot.itemName}, Count: {slot.count}, Icon: {(slot.icon != null ? "assigned" : "null")}");
            idx++;
        }
    }

    public Inventory GetInventoryByName(string name)
    {
        return inventoryByName.TryGetValue(name, out var inventory) ? inventory : null;
    }

    public async void AddItem(string inventoryName, Item item)
    {
        if (!isInitialized)
        {
            Debug.LogWarning("InventoryManager not initialized yet");
            return;
        }

        if (!inventoryByName.TryGetValue(inventoryName, out var inventory))
        {
            Debug.LogWarning($"Inventory {inventoryName} not found locally");
            return;
        }

        bool localHad = HasItem(inventoryName, item);
        if (localHad)
        {
            inventory.IncreaseItemQuantity(item.Data.itemName);
        }
        else
        {
            inventory.Add(item);
        }

        string itemTypeKey = item.Data.itemName;
        bool existsOnServer = inventoryItems.Any(i => string.Equals(i.itemType, itemTypeKey, StringComparison.OrdinalIgnoreCase));

        string userId = AuthManager.Instance?.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogWarning("UserId missing, cannot sync with server");
            return;
        }

        if (existsOnServer)
        {
            Debug.Log($"Item '{itemTypeKey}' exists on server → calling Update (PUT)");
            await SyncInventory(inventoryName);
        }
        else
        {
            Debug.Log($"Item '{itemTypeKey}' NOT on server → calling Add (POST)");
            await AddItemToServer(userId, itemTypeKey, 1);
            await LoadInventory(userId);
        }
    }

    private async Task AddItemToServer(string userId, string itemType, int quantity)
    {
        try
        {
            var payload = new AddItemRequest
            {
                userId = userId,
                itemType = itemType,
                quantity = quantity
            };
            string json = JsonUtility.ToJson(payload);
            string url = ApiRoutes.ADD_ITEM;

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                string token = LocalStorageHelper.GetToken();
                if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", $"Bearer {token}");

                await SendRequestAsync(request);

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"AddItemToServer successful: {request.downloadHandler.text}");
                }
                else
                {
                    Debug.LogError($"AddItemToServer failed: {request.error} - {request.downloadHandler.text}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Exception in AddItemToServer: " + ex.Message);
        }
    }

    public async Task SyncInventory(string inventoryName)
    {
        if (!inventoryByName.TryGetValue(inventoryName, out var inventory))
        {
            Debug.LogWarning($"Inventory {inventoryName} not found");
            return;
        }

        string userId = AuthManager.Instance?.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogWarning("UserId is null or empty, cannot sync inventory!");
            return;
        }

        var nonEmptySlots = inventory.slots.Where(s => !s.IsEmpty).ToList();
        if (!nonEmptySlots.Any())
        {
            Debug.Log($"Inventory {inventoryName} is empty, skipping sync");
            return;
        }

        InventoryUpdateRequest requestData = new InventoryUpdateRequest
        {
            userId = userId,
            slots = nonEmptySlots
        };

        string jsonData = JsonUtility.ToJson(requestData);
        Debug.Log($"Syncing inventory {inventoryName} with data: {jsonData}");

        string url = ApiRoutes.UPDATE_ITEM;

        using (UnityWebRequest request = UnityWebRequest.Put(url, jsonData))
        {
            request.SetRequestHeader("Content-Type", "application/json");
            string token = LocalStorageHelper.GetToken();
            if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", $"Bearer {token}");

            await SendRequestAsync(request);

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"Inventory {inventoryName} synced successfully: {request.downloadHandler.text}");
                await LoadInventory(userId);
            }
            else
            {
                Debug.LogError($"Failed to sync inventory {inventoryName}: {request.error} - Response: {request.downloadHandler.text}");
            }
        }
    }

    public bool HasItem(string inventoryName, Item item)
    {
        if (!inventoryByName.ContainsKey(inventoryName)) return false;
        Inventory inventory = inventoryByName[inventoryName];
        foreach (Inventory.Slot slot in inventory.slots)
        {
            if (!slot.IsEmpty && slot.itemData != null && slot.itemData.itemName == item.Data.itemName)
            {
                return true;
            }
        }
        return false;
    }

    public async void UseItem(string inventoryName, int slotIndex, int amount = 1)
    {
        if (!isInitialized)
        {
            Debug.LogWarning("InventoryManager not initialized yet");
            return;
        }
        if (inventoryByName.TryGetValue(inventoryName, out var inventory))
        {
            if (slotIndex >= 0 && slotIndex < inventory.slots.Count)
            {
                Debug.Log($"Using item at {inventoryName}[{slotIndex}], Before: Count = {inventory.slots[slotIndex].count}, Type = {inventory.slots[slotIndex].itemData?.itemType}");
                inventory.Remove(slotIndex, amount);
                Debug.Log($"After: Count = {inventory.slots[slotIndex].count}");
                if (inventory.slots[slotIndex].count <= 0)
                {
                    await DeleteItem(inventoryName, slotIndex);
                }
                else
                {
                    await SyncInventory(inventoryName);
                }
            }
        }
    }

    public async Task DeleteItem(string inventoryName, int slotIndex)
    {
        if (inventoryByName.TryGetValue(inventoryName, out var inventory))
        {
            if (slotIndex < 0 || slotIndex >= inventory.slots.Count || inventory.slots[slotIndex].IsEmpty)
            {
                Debug.LogWarning($"Invalid slot index {slotIndex} or slot is empty");
                return;
            }
            string itemId = inventory.slots[slotIndex].itemData?.id;
            if (!string.IsNullOrEmpty(itemId))
            {
                string url = ApiRoutes.DELETE_ITEM.Replace("{inventoryId}", itemId);
                using (UnityWebRequest request = UnityWebRequest.Delete(url))
                {
                    string token = LocalStorageHelper.GetToken();
                    if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", $"Bearer {token}");
                    await SendRequestAsync(request);
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        inventory.Remove(slotIndex);
                        await SyncInventory(inventoryName);
                        Debug.Log($"Item deleted successfully from {inventoryName}[{slotIndex}]");
                    }
                    else
                    {
                        Debug.LogError($"Failed to delete item: {request.error} - Response: {request.downloadHandler.text}");
                    }
                }
            }
            else
            {
                inventory.Remove(slotIndex);
                Debug.Log($"Item removed locally from {inventoryName}[{slotIndex}]");
            }
        }
    }

    private void OnDestroy()
    {
        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.OnUserInfoReceived -= OnUserDataReceived;
        }
    }

    private static async Task SendRequestAsync(UnityWebRequest request)
    {
        var op = request.SendWebRequest();
        await Task.Run(() =>
        {
            while (!op.isDone) System.Threading.Thread.Sleep(1);
        });
    }

    [Serializable]
    private class AddItemRequest
    {
        public string userId;
        public string itemType;
        public int quantity;
    }
}
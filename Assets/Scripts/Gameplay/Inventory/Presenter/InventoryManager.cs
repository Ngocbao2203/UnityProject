using System;
using System.Collections.Generic;
using System.Linq; // <-- thêm để dùng LINQ
using UnityEngine;

using CGP.Gameplay.Auth;
using CGP.Gameplay.Items;
using CGP.Gameplay.InventorySystem;
using CGP.Gameplay.Config;
using CGP.Networking.DTOs; // để dùng UserData

// Alias ngắn
using Inv = CGP.Gameplay.InventorySystem.Inventory;
using Slot = CGP.Gameplay.InventorySystem.Inventory.Slot;

namespace CGP.Gameplay.Inventory.Presenter
{
    /// <summary>
    /// Core (partial). Giữ vòng đời, binding, helper public, StarterPack.
    /// Phần Models/Network/Sync được tách ở các file partial khác.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public partial class InventoryManager : MonoBehaviour
    {
        // ===================== Singleton =====================
        public static InventoryManager Instance { get; private set; }

        // ===================== Hằng số & Kho =====================
        public const string BACKPACK = "Backpack";
        public const string TOOLBAR = "Toolbar";

        [Header("Backpack")]
        public Inv backpack;
        public int backpackSlotsCount = 27;

        [Header("Toolbar")]
        public Inv toolbar;
        public int toolbarSlotsCount = 7;

        // ===================== Trạng thái & cache =====================
        public List<InventoryItem> inventoryItems = new();

        // name => inventory
        private readonly Dictionary<string, Inv> _invByName = new();

        // "Inv:Idx" -> recordId (server)
        private readonly Dictionary<string, string> _recordIdBySlot = new();
        private static string SlotKey(string inv, int idx) => $"{inv}:{idx}";

        // debounce & hàng đợi
        private bool _isInitialized, _isLoading, _isDragging, _isSyncing;
        private readonly Queue<Action> _pending = new();
        private readonly Dictionary<string, float> _lastSyncAt = new();

        // drag snapshot (để UI mượt hơn nếu cần)
        private Slot _dragSnapshot;
        private string _dragFromInv;
        private int _dragFromSlot = -1;

        // ===== Guard chống double-consume cùng 1 frame =====
        private readonly HashSet<string> _consumingKeys = new();

        // Sự kiện cho UI
        public event Action OnInventoryLoaded;

        private static readonly System.Threading.SemaphoreSlim _starterPackLock = new(1, 1);
        private bool _starterPackInProgress = false;

        // ===================== Vòng đời =====================
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

        private System.Collections.IEnumerator WaitForAuthManager()
        {
            float t = 0f;
            while (AuthManager.Instance == null && t < 10f)
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
        }

        private async void OnUserDataReceived(bool success, string message, UserData user)
        {
            if (!success || user == null || _isLoading || _isDragging) return;

            _isLoading = true;
            await LoadInventory(user.id);
            _isLoading = false;
        }

        // ===================== Public helpers =====================
        public Inv GetInventoryByName(string name)
            => _invByName.TryGetValue(name, out var inv) ? inv : null;

        public void SetDragState(bool dragging)
        {
            _isDragging = dragging;
            if (!dragging) ProcessPendingQueue();
        }

        public bool IsDragging() => _isDragging;
        public bool IsSyncing() => _isSyncing;

        // ===================== Khởi tạo rỗng =====================
        private void InitializeEmptyInventories()
        {
            backpack = new Inv(backpackSlotsCount);
            toolbar = new Inv(toolbarSlotsCount);

            _invByName.Clear();
            _invByName.Add(BACKPACK, backpack);
            _invByName.Add(TOOLBAR, toolbar);

            backpack.slots = new List<Slot>();
            toolbar.slots = new List<Slot>();
            for (int i = 0; i < backpackSlotsCount; i++) backpack.slots.Add(new Slot());
            for (int i = 0; i < toolbarSlotsCount; i++) toolbar.slots.Add(new Slot());

            _recordIdBySlot.Clear();
            _isInitialized = true;
        }

        private void ProcessPendingQueue()
        {
            while (_pending.Count > 0 && !_isDragging && !_isSyncing)
                _pending.Dequeue()?.Invoke();
        }

        // ===================== Auth guard =====================
        private bool EnsureAuthReady(out string userId)
        {
            userId = AuthManager.Instance?.GetCurrentUserId();
            return AuthManager.Instance != null
                   && AuthManager.Instance.IsUserDataReady
                   && !string.IsNullOrEmpty(userId);
        }

        // ===================== Load & Apply local =====================
        private async System.Threading.Tasks.Task LoadInventory(string userId, bool applyToLocal = true)
        {
            try
            {
                var list = await FetchInventoryData(userId);   // (ở file Network)
                inventoryItems = list ?? new List<InventoryItem>();

                if (applyToLocal)
                {
                    ApplyServerToLocal();
                    OnInventoryLoaded?.Invoke();
                }
            }
            catch
            {
                // tùy bạn thêm log
            }
        }

        // === Thay thế toàn bộ hàm ApplyServerToLocal hiện có bằng bản dưới đây ===
        private void ApplyServerToLocal()
        {
            if (_isDragging) return;

            // 0) Clear local
            for (int i = 0; i < backpack.slots.Count; i++) backpack.slots[i] = new Slot();
            for (int i = 0; i < toolbar.slots.Count; i++) toolbar.slots[i] = new Slot();
            _recordIdBySlot.Clear();

            if (inventoryItems == null || inventoryItems.Count == 0) return;

            // 1) MERGE: gộp các record trùng (Inv, Slot, ItemId)
            var merged = inventoryItems
                .Where(r => r != null && r.quantity > 0)
                .GroupBy(r => new
                {
                    Inv = string.Equals(r.inventoryType, TOOLBAR, System.StringComparison.OrdinalIgnoreCase) ? TOOLBAR : BACKPACK,
                    Slot = r.slotIndex,
                    Item = r.itemId ?? string.Empty
                })
                .Select(g =>
                {
                    // chọn 1 record đại diện (id/itemType) — có thể chọn newest nếu bạn có timestamp
                    var repr = g.First();
                    return new
                    {
                        Inv = g.Key.Inv,
                        Slot = g.Key.Slot,
                        ItemId = string.IsNullOrEmpty(g.Key.Item) ? repr.itemId : g.Key.Item,
                        ItemType = repr.itemType,
                        RecordId = repr.id,
                        Qty = g.Sum(x => Mathf.Max(0, x.quantity))
                    };
                })
                .ToList();

            // 2) Map sang ItemData để có icon + tên hiển thị
            var im = GameManager.instance ? GameManager.instance.itemManager
                              : UnityEngine.Object.FindFirstObjectByType<ItemManager>();

            foreach (var m in merged)
            {
                var inv = GetInventoryByName(m.Inv) ?? backpack;
                if (m.Slot < 0 || m.Slot >= inv.slots.Count) continue;

                var slot = new Slot { count = m.Qty };

                ItemData data = null;
                if (!string.IsNullOrEmpty(m.ItemId))
                    data = im?.GetItemDataByServerId(m.ItemId);
                if (data == null && !string.IsNullOrEmpty(m.ItemType))
                    data = im?.GetItemDataByName(m.ItemType);

                if (data != null)
                {
                    slot.itemName = data.itemName;
                    slot.icon = data.icon;
                    slot.itemData = data;
                }
                else
                {
                    slot.itemName = !string.IsNullOrEmpty(m.ItemType) ? m.ItemType : (m.ItemId ?? "(Unknown)");
                }

                inv.slots[m.Slot] = slot;

                // lưu record id để Delete/Update về sau
                if (!string.IsNullOrEmpty(m.RecordId))
                    _recordIdBySlot[SlotKey(m.Inv, m.Slot)] = m.RecordId;
            }

            OnInventoryLoaded?.Invoke();
        }

        // ===================== Starter Pack =====================
        public async System.Threading.Tasks.Task EnsureStarterPackOnFirstLogin(StarterPackConfig cfg, bool reloadAfter = true)
        {
            // khóa chống gọi chồng
            if (_starterPackInProgress) return;
            _starterPackInProgress = true;
            await _starterPackLock.WaitAsync();
            try
            {
                if (!EnsureAuthReady(out var userId) || cfg == null) return;

                string playerPrefKey = $"cgp_starter_pack_given:{userId}";

                // Nếu client đã đánh dấu → bỏ qua
                if (PlayerPrefs.GetInt(playerPrefKey, 0) == 1)
                {
                    if (reloadAfter) await LoadInventory(userId, applyToLocal: true);
                    return;
                }

                // Lấy snapshot server ban đầu
                var server = await FetchInventoryData(userId) ?? new List<InventoryItem>();

                // Kiểm tra marker trên server
                bool hasMarkerOnServer = !string.IsNullOrEmpty(cfg.markerItemId) &&
                                         server.Any(it =>
                                            string.Equals(it.itemId, cfg.markerItemId, StringComparison.OrdinalIgnoreCase) &&
                                            string.Equals(it.inventoryType ?? "", (cfg.markerInventoryType ?? "System"), StringComparison.OrdinalIgnoreCase) &&
                                            it.quantity > 0);

                if (hasMarkerOnServer)
                {
                    // server đã cho → đánh dấu client và thoát
                    PlayerPrefs.SetInt(playerPrefKey, 1);
                    PlayerPrefs.Save();
                    if (reloadAfter) await LoadInventory(userId, applyToLocal: true);
                    return;
                }

                // ❗ ĐÁNH DẤU SỚM trên client để chặn lần gọi thứ hai trong cùng phiên
                PlayerPrefs.SetInt(playerPrefKey, 1);
                PlayerPrefs.Save();

                // 1) TẠO MARKER TRƯỚC
                if (!string.IsNullOrEmpty(cfg.markerItemId))
                {
                    string invMarker = string.IsNullOrEmpty(cfg.markerInventoryType) ? "System" : cfg.markerInventoryType;
                    int capMarker = string.Equals(invMarker, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? toolbarSlotsCount : backpackSlotsCount;

                    // tìm slot trống cho marker
                    int markerSlot = -1;
                    for (int i = 0; i < capMarker; i++)
                        if (!server.Any(it => string.Equals(it.inventoryType, invMarker, StringComparison.OrdinalIgnoreCase) && it.slotIndex == i))
                        { markerSlot = i; break; }

                    if (markerSlot >= 0)
                    {
                        var markerId = await PostCreate(userId, cfg.markerItemId, 1, invMarker, markerSlot);
                        if (!string.IsNullOrEmpty(markerId))
                        {
                            server.Add(new InventoryItem
                            {
                                id = markerId,
                                userId = userId,
                                itemId = cfg.markerItemId,
                                inventoryType = invMarker,
                                slotIndex = markerSlot,
                                quantity = 1
                            });
                        }
                        else
                        {
                            // nếu tạo marker thất bại, vẫn tiếp tục nhưng vẫn có nguy cơ double nếu gọi lại
                            Debug.LogWarning("[StarterPack] Failed to create marker.");
                        }
                    }
                }

                // 2) CẤP QUÀ — đảm bảo tối thiểu, KHÔNG cộng dồn
                if (cfg.items != null)
                {
                    foreach (var g in cfg.items)
                    {
                        if (g == null || g.item == null || string.IsNullOrEmpty(g.item.id)) continue;

                        int desiredQty = Mathf.Max(1, g.quantity);
                        string invName = string.IsNullOrEmpty(g.inventoryType) ? BACKPACK : g.inventoryType;
                        int capacity = string.Equals(invName, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? toolbarSlotsCount : backpackSlotsCount;

                        // tìm record cùng item (bất kể slot) trong inventory đích
                        var exist = server.FirstOrDefault(it =>
                            string.Equals(it.inventoryType, invName, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(it.itemId, g.item.id, StringComparison.OrdinalIgnoreCase));

                        if (exist != null)
                        {
                            // chỉ nâng lên tối thiểu desiredQty
                            int newQty = Mathf.Max(exist.quantity, desiredQty);
                            if (newQty != exist.quantity)
                            {
                                var dto = new UpdateDto
                                {
                                    Id = exist.id,
                                    UserId = exist.userId,
                                    ItemId = exist.itemId,
                                    Quantity = newQty,
                                    InventoryType = invName,
                                    SlotIndex = exist.slotIndex
                                };
                                var put = await PutUpdate(dto);
                                if (put.ok) exist.quantity = newQty;
                            }
                            continue;
                        }

                        // chưa có → đặt vào slot mong muốn/trống
                        bool SlotOccupied(int idx) => server.Any(it =>
                            string.Equals(it.inventoryType, invName, StringComparison.OrdinalIgnoreCase) &&
                            it.slotIndex == idx);

                        int slot = (g.preferredSlot >= 0 && g.preferredSlot < capacity && !SlotOccupied(g.preferredSlot))
                                   ? g.preferredSlot : -1;

                        if (slot < 0)
                        {
                            for (int i = 0; i < capacity; i++)
                                if (!SlotOccupied(i)) { slot = i; break; }
                        }

                        if (slot < 0)
                        {
                            Debug.LogWarning($"[StarterPack] No free slot in {invName} for '{g.item.itemName}'.");
                            continue;
                        }

                        var newId = await PostCreate(userId, g.item.id, desiredQty, invName, slot);
                        if (!string.IsNullOrEmpty(newId))
                        {
                            server.Add(new InventoryItem
                            {
                                id = newId,
                                userId = userId,
                                itemId = g.item.id,
                                itemType = g.item.itemName,
                                inventoryType = invName,
                                slotIndex = slot,
                                quantity = desiredQty
                            });
                        }
                    }
                }

                if (reloadAfter)
                {
                    await LoadInventory(userId, applyToLocal: true);
                }
            }
            finally
            {
                _starterPackLock.Release();
                _starterPackInProgress = false;
            }
        }

        public void EnsureStarterPackOnFirstLogin(bool reloadAfterSync = true, bool allowCreateIfMissing = true)
        {
            StartCoroutine(_StarterPackSyncAfterFlags(reloadAfterSync, allowCreateIfMissing));
        }

        private System.Collections.IEnumerator _StarterPackSyncAfterFlags(bool reloadAfterSync, bool allowCreateIfMissing)
        {
            float t = 0f;
            while ((AuthManager.Instance == null || !AuthManager.Instance.IsUserDataReady) && t < 5f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!reloadAfterSync) yield break;

            var t1 = SyncInventory(BACKPACK, reloadAfterSync: true, allowCreateIfMissing: allowCreateIfMissing, ignoreDebounce: true);
            var t2 = SyncInventory(TOOLBAR, reloadAfterSync: true, allowCreateIfMissing: allowCreateIfMissing, ignoreDebounce: true);
            while (!t1.IsCompleted || !t2.IsCompleted) yield return null;
        }

        // ===================== Drag snapshot (optional) =====================
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
                    _dragSnapshot = new Slot { itemName = s.itemName, count = s.count, icon = s.icon, itemData = s.itemData };
            }
        }
        public void EndDrag() { }

        // ===================== Consume API (fix -2 & ẩn icon) =====================
        public async void UseItem(string inventoryName, int slotIndex, int amount = 1)
        {
            if (!_isInitialized) return;

            string key = $"{inventoryName}:{slotIndex}";
            if (_consumingKeys.Contains(key)) return; // đang xử lý

            if (_isDragging || _isSyncing)
            {
                _pending.Enqueue(() => UseItem(inventoryName, slotIndex, amount));
                return;
            }

            if (!ValidateInventories(inventoryName, out var inv)) return;
            if (slotIndex < 0 || slotIndex >= inv.slots.Count) return;

            var s = inv.slots[slotIndex];
            if (s == null || s.IsEmpty) return;

            _consumingKeys.Add(key);
            try
            {
                // Trừ đúng 1 lần
                s.count = Mathf.Max(0, s.count - amount);

                if (s.count <= 0)
                {
                    // Clear local để UI ẩn icon ngay
                    inv.slots[slotIndex] = new Slot();
                    await DeleteItem(inventoryName, slotIndex, alreadyCleared: true);
                }
                else
                {
                    await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: true);
                }

                OnInventoryLoaded?.Invoke(); // cho UI refresh nhẹ
            }
            finally
            {
                _consumingKeys.Remove(key);
            }
        }

        public async System.Threading.Tasks.Task ReloadFromServer()
        {
            if (!EnsureAuthReady(out var userId)) return;
            await LoadInventory(userId, applyToLocal: true); // sẽ gọi ApplyServerToLocal + OnInventoryLoaded
        }

        public async System.Threading.Tasks.Task DeleteItem(string inventoryName, int slotIndex, bool alreadyCleared = false)
        {
            if (!_invByName.TryGetValue(inventoryName, out var inv)) return;
            if (slotIndex < 0 || slotIndex >= inv.slots.Count) return;

            string key = $"{inventoryName}:{slotIndex}";

            // Nếu UseItem đã clear slot, không đụng local nữa để tránh -2
            if (!alreadyCleared)
                inv.slots[slotIndex] = new Slot();

            if (_recordIdBySlot.TryGetValue(key, out var recordId) && !string.IsNullOrEmpty(recordId))
            {
                await DeleteRecord(recordId); // ở InventoryManager.Network.cs
                _recordIdBySlot.Remove(key);
            }

            await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: true);
        }

        // ===================== Validate =====================
        private bool ValidateInventories(string inventoryName, out Inv inv)
        {
            inv = null;
            if (string.IsNullOrEmpty(inventoryName)) return false;
            if (!_invByName.TryGetValue(inventoryName, out inv) || inv == null) return false;

            if (inv.slots == null) inv.slots = new List<Slot>();
            int need = inventoryName.Equals(TOOLBAR, StringComparison.OrdinalIgnoreCase) ? toolbarSlotsCount : backpackSlotsCount;
            while (inv.slots.Count < need) inv.slots.Add(new Slot());
            for (int i = 0; i < inv.slots.Count; i++) if (inv.slots[i] == null) inv.slots[i] = new Slot();
            return true;
        }

        // ===================== Debug tiện dụng =====================
        public void DebugDumpServerInventory()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Inventory] Server snapshot:");
            if (inventoryItems == null || inventoryItems.Count == 0) sb.AppendLine("  (empty)");
            else foreach (var it in inventoryItems)
                    sb.AppendLine($"  - id={it?.id} itemId={it?.itemId} type={it?.itemType} inv={it?.inventoryType} slot={it?.slotIndex} qty={it?.quantity}");
            Debug.Log(sb.ToString());
        }
    }
}

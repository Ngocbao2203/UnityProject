using System;
using System.Collections.Generic;
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

        private void ApplyServerToLocal()
        {
            if (_isDragging) return;

            // clear local
            for (int i = 0; i < backpack.slots.Count; i++) backpack.slots[i] = new Slot();
            for (int i = 0; i < toolbar.slots.Count; i++) toolbar.slots[i] = new Slot();
            _recordIdBySlot.Clear();

            if (inventoryItems == null || inventoryItems.Count == 0) return;

            var im = GameManager.instance ? GameManager.instance.itemManager
                                          : FindFirstObjectByType<ItemManager>();

            foreach (var rec in inventoryItems)
            {
                if (rec == null || rec.quantity <= 0) continue;

                var invName = string.Equals(rec.inventoryType, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? TOOLBAR : BACKPACK;
                var inv = GetInventoryByName(invName) ?? backpack;

                if (rec.slotIndex < 0 || rec.slotIndex >= inv.slots.Count) continue;

                var slot = new Slot { count = rec.quantity };

                ItemData data = null;
                if (im != null)
                {
                    if (!string.IsNullOrEmpty(rec.itemId))
                        data = im.GetItemDataByServerId(rec.itemId);
                    if (data == null && !string.IsNullOrEmpty(rec.itemType))
                        data = im.GetItemDataByName(rec.itemType);
                }

                if (data != null)
                {
                    slot.itemName = data.itemName;
                    slot.icon = data.icon;
                    slot.itemData = data;
                }
                else
                {
                    slot.itemName = !string.IsNullOrEmpty(rec.itemType) ? rec.itemType : (rec.itemId ?? "(Unknown)");
                }

                inv.slots[rec.slotIndex] = slot;

                if (!string.IsNullOrEmpty(rec.id))
                    _recordIdBySlot[SlotKey(invName, rec.slotIndex)] = rec.id;
            }
        }

        // ===================== Starter Pack =====================
        public async System.Threading.Tasks.Task EnsureStarterPackOnFirstLogin(StarterPackConfig cfg, bool reloadAfter = true)
        {
            const string KEY = "cgp_starter_pack_given";

            // Cấp quà lần đầu (tùy bạn xử lý thêm theo cfg.items)
            if (PlayerPrefs.GetInt(KEY, 0) == 0)
            {
                PlayerPrefs.SetInt(KEY, 1);
                PlayerPrefs.Save();
            }

            if (reloadAfter)
            {
                await System.Threading.Tasks.Task.WhenAll(
                    SyncInventory(BACKPACK, reloadAfterSync: true, allowCreateIfMissing: true, ignoreDebounce: true),
                    SyncInventory(TOOLBAR, reloadAfterSync: true, allowCreateIfMissing: true, ignoreDebounce: true)
                );
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

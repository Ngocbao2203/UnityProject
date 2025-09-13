using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

using CGP.Gameplay.Items;
using CGP.Gameplay.InventorySystem;

using Inv = CGP.Gameplay.InventorySystem.Inventory;
using Slot = CGP.Gameplay.InventorySystem.Inventory.Slot;

namespace CGP.Gameplay.Inventory.Presenter
{
    public partial class InventoryManager
    {
        // ====== chống double-consume theo slot (cooldown ngắn) ======
        private readonly Dictionary<string, float> _consumeCooldownUntil = new();
        private static string ConsumeKey(string inv, int slot) => $"{inv}:{slot}";

        // ===== Public CRUD =====
        public async void AddItem(string inventoryName, Item item)
        {
            if (!_isInitialized) return;
            if (_isDragging || _isSyncing) { _pending.Enqueue(() => AddItem(inventoryName, item)); return; }
            if (!EnsureAuthReady(out var userId)) return;
            if (!_invByName.TryGetValue(inventoryName, out var inv)) return;

            var id = item?.Data?.id;
            if (string.IsNullOrEmpty(id)) return;

            // ưu tiên stack
            int target = -1;
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                if (!s.IsEmpty && s.itemData != null && s.itemData.id == id && s.count < s.maxAllowed) { target = i; break; }
            }
            if (target < 0)
                for (int i = 0; i < inv.slots.Count; i++)
                    if (inv.slots[i].IsEmpty) { target = i; break; }

            if (target < 0) { Debug.LogWarning("[Inventory] Hết chỗ!"); return; }

            if (inv.slots[target].IsEmpty)
                inv.slots[target] = new Slot { itemName = item.Data.itemName, icon = item.Data.icon, itemData = item.Data, count = 1 };
            else
                inv.slots[target].count += 1;

            await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: true, ignoreDebounce: false);
        }

        public async Task<bool> MoveItem(string fromInventory, int fromSlot, string toInventory, int toSlot)
        {
            if (!_isInitialized || _isDragging || _isSyncing) return false;

            if (!_invByName.TryGetValue(fromInventory, out var fromInv)) return false;
            if (!_invByName.TryGetValue(toInventory, out var toInv)) return false;
            if (fromSlot < 0 || fromSlot >= fromInv.slots.Count) return false;
            if (toSlot < 0 || toSlot >= toInv.slots.Count) return false;

            var a = fromInv.slots[fromSlot];
            var b = toInv.slots[toSlot];
            if (a.IsEmpty) return false;

            _isSyncing = true;
            try
            {
                // move/stack/swap local
                if (b.IsEmpty)
                {
                    toInv.slots[toSlot] = new Slot { itemName = a.itemName, count = a.count, icon = a.icon, itemData = a.itemData };
                    fromInv.slots[fromSlot] = new Slot();
                }
                else if (a.itemData != null && b.itemData != null && a.itemData.id == b.itemData.id)
                {
                    b.count += a.count;
                    fromInv.slots[fromSlot] = new Slot();
                }
                else
                {
                    fromInv.slots[fromSlot] = new Slot { itemName = b.itemName, count = b.count, icon = b.icon, itemData = b.itemData };
                    toInv.slots[toSlot] = new Slot { itemName = a.itemName, count = a.count, icon = a.icon, itemData = a.itemData };
                }

                // Đồng bộ tối thiểu
                if (fromInventory != toInventory)
                {
                    await SyncInventory(fromInventory, reloadAfterSync: false, allowCreateIfMissing: true);
                    await SyncInventory(toInventory, reloadAfterSync: true, allowCreateIfMissing: true);
                }
                else
                {
                    await SyncInventory(fromInventory, reloadAfterSync: true, allowCreateIfMissing: true);
                }

                return true;
            }
            finally { _isSyncing = false; }
        }

        // ======= TryConsume: trừ đúng 1 lần + clear ngay khi =0 + chống double call =======
        public async void TryConsume(string inventoryName, int slotIndex, int amount = 1)
        {
            if (!_isInitialized) return;
            if (_isDragging || _isSyncing) { _pending.Enqueue(() => TryConsume(inventoryName, slotIndex, amount)); return; }
            if (!ValidateInventories(inventoryName, out var inv)) return;
            if (slotIndex < 0 || slotIndex >= inv.slots.Count) return;

            // chặn spam / double-fire trong khoảng rất ngắn
            var ckey = ConsumeKey(inventoryName, slotIndex);
            if (_consumeCooldownUntil.TryGetValue(ckey, out var until) && Time.unscaledTime < until) return;
            _consumeCooldownUntil[ckey] = Time.unscaledTime + 0.2f; // 200ms

            var s = inv.slots[slotIndex];
            if (s == null || s.IsEmpty) return;

            s.count = Mathf.Max(0, s.count - amount);
            if (s.count == 0) inv.slots[slotIndex] = new Slot(); // clear icon ngay

            await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: false);
        }

        public async Task DeleteItem(string inventoryName, int slotIndex)
        {
            if (!_invByName.TryGetValue(inventoryName, out var inv)) return;
            if (slotIndex < 0 || slotIndex >= inv.slots.Count) return;

            inv.slots[slotIndex] = new Slot(); // clear local
            await SyncInventory(inventoryName, reloadAfterSync: true, allowCreateIfMissing: false);
        }

        public bool HasItem(string inventoryName, Item item)
        {
            if (!_invByName.ContainsKey(inventoryName)) return false;
            var inv = _invByName[inventoryName];
            var id = item?.Data?.id;
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var s in inv.slots)
                if (!s.IsEmpty && s.itemData != null && s.itemData.id == id) return true;
            return false;
        }

        public int GetQuantityByItemId(string itemId)
        {
            if (!_isInitialized || string.IsNullOrEmpty(itemId)) return 0;
            int total = 0;
            if (inventoryItems != null)
                foreach (var it in inventoryItems)
                    if (it != null && string.Equals(it.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                        total += Mathf.Max(0, it.quantity);
            return total;
        }

        // ===== SyncInventory: thuật toán DIFF gọn gàng =====
        public async Task<bool> SyncInventory(string inventoryName, bool reloadAfterSync = true, bool allowCreateIfMissing = true, bool ignoreDebounce = false)
        {
            try
            {
                if (!EnsureAuthReady(out var userId)) return false;
                if (!ValidateInventories(inventoryName, out var inv)) return false;

                if (!ignoreDebounce)
                {
                    if (!_lastSyncAt.TryGetValue(inventoryName, out var last)) last = -999f;
                    if (Time.unscaledTime - last < 0.08f) return false;
                }
                _lastSyncAt[inventoryName] = Time.unscaledTime;

                // 1) Chuẩn hoá non-null
                for (int i = 0; i < inv.slots.Count; i++)
                    if (inv.slots[i] == null) inv.slots[i] = new Slot();

                // 2) Snapshot server theo slot của inventory này
                var serverList = await FetchInventoryData(userId) ?? new List<InventoryItem>();
                var serverBySlot = new Dictionary<int, InventoryItem>();
                foreach (var it in serverList)
                {
                    var invName = string.Equals(it.inventoryType, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? TOOLBAR : BACKPACK;
                    if (!string.Equals(invName, inventoryName, StringComparison.OrdinalIgnoreCase)) continue;
                    serverBySlot[it.slotIndex] = it;
                }

                bool changed = false;

                // 3) Diff theo slot
                for (int i = 0; i < inv.slots.Count; i++)
                {
                    var local = inv.slots[i];
                    serverBySlot.TryGetValue(i, out var srv);

                    // (a) Local rỗng, server có -> DELETE
                    if ((local == null || local.IsEmpty) && srv != null)
                    {
                        if (await DeleteRecord(srv.id)) changed = true;
                        continue;
                    }

                    // (b) Local có, server rỗng -> CREATE (nếu cho phép)
                    if (!(local == null || local.IsEmpty) && srv == null)
                    {
                        if (!allowCreateIfMissing) continue;
                        var itemId = local.itemData?.id; if (string.IsNullOrEmpty(itemId)) continue;
                        var newId = await PostCreate(userId, itemId, local.count, inventoryName, i);
                        if (!string.IsNullOrEmpty(newId)) changed = true;
                        continue;
                    }

                    // (c) Cả hai đều có → UPDATE khi khác nhau
                    if (!(local == null || local.IsEmpty) && srv != null)
                    {
                        var itemId = local.itemData?.id; if (string.IsNullOrEmpty(itemId)) continue;
                        bool diff = itemId != srv.itemId || local.count != srv.quantity || srv.slotIndex != i;
                        if (!diff) continue;

                        var dto = new UpdateDto
                        {
                            Id = srv.id,
                            UserId = srv.userId,
                            ItemId = itemId,
                            Quantity = local.count,
                            InventoryType = inventoryName,
                            SlotIndex = i
                        };

                        var put = await PutUpdate(dto);
                        if (put.ok) { changed = true; continue; }

                        // fallback đơn giản: xoá record cũ rồi tạo mới
                        await DeleteRecord(srv.id);
                        var created = await PostCreate(userId, itemId, local.count, inventoryName, i);
                        if (!string.IsNullOrEmpty(created)) changed = true;
                    }
                }

                if (changed && reloadAfterSync) await LoadInventory(userId, applyToLocal: true);
                return changed;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Inventory] Sync fatal: {e}");
                return false;
            }
        }

        // Cập nhật quality (giữ API)
        public async Task<bool> UpdateItemQuality(string inventoryName, int slotIndex, int newQuality, bool reloadAfterSync = true)
        {
            if (!EnsureAuthReady(out var userId)) return false;
            if (!_invByName.TryGetValue(inventoryName, out var inv)) return false;
            if (slotIndex < 0 || slotIndex >= inv.slots.Count) return false;

            var serverList = await FetchInventoryData(userId);
            var rec = serverList.FirstOrDefault(x =>
                string.Equals(x.inventoryType, inventoryName, StringComparison.OrdinalIgnoreCase) &&
                x.slotIndex == slotIndex);
            if (rec == null) return false;

            var dto = new UpdateDto
            {
                Id = rec.id,
                UserId = rec.userId,
                ItemId = rec.itemId,
                Quantity = rec.quantity,
                InventoryType = inventoryName,
                SlotIndex = slotIndex
            };

            var put = await PutUpdate(dto, newQuality);
            if (!put.ok) return false;

            if (reloadAfterSync) await LoadInventory(userId, applyToLocal: true);
            return true;
        }

        // Giữ wrapper cũ để tương thích
        public async Task LoadInventoryPublic(string userId, bool applyToLocal = true)
            => await LoadInventory(userId, applyToLocal);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace CGP.Gameplay.Inventory.Presenter
{
    public partial class InventoryManager
    {
        [Serializable]
        public struct StarterGrantLite
        {
            public string itemId;        // id item trên server (bắt buộc)
            public int quantity;      // >=1
            public string inventoryType; // "Backpack" / "Toolbar" / ...
            public int preferredSlot; // -1 = auto
        }

        /// <summary>
        /// Cấp quà tân thủ không phụ thuộc ScriptableObject tùy biến.
        /// Truyền trực tiếp danh sách itemId/qty/đích/slot, kèm marker để đảm bảo mỗi account chỉ nhận 1 lần.
        /// </summary>
        public async Task EnsureStarterPackOnFirstLogin_ByIds(
            IEnumerable<StarterGrantLite> grants,
            string markerItemId = null,            // optional: id “đánh dấu đã tặng” trên server
            string markerInventoryType = "System", // kho chứa marker
            bool reloadAfter = true)
        {
            if (!EnsureAuthReady(out var userId)) return;

            // 1) đọc snapshot
            var server = await FetchInventoryData(userId) ?? new List<InventoryItem>();

            // 2) đã có marker => đã tặng
            if (!string.IsNullOrEmpty(markerItemId))
            {
                bool hasMarker = server.Any(it =>
                    string.Equals(it.itemId, markerItemId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(it.inventoryType ?? "", markerInventoryType ?? "System", StringComparison.OrdinalIgnoreCase) &&
                    it.quantity > 0);
                if (hasMarker) { if (reloadAfter) await LoadInventory(userId, true); return; }
            }

            // 3) cấp quà
            if (grants != null)
            {
                foreach (var g in grants)
                {
                    if (string.IsNullOrEmpty(g.itemId) || g.quantity <= 0) continue;

                    string invName = string.IsNullOrEmpty(g.inventoryType) ? BACKPACK : g.inventoryType;
                    int capacity = string.Equals(invName, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? toolbarSlotsCount : backpackSlotsCount;

                    bool SlotOccupied(int idx) => server.Any(it =>
                        string.Equals(it.inventoryType, invName, StringComparison.OrdinalIgnoreCase) &&
                        it.slotIndex == idx);

                    // 3a) thử stack nếu đã có cùng item
                    var exist = server.FirstOrDefault(it =>
                        string.Equals(it.inventoryType, invName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(it.itemId, g.itemId, StringComparison.OrdinalIgnoreCase));

                    if (exist != null)
                    {
                        var dto = new UpdateDto
                        {
                            Id = exist.id,
                            UserId = exist.userId,
                            ItemId = exist.itemId,
                            Quantity = Mathf.Max(0, exist.quantity + g.quantity),
                            InventoryType = invName,
                            SlotIndex = exist.slotIndex
                        };
                        var put = await PutUpdate(dto);
                        if (put.ok) { exist.quantity = dto.Quantity; continue; }
                    }

                    // 3b) không stack được -> tìm slot
                    int slot = (g.preferredSlot >= 0 && g.preferredSlot < capacity && !SlotOccupied(g.preferredSlot))
                               ? g.preferredSlot : -1;
                    if (slot < 0)
                        for (int i = 0; i < capacity; i++) if (!SlotOccupied(i)) { slot = i; break; }

                    if (slot < 0) { Debug.LogWarning($"[StarterPack] Hết chỗ trong {invName}"); continue; }

                    var newId = await PostCreate(userId, g.itemId, g.quantity, invName, slot);
                    if (!string.IsNullOrEmpty(newId))
                        server.Add(new InventoryItem { id = newId, userId = userId, itemId = g.itemId, inventoryType = invName, slotIndex = slot, quantity = g.quantity });
                }
            }

            // 4) ghi marker
            if (!string.IsNullOrEmpty(markerItemId))
            {
                string invName = string.IsNullOrEmpty(markerInventoryType) ? "System" : markerInventoryType;
                int capacity = string.Equals(invName, TOOLBAR, StringComparison.OrdinalIgnoreCase) ? toolbarSlotsCount : backpackSlotsCount;

                bool SlotOccupied2(int idx) => server.Any(it =>
                    string.Equals(it.inventoryType, invName, StringComparison.OrdinalIgnoreCase) && it.slotIndex == idx);

                int markerSlot = -1;
                for (int i = 0; i < capacity; i++) if (!SlotOccupied2(i)) { markerSlot = i; break; }

                if (markerSlot >= 0)
                {
                    var markerId = await PostCreate(userId, markerItemId, 1, invName, markerSlot);
                    if (!string.IsNullOrEmpty(markerId))
                        server.Add(new InventoryItem { id = markerId, userId = userId, itemId = markerItemId, inventoryType = invName, slotIndex = markerSlot, quantity = 1 });
                }
            }

            // 5) reload UI
            if (reloadAfter) await LoadInventory(userId, true);
        }
    }
}

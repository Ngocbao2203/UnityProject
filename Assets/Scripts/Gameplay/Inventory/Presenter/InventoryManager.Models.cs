using System;
using System.Collections.Generic;

namespace CGP.Gameplay.Inventory.Presenter
{
    public partial class InventoryManager
    {
        // ====== API payloads ======
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

        // lỗi thường gặp từ server
        private static bool IsNotExist(HttpResult r)
            => !r.ok && (r.code == 400 || r.code == 404);

        private static bool IsOccupied(HttpResult r)
        {
            if (r.ok) return false;
            var t = (r.body ?? string.Empty).ToLowerInvariant();
            return t.Contains("đã tồn tại") || t.Contains("already") || t.Contains("occupied");
        }
    }
}

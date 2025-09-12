using System;

namespace CGP.Networking.DTOs
{
    [Serializable]
    public class InventoryItemDto
    {
        public string id;
        public string userId;
        public string itemId;
        public int quantity;
        public string inventoryType;
        public int slotIndex;
        public string acquiredAt;
    }
}

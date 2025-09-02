using System;
using System.Collections.Generic;

[Serializable]
public class InventoryResponse
{
    public int error;
    public string message;
    public InventoryItem[] data;
    public int count; // nếu API không trả, cũng không sao
}

[Serializable]
public class InventoryItem
{
    public string id;             // id bản ghi inventory (nếu BE trả)
    public string userId;

    // QUAN TRỌNG: đúng case "itemId" như JSON để JsonUtility map được
    public string itemId;

    public string itemType;       // nhãn/loại (nếu cần hiển thị text)
    public int quantity;

    // tuỳ API có/không:
    public string inventoryType;  // "Backpack" / "Toolbar"
    public int slotIndex;

    public string acquiredAt;
    public string creationDate;
    public string modificationDate;
}

[Serializable]
public class InventorySlotRequest
{
    // Giữ cả 2 cho tương thích BE cũ: cả "id" và "itemId" đều mang GUID của Item
    public string id;         // legacy
    public string itemId;     // chuẩn dùng
    public string userId;
    public string itemType;   // nếu BE còn dùng
    public int quantity;
}

[Serializable]
public class InventoryUpdateRequest
{
    public string userId;
    public List<InventorySlotRequest> slots;
}

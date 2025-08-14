using System;

[Serializable]
public class InventoryResponse
{
    public int error;
    public string message;
    public InventoryItem[] data;
    public int count;
}

[Serializable]
public class InventoryItem
{
    public string id;
    public string userId;
    public string itemType;
    public int quantity;
    public string acquiredAt;
}

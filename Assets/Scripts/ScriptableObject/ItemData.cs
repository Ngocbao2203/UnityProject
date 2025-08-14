using UnityEngine;

[CreateAssetMenu(fileName = "Item Data", menuName = "ItemData", order = 50)]
public class ItemData : ScriptableObject
{
    public string id = System.Guid.NewGuid().ToString(); // Thêm ID duy nhất (ví dụ: GUID)
    public string itemName = "Item Name";
    public Sprite icon;
    public GameObject cropPrefab;
    public ItemType itemType = ItemType.Other; // Thêm thuộc tính để phân loại

    public enum ItemType
    {
        Seed, // Hạt giống
        Tool, // Công cụ
        Other // Loại khác (nếu cần)
    }
}
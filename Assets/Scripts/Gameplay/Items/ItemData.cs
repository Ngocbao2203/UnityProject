using UnityEngine;
using CGP.Gameplay.Items;

namespace CGP.Gameplay.Items
{
    [CreateAssetMenu(fileName = "Item Data", menuName = "ItemData", order = 50)]
    public class ItemData : ScriptableObject
    {
        public string id = System.Guid.NewGuid().ToString(); // GUID duy nhất
        public string itemName = "Item Name";
        [TextArea] public string description;                // 🆕 mô tả item
        public Sprite icon;
        public GameObject cropPrefab;
        public ItemType itemType = ItemType.Other;
        public bool isStackable = true;                      // 🆕 có cho phép stack không

        public enum ItemType
        {
            Seed,
            Tool,
            Crop,
            Other
        }
    }
}

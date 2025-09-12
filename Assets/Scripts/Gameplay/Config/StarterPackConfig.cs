using System;
using System.Collections.Generic;
using UnityEngine;
using CGP.Gameplay.Items;

namespace CGP.Gameplay.Config
{
    [CreateAssetMenu(menuName = "Game/Starter Pack", fileName = "StarterPack")]
    public class StarterPackConfig : ScriptableObject
    {
        [Serializable]
        public class Grant
        {
            [Tooltip("ItemData có trường id (dùng đồng bộ với server)")]
            public ItemData item;

            [Min(1)] public int quantity = 1;

            [Tooltip("Backpack / Toolbar / ... (khớp với server)")]
            public string inventoryType = "Backpack";

            [Tooltip("-1 = tự tìm slot trống; >=0 = cố gắng đặt vào slot này")]
            public int preferredSlot = -1;
        }

        [Header("Danh sách quà")]
        public List<Grant> items = new();

        [Header("Marker (để đảm bảo chỉ cấp 1 lần)")]
        [Tooltip("ItemId đặc biệt đã seed sẵn trong DB; không hiện lên UI")]
        public string markerItemId = "00000000-0000-0000-0000-00000000C0DE";

        [Tooltip("InventoryType dùng chứa marker (không hiển thị lên UI)")]
        public string markerInventoryType = "System";
    }
}

using UnityEngine;
using CGP.Gameplay.Items;            // dùng ItemData
using CGP.Networking.Clients;        // gọi API
using CGP.Networking.DTOs;

namespace CGP.Gameplay.Shop
{

    [CreateAssetMenu(fileName = "ProductData", menuName = "Shop/Product")]
    public class ProductData : ScriptableObject
    {
        public string productName;
        public int price;
        public Sprite icon;
        public ItemData itemData;
    }

}
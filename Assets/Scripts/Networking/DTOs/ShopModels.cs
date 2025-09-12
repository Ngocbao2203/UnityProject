using System;

namespace CGP.Networking.DTOs
{
    [Serializable]
    public class ShopPriceDto
    {
        public string itemId;
        public string itemName;
        public int sellPrice;
        public string iconUrl;
    }
}

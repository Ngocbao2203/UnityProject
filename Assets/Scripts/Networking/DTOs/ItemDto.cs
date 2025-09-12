using System;

namespace CGP.Networking.DTOs
{
    [Serializable]
    public class ItemDto
    {
        public string id;
        public string nameItem;
        public string description;
        public string itemType;
        public bool isStackable;
        // thêm field khác nếu BE có, ví dụ: public string iconUrl;
    }
}

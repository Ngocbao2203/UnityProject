using CGP.Framework;

namespace CGP.Framework
{
    public static class ApiRoutes
    {
        public const string BASE_URL = "https://cgpwebapi20250901190829-a7a8haa8bmecbtdm.southeastasia-01.azurewebsites.net";

        // ===== Auth =====
        public static class Auth
        {
            public const string LOGIN = BASE_URL + "/api/Auth/user/login";
            public const string GET_CURRENT_USER = BASE_URL + "/api/User/GetCurrentUser";
        }

        // ===== Inventory =====
        public static class Inventory
        {
            public const string GET_BY_USERID = BASE_URL + "/api/Inventory/GetInventoryByUserId/{userId}";
            public const string GET_BY_ID = BASE_URL + "/api/Inventory/GetInventoryById/{inventoryId}";
            public const string ADD_ITEM_TO_INVENTORY = BASE_URL + "/api/Inventory/AddItemToInventory";
            public const string UPDATE_ITEM = BASE_URL + "/api/Inventory/UpdateInventory";
            public const string DELETE_ITEM = BASE_URL + "/api/Inventory/DeleteInventory/{inventoryId}";
        }

        // ===== Item =====
        public static class Item
        {
            public const string GET_ALL = BASE_URL + "/api/Item/GetAllItems";
            public const string GET_BY_ID = BASE_URL + "/api/Item/GetItemById/{id}";
            public const string CREATE = BASE_URL + "/api/Item/CreateItem";
            public const string UPDATE = BASE_URL + "/api/Item/UpdateItem";
            public const string DELETE = BASE_URL + "/api/Item/DeleteItem/{id}";
        }

        // ===== Farmland =====
        public static class Farmland
        {
            public const string GET_FARMLANDS = BASE_URL + "/api/Farmland/GetFarmlands/{userId}";
            public const string PLOW = BASE_URL + "/api/Farmland/Plow";
            public const string PLANT = BASE_URL + "/api/Farmland/Plant";
            public const string WATER = BASE_URL + "/api/Farmland/Water";
            public const string HARVEST = BASE_URL + "/api/Farmland/Harvest";
        }

        // ===== Point =====
        public static class Point
        {
            public const string GET_BY_USERID = BASE_URL + "/api/Point/GetPointsByUserId/{userId}";
        }

        // ===== ShopPrice =====
        public static class ShopPrice
        {
            // Read
            public const string GET_ITEMS_SELL = BASE_URL + "/api/ShopPrice/GetItemsSell";
            public const string GET_ITEMS_IN_BACKPACK = BASE_URL + "/api/ShopPrice/GetItemsInBackpackByUserId/{userId}";
            public const string GET_ITEM_IN_SHOP = BASE_URL + "/api/ShopPrice/GetItemInShop/{id}";

            // Manage catalog
            public const string ADD_ITEM_TO_SHOP = BASE_URL + "/api/ShopPrice/AddItemToShop";          // POST (multipart/form-data): ItemId, Price
            public const string UPDATE_ITEM_IN_SHOP = BASE_URL + "/api/ShopPrice/UpdateItemInShop";       // PUT (multipart/form-data): Id, Price
            public const string REMOVE_ITEM_IN_SHOP = BASE_URL + "/api/ShopPrice/RemoveItemInShop/{id}";  // DELETE

            // Sell transaction
            public const string SELL_ITEM = BASE_URL + "/api/ShopPrice/SellItem";               // POST (multipart/form-data): UserId, ItemId, Quantity
        }

        public static class Quest
        {
            public const string CREATE_QUEST = BASE_URL + "/api/Quest/CreateQuest";

            // Lấy toàn bộ Quest gốc (metadata)
            public const string GET_ALL_QUESTS = BASE_URL + "/api/Quest/GetAllQuests";

            // Lấy trạng thái UserQuest
            public const string GET_USER_QUESTS = BASE_URL + "/api/UserQuest/GetUserQuests?userId={userId}";

            // Claim reward (truyền userQuestId)
            public const string COMPLETE_QUEST = BASE_URL + "/api/UserQuest/CompleteQuest";
        }
    }
}

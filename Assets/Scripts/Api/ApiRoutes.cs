public static class ApiRoutes
{
    public const string BASE_URL = "https://localhost:7254";

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
}

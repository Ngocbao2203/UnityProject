public static class ApiRoutes
{
    public const string BASE_URL = "https://localhost:7254";

    // Auth
    public const string LOGIN = BASE_URL + "/api/Auth/user/login";
    public const string GET_CURRENT_USER = BASE_URL + "/api/User/GetCurrentUser";

    // Inventory
    public const string GET_INVENTORY_BY_USERID = BASE_URL + "/api/Inventory/GetInventoryByUserId/{userId}";
    public const string GET_INVENTORY_BY_ID = BASE_URL + "/api/Inventory/GetInventoryById/{inventoryId}";
    public const string ADD_ITEM = BASE_URL + "/api/Inventory/AddToInventory";
    public const string UPDATE_ITEM = BASE_URL + "/api/Inventory/UpdateInventory";
    public const string DELETE_ITEM = BASE_URL + "/api/Inventory/DeleteInventory/{inventoryId}";
}

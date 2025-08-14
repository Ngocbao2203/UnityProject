using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class OneTimePickup : MonoBehaviour
{
    [Tooltip("Key dùng trong ItemManager để lấy Item (ví dụ: 'CarrotSeed_Starter')")]
    public string itemID;

    [Tooltip("Tên inventory muốn thêm vào (Backpack/Toolbar). Mặc định Backpack.")]
    public string targetInventoryName = InventoryManager.BACKPACK;

    private ItemManager itemManager;
    private string userScopedKey; // userId + itemID để đảm bảo mỗi acc chỉ nhặt 1 lần

    void Awake()
    {
        // Dùng API mới thay cho FindObjectOfType (tránh warning)
        itemManager = FindFirstObjectByType<ItemManager>();
        if (itemManager == null)
        {
            Debug.LogError("OneTimePickup: Không tìm thấy ItemManager trong scene.");
        }
    }

    void Start()
    {
        // Ghép key theo user để “mỗi acc chỉ nhặt 1 lần”
        string userId = AuthManager.Instance != null ? AuthManager.Instance.GetCurrentUserId() : "";
        userScopedKey = string.IsNullOrEmpty(userId) ? itemID : $"{userId}:{itemID}";

        // Nếu đã nhặt rồi thì ẩn luôn
        if (PlayerPrefs.GetInt(userScopedKey, 0) == 1)
        {
            gameObject.SetActive(false);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (InventoryManager.Instance == null)
        {
            Debug.LogError("OneTimePickup: Không có InventoryManager.Instance.");
            return;
        }
        if (itemManager == null)
        {
            Debug.LogError("OneTimePickup: Không có ItemManager.");
            return;
        }

        // Lấy Item (prefab/data) từ ItemManager theo itemID
        Item item = itemManager.GetItemByName(itemID);
        if (item == null)
        {
            Debug.LogWarning($"OneTimePickup: Không tìm thấy Item với key '{itemID}' trong ItemManager.");
            return;
        }

        // ✅ GỌI ĐÚNG CHỮ KÝ: (string inventoryName, Item item)
        InventoryManager.Instance.AddItem(targetInventoryName, item);

        // Đánh dấu là đã nhặt (theo acc)
        PlayerPrefs.SetInt(userScopedKey, 1);
        PlayerPrefs.Save();

        // Xóa/ẩn vật phẩm ngoài map
        Destroy(gameObject);
    }
}

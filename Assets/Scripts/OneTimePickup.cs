using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class OneTimePickup : MonoBehaviour
{
    [Tooltip("Key dùng trong ItemManager để lấy Item (ví dụ: 'CarrotSeed_Starter')")]
    public string itemID;

    [Tooltip("Số lượng thêm vào mỗi lần nhặt")]
    [Min(1)] public int amount = 1;

    [Tooltip("Tên inventory muốn thêm vào (Backpack/Toolbar). Mặc định Backpack.")]
    public string targetInventoryName = InventoryManager.BACKPACK;

    private ItemManager itemManager;
    private string userScopedKey; // userId + itemID
    private bool picked = false;  // chống double-trigger 1 frame

    private void Reset()
    {
        // đảm bảo collider là trigger
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void Awake()
    {
        itemManager = FindFirstObjectByType<ItemManager>();
        if (itemManager == null)
            Debug.LogError("[OneTimePickup] Không tìm thấy ItemManager trong scene.");
    }

    private void Start()
    {
        // Đợi AuthManager sẵn sàng (nếu cần)
        string userId = AuthManager.Instance != null ? AuthManager.Instance.GetCurrentUserId() : "";
        userScopedKey = string.IsNullOrEmpty(userId) ? itemID : $"{userId}:{itemID}";

        if (PlayerPrefs.GetInt(userScopedKey, 0) == 1)
            gameObject.SetActive(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (picked) return;
        if (!other.CompareTag("Player")) return;

        if (InventoryManager.Instance == null)
        {
            Debug.LogError("[OneTimePickup] Không có InventoryManager.Instance.");
            return;
        }
        if (itemManager == null)
        {
            Debug.LogError("[OneTimePickup] Không có ItemManager.");
            return;
        }

        var item = itemManager.GetItemByName(itemID);
        if (item == null)
        {
            Debug.LogWarning($"[OneTimePickup] Không tìm thấy Item với key '{itemID}' trong ItemManager.");
            return;
        }

        picked = true;

        // Nếu AddItem không hỗ trợ amount, lặp amount lần
        for (int i = 0; i < Mathf.Max(1, amount); i++)
            InventoryManager.Instance.AddItem(targetInventoryName, item);

        PlayerPrefs.SetInt(userScopedKey, 1);
        PlayerPrefs.Save();

        // TODO: SFX/VFX nếu muốn
        // AudioSource.PlayClipAtPoint(pickSound, transform.position);

        Destroy(gameObject);
    }
}

using System.Collections.Generic;
using UnityEngine;

public class ShopManager : MonoBehaviour
{
    [Header("UI Settings")]
    public GameObject productPrefab;      // Prefab cho mỗi sản phẩm
    public Transform contentPanel;        // Panel chứa các sản phẩm

    public GameObject shopUI;             // UI gốc của cửa hàng

    [Header("Product Data")]
    public List<ProductData> itemList;    // Danh sách sản phẩm (ScriptableObjects)

    private bool isShopOpen = false;      // Trạng thái shop

    void Start()
    {
        PopulateShop();                   // Tạo các item UI khi khởi động
        shopUI.SetActive(false);          // Ẩn UI shop ban đầu
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            ToggleShop();                 // Bật/tắt shop khi nhấn C
        }
    }

    void ToggleShop()
    {
        isShopOpen = !isShopOpen;
        shopUI.SetActive(isShopOpen);
    }

    void PopulateShop()
    {
        foreach (Transform child in contentPanel)
        {
            Destroy(child.gameObject);
        }

        foreach (ProductData item in itemList)
        {
            GameObject obj = Instantiate(productPrefab, contentPanel); // Tạo UI item mới
            Debug.Log($"Created UI for: {item.productName}");
            Product_UI ui = obj.GetComponent<Product_UI>();
            ui.Setup(item, this); // Gửi dữ liệu và tham chiếu ShopManager vào
        }
    }

    public void SellItem(ProductData item)
    {
        Inventory backpack = InventoryManager.Instance.GetInventoryByName("Backpack");

        // Tìm slot có item để xóa
        for (int i = 0; i < backpack.slots.Count; i++)
        {
            var slot = backpack.slots[i];

            if (slot != null && slot.itemData == item.itemData && slot.count > 0)
            {
                // Bán thành công
                CurrencyManager.Instance.AddCoins(item.price);
                backpack.Remove(i); // 👉 TRỪ item ở vị trí i
                Debug.Log($"✅ Đã bán {item.productName}, +{item.price}$");
                return;
            }
        }

        Debug.LogWarning($"❌ Không có {item.productName} trong Backpack để bán!");
    }
}

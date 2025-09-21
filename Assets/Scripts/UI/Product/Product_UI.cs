using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CGP.Gameplay.Inventory.Presenter; // để lấy qty theo itemId

namespace CGP.Gameplay.Shop
{
    public class Product_UI : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI priceText;
        [SerializeField] private TextMeshProUGUI ownedText;   // (tuỳ chọn) “xN” đang có
        [SerializeField] private Button sellButton;

        private ProductData currentItem;
        private ShopManager shopManager;

        private void Awake()
        {
            if (!sellButton) sellButton = GetComponentInChildren<Button>(true);
        }

        private void OnDestroy()
        {
            if (sellButton) sellButton.onClick.RemoveAllListeners();
        }

        public void Setup(ProductData item, ShopManager manager)
        {
            currentItem = item;
            shopManager = manager;

            if (iconImage)
            {
                iconImage.sprite = item ? item.icon : null;
                iconImage.enabled = iconImage.sprite != null;
            }

            if (nameText) nameText.text = item ? item.productName : "(Unknown)";
            if (priceText) priceText.text = item ? (item.price.ToString() + "$") : "-";

            // (tuỳ chọn) hiện số lượng đang có trong kho
            if (ownedText && item && item.itemData)
            {
                var itemId = item.itemData.id;
                int owned = InventoryManager.Instance ? InventoryManager.Instance.GetQuantityByItemId(itemId) : 0;
                ownedText.text = owned > 0 ? $"x{owned}" : "x0";
            }

            if (sellButton)
            {
                sellButton.onClick.RemoveAllListeners();
                sellButton.onClick.AddListener(OnClickSell);
                sellButton.interactable = (item != null && manager != null);
            }
        }

        private void OnClickSell()
        {
            if (!shopManager || !currentItem)
            {
                Debug.LogWarning("[Product_UI] Cannot sell: missing shopManager or currentItem");
                return;
            }

            // Mở popup chọn số lượng (thay vì bán 1 cái luôn)
            shopManager.OpenSellDialog(currentItem);
        }
    }
}

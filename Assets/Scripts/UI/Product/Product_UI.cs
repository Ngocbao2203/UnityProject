using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CGP.Gameplay.Shop
{
    public class Product_UI : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI priceText;
        [SerializeField] private Button sellButton;

        private ProductData currentItem;
        private ShopManager shopManager;

        private void Awake()
        {
            // Fallback nếu quên kéo trong Inspector
            if (iconImage == null) iconImage = GetComponentInChildren<Image>(true);
            if (nameText == null) nameText = GetComponentInChildren<TextMeshProUGUI>(true);
            if (priceText == null) priceText = GetComponentsInChildren<TextMeshProUGUI>(true).Length > 1
                                                ? GetComponentsInChildren<TextMeshProUGUI>(true)[1]
                                                : priceText;
            if (sellButton == null) sellButton = GetComponentInChildren<Button>(true);
        }

        private void OnDestroy()
        {
            if (sellButton != null) sellButton.onClick.RemoveListener(SellItem);
        }

        public void Setup(ProductData item, ShopManager manager)
        {
            currentItem = item;
            shopManager = manager;

            if (iconImage != null)
            {
                iconImage.sprite = item != null ? item.icon : null;
                iconImage.enabled = (iconImage.sprite != null);
            }

            if (nameText != null)
                nameText.text = item != null ? item.productName : "(Unknown)";

            if (priceText != null)
                priceText.text = item != null ? (item.price.ToString() + "$") : "-";

            if (sellButton != null)
            {
                sellButton.onClick.RemoveAllListeners();
                sellButton.onClick.AddListener(SellItem);
                sellButton.interactable = (item != null && manager != null);
            }
        }

        private void SellItem()
        {
            if (shopManager == null || currentItem == null)
            {
                Debug.LogWarning("[Product_UI] Cannot sell: missing shopManager or currentItem");
                return;
            }
            shopManager.SellItem(currentItem);
        }
    }
}

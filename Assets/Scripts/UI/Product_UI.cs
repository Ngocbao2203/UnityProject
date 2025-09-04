using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Product_UI : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI priceText;
    public Button sellButton;

    private ProductData currentItem;

    private ShopManager shopManager;

    public void Setup(ProductData item, ShopManager manager)
    {
        currentItem = item;
        shopManager = manager;

        iconImage.sprite = item.icon;
        nameText.text = item.productName;
        priceText.text = item.price + "$";

        sellButton.onClick.RemoveAllListeners();          // <-- thêm
        sellButton.onClick.AddListener(SellItem);
    }

    void SellItem()
    {
        shopManager.SellItem(currentItem);
    }
}

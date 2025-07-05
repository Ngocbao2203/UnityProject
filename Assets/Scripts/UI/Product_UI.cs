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
        //Debug.Log($"Setting up: {item.productName}, {item.price}, Icon: {item.icon}");

        iconImage.sprite = item.icon;
        //if (iconImage.sprite == null)
        //    Debug.LogWarning("❌ iconImage.sprite is NULL!");
        nameText.text = item.productName;
        //Debug.Log($"Name set to: {nameText.text}");
        priceText.text = item.price.ToString() + "$";
        //Debug.Log($"Price set to: {priceText.text}");

        sellButton.onClick.AddListener(SellItem);
    }

    void SellItem()
    {
        shopManager.SellItem(currentItem);
    }
}

using UnityEngine;

[CreateAssetMenu(fileName = "ProductData", menuName = "Shop/Product")]
public class ProductData : ScriptableObject
{
    public string productName;
    public int price;
    public Sprite icon;
    public ItemData itemData;
}

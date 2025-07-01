using UnityEngine;

[CreateAssetMenu(fileName = "Item Data", menuName = "ItemData", order = 50)]
public class ItemData : ScriptableObject
{
    public string itemName = "Item Name";
    public Sprite icon;
    public GameObject cropPrefab;
}

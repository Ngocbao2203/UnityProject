using UnityEngine;
using UnityEngine.UI;
using TMPro; // Import TextMeshPro namespace for text components
public class Slot_UI : MonoBehaviour
{
    public Image itemIcon; // Reference to the UI Image component for the item icon
    public TextMeshProUGUI quantityText; // Reference to the UI Text component for displaying item count

    public void SetItem(Inventory.Slot slot)
    {
        if(slot != null)
        {
            itemIcon.sprite = slot.icon; // Set the item icon sprite
            itemIcon.color = new Color(1, 1, 1, 1); // Set the icon color to fully opaque
            quantityText.text = slot.count.ToString(); // Set the text to display the item count
        }    
    }
    public void SetEmpty()
    {
        itemIcon.sprite = null; // Clear the item icon sprite
        itemIcon.color = new Color(1, 1, 1, 0); // Set the icon color to fully transparent
        quantityText.text = ""; // Clear the text for item count
    }
}

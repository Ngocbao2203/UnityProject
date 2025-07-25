using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Slot_UI : MonoBehaviour
{
    public int slotID = -1;
    public Image itemIcon;
    public TextMeshProUGUI quantityText;
    public GameObject highlight;

    public Inventory inventory;

    public void SetItem(Inventory.Slot slot)
    {
        itemIcon.sprite = slot.icon;
        itemIcon.color = new Color(1, 1, 1, 1);
        quantityText.text = slot.count.ToString();
    }

    public void SetEmpty()
    {
        itemIcon.sprite = null;
        itemIcon.color = new Color(1, 1, 1, 0);
        quantityText.text = "";
    }

    public void SetHighlight(bool isOn)
    {
        if (highlight != null)
        {
            highlight.SetActive(isOn);
        }
    }

    // Thêm phương thức để lấy slot từ inventory
    public Inventory.Slot GetSlot()
    {
        if (inventory != null && slotID >= 0 && slotID < inventory.slots.Count)
        {
            return inventory.slots[slotID];
        }
        Debug.LogWarning($"GetSlot failed for slotID {slotID}, inventory may be null or invalid");
        return null;
    }

    // Thêm phương thức để cập nhật giao diện
    public void UpdateSlotUI()
    {
        if (inventory != null && slotID >= 0 && slotID < inventory.slots.Count)
        {
            Inventory.Slot slot = inventory.slots[slotID];
            if (slot.IsEmpty)
            {
                SetEmpty();
            }
            else
            {
                SetItem(slot);
                Debug.Log($"Updated slot {slotID}: {slot.itemName}, Count: {slot.count}");
            }
        }
        else
        {
            Debug.LogError($"Invalid slotID {slotID} or inventory null for Slot_UI");
        }
    }
}
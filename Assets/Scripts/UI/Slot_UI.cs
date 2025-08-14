using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class Slot_UI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDropHandler
{
    public int slotID = -1;
    [SerializeField] public Image itemIcon; // Đảm bảo public
    [SerializeField] public TextMeshProUGUI quantityText; // Đảm bảo public
    [SerializeField] public GameObject highlight; // Đảm bảo public
    [SerializeField] public Inventory inventory; // Đảm bảo public

    private void Awake()
    {
        if (itemIcon == null) Debug.LogError($"ItemIcon not assigned for Slot_UI on {gameObject.name}");
        if (quantityText == null) Debug.LogError($"QuantityText not assigned for Slot_UI on {gameObject.name}");
        if (highlight == null) Debug.LogWarning($"Highlight not assigned for Slot_UI on {gameObject.name}, functionality may be limited");
        if (inventory == null) Debug.LogError($"Inventory not assigned for Slot_UI on {gameObject.name}");
    }

    public void SetItem(Inventory.Slot slot)
    {
        if (itemIcon != null && quantityText != null && slot != null)
        {
            itemIcon.sprite = slot.icon;
            itemIcon.color = new Color(1, 1, 1, 1);
            quantityText.text = slot.count > 0 ? slot.count.ToString() : "";
        }
        else
        {
            Debug.LogWarning($"Cannot set item for slot {slotID} due to null references");
        }
    }

    public void SetEmpty()
    {
        if (itemIcon != null && quantityText != null)
        {
            itemIcon.sprite = null;
            itemIcon.color = new Color(1, 1, 1, 0);
            quantityText.text = "";
        }
    }

    public void SetHighlight(bool isOn)
    {
        if (highlight != null)
        {
            highlight.SetActive(isOn);
        }
    }

    public Inventory.Slot GetSlot()
    {
        if (inventory != null && slotID >= 0 && slotID < inventory.slots.Count)
        {
            return inventory.slots[slotID];
        }
        //Debug.LogWarning($"GetSlot failed for slotID {slotID}, inventory may be null or invalid");
        return null;
    }

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
                //Debug.Log($"Updated slot {slotID}: {slot.itemName}, Count: {slot.count}");
            }
        }
        else
        {
            //Debug.LogError($"Invalid slotID {slotID} or inventory null for Slot_UI on {gameObject.name}");
            SetEmpty();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (inventory != null && !GetSlot().IsEmpty)
        {
            Inventory_UI inventoryUI = GetComponentInParent<Inventory_UI>();
            if (inventoryUI != null)
            {
                inventoryUI.SlotBeginDrag(this);
            }
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Inventory_UI inventoryUI = GetComponentInParent<Inventory_UI>();
        if (inventoryUI != null)
        {
            inventoryUI.SlotEndDrag();
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        Inventory_UI inventoryUI = GetComponentInParent<Inventory_UI>();
        if (inventoryUI != null)
        {
            inventoryUI.SlotDrop(this);
        }
    }
}
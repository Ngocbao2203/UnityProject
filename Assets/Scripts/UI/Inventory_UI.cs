using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Inventory_UI : MonoBehaviour
{
    public string inventoryName;
    public List<Slot_UI> slots = new List<Slot_UI>();
    public Canvas canvas;
    private Inventory inventory;

    private void Start()
    {
        canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("Canvas not found in scene!");
            return;
        }

        if (GameManager.instance == null || GameManager.instance.player == null || GameManager.instance.player.inventoryManager == null)
        {
            Debug.LogError("GameManager or inventoryManager not initialized!");
            return;
        }

        inventory = GameManager.instance.player.inventoryManager.GetInventoryByName(inventoryName);
        if (inventory == null)
        {
            Debug.LogError($"Inventory with name {inventoryName} not found!");
            return;
        }

        SetupSlots();
        Refresh();

        // Lắng nghe sự kiện từ InventoryManager
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnInventoryLoaded += Refresh;
        }
    }

    private void OnDestroy()
    {
        // Hủy đăng ký sự kiện khi object bị hủy
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnInventoryLoaded -= Refresh;
        }
    }

    public void Refresh()
    {
        if (inventory == null || slots == null) return;

        //Debug.Log($"Refreshing {inventoryName} with {inventory.slots.Count} slots");
        if (slots.Count != inventory.slots.Count)
        {
            AdjustSlotCount(inventory.slots.Count);
        }

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].slotID < 0)
            {
                slots[i].slotID = i;
            }
            slots[i].UpdateSlotUI();
            //Debug.Log($"Slot {i} updated: {inventory.slots[i].itemName}, Count: {inventory.slots[i].count}");
        }
    }

    private void AdjustSlotCount(int targetCount)
    {
        while (slots.Count < targetCount)
        {
            GameObject newSlot = new GameObject($"Slot_{slots.Count}");
            newSlot.transform.SetParent(transform, false);
            Slot_UI slotUI = newSlot.AddComponent<Slot_UI>();
            slotUI.inventory = inventory;
            slots.Add(slotUI);
        }
        while (slots.Count > targetCount)
        {
            Slot_UI slot = slots[slots.Count - 1];
            slots.RemoveAt(slots.Count - 1);
            Destroy(slot.gameObject);
        }
    }

    public void Remove()
    {
        if (UI_Manager.draggedSlot == null || inventory == null) return;

        string itemName = inventory.slots[UI_Manager.draggedSlot.slotID].itemName;
        Item itemToDrop = GameManager.instance.itemManager.GetItemByName(itemName);
        if (itemToDrop != null)
        {
            if (UI_Manager.dragSingle)
            {
                GameManager.instance.player.DropItem(itemToDrop);
                inventory.Remove(UI_Manager.draggedSlot.slotID);
            }
            else
            {
                int count = inventory.slots[UI_Manager.draggedSlot.slotID].count;
                GameManager.instance.player.DropItem(itemToDrop, count);
                inventory.Remove(UI_Manager.draggedSlot.slotID, count);
            }
            StartCoroutine(SyncAfterRemove(UI_Manager.draggedSlot.slotID));
            Refresh();
        }
        UI_Manager.draggedSlot = null;
    }

    private IEnumerator SyncAfterRemove(int slotIndex)
    {
        yield return new WaitForSeconds(0.1f);
        InventoryManager.Instance.SyncInventory(inventoryName).ConfigureAwait(false);
    }

    public void SlotBeginDrag(Slot_UI slot)
    {
        if (slot == null || slot.inventory.slots[slot.slotID].IsEmpty) return;

        // Kiểm tra xem có đang drag rồi không để tránh duplicate calls
        if (UI_Manager.draggedSlot != null)
        {
            Debug.Log("Already dragging, ignoring duplicate call");
            return;
        }

        // Thông báo cho InventoryManager rằng đang bắt đầu drag
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.SetDragState(true);
        }

        UI_Manager.draggedSlot = slot;
        UI_Manager.draggedIcon = Instantiate(slot.itemIcon);
        UI_Manager.draggedIcon.transform.SetParent(canvas.transform);
        UI_Manager.draggedIcon.raycastTarget = false;
        UI_Manager.draggedIcon.rectTransform.sizeDelta = new Vector2(50, 50);
        MoveToMousePosition(UI_Manager.draggedIcon.gameObject);

        Debug.Log($"Started dragging item: {slot.inventory.slots[slot.slotID].itemName}");
    }

    public void SlotDrag()
    {
        if (UI_Manager.draggedSlot != null && UI_Manager.draggedIcon != null)
        {
            MoveToMousePosition(UI_Manager.draggedIcon.gameObject);
        }
    }

    public void SlotEndDrag()
    {
        Debug.Log("Ending drag operation");

        // Đảm bảo cleanup được gọi
        if (UI_Manager.draggedIcon != null)
        {
            Destroy(UI_Manager.draggedIcon.gameObject);
            UI_Manager.draggedIcon = null;
        }

        // Chỉ reset drag state và draggedSlot sau một khoảng thời gian ngắn
        // để đảm bảo SlotDrop có thể hoàn thành trước
        StartCoroutine(DelayedDragCleanup());
    }

    private IEnumerator DelayedDragCleanup()
    {
        yield return new WaitForEndOfFrame();

        // Thông báo cho InventoryManager rằng đã kết thúc drag
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.SetDragState(false);
        }

        // Reset dragged slot nếu không có drop thành công
        if (UI_Manager.draggedSlot != null)
        {
            Debug.Log("Drag ended without successful drop - resetting");
            UI_Manager.draggedSlot = null;
        }
    }

    public async void SlotDrop(Slot_UI slot)
    {
        if (UI_Manager.draggedSlot == null || slot == null)
        {
            Debug.Log("Invalid drop operation");
            return;
        }

        Debug.Log($"Dropping item from slot {UI_Manager.draggedSlot.slotID} to slot {slot.slotID}");

        // Sử dụng MoveItem từ InventoryManager thay vì MoveSlot cũ
        string fromInventoryName = GetInventoryNameFromSlot(UI_Manager.draggedSlot);
        string toInventoryName = GetInventoryNameFromSlot(slot);

        Debug.Log($"From inventory: {fromInventoryName}, To inventory: {toInventoryName}");
        Debug.Log($"From slot: {UI_Manager.draggedSlot.slotID}, To slot: {slot.slotID}");
        Debug.Log($"IsDragging: {InventoryManager.Instance.IsDragging()}, IsSyncing: {InventoryManager.Instance.IsSyncing()}");

        // Tạm thời set drag state về false trước khi move
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.SetDragState(false);
        }

        bool success = await InventoryManager.Instance.MoveItem(
            fromInventoryName,
            UI_Manager.draggedSlot.slotID,
            toInventoryName,
            slot.slotID
        );

        if (success)
        {
            Debug.Log("Item moved successfully");
            GameManager.instance.uiManager.RefreshAll();
        }
        else
        {
            Debug.Log("Failed to move item - checking inventories...");
            var fromInv = InventoryManager.Instance.GetInventoryByName(fromInventoryName);
            var toInv = InventoryManager.Instance.GetInventoryByName(toInventoryName);
            Debug.Log($"From inventory exists: {fromInv != null}, slots count: {fromInv?.slots?.Count}");
            Debug.Log($"To inventory exists: {toInv != null}, slots count: {toInv?.slots?.Count}");
            if (fromInv != null && UI_Manager.draggedSlot.slotID < fromInv.slots.Count)
            {
                Debug.Log($"From slot item: {fromInv.slots[UI_Manager.draggedSlot.slotID].itemName}, count: {fromInv.slots[UI_Manager.draggedSlot.slotID].count}");
            }
        }

        // Reset drag state
        UI_Manager.draggedSlot = null;
    }

    private string GetInventoryNameFromSlot(Slot_UI slot)
    {
        // Tìm inventory name từ slot
        Inventory_UI[] inventoryUIs = FindObjectsByType<Inventory_UI>(FindObjectsSortMode.None);
        foreach (var invUI in inventoryUIs)
        {
            if (invUI.slots.Contains(slot))
            {
                return invUI.inventoryName;
            }
        }
        return this.inventoryName; // fallback
    }

    private void MoveToMousePosition(GameObject toMove)
    {
        if (canvas != null && toMove != null)
        {
            Vector2 position;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas.transform as RectTransform, Input.mousePosition, null, out position);
            toMove.transform.position = canvas.transform.TransformPoint(position);
        }
    }

    private void SetupSlots()
    {
        int counter = 0;
        foreach (Slot_UI slot in slots)
        {
            slot.slotID = counter;
            slot.inventory = inventory;
            counter++;
        }
    }
}
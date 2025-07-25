using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Toolbar_UI : MonoBehaviour
{
    public List<Slot_UI> toolbarSlots = new List<Slot_UI>(); // Danh sách các Slot_UI cho toolbar slots
    public Slot_UI selectedSlot; // Biến để lưu slot được chọn, công khai cho Player truy cập

    private void Start()
    {
        // Gán slotID và inventory cho từng Slot_UI
        for (int i = 0; i < toolbarSlots.Count; i++)
        {
            toolbarSlots[i].slotID = i;
            toolbarSlots[i].inventory = GameManager.instance.player.inventoryManager.toolbar;
        }
        SelectSlot(0); // Chọn slot đầu tiên khi khởi động
    }

    private void Update()
    {
        CheckAlphaNumericKeys(); // Kiểm tra phím số để chọn slot
        CheckUseItem(); // Kiểm tra phím Space để kích hoạt hành động
    }

    public void SelectSlot(int index)
    {
        if (toolbarSlots.Count == 7 && index >= 0 && index < toolbarSlots.Count) // Kiểm tra chỉ số hợp lệ
        {
            if (selectedSlot != null)
            {
                selectedSlot.SetHighlight(false); // Tắt highlight slot cũ
            }
            selectedSlot = toolbarSlots[index];
            selectedSlot.SetHighlight(true); // Bật highlight slot mới

            GameManager.instance.player.inventoryManager.toolbar.SelectSlot(index); // Cập nhật slot trong Inventory
        }
    }

    private void CheckAlphaNumericKeys()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(0);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(1);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(2);
        else if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlot(3);
        else if (Input.GetKeyDown(KeyCode.Alpha5)) SelectSlot(4);
        else if (Input.GetKeyDown(KeyCode.Alpha6)) SelectSlot(5);
        else if (Input.GetKeyDown(KeyCode.Alpha7)) SelectSlot(6);
    }

    private void CheckUseItem()
    {
        if (selectedSlot != null && Input.GetKeyDown(KeyCode.Space)) // Kích hoạt hành động chính bằng Space
        {
            Player player = GameManager.instance.player;
            if (player != null)
            {
                player.HandlePrimaryAction(); // Gọi phương thức công khai
            }
        }
    }
}
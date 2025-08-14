using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Toolbar_UI : MonoBehaviour
{
    public List<Slot_UI> toolbarSlots = new List<Slot_UI>();
    public Slot_UI selectedSlot;

    private void Start()
    {
        if (toolbarSlots == null || toolbarSlots.Count == 0)
        {
            Debug.LogError("Toolbar slots not assigned or empty!");
            return;
        }

        Inventory toolbarInventory = null;
        if (GameManager.instance != null && GameManager.instance.player != null && GameManager.instance.player.inventoryManager != null)
        {
            toolbarInventory = GameManager.instance.player.inventoryManager.GetInventoryByName(InventoryManager.TOOLBAR);
        }
        else
        {
            Debug.LogError("GameManager, Player, or InventoryManager not initialized!");
            return;
        }

        for (int i = 0; i < toolbarSlots.Count; i++)
        {
            if (toolbarSlots[i] != null)
            {
                toolbarSlots[i].slotID = i;
                toolbarSlots[i].inventory = toolbarInventory;
            }
            else
            {
                Debug.LogWarning($"Slot_UI at index {i} is null!");
            }
        }
        SelectSlot(0);
    }

    private void Update()
    {
        CheckAlphaNumericKeys();
        CheckUseItem();
    }

    public void SelectSlot(int index)
    {
        if (toolbarSlots == null || toolbarSlots.Count != 7 || index < 0 || index >= toolbarSlots.Count)
        {
            Debug.LogWarning($"Invalid slot index {index} or toolbarSlots count {toolbarSlots.Count}!");
            return;
        }

        if (selectedSlot != null)
        {
            selectedSlot.SetHighlight(false);
        }
        selectedSlot = toolbarSlots[index];
        if (selectedSlot != null)
        {
            selectedSlot.SetHighlight(true);
            if (GameManager.instance != null && GameManager.instance.player != null &&
                GameManager.instance.player.inventoryManager != null)
            {
                GameManager.instance.player.inventoryManager.toolbar.SelectSlot(index);
            }
        }
        else
        {
            Debug.LogWarning($"Selected slot at index {index} is null!");
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
        if (selectedSlot != null && Input.GetKeyDown(KeyCode.Space))
        {
            Player player = GameManager.instance?.player;
            if (player != null)
            {
                player.HandlePrimaryAction();
            }
            else
            {
                Debug.LogWarning("Player not found in GameManager!");
            }
        }
    }

    public void Refresh()
    {
        for (int i = 0; i < toolbarSlots.Count; i++)
        {
            if (toolbarSlots[i] != null)
            {
                toolbarSlots[i].UpdateSlotUI();
            }
        }
        if (selectedSlot != null)
        {
            selectedSlot.SetHighlight(true);
        }
    }
}
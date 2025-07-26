using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI_Manager : MonoBehaviour
{
    public Dictionary<string, Inventory_UI> inventoryUIByName = new Dictionary<string, Inventory_UI>();
    public List<Inventory_UI> inventoryUIs;

    public GameObject inventoryPanel;
    public GameObject questPanel;

    public static Slot_UI draggedSlot;
    public static Image draggedIcon;

    public static bool dragSingle;

    private void Awake()
    {
        Initialize();
    }

    private void Start()
    {
        ToggleInventoryUI();
        if (questPanel != null)
        {
            questPanel.SetActive(false); // Tắt bảng Quest khi game bắt đầu
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.B))
        {
            ToggleInventoryUI();
        }

        if (Input.GetKey(KeyCode.LeftShift))
        {
            dragSingle = true;
        }
        if (Input.GetKeyDown(KeyCode.J))
        {
            ToggleQuestPanel();
        }
        else
        {
            dragSingle = false;
        }
    }

    public void ToggleInventoryUI()
    {
        if (inventoryPanel != null)
        {
            if (!inventoryPanel.activeSelf)
            {
                inventoryPanel.SetActive(true);
                RefreshInventoryUI("Backpack");
            }
            else
            {
                inventoryPanel.SetActive(false);
            }
        }
    }
    public void ToggleQuestPanel()
    {
        if (questPanel != null)
        {
            questPanel.SetActive(!questPanel.activeSelf);
        }
    }

    public void RefreshInventoryUI(string inventoryName)
    {
        if (inventoryUIByName.ContainsKey(inventoryName))
        {
            inventoryUIByName[inventoryName].Refresh();
        }
    }

    public void RefreshAll()
    {
        foreach (KeyValuePair<string, Inventory_UI> keyValuePair in inventoryUIByName)
        {
            keyValuePair.Value.Refresh();
        }
    }

    public Inventory_UI GetInventoryUI(string inventoryName)
    {
        if (inventoryUIByName.ContainsKey(inventoryName))
        {
            return inventoryUIByName[inventoryName];
        }

        return null;
    }

    private void Initialize()
    {
        foreach (Inventory_UI ui in inventoryUIs)
        {
            if (!inventoryUIByName.ContainsKey(ui.inventoryName))
            {
                inventoryUIByName.Add(ui.inventoryName, ui);
            }
        }
    }

}
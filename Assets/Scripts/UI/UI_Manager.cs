using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI_Manager : MonoBehaviour
{
    public Dictionary<string, Inventory_UI> inventoryUIByName = new Dictionary<string, Inventory_UI>();
    public List<Inventory_UI> inventoryUIs;
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject questPanel;
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
        else if (Input.GetKeyUp(KeyCode.LeftShift)) // Chỉ đặt lại khi thả phím
        {
            dragSingle = false;
        }
        if (Input.GetKeyDown(KeyCode.J))
        {
            ToggleQuestPanel();
        }
    }

    public void ToggleInventoryUI()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(!inventoryPanel.activeSelf);
            if (inventoryPanel.activeSelf)
            {
                RefreshInventoryUI("Backpack"); // Làm mới Backpack khi mở
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
        if (inventoryUIByName.ContainsKey(inventoryName) && inventoryUIByName[inventoryName] != null)
        {
            inventoryUIByName[inventoryName].Refresh();
        }
        else
        {
            Debug.LogWarning($"Inventory UI for {inventoryName} not found or null!");
        }
    }

    public void RefreshAll()
    {
        foreach (var ui in inventoryUIByName.Values)
        {
            if (ui != null) ui.Refresh();
        }
    }

    public Inventory_UI GetInventoryUI(string inventoryName)
    {
        return inventoryUIByName.ContainsKey(inventoryName) ? inventoryUIByName[inventoryName] : null;
    }

    private void Initialize()
    {
        if (inventoryUIs == null)
        {
            Debug.LogError("inventoryUIs list is null!");
            return;
        }

        foreach (Inventory_UI ui in inventoryUIs)
        {
            if (ui != null && !string.IsNullOrEmpty(ui.inventoryName) && !inventoryUIByName.ContainsKey(ui.inventoryName))
            {
                inventoryUIByName.Add(ui.inventoryName, ui);
            }
            else if (ui == null)
            {
                Debug.LogWarning("Null Inventory_UI found in inventoryUIs list!");
            }
        }
    }
}
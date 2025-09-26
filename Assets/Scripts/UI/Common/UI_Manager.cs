using System.Collections.Generic;
using UnityEngine;

namespace CGP.UI
{
    public class UI_Manager : MonoBehaviour
    {
        public static UI_Manager Instance { get; private set; }   // 🆕 Singleton

        [Header("Inventory UIs")]
        [SerializeField] private List<Inventory_UI> inventoryUIs = new();
        private readonly Dictionary<string, Inventory_UI> inventoryUIByName = new();

        [Header("Panels")]
        [SerializeField] private GameObject inventoryPanel;
        [SerializeField] private GameObject questPanel;
        [SerializeField] private GameObject shopPanel;
        [SerializeField] private GameObject tutorialPanel;

        [Header("Tooltip")]
        public ItemTooltip Tooltip;   // 🆕 kéo prefab/obj ItemTooltip vào đây trong Inspector

        // ===== Drag state =====
        public static Slot_UI draggedSlot;
        public static UnityEngine.UI.Image draggedIcon;
        public static bool dragSingle;

        private void Awake()
        {
            // Singleton
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildInventoryMap();
        }

        private void Start()
        {
            CloseAllPanels();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.Tab))
                ToggleInventoryUI();

            if (Input.GetKeyDown(KeyCode.C)) ToggleShopPanel();
            if (Input.GetKeyDown(KeyCode.I)) ToggleTutorialPanel();
            if (Input.GetKeyDown(KeyCode.J)) ToggleQuestPanel();

            dragSingle = Input.GetKey(KeyCode.LeftShift);
        }

        // ---------- Toggle Panels ----------
        public void ToggleInventoryUI()
        {
            TogglePanel(inventoryPanel, () => RefreshInventoryUI("Backpack"));
        }

        public void ToggleQuestPanel() => TogglePanel(questPanel);
        public void ToggleShopPanel() => TogglePanel(shopPanel);
        public void ToggleTutorialPanel() => TogglePanel(tutorialPanel);
        
        private void TogglePanel(GameObject panel, System.Action afterOpen = null)
        {
            if (!panel) return;

            bool willOpen = !panel.activeSelf;
            CloseAllPanels();

            if (willOpen)
            {
                panel.SetActive(true);
                afterOpen?.Invoke();
                Debug.Log($"[UI] Open {panel.name}");
            }
        }

        public void CloseAllPanels()
        {
            if (inventoryPanel) inventoryPanel.SetActive(false);
            if (questPanel) questPanel.SetActive(false);
            if (shopPanel) shopPanel.SetActive(false);
            if (tutorialPanel) tutorialPanel.SetActive(false);

            // 🆕 đóng tooltip khi tắt panel
            Tooltip?.Hide();
        }

        // ---------- Inventory helpers ----------
        public void RefreshInventoryUI(string inventoryName)
        {
            if (inventoryUIByName.TryGetValue(inventoryName, out var ui) && ui != null)
                ui.Refresh();
            else
                Debug.LogWarning($"[UI] Inventory UI '{inventoryName}' not found or null!");
        }

        public void RefreshAll()
        {
            foreach (var ui in inventoryUIByName.Values)
                if (ui) ui.Refresh();
        }

        public Inventory_UI GetInventoryUI(string inventoryName)
            => inventoryUIByName.TryGetValue(inventoryName, out var ui) ? ui : null;

        private void BuildInventoryMap()
        {
            inventoryUIByName.Clear();
            foreach (var ui in inventoryUIs)
            {
                if (!ui) continue;
                if (!string.IsNullOrEmpty(ui.inventoryName) && !inventoryUIByName.ContainsKey(ui.inventoryName))
                    inventoryUIByName.Add(ui.inventoryName, ui);
            }
        }

        public void DebugBackpackClick()
        {
            Debug.Log(">>> Backpack Button Clicked <<<");
        }
    }
}

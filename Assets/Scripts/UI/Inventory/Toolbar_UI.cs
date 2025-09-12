using System.Collections.Generic;
using UnityEngine;
using CGP.Gameplay.InventorySystem;
using CGP.Gameplay.Inventory.Presenter; // InventoryManager
using CGP.Gameplay.Systems;

namespace CGP.UI
{
    public class Toolbar_UI : MonoBehaviour
    {
        public List<Slot_UI> toolbarSlots = new List<Slot_UI>();
        public Slot_UI selectedSlot;
        [SerializeField] int slotCount = 7;

        void Start()
        {
            if (toolbarSlots == null || toolbarSlots.Count == 0)
            { Debug.LogError("Toolbar slots not assigned"); return; }

            var inv = GameManager.instance?.player?.inventoryManager?
                .GetInventoryByName(InventoryManager.TOOLBAR);
            if (inv == null) { Debug.LogError("Toolbar inventory not found"); return; }

            for (int i = 0; i < toolbarSlots.Count; i++)
            {
                var s = toolbarSlots[i]; if (!s) continue;
                s.slotID = i; s.inventory = inv; s.UpdateSlotUI();
            }
            SelectSlot(0);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(2);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlot(3);
            else if (Input.GetKeyDown(KeyCode.Alpha5)) SelectSlot(4);
            else if (Input.GetKeyDown(KeyCode.Alpha6)) SelectSlot(5);
            else if (Input.GetKeyDown(KeyCode.Alpha7)) SelectSlot(6);

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                int cur = Mathf.Clamp(toolbarSlots.IndexOf(selectedSlot), 0, slotCount - 1);
                int next = (scroll > 0) ? (cur - 1 + slotCount) % slotCount : (cur + 1) % slotCount;
                SelectSlot(next);
            }

            if (selectedSlot != null && Input.GetKeyDown(KeyCode.Space))
                GameManager.instance?.player?.HandlePrimaryAction();
        }

        public void SelectSlot(int index)
        {
            if (toolbarSlots == null || toolbarSlots.Count != slotCount) return;
            if (index < 0 || index >= toolbarSlots.Count) return;

            if (selectedSlot) selectedSlot.SetSelected(false);
            selectedSlot = toolbarSlots[index];
            selectedSlot?.SetSelected(true);

            GameManager.instance?.player?.inventoryManager?.toolbar.SelectSlot(index);
        }

        public void Refresh()
        {
            foreach (var s in toolbarSlots) s?.UpdateSlotUI();
            if (selectedSlot) selectedSlot.SetSelected(true);
        }
    }
}

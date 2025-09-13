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

        Inventory _toolbar;
        InventoryManager _im;

        void Start()
        {
            if (toolbarSlots == null || toolbarSlots.Count == 0)
            { Debug.LogError("Toolbar slots not assigned"); return; }

            _im = InventoryManager.Instance;

            _toolbar = GameManager.instance?.player?.inventoryManager?
                .GetInventoryByName(InventoryManager.TOOLBAR);
            if (_toolbar == null) { Debug.LogError("Toolbar inventory not found"); return; }

            for (int i = 0; i < toolbarSlots.Count; i++)
            {
                var s = toolbarSlots[i]; if (!s) continue;
                s.slotID = i; s.inventory = _toolbar; s.UpdateSlotUI();
            }

            // nghe reload nhẹ từ InventoryManager
            if (_im != null) _im.OnInventoryLoaded += OnInvLoaded;

            SelectSlot(0);
        }

        void OnDestroy()
        {
            if (_im != null) _im.OnInventoryLoaded -= OnInvLoaded;
        }

        void OnInvLoaded()
        {
            // chỉ cập nhật icon/qty, không đổi highlight
            Refresh();
        }

        void Update()
        {
            int cnt = toolbarSlots?.Count ?? 0;
            if (cnt == 0) return;

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
                int cur = selectedSlot ? toolbarSlots.IndexOf(selectedSlot) : 0;
                cur = Mathf.Clamp(cur, 0, cnt - 1);
                int next = (scroll > 0) ? (cur - 1 + cnt) % cnt : (cur + 1) % cnt;
                SelectSlot(next);
            }

            if (selectedSlot != null && Input.GetKeyDown(KeyCode.Space))
                GameManager.instance?.player?.HandlePrimaryAction();
        }

        public void SelectSlot(int index)
        {
            int cnt = toolbarSlots?.Count ?? 0;
            if (cnt == 0) return;

            index = Mathf.Clamp(index, 0, cnt - 1);

            if (selectedSlot) selectedSlot.SetSelected(false);
            selectedSlot = toolbarSlots[index];
            selectedSlot?.SetSelected(true);

            GameManager.instance?.player?.inventoryManager?.toolbar.SelectSlot(index);
        }

        public void Refresh()
        {
            if (_toolbar == null || toolbarSlots == null) return;

            for (int i = 0; i < toolbarSlots.Count; i++)
            {
                var s = toolbarSlots[i]; if (!s) continue;
                s.slotID = i; s.inventory = _toolbar; s.UpdateSlotUI();
            }
            if (selectedSlot) selectedSlot.SetSelected(true);
        }
    }
}

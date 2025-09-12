using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using CGP.Gameplay.Items;
using CGP.Gameplay.InventorySystem;
using CGP.Gameplay.Inventory.Presenter; // InventoryManager
using CGP.Gameplay.Systems;             // Player
using CGP.Gameplay.Auth;

namespace CGP.UI
{
    public class Inventory_UI : MonoBehaviour
    {
        [Header("Inventory Source")]
        public string inventoryName = "Backpack";

        [Header("Slot prefab")]
        public Slot_UI slotPrefab;

        [Header("Runtime")]
        public List<Slot_UI> slots = new();

        [Header("FX")]
        [SerializeField] bool animateOnRefresh = true;   // chỉ áp dụng cho refresh “toàn lưới”, KHÔNG áp cho drag-drop

        Canvas _canvas;
        CanvasGroup _cg;
        Inventory _inventory;
        InventoryManager _imRef;

        // suppress soft-reload ngay sau khi move (tránh double refresh từ event)
        float _suppressLoadedUntil = -1f;

        void Start()
        {
            _canvas = FindFirstObjectByType<Canvas>();
            if (!_canvas) { Debug.LogError("Canvas not found"); return; }

            var mgr = GameManager.instance?.player?.inventoryManager;
            if (mgr == null) { Debug.LogError("inventoryManager missing"); return; }

            if (!slotPrefab) { Debug.LogError("slotPrefab not assigned"); return; }

            _inventory = mgr.GetInventoryByName(inventoryName);
            if (_inventory == null) { Debug.LogError($"Inventory '{inventoryName}' not found"); return; }

            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 1f;

            SetupSlots();
            Refresh(); // lần đầu có thể fade (nếu bật)

            // nghe event nhưng chỉ refresh nhẹ, và có suppress
            _imRef = InventoryManager.Instance;
            if (_imRef != null) _imRef.OnInventoryLoaded += OnInventoryLoaded_Soft;
        }

        void OnDestroy()
        {
            if (_imRef != null) _imRef.OnInventoryLoaded -= OnInventoryLoaded_Soft;
        }

        // ===== Public Refresh APIs =====
        public void Refresh() => DoRefresh(true);
        public void Refresh(bool animate) => DoRefresh(animate);

        void DoRefresh(bool animate)
        {
            if (_inventory == null) return;

            int target = _inventory.slots != null ? _inventory.slots.Count : 0;
            AdjustSlotCount(target);

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (!s) continue;
                s.slotID = i;
                s.inventory = _inventory;
                s.UpdateSlotUI();
            }

            if (animateOnRefresh && animate && _cg != null)
                StartCoroutine(FadeIn());
        }

        IEnumerator FadeIn()
        {
            _cg.alpha = 0f;
            float t = 0f, dur = .12f;
            while (t < dur) { t += Time.unscaledDeltaTime; _cg.alpha = Mathf.SmoothStep(0, 1, t / dur); yield return null; }
            _cg.alpha = 1f;
        }

        void AdjustSlotCount(int target)
        {
            while (slots.Count < target)
            {
                var ui = Instantiate(slotPrefab, transform);
                ui.inventory = _inventory;
                ui.slotID = slots.Count;
                slots.Add(ui);
            }
            while (slots.Count > target)
            {
                var tail = slots[^1];
                slots.RemoveAt(slots.Count - 1);
                if (tail) Destroy(tail.gameObject);
            }
        }

        void SetupSlots()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (!s) continue;
                s.slotID = i;
                s.inventory = _inventory;
            }
        }

        // ===== Drag & Drop =====
        public void SlotBeginDrag(Slot_UI slot)
        {
            if (slot == null || slot.inventory == null) return;
            var data = slot.inventory.slots[slot.slotID];
            if (data == null || data.IsEmpty) return;
            if (UI_Manager.draggedSlot != null) return;

            InventoryManager.Instance?.SetDragState(true);

            UI_Manager.draggedSlot = slot;
            UI_Manager.draggedIcon = Instantiate(slot.itemIcon);
            UI_Manager.draggedIcon.transform.SetParent(_canvas.transform, false);
            UI_Manager.draggedIcon.raycastTarget = false;
            UI_Manager.draggedIcon.rectTransform.sizeDelta = new Vector2(50, 50);
            UI_Manager.draggedIcon.color = new Color(1, 1, 1, 0.9f);
            UI_Manager.draggedIcon.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -2);

            MoveToMousePosition(UI_Manager.draggedIcon.gameObject, true);
        }

        public void SlotDrag()
        {
            if (UI_Manager.draggedIcon != null)
                MoveToMousePosition(UI_Manager.draggedIcon.gameObject, false);
        }

        public void SlotEndDrag()
        {
            if (UI_Manager.draggedIcon) Destroy(UI_Manager.draggedIcon.gameObject);
            UI_Manager.draggedIcon = null;
            StartCoroutine(DelayedDragCleanup());
        }

        IEnumerator DelayedDragCleanup()
        {
            yield return new WaitForEndOfFrame();
            InventoryManager.Instance?.SetDragState(false);
            UI_Manager.draggedSlot = null;
        }

        public async void SlotDrop(Slot_UI slot)
        {
            if (UI_Manager.draggedSlot == null || slot == null) return;

            string fromInv = GetInventoryNameFromSlot(UI_Manager.draggedSlot);
            string toInv = GetInventoryNameFromSlot(slot);
            int fromIdx = UI_Manager.draggedSlot.slotID;
            int toIdx = slot.slotID;

            InventoryManager.Instance?.SetDragState(false);

            bool ok = await InventoryManager.Instance.MoveItem(
                fromInv, fromIdx, toInv, toIdx);

            if (ok)
            {
                // chặn event reload ngay sau khi move
                _suppressLoadedUntil = Time.unscaledTime + 0.3f;

                // chỉ refresh “cục bộ” các ô liên quan, KHÔNG fade
                SoftRefresh(fromInv, fromIdx);
                SoftRefresh(toInv, toIdx);

                // nếu dính toolbar, chỉ refresh toolbar nhẹ
                if (fromInv == InventoryManager.TOOLBAR || toInv == InventoryManager.TOOLBAR)
                    FindFirstObjectByType<Toolbar_UI>()?.Refresh();
            }

            UI_Manager.draggedSlot = null;
        }

        void SoftRefresh(string invName, params int[] indices)
        {
            if (indices == null || indices.Length == 0) return;

            var uis = FindObjectsByType<Inventory_UI>(FindObjectsSortMode.None);
            foreach (var ui in uis)
            {
                if (ui.inventoryName != invName) continue;
                ui.RefreshSlots(false, indices);
            }
        }

        // vẽ lại đúng các index, không đụng CanvasGroup/fade, không AdjustSlotCount
        public void RefreshSlots(bool animate, params int[] indices)
        {
            if (_inventory == null || slots == null) return;
            if (indices == null || indices.Length == 0) return;

            var uniq = indices.Distinct();
            foreach (var i in uniq)
            {
                if (i < 0 || i >= slots.Count) continue;
                var s = slots[i];
                if (!s) continue;
                // đảm bảo binding đúng inventory + id
                s.slotID = i;
                s.inventory = _inventory;
                s.UpdateSlotUI();
            }
            // tuyệt đối KHÔNG fade ở đây
        }

        string GetInventoryNameFromSlot(Slot_UI slot)
        {
            foreach (var ui in FindObjectsByType<Inventory_UI>(FindObjectsSortMode.None))
                if (ui.slots.Contains(slot)) return ui.inventoryName;
            return inventoryName;
        }

        void MoveToMousePosition(GameObject go, bool snap)
        {
            if (!_canvas || !go) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvas.transform as RectTransform, Input.mousePosition, null, out var lp);
            var world = _canvas.transform.TransformPoint(lp);
            if (snap) go.transform.position = world;
            else go.transform.position = Vector3.Lerp(go.transform.position, world, 0.35f);
        }

        // Optional: drop ra đất (giữ API cũ)
        public void Remove()
        {
            if (UI_Manager.draggedSlot == null || _inventory == null) return;

            string itemName = _inventory.slots[UI_Manager.draggedSlot.slotID].itemName;
            Item item = GameManager.instance.itemManager.GetItemByName(itemName);
            if (item != null)
            {
                if (UI_Manager.dragSingle)
                {
                    GameManager.instance.player.DropItem(item);
                    _inventory.Remove(UI_Manager.draggedSlot.slotID);
                }
                else
                {
                    int c = _inventory.slots[UI_Manager.draggedSlot.slotID].count;
                    GameManager.instance.player.DropItem(item, c);
                    _inventory.Remove(UI_Manager.draggedSlot.slotID, c);
                }
                StartCoroutine(SyncAfterRemove());
                Refresh(false);
            }
            UI_Manager.draggedSlot = null;
        }

        IEnumerator SyncAfterRemove()
        {
            yield return new WaitForSeconds(0.1f);
            InventoryManager.Instance?.SyncInventory(inventoryName).ConfigureAwait(false);
        }

        void OnInventoryLoaded_Soft()
        {
            // nếu vừa move xong, bỏ qua lần reload này để tránh nháy
            if (Time.unscaledTime < _suppressLoadedUntil) return;
            Refresh(false); // refresh nhẹ, không fade
        }
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using CGP.Gameplay.InventorySystem;
using CGP.Gameplay.Items;

namespace CGP.UI
{
    public class Slot_UI : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IDropHandler
    {
        [Header("Binding")]
        public int slotID = -1;
        public Image itemIcon;
        public TextMeshProUGUI quantityText;
        public GameObject highlight;   // Image viền (tắt mặc định)
        public Inventory inventory;
        public Sprite placeholderIcon;

        [Header("FX")]
        public float hoverScale = 1.05f;
        public float scaleSpeed = 12f;
        public float popScale = 1.12f;

        RectTransform _rt;
        Vector3 _baseScale;
        bool _hovering;
        int _lastCount = int.MinValue;

        // cache parent để lấy inventoryName khi cần
        Inventory_UI _parentUI;

        void Awake()
        {
            _rt = (RectTransform)transform;
            _baseScale = _rt.localScale;
            if (highlight) highlight.SetActive(false);
            _parentUI = GetComponentInParent<Inventory_UI>();
        }

        void Update()
        {
            var target = _hovering ? _baseScale * hoverScale : _baseScale;
            _rt.localScale = Vector3.Lerp(_rt.localScale, target, Time.unscaledDeltaTime * scaleSpeed);
        }

        // ===== Data → UI =====
        public void UpdateSlotUI()
        {
            if (inventory == null || slotID < 0 || slotID >= inventory.slots.Count)
            {
                SetEmpty();
                return;
            }

            var s = inventory.slots[slotID];
            if (s == null || s.count <= 0)
            {
                SetEmpty();
                return;
            }

            // Fallback resolve: chỉ tra bằng tên item (KHÔNG dùng tên để tra server id)
            if ((s.itemData == null || s.icon == null) && GameManager.instance != null)
            {
                var im = GameManager.instance.itemManager;
                if (im != null)
                {
                    if (s.itemData == null && !string.IsNullOrEmpty(s.itemName))
                        s.itemData = im.GetItemDataByName(s.itemName);

                    if (s.itemData != null && s.icon == null)
                        s.icon = s.itemData.icon;
                }
            }

            if (_lastCount != int.MinValue && _lastCount != s.count)
                StartCoroutine(Pop());
            _lastCount = s.count;

            SetItem(s);
        }

        void SetItem(Inventory.Slot slot)
        {
            var icon = slot.icon != null ? slot.icon : placeholderIcon;

            if (itemIcon)
            {
                if (icon != null)
                {
                    itemIcon.enabled = true;
                    itemIcon.sprite = icon;
                    itemIcon.color = Color.white;
                }
                else itemIcon.enabled = false;
            }

            if (quantityText)
            {
                bool show = slot.count > 0;
                quantityText.gameObject.SetActive(show);
                quantityText.text = show ? slot.count.ToString() : "";
            }

            var bg = GetComponent<Image>();
            if (bg) bg.color = new Color(1, 1, 1, 1);
        }

        void SetEmpty()
        {
            if (itemIcon) { itemIcon.sprite = null; itemIcon.enabled = false; }
            if (quantityText) { quantityText.text = ""; quantityText.gameObject.SetActive(false); }
            var bg = GetComponent<Image>();
            if (bg) bg.color = new Color(1, 1, 1, 0.65f);
            _lastCount = int.MinValue;
        }

        public void SetSelected(bool on)
        {
            if (highlight) highlight.SetActive(on);
            StopAllCoroutines();
            if (on) StartCoroutine(Pop()); else _rt.localScale = Vector3.one;
        }

        IEnumerator Pop()
        {
            float t = 0f, up = .08f, down = .10f, peak = popScale;
            while (t < up) { t += Time.unscaledDeltaTime; _rt.localScale = Vector3.Lerp(Vector3.one, Vector3.one * peak, t / up); yield return null; }
            t = 0f;
            while (t < down) { t += Time.unscaledDeltaTime; _rt.localScale = Vector3.Lerp(Vector3.one * peak, Vector3.one, t / down); yield return null; }
            _rt.localScale = Vector3.one;
        }

        // ===== Quick consume API (gọi từ chỗ khác nếu cần) =====
        public void QuickConsume(int amount = 1)
        {
            var invName = _parentUI != null ? _parentUI.inventoryName : "Backpack";
            CGP.Gameplay.Inventory.Presenter.InventoryManager.Instance?
                .TryConsume(invName, slotID, amount);

            // tự làm mới ngay để icon biến mất nếu = 0
            UpdateSlotUI();
        }

        // ===== Pointer events =====
        public void OnPointerEnter(PointerEventData e) { _hovering = true; if (highlight) highlight.SetActive(true); }
        public void OnPointerExit(PointerEventData e) { _hovering = false; if (highlight) highlight.SetActive(false); }

        public void OnPointerDown(PointerEventData e)
        {
            // chỉ bắt drag khi có item
            var data = inventory != null && slotID >= 0 && slotID < (inventory?.slots?.Count ?? 0)
                ? inventory.slots[slotID] : null;
            if (data != null && data.count > 0)
                GetComponentInParent<Inventory_UI>()?.SlotBeginDrag(this);
        }

        public void OnPointerUp(PointerEventData e)
        {
            GetComponentInParent<Inventory_UI>()?.SlotEndDrag();

            // tuỳ chọn: Right click để consume nhanh 1 item (nếu bạn muốn)
            if (e != null && e.button == PointerEventData.InputButton.Right)
                QuickConsume(1);
        }

        public void OnDrop(PointerEventData e)
        {
            GetComponentInParent<Inventory_UI>()?.SlotDrop(this);
            StopAllCoroutines(); StartCoroutine(Pop());
        }
    }
}

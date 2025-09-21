using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Alias vào đúng class Inventory (chứa nested class Slot)
using Inv = CGP.Gameplay.InventorySystem.Inventory;

namespace CGP.Gameplay.Shop
{
    public class SellDialogUI : MonoBehaviour
    {
        [Header("Bind")]
        [SerializeField] private Image icon;
        [SerializeField] private Slider slider;
        [SerializeField] private TMP_Text qtyText;          // "xN → price$"
        [SerializeField] private Button btnMinus;
        [SerializeField] private Button btnPlus;
        [SerializeField] private Button btnCancel;
        [SerializeField] private Button btnConfirm;

        // state
        private int _owned;          // có trong kho
        private int _unitPrice;      // giá/1 item
        private Func<int, Task<bool>> _onConfirmAsync; // callback xác nhận (trả true nếu OK)
        private Action<int> _onConfirm;               // fallback sync

        private void Awake()
        {
            // Fallback bind
            if (!icon) icon = transform.Find("Window/Icon")?.GetComponent<Image>();
            if (!slider) slider = transform.Find("Window/SliderRow/Amount")?.GetComponent<Slider>();
            if (!qtyText) qtyText = transform.Find("Window/QtyText")?.GetComponent<TMP_Text>();
            if (!btnMinus) btnMinus = transform.Find("Window/SliderRow/Minus")?.GetComponent<Button>();
            if (!btnPlus) btnPlus = transform.Find("Window/SliderRow/Plus")?.GetComponent<Button>();
            if (!btnCancel) btnCancel = transform.Find("Window/Buttons/Cancel")?.GetComponent<Button>();
            if (!btnConfirm) btnConfirm = transform.Find("Window/Buttons/Confirm")?.GetComponent<Button>();

            if (btnMinus) btnMinus.onClick.AddListener(() => Step(-1));
            if (btnPlus) btnPlus.onClick.AddListener(() => Step(+1));
            if (btnCancel) btnCancel.onClick.AddListener(Hide);
            if (btnConfirm) btnConfirm.onClick.AddListener(OnClickConfirm);

            if (slider)
            {
                slider.wholeNumbers = true;
                slider.onValueChanged.AddListener(_ => RefreshTexts());
            }

            gameObject.SetActive(false);
        }

        // ======== Overload ưu tiên: nhận thẳng Slot từ InventorySystem ========
        public void Show(Inv.Slot slot, int unitPrice, Func<int, Task<bool>> onConfirmAsync)
        {
            if (slot == null || slot.IsEmpty) { Debug.LogWarning("[SellDialogUI] Slot null/empty"); return; }
            _onConfirmAsync = onConfirmAsync;
            _onConfirm = null;

            // chọn icon: ưu tiên itemData.icon nếu có
            var spr = slot.itemData ? slot.itemData.icon : slot.icon;
            InternalShow(spr, slot.count, unitPrice);
        }

        public void Show(Inv.Slot slot, int unitPrice, Action<int> onConfirm)
        {
            if (slot == null || slot.IsEmpty) { Debug.LogWarning("[SellDialogUI] Slot null/empty"); return; }
            _onConfirm = onConfirm;
            _onConfirmAsync = null;

            var spr = slot.itemData ? slot.itemData.icon : slot.icon;
            InternalShow(spr, slot.count, unitPrice);
        }

        // ======== Overload cũ: vẫn giữ để tương thích ========
        public void Show(Sprite spr, int owned, int unitPrice, Func<int, Task<bool>> onConfirmAsync)
        {
            _onConfirmAsync = onConfirmAsync;
            _onConfirm = null;
            InternalShow(spr, owned, unitPrice);
        }

        public void Show(Sprite spr, int owned, int unitPrice, Action<int> onConfirm)
        {
            _onConfirm = onConfirm;
            _onConfirmAsync = null;
            InternalShow(spr, owned, unitPrice);
        }

        // ======== Core hiển thị ========
        private void InternalShow(Sprite spr, int owned, int unitPrice)
        {
            _owned = Mathf.Max(0, owned);
            _unitPrice = Mathf.Max(0, unitPrice);

            if (icon) { icon.sprite = spr; icon.enabled = spr != null; }

            if (slider)
            {
                // Cho phép xem 0 để hiện tổng tiền; auto gợi ý 1 nếu có hàng
                slider.minValue = 0;
                slider.maxValue = _owned;
                slider.value = (_owned > 0) ? 1 : 0;
            }

            RefreshTexts();
            SetInteractable(true);
            gameObject.SetActive(true);

            Debug.Log($"[SellDialogUI] Show owned={_owned}, unitPrice={_unitPrice}");
        }

        public void Hide() => gameObject.SetActive(false);

        private int CurrentQty => slider ? Mathf.RoundToInt(slider.value) : 0;

        private void Step(int delta)
        {
            if (!slider) return;
            var v = Mathf.Clamp(CurrentQty + delta, (int)slider.minValue, (int)slider.maxValue);
            if (v != CurrentQty) slider.value = v;
            RefreshTexts();
        }

        private void RefreshTexts()
        {
            int q = CurrentQty;
            long total = (long)q * _unitPrice;

            if (qtyText) qtyText.text = $"x{q}  →  {total}$";

            // cập nhật tương tác nút theo giá trị hiện tại
            if (btnConfirm) btnConfirm.interactable = (q > 0 && q <= _owned);
            if (btnMinus) btnMinus.interactable = (_owned > 0 && q > (int)(slider ? slider.minValue : 0));
            if (btnPlus) btnPlus.interactable = (_owned > 0 && q < _owned);
            if (slider) slider.interactable = (_owned > 0);
        }

        private async void OnClickConfirm()
        {
            int qty = CurrentQty;
            if (qty <= 0 || qty > _owned) return;

            SetInteractable(false);

            bool ok = true;
            try
            {
                if (_onConfirmAsync != null)
                    ok = await _onConfirmAsync.Invoke(qty);
                else
                {
                    _onConfirm?.Invoke(qty);
                    ok = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SellDialogUI] Confirm exception: " + e.Message);
                ok = false;
            }

            if (ok) Hide();
            else SetInteractable(true);
        }

        private void SetInteractable(bool on)
        {
            if (slider) slider.interactable = on && _owned > 0;
            if (btnCancel) btnCancel.interactable = on;

            // phụ thuộc vào giá trị hiện tại
            if (btnConfirm) btnConfirm.interactable = on && CurrentQty > 0 && CurrentQty <= _owned;
            if (btnPlus) btnPlus.interactable = on && _owned > 0 && CurrentQty < _owned;
            if (btnMinus) btnMinus.interactable = on && _owned > 0 && CurrentQty > (int)(slider ? slider.minValue : 0);
        }
    }
}

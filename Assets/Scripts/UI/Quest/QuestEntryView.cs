using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CGP.Gameplay.Items;
using CGP.Gameplay.Quests;

namespace CGP.UI.Quests
{
    public class QuestEntryView : MonoBehaviour
    {
        [Header("Refs")]
        public Image leftIcon;
        public TMP_Text qtyText;

        // Đổi tên biến
        public TMP_Text nameText;          // hiển thị questName
        public TMP_Text descriptionText;   // hiển thị quest description

        public Button actionButton;
        public TMP_Text actionText;
        public Sprite placeholderIcon;

        QuestVM _vm;
        public QuestVM VM => _vm;
        public bool Claimable => _vm != null && !_vm.isClaimed && _vm.canClaim;
        System.Action<QuestVM> _onClaim;

        public void Bind(QuestVM vm, System.Action<QuestVM> onClaim)
        {
            _vm = vm;
            _onClaim = onClaim;

            if (_vm == null)
            {
                ApplyEmpty();
                return;
            }

            if (nameText) nameText.text = _vm.meta.questName;
            if (descriptionText) descriptionText.text = _vm.meta.description;

            if (qtyText)
            {
                var showQty = _vm.meta.amountReward > 1;
                qtyText.gameObject.SetActive(showQty);
                qtyText.text = showQty ? $"x{_vm.meta.amountReward}" : string.Empty;
            }

            SetRewardIcon(_vm.meta.reward);
            SetState(_vm.isClaimed, _vm.canClaim);

            if (actionButton)
            {
                actionButton.onClick.RemoveAllListeners();
                actionButton.onClick.AddListener(() =>
                {
                    Debug.Log($"[QuestEntryView] CLICK '{_vm.meta.questName}' | canClaim={_vm.canClaim} | isClaimed={_vm.isClaimed}");
                    _onClaim?.Invoke(_vm);
                });
            }
        }

        public void Refresh(QuestVM vm)
        {
            _vm = vm;
            if (_vm == null)
            {
                ApplyEmpty();
                return;
            }

            if (nameText) nameText.text = _vm.meta.questName;
            if (descriptionText) descriptionText.text = _vm.meta.description;

            if (qtyText)
            {
                var showQty = _vm.meta.amountReward > 1;
                qtyText.gameObject.SetActive(showQty);
                qtyText.text = showQty ? $"x{_vm.meta.amountReward}" : string.Empty;
            }

            SetRewardIcon(_vm.meta.reward);
            SetState(_vm.isClaimed, _vm.canClaim);
        }

        public void SetClaimFinished(QuestVM updatedVm) => Refresh(updatedVm);

        public void SetClaimFinished(bool claimed = true, bool canClaim = false)
        {
            SetState(claimed, canClaim);
        }

        void SetRewardIcon(string serverItemId)
        {
            Sprite s = null;

            var im = GameManager.instance ? GameManager.instance.itemManager : null;
            if (im != null && !string.IsNullOrEmpty(serverItemId))
            {
                ItemData d = im.GetItemDataByServerId(serverItemId);
                if (d != null && d.icon != null) s = d.icon;
            }

            if (s == null) s = placeholderIcon;

            if (leftIcon)
            {
                leftIcon.enabled = s != null;
                leftIcon.sprite = s;
                leftIcon.preserveAspect = true;
                leftIcon.color = Color.white;
            }
        }

        void SetState(bool claimed, bool canClaim)
        {
            var interact = !claimed && canClaim;
            if (actionButton) actionButton.interactable = interact;
            if (actionText) actionText.text = claimed ? "Đã nhận" : "Nhận";
            Debug.Log($"[QuestEntryView] SetState: claimed={claimed}, canClaim={canClaim}, btnInteractable={interact}");
        }

        void ApplyEmpty()
        {
            if (nameText) nameText.text = string.Empty;
            if (descriptionText) descriptionText.text = string.Empty;
            if (qtyText) qtyText.text = string.Empty;
            if (leftIcon)
            {
                leftIcon.enabled = false;
                leftIcon.sprite = null;
            }
            SetState(claimed: false, canClaim: false);
        }
    }
}

// Scripts/UI/Quest/QuestPanel.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI; // for LayoutRebuilder

namespace CGP.UI.Quests
{
    public class QuestPanel : MonoBehaviour
    {
        [Header("Bindings")]
        public RectTransform content;   // Content của ScrollView
        public QuestEntryView entryPrefab;

        readonly List<QuestEntryView> _pool = new();
        bool _isActive, _isRefreshing;
        int _refreshVersion = 0; // chống out-of-order refresh

        void OnEnable()
        {
            _isActive = true;

            // KHÔNG destroy children nữa -> giữ pool để tái sử dụng
            _ = RefreshUI();
        }

        void OnDisable() => _isActive = false;

        public async Task RefreshUI()
        {
            if (_isRefreshing || !_isActive) return;
            _isRefreshing = true;
            int version = ++_refreshVersion;

            try
            {
                // Guard binding
                if (!content) { Debug.LogError("[QuestPanel] 'content' chưa gán!"); return; }
                if (!entryPrefab) { Debug.LogError("[QuestPanel] 'entryPrefab' chưa gán!"); return; }

                var qm = CGP.Gameplay.Quests.QuestManager.Instance;
                if (qm == null) { Debug.LogWarning("[QuestPanel] QuestManager.Instance = null"); return; }

                await qm.Refresh();
                if (!_isActive || version != _refreshVersion) return;

                var list = qm.Quests ?? new List<CGP.Gameplay.Quests.QuestVM>();
                Debug.Log($"[QuestPanel] Bind {list.Count} quests");

                EnsurePool(list.Count);

                // bind
                for (int i = 0; i < _pool.Count; i++)
                {
                    var view = _pool[i];
                    var go = view.gameObject;
                    if (i < list.Count)
                    {
                        go.SetActive(true);
                        view.Bind(list[i], OnClaimClicked);
                        Debug.Log($"[QuestPanel] Row {i}: {list[i].meta.questName} | {list[i].progressText} | x{list[i].meta.amountReward}");
                    }
                    else
                    {
                        go.SetActive(false);
                    }
                }

                // Force rebuild layout để ContentSizeFitter/LayoutGroup cập nhật ngay
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        async void OnClaimClicked(CGP.Gameplay.Quests.QuestVM vm)
        {
            if (!_isActive || vm == null) return;

            Debug.Log($"[QuestPanel] OnClaimClicked: {vm.meta.questName} | questId={vm.state?.questId ?? vm.meta?.id} | canClaim={vm.canClaim} | isClaimed={vm.isClaimed}");

            var qm = CGP.Gameplay.Quests.QuestManager.Instance;
            if (qm == null) { Debug.LogWarning("[QuestPanel] QuestManager null"); return; }

            // Tắt tạm tất cả nút Claim để tránh spam trong lúc chờ API
            SetAllClaimButtonsInteractable(false);

            var ok = await qm.Claim(vm);
            Debug.Log($"[QuestPanel] Claim result: {ok}");

            if (!_isActive) return;

            // Làm mới danh sách (server đã cộng thưởng; VM có thể thay đổi)
            await RefreshUI();

            // Nếu panel vẫn mở nhưng không làm mới được thì bật lại nút
            SetAllClaimButtonsInteractable(true);
        }

        void SetAllClaimButtonsInteractable(bool on)
        {
            foreach (var v in _pool)
            {
                if (!v || !v.gameObject.activeInHierarchy) continue;
                if (v.actionButton) v.actionButton.interactable = on && v.Claimable;
            }
        }

        void EnsurePool(int n)
        {
            // tạo thêm nếu thiếu
            while (_pool.Count < n)
            {
                var v = Instantiate(entryPrefab, content);
                v.gameObject.SetActive(true);
                _pool.Add(v);
            }
        }
    }
}

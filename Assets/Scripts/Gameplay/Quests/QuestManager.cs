using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using CGP.Gameplay.Auth;
using CGP.Networking.Clients;
using CGP.Networking.DTOs;

namespace CGP.Gameplay.Quests
{
    public class QuestManager : MonoBehaviour
    {
        public static QuestManager Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[Quest] QuestManager.Awake -> Instance set + DontDestroyOnLoad");
        }

        // === AUTO-BOOTSTRAP ===
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RuntimeEnsure() => Ensure();

        public static void Ensure()
        {
            if (Instance != null) { Debug.Log("[Quest] Ensure: already exists"); return; }
            var go = new GameObject("[QuestManager]");
            go.AddComponent<QuestManager>(); // Awake sẽ set Instance + DontDestroyOnLoad
            Debug.Log("[Quest] Ensure: created QuestManager GameObject");
        }

        readonly QuestClient _client = new QuestClient();

        // Danh sách VM để UI bind
        public List<QuestVM> Quests { get; private set; } = new();

        // Override local để không bị "bật lại" khi server chưa flip rewardClaimed
        private readonly HashSet<string> _claimedOverride = new(); // key: userQuestId nếu có, fallback questId(meta)

        public async Task Refresh()
        {
            if (AuthManager.Instance == null)
            {
                Debug.LogWarning("[Quest] Refresh: AuthManager.Instance = null");
                return;
            }

            var userId = AuthManager.Instance.GetCurrentUserId();
            Debug.Log($"[Quest] Refresh: userId = {userId}, IsUserDataReady={AuthManager.Instance.IsUserDataReady}");
            if (string.IsNullOrEmpty(userId)) return;

            var metas = await _client.GetAllMetas();
            var states = await _client.GetUserStates(userId);

            Debug.Log($"[Quest] metas={metas?.Count ?? 0}, states={states?.Count ?? 0}");

            var stateByQid = states?.ToDictionary(s => s.questId, s => s) ?? new();

            Quests = new List<QuestVM>();
            foreach (var m in metas)
            {
                stateByQid.TryGetValue(m.id, out var st);

                // Fallback: BE trả target = 0 nhưng status = Completed -> hiển thị 1/1
                if (st != null && st.target == 0 &&
                    string.Equals(st.status, "Completed", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (st.progress <= 0) st.progress = 1;
                    st.target = 1;
                }

                // Áp override local nếu đã claim trước đó
                // Ưu tiên key = userQuestId (st.id), nếu null thì dùng quest meta id
                var key = st?.id ?? m.id;
                if (!string.IsNullOrEmpty(key) && _claimedOverride.Contains(key))
                {
                    st ??= new UserQuestState { id = key, questId = m.id, target = 1, progress = 1 };
                    st.rewardClaimed = true;
                }

                Quests.Add(new QuestVM { meta = m, state = st });
            }

            Debug.Log($"[Quest] Built VM list: {Quests.Count}");
        }

        public async Task<bool> Claim(QuestVM vm)
        {
            if (vm?.state == null || vm.isClaimed || !vm.canClaim) return false;

            var userId = AuthManager.Instance?.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return false;

            var ok = await _client.CompleteQuest(userId, vm.state.id);
            Debug.Log($"[Quest] CompleteQuest result={ok}");

            if (ok)
            {
                // Khóa nút/ngăn spam ngay lập tức
                vm.MarkClaimedLocal();

                // Kéo snapshot mới nhất từ server về local để thấy phần thưởng ngay
                var inv = CGP.Gameplay.Inventory.Presenter.InventoryManager.Instance;
                if (inv != null)
                {
                    await inv.ReloadFromServer();   // <-- thêm hàm này trong InventoryManager như mình đã gửi
                }
            }

            return ok;
        }
    }
}

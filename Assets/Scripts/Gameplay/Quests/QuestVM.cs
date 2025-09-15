// Scripts/Gameplay/Quests/QuestVM.cs
using CGP.Networking.DTOs;

namespace CGP.Gameplay.Quests
{
    public class QuestVM
    {
        public QuestMeta meta;
        public UserQuestState state;

        public bool isClaimed => state?.IsClaimed() ?? false;
        public bool canClaim => state?.CanClaim() ?? false;

        public string progressText
        {
            get
            {
                if (state == null) return "";
                // Nếu server báo Completed nhưng không có target, hiển thị 1/1 cho rõ ràng
                if (state.status == "Completed") return "1/1";
                var tgt = state.target <= 0 ? 1 : state.target;
                return $"{state.progress}/{tgt}";
            }
        }

        // Đánh dấu local đã nhận (khi server trả 200 nhưng không cập nhật state)
        public void MarkClaimedLocal()
        {
            if (state != null) state.rewardClaimed = true;
        }
    }
}
